"""codex_handoff: что делал Codex в этом worktree — для эксперта, принимающего работу.

Читает журналы Codex (`~/.codex/sessions/**/*.jsonl`), выбирает сессии с cwd = worktree и выдаёт
ограниченную выжимку: реплики пользователя, сообщения агента и итог последнего хода. Только чтение;
это история переписки, не факт состояния: сверяй с git, хабом и документами задачи.
"""

import sys

sys.dont_write_bytecode = True

import argparse
import datetime
import json
import os
from pathlib import Path

MAX_BYTES = 20000
USER_LIMIT, AGENT_LIMIT = 6, 8
USER_CHARS, AGENT_CHARS = 900, 1400
SKIP_PREFIXES = ("<environment_context>", "<user_instructions>", "<permissions", "# AGENTS.md", "<INSTRUCTIONS>")


def norm(path):
    return os.path.normcase(os.path.normpath(str(path))).rstrip("\\/")


def session_cwd(path):
    try:
        with open(path, encoding="utf-8") as handle:
            for _ in range(5):
                line = handle.readline()
                if not line:
                    break
                record = json.loads(line)
                if record.get("type") == "session_meta":
                    return (record.get("payload") or {}).get("cwd")
    except (OSError, ValueError, UnicodeDecodeError):
        return None
    return None


def clip(text, limit):
    text = text.strip()
    return text if len(text) <= limit else text[: limit - 1] + "…"


def extract(path):
    users, agents, finals, seen = [], [], [], set()
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            try:
                record = json.loads(line)
            except ValueError:
                continue
            payload = record.get("payload") or {}
            stamp = (record.get("timestamp") or "")[:16].replace("T", " ")
            kind = payload.get("type")
            text, bucket = None, None
            if record.get("type") == "event_msg" and kind == "user_message":
                text, bucket = payload.get("message"), users
            elif record.get("type") == "event_msg" and kind == "agent_message":
                text, bucket = payload.get("message"), agents
            elif record.get("type") == "event_msg" and kind == "task_complete":
                text, bucket = payload.get("last_agent_message"), finals
            elif record.get("type") == "response_item" and kind == "message":
                role = payload.get("role")
                text = "".join(c.get("text", "") for c in payload.get("content", []) if isinstance(c, dict))
                bucket = users if role == "user" else agents if role == "assistant" else None
            if not text or bucket is None or text.lstrip().startswith(SKIP_PREFIXES):
                continue
            key = text.strip()[:200]
            if key in seen:
                continue
            seen.add(key)
            bucket.append((stamp, text))
    return users, agents, finals


def main(argv=None):
    parser = argparse.ArgumentParser(prog="codex_handoff", description="Выжимка последних сессий Codex для worktree. Только чтение.")
    parser.add_argument("--worktree", default=".", help="корень worktree (по умолчанию текущий каталог)")
    parser.add_argument("--sessions", default=str(Path.home() / ".codex" / "sessions"))
    parser.add_argument("--last", type=int, default=2, help="сколько последних сессий показать")
    args = parser.parse_args(argv)
    target = norm(Path(args.worktree).resolve())
    files = sorted(Path(args.sessions).rglob("*.jsonl"), key=lambda p: p.stat().st_mtime, reverse=True)
    matched = [p for p in files if session_cwd(p) and norm(session_cwd(p)) == target][: max(1, args.last)]
    out = [f"# Codex в `{args.worktree}`: сессий найдено {len(matched)}",
           "> История переписки, не факт состояния: сверь с `git status`/diff, хабом и Readme задачи.", ""]
    if not matched:
        out.append("Сессий Codex с этим cwd нет.")
    for path in reversed(matched):
        users, agents, finals = extract(path)
        changed = datetime.datetime.fromtimestamp(path.stat().st_mtime).strftime("%Y-%m-%d %H:%M")
        out.append(f"## Сессия {path.name} (последняя запись {changed})")
        out.append("### Пользователь (последние)")
        out += [f"- [{s}] {clip(t, USER_CHARS)}" for s, t in users[-USER_LIMIT:]] or ["- нет"]
        out.append("### Агент (последние)")
        out += [f"- [{s}] {clip(t, AGENT_CHARS)}" for s, t in agents[-AGENT_LIMIT:]] or ["- нет"]
        if finals:
            s, t = finals[-1]
            out += ["### Итог последнего хода", f"[{s}] {clip(t, AGENT_CHARS * 2)}"]
        out.append("")
    text = "\n".join(out)
    data = text.encode("utf-8")
    if len(data) > MAX_BYTES:
        text = data[:MAX_BYTES].decode("utf-8", "ignore") + "\n… (обрезано; полный журнал — файл сессии выше)\n"
    sys.stdout.buffer.write(text.encode("utf-8"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
