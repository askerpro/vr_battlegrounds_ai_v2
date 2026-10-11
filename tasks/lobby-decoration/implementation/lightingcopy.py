"""Выбранные опоры освещения: внешние источники только читаются, существующие игровые копии сохраняются."""
from pathlib import Path
import argparse, hashlib, json, re, shutil, uuid

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--apply', action='store_true', help='Копировать замыкание пяти выбранных моделей')
parser.add_argument('--batch', choices=('first', 'more'), default='first', help='Партия источников, выбранная арт-руководителем')
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
packs = Path('F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs')
owned = root / 'Assets/Art/Lobby/Imported/Catalog'
selected = [packs / ('RuralTown/URP/Art/Prefabs/' + n + '.prefab') for n in ('SM_LampPost', 'SM_TopLight')]
selected += [packs / ('PostApocalypticTown/URP/Art/Prefabs/' + n + '.prefab') for n in ('SM_Light1', 'SM_Light2', 'SM_Light3')]
if args.batch == 'more':
    selected = [packs / ('RuralTown/URP/Art/Prefabs/' + n + '.prefab') for n in ('SM_Lamp', 'SM_Lamp_02', 'SM_Electric_Pole')]
    selected += [packs / 'PostApocalypticTown/URP/Art/Prefabs/SM_Lamp.prefab']
    selected += [packs / ('AbandonedFactory/Art/Prefabs/' + n + '.prefab') for n in ('SM_CoverLight_01', 'SM_Lamp_01a')]
guid_pattern = re.compile(r'^guid: ([0-9a-f]{32})', re.M)
refs_pattern = re.compile(r'(?<![0-9a-f])([0-9a-f]{32})(?![0-9a-f])')
explicit_ref_pattern = re.compile(r'(?:guid:\s*|["\\]*guid["\\]*\s*:\s*["\\]*)([0-9a-f]{32})')

def guid(meta):
    match = guid_pattern.search(meta.read_text(encoding='utf-8-sig', errors='replace'))
    if not match:
        raise RuntimeError('Нет GUID: ' + str(meta))
    return match.group(1)

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

index = {}
for pack in ('RuralTown', 'PostApocalypticTown', 'AbandonedFactory'):
    for meta in (packs / pack).rglob('*.meta'):
        source = Path(str(meta)[:-5])
        if source.is_file():
            key = guid(meta)
            if key in index and digest(index[key]) != digest(source):
                raise RuntimeError('Коллизия исходников: ' + key)
            index[key] = source
existing, unparsed_existing = {}, []
for meta in (root / 'Assets').rglob('*.meta'):
    source = Path(str(meta)[:-5])
    if source.is_file():
        match = guid_pattern.search(meta.read_text(encoding='utf-8-sig', errors='replace'))
        if match:
            existing.setdefault(match.group(1), []).append(source)
        elif meta.is_relative_to(owned):
            raise RuntimeError('Повреждённая собственная meta: ' + str(meta))
        else:
            # Не исправляем посторонние SDK/license meta; фиксируем предел глобального индекса.
            unparsed_existing.append(str(meta.relative_to(root)))
seen, external, records = set(), set(), []
# Ранее адаптированные материалы переиспользуем только по записанному происхождению.
provenance_path = root / 'tasks/lobby-decoration/reports/implementation/interior/real-source-manifest.json'
provenance = json.loads(provenance_path.read_text(encoding='utf-8'))['files']

def proven_adaptation(source, current, key):
    if source.suffix != '.mat' or not current.is_relative_to(root / 'Assets/Art/Lobby/Interior/Imported'):
        return False
    return any(r.get('guid') == key and Path(r.get('source', '')) == source
               and root / r.get('destination', '') == current
               and r.get('sha256', '').lower() == digest(source).lower()
               and r.get('adaptation', {}).get('adapter') == 'URP/Lit'
               for r in provenance)

def folders(path):
    if not path.is_relative_to(owned):
        raise RuntimeError('Папка вне выбранного Imported: ' + str(path))
    path.mkdir(parents=True, exist_ok=True)
    for folder in (path, *path.parents):
        if folder == root / 'Assets':
            break
        meta = Path(str(folder) + '.meta')
        if not meta.exists():
            meta.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')

def visit(source):
    if source in seen:
        return
    seen.add(source)
    if not source.is_file():
        raise RuntimeError('Отсутствует источник: ' + str(source))
    if source.read_bytes()[:80].startswith(b'version https://git-lfs.github.com/spec'):
        raise RuntimeError('Нематериализованный LFS: ' + str(source))
    key = guid(Path(str(source) + '.meta'))
    target = owned / source.relative_to(packs)
    matches = existing.get(key, [])
    if len(matches) > 1:
        raise RuntimeError('Несколько игровых владельцев GUID: ' + key)
    record = {'source': str(source), 'path': str(target.relative_to(root)), 'guid': key, 'sourceSha256': digest(source)}
    if matches:
        current = matches[0]
        adapted = digest(current) != digest(source)
        if current != target and adapted and not proven_adaptation(source, current, key):
            raise RuntimeError('GUID-коллизия с игровым владельцем: ' + str(current))
        record.update(reused=True, reusePath=str(current.relative_to(root)), ownedAdaptation=adapted,
                      actualSha256=digest(current))
    else:
        record['reused'] = False
        existing[key] = [target]
    records.append(record)
    if source.suffix.lower() in ('.prefab', '.mat', '.asset', '.shadergraph', '.shadersubgraph', '.shader', '.hlsl'):
        text = source.read_text(encoding='utf-8-sig', errors='replace')
        explicit = set(explicit_ref_pattern.findall(text))
        for ref in refs_pattern.findall(text):
            if ref in index:
                visit(index[ref])
            elif ref in explicit and ref not in existing and ref != '0' * 32:
                external.add(ref)
    # Importer externalObjects тоже могут задавать материалы FBX; исходная meta сохраняется.
    for ref in explicit_ref_pattern.findall(Path(str(source) + '.meta').read_text(encoding='utf-8-sig', errors='replace')):
        if ref == key:
            continue
        if ref in index:
            visit(index[ref])
        elif ref not in existing and ref != '0' * 32:
            external.add(ref)

for source in selected:
    visit(source)
# Копирование начинается только после проверки полного замыкания.
if args.apply:
    for record in records:
        if record['reused']:
            continue
        source, target = Path(record['source']), root / record['path']
        if target.exists() or Path(str(target) + '.meta').exists():
            raise RuntimeError('Новый путь уже занят: ' + str(target))
        folders(target.parent)
        shutil.copyfile(source, target)
        shutil.copyfile(Path(str(source) + '.meta'), Path(str(target) + '.meta'))
report = {'applied': args.apply, 'roots': [str(p) for p in selected], 'files': records, 'externalGuids': sorted(external), 'collisions': 0, 'unparsedExistingMetas': unparsed_existing}
if args.apply:
    out = root / ('tasks/lobby-decoration/reports/implementation/lighting-candidates-copy' + ('-more' if args.batch == 'more' else '') + '.json')
    out.write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps({'applied': args.apply, 'files': len(records), 'new': sum(not r['reused'] for r in records), 'externalCount': len(external), 'externalExamples': sorted(external)[:10], 'unparsedExistingMetaCount': len(unparsed_existing), 'collisions': 0}))
