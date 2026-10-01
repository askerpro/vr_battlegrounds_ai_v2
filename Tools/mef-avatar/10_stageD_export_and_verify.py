# 10_stageD_export_and_verify.py — экспорт FBX и сверка с исходником на уровне файлов (транскрипт #344, #348–#354).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())
import re
from io_scene_fbx import parse_fbx

NAMES = [f'MEF_Optimized_{p}_LOD{l}' for l in (0, 1, 2) for p in ('Body', 'Head')]
# В FBX — только атласный UV (переименован в 'UVMap'), без цветов вершин (TeamMask) — после этого из файла
# перезапекать нельзя: для перезапеканий брать mef_stageD_preLOD.blend.
for n in NAMES:
    me = bpy.data.objects[n].data
    for ln in [u.name for u in me.uv_layers if u.name != 'atlas']: me.uv_layers.remove(me.uv_layers[ln])
    me.uv_layers['atlas'].name = 'UVMap'; me.uv_layers['UVMap'].active = True; me.uv_layers['UVMap'].active_render = True
    ca = me.color_attributes.get('TeamMask')
    if ca: me.color_attributes.remove(ca)
for x in bpy.data.materials['MEF_Optimized'].node_tree.nodes:
    if x.type in ('UVMAP', 'NORMAL_MAP'): x.uv_map = 'UVMap'   # иначе превью в Blender нетекстурное (только косметика)

# ГРАБЛИ: скрытые во вьюпорте объекты не выделяются -> в первом проходе LOD1/LOD2 не попали в FBX.
for o in bpy.data.objects: o.hide_viewport = False; o.hide_set(False)
# Корень MEF после импорта имеет scale 0.01. Чтобы в файле было как у исходника (UnitScaleFactor = 1, т.е. см,
# и MEF без масштаба), MEF временно ставится в 1, а global_scale=0.01 + FBX_SCALE_ALL.
mef = bpy.data.objects['MEF']; old = mef.scale.copy(); mef.scale = (1, 1, 1)
bpy.ops.object.select_all(action='DESELECT')
sel = [o for o in bpy.data.objects if o.type in ('EMPTY', 'ARMATURE') or o.name in NAMES]   # 49 пустышек + арматура + 6 мешей, без ShotCam
for o in sel: o.select_set(True)
bpy.context.view_layer.objects.active = bpy.data.objects['RL_BoneRoot']
try:
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX, use_selection=True, object_types={'EMPTY', 'ARMATURE', 'MESH'},
        global_scale=0.01, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        use_space_transform=True, bake_space_transform=False,
        axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=False,            # Armature не применять — меши в bind-позе
        mesh_smooth_type='OFF', use_tspace=False, use_triangles=True,
        add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X',
        use_armature_deform_only=False, armature_nodetype='ROOT',
        bake_anim=False, path_mode='STRIP', embed_textures=False, use_custom_props=False, colors_type='NONE')
finally:
    mef.scale = old
print('fbx size', os.path.getsize(OUT_FBX))   # 2 818 908 байт
save_checkpoint('mef_stageD_repro.blend')

# ---------- Сверка FBX↔FBX ----------
def Rx(a): c, s = math.cos(a), math.sin(a); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def Ry(a): c, s = math.cos(a), math.sin(a); return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
def Rz(a): c, s = math.cos(a), math.sin(a); return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
ORD = {0: lambda x, y, z: Rz(z) @ Ry(y) @ Rx(x), 1: lambda x, y, z: Ry(y) @ Rz(z) @ Rx(x), 2: lambda x, y, z: Rx(x) @ Rz(z) @ Ry(y),
       3: lambda x, y, z: Rz(z) @ Rx(x) @ Ry(y), 4: lambda x, y, z: Ry(y) @ Rx(x) @ Rz(z), 5: lambda x, y, z: Rx(x) @ Ry(y) @ Rz(z)}
def local(p):
    T = np.eye(4); T[:3, 3] = p.get('Lcl Translation', (0, 0, 0))
    R = np.eye(4); R[:3, :3] = ORD[0](*[math.radians(a) for a in p.get('PreRotation', (0, 0, 0))]) @ ORD[int(p.get('RotationOrder', 0))](*[math.radians(a) for a in p.get('Lcl Rotation', (0, 0, 0))])
    return T @ R @ np.diag(list(p.get('Lcl Scaling', (1, 1, 1))) + [1])
def ang(A, B):
    R1 = A[:3, :3] / np.linalg.norm(A[:3, :3], axis=0); R2 = B[:3, :3] / np.linalg.norm(B[:3, :3], axis=0)
    return math.degrees(math.acos(max(-1, min(1, (np.trace(R1.T @ R2) - 1) / 2))))

txt = open(SRC_FBX, 'r', encoding='utf-8', errors='ignore').read()      # исходник — ASCII: регэкспы
S = {}
for m in re.finditer(r'Model: (\d+), "Model::([^"]+)", "(\w+)" \{', txt):
    i = m.end(); blk = txt[i:txt.find('\n\t}', i)]; p = {}
    for mm in re.finditer(r'P: "([^"]+)", "[^"]*", "[^"]*", "[^"]*",([^\n]+)', blk):
        try: vals = [float(x) for x in mm.group(2).split(',')]
        except ValueError: continue
        p[mm.group(1)] = vals if len(vals) > 1 else vals[0]
    S[int(m.group(1))] = dict(name=m.group(2), type=m.group(3), L=local(p), parent=None)
for m in re.finditer(r'C: "OO",(\d+),(\d+)', txt):                     # ГРАБЛИ: брать родителя только Model->Model
    c, pa = int(m.group(1)), int(m.group(2))                          # (кость ещё и OO-ребёнок своего кластера)
    if c in S and pa in S: S[c]['parent'] = S[pa]['name']
sb = {}
for m in re.finditer(r'Node: (\d+)\s*\n\s*Matrix: \*16 \{\s*\n\s*a: ([^\n]+)', txt):
    k = int(m.group(1))
    if k in S and S[k]['name'] not in sb: sb[S[k]['name']] = np.array([float(x) for x in m.group(2).split(',')]).reshape(4, 4).T
root, ver = parse_fbx.parse(OUT_FBX)                                    # результат — бинарный: парсер аддона io_scene_fbx
objs = next(e for e in root.elems if e.id == b'Objects'); conns = next(e for e in root.elems if e.id == b'Connections')
O = {}
for m in objs.elems:
    if m.id != b'Model': continue
    p70 = next(c for c in m.elems if c.id == b'Properties70'); p = {}
    for c in p70.elems:
        v = c.props[4:]; p[c.props[0].decode()] = list(v) if len(v) > 1 else v[0]
    O[m.props[0]] = dict(name=m.props[1].split(b'\x00')[0].decode(), type=m.props[2].decode(), L=local(p), parent=None)
for c in conns.elems:
    if c.props[0] == b'OO' and c.props[1] in O and c.props[2] in O: O[c.props[1]]['parent'] = O[c.props[2]]['name']
ob = {}
for pz in [e for e in objs.elems if e.id == b'Pose']:
    for pn in [c for c in pz.elems if c.id == b'PoseNode']:
        nid = next(c for c in pn.elems if c.id == b'Node').props[0]; mat = next(c for c in pn.elems if c.id == b'Matrix').props[0]
        if nid in O: ob.setdefault(O[nid]['name'], np.array(mat, float).reshape(4, 4).T)
ts = {v['name']: v for v in S.values()}; to = {v['name']: v for v in O.values()}
common = [n for n in ts if n in to]; bones = [n for n in common if ts[n]['type'] == 'LimbNode']
print('common nodes', len(common), '(ожидаемо 152 = 102 кости + 50 Null/Root)')
print('parent mismatches', [n for n in common if ts[n]['parent'] != to[n]['parent']])
print('Lcl bones max rot %.5f deg' % max(ang(ts[n]['L'], to[n]['L']) for n in bones))            # было 0.00124
rel = [ang(np.linalg.inv(sb[ts[n]['parent']]) @ sb[n], np.linalg.inv(ob[ts[n]['parent']]) @ ob[n]) for n in bones]
print('bind rel-to-parent max rot %.5f deg' % max(rel))                                           # было 0.00196
# Известное отличие: 6 мешей лежат в корне файла (parent None), а не под RL_BoneRoot, как 49 исходных.
