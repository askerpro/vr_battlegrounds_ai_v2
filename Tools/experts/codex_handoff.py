"""Старое имя session_handoff.py (только Codex) — оставлено для уже выданных промптов."""

import sys

sys.dont_write_bytecode = True

from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import session_handoff  # noqa: E402

if __name__ == "__main__":
    sys.exit(session_handoff.main(["--client", "codex", *sys.argv[1:]]))
