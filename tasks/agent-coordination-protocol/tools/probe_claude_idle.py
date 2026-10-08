"""Проверка реакции собственной streaming-сессии на Monitor/asyncRewake."""
import argparse
import json
import queue
import subprocess
import threading
import time
import uuid
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--executable", required=True)
parser.add_argument("--mode", choices=("rewake", "monitor"), required=True)
parser.add_argument("--exchange", action="store_true")
args = parser.parse_args()
task = Path(__file__).resolve().parent.parent
reports = task / "reports"
workspace = reports / ("idle-" + args.mode)
workspace.mkdir(parents=True, exist_ok=True)
event_path = reports / (args.mode + "-event.json")
if event_path.exists():
    event_path.unlink()
nonce = uuid.uuid4().hex
expected = "EVENT_ACK_" + nonce
store = None
codex_child = None
if args.exchange:
    sys.path.insert(0, "F:/UnityProjects/agent-infra")
    from agent_coordination.store import CoordinationStore
    state = reports / ("exchange-" + nonce)
    store = CoordinationStore(state)
    for name, client in (("claude-probe", "claude"), ("codex-probe", "codex")):
        store.register({"task_id": name, "owner": name + "-owner", "client": client,
            "worktree": str(state / name), "base_sha": "a" * 40,
            "doc_path": "tasks/" + name + "/Readme.md", "title": name,
            "stages": [{"id": "probe", "writes": ["tasks/" + name + "/**"], "after": [], "needs": []}]}, 0)
watch_command = "python -X utf8 " + (task / "tools/watch_probe_event.py").as_posix() + " --event " + event_path.as_posix()
command = [args.executable, "--print", "--restricted", "--strict-mcp-config",
           "--mcp-config", '{"mcpServers":{}}', "--no-session-persistence",
           "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
           "--max-budget-usd", "1", "--system-prompt",
           "You are a transport probe. Never read or edit files. Respond READY initially; on a transport test event echo only its requested EVENT_ACK literal."]
if args.mode == "rewake":
    command += ["--tools", "", "--settings", str(task / "tools/claude-rewake-probe-settings.json")]
    prompt = "Respond exactly READY. A background hook will later deliver a transport test event."
else:
    command += ["--tools", "Monitor", "--allowedTools", "Bash(" + watch_command + ")"]
    prompt = "Use Monitor once with command '" + watch_command + "' and timeout_ms 40000. Then respond READY. On its event respond with the exact EVENT_ACK literal. No other commands."
process = subprocess.Popen(command, cwd=workspace, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
    stderr=subprocess.PIPE, encoding="utf-8", errors="replace",
    creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
inbox = queue.Queue()
transcript, errors, texts = [], [], []

def read_output():
    for line in process.stdout:
        try:
            inbox.put(json.loads(line))
        except json.JSONDecodeError:
            inbox.put({"non_json": line.rstrip()})

def read_error():
    for line in process.stderr:
        errors.append(line.rstrip())

threading.Thread(target=read_output, daemon=True).start()
threading.Thread(target=read_error, daemon=True).start()
process.stdin.write(json.dumps({"type": "user", "message": {"role": "user", "content": prompt}}) + "\n")
process.stdin.flush()
start = time.monotonic()
data = {"mode": args.mode, "ok": False, "event_sent_after_initial_result": False,
        "nonce": nonce, "expected": expected, "user_prompts_sent": 1}
try:
    while time.monotonic() - start < 50:
        try:
            item = inbox.get(timeout=0.25)
        except queue.Empty:
            if process.poll() is not None:
                break
            continue
        transcript.append(item)
        if item.get("type") == "system" and item.get("subtype") == "init":
            data["monitor_in_tools"] = "Monitor" in item.get("tools", [])
        if item.get("type") == "assistant":
            for part in item.get("message", {}).get("content", []):
                if part.get("type") == "text":
                    texts.append(part.get("text", ""))
        if item.get("type") == "result":
            if not data["event_sent_after_initial_result"]:
                data["initial_result_subtype"] = item.get("subtype")
                if store:
                    first = store.send_message("claude-probe", "claude-probe-owner", "codex-probe",
                        "pilot_request", json.dumps({"nonce": nonce}))
                    data["request_event_id"] = first["id"]
                    codex_report = reports / "cross-pilot-codex-2026-10-09.json"
                    codex_child = subprocess.Popen([sys.executable, "-X", "utf8",
                        str(task / "tools/probe_codex_live.py"), "--executable",
                        "C:/Users/asker/.codex/packages/app-server-daemon/current/bin/codex.exe",
                        "--report", str(codex_report), "--exchange-state", str(state),
                        "--notify-file", str(event_path)], stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        encoding="utf-8", errors="replace", creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
                else:
                    event_path.write_text(json.dumps({"event_id": 1, "nonce": nonce}), encoding="utf-8")
                data["event_sent_after_initial_result"] = True
            else:
                if store and event_path.exists():
                    notification = json.loads(event_path.read_text(encoding="utf-8"))
                    expected = "EVENT_ACK_" + notification["nonce"]
                    data["expected"] = expected
                if not any(expected in text for text in texts):
                    continue
                data["ok"] = item.get("subtype") == "success" and not item.get("is_error")
                data["final_result_subtype"] = item.get("subtype")
                if store and data["ok"]:
                    store.ack_events("claude-probe", "claude-probe-owner", [notification["event_id"]])
                    store.send_message("claude-probe", "claude-probe-owner", "codex-probe",
                        "pilot_final", json.dumps({"nonce": nonce, "reply_to": notification["event_id"]}))
                    codex_output, codex_error = codex_child.communicate(timeout=30)
                    data["codex_exit_code"] = codex_child.returncode
                    data["codex_summary"] = codex_output
                    data["ok"] = data["ok"] and codex_child.returncode == 0
                break
finally:
    process.stdin.close()
    try:
        process.wait(timeout=3)
    except subprocess.TimeoutExpired:
        process.terminate()
        process.wait(timeout=3)
    if codex_child and codex_child.poll() is None:
        codex_child.terminate()
        codex_child.wait(timeout=3)
    data.update(elapsed_seconds=round(time.monotonic() - start, 3),
                exit_code=process.returncode, transcript=transcript, stderr=errors,
                assistant_texts=texts)
    report = reports / ("claude-idle-" + args.mode + "-2026-10-09.json")
    report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps({key: data.get(key) for key in (
    "mode", "ok", "elapsed_seconds", "monitor_in_tools", "initial_result_subtype",
    "event_sent_after_initial_result", "final_result_subtype", "user_prompts_sent")}, ensure_ascii=False))
raise SystemExit(0 if data["ok"] else 1)
