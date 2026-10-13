#!/usr/bin/env python3
"""Hook launcher координации: PreToolUse gate и учёт вопросов человеку.

PreToolUse: пока контроль выключен (config version=1, mode=off), обычные разрешения
сохраняются и runtime не вызывается. События вопросов человеку (Stop, UserPromptSubmit,
SessionStart, SessionEnd и Pre/PostToolUse, PostToolUseFailure для AskUserQuestion) передаются
развёрнутому runtime при любом mode, но только если runtime их поддерживает
(agent_coordination/human_questions.py). Наружу пересылается только проверенное решение Stop
{"decision":"block"}; для остальных событий вывод runtime отбрасывается, поэтому отказ в
диалоге AskUserQuestion невозможен при любой версии runtime. Любой сбой — exit 0 без вывода.
"""
import json
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))

QUESTION_EVENTS = ("Stop", "UserPromptSubmit", "SessionStart", "SessionEnd")
RUNTIME_TIMEOUT = 8  # меньше таймаутов hook в settings
HIDDEN = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0


def _event(raw):
    try:
        call = json.loads(raw.decode("utf-8") or "{}")
    except ValueError:
        return None, None
    if not isinstance(call, dict):
        return None, None
    return call.get("hook_event_name"), call.get("tool_name")


def _stop_decision(stdout):
    # Только {"decision":"block","reason":<строка>} из первой строки; всё прочее отбрасывается.
    try:
        value = json.loads(stdout.decode("utf-8").strip().splitlines()[0])
    except (ValueError, IndexError, UnicodeDecodeError):
        return None
    if isinstance(value, dict) and value.get("decision") == "block" and isinstance(value.get("reason"), str):
        return {"decision": "block", "reason": value["reason"]}
    return None


def _questions(raw, event):
    try:
        from broker_runtime import runtime_dir
        runtime = runtime_dir()
        script = runtime / "coordination-hook.py"
        # Старый runtime без human_questions отправил бы события в gate/аудит: не вызывать.
        if not (runtime / "agent_coordination/human_questions.py").is_file():
            return 0
        env = dict(os.environ, AGENT_COORDINATION_STATE_DIR=str(runtime.parent.parent / "coordination"))
        done = subprocess.run([sys.executable, "-X", "utf8", str(script)], input=raw, capture_output=True,
                              timeout=RUNTIME_TIMEOUT, creationflags=HIDDEN, env=env)
        decision = _stop_decision(done.stdout) if event == "Stop" and done.returncode == 0 else None
        if decision:
            sys.stdout.write(json.dumps(decision, ensure_ascii=True) + "\n")
            sys.stdout.flush()
    except Exception:
        pass
    return 0


def main():
    raw = sys.stdin.buffer.read()
    event, tool = _event(raw)
    if event in QUESTION_EVENTS or tool == "AskUserQuestion":
        return _questions(raw, event)
    if event not in (None, "PreToolUse"):
        return 0
    from broker_runtime import runtime_dir
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
        return subprocess.run([sys.executable, "-X", "utf8", str(script)], input=raw).returncode
    except (ValueError, OSError, RuntimeError) as error:
        print(json.dumps({"hookSpecificOutput": {
            "hookEventName": "PreToolUse", "permissionDecision": "deny",
            "permissionDecisionReason": "Координация: " + str(error)}}, ensure_ascii=True))
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
