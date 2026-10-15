"""Штатный selected-path checkpoint с фиксацией argv и сохранности HEAD/индексов."""
import argparse
import hashlib
import json
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path


def git(*args, cwd=None):
    return subprocess.check_output(["git", "--no-optional-locks", *args], cwd=cwd)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def state(root):
    index = git("rev-parse", "--path-format=absolute", "--git-path", "index", cwd=root).decode().strip()
    marker = root / ".git"
    return {
        "head": git("rev-parse", "HEAD", cwd=root).decode().strip(),
        "index_sha256": sha(index),
        "status_sha256": hashlib.sha256(git("status", "--porcelain=v1", "-z", "--untracked-files=all", cwd=root)).hexdigest(),
        "git_marker_sha256": sha(marker) if marker.is_file() else "directory",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--label", default="scene-detail-ui-provider")
    parser.add_argument("--out", default="tasks/scene-detail/reports/ui-provider/checkpoint-retry.json")
    parser.add_argument("--authorization-event", type=int, default=7657)
    parser.add_argument("--expected-base")
    args = parser.parse_args()
    root = Path(git("rev-parse", "--show-toplevel").decode().strip())
    foreign = root / ".claude/worktrees/agent-a285bf5f9a5db01c1"
    out = (root / args.out).resolve()
    if not out.is_relative_to((root / "tasks/scene-detail/reports").resolve()):
        raise ValueError("Квитанция должна оставаться в reports своей задачи")
    if out.exists():
        raise RuntimeError("Квитанция повтора уже есть: сначала проверить durable исход")
    baseline = json.loads((root / ".agent-state/editor-broker/config.json").read_text(encoding="utf-8-sig"))["baseline_sha"]
    if args.expected_base and baseline != args.expected_base:
        raise ValueError("Текущая база изменилась до checkpoint")
    paths = ["Assets/Scripts/Maps/Detail", "Assets/Editor/VR_Battlegrounds/Maps/Detail",
             "Assets/Scenes/Lobby.unity", "Assets/Scenes/Lobby/LobbyDetailProfile.asset",
             "ProjectSettings/QualitySettings.asset"]
    textures = git("diff", "--name-only", "--", "Assets/Art/Lobby", "Assets/Materials/Lobby").decode().splitlines()
    if len(textures) != 140 or any(not p.endswith(".meta") for p in textures):
        raise RuntimeError("Состав140 texture-meta изменился; автоматический повтор отменён")
    paths.extend(textures)
    argv = [sys.executable, "Tools/agents/editor-broker.py", "checkpoint", "--label",
            args.label, "--onto-baseline"]
    for path in paths:
        argv.extend(("--path", path))
    version = (root / ".agent-state/editor-broker/runtime/VERSION").read_text().strip()
    before, foreign_before = state(root), state(foreign)
    report = {"authorization_event": args.authorization_event, "base_before": baseline,
              "started_utc": datetime.now(timezone.utc).isoformat(),
              "runtime_version": version, "argv": argv, "before": before, "foreign_before": foreign_before}
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    result = subprocess.run(argv, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    report.update({"exit_code": result.returncode, "stdout": result.stdout.decode("utf-8", errors="replace"),
                   "stderr": result.stderr.decode("utf-8", errors="replace"), "after": state(root),
                   "foreign_after": state(foreign), "finished_utc": datetime.now(timezone.utc).isoformat()})
    report["head_preserved"] = before["head"] == report["after"]["head"]
    report["index_preserved"] = before["index_sha256"] == report["after"]["index_sha256"]
    report["foreign_git_state_preserved"] = foreign_before == report["foreign_after"]
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    try:
        checkpoint_result = json.loads(report["stdout"])
    except ValueError:
        checkpoint_result = {"stdout_not_json": True}
    print(json.dumps({"exit_code": result.returncode,
                      "result": {key: checkpoint_result.get("result", {}).get(key) for key in ("sha", "ref", "parent")},
                      "stderr": report["stderr"][:2000],
                      "head_preserved": report["head_preserved"], "index_preserved": report["index_preserved"],
                      "foreign_git_state_preserved": report["foreign_git_state_preserved"], "report": str(out)}, ensure_ascii=False))
    if result.returncode or not all(report[k] for k in ("head_preserved", "index_preserved", "foreign_git_state_preserved")):
        raise SystemExit(1)


if __name__ == "__main__":
    main()
