"""Штатно дождаться своей OFFERED заявки и однократно claim; capability остаётся в reports/private."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--ticket", type=int, required=True)
args = parser.parse_args()
workspace = Path(__file__).resolve().parents[3]
broker = workspace / "Tools/agents/editor-broker.py"
private = workspace / "tasks/debug-menu-refactor/reports/private"
private.mkdir(parents=True, exist_ok=True)
watch = subprocess.run([sys.executable, "-B", "-X", "utf8", str(broker), "watch-ticket",
                        "--ticket", str(args.ticket), "--owner", "ui", "--until", "offered"],
                       cwd=workspace, capture_output=True, text=True, encoding="utf-8")
(private / f"watch-{args.ticket}.json").write_text(watch.stdout, encoding="utf-8", newline="\n")
receipt = json.loads(watch.stdout)
if watch.returncode != 0 or not receipt.get("ok") or receipt.get("result", {}).get("phase") != "OFFERED":
    print(json.dumps({"ok": False, "ticket": args.ticket, "watch": receipt}))
    raise SystemExit(1)
claim = subprocess.run([sys.executable, "-B", "-X", "utf8", str(broker), "claim",
                        "--ticket", str(args.ticket), "--owner", "ui"],
                       cwd=workspace, capture_output=True, text=True, encoding="utf-8")
path = private / f"lease-{args.ticket}.json"
path.write_text(claim.stdout, encoding="utf-8", newline="\n")
claimed = json.loads(claim.stdout)
print(json.dumps({"ok": claimed.get("ok"), "ticket": args.ticket,
                  "private_file": str(path), "exit_code": claim.returncode}))
raise SystemExit(claim.returncode)
