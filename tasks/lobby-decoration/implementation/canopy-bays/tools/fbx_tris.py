"""Офлайн-подсчёт треугольников и габарита Geometry в бинарных FBX (без Unity).
Использование: python -I fbx_tris.py <out.json> <fbx-or-dir> [...]"""
import json, os, struct, sys, zlib

def read_props(buf, pos, count):
    props = []
    for _ in range(count):
        t = chr(buf[pos]); pos += 1
        if t in 'YCIFDL':
            size = {'Y': 2, 'C': 1, 'I': 4, 'F': 4, 'D': 8, 'L': 8}[t]
            fmt = {'Y': '<h', 'C': '<?', 'I': '<i', 'F': '<f', 'D': '<d', 'L': '<q'}[t]
            props.append(struct.unpack_from(fmt, buf, pos)[0]); pos += size
        elif t in 'fdlib':
            n, enc, clen = struct.unpack_from('<III', buf, pos); pos += 12
            data = buf[pos:pos + clen]; pos += clen
            if enc == 1: data = zlib.decompress(data)
            code = {'f': 'f', 'd': 'd', 'l': 'q', 'i': 'i', 'b': '?'}[t]
            props.append(struct.unpack('<%d%s' % (n, code), data[:n * struct.calcsize(code)]))
        elif t in 'SR':
            n = struct.unpack_from('<I', buf, pos)[0]; pos += 4
            props.append(bytes(buf[pos:pos + n])); pos += n
        else:
            raise ValueError('prop type ' + t)
    return props

def walk(buf, pos, wide, out, path):
    while True:
        if wide:
            end, nprops, plen = struct.unpack_from('<QQQ', buf, pos); pos += 24
        else:
            end, nprops, plen = struct.unpack_from('<III', buf, pos); pos += 12
        nlen = buf[pos]; pos += 1
        if end == 0: return pos
        name = buf[pos:pos + nlen].decode('ascii', 'replace'); pos += nlen
        props = read_props(buf, pos, nprops); pos += plen
        node = {'name': name, 'props': props, 'children': []}
        if pos < end:
            walk(buf, pos, wide, node['children'], path + [name])
        out.append(node); pos = end

def geometries(fbx):
    buf = open(fbx, 'rb').read()
    if not buf.startswith(b'Kaydara FBX Binary'): return None
    version = struct.unpack_from('<I', buf, 23)[0]
    root = []; walk(buf, 27, version >= 7500, root, [])
    result = []
    for top in root:
        if top['name'] != 'Objects': continue
        for g in top['children']:
            if g['name'] != 'Geometry': continue
            label = g['props'][1].split(b'\x00')[0].decode('utf-8', 'replace') if len(g['props']) > 1 else '?'
            idx = next((c['props'][0] for c in g['children'] if c['name'] == 'PolygonVertexIndex'), ())
            verts = next((c['props'][0] for c in g['children'] if c['name'] == 'Vertices'), ())
            tris = 0; n = 0
            for v in idx:
                n += 1
                if v < 0: tris += n - 2; n = 0
            size = None
            if verts:
                xs, ys, zs = verts[0::3], verts[1::3], verts[2::3]
                size = [round(max(a) - min(a), 3) for a in (xs, ys, zs)]
            result.append({'geometry': label, 'triangles': tris, 'rawSize': size})
    return result

def main():
    out = sys.argv[1]; report = {}
    for arg in sys.argv[2:]:
        files = [arg] if os.path.isfile(arg) else [os.path.join(arg, f) for f in sorted(os.listdir(arg)) if f.lower().endswith('.fbx')]
        for f in files:
            try: report[os.path.basename(f)] = geometries(f)
            except Exception as e: report[os.path.basename(f)] = {'error': str(e)}
    json.dump(report, open(out, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print('files', len(report), '->', out)

if __name__ == '__main__': main()
