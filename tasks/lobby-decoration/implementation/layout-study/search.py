# -*- coding: utf-8 -*-
"""Поиск расстановки зон: отжиг по (x, z, yaw кратно 90) с жёсткими ограничениями.

Запуск: python -X utf8 search.py [--model refined|aabb] [--restarts N] [--iters N] [--seed S]
        [--dmin 15] [--dmax 40] [--tag name]
Результат: reports/implementation/layout-study/search-<tag>.json (топ вариантов).
"""
import argparse
import json
import math
import os
import random

import numpy as np

import layout_model as lm
import site_plan as st

ARC = (20, 225)          # СВ (с 20°) .. ЮЗ (225°); север — стрельбище, запад закрыт стеной
GAP_ARC = (40, 225)
CENTER = (0.0, 0.0)


def fp_center(fp):
    return ((fp[0] + fp[2]) / 2, (fp[1] + fp[3]) / 2)


def in_west_block(fp):
    # за западной стеной и вдоль стрельбища — ограда/скалы, не база
    return fp[0] < -10.0 and fp[3] > -14.0


def violations(pl, zones, args):
    """Сумма нарушений (0 = допустимо) и подробности."""
    v, det = 0.0, []
    fps = {n: zones[n].footprint(*pl[n]) for n in pl}
    ko = (lm.RANGE_KEEPOUT[0] - 2, lm.RANGE_KEEPOUT[1] - 2, lm.RANGE_KEEPOUT[2] + 2, lm.RANGE_KEEPOUT[3] + 2)
    for n, fp in fps.items():
        cx, cz = fp_center(fp)
        dc = lm.rect_point_dist(lm.ARENA, cx, cz)
        if dc < args.dmin:
            v += args.dmin - dc; det.append(f"{n}: центр ближе {args.dmin} м ({dc:.1f})")
        if dc > args.dmax:
            v += dc - args.dmax; det.append(f"{n}: центр дальше {args.dmax} м")
        de = lm.rect_gap(fp, lm.ARENA)
        if de < args.edge_min:
            v += args.edge_min - de; det.append(f"{n}: кромка ближе {args.edge_min} м к арене")
        g = lm.rect_gap(fp, ko)
        if g < 0 or (g == 0):
            v += 1 + abs(g); det.append(f"{n}: в коридоре стрельбища")
        if in_west_block(fp):
            v += 1 + max(0, -10 - fp[0]); det.append(f"{n}: за западной стеной")
    names = list(fps)
    for i in range(len(names)):
        for j in range(i + 1, len(names)):
            g = lm.rect_gap(fps[names[i]], fps[names[j]])
            if g < args.gap:
                v += args.gap - g; det.append(f"{names[i]}~{names[j]}: зазор {g:.1f}")
    # входы: перед входом 3 м свободно; нормаль к арене (кроме ворот КПП — наружу)
    for n in pl:
        for label, (ex, ez), (nx, nz), role in zones[n].entrances_world(*pl[n]):
            to_c = np.array([CENTER[0] - ex, CENTER[1] - ez])
            to_c /= np.linalg.norm(to_c) + 1e-9
            d = nx * to_c[0] + nz * to_c[1]
            if role == "gate":
                if d > -0.7:
                    v += (d + 0.7) * 5; det.append(f"{n}: ворота не наружу ({d:.2f})")
            else:
                if d < args.face_min:
                    w = 3.0 if "допущение" not in label else 1.0
                    v += (args.face_min - d) * w; det.append(f"{n}: вход «{label}» отвёрнут ({d:.2f})")
            # зона перед входом
            px, pz = ex + nx * 1.5, ez + nz * 1.5
            front = (px - 1.5 if nx == 0 else min(ex, ex + nx * 3), pz - 1.5 if nz == 0 else min(ez, ez + nz * 3),
                     px + 1.5 if nx == 0 else max(ex, ex + nx * 3), pz + 1.5 if nz == 0 else max(ez, ez + nz * 3))
            for m in pl:
                if m != n and lm.rect_gap(front, fps[m]) < 0.5:
                    v += 1; det.append(f"{n}: вход закрыт {m}")
    # КПП: ворота должны быть на внешнем контуре — по направлению наружу за ним нет зон
    cp = zones["CheckpointZone"].entrances_world(*pl["CheckpointZone"])
    (gx, gz), (gnx, gnz) = cp[0][1], cp[0][2]
    for m in pl:
        if m == "CheckpointZone":
            continue
        fp = fps[m]
        # любая точка зоны дальше ворот по нормали и в пределах полосы ±12 м
        corners = [(fp[0], fp[1]), (fp[2], fp[1]), (fp[0], fp[3]), (fp[2], fp[3])]
        ahead = max((cx - gx) * gnx + (cz - gz) * gnz for cx, cz in corners)
        lateral = min(abs(-(cx - gx) * gnz + (cz - gz) * gnx) for cx, cz in corners)
        if ahead > -1 and lateral < 12:
            v += 2; det.append(f"КПП: за воротами {m}")
    # ворота КПП лежат на ограде (выпуклый контур зон + стрельбище + западная линия)
    _, _, gd = st.fence(pl, zones)
    if gd > 0.5:
        v += gd; det.append(f"КПП: ворота в {gd:.1f} м от ограды")
    return v, det


def boxes_for(pl, zones, model):
    bx = list(lm.fixed_boxes(model))
    for n, p in pl.items():
        bx += zones[n].boxes(*p, model)
    return bx


def score(pl, zones, args, eyes, center_idx, detail=False):
    v, det = violations(pl, zones, args)
    m = lm.sector_mask(*ARC)
    if args.model == "combo":
        cov_a = lm.coverage(boxes_for(pl, zones, "aabb"), eyes)
        cov = lm.coverage(boxes_for(pl, zones, "refined"), eyes)
        cmean = args.w_aabb * cov_a[:, m].mean() + (1 - args.w_aabb) * cov[:, m].mean()
        cov = cov | cov_a if args.gaps_from == "aabb" else cov
    else:
        cov = lm.coverage(boxes_for(pl, zones, args.model), eyes)
        cmean = cov[:, m].mean()
    runs = lm.runs_uncovered(cov[center_idx], *GAP_ARC)
    # форма просветов из центра: длинные дыры штрафуются, 2–3 просвета по 5–12° поощряются
    long_pen = sum(max(0, (b - a) - 12) for a, b in runs) / 180.0
    good = sum(1 for a, b in runs if 5 <= b - a <= 12)
    gap_bonus = 0.01 * min(good, 3) - 0.01 * max(0, good - 3)
    s = cmean - args.wlong * long_pen + args.wgap * gap_bonus - args.wviol * v
    if detail:
        return s, dict(viol=v, det=det, cov=cmean, runs=runs, long_pen=long_pen, good=good)
    return s


def random_state(zones, rng, args):
    pl = {}
    for n in zones:
        while True:
            ang = rng.uniform(ARC[0] - 10, ARC[1] + 10)
            r = rng.uniform(15, 40)
            x, z = r * math.sin(math.radians(ang)), r * math.cos(math.radians(ang))
            k = rng.choice([0, 90, 180, 270])
            pl[n] = (x, z, k)
            break
    return pl


def anneal(zones, args, rng, eyes, cidx):
    if args.init:
        with open(args.init, encoding="utf-8") as f:
            pl = {n: tuple(v) for n, v in json.load(f)["placement"].items()}
    else:
        pl = random_state(zones, rng, args)
    cur = score(pl, zones, args, eyes, cidx)
    best, bestpl = cur, dict(pl)
    T0 = args.t0
    for it in range(args.iters):
        T = T0 * (1 - it / args.iters) + 1e-4
        new = dict(pl)
        r = rng.random()
        n = rng.choice(list(zones))
        x, z, k = new[n]
        if r < 0.65:
            sig = args.sigma * (1 - it / args.iters) + 0.5
            new[n] = (x + rng.gauss(0, sig), z + rng.gauss(0, sig), k)
        elif r < 0.85:
            new[n] = (x, z, rng.choice([0, 90, 180, 270]))
        else:
            m = rng.choice(list(zones))
            new[n], new[m] = (new[m][0], new[m][1], k), (x, z, new[m][2])
        s = score(new, zones, args, eyes, cidx)
        if s > cur or rng.random() < math.exp((s - cur) / T):
            pl, cur = new, s
            if s > best:
                best, bestpl = s, dict(new)
    return best, bestpl


def snap(pl, step=0.5):
    return {n: (round(x / step) * step, round(z / step) * step, k) for n, (x, z, k) in pl.items()}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="refined")
    ap.add_argument("--restarts", type=int, default=12)
    ap.add_argument("--iters", type=int, default=2500)
    ap.add_argument("--seed", type=int, default=1)
    ap.add_argument("--dmin", type=float, default=15.0)
    ap.add_argument("--dmax", type=float, default=40.0)
    ap.add_argument("--edge_min", type=float, default=6.0)
    ap.add_argument("--gap", type=float, default=2.0)
    ap.add_argument("--face_min", type=float, default=0.0)
    ap.add_argument("--wlong", type=float, default=0.3)
    ap.add_argument("--wgap", type=float, default=1.0)
    ap.add_argument("--tag", default="refined")
    ap.add_argument("--wviol", type=float, default=0.5)
    ap.add_argument("--gaps_from", default="aabb")
    ap.add_argument("--transport", action="store_true")
    ap.add_argument("--init", default="")
    ap.add_argument("--t0", type=float, default=0.05)
    ap.add_argument("--sigma", type=float, default=4.0)
    ap.add_argument("--w_aabb", type=float, default=0.5)
    args = ap.parse_args()
    zones = lm.build_zones(include_transport=args.transport)
    rng = random.Random(args.seed)
    eyes = lm.eye_probe()
    cidx = 4
    results = []
    for r in range(args.restarts):
        s, pl = anneal(zones, args, rng, eyes, cidx)
        pl = snap(pl)
        s2, d = score(pl, zones, args, eyes, cidx, detail=True)
        results.append(dict(score=s2, placement={n: list(v) for n, v in pl.items()}, **{k: d[k] for k in ("viol", "cov", "runs", "good")}, det=d["det"]))
        print(f"restart {r}: score {s2:.3f} cov {d['cov']*100:.1f}% viol {d['viol']:.2f} runs {d['runs']}", flush=True)
        results.sort(key=lambda r: -r["score"])
        with open(os.path.join(lm.OUT, f"search-{args.tag}.json"), "w", encoding="utf-8") as f:
            json.dump(dict(args=vars(args), results=results), f, ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
