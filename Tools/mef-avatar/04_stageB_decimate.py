# 04_stageB_decimate.py — этап B: бережная децимация LOD0 (транскрипт #266 dec(), #271 PLAN, #270 удаление часов).
# Вход: mef_stageA3.blend. Выход: mef_stageB.blend — 15 раздельных мешей, 33 415 tris (тело 28 832, голова 4 583).
#
# Правила (выведены из провала первого прохода — шипы, порванные лямки, «перепонка» подмышкой):
#  - Collapse БЕЗ симметрии (use_symmetry=False);
#  - вершины открытых границ + 1 кольцо — вес 0 в группе DEC (не схлопываются);
#  - видимая ткань — коэффициент ≥ 0.6; перчатки — абсолютный бюджет 9000;
#  - после каждой детали сверка metrics(): parts, bnd, nonman не должны меняться.
# Бюджеты подобраны на глаз и согласованы с пользователем («перчатки 8–10k, ткань ≥0.5»).
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

# Часы (WATCH из Raider) по решению пользователя удалены на этом этапе вместе с материалами — в финальном FBX их нет.
w = bpy.data.objects.get('WATCH')
if w: bpy.data.objects.remove(w, do_unlink=True)
for mn in ('kyle_desert_watch', 'watch_case_plain', 'watch_face_plain'):
    m = bpy.data.materials.get(mn)
    if m: bpy.data.materials.remove(m)
for im in [im for im in bpy.data.images if 'kyle_desert_watch' in im.name]: bpy.data.images.remove(im)
bpy.ops.outliner.orphans_purge(do_recursive=True)
# ГРАБЛИ (этап C, #290): orphans_purge после замены материалов на MEF_Optimized снёс исходный материал
# usmc_helmet (стал без пользователей) — в C2 его пришлось возвращать append'ом из mef_stageB.blend.
# Пока нужны перезапекания/LOD с бюджетом по частям, исходным материалам ставить use_fake_user=True.

PLAN = {'SEModelMesh_173': 9000,   # перчатки 32 844 -> 9 000 (0.27)
        'SEModelMesh_138': 0.65,   # одежда/жилет/ботинки
        'POUCH_C': 0.6, 'POUCH_A': 0.6, 'POUCH_B': 0.6,
        # возвращённые 2026-10-01: сумка на животе и навесное каски (мелочь < 300 tris не трогается)
        'SEModelMesh_146': 0.65, 'SEModelMesh_183': 0.6, 'SEModelMesh_184': 0.6, 'SEModelMesh_186': 0.6,
        'SEModelMesh_187': 0.6,
        'SEModelMesh_205': 0.6, 'SEModelMesh_206': 0.6,   # предплечья
        'SEModelMesh_180': 0.65,   # балаклава
        'SEModelMesh_181': 0.6,    # очки
        'SEModelMesh_185': 0.6}    # лицо
# не трогались: 179 каска (722), 182 наушники (564), 141 ремень (402), 194 стекло очков (74)
for n, t in PLAN.items():
    o = bpy.data.objects[n]; m0 = metrics(o)
    decimate(o, t, rings=1, border_weight=0.0)
    m1 = metrics(o)
    ok = m1['parts'] <= m0['parts'] and abs(m1['bnd'] - m0['bnd']) <= max(2, 0.02 * m0['bnd']) and m1['nonman'] <= m0['nonman']
    print(n, m0, '->', m1, '' if ok else '  <-- CHECK')

print('zero-weight', {o.name: sum(1 for v in o.data.vertices if sum(g.weight for g in v.groups) < 1e-6) for o in bpy.data.objects if o.type == 'MESH'})
rows = [(o.name, tris(o)) for o in bpy.data.objects if o.type == 'MESH']
print(sorted(rows), 'TOTAL', sum(r[1] for r in rows))
save_checkpoint('mef_stageB_repro.blend')
