# 09_stageD_exact_head_bind_and_pose.py — точная rest-матрица CC_Base_Head и поза всех костей из данных исходника
# (транскрипт #342–#343; исследование проблемы — #143–#162 первого прохода).
#
# Проблема: длина кости CC_Base_Head в исходнике ≈ 1 мкм (в Blender-единицах 0.01). Импортёр восстанавливает
# ориентацию кости из head/tail/roll, и на такой длине теряет 0.11° (float32). Экспорт уносит эту ошибку в FBX
# (bind + Lcl головы, FacialBone, глаз до 0.27°). Повторный импорт в Blender снова округляет — поэтому проверять
# надо по самим FBX-файлам (10_export_and_verify.py), а не реимпортом.
# Решение: в Edit Mode длина Head = 1000 (10 см в мире; в FBX хвостов нет, Unity не видит) и matrix = точная
# bind-матрица из ASCII FBX (относительно RL_BoneRoot); затем pose каждой кости = цепочка Lcl (PreRotation+Lcl Rotation,
# RotationOrder) из исходника, сверху вниз по иерархии.
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())
import re

txt = open(SRC_FBX, 'r', encoding='utf-8', errors='ignore').read()   # ASCII FBX, 77 МБ, парсится регэкспами ~0.1 с
def Rx(a): c, s = math.cos(a), math.sin(a); return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
def Ry(a): c, s = math.cos(a), math.sin(a); return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
def Rz(a): c, s = math.cos(a), math.sin(a); return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])
ORD = {0: lambda x, y, z: Rz(z) @ Ry(y) @ Rx(x), 1: lambda x, y, z: Ry(y) @ Rz(z) @ Rx(x), 2: lambda x, y, z: Rx(x) @ Rz(z) @ Ry(y),
       3: lambda x, y, z: Rz(z) @ Rx(x) @ Ry(y), 4: lambda x, y, z: Ry(y) @ Rx(x) @ Rz(z), 5: lambda x, y, z: Rx(x) @ Ry(y) @ Rz(z)}
def props(n):
    m = re.search(r'"Model::%s", "(LimbNode|Root|Null)" \{' % re.escape(n), txt); i = m.start(); blk = txt[i:txt.index('Culling', i)]; p = {}
    for mm in re.finditer(r'P: "([^"]+)", "[^"]*", "[^"]*", "[^"]*",([^\n]+)', blk):
        try: vals = [float(x) for x in mm.group(2).split(',')]
        except ValueError: continue
        p[mm.group(1)] = vals if len(vals) > 1 else vals[0]
    return p
def local(p):
    T = np.eye(4); T[:3, 3] = p.get('Lcl Translation', (0, 0, 0))
    R = np.eye(4); R[:3, :3] = ORD[0](*[math.radians(a) for a in p.get('PreRotation', (0, 0, 0))]) @ \
                               ORD[int(p.get('RotationOrder', 0))](*[math.radians(a) for a in p.get('Lcl Rotation', (0, 0, 0))])
    return T @ R @ np.diag(list(p.get('Lcl Scaling', (1, 1, 1))) + [1])
# (PostRotation/Pivots/Offsets/Geometric* в исходнике отсутствуют — проверено)

arm = bpy.data.objects['RL_BoneRoot']
G = {}   # поза кости в пространстве RL_BoneRoot = произведение Lcl по цепочке
for b in sorted(arm.data.bones, key=lambda b: len(b.parent_recursive)):
    G[b.name] = (G[b.parent.name] if b.parent else np.eye(4)) @ local(props(b.name))

names = {int(m.group(1)): m.group(2) for m in re.finditer(r'Model: (\d+), "Model::([^"]+)"', txt)}
sb = {}  # bind-матрицы из первого BindPose, где узел встретился
for m in re.finditer(r'Node: (\d+)\s*\n\s*Matrix: \*16 \{\s*\n\s*a: ([^\n]+)', txt):
    n = names.get(int(m.group(1)))
    if n and n not in sb: sb[n] = np.array([float(x) for x in m.group(2).split(',')]).reshape(4, 4).T
bpy.app.driver_namespace['src_bind'] = {k: v.tolist() for k, v in sb.items()}
E = Matrix((np.linalg.inv(sb['RL_BoneRoot']) @ sb['CC_Base_Head']).tolist())
loc, rot, _ = E.decompose(); E = Matrix.LocRotScale(loc, rot, None)

bpy.ops.object.select_all(action='DESELECT'); arm.hide_viewport = False; arm.select_set(True); bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='EDIT'); eb = arm.data.edit_bones['CC_Base_Head']; eb.length = 1000.0; eb.matrix = E
bpy.ops.object.mode_set(mode='POSE')
for pb in sorted(arm.pose.bones, key=lambda p: len(p.bone.parent_recursive)):
    pb.matrix = Matrix(G[pb.name]); bpy.context.view_layer.update()
bpy.ops.object.mode_set(mode='OBJECT')
# ожидаемо: pose vs source chain ≤0.0135°, rest vs source bind ≤0.009°
