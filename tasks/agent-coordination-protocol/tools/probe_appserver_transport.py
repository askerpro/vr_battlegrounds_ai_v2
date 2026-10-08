"""Проверка JSON-RPC handshake без открытия чатов или запуска ходов модели."""
import argparse
import json
import queue
import subprocess
import threading
import time
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--executable", required=True)
    parser.add_argument("--report", required=True)
    parser.add_argument("--transport", choices=("proxy", "stdio"), default="proxy")
    parser.add_argument("--sock")
    parser.add_argument("--close-input", action="store_true")
    args = parser.parse_args()
    command = [args.executable, "app-server"]
    if args.transport == "proxy":
        command.append("proxy")
        if args.sock:
            command.extend(["--sock", args.sock])
    else:
        command.extend(["--listen", "stdio://"])
    process = subprocess.Popen(
        command,
        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
        text=True, encoding="utf-8", errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    incoming = queue.Queue()
    errors = []

    def read_stdout():
        for line in process.stdout:
            try:
                incoming.put(json.loads(line))
            except json.JSONDecodeError:
                incoming.put({"invalid_json": True})

    def read_stderr():
        for line in process.stderr:
            errors.append(line.rstrip())

    threads = [threading.Thread(target=read_stdout, daemon=True),
               threading.Thread(target=read_stderr, daemon=True)]
    for thread in threads:
        thread.start()
    result = {"probe": "initialize-only", "model_turns_started": 0,
              "thread_requests": 0, "ok": False, "transport": args.transport,
              "non_response_messages": [], "deadline_seconds": 15,
              "close_input": args.close_input}
    try:
        request = {"id": 1, "method": "initialize", "params": {
            "clientInfo": {"name": "coordination_transport_research",
                           "version": "0.1.0"}, "capabilities": {}}}
        process.stdin.write(json.dumps(request) + "\n")
        process.stdin.flush()
        if args.close_input:
            process.stdin.close()
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            try:
                message = incoming.get(timeout=0.25)
            except queue.Empty:
                if process.poll() is not None:
                    break
                continue
            if message.get("id") == 1:
                result["response"] = message
                result["ok"] = "result" in message and "error" not in message
                if result["ok"] and not args.close_input:
                    process.stdin.write(json.dumps({"method": "initialized"}) + "\n")
                    process.stdin.flush()
                break
            result["non_response_messages"].append({
                "keys": list(message), "method": message.get("method")})
    except (OSError, BrokenPipeError) as error:
        result["transport_error"] = str(error)
    finally:
        if not process.stdin.closed:
            process.stdin.close()
        try:
            process.wait(timeout=3)
        except subprocess.TimeoutExpired:
            # Завершаем только дочерний proxy этой пробы, не общий daemon.
            process.terminate()
            process.wait(timeout=3)
        for thread in threads:
            thread.join(timeout=1)
        result["proxy_exit_code"] = process.returncode
        result["stderr"] = errors[:10]
        report = Path(args.report)
        report.parent.mkdir(parents=True, exist_ok=True)
        report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                          encoding="utf-8")
    print(json.dumps({key: result[key] for key in (
        "probe", "ok", "model_turns_started", "thread_requests", "proxy_exit_code")},
        ensure_ascii=False))
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
