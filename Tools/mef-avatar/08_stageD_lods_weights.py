# 08_stageD_lods_weights.py — LOD1/LOD2 и чистка весов (транскрипт #324 make_lod, #334/#339 build, #341 веса).
# Вход: mef_stageC2.blend. Перед LOD сохранён mef_stageD_preLOD.blend (UVSet0 ещё есть — для перезапеканий).
# LOD = копия LOD0, временно разбитая по исходному материалу (атрибут src_mat) -> у каждой части свой бюджет.
# Итог: LOD1 Body 6 798 / Head 1 321; LOD2 Body 4 008 / Head 484.
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

save_checkpoint('mef_stageD_preLOD_repro.blend')
mat_id = bpy.app.driver_namespace['mat_id']; inv = {v: k for k, v in mat_id.items()}

def make_lod(src, name, plan, default=(10 ** 9, 0.0)):
    """plan: {исходный материал: (целевые tris, вес границы)}; вес границы 0.0 = края не трогать.
    default — для материалов вне плана (доля < 1 или абсолютный бюджет); навесное, нашивки, тангента."""
    s = bpy.data.objects[src]; o = s.copy(); o.data = s.data.copy(); o.name = name; o.data.name = name
    for c in s.users_collection: c.objects.link(o)
    me = o.data; ids = np.empty(len(me.polygons), np.int32); me.attributes['src_mat'].data.foreach_get('value', ids)
    used = sorted(set(ids.tolist())); me.materials.clear()
    for u in used: me.materials.append(bpy.data.materials[inv[u]])
    me.polygons.foreach_set('material_index', np.array([used.index(i) for i in ids], np.int32)); me.update()
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='MATERIAL'); bpy.ops.object.mode_set(mode='OBJECT')
    pieces = list(bpy.context.selected_objects)
    for p in pieces:
        mn = p.data.materials[p.data.polygons[0].material_index].name
        tgt, bw = plan.get(mn, default)
        m0 = metrics(p); decimate(p, tgt, rings=0, border_weight=bw); m1 = metrics(p)
        print('  %-14s tris %5d->%5d parts %d->%d bnd %d->%d nonman %d->%d' % (mn[:14], m0['tris'], m1['tris'], m0['parts'], m1['parts'], m0['bnd'], m1['bnd'], m0['nonman'], m1['nonman']))
    bpy.ops.object.select_all(action='DESELECT')
    for p in pieces: p.select_set(True)
    bpy.context.view_layer.objects.active = o; bpy.ops.object.join(); bpy.ops.object.select_all(action='DESELECT')
    o.name = name; o.data.name = name
    m = bpy.data.materials['MEF_Optimized']; o.data.materials.clear(); o.data.materials.append(m)
    o.data.polygons.foreach_set('material_index', np.zeros(len(o.data.polygons), np.int32)); o.data.update()
    print(name, '=', tris(o))

UNI, GLV, SKN = 'body_mp_western_milsim_usmc_mef_1_2_lod1', 'xmaterial_4382e3ff4553e0d2', 'base_male_arm_r'
# 2026-10-01: кобуры в теле нет; детали вне плана (навесное каски, нашивки, химсвет) — default-доля.
# LOD1/LOD2 видны только дальше ~2 м: мелочь там можно ужимать сильно.
# LOD1 из LOD0, края полностью защищены
make_lod('MEF_Optimized_Body_LOD0', 'MEF_Optimized_Body_LOD1', {UNI: (4800, 0.0), GLV: (1600, 0.0), SKN: (500, 0.0)}, default=(0.4, 0.0))
make_lod('MEF_Optimized_Head_LOD0', 'MEF_Optimized_Head_LOD1', {'usmc_helmet': (320, 0.0), 'mp_western_milsim_usmc_mef_balaclava_1_1': (420, 0.0),
         'usmc_goggles': (260, 0.0), 'head_sc_m_fortino': (120, 0.0), 'usmc_headset': (120, 0.0), 'russian_army_goggles_glass': (40, 0.0)},
         default=(0.4, 0.0))
# LOD2 головы — из LOD0 с ослабленной защитой краёв (вес 0.2): 484 tris
make_lod('MEF_Optimized_Head_LOD0', 'MEF_Optimized_Head_LOD2', {'usmc_helmet': (130, 0.2), 'mp_western_milsim_usmc_mef_balaclava_1_1': (150, 0.2),
         'usmc_goggles': (90, 0.2), 'head_sc_m_fortino': (60, 0.2), 'usmc_headset': (40, 0.2), 'russian_army_goggles_glass': (14, 0.2)},
         default=(0.2, 0.2))
# LOD2 тела — ИЗ LOD1, края защищены. Попытки 1350/2400/2600 по форме рвали жилет/рукав и давали шипы на подсумках
# и щель штаны/ботинки (смотрели рендеры D_lods_*.png) -> остановились на 3200 по форме: 4 008 tris вместо ~2 500.
make_lod('MEF_Optimized_Body_LOD1', 'MEF_Optimized_Body_LOD2', {UNI: (3400, 0.0), GLV: (400, 0.0), SKN: (260, 0.0)}, default=(0.5, 0.0))

# Веса на всех 6 мешах: только группы-кости, clean 0.001 -> limit 4 -> normalize -> clean 0
arm = bpy.data.objects['RL_BoneRoot']; bones = {b.name for b in arm.data.bones}
for n in [f'MEF_Optimized_{p}_LOD{l}' for l in (0, 1, 2) for p in ('Body', 'Head')]:
    o = bpy.data.objects[n]; me = o.data
    for g in [g for g in o.vertex_groups if g.name not in bones]: o.vertex_groups.remove(g)
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.vertex_group_clean(group_select_mode='ALL', limit=0.001)
    bpy.ops.object.vertex_group_limit_total(group_select_mode='ALL', limit=4)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL', lock_active=False)
    bpy.ops.object.vertex_group_clean(group_select_mode='ALL', limit=0.0)
    print(n, tris(o), 'maxinf', max(len(v.groups) for v in me.vertices),
          'unweighted', sum(1 for v in me.vertices if not v.groups))
save_checkpoint('mef_stageD_lods_repro.blend')
