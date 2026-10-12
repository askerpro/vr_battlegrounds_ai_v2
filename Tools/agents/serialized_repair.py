#!/usr/bin/env python3
"""Восстановление serialized binary (LF->CRLF) из закреплённого runtime: --repo обязателен, по умолчанию dry-run."""
import os
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from broker_runtime import require  # noqa: E402

if __name__ == "__main__":
    try:
        script = require() / "editor_broker" / "serialized_repair.py"
    except RuntimeError as error:
        print("serialized_repair: " + str(error), file=sys.stderr)
        raise SystemExit(2)
    if not script.is_file():
        print("serialized_repair: runtime без editor_broker/serialized_repair.py; обновите runtime",
              file=sys.stderr)
        raise SystemExit(2)
    hidden = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
    raise SystemExit(subprocess.call([sys.executable, "-B", str(script), *sys.argv[1:]],
                                    creationflags=hidden, stdin=sys.stdin,
                                    stdout=sys.stdout, stderr=sys.stderr))
