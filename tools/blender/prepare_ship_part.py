"""Turn one generated ship part into a game-ready model (docs/reference/'Ship art -
what to generate.docx'): import it, put it in the game's frame — the right size, the
right way round, its feet on y = 0 — decimate it to a budget, flat-shade it, write its
maps out by role, and export the FBX the ship prefab uses.

  blender -b -P tools/blender/prepare_ship_part.py -- <Part> <generated.fbx|.glb> <out.fbx> [faces]

The parts table lives in ShipPartTable below: target size in metres (x = width,
y = depth, z = height, as the ship uses them), how to fit it, and the triangle budget.
A part whose generated proportions differ from the target is scaled uniformly by
default, so nothing is stretched; the hull is the one exception (`stretch`), because
the deck's length and width are fixed by the game.

A generation that loses most of its faces to the budget loses its texture with them:
Meshy's atlas is thousands of tiny islands, and collapsing faces drags their UVs
across island edges until the surface samples noise. So a part decimated past
BAKE_RATIO keeps a copy of the original, is unwrapped afresh, and has its colour and
normals baked from that copy onto the new unwrap (Cycles, selected to active).

The hull gets more (the hull_* steps): it is the deck the crew walks on and the wall
that keeps them aboard, so its deck plane is found and flattened, the elevator's well
is cut through it, the bulwark is set to the height the game wants, the deck faces go
into a `DeckPlate` material slot with UVs in metres for the tiling texture, and the
mesh is shifted so the deck is at z = 0 (every other part stands on z = 0).
"""
import math
import os
import sys

import bpy
from mathutils import Vector

# part: (x, y, z) metres, fit mode, triangle budget
#   "uniform" – one scale for every axis, the largest target dimension wins
#   "stretch" – each axis scaled to its target (only where the game fixes the shape)
TABLE = {
    "Hull":         ((20.0, 48.0, 8.0), "stretch", 25000),   # x across the ship, y fore-and-aft (Unity z); see the hull_* steps
    "Tower":        ((8.0, 6.0, 6.0), "stretch", 20000),
    "Railing":      ((2.0, 0.2, 1.2), "uniform", 4000),
    "StorageRoom":  ((2.8, 3.0, 2.2), "stretch", 12000),
    "CabinHousing": ((5.0, 5.0, 3.0), "stretch", 15000),
    "Crane":        ((3.0, 9.0, 7.0), "uniform", 15000),
    "Winch":        ((2.0, 1.2, 1.6), "uniform", 8000),
    "Container":    ((6.0, 2.6, 2.6), "stretch", 8000),
    "Console":      ((1.8, 0.8, 1.8), "uniform", 8000),
    "TvCabinet":    ((3.6, 0.4, 3.3), "uniform", 8000),
    "DeckLamp":     ((0.6, 0.6, 1.9), "uniform", 4000),
    "CabinDoor":    ((1.0, 0.12, 2.6), "uniform", 5000),
    "StorageSill":  ((1.4, 0.5, 0.12), "uniform", 3000),
    "Bollard":      ((0.8, 0.8, 0.7), "uniform", 4000),
    "Pipes":        ((2.0, 0.6, 1.5), "uniform", 8000),
    "Ladder":       ((0.5, 0.15, 3.0), "uniform", 4000),
    "Barrel":       ((0.6, 0.6, 0.9), "uniform", 4000),
    "Crate":        ((0.8, 0.6, 0.6), "uniform", 4000),
    "CableCoil":    ((1.2, 1.2, 0.35), "uniform", 4000),
    "Lifebuoy":     ((0.9, 0.35, 2.0), "uniform", 4000),
    "Toolbox":      ((0.7, 0.45, 0.45), "uniform", 4000),
    "Couch":        ((2.0, 0.85, 0.8), "uniform", 5000),
    "Bench":        ((1.6, 0.5, 0.5), "uniform", 4000),
    "Table":        ((1.2, 0.8, 0.75), "uniform", 4000),
    "NamePlate":    ((3.0, 0.15, 0.6), "uniform", 3000),
    "Signs":        ((1.9, 0.12, 0.45), "uniform", 3000),
    "ElevatorCar":  ((5.0, 5.0, 3.5), "stretch", 15000),
    "CarPanel":     ((0.6, 0.12, 0.4), "uniform", 3000),
    "TubeSection":  ((5.0, 5.0, 2.0), "stretch", 6000),
    "TubeFoot":     ((7.0, 7.0, 3.0), "stretch", 12000),
}

# Decimation that keeps fewer than one face in four gets its maps baked afresh.
BAKE_RATIO = 4

# The hull's rules, in metres: the bulwark is chest high, well over the 65 cm
# jump (Dan, 23 September 2026: no fences, "the sides of the ship tall enough so
# players cant jump over them"), and the well is the game's WellRadius plus the
# wall's own thickness.
HULL_BULWARK = 1.2
HULL_WELL_RADIUS = 3.74


# ---- the hull -----------------------------------------------------------------

def hull_measure(data):
    """The deck plane (the most populated 10 cm of height inside the bulwark) and the top."""
    from collections import Counter
    inner = [v.co.z for v in data.vertices if abs(v.co.x) < 7.0 and abs(v.co.y) < 16.0]
    deck = max(Counter(round(z * 10) / 10 for z in inner).items(), key=lambda kv: kv[1])[0]
    top = max(v.co.z for v in data.vertices)
    print("hull deck plane z=%.2f, top z=%.2f, bulwark as generated %.2f" % (deck, top, top - deck))
    return deck, top


# How far over the deck plane still counts as deck: everything. The generation's
# raised foredeck and its ragged bulwark both come down to the deck plane; the
# ship's side is built in Unity from the hull's outline (ShipDeckDressing), a
# clean wall of HULL_BULWARK, which a scaled generated wall never was.
HULL_DECK_BAND = 6.0


def hull_flatten(data, deck, top):
    """Flatten the deck (hatches, bumps and the raised foredeck would trip the walk),
    bring what stands above it - the bulwark - down to its height, and put the deck
    at z = 0; everything below the deck is left alone."""
    k = HULL_BULWARK / max(top - deck - HULL_DECK_BAND, 0.01)
    for v in data.vertices:
        z = v.co.z
        if -0.35 < z - deck < HULL_DECK_BAND:
            z = deck
        elif z > deck:
            z = deck + (z - deck - HULL_DECK_BAND) * k
        v.co.z = z - deck
    data.update()


def hull_cut_well(mesh):
    """The well: a shaft straight through, where the game's well is."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=64, radius=HULL_WELL_RADIUS, depth=40.0, location=(0.0, 0.0, 0.0))
    cutter = bpy.context.active_object
    mod = mesh.modifiers.new("Well", "BOOLEAN")
    mod.operation = "DIFFERENCE"
    mod.object = cutter
    mod.solver = "EXACT"
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Well")
    bpy.data.objects.remove(cutter, do_unlink=True)


def hull_deck_slot(data):
    """The deck faces into their own slot, UVs in metres: Unity gives that slot the
    tiling deck plate (ShipModelSetup.Apply), cut exactly to the hull and the well."""
    plate = bpy.data.materials.new("DeckPlate")
    plate.use_nodes = True
    data.materials.append(plate)
    slot = len(data.materials) - 1
    uv = data.uv_layers.active
    faces = 0
    for poly in data.polygons:
        if poly.normal.z > 0.9 and abs(poly.center.z) < 0.02:
            poly.material_index = slot
            for li in poly.loop_indices:
                co = data.vertices[data.loops[li].vertex_index].co
                uv.data[li].uv = (co.x, co.y)
            faces += 1
    print("deck faces %d" % faces)
    return plate


# ---- the bake -----------------------------------------------------------------

def unwrap_fresh(low):
    """A new UV map on the decimated mesh, one island per smooth patch."""
    layer = low.data.uv_layers.new(name="Baked")
    low.data.uv_layers.active = layer
    layer.active_render = True
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66.0), island_margin=0.003, correct_aspect=True, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    # Only the fresh unwrap goes out: Unity reads the first UV set, and the old
    # atlas layout is exactly what the bake replaces.
    for other in [l for l in low.data.uv_layers if l.name != "Baked"]:
        low.data.uv_layers.remove(other)
    low.data.uv_layers.active = low.data.uv_layers["Baked"]


def bake_from(high, low, part, size, ray):
    """Colour and tangent normals from the original onto the decimated mesh's fresh
    unwrap. The low mesh ends up with one material whose nodes carry the two baked
    images, which the map export below writes out like any other part's."""
    colour = bpy.data.images.new(part + "_BaseColor", size, size)
    normal = bpy.data.images.new(part + "_Normal", size, size)
    normal.colorspace_settings.name = "Non-Color"

    mat = bpy.data.materials.new(part)
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
    tex_c = nodes.new("ShaderNodeTexImage"); tex_c.image = colour
    tex_n = nodes.new("ShaderNodeTexImage"); tex_n.image = normal
    nmap = nodes.new("ShaderNodeNormalMap")
    links.new(tex_c.outputs["Color"], bsdf.inputs["Base Color"])
    links.new(tex_n.outputs["Color"], nmap.inputs["Color"])
    links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])

    # Every slot of the low mesh needs an image node to bake into; the hull's deck
    # slot keeps its own material (Unity swaps it for the plate) with a node added.
    targets = []
    for i, m in enumerate(low.data.materials):
        if m is not None and m.name == "DeckPlate":
            n_c = m.node_tree.nodes.new("ShaderNodeTexImage"); n_c.image = colour
            n_n = m.node_tree.nodes.new("ShaderNodeTexImage"); n_n.image = normal
            targets.append((m, n_c, n_n))
        else:
            low.data.materials[i] = mat
    targets.append((mat, tex_c, tex_n))
    if not low.data.materials:
        low.data.materials.append(mat)

    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 4
    bake = scene.render.bake
    bake.use_selected_to_active = True
    bake.use_cage = False
    bake.cage_extrusion = 0.02
    bake.max_ray_distance = ray
    bake.margin = 8
    bake.use_pass_direct = False
    bake.use_pass_indirect = False
    bake.use_pass_color = True

    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low

    for m, n_c, n_n in targets:
        m.node_tree.nodes.active = n_c
    bpy.ops.object.bake(type="DIFFUSE")
    for m, n_c, n_n in targets:
        m.node_tree.nodes.active = n_n
    bpy.ops.object.bake(type="NORMAL", normal_space="TANGENT")
    print("baked colour and normals at %d from %d faces" % (size, len(high.data.polygons)))

    for m, n_c, n_n in targets:
        if m is not mat:
            m.node_tree.nodes.remove(n_c)
            m.node_tree.nodes.remove(n_n)
    bpy.data.objects.remove(high, do_unlink=True)


# ---- the run ------------------------------------------------------------------

def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    part, src, dst = args[0], os.path.abspath(args[1]), os.path.abspath(args[2])
    if part not in TABLE:
        raise SystemExit("unknown part " + part)
    target, fit, budget = TABLE[part]
    if len(args) > 3:
        budget = int(args[3])

    bpy.ops.wm.read_factory_settings(use_empty=True)
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
    mesh.name = mesh.data.name = part
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    def bounds():
        lo = Vector((1e9, 1e9, 1e9)); hi = Vector((-1e9, -1e9, -1e9))
        for v in mesh.data.vertices:
            lo = Vector(map(min, lo, v.co)); hi = Vector(map(max, hi, v.co))
        return lo, hi

    lo, hi = bounds()
    size = hi - lo
    print("generated %.2f x %.2f x %.2f" % (size.x, size.y, size.z))

    # A part that came out lying the wrong way round: the longest axis of the target
    # should be the longest axis of the mesh. Rotate about z when x and y disagree.
    if (size.x > size.y) != (target[0] > target[1]) and abs(size.x - size.y) > 0.02:
        for v in mesh.data.vertices:
            v.co = Vector((-v.co.y, v.co.x, v.co.z))
        mesh.data.update()
        lo, hi = bounds()
        size = hi - lo
        print("turned a quarter: %.2f x %.2f x %.2f" % (size.x, size.y, size.z))

    if fit == "stretch":
        factors = Vector((target[0] / max(size.x, 1e-6), target[1] / max(size.y, 1e-6), target[2] / max(size.z, 1e-6)))
    else:
        k = min(target[i] / max((size.x, size.y, size.z)[i], 1e-6) for i in range(3))
        factors = Vector((k, k, k))
    centre = (lo + hi) / 2
    for v in mesh.data.vertices:
        v.co = Vector(((v.co.x - centre.x) * factors.x,
                       (v.co.y - centre.y) * factors.y,
                       (v.co.z - lo.z) * factors.z))
    mesh.data.update()
    lo, hi = bounds()
    size = hi - lo
    print("fitted (%s) %.2f x %.2f x %.2f, target %.2f x %.2f x %.2f" % (fit, size.x, size.y, size.z, *target))

    # The original kept aside when the budget would take most of it: the bake source.
    faces = len(mesh.data.polygons)
    high = None
    if faces > budget * BAKE_RATIO:
        high = mesh.copy()
        high.data = mesh.data.copy()
        high.name = part + "_high"
        bpy.context.collection.objects.link(high)

    if faces > budget:
        mod = mesh.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = budget / faces
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = mesh
        mesh.select_set(True)
        bpy.ops.object.modifier_apply(modifier="Decimate")
        print("decimated %d -> %d faces" % (faces, len(mesh.data.polygons)))
    bpy.ops.object.shade_flat()

    if part == "Hull":
        deck, top = hull_measure(mesh.data)
        hull_flatten(mesh.data, deck, top)
        if high is not None:
            hull_flatten(high.data, deck, top)  # the bake source keeps the same shape
        hull_cut_well(mesh)

    if high is not None:
        unwrap_fresh(mesh)
    if part == "Hull":
        hull_deck_slot(mesh.data)  # after the unwrap, so the metre UVs are the ones kept
    if high is not None:
        bake_from(high, mesh, part, 2048 if max(target) >= 4.0 else 1024, 1.0 if part == "Hull" else 0.3)

    # The maps out beside the model, named by their role, so Unity's setup finds them.
    map_dir = os.path.join(os.path.dirname(dst), "Maps")
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

        for socket, role in (("Base Color", "BaseColor"), ("Normal", "Normal")):  # the two the ship material reads
            image = image_on(socket)
            if image is None:
                continue
            # Colour as JPEG, the rest lossless: thirty parts' maps go through Git
            # LFS, and a 2048 colour PNG is twenty times the size for no visible gain.
            path = os.path.join(map_dir, part + "_" + role + (".jpg" if role == "BaseColor" else ".png"))
            # save_render writes in the scene render format, whatever the extension says.
            fmt = bpy.context.scene.render.image_settings
            fmt.file_format = "JPEG" if role == "BaseColor" else "PNG"
            fmt.quality = 94
            fmt.color_mode = "RGB" if role == "BaseColor" else "RGBA"
            image.save_render(path)
            image.name = part + "_" + role
            image.filepath = image.filepath_raw = path
            if image.packed_file is not None:
                image.unpack(method="REMOVE")
            image.source = "FILE"
            image.reload()
            print("map", role, os.path.basename(path), image.size[:])
        mat.name = part if mat.name != "DeckPlate" else mat.name

    os.makedirs(os.path.dirname(dst), exist_ok=True)
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=dst,
        use_selection=False,
        object_types={"MESH"},
        add_leaf_bones=False,
        bake_anim=False,
        axis_forward="-Z",
        axis_up="Y",
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        mesh_smooth_type="OFF",
        path_mode="STRIP",  # no .fbm copies beside the model: Unity reads Maps/ by name
        embed_textures=False,
    )
    print("wrote", dst)


main()
