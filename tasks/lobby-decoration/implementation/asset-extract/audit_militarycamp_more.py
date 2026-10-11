"""Офлайн-аудит новых префабов MilitaryCamp в кэше urp-selected (read-only).

Для каждого корня: LODGroup и уровни, треугольники/вершины по мешам FBX (бинарный
FBX-парсер по образцу exterior-paid/audit_fir_fbx.py), материалы, шейдеры, текстуры
(PNG IHDR + maxTextureSize из .meta), замыкание (как в extract и как в
copy_militarycamp.py), коллизии GUID с Assets/**/*.meta проекта, внешние GUID.
"""
import hashlib
import json
import re
import struct
import zlib
from pathlib import Path, PurePosixPath

PROJECT = Path(__file__).resolve().parents[4]
TASK = PROJECT / 'tasks/lobby-decoration'
CACHE = TASK / 'reports/asset-evaluation/military-base-megapack-372662/urp-selected'
REPORTS = TASK / 'reports/asset-extract'
DEST = PROJECT / 'Assets/Art/Lobby/Imported/MilitaryCamp'
PREFIX = 'Assets/Hivemind/MilitaryCamp/URP/'
closure_by_root = json.loads((REPORTS / 'extract-more-closure.json').read_text(encoding='utf-8'))
manifest = json.loads((CACHE / 'manifest.json').read_text(encoding='utf-8'))
before = json.loads((REPORTS / 'manifest.before-extract-more.json').read_text(encoding='utf-8'))
entries = {e['guid']: e for e in manifest['assets']}
extracted = {g: e for g, e in entries.items() if e.get('extracted') and e.get('sha256')}

# 1. Старые записи не изменены.
changed_old = [e['guid'] for e in before['assets'] if entries.get(e['guid']) != e]


# 2. FBX binary parser (7.x, 32/64-битные заголовки узлов).
def parse_fbx(path):
    data = path.read_bytes()
    if not data.startswith(b'Kaydara FBX Binary'):
        return {'error': 'not binary FBX'}
    version = struct.unpack_from('<I', data, 23)[0]
    wide = version >= 7500
    head = 25 if wide else 13

    def prop(offset, node_name):
        kind = chr(data[offset]); offset += 1
        scalar = {'Y': ('h', 2), 'C': ('?', 1), 'I': ('i', 4), 'F': ('f', 4), 'D': ('d', 8), 'L': ('q', 8)}
        if kind in scalar:
            fmt, size = scalar[kind]
            return struct.unpack_from('<' + fmt, data, offset)[0], offset + size
        if kind in 'SR':
            size = struct.unpack_from('<I', data, offset)[0]; offset += 4
            value = data[offset:offset + size]
            return (value.decode('utf-8', errors='replace') if kind == 'S' else None), offset + size
        if kind in 'fdlibc':
            count, encoding, size = struct.unpack_from('<III', data, offset); offset += 12
            blob = data[offset:offset + size]
            if node_name in ('PolygonVertexIndex', 'Vertices'):
                if encoding:
                    blob = zlib.decompress(blob)
                fmt = {'i': 'i', 'd': 'd', 'f': 'f', 'l': 'q'}[kind]
                return struct.unpack('<' + fmt * count, blob), offset + size
            return None, offset + size
        raise ValueError(kind)

    def node(offset):
        if wide:
            end, count, _ = struct.unpack_from('<QQQ', data, offset); name_size = data[offset + 24]
        else:
            end, count, _ = struct.unpack_from('<III', data, offset); name_size = data[offset + 12]
        if not end:
            return None, offset + head
        name = data[offset + head:offset + head + name_size].decode('utf-8')
        cursor = offset + head + name_size
        properties = []
        for _ in range(count):
            value, cursor = prop(cursor, name)
            properties.append(value)
        children = []
        while cursor < end - head:
            child, cursor = node(cursor)
            if child is None:
                break
            children.append(child)
        return dict(name=name, props=properties, children=children), end

    nodes, cursor = [], 27
    while cursor < len(data) - head:
        item, cursor = node(cursor)
        if item is None:
            break
        nodes.append(item)
    objects = next(i for i in nodes if i['name'] == 'Objects')['children']
    connections = next(i for i in nodes if i['name'] == 'Connections')['children']
    by_id = {i['props'][0]: i for i in objects if i['props']}
    models = {}
    for key, model in by_id.items():
        if model['name'] != 'Model' or len(model['props']) < 3 or model['props'][2] != 'Mesh':
            continue
        linked = [c['props'][1] for c in connections if len(c['props']) >= 3 and c['props'][0] == 'OO' and c['props'][2] == key]
        tris = verts = corners = 0
        for value in linked:
            geometry = by_id.get(value)
            if not geometry or geometry['name'] != 'Geometry':
                continue
            indices = next(c for c in geometry['children'] if c['name'] == 'PolygonVertexIndex')['props'][0]
            size = 0
            for index in indices:
                size += 1
                if index < 0:
                    tris += size - 2; size = 0
            corners += len(indices)
            verts += len(next(c for c in geometry['children'] if c['name'] == 'Vertices')['props'][0]) // 3
        models[model['props'][1].split('\x00')[0]] = {'triangles': tris, 'controlPoints': verts, 'corners': corners}
    return {'version': version, 'models': models}


def file_id_names(meta_text):
    names = dict(re.findall(r'^\s{4}(-?\d+): (.+)$', meta_text.split('externalObjects')[0], re.M))
    for first, second in re.findall(r'- first:\s*\n\s+\d+: (-?\d+)\s*\n\s+second: (.+)', meta_text):
        names[first] = second
    return names


def png_size(path):
    raw = path.read_bytes()[:24]
    if raw[:8] == b'\x89PNG\r\n\x1a\n':
        return struct.unpack('>II', raw[16:24])
    return None


def tex_info(guid):
    entry = entries[guid]
    path = CACHE / entry['path']
    meta = Path(str(path) + '.meta').read_text(encoding='utf-8')
    default = re.search(r'buildTarget: DefaultTexturePlatform\s*\n\s+maxTextureSize: (\d+)', meta)
    android = re.search(r'buildTarget: Android\s*\n\s+maxTextureSize: (\d+)(?:.*\n){1,10}?\s+overridden: (\d)', meta)
    size = png_size(path)
    return {'name': PurePosixPath(entry['path']).name, 'size': f'{size[0]}x{size[1]}' if size else '?',
            'maxSize': int(default[1]) if default else None,
            'android': (int(android[1]) if android and android[2] == '1' else None)}


fbx_cache = {}
rows = {}
for root, info in closure_by_root.items():
    guids = info['guids']
    prefab_guid = next(g for g in guids if entries[g]['path'].endswith('/' + root + '.prefab'))
    text = (CACHE / entries[prefab_guid]['path']).read_text(encoding='utf-8')
    lod_groups = text.count('\nLODGroup:')
    lod_levels = len(re.findall(r'- screenRelativeHeight:', text))
    meshes = re.findall(r'm_Mesh: \{fileID: (-?\d+), guid: ([0-9a-f]{32})', text)
    lods = {}
    for file_id, guid in meshes:
        entry = entries.get(guid)
        if not entry or not entry['path'].lower().endswith('.fbx'):
            lods.setdefault('?', []).append(guid); continue
        fbx_path = CACHE / entry['path']
        if guid not in fbx_cache:
            meta_text = Path(str(fbx_path) + '.meta').read_text(encoding='utf-8')
            fbx_cache[guid] = (parse_fbx(fbx_path), file_id_names(meta_text),
                               re.search(r'isReadable: (\d)', meta_text)[1])
        parsed, names, readable = fbx_cache[guid]
        mesh_name = names.get(file_id, f'fileID {file_id}')
        stats = parsed['models'].get(mesh_name, {})
        lods[mesh_name] = {**stats, 'fbx': PurePosixPath(entry['path']).name, 'readable': readable}
    materials = sorted({entries[g]['path'].split('/')[-1] for g in re.findall(r'\{fileID: 2100000, guid: ([0-9a-f]{32})', text) if g in entries})
    textures = [tex_info(g) for g in guids if entries[g]['path'].lower().endswith(('.png', '.tga', '.jpg', '.psd'))]
    shaders = sorted({entries[g]['path'].split('/')[-1] for g in guids if entries[g]['path'].endswith(('.shadergraph', '.shadersubgraph', '.shader'))})
    colliders = {'box': text.count('\nBoxCollider:'), 'mesh': text.count('\nMeshCollider:'),
                 'capsule': text.count('\nCapsuleCollider:'), 'sphere': text.count('\nSphereCollider:')}
    # Замыкание: все файлы извлечены и SHA совпадает.
    bad = [entries[g]['path'] for g in guids if entries[g].get('bytes') is not None and
           (g not in extracted or hashlib.sha256((CACHE / entries[g]['path']).read_bytes()).hexdigest() != entries[g]['sha256'])]
    # Замыкание в стиле copy_militarycamp.py (только тело asset, без .meta).
    pending, chosen, copy_external = [prefab_guid], set(), set()
    while pending:
        guid = pending.pop()
        if guid in chosen:
            continue
        chosen.add(guid)
        raw = (CACHE / extracted[guid]['path']).read_bytes()
        if b'\0' not in raw[:1000]:
            for dep in re.findall(rb'(?<![0-9a-f])([0-9a-f]{32})(?![0-9a-f])', raw):
                dep = dep.decode()
                if dep in extracted:
                    pending.append(dep)
                elif dep != '0' * 32:
                    copy_external.add(dep)
    lod0 = [v for k, v in lods.items() if k.endswith('LOD0')] or list(lods.values())[:1]
    rows[root] = {'prefabGuid': prefab_guid, 'lodGroups': lod_groups, 'lodLevels': lod_levels,
                  'renderers': len(meshes), 'lod0Triangles': sum(v.get('triangles', 0) for v in lod0),
                  'meshes': lods, 'materials': materials, 'shaders': shaders, 'textures': textures,
                  'colliders': colliders, 'closureFiles': len([g for g in guids if entries[g].get('bytes') is not None]),
                  'closureBad': bad, 'copyClosureFiles': len(chosen), 'copyClosureExternal': sorted(copy_external),
                  'extractExternal': info['external']}

# 3. Коллизии GUID с проектом (как в copy_militarycamp.py).
project_guids = {}
for meta in (PROJECT / 'Assets').rglob('*.meta'):
    match = re.search(r'^guid: ([0-9a-f]{32})', meta.read_text(encoding='utf-8', errors='ignore'), re.M)
    if match:
        project_guids.setdefault(match[1], []).append(str(meta)[:-5])
all_new = {g for info in closure_by_root.values() for g in info['guids']}
collisions, present = {}, {}
for guid in all_new:
    if entries[guid].get('bytes') is None:
        continue
    target = DEST / entries[guid]['path'].removeprefix(PREFIX)
    hits = project_guids.get(guid, [])
    other = [p for p in hits if Path(p) != target]
    if other:
        collisions[entries[guid]['path']] = other
    elif hits:
        present[entries[guid]['path']] = hits
for root, row in rows.items():
    row['guidCollisions'] = sum(1 for g in closure_by_root[root]['guids'] if entries[g]['path'] in collisions)
    row['alreadyInProject'] = sum(1 for g in closure_by_root[root]['guids'] if entries[g]['path'] in present)

# 4. Внешние GUID: ищем в пакетах проекта.
external = sorted({g for r in rows.values() for g in r['extractExternal'] + r['copyClosureExternal']})
resolved = {g: None for g in external}
for base in (PROJECT / 'Library/PackageCache', PROJECT / 'Packages', PROJECT / 'Assets'):
    if not base.exists():
        continue
    for meta in base.rglob('*.meta'):
        try:
            head = meta.read_text(encoding='utf-8', errors='ignore')[:200]
        except OSError:
            continue
        match = re.search(r'guid: ([0-9a-f]{32})', head)
        if match and match[1] in resolved and not resolved[match[1]]:
            resolved[match[1]] = str(meta.relative_to(PROJECT))[:-5].replace('\\', '/')

report = {'changedOldEntries': changed_old, 'manifestAssets': len(manifest['assets']),
          'guidCollisions': collisions, 'alreadyInProjectSamePath': present,
          'externalGuids': resolved, 'roots': rows}
(REPORTS / 'audit-militarycamp-more.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
summary = {root: [r['lodGroups'], r['lodLevels'], r['lod0Triangles'], len(r['materials']), len(r['textures']),
                  r['guidCollisions'], len(r['closureBad']), r['copyClosureFiles'], r['closureFiles']] for root, r in rows.items()}
print(json.dumps({'changedOld': len(changed_old), 'collisions': len(collisions), 'present': len(present),
                  'external': resolved, 'roots[lodGroups,levels,lod0Tris,mats,texs,coll,bad,copyFiles,files]': summary},
                 ensure_ascii=False, indent=0))
