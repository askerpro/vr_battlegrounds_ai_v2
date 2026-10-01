# 01_import.py — импорт исходного ASCII FBX (транскрипт: вызовы #5–#7, #24–#26, #184).
#
# bpy.ops.import_scene.fbx (старый Python-импортёр) падает: "ASCII FBX files are not supported".
# Никакой конвертации не было: в Blender 5.2 есть встроенный C++-импортёр на ufbx —
# bpy.ops.wm.fbx_import — он читает ASCII напрямую. Отдельный конвертер (FBX Converter и т.п.) не нужен.
exec(open(r"F:\UnityProjects\Vr_Battlegrounds_ai\Tools\mef-avatar\00_common.py", encoding="utf-8").read())

clear_scene()
bpy.ops.wm.fbx_import(filepath=SRC_FBX, use_anim=False)   # остальные параметры по умолчанию (global_scale 1, custom normals on)

missing = relink_textures()
print('missing images:', missing)   # должно быть [] (9 картинок берутся из Raider/Textures)

# Shape keys у всех 49 мешей — только Basis + V_None, не используются
for o in bpy.data.objects:
    if o.type == 'MESH' and o.data.shape_keys:
        o.shape_key_clear()

# Что получается после импорта (для справки):
#  MEF (Empty, scale 0.01, rot X 90°) -> Armature (Empty, rot X -89.98°) -> RL_BoneRoot (ARMATURE, 102 кости)
#  43 пустышки *_end на костях + SideRt, GameObject, prop_handGunHolster, ItemReciever
#  51 меш: SEModelMesh_136…207 (49 skinned) + handgunHolster_geo, strap_geo (жёстко на пустышках)
#  ~105 650 tris, 27 материалов. Голова (главная кость = CC_Base_Head или потомок) — 15 мешей 179–194.
tot = 0
for o in sorted([o for o in bpy.data.objects if o.type == 'MESH'], key=lambda o: o.name):
    t = tris(o); tot += t
    print(o.name, t, o.data.materials[0].name if o.data.materials else None)
print('total tris', tot, 'materials', len(bpy.data.materials))
