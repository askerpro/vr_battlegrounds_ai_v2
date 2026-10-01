# 07_stageC2_bake.py — запекание атласа (транскрипт C: #284/#287/#288/#290, C2: #307–#309).
# Cycles, CPU, без денойза, target IMAGE_TEXTURES, use_selected_to_active=False (запекание «сам в себя»):
# активный/рендер UV = 'atlas', а все исходные Image Texture явно читают 'UVSet0' через узел UV Map.
#   2026-10-01: Albedo/Normal 4096 (margin 12), TeamMask 2048 RGB из классов граней (margin 6), без порога.


exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

objs = [bpy.data.objects['MEF_Optimized_Head_LOD0'], bpy.data.objects['MEF_Optimized_Body_LOD0']]

# Если меши уже на атласном материале — вернуть исходные материалы по атрибуту src_mat (C2, #300)
mat_id = bpy.app.driver_namespace.get('mat_id') or {n: i for i, n in enumerate(sorted(
    ['base_male_arm_r', 'body_mp_western_milsim_usmc_mef_1_2_lod1', 'head_sc_m_fortino', 'mp_western_milsim_usmc_mef_balaclava_1_1',
     'russian_army_goggles_glass', 'texture_mat_holster_Albedo', 'usmc_goggles', 'usmc_headset', 'usmc_helmet', 'xmaterial_4382e3ff4553e0d2']))}
inv = {v: k for k, v in mat_id.items()}
for o in objs:
    me = o.data
    if len(me.materials) == 1 and me.materials[0].name == 'MEF_Optimized':
        ids = np.empty(len(me.polygons), np.int32); me.attributes['src_mat'].data.foreach_get('value', ids)
        used = sorted(set(ids.tolist())); me.materials.clear()
        for u in used: me.materials.append(bpy.data.materials[inv[u]])
        me.polygons.foreach_set('material_index', np.array([used.index(i) for i in ids], np.int32)); me.update()

def get_img(name, size, noncolor):
    im = bpy.data.images.get(name) or bpy.data.images.new(name, size, size, alpha=False)
    if noncolor: im.colorspace_settings.name = 'Non-Color'
    return im
# 2026-10-01: атлас 4096 (на 2048 при заполнении ~50 % деталь вблизи терялась), маска 2048.
AS, MS = 4096, 2048
alb = get_img('MEF_Optimized_Albedo', AS, False); nrm = get_img('MEF_Optimized_Normal', AS, True); msk = get_img('MEF_Optimized_TeamMask', MS, True)

mats = {m for o in objs for m in o.data.materials}
for m in mats:
    nt = m.node_tree
    for nn in [x for x in nt.nodes if x.name.startswith('BK_')]: nt.nodes.remove(nn)
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UVSet0'; uvn.name = 'BK_UV'
    for x in list(nt.nodes):
        if x.type == 'TEX_IMAGE':
            for l in list(x.inputs['Vector'].links): nt.links.remove(l)
            nt.links.new(uvn.outputs['UV'], x.inputs['Vector'])
        if x.type == 'NORMAL_MAP': x.uv_map = 'UVSet0'
    t = nt.nodes.new('ShaderNodeTexImage'); t.name = 'BK_TARGET'
    for x in nt.nodes: x.select = False
    t.select = True; nt.nodes.active = t
for o in objs: o.data.uv_layers.active = o.data.uv_layers['atlas']; o.data.uv_layers['atlas'].active_render = True

sc = bpy.context.scene; sc.render.engine = 'CYCLES'; sc.cycles.use_denoising = False   # device CPU
b = sc.render.bake; b.target = 'IMAGE_TEXTURES'; b.use_selected_to_active = False
bpy.ops.object.select_all(action='DESELECT')
for o in objs: o.select_set(True)
bpy.context.view_layer.objects.active = objs[1]
def target(im):
    for m in mats: n = m.node_tree.nodes['BK_TARGET']; n.image = im; m.node_tree.nodes.active = n

target(alb); sc.cycles.samples = 16
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, margin=12, use_clear=True)
target(nrm); sc.cycles.samples = 8
bpy.ops.object.bake(type='NORMAL', normal_space='TANGENT', margin=12, use_clear=True)

# TeamMask: временно Surface = Emission(Attribute 'TeamMask'), потом связи восстанавливаются
saved = {}
for m in mats:
    nt = m.node_tree; out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output)
    lk = out.inputs['Surface'].links[0]; saved[m.name] = (lk.from_node.name, lk.from_socket.identifier)
    at = nt.nodes.new('ShaderNodeAttribute'); at.attribute_name = 'TeamMask'; at.name = 'BK_ATTR'
    em = nt.nodes.new('ShaderNodeEmission'); em.name = 'BK_EM'
    nt.links.new(at.outputs['Color'], em.inputs['Color']); nt.links.new(em.outputs['Emission'], out.inputs['Surface'])
target(msk); sc.cycles.samples = 4
bpy.ops.object.bake(type='EMIT', margin=6, use_clear=True)
for m in mats:
    nt = m.node_tree; out = next(n for n in nt.nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output)
    fn, fs = saved[m.name]; src = nt.nodes[fn]; nt.links.new(next(s for s in src.outputs if s.identifier == fs), out.inputs['Surface'])
    nt.nodes.remove(nt.nodes['BK_EM']); nt.nodes.remove(nt.nodes['BK_ATTR'])

# 2026-10-01: порога по яркости больше нет — каналы R/G/B маски приходят готовыми из классов граней (05).
M = np.array(msk.pixels[:], np.float32).reshape(MS, MS, 4)
print('mask coverage R %.3f G %.3f B %.3f' % tuple((M[..., k] > 0.5).mean() for k in range(3)))

for n in ('MEF_Optimized_Albedo', 'MEF_Optimized_Normal', 'MEF_Optimized_TeamMask'):
    im = bpy.data.images[n]; im.filepath_raw = os.path.join(OUT_DIR, n + '.png'); im.file_format = 'PNG'; im.save()
# ГРАБЛИ: превью через im.copy()+scale() сохранило СТАРУЮ раскладку маски — проверять по пикселям live vs файл.

# Атласный материал (для Blender-превью; в FBX уходит только имя слота 'MEF_Optimized')
mat = bpy.data.materials.get('MEF_Optimized') or bpy.data.materials.new('MEF_Optimized'); mat.use_nodes = True; nt = mat.node_tree
if not any(n.type == 'TEX_IMAGE' for n in nt.nodes):
    bs = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'); bs.inputs['Roughness'].default_value = 0.8
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'atlas'
    ta = nt.nodes.new('ShaderNodeTexImage'); ta.image = alb; nt.links.new(uvn.outputs['UV'], ta.inputs['Vector']); nt.links.new(ta.outputs['Color'], bs.inputs['Base Color'])
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = nrm; nt.links.new(uvn.outputs['UV'], tn.inputs['Vector'])
    nm = nt.nodes.new('ShaderNodeNormalMap'); nm.uv_map = 'atlas'; nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bs.inputs['Normal'])
for o in objs:
    me = o.data; me.materials.clear(); me.materials.append(mat)
    me.polygons.foreach_set('material_index', np.zeros(len(me.polygons), np.int32)); me.update()
for im in (alb, nrm, msk): im.pack()
save_checkpoint('mef_stageC2_repro.blend')
# Сверка: рендер EEVEE исходных материалов и атласа в 7 ракурсах, mean|diff| 0.03–0.04, атлас темнее на 7–10 %.
