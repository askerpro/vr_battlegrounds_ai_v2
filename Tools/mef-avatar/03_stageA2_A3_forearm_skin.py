# 03_stageA2_A3_forearm_skin.py — стык «голая кожа предплечья / закатанный рукав».
# Транскрипт: #226 (A2: вытянуть кожу в рукав), #243 (A3: UV продолжения + зазор до рукава),
#             #253 (A3: итеративная подгонка зазора ≥3 мм в покое и в позе).
# Причина: сетка кожи исходника заходит в рукав всего на ~3 см; у левого рукава манжета снизу открыта,
# после удаления скрытых граней был виден край кожи / дыра. Применяется к SEModelMesh_206 (L) и 205 (R).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())
from mathutils.bvhtree import BVHTree

arm = bpy.data.objects['RL_BoneRoot']; AW = arm.matrix_world
SKIN = (('SEModelMesh_206', 'L'), ('SEModelMesh_205', 'R'))

# ---------- A2: выдавить открытый край кожи у локтя на 5 см внутрь рукава с сужением 12% ----------
for n, side in SKIN:
    o = bpy.data.objects[n]; W = o.matrix_world; Wi = W.inverted()
    p0 = AW @ arm.data.bones[f'CC_Base_{side}_Forearm'].head_local; p1 = AW @ arm.data.bones[f'CC_Base_{side}_Hand'].head_local
    A = (p1 - p0).normalized()
    bm = bmesh.new(); bm.from_mesh(o.data)
    tv = lambda v: (W @ v.co - p0).dot(A)
    bnd = [e for e in bm.edges if e.is_boundary and max(tv(e.verts[0]), tv(e.verts[1])) < 12]   # край у локтя (t<12 см)
    ret = bmesh.ops.extrude_edge_only(bm, edges=bnd)      # веса копируются с края автоматически
    for v in [g for g in ret['geom'] if isinstance(g, bmesh.types.BMVert)]:
        w = W @ v.co; t = (w - p0).dot(A); c = p0 + A * t; r = w - c
        v.co = Wi @ (p0 + A * (t - 5.0) + r * 0.88)
    bm.normal_update(); bm.to_mesh(o.data); bm.free(); o.data.update()

# ---------- A3: UV-продолжение кольца + зазор до внутренней стороны рукава ----------
sl = bpy.data.objects['SEModelMesh_138']; SW = sl.matrix_world
sbvh = BVHTree.FromPolygons([tuple(SW @ v.co) for v in sl.data.vertices], [tuple(p.vertices) for p in sl.data.polygons])
for n, side in SKIN:
    o = bpy.data.objects[n]; W = o.matrix_world; Wi = W.inverted()
    p0 = AW @ arm.data.bones[f'CC_Base_{side}_Forearm'].head_local; p1 = AW @ arm.data.bones[f'CC_Base_{side}_Hand'].head_local
    A = (p1 - p0).normalized()
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
    T = lambda v: (W @ v.co - p0).dot(A)
    ref = [W @ v.co for v in bm.verts if 5.0 < T(v) < 6.5]; cref = sum(ref, Vector()) / len(ref)
    off = cref - (p0 + A * ((cref - p0).dot(A)))          # ось сечения кожи смещена от оси кости (~0.85 см)
    for v in bm.verts:                                    # радиальный зажим: до рукава ≥ 3 мм, радиус ≥ 3 мм
        t = T(v)
        if t > 7.5: continue
        w = W @ v.co; c = p0 + A * t + off; r = w - c; d = r.normalized(); hit = sbvh.ray_cast(c, d, 25)
        if hit[0] is None: continue
        lim = max(hit[3] - 0.3, 0.3)
        if r.length > lim: v.co = Wi @ (c + d * lim)
    uv = bm.loops.layers.uv['UVSet0']
    for v2 in [v for v in bm.verts if any(e.is_boundary for e in v.link_edges) and T(v) < 4.6]:
        # UV новой вершины = продолжение кольца края (а не копия края, как в A2 — давало растяжку)
        nb = [e.other_vert(v2) for e in v2.link_edges]; v1 = max(nb, key=T)
        n1 = max([e.other_vert(v1) for e in v1.link_edges if e.other_vert(v1) is not v2], key=T)
        avg = lambda v: (lambda l: sum(l, l[0] * 0) / len(l))([lp[uv].uv for lp in v.link_loops])
        u1 = avg(v1); un = avg(n1); L1 = (W @ v1.co - W @ n1.co).length; L2 = (W @ v2.co - W @ v1.co).length
        k = min(L2 / max(L1, 1e-4), 2.0)
        for lp in v2.link_loops: lp[uv].uv = u1 + (u1 - un) * k
    bm.to_mesh(o.data); bm.free(); o.data.update()

# Итеративная подгонка: проверка на деформированных мешах в покое и в позе «плечо −45°, локоть 60°»
rest = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
def rot_about(pb, axis, deg):
    hd = pb.head.copy(); pb.matrix = Matrix.Translation(hd) @ Matrix.Rotation(math.radians(deg), 4, axis) @ Matrix.Translation(-hd) @ pb.matrix
    bpy.context.view_layer.update()
def pose_on():
    rot_about(arm.pose.bones['CC_Base_L_Upperarm'], 'Y', 45); rot_about(arm.pose.bones['CC_Base_R_Upperarm'], 'Y', -45)
    rot_about(arm.pose.bones['CC_Base_L_Forearm'], 'Z', 60); rot_about(arm.pose.bones['CC_Base_R_Forearm'], 'Z', -60)
def pose_off():
    for pb in sorted(arm.pose.bones, key=lambda p: len(p.bone.parent_recursive)): pb.matrix = rest[pb.name]; bpy.context.view_layer.update()
def evw(o):
    dg = bpy.context.evaluated_depsgraph_get(); e = o.evaluated_get(dg); mm = e.to_mesh(); Wm = o.matrix_world
    v = [Wm @ x.co for x in mm.vertices]; f = [tuple(p.vertices) for p in mm.polygons]; e.to_mesh_clear(); return v, f
def measure(n, side):
    slv, slf = evw(bpy.data.objects['SEModelMesh_138']); b = BVHTree.FromPolygons(slv, slf); sv, _ = evw(bpy.data.objects[n])
    a0 = AW @ arm.pose.bones[f'CC_Base_{side}_Forearm'].head; a1 = AW @ arm.pose.bones[f'CC_Base_{side}_Hand'].head; A = (a1 - a0).normalized()
    ts = [(p - a0).dot(A) for p in sv]; ring = [p for p, t in zip(sv, ts) if 5 < t < 6.5]; cref = sum(ring, Vector()) / len(ring)
    off = cref - (a0 + A * ((cref - a0).dot(A))); need = {}
    for i, (p, t) in enumerate(zip(sv, ts)):
        if t > 7.5: continue
        c = a0 + A * t + off; r = p - c; h = b.ray_cast(c, r.normalized(), 25)
        if h[0] is None: continue
        g = h[3] - r.length
        if g < 0.3: need[i] = 0.3 - g
    return need
for it in range(3):
    pose_on(); needs = {n: measure(n, s) for n, s in SKIN}; pose_off()
    rneeds = {n: measure(n, s) for n, s in SKIN}
    print('iter', it, {n: len(v) for n, v in needs.items()}, 'rest', {n: len(v) for n, v in rneeds.items()})
    if not any(needs.values()) and not any(rneeds.values()): break
    for n, side in SKIN:
        o = bpy.data.objects[n]; W = o.matrix_world; Wi = W.inverted()
        p0 = AW @ arm.data.bones[f'CC_Base_{side}_Forearm'].head_local; p1 = AW @ arm.data.bones[f'CC_Base_{side}_Hand'].head_local; A = (p1 - p0).normalized()
        ws = [W @ v.co for v in o.data.vertices]; ts = [(w - p0).dot(A) for w in ws]
        ring = [w for w, t in zip(ws, ts) if 5 < t < 6.5]; cref = sum(ring, Vector()) / len(ring); off = cref - (p0 + A * ((cref - p0).dot(A)))
        allneed = {}
        for d in (needs[n], rneeds[n]):
            for i, x in d.items(): allneed[i] = max(allneed.get(i, 0), x)
        for i, x in allneed.items():
            w = ws[i]; c = p0 + A * ts[i] + off; r = w - c; o.data.vertices[i].co = Wi @ (c + r.normalized() * max(r.length - x, 0.3))
        o.data.update()
# остаток в оригинале: 2–5 вершин на руку упираются в минимальный радиус 3 мм (складка рукава), визуально не видно
save_checkpoint('mef_stageA3_repro.blend')
