import bpy
import mathutils
import sys


def get_fbx_path():
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1:]
        if args:
            return args[0]
    raise RuntimeError("FBX path argument is required after '--'.")


def deep_clean():
    if bpy.context.active_object and bpy.context.active_object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for m in list(bpy.data.meshes):
        bpy.data.meshes.remove(m, do_unlink=True)
    for a in list(bpy.data.armatures):
        bpy.data.armatures.remove(a, do_unlink=True)
    for img in list(bpy.data.images):
        bpy.data.images.remove(img, do_unlink=True)
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat, do_unlink=True)


def find_head_bone(amt):
    for candidate in ["head", "Head", "HEAD"]:
        bone = amt.edit_bones.get(candidate)
        if bone:
            return bone
    for bone in amt.edit_bones:
        if "head" in bone.name.lower():
            return bone
    return None


def add_eye_bones(armature_obj):
    bpy.context.view_layer.objects.active = armature_obj
    bpy.ops.object.mode_set(mode="EDIT")
    amt = armature_obj.data

    head_bone = find_head_bone(amt)
    if not head_bone:
        raise RuntimeError("Head bone was not found.")

    eye_meshes = [child for child in armature_obj.children if child.type == "MESH" and "eye" in child.name.lower()]

    left_eye_pos_global = None
    right_eye_pos_global = None

    if eye_meshes:
        print("Found eye meshes:", [m.name for m in eye_meshes])
        left_verts = []
        right_verts = []

        for mesh_obj in eye_meshes:
            mat_world = mesh_obj.matrix_world
            for v in mesh_obj.data.vertices:
                global_pos = mat_world @ v.co
                if global_pos.x > 0.005:
                    left_verts.append(global_pos)
                elif global_pos.x < -0.005:
                    right_verts.append(global_pos)

        if left_verts and right_verts:
            left_eye_pos_global = sum(left_verts, mathutils.Vector()) / len(left_verts)
            right_eye_pos_global = sum(right_verts, mathutils.Vector()) / len(right_verts)
            print("Eye positions:", left_eye_pos_global, right_eye_pos_global)

            left_eye_pos_global.y -= 0.015
            right_eye_pos_global.y -= 0.015

    left_eye_pos_local = None
    right_eye_pos_local = None

    if left_eye_pos_global and right_eye_pos_global:
        arm_mat_inv = armature_obj.matrix_world.inverted()
        left_eye_pos_local = arm_mat_inv @ left_eye_pos_global
        right_eye_pos_local = arm_mat_inv @ right_eye_pos_global

    if not left_eye_pos_local:
        print("WARNING: Eye meshes were not found or could not be split. Using approximate head-local positions.")
        local_matrix = head_bone.matrix.copy()
        left_eye_pos_local = local_matrix @ mathutils.Vector((-0.035, 0.05, 0.1))
        right_eye_pos_local = local_matrix @ mathutils.Vector((0.035, 0.05, 0.1))

    def create_eye(name, local_pos):
        if name in amt.edit_bones:
            amt.edit_bones.remove(amt.edit_bones[name])
        eye = amt.edit_bones.new(name)
        eye.parent = head_bone
        eye.head = local_pos

        tail_pos = local_pos.copy()
        scale_y = armature_obj.scale.y if abs(armature_obj.scale.y) > 0.0001 else 1.0
        tail_pos.y -= 0.02 * (1.0 / scale_y)

        eye.tail = tail_pos
        eye.use_deform = True
        print(f"Bone {name} set to {eye.head}")

    create_eye("LeftEye", left_eye_pos_local)
    create_eye("RightEye", right_eye_pos_local)

    bpy.ops.object.mode_set(mode="OBJECT")

    # Give Unity a tiny skin weight reference so imported humanoid skeleton keeps the new bones.
    for child in armature_obj.children:
        if child.type == "MESH":
            vg_left = child.vertex_groups.get("LeftEye") or child.vertex_groups.new(name="LeftEye")
            vg_right = child.vertex_groups.get("RightEye") or child.vertex_groups.new(name="RightEye")
            if len(child.data.vertices) > 0:
                vg_left.add([0], 0.001, "REPLACE")
                vg_right.add([0], 0.001, "REPLACE")


def main():
    filename = get_fbx_path()
    deep_clean()
    bpy.ops.import_scene.fbx(filepath=filename)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError("No armature found in FBX.")
    add_eye_bones(armatures[0])

    for obj in bpy.data.objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=filename, use_selection=True, bake_anim=False, add_leaf_bones=False)
    print(f"Success! Eye bones injected and FBX exported cleanly: {filename}")


if __name__ == "__main__":
    main()
