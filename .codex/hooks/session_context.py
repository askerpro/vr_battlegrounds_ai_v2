"""Загрузить постоянные правила терминала в контекст Codex без изменения файлов."""

import json
from pathlib import Path
import sys


def main():
    root = Path(__file__).resolve().parents[2]
    rules = root / ".agents" / "rules" / "terminal.md"
    text = rules.read_text(encoding="utf-8")
    marker = "## Редактирование файлов и передача кода (находки 2026-10-02)"
    context = text[text.index(marker):]
    # ASCII-экранирование JSON сохраняет кириллицу даже в старой Windows-консоли.
    json.dump({"hookSpecificOutput": {
        "hookEventName": "SessionStart",
        "additionalContext": context,
    }}, sys.stdout, ensure_ascii=True)
    sys.stdout.write("\n")


if __name__ == "__main__":
    main()
