"""Одно ограниченное ожидание только тестового события, без доступа к хабу."""
import argparse
import json
import sys
import time
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--event", required=True)
parser.add_argument("--rewake", action="store_true")
args = parser.parse_args()
deadline = time.monotonic() + 35
while time.monotonic() < deadline:
    path = Path(args.event)
    if path.exists():
        data = json.loads(path.read_text(encoding="utf-8"))
        if "exchange_state" in data:
            sys.path.insert(0, "F:/UnityProjects/agent-infra")
            from agent_coordination.store import CoordinationStore
            pending = CoordinationStore(data["exchange_state"]).inbox("claude-probe", "claude-probe-owner")
            if not any(event["id"] == data["event_id"] for event in pending):
                raise SystemExit(3)
        text = "Transport test event: respond exactly EVENT_ACK_" + data["nonce"]
        print(text, file=sys.stderr if args.rewake else sys.stdout, flush=True)
        raise SystemExit(2 if args.rewake else 0)
    time.sleep(0.1)
raise SystemExit(0)
