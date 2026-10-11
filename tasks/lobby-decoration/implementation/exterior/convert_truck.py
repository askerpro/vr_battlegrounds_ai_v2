"""Ограниченный offline GLB→OBJ конвертер: только supply truck, без исполнения общего helper."""
from pathlib import Path
import hashlib
import json
import math
import struct
import uuid

ROOT = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
SOURCE = ROOT / 'tasks/lobby-decoration/reports/acquired-assets/supply-truck.glb'
OUT = ROOT / 'Assets/Art/Lobby/Exterior/Imported/Truck'
REPORT = ROOT / 'tasks/lobby-decoration/reports/implementation/exterior'


def meta(path, importer='DefaultImporter'):
    target = Path(str(path) + '.meta')
    if not target.exists():
        target.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex + '\n' +
                          ('folderAsset: yes\n' if path.is_dir() else '') + importer +
                          ':\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n', encoding='utf-8')


def mul(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def matrix(node):
    if 'matrix' in node:
        return [[node['matrix'][j * 4 + i] for j in range(4)] for i in range(4)]
    x, y, z, w = node.get('rotation', [0, 0, 0, 1])
    scale, t = node.get('scale', [1, 1, 1]), node.get('translation', [0, 0, 0])
    result = [[1 - 2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w), t[0]],
              [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w), t[1]],
              [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y), t[2]], [0, 0, 0, 1]]
    for i in range(3):
        for j in range(3):
            result[i][j] *= scale[j]
    return result


raw = SOURCE.read_bytes()
assert hashlib.sha256(raw).hexdigest() == '042ce7bc8dabe9af4dc3eb4a17f3b3627653dd7e2efe70ee3aa65672a1dd2ea2'
magic, version, total = struct.unpack_from('<III', raw)
assert magic == 0x46546c67 and version == 2 and total == len(raw)
offset = 12
gltf, binary = None, b''
while offset < len(raw):
    length, kind = struct.unpack_from('<II', raw, offset)
    data = raw[offset + 8:offset + 8 + length]
    offset += length + 8
    if kind == 0x4e4f534a:
        gltf = json.loads(data)
    elif kind == 0x004e4942:
        binary = data


def accessor(index):
    entry = gltf['accessors'][index]
    assert 'sparse' not in entry
    view = gltf['bufferViews'][entry['bufferView']]
    assert view.get('buffer', 0) == 0
    ct = entry['componentType']
    fmt, width = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}[ct]
    count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[entry['type']]
    start = view.get('byteOffset', 0) + entry.get('byteOffset', 0)
    stride = view.get('byteStride', count * width)
    result = []
    for i in range(entry['count']):
        value = struct.unpack_from('<' + fmt * count, binary, start + i * stride)
        if entry.get('normalized', False):
            maximum = {5120: 127, 5121: 255, 5122: 32767, 5123: 65535, 5125: 4294967295}[ct]
            value = tuple(max(-1, v / maximum) for v in value)
        result.append(value)
    return result


OUT.mkdir(exist_ok=True)
meta(OUT)
lines = ['# CC0 3D Assets supply truck; glTF transforms applied; right-handed OBJ', 'mtllib SupplyTruck.mtl']
vertices, faces, face_materials = [], [], []
materials = gltf.get('materials', [])


def visit(index, parent):
    node = gltf['nodes'][index]
    world = mul(parent, matrix(node))
    if 'mesh' in node:
        for p, primitive in enumerate(gltf['meshes'][node['mesh']]['primitives']):
            assert primitive.get('mode', 4) == 4
            base = len(vertices)
            for vertex in accessor(primitive['attributes']['POSITION']):
                transformed = [sum(world[i][j] * vertex[j] for j in range(3)) + world[i][3] for i in range(3)]
                assert all(math.isfinite(value) for value in transformed)
                vertices.append(transformed)
                lines.append('v ' + ' '.join(f'{value:.9g}' for value in transformed))
            mat = primitive.get('material', 0)
            lines.extend([f'g node_{index}_primitive_{p}', f'usemtl truck_{mat}_{materials[mat]["name"]}'])
            indices = accessor(primitive['indices']) if 'indices' in primitive else [(i,) for i in range(len(vertices)-base)]
            det = world[0][0]*(world[1][1]*world[2][2]-world[1][2]*world[2][1])-world[0][1]*(world[1][0]*world[2][2]-world[1][2]*world[2][0])+world[0][2]*(world[1][0]*world[2][1]-world[1][1]*world[2][0])
            assert len(indices) % 3 == 0
            for i in range(0, len(indices), 3):
                face = [base + indices[i+j][0] for j in range(3)]
                assert all(base <= v < len(vertices) for v in face)
                if det < 0:
                    face.reverse()
                faces.append(face)
                face_materials.append(mat)
                lines.append('f ' + ' '.join(str(v + 1) for v in face))
    for child in node.get('children', []):
        visit(child, world)


identity = [[float(i == j) for j in range(4)] for i in range(4)]
for node in gltf['scenes'][gltf.get('scene', 0)]['nodes']:
    visit(node, identity)
obj = OUT / 'SupplyTruck.obj'
if obj.exists():
    raise RuntimeError('Do not overwrite existing conversion without owner review')
obj.write_text('\n'.join(lines) + '\n', encoding='utf-8')
meta(obj, 'ModelImporter')
mtl = OUT / 'SupplyTruck.mtl'
mtl_lines = []
for i, material in enumerate(materials):
    color = material['pbrMetallicRoughness']['baseColorFactor']
    mtl_lines.extend([f'newmtl truck_{i}_{material["name"]}', 'Kd ' + ' '.join(str(v) for v in color[:3]), 'd 1', 'illum 2'])
mtl.write_text('\n'.join(mtl_lines) + '\n', encoding='utf-8')
meta(mtl)
report = dict(source='https://3dassets.dev/assets/armoured-column-and-field-camp-supply-truck-a0414d94', license='CC0 1.0',
              source_sha256=hashlib.sha256(raw).hexdigest(), obj=str(obj.relative_to(ROOT)),
              obj_sha256=hashlib.sha256(obj.read_bytes()).hexdigest(), triangles=len(faces), vertices=len(vertices),
              material_groups=len(materials), materials=materials,
              min=[min(v[i] for v in vertices) for i in range(3)], max=[max(v[i] for v in vertices) for i in range(3)],
              scope='Offline conversion/preview; root quality review and native preview pending')
(REPORT / 'truck-conversion.json').write_text(json.dumps(report, indent=2), encoding='utf-8')

# Геометрический preview конвертированного объекта; это не Unity-снимок.
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d.art3d import Poly3DCollection
fig = plt.figure(figsize=(10, 7), facecolor='#c3c4be')
ax = fig.add_subplot(111, projection='3d', facecolor='#c3c4be')
polys = [[(vertices[v][0], vertices[v][2], vertices[v][1]) for v in face] for face in faces]
colors = [materials[index]['pbrMetallicRoughness']['baseColorFactor'][:3] for index in face_materials]
ax.add_collection3d(Poly3DCollection(polys, facecolors=colors, edgecolors='#333333', linewidths=.08))
bounds = report['min'], report['max']
ax.set_xlim(bounds[0][0], bounds[1][0]); ax.set_ylim(bounds[0][2], bounds[1][2]); ax.set_zlim(bounds[0][1], bounds[1][1])
ax.set_box_aspect([bounds[1][0]-bounds[0][0], bounds[1][2]-bounds[0][2], bounds[1][1]-bounds[0][1]])
ax.view_init(elev=18, azim=-128)
ax.set_axis_off()
ax.set_title('Supply truck — converted geometry candidate (offline, not Unity)', fontsize=12)
fig.savefig(REPORT / 'truck-geometry-preview.png', dpi=130, bbox_inches='tight')
print(json.dumps({key: value for key, value in report.items() if key != 'materials'}))
