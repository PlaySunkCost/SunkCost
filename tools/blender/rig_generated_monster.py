"""Rig any generated monster mesh (Meshy and the like) for the game
(docs/MONSTER_MODELS.md): frame it, decimate it, build the kind's armature,
skin it, hang BeamOrigin and Voice on the head, key the kind's clips, and
export the FBX the prefab wears.

  blender -b -P tools/blender/rig_generated_monster.py -- <Kind> <generated.fbx|.glb> <out.fbx> [target_faces]

Kind is a MonsterKind name: Listener, Lure, LongWalker, WeepingAngel, Charger.
The armature is one layout — root, hips, spine, neck, head, arms, digitigrade
legs, and a pair of side bones for the ears or the lantern — stretched to the
kind's height and build, so a mesh from any generation gets the bones its clips
expect. The clips come from make_listener.py's library, which keys by bone name.
"""
import os
import sys

import math
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_listener as L  # noqa: E402

# Per kind: the height the game gives it, how wide it is built, where the side
# bones sit (ears, a lantern stalk), and which clips it needs.
KINDS = {
    #                 height  build  side bones   four legs  clips
    "Listener":      (2.0,    1.00,  "ears",      False,     ("Idle", "Drawn", "Hunting", "Shooting")),
    "Lure":          (2.2,    0.92,  "lantern",   False,     ("Idle", "Drawn", "Hunting", "Shooting")),
    "LongWalker":    (3.2,    0.78,  None,        False,     ("Idle", "Drawn", "Hunting")),
    "WeepingAngel":  (2.1,    1.05,  None,        False,     ("Idle", "Hunting", "Frozen")),
    "Charger":       (1.4,    1.45,  None,        True,      ("Idle", "Hunting", "Windup", "Rushing")),
}


def build_armature(height, build, side, four_legs=False, length=None):
    """The Listener's layout scaled to this kind: the fractions are of the height,
    the widths of the height times the build factor. A four-legged kind (the
    Charger) lays the spine horizontally and puts the arm bones down as front legs,
    keeping the same bone names so the clips key it without a second animation set."""
    if four_legs:
        return build_quadruped(height, build, length)
    h, w = height, height * build
    arm_data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    L.BONES.clear()

    def bone(name, head, tail, parent=None, connected=False):
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent is not None:
            b.parent = arm_data.edit_bones[parent]
            b.use_connect = connected
        L.BONES[name] = (Vector(head), Vector(tail))
        return b

    bone("Root", (0, 0, 0), (0, 0, 0.52 * h))
    bone("Hips", (0, 0, 0.52 * h), (0, 0, 0.61 * h), "Root", True)
    bone("Spine", (0, 0, 0.61 * h), (0, 0, 0.78 * h), "Hips", True)
    bone("Neck", (0, 0, 0.78 * h), (0, 0.005 * h, 0.86 * h), "Spine", True)
    bone("Head", (0, 0.005 * h, 0.86 * h), (0, 0, h), "Neck", True)
    for s, tag in ((-1, "L"), (1, "R")):
        if side == "ears":
            bone("Ear" + tag, (s * 0.065 * w, 0, 0.93 * h), (s * 0.25 * w, -0.03 * h, 1.01 * h), "Head")
        elif side == "lantern":
            # One stalk over the head; the pair keeps the clips' bone names working.
            bone("Ear" + tag, (s * 0.03 * w, -0.02 * h, 0.94 * h), (s * 0.08 * w, -0.14 * h, 1.06 * h), "Head")
        bone("UpperArm" + tag, (s * 0.135 * w, 0, 0.775 * h), (s * 0.185 * w, 0.02 * h, 0.575 * h), "Spine")
        bone("LowerArm" + tag, (s * 0.185 * w, 0.02 * h, 0.575 * h), (s * 0.175 * w, -0.02 * h, 0.38 * h), "UpperArm" + tag, True)
        bone("Hand" + tag, (s * 0.175 * w, -0.02 * h, 0.38 * h), (s * 0.16 * w, -0.07 * h, 0.25 * h), "LowerArm" + tag, True)
        bone("Thigh" + tag, (s * 0.05 * w, 0, 0.51 * h), (s * 0.055 * w, -0.03 * h, 0.31 * h), "Hips")
        bone("Shin" + tag, (s * 0.055 * w, -0.03 * h, 0.31 * h), (s * 0.05 * w, 0.035 * h, 0.15 * h), "Thigh" + tag, True)
        bone("Foot" + tag, (s * 0.05 * w, 0.035 * h, 0.15 * h), (s * 0.05 * w, -0.10 * h, 0.018 * h), "Shin" + tag, True)
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    return arm


def build_quadruped(height, build, length=None):
    """A low, wide, four-legged body about twice as long as it is high (the
    Charger): the spine runs from the hips at the back to the head at the front,
    the "arms" are the front legs and the "thighs" the back ones. Same bone names
    as the two-legged layout, so the clips drive it unchanged — a walk swings the
    legs, the Charger's wind-up shakes the whole body.
    Facing −Y, so the head is at negative y and the tail at positive."""
    h = height
    length = length or h * 2.0 * (build / 1.45)  # the mesh's own front-to-back size when it is known
    w = h * build
    back, front = 0.42 * length, -0.42 * length  # y of the hips and the shoulders
    arm_data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    L.BONES.clear()

    def bone(name, head, tail, parent=None, connected=False):
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent is not None:
            b.parent = arm_data.edit_bones[parent]
            b.use_connect = connected
        L.BONES[name] = (Vector(head), Vector(tail))
        return b

    bone("Root", (0, back, 0), (0, back, 0.62 * h))
    bone("Hips", (0, back, 0.62 * h), (0, back * 0.45, 0.66 * h), "Root", True)
    bone("Spine", (0, back * 0.45, 0.66 * h), (0, front * 0.55, 0.68 * h), "Hips", True)
    bone("Neck", (0, front * 0.55, 0.68 * h), (0, front * 0.85, 0.60 * h), "Spine", True)
    bone("Head", (0, front * 0.85, 0.60 * h), (0, front * 1.20, 0.42 * h), "Neck", True)  # the lowered ram head
    for s, tag in ((-1, "L"), (1, "R")):
        # The front legs, under the shoulders.
        bone("UpperArm" + tag, (s * 0.22 * w, front * 0.62, 0.56 * h), (s * 0.24 * w, front * 0.66, 0.30 * h), "Spine")
        bone("LowerArm" + tag, (s * 0.24 * w, front * 0.66, 0.30 * h), (s * 0.24 * w, front * 0.60, 0.12 * h), "UpperArm" + tag, True)
        bone("Hand" + tag, (s * 0.24 * w, front * 0.60, 0.12 * h), (s * 0.24 * w, front * 0.80, 0.02 * h), "LowerArm" + tag, True)
        # The back legs, under the hips.
        bone("Thigh" + tag, (s * 0.24 * w, back * 0.72, 0.56 * h), (s * 0.26 * w, back * 0.80, 0.30 * h), "Hips")
        bone("Shin" + tag, (s * 0.26 * w, back * 0.80, 0.30 * h), (s * 0.26 * w, back * 0.70, 0.12 * h), "Thigh" + tag, True)
        bone("Foot" + tag, (s * 0.26 * w, back * 0.70, 0.12 * h), (s * 0.26 * w, back * 0.90, 0.02 * h), "Shin" + tag, True)
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    return arm


def prowl(arm):
    """The four-legged Hunting (24 September 2026): the upright monsters' Hunting
    pitches the spine 38 degrees and drops the root to crouch, which drove a
    horizontal body's head through the floor. The Charger stalks level instead, head
    a little low, all four legs walking in diagonal pairs: Rushing's gait, slower."""
    old = bpy.data.actions.get("Hunting")
    if old is not None:
        bpy.data.actions.remove(old)
    keys = {n: [] for n in ("Root", "Spine", "Neck", "Head", "ThighL", "ThighR", "ShinL", "ShinR", "UpperArmL", "UpperArmR", "LowerArmL", "LowerArmR")}
    for phase in (0.0, 0.5, 1.0):
        frame = 1 + int(phase * 23)
        s = math.cos(phase * 2 * math.pi)
        keys["Root"].append((frame, L.Z3, (0, 0, -0.01 * abs(s))))
        keys["Spine"].append((frame, L.R(2 * s, 0, 0), L.Z3))
        keys["Neck"].append((frame, L.R(-6, 0, 0), L.Z3))
        keys["Head"].append((frame, L.R(-6, 0, 0), L.Z3))
        keys["ThighL"].append((frame, L.R(-22 * s, 0, 0), L.Z3))
        keys["ThighR"].append((frame, L.R(22 * s, 0, 0), L.Z3))
        keys["ShinL"].append((frame, L.R(28 * max(0.0, s), 0, 0), L.Z3))
        keys["ShinR"].append((frame, L.R(28 * max(0.0, -s), 0, 0), L.Z3))
        keys["UpperArmL"].append((frame, L.R(22 * s, 0, 0), L.Z3))
        keys["UpperArmR"].append((frame, L.R(-22 * s, 0, 0), L.Z3))
        keys["LowerArmL"].append((frame, L.R(-14 * max(0.0, -s), 0, 0), L.Z3))
        keys["LowerArmR"].append((frame, L.R(-14 * max(0.0, s), 0, 0), L.Z3))
    L.action(arm, "Hunting", 24, keys)


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    kind, src, dst = args[0], os.path.abspath(args[1]), os.path.abspath(args[2])
    target_faces = int(args[3]) if len(args) > 3 else 20000
    if kind not in KINDS:
        raise SystemExit("unknown kind " + kind + "; one of " + ", ".join(KINDS))
    height, build, side, four_legs, clips = KINDS[kind]

    L.clear()
    if src.lower().endswith(".fbx"):
        bpy.ops.import_scene.fbx(filepath=src)
    else:
        bpy.ops.import_scene.gltf(filepath=src)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    if not meshes:
        raise SystemExit("no mesh in " + src)
    for o in bpy.data.objects:
        o.select_set(o.type == "MESH")
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    mesh = bpy.context.active_object
    for o in list(bpy.data.objects):
        if o is not mesh:
            bpy.data.objects.remove(o, do_unlink=True)
    mesh.name = mesh.data.name = kind

    # The frame: feet at the origin, the kind's height, centred on x and y.
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
    for v in mesh.data.vertices:
        lo = Vector(map(min, lo, v.co)); hi = Vector(map(max, hi, v.co))
    scale = height / (hi.z - lo.z)
    centre = (lo + hi) / 2
    for v in mesh.data.vertices:
        v.co = Vector(((v.co.x - centre.x) * scale, (v.co.y - centre.y) * scale, (v.co.z - lo.z) * scale))
    mesh.data.update()
    print("framed: scale %.4f, height %.2f" % (scale, (hi.z - lo.z) * scale))

    faces = len(mesh.data.polygons)
    if faces > target_faces:
        mod = mesh.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = target_faces / faces
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = mesh
        mesh.select_set(True)
        bpy.ops.object.modifier_apply(modifier="Decimate")
        print("decimated %d -> %d faces" % (faces, len(mesh.data.polygons)))
    # Smooth by angle, not flat: Meshy's normal map was baked for smooth normals, and
    # flat facets showed on every limb (24 September 2026).
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))

    framed_lo = Vector((1e9, 1e9, 1e9)); framed_hi = Vector((-1e9, -1e9, -1e9))
    for v in mesh.data.vertices:
        framed_lo = Vector(map(min, framed_lo, v.co)); framed_hi = Vector(map(max, framed_hi, v.co))
    measured_length = framed_hi.y - framed_lo.y
    print("framed body: %.2f wide, %.2f long, %.2f high" % (framed_hi.x - framed_lo.x, measured_length, framed_hi.z - framed_lo.z))
    arm = build_armature(height, build, side, four_legs, measured_length)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    weighted = False
    try:
        bpy.ops.object.parent_set(type="ARMATURE_AUTO")
        weighted = len(mesh.vertex_groups) > 0 and any(len(v.groups) > 0 for v in mesh.data.vertices[:200])
    except RuntimeError as e:
        print("bone heat failed:", e)
    if not weighted:
        print("falling back to nearest-bone weights")
        for g in list(mesh.vertex_groups):
            mesh.vertex_groups.remove(g)
        for m in list(mesh.modifiers):
            if m.type == "ARMATURE":
                mesh.modifiers.remove(m)
        L.skin_to(mesh, arm)
    else:
        print("automatic weights: %d groups" % len(mesh.vertex_groups))

    # Anything out past the skull at ear height belongs to its side bone alone:
    # bone heat shares a fan between the head and the neck, and it then shears.
    if side is not None:
        ear_l, ear_r = mesh.vertex_groups.get("EarL"), mesh.vertex_groups.get("EarR")
        if ear_l is not None and ear_r is not None:
            fixed = 0
            for v in mesh.data.vertices:
                if v.co.z > 0.81 * height and abs(v.co.x) > 0.06 * height * build:
                    for g in list(v.groups):
                        mesh.vertex_groups[g.group].remove([v.index])
                    (ear_l if v.co.x < 0 else ear_r).add([v.index], 1.0, "REPLACE")
                    fixed += 1
            print("side vertices pinned to their bones:", fixed)

    # The maps out under their role's name, so Unity's setup can find them.
    map_dir = os.path.join(os.path.dirname(dst), "Generated~", "maps")
    os.makedirs(map_dir, exist_ok=True)
    for mat in mesh.data.materials:
        if mat is None or not mat.use_nodes:
            continue
        bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf is None:
            continue

        def image_on(socket_name):
            sock = bsdf.inputs.get(socket_name)
            if sock is None or not sock.is_linked:
                return None
            node = sock.links[0].from_node
            if node.type == "NORMAL_MAP":
                col = node.inputs.get("Color")
                node = col.links[0].from_node if col is not None and col.is_linked else None
            return node.image if node is not None and node.type == "TEX_IMAGE" else None

        for socket, role in (("Base Color", "BaseColor"), ("Normal", "Normal"), ("Roughness", "Roughness"), ("Metallic", "Metallic")):
            image = image_on(socket)
            if image is None:
                continue
            path = os.path.join(map_dir, kind + "_" + role + ".png")
            image.save_render(path)
            image.name = kind + "_" + role
            image.filepath = image.filepath_raw = path
            if image.packed_file is not None:
                image.unpack(method="REMOVE")
            image.source = "FILE"
            image.reload()
            print("map", role, "=", os.path.basename(path), image.size[:])

    if four_legs:
        head_tip = L.BONES["Head"][1]
        L.anchor("BeamOrigin", (head_tip.x, head_tip.y - 0.05 * height, head_tip.z), arm, "Head")
        L.anchor("Voice", (head_tip.x, head_tip.y, head_tip.z + 0.05 * height), arm, "Head")
    else:
        L.anchor("BeamOrigin", (0, -0.085 * height, 0.89 * height), arm, "Head")
        L.anchor("Voice", (0, 0, 0.93 * height), arm, "Head")
    L.animate(arm)
    if four_legs:
        prowl(arm)
    # A kind's own clips (tools/blender/clips/<Kind>.py, 24 September 2026): its
    # animate(arm, height, L) replaces or adds actions, its CLIPS names the extra
    # ones to keep. One file per monster, so each can be reworked on its own.
    own = os.path.join(HERE, "clips", kind + ".py")
    if os.path.exists(own):
        import importlib.util
        spec = importlib.util.spec_from_file_location("clips_" + kind, own)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        module.animate(arm, height, L)
        clips = tuple(clips) + tuple(getattr(module, "CLIPS", ()))
        print("own clips from", own)
    # Only the clips this kind uses ride along; the rest are dropped before the export.
    for act in list(bpy.data.actions):
        if act.name not in clips:
            bpy.data.actions.remove(act)
    print("clips:", sorted(a.name for a in bpy.data.actions))

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_bones=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.0,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="OFF",
        path_mode="COPY",
        embed_textures=True,
    )
    blend = os.path.join(HERE, kind + "Generated.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    print("wrote", dst, "and", blend)


main()
