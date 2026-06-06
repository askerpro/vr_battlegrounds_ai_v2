---
description: How to add UltimateXR compatible wrist torsion bones and fix forearm deformation for avatars
---

# Add Wrist Torsion Bones for UltimateXR

Если у аватара при вращении кистей (в VR) перекручивается предплечье ("эффект выжатого полотенца"), значит меш не приспособлен для `UxrWristTorsionIKSolver`. Ему не хватает распределенных twist-костей.

Этот воркфлоу описывает, как с помощью MCP инструмента `mcp_blender_execute_blender_code` (Python скрипта внутри Blender) автоматически сгенерировать 3 дополнительные twist-кости (`lowerarm_twist_02`, `03`, `04`) для левой и правой руки, и математически гладко перераспределить на них веса вершин (Skin Weights).

## Актуальный запуск через Unity Tools
Выдели FBX asset в Unity Project window и запусти:
`Tools/VR Battlegrounds/Avatars/Custom Avatar Pipeline/2. Add Wrist Torsion Bones`

Эта команда берет выбранный FBX, запускает Blender в background mode и выполняет актуальный скрипт:
`Assets/Editor/VR_Battlegrounds/Avatars/BlenderScripts/add_wrist_torsion_bones.py`

Ниже оставлен legacy-вариант для справки, если нужно выполнить код вручную через Blender MCP.

## Legacy: Подготовка скрипта
Скопируйте следующий Python-скрипт. Вам нужно будет только подставить абсолютный путь к вашему FBX в переменную `filename` и убедиться, что названия костей (`lowerarm`, `hand`) совпадают с вашим скелетом.

```python
import bpy
import re
import mathutils

def deep_clean():
    if bpy.context.active_object and bpy.context.active_object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes): bpy.data.meshes.remove(m, do_unlink=True)
    for a in list(bpy.data.armatures): bpy.data.armatures.remove(a, do_unlink=True)
    for img in list(bpy.data.images): bpy.data.images.remove(img, do_unlink=True)
    for mat in list(bpy.data.materials): bpy.data.materials.remove(mat, do_unlink=True)

def process_side(mesh_obj, armature_obj, side_suffix):
    mesh = mesh_obj.data
    vgs = mesh_obj.vertex_groups
    lowerarm_name = f"lowerarm_{side_suffix}"
    hand_name = f"hand_{side_suffix}"
    if lowerarm_name not in vgs or hand_name not in vgs:
        return
    
    twist_names = [f"lowerarm_twist_0{i}_{side_suffix}" for i in range(1, 4)]
    
    # We will use lowerarm and the 3 twists.
    # centers will map linearly: lowerarm is 0.0, twist_01 0.25, twist_02 0.5, twist_03 0.75, (hand is 1.0)
    g_names = [lowerarm_name] + twist_names
    centers = [0.0, 0.25, 0.50, 0.75]
    
    g_indices = []
    for gn in g_names:
        vg = vgs.get(gn)
        if vg is None: vg = vgs.new(name=gn)
        g_indices.append(vg.index)

    bpy.context.view_layer.objects.active = armature_obj
    bpy.ops.object.mode_set(mode='EDIT')
    amt = armature_obj.data
    l_bone = amt.edit_bones.get(lowerarm_name)
    h_bone = amt.edit_bones.get(hand_name)
    if not (l_bone and h_bone):
        bpy.ops.object.mode_set(mode='OBJECT')
        return

    # Delete existing twist bones
    for name in twist_names:
        if name in amt.edit_bones: amt.edit_bones.remove(amt.edit_bones[name])
        
    twist_bones = [amt.edit_bones.new(name) for name in twist_names]
    num_segments = len(twist_names) + 1
    pos_start = l_bone.head.copy()
    pos_end = h_bone.head.copy()
    
    for i, tb in enumerate(twist_bones):
        tb.parent = l_bone
        tb.use_deform = True
        fraction = (i + 1) / num_segments
        tb.head = pos_start.lerp(pos_end, fraction)
        tb.tail = pos_start.lerp(pos_end, fraction + 0.05)
        tb.roll = l_bone.roll

    bpy.ops.object.mode_set(mode='OBJECT')
    
    # Geometry Based Weight Painting
    mesh_matrix = armature_obj.matrix_world.inverted() @ mesh_obj.matrix_world
    elbow_pos = pos_start
    wrist_pos = pos_end
    arm_axis = wrist_pos - elbow_pos
    arm_len = arm_axis.length
    if arm_len < 0.001: return
    arm_dir = arm_axis.normalized()
    
    hand_idx = vgs[hand_name].index
    
    # Optional: we can clamp max gradient effect to wrists to not deform elbow much
    for v in mesh.vertices:
        v_arm_space = mesh_matrix @ v.co
        w_l = sum(g.weight for g in v.groups if g.group in g_indices)
        
        w_h = 0.0
        for g in v.groups:
            if g.group == hand_idx: 
                w_h = g.weight
                break
                
        w_total = w_l + w_h
                
        if w_total > 0.001:
            t = (v_arm_space - elbow_pos).dot(arm_dir) / arm_len
            t = max(0.0, min(1.0, t))
            
            # Clear old weights for twist bones, lowerarm AND hand
            for g_idx in g_indices + [hand_idx]:
                try: vgs[g_idx].remove([v.index])
                except RuntimeError: pass
                
            for i in range(len(centers) - 1):
                c1, c2 = centers[i], centers[i+1]
                if c1 <= t <= c2:
                    f = (t - c1) / (c2 - c1)
                    w1, w2 = w_total * (1.0 - f), w_total * f
                    if w1 > 0.001: vgs[g_indices[i]].add([v.index], w1, 'REPLACE')
                    if w2 > 0.001: vgs[g_indices[i+1]].add([v.index], w2, 'REPLACE')
                    break
            else:
                if t >= 0.75: vgs[g_indices[-1]].add([v.index], w_total, 'REPLACE')


def main():
    try:
        deep_clean()
        filename = r"F:\UnityProjects\Vr_Battlegrounds_ai\Assets\ThirdParty\Military Soldier Mega Bundle\Heavy Soldier\Mesh\Heavy_Soldier_Rig_Mask_Winter.fbx"
        bpy.ops.import_scene.fbx(filepath=filename)
        armatures = [o for o in bpy.data.objects if o.type == 'ARMATURE']
        if not armatures: return
        arm_obj = armatures[0]
        for obj in bpy.data.objects:
            if obj.type == 'MESH':
                process_side(obj, arm_obj, "l")
                process_side(obj, arm_obj, "r")
        for obj in bpy.data.objects: obj.select_set(True)
        bpy.ops.export_scene.fbx(filepath=filename, use_selection=True, bake_anim=False, add_leaf_bones=False)
        print("Success! Torsion bones injected and FBX exported cleanly.")
    except Exception as e:
        import traceback; print(traceback.format_exc())

main()
```

## Шаг 2: Выполнение через инструмент
Запросите AI запустить этот скрипт, используя инструмент `mcp_blender_execute_blender_code`. Скрипт выполняется напрямую в фоновом процессе Blender, загружает FBX, меняет его и пересохраняет.
