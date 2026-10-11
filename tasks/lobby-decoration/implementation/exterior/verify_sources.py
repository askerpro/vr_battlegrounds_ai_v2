"""Проверка переноса dependencies и GUID, не игровой тест."""
import hashlib
import json
from pathlib import Path
import re

root = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
report = root / 'tasks/lobby-decoration/reports/implementation/exterior'
rows = json.loads((report / 'selected-assets.json').read_text(encoding='utf-8'))
guids = {}
for directory in (root / 'Assets', root / 'Library/PackageCache'):
    for meta in directory.rglob('*.meta'):
        match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(encoding='utf-8-sig', errors='replace'), re.M)
        if match:
            guids.setdefault(match[1], []).append(str(meta))
errors = []
dependencies = []
for row in rows:
    target = root / row['target']
    payload = hashlib.sha256(target.read_bytes()).hexdigest()
    if payload != row['source_sha256']:
        errors.append('Payload mismatch: ' + row['target'])
    if len(guids.get(row['guid'], [])) != 1:
        errors.append('GUID collision/missing: ' + row['guid'])
    if target.suffix.lower() in ('.png', '.tga'):
        limits = re.findall(r'maxTextureSize: (\d+)', Path(str(target) + '.meta').read_text(encoding='utf-8'))
        if not limits or any(int(value) > 1024 for value in limits):
            errors.append('Texture import size: ' + row['target'])
    if target.suffix.lower() in ('.prefab', '.mat'):
        for guid in set(re.findall(r'guid: ([0-9a-f]{32})', target.read_text(encoding='utf-8-sig'))):
            if guid != '0' * 32 and not guids.get(guid):
                errors.append('Unresolved dependency: ' + guid)
            dependencies.append(dict(owner=row['target'], guid=guid, resolution=guids.get(guid, [])))
result = dict(passed=not errors, asset_count=len(rows), errors=errors, dependencies=dependencies,
              scope='Offline payload SHA, selected GUID uniqueness, prefab/material GUID dependency resolution and texture import <=1024; no Unity import/preview')
(report / 'source-integrity.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({key: value for key, value in result.items() if key != 'dependencies'}, ensure_ascii=False))
raise SystemExit(0 if not errors else 1)
