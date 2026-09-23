"""One small three-quarter render of a model, for identifying a pile of downloads.

  blender -b -P tools/blender/thumbnail_model.py -- <model.fbx|.glb> <out.png>
"""
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
path, out = os.path.abspath(args[0]), os.path.abspath(args[1])
bpy.ops.wm.read_factory_settings(use_empty=True)
if path.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=path)
else:
    bpy.ops.import_scene.gltf(filepath=path)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if not meshes:
    print("NO MESH", path)
    raise SystemExit

lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
faces = 0
for m in meshes:
    faces += len(m.data.polygons)
    for corner in m.bound_box:
        w = m.matrix_world @ Vector(corner)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
size = hi - lo
print("SIZE %.2f x %.2f x %.2f  FACES %d" % (size.x, size.y, size.z, faces))

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x = scene.render.resolution_y = 420
world = bpy.data.worlds.new("W"); scene.world = world; world.color = (0.07, 0.08, 0.11)
centre = (lo + hi) / 2
dist = max(size) * 1.7
cam_data = bpy.data.cameras.new("C"); cam = bpy.data.objects.new("C", cam_data)
scene.collection.objects.link(cam); scene.camera = cam
cam_data.lens = 45
cam.location = centre + Vector((dist * 0.7, -dist * 0.85, dist * 0.45))
cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
scene.render.filepath = out
bpy.ops.render.render(write_still=True)
print("THUMB", out)
