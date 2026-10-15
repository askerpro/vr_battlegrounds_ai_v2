"""Проверка provenance input: своя дельта UI и побайтно уже принятые файлы origin/dev."""
import argparse
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--input", required=True)
parser.add_argument("--accepted", required=True)
args = parser.parse_args()
workspace = Path(__file__).resolve().parents[3]


def git(*arguments):
    return subprocess.check_output(["git", *arguments], cwd=workspace).decode("utf-8").strip()


source_paths = {
    "Assets/Scripts/UI/Menu/MenuDebug.cs", "Assets/Scripts/UI/Menu/MenuPerfTests.cs",
    "Assets/Scripts/UI/Menu/MenuPlayersTeams.cs", "Assets/Scripts/UI/Menu/Kit/MenuKit.cs",
}
parent = git("rev-parse", args.input + "^")
paths = git("diff", "--name-only", parent, args.input).splitlines()
owned, accepted, failures = [], [], []
for path in paths:
    if path in source_paths or path.startswith(("tasks/debug-menu-refactor/", "experts/ui/")):
        owned.append(path)
    elif git("ls-tree", args.input, "--", path) == git("ls-tree", args.accepted, "--", path):
        accepted.append(path)
    else:
        failures.append("Чужая дельта не совпадает с accepted source: " + path)
for path in source_paths:
    if git("rev-parse", args.input + ":" + path) != git("hash-object", path):
        failures.append("Input не совпадает с рабочим UI-исходником: " + path)
report = {"passed": not failures, "input": args.input, "base": parent,
          "accepted_source": git("rev-parse", args.accepted), "owned": owned,
          "accepted_foreign": accepted, "failures": failures,
          "scope": "Статическое происхождение Git blobs; не Unity, не grant чужого WIP и не приёмка"}
output = workspace / "tasks/debug-menu-refactor/reports/input-scope-current.json"
output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
print(json.dumps({"passed": report["passed"], "input": args.input, "base": parent,
                  "owned_count": len(owned), "accepted_count": len(accepted),
                  "failures": failures, "report": str(output)}, ensure_ascii=False))
raise SystemExit(0 if report["passed"] else 1)
