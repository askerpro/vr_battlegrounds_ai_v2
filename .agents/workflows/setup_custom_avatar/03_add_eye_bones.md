---
description: Blender script to add LeftEye and RightEye bones to the avatar's armature
---

# Add Eye Bones

Этот скрипт добавляет "виртуальные" кости глаз (`LeftEye` и `RightEye`) внутрь кости головы в Blender.
Благодаря этому, Unity при импорте FBX автоматически распознает их и замапит в Humanoid-риг, а UltimateXR (UxrAvatar) автоматически использует их для вычисления `Eyes Base Height` и `Eyes Forward Offset`, что убережет камеру от клиппинга кусков шлема или лица.

## Инструкция для ИИ
Если ты выполняешь этот шаг отдельно от других, загрузи FBX, выполни код ниже через `mcp_blender_execute_blender_code`, а потом экспортируй обратно. 
Если ты объединяешь шаги, просто добавь логику из этого Python-блока к своему скрипту перед финальным экспортом.

## Скрипт (Blender Python)
```python
import bpy
import mathutils

def add_eye_bones(armature_obj, head_bone_name="head"):
    bpy.context.view_layer.objects.active = armature_obj
    bpy.ops.object.mode_set(mode='EDIT')
    amt = armature_obj.data
    
    head_bone = amt.edit_bones.get(head_bone_name)
    if not head_bone:
        print(f"Кость головы '{head_bone_name}' не найдена!")
        return

    # --- УМНЫЙ АЛГОРИТМ ПОИСКА ГЛАЗ ---
    # Ищем меши глаз внутри арматуры
    eye_meshes = [child for child in armature_obj.children if child.type == 'MESH' and 'eye' in child.name.lower()]
    
    left_eye_pos_global = None
    right_eye_pos_global = None

    if eye_meshes:
        print("Найдены меши глаз:", [m.name for m in eye_meshes])
        left_verts = []
        right_verts = []
        
        # Получаем матрицу инверсии головы для оценки того, что слева, а что справа (в локальных координатах кости головы)
        # Это важно, так как ось локального 'вправо'/-'влево' у головы может быть любой (чаще всего это Z или X)
        mat_inv = head_bone.matrix.inverted()
        
        for mesh_obj in eye_meshes:
            mat_world = mesh_obj.matrix_world
            for v in mesh_obj.data.vertices:
                global_pos = mat_world @ v.co
                local_pos = mat_inv @ global_pos
                
                # По опыту для Unity-ориентированных FBX, левый глаз обычно имеет глобальный +X, а правый -X
                # Но универсальнее разделить их по какому-то признаку
                if global_pos.x > 0.005: 
                    left_verts.append(global_pos)
                elif global_pos.x < -0.005:
                    right_verts.append(global_pos)
        
        if left_verts and right_verts:
            left_eye_pos_global = sum(left_verts, mathutils.Vector()) / len(left_verts)
            right_eye_pos_global = sum(right_verts, mathutils.Vector()) / len(right_verts)
            print("Расчет завершен. Левый глаз (глобально):", left_eye_pos_global, "Правый глаз:", right_eye_pos_global)
            
            # Легкая корректировка: сместим кость чуть вперед от центра глазного яблока к поверхности зрачка
            left_eye_pos_global.y -= 0.015
            right_eye_pos_global.y -= 0.015

    left_eye_pos_local = None
    right_eye_pos_local = None

    if left_eye_pos_global and right_eye_pos_global:
        # Координаты редактируемых костей (edit_bones.head/tail) ожидаются в ЛОКАЛЬНОМ пространстве арматуры!
        # Если арматура отмасштабирована (scale 0.01), мы обязаны перевести точки обратно в локальное пространство.
        arm_mat_inv = armature_obj.matrix_world.inverted()
        left_eye_pos_local = arm_mat_inv @ left_eye_pos_global
        right_eye_pos_local = arm_mat_inv @ right_eye_pos_global

    # Если меши не найдены, fallback на хардкод
    if not left_eye_pos_local:
        print("ВНИМАНИЕ: Меши глаз не найдены или недостаточно вершин. Использую примерный хардкод.")
        local_matrix = head_bone.matrix.copy()
        left_eye_pos_local = local_matrix @ mathutils.Vector((-0.035, 0.05, 0.1))
        right_eye_pos_local = local_matrix @ mathutils.Vector((0.035, 0.05, 0.1))

    # Создание костей
    def create_eye(name, local_pos):
        if name in amt.edit_bones:
            amt.edit_bones.remove(amt.edit_bones[name])
        eye = amt.edit_bones.new(name)
        eye.parent = head_bone
        eye.head = local_pos
        
        # Хвост кости делаем немного впереди (направляем кость)
        tail_pos = local_pos.copy()
        
        # Направление зависит от локального пространства арматуры! В Unity Вперед - Z.
        # Если меш при импорте смотрит по -Y, двигаем по Y:
        # Для FBX лучше просто чуть-чуть сместить.
        tail_pos.y -= (0.02 * (1.0 / armature_obj.scale.y))
        
        eye.tail = tail_pos
        eye.use_deform = True # ВАЖНО для Unity
        print(f"Кость {name} установлена в позицию {eye.head}")
        
    create_eye("LeftEye", left_eye_pos_local)
    create_eye("RightEye", right_eye_pos_local)

    bpy.ops.object.mode_set(mode='OBJECT')
    
    # --- ХАК ДЛЯ UNITY (SKIN WEIGHTS) ---
    for child in armature_obj.children:
        if child.type == 'MESH':
            vg_left = child.vertex_groups.get("LeftEye") or child.vertex_groups.new(name="LeftEye")
            vg_right = child.vertex_groups.get("RightEye") or child.vertex_groups.new(name="RightEye")
            if len(child.data.vertices) > 0:
                vg_left.add([0], 0.001, 'REPLACE')
                vg_right.add([0], 0.001, 'REPLACE')

    print("Кости глаз успешно встроены по процедурным координатам и 'утяжелены'.")
```

## Шаг 3.2: Автоматическое назначение костей в Unity (C# Automap)
Поскольку Unity не всегда автоматически подхватывает новые кости глаз в Humanoid-аватаре (особенно если он обновляется, а не создается с нуля), необходимо вызвать метод C#, который модифицирует `.meta` файл и жестко прописывает маппинг.

**Инструкция для ИИ:**
Используй инструмент `mcp_unityMCP_execute_menu_item` и передай путь меню `Tools/VR Battlegrounds/Avatars/Map Eyes To FBX` (убедись, что скрипт `ApplyEyeMapping.cs` присутствует в проекте).
Это автоматически добавит структуру EyeMapping в `HumanDescription` и выполнит Reimport для FBX.
