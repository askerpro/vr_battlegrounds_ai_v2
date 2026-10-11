"""Чтение raw FBX geometry без Unity: размер/полигоны, не нативный визуальный preview."""
from pathlib import Path
import struct, zlib, json

base = Path('F:/UnityProjects/EnvironmentPackCatalog/Assets/env_packs')
sources = [base/'HauntedFarmHouse/URP/Art/StaticMeshes/Props/General/Misc/SM_Mug_01.fbx'] + [
    base/f'RuralTown/URP/Art/Meshes/Props/SM_Chair_{n}.fbx' for n in ('01','02','03')]
records = []
for src in sources:
    raw = src.read_bytes()
    if not raw.startswith(b'Kaydara FBX Binary'): raise RuntimeError('Не binary FBX: '+str(src))
    version = struct.unpack_from('<I',raw,23)[0]
    wide = version >= 7500
    header = '<QQQB' if wide else '<IIIB'
    header_size = struct.calcsize(header)
    def node(pos):
        end,count,size,length = struct.unpack_from(header, raw, pos)
        if not end: return None, pos+header_size
        pos += header_size
        name = raw[pos:pos+length].decode(); pos += length
        props=[]
        for _ in range(count):
            kind = chr(raw[pos]); pos += 1
            if kind in 'YCLFDI':
                fmt = {'Y':'h','C':'?','L':'q','F':'f','D':'d','I':'i'}[kind]
                props.append(struct.unpack_from('<'+fmt,raw,pos)[0]); pos += struct.calcsize(fmt)
            elif kind in 'S R'.replace(' ',''):
                length = struct.unpack_from('<I',raw,pos)[0]; pos += 4
                value=raw[pos:pos+length];pos += length
                props.append(value.decode(errors='replace') if kind=='S' else value)
            elif kind in 'fdilb':
                count,encoding,length = struct.unpack_from('<III',raw,pos);pos += 12
                block=raw[pos:pos+length];pos += length
                if encoding: block=zlib.decompress(block)
                fmt={'f':'f','d':'d','i':'i','l':'q','b':'?'}[kind]
                props.append(struct.unpack('<'+fmt*count,block))
            else: raise RuntimeError('FBX property '+kind)
        children=[]
        while pos < end-header_size:
            child,pos=node(pos)
            if child: children.append(child)
        return (name,props,children),end
    nodes=[]; pos=27
    while pos<len(raw)-header_size:
        value,pos=node(pos)
        if value is None: break
        nodes.append(value)
    def walk(values):
        for value in values:
            yield value
            yield from walk(value[2])
    meshes=[]
    for name,props,children in walk(nodes):
        if name!='Geometry' or len(props)<3 or props[2]!='Mesh': continue
        values={n:p for n,p,c in children}
        if 'Vertices' not in values: continue
        v=values['Vertices'][0]
        xyz=[v[a::3] for a in range(3)]
        polygons=values.get('PolygonVertexIndex',[()])[0]
        meshes.append({'name':props[1], 'vertices':len(v)//3,'polygons':sum(i<0 for i in polygons),
                       'raw_min':[min(a) for a in xyz], 'raw_max':[max(a) for a in xyz]})
    units=[p for n,p,c in walk(nodes) if n=='P' and p and p[0]=='UnitScaleFactor']
    records.append({'source':str(src),'version':version,'bytes':len(raw),'unit_property':units,'geometry':meshes})
report=Path(__file__).resolve().parents[2]/'reports/implementation/interior/fbx-source-inspect.json'
report.parent.mkdir(parents=True,exist_ok=True)
report.write_text(json.dumps(records,indent=2),encoding='utf-8')
for r in records: print(Path(r['source']).name, 'units',r['unit_property'],'mesh',[(m['name'],m['vertices'],m['polygons'],m['raw_min'],m['raw_max']) for m in r['geometry']])
