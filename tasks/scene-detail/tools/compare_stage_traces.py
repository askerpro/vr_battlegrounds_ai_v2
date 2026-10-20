"""Офлайн-сравнение двух stage-трасс ovrgpuprofiler (-t, detailed mode) с Quest.

Читает raw/capture-NNN-stage.txt из каталогов штатного collector
(Tools/Perf/quest_gpu_breakdown.py --capture-kind stage) и пишет JSON:
стадии каждой поверхности, медианы по основному стерео-проходу и разницу
времени Render по бинам экрана (один и тот же topLeft в A и B).

Ограничения: это instrumented-трасса за ~0,1 с (несколько кадров), не бюджетный
замер; Preempt — время вытеснения GPU другим контекстом (компоновщик), не работа
приложения; стадии разных поверхностей не суммируются в «время кадра».
"""
import argparse
import hashlib
import json
import re
import statistics
from pathlib import Path

SURFACE_RE = re.compile(
    r"^(Surface|Compute)\s+(\d+)\s*\|\s*([^|]*)\|(.*)\|\s*([\d.]+) ms\s*\|\s*(\d+)\s+stages"
)
BINS_RE = re.compile(r"(\d+)\s+(\d+)x(\d+) bins \(\s*(\d+)\s+rendered\)")
BIN_RE = re.compile(r"^\tBin (\S+)\s*(?:\|\s*topLeft\s+(\d+)\s*x(\d+)\s*\|\s*([^|]*)\|\s*Fov\s+(.*))?$")
STAGE_RE = re.compile(r"^\t\tStage\s+(\d+)\s*:\s*(\w+)\s+([\d.]+) ms")
CLOCKS_RE = re.compile(r"^\t\t\tClocks\s*:\s*([\d.]+)")


def parse_trace(path):
    surfaces = []
    current = None
    current_bin = None
    header = {}
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.rstrip("\r")
        m = SURFACE_RE.match(line)
        if m:
            kind, idx, size, desc, total, nstages = m.groups()
            bins = BINS_RE.search(desc)
            current = {
                "index": int(idx),
                "kind": kind.lower(),
                "size": size.strip() or None,
                "description": " ".join(desc.split()),
                "binCount": int(bins.group(1)) if bins else None,
                "renderedBins": int(bins.group(4)) if bins else None,
                "reportedTotalMs": float(total),
                "stageCount": int(nstages),
                "stages": [],
            }
            surfaces.append(current)
            current_bin = None
            continue
        if line.startswith("Process "):
            header["process"] = line
        elif line.startswith("Captured "):
            header["captured"] = line
        elif line.startswith("Tracing "):
            header["tracing"] = line
        if current is None:
            continue
        m = BIN_RE.match(line)
        if m:
            name, x, y, logical, fov = m.groups()
            current_bin = {
                "bin": name,
                "topLeft": [int(x), int(y)] if x is not None else None,
                "fov": fov.strip() if fov else None,
            }
            continue
        m = STAGE_RE.match(line)
        if m:
            current["stages"].append({
                "stage": int(m.group(1)),
                "name": m.group(2),
                "ms": float(m.group(3)),
                "clocks": None,
                "bin": current_bin["bin"] if current_bin else None,
                "topLeft": current_bin["topLeft"] if current_bin else None,
                "fov": current_bin["fov"] if current_bin else None,
            })
            continue
        m = CLOCKS_RE.match(line)
        if m and current["stages"]:
            current["stages"][-1]["clocks"] = float(m.group(1))
    for s in surfaces:
        by_name = {}
        for st in s["stages"]:
            by_name[st["name"]] = round(by_name.get(st["name"], 0.0) + st["ms"], 6)
        s["stageMs"] = by_name
        s["stageSumMs"] = round(sum(by_name.values()), 6)
        s["stageSumExclPreemptMs"] = round(s["stageSumMs"] - by_name.get("Preempt", 0.0), 6)
    return header, surfaces


def surface_class(s):
    if s["kind"] == "compute":
        return "compute"
    return "surface:" + (s["size"] or "?") + ("|msaa4" if "MSAA 4" in s["description"] else "")


def med(values):
    return round(statistics.median(values), 6) if values else None


def summarize(surfaces):
    classes = {}
    for s in surfaces:
        classes.setdefault(surface_class(s), []).append(s)
    out = {}
    for cls, items in classes.items():
        names = sorted({n for s in items for n in s["stageMs"]})
        out[cls] = {
            "executions": len(items),
            "medianReportedTotalMs": med([s["reportedTotalMs"] for s in items]),
            "medianStageSumExclPreemptMs": med([s["stageSumExclPreemptMs"] for s in items]),
            "medianStageMs": {n: med([s["stageMs"].get(n, 0.0) for s in items]) for n in names},
            "renderedBins": sorted({s["renderedBins"] for s in items if s["renderedBins"] is not None}),
        }
    return out


def bin_render(surfaces, cls):
    """Медиана Render по каждому бину основного прохода (ключ — topLeft+fov)."""
    acc = {}
    for s in surfaces:
        if surface_class(s) != cls:
            continue
        per = {}
        for st in s["stages"]:
            if st["name"] != "Render" or st["topLeft"] is None:
                continue
            key = "%dx%d|%s" % (st["topLeft"][0], st["topLeft"][1], st["fov"])
            per[key] = per.get(key, 0.0) + st["ms"]
        for k, v in per.items():
            acc.setdefault(k, []).append(v)
    return {k: {"medianMs": med(v), "executions": len(v)} for k, v in acc.items()}


def implied_clock_mhz(surfaces):
    vals = [st["clocks"] / (st["ms"] * 1000.0) for s in surfaces for st in s["stages"]
            if st["clocks"] and st["ms"] >= 0.1]
    return med(vals)


def load(raw_dir):
    raw_dir = Path(raw_dir)
    trace = sorted((raw_dir / "raw").glob("capture-*-stage.txt"))
    if len(trace) != 1:
        raise SystemExit("ожидалась ровно одна stage-трасса в %s, найдено %d" % (raw_dir, len(trace)))
    data = trace[0].read_bytes()
    header, surfaces = parse_trace(trace[0])
    receipt = json.loads((raw_dir / "capture.json").read_text(encoding="utf-8-sig"))
    return {
        "dir": str(raw_dir),
        "trace": str(trace[0]),
        "traceSha256": hashlib.sha256(data).hexdigest(),
        "header": header,
        "modeOwnership": receipt.get("modeOwnership"),
        "phase": [c.get("phase") for c in receipt.get("captures", [])],
        "impliedClockMHz": implied_clock_mhz(surfaces),
        "summary": summarize(surfaces),
        "surfaces": surfaces,
    }


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("a")
    p.add_argument("b")
    p.add_argument("--out", required=True)
    args = p.parse_args()
    a, b = load(args.a), load(args.b)
    main_cls = max(a["summary"], key=lambda c: a["summary"][c]["medianReportedTotalMs"] or 0)
    delta = {}
    if main_cls in b["summary"]:
        sa, sb = a["summary"][main_cls], b["summary"][main_cls]
        for n in sorted(set(sa["medianStageMs"]) | set(sb["medianStageMs"])):
            va, vb = sa["medianStageMs"].get(n), sb["medianStageMs"].get(n)
            delta[n] = None if va is None or vb is None else round(vb - va, 6)
        delta["stageSumExclPreempt"] = round(sb["medianStageSumExclPreemptMs"] - sa["medianStageSumExclPreemptMs"], 6)
    ba, bb = bin_render(a["surfaces"], main_cls), bin_render(b["surfaces"], main_cls)
    bins = []
    for k in sorted(set(ba) | set(bb)):
        va = ba.get(k, {}).get("medianMs")
        vb = bb.get(k, {}).get("medianMs")
        bins.append({"bin": k, "aMs": va, "bMs": vb,
                     "deltaMs": None if va is None or vb is None else round(vb - va, 6)})
    bins.sort(key=lambda r: abs(r["deltaMs"]) if r["deltaMs"] is not None else -1, reverse=True)
    result = {
        "schemaVersion": 1,
        "kind": "vrb.quest-stage-trace-ab",
        "status": "instrumented-limited",
        "limitations": [
            "Detailed/instrumented trace (~0.1 s, несколько кадров) — не бюджетный замер; normal detailedOFF A/B отдельно",
            "Preempt — вытеснение GPU другим контекстом (компоновщик/timewarp), не работа приложения",
            "Один захват на вариант: разброс между запусками этой трассой не оценён",
            "Стадии разных поверхностей не суммируются в время кадра; Render по бинам не аддитивен к объектам сцены",
        ],
        "mainSurfaceClass": main_cls,
        "mainSurfaceMedianStageDeltaMs": delta,
        "mainSurfaceBinRenderDeltaTop": bins[:15],
        "binCount": len(bins),
        "a": {k: v for k, v in a.items() if k != "surfaces"},
        "b": {k: v for k, v in b.items() if k != "surfaces"},
    }
    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    (out / "comparison.json").write_text(json.dumps(result, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    for tag, d in (("a", a), ("b", b)):
        with (out / ("surfaces-%s.jsonl" % tag)).open("w", encoding="utf-8", newline="\n") as f:
            for s in d["surfaces"]:
                f.write(json.dumps(s, ensure_ascii=False) + "\n")
    print(json.dumps({"out": str(out / "comparison.json"), "mainSurfaceClass": main_cls,
                      "delta": delta, "aClockMHz": a["impliedClockMHz"], "bClockMHz": b["impliedClockMHz"]},
                     ensure_ascii=False))


if __name__ == "__main__":
    main()
