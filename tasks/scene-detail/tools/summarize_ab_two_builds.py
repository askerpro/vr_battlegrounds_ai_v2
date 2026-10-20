"""Сводка normal A/B двух сборок (run_ab_two_builds.py): AppGPUTime p50 измеряемой фазы каждого прогона.

Сравнение пар через `gpu_analyze compare` требует одной ревизии сборки, поэтому здесь — import-runtime каждого прогона
и медиана метрики app_gpu_ms по observations.csv измеряемой фазы (вид задан). Метки берутся из имён bundle (NN-<метка><n>).
"""
import argparse
import csv
import glob
import json
import statistics
import subprocess
import sys
from pathlib import Path


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("series_dir")
    p.add_argument("--metric", default="app_gpu_ms")
    args = p.parse_args()
    series = Path(args.series_dir)
    repo = Path(__file__).resolve().parents[3]
    rows = json.loads((series / "progress.json").read_text(encoding="utf-8"))
    runs = []
    for row in rows:
        run_id = Path(row["bundle"]).name.split("-", 1)[1]
        if row.get("verdict") != "GREEN" or not row.get("resultDir"):
            runs.append({"run": run_id, "verdict": row.get("verdict")})
            continue
        out = series / "runtime" / run_id
        if not out.exists():
            source = glob.glob(row["resultDir"] + "/source/roles/client-1/source/processes/*/perf-runtime")
            subprocess.run([sys.executable, "-B", "-X", "utf8", str(repo / "Tools/Perf/gpu_analyze.py"), "import-runtime",
                            source[0], "--out", str(out)], check=True, capture_output=True)
        phases = [d for d in sorted(out.iterdir()) if d.is_dir() and d.name.startswith("phase-")]
        measured = [d for d in phases if json.loads((d / "run.json").read_text(encoding="utf-8"))
                    .get("conditions", {}).get("viewId")]
        phase = measured[-1] if measured else phases[-1]
        with (phase / "observations.csv").open(encoding="utf-8") as f:
            values = [float(r["value"]) for r in csv.DictReader(f) if r["metric"] == args.metric]
        conditions = json.loads((phase / "run.json").read_text(encoding="utf-8")).get("conditions", {})
        runs.append({"run": run_id, "label": run_id.rstrip("0123456789"), "verdict": "GREEN", "n": len(values),
                     "p50Ms": round(statistics.median(values), 3) if values else None,
                     "sourceSha": (conditions.get("sourceSha") or "")[:12], "phase": str(phase)})
    labels = sorted({r["label"] for r in runs if r.get("p50Ms") is not None})
    groups = {label: [r["p50Ms"] for r in runs if r.get("label") == label and r.get("p50Ms") is not None] for label in labels}
    summary = {"schemaVersion": 1, "kind": "vrb.quest-ab-two-builds", "series": str(series), "metric": args.metric,
               "runs": runs,
               "groups": {k: {"n": len(v), "medianOfP50Ms": round(statistics.median(v), 3), "min": min(v), "max": max(v)}
                          for k, v in groups.items()},
               "limitations": ["AppGPUTime — API observations, не верифицированные исходные GPU-кадры",
                               "Сборки разные: разница включает все изменения между ревизиями",
                               "Блоки A-B-A (не чередование пар); дрейф оценивается по A до и после B"]}
    (series / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    for r in runs:
        print(r.get("run"), r.get("verdict"), r.get("p50Ms"), r.get("n"), r.get("sourceSha"))
    print(json.dumps(summary["groups"], ensure_ascii=False))


if __name__ == "__main__":
    main()
