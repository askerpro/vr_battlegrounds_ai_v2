"""Дополнение кэша urp-selected префабами MilitaryCamp (URP) с полным замыканием GUID.

Тот же способ, что extract_selected.py: замыкание по текстовым asset/asset.meta из
urp-source-cache, родительские папки, потоковое извлечение из .unitypackage.
В отличие от него, manifest.json не пересобирается заново, а дополняется:
существующие записи сохраняются, roots и external_guids_to_resolve объединяются.
В Assets/ ничего не пишет.
"""
import hashlib
import json
import re
import shutil
import sys
import tarfile
from pathlib import Path, PurePosixPath

TASK = Path(__file__).resolve().parents[2]
BASE = TASK / 'reports/asset-evaluation/military-base-megapack-372662'
CACHE = BASE / 'urp-source-cache'
OUT = BASE / 'urp-selected'
REPORTS = TASK / 'reports/asset-extract'

NEW_ROOTS = [
    'SM_Border_2m',
    'SM_Border_90d',
    'SM_Building_roof_1x2_02',
    'SM_Building_roof_2x4_01',
    'SM_Building_wall_1m_A_2',
    'SM_Building_wall_corner_90d_A_2',
    'SM_Building_wall_entrance_2m_B',
    'SM_Building_wall_window_2m_A_2',
    'SM_Building_wall_window_2m_B_2',
    'SM_Cornice_1m_A',
    'SM_Cornice_2m_A',
    'SM_Cornice_3m_A',
    'SM_Cornice_90d_A',
    'SM_Fence_1m',
    'SM_Fence_90d',
    'SM_Tire_02',
]
VERIFY_ROOTS = []

index = json.loads((CACHE / 'index.json').read_text(encoding='utf-8'))
source = Path(index['source'])
if source.stat().st_size != index['source_bytes'] or source.stat().st_mtime_ns != index['source_mtime_ns']:
    raise RuntimeError('Source package changed since indexing')
assets = {item['guid']: item for item in index['assets'] if item.get('path')}
by_name = {PurePosixPath(item['path']).name: guid for guid, item in assets.items()}
root_files = [name + '.prefab' for name in NEW_ROOTS + VERIFY_ROOTS]
absent = [name for name in root_files if name not in by_name]
if absent:
    raise RuntimeError(f'Roots not found in index: {absent}')


def closure(start):
    selected, missing, pending = set(start), set(), list(start)
    while pending:
        guid = pending.pop()
        for name in ('asset', 'asset.meta'):
            path = CACHE / guid / name
            if not path.exists():
                continue
            text = path.read_text(encoding='utf-8-sig', errors='replace')
            refs = set(re.findall(r'(?i)guid\s*[:=]\s*([0-9a-f]{32})', text))
            refs.update(re.findall(r'(?i)"guid"\s*:\s*"([0-9a-f]{32})"', text))
            refs.update(c for c in re.findall(r'(?i)[0-9a-f]{32}', text) if c in assets)
            for ref in refs:
                if ref in assets and ref not in selected:
                    selected.add(ref)
                    pending.append(ref)
                elif ref not in assets and not ref.startswith('0000000000000000'):
                    missing.add(ref)
    paths = {item['path']: guid for guid, item in assets.items()}
    for guid in list(selected):
        for parent in PurePosixPath(assets[guid]['path']).parents:
            if str(parent) in paths:
                selected.add(paths[str(parent)])
    return selected, missing


def target_for(guid):
    relative = PurePosixPath(assets[guid]['path'])
    if relative.is_absolute() or '..' in relative.parts or relative.parts[0] != 'Assets':
        raise RuntimeError('Unsafe source path')
    target = OUT.joinpath(*relative.parts)
    if not target.resolve().is_relative_to(OUT.resolve()):
        raise RuntimeError('Target outside source cache')
    return target


per_root = {}
selected, missing = set(), set()
for name in root_files:
    deps, ext = closure({by_name[name]})
    per_root[name[:-7]] = {'guids': sorted(deps), 'external': sorted(ext)}
    selected |= deps
    missing |= ext

previous = json.loads((OUT / 'manifest.json').read_text(encoding='utf-8'))
REPORTS.mkdir(parents=True, exist_ok=True)
backup = REPORTS / 'manifest.before-extract-more.json'
if not backup.exists():
    backup.write_text(json.dumps(previous, indent=2, ensure_ascii=False), encoding='utf-8')
known = {item['guid']: item for item in previous['assets']}

for guid in selected:
    target = target_for(guid)
    target.parent.mkdir(parents=True, exist_ok=True)
    meta = CACHE / guid / 'asset.meta'
    meta_target = Path(str(target) + '.meta')
    if meta.exists() and (not meta_target.exists() or meta_target.read_bytes() != meta.read_bytes()):
        shutil.copyfile(meta, meta_target)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


files = {guid for guid in selected if assets[guid].get('bytes') is not None}
done = {guid for guid in files if known.get(guid, {}).get('sha256') and target_for(guid).is_file()
        and sha(target_for(guid)) == known[guid]['sha256']}
todo = files - done
print(json.dumps({'selected': len(selected), 'files': len(files), 'alreadyExtracted': len(done), 'toExtract': len(todo)}), flush=True)
if '--dry-run' in sys.argv:
    sys.exit(0)
if todo:
    with tarfile.open(source, 'r|gz') as archive:
        for member in archive:
            parts = member.name.strip('/').split('/')
            if not member.isfile() or len(parts) != 2 or parts[0] not in todo or parts[1] != 'asset':
                continue
            with target_for(parts[0]).open('wb') as handle:
                shutil.copyfileobj(archive.extractfile(member), handle, 1024 * 1024)
            done.add(parts[0])
            if done >= files:
                break

entries = {guid: dict(item) for guid, item in known.items()}
for guid in selected:
    if guid in entries and entries[guid].get('extracted') and entries[guid].get('sha256'):
        continue
    entries[guid] = {**assets[guid], 'extracted': guid in done,
                     'sha256': sha(target_for(guid)) if guid in done else None}
manifest = {
    'source': previous['source'],
    'roots': sorted(set(previous['roots']) | set(root_files)),
    'external_guids_to_resolve': sorted(set(previous['external_guids_to_resolve']) | missing),
    'assets': [entries[guid] for guid in sorted(entries)],
}
temporary = OUT / 'manifest.json.tmp'
temporary.write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf-8')
temporary.replace(OUT / 'manifest.json')
(REPORTS / 'extract-more-closure.json').write_text(json.dumps(per_root, indent=2), encoding='utf-8')
print(json.dumps({'manifestAssets': len(manifest['assets']), 'previousAssets': len(previous['assets']),
                  'newlyExtracted': len(done & todo), 'missingFiles': sorted(assets[g]['path'] for g in files - done),
                  'external': sorted(missing)}, ensure_ascii=False), flush=True)
