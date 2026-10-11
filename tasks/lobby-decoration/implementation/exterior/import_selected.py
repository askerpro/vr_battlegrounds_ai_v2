"""Перенос только выбранных внешних assets; без Unity и перезаписи существующих GUID."""
import hashlib
import json
from pathlib import Path
import re
import shutil
import uuid

ROOT = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
SOURCE = Path('F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs')
TARGET = ROOT / 'Assets/Art/Lobby/Exterior/Imported'
REPORT = ROOT / 'tasks/lobby-decoration/reports/implementation/exterior'
SELECTED = [
    'RPG_FPS_game_assets_industrial/Oil_tanks/Oil_tank_v1/Oil_tank_v1.prefab',
    'RPG_FPS_game_assets_industrial/Oil_tanks/Oil_tank_v1/Source/Oil_tank_v1.FBX',
    'RPG_FPS_game_assets_industrial/Oil_tanks/Oil_tank_v1/Source/Oil_tank_v1.mat',
    'RPG_FPS_game_assets_industrial/Oil_tanks/Oil_tank_v1/Source/Oil_tank_v1.tga',
    'PostApocalypticTown/URP/Art/Textures/LandscapeMaterial/T_Sand1_A.png',
    'PostApocalypticTown/URP/Art/Textures/LandscapeMaterial/T_Sand1_N.png',
]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def guid(path):
    match = re.search(r'^guid: ([0-9a-f]{32})$', path.read_text(encoding='utf-8-sig'), re.M)
    if not match:
        raise ValueError(f'No GUID: {path}')
    return match[1]


def folder(path):
    if path.exists():
        return
    folder(path.parent)
    path.mkdir()
    Path(str(path) + '.meta').write_text(
        'fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')


index = {}
unreadable_meta = []
for meta in (ROOT / 'Assets').rglob('*.meta'):
    try:
        key = guid(meta)
    except ValueError:
        unreadable_meta.append(str(meta.relative_to(ROOT)).replace('\\', '/'))
        continue
    index.setdefault(key, []).append(Path(str(meta)[:-5]))
rows = []
for relative in SELECTED:
    source = SOURCE / relative
    source_meta = Path(str(source) + '.meta')
    key = guid(source_meta)
    matches = index.get(key, [])
    if len(matches) > 1:
        raise RuntimeError(f'Duplicate project GUID {key}: {matches}')
    if matches:
        target = matches[0]
        action = 'reuse'
    else:
        target = TARGET / relative
        if target.exists() or Path(str(target) + '.meta').exists():
            raise RuntimeError(f'Target collision: {target}')
        folder(target.parent)
        shutil.copy2(source, target)
        shutil.copy2(source_meta, str(target) + '.meta')
        action = 'copy'
        if source.suffix.lower() in ('.png', '.tga'):
            meta = Path(str(target) + '.meta')
            text = meta.read_text(encoding='utf-8-sig')
            text = re.sub(r'(maxTextureSize: )\d+', r'\g<1>1024', text)
            meta.write_text(text, encoding='utf-8')
        index[key] = [target]
    rows.append(dict(source=str(source), target=str(target.relative_to(ROOT)).replace('\\', '/'), guid=key,
                     action=action, sha256=digest(target), source_sha256=digest(source),
                     meta_sha256=digest(Path(str(target) + '.meta')),
                     adaptation='Texture import maxTextureSize <=1024; preserved GUID' if source.suffix.lower() in ('.png', '.tga') and action == 'copy' else 'none'))
REPORT.mkdir(parents=True, exist_ok=True)
(REPORT / 'selected-assets.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
(REPORT / 'guid-index-skipped.json').write_text(json.dumps(unreadable_meta, indent=2), encoding='utf-8')
try:
    from PIL import Image
    Image.open(SOURCE / SELECTED[3]).save(REPORT / 'oil-tank-texture-preview.png')
except ImportError:
    pass
print(json.dumps(rows, ensure_ascii=False, indent=2))
