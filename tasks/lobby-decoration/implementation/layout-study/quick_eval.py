# -*- coding: utf-8 -*-
"""Быстрая оценка одной расстановки: нарушения, покрытие (обе модели, 3°), просветы из центра.
Запуск: python -X utf8 quick_eval.py layout.json"""
import json
import sys
from types import SimpleNamespace

import numpy as np

import layout_model as lm
import search as se


def main():
    zones = lm.build_zones(include_transport=True)
    with open(sys.argv[1], encoding="utf-8") as f:
        pl = {n: tuple(v) for n, v in json.load(f)["placement"].items()}
    args = SimpleNamespace(dmin=15.0, dmax=40.0, edge_min=6.0, gap=2.0, face_min=0.0)
    v, det = se.violations(pl, zones, args)
    print("нарушения", round(v, 2), det)
    eyes = lm.eye_grid()
    for model in ("aabb", "refined"):
        cov = lm.coverage(se.boxes_for(pl, zones, model), eyes)
        stt = lm.sector_stats(cov)
        c0 = lm.coverage(se.boxes_for(pl, zones, model), np.array([[0.0, 0.0]]))[0]
        print(model, {k: round(x["mean"] * 100, 1) for k, x in stt.items() if k in ("NE", "E", "SE", "S", "SW", "Восток 45–135", "Юг 135–225", "Восток+юг 45–225")},
              "просветы из центра", lm.runs_uncovered(c0, 20, 226))
    for n, p in pl.items():
        fp = zones[n].footprint(*p)
        print(f"  {n:18s} fp x[{fp[0]:.1f};{fp[2]:.1f}] z[{fp[1]:.1f};{fp[3]:.1f}] az {lm.azimuth(fp[0], fp[1]):.0f}/{lm.azimuth(fp[2], fp[1]):.0f}/{lm.azimuth(fp[0], fp[3]):.0f}/{lm.azimuth(fp[2], fp[3]):.0f}")


if __name__ == "__main__":
    main()
