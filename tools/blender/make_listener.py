"""The Listener, built from Dan's concept (docs/reference/monsters/listener-concept.webp,
22 September 2026): a thin grey humanoid, a smooth eyeless skull with an open
mouth, two huge red fan ears, a spine of spikes, long clawed hands, digitigrade
feet. Low-poly and flat-shaded like the picture. Rigged; four clips (Idle,
Drawn, Hunting, Shooting); BeamOrigin in the mouth and Voice in the skull on the
head bone. Built the way docs/MONSTER_MODELS.md asks of any monster model.

Run headless:
  blender -b -P tools/blender/make_listener.py -- Assets/_Project/Models/Monsters/Listener/Listener.fbx
Also saves tools/blender/Listener.blend for hand tweaks.
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

FPS = 30
HEIGHT = 2.0  # to the top of the skull; the ears rise above it


def out_path():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    return os.path.abspath(args[0] if args else "Assets/_Project/Models/Monsters/Listener/Listener.fbx")


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.fps = FPS


def material(name, rgb, emission=None, strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = 0.85
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*rgb, 1.0)
    return m


def new_object(name, mesh):
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    return obj


# ---- the body: a stick figure the Skin modifier wraps ---------------------------

def build_body(skin_mat):
    """Vertices with radii, edges between them; the skin modifier makes the flesh."""
    P = {}
    E = []

    def v(name, x, y, z, r):
        P[name] = (Vector((x, y, z)), r)

    def e(a, b):
        E.append((a, b))

    # Torso (facing -Y: the chest leans a little forward).
    v("pelvis", 0, 0.0, 1.05, 0.085)
    v("belly", 0, 0.0, 1.22, 0.07)
    v("chest", 0, -0.02, 1.40, 0.15)
    v("shoulders", 0, 0.0, 1.55, 0.12)
    v("neck0", 0, 0.0, 1.61, 0.045)
    v("neck1", 0, 0.01, 1.70, 0.04)
    e("pelvis", "belly"); e("belly", "chest"); e("chest", "shoulders"); e("shoulders", "neck0"); e("neck0", "neck1")
    for s, tag in ((-1, "L"), (1, "R")):
        # Arms: long, to the knees, three clawed fingers.
        v("shoulder" + tag, s * 0.27, 0.0, 1.55, 0.06)
        v("elbow" + tag, s * 0.37, 0.04, 1.15, 0.04)
        v("wrist" + tag, s * 0.35, -0.04, 0.76, 0.035)
        v("palm" + tag, s * 0.34, -0.09, 0.66, 0.042)
        e("shoulders", "shoulder" + tag); e("shoulder" + tag, "elbow" + tag); e("elbow" + tag, "wrist" + tag); e("wrist" + tag, "palm" + tag)
        for i, (dx, dy) in enumerate(((-0.06, -0.02), (-0.02, -0.06), (0.02, -0.06), (0.06, -0.02))):
            f = "finger%s%d" % (tag, i)
            v(f + "a", s * 0.34 + dx, -0.12 + dy, 0.56, 0.013)
            v(f + "b", s * 0.34 + dx * 1.5, -0.17 + dy, 0.42, 0.005)
            e("palm" + tag, f + "a"); e(f + "a", f + "b")
        # Legs: digitigrade — the ankle high and back, a long foot forward.
        v("hip" + tag, s * 0.10, 0.0, 1.02, 0.06)
        v("knee" + tag, s * 0.11, -0.06, 0.62, 0.055)
        v("ankle" + tag, s * 0.10, 0.07, 0.30, 0.042)
        v("heel" + tag, s * 0.10, 0.09, 0.07, 0.045)
        v("toe" + tag, s * 0.10, -0.20, 0.035, 0.04)
        v("toetip" + tag, s * 0.10, -0.32, 0.02, 0.015)
        e("pelvis", "hip" + tag); e("hip" + tag, "knee" + tag); e("knee" + tag, "ankle" + tag); e("ankle" + tag, "heel" + tag); e("heel" + tag, "toe" + tag); e("toe" + tag, "toetip" + tag)

    mesh = bpy.data.meshes.new("BodyStick")
    names = list(P.keys())
    index = {n: i for i, n in enumerate(names)}
    mesh.from_pydata([P[n][0] for n in names], [(index[a], index[b]) for a, b in E], [])
    mesh.update()
    obj = new_object("Body", mesh)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    layer = bm.verts.layers.skin.verify()
    bm.verts.ensure_lookup_table()
    for n in names:
        r = P[n][1]
        bm.verts[index[n]][layer].radius = (r, r)
        bm.verts[index[n]][layer].use_root = n == "pelvis"
    bm.to_mesh(mesh)
    bm.free()
    mod = obj.modifiers.new("Skin", "SKIN")
    mod.use_smooth_shade = False
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Skin")
    obj.data.materials.append(skin_mat)
    return obj


# ---- the head: an eyeless faceted skull with a mouth -----------------------------

def build_head(skin_mat, mouth_mat):
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1.0, location=(0, 0, 0))
    obj = bpy.context.active_object
    obj.name = "Head"
    obj.data.materials.append(skin_mat)
    obj.data.materials.append(mouth_mat)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    centre_z = 1.84
    for vert in bm.verts:
        x, y, z = vert.co
        # A tall faceted skull, wider at the crown, tapering to a pointed chin, longer front to back.
        taper = 1.0 if z >= 0 else 1.0 + 0.45 * z  # z in [-1, 0] narrows the jaw
        vert.co = Vector((x * 0.16 * taper, y * 0.20 * taper - 0.03, z * 0.18 + centre_z))
    # The mouth: the faces on the lower front, inset and pushed in, dark.
    mouth_at = Vector((0.0, -0.20, centre_z - 0.08))
    faces = [f for f in bm.faces if (f.calc_center_median() - mouth_at).length < 0.09]
    if faces:
        res = bmesh.ops.inset_region(bm, faces=faces, thickness=0.012, depth=0.0)
        inner = [f for f in faces]
        for f in inner:
            f.material_index = 1
        verts = {v for f in inner for v in f.verts}
        for v in verts:
            v.co += Vector((0.0, 0.05, 0.0))  # into the skull (the face is at -Y)
    bm.to_mesh(obj.data)
    bm.free()
    return obj


# ---- the ears: two red fans, pleated, on the temples ----------------------------

def build_ear(side, ear_mat, ear_dark_mat, skin_mat):
    """side -1 = left (−X), +1 = right (+X). A fan of ribs from the temple — a
    grey stem, a dark-red base, the red membrane — pleated by ±y offsets. The two
    fans stay apart: each spans from below the horizontal to well short of the top
    of the skull, like the picture's frill."""
    root = Vector((side * 0.13, 0.0, 1.86))
    ribs = 10
    # Not a full fan (Dan, 22 September 2026: "a little more closed"): a narrower
    # spread, smaller, and cupped — the outer edge curls forward like a petal.
    a0, a1 = math.radians(-18), math.radians(60)
    r_in, r_mid, r_out = 0.13, 0.27, 0.50
    inner, middle, outer = [], [], []
    for i in range(ribs):
        t = i / (ribs - 1)
        a = a0 + (a1 - a0) * t
        d = Vector((side * math.cos(a), 0.0, math.sin(a)))
        pleat = 0.014 * (1 if i % 2 == 0 else -1)
        cup = -0.07  # forward (−Y) at the rim: the cupping
        inner.append(root + d * r_in + Vector((0, pleat * 0.3, 0)))
        middle.append(root + d * r_mid + Vector((0, pleat * 0.7 + cup * 0.35, 0)))
        outer.append(root + d * r_out + Vector((0, pleat + cup, 0)))
    verts = [root] + inner + middle + outer
    faces, mats = [], []
    for i in range(ribs - 1):
        faces.append((0, 1 + i, 2 + i)); mats.append(0)                                                  # grey wedge
        faces.append((1 + i, 1 + ribs + i, 2 + ribs + i, 2 + i)); mats.append(1)                         # dark red base
        faces.append((1 + ribs + i, 1 + 2 * ribs + i, 2 + 2 * ribs + i, 2 + ribs + i)); mats.append(2)   # red membrane
    mesh = bpy.data.meshes.new("Ear")
    mesh.from_pydata(verts, [], faces)
    mesh.update()
    obj = new_object("EarL" if side < 0 else "EarR", mesh)
    obj.data.materials.append(skin_mat)
    obj.data.materials.append(ear_dark_mat)
    obj.data.materials.append(ear_mat)
    for i, poly in enumerate(obj.data.polygons):
        poly.material_index = mats[i]
        poly.use_smooth = False
    # Every vertex of the fan in a group named after its bone, so the skinning keeps the whole fan on it after the join.
    group = obj.vertex_groups.new(name="EarL" if side < 0 else "EarR")
    group.add(list(range(len(verts))), 1.0, "REPLACE")
    # The whole fan leans back a little, like the picture's.
    pivot = Matrix.Translation(root)
    tilt = Matrix.Rotation(math.radians(-8), 4, "X") @ Matrix.Rotation(math.radians(side * 4), 4, "Z")
    obj.matrix_world = pivot @ tilt @ pivot.inverted()
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    obj.select_set(False)
    mod = obj.modifiers.new("Solidify", "SOLIDIFY")
    mod.thickness = 0.02
    mod.offset = 0.0
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Solidify")
    obj.select_set(False)
    return obj


# ---- the spikes down the back -----------------------------------------------------

def build_spikes(skin_mat):
    spikes = []
    for i in range(8):
        z = 1.64 - i * 0.075
        length = 0.21 if 1 <= i <= 5 else 0.14
        bpy.ops.mesh.primitive_cone_add(vertices=5, radius1=0.032, radius2=0.0, depth=length, location=(0.0, 0.11 + (0.02 if i > 3 else 0.0), z))
        c = bpy.context.active_object
        c.name = "Spike%d" % i
        c.rotation_euler = (math.radians(-125), 0.0, 0.0)  # points back and up
        c.data.materials.append(skin_mat)
        spikes.append(c)
    return spikes


def join(parts, name):
    for p in parts:
        p.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.ops.object.join()
    obj = bpy.context.active_object
    obj.name = name
    obj.data.name = name
    bpy.ops.object.shade_flat()
    obj.select_set(False)
    return obj


# ---- the armature -------------------------------------------------------------------

BONES = {}


def build_armature():
    arm_data = bpy.data.armatures.new("Armature")
    arm = new_object("Armature", arm_data)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")

    def bone(name, head, tail, parent=None, connected=False):
        b = arm_data.edit_bones.new(name)
        b.head, b.tail = Vector(head), Vector(tail)
        if parent is not None:
            b.parent = arm_data.edit_bones[parent]
            b.use_connect = connected
        BONES[name] = (Vector(head), Vector(tail))
        return b

    bone("Root", (0, 0, 0), (0, 0, 1.05))
    bone("Hips", (0, 0, 1.05), (0, 0, 1.22), "Root", True)
    bone("Spine", (0, 0, 1.22), (0, 0, 1.55), "Hips", True)
    bone("Neck", (0, 0, 1.55), (0, 0.01, 1.73), "Spine", True)
    bone("Head", (0, 0.01, 1.73), (0, 0.0, 2.0), "Neck", True)
    for s, tag in ((-1, "L"), (1, "R")):
        bone("Ear" + tag, (s * 0.13, 0.0, 1.86), (s * 0.50, -0.06, 2.02), "Head")
        bone("UpperArm" + tag, (s * 0.25, 0.0, 1.55), (s * 0.35, 0.04, 1.15), "Spine")
        bone("LowerArm" + tag, (s * 0.35, 0.04, 1.15), (s * 0.33, -0.04, 0.76), "UpperArm" + tag, True)
        bone("Hand" + tag, (s * 0.33, -0.04, 0.76), (s * 0.32, -0.14, 0.50), "LowerArm" + tag, True)
        bone("Thigh" + tag, (s * 0.10, 0.0, 1.02), (s * 0.11, -0.06, 0.62), "Hips")
        bone("Shin" + tag, (s * 0.11, -0.06, 0.62), (s * 0.10, 0.07, 0.30), "Thigh" + tag, True)
        bone("Foot" + tag, (s * 0.10, 0.07, 0.30), (s * 0.10, -0.20, 0.035), "Shin" + tag, True)
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)
    return arm


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (a + ab * t)).length


def skin_to(mesh_obj, arm):
    """Every vertex to its nearest bone, blended with the second where a joint is
    close; the ears to their ear bone outright; the fingers stay with the hand."""
    existing = {g.name: g for g in mesh_obj.vertex_groups}  # the ear fans tagged themselves before the join
    groups = {name: existing.get(name) or mesh_obj.vertex_groups.new(name=name) for name in BONES}
    ear_indices = {groups[n].index for n in ("EarL", "EarR")}
    body_bones = [n for n in BONES if not n.startswith("Ear")]
    for v in mesh_obj.data.vertices:
        p = v.co
        if any(g.group in ear_indices for g in v.groups):
            continue
        ranked = sorted(((seg_dist(p, *BONES[n]), n) for n in body_bones), key=lambda d: d[0])
        (d0, n0), (d1, n1) = ranked[0], ranked[1]
        if d1 - d0 < 0.05 and d1 < 0.16 and n0 != "Root" and n1 != "Root":
            groups[n0].add([v.index], 0.65, "REPLACE")
            groups[n1].add([v.index], 0.35, "REPLACE")
        else:
            groups[n0].add([v.index], 1.0, "REPLACE")
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm
    mesh_obj.parent = arm


def anchor(name, world, arm, bone_name):
    empty = bpy.data.objects.new(name, None)
    empty.empty_display_type = "PLAIN_AXES"
    empty.empty_display_size = 0.08
    bpy.context.collection.objects.link(empty)
    empty.parent = arm
    empty.parent_type = "BONE"
    empty.parent_bone = bone_name
    bpy.context.view_layer.update()
    empty.matrix_world = Matrix.Translation(Vector(world))
    return empty


# ---- the clips ------------------------------------------------------------------------

def action(arm, name, frames, keys):
    """keys: {bone: [(frame, (rx, ry, rz) degrees, (lx, ly, lz) metres)]} in the bone's own axes."""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        ad.action_slot = act.slots.new(id_type="OBJECT", name=arm.name)
    except (AttributeError, TypeError):
        pass
    # Every bone at rest on the first and last frame, so a bone this clip does not
    # animate is at rest in it — not wherever the previous clip left it (the
    # exporter bakes whatever the pose holds for unkeyed bones).
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
        for frame in (1, frames):
            pb.keyframe_insert("rotation_euler", frame=frame)
            pb.keyframe_insert("location", frame=frame)
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
    ad.action = None
    return act


Z3 = (0, 0, 0)


def R(x=0, y=0, z=0):
    return (x, y, z)


def mirror_ears(fold, flare, twitch=0.0):
    """The ear bones point outward: rotation about their Z folds the fan back
    (mirrored per side), about their X raises the tip."""
    return {"EarL": R(flare, 0, -(fold + twitch)), "EarR": R(flare, 0, fold - twitch)}


def key_ears(fold, flare, twitch=0.0):
    ears = mirror_ears(fold, flare, twitch)
    return ears["EarL"], ears["EarR"]


def animate(arm):
    # Idle (2.4 s loop): standing straight, the ears folded back and breathing, the
    # head turning a little as if listening.
    idle = {"Head": [], "EarL": [], "EarR": [], "Spine": []}
    for frame, fold, flare, yaw in ((1, 20, -6, 0), (24, 26, -2, 9), (48, 18, -8, -7), (72, 20, -6, 0)):
        l, r = key_ears(fold, flare)
        idle["EarL"].append((frame, l, Z3))
        idle["EarR"].append((frame, r, Z3))
        idle["Head"].append((frame, R(2, yaw, 0), Z3))
        idle["Spine"].append((frame, R(1.5 if frame in (24, 72) else 0, 0, 0), Z3))
    action(arm, "Idle", 72, idle)

    # Drawn (1 s loop): a careful walk toward the sound — ears fanning open, the head
    # cocked, the legs swinging, the arms counter-swinging.
    def walk(frames, swing, bend, drop, pitch, cock, fold, arm_swing):
        half = frames // 2
        keys = {n: [] for n in ("ThighL", "ThighR", "ShinL", "ShinR", "UpperArmL", "UpperArmR", "LowerArmL", "LowerArmR", "Root", "Spine", "Head", "EarL", "EarR", "Hips")}
        for i, phase in enumerate((0.0, 0.5, 1.0)):
            frame = 1 + int(phase * (frames - 1))
            s = math.cos(phase * 2 * math.pi)  # +1 left leg forward, -1 right leg forward
            keys["ThighL"].append((frame, R(-swing * s, 0, 0), Z3))
            keys["ThighR"].append((frame, R(swing * s, 0, 0), Z3))
            keys["ShinL"].append((frame, R(bend * max(0.0, s), 0, 0), Z3))
            keys["ShinR"].append((frame, R(bend * max(0.0, -s), 0, 0), Z3))
            keys["UpperArmL"].append((frame, R(arm_swing * s, 0, 0), Z3))
            keys["UpperArmR"].append((frame, R(-arm_swing * s, 0, 0), Z3))
            keys["LowerArmL"].append((frame, R(-12, 0, 0), Z3))
            keys["LowerArmR"].append((frame, R(-12, 0, 0), Z3))
            keys["Root"].append((frame, Z3, (0, -drop, 0)))
            keys["Spine"].append((frame, R(pitch, 0, 0), Z3))
            keys["Hips"].append((frame, R(0, 0, 0), Z3))
            keys["Head"].append((frame, R(-pitch * 0.5, 0, cock), Z3))
            l, r = key_ears(fold, 4)
            keys["EarL"].append((frame, l, Z3))
            keys["EarR"].append((frame, r, Z3))
        # A mid-step bob on the quarter frames.
        for phase in (0.25, 0.75):
            frame = 1 + int(phase * (frames - 1))
            keys["Root"].append((frame, Z3, (0, -drop - 0.025, 0)))
        return keys

    action(arm, "Drawn", 30, walk(30, swing=22, bend=30, drop=0.0, pitch=8, cock=14, fold=12, arm_swing=14))

    # Hunting (0.7 s loop): the crouched stance of the attack picture, walking —
    # low, the torso pitched forward, the hands forward, the ears wide, quicker.
    hunting = walk(21, swing=28, bend=42, drop=0.28, pitch=38, cock=0, fold=-6, arm_swing=8)
    for n in ("UpperArmL", "UpperArmR"):
        hunting[n] = [(f, R(-55 + (r[0] if isinstance(r, tuple) else 0) * 0.3, 0, 0), l) for f, r, l in hunting[n]]
    for n in ("LowerArmL", "LowerArmR"):
        hunting[n] = [(f, R(-35, 0, 0), l) for f, r, l in hunting[n]]
    hunting["Head"] = [(f, R(-26, 0, 0), l) for f, r, l in hunting["Head"]]
    action(arm, "Hunting", 21, hunting)

    # Shooting (0.5 s, plays once and holds): from the crouch the head thrusts
    # forward, the ears snap fully open, the whole thing leans into the beam.
    l0, r0 = key_ears(-6, 6)
    l1, r1 = key_ears(-16, 22)
    shooting = {
        "Root": [(1, Z3, (0, -0.28, 0)), (6, Z3, (0, -0.32, 0)), (15, Z3, (0, -0.30, 0))],
        "Spine": [(1, R(38, 0, 0), Z3), (6, R(50, 0, 0), Z3), (15, R(46, 0, 0), Z3)],
        "Neck": [(1, R(0, 0, 0), Z3), (6, R(-10, 0, 0), (0, 0.04, 0)), (15, R(-8, 0, 0), (0, 0.03, 0))],
        "Head": [(1, R(-26, 0, 0), Z3), (6, R(-42, 0, 0), (0, 0.10, 0)), (15, R(-40, 0, 0), (0, 0.09, 0))],
        "EarL": [(1, l0, Z3), (5, l1, Z3), (15, l1, Z3)],
        "EarR": [(1, r0, Z3), (5, r1, Z3), (15, r1, Z3)],
        "ThighL": [(1, R(-30, 0, 0), Z3), (15, R(-34, 0, 0), Z3)],
        "ThighR": [(1, R(-30, 0, 0), Z3), (15, R(-34, 0, 0), Z3)],
        "ShinL": [(1, R(44, 0, 0), Z3), (15, R(48, 0, 0), Z3)],
        "ShinR": [(1, R(44, 0, 0), Z3), (15, R(48, 0, 0), Z3)],
        # The arms wide and low, the claws splayed (Dan: "shooting with his hands spreading").
        "UpperArmL": [(1, R(-55, 0, 0), Z3), (6, R(-38, 0, 62), Z3), (15, R(-40, 0, 58), Z3)],
        "UpperArmR": [(1, R(-55, 0, 0), Z3), (6, R(-38, 0, -62), Z3), (15, R(-40, 0, -58), Z3)],
        "LowerArmL": [(1, R(-35, 0, 0), Z3), (6, R(-18, 0, 10), Z3), (15, R(-20, 0, 8), Z3)],
        "LowerArmR": [(1, R(-35, 0, 0), Z3), (6, R(-18, 0, -10), Z3), (15, R(-20, 0, -8), Z3)],
        "HandL": [(1, R(0, 0, 0), Z3), (6, R(22, 0, 28), Z3), (15, R(20, 0, 25), Z3)],
        "HandR": [(1, R(0, 0, 0), Z3), (6, R(22, 0, -28), Z3), (15, R(20, 0, -25), Z3)],
    }
    action(arm, "Shooting", 15, shooting)


# ---- export -------------------------------------------------------------------------------

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
        mesh_smooth_type="OFF",
        path_mode="AUTO",
        embed_textures=False,
    )


def main():
    path = out_path()
    clear()
    skin_mat = material("ListenerSkin", (0.23, 0.25, 0.31))
    ear_mat = material("ListenerEar", (0.46, 0.16, 0.18), emission=(0.2, 0.04, 0.05), strength=0.3)
    ear_dark_mat = material("ListenerEarBase", (0.22, 0.09, 0.12))
    mouth_mat = material("ListenerMouth", (0.02, 0.015, 0.03), emission=(0.25, 0.05, 0.4), strength=0.6)
    parts = [build_body(skin_mat), build_head(skin_mat, mouth_mat), build_ear(-1, ear_mat, ear_dark_mat, skin_mat), build_ear(1, ear_mat, ear_dark_mat, skin_mat)] + build_spikes(skin_mat)
    mesh = join(parts, "Listener")
    arm = build_armature()
    skin_to(mesh, arm)
    anchor("BeamOrigin", (0, -0.20, 1.80), arm, "Head")
    anchor("Voice", (0, 0, 1.86), arm, "Head")
    animate(arm)
    export(path)
    blend = os.path.join(os.path.dirname(os.path.abspath(__file__)), "Listener.blend")
    bpy.ops.wm.save_as_mainfile(filepath=blend)
    print("wrote", path, "and", blend)


main()
