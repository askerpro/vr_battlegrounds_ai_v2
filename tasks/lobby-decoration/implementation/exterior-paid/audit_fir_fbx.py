"""Ограниченный read-only FBX binary audit material connections/LOD topology."""
import json
from pathlib import Path
import struct
import sys
import zlib

root = Path('F:/UnityProjects/Vr_Battlegrounds_ai')
path = Path(sys.argv[1]) if len(sys.argv)>1 else root / 'Assets/Art/Lobby/Imported/Catalog/RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir/Meshes/SM_SilverFir_M_01.fbx'
data = path.read_bytes()
version = struct.unpack_from('<I', data, 23)[0]
assert version < 7500


def prop(offset, node_name):
    kind = chr(data[offset]); offset += 1
    scalar = {'Y': ('h',2), 'C': ('?',1), 'I': ('i',4), 'F': ('f',4), 'D': ('d',8), 'L': ('q',8)}
    if kind in scalar:
        fmt, size = scalar[kind]
        return struct.unpack_from('<'+fmt, data, offset)[0], offset+size
    if kind in 'SR':
        size = struct.unpack_from('<I', data, offset)[0]; offset += 4
        value = data[offset:offset+size]
        return value.decode('utf-8', errors='replace') if kind=='S' else None, offset+size
    if kind in 'fdlibc':
        count, encoding, size = struct.unpack_from('<III', data, offset); offset += 12
        blob = data[offset:offset+size]
        if node_name in ('Materials','PolygonVertexIndex','Vertices'):
            if encoding: blob = zlib.decompress(blob)
            fmt = {'i':'i','d':'d','f':'f'}[kind]
            value = struct.unpack('<' + fmt*count, blob)
        else: value = dict(array_type=kind, count=count)
        return value, offset+size
    raise ValueError(kind)


def node(offset):
    end, count, prop_size, name_size = struct.unpack_from('<IIIB', data, offset)
    if not end: return None, offset+13
    name = data[offset+13:offset+13+name_size].decode('utf-8')
    cursor = offset+13+name_size
    properties = []
    for _ in range(count):
        value, cursor = prop(cursor, name)
        properties.append(value)
    children = []
    while cursor < end-13:
        child, cursor = node(cursor)
        if child is None: break
        children.append(child)
    return dict(name=name, props=properties, children=children), end


nodes = []
cursor = 27
while cursor < len(data)-13:
    item, cursor = node(cursor)
    if item is None: break
    nodes.append(item)
objects = next(item for item in nodes if item['name']=='Objects')['children']
connections = next(item for item in nodes if item['name']=='Connections')['children']
by_id = {item['props'][0]: item for item in objects if item['props']}
materials = {key: item['props'][1] for key,item in by_id.items() if item['name']=='Material'}
rows = []
for key, model in by_id.items():
    if model['name']!='Model' or len(model['props'])<3 or model['props'][2]!='Mesh': continue
    linked = [item['props'][1] for item in connections if len(item['props'])>=3 and item['props'][0]=='OO' and item['props'][2]==key]
    slots = [materials[value] for value in linked if value in materials]
    geometries = [by_id[value] for value in linked if value in by_id and by_id[value]['name']=='Geometry']
    topology = []
    for geometry in geometries:
        indices = next(child for child in geometry['children'] if child['name']=='PolygonVertexIndex')['props'][0]
        polygons = sum(index < 0 for index in indices)
        layers = [child for child in geometry['children'] if child['name']=='LayerElementMaterial']
        counts = {}
        for layer in layers:
            values = next(child for child in layer['children'] if child['name']=='Materials')['props'][0]
            counts = {str(value):values.count(value) for value in set(values)}
        positions = next(child for child in geometry['children'] if child['name']=='Vertices')['props'][0]
        topology.append(dict(polygons=polygons, index_count=len(indices), all_triangles=len(indices)==polygons*3, material_polygon_counts=counts,
                             raw_geometry_min=[min(positions[i::3]) for i in range(3)], raw_geometry_max=[max(positions[i::3]) for i in range(3)]))
    rows.append(dict(model=model['props'][1], material_slots=slots, geometry=topology))
output = root / 'tasks/lobby-decoration/reports/implementation/exterior-paid'
output.mkdir(parents=True, exist_ok=True)
name = 'fir-variant-'+path.stem+'-audit.json' if len(sys.argv)>1 else 'fir-fbx-slot-audit.json'
(output / name).write_text(json.dumps(dict(source=str(path), version=version, models=rows, scope='Source binary FBX only, not imported Renderer slots/UV validation'), ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(source=path.stem, models=[dict(name=row['model'].split('\x00')[0], slots=[slot.split('\x00')[0] for slot in row['material_slots']], triangles=sum(geo['polygons'] for geo in row['geometry'])) for row in rows]), ensure_ascii=False))
