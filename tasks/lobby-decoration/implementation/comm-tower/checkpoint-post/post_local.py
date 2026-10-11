# -*- coding: utf-8 -*-
"""Локальная система для 6 одинаковых постов (кластеры n=129 из find_posts.py):
центр — среднее 4 угловых модулей нижнего ряда, рыскание — по входной стене, дверь -> +Z.
Печатает переменные (не общие для всех 6) объекты по уровням, чтобы выбрать экземпляр.
Выход: reports/implementation/comm-tower/checkpoint-post/post-variants.json
"""
import json, math, os
from collections import Counter

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../../reports/implementation/comm-tower/checkpoint-post"))
clusters = json.load(open(os.path.join(REP, "building-clusters.json"), encoding="utf-8"))
inst_all = json.load(open(os.path.normpath(os.path.join(REP, "../showcase-instances.json")), encoding="utf-8"))["instances"]


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)

def yaw_of(q):
    x, y, z, w = q
    return math.degrees(math.atan2(2*(w*y + x*z), 1 - 2*(x*x + y*y)))

def qyaw(deg):
    h = math.radians(deg) / 2
    return (0.0, math.sin(h), 0.0, math.cos(h))

def euler(q):
    x, y, z, w = q
    ex = math.degrees(math.asin(max(-1.0, min(1.0, 2*(w*x - y*z)))))
    ey = math.degrees(math.atan2(2*(w*y + x*z), 1 - 2*(x*x + y*y)))
    ez = math.degrees(math.atan2(2*(w*z + x*y), 1 - 2*(x*x + z*z)))
    n = lambda a: 0.0 if abs(round(a, 2)) < 0.01 else round(a, 2)
    return [n(ex), n(ey), n(ez)]


def frame(c):
    parts = c["parts"]
    corners = [p for p in parts if p["prefab"].startswith("SM_Building_wall_corner_90d")]
    y0 = min(p["pos"][1] for p in corners)
    low = [p for p in corners if abs(p["pos"][1] - y0) < 0.05]
    cx = sum(p["pos"][0] for p in low) / len(low); cz = sum(p["pos"][2] for p in low) / len(low)
    door = next(p for p in parts if p["prefab"] == "SM_Door_single")
    ent = next(p for p in parts if p["prefab"].startswith("SM_Building_wall_entrance"))
    # выбираем поворот кратный 90° от рыскания входной стены, при котором дверь уходит в +Z
    base = yaw_of(ent["rot"])
    best = None
    for k in range(4):
        ang = base + 90 * k
        q = qyaw(-ang)
        d = [door["pos"][0] - cx, 0, door["pos"][2] - cz]
        r = qmul(qmul(q, (d[0], 0, d[2], 0)), (0, -q[1], 0, q[3]))
        if best is None or r[2] > best[1]:
            best = (ang, r[2])
    ang = best[0]
    return (cx, y0, cz), ang


def localize(objs, org, ang):
    q = qyaw(-ang)
    out = []
    for o in objs:
        d = (o["pos"][0] - org[0], o["pos"][1] - org[1], o["pos"][2] - org[2])
        r = qmul(qmul(q, (d[0], d[1], d[2], 0)), (0, -q[1], 0, q[3]))
        lr = qmul(q, tuple(o["rot"]))
        out.append({"stem": o["prefab"], "position": [round(r[0], 3), round(r[1], 3), round(r[2], 3)],
                    "rotationEuler": euler(lr), "quaternion": [round(v, 5) for v in lr], "scale": o["scale"]})
    return out


if __name__ == "__main__":
    posts = [c for c in clusters if c["nParts"] == 129]
    res = []
    for c in posts:
        org, ang = frame(c)
        # окрестность шире исходной: 9 м от центра, по высоте -1..+6
        near = [o for o in inst_all if math.hypot(o["pos"][0] - org[0], o["pos"][2] - org[2]) < 9.0
                and -1.5 <= o["pos"][1] - org[1] <= 6 and not o["prefab"].startswith(("SM_Cliff", "SM_Rock"))]
        loc = localize(near, org, ang)
        res.append({"origin": org, "yaw": ang, "parts": loc})
    sigs = [Counter((p["stem"], round(p["position"][0], 1), round(p["position"][1], 1), round(p["position"][2], 1)) for p in r["parts"]) for r in res]
    common = sigs[0]
    for s in sigs[1:]:
        common = common & s
    print("common parts:", sum(common.values()))
    for i, r in enumerate(res):
        var = [p for p in r["parts"] if (p["stem"], round(p["position"][0], 1), round(p["position"][1], 1), round(p["position"][2], 1)) not in common]
        r["variable"] = var
        print("post", i, [round(v, 2) for v in r["origin"]], "yaw %.1f" % r["yaw"], "variable", len(var))
        for p in sorted(var, key=lambda p: p["position"][1]):
            print("    %-30s %s" % (p["stem"], p["position"]))
    json.dump({"commonKeys": [list(k) for k in common.keys()], "posts": res}, open(os.path.join(REP, "post-variants.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
