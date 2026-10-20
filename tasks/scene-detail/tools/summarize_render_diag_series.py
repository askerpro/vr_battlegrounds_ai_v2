"""Сводка normal A/B серии render_diag на Quest: import-runtime каждого прогона и compare пар.

Пары задаются как «контроль none → вариант» по id из имён bundle (NN-<id>). Время — AppGPUTime p50 API-наблюдений
(normal, detailed OFF): разница пар сравнивается с A/A; источник GPU-кадров не верифицирован (см. limitations).
"""
import argparse
import glob
import json
import subprocess
import sys
from pathlib import Path

PAIRS = [("aa1", "aa2"), ("n3", "fb1"), ("n4", "fb2"), ("n5", "fu1"), ("n6", "fu2"), ("n7", "sk1"), ("n8", "sk2")]


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("series_dir")
    p.add_argument("--pairs", default=None, help="контроль:вариант через запятую, например aa1:aa2,n3:fb1")
    args = p.parse_args()
    pairs = [tuple(x.split(":")) for x in args.pairs.split(",")] if args.pairs else PAIRS
    series = Path(args.series_dir)
    repo = Path(__file__).resolve().parents[3]
    analyze = repo / "Tools/Perf/gpu_analyze.py"
    progress = json.loads((series / "progress.json").read_text(encoding="utf-8"))
    phases = {}
    for row in progress:
        if row.get("verdict") != "GREEN" or not row.get("resultDir"):
            continue
        run_id = Path(row["bundle"]).name.split("-", 1)[1]
        runtime = glob.glob(row["resultDir"] + "/source/roles/client-1/source/processes/*/perf-runtime")
        out = series / "runtime" / run_id
        if not out.exists():
            subprocess.run([sys.executable, "-B", "-X", "utf8", str(analyze), "import-runtime", runtime[0], "--out", str(out)],
                           check=True, capture_output=True)
        dirs = sorted(d for d in out.iterdir() if d.is_dir() and d.name.startswith("phase-"))
        measured = [d for d in dirs if json.loads((d / "run.json").read_text(encoding="utf-8"))
                    .get("conditions", {}).get("viewId")]
        phases[run_id] = measured[-1] if measured else dirs[-1]
    rows = []
    for a, b in pairs:
        if a not in phases or b not in phases:
            rows.append({"a": a, "b": b, "status": "missing"})
            continue
        out = series / "compare" / (a + "-vs-" + b)
        if not (out / "comparison.json").exists():
            subprocess.run([sys.executable, "-B", "-X", "utf8", str(analyze), "compare", str(phases[a]), str(phases[b]),
                            "--allow-condition", "renderDiagnostic", "--out", str(out)], check=True, capture_output=True)
        comp = json.loads((out / "comparison.json").read_text(encoding="utf-8"))
        gpu = comp.get("observationMetrics", {}).get("app_gpu_ms", {})
        cond_b = json.loads((phases[b] / "run.json").read_text(encoding="utf-8")).get("conditions", {})
        rows.append({"a": a, "b": b, "variantB": cond_b.get("renderDiagnostic"), "status": comp.get("status"),
                     "aP50Ms": gpu.get("a", {}).get("p50"), "bP50Ms": gpu.get("b", {}).get("p50"),
                     "deltaP50Ms": gpu.get("deltaP50"), "aP95Ms": gpu.get("a", {}).get("p95"),
                     "bP95Ms": gpu.get("b", {}).get("p95"), "comparison": str(out / "comparison.json")})
    summary = {"schemaVersion": 1, "kind": "vrb.quest-render-diag-series", "series": str(series), "rows": rows,
               "limitations": ["AppGPUTime — API observations, не верифицированные исходные GPU-кадры",
                               "По 2 пары на вариант с чередованием порядка; шум оценивается по A/A",
                               "skybox-color меняет и отражение зеркала (камера отражения копирует clear камеры)",
                               "floor-unlit — раскладка текстуры по UV меша, визуально не равна исходному полу"]}
    (series / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    for r in rows:
        if r.get("status") == "missing":
            print("%-4s vs %-4s missing" % (r["a"], r["b"]))
        else:
            print("%-4s vs %-4s %-26s A=%.3f B=%.3f Δ=%+.3f ms (%s)" % (
                r["a"], r["b"], r["variantB"], r["aP50Ms"] or 0, r["bP50Ms"] or 0, r["deltaP50Ms"] or 0, r["status"]))


if __name__ == "__main__":
    main()
