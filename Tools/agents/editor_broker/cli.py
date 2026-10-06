"""CLI с ограниченным JSON-выводом; адресное wait сохраняет FIFO-резервирование."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import subprocess
import sys

from .git_state import GitState
from .human import HumanHandoff
from .mcp_gate import decide
from .service import EditorBroker, atomic_json, default_state_dir
from .watch import watch_ticket


def parser():
    root = argparse.ArgumentParser(description="Изолированные worktree и FIFO для Unity")
    root.add_argument("--repo", default=str(Path.cwd()), help="Worktree вызывающего агента")
    root.add_argument("--state-dir", help="Общий постоянный каталог контроллера")
    commands = root.add_subparsers(dest="command", required=True)
    initialize = commands.add_parser("init")
    initialize.add_argument("--editor-root", required=True)
    initialize.add_argument("--baseline", required=True)
    initialize.add_argument("--no-install", action="store_true", help="Только для независимого стенда")
    checkpoint = commands.add_parser("checkpoint")
    checkpoint.add_argument("--label", required=True)
    checkpoint.add_argument("--path", action="append")
    request = commands.add_parser("request")
    request.add_argument("--owner", required=True)
    request.add_argument("--input", required=True)
    request.add_argument("--base")
    request.add_argument("--output", action="append", default=[])
    request.add_argument("--key", required=True)
    request.add_argument("--agent-root")
    for name in ("wait", "watch-ticket", "claim", "cancel", "receive", "ack"):
        command = commands.add_parser(name)
        command.add_argument("--ticket", required=True)
        command.add_argument("--owner", required=True)
        if name == "wait":
            command.add_argument("--timeout", type=float, default=60)
        if name == "watch-ticket":
            command.add_argument("--timeout", type=float)
            command.add_argument("--until", choices=("offered", "result"), default="offered")
        if name == "receive":
            command.add_argument("--agent-root")
    for name in ("begin", "guard", "renew", "finish", "recover"):
        command = commands.add_parser(name)
        command.add_argument("--ticket", required=True)
        command.add_argument("--token", required=True)
        if name == "finish":
            command.add_argument("--artifact", action="append", default=[])
        if name == "recover":
            command.add_argument("--editor-stopped", action="store_true")
    commands.add_parser("status")
    publish = commands.add_parser("publish-base", help="Опубликовать уже принятую базу при пустой очереди")
    publish.add_argument("--sha", required=True)
    publish.add_argument("--source-root")
    events = commands.add_parser("events")
    events.add_argument("--owner", required=True)
    events.add_argument("--after", type=int, default=0)
    # Команды Unity-панели передачи редактора человеком.
    commands.add_parser("human-release", help="Сохранённые правки в stash, редактор агентам")
    commands.add_parser("human-resume", help="Остановить выдачу, вернуть stash после аренды")
    defer = commands.add_parser("human-defer", help="Отказ агенту: N минут или без срока (0)")
    defer.add_argument("--minutes", type=float, default=0)
    commands.add_parser("human-status")
    commands.add_parser("human-clear", help="Забыть разобранную вручную аварийную передачу")
    gate = commands.add_parser("mcp-gate", help="Решение по вызову Unity MCP; JSON вызова — в stdin")
    gate.add_argument("--agent-root")
    return root


def public(value):
    if isinstance(value, dict):
        return {key: public(item) for key, item in value.items() if key != "token"}
    if isinstance(value, list):
        return [public(item) for item in value]
    return value


def main(argv=None):
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
        sys.stderr.reconfigure(encoding="utf-8", errors="replace")
    args = parser().parse_args(argv)
    try:
        state = Path(args.state_dir).resolve() if args.state_dir else default_state_dir(args.repo)
        # После init выполняется закреплённая runtime-копия, а не код гостевой ветки.
        runtime = state / "runtime" / "editor-broker.py"
        here = Path(__file__).resolve().parents[1] / "editor-broker.py"
        if args.command != "init" and runtime.is_file() and runtime.resolve() != here.resolve():
            return subprocess.call([sys.executable, str(runtime), *(sys.argv[1:] if argv is None else argv)])
        if args.command == "init":
            result = EditorBroker.initialize(args.editor_root, state, args.baseline,
                                            install=not args.no_install)
        elif args.command == "checkpoint":
            result = GitState(args.repo).checkpoint(state, args.label, paths=args.path)
        else:
            broker = EditorBroker(state)
            if args.command == "request":
                result = broker.request(args.owner, args.base or broker.config["baseline_sha"],
                    args.input, args.agent_root or args.repo, args.output, args.key)
            elif args.command == "wait":
                result = broker.store.wait(args.ticket, args.owner, timeout=args.timeout)
            elif args.command == "watch-ticket":
                exit_code, event = watch_ticket(broker.store, args.ticket, args.owner, args.until, args.timeout)
                print(json.dumps({"ok": exit_code == 0, "result": event}, ensure_ascii=False))
                return exit_code
            elif args.command == "claim":
                result = broker.claim(args.ticket, args.owner)
            elif args.command == "cancel":
                result = broker.store.cancel(args.ticket, args.owner)
            elif args.command == "receive":
                result = broker.receive(args.ticket, args.owner, args.agent_root or args.repo)
            elif args.command == "ack":
                result = broker.store.ack_result(args.ticket, args.owner)
            elif args.command == "events":
                result = broker.store.events(args.owner, args.after)
            elif args.command == "publish-base":
                result = broker.publish_base(args.sha, args.source_root or args.repo)
            elif args.command == "status":
                result = broker.store.status()
                tickets = result.get("tickets", [])
                report = state / "reports" / "status.json"
                atomic_json(report, public(result))
                result = {**result, "ticket_count": len(tickets), "tickets": tickets[:10],
                          "report_path": str(report)}
            elif args.command == "mcp-gate":
                # Байты: кодировка консоли Windows (cp1251) испортила бы кириллицу в коде вызова.
                call = json.loads(sys.stdin.buffer.read().decode("utf-8") or "{}")
                result = decide(broker, args.agent_root or args.repo, call.get("tool", ""),
                                call.get("arguments") or {}, call.get("pinned"))
            elif args.command.startswith("human-"):
                human = HumanHandoff(broker)
                result = {"human-release": human.release, "human-resume": human.resume,
                          "human-status": human.status, "human-clear": human.clear,
                          "human-defer": lambda: human.defer(args.minutes)}[args.command]()
            elif args.command == "renew":
                result = broker.store.renew(args.ticket, args.token)
            elif args.command == "finish":
                result = broker.finish(args.ticket, args.token, args.artifact)
            elif args.command == "recover" and args.editor_stopped:
                result = broker.recover_stopped(args.ticket, args.token)
            else:
                result = getattr(broker, args.command)(args.ticket, args.token)
        if args.command != "mcp-gate":  # вызывается на каждый MCP-вызов, отчёт панели не нужен
            _human_snapshot(state)
        # Только claim сообщает capability получившему аренду владельцу.
        output = result if args.command == "claim" else public(result)
        print(json.dumps({"ok": True, "result": output}, ensure_ascii=False))
        return 0
    except Exception as error:
        _human_snapshot(state if "state" in locals() else None)
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False))
        return 2


def _human_snapshot(state):
    """Панель Unity читает файл, а не CLI; сбой отчёта не меняет итог команды."""
    if state is None or not (state / "config.json").is_file():
        return
    try:
        HumanHandoff(EditorBroker(state)).snapshot()
    except Exception:
        pass


if __name__ == "__main__":
    raise SystemExit(main())
