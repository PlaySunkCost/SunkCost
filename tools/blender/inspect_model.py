"""Inspect a generated model (Meshy and the like) before rigging: import it, print
its size, its counts, its materials and images, and render it from the front, the
side and the back so the facing is judged by eye.

  blender -b -P tools/blender/inspect_model.py -- <model.fbx|.glb> <out_dir>
"""
import math
import os
import sys

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
path, out = os.path.abspath(args[0]), os.path.abspath(args[1])
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
if path.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=path)
else:
    bpy.ops.import_scene.gltf(filepath=path)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
print("objects:", [(o.name, o.type, tuple(round(v, 3) for v in o.scale)) for o in bpy.data.objects])
lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
verts = faces = 0
for m in meshes:
    for corner in m.bound_box:
        w = m.matrix_world @ Vector(corner)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
    verts += len(m.data.vertices); faces += len(m.data.polygons)
    print("mesh", m.name, "verts", len(m.data.vertices), "faces", len(m.data.polygons), "materials", [s.material.name if s.material else None for s in m.material_slots], "uv", [u.name for u in m.data.uv_layers], "groups", len(m.vertex_groups))
size = hi - lo
print("bounds lo", tuple(round(v, 3) for v in lo), "hi", tuple(round(v, 3) for v in hi), "size", tuple(round(v, 3) for v in size))
print("armatures", [a.name for a in arms], "images", [(i.name, i.size[:], i.packed_file is not None) for i in bpy.data.images])
for mat in bpy.data.materials:
    nodes = mat.node_tree.nodes if mat.use_nodes else []
    print("material", mat.name, [(n.type, getattr(n.image, "name", None) if n.type == "TEX_IMAGE" else "") for n in nodes])

# Renders: workbench, flat colour + texture, from the front (-Y), the right side (+X) and the back (+Y).
scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x = 900; scene.render.resolution_y = 900
scene.render.film_transparent = False
world = bpy.data.worlds.new("W"); scene.world = world; world.color = (0.08, 0.09, 0.12)
centre = (lo + hi) / 2
dist = max(size) * 1.6
cam_data = bpy.data.cameras.new("Cam"); cam = bpy.data.objects.new("Cam", cam_data); scene.collection.objects.link(cam); scene.camera = cam
cam_data.lens = 50
for name, offset in (("front", Vector((0, -dist, 0))), ("side", Vector((dist, 0, 0))), ("back", Vector((0, dist, 0)))):
    cam.location = centre + offset
    direction = centre - cam.location
    cam.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, "inspect-" + name + ".png")
    bpy.ops.render.render(write_still=True)
    print("rendered", scene.render.filepath)
