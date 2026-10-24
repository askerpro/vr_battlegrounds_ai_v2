"""Точка входа heavy-регрессии: python Tools/e2e/heavy.py --profile editor --suite default."""
import argparse
import asyncio
from datetime import datetime
import json
import os
from pathlib import Path
import subprocess
import sys

from . import editmode as M
from . import freshness as F
from . import suite as S
from . import verdict as V

PROJECT_ROOT = Path(__file__).resolve().parents[3]
RESULTS = PROJECT_ROOT / "Tools" / "e2e" / "results" / "heavy"


def git(*args, timeout=300, env=None):
    return subprocess.run(["git", *args], cwd=PROJECT_ROOT, capture_output=True, text=True,
                          encoding="utf-8", errors="replace", timeout=timeout,
                          env=dict(os.environ, **env) if env else None)


def normalized(path):
    return str(Path(path).resolve()).replace("\\", "/").rstrip("/").casefold()


def linked_worktree():
    gitdir = git("rev-parse", "--absolute-git-dir").stdout.strip()
    common = git("rev-parse", "--path-format=absolute", "--git-common-dir").stdout.strip()
    return bool(gitdir and common) and normalized(gitdir) != normalized(common)


def branch_check(fetch):
    """Ветка (HEAD) поверх свежей origin/dev."""
    note = ""
    if fetch:
        fetched = git("fetch", "--quiet", "origin", "dev")
        if fetched.returncode:
            note = " (fetch не удался: " + fetched.stderr.strip()[:200] + "; сравнение с локальной origin/dev)"
    origin = git("rev-parse", "origin/dev").stdout.strip()
    head = git("rev-parse", "HEAD").stdout.strip()
    dirty = bool(git("status", "--porcelain", "--untracked-files=no").stdout.strip())
    drift = F.drift(git, head, origin)
    info = {"head": head, "originDev": origin, "trackedChanges": dirty, "drift": drift}
    result = F.check("Ветка содержит игровые изменения свежей origin/dev", drift, head, origin)
    result["detail"] = str(result.get("detail") or "") + note
    return result, info


def prepare_outdir(arguments, profile, suite_name):
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    outdir = Path(arguments.output_dir).resolve() if arguments.output_dir else RESULTS / (stamp + "-" + profile + "-" + suite_name)
    probe = outdir / "verdict.json"
    if outdir.is_relative_to(PROJECT_ROOT) and git("check-ignore", "--quiet", "--no-index", str(probe)).returncode != 0:
        raise ValueError("Каталог отчёта должен быть исключён из Git: " + str(outdir))
    outdir.mkdir(parents=True, exist_ok=True)
    return outdir


def editmode_item(record):
    """Запись EditMode в списке вердикта рядом со сценариями."""
    summary = record.get("summary") or {}
    text = ""
    if summary.get("total"):
        text = (str(summary.get("total")) + " тестов, красных " + str(len(record.get("red", []))) + ": новых "
                + str(len(record.get("newRed", []))) + ", известных " + str(len(record.get("stillRed", [])))
                + ", предупреждений " + str(len(record.get("warnings", []))))
    return {"name": "EditMode", "topology": "", "verdict": record["verdict"], "reasons": record.get("reasons", []),
            "notes": record.get("notes", []), "summary": text, "editmode": record}


def scenario_summary(record):
    parts = []
    for participant in record.get("participants", []):
        parts.append(participant["role"] + " " + str(participant["checksPassed"]) + "/" + str(participant["checksTotal"]))
    totals = {}
    for scan in (record.get("logs") or {}).get("scans", []):
        for name, count in scan["counts"].items():
            totals[name] = totals.get(name, 0) + count
    if totals:
        parts.append("логи: UxrComponentNotFound " + str(totals.get("uxrComponentNotFound", 0))
                     + ", NRE " + str(totals.get("nullReference", 0)) + ", Exception " + str(totals.get("exception", 0)))
    if record.get("readySeconds") is not None:
        parts.append("Ready " + str(record["readySeconds"]) + " с")
    if record.get("launchRetries"):
        parts.append("повтор старта стенда ×" + str(len(record["launchRetries"])) + " (launchRetries в verdict.json)")
    return "; ".join(parts)


def main(argv=None):
    parser = argparse.ArgumentParser(prog="heavy.py", description=(
        "Heavy-регрессия перед вливанием: набор e2e-сценариев и один вердикт. Код 0 — можно вливать, "
        "1 — найдена регрессия, 2 — вердикт не получен (тоже нельзя вливать). Профиль editor запускать "
        "из linked worktree в своей RUNNING аренде worker: вызовы Unity идут через MCP-прокси этого worktree."))
    parser.add_argument("--profile", default="editor", choices=S.PROFILES)
    parser.add_argument("--suite", default="default", help="имя набора в Tools/e2e/suites или путь к .json")
    parser.add_argument("--scenario", action="append", help="прогнать только этот сценарий набора (можно повторять)")
    parser.add_argument("--output-dir", help="каталог отчёта (исключённый из Git); по умолчанию Tools/e2e/results/heavy/<время>-<профиль>-<набор>")
    parser.add_argument("--no-fetch", action="store_true", help="не обновлять origin/dev перед проверкой ветки")
    parser.add_argument("--editmode", default="off", choices=("off", "also", "only"),
                        help="EditMode после сценариев (also) или вместо них (only): новые красные против "
                             "Tools/e2e/editmode-known-red.json")
    parser.add_argument("--build-checkout", help="профиль windows: выделенный сборочный checkout Win64; по умолчанию "
                                                 "F:/CodexWorktrees/build-test-win64/Vr_Battlegrounds_ai")
    arguments = parser.parse_args(argv)
    if arguments.editmode != "off" and arguments.profile != "editor":
        print("heavy: --editmode только в профиле editor (EditMode идёт в Unity worker)", file=sys.stderr)
        return 2
    try:
        return run(arguments)
    except Exception as error:  # noqa: BLE001 — любой сбой самого heavy: вердикта нет, это не регрессия
        print("heavy: сбой без вердикта: " + type(error).__name__ + ": " + str(error), file=sys.stderr)
        return 2


def run(arguments):
    def log(text):
        print(text, flush=True)

    try:
        suite = S.load(arguments.suite)
        scenarios = [] if arguments.editmode == "only" else S.select(suite, arguments.profile, arguments.scenario)
        framework = V.load_known_noise()
        known_red = None if arguments.editmode == "off" else M.load_known_red()
        outdir = prepare_outdir(arguments, arguments.profile, suite.name)
    except (S.SuiteError, V.NoiseError, M.KnownRedError, ValueError) as error:
        print("heavy: " + str(error), file=sys.stderr)
        return 2
    trace_path = outdir / "calls.jsonl"

    def trace(item):
        with trace_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(item, ensure_ascii=False) + "\n")

    report = {"profile": arguments.profile, "suite": suite.name, "suitePath": suite.path,
              "started": datetime.now().astimezone().isoformat(timespec="seconds"), "worktree": str(PROJECT_ROOT),
              "reportPath": str(outdir / "verdict.json"), "checks": [], "scenarios": []}
    planned = [s.name for s in scenarios] + ([] if arguments.editmode == "off" else ["EditMode"])
    log("HEAVY " + arguments.profile + "/" + suite.name + ": " + ", ".join(planned) + " → " + str(outdir))
    check, branch = branch_check(not arguments.no_fetch)
    report["checks"].append(check)
    report["branch"] = branch
    records = []
    if arguments.profile == "windows":
        from . import windows_profile
        checks, records, extra = windows_profile.run(PROJECT_ROOT, git, scenarios, outdir, log, framework,
                                                     arguments.build_checkout)
        report["checks"].extend(checks)
        report.update(extra)
    elif arguments.profile != "editor":
        report["checks"].append({"name": "Профиль " + arguments.profile, "verdict": V.INCONCLUSIVE,
                                 "detail": "ещё не реализован (эпик e2e-regression, этап HeavyQuest)"})
    elif not linked_worktree():
        report["checks"].append({"name": "Запуск из linked worktree", "verdict": V.INCONCLUSIVE,
                                 "detail": "основной checkout адресует Unity человека; запускать из worktree задачи в своей аренде"})
    else:
        from . import editor_profile
        checks, records, extra = asyncio.run(editor_profile.run(
            PROJECT_ROOT, scenarios, outdir, log, trace, framework, branch.get("originDev"), known_red))
        report["checks"].extend(checks)
        editmode_record = extra.pop("editmode", None)
        report.update(extra)
        worker_head = (extra.get("worker") or {}).get("head")
        if worker_head:
            tree, error = F.snapshot_tree(git, outdir / "snapshot.index")
            (outdir / "snapshot.index").unlink(missing_ok=True)
            identity = F.code_identity(git, worker_head, tree) if tree else {"known": False, "error": error}
            report["codeIdentity"] = identity
            report["checks"].append(F.identity_check(identity, worker_head))
        if known_red is not None:
            if editmode_record is None:
                editmode_record = {"verdict": V.INCONCLUSIVE, "reasons": ["EditMode не выполнен — см. проверки выше"]}
            elif editmode_record.get("red") is not None:
                candidate = M.candidate_known_red(editmode_record, known_red, worker_head)
                (outdir / "editmode-known-red.candidate.json").write_text(
                    json.dumps(candidate, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            report["editmode"] = editmode_record
    for record in records:
        verdict, reasons = V.scenario_verdict(record)
        record["verdict"], record["reasons"], record["summary"] = verdict, reasons, scenario_summary(record)
        for participant in record.get("participants", []):
            participant.pop("data", None)
        report["scenarios"].append(record)
    ran = {r["name"] for r in records}
    for scenario in scenarios:
        if scenario.name not in ran and records:
            report["scenarios"].append({"name": scenario.name, "topology": scenario.topology, "verdict": V.INCONCLUSIVE,
                                        "reasons": ["не запускался: стенд не убран после предыдущего сценария"]})
    if known_red is not None:
        report["scenarios"].append(editmode_item(report.get("editmode") or {
            "verdict": V.INCONCLUSIVE, "reasons": ["EditMode не выполнен — профиль или checkout не подходят"]}))
    report["verdict"], report["exitCode"] = V.aggregate(report["checks"], report["scenarios"])
    report["finished"] = datetime.now().astimezone().isoformat(timespec="seconds")
    (outdir / "verdict.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    log(V.render_text(report))
    return report["exitCode"]
