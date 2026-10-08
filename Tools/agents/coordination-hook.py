#!/usr/bin/env python3
"""PreToolUse заглушка. Пока контроль выключен, обычные разрешения сохраняются."""
import json
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from broker_runtime import runtime_dir


def main():
    runtime = runtime_dir()
    config = runtime.parent.parent / "coordination/config.json"
    try:
        if not config.exists():
            return 0
        settings = json.loads(config.read_text(encoding="utf-8"))
        if settings.get("mode") == "off" and settings.get("version") == 1:
            return 0
        script = runtime / "coordination-hook.py"
        if not script.is_file():
            raise RuntimeError("Строгий контроль включён, но runtime координации не развёрнут")
        return subprocess.call([sys.executable, "-X", "utf8", str(script)])
    except (ValueError, OSError, RuntimeError) as error:
        print(json.dumps({"hookSpecificOutput": {
            "hookEventName": "PreToolUse", "permissionDecision": "deny",
            "permissionDecisionReason": "Координация: " + str(error)}}, ensure_ascii=True))
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
