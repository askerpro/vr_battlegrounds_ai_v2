# -*- coding: utf-8 -*-
"""Перевод частей кластеров вышек в локальную систему основания (нижняя SM_Antenna_a,
y=0 — её pivot). Проверка идентичности вариантов и экспорт JSON выбранных вышек.
Выход: implementation/comm-tower/tower-<name>.json, reports/.../tower-variants.json
"""
import json, math, os
from collections import Counter

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../reports/implementation/comm-tower"))
clusters = json.load(open(os.path.join(REP, "tower-clusters.json"), encoding="utf-8"))


def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw*bx + ax*bw + ay*bz - az*by, aw*by - ax*bz + ay*bw + az*bx,
            aw*bz + ax*by - ay*bx + az*bw, aw*bw - ax*bx - ay*by - az*bz)

def qinv(q):
    return (-q[0], -q[1], -q[2], q[3])

def qrot(q, v):
    r = qmul(qmul(q, (v[0], v[1], v[2], 0.0)), qinv(q))
    return r[:3]

def euler(q):
    """Unity ZXY -> эйлеровы углы (x,y,z) в градусах."""
    x, y, z, w = q
    sinx = 2*(w*x - y*z)
    sinx = max(-1.0, min(1.0, sinx))
    ex = math.degrees(math.asin(sinx))
    ey = math.degrees(math.atan2(2*(w*y + x*z), 1 - 2*(x*x + y*y)))
    ez = math.degrees(math.atan2(2*(w*z + x*y), 1 - 2*(x*x + z*z)))
    def n(a):
        a = round(a, 2)
        return 0.0 if abs(a) < 0.01 else a
    return [n(ex), n(ey), n(ez)]


def localize(c):
    a = sorted([p for p in c["parts"] if p["prefab"] == "SM_Antenna_a"], key=lambda p: p["pos"][1])
    base = a[0]
    bp, br = base["pos"], tuple(base["rot"])
    # базис — только по рысканию основания (в сцене основание может быть не строго вертикально)
    inv = qinv(br)
    out = []
    for p in c["parts"]:
        d = [p["pos"][i] - bp[i] for i in range(3)]
        lp = qrot(inv, d)
        lr = qmul(inv, tuple(p["rot"]))
        out.append({"stem": p["prefab"], "position": [round(v, 3) for v in lp],
                    "rotationEuler": euler(lr), "quaternion": [round(v, 5) for v in lr],
                    "scale": p["scale"]})
    out.sort(key=lambda o: (o["position"][1], o["stem"], o["position"][0], o["position"][2]))
    return out, base


def sig(parts):
    return tuple((o["stem"], round(o["position"][0], 1), round(o["position"][1], 1), round(o["position"][2], 1),
                  round(o["rotationEuler"][1]) % 360) for o in parts)

towers = [c for c in clusters if c["composition"].get("SM_Antenna_a")]
variants = {}
for idx, c in enumerate(towers):
    parts, base = localize(c)
    s = sig(parts)
    variants.setdefault(s, []).append({"cluster": idx, "basePos": base["pos"], "baseRot": base["rot"], "baseEuler": euler(tuple(base["rot"])), "parts": parts})

rep = []
for s, lst in variants.items():
    rep.append({"count": len(lst), "nParts": len(lst[0]["parts"]), "height": max(p["position"][1] for p in lst[0]["parts"]),
                "instances": [{"cluster": v["cluster"], "basePos": v["basePos"], "baseEuler": v["baseEuler"]} for v in lst],
                "parts": lst[0]["parts"]})
rep.sort(key=lambda r: (-r["height"], -r["count"]))
json.dump(rep, open(os.path.join(REP, "tower-variants.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
for r in rep:
    print("variant: count=%d parts=%d topPivot=%.2f" % (r["count"], r["nParts"], r["height"]))
    for v in r["instances"]:
        print("   cluster", v["cluster"], v["basePos"], v["baseEuler"])
    for o in r["parts"]:
        print("   %-20s pos=%-28s eul=%-24s s=%s" % (o["stem"], o["position"], o["rotationEuler"], o["scale"]))
