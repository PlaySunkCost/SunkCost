"""The Weeping Angel's own rig fit and clips (tools/blender/clips/<Kind>.py, run by
rig_generated_monster.py after the shared library; 24 September 2026, mon-angel).

The generated mesh stands with its hands over its face (Dan's Meshy generation,
docs/reference/monsters/weepingangel-concept.webp), but the shared layout hangs the
arm bones straight down beside the body, outside the mesh: the arms never moved
and the hands were skinned to the head. So this file first fits the arm bones to
the mesh's own arms (shoulder, elbow at the chest, wrist at the jaw, fingers over
the eyes) and skins again, then keys the Angel's clips on that rig:

  Idle      weeping: hands over the face, a slow sob in the shoulders and the head.
  Hunting   what nobody sees: a low, bounding lunge, the arms reaching ahead.
  Frozen    what everybody sees: a statue pose, held dead still. The clip is a strip
            of FROZEN_POSES segments, each one constant; WeepingAngelStatue.cs picks
            the segment the server chose (WeepingAngel.FrozenVariant) and holds it.
  Grabbing  the embrace (Dan): the hands leave the face, take the diver's head, a
            beat of stillness, then death. Longer than the hold, so it never loops.

Poses are written as world-space rotations of each bone from its rest, and the arms
by two-bone IK to world points, so the numbers read as the picture: +X pitches
forward, the Angel faces -Y, its "L" bones are on -X.
"""
import math

import bpy
from mathutils import Euler, Matrix, Vector

CLIPS = ("Grabbing",)

# The Frozen strip: segment i holds FROZEN_POSES[i] over frames [1 + i*SEG, (i+1)*SEG].
# WeepingAngelStatue seeks to the middle of a segment; keep the order in step with it.
SEG = 60  # 2 s a pose: even unheld, a statue would not change under a look for 2 s
FROZEN_POSES = ("Lunge", "Reach", "Weep", "Creep", "Turn")

FPS = 30
H = 2.1  # the height the rig frames it to; every point below is at this height


# ---- the rig fit ------------------------------------------------------------------------

# The arms as the mesh holds them (measured on the framed mesh, 24 September 2026).
ARM_REST = {
    "shoulder": (0.200, 0.02, 1.70),
    "elbow": (0.135, -0.17, 1.46),
    "wrist": (0.075, -0.19, 1.80),
    "tip": (0.050, -0.20, 1.97),
}


def _p(key, s, k):
    x, y, z = ARM_REST[key]
    return Vector((s * x * k, y * k, z * k))


def fit_arms(arm, height):
    k = height / H
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    for s, tag in ((-1, "L"), (1, "R")):
        chain = (("UpperArm", "shoulder", "elbow"), ("LowerArm", "elbow", "wrist"), ("Hand", "wrist", "tip"))
        for name, a, b in chain:
            bone = eb[name + tag]
            bone.use_connect = False
            bone.head, bone.tail = _p(a, s, k), _p(b, s, k)
        for name, _, _ in chain[1:]:
            eb[name + tag].use_connect = True
        for name, _, _ in chain:
            eb[name + tag].align_roll(Vector((0, 1, 0)))  # local Z toward the back: the palm side at rest
    bpy.ops.object.mode_set(mode="OBJECT")


def _seg(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
    return (p - (a + ab * t)).length


def _arm_side(p, k):
    """Which forearm-or-hand the vertex is part of (-1, 1) or 0 for the body. The
    hands lie over the face as the front skin of the head, the forearms against the
    chest; both are told from the body by where they sit (tuned on the render)."""
    for s in (-1, 1):
        if p.x * s < -0.01 * k:
            continue
        E, W, T = _p("elbow", s, k), _p("wrist", s, k), _p("tip", s, k)
        if 1.76 * k < p.z < 1.99 * k and _seg(p, W, T) < 0.06 * k and p.y < -0.150 * k:
            return s
        if _seg(p, E, W) < 0.065 * k and p.y < (-0.12 - (p.z / k - 1.46) * 0.1) * k:
            return s
    return 0


def free_hands(mesh, height):
    """The Meshy blob is one skin: the hands are the front of the face, the forearms
    grow out of the chest. Cut them free — the faces that bridge a forearm or hand to
    the body go, and both sides are capped — so the embrace can lift them away
    without dragging the face along. The caps take their neighbours' texture, and
    under the hands the Angel is left a worn, featureless stone face."""
    import bmesh
    k = height / H
    me = mesh.data
    bm = bmesh.new()
    bm.from_mesh(me)
    side = {v.index: _arm_side(v.co, k) for v in bm.verts}
    bridges = [f for f in bm.faces if len({side[v.index] for v in f.verts}) > 1]  # arm to body, and one hand to the other
    # an elbow stays joined to its upper arm: keep the bridges round the elbow
    keep = []
    for f in bridges:
        c = f.calc_center_median()
        if any(_seg(c, _p("shoulder", s, k), _p("elbow", s, k)) < 0.05 * k and c.z < 1.55 * k for s in (-1, 1)):
            keep.append(f)
    cut = [f for f in bridges if f not in keep]
    bmesh.ops.delete(bm, geom=cut, context="FACES_ONLY")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    before = set(bm.faces)
    edges = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    caps = [f for f in bm.faces if f not in before]
    uv = bm.loops.layers.uv.active
    for f in caps:
        f.smooth = True
        if uv is None:
            continue
        for loop in f.loops:
            other = next((l for l in loop.vert.link_loops if l.face not in caps), None)
            if other is not None:
                loop[uv].uv = other[uv].uv
    bm.to_mesh(me)
    bm.free()
    me.update()
    arm_verts = sum(1 for s in side.values() if s != 0)
    print("angel: hands freed — %d arm vertices, %d faces cut, %d kept at the elbows, %d caps" % (arm_verts, len(cut), len(keep), len(caps)))


def reskin(arm, mesh):
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    if arm.animation_data:
        arm.animation_data.action = None
    for g in list(mesh.vertex_groups):
        mesh.vertex_groups.remove(g)
    for m in list(mesh.modifiers):
        if m.type == "ARMATURE":
            mesh.modifiers.remove(m)
    bpy.ops.object.select_all(action="DESELECT")
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    print("angel: reskinned, %d groups" % len(mesh.vertex_groups))


def fix_weights(mesh, height):
    """Bone heat shares the freed hands with the head and the forearms with the chest.
    A forearm or hand vertex belongs to its own arm's bones only (by distance, so the
    wrist and the elbow still bend smoothly); nothing on the body follows a forearm
    or a hand."""
    k = height / H
    groups = {g.name: g for g in mesh.vertex_groups}
    for n in ("UpperArmL", "LowerArmL", "HandL", "UpperArmR", "LowerArmR", "HandR"):
        if n not in groups:
            groups[n] = mesh.vertex_groups.new(name=n)
    by_index = {g.index: g for g in mesh.vertex_groups}
    moved = 0
    for v in mesh.data.vertices:
        s = _arm_side(v.co, k)
        tag = "L" if s < 0 else "R"
        if s != 0:
            for g in list(v.groups):
                by_index[g.group].remove([v.index])
            S, E, W, T = (_p(n, s, k) for n in ("shoulder", "elbow", "wrist", "tip"))
            d = {"UpperArm": _seg(v.co, S, E), "LowerArm": _seg(v.co, E, W), "Hand": _seg(v.co, W, T)}
            w = {n: 1.0 / max(1e-4, x) ** 4 for n, x in d.items()}
            total = sum(w.values())
            for n, x in w.items():
                if x / total > 0.02:
                    groups[n + tag].add([v.index], x / total, "REPLACE")
            moved += 1
        else:
            names = [(by_index[g.group].name, g.weight) for g in v.groups]
            if not any(n.startswith(("LowerArm", "Hand")) for n, _ in names):
                continue
            keep = [(n, w) for n, w in names if not n.startswith(("LowerArm", "Hand"))]
            for n, _ in names:
                if n.startswith(("LowerArm", "Hand")):
                    groups[n].remove([v.index])
            total = sum(w for _, w in keep)
            if total <= 1e-4:
                groups["Head" if v.co.z > 1.75 * k else "Spine"].add([v.index], 1.0, "REPLACE")
            else:
                for n, w in keep:
                    groups[n].add([v.index], w / total, "REPLACE")
    print("angel: %d arm vertices weighted to their arm alone" % moved)


# ---- posing -------------------------------------------------------------------------------

def rot(x=0.0, y=0.0, z=0.0):
    """A world rotation, degrees, applied X then Y then Z."""
    return (Matrix.Rotation(math.radians(z), 3, "Z") @ Matrix.Rotation(math.radians(y), 3, "Y") @ Matrix.Rotation(math.radians(x), 3, "X"))


def frame(y, ref):
    y = y.normalized()
    z = (ref - y * ref.dot(y)).normalized()
    x = y.cross(z)
    m = Matrix.Identity(3)
    m.col[0], m.col[1], m.col[2] = x, y, z
    return m


CHAINS = {
    "armL": ("UpperArmL", "LowerArmL", "HandL"), "armR": ("UpperArmR", "LowerArmR", "HandR"),
    "legL": ("ThighL", "ShinL", "FootL"), "legR": ("ThighR", "ShinR", "FootR"),
}


class Rig:
    """Forward kinematics on the armature's rest matrices. A pose is
      {"root": offset, "world": {bone: 3x3 world rotation from rest},
       "ik": {chain: goal or callable(rig, posed) -> goal}}
    with a goal (end point, pole point, end bone's world rotation from rest or None).
    The trunk is placed first, then the chains, so a goal can hang off the posed head
    (a hand kept on the face). solve() returns each bone's basis as Euler degrees and
    a location, the keys make_action writes."""

    def __init__(self, arm):
        self.arm = arm
        self.rest = {b.name: b.matrix_local.copy() for b in arm.data.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in arm.data.bones}

        def depth(n):
            d = 0
            while self.parent[n] is not None:
                n = self.parent[n]
                d += 1
            return d
        self.order = sorted(self.rest, key=depth)
        self.chain_of = {n: c for c, names in CHAINS.items() for n in names}
        self.lengths = {}
        for c, (a, b, e) in CHAINS.items():
            A, B, E = (self.rest[n].to_translation() for n in (a, b, e))
            self.lengths[c] = ((B - A).length, (E - B).length)

    def point(self, name):
        return self.rest[name].to_translation()

    def delta(self, posed, name):
        """The world rotation a posed bone carries from its rest."""
        return posed[name].to_3x3() @ self.rest[name].to_3x3().inverted()

    def carried(self, posed, name, rest_point):
        """Where a rest-pose point rides when it is fixed to a posed bone."""
        return (posed[name] @ self.rest[name].inverted() @ Vector(rest_point).to_4d()).to_3d()

    def solve(self, pose):
        world = pose.get("world", {})
        root_off = Vector(pose.get("root", (0, 0, 0)))
        ik = pose.get("ik", {})
        posed, basis = {}, {}

        def place(n, want_rot=None, loc=Vector()):
            rest = self.rest[n]
            p = self.parent[n]
            m0 = rest.copy() if p is None else posed[p] @ self.rest[p].inverted() @ rest
            b_rot = Matrix.Identity(3) if want_rot is None else m0.to_3x3().inverted() @ want_rot @ rest.to_3x3()
            b_loc = rest.to_3x3().inverted() @ loc if p is None else Vector()
            b = Matrix.Translation(b_loc) @ b_rot.to_4x4()
            posed[n] = m0 @ b
            basis[n] = b

        for n in self.order:
            if self.chain_of.get(n) in ik:
                continue
            place(n, world.get(n), root_off if self.parent[n] is None else Vector())
        for c, goal in ik.items():
            if callable(goal):
                goal = goal(self, posed)
            self._chain(c, goal, place, posed)
        out = {}
        for n, b in basis.items():
            e = b.to_3x3().to_euler("XYZ")
            out[n] = (tuple(math.degrees(a) for a in e), tuple(b.to_translation()))
        return out, posed

    def _chain(self, c, goal, place, posed):
        end, pole, end_rot = goal
        a, b, e = CHAINS[c]
        l1, l2 = self.lengths[c]
        A0, B0, E0 = self.point(a), self.point(b), self.point(e)
        A = (posed[self.parent[a]] @ self.rest[self.parent[a]].inverted() @ self.rest[a]).to_translation()
        W = Vector(end)
        d_vec = W - A
        d = max(abs(l1 - l2) + 1e-3, min(l1 + l2 - 1e-3, d_vec.length))
        u = d_vec.normalized()
        pv = Vector(pole) - A
        v = pv - u * pv.dot(u)
        v = v.normalized() if v.length > 1e-5 else Vector((0, -1, 0))
        along = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
        B = A + u * along + v * math.sqrt(max(0.0, l1 * l1 - along * along))
        W = A + u * d
        n_new = u.cross(v)
        u0 = (E0 - A0).normalized()
        v0 = (B0 - A0) - u0 * (B0 - A0).dot(u0)
        n_old = u0.cross(v0.normalized())
        place(a, frame(B - A, n_new) @ frame(B0 - A0, n_old).transposed())
        place(b, frame(W - B, n_new) @ frame(E0 - B0, n_old).transposed())
        place(e, end_rot)


def aim(rig, bone, new_dir, new_ref, rest_ref=(0, 1, 0)):
    """The world rotation that turns a bone's rest direction to new_dir, its rest_ref
    side (a palm's normal) to new_ref."""
    rest_dir = rig.rest[bone].to_3x3() @ Vector((0, 1, 0))
    return frame(Vector(new_dir), Vector(new_ref)) @ frame(rest_dir, Vector(rest_ref)).transposed()


def make_action(arm, name, frames, keyed, interpolation="BEZIER"):
    """keyed: [(frame, {bone: (euler_deg, loc)})]. Every bone is keyed on every listed
    frame (rest where the pose leaves it), so nothing carries over from another clip."""
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        ad.action_slot = act.slots.new(id_type="OBJECT", name=arm.name)
    except (AttributeError, TypeError):
        pass
    prev = {}
    for f, bones in keyed:
        for pb in arm.pose.bones:
            pb.rotation_mode = "XYZ"
            e, loc = bones.get(pb.name, ((0, 0, 0), (0, 0, 0)))
            want = tuple(math.radians(a) for a in e)
            # keep Euler continuity with the previous key so no channel spins the long way round
            if pb.name in prev:
                want = tuple(Euler(want, "XYZ").to_matrix().to_euler("XYZ", Euler(prev[pb.name], "XYZ")))
            prev[pb.name] = want
            pb.rotation_euler = want
            pb.location = loc
            pb.keyframe_insert("rotation_euler", frame=f)
            pb.keyframe_insert("location", frame=f)
    act.frame_range = (1, frames)
    try:
        act.use_frame_range = True
    except AttributeError:
        pass
    _set_interpolation(act, interpolation)
    ad.action = None
    return act


def _set_interpolation(act, mode):
    curves = []
    try:
        curves = list(act.fcurves)
    except AttributeError:
        pass
    if not curves:
        try:
            for layer in act.layers:
                for strip in layer.strips:
                    for bag in strip.channelbags:
                        curves.extend(bag.fcurves)
        except AttributeError:
            pass
    for c in curves:
        for kp in c.keyframe_points:
            kp.interpolation = mode


# ---- the entry point ------------------------------------------------------------------------

def animate(arm, height, L):
    mesh = next(o for o in bpy.data.objects if o.type == "MESH" and o.parent is arm)
    fit_arms(arm, height)
    free_hands(mesh, height)
    reskin(arm, mesh)
    fix_weights(mesh, height)
    rig = Rig(arm)
    build_clips(rig, arm, height)


# ---- the pose book ----------------------------------------------------------------------------

SIDES = (("L", -1), ("R", 1))

# The embrace (the grab core holds the diver here: keep in step with the prefab's hold
# numbers): the diver's feet this far in front of the Angel's, the diver's eyes at the
# diver's standing eye height.
HOLD_METERS = 0.70
VICTIM_EYE = 1.60
EMBRACE_FRAMES = 90      # 3 s: longer than the hold, so the loop never shows
KILL_FRAME = 66          # 2.2 s: the squeeze at the end of the stillness

# Hunting: an in-place sprint, the feet swept back at the ground's speed in stance.
RUN_FRAMES = 12          # one cycle, two steps (0.4 s)
RUN_SWEEP = 0.90         # metres an ankle travels back while planted
RUN_STANCE = 0.20        # the part of a foot's cycle it is on the ground
# The speed this clip matches at 1x playback: sweep / (stance x cycle seconds) = 11.25 m/s.
RUN_SPEED = RUN_SWEEP / (RUN_STANCE * RUN_FRAMES / FPS)


def trunk(lean=0.0, side=0.0, twist=0.0, neck=0.0, head=0.0, tilt=0.0, turn=0.0, hips=None):
    """World rotations for the trunk: lean pitches forward, side leans to the Angel's
    left (+X), twist and turn yaw toward its left; neck and head add their pitch."""
    hl, hs, ht = hips if hips is not None else (lean * 0.35, side * 0.4, twist * 0.5)
    return {
        "Hips": rot(hl, hs, ht),
        "Spine": rot(lean, side, twist),
        "Neck": rot(lean + neck, side + tilt * 0.3, twist + turn * 0.4),
        "Head": rot(lean + neck + head, side + tilt, twist + turn),
    }


def on_face(tag, s, slide=(0.0, 0.0, 0.0), open_deg=0.0):
    """A hand kept where it rests on the face, riding the posed head; slide (in the
    head's frame, metres) and open_deg (the hand turned out, degrees) peel it away."""
    def goal(rig, posed):
        hd = rig.delta(posed, "Head")
        wrist = rig.carried(posed, "Head", rig.point("Hand" + tag)) + hd @ Vector(slide)
        elbow = rig.point("LowerArm" + tag)
        pole = rig.carried(posed, "Spine", elbow + Vector((s * 0.10, -0.30, -0.05)))
        turn = rot(0, 0, -s * open_deg) if open_deg else Matrix.Identity(3)
        return wrist, pole, hd @ turn
    return goal


def reach(tag, s, wrist, fingers, palm, pole_off=(0.25, 0.05, -0.25)):
    """A hand sent to a world point, its fingers along `fingers`, its palm facing `palm`."""
    def goal(rig, posed):
        sh = rig.carried(posed, "Spine", rig.point("UpperArm" + tag))
        pole = sh + Vector((s * pole_off[0], pole_off[1], pole_off[2]))
        return Vector(wrist), pole, aim(rig, "Hand" + tag, fingers, palm)
    return goal


def plant(tag, s, dx=0.0, dy=0.0, dz=0.0, toes=0.0, yaw=0.0):
    """A foot set down dx, dy from where it stands at rest (dz lifts it), turned yaw
    degrees, up on its toes by `toes` degrees (the heel rises round the toe)."""
    def goal(rig, posed):
        ankle0, toe0 = rig.point("Foot" + tag), rig.arm.data.bones["Foot" + tag].tail_local.copy()
        r = rot(toes, 0, yaw)
        toe = toe0 + Vector((dx, dy, dz))
        ankle = toe + r @ (ankle0 - toe0)
        hip = rig.carried(posed, "Hips", rig.point("Thigh" + tag))
        pole = hip + rot(0, 0, yaw) @ Vector((s * 0.05, -0.8, -0.35))
        return ankle, pole, r
    return goal


def standing(**extra):
    ik = {"legL": plant("L", -1), "legR": plant("R", 1)}
    ik.update(extra)
    return ik


def pose(root=(0, 0, 0), world=None, ik=None):
    return {"root": root, "world": world or {}, "ik": ik or {}}


def hands_on_face(**legs):
    return {"armL": on_face("L", -1), "armR": on_face("R", 1), **legs}


def frozen_poses():
    """The statues (Dan: it should freeze in striking, varied, unnerving poses)."""
    lunge = pose((0.0, 0.03, -0.13), trunk(lean=24, side=3, twist=-8, neck=4, head=8, tilt=-5),
                 {"legL": plant("L", -1, dx=-0.03, dy=-0.48), "legR": plant("R", 1, dx=0.02, dy=0.46, toes=38),
                  **hands_on_face()})
    reach_pose = pose((0.0, -0.04, -0.03), trunk(lean=12, side=-3, twist=10, neck=4, head=6, tilt=20, turn=-6),
                      {"legL": plant("L", -1, dy=-0.22), "legR": plant("R", 1, dy=0.14, toes=14),
                       "armR": on_face("R", 1),
                       "armL": reach("L", -1, (-0.14, -0.78, 1.55), (0.05, -1.0, 0.12), (0.2, 0.2, -1.0))})
    weep = pose((0.02, 0.0, -0.01), trunk(lean=5, side=4, neck=6, head=12, tilt=-9, hips=(1, -4, 0)),
                standing(**hands_on_face()))
    creep = pose((0.0, 0.08, -0.36), trunk(lean=40, side=-4, twist=6, neck=-10, head=-14, tilt=14, turn=4),
                 {"legL": plant("L", -1, dx=-0.04, dy=-0.34), "legR": plant("R", 1, dx=0.03, dy=0.40, toes=42),
                  "armL": on_face("L", -1),
                  "armR": reach("R", 1, (0.20, -0.66, 0.42), (0.1, -0.7, -0.7), (0.0, 0.4, 1.0), pole_off=(0.3, 0.2, 0.1))})
    turn = pose((0.0, 0.0, -0.02), trunk(lean=6, twist=48, neck=2, head=10, tilt=-10, turn=-86, hips=(2, 0, 34)),
                {"legL": plant("L", -1, dx=0.06, dy=-0.18, yaw=34), "legR": plant("R", 1, dx=-0.02, dy=0.10, toes=16, yaw=22),
                 "armL": on_face("L", -1),
                 "armR": reach("R", 1, (0.30, 0.10, 0.98), (0.1, 0.1, -1.0), (-1.0, 0.0, 0.0), pole_off=(0.2, 0.3, 0.0))})
    return {"Lunge": lunge, "Reach": reach_pose, "Weep": weep, "Creep": creep, "Turn": turn}


def idle_keys():
    """A slow sob: settle, a catch of breath that lifts the shoulders, a long bowing
    exhale, the head tipping. 3 s, the hands on the face throughout."""
    beats = [
        (1, dict(lean=4, neck=5, head=9, tilt=-5), 0.0),
        (18, dict(lean=3, neck=5, head=7, tilt=-4), 0.0),
        (25, dict(lean=0, neck=2, head=2, tilt=-2), 0.012),   # the catch of breath
        (30, dict(lean=1, neck=3, head=4, tilt=-3), 0.008),
        (52, dict(lean=6, neck=8, head=13, tilt=-7), -0.008),  # the long exhale, bowing
        (70, dict(lean=5, neck=7, head=11, tilt=-6), -0.004),
        (90, dict(lean=4, neck=5, head=9, tilt=-5), 0.0),
    ]
    return [(f, pose((0, 0, lift), trunk(**t), standing(**hands_on_face()))) for f, t, lift in beats]


def run_keys():
    """The concept's lunge, running: pitched forward, the hands never leaving the
    face (it cannot look either), long low strides and a flight between them."""
    keys = []
    for f in range(1, RUN_FRAMES + 2):
        ph = (f - 1) / RUN_FRAMES
        legs = {}
        for tag, s, off in (("L", -1, 0.0), ("R", 1, 0.5)):
            q = (ph + off) % 1.0
            if q < RUN_STANCE:  # planted, swept back at the ground's speed
                t = q / RUN_STANCE
                dy, dz, toes = -RUN_SWEEP / 2 + RUN_SWEEP * t, 0.0, 10 + 30 * t
            else:               # the heel kicks up behind, the knee drives through, the foot reaches ahead
                t = (q - RUN_STANCE) / (1 - RUN_STANCE)
                dy = RUN_SWEEP / 2 - RUN_SWEEP * (0.5 - 0.5 * math.cos(math.pi * t))
                dz = 0.34 * math.sin(math.pi * t) ** 1.4
                toes = 40 * (1 - t) + 10 * t
            legs["leg" + tag] = plant(tag, s, dx=-s * 0.02, dy=dy, dz=dz, toes=toes)
        bob = -0.10 + 0.05 * math.cos(4 * math.pi * (ph - RUN_STANCE / 2))
        twist = 7 * math.cos(2 * math.pi * ph)
        keys.append((f, pose((0, 0, bob), trunk(lean=24, twist=twist, neck=2, head=4, hips=(8, 0, -twist)),
                             {**legs, **hands_on_face()})))
    return keys


def embrace_keys():
    """The embrace (Dan): the hands leave the face and take the diver's head, a beat
    of stillness, then death."""
    y = -HOLD_METERS
    head_c = Vector((0, y - 0.06, VICTIM_EYE + 0.02))  # the middle of the diver's head

    def take(depth=0.0, squeeze=0.0):
        arms = {}
        for tag, s in SIDES:
            wrist = head_c + Vector((s * (0.165 - squeeze), 0.10 - depth, -0.03))
            arms["arm" + tag] = reach(tag, s, wrist, (0.0, -1.0, 0.35), (-s, 0.0, 0.0), pole_off=(0.30, 0.05, -0.30))
        return arms

    def parted(amount):
        return {"arm" + t: on_face(t, s, slide=(s * 0.05 * amount, -0.06 * amount, 0.01 * amount), open_deg=35 * amount) for t, s in SIDES}

    def opened():
        arms = {}
        for tag, s in SIDES:
            arms["arm" + tag] = reach(tag, s, (s * 0.19, -0.36, 1.74), (s * 0.25, -0.35, 0.9), (s * 0.15, -1.0, 0.1), pole_off=(0.30, 0.05, -0.25))
        return arms

    legs = standing()
    lean_in = {"legL": plant("L", -1, dy=-0.10), "legR": plant("R", 1, dy=0.12, toes=10)}
    beats = [
        (1, (0, 0, 0), dict(lean=7, neck=8, head=10, tilt=-4), {**legs, **hands_on_face()}),
        (8, (0, 0, 0), dict(lean=8, neck=8, head=11, tilt=-6), {**legs, **hands_on_face()}),
        (13, (0, 0, 0), dict(lean=8, neck=8, head=11, tilt=-4), {**legs, **parted(0.15)}),
        (20, (0, 0.01, 0.005), dict(lean=5, neck=7, head=8, tilt=-2), {**legs, **parted(0.7)}),
        (25, (0, 0.02, 0.01), dict(lean=3, neck=6, head=8, tilt=0), {**legs, **opened()}),
        (28, (0, 0.02, 0.01), dict(lean=3, neck=6, head=9, tilt=1), {**legs, **opened()}),   # the face, bare
        (31, (0, -0.05, -0.03), dict(lean=16, neck=2, head=2, tilt=3), {**lean_in, **take(depth=0.02)}),  # the snap
        (35, (0, -0.04, -0.025), dict(lean=14, neck=2, head=1, tilt=2), {**lean_in, **take()}),
        (58, (0, -0.04, -0.025), dict(lean=14, neck=2, head=1, tilt=2), {**lean_in, **take()}),  # the stillness
        (KILL_FRAME, (0, -0.045, -0.03), dict(lean=15, neck=2, head=3, tilt=6), {**lean_in, **take(squeeze=0.02)}),
        (EMBRACE_FRAMES, (0, -0.045, -0.03), dict(lean=15, neck=2, head=3, tilt=6), {**lean_in, **take(squeeze=0.02)}),
    ]
    return [(f, pose(r, trunk(**t), ik)) for f, r, t, ik in beats]


def solved(rig, keys):
    return [(f, rig.solve(p)[0]) for f, p in keys]


def build_clips(rig, arm, height):
    if abs(height - H) > 1e-3:
        print("angel: WARNING the pose book is written for %.2f m, the rig is %.2f m" % (H, height))
    make_action(arm, "Idle", 90, solved(rig, idle_keys()))
    make_action(arm, "Hunting", RUN_FRAMES + 1, solved(rig, run_keys()))
    statues = frozen_poses()
    strip = []
    for i, name in enumerate(FROZEN_POSES):
        keys = rig.solve(statues[name])[0]
        strip += [(1 + i * SEG, keys), ((i + 1) * SEG, keys)]
    make_action(arm, "Frozen", SEG * len(FROZEN_POSES), strip, interpolation="CONSTANT")
    make_action(arm, "Grabbing", EMBRACE_FRAMES, solved(rig, embrace_keys()))
    print("angel: clips Idle (90), Hunting (%d, %.2f m/s at 1x), Frozen (%d poses x %d), Grabbing (%d, kill at %d)"
          % (RUN_FRAMES + 1, RUN_SPEED, len(FROZEN_POSES), SEG, EMBRACE_FRAMES, KILL_FRAME))
