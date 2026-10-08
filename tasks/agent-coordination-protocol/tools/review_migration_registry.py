"""Свести формальные планы и договорённости live registry, не раскрывая tokens."""
import json
from pathlib import Path
import sys

reports = Path(__file__).resolve().parent.parent / "reports"
root = reports.parents[2]
sys.path.insert(0, str(root / ".agent-state/editor-broker/runtime"))
from agent_coordination.store import CoordinationStore
store = CoordinationStore(root / ".agent-state/coordination")
with store.transaction(write=False) as db:
    tasks = store.all(db, "tasks")
    contracts = store.all(db, "contracts")
result = {"tasks": [{"task": task["task_id"], "owner": task["owner"], "worktree": task["worktree"],
                       "revision": task["revision"], "doc_path": task["doc_path"], "stages": [
                           {"id": stage["id"], "state": stage["state"], "writes": stage["spec"]["writes"],
                            "after": stage["spec"].get("after", []), "needs": stage["spec"].get("needs", [])}
                           for stage in task["stages"]]} for task in tasks],
          "contracts": [{"id": item.get("contract_id", item.get("id")),
                          "owner": item.get("owner_task"), "revision": item.get("revision"),
                          "state": item.get("state")} for item in contracts]}
(reports / "migration-registry-review.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"tasks": [{key: task[key] for key in ("task", "revision", "doc_path")}
                            for task in result["tasks"]], "contract_count": len(contracts),
                  "report": str(reports / "migration-registry-review.json")}, ensure_ascii=False))
