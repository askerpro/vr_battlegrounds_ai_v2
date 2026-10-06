#!/usr/bin/env python3
"""Заглушка editor-broker: запускает развёрнутый брокер из общего runtime проекта.

Исходники и развёртывание — F:/UnityProjects/agent-infra (README, deploy.py). Аргументы, stdin
и код возврата передаются как есть; `init` выполняется из agent-infra, пока runtime ещё нет.
"""
import json
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from broker_runtime import require  # noqa: E402

if __name__ == "__main__":
    try:
        script = require() / "editor-broker.py"
    except RuntimeError as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False))
        raise SystemExit(2)
    raise SystemExit(subprocess.call([sys.executable, str(script), *sys.argv[1:]]))
