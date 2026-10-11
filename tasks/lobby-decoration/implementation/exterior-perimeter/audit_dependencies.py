"""GUID-readiness helper; Unity AssetDatabase загрузка здесь не заявляется проверенной."""
import json
from pathlib import Path
import re

root = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
cache = root / 'tasks/lobby-decoration/reports/asset-evaluation/military-base-megapack-372662/urp-selected'
ids = {
    'Sandbag': '001dd3cdc49e4775ab15cb63cc6aa80f',
    'AmmoA': 'abc00000000012017236203265661070',
    'AmmoB': 'abc00000000003273235488023318933',
    'CrateA': 'abc00000000015158636773066772218',
    'CrateB': 'abc00000000006183764951078403713',
    'Barrel': 'abc00000000011043161318511504812',
    'Fence': 'abc00000000004669335183269123642',
}
matches = {guid: [] for guid in ids.values()}
for meta in (root / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
    if match and match[1] in matches:
        matches[match[1]].append(str(meta.relative_to(root))[:-5].replace('\\', '/'))
rows = []
cached_matches = {guid: [] for guid in ids.values()}
if cache.exists():
    for meta in cache.rglob('*.meta'):
        match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
        if match and match[1] in cached_matches:
            cached_matches[match[1]].append(str(meta.relative_to(root))[:-5].replace('\\', '/'))
for role, guid in ids.items():
    cached = cached_matches[guid]
    rows.append(dict(role=role, guid=guid, main_paths=matches[guid], selected_cache_paths=cached,
                     status='main_present' if len(matches[guid]) == 1 else 'collision' if len(matches[guid]) > 1 else 'await_shared_owner_copy'))
errors = [row['role'] + ': ' + row['status'] for row in rows if row['status'] != 'main_present']
report = dict(scope='Offline GUID/text cache existence only; no native import/preview', dependencies=rows, readiness_errors=errors)
output = root / 'tasks/lobby-decoration/reports/implementation/exterior-perimeter'
output.mkdir(parents=True, exist_ok=True)
(output / 'asset-dependencies-offline.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(dependencies=len(rows), main_present=sum(row['status']=='main_present' for row in rows), readiness_errors=errors)))
