# -*- coding: utf-8 -*-
"""Геометрическая модель зон лобби для оценки перекрытия обзора (без Unity).

Оси: X восток, Z север, метры. Азимут 0° = север, по часовой (90° = восток).
Источник: reports/implementation/layout-study/scene-layout-2026-10-11.json (+ нативные
отчёты зон для уточнённой модели «видимого силуэта»).
Поворот Unity yaw (по часовой сверху): (x,z) -> (x cos t + z sin t, -x sin t + z cos t).
Yaw перебирается кратно 90°, поэтому все части остаются AABB.
"""
import json
import math
import os

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
TASK = os.path.normpath(os.path.join(HERE, "..", ".."))
REPORTS = os.path.join(TASK, "reports", "implementation")
OUT = os.path.join(REPORTS, "layout-study")
LAYOUT_JSON = os.path.join(OUT, "scene-layout-2026-10-11.json")

EYE = 1.6
RANGE_MAX = 60.0
ALPHAS_DEG = np.arange(0.0, 3.01, 0.5)          # угловая высота, которую зона должна закрыть
ARENA = (-7.75, -9.48, 7.75, 9.48)               # xmin, zmin, xmax, zmax
TENT_Z = 7.48
# Стрельбище: огневой коридор + мишени + вал. Не перекрывать и не трогать.
RANGE_KEEPOUT = (-14.0, 9.48, 14.0, 45.0)
BACKSTOP = (-11.73, 28.0, 12.03, 42.67)
TARGETS_Z = 22.5

BASE_ZONES = ["CheckpointZone", "BarracksZone", "ConstructionZone", "RestCanopyZone",
              "WarehouseZone", "VehicleYardZone", "ShippingDepotZone"]


def load_layout():
    with open(LAYOUT_JSON, encoding="utf-8") as f:
        return json.load(f)


def rot(x, z, deg):
    t = math.radians(deg)
    c, s = round(math.cos(t), 12), round(math.sin(t), 12)
    return x * c + z * s, -x * s + z * c


class Part:
    """AABB части в локальной системе зоны (смещение от pivot при текущем yaw)."""

    def __init__(self, name, x0, z0, x1, z1, y0, y1):
        self.name, self.x0, self.z0, self.x1, self.z1, self.y0, self.y1 = name, x0, z0, x1, z1, y0, y1

    def placed(self, px, pz, dyaw):
        xs, zs = [], []
        for (x, z) in ((self.x0, self.z0), (self.x1, self.z0), (self.x0, self.z1), (self.x1, self.z1)):
            rx, rz = rot(x, z, dyaw)
            xs.append(rx + px)
            zs.append(rz + pz)
        return (min(xs), min(zs), max(xs), max(zs), self.y0, self.y1, self.name)


class Zone:
    def __init__(self, name, pivot, yaw, parts_aabb, parts_refined, entrances, note=""):
        self.name = name
        self.pivot = pivot            # текущий мировой pivot (x,z)
        self.yaw = yaw                # текущий мировой yaw
        self.parts = {"aabb": parts_aabb, "refined": parts_refined}
        # entrances: список (метка, локальная точка (x,z), локальная нормаль (x,z), роль)
        self.entrances = entrances
        self.note = note
        xs = [p.x0 for p in parts_aabb] + [p.x1 for p in parts_aabb]
        zs = [p.z0 for p in parts_aabb] + [p.z1 for p in parts_aabb]
        self.local_rect = Part("footprint", min(xs), min(zs), max(xs), max(zs), 0, max(p.y1 for p in parts_aabb))

    def footprint(self, px, pz, dyaw):
        return self.local_rect.placed(px, pz, dyaw)[:4]

    def boxes(self, px, pz, dyaw, model):
        return [p.placed(px, pz, dyaw) for p in self.parts[model]]

    def entrances_world(self, px, pz, dyaw):
        res = []
        for label, (lx, lz), (nx, nz), role in self.entrances:
            wx, wz = rot(lx, lz, dyaw)
            nwx, nwz = rot(nx, nz, dyaw)
            res.append((label, (wx + px, wz + pz), (nwx, nwz), role))
        return res


def _local(name, wmin, wmax, pivot):
    return Part(name, wmin[0] - pivot[0], wmin[2] - pivot[1], wmax[0] - pivot[0], wmax[2] - pivot[1], wmin[1], wmax[1])


def _wb(b):
    c, e = b["m_Center"], b["m_Extent"]
    return ([c["x"] - e["x"], c["y"] - e["y"], c["z"] - e["z"]], [c["x"] + e["x"], c["y"] + e["y"], c["z"] + e["z"]])


TRANSPORT = "TransportZone"


def transport_zone():
    """Новая зона по брифу пользователя 2026-10-11: ~10x14 м, h~3 м, pivot в центре.
    Состав (допущение по раскладке): SM_ArmoredVehicle 3.1x5.7x2.4, прицеп-цистерна
    SM_WaterStorageTank ~2.5x5x3.0, генератор SM_MobileGenerator ~2x3.5x2.3, канистры/бочки <1 м.
    Подъезд — длинная сторона (локальный -X)."""
    aabb = [Part("TransportZone", -5.0, -7.0, 5.0, 7.0, 0.0, 3.0)]
    refined = [Part("ArmoredVehicle", -3.5, -6.0, -0.4, -0.3, 0.0, 2.41),
               Part("WaterStorageTankTrailer", 0.8, -6.0, 3.3, -1.0, 0.0, 3.0),
               Part("MobileGenerator", 0.8, 1.0, 2.8, 4.5, 0.0, 2.3),
               Part("CanistersBarrels", -3.5, 2.0, -0.5, 5.5, 0.0, 0.95)]
    ent = [("подъезд к технике (длинная сторона) — допущение", (-5.0, 0.0), (-1, 0), "entrance")]
    return Zone(TRANSPORT, None, 0.0, aabb, refined, ent, note="новая зона")


def build_zones(include_transport=False):
    data = load_layout()
    zones = {}
    for z in data["zones"]:
        if z["name"] not in BASE_ZONES:
            continue
        piv = (z["pos"][0], z["pos"][2])
        aabb = [_local(c["name"], c["min"], c["max"], piv) for c in z["children"]]
        zones[z["name"]] = dict(pivot=piv, yaw=z["yaw"], aabb=aabb, raw=z)

    # ---------- уточнённые («видимый силуэт») части ----------
    refined = {}
    # КПП: нативные мировые bounds старой раскладки, перенос (+21.26, +7.93) без поворота
    cp = zones["CheckpointZone"]
    piv = cp["pivot"]
    with open(os.path.join(REPORTS, "checkpoint-zone", "native-build-road-sign.json"), encoding="utf-8") as f:
        cpj = json.load(f)
    a0, a1 = _wb(cpj["actualBounds"])
    dx = cp["raw"]["min"][0] - a0[0]
    dz = cp["raw"]["min"][2] - a0[2]
    parts = []
    for it in cpj["items"]:
        lo, hi = _wb(it["worldBounds"])
        role = it["role"]
        if role.startswith(("Guard", "UpperWindow", "EntrySide", "EntryHeader")) and hi[1] > 1.0:
            parts.append(Part(role, lo[0] + dx - piv[0], lo[2] + dz - piv[1], hi[0] + dx - piv[0], hi[2] + dz - piv[1], lo[1], hi[1]))
        if role == "Watchtower":
            # решётчатая вышка: глухой только верхний пост (допущение: от 4.5 м)
            parts.append(Part("WatchtowerCabin", lo[0] + dx - piv[0], lo[2] + dz - piv[1], hi[0] + dx - piv[0], hi[2] + dz - piv[1], 4.5, hi[1]))
    refined["CheckpointZone"] = parts
    # AABB-модель КПП: у зоны один child на весь участок вместе с проезжей дорогой, поэтому
    # берём AABB её собственных частей (пост, вышка, створки, барьеры); дорога — не преграда.
    groups = {}
    for it in cpj["items"]:
        lo, hi = _wb(it["worldBounds"])
        role = it["role"]
        if role == "RoadSurface" or hi[1] < 0.05:
            continue
        g = ("GuardHouse" if role.startswith(("Guard", "UpperWindow", "EntrySide", "EntryHeader", "DoorFrame"))
             else role if role.startswith(("Watchtower", "Road", "GuardApproach")) else f"Gate{int(lo[0])}")
        b = groups.setdefault(g, [1e9, 1e9, 1e9, -1e9, -1e9, -1e9])
        b[0], b[1], b[2] = min(b[0], lo[0]), min(b[1], lo[1]), min(b[2], lo[2])
        b[3], b[4], b[5] = max(b[3], hi[0]), max(b[4], hi[1]), max(b[5], hi[2])
    zones["CheckpointZone"]["aabb_parts"] = [Part(g, b[0] + dx - piv[0], b[2] + dz - piv[1], b[3] + dx - piv[0], b[5] + dz - piv[1], b[1], b[4])
                                             for g, b in groups.items()]
    road_lo, road_hi = _wb(cpj["road"])
    cp_road = (road_lo[0] + dx - piv[0], road_lo[2] + dz - piv[1], road_hi[0] + dx - piv[0], road_hi[2] + dz - piv[1])
    cp["road_local"] = cp_road

    # Казармы: только корпуса (крыльцо, стулья, фонарь и тонкая опора прожектора не глухие)
    refined["BarracksZone"] = [p for p in zones["BarracksZone"]["aabb"] if p.name.startswith("Казарма")]

    # Стройка: нативные строки, перенос по min X и центру Z
    co = zones["ConstructionZone"]
    with open(os.path.join(REPORTS, "construction-zone", "native-bounds.json"), encoding="utf-8") as f:
        coj = json.load(f)
    dx = co["raw"]["min"][0] - coj["aggregateMin"][0]
    dz = (co["raw"]["min"][2] + co["raw"]["max"][2]) / 2 - (coj["aggregateMin"][2] + coj["aggregateMax"][2]) / 2
    piv = co["pivot"]
    refined["ConstructionZone"] = [Part(r["name"], r["min"][0] + dx - piv[0], r["min"][2] + dz - piv[1],
                                        r["max"][0] + dx - piv[0], r["max"][2] + dz - piv[1], r["min"][1], r["max"][1])
                                   for r in coj["rows"]]

    # Навес отдыха: открыт снизу; глухая только крыша (карниз ~2.1 м .. конёк ~3.5 м)
    rc = zones["RestCanopyZone"]
    with open(os.path.join(REPORTS, "rest-canopy-zone", "latest-build-open.json"), encoding="utf-8") as f:
        rcj = json.load(f)
    rlo, rhi = _wb(rcj["ownRoofBounds"])
    ex_long = (rhi[0] - rlo[0]) / 2   # конёк вдоль X в старой раскладке
    ex_short = (rhi[2] - rlo[2]) / 2
    cx = (rc["raw"]["min"][0] + rc["raw"]["max"][0]) / 2 - rc["pivot"][0]
    cz = (rc["raw"]["min"][2] + rc["raw"]["max"][2]) / 2 - rc["pivot"][1]
    # текущий yaw 270: конёк вдоль Z
    refined["RestCanopyZone"] = [Part("OpenRoof", cx - ex_short, cz - ex_long, cx + ex_short, cz + ex_long, 2.09, rhi[1])]

    for n in ("WarehouseZone", "VehicleYardZone", "ShippingDepotZone"):
        refined[n] = [p for p in zones[n]["aabb"] if not p.name.startswith("SM_Door_lamp")]

    # ---------- входы (локально, текущая ориентация) ----------
    def edge_mid(zn, side, inset=0.0):
        xs = [p.x0 for p in zones[zn]["aabb"]] + [p.x1 for p in zones[zn]["aabb"]]
        zs = [p.z0 for p in zones[zn]["aabb"]] + [p.z1 for p in zones[zn]["aabb"]]
        x0, x1, z0, z1 = min(xs), max(xs), min(zs), max(zs)
        return {"S": ((x0 + x1) / 2, z0), "N": ((x0 + x1) / 2, z1), "W": (x0, (z0 + z1) / 2), "E": (x1, (z0 + z1) / 2)}[side]

    rx0, rz0, rx1, rz1 = cp_road
    ent = {
        "BarracksZone": [("входы казарм A/B (юг, ступени)", edge_mid("BarracksZone", "S"), (0, -1), "entrance")],
        "WarehouseZone": [("вход склада/мастерской (фонарь на западном фасаде) — допущение", edge_mid("WarehouseZone", "W"), (-1, 0), "entrance")],
        "CheckpointZone": [("ворота/дорога наружу", ((rx0 + rx1) / 2, rz0), (0, -1), "gate"),
                           ("въезд в базу (дорога КПП)", ((rx0 + rx1) / 2, rz1), (0, 1), "entrance")],
        "ConstructionZone": [("восточный проход стройки", edge_mid("ConstructionZone", "E"), (1, 0), "entrance")],
        "RestCanopyZone": [("длинная открытая сторона навеса", edge_mid("RestCanopyZone", "W"), (-1, 0), "entrance")],
        "VehicleYardZone": [("подъезд к машине (север) — допущение", edge_mid("VehicleYardZone", "N"), (0, 1), "entrance")],
        "ShippingDepotZone": [("проход к контейнерам (север) — допущение", edge_mid("ShippingDepotZone", "N"), (0, 1), "entrance")],
    }

    out = {}
    for n in BASE_ZONES:
        z = zones[n]
        out[n] = Zone(n, z["pivot"], z["yaw"], z["aabb"], refined[n], ent[n])
        if "aabb_parts" in z:
            out[n].parts["aabb"] = z["aabb_parts"]   # footprint остаётся по полному child
        if n == "CheckpointZone":
            out[n].road_local = z["road_local"]
    if include_transport:
        out[TRANSPORT] = transport_zone()
    return out


def fixed_boxes(model="aabb"):
    """Неподвижные перекрытия: вал/скалы + новый декор флангов стрельбища (бриф 2026-10-11):
    габионы HESCO и штабеля патронных ящиков, X вне ±6 (+дорожка), Z 10..28, высота 1–2 м."""
    x0, z0, x1, z1 = BACKSTOP
    h = 2.0 if model == "aabb" else 1.5   # уточнённая: ряды 1–1.5 м, местами 2
    return [(x0, z0, x1, z1, -0.31, 5.01, "RockBackstop"),
            (8.0, 10.5, 12.5, 28.0, 0.0, h, "RangeFlankEast_HESCO_Ammo"),
            (-12.5, 10.5, -8.0, 28.0, 0.0, h, "RangeFlankWest_HESCO_Ammo")]


def eye_grid(step=1.5):
    xs = np.arange(ARENA[0] + 0.75, ARENA[2] - 0.74, step)
    zs = np.arange(ARENA[1] + 0.75, ARENA[3] - 0.74, step)
    X, Z = np.meshgrid(xs, zs)
    return np.stack([X.ravel(), Z.ravel()], axis=1)


def eye_probe():
    """9 точек для быстрого поиска: центр, середины сторон и углы (внутри пола)."""
    pts = []
    for x in (-6.75, 0.0, 6.75):
        for z in (-8.25, 0.0, 8.25):
            pts.append((x, z))
    return np.array(pts)


AZ = np.arange(0, 360, 1.0)


def coverage(boxes, eyes, alphas_deg=ALPHAS_DEG, rmax=RANGE_MAX):
    """bool[P, D]: луч из точки P по азимуту D закрыт на всех угловых высотах alphas."""
    boxes = [bx for bx in boxes if bx[5] > EYE]          # ниже глаз лучи с α>=0 не перекрывает
    if not boxes:
        return np.zeros((len(eyes), len(AZ)), bool)
    b = np.array([bx[:6] for bx in boxes], float)          # B x 6
    az = np.radians(AZ)
    dx, dz = np.sin(az), np.cos(az)                          # D
    ex, ez = eyes[:, 0][:, None, None], eyes[:, 1][:, None, None]
    DX, DZ = dx[None, :, None], dz[None, :, None]
    bx0, bz0, bx1, bz1 = (b[:, i][None, None, :] for i in range(4))
    np.seterr(divide="ignore", invalid="ignore")
    if True:
        tx0 = (bx0 - ex) / DX
        tx1 = (bx1 - ex) / DX
        tz0 = (bz0 - ez) / DZ
        tz1 = (bz1 - ez) / DZ
    inx = (ex >= bx0) & (ex <= bx1)
    inz = (ez >= bz0) & (ez <= bz1)
    tminx = np.where(np.abs(DX) < 1e-12, np.where(inx, -np.inf, np.inf), np.minimum(tx0, tx1))
    tmaxx = np.where(np.abs(DX) < 1e-12, np.where(inx, np.inf, -np.inf), np.maximum(tx0, tx1))
    tminz = np.where(np.abs(DZ) < 1e-12, np.where(inz, -np.inf, np.inf), np.minimum(tz0, tz1))
    tmaxz = np.where(np.abs(DZ) < 1e-12, np.where(inz, np.inf, -np.inf), np.maximum(tz0, tz1))
    t0 = np.maximum(np.maximum(tminx, tminz), 0.0)
    t1 = np.minimum(np.minimum(tmaxx, tmaxz), rmax)
    hit = t1 > t0                                           # P x D x B
    y0 = b[:, 4][None, None, :]
    y1 = b[:, 5][None, None, :]
    covered_all = np.ones(hit.shape[:2], bool)
    for a in np.radians(alphas_deg):
        ta = math.tan(a)
        ya = EYE + t0 * ta
        yb = EYE + t1 * ta
        blk = hit & (np.maximum(ya, y0) <= np.minimum(yb, y1))
        covered_all &= blk.any(axis=2)
    return covered_all


SECTORS = [("N", 337.5, 22.5), ("NE", 22.5, 67.5), ("E", 67.5, 112.5), ("SE", 112.5, 157.5),
           ("S", 157.5, 202.5), ("SW", 202.5, 247.5), ("Восток 45–135", 45, 135), ("Юг 135–225", 135, 225),
           ("Восток+юг 45–225", 45, 225), ("СВ+В+Ю 20–225", 20, 225)]


def sector_mask(a0, a1):
    if a0 <= a1:
        return (AZ >= a0) & (AZ < a1)
    return (AZ >= a0) | (AZ < a1)


def sector_stats(cov):
    """cov bool[P,D] -> {сектор: (среднее по точкам, из центра?, худшая точка)}."""
    res = {}
    for name, a0, a1 in SECTORS:
        m = sector_mask(a0, a1)
        per_pt = cov[:, m].mean(axis=1)
        res[name] = dict(mean=float(per_pt.mean()), worst=float(per_pt.min()), best=float(per_pt.max()))
    return res


def runs_uncovered(cov_row, a0, a1):
    """Непрерывные непокрытые интервалы азимута внутри [a0,a1) для одной точки."""
    idx = [int(a) for a in AZ if a0 <= a < a1]
    runs, cur = [], None
    for a in idx:
        if not cov_row[a]:
            cur = [a, a] if cur is None else [cur[0], a]
        else:
            if cur is not None:
                runs.append((cur[0], cur[1] + 1))
                cur = None
    if cur is not None:
        runs.append((cur[0], cur[1] + 1))
    return runs


def rect_gap(a, b):
    """Минимальное расстояние между AABB (x0,z0,x1,z1); <0 — пересечение (глубина)."""
    dx = max(a[0] - b[2], b[0] - a[2])
    dz = max(a[1] - b[3], b[1] - a[3])
    if dx < 0 and dz < 0:
        return max(dx, dz)
    return math.hypot(max(dx, 0), max(dz, 0))


def rect_point_dist(r, x, z):
    dx = max(r[0] - x, 0, x - r[2])
    dz = max(r[1] - z, 0, z - r[3])
    return math.hypot(dx, dz)


def azimuth(x, z):
    return (math.degrees(math.atan2(x, z)) + 360) % 360


# Восточная сторона навеса (уточнение пользователя 2026-10-11): стойки делят длинную сторону
# z[-7.48;7.48] на пролёты ~3.74 м; закрытые пролёты — «рабочие стены» ~2.1 м у X 7.75..8.9.
BAY_COUNT = 4
BAY_WALL = (7.75, 8.9, 2.1)


def east_bays():
    edges = np.linspace(-TENT_Z, TENT_Z, BAY_COUNT + 1)
    # нумерация с севера: 1 — северный пролёт
    return [(i + 1, float(edges[BAY_COUNT - i - 1]), float(edges[BAY_COUNT - i])) for i in range(BAY_COUNT)]


def bay_walls(open_bays):
    x0, x1, h = BAY_WALL
    return [(x0, z0, x1, z1, 0.0, h, f"WorkWall_bay{i}") for i, z0, z1 in east_bays() if i not in open_bays]
