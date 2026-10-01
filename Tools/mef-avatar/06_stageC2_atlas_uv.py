# 06_stageC2_atlas_uv.py — UV атласа из исходных островов UVSet0 (финальная версия C2: транскрипт #301–#304).
# Без перераскладки (smart unwrap не делали — лишние швы). Итог: 978 островов, заполнение 49.7 %,
# px/см @2048: одежда/кожа/каска 5.8, лицо 11.7, перчатки 11.3 (2×), прочее 8.8.
#
# ЗАПУСКАТЬ ЧАСТЯМИ (как в оригинале): часть 1 -> отдельный вызов pack_islands -> часть 3.
# В MCP pack_islands молча ничего не делал, если вызывать его в том же execute, где вошли в Edit Mode,
# и если атрибуты .uv_select_* = False (см. README, «Грабли»).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

objs = [bpy.data.objects['MEF_Optimized_Head_LOD0'], bpy.data.objects['MEF_Optimized_Body_LOD0']]
DOUBLE = {'head_sc_m_fortino', 'mp_western_milsim_usmc_mef_balaclava_1_1', 'xmaterial_4382e3ff4553e0d2'}  # лицо, балаклава, перчатки — 2× плотность

def uv_islands(bm, uv):
    """Острова по совпадению UV на обоих концах общего ребра (union-find)."""
    bm.faces.ensure_lookup_table(); parent = list(range(len(bm.faces)))
    def find(x):
        while parent[x] != x: parent[x] = parent[parent[x]]; x = parent[x]
        return x
    for e in bm.edges:
        lf = e.link_loops
        if len(lf) != 2: continue
        a, b = lf
        if (a[uv].uv - b.link_loop_next[uv].uv).length < 1e-5 and (a.link_loop_next[uv].uv - b[uv].uv).length < 1e-5:
            ra, rb = find(a.face.index), find(b.face.index)
            if ra != rb: parent[ra] = rb
    groups = {}
    for f in bm.faces: groups.setdefault(find(f.index), []).append(f)
    return list(groups.values())

# ---------- Часть 1: новый слой 'atlas' = копия UVSet0, масштаб каждого острова к единой плотности ----------
data = []
for o in objs:
    me = o.data
    if 'atlas' in me.uv_layers: me.uv_layers.remove(me.uv_layers['atlas'])
    me.uv_layers.active = me.uv_layers['UVSet0']; at = me.uv_layers.new(name='atlas', do_init=True); me.uv_layers.active = at
    bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv['atlas']; isl = []
    for g in uv_islands(bm, uv):
        a3 = sum(f.calc_area() for f in g)
        au = sum(abs((f.loops[i][uv].uv - f.loops[0][uv].uv).cross(f.loops[i + 1][uv].uv - f.loops[0][uv].uv)) / 2 for f in g for i in range(1, len(f.loops) - 1))
        pts = np.array([l[uv].uv[:] for f in g for l in f.loops])
        key = (g[0].material_index, len(g), tuple(np.round(pts.min(0), 3)), tuple(np.round(pts.max(0), 3)))
        isl.append([g, a3, au, key])
    data.append((o, bm, uv, isl))
ratios = [math.sqrt(a3 / au) for _, _, _, isl in data for g, a3, au, k in isl if au > 1e-9 and a3 > 0]
med = float(np.median(ratios))
dup_faces = {}; seen = {}
for oi, (o, bm, uv, isl) in enumerate(data):
    for g, a3, au, key in isl:
        mat = o.data.materials[g[0].material_index].name
        if (oi,) + key in seen:            # наложенный дубликат в исходной UV (вторая перчатка, повторы подсумков/очков)
            dup_faces.setdefault(oi, []).extend(f.index for f in g); continue
        seen[(oi,) + key] = True
        s = math.sqrt(a3 / au) if au > 1e-9 else med * 4
        s = min(s, 4 * med) / med * (2.0 if mat in DOUBLE else 1.0)   # только верхний зажим (нижний раздувал вырожденные)
        pts = [l[uv].uv.copy() for f in g for l in f.loops]; c = sum(pts, pts[0] * 0) / len(pts)
        for f in g:
            for l in f.loops: l[uv].uv = c + (l[uv].uv - c) * s
    bm.to_mesh(o.data); bm.free()
bpy.app.driver_namespace['dup_faces'] = dup_faces
for oi, o in enumerate(objs):
    me = o.data; h = np.zeros(len(me.polygons), bool); h[dup_faces.get(oi, [])] = True; me.polygons.foreach_set('hide', h)  # дубли не пакуем
    for an in ('.uv_select_vert', '.uv_select_edge', '.uv_select_face'):    # ГРАБЛИ: без этого pack_islands — no-op
        a = me.attributes.get(an)
        if a: a.data.foreach_set('value', np.ones(len(a.data), bool))
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = objs[1]
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')

# ---------- Часть 2 (ОТДЕЛЬНЫЙ вызов): упаковка ----------
PART2 = r'''
import bpy
bpy.ops.uv.select_all(action='SELECT')
bpy.ops.uv.pack_islands(udim_source='CLOSEST_UDIM', rotate=True, rotate_method='ANY', scale=True, merge_overlap=False,
                        margin_method='FRACTION', margin=0.003, shape_method='CONCAVE')   # ~19 с
bpy.ops.object.mode_set(mode='OBJECT')
'''
# exec(PART2)

# ---------- Часть 3 (ОТДЕЛЬНЫЙ вызов; нужен uv_islands — exec этого файла до части 1 включительно не повторять,
#            а просто определить функцию заново или держать её в driver_namespace): дубли -> на атласную позицию своего представителя (аффинная lstsq) ----------
PART3 = r'''
objs = [bpy.data.objects['MEF_Optimized_Head_LOD0'], bpy.data.objects['MEF_Optimized_Body_LOD0']]
dup = bpy.app.driver_namespace['dup_faces']
for oi, o in enumerate(objs):
    me = o.data; me.polygons.foreach_set('hide', np.zeros(len(me.polygons), bool))
    if oi not in dup: continue
    bm = bmesh.new(); bm.from_mesh(me); u0 = bm.loops.layers.uv['UVSet0']; ua = bm.loops.layers.uv['atlas']
    dset = set(dup[oi]); reps = {}; dups = []
    for g in uv_islands(bm, u0):
        pts = np.array([l[u0].uv[:] for f in g for l in f.loops])
        key = (g[0].material_index, len(g), tuple(np.round(pts.min(0), 3)), tuple(np.round(pts.max(0), 3)))
        (dups.append((key, g)) if g[0].index in dset else reps.setdefault(key, g))
    for key, g in dups:
        r = reps.get(key)
        if r is None: print('no rep for', key); continue
        src = np.array([l[u0].uv[:] for f in r for l in f.loops]); dst = np.array([l[ua].uv[:] for f in r for l in f.loops])
        X = np.hstack([src, np.ones((len(src), 1))]); Mx = np.linalg.lstsq(X, dst, rcond=None)[0]   # невязка ~1e-8
        for f in g:
            for l in f.loops: l[ua].uv = tuple(np.array([*l[u0].uv[:], 1.0]) @ Mx)
    bm.to_mesh(me); bm.free()
'''
# exec(PART3)
