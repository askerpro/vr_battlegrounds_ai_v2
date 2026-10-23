"""Текстура светящейся подложки слота арсенала: мягкое пятно света (RGB белый, альфа — спад).

Запуск: python -I tasks/arsenal-slot-lighting/tools/glow_texture.py <выходной .png>
Пятно вытянуто по высоте слота и смещено вверх (свет сверху-спереди), край — ноль, чтобы
аддитивный квад не давал видимой рамки.
"""
import math
import sys

from PIL import Image

W, H = 128, 256
CX, CY = 0.5, 0.42          # центр пятна (доля ширины, доля высоты сверху)
RX, RY = 0.46, 0.50         # полуоси эллипса до нуля
CORE = 0.35                 # доля радиуса с почти полной яркостью


def falloff(d):
    if d >= 1.0:
        return 0.0
    if d <= CORE:
        return 1.0
    t = (d - CORE) / (1.0 - CORE)
    return 0.5 * (1.0 + math.cos(math.pi * t))


def main(path):
    img = Image.new("RGBA", (W, H))
    px = img.load()
    for y in range(H):
        for x in range(W):
            dx = ((x + 0.5) / W - CX) / RX
            dy = ((y + 0.5) / H - CY) / RY
            a = falloff(math.hypot(dx, dy))
            px[x, y] = (255, 255, 255, round(255 * a))
    img.save(path)


if __name__ == "__main__":
    main(sys.argv[1])
