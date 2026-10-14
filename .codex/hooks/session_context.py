"""Загрузить правила терминала и назначенную роль Codex без изменения файлов."""

import sys

sys.dont_write_bytecode = True

import json
from pathlib import Path
import re

MARKER = "## Редактирование файлов и передача кода (находки 2026-10-02)"
MAX_CONTEXT_CHARS = 12000
MAX_ROLE_CHARS = 4000
MAX_FILE_BYTES = 16384
AGENT_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]{0,63}\Z")
SOURCES = {"startup", "resume", "clear", "compact"}
SCOPE = (
    "Назначение ниже относится только основной сессии Codex в этом worktree. "
    "В основной сессии прими роль непосредственно, без создания субагента. "
    "Делегированные агенты сохраняют выданную им задачу и роль; это назначение "
    "не меняет их роль и не предписывает им повторять startup основного агента. "
    "Назначение не выдаёт допуск, аренду или владение задачей; не выбирай задачу "
    "и не меняй чужие маршруты автоматически; собственный маршрут обновляй штатно по AGENTS.md.\n\n"
)


class RoleError(Exception):
    """Диагностика содержит только известное описание ошибки, без входных данных."""


def read_local(root, path):
    if not path.resolve().is_relative_to(root):
        raise RoleError("назначение или профиль ведёт за пределы worktree")
    with path.open("rb") as stream:
        raw = stream.read(MAX_FILE_BYTES + 1)
    if len(raw) > MAX_FILE_BYTES:
        raise RoleError("файл назначения или профиля превышает допустимый размер")
    return raw.decode("utf-8")


def assigned_role(root):
    try:
        settings = json.loads(read_local(root, root / ".claude" / "settings.local.json"))
    except FileNotFoundError:
        return ""
    except (ValueError, UnicodeError):
        raise RoleError("назначение не является корректным UTF-8 JSON")
    if not isinstance(settings, dict):
        raise RoleError("назначение должно быть объектом JSON")
    agent = settings.get("agent")
    if agent is None or agent == "":
        return ""
    if not isinstance(agent, str) or not AGENT_RE.fullmatch(agent):
        raise RoleError("имя профиля должно быть простым именем файла без каталогов и расширения")
    try:
        import tomllib
        profile = tomllib.loads(read_local(root, root / ".codex" / "agents" / (agent + ".toml")))
    except FileNotFoundError:
        raise RoleError("назначенный профиль отсутствует в .codex/agents")
    except ImportError:
        raise RoleError("для чтения профиля нужен Python 3.11 или новее")
    except (ValueError, UnicodeError):
        raise RoleError("назначенный профиль не является корректным UTF-8 TOML")
    instructions = profile.get("developer_instructions")
    description = profile.get("description", "")
    if not isinstance(instructions, str) or not instructions.strip():
        raise RoleError("профиль не содержит непустых developer_instructions")
    if not isinstance(description, str):
        raise RoleError("description профиля должна быть строкой")
    if len(instructions) + len(description) > MAX_ROLE_CHARS:
        raise RoleError("профиль слишком велик для контекста; прочитай назначенный .codex/agents профиль вручную")
    return SCOPE + f"## Назначенная роль Codex: {agent}\n\n" + description + "\n\n" + instructions + "\n\n"


def codex_context(root):
    prefix = ""
    try:
        raw = sys.stdin.read(MAX_FILE_BYTES + 1)
        payload = json.loads(raw) if len(raw) <= MAX_FILE_BYTES else None
    except (ValueError, UnicodeError):
        payload = None
    # Это фильтр явных событий, а не доказательство root-only исполнения runtime.
    eligible = (isinstance(payload, dict) and payload.get("hook_event_name") == "SessionStart"
                and isinstance(payload.get("source"), str) and payload["source"] in SOURCES
                and "agent_id" not in payload and "agent_type" not in payload)
    if eligible:
        try:
            prefix = assigned_role(root)
        except (RoleError, OSError, RuntimeError) as exc:
            reason = str(exc) if isinstance(exc, RoleError) else "не удалось безопасно прочитать назначение или профиль"
            prefix = f"Роль Codex не загружена: {reason}. Текущая роль сохраняется; не угадывай другую.\n\n"
    rules = root / ".agents" / "rules" / "terminal.md"
    try:
        with rules.open(encoding="utf-8") as stream:
            text = stream.read(MAX_CONTEXT_CHARS + 1)
        context = text[text.index(MARKER):]
    except (OSError, UnicodeError, ValueError):
        context = "Правила терминала не удалось загрузить; прочитай .agents/rules/terminal.md вручную.\n"
    available = MAX_CONTEXT_CHARS - len(prefix)
    if len(context) > available:
        tail = "\n\nПолные правила: .agents/rules/terminal.md (показана начальная часть).\n"
        context = context[:available - len(tail)] + tail
    return prefix + context


def main():
    root = Path(__file__).resolve().parents[2]
    if "--codex-main-role" in sys.argv[1:]:
        context = codex_context(root)
    else:
        # Совместимость Claude: прежний контекст и формат stdout без изменений.
        rules = root / ".agents" / "rules" / "terminal.md"
        text = rules.read_text(encoding="utf-8")
        context = text[text.index(MARKER):]
    # ASCII-экранирование JSON сохраняет кириллицу даже в старой Windows-консоли.
    json.dump({"hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": context,
    }}, sys.stdout, ensure_ascii=True)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
