"""Read-only проверка текущих планов перед enforced, без чужих правок и ACK."""
import json
from pathlib import Path
import sys

task_folder = Path(__file__).resolve().parent.parent
reports = task_folder / "reports"
root = task_folder.parent.parent
sys.path.insert(0, str(root / ".agent-state/editor-broker/runtime"))
from agent_coordination.documents import plan_view
from agent_coordination.model import CoordinationError, validate_plan
from agent_coordination.store import CoordinationStore

store = CoordinationStore(root / ".agent-state/coordination")
with store.transaction(write=False) as db:
    tasks = store.all(db, "tasks")
results = []
for task in tasks:
    errors = []
    try:
        validate_plan(plan_view(task))
    except (CoordinationError, KeyError, ValueError) as error:
        errors.append(str(error))
    if not task.get("plan_source"):
        errors.append("MIGRATION: требуется новая регистрация с привязкой к локальному плану")
    else:
        try:
            store.check_plan_source(task)
        except (CoordinationError, OSError, ValueError) as error:
            errors.append(str(error))
    results.append({"task": task["task_id"], "revision": task["revision"], "ready": not errors,
                    "errors": errors, "document": str(Path(task["worktree"]) / task["doc_path"])})
result = {"ready": all(item["ready"] for item in results), "tasks": results,
          "scope": "формат и актуальность планов; не подтверждает полноту контрактов и клиентский перехват"}
reports.mkdir(exist_ok=True)
(reports / "migration-readiness.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"ready": result["ready"], "tasks": [{key: item[key] for key in
                  ("task", "revision", "ready", "errors")} for item in results],
                  "report": str(reports / "migration-readiness.json")}, ensure_ascii=False))
