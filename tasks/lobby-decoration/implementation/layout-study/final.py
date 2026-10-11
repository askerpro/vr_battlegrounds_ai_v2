# -*- coding: utf-8 -*-
"""Полная оценка: текущая vs предложенная расстановка, ограда, дорожки, деревья, картинки.

Запуск: python -X utf8 final.py [proposed-layout.json]
Вход: proposed-layout.json рядом со скриптом — {"placement": {зона: [pivotX, pivotZ, deltaYaw]}}.
Выход: reports/implementation/layout-study/{layout-result.json, topview.png, polar.png}.
"""
import json
import math
import os
import sys

import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle, Polygon, Wedge, Circle

import layout_model as lm
import site_plan as st

RU = {"CheckpointZone": "КПП", "BarracksZone": "Казармы", "ConstructionZone": "Стройка",
      "RestCanopyZone": "Навес отдыха", "WarehouseZone": "Склад/мастерская",
      "VehicleYardZone": "Техника/ГСМ", "ShippingDepotZone": "Контейнеры", "TransportZone": "Транспорт (новая)"}
SOLID = {"BarracksZone", "WarehouseZone", "ShippingDepotZone", "CheckpointZone", "TransportZone"}

# палитра (reference palette, светлая тема)
C_SURF, C_TXT, C_TXT2 = "#fcfcfb", "#0b0b0b", "#52514e"
C_SOLID, C_SEE, C_GAP, C_PATH, C_RED, C_GRID = "#2a78d6", "#eb6834", "#1baf7a", "#d9cfb8", "#e34948", "#e4e3df"
C_BEFORE, C_AFTER = "#eb6834", "#2a78d6"


OPEN_BAYS = {2, 3}


def placement_boxes(pl, zones, model, walls=False):
    bx = list(lm.fixed_boxes(model)) + (lm.bay_walls(OPEN_BAYS) if walls else [])
    for n, p in pl.items():
        bx += zones[n].boxes(*p, model)
    return bx


def ray_hits(pl, zones, data, az, origin=(0.0, 0.0), rmax=150.0):
    """Что лежит по лучу (любая высота): зоны, деревья, ограда. Для подписи просвета."""
    dx, dz = math.sin(math.radians(az)), math.cos(math.radians(az))
    hits = []
    for n, p in pl.items():
        for b in zones[n].boxes(*p, "aabb"):
            t = _slab(origin, (dx, dz), b)
            if t is not None:
                hits.append((t, f"{RU[n]} / {b[6]} (h {b[5]:.1f} м)"))
    for tr in data["trees"]:
        vx, vz = tr["x"] - origin[0], tr["z"] - origin[1]
        t = vx * dx + vz * dz
        if t > 0 and abs(-vx * dz + vz * dx) < 3.5:
            hits.append((t, f"пихта {tr['parent']}"))
    hits.sort()
    seen, out = set(), []
    for t, lab in hits:
        key = lab.split(" / ")[0]
        if key in seen:
            continue
        seen.add(key)
        out.append(f"{t:.0f} м: {lab}")
        if len(out) >= 4:
            break
    return out


def _slab(o, d, b):
    t0, t1 = 0.0, 1e9
    for oa, da, lo, hi in ((o[0], d[0], b[0], b[2]), (o[1], d[1], b[1], b[3])):
        if abs(da) < 1e-12:
            if oa < lo or oa > hi:
                return None
        else:
            a, c = (lo - oa) / da, (hi - oa) / da
            t0, t1 = max(t0, min(a, c)), min(t1, max(a, c))
    return t0 if t1 > t0 else None


def explicit_paths(spec, pl, zones):
    fps = {n: zones[n].footprint(*p) for n, p in pl.items()}
    keep = [("стрельбище", (lm.RANGE_KEEPOUT[0], lm.RANGE_KEEPOUT[1], lm.RANGE_KEEPOUT[2], lm.RANGE_KEEPOUT[3]))]
    keep += [(fb[6], fb[:4]) for fb in lm.fixed_boxes()[1:]]
    out = []
    for p in spec["paths"]:
        pts = [tuple(v) for v in p["pts"]]
        segs = list(zip(pts[:-1], pts[1:]))
        rects = [st._rect_from_seg(a, b, p["width"]) for a, b in segs]
        clash = []
        for i, r in enumerate(rects):
            for m, fp in fps.items():
                if m == p["zone"] and i == 0:
                    continue
                if lm.rect_gap(r, fp) < 0.2:
                    clash.append(m)
            for lab, kr in keep:
                if lm.rect_gap(r, kr) < 0:
                    clash.append(lab)
        out.append(dict(zone=p["zone"], label=p["label"], width=p["width"], clash=len(clash), clash_with=clash,
                        length=sum(math.dist(a, b) for a, b in segs), segs=[[list(a), list(b)] for a, b in segs],
                        rects=[list(r) for r in rects]))
    return out


def evaluate(pl, zones, eyes, walls=False):
    res = {}
    for model in ("aabb", "refined"):
        for lab, al in (("3deg", lm.ALPHAS_DEG), ("1.5deg", np.arange(0, 1.51, 0.5))):
            cov = lm.coverage(placement_boxes(pl, zones, model, walls), eyes, al)
            res[f"{model}_{lab}"] = dict(stats=lm.sector_stats(cov), cov=cov)
    return res


def gaps_from_center(cov_center, a0=30, a1=225):
    return [(a, b) for a, b in lm.runs_uncovered(cov_center, a0, a1)]


def draw_top(ax, pl, zones, data, title, fence=None, paths=None, gaps=None, removed=None, road=None, outer=None):
    ax.set_facecolor(C_SURF)
    ax.set_title(title, color=C_TXT, fontsize=11)
    # дальние деревья
    for tr in data["trees"]:
        ax.add_patch(Circle((tr["x"], tr["z"]), 1.2, color="#b9c8b0", lw=0, zorder=1))
    if fence:
        ax.add_patch(Polygon(fence, closed=True, fill=False, ec=C_TXT2, lw=1.4, ls="--", zorder=2))
    if paths:
        for lab, r, _ in st.apron_rects():
            ax.add_patch(Rectangle((r[0], r[1]), r[2] - r[0], r[3] - r[1], color=C_PATH, lw=0, zorder=2))
        for pth in paths:
            for r in pth["rects"]:
                ax.add_patch(Rectangle((r[0], r[1]), r[2] - r[0], r[3] - r[1], color=C_PATH, lw=0, zorder=2))
    for r in (road, outer):
        if r:
            ax.add_patch(Rectangle((r[0], r[1]), r[2] - r[0], r[3] - r[1], color="#c9bda0", lw=0, zorder=2))
    if gaps:
        for a, b in gaps:
            # matplotlib Wedge: углы от оси X против часовой; азимут -> 90 - az
            ax.add_patch(Wedge((0, 0), 70, 90 - b, 90 - a, color=C_GAP, alpha=0.18, lw=0, zorder=1.5))
            m = math.radians((a + b) / 2)
            ax.text(52 * math.sin(m), 52 * math.cos(m), f"{a}–{b}°", color=C_TXT, fontsize=7, ha="center", zorder=6)
    # арена, навес, стрельбище
    x0, z0, x1, z1 = lm.ARENA
    ax.add_patch(Rectangle((x0, z0), x1 - x0, z1 - z0, fc="#efeee9", ec=C_TXT, lw=1.2, zorder=3))
    ax.plot([x0, x0], [-lm.TENT_Z, lm.TENT_Z], color=C_TXT, lw=3, zorder=4)
    if fence:
        for i, bz0, bz1 in lm.east_bays():
            if i not in OPEN_BAYS:
                ax.plot([x1 + 0.6, x1 + 0.6], [bz0 + 0.15, bz1 - 0.15], color=C_TXT, lw=3, zorder=4)
            ax.text(x1 - 1.2, (bz0 + bz1) / 2, str(i), fontsize=6, color=C_TXT2, ha="center", va="center", zorder=5)
    ax.text(0, 0, "арена\n(пол)", ha="center", va="center", fontsize=8, color=C_TXT, zorder=5)
    k = lm.RANGE_KEEPOUT
    ax.add_patch(Rectangle((k[0], k[1]), k[2] - k[0], k[3] - k[1], fill=False, ec=C_TXT2, lw=0.8, ls=":", zorder=3))
    b = lm.BACKSTOP
    ax.add_patch(Rectangle((b[0], b[1]), b[2] - b[0], b[3] - b[1], fc="#cfcbc2", ec="none", zorder=3))
    for fb in lm.fixed_boxes()[1:]:
        ax.add_patch(Rectangle((fb[0], fb[1]), fb[2] - fb[0], fb[3] - fb[1], fc="#e6dcc4", ec=C_TXT2, lw=0.4, zorder=3))
    for x in np.linspace(-6, 6, 5):
        ax.plot([x], [lm.TARGETS_Z], marker="s", ms=4, color=C_TXT, zorder=4)
    ax.text(0, 35, "вал / скалы", ha="center", fontsize=7, color=C_TXT2, zorder=5)
    ax.text(0, 14, "стрельбище", ha="center", fontsize=7, color=C_TXT2, zorder=5)
    for n, p in pl.items():
        z = zones[n]
        col = C_SOLID if n in SOLID else C_SEE
        fp = z.footprint(*p)
        ax.add_patch(Rectangle((fp[0], fp[1]), fp[2] - fp[0], fp[3] - fp[1], fill=False, ec=col, lw=1.0, zorder=4))
        for bb in z.boxes(*p, "aabb"):
            ax.add_patch(Rectangle((bb[0], bb[1]), bb[2] - bb[0], bb[3] - bb[1], fc=col, alpha=0.35, ec=col, lw=0.6, zorder=4))
        for bb in z.boxes(*p, "refined"):
            if bb[5] > lm.EYE and bb[4] < lm.EYE:
                ax.add_patch(Rectangle((bb[0], bb[1]), bb[2] - bb[0], bb[3] - bb[1], fc=col, alpha=0.8, ec="none", zorder=4))
        for lab, (ex, ez), (nx, nz), role in z.entrances_world(*p):
            ax.annotate("", xy=(ex + nx * 3, ez + nz * 3), xytext=(ex, ez),
                        arrowprops=dict(arrowstyle="-|>", color=C_RED if role == "gate" else C_TXT, lw=1.2), zorder=6)
        ax.text((fp[0] + fp[2]) / 2, fp[3] + 1.0, RU[n], ha="center", fontsize=8, color=C_TXT, zorder=6,
                bbox=dict(fc=C_SURF, ec="none", alpha=0.75, pad=0.5))
    if removed:
        for t in removed:
            ax.plot(t["x"], t["z"], marker="x", color=C_RED, ms=6, mew=1.6, zorder=7)
    ax.set_xlim(-30, 75)
    ax.set_ylim(-65, 50)
    ax.set_aspect("equal")
    ax.grid(color=C_GRID, lw=0.5)
    ax.set_xlabel("X, м (восток →)", color=C_TXT2, fontsize=8)
    ax.set_ylabel("Z, м (север ↑)", color=C_TXT2, fontsize=8)
    ax.tick_params(colors=C_TXT2, labelsize=7)


def main():
    zones = lm.build_zones(include_transport=True)
    data = lm.load_layout()
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(lm.HERE, "proposed-layout.json")
    with open(src, encoding="utf-8") as f:
        spec = json.load(f)
    prop = {n: tuple(v) for n, v in spec["placement"].items()}
    cur = {n: (z.pivot[0], z.pivot[1], 0) for n, z in zones.items() if z.pivot is not None}
    eyes = lm.eye_grid()
    cidx = int(np.argmin(np.hypot(eyes[:, 0], eyes[:, 1])))
    center = np.array([[0.0, 0.0]])
    ev = {"current": evaluate(cur, zones, eyes), "proposed": evaluate(prop, zones, eyes),
          "proposed_with_bay_walls": evaluate(prop, zones, eyes, walls=True)}
    evc = {k: {m: lm.coverage(placement_boxes(p, zones, m), center)[0] for m in ("aabb", "refined")}
           for k, p in (("current", cur), ("proposed", prop))}
    fence, gate, gate_d = st.fence(prop, zones)
    pths = explicit_paths(spec, prop, zones) if "paths" in spec else st.paths(prop, zones)
    removed, road, outer = st.tree_conflicts(prop, zones, pths, fence, data)
    gaps = gaps_from_center(evc["proposed"]["aabb"])
    gaps_ref = gaps_from_center(evc["proposed"]["refined"])
    gap_info = [dict(az=[a, b], width=b - a, center=(a + b) / 2, sees=ray_hits(prop, zones, data, (a + b) / 2))
                for a, b in gaps]

    rows = []
    for n in prop:
        x, z, d = prop[n]
        fp = zones[n].footprint(x, z, d)
        cx, cz = (fp[0] + fp[2]) / 2, (fp[1] + fp[3]) / 2
        rows.append(dict(zone=n, pivot=[round(x, 2), round(z, 2)], yaw=(zones[n].yaw + d) % 360, dyaw=d,
                         footprint=[round(v, 2) for v in fp], center_to_arena=round(lm.rect_point_dist(lm.ARENA, cx, cz), 1),
                         edge_to_arena=round(lm.rect_gap(fp, lm.ARENA), 1),
                         azimuth=round(lm.azimuth(cx, cz)), old_pivot=[round(v, 2) for v in zones[n].pivot] if zones[n].pivot else None,
                         old_yaw=zones[n].yaw,
                         entrances=[dict(label=l, at=[round(e[0], 1), round(e[1], 1)], normal=[round(nn[0], 2), round(nn[1], 2)], role=r)
                                    for l, e, nn, r in zones[n].entrances_world(x, z, d)]))
    fps = {n: zones[n].footprint(*prop[n]) for n in prop}
    pair_gaps = sorted((round(lm.rect_gap(fps[a], fps[b]), 1), a, b) for i, a in enumerate(fps) for b in list(fps)[i + 1:])
    out = dict(
        source=os.path.basename(src),
        zones=rows,
        min_pair_gaps=pair_gaps[:6],
        open_bays=sorted(OPEN_BAYS), bays=[list(b) for b in lm.east_bays()],
        coverage={k: {m: {s: round(v["mean"] * 100, 1) for s, v in e["stats"].items()} for m, e in ev[k].items()} for k in ev},
        coverage_worst_point={k: {m: {s: round(v["worst"] * 100, 1) for s, v in e["stats"].items()} for m, e in ev[k].items()} for k in ev},
        gaps_center_aabb=gap_info,
        gaps_center_refined=gaps_ref,
        fence=dict(polygon=[[round(a, 1), round(b, 1)] for a, b in fence], perimeter=round(st.poly_perimeter(fence), 1),
                   gate=dict(at=[round(v, 1) for v in gate[1]], normal=[round(v, 2) for v in gate[2]], dist_to_fence=round(gate_d, 2))),
        paths=[dict(zone=p["zone"], label=p["label"], width=p["width"], length=round(p["length"], 1), clash=p["clash"], clash_with=p.get("clash_with"),
                    segs=[[[round(v, 1) for v in a], [round(v, 1) for v in b]] for a, b in p["segs"]]) for p in pths],
        apron=[dict(label=l, rect=[round(v, 1) for v in r], width=w) for l, r, w in st.apron_rects()],
        checkpoint_road=[round(v, 1) for v in road], outer_road=[round(v, 1) for v in outer],
        trees_removed=removed,
        trees_outside_fence=sum(1 for t in data["trees"] if not st.point_in_poly(fence, t["x"], t["z"])),
        trees_total=len(data["trees"]),
    )
    with open(os.path.join(lm.OUT, "layout-result.json"), "w", encoding="utf-8") as f:
        json.dump(out, f, ensure_ascii=False, indent=1)

    # --- вид сверху ---
    plt.rcParams["font.family"] = "DejaVu Sans"
    fig, axs = plt.subplots(1, 2, figsize=(18, 10), facecolor=C_SURF)
    cfence = None
    draw_top(axs[0], cur, zones, data, "Сейчас (просветы из центра, модель AABB, 3°)",
             gaps=gaps_from_center(evc["current"]["aabb"]))
    draw_top(axs[1], prop, zones, data, "Предложение: зоны, просветы, дорожки, ограда, ворота",
             fence=fence, paths=pths, gaps=gaps, removed=removed, road=road, outer=outer)
    from matplotlib.lines import Line2D
    from matplotlib.patches import Patch
    leg = [Patch(fc=C_SOLID, alpha=0.8, label="глухой силуэт (уточнённая модель)"),
           Patch(fc=C_SOLID, alpha=0.35, label="AABB частей: глухие зоны"),
           Patch(fc=C_SEE, alpha=0.35, label="AABB частей: сквозные/низкие зоны"),
           Patch(fc=C_GAP, alpha=0.3, label="просвет из центра арены"),
           Patch(fc=C_PATH, label="дорожки"), Line2D([], [], color=C_TXT2, ls="--", label="ограда"),
           Line2D([], [], color=C_RED, marker="x", ls="", label="дерево под удаление"),
           Line2D([], [], color=C_TXT, marker=r"$\rightarrow$", ls="", label="вход / ворота (красная)")]
    axs[1].legend(handles=leg, loc="lower right", fontsize=7, framealpha=0.9)
    fig.tight_layout()
    fig.savefig(os.path.join(lm.OUT, "topview.png"), dpi=110, facecolor=C_SURF)

    # --- полярная диаграмма покрытия ---
    fig = plt.figure(figsize=(14, 8), facecolor=C_SURF)
    for i, model in enumerate(("aabb", "refined")):
        ax = fig.add_subplot(1, 2, i + 1, projection="polar")
        ax.set_theta_zero_location("N")
        ax.set_theta_direction(-1)
        th = np.radians(lm.AZ)
        for key, col, lab in (("current", C_BEFORE, "сейчас"), ("proposed", C_AFTER, "предложение"),
                              ("proposed_with_bay_walls", C_GAP, "предложение + рабочие стены (открыты пролёты 2, 3)")):
            frac = ev[key][f"{model}_3deg"]["cov"].mean(axis=0)
            ax.plot(np.append(th, th[0]), np.append(frac, frac[0]) * 100, color=col, lw=2, label=lab)
        ax.set_ylim(0, 100)
        ax.set_yticks([25, 50, 75, 100])
        ax.tick_params(colors=C_TXT2, labelsize=7)
        name = "AABB частей (по заданию)" if model == "aabb" else "уточнённая: открытый навес, решётки, низкие части — сквозные"
        ax.set_title(f"Доля точек пола, где луч закрыт до 3°\n{name}", color=C_TXT, fontsize=10)
        for a0, a1, lab in ((45, 135, "восток"), (135, 225, "юг")):
            ax.fill_between(np.radians(np.arange(a0, a1 + 1)), 0, 100, color=C_GRID, alpha=0.5, zorder=0)
    h, l = ax.get_legend_handles_labels()
    fig.legend(h, l, loc="lower center", ncol=2, fontsize=9, frameon=False)
    fig.subplots_adjust(top=0.84, bottom=0.08, wspace=0.3)
    fig.savefig(os.path.join(lm.OUT, "polar.png"), dpi=110, facecolor=C_SURF)
    print(json.dumps({k: out[k] for k in ("coverage", "gaps_center_aabb", "gaps_center_refined", "min_pair_gaps", "fence")}, ensure_ascii=False, indent=1)[:6000])
    for r in rows:
        print(r["zone"], r["pivot"], r["yaw"], r["center_to_arena"], r["edge_to_arena"], r["azimuth"])
    for p in out["paths"]:
        print("path", p["zone"], p["clash"], p["length"])
    print("trees removed", len(removed), [(t["parent"], t["x"], t["z"], t["reason"]) for t in removed])


if __name__ == "__main__":
    main()
