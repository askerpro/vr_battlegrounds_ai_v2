"""Эксперимент: цветовая сегментация фотографии карты №4, не генератор карт.

Исходное фото не изменяется. Маски/контуры — диагностические результаты анализа.
Запуск из корня проекта; требуется bundled Python с numpy/Pillow.
"""
from pathlib import Path
import json
from collections import deque

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = Path('Temp/LevelDesign/Reference04Trace')
OUT.mkdir(parents=True, exist_ok=True)
W, H = 948, 775
CORNERS = [(241, 229), (1036, 251), (995, 875), (198, 832)]


def homography():
    a, b = [], []
    for (u, v), (x, y) in zip([(0, 0), (1, 0), (1, 1), (0, 1)], CORNERS):
        a.extend([[u, v, 1, 0, 0, 0, -x*u, -x*v],
                  [0, 0, 0, u, v, 1, -y*u, -y*v]])
        b.extend([x, y])
    h = np.linalg.solve(a, b)
    return (h[0]/W, h[1]/H, h[2], h[3]/W, h[4]/H, h[5], h[6]/W, h[7]/H)


def components(mask):
    # 8-связность; маска достаточно мала для явного обхода без дополнительных пакетов.
    pending = mask.copy()
    result = []
    for y, x in zip(*np.nonzero(mask)):
        if not pending[y, x]:
            continue
        pending[y, x] = False
        q, points = deque([(x, y)]), []
        while q:
            px, py = q.popleft()
            points.append((px, py))
            for ny in range(max(0, py-1), min(H, py+2)):
                for nx in range(max(0, px-1), min(W, px+2)):
                    if pending[ny, nx]:
                        pending[ny, nx] = False
                        q.append((nx, ny))
        if len(points) >= 65:
            result.append(np.asarray(points))
    return result


def hull(points):
    points = sorted(set(map(tuple, points)))
    def cross(o, a, b):
        return (a[0]-o[0])*(b[1]-o[1])-(a[1]-o[1])*(b[0]-o[0])
    lower, upper = [], []
    for p in points:
        while len(lower) >= 2 and cross(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper) >= 2 and cross(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return np.asarray(lower[:-1]+upper[:-1], dtype=float)


def fitted_rect(points):
    # Минимальный ориентированный прямоугольник по граням выпуклой оболочки.
    best = None
    boundary = hull(points)
    for d in np.roll(boundary, -1, axis=0)-boundary:
        norm = np.linalg.norm(d)
        if norm < 1:
            continue
        axis = d/norm
        basis = np.array([axis, [-axis[1], axis[0]]]).T
        p = points @ basis
        lo, hi = p.min(axis=0), p.max(axis=0)
        size = hi-lo+1
        candidate = (float(np.prod(size)), basis, lo, hi, size)
        if best is None or candidate[0] < best[0]:
            best = candidate
    _, basis, lo, hi, size = best
    k = int(size.argmax())
    axis = basis[:, k]
    center = ((lo+hi)/2) @ basis.T
    angle = float(np.degrees(np.arctan2(axis[0], axis[1])))
    return center, max(size)/50, min(size)/50, angle, boundary


def main():
    source = Image.open('Assets/Art/Reference04/Reference04_Source.jpg').convert('RGB')
    image = source.transform((W, H), Image.Transform.PERSPECTIVE, homography(),
                             Image.Resampling.BICUBIC)
    image.save(OUT/'rectified.png')
    hsv = np.asarray(image.convert('HSV')).astype(int)
    hue, sat, val = [hsv[:, :, i] for i in range(3)]
    warm = (hue < 40) & (sat > 22) & (val > 95)
    neutral = (sat < 55) & (val > 115)
    seed = warm | neutral
    # Базы и внешний декор не входят в реконструкцию игровых укрытий.
    seed[:, :110] = False
    seed[:, 847:] = False
    seed[:12] = False
    seed[-12:] = False
    binary = Image.fromarray((seed*255).astype('uint8'))
    binary = binary.filter(ImageFilter.MedianFilter(3))
    binary = binary.filter(ImageFilter.MaxFilter(3)).filter(ImageFilter.MinFilter(3))
    mask = np.asarray(binary) > 0
    labels = components(mask)
    preview = image.copy()
    draw = ImageDraw.Draw(preview)
    accepted = np.zeros((H, W), dtype=bool)
    proposals = []
    for pts in labels:
        lo, hi = pts.min(axis=0), pts.max(axis=0)
        width, depth = (hi-lo+1)/50
        if max(width, depth) < .40 or len(pts) < 100:
            continue
        center, length, thickness, angle, boundary = fitted_rect(pts)
        ext = [thickness, length]
        fill = len(pts)/((hi[0]-lo[0]+1)*(hi[1]-lo[1]+1))
        aspect = float(ext[1]/max(ext[0], .01))
        kind = 'wall' if aspect > 2.4 and ext[0] < .65 else 'crate'
        confidence = 'review' if fill < .35 or max(width, depth) > 3 else 'candidate'
        ident = len(proposals)+1
        accepted[pts[:, 1], pts[:, 0]] = True
        row = dict(id=ident, kind=kind, x=round(float(center[1]/H*15.5-7.75), 3),
                   z=round(float(center[0]/W*18.96-9.48), 3), yaw=round(angle, 2),
                   length=round(float(ext[1]), 3), thickness=round(float(ext[0]), 3),
                   area=round(len(pts)/2500, 3), fill=round(float(fill), 3),
                   status=confidence, pixels=len(pts), bbox=[int(n) for n in [*lo, *hi]])
        proposals.append(row)
        draw.rectangle([tuple(lo), tuple(hi)], outline='red' if confidence=='review' else 'cyan', width=2)
        draw.text(tuple(lo), str(ident), fill='black', stroke_width=1, stroke_fill='white')
    preview.save(OUT/'detections.png')
    # Объекты чёрные, фон белый — именно маска, а не отредактированное художественное фото.
    Image.fromarray(np.where(accepted, 0, 255).astype('uint8')).save(OUT/'binary.png')
    (OUT/'placements.json').write_text(json.dumps(dict(experimental=True,
        sourceCorners=CORNERS, objects=proposals), ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(dict(objects=len(proposals), walls=sum(p['kind']=='wall' for p in proposals),
                         review=sum(p['status']=='review' for p in proposals)), ensure_ascii=False))

    # Второй проход: сплошные прямоугольные крыши, длинные стенки и круглые крышки.
    # Контуры крупных составных групп не заменяются одним гигантским блоком.
    final = []
    mask_draw = Image.new('L', (W, H), 255)
    painter = ImageDraw.Draw(mask_draw)
    marked = image.copy()
    marker = ImageDraw.Draw(marked)

    def add(points, kind, confidence):
        center, length, thickness, yaw, boundary = fitted_rect(points)
        if center[1] > H-28:
            return
        if kind == 'can':
            length = thickness = (length+thickness)/2
        if min(length, thickness) < .08 or max(length, thickness) < .4:
            return
        # Никаких автоматических помещений поверх составной группы.
        if kind != 'wall' and max(length, thickness) > 2.2:
            return
        poly = [tuple(p) for p in boundary]
        painter.polygon(poly, fill=0)
        marker.line(poly+[poly[0]], fill='cyan' if confidence=='candidate' else 'orange', width=2)
        ident = len(final)+1
        marker.text(tuple(center), str(ident), fill='white', stroke_width=1, stroke_fill='black')
        final.append(dict(id=ident, kind=kind, x=round(float(center[1]/50-7.75), 3),
                          z=round(float(center[0]/50-9.48), 3), yaw=round(yaw, 2),
                          length=round(float(length), 3), thickness=round(float(thickness), 3),
                          status=confidence))

    # В крупных компонентах ищем длинные стенки по насыщенному коричневому верху.
    brown = (hue < 37) & (hue > 9) & (sat > 65) & (val > 70) & (val < 220)
    brown &= mask
    for pts in components(brown):
        if len(pts) < 150:
            continue
        center, length, thickness, yaw, boundary = fitted_rect(pts)
        if length/max(thickness, .01) > 3 and length > .7 and thickness < .65:
            add(pts, 'wall', 'candidate')

    # Круглые крышки распознаются отдельно: красные бочки и светлые круглые элементы.
    round_seed = ((hue < 10) & (sat > 100) & (val > 120)) | ((sat < 70) & (val > 145))
    round_seed &= mask
    round_centers = []
    for pts in components(round_seed):
        if len(pts) < 180:
            continue
        center, length, thickness, yaw, boundary = fitted_rect(pts)
        if not (.35 < thickness < 1.15 and length/thickness < 1.35):
            continue
        area = abs(np.sum(boundary[:, 0]*np.roll(boundary[:, 1], -1)-boundary[:, 1]*np.roll(boundary[:, 0], -1)))/2
        perimeter = np.linalg.norm(np.roll(boundary, -1, axis=0)-boundary, axis=1).sum()
        circularity = 4*np.pi*area/max(perimeter*perimeter, 1)
        if circularity > .86:
            add(pts, 'can', 'candidate')
            round_centers.append(center)

    for pts in labels:
        center, length, thickness, yaw, boundary = fitted_rect(pts)
        is_wall = length/max(thickness, .01) > 2.8 and thickness < .55
        if (length > 2.2 and not is_wall) or thickness > 1.5 or max(length, thickness) < .4:
            continue
        if any(np.linalg.norm(center-c) < 26 for c in round_centers):
            continue
        if any(np.linalg.norm(center-np.array([(r['z']+9.48)*50, (r['x']+7.75)*50])) < 25 for r in final):
            continue
        kind = 'wall' if length/max(thickness, .01) > 2.8 and thickness < .55 else 'crate'
        # Отсеиваем микродетали/растения, рамки пустых комнат и крестовины внутри ящиков.
        if kind=='crate' and (length < .6 or thickness < .35):
            continue
        add(pts, kind, 'review' if len(pts)/(length*thickness*2500) < .35 else 'candidate')

    # Тёмные полые цилиндры: голосование окружности по перепаду яркости кольца.
    gray = np.asarray(image.convert('L')).astype(float)
    edge = np.hypot(np.gradient(gray, axis=0), np.gradient(gray, axis=1)) > 22
    scores = np.zeros((H, W))
    radii = np.zeros((H, W))
    for radius in [14, 16, 18, 20, 22, 24, 26]:
        vote = np.zeros((H, W))
        light = np.zeros((H, W))
        for angle in np.linspace(0, 2*np.pi, 40, endpoint=False):
            dx, dy = int(round(radius*np.cos(angle))), int(round(radius*np.sin(angle)))
            vote += np.roll(edge, (-dy, -dx), axis=(0, 1))
            light += np.roll(gray, (-dy, -dx), axis=(0, 1))
        score = vote/40
        score[(gray > 80) | (sat > 120) | (light/40-gray < 35)] = 0
        better = score > scores
        scores[better] = score[better]
        radii[better] = radius
    scores[:, :120] = 0
    scores[:, 850:] = 0
    scores[:30] = 0
    scores[-35:] = 0
    for _ in range(12):
        index = np.unravel_index(np.argmax(scores), scores.shape)
        y, x = index
        score, radius = scores[index], radii[index]
        if score < .55:
            break
        scores[max(0,y-35):min(H,y+36), max(0,x-35):min(W,x+36)] = 0
        outer = [(int(round(x+radius*1.4*np.cos(a))), int(round(y+radius*1.4*np.sin(a))))
                 for a in np.linspace(0, 2*np.pi, 40, endpoint=False)]
        green = sum(38 < hue[oy, ox] < 95 and sat[oy, ox] > 45
                    for ox, oy in outer if 0 <= ox < W and 0 <= oy < H)
        if green < 12:
            # Чёрные прорези в ящиках/паллетах не являются самостоятельными бочками.
            continue
        if any(np.linalg.norm(np.array([x,y])-np.array([(r['z']+9.48)*50,(r['x']+7.75)*50])) < 30 for r in final):
            continue
        angles = np.linspace(0, 2*np.pi, 48, endpoint=False)
        points = np.array([x+radius*np.cos(angles), y+radius*np.sin(angles)]).T
        add(points, 'can', 'review')

    mask_draw.save(OUT/'fitted_binary.png')
    marked.save(OUT/'fitted_detections.png')
    (OUT/'fitted_placements.json').write_text(json.dumps(dict(experimental=True, objects=final), indent=2), encoding='utf-8')
    print(json.dumps(dict(fitted=len(final), counts={k:sum(r['kind']==k for r in final) for k in ['wall','crate','can']})))


if __name__ == '__main__':
    main()
