# 05_stageC_merge_head_body.py — этап C, шаг 1: атрибуты маски/материала и слияние в Head/Body
# (транскрипт #283; G-канал маски добавлен в C2, #301).
# Вход: mef_stageB.blend (15 мешей). Выход: MEF_Optimized_Head_LOD0 (4 583) + MEF_Optimized_Body_LOD0 (28 832),
# пока с исходными материалами (нужны для запекания и для LOD-бюджетов по частям).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

# ---------- TeamMask: цвет граней из класса mask_cls (02) ----------
# 2026-10-01: три независимых канала вместо одного «перекрашивается» + порога по яркости:
#   R — одежда (рубашка, штанины, балаклава), G — экипировка (бронежилет, сумки, подсумки без магазинов, ремни, лямки, наколенник),
#   B — каска. Чёрное — всё остальное (кожа, перчатки, ботинки, нашивки, навесное каски, стекло).
# FACE-домен: граница классов проходит по рёбрам, без размытия интерполяцией вершин.
allmats = sorted({m.name for o in bpy.data.objects if o.type == 'MESH' for m in o.data.materials})
mat_id = {n: i for i, n in enumerate(allmats)}          # src_mat -> имя материала (порядок сортировки!)
bpy.app.driver_namespace['mat_id'] = mat_id
for m in bpy.data.materials: m.use_fake_user = True     # см. грабли с usmc_helmet
CLS_RGB = np.array([(0, 0, 0, 1), (1, 0, 0, 1), (0, 1, 0, 1), (0, 0, 1, 1)], np.float32)

for o in [o for o in bpy.data.objects if o.type == 'MESH']:
    me = o.data
    cls = np.zeros(len(me.polygons), np.int32)
    if 'mask_cls' in me.attributes: me.attributes['mask_cls'].data.foreach_get('value', cls)
    for n in [c.name for c in me.color_attributes]: me.color_attributes.remove(me.color_attributes[n])
    a = me.attributes.new("TeamMask", "FLOAT_COLOR", "FACE")
    a.data.foreach_set('color', CLS_RGB[cls].ravel())
    # исходный материал каждой грани — чтобы после замены на атласный материал строить LOD по частям / перезапекать
    fa = me.attributes.new('src_mat', 'INT', 'FACE')
    fa.data.foreach_set('value', [mat_id[me.materials[p.material_index].name] for p in me.polygons])
    me.uv_layers['UVSet0'].active = True; me.uv_layers['UVSet0'].active_render = True

# ---------- Голова отдельно от тела ----------
# Правило проекта (game-variant.md §3а): у своего игрока голова выключается через UxrMirrorAvatar._localDisabledGameObjects,
# значит голова — отдельный меш. Критерий: главная кость вершины (max weight) = CC_Base_Head или её потомок.
# 2026-10-01: голова определяется по костям, а не списком — навесное каски (ПНВ, фонарь, кожух…) тоже голова.
arm = bpy.data.objects['RL_BoneRoot']; hd = arm.data.bones['CC_Base_Head']; hs = {hd.name} | {c.name for c in hd.children_recursive}
def head_share(o):
    gn = [g.name for g in o.vertex_groups]
    hv = sum(1 for v in o.data.vertices if v.groups and gn[max(v.groups, key=lambda g: g.weight).group] in hs)
    return hv / max(1, len(o.data.vertices))
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
HEAD = [o.name for o in meshes if head_share(o) >= 0.8]
# Балаклава закрывает и шею (доля головы < 0.8), но относится к голове: иначе у своего игрока она в камере.
HEAD += [n for n in ('SEModelMesh_180',) if n not in HEAD]
BODY = ['SEModelMesh_138'] + [o.name for o in meshes if o.name not in HEAD and o.name != 'SEModelMesh_138']
print('HEAD', sorted(HEAD))
h = join(HEAD, 'MEF_Optimized_Head_LOD0'); b = join(BODY, 'MEF_Optimized_Body_LOD0')

arm = bpy.data.objects['RL_BoneRoot']; hd = arm.data.bones['CC_Base_Head']; hs = {hd.name} | {c.name for c in hd.children_recursive}
for o in (h, b):
    gn = [g.name for g in o.vertex_groups]
    hv = sum(1 for v in o.data.vertices if v.groups and gn[max(v.groups, key=lambda g: g.weight).group] in hs)
    print(o.name, tris(o), 'head-dominant %.3f' % (hv / len(o.data.vertices)))   # ожидаемо 0.935 / 0.001
