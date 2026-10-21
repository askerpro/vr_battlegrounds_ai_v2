"""Сводка тура по видам Lobby: AppGPUTime p50 каждой измеряемой фазы каждого прогона серии run_quest_series.py.

Каждый прогон — полный views file (все виды подряд). import-runtime прогона раскладывает фазы; фаза с viewId в
conditions — измеряемая. p50 вида = медиана p50 прогонов. Худший вид сравнивается с бюджетом (по умолчанию 7 мс).
"""
import argparse
import csv
import glob
import json
import statistics
import subprocess
import sys
from pathlib import Path


def phase_p50(phase: Path, metric: str):
    with (phase / "observations.csv").open(encoding="utf-8") as f:
        values = [float(r["value"]) for r in csv.DictReader(f) if r["metric"] == metric]
    return (round(statistics.median(values), 3) if values else None), len(values)


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("progress", help="progress JSON серии run_quest_series.py")
    p.add_argument("--out-dir", required=True, help="куда положить runtime/ и summary.json")
    p.add_argument("--metric", default="app_gpu_ms")
    p.add_argument("--budget-ms", type=float, default=7.0)
    args = p.parse_args()
    repo = Path(__file__).resolve().parents[3]
    out_dir = Path(args.out_dir)
    rows = json.loads(Path(args.progress).read_text(encoding="utf-8"))
    runs, per_view, conditions_seen = [], {}, set()
    for row in rows:
        run_id = Path(row["bundle"]).name
        if row.get("verdict") != "GREEN" or not row.get("resultDir"):
            runs.append({"run": run_id, "verdict": row.get("verdict"), "reason": row.get("reason")})
            continue
        out = out_dir / "runtime" / run_id
        if not out.exists():
            source = glob.glob(row["resultDir"] + "/source/roles/client-1/source/processes/*/perf-runtime")
            if len(source) != 1:
                raise SystemExit("perf-runtime прогона %s не найден однозначно: %s" % (run_id, source))
            subprocess.run([sys.executable, "-B", "-X", "utf8", str(repo / "Tools/Perf/gpu_analyze.py"), "import-runtime",
                            source[0], "--out", str(out)], check=True, capture_output=True)
        views = []
        for phase in sorted(d for d in out.iterdir() if d.is_dir() and d.name.startswith("phase-")):
            conditions = json.loads((phase / "run.json").read_text(encoding="utf-8")).get("conditions", {})
            view = conditions.get("viewId")
            if not view:
                continue
            p50, n = phase_p50(phase, args.metric)
            conditions_seen.add(json.dumps({k: conditions.get(k) for k in (
                "deviceModel", "developmentBuild", "sceneId", "refreshHz", "eyeResolution", "ffr", "cpuLevel",
                "gpuLevel", "qualityHash", "puppetCount", "clutterCount")}, sort_keys=True, ensure_ascii=False))
            views.append({"view": view, "phase": phase.name, "p50Ms": p50, "n": n})
            if p50 is not None:
                per_view.setdefault(view, []).append(p50)
        runs.append({"run": run_id, "verdict": "GREEN", "resultDir": row["resultDir"], "views": views})
    table = {view: {"runs": len(v), "p50s": v, "medianP50Ms": round(statistics.median(v), 3),
                    "spreadMs": round(max(v) - min(v), 3)} for view, v in per_view.items()}
    worst = max(table.items(), key=lambda kv: kv[1]["medianP50Ms"]) if table else None
    summary = {"schemaVersion": 1, "kind": "vrb.quest-lobby-view-tour", "progress": str(Path(args.progress)),
               "metric": args.metric, "budgetMs": args.budget_ms, "runs": runs, "views": table,
               "worst": {"view": worst[0], "medianP50Ms": worst[1]["medianP50Ms"],
                         "withinBudget": worst[1]["medianP50Ms"] <= args.budget_ms} if worst else None,
               "conditions": [json.loads(c) for c in sorted(conditions_seen)],
               "limitations": ["AppGPUTime — API observations, не верифицированные исходные GPU-кадры",
                               "Отклонения позы вида (XZ/yaw/пол) сверяются отдельно по отчётам вида"]}
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1) + "\n",
                                          encoding="utf-8", newline="\n")
    for view, t in sorted(table.items(), key=lambda kv: -kv[1]["medianP50Ms"]):
        print("%-14s %6.3f  runs=%d spread=%.3f  %s" % (view, t["medianP50Ms"], t["runs"], t["spreadMs"], t["p50s"]))
    print(json.dumps({"worst": summary["worst"], "conditionSets": len(conditions_seen)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
