# -*- coding: utf-8 -*-
"""Кластеризация экземпляров SM_Antenna_* / SM_SatelliteDish_* по XZ в вышки.
Вход: reports/implementation/comm-tower/showcase-instances.json
Выход: reports/implementation/comm-tower/tower-clusters.json + печать сводки.
"""
import json, math, os, sys
from collections import Counter

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../reports/implementation/comm-tower"))
data = json.load(open(os.path.join(REP, "showcase-instances.json"), encoding="utf-8"))
inst = data["instances"]

KEYS = ("SM_Antenna_", "SM_SatelliteDish_")
parts = [o for o in inst if o["prefab"].startswith(KEYS)]
R = float(sys.argv[1]) if len(sys.argv) > 1 else 4.0

# single-link кластеризация по XZ
n = len(parts); par = list(range(n))
def f(i):
    while par[i] != i:
        par[i] = par[par[i]]; i = par[i]
    return i
for i in range(n):
    for j in range(i + 1, n):
        a, b = parts[i]["pos"], parts[j]["pos"]
        if math.hypot(a[0] - b[0], a[2] - b[2]) < R:
            par[f(i)] = f(j)
groups = {}
for i in range(n):
    groups.setdefault(f(i), []).append(parts[i])

clusters = []
for g in groups.values():
    xs = [p["pos"][0] for p in g]; ys = [p["pos"][1] for p in g]; zs = [p["pos"][2] for p in g]
    c = Counter(p["prefab"] for p in g)
    # другие объекты рядом (лампы, лестницы, кабели)
    cx, cz = sum(xs) / len(xs), sum(zs) / len(zs)
    near = Counter(o["prefab"] for o in inst if not o["prefab"].startswith(KEYS)
                   and math.hypot(o["pos"][0] - cx, o["pos"][2] - cz) < 4.0 and min(ys) - 1 <= o["pos"][1] <= max(ys) + 6)
    clusters.append({"center": [round(cx, 2), round(min(ys), 2), round(cz, 2)], "ymin": min(ys), "ymax": max(ys),
                     "count": len(g), "parents": sorted(set(p["parentPath"] for p in g)),
                     "composition": dict(sorted(c.items())), "nearby": dict(near.most_common(12)), "parts": g})
clusters.sort(key=lambda c: -(c["ymax"] - c["ymin"]))
json.dump(clusters, open(os.path.join(REP, "tower-clusters.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for k, c in enumerate(clusters):
    print(k, c["center"], "h=%.2f" % (c["ymax"] - c["ymin"]), c["count"], c["composition"], "| near:", c["nearby"], c["parents"][:2])
