import bpy
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


def process_side(mesh_obj, armature_obj, side_suffix):
    vgs = mesh_obj.vertex_groups
    lowerarm_name = f"lowerarm_{side_suffix}"
    hand_name = f"hand_{side_suffix}"
    if lowerarm_name not in vgs or hand_name not in vgs:
        print(f"Skipping {mesh_obj.name} side {side_suffix}: missing {lowerarm_name} or {hand_name} vertex group.")
        return

    twist_names = [f"lowerarm_twist_0{i}_{side_suffix}" for i in range(1, 4)]

    # lowerarm + three twist bones distribute the old forearm/hand weight gradually.
    g_names = [lowerarm_name] + twist_names
    centers = [0.0, 0.25, 0.50, 0.75]

    g_indices = []
    for gn in g_names:
        vg = vgs.get(gn)
        if vg is None:
            vg = vgs.new(name=gn)
        g_indices.append(vg.index)

    bpy.context.view_layer.objects.active = armature_obj
    bpy.ops.object.mode_set(mode="EDIT")
    amt = armature_obj.data
    l_bone = amt.edit_bones.get(lowerarm_name)
    h_bone = amt.edit_bones.get(hand_name)
    if not (l_bone and h_bone):
        bpy.ops.object.mode_set(mode="OBJECT")
        print(f"Skipping side {side_suffix}: missing {lowerarm_name} or {hand_name} edit bone.")
        return

    for name in twist_names:
        if name in amt.edit_bones:
            amt.edit_bones.remove(amt.edit_bones[name])

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

    bpy.ops.object.mode_set(mode="OBJECT")

    mesh_matrix = armature_obj.matrix_world.inverted() @ mesh_obj.matrix_world
    elbow_pos = pos_start
    wrist_pos = pos_end
    arm_axis = wrist_pos - elbow_pos
    arm_len = arm_axis.length
    if arm_len < 0.001:
        return
    arm_dir = arm_axis.normalized()

    hand_idx = vgs[hand_name].index

    for v in mesh_obj.data.vertices:
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

            for g_idx in g_indices + [hand_idx]:
                try:
                    vgs[g_idx].remove([v.index])
                except RuntimeError:
                    pass

            for i in range(len(centers) - 1):
                c1, c2 = centers[i], centers[i + 1]
                if c1 <= t <= c2:
                    f = (t - c1) / (c2 - c1)
                    w1, w2 = w_total * (1.0 - f), w_total * f
                    if w1 > 0.001:
                        vgs[g_indices[i]].add([v.index], w1, "REPLACE")
                    if w2 > 0.001:
                        vgs[g_indices[i + 1]].add([v.index], w2, "REPLACE")
                    break
            else:
                if t >= 0.75:
                    vgs[g_indices[-1]].add([v.index], w_total, "REPLACE")


def main():
    filename = get_fbx_path()
    deep_clean()
    bpy.ops.import_scene.fbx(filepath=filename)

    armatures = [o for o in bpy.data.objects if o.type == "ARMATURE"]
    if not armatures:
        raise RuntimeError("No armature found in FBX.")
    arm_obj = armatures[0]

    for obj in bpy.data.objects:
        if obj.type == "MESH":
            process_side(obj, arm_obj, "l")
            process_side(obj, arm_obj, "r")

    for obj in bpy.data.objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=filename, use_selection=True, bake_anim=False, add_leaf_bones=False)
    print(f"Success! Torsion bones injected and FBX exported cleanly: {filename}")


if __name__ == "__main__":
    main()
