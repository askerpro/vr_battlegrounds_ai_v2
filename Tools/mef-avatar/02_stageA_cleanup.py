# 02_stageA_cleanup.py — этап A: удаление снаряжения, отбор кусков, кобура, скрытая геометрия.
# Транскрипт: #188 (удаление + split подсумков), #189 (кобура в skinned), #190–#191 (зазор кобуры),
#             #192 (окклюзия, 2 кольца), #205–#206 + #220 (повтор окклюзии с запасом 4 см на объектах со стыками).
# Выход: mef_stageA*.blend — 15 раздельных мешей с исходными материалами (+ часы, позже удалены).
#
# ВСЕ списки ниже выбраны НА ГЛАЗ: по таблицам parts_table() (центр/размер куска в см) и рендерам.
# Индексы компонентов валидны только для свежего импорта MEF.fbx этим же импортёром (wm.fbx_import, 5.2.2).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())
from mathutils.bvhtree import BVHTree
from mathutils import kdtree
import heapq

# ---------- 1. Целиком удаляемые объекты ----------
DEL = [
    # снаряжение/мелочь: IR-патч сбоку (161), турникет (165), наручники (168–171), мелочь формы (153–156),
    # глаза (190), GPS на запястье (207), подсумок турникета (162), рация (163)
    'SEModelMesh_160', 'SEModelMesh_161', 'SEModelMesh_165',   # 160 — IR-патч на гидропаке (спина), без него висит в воздухе
    'SEModelMesh_168', 'SEModelMesh_169', 'SEModelMesh_170', 'SEModelMesh_171',
    'SEModelMesh_153', 'SEModelMesh_154', 'SEModelMesh_155', 'SEModelMesh_156', 'SEModelMesh_190',
    'SEModelMesh_207', 'SEModelMesh_162', 'SEModelMesh_163',
    'strap_geo',                       # петли кобуры (это НЕ ножные ремни), материал Lit
    # остальные объекты материала формы body_mp_*: гидропак (139), сумки на бёдрах и т.п.
    'SEModelMesh_139', 'SEModelMesh_140', 'SEModelMesh_142', 'SEModelMesh_143', 'SEModelMesh_144', 'SEModelMesh_145',
    'SEModelMesh_148', 'SEModelMesh_149', 'SEModelMesh_150', 'SEModelMesh_151',
    # Кобура в тело не входит (2026-10-01): отдельный жёсткий префаб из оригинального handgunHolster_geo в Unity.
    'handgunHolster_geo',
]
# Возвращены 2026-10-01 (решение пользователя по сравнению с оригиналом): каска целиком — липучки 183, крепление ПНВ 184,
# фонарь 186, батарейный блок 187, кожух 188, нашивки 189/191/192; на груди — флаг 136, химсвет 157;
# сумка на животе 146; тангента рации на груди 147 (с проводом гарнитуры 138:71).
for n in DEL:
    bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)

# ---------- 1б. Класс маски команды на каждой грани (FACE INT 'mask_cls') ----------
# 0 — не перекрашивать (кожа, ботинки, нашивки, стекло, магазины, провода), 1 — одежда (R), 2 — экипировка (G:
# бронежилет, сумки и подсумки без магазинов, ремни и лямки, наколенник), 3 — каска и очки (B).
# Ставится до отбора кусков, пока индексы компонентов совпадают с таблицами. Переживает децимацию и join
# (у объектов без атрибута join ставит 0). В 05 из него строится цвет TeamMask.
CLS_NONE, CLS_CLOTH, CLS_GEAR, CLS_HELMET = 0, 1, 2, 3
def set_cls(name, cls_of_face):
    o = bpy.data.objects[name]; me = o.data
    a = me.attributes.get('mask_cls') or me.attributes.new('mask_cls', 'INT', 'FACE')
    a.data.foreach_set('value', [cls_of_face(p.index) for p in me.polygons])
def uv_islands0(bm):
    uv = bm.loops.layers.uv['UVSet0']; bm.faces.ensure_lookup_table(); parent = list(range(len(bm.faces)))
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
    g = {}
    for f in bm.faces: g.setdefault(find(f.index), []).append(f.index)
    return list(g.values())
# 138: 1 рубашка — одежда; 3 штаны — одежда только две штанины (два самых больших UV-острова), остальные острова
# штанов (лямки платформы, наколенник, вставки) — экипировка; жилет, панель, пояс сумки, пряжки, подсумки — экипировка;
# нашивка B POS (69) и ботинки (18/19) — не красить.
bm = bmesh.new(); bm.from_mesh(bpy.data.objects['SEModelMesh_138'].data); cs = comps(bm); fc = {}
for i, c in enumerate(cs):
    for f in c: fc[f.index] = i
pants = {f.index for f in cs[3]}
legs = set()
for isl in sorted([i for i in uv_islands0(bm) if i[0] in pants], key=len, reverse=True)[:2]: legs |= set(isl)
bm.free()
def cls138(fi):
    c = fc[fi]
    if c == 1 or fi in legs: return CLS_CLOTH
    if c in (18, 19, 69, 70, 71): return CLS_NONE   # ботинки, нашивка, провода
    return CLS_GEAR
set_cls('SEModelMesh_138', cls138)
for n in ('SEModelMesh_138_001', 'SEModelMesh_141', 'SEModelMesh_146'): set_cls(n, lambda fi: CLS_GEAR)   # подсумки груди, ремень, сумка
set_cls('SEModelMesh_180', lambda fi: CLS_CLOTH)    # балаклава
set_cls('SEModelMesh_179', lambda fi: CLS_HELMET)   # каска (оболочка, ремешки, рельсы)
set_cls('SEModelMesh_181', lambda fi: CLS_HELMET)   # очки (оправа, ремешок) — голова «свой/чужой», 2026-10-01

# ---------- 2. Отбор кусков внутри объектов ----------
def inbox(c, lo, hi): return all(lo[k] <= c[k] <= hi[k] for k in range(3))

def split(name, keep_fn, pouch_fn, pouch_name):
    """keep_fn — что остаётся в объекте; pouch_fn — что уходит отдельным объектом POUCH_*; остальное удаляется."""
    o = bpy.data.objects[name]; M = o.matrix_world
    bm = bmesh.new(); bm.from_mesh(o.data); cs = comps(bm); keep = []; pouch = []
    for i, c in enumerate(cs):
        pts = [M @ v.co for f in c for v in f.verts]
        ce = (Vector([min(p[k] for p in pts) for k in range(3)]) + Vector([max(p[k] for p in pts) for k in range(3)])) / 2
        if keep_fn(i, ce): keep.append(i)
        elif pouch_fn(i, ce): pouch.append(i)
    if pouch:
        me2 = o.data.copy(); p = o.copy(); p.data = me2; p.name = pouch_name; o.users_collection[0].objects.link(p)
        b2 = bmesh.new(); b2.from_mesh(me2); c2 = comps(b2)
        bmesh.ops.delete(b2, geom=[f for i, c in enumerate(c2) if i not in pouch for f in c], context='FACES'); b2.to_mesh(me2); b2.free()
    bmesh.ops.delete(bm, geom=[f for i, c in enumerate(cs) if i not in keep for f in c], context='FACES'); bm.to_mesh(o.data); bm.free()
    print(name, 'keep', keep, 'pouch parts', len(pouch))

# 138 = одежда+жилет+ботинки: 1 рубашка/рукава, 2 жилет, 3 штаны, 18/19 ботинки. Пояс/подсумки на поясе -> POUCH_A
# + 2026-10-01: 4 панель нашивок на груди, 69 нашивка B POS, 16 пояс сумки на животе, 27/30 пряжки подтяжек,
#   71 провод гарнитуры через плечо к тангенте на груди (70/72/73 — петля и провода к рации на спине, рация удалена).
# Правая боковая сумка (бокс x −24…−17) больше не берётся — её место занимает кобура.
split('SEModelMesh_138', lambda i, c: i in (1, 2, 3, 4, 16, 18, 19, 27, 30, 69, 71),
      lambda i, c: i in (10, 11) or inbox(c, (16, -7, 97), (23, 5, 110)), 'POUCH_A')
# 141 = ремень (компонент 3) + подсумки на ремне -> POUCH_B
split('SEModelMesh_141', lambda i, c: i == 3, lambda i, c: i in (0, 1, 2, 4, 5), 'POUCH_B')
# 138_001 = магазинные подсумки на груди жилета — целиком
bpy.data.objects['SEModelMesh_138_001'].name = 'POUCH_C'

# ---------- 2б. Оторванные куски (2026-10-01) ----------
# После удаления снаряжения в объектах остаются детали, висевшие на нём (пример: пластина 22 tris в 12 см за спиной —
# от гидропака). Правило: связный кусок, у которого ни одна вершина не ближе DEBRIS_CM к геометрии других кусков, — мусор.
DEBRIS_CM = 5.0
def remove_debris():
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    pts = []; owner = []
    per = {}
    for o in meshes:
        bm = bmesh.new(); bm.from_mesh(o.data); M = o.matrix_world; cs = comps(bm)
        per[o.name] = (bm, cs)
        for ci, c in enumerate(cs):
            for v in {v for f in c for v in f.verts}: pts.append(M @ v.co); owner.append((o.name, ci))
    kd = kdtree.KDTree(len(pts))
    for i, p in enumerate(pts): kd.insert(p, i)
    kd.balance()
    for o in meshes:
        bm, cs = per[o.name]; M = o.matrix_world; drop = []
        for ci, c in enumerate(cs):
            vs = list({v for f in c for v in f.verts}); near = False
            for v in vs[::max(1, len(vs) // 60)]:
                for co, j, d in kd.find_range(M @ v.co, DEBRIS_CM):
                    if owner[j] != (o.name, ci): near = True; break
                if near: break
            if not near: drop.append(ci)
        if drop:
            print('debris', o.name, [(ci, len(cs[ci])) for ci in drop])
            bmesh.ops.delete(bm, geom=[f for ci in drop for f in cs[ci]], context='FACES'); bm.to_mesh(o.data)
        bm.free()
remove_debris()

# ---------- 2в. Магазины в подсумках — не экипировка (2026-10-01) ----------
# Экипировка (G) = бронежилет, сумки, ремни/лямки, наколенник и подсумки, но не магазины в них. Магазины — отдельные
# связные куски внутри POUCH_B/POUCH_C; отличаются тёмной текстурой: медиана яркости ткани подсумков 0.30–0.39,
# автоматных магазинов ~0.18, пистолетных ~0.04. У POUCH_A так не мерить — у двух его кусков UV сэмплируются в чёрное.
MAG_LUM = 0.25
body_img = next(i for i in bpy.data.images if i.name.startswith('body_mp_western_milsim_usmc_mef_1_2_lod1_c'))
BW, BH = body_img.size; body_px = np.array(body_img.pixels[:], np.float32).reshape(BH, BW, 4)
for n in ('POUCH_B', 'POUCH_C'):
    o = bpy.data.objects[n]; me = o.data; bm = bmesh.new(); bm.from_mesh(me); uv = bm.loops.layers.uv['UVSet0']
    cls = np.zeros(len(me.polygons), np.int32); me.attributes['mask_cls'].data.foreach_get('value', cls); mags = []
    for i, c in enumerate(comps(bm)):
        ls = []
        for f in c:
            u = sum((l[uv].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
            p = body_px[min(int((u.y % 1) * BH), BH - 1), min(int((u.x % 1) * BW), BW - 1)]
            ls.append(0.2126 * p[0] + 0.7152 * p[1] + 0.0722 * p[2])
        if float(np.median(ls)) < MAG_LUM:
            mags.append(i); cls[[f.index for f in c]] = CLS_NONE
    me.attributes['mask_cls'].data.foreach_set('value', cls); bm.free()
    print(n, 'магазины (не экипировка):', mags)

# Голова: 2026-10-01 каска, очки и гарнитура берутся целиком (с ремешками, проводами и микрофоном) — KEEP_HEAD снят.
# Кобура: в тело не входит, см. DEL.

# ---------- 4. Скрытая геометрия: лучи из вершин ----------
def occlusion(targets=None, margin_cm=None, rings=2):
    """Вершина видима, если хотя бы один из 40 лучей (из 64 направлений Фибоначчи, ближайших к нормали)
    уходит в бесконечность без попадания в общий BVH всех мешей. Удаляются грани, у которых нет ни одной
    «сохранённой» вершины. Сохранённые = видимые, расширенные либо на `rings` колец рёбер (первый вариант),
    либо на геодезическое расстояние margin_cm по поверхности (финальный вариант для объектов со стыками)."""
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_viewport]
    V = []; P = []; off = 0; data = {}
    for o in meshes:
        me = o.data; W = o.matrix_world; N3 = W.to_3x3().inverted().transposed()
        cw = [W @ v.co for v in me.vertices]; nw = [(N3 @ v.normal).normalized() for v in me.vertices]
        data[o.name] = (cw, nw); V.extend(map(tuple, cw)); P.extend([tuple(i + off for i in p.vertices) for p in me.polygons]); off += len(me.vertices)
    bvh = BVHTree.FromPolygons(V, P); dirs = fib_dirs(64)
    for name in (targets or list(data)):
        cw, nw = data[name]; n = len(cw); vis = np.zeros(n, bool)
        for i in range(n):
            nn = nw[i]; p = cw[i] + nn * 0.03
            for d in sorted(dirs, key=lambda d: -d.dot(nn))[:40]:
                if bvh.ray_cast(p + d * 0.001, d)[0] is None: vis[i] = True; break
        o = bpy.data.objects[name]; bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table()
        if margin_cm is None:
            keep = vis.copy()
            for _ in range(rings):
                nk = keep.copy()
                for e in bm.edges:
                    a, b = e.verts[0].index, e.verts[1].index
                    if keep[a] or keep[b]: nk[a] = nk[b] = True
                keep = nk
        else:
            dist = np.full(n, np.inf); hp = []
            for i in np.where(vis)[0]: dist[i] = 0; hp.append((0.0, int(i)))
            heapq.heapify(hp)
            while hp:
                dd, i = heapq.heappop(hp)
                if dd > dist[i] or dd > margin_cm: continue
                for e in bm.verts[i].link_edges:
                    j = e.other_vert(bm.verts[i]).index; nd_ = dd + (cw[i] - cw[j]).length
                    if nd_ < dist[j]: dist[j] = nd_; heapq.heappush(hp, (nd_, j))
            keep = dist <= margin_cm
        t0 = len(bm.faces)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if not any(keep[x.index] for x in f.verts)], context='FACES')
        bmesh.ops.delete(bm, geom=[x for x in bm.verts if not x.link_faces], context='VERTS')
        print(name, 'visible %.2f' % vis.mean(), t0, '->', len(bm.faces)); bm.to_mesh(o.data); bm.free(); o.data.update()

# Вариант этапа A (#192): 2 кольца на всех мешах -> снимал лицо под балаклавой, низ балаклавы под воротником,
# тыльные стороны подсумков/кобуры. Но открыл щель кожа/рукав.
# Финал (A2, #205 + #220): объекты со стыками восстановлены из исходника (те же KEEP) и пройдены с запасом 4 см.
JOINT_OBJECTS = ('SEModelMesh_138', 'SEModelMesh_173', 'SEModelMesh_180', 'SEModelMesh_181',
                 'SEModelMesh_182', 'SEModelMesh_185', 'SEModelMesh_205', 'SEModelMesh_206')
# 2026-10-01: мелкую фурнитуру (< OCCLUSION_MIN_TRIS) окклюзией не трогать — лучи из соседних деталей «закрывали»
# видимые коннекторы целиком (тангента 147 потеряла коннектор под коробкой, батарейный блок 187 — 48 tris), а выигрыш —
# десятки треугольников.
OCCLUSION_MIN_TRIS = 1500
occlusion(targets=[n for n in [o.name for o in bpy.data.objects if o.type == 'MESH']
                   if n not in JOINT_OBJECTS and n not in ('SEModelMesh_182',) and tris(bpy.data.objects[n]) >= OCCLUSION_MIN_TRIS], rings=2)
# Гарнитура (182) — провода и микрофон из тонких кусков: окклюзию не применять (снимала ~40 tris коннекторов).
NO_OCCLUSION = {'SEModelMesh_182'}
occlusion(targets=[n for n in JOINT_OBJECTS if n not in NO_OCCLUSION], margin_cm=4.0)
# ожидаемо (без часов): 138 15354->~15162, 180 2879->2233, 185 2287->1422, остальные почти без изменений

print('TOTAL', sum(tris(o) for o in bpy.data.objects if o.type == 'MESH'))
save_checkpoint('mef_stageA_repro.blend')
