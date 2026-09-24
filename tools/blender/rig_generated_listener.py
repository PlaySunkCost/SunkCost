"""Rig a generated Listener (Meshy, from Dan's concept — 22 September 2026) with the
Listener's armature and its four clips from make_listener.py, and export it as the
prefab's model (docs/MONSTER_MODELS.md).

The generated mesh is put in the frame every monster model uses — feet at the
origin, 2.0 m to the top of the skull, facing −Y (checked by eye on
inspect-front.png) — decimated to a game-sized count, skinned with automatic
weights (nearest-bone weights if bone heat fails), given BeamOrigin and Voice on
the head bone, and keyed with Idle, Drawn, Hunting and Shooting. Textures stay
embedded.

  blender -b -P tools/blender/rig_generated_listener.py -- <generated.fbx> <out.fbx> [target_faces]
"""
import os
import sys

import math
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_listener as L  # noqa: E402

args = sys.argv[sys.argv.index("--") + 1:]
src, dst = os.path.abspath(args[0]), os.path.abspath(args[1])
target_faces = int(args[2]) if len(args) > 2 else 20000

L.clear()
if src.lower().endswith(".fbx"):
    bpy.ops.import_scene.fbx(filepath=src)
else:
    bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh in " + src)
# One mesh: join if the generator split it.
for o in bpy.data.objects:
    o.select_set(o.type == "MESH")
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
mesh = bpy.context.active_object
for o in list(bpy.data.objects):
    if o is not mesh:
        bpy.data.objects.remove(o, do_unlink=True)
mesh.name = "Listener"
mesh.data.name = "Listener"

# The frame: feet at the origin, HEIGHT tall, centred on x and y.
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
for v in mesh.data.vertices:
    lo = Vector(map(min, lo, v.co)); hi = Vector(map(max, hi, v.co))
scale = L.HEIGHT / (hi.z - lo.z)
centre = (lo + hi) / 2
for v in mesh.data.vertices:
    v.co = Vector(((v.co.x - centre.x) * scale, (v.co.y - centre.y) * scale, (v.co.z - lo.z) * scale))
mesh.data.update()
print("framed: scale %.4f, height %.2f" % (scale, (hi.z - lo.z) * scale))

# Game-sized: decimate to about target_faces triangles (the UVs and the texture survive a collapse).
faces = len(mesh.data.polygons)
if faces > target_faces:
    mod = mesh.modifiers.new("Decimate", "DECIMATE")
    mod.ratio = target_faces / faces
    mod.use_collapse_triangulate = True
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Decimate")
    print("decimated %d -> %d faces" % (faces, len(mesh.data.polygons)))
# Smooth by angle, not flat (24 September 2026): Meshy's normal map was baked for smooth normals.
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))

# The armature: the Listener's own layout (it was laid out on the same concept).
arm = L.build_armature()
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

# The ears whole on their bones: bone heat shares a fan between the head, the
# neck and the ear bone, and a folded ear then shears. Anything out past the
# skull's side and up at ear height belongs to the ear alone.
ear_l = mesh.vertex_groups.get("EarL"); ear_r = mesh.vertex_groups.get("EarR")
if ear_l is not None and ear_r is not None:
    fixed = 0
    for v in mesh.data.vertices:
        if v.co.z > 1.62 and abs(v.co.x) > 0.12:
            for g in list(v.groups):
                mesh.vertex_groups[g.group].remove([v.index])
            (ear_l if v.co.x < 0 else ear_r).add([v.index], 1.0, "REPLACE")
            fixed += 1
    print("ear vertices pinned to the ear bones:", fixed)

# The maps named by their role, so Unity's setup can find them after extraction.
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
    # The exporter embeds a map under its *file* name, so each one is written out
    # under the role's name and the image pointed at that file before the export.
    map_dir = os.path.join(os.path.dirname(dst), "Generated~", "maps")
    os.makedirs(map_dir, exist_ok=True)
    for socket, role in (("Base Color", "BaseColor"), ("Normal", "Normal"), ("Roughness", "Roughness"), ("Metallic", "Metallic")):
        image = image_on(socket)
        if image is None:
            continue
        path = os.path.join(map_dir, "Listener_" + role + ".png")
        image.save_render(path)
        image.name = "Listener_" + role
        image.filepath = path
        image.filepath_raw = path
        if image.packed_file is not None:
            image.unpack(method="REMOVE")
        image.source = "FILE"
        image.reload()
        print("map", role, "=", os.path.basename(image.filepath), image.size[:])

L.anchor("BeamOrigin", (0, -0.17, 1.78), arm, "Head")
L.anchor("Voice", (0, 0, 1.86), arm, "Head")
L.animate(arm)

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
blend = os.path.join(HERE, "ListenerGenerated.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("wrote", dst, "and", blend)
