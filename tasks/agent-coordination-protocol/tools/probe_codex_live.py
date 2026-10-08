"""Проверка одного хода собственной ephemeral-сессии Codex."""
import argparse
import json
import queue
import subprocess
import threading
import time
import uuid
import sys
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--executable", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--exchange-state")
    parser.add_argument("--notify-file")
    args = parser.parse_args()
    report = Path(args.report).resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    workspace = report.parent / "codex-live-fixture"
    workspace.mkdir(exist_ok=True)
    expected = "TRANSPORT_ACK_" + uuid.uuid4().hex
    store = None
    first_event = None
    if args.exchange_state:
        sys.path.insert(0, "F:/UnityProjects/agent-infra")
        from agent_coordination.store import CoordinationStore
        store = CoordinationStore(args.exchange_state)
        first_event = next(e for e in store.inbox("codex-probe", "codex-probe-owner")
                           if e["kind"] == "pilot_request")
        nonce = json.loads(first_event["payload"]["body"])["nonce"]
        expected = "EVENT_ACK_" + str(first_event["id"]) + "_" + nonce
    process = subprocess.Popen([args.executable, "app-server", "--listen", "stdio://"],
        cwd=workspace, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        encoding="utf-8", errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    incoming = queue.Queue()
    errors = []
    transcript = []

    def reader():
        for line in process.stdout:
            try:
                incoming.put(json.loads(line))
            except json.JSONDecodeError:
                incoming.put({"invalid_json": True})

    def stderr_reader():
        for line in process.stderr:
            errors.append(line.rstrip())

    threading.Thread(target=reader, daemon=True).start()
    threading.Thread(target=stderr_reader, daemon=True).start()
    data = {"probe": "codex-ephemeral-literal", "expected": expected, "ok": False,
            "thread_requests": 0, "model_turns_started": 0}
    deadline = time.monotonic() + 55
    start = time.monotonic()

    def send(value):
        process.stdin.write(json.dumps(value) + "\n")
        process.stdin.flush()

    def receive():
        while time.monotonic() < deadline:
            try:
                item = incoming.get(timeout=0.25)
                transcript.append(item)
                if "method" in item and "id" in item:
                    send({"id": item["id"], "error": {"code": -32601,
                          "message": "Transport probe does not authorize tools"}})
                return item
            except queue.Empty:
                if process.poll() is not None:
                    raise RuntimeError("app-server exited before response")
        raise TimeoutError("55-second probe deadline")

    def request(identifier, method, params):
        send({"id": identifier, "method": method, "params": params})
        while True:
            item = receive()
            if item.get("id") == identifier:
                if "error" in item:
                    raise RuntimeError(json.dumps(item["error"]))
                return item["result"]

    try:
        request(1, "initialize", {"clientInfo": {"name": "coordination_live_probe",
                "version": "0.1.0"}, "capabilities": {}})
        send({"method": "initialized"})
        data["thread_requests"] = 1
        thread = request(2, "thread/start", {"cwd": str(workspace), "ephemeral": True,
            "approvalPolicy": "never", "sandbox": "read-only",
            "developerInstructions": "Transport test only. Return the requested literal. Do not use tools."})
        thread_id = thread["thread"]["id"]
        data["thread_id"] = thread_id
        def model_turn(identifier, literal):
            turn = request(identifier, "turn/start", {"threadId": thread_id,
                "input": [{"type": "text", "text": "Return exactly: " + literal}]})
            data["model_turns_started"] += 1
            data["turn_id"] = turn["turn"]["id"]
            responses = []
            while True:
                item = receive()
                params = item.get("params", {})
                if item.get("method") == "item/completed":
                    completed = params.get("item", {})
                    if completed.get("type") == "agentMessage":
                        responses.append(completed.get("text", ""))
                if item.get("method") == "turn/completed" and params.get("turn", {}).get("id") == data["turn_id"]:
                    data["turn_status"] = params["turn"].get("status")
                    data["response"] = "\n".join(responses).strip()
                    return data["turn_status"] == "completed" and data["response"] == literal
        data["ok"] = model_turn(3, expected)
        if store and data["ok"]:
            store.ack_events("codex-probe", "codex-probe-owner", [first_event["id"]])
            reply = store.send_message("codex-probe", "codex-probe-owner", "claude-probe",
                "pilot_reply", json.dumps({"nonce": nonce, "reply_to": first_event["id"]}))
            data["reply_event_id"] = reply["id"]
            Path(args.notify_file).write_text(json.dumps({"nonce": str(reply["id"]) + "_" + nonce,
                "event_id": reply["id"], "exchange_state": args.exchange_state}), encoding="utf-8")
            final_event = None
            while time.monotonic() < deadline:
                final_event = next((e for e in store.inbox("codex-probe", "codex-probe-owner")
                                    if e["kind"] == "pilot_final"), None)
                if final_event:
                    break
                time.sleep(0.1)
            if not final_event:
                raise TimeoutError("Claude final event not received")
            final_literal = "EVENT_ACK_" + str(final_event["id"]) + "_" + nonce
            data["ok"] = model_turn(4, final_literal)
            if data["ok"]:
                store.ack_events("codex-probe", "codex-probe-owner", [final_event["id"]])
                data["final_event_id"] = final_event["id"]
    except (RuntimeError, TimeoutError, OSError) as error:
        data["ok"] = False
        data["error"] = str(error)
    finally:
        process.stdin.close()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            process.terminate()
            process.wait(timeout=3)
        data.update(elapsed_seconds=round(time.monotonic() - start, 3),
                    transcript=transcript, stderr=errors, exit_code=process.returncode)
        report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: data.get(key) for key in (
        "probe", "ok", "elapsed_seconds", "thread_requests", "model_turns_started",
        "turn_status", "error")}, ensure_ascii=False))
    return 0 if data["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
