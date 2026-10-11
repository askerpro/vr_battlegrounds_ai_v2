# -*- coding: utf-8 -*-
"""Пролёты восточной стороны навеса: что видно через каждый и какие оставить открытыми.

Запуск: python -X utf8 bays.py layout.json
Выход: reports/implementation/layout-study/bays-<имя>.json
"""
import itertools
import json
import math
import os
import sys

import numpy as np

import layout_model as lm
import search as se


def exit_bay(eyes):
    """int[P,D]: номер пролёта, через который луч покидает пол по восточной кромке (0 — не через навесную сторону,
    -1 — через восточную кромку вне навеса: торцевые полосы z за ±7.48)."""
    az = np.radians(lm.AZ)
    sx, cz = np.sin(az), np.cos(az)
    res = np.zeros((len(eyes), len(az)), int)
    for p, (ex, ez) in enumerate(eyes):
        with np.errstate(divide="ignore", invalid="ignore"):
            t = (lm.ARENA[2] - ex) / sx
        zh = ez + t * cz
        ok = (sx > 1e-9) & (zh >= lm.ARENA[1]) & (zh <= lm.ARENA[3])
        res[p, ok] = -1
        for i, z0, z1 in lm.east_bays():
            res[p, ok & (zh >= z0) & (zh < z1)] = i
    return res


def main():
    src = sys.argv[1]
    zones = lm.build_zones(include_transport=True)
    with open(src, encoding="utf-8") as f:
        pl = {n: tuple(v) for n, v in json.load(f)["placement"].items()}
    eyes = lm.eye_grid()
    bay = exit_bay(eyes)
    m_es = lm.sector_mask(45, 225)
    out = {"layout": os.path.basename(src), "bays": [], "combos": []}
    base = {m: lm.coverage(se.boxes_for(pl, zones, m), eyes) for m in ("aabb", "refined")}
    for i, z0, z1 in lm.east_bays():
        sel = bay == i
        a_from_c = (round(lm.azimuth(lm.ARENA[2], z1)), round(lm.azimuth(lm.ARENA[2], z0)))
        hist = np.bincount(np.nonzero(sel)[1], minlength=360)
        core = [int(a) for a in np.nonzero(hist >= np.percentile(hist[hist > 0], 50))[0]]
        out["bays"].append(dict(bay=i, z=[round(z0, 2), round(z1, 2)], az_from_center=a_from_c,
                                az_core=[min(core), max(core)] if core else None,
                                rays=int(sel.sum()),
                                covered_aabb=round(float(base["aabb"][sel].mean()) * 100, 1),
                                covered_refined=round(float(base["refined"][sel].mean()) * 100, 1)))
    sel = bay == -1
    out["end_strips_east_edge"] = dict(rays=int(sel.sum()), covered_aabb=round(float(base["aabb"][sel].mean()) * 100, 1),
                                       covered_refined=round(float(base["refined"][sel].mean()) * 100, 1))
    for k in range(0, lm.BAY_COUNT + 1):
        for opened in itertools.combinations(range(1, lm.BAY_COUNT + 1), k):
            row = dict(open=list(opened))
            for m in ("aabb", "refined"):
                bx = se.boxes_for(pl, zones, m) + lm.bay_walls(set(opened))
                cov = lm.coverage(bx, eyes)
                st = lm.sector_stats(cov)
                row[m] = {s: round(st[s]["mean"] * 100, 1) for s in ("NE", "E", "SE", "Восток 45–135", "Юг 135–225", "Восток+юг 45–225")}
                # сквозной вид на базу через открытые пролёты: доля таких лучей, упёршихся в зону
                through = np.isin(bay, list(opened))
                row[m]["через открытые: на зону %"] = round(float(base[m][through].mean()) * 100, 1) if through.any() else None
            out["combos"].append(row)
    with open(os.path.join(lm.OUT, f"bays-{os.path.splitext(os.path.basename(src))[0]}.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for b in out["bays"]:
        print(b)
    print("торцевые полосы:", out["end_strips_east_edge"])
    for r in out["combos"]:
        if len(r["open"]) in (0, 2, 4):
            print(r["open"], "aabb", r["aabb"]["Восток+юг 45–225"], r["aabb"]["Восток 45–135"], r["aabb"]["через открытые: на зону %"],
                  "| refined", r["refined"]["Восток+юг 45–225"], r["refined"]["Восток 45–135"], r["refined"]["через открытые: на зону %"])


if __name__ == "__main__":
    main()
