#!/usr/bin/env python3
"""Заглушка общего протокола: исходники и runtime принадлежат agent-infra."""
import json
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from broker_runtime import require


def main():
    try:
        script = require() / "coordination.py"
        if not script.is_file():
            raise RuntimeError("Координация ещё не развёрнута; пакет и план: tasks/agent-coordination-protocol/Readme.md")
        return subprocess.call([sys.executable, "-X", "utf8", str(script), *sys.argv[1:]])
    except RuntimeError as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=True))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
