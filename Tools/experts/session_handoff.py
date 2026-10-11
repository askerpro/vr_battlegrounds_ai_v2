"""session_handoff: что делали агенты (Codex и Claude Code) в этом worktree — для эксперта, принимающего работу.

Читает журналы `~/.codex/sessions/**/*.jsonl` и `~/.claude/projects/*/*.jsonl`, выбирает сессии, чей cwd —
сам worktree или его родительский каталог, и выдаёт ограниченную выжимку: реплики пользователя, сообщения
агента, итог последнего хода. Только чтение; это история переписки, не факт состояния: сверяй с git,
хабом и документами задачи.
"""

import sys

sys.dont_write_bytecode = True

import argparse
import json
import os
from pathlib import Path

MAX_BYTES = 20000
USER_LIMIT, AGENT_LIMIT = 6, 8
USER_CHARS, AGENT_CHARS = 900, 1400
HEAD_LINES = 40
SKIP_PREFIXES = ("<environment_context>", "<user_instructions>", "<permissions", "# AGENTS.md", "<INSTRUCTIONS>",
                 "<command-", "<local-command", "<system-reminder>", "<task-notification>", "Caveat:",
                 "Another Claude session sent a message", "<agent-message", "<cross-session-message",
                 "Your response above was cut off", "[Request interrupted", "This session is being continued")


def norm(path):
    return os.path.normcase(os.path.normpath(str(path))).rstrip("\\/")


def head_cwd(path, client):
    """cwd сессии из первых строк журнала."""
    try:
        with open(path, encoding="utf-8") as handle:
            for _ in range(HEAD_LINES):
                line = handle.readline()
                if not line:
                    break
                record = json.loads(line)
                if client == "codex" and record.get("type") == "session_meta":
                    meta = record.get("payload") or {}
                    # служебные под-сессии (guardian_review и др.) — не основная работа
                    if meta.get("parent_thread_id") or meta.get("thread_source") not in (None, "user"):
                        return None
                    return meta.get("cwd")
                if client == "claude" and record.get("cwd"):
                    return record["cwd"]
    except (OSError, ValueError, UnicodeDecodeError):
        return None
    return None


def last_stamp(path):
    """Время последней записи по меткам timestamp в хвосте журнала (mtime у журналов Codex ненадёжен)."""
    try:
        with open(path, "rb") as handle:
            handle.seek(0, os.SEEK_END)
            size = handle.tell()
            handle.seek(max(0, size - 65536))
            tail = handle.read().decode("utf-8", "ignore").splitlines()
    except OSError:
        return ""
    for line in reversed(tail):
        try:
            stamp = json.loads(line).get("timestamp")
        except ValueError:
            continue
        if isinstance(stamp, str):
            return stamp
    return ""


def matches(cwd, target):
    if not cwd:
        return False
    cwd = norm(cwd)
    return cwd == target or cwd == norm(Path(target).parent)


def clip(text, limit):
    text = text.strip()
    return text if len(text) <= limit else text[: limit - 1] + "…"


def blocks_text(content):
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "".join(c.get("text", "") for c in content if isinstance(c, dict) and c.get("type") in (None, "text", "input_text", "output_text"))
    return ""


def records(path, client):
    """(bucket, stamp, text): bucket user|agent|final."""
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            try:
                record = json.loads(line)
            except ValueError:
                continue
            stamp = (record.get("timestamp") or "")[:16].replace("T", " ")
            if client == "claude":
                if record.get("isSidechain") or record.get("isMeta") or record.get("type") not in ("user", "assistant"):
                    continue
                message = record.get("message") or {}
                content = message.get("content")
                # результаты инструментов (tool_result) не дают текста и отсеиваются как пустые
                yield ("user" if record["type"] == "user" else "agent"), stamp, blocks_text(content)
                continue
            payload = record.get("payload") or {}
            kind = payload.get("type")
            if record.get("type") == "event_msg" and kind == "user_message":
                yield "user", stamp, payload.get("message") or ""
            elif record.get("type") == "event_msg" and kind == "agent_message":
                yield "agent", stamp, payload.get("message") or ""
            elif record.get("type") == "event_msg" and kind == "task_complete":
                yield "final", stamp, payload.get("last_agent_message") or ""
            elif record.get("type") == "response_item" and kind == "message":
                role = payload.get("role")
                if role in ("user", "assistant"):
                    yield ("user" if role == "user" else "agent"), stamp, blocks_text(payload.get("content"))


def extract(path, client):
    found = {"user": [], "agent": [], "final": []}
    seen = set()
    for bucket, stamp, text in records(path, client):
        if not text or not text.strip() or text.lstrip().startswith(SKIP_PREFIXES):
            continue
        key = (bucket, text.strip()[:200])
        if key in seen:
            continue
        seen.add(key)
        found[bucket].append((stamp, text))
    return found


def candidates(codex_root, claude_root, target):
    found = []
    for client, root in (("codex", codex_root), ("claude", claude_root)):
        if not root.is_dir():
            continue
        pattern = "**/*.jsonl" if client == "codex" else "*/*.jsonl"
        for path in root.glob(pattern):
            if matches(head_cwd(path, client), target):
                found.append((last_stamp(path), client, path))
    return sorted(found, reverse=True)


def main(argv=None):
    parser = argparse.ArgumentParser(prog="session_handoff", description="Выжимка последних сессий Codex и Claude Code для worktree. Только чтение.")
    parser.add_argument("--worktree", default=".", help="корень worktree (по умолчанию текущий каталог)")
    parser.add_argument("--last", type=int, default=2, help="сколько последних сессий показать")
    parser.add_argument("--client", choices=("all", "codex", "claude"), default="all")
    parser.add_argument("--codex-sessions", default=str(Path.home() / ".codex" / "sessions"))
    parser.add_argument("--claude-projects", default=str(Path.home() / ".claude" / "projects"))
    args = parser.parse_args(argv)
    target = norm(Path(args.worktree).resolve())
    found = [c for c in candidates(Path(args.codex_sessions), Path(args.claude_projects), target)
             if args.client in ("all", c[1])][: max(1, args.last)]
    out = [f"# Агенты в `{args.worktree}`: сессий показано {len(found)}",
           "> История переписки, не факт состояния: сверь с `git status`/diff, хабом и Readme задачи.", ""]
    if not found:
        out.append("Сессий Codex/Claude с этим cwd (или родительским) нет.")
    for stamp, client, path in reversed(found):
        data = extract(path, client)
        out.append(f"## {client}: {path.name} (последняя запись {stamp[:16].replace('T', ' ')} UTC)")
        out.append("### Пользователь (последние)")
        out += [f"- [{s}] {clip(t, USER_CHARS)}" for s, t in data["user"][-USER_LIMIT:]] or ["- нет"]
        out.append("### Агент (последние)")
        out += [f"- [{s}] {clip(t, AGENT_CHARS)}" for s, t in data["agent"][-AGENT_LIMIT:]] or ["- нет"]
        if data["final"]:
            s, t = data["final"][-1]
            out += ["### Итог последнего хода", f"[{s}] {clip(t, AGENT_CHARS * 2)}"]
        out.append("")
    text = "\n".join(out)
    raw = text.encode("utf-8")
    if len(raw) > MAX_BYTES:
        text = raw[:MAX_BYTES].decode("utf-8", "ignore") + "\n… (обрезано; полный журнал — файл сессии выше)\n"
    sys.stdout.buffer.write(text.encode("utf-8"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
