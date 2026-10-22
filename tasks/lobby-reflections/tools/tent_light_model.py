"""Оценка рассеянной освещённости шатра Lobby от точечных ламп (lobby-reflections, 2026-10-22).

Модель: яркость SH по шести направлениям ≈ b + k · Σ I·att(d)/d² · (|lx|+|ly|+|lz|)/6, att — сглаживание URP
по range. b и k подбираются по замеру текущей сцены (tent-light-map.txt). Модель не видит тени и отражённый свет —
только для выбора начальных интенсивностей; итог проверяется запеканием и картой освещённости.

Запуск: python tasks/lobby-reflections/tools/tent_light_model.py
"""
import math

CURRENT = [  # (x, y, z, intensity, range): 8 ламп шатра, i=9,6 r=10
    (5.8, 3.0, -4.8, 9.6, 10), (-5.8, 3.0, -4.8, 9.6, 10), (5.8, 3.0, 4.8, 9.6, 10), (-5.8, 3.0, 4.8, 9.6, 10),
    (7.3, 3.1, 3.0, 9.6, 10), (7.3, 3.1, -3.0, 9.6, 10), (-7.0, 3.6, 6.3, 9.6, 10), (-7.05, 3.6, -5.95, 9.6, 10),
]
# Замер 2026-10-22 (тикет 1026): точка, яркость.
MEASURED = [
    ((-6.8, 1.2, -0.4), .23), ((7.3, 1.0, 3.7), .51), ((7.4, 1.0, 2.3), .58), ((7.0, 1.0, 6.2), .33),
    ((-7.5, 1.0, 5.9), .40), ((7.3, 1.0, -3.9), .30), ((-7.0, 1.0, 6.6), .35), ((-7.0, 1.0, 5.6), .46),
    ((7.3, 1.0, 6.3), .29), ((-6.6, 1.0, 7.3), .29), ((-6.6, 1.0, -7.3), .36), ((6.6, 1.0, 7.3), .27),
    ((-7.3, 1.0, -5.9), .27), ((6.6, 1.0, -7.3), .32), ((0.0, 1.0, 0.0), .13),
]


def att(d, r):
    f = max(0.0, 1 - (d / r) ** 4)
    return f * f


def raw(p, lights):
    s = 0.0
    for x, y, z, i, r in lights:
        lx, ly, lz = x - p[0], y - p[1], z - p[2]
        d = math.sqrt(lx * lx + ly * ly + lz * lz)
        s += i * att(d, r) / (d * d) * (abs(lx) + abs(ly) + abs(lz)) / d / 6
    return s


def fit():
    xs = [raw(p, CURRENT) for p, _ in MEASURED]
    ys = [v for _, v in MEASURED]
    n = len(xs)
    mx, my = sum(xs) / n, sum(ys) / n
    k = sum((a - mx) * (b - my) for a, b in zip(xs, ys)) / sum((a - mx) ** 2 for a in xs)
    return my - k * mx, k


def grid_stats(lights, b, k, y=1.2):
    vals = []
    for ix in range(-14, 15):
        for iz in range(-14, 15):
            p = (ix * .5, y, iz * .5)
            if abs(p[0]) <= 7.5 and abs(p[2]) <= 7.3:
                vals.append(b + k * raw(p, lights))
    vals.sort()
    n = len(vals)
    return vals[0], vals[n // 10], vals[n // 2], vals[n * 9 // 10], vals[-1]


def proposal(fill_i, fill_r, accent_i, accent_r):
    """Сетка 4×4 под кровлей (x ±5,7/±1,9; z ±5,6/±1,9, y 3,6) + акцент над стеной арсенала CT."""
    lights = []
    for x in (-5.7, -1.9, 1.9, 5.7):
        for z in (-5.6, -1.9, 1.9, 5.6):
            lights.append((x, 3.6, z, fill_i, fill_r))
    accent = (-6.3, 2.9, -0.4, accent_i, accent_r)
    return lights, accent


if __name__ == "__main__":
    b, k = fit()
    print("fit b=%.3f k=%.3f" % (b, k))
    for p, v in MEASURED:
        print("  %s measured %.2f model %.2f" % (p, v, b + k * raw(p, CURRENT)))
    print("current grid min/p10/median/p90/max: %.2f %.2f %.2f %.2f %.2f" % grid_stats(CURRENT, b, k))
    for fill_i in (1.5, 2.0, 2.5, 3.0):
        lights, accent = proposal(fill_i, 6.0, 6.0, 4.0)
        stats = grid_stats(lights, b, k)
        arsenal = b + k * raw((-6.8, 1.2, -0.4), lights + [accent])
        center = b + k * raw((0, 1.2, 0), lights)
        print("fill i=%.1f r=6: min %.2f p10 %.2f median %.2f p90 %.2f max %.2f | center %.2f | arsenal+accent(i=6,r=4) %.2f"
              % ((fill_i,) + stats + (center, arsenal)))
