# 00_common.py — общие пути и хелперы конвейера MEF_Optimized.
# В сессии 2026-09-28 эти функции жили в bpy.app.driver_namespace (comps, metrics, dec, make_lod...)
# и переиспользовались между вызовами execute_blender_code. Здесь они собраны в один модуль:
# каждый следующий скрипт делает exec(open(COMMON).read()) или просто вставляется после этого файла.
#
# Blender 5.2.2 LTS. Единицы: FBX в сантиметрах, после импорта корень MEF имеет scale 0.01,
# меши лежат в пространстве RL_BoneRoot (координаты вершин ~ в 100 раз больше «мировых» см).
import bpy, bmesh, os, math
import numpy as np
from mathutils import Vector, Matrix

PROJECT = r"F:\UnityProjects\Vr_Battlegrounds_ai"
SRC_FBX = PROJECT + r"\Assets\ThirdParty\BonelabAvatars\US_MARINES\FBX\MEF\MEF.fbx"
RAIDER_FBX = PROJECT + r"\Assets\ThirdParty\BonelabAvatars\US_MARINES\FBX\Raider\US Raider 2 rig.fbx"
TEX_DIRS = [PROJECT + r"\Assets\ThirdParty\BonelabAvatars\US_MARINES\FBX\MEF\Textures",
            PROJECT + r"\Assets\ThirdParty\BonelabAvatars\US_MARINES\FBX\Raider\Textures"]  # 9 картинок MEF лежат только у Raider
# Результат пишется в out/ (не в Assets): в общий редактор Unity кладётся только после проверки.
OUT_DIR = PROJECT + r"\Tools\mef-avatar\out"
ASSET_DIR = PROJECT + r"\Assets\Models\Avatars\MEF_Optimized"
os.makedirs(OUT_DIR, exist_ok=True)
OUT_FBX = OUT_DIR + r"\MEF_Optimized.fbx"
SCRATCH = PROJECT + r"\Tools\mef-avatar\checkpoints"   # локально, в .gitignore

def tris(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

def clear_scene():
    # НИКОГДА не read_factory_settings / read_homefile / open_mainfile — это выгружает сокет Blender MCP
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    # ГРАБЛИ: 05 ставит материалам use_fake_user — без сброса purge их не удаляет, повторный импорт даёт имена «.001»,
    # и скрипты, ищущие материалы по имени (06/07/08), молча промахиваются.
    for m in bpy.data.materials: m.use_fake_user = False
    bpy.ops.outliner.orphans_purge(do_recursive=True)

def relink_textures():
    """Перепривязка картинок по имени файла + Non-Color для нормалей."""
    for im in bpy.data.images:
        if not im.filepath:
            continue
        fn = os.path.basename(im.filepath.replace('\\', '/'))
        for d in TEX_DIRS:
            p = os.path.join(d, fn)
            if os.path.exists(p):
                im.filepath = p
                break
        im.reload()
        if fn.endswith(('_n.png', '_n_0.png', 'Normal.png')):
            im.colorspace_settings.name = 'Non-Color'
    return [im.name for im in bpy.data.images if im.filepath and im.size[0] == 0]

def comps(bm):
    """Связные компоненты (loose parts) по рёбрам; порядок = порядок обхода граней.
    ВАЖНО: индексы компонентов в KEEP-списках ниже получены именно этой функцией на свежем импорте."""
    bm.faces.ensure_lookup_table(); seen = set(); out = []
    for f in bm.faces:
        if f.index in seen:
            continue
        st = [f]; comp = []; seen.add(f.index)
        while st:
            g = st.pop(); comp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index); st.append(h)
        out.append(comp)
    return out

def parts_table(o):
    """Отладочная таблица компонентов: (idx, tris, center, size) в мировых см — по ней выбирали KEEP на глаз."""
    bm = bmesh.new(); bm.from_mesh(o.data); M = o.matrix_world; rows = []
    for i, c in enumerate(comps(bm)):
        pts = [M @ v.co for f in c for v in f.verts]
        mn = Vector([min(p[k] for p in pts) for k in range(3)]); mx = Vector([max(p[k] for p in pts) for k in range(3)])
        rows.append((i, sum(len(f.verts) - 2 for f in c), tuple(round(x) for x in (mn + mx) / 2), tuple(round(x) for x in mx - mn)))
    bm.free(); return rows

def metrics(o):
    """Контроль целостности до/после децимации: tris, открытые рёбра, неманифолд, число кусков, «щепки»."""
    bm = bmesh.new(); bm.from_mesh(o.data); bm.transform(o.matrix_world)
    t = sum(len(f.verts) - 2 for f in bm.faces)
    b = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if len(e.link_faces) > 2)
    nc = len(comps(bm))
    bmesh.ops.triangulate(bm, faces=bm.faces[:]); sl = 0
    for f in bm.faces:
        e = [x.calc_length() for x in f.edges]; a = f.calc_area()
        if a < 1e-9 or max(e) ** 2 / (a + 1e-12) > 60:
            sl += 1
    bm.free()
    return dict(tris=t, bnd=b, nonman=nm, parts=nc, sliver=sl)

def protect_group(o, rings=1, name='DEC', border_weight=0.0):
    """Группа для Decimate: 1.0 везде, border_weight на вершинах открытых границ (+ rings колец вокруг)."""
    me = o.data; bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    prot = {v.index for v in bm.verts if any(e.is_boundary for e in v.link_edges)}
    for _ in range(rings):
        prot |= {e.other_vert(bm.verts[i]).index for i in list(prot) for e in bm.verts[i].link_edges}
    bm.free()
    g = o.vertex_groups.get(name) or o.vertex_groups.new(name=name)
    g.add([v.index for v in me.vertices if v.index not in prot], 1.0, 'REPLACE')
    if prot:
        g.add(list(prot), border_weight, 'REPLACE')

def decimate(o, ratio_or_target, rings=1, border_weight=0.0):
    """Бережный Collapse: без симметрии (симметрия дала шипы и «перепонку» в первом проходе),
    границы защищены группой вершин, результат триангулирован."""
    t0 = tris(o)
    ratio = ratio_or_target / t0 if ratio_or_target > 1 else ratio_or_target
    if ratio >= 1:
        return
    protect_group(o, rings, border_weight=border_weight)
    md = o.modifiers.new('Dec', 'DECIMATE'); md.decimate_type = 'COLLAPSE'; md.ratio = ratio
    md.use_collapse_triangulate = True; md.use_symmetry = False
    md.vertex_group = 'DEC'; md.vertex_group_factor = 1.0
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    bpy.ops.object.modifier_move_to_index(modifier='Dec', index=0)   # раньше Armature, иначе применится поверх позы
    bpy.ops.object.modifier_apply(modifier='Dec')
    o.vertex_groups.remove(o.vertex_groups['DEC'])

def join(names, newname):
    bpy.ops.object.select_all(action='DESELECT')
    objs = [bpy.data.objects[n] for n in names]
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]; bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active; ob.name = newname; ob.data.name = newname
    bpy.ops.object.select_all(action='DESELECT'); return ob

def fib_dirs(K=64):
    dirs = []
    for i in range(K):
        y = 1 - 2 * (i + 0.5) / K; r = math.sqrt(1 - y * y); th = math.pi * (3 - math.sqrt(5)) * i
        dirs.append(Vector((r * math.cos(th), r * math.sin(th), y)))
    return dirs

def save_checkpoint(name):
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SCRATCH, name), copy=True)
