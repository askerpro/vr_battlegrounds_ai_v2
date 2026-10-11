# -*- coding: utf-8 -*-
"""Поиск одноэтажных построек-постов в LV_Showcase: кластеры модулей здания
(Building_*/Cornice/Border/Fence/Stairs/Door) + соседние ящики/мешки/столбы.
Вход: reports/implementation/comm-tower/showcase-instances.json (parse_showcase.py)
Выход: reports/implementation/comm-tower/checkpoint-post/building-clusters.json
"""
import json, math, os
from collections import Counter

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../../reports/implementation/comm-tower"))
OUT = os.path.join(REP, "checkpoint-post")
inst = json.load(open(os.path.join(REP, "showcase-instances.json"), encoding="utf-8"))["instances"]

BLD = ("SM_Building_", "SM_Cornice_", "SM_Border_", "SM_Fence_", "SM_Stairs_", "SM_Door_single", "SM_Door_lamp")
parts = [o for o in inst if o["prefab"].startswith(BLD) and not o["prefab"].startswith(("SM_Fence_long", "SM_Fence_short"))]
R = 2.3
# сетка для single-link
cell = {}
for i, o in enumerate(parts):
    k = (int(math.floor(o["pos"][0] / R)), int(math.floor(o["pos"][2] / R)))
    cell.setdefault(k, []).append(i)
par = list(range(len(parts)))
def f(i):
    while par[i] != i:
        par[i] = par[par[i]]; i = par[i]
    return i
for i, o in enumerate(parts):
    kx, kz = int(math.floor(o["pos"][0] / R)), int(math.floor(o["pos"][2] / R))
    for dx in (-1, 0, 1):
        for dz in (-1, 0, 1):
            for j in cell.get((kx + dx, kz + dz), []):
                if j <= i: continue
                p = parts[j]["pos"]
                if math.hypot(o["pos"][0] - p[0], o["pos"][2] - p[2]) < R and abs(o["pos"][1] - p[1]) < 5:
                    par[f(i)] = f(j)
groups = {}
for i in range(len(parts)):
    groups.setdefault(f(i), []).append(parts[i])

EXTRA = ("SM_SandBarrier", "SM_AmmoCartridgeBoxes", "SM_MilitaryCrate", "SM_Palete", "SM_AmmoCase", "SM_Hangar_Pillar",
         "SM_Wall_Pillar", "SM_Concrete_support", "SM_Concrete_pile", "SM_pipe", "SM_Door_", "SM_Lamp", "SM_AlarmLight",
         "SM_Sign", "SM_Vent", "SM_TarpCrate", "SM_WoodCrate", "SM_StrappedContainer", "SM_Antenna", "SM_SatelliteDish",
         "SM_Generator", "SM_Barrel", "SM_FuelCanister", "SM_Concrete_beam", "SM_Floor_", "SM_Wall_")
res = []
for g in groups.values():
    c = Counter(p["prefab"] for p in g)
    walls = [p for p in g if p["prefab"].startswith("SM_Building_wall")]
    if not walls:
        continue
    xs = [p["pos"][0] for p in g]; zs = [p["pos"][2] for p in g]; ys = [p["pos"][1] for p in walls]
    x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
    near = [o for o in inst if o["prefab"].startswith(EXTRA) and o not in g
            and x0 - 3 <= o["pos"][0] <= x1 + 3 and z0 - 3 <= o["pos"][2] <= z1 + 3 and min(ys) - 1 <= o["pos"][1] <= max(ys) + 8]
    res.append({"bbox": [round(x0, 2), round(z0, 2), round(x1, 2), round(z1, 2)], "size": [round(x1 - x0, 2), round(z1 - z0, 2)],
                "wallY": [round(min(ys), 2), round(max(ys), 2)], "nParts": len(g), "composition": dict(sorted(c.items())),
                "near": dict(sorted(Counter(o["prefab"] for o in near).items())),
                "parts": g, "nearParts": near})
res.sort(key=lambda r: r["nParts"])
json.dump(res, open(os.path.join(OUT, "building-clusters.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for k, r in enumerate(res):
    st = r["composition"].get("SM_Stairs_2m_A", 0)
    crates = sum(v for kk, v in r["near"].items() if kk.startswith(("SM_AmmoCartridge", "SM_MilitaryCrate")))
    sand = sum(v for kk, v in r["near"].items() if kk.startswith("SM_SandBarrier"))
    print(k, r["bbox"], r["size"], "wallY", r["wallY"], "n", r["nParts"], "stairs", st, "crates", crates, "sand", sand)
