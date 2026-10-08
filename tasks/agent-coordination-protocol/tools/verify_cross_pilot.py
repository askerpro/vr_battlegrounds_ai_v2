"""Проверка доказательств пилота и восстановления адресованного inbox."""
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, "F:/UnityProjects/agent-infra")
from agent_coordination.store import CoordinationStore

if len(sys.argv) == 3 and sys.argv[1] == "--read-child":
    store = CoordinationStore(sys.argv[2])
    print(json.dumps([event["id"] for event in store.inbox("claude-probe", "claude-probe-owner")]))
    raise SystemExit(0)

reports = Path(__file__).resolve().parent.parent / "reports"
claude = json.loads((reports / "claude-idle-monitor-2026-10-09.json").read_text(encoding="utf-8"))
codex = json.loads((reports / "cross-pilot-codex-2026-10-09.json").read_text(encoding="utf-8"))
assert claude["ok"] and codex["ok"]
assert claude["user_prompts_sent"] == 1
assert claude["event_sent_after_initial_result"]
assert codex["thread_requests"] == 1 and codex["model_turns_started"] == 2
assert codex["turn_status"] == "completed"
state = reports / ("exchange-" + claude["nonce"])
store = CoordinationStore(state)
events = [e for e in store.events(0, 100) if e["kind"].startswith("pilot_")]
assert [e["kind"] for e in events] == ["pilot_request", "pilot_reply", "pilot_final"]
assert [e["recipient"] for e in events] == ["codex-probe", "claude-probe", "codex-probe"]
for name in ("codex-probe", "claude-probe"):
    assert not any(e["kind"].startswith("pilot_") for e in store.inbox(name, name + "-owner"))

# Доставщик прочитал событие и завершился без ACK. Новый процесс должен получить его снова.
recovery = store.send_message("codex-probe", "codex-probe-owner", "claude-probe",
                              "recovery_probe", "No action: delivery recovery only")
child = subprocess.run([sys.executable, "-X", "utf8", str(Path(__file__).resolve()),
                        "--read-child", str(state)], capture_output=True, text=True,
                        encoding="utf-8", timeout=10, check=True)
assert recovery["id"] in json.loads(child.stdout)
reopened = CoordinationStore(state)
assert recovery["id"] in [e["id"] for e in reopened.inbox("claude-probe", "claude-probe-owner")]
reopened.ack_events("claude-probe", "claude-probe-owner", [recovery["id"]])
reopened.ack_events("claude-probe", "claude-probe-owner", [recovery["id"]])
assert recovery["id"] not in [e["id"] for e in reopened.inbox("claude-probe", "claude-probe-owner")]
result = {"passed": 2, "checks": ["live-two-client-roundtrip", "read-exit-replay-and-idempotent-ack"],
          "event_ids": [e["id"] for e in events], "codex_thread_id": codex["thread_id"],
          "recovery_event_id": recovery["id"], "limits": ["no generation fencing implemented",
          "no action exactly-once claim", "controlled sessions only"]}
(reports / "cross-pilot-verification-2026-10-09.json").write_text(
    json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, ensure_ascii=False))
