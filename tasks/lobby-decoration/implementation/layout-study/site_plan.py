# -*- coding: utf-8 -*-
"""Ограда, ворота КПП, дорожки и конфликты деревьев для расстановки зон."""
import math

import layout_model as lm

FENCE_ZONE_MARGIN = 3.0      # полоса обслуживания между зоной и оградой
FENCE_WEST_X = -14.0         # ограда сразу за западной стеной/стрельбищем (за ней скалы)
FENCE_NORTH_Z = 45.0         # за валом стрельбища
TREE_ZONE_MARGIN = 2.5       # ствол + нижний ярус кроны пихты (крона ~8.5 м)
TREE_PATH_MARGIN = 1.5
APRON = 3.0                  # ширина дорожки-обхода вдоль открытых сторон арены
APRON_OFFSET = 1.0           # от края физического пола до дорожки


def convex_hull(pts):
    pts = sorted(set(pts))
    if len(pts) <= 2:
        return pts

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo, up = [], []
    for p in pts:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], p) <= 0:
            lo.pop()
        lo.append(p)
    for p in reversed(pts):
        while len(up) >= 2 and cross(up[-2], up[-1], p) <= 0:
            up.pop()
        up.append(p)
    return lo[:-1] + up[:-1]


def seg_point_dist(a, b, p):
    ax, az = a
    bx, bz = b
    px, pz = p
    vx, vz = bx - ax, bz - az
    L2 = vx * vx + vz * vz
    t = 0 if L2 == 0 else max(0, min(1, ((px - ax) * vx + (pz - az) * vz) / L2))
    return math.hypot(ax + t * vx - px, az + t * vz - pz)


def poly_perimeter(poly):
    return sum(math.dist(poly[i], poly[(i + 1) % len(poly)]) for i in range(len(poly)))


def fence(pl, zones):
    pts = []
    cpn = "CheckpointZone"
    for n, p in pl.items():
        fp = zones[n].footprint(*p)
        m = FENCE_ZONE_MARGIN
        if n == cpn:
            # сторона ворот лежит прямо на ограде
            (_, (gx, gz), (nx, nz), _) = zones[n].entrances_world(*p)[0]
            ex = [m, m, m, m]  # xmin zmin xmax zmax
            if nx < -0.5: ex[0] = 0
            if nx > 0.5: ex[2] = 0
            if nz < -0.5: ex[1] = 0
            if nz > 0.5: ex[3] = 0
            r = (fp[0] - ex[0], fp[1] - ex[1], fp[2] + ex[2], fp[3] + ex[3])
        else:
            r = (fp[0] - m, fp[1] - m, fp[2] + m, fp[3] + m)
        pts += [(r[0], r[1]), (r[2], r[1]), (r[0], r[3]), (r[2], r[3])]
    rk = lm.RANGE_KEEPOUT
    pts += [(FENCE_WEST_X, FENCE_NORTH_Z), (rk[2] + 2, FENCE_NORTH_Z)]
    south = min(p[1] for p in pts)
    pts += [(FENCE_WEST_X, south)]
    hull = convex_hull(pts)
    gate = zones[cpn].entrances_world(*pl[cpn])[0]
    gd = min(seg_point_dist(hull[i], hull[(i + 1) % len(hull)], gate[1]) for i in range(len(hull)))
    return hull, gate, gd


def apron_rects():
    """L-образная дорожка вдоль восточной и южной кромки пола арены (снаружи ограждения)."""
    ax0, az0, ax1, az1 = lm.ARENA
    o, w = APRON_OFFSET, APRON
    east = (ax1 + o, az0 - o - w, ax1 + o + w, az1 + o)
    south = (ax0 - 2, az0 - o - w, ax1 + o + w, az0 - o)
    return [("обход арены (восток)", east, w), ("обход арены (юг)", south, w)]


def _rect_from_seg(a, b, w):
    return (min(a[0], b[0]) - w / 2, min(a[1], b[1]) - w / 2, max(a[0], b[0]) + w / 2, max(a[1], b[1]) + w / 2)


def _nearest_apron_point(p):
    best = None
    for _, r, _ in apron_rects():
        cx = min(max(p[0], r[0] + 1.5), r[2] - 1.5)
        cz = min(max(p[1], r[1] + 1.5), r[3] - 1.5)
        d = math.hypot(cx - p[0], cz - p[1])
        if best is None or d < best[0]:
            best = (d, (cx, cz))
    return best[1]


def paths(pl, zones):
    """Ответвления от обхода арены к каждому входу (Г-образно, без пересечения зон)."""
    fps = {n: zones[n].footprint(*p) for n, p in pl.items()}
    out = []
    for n, p in pl.items():
        for label, (ex, ez), (nx, nz), role in zones[n].entrances_world(*p):
            if role == "gate":
                continue
            w = 4.0 if n == "CheckpointZone" else 2.5
            start = (ex + nx * 0.2, ez + nz * 0.2)
            out_pt = (ex + nx * (w / 2 + 1.0), ez + nz * (w / 2 + 1.0))
            target = _nearest_apron_point(out_pt)
            best = None
            for order in ("xz", "zx"):
                mid = (target[0], out_pt[1]) if order == "xz" else (out_pt[0], target[1])
                segs = [(start, out_pt), (out_pt, mid), (mid, target)]
                rects = [_rect_from_seg(a, b, w) for a, b in segs if math.dist(a, b) > 0.01]
                clash = 0
                for m, fp in fps.items():
                    for i, r in enumerate(rects):
                        if m == n and i == 0:
                            continue
                        if lm.rect_gap(r, fp) < 0.3:
                            clash += 1
                ko = lm.RANGE_KEEPOUT
                for r in rects:
                    if lm.rect_gap(r, (ko[0], ko[1] + 6, ko[2], ko[3])) < 0:
                        clash += 1
                length = sum(math.dist(a, b) for a, b in segs)
                cand = (clash, length, segs, rects)
                if best is None or cand[:2] < best[:2]:
                    best = cand
            out.append(dict(zone=n, label=label, width=w, clash=best[0], length=best[1],
                            segs=[[list(a), list(b)] for a, b in best[2]], rects=[list(r) for r in best[3]]))
    return out


def tree_conflicts(pl, zones, path_list, hull, data):
    fps = {n: zones[n].footprint(*p) for n, p in pl.items()}
    res = []
    rects = [(pp["zone"] + ": " + pp["label"], r) for pp in path_list for r in pp["rects"]]
    rects += [(lab, r) for lab, r, _ in apron_rects()]
    cpn = "CheckpointZone"
    road = zones[cpn].road_local
    x0, z0 = lm.rot(road[0], road[1], pl[cpn][2])
    x1, z1 = lm.rot(road[2], road[3], pl[cpn][2])
    road_w = (min(x0, x1) + pl[cpn][0], min(z0, z1) + pl[cpn][1], max(x0, x1) + pl[cpn][0], max(z0, z1) + pl[cpn][1])
    # дорога наружу за воротами: 25 м по нормали ворот
    gate = zones[cpn].entrances_world(*pl[cpn])[0]
    (gx, gz), (gnx, gnz) = gate[1], gate[2]
    outer = _rect_from_seg((gx, gz), (gx + gnx * 25, gz + gnz * 25), 8.0)
    rects += [("дорога КПП", road_w), ("дорога за воротами", outer)]
    for t in data["trees"]:
        x, z = t["x"], t["z"]
        hit = None
        for n, fp in fps.items():
            if lm.rect_point_dist(fp, x, z) < TREE_ZONE_MARGIN:
                hit = "зона " + n
                break
        if hit is None:
            for lab, r in rects:
                if lm.rect_point_dist(r, x, z) < TREE_PATH_MARGIN:
                    hit = "дорожка: " + lab
                    break
        if hit:
            res.append(dict(name=t["name"], parent=t["parent"], x=round(x, 2), z=round(z, 2), reason=hit))
    return res, road_w, outer


def point_in_poly(poly, x, z):
    inside = False
    n = len(poly)
    for i in range(n):
        (x1, z1), (x2, z2) = poly[i], poly[(i + 1) % n]
        if (z1 > z) != (z2 > z):
            xi = x1 + (z - z1) * (x2 - x1) / (z2 - z1)
            if xi > x:
                inside = not inside
    return inside
