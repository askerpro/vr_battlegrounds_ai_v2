"""Краткий прогресс реальной миграции без токенов и исходных документов в контексте."""
import json
from pathlib import Path
import subprocess
import sys

reports = Path(__file__).resolve().parent.parent / "reports"
root = reports.parents[2]
inventory = json.loads((reports / "migration-inventory.json").read_text(encoding="utf-8"))["active"]
response = subprocess.run([sys.executable, "-X", "utf8", str(root / "Tools/agents/coordination.py"), "status"],
                          cwd=root, check=True, capture_output=True, text=True, encoding="utf-8")
tasks = json.loads(response.stdout)["result"]["tasks"]
registered, missing = [], []
for expected in inventory:
    found = next((task for task in tasks if task["task_id"] == expected["task_id"] or
                  task["owner"] == expected["owner_candidate"]), None)
    if found:
        registered.append({"task": expected["task_id"], "registered_id": found["task_id"],
                           "revision": found["revision"], "stages": len(found["stages"])})
    else:
        missing.append(expected["task_id"])
mode = json.loads((root / ".agent-state/coordination/config.json").read_text(encoding="utf-8"))["mode"]
result = {"mode": mode, "active_registered": registered, "missing": missing,
          "controller_registered": any(task["task_id"] == "agent-coordination-protocol" for task in tasks)}
(reports / "migration-progress.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False))
