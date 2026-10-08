"""Изолированная проверка ответа Claude без инструментов и сохранения чата."""
import argparse
import json
import subprocess
import time
import uuid
from pathlib import Path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--executable", required=True)
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    report = Path(args.report).resolve()
    report.parent.mkdir(parents=True, exist_ok=True)
    workspace = report.parent / "claude-live-fixture"
    workspace.mkdir(exist_ok=True)
    nonce = uuid.uuid4().hex
    expected = "TRANSPORT_ACK_" + nonce
    command = [args.executable, "--print", "--restricted", "--tools", "",
               "--strict-mcp-config", "--mcp-config", '{"mcpServers":{}}',
               "--no-session-persistence", "--output-format", "json",
               "--max-budget-usd", "1", "--system-prompt",
               "You are a transport test. Return only the requested literal. Do not use tools.",
               "Return exactly: " + expected]
    start = time.monotonic()
    process = subprocess.Popen(command, cwd=workspace, stdin=subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               encoding="utf-8", errors="replace",
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    timed_out = False
    try:
        stdout, stderr = process.communicate(timeout=55)
    except subprocess.TimeoutExpired:
        timed_out = True
        process.terminate()
        stdout, stderr = process.communicate(timeout=5)
    parsed = None
    try:
        parsed = json.loads(stdout)
    except json.JSONDecodeError:
        pass
    response = parsed.get("result", "") if isinstance(parsed, dict) else ""
    ok = process.returncode == 0 and response.strip() == expected
    data = {"probe": "claude-isolated-literal", "ok": ok, "elapsed_seconds":
            round(time.monotonic() - start, 3), "exit_code": process.returncode,
            "timed_out": timed_out, "nonce": nonce, "expected": expected,
            "response": parsed, "stdout": stdout, "stderr": stderr,
            "tools_enabled": False, "session_persistence": False}
    report.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    summary = {key: data[key] for key in ("probe", "ok", "elapsed_seconds", "exit_code", "timed_out")}
    if isinstance(parsed, dict):
        summary["subtype"] = parsed.get("subtype")
        summary["is_error"] = parsed.get("is_error")
        if parsed.get("is_error"):
            summary["error"] = response[:300]
    print(json.dumps(summary, ensure_ascii=False))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
