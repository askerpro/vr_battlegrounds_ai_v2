"""Read-only аудит cached URP YAML; пишет только отдельный отчёт exterior-review."""
from collections import Counter
import json
from pathlib import Path
import re

root = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
cache = root / 'tasks/lobby-decoration/reports/asset-evaluation/military-base-megapack-372662/urp-source-cache'
out = root / 'tasks/lobby-decoration/reports/implementation/exterior-review'
index = json.loads((cache / 'index.json').read_text(encoding='utf-8'))['assets']
paths = {row['guid']: row['path'] for row in index}
tokens = ['ArmoredVehicle', 'WatchTower', 'AmmoCase', 'AmmoCartridgeBoxes', 'MilitaryCrate', 'WoodCrate', 'Fence', 'Barrel', 'Lamp', 'AlarmLight', 'FuelCanister']
selected = [row for row in index if row['path'].endswith('.prefab') and any(token.lower() in row['path'].lower() for token in tokens)]
classes = {'1':'GameObject','4':'Transform','23':'MeshRenderer','33':'MeshFilter','54':'Rigidbody','64':'MeshCollider','65':'BoxCollider','95':'Animator','108':'Light','111':'Animation','114':'MonoBehaviour','135':'SphereCollider','136':'CapsuleCollider','137':'SkinnedMeshRenderer','205':'LODGroup','82':'AudioSource'}
rows = []
for entry in selected:
    text = (cache / entry['guid'] / 'asset').read_text(encoding='utf-8-sig')
    headers = re.findall(r'^--- !u!(\d+) &', text, re.M)
    counts = Counter(classes.get(kind, 'ClassID_' + kind) for kind in headers)
    refs = sorted(set(re.findall(r'guid: ([0-9a-f]{32})', text)))
    material_guids = [guid for guid in refs if paths.get(guid, '').endswith('.mat')]
    materials = []
    for guid in material_guids:
        data = (cache / guid / 'asset').read_text(encoding='utf-8-sig')
        shader = re.search(r'm_Shader: \{fileID: -?\d+, guid: ([0-9a-f]{32})', data)
        textures = sorted(set(re.findall(r'm_Texture: \{fileID: \d+, guid: ([0-9a-f]{32})', data)))
        materials.append(dict(guid=guid, path=paths[guid], shader_guid=shader[1] if shader else None,
                              shader_path=paths.get(shader[1], 'external/package/builtin') if shader else None,
                              textures=[dict(guid=value, path=paths.get(value, 'unresolved')) for value in textures],
                              surface=re.findall(r'- _Surface: (\S+)', data), alpha_clip=re.findall(r'- _AlphaClip: (\S+)', data),
                              keywords=re.findall(r'^  - (_\S+)$', data, re.M)))
    lods = []
    for block in re.split(r'^--- !u!', text, flags=re.M)[1:]:
        if block.startswith('205 '):
            lods.append(dict(thresholds=[float(v) for v in re.findall(r'screenRelativeHeight: ([\d.eE+-]+)', block)],
                             size=float(re.search(r'm_Size: ([\d.eE+-]+)', block)[1]),
                             reference_point=re.search(r'm_LocalReferencePoint: (.*)', block)[1]))
    scale = re.search(r'm_LocalScale: (.*)', text)
    cleanup = {kind:count for kind,count in counts.items() if kind not in ('GameObject','Transform','MeshRenderer','MeshFilter','LODGroup')}
    rows.append(dict(guid=entry['guid'], path=entry['path'], root_scale=scale[1] if scale else None,
                     components=dict(counts), cleanup_components=cleanup, lods=lods,
                     renderer_names=re.findall(r'm_Name: (.*LOD\d.*)', text),
                     mesh_dependencies=[dict(guid=guid,path=paths.get(guid)) for guid in refs if paths.get(guid, '').lower().endswith(('.fbx','.obj','.asset'))],
                     materials=materials, unresolved_prefab_dependencies=[guid for guid in refs if guid not in paths and guid!='0'*32],
                     scope='Serialized URP metadata only; no mesh triangles/bounds/material import/visual validation'))
out.mkdir(parents=True, exist_ok=True)
(out / 'militarycamp-prefab-audit.json').write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
for row in rows:
    name = Path(row['path']).name
    if '_base.' in name or '_top.' in name or 'Fence_' in name and name not in ('SM_Fence_2m.prefab','SM_Fence_long_4m.prefab','SM_fence_corner.prefab') or '_1.prefab' in name and 'Boxes' in name:
        continue
    print(name, row['guid'], 'LOD', [len(v['thresholds']) for v in row['lods']], 'cleanup', row['cleanup_components'], 'materials', [Path(m['path']).name for m in row['materials']])
print('Audited', len(rows), 'prefabs; no Unity operations or asset writes')
