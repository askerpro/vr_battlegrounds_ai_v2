# -*- coding: utf-8 -*-
"""Экспорт состава вышек связи из LV_Showcase (варианты из tower-variants.json) в
implementation/comm-tower/tower-<name>.json; y=0 — низ бетонных опор SM_Antenna_c (земля).
Плюс оценка треугольников: сумма LOD0 и фактический LOD на дистанциях (Quest lodBias 1.5).
"""
import json, math, os

ROOT = os.path.dirname(os.path.abspath(__file__))
REP = os.path.normpath(os.path.join(ROOT, "../../reports/implementation/comm-tower"))
variants = json.load(open(os.path.join(REP, "tower-variants.json"), encoding="utf-8"))
fbx = json.load(open(os.path.join(REP, "fbx-tris.json"), encoding="utf-8"))
GROUND = 0.373  # pivot SM_Antenna_a над низом SM_Antenna_c (высота опоры 0.3745 м)

IMPORTED = {"SM_Antenna_a", "SM_Antenna_c", "SM_Antenna_d", "SM_Antenna_e", "SM_Antenna_f", "SM_Antenna_g", "SM_Antenna_h", "SM_SatelliteDish_01"}
ROLE = {
    "SM_Antenna_a": "секция решётчатой фермы 1.80x4.02x1.80, pivot снизу по центру",
    "SM_Antenna_c": "бетонная опора-подпятник под ногу 0.47x0.37x0.47",
    "SM_Antenna_d": "малая тарелка РРЛ ~D0.87 на кронштейне",
    "SM_Antenna_e": "тарелка РРЛ ~D1.65 на кронштейне",
    "SM_Antenna_f": "малый блок/облучатель 0.53x0.65x0.82 на кронштейне",
    "SM_Antenna_g": "штырь 4.4 м на верхнем торце ноги",
    "SM_Antenna_h": "панельная антенна 1.86 м на кронштейне",
    "SM_Antenna_i": "длинный штырь/мачта 12.4 м на хомутах сбоку ноги (выступ ~9.8 м вверх)",
    "SM_Antenna_j": "панельная антенна 2.15 м на кронштейне",
    "SM_Antenna_k": "высокая панель/фидерный блок 3.92 м на кронштейне",
}
# LODGroup из URP-префабов: size, пороги screenRelativeHeight
LODS = {
    "SM_Antenna_a": (4.7278, [0.5996, 0.4048, 0.2481, 0.0196]),
    "SM_Antenna_c": (0.7321, [0.3956, 0.0217]),
    "SM_Antenna_d": (1.1617, [0.5996, 0.3956, 0.2481, 0.0228]),
    "SM_Antenna_e": (1.7138, [0.5996, 0.3956, 0.2499, 0.0206]),
    "SM_Antenna_f": (1.1204, [0.6024, 0.4002, 0.2463, 0.0196]),
    "SM_Antenna_g": (4.4143, [0.6027, 0.2527, 0.0211]),
    "SM_Antenna_h": (1.9642, [0.5967, 0.3956, 0.2463, 0.0190]),
    "SM_Antenna_i": (12.3774, [0.5998, 0.3960, 0.2527, 0.0200]),
    "SM_Antenna_j": (2.3641, [0.5967, 0.3979, 0.2499, 0.0228]),
    "SM_Antenna_k": (4.0995, [0.6024, 0.3956, 0.2517, 0.0190]),
}
LOD_BIAS = 1.5      # ProjectSettings/QualitySettings, профиль Quest
TAN_HALF_FOV = 1.0  # ~90° вертикальный FOV глаза Quest


def tris(stem, lod=0):
    ms = fbx[stem + ".fbx"]["models"]
    return ms[min(lod, len(ms) - 1)]["triangles"]


def tris_at(stem, dist):
    size, th = LODS[stem]
    rel = size * LOD_BIAS / (2 * dist * TAN_HALF_FOV)
    for lod, t in enumerate(th):
        if rel >= t:
            return tris(stem, lod)
    return 0  # culled


def budget(parts, dists=(10, 20, 40, 60)):
    lod0 = sum(tris(p["stem"]) for p in parts)
    lod1 = sum(tris(p["stem"], 1) for p in parts)
    at = {}
    for d in dists:
        s = 0
        for p in parts:
            y = p["position"][1]
            s += tris_at(p["stem"], math.hypot(d, y))  # грубо: горизонтальная дистанция d до основания
        at[str(d)] = s
    at["lod1Sum"] = lod1
    return lod0, at


def emit(name, desc, parts, src):
    lod0, at = budget(parts)
    doc = {
        "name": name, "description": desc,
        "source": "HDRP(Default)/Scenes/Showcase/LV_Showcase.unity (MilitaryCamp), разбор parse_showcase.py/tower_local.py",
        "sourceInstances": src,
        "frame": "локальная система основания вышки: y=0 — низ бетонных опор (земля), ось Y вверх, оси X/Z вдоль граней фермы; поворот — эйлер Unity (ZXY) и кватернион [x,y,z,w]",
        "prefabGuidNote": "GUID префабов SM_Antenna_* совпадают в HDRP и URP архиве",
        "triangles": {"lod0Sum": lod0, "questRuntimeByHorizontalDistance_m": at, "lodBias": LOD_BIAS},
        "parts": [],
    }
    for p in parts:
        doc["parts"].append({
            "stem": p["stem"], "role": ROLE.get(p["stem"], ""),
            "position": [p["position"][0], round(p["position"][1] + GROUND, 3), p["position"][2]],
            "rotationEuler": p["rotationEuler"], "quaternion": p["quaternion"], "scale": p["scale"],
            "imported": p["stem"] in IMPORTED, "lod0Triangles": tris(p["stem"]),
        })
    path = os.path.join(ROOT, "tower-%s.json" % name)
    json.dump(doc, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(name, len(parts), "lod0", lod0, at, "top", max(q["position"][1] for q in doc["parts"]))
    return doc


tall = next(v for v in variants if v["nParts"] == 21)
short = next(v for v in variants if v["nParts"] == 15)
emit("tall", "Высокая вышка связи: 5 секций фермы (~20.5 м) + 4 штыря наверху (~24.9 м), 12 м штырь на ноге, 2 тарелки РРЛ, 4 панели/блока", tall["parts"], tall["instances"])
emit("short", "Низкая вышка связи: 3 секции фермы (~12.4 м) + 4 штыря наверху (~16.9 м), 12 м штырь на ноге, 1 тарелка РРЛ, 2 панели", short["parts"], short["instances"])


def pick(parts, rule):
    out = []; used = {}
    for p in parts:
        k = p["stem"]
        if rule(p, used.get(k, 0)):
            out.append(p); used[k] = used.get(k, 0) + 1
    return out

# Облегчённые варианты: только уже импортированные stems
# short-lite: 3 секции + 4 опоры + 2 штыря по диагонали + тарелка e (поворот как в short)
def short_lite_rule(p, n):
    s = p["stem"]; x, y, z = p["position"]
    if s in ("SM_Antenna_a", "SM_Antenna_c", "SM_Antenna_e"):
        return True
    if s == "SM_Antenna_g":
        return (x > 0) == (z > 0)   # диагональ
    return False
emit("short-lite", "Облегчённая низкая вышка (Quest): 3 секции, 4 опоры, 2 штыря по диагонали, тарелка e; только импортированные stems", pick(short["parts"], short_lite_rule), short["instances"])

# tall-lite: 5 секций + опоры + 2 штыря + e (нижняя) + h — без d/f/i/j/k и второй e
def tall_lite_rule(p, n):
    s = p["stem"]; x, y, z = p["position"]
    if s in ("SM_Antenna_a", "SM_Antenna_c", "SM_Antenna_h"):
        return True
    if s == "SM_Antenna_e":
        return n == 0
    if s == "SM_Antenna_g":
        return (x > 0) == (z > 0)
    return False
emit("tall-lite", "Облегчённая высокая вышка (Quest): 5 секций, 4 опоры, 2 штыря, тарелка e (нижняя), панель h; только импортированные stems", pick(tall["parts"], tall_lite_rule), tall["instances"])
