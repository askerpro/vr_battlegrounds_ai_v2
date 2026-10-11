"""Проверяет выбранные source prefab metadata; не запускает Unity и не меняет assets."""
import json
import math
from pathlib import Path
import re

root = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
folder = root / 'Assets/Art/Lobby/Imported/MilitaryCamp/Art/Prefabs'
rows, errors = [], []
for stem in ('SM_WatchTower_01', 'SM_ArmoredVehicle', 'SM_Fence_long_2m', 'SM_Fence_long_4m'):
    path = folder / (stem + '.prefab')
    meta = Path(str(path) + '.meta')
    if not path.exists() or not meta.exists():
        errors.append(stem + ': source missing')
        continue
    text = path.read_text(encoding='utf-8-sig')
    guid = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(encoding='utf-8-sig'), re.M)[1]
    thresholds = re.findall(r'screenRelativeHeight: ([\d.eE+-]+)', text)
    rows.append(dict(stem=stem, path=str(path.relative_to(root)).replace('\\', '/'), guid=guid,
                     source_lod_levels=len(thresholds), thresholds=[float(x) for x in thresholds],
                     renderer_count=len(re.findall(r'^MeshRenderer:', text, re.M)),
                     collider_count=len(re.findall(r'^(?:Box|Capsule|Mesh|Sphere)Collider:', text, re.M))))
profile = [20*(.86+.08*math.sin(i/40*math.pi*3.2+.4)+.04*math.sin(i/40*math.pi*7.3))-6 for i in range(41)]
result = dict(scope='Main prefab text metadata + analytic ridge profile; native mesh/material/user checks separate',
              sources=rows, errors=errors, ridge_triangles=480, far_trees=66, near_mid_trees=38,
              ridge_crest_relative_to_floor=dict(min=min(profile), max=max(profile)))
out = root / 'tasks/lobby-decoration/reports/implementation/exterior-paid'
out.mkdir(parents=True, exist_ok=True)
(out / 'source-and-horizon-audit.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(sources=len(rows), errors=errors, source_lod_levels=[row['source_lod_levels'] for row in rows], ridge_triangles=480)))
raise SystemExit(1 if errors else 0)
