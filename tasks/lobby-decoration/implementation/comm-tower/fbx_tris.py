# -*- coding: utf-8 -*-
"""Офлайн-подсчёт треугольников и габаритов по моделям binary FBX (по мотивам
exterior-paid/audit_fir_fbx.py, но пишет только в reports/implementation/comm-tower).
Использование: python -X utf8 fbx_tris.py <file.fbx> [...]
"""
import json, os, struct, sys, zlib

ROOT = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(ROOT, "../../reports/implementation/comm-tower/fbx-tris.json"))


def parse(path):
    data = open(path, "rb").read()
    version = struct.unpack_from("<I", data, 23)[0]
    wide = version >= 7500
    hdr = 25 if wide else 13

    def prop(off, nname):
        k = chr(data[off]); off += 1
        sc = {"Y": ("h", 2), "C": ("?", 1), "I": ("i", 4), "F": ("f", 4), "D": ("d", 8), "L": ("q", 8)}
        if k in sc:
            f, s = sc[k]; return struct.unpack_from("<" + f, data, off)[0], off + s
        if k in "SR":
            s = struct.unpack_from("<I", data, off)[0]; off += 4
            v = data[off:off + s]
            return (v.decode("utf-8", "replace") if k == "S" else None), off + s
        if k in "fdlibc":
            cnt, enc, s = struct.unpack_from("<III", data, off); off += 12
            blob = data[off:off + s]
            if nname in ("PolygonVertexIndex", "Vertices", "Lcl Scaling"):
                if enc: blob = zlib.decompress(blob)
                v = struct.unpack("<" + {"i": "i", "d": "d", "f": "f", "l": "q", "b": "b", "c": "B"}[k] * cnt, blob)
            else:
                v = None
            return v, off + s
        raise ValueError(k)

    def node(off):
        if wide:
            end, cnt, _ps = struct.unpack_from("<QQQ", data, off); nl = data[off + 24]
        else:
            end, cnt, _ps = struct.unpack_from("<III", data, off); nl = data[off + 12]
        if not end:
            return None, off + hdr
        name = data[off + hdr:off + hdr + nl].decode("utf-8", "replace")
        cur = off + hdr + nl
        props = []
        for _ in range(cnt):
            v, cur = prop(cur, name); props.append(v)
        ch = []
        while cur < end - hdr:
            c, cur = node(cur)
            if c is None: break
            ch.append(c)
        return {"name": name, "props": props, "children": ch}, end

    nodes = []; cur = 27
    while cur < len(data) - hdr:
        it, cur = node(cur)
        if it is None: break
        nodes.append(it)
    objs = next(n for n in nodes if n["name"] == "Objects")["children"]
    conns = next(n for n in nodes if n["name"] == "Connections")["children"]
    by_id = {o["props"][0]: o for o in objs if o["props"]}
    rows = []
    for key, m in by_id.items():
        if m["name"] != "Model" or len(m["props"]) < 3 or m["props"][2] != "Mesh":
            continue
        linked = [c["props"][1] for c in conns if len(c["props"]) >= 3 and c["props"][0] == "OO" and c["props"][2] == key]
        tris = 0; mn = [1e18] * 3; mx = [-1e18] * 3
        for g in (by_id[v] for v in linked if v in by_id and by_id[v]["name"] == "Geometry"):
            idx = next(c for c in g["children"] if c["name"] == "PolygonVertexIndex")["props"][0]
            n = 0
            for i in idx:
                n += 1
                if i < 0:
                    tris += n - 2; n = 0
            vs = next(c for c in g["children"] if c["name"] == "Vertices")["props"][0]
            for a in range(3):
                mn[a] = min(mn[a], min(vs[a::3])); mx[a] = max(mx[a], max(vs[a::3]))
        rows.append({"model": m["props"][1].split("\x00")[0], "triangles": tris,
                     "rawSize": [round(mx[a] - mn[a], 3) for a in range(3)],
                     "rawMin": [round(v, 3) for v in mn], "rawMax": [round(v, 3) for v in mx]})
    rows.sort(key=lambda r: r["model"])
    return {"version": version, "models": rows}


def main():
    res = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
    for p in sys.argv[1:]:
        r = parse(p)
        res[os.path.basename(p)] = r
        print(os.path.basename(p), [(m["model"], m["triangles"], m["rawSize"]) for m in r["models"]])
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
