# -*- coding: utf-8 -*-
"""Покрытие горизонта для текущей расстановки зон (обе модели геометрии)."""
import json
import os

import numpy as np

import layout_model as lm


def evaluate(placement, zones, model, eyes, alphas=lm.ALPHAS_DEG):
    boxes = list(lm.fixed_boxes(model))
    for n, (px, pz, dyaw) in placement.items():
        boxes += zones[n].boxes(px, pz, dyaw, model)
    return lm.coverage(boxes, eyes, alphas)


def current_placement(zones):
    return {n: (z.pivot[0], z.pivot[1], 0) for n, z in zones.items()}


def main():
    zones = lm.build_zones()
    eyes = lm.eye_grid()
    pl = current_placement(zones)
    out = {"eyes": len(eyes), "alphas_deg": lm.ALPHAS_DEG.tolist(), "models": {}}
    for model in ("aabb", "refined"):
        for label, alphas in (("3deg", lm.ALPHAS_DEG), ("1.5deg", np.arange(0, 1.51, 0.5))):
            cov = evaluate(pl, zones, model, eyes, alphas)
            st = lm.sector_stats(cov)
            out["models"][f"{model}_{label}"] = st
            c = cov[np.argmin(np.hypot(eyes[:, 0], eyes[:, 1]))]
            print(model, label, {k: round(v["mean"] * 100, 1) for k, v in st.items()})
    with open(os.path.join(lm.OUT, "coverage-current.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)
    for n, z in zones.items():
        fp = z.footprint(*pl[n])
        print(n, [round(v, 1) for v in fp], "dist arena", round(lm.rect_gap(fp, lm.ARENA), 1),
              "h", round(z.local_rect.y1, 2), [(p.name, round(p.y1, 2)) for p in z.parts["refined"]][:4])


if __name__ == "__main__":
    main()
