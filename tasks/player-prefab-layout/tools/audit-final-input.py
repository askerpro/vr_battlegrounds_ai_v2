"""Статический аудит LF/scope и идентичности проверенного Unity input перед публикацией."""
import json
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[3]
INPUT = "d928805a31e4787bc4a2454b4d7c2861c04ee17c"


def git(*args):
    return subprocess.run(["git", "--no-pager", *args], cwd=ROOT, check=True, capture_output=True).stdout


paths = git("diff", "--cached", "--name-only", "--diff-filter=ACMR", "-z").decode("utf-8").strip("\0").split("\0")
issues = []
physical_checked = 0
staged_checked = 0
product_checked = 0
text_extensions = {".cs", ".py", ".md", ".json", ".txt", ".meta", ".prefab", ".mat", ".unity"}
for path in paths:
    if not path:
        continue
    physical = (ROOT / path).read_bytes()
    blob = git("show", ":" + path)
    if pathlib.Path(path).suffix in text_extensions or physical.startswith(b"%YAML"):
        physical_checked += 1
        staged_checked += 1
        if b"\r" in physical:
            issues.append({"path": path, "source": "physical", "issue": "CR"})
        if b"\r" in blob:
            issues.append({"path": path, "source": "index", "issue": "CR"})
    if path.startswith("Assets/"):
        product_checked += 1
        if blob != git("show", INPUT + ":" + path):
            issues.append({"path": path, "issue": "differs_from_tested_input"})
    if "/reports/" in path or path.startswith(("Assets/Data/", "Assets/Scripts/", "Assets/ThirdParty/")) or "BotCombatStand" in path:
        issues.append({"path": path, "issue": "outside_owned_final_scope"})

# Удалённые старые пути также должны отсутствовать в проверенном input.
deleted = git("diff", "--cached", "--name-only", "--diff-filter=D", "-z").decode("utf-8").strip("\0").split("\0")
for path in filter(None, deleted):
    result = subprocess.run(["git", "cat-file", "-e", INPUT + ":" + path], cwd=ROOT, capture_output=True)
    if result.returncode == 0:
        issues.append({"path": path, "issue": "deleted_after_tested_input"})

report = {"tested_input": INPUT, "physical_lf_checked": physical_checked, "index_lf_checked": staged_checked,
          "product_blobs_identical_to_tested_input": product_checked, "issues": issues, "passed": not issues}
target = ROOT / "tasks/player-prefab-layout/reports/test-repair/final-input-identity-lf.json"
target.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps(report, ensure_ascii=False))
sys.exit(bool(issues))
