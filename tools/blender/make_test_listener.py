"""A rigged test Listener for the monster model pipeline (docs/MONSTER_MODELS.md).

Not the real look — a stand-in built the way Dan's Blender models must be built,
so the import pipeline is proven end to end before his first FBX: feet at the
origin, facing -Y (Unity's +Z after the exporter's -Z/Y axes), 1 unit = 1 m, an
armature with a few bones, vertex groups by bone, two empties parented to the
head bone (BeamOrigin, Voice), one action per CreaturePose it animates (Idle,
Drawn, Hunting, Shooting), no eyes (the Listener is blind).

Run headless:
  blender -b -P tools/blender/make_test_listener.py -- Assets/_Project/Models/Monsters/Listener/Listener.fbx
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

FPS = 30


def out_path():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return os.path.abspath(args[0] if args else "Assets/_Project/Models/Monsters/Listener/Listener.fbx")


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = FPS


def material(name, rgba, emission=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = rgba
    bsdf.inputs["Roughness"].default_value = 0.75
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = emission
        bsdf.inputs["Emission Strength"].default_value = 1.0
    m.diffuse_color = rgba
    return m


def primitive(op, name, **kwargs):
    op(**kwargs)
    obj = bpy.context.active_object
    obj.name = name
    return obj


def build_mesh(skin, ear):
    parts = []
    # A tall drum of a body from the hips up, two stumpy legs, a head, two fanned ears.
    body = primitive(bpy.ops.mesh.primitive_cylinder_add, "BodyPart", vertices=24, radius=0.5, depth=1.2, location=(0, 0, 1.1))
    parts.append(body)
    for sx in (-0.22, 0.22):
        leg = primitive(bpy.ops.mesh.primitive_cylinder_add, "LegPart", vertices=12, radius=0.14, depth=0.5, location=(sx, 0, 0.25))
        parts.append(leg)
    head = primitive(bpy.ops.mesh.primitive_uv_sphere_add, "HeadPart", segments=24, ring_count=12, radius=0.32, location=(0, -0.05, 1.95))
    head.scale = (1.0, 1.15, 0.9)
    parts.append(head)
    ears = []
    for sx, name in ((-1, "EarLPart"), (1, "EarRPart")):
        e = primitive(bpy.ops.mesh.primitive_circle_add, name, vertices=20, radius=0.42, fill_type="NGON", location=(sx * 0.55, 0.0, 2.05))
        e.rotation_euler = (0.0, math.radians(90 - sx * 20), 0.0)
        e.scale = (1.0, 1.35, 1.0)
        ears.append(e)
    for e in ears:
        bpy.context.view_layer.objects.active = e
        e.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_all(action="SELECT")
        bpy.ops.mesh.solidify(thickness=0.04)
        bpy.ops.object.mode_set(mode="OBJECT")
        e.select_set(False)
    parts += ears
    for p in parts:
        p.data.materials.append(ear if p.name.startswith("Ear") else skin)
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    mesh = bpy.context.active_object
    mesh.name = "Listener"
    mesh.data.name = "Listener"
    bpy.ops.object.shade_smooth()
    return mesh


def build_armature():
    arm_data = bpy.data.armatures.new("Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    bpy.context.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None, connected=False):
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent is not None:
            b.parent = parent
            b.use_connect = connected
        return b

    root = bone("Root", (0, 0, 0), (0, 0, 0.5))
    spine = bone("Spine", (0, 0, 0.5), (0, 0, 1.7), root, True)
    head = bone("Head", (0, 0, 1.7), (0, 0, 2.3), spine, True)
    bone("EarL", (-0.35, 0, 2.05), (-0.95, 0, 2.15), head)
    bone("EarR", (0.35, 0, 2.05), (0.95, 0, 2.15), head)
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm


def skin(mesh, arm):
    groups = {name: mesh.vertex_groups.new(name=name) for name in ("Root", "Spine", "Head", "EarL", "EarR")}
    for v in mesh.data.vertices:
        x, z = v.co.x, v.co.z
        if z > 1.6 and x < -0.36:
            name = "EarL"
        elif z > 1.6 and x > 0.36:
            name = "EarR"
        elif z > 1.65:
            name = "Head"
        elif z > 0.5:
            name = "Spine"
        else:
            name = "Root"
        groups[name].add([v.index], 1.0, "REPLACE")
    mod = mesh.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh.parent = arm


def anchor(name, world, arm, bone_name):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    empty.empty_display_size = 0.1
    bpy.context.collection.objects.link(empty)
    empty.parent = arm
    empty.parent_type = "BONE"
    empty.parent_bone = bone_name
    bpy.context.view_layer.update()
    empty.matrix_world = Matrix.Translation(Vector(world))
    return empty


def action(arm, name, frames, keys):
    """keys: {bone: [(frame, (rx, ry, rz) degrees, (lx, ly, lz))]}"""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        slot = act.slots.new(id_type="OBJECT", name=arm.name)
        ad.action_slot = slot
    except (AttributeError, TypeError):
        pass
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    for bone_name, frame_keys in keys.items():
        pb = arm.pose.bones[bone_name]
        for frame, rot, loc in frame_keys:
            pb.rotation_euler = tuple(math.radians(a) for a in rot)
            pb.location = loc
            pb.keyframe_insert("rotation_euler", frame=frame)
            pb.keyframe_insert("location", frame=frame)
    act.frame_range = (1, frames)
    try:
        act.use_frame_range = True
    except AttributeError:
        pass
    ad.action = None  # keyframes interpolate Bezier by default; Blender 5's layered actions keep the curves under the slot

    return act


def animate(arm):
    z = (0, 0, 0)
    # Idle: the ears fan slowly, the head sways — a thing listening. 2 s loop.
    action(arm, "Idle", 60, {
        "EarL": [(1, (0, 0, 0), z), (30, (0, 12, 0), z), (60, (0, 0, 0), z)],
        "EarR": [(1, (0, 0, 0), z), (30, (0, -12, 0), z), (60, (0, 0, 0), z)],
        "Head": [(1, (0, 0, -4), z), (30, (0, 0, 4), z), (60, (0, 0, -4), z)],
    })
    # Drawn: a walking bob of the spine, the head cocked toward the sound. 1 s loop.
    action(arm, "Drawn", 30, {
        "Spine": [(1, (0, 0, 0), (0, 0, 0)), (8, (2, 0, 0), (0, 0.03, 0)), (15, (0, 0, 0), (0, 0, 0)), (23, (2, 0, 0), (0, 0.03, 0)), (30, (0, 0, 0), (0, 0, 0))],
        "Head": [(1, (0, 0, 10), z), (30, (0, 0, 10), z)],
        "EarL": [(1, (0, 6, 0), z), (15, (0, 14, 0), z), (30, (0, 6, 0), z)],
        "EarR": [(1, (0, -6, 0), z), (15, (0, -14, 0), z), (30, (0, -6, 0), z)],
    })
    # Hunting: the same walk, quicker and lower. 0.6 s loop.
    action(arm, "Hunting", 18, {
        "Spine": [(1, (4, 0, 0), (0, 0, 0)), (5, (6, 0, 0), (0, 0.05, 0)), (9, (4, 0, 0), (0, 0, 0)), (14, (6, 0, 0), (0, 0.05, 0)), (18, (4, 0, 0), (0, 0, 0))],
        "Head": [(1, (8, 0, 0), z), (18, (8, 0, 0), z)],
        "EarL": [(1, (0, 20, 0), z), (18, (0, 20, 0), z)],
        "EarR": [(1, (0, -20, 0), z), (18, (0, -20, 0), z)],
    })
    # Shooting: the head thrusts forward and the ears flare; plays once. 0.5 s.
    action(arm, "Shooting", 15, {
        "Head": [(1, (0, 0, 0), (0, 0, 0)), (4, (-18, 0, 0), (0, 0.12, 0)), (15, (0, 0, 0), (0, 0, 0))],
        "Spine": [(1, (0, 0, 0), z), (4, (-6, 0, 0), z), (15, (0, 0, 0), z)],
        "EarL": [(1, (0, 0, 0), z), (4, (0, 35, 0), z), (15, (0, 0, 0), z)],
        "EarR": [(1, (0, 0, 0), z), (4, (0, -35, 0), z), (15, (0, 0, 0), z)],
    })


def export(path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=path,
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
        mesh_smooth_type="FACE",
        path_mode="AUTO",
        embed_textures=False,
    )


def main():
    path = out_path()
    clear()
    skin_mat = material("ListenerSkin", (0.08, 0.10, 0.13, 1.0))
    ear_mat = material("ListenerEar", (0.10, 0.45, 0.42, 1.0), emission=(0.05, 0.35, 0.32, 1.0))
    mesh = build_mesh(skin_mat, ear_mat)
    arm = build_armature()
    skin(mesh, arm)
    anchor("BeamOrigin", (0, -0.42, 1.95), arm, "Head")
    anchor("Voice", (0, 0, 1.9), arm, "Head")
    animate(arm)
    export(path)
    print("wrote", path)


main()
