"""Render a rigged monster FBX in each of its clips, without Unity: the rest pose
from the front and the side, then one frame per action. For judging a rig and its
clips before the editor is available.

  blender -b -P tools/blender/shoot_rigged.py -- <rigged.fbx> <out_dir> [label]
"""
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
path, out = os.path.abspath(args[0]), os.path.abspath(args[1])
label = args[2] if len(args) > 2 else os.path.splitext(os.path.basename(path))[0]
os.makedirs(out, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
actions = sorted(a.name for a in bpy.data.actions)
print("mesh", [m.name for m in meshes], "armature", [a.name for a in arms], "actions", actions)

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x = 800
scene.render.resolution_y = 800
world = bpy.data.worlds.new("W"); scene.world = world; world.color = (0.05, 0.06, 0.09)

lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
for m in meshes:
    for corner in m.bound_box:
        w = m.matrix_world @ Vector(corner)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
centre = (lo + hi) / 2
dist = max(hi - lo) * 1.7
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
cam_data.lens = 50


def shoot(name, offset):
    cam.location = centre + offset
    cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, label + "-" + name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", scene.render.filepath)


three_quarter = Vector((dist * 0.62, -dist * 0.72, dist * 0.12))
shoot("front", Vector((0, -dist, 0)))
shoot("side", Vector((dist, 0, 0)))

arm = arms[0] if arms else None
if arm is not None:
    ad = arm.animation_data or arm.animation_data_create()
    for name in actions:
        act = bpy.data.actions[name]
        pose = name.rsplit("|", 1)[-1]  # the exporter names a take after its object: "Armature|Idle"
        ad.action = act
        try:
            slots = list(act.slots)
            if slots:
                ad.action_slot = slots[0]
        except AttributeError:
            pass
        start, end = act.frame_range
        scene.frame_set(int(start + (end - start) * (0.25 if pose in ("Shooting", "Windup") else 0.4)))
        bpy.context.view_layer.update()
        shoot(pose, three_quarter)
    ad.action = None
