import json
import sys
from pathlib import Path

root = Path(__file__).parent / 'visual-review'
for weapon in sys.argv[1:]:
    data = json.loads((root / weapon / 'geometry.json').read_text(encoding='utf-8'))
    print(weapon, 'M', data['shotOriginMeters'])
    for surface in data['surfaces']:
        matrix = surface['localToPrefabMeters']
        points = []
        for i, vertex in enumerate(surface['vertices']):
            v = [vertex['x'], vertex['y'], vertex['z'], 1]
            point = [sum(matrix[f'e{a}{b}'] * v[b] for b in range(4)) for a in range(3)]
            points.append((i, *point))
        low = [min(p[a] for p in points) for a in range(1, 4)]
        high = [max(p[a] for p in points) for a in range(1, 4)]
        print(surface['nodePath'], 'bounds', [round(x, 6) for x in low], [round(x, 6) for x in high])
        if any(term in surface['nodePath'] for term in ['Bolt[', '/Sight[', 'FrontSight', 'SightFrame[']) or (weapon == 'Viper' and '/Base[' in surface['nodePath']):
            candidates = sorted((p for p in points if abs(p[1]) < .0015), key=lambda p: -p[2])
            unique = []
            seen = set()
            for p in candidates:
                key = tuple(round(x, 6) for x in p[1:])
                if key not in seen:
                    seen.add(key)
                    unique.append(p)
                if len(unique) == 14:
                    break
            print('centre top vertices', [[p[0], *[round(x, 6) for x in p[1:]]] for p in unique])
            if weapon == 'Viper' and any(t in surface['nodePath'] for t in ['/Bolt[', '/Base[']):
                candidates = sorted((p for p in points if abs(p[1]) < .003 and p[3] > .08), key=lambda p: -p[2])
                print('front post vertices', [[p[0], *[round(x, 6) for x in p[1:]]] for p in candidates[:18]])
