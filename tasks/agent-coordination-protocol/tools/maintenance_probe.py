"""Краткий статус обслуживания; полный публичный ответ хранится только в reports."""
import json
from pathlib import Path
import subprocess
import sys

reports = Path(__file__).resolve().parent.parent / "reports"
root = reports.parents[2]
sys.path.insert(0, str(root / ".agent-state/editor-broker/runtime"))
from editor_broker.queue import BrokerStore
store = BrokerStore(root / ".agent-state/editor-broker")
response = {"ok": True, "result": store.status()}
(reports / "maintenance-broker-status.json").write_text(json.dumps(response, ensure_ascii=False, indent=2), encoding="utf-8")
status = response.get("result", {})
def brief(ticket):
    if not ticket:
        return None
    return {k: ticket.get(k) for k in ("id", "owner", "phase", "agent_root", "updated_at", "lease_expires_at")}
terminal = {"DONE", "FAILED", "CANCELLED"}
pending = [brief(t) for t in status.get("tickets", []) if t.get("phase") not in terminal]
print(json.dumps({"ok": response.get("ok"), "blocked": status.get("blocked"),
                  "recovery_required": status.get("recovery_required"), "paused": store.hold()["paused"],
                  "active": brief(status.get("active")), "offered": brief(status.get("offered")),
                  "pending_count": len(pending), "pending": pending[:10]}, ensure_ascii=False))
