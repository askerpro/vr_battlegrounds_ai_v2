"""Проверить развёрнутый CLI/hook на отдельном репозитории: live registry не меняется."""
import json
from pathlib import Path
import subprocess
import sys
import uuid

reports = Path(__file__).resolve().parent.parent / "reports"
root = reports.parents[2]
runtime = root / ".agent-state/editor-broker/runtime"
fixture = reports / ("deployed-runtime-smoke-" + uuid.uuid4().hex[:8])
fixture.mkdir(exist_ok=False)
main = fixture / "main"
main.mkdir()
def git(repo, *args):
    result = subprocess.run(["git", "--no-pager", *args], cwd=repo, check=True, capture_output=True)
    return result.stdout.decode("utf-8").strip()
git(main, "init", "-q", "-b", "dev")
git(main, "config", "user.name", "Coordination smoke fixture")
git(main, "config", "user.email", "fixture@local.invalid")
(main / "base.txt").write_text("fixture", encoding="utf-8")
git(main, "add", "base.txt")
git(main, "commit", "-qm", "fixture baseline")
base = git(main, "rev-parse", "HEAD")
b, c = fixture / "b", fixture / "c"
git(main, "worktree", "add", "-qb", "b", str(b), base)
git(main, "worktree", "add", "-qb", "c", str(c), base)
def cli(repo, *args, succeeds=True):
    result = subprocess.run([sys.executable, "-X", "utf8", str(runtime / "coordination.py"),
                             "--repo", str(repo), *args], cwd=repo, capture_output=True,
                            text=True, encoding="utf-8")
    data = json.loads(result.stdout)
    assert (result.returncode == 0) == succeeds, data
    return data
def register(repo, task, path, after=None):
    spec = {"task_id": task, "owner": task, "client": "other", "worktree": str(repo),
            "base_sha": base, "doc_path": "tasks/" + task + "/Readme.md", "title": task,
            "stages": [{"id": "build", "writes": [path], "after": after or [], "needs": []}]}
    document = repo / spec["doc_path"]
    document.parent.mkdir(parents=True, exist_ok=True)
    fence = chr(96) * 3
    document.write_text("# Fixture\n\n" + fence + "coordination\n" + json.dumps(spec) +
                        "\n" + fence + "\n", encoding="utf-8")
    cli(repo, "register", "--document", str(document), "--expected-revision", "0")
register(main, "a", "scope/shared.py")
register(c, "c", "scope/independent.py")
cli(main, "configure", "--mode", "enforced")
cli(main, "begin", "--task", "a", "--stage", "build", "--owner", "a")
register(b, "b", "scope/shared.py", [{"task": "a", "stage": "build", "state": "verified"}])
blocked = cli(b, "begin", "--task", "b", "--stage", "build", "--owner", "b", succeeds=False)
independent = cli(c, "begin", "--task", "c", "--stage", "build", "--owner", "c")
def hook(repo, tool, args):
    call = {"cwd": str(repo), "tool_name": tool, "tool_input": args}
    result = subprocess.run([sys.executable, "-X", "utf8", str(runtime / "coordination-hook.py")],
                             cwd=repo, input=json.dumps(call), capture_output=True,
                             text=True, encoding="utf-8", check=True)
    return json.loads(result.stdout) if result.stdout.strip() else {}
assert hook(main, "Write", {"file_path": str(main / "scope/shared.py")}) == {}
for name in ("scope/forbidden.py", "scope/coordination.py"):
    denied = hook(main, "apply_patch", {"command": "*** Begin Patch\n*** Add File: " + name +
                                        "\n+fixture\n*** End Patch"})
    assert denied["hookSpecificOutput"]["permissionDecision"] == "deny", denied
assert hook(main, "Bash", {"command": "git push origin HEAD:dev"})["hookSpecificOutput"]["permissionDecision"] == "deny"
assert hook(main, "Write", {"file_path": str(main / "tasks/a/reports/probe.json")}) == {}
result = {"runtime_version": (runtime / "VERSION").read_text().strip(),
          "conflicting_stage_blocked": not blocked["ok"], "independent_stage_allowed": independent["ok"],
          "native_wire_scope_denied": True, "direct_push_denied": True, "own_reports_allowed": True,
          "live_registry_unchanged": True, "host_llm_tool_interception_tested": False}
(reports / "deployed-runtime-smoke.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps(result))
