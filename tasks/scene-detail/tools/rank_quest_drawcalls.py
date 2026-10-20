"""Рейтинг draw-вызовов Quest по PIL-счётчикам из ответов adb-session (meta_renderdoc_quest.py finish).

Читает tool-logs/*adb-session.replies.jsonl и пишет ranking.json: доли Clocks, накопительная доля,
треугольники/инстансы из `draws`, признаки узкого места. Clocks — такты replay захваченного кадра на
шлеме (instrumented, не бюджетное время); доли между draw сопоставимы, миллисекунды — оценка.
"""
import argparse
import glob
import json
from pathlib import Path


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("run_dir", help="папка --out адаптера")
    p.add_argument("--clock-mhz", type=float, default=None, help="для оценки мс (по stage-трассе ~545 МГц)")
    p.add_argument("--top", type=int, default=40)
    args = p.parse_args()
    run = Path(args.run_dir)
    replies_file = sorted(glob.glob(str(run / "tool-logs" / "*adb-session.replies.jsonl")))[-1]
    replies = {}
    for line in open(replies_file, encoding="utf-8"):
        row = json.loads(line)
        name = row["request"] if isinstance(row["request"], str) else row["request"].get("cmd")
        replies[name] = row["reply"]
    draws = {d["eid"]: d for d in replies.get("draws", {}).get("draws", [])}
    states = replies["fetch_drawcall_states"]
    rows = []
    for dc in states["drawcalls"]:
        m = dc.get("metrics", {})
        d = draws.get(dc["eid"], {})
        rows.append({"eid": dc["eid"], "pass": dc.get("pass"), "marker": d.get("marker"),
                     "clocks": m.get("Clocks"), "shadersBusyPct": m.get("% Shaders Busy"),
                     "textureFetchStallPct": m.get("% Texture Fetch Stall"),
                     "vertexFetchStallPct": m.get("% Vertex Fetch Stall"), "lrzBusyPct": m.get("% LRZ Busy"),
                     "triangles": d.get("triangles"), "instances": d.get("instances"),
                     "lrz": dc.get("states", {}).get("hardware_lrz_state")})
    total = sum(r["clocks"] or 0 for r in rows)
    rows.sort(key=lambda r: -(r["clocks"] or 0))
    acc = 0.0
    for i, r in enumerate(rows, 1):
        acc += r["clocks"] or 0
        r["rank"] = i
        r["sharePct"] = round(100.0 * (r["clocks"] or 0) / total, 3) if total else None
        r["cumulativePct"] = round(100.0 * acc / total, 2) if total else None
        if args.clock_mhz:
            r["estMs"] = round((r["clocks"] or 0) / (args.clock_mhz * 1000.0), 4)
    tri_total = sum(r["triangles"] or 0 for r in rows)
    out = {"schemaVersion": 1, "kind": "vrb.quest-drawcall-ranking", "status": "instrumented-replay",
           "source": replies_file, "health": states.get("health"),
           "countersReceived": [c.get("name") for c in states.get("counters", [])],
           "info": replies.get("info"), "passes": replies.get("passes", {}).get("passes"),
           "drawcalls": len(rows), "totalClocks": total, "totalTriangles": tri_total,
           "estTotalMs": round(total / (args.clock_mhz * 1000.0), 3) if args.clock_mhz else None,
           "drawsFor50Pct": next((r["rank"] for r in rows if r["cumulativePct"] >= 50), None),
           "drawsFor80Pct": next((r["rank"] for r in rows if r["cumulativePct"] >= 80), None),
           "limitations": ["Clocks — replay захваченного кадра в drawcall-profiling режиме, не бюджетный замер",
                           "Draw ещё не связаны с объектами Unity: нужны pipeline/bindings по eid",
                           "Счётчики PS Invocations и LRZ Prim Killed не сопоставлены на этом устройстве"],
           "top": rows[:args.top], "all": rows}
    (run / "ranking.json").write_text(json.dumps(out, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({k: out[k] for k in ("drawcalls", "totalClocks", "totalTriangles", "estTotalMs",
                                          "drawsFor50Pct", "drawsFor80Pct", "countersReceived")}, ensure_ascii=False))
    for r in rows[:25]:
        print("%2d eid=%-5s %5.1f%% cum=%5.1f%% clk=%9.0f tri=%7s inst=%-3s busy=%5.1f tex=%5.1f vtx=%5.2f lrz=%5.1f %s" % (
            r["rank"], r["eid"], r["sharePct"], r["cumulativePct"], r["clocks"], r["triangles"], r["instances"],
            r["shadersBusyPct"] or 0, r["textureFetchStallPct"] or 0, r["vertexFetchStallPct"] or 0,
            r["lrzBusyPct"] or 0, r["pass"]))


if __name__ == "__main__":
    main()
