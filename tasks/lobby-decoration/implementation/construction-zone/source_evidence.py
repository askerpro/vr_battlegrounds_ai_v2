"""Проверяет выбранные prefab-зависимости без запуска Unity и изменения исходников."""
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parents[4]
builder = root / 'Assets/Editor/VR_Battlegrounds/Maps/LobbyDecoration/LobbyConstructionZoneBuilder.cs'
text = builder.read_text(encoding='utf-8')
required = re.findall(r'"(SM_[^"]+)"', text.split('public static void Build')[0])
rows = []
for stem in required:
    matches = list((root / 'Assets/Art/Lobby/Imported/MilitaryCamp').rglob(stem + '.prefab'))
    if len(matches) != 1:
        raise RuntimeError(f'{stem}: expected one source, found {len(matches)}')
    path = matches[0]
    yaml = path.read_text(encoding='utf-8')
    guid = re.search(r'^guid: (\w+)', Path(str(path)+'.meta').read_text(), re.M).group(1)
    rows.append(dict(stem=stem, path=path.relative_to(root).as_posix(), guid=guid,
                     lod_levels=len(re.findall(r'screenRelativeHeight:', yaml)),
                     source_colliders=len(re.findall(r'^--- !u!(?:64|65|135|136|143|146|154) ', yaml, re.M))))
report = root / 'tasks/lobby-decoration/reports/implementation/construction-zone'
report.mkdir(parents=True, exist_ok=True)
(report / 'source-evidence.json').write_text(json.dumps(dict(sources=rows, scope='Read-only prefab metadata; geometry/material appearance requires native validation'), indent=2), encoding='utf-8')
print(f'{len(rows)}/{len(required)} exact prefab sources available; metadata only')
