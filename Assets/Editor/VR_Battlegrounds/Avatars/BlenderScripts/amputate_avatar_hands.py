import bpy
import bmesh
import re
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
    for mat in list(bpy.data.materials):
        bpy.data.materials.remove(mat, do_unlink=True)
    for img in list(bpy.data.images):
        bpy.data.images.remove(img, do_unlink=True)


def dot_product(v1, v2):
    return v1.x * v2.x + v1.y * v2.y + v1.z * v2.z


def seal_sleeves(bm, vgs_dict, arm_obj):
    deform = bm.verts.layers.deform.active or bm.verts.layers.deform.verify()

    lowerarm_indices = set()
    hand_indices = set()
    for bone in arm_obj.data.bones:
        bname = bone.name.lower()
        if "lowerarm" in bname and bone.name in vgs_dict:
            lowerarm_indices.add(vgs_dict[bone.name])
        if any(k in bname for k in ["hand", "thumb", "index", "middle", "ring", "pinky"]) and bone.name in vgs_dict:
            hand_indices.add(vgs_dict[bone.name])

    if not lowerarm_indices:
        return

    boundary_edges = [e for e in bm.edges if e.is_boundary]
    sleeve_edges = []
    for e in boundary_edges:
        is_sleeve = False
        for v in e.verts:
            dvert = v[deform]
            w = sum(weight for g_idx, weight in dvert.items() if g_idx in lowerarm_indices or g_idx in hand_indices)
            if w > 0.01:
                is_sleeve = True
                break
        if is_sleeve:
            sleeve_edges.append(e)

    if sleeve_edges:
        import mathutils

        edges_set = set(sleeve_edges)
        islands = []

        while edges_set:
            start_edge = edges_set.pop()
            island = [start_edge]
            verts_to_check = [start_edge.verts[0], start_edge.verts[1]]
            while verts_to_check:
                curr_v = verts_to_check.pop()
                for e in curr_v.link_edges:
                    if e in edges_set:
                        edges_set.remove(e)
                        island.append(e)
                        verts_to_check.extend([v for v in e.verts if v != curr_v])
            islands.append(island)

        for island in islands:
            res = bmesh.ops.extrude_edge_only(bm, edges=island)
            new_verts = [v for v in res["geom"] if isinstance(v, bmesh.types.BMVert)]
            if new_verts:
                center = mathutils.Vector((0, 0, 0))
                for v in new_verts:
                    center += v.co
                center /= len(new_verts)
                for v in new_verts:
                    v.co = center

                orig_v = island[0].verts[0]
                orig_weights = orig_v[deform]
                for v in new_verts:
                    v[deform].clear()
                    for g_idx, weight in orig_weights.items():
                        v[deform][g_idx] = weight

                bmesh.ops.remove_doubles(bm, verts=new_verts, dist=0.001)


def amputate_hands(mesh_obj, arm_obj):
    vgs = mesh_obj.vertex_groups
    mesh = mesh_obj.data
    hand_bones = []
    lowerarm_bones = []

    for bone in arm_obj.data.bones:
        bname = bone.name.lower()
        if any(keyword in bname for keyword in ["hand", "thumb", "index", "middle", "ring", "pinky", "weapon"]):
            hand_bones.append(bone.name)
        if "lowerarm" in bname:
            lowerarm_bones.append(bone.name)

    hand_vg_indices = set()
    for bname in hand_bones:
        vg = vgs.get(bname)
        if vg:
            hand_vg_indices.add(vg.index)

    lowerarm_vg_indices = set()
    for bname in lowerarm_bones:
        vg = vgs.get(bname)
        if vg:
            lowerarm_vg_indices.add(vg.index)

    cutoffs = []
    for side in ["l", "r"]:
        lowerarm = arm_obj.data.bones.get(f"lowerarm_{side}") or arm_obj.data.bones.get(f"LowerArm_{side.upper()}")
        hand = arm_obj.data.bones.get(f"hand_{side}") or arm_obj.data.bones.get(f"Hand_{side.upper()}")

        if not lowerarm:
            for b in arm_obj.data.bones:
                if "lowerarm" in b.name.lower() and side in b.name.lower():
                    lowerarm = b
                    break
        if not hand:
            for b in arm_obj.data.bones:
                if "hand" in b.name.lower() and side in b.name.lower() and "ik" not in b.name.lower():
                    hand = b
                    break

        if lowerarm and hand:
            arm_mat = arm_obj.matrix_world
            la_head = arm_mat @ lowerarm.head_local
            hand_head = arm_mat @ hand.head_local

            vec = hand_head - la_head
            length = vec.length
            if length > 0:
                direction = vec / length
                cutoff_pt = hand_head + direction * 0.01
                cutoffs.append((cutoff_pt, direction))

    if not hand_vg_indices and not cutoffs:
        return

    bm = bmesh.new()
    bm.from_mesh(mesh)
    deform = bm.verts.layers.deform.active
    if not deform:
        deform = bm.verts.layers.deform.verify()

    verts_to_delete = []
    mat_world = mesh_obj.matrix_world

    for bv in bm.verts:
        delete_this = False

        dvert = bv[deform]
        w_hand = 0.0
        w_lowerarm = 0.0
        for group_idx, weight in dvert.items():
            if group_idx in hand_vg_indices:
                w_hand += weight
            elif group_idx in lowerarm_vg_indices:
                w_lowerarm += weight

        if w_hand > 0.95:
            delete_this = True

        if not delete_this and (w_hand > 0.1 or w_lowerarm > 0.1):
            vp = mat_world @ bv.co
            for cutoff_pt, direction in cutoffs:
                if dot_product(vp - cutoff_pt, direction) > 0:
                    delete_this = True
                    break

        if delete_this:
            verts_to_delete.append(bv)

    print(f"Mesh {mesh_obj.name}: Selected {len(verts_to_delete)} hand/glove vertices for amputation.")

    if verts_to_delete:
        bmesh.ops.delete(bm, geom=verts_to_delete, context="VERTS")

    vgs_dict = {vg.name: vg.index for vg in mesh_obj.vertex_groups}
    seal_sleeves(bm, vgs_dict, arm_obj)

    bm.to_mesh(mesh)
    mesh.update()
    bm.free()


def merge_duplicate_material_names():
    for mat in bpy.data.materials:
        match = re.match(r"^(.*?)(\.\d{3})$", mat.name)
        if match:
            base_name = match.group(1)
            base_mat = bpy.data.materials.get(base_name)
            if base_mat:
                mat.user_remap(base_mat)
            else:
                mat.name = base_name


def main():
    filename = get_fbx_path()
    deep_clean()
    bpy.ops.import_scene.fbx(filepath=filename)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError("No armature found in FBX.")
    arm_obj = armatures[0]

    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="OBJECT")

    for obj in bpy.data.objects:
        if obj.type == "MESH":
            amputate_hands(obj, arm_obj)

    merge_duplicate_material_names()

    for obj in bpy.data.objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=filename, use_selection=True, bake_anim=False, add_leaf_bones=False)
    print(f"Success! Hands amputated and FBX exported cleanly: {filename}")


if __name__ == "__main__":
    main()
