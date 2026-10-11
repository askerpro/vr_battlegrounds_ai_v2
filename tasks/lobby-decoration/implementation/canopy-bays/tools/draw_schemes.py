"""Схемы пролётов навеса (план сверху + фасад востока изнутри) по раскладке черновика LobbyCanopyBaysBuilder.
Габариты — измерены в Unity 2026-10-11 (reports/implementation/canopy-bays/prefab-measurements.txt),
позиции повторяют расчёт builder'а (до запуска в Unity это проектные, а не фактические значения).
Использование: python -I draw_schemes.py <out-dir>"""
import os, sys

OUT = sys.argv[1]

# (имя, xmin, xmax, zmin, zmax, высота, класс)
EAST = [
    ("SupplyLocker SM_ClothesCabinet", 7.70, 8.44, 3.87, 5.29, 2.37, "closed"),
    ("FieldTable SM_Table + кейс/коробки", 7.70, 8.57, 5.35, 6.75, 1.17, "closed"),
    ("BackStack поддон+WoodCrate_big_02+MilitaryCrate_02", 8.63, 9.89, 5.42, 6.68, 1.98, "back"),
    ("GasCorner PropaneTank_02+FuelCanister", 7.84, 8.42, 6.82, 7.14, 1.00, "closed"),
    ("CrateTower WoodCrate_big_01+MilitaryCrate_02", 7.70, 9.76, -5.18, -3.87, 2.14, "closed"),
    ("SteelCabinet SM_cabinet_a + ящик/кейс", 7.70, 8.44, -6.71, -5.24, 1.52, "closed"),
    ("BackStack поддон+TarpCrate_01+MilitaryCrate_02", 8.50, 9.71, -6.60, -5.33, 1.84, "back"),
    ("GasCorner PropaneTank_02+FuelCanister", 7.84, 8.42, -7.09, -6.77, 1.00, "closed"),
    ("EdgeFill SandBarrier_04+06", 7.80, 8.25, 3.13, 3.81, 0.40, "fill"),
    ("EdgeFill SandBarrier_04+06", 7.80, 8.25, -3.81, -3.13, 0.40, "fill"),
]
POSTS_NEW = [3.74, 0.0, -3.74]
POSTS_CORNER = [7.40, -7.40]
TIED = [(3.40, 3.64), (0.10, 0.34), (-0.34, -0.10), (-3.64, -3.40)]
SANDBAGS_KEEP = [(-9.20, -7.63), (-3.07, -1.52), (-1.54, 0.01), (-0.01, 1.54), (1.52, 3.07), (7.63, 9.20)]
SANDBAGS_REMOVE = [(-7.66, -6.11), (-6.13, -4.58), (-4.60, -3.05), (3.05, 4.60), (4.58, 6.13), (6.11, 7.66)]
LAMPS_E = [(4.49, 6.73), (-6.73, -4.49)]
EXISTING = [  # (имя, xmin, xmax, zmin, zmax)
    ("восточные полки", 6.95, 7.61, 4.19, 5.51), ("восточный стол", 6.94, 7.62, 5.70, 6.80),
    ("Table_North", -7.75, 7.75, 7.02, 7.48), ("Table_South", -7.75, 7.75, -7.48, -7.02),
    ("Table_West", -7.75, -7.29, 5.10, 7.02), ("Table_West", -7.75, -7.29, -7.02, -6.55),
    ("шкаф одежды", -7.58, -6.98, -5.32, -4.22), ("полевой стол", -7.62, -6.94, -6.48, -5.38),
    ("кейсы", -7.30, -6.68, 5.15, 7.00),
]
WEST = [
    ("WallCableSouth y3.95", -7.75, -7.72, -7.25, -4.25), ("WallCableNorth y3.95", -7.75, -7.72, 3.65, 7.25),
    ("JunctionBox", -7.75, -7.63, -7.05, -6.75), ("JunctionBox", -7.75, -7.63, 6.43, 6.67),
    ("ToolBoard y1.27–1.84", -7.75, -7.73, -6.42, -5.44),
    ("LockerTop crate", -7.57, -6.98, -5.16, -4.37),
]
PENDANTS = [(-7.00, 6.30), (-7.05, -5.95)]
DRAPES = [(sx, sz) for sx in (-1, 1) for sz in (-1, 1)]
COLUMNS = [(2.95, 3.38), (2.95, -3.38), (-2.95, 3.38), (-2.95, -3.38)]

COL = {"closed": "#8a6d3b", "back": "#5c4a2a", "fill": "#b5a06a", "west": "#4a6b8a", "exist": "#cfcfcf"}


def svg(w, h, body):
    return ('<svg xmlns="http://www.w3.org/2000/svg" width="%d" height="%d" font-family="Segoe UI, Arial" font-size="11">'
            '<rect width="100%%" height="100%%" fill="#fbfbf8"/>%s</svg>' % (w, h, body))


def top_view():
    S, ox, oz = 34.0, 380.0, 360.0   # 1 м = 34 px; X вправо (восток), Z вверх (север)
    X = lambda x: ox + x * S
    Z = lambda z: oz - z * S
    b = []
    rect = lambda x0, x1, z0, z1, fill, op=1, stroke="#333", dash="": b.append(
        '<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s" fill-opacity="%.2f" stroke="%s" stroke-width="0.8" %s/>'
        % (X(x0), Z(z1), (x1 - x0) * S, (z1 - z0) * S, fill, op, stroke, ('stroke-dasharray="%s"' % dash) if dash else ""))
    text = lambda x, z, t, anchor="start", color="#222", size=11: b.append(
        '<text x="%.1f" y="%.1f" text-anchor="%s" fill="%s" font-size="%d">%s</text>' % (X(x), Z(z), anchor, color, size, t))
    rect(-7.75, 7.75, -9.48, 9.48, "#eeeeea", 1, "#999")                      # физический пол
    rect(-7.75, 7.75, -7.48, 7.48, "none", 1, "#6b8e23", "6,3")               # навес
    text(-7.6, 9.2, "физический пол X±7.75, Z±9.48; навес Z±7.48 (пунктир)", size=10)
    rect(7.0, 10.5, 0.40, 3.35, "#7fbf7f", .25, "#2e7d32", "4,2"); text(8.9, 1.9, "пролёт 2 открыт", "middle", "#2e7d32")
    rect(7.0, 10.5, -3.35, -0.40, "#7fbf7f", .25, "#2e7d32", "4,2"); text(8.9, -1.9, "пролёт 3 открыт", "middle", "#2e7d32")
    rect(7.65, 10.2, 3.83, 6.97, "none", 1, "#8a6d3b", "2,2"); rect(7.80, 10.2, 6.97, 7.30, "none", 1, "#8a6d3b", "2,2"); text(10.25, 5.6, "пролёт 1 закрыт", color="#8a6d3b")
    rect(7.65, 10.2, -6.97, -3.83, "none", 1, "#8a6d3b", "2,2"); rect(7.80, 10.2, -7.30, -6.97, "none", 1, "#8a6d3b", "2,2"); text(10.25, -5.6, "пролёт 4 закрыт", color="#8a6d3b")
    rect(-7.75, -6.0, 3.5, 5.2, "#9ad0ff", .35, "#1e88e5", "4,2"); text(-5.9, 4.3, "пол перед зеркалом свободен", color="#1e88e5")
    rect(-7.67, -6.09, -4.13, 3.55, "#ffcc80", .5, "#e65100"); text(-5.95, -0.3, "станция арсенала (+подъём до 4.05)", color="#e65100")
    rect(-7.55, -7.31, 3.57, 5.08, "#90caf9", 1, "#1565c0")
    for x, z in COLUMNS:
        rect(x - .21, x + .21, z - .21, z + .21, "#616161", 1)
    text(0, -0.2, "колонны X±2.95 Z±3.38 (физические)", "middle", "#616161", 10)
    for n, x0, x1, z0, z1 in EXISTING:
        rect(x0, x1, z0, z1, COL["exist"], 1, "#9e9e9e")
    for z0, z1 in SANDBAGS_KEEP:
        rect(7.80, 8.20, z0, z1, "#d7ccc8", 1, "#8d6e63")
    for z0, z1 in SANDBAGS_REMOVE:
        rect(7.80, 8.20, z0, z1, "none", 1, "#d32f2f", "3,2")
    text(8.3, -8.9, "красный пунктир — сегменты EastLowBaseBoundary, Build удаляет сам (6 шт.)", color="#d32f2f", size=10)
    for n, x0, x1, z0, z1, h, cls in EAST:
        rect(x0, x1, z0, z1, COL[cls], .85)
        text(x1 + .05, (z0 + z1) / 2 - .1, "%s, h %.2f" % (n.split(" ")[0], h), size=9)
    for z in POSTS_NEW:
        rect(7.59, 7.75, z - .08, z + .08, "#212121", 1)
    for z in POSTS_CORNER:
        rect(7.59, 7.75, z - .08, z + .08, "#757575", 1); rect(-7.75, -7.59, z - .08, z + .08, "#757575", 1)
    for z0, z1 in TIED:
        rect(7.655, 7.845, z0, z1, "#9e9d24", 1)
    for z0, z1 in LAMPS_E:
        rect(7.14, 7.46, z0, z1, "#fff176", .9, "#f9a825")
    for n, x0, x1, z0, z1 in WEST:
        rect(x0, x1, z0, z1, COL["west"], .9)
    for x, z in PENDANTS:
        b.append('<circle cx="%.1f" cy="%.1f" r="5" fill="#fff176" stroke="#f9a825"/>' % (X(x), Z(z)))
    for sx, sz in DRAPES:
        rect(min(sx * 7.01, sx * 7.495), max(sx * 7.01, sx * 7.495), sz * 7.40 - .05, sz * 7.40 + .05, "#9e9d24", 1)
    for sx in (-1, 1):
        for sz in (-1, 1):
            b.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#9e9d24" stroke-width="3" opacity=".6"/>'
                     % (X(sx * .2), Z(sz * 7.38), X(sx * 7.45), Z(sz * 7.38)))
    b.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#9e9d24" stroke-width="3" opacity=".6"/>' % (X(7.69), Z(7.30), X(7.69), Z(-7.30)))
    text(-7.6, 10.3, "↑ север: стрельбище (Z>7.5 — ничего выше 0.5 м)", size=12)
    text(-7.6, -10.0, "План навеса сверху — черновик LobbyCanopyBaysBuilder (2026-10-11)", size=13)
    leg = [("закрытый пролёт, передний ряд", COL["closed"]), ("задний штабель за кромкой", COL["back"]), ("стык мешков", COL["fill"]),
           ("западная детализация", COL["west"]), ("штора/валик", "#9e9d24"), ("плафон (сокет света)", "#fff176"), ("существующий декор", COL["exist"])]
    for i, (t, c) in enumerate(leg):
        b.append('<rect x="%d" y="%d" width="12" height="12" fill="%s" stroke="#333"/>' % (860, 30 + i * 16, c))
        b.append('<text x="%d" y="%d">%s</text>' % (878, 40 + i * 16, t))
    return svg(1080, 740, "".join(b))


def facade():
    # Вид изнутри навеса на восток: север слева (Z +7.6), юг справа; Y вверх.
    S, ox, oy = 52.0, 420.0, 300.0
    Zp = lambda z: ox - z * S
    Y = lambda y: oy - y * S
    b = []
    rect = lambda z0, z1, y0, y1, fill, op=1, stroke="#333", dash="": b.append(
        '<rect x="%.1f" y="%.1f" width="%.1f" height="%.1f" fill="%s" fill-opacity="%.2f" stroke="%s" stroke-width="0.8" %s/>'
        % (Zp(z1), Y(y1), (z1 - z0) * S, (y1 - y0) * S, fill, op, stroke, ('stroke-dasharray="%s"' % dash) if dash else ""))
    text = lambda z, y, t, anchor="start", color="#222", size=11: b.append(
        '<text x="%.1f" y="%.1f" text-anchor="%s" fill="%s" font-size="%d">%s</text>' % (Zp(z), Y(y), anchor, color, size, t))
    b.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#555"/>' % (Zp(7.9), Y(0), Zp(-7.9), Y(0)))
    rect(-7.48, 7.48, 4.05, 4.19, "#795548", 1)                        # карнизная балка/кромка кровли
    text(7.45, 4.35, "кромка кровли y 4.19, балка 4.05", size=10)
    rect(-3.35, 3.35, 0.5, 3.80, "#7fbf7f", .18, "#2e7d32", "4,2")
    text(1.9, 2.2, "пролёт 2 открыт", "middle", "#2e7d32"); text(-1.9, 2.2, "пролёт 3 открыт", "middle", "#2e7d32")
    for z0, z1 in SANDBAGS_KEEP:
        if abs(z0) < 7.5:
            rect(z0, z1, 0, .45, "#d7ccc8", 1, "#8d6e63")
    for n, x0, x1, z0, z1, h, cls in sorted(EAST, key=lambda e: -e[1]):
        rect(z0, z1, 0, h, COL[cls], .9 if cls != "back" else .6, "#333", "3,2" if cls == "back" else "")
    for n, x0, x1, z0, z1, h, cls in EAST:
        if cls in ("closed", "back"):
            text((z0 + z1) / 2, h + .08 if cls == "closed" else h - .2, n.split(" ")[0], "middle", "#222", 9)
    for z in POSTS_NEW + POSTS_CORNER:
        rect(z - .08, z + .08, 0, 4.19, "#212121" if z in POSTS_NEW else "#757575", 1)
    for z0, z1 in TIED:
        rect(z0, z1, .50, 3.80, "#9e9d24", .85); rect(z0 - .005, z1 + .005, 1.23, 1.31, "#212121", 1)
    rect(-7.30, 7.30, 3.83, 4.03, "#9e9d24", .9)
    text(0, 3.6, "валики у карниза по всем пролётам (y 3.83–4.03); подвязанные шторы у стоек открытых пролётов", "middle", "#555", 10)
    for z0, z1 in LAMPS_E:
        rect(z0, z1, 3.28, 3.55, "#fff176", 1, "#f9a825")
        for dz in (-.85, .85):
            zc = (z0 + z1) / 2 + dz
            b.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#555"/>' % (Zp(zc), Y(3.55), Zp(zc), Y(4.27)))
        text((z0 + z1) / 2, 3.12, "плафон SM_Lamp_long + сокет", "middle", "#f57f17", 9)
    for y, t in ((2.2, "2.2"), (2.0, "2.0")):
        b.append('<line x1="%.1f" y1="%.1f" x2="%.1f" y2="%.1f" stroke="#d32f2f" stroke-dasharray="2,3"/>' % (Zp(7.6), Y(y), Zp(-7.6), Y(y)))
        text(-7.65, y - .05, t + " м", color="#d32f2f", size=9)
    text(7.6, -.35, "← север (стрельбище)", size=11); text(-7.6, -.35, "юг (лагерь) →", "end", size=11)
    text(7.6, -.7, "Фасад востока изнутри навеса (X≥7.6); пунктир — задний штабель за кромкой пола", size=12)
    return svg(860, 360, "".join(b))


os.makedirs(OUT, exist_ok=True)
for name, content in (("canopy-bays-top.svg", top_view()), ("canopy-bays-east-facade.svg", facade())):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(content)
    print(os.path.join(OUT, name))
