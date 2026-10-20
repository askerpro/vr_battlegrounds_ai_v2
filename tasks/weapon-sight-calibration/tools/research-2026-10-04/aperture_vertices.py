import json
from pathlib import Path
data = json.loads((Path(__file__).parent/'visual-review/SRM12/geometry.json').read_text(encoding='utf-8'))
s = next(s for s in data['surfaces'] if s['nodePath'].endswith('/Switch[9]'))
matrix = s['localToPrefabMeters']
seen = set()
points = []
for i, vertex in enumerate(s['vertices']):
    v = [vertex['x'], vertex['y'], vertex['z'], 1]
    p = tuple(sum(matrix[f'e{a}{b}'] * v[b] for b in range(4)) for a in range(3))
    if .097 < p[1] < .112 and abs(p[0]) < .007 and p not in seen:
        seen.add(p)
        points.append((i, *p))
for p in sorted(points, key=lambda p: abs(p[1]))[:28]:
    print(p[0], *[round(x, 8) for x in p[1:]])
