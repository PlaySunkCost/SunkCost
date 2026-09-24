"""The Listener's own rig fit and clips (mon-listener, 24 September 2026;
docs/DESIGN.md §6, docs/MONSTER_MODELS.md "The Listener").

rig_generated_monster.py runs animate(arm, height, L) after the shared library.
Every Listener clip is keyed here, so the shared upright clips never reach it.

The fit. The shared layout put the head bone above and behind Dan's Meshy skull
(it ends at 2.0 m; the skull hangs forward, 1.5-1.9 m), so bone heat gave the
skull to the neck, the spine, the ears and even the arms (the head bone carried
about a tenth of it), and the ear fans' lower edges to the upper arms. Here the
neck and head bones are moved into the skull, the ear bones to the fans' roots,
the skull is skinned to the head and each fan to its ear, and the anchors are
put back: BeamOrigin on the lower face where the mouth would be (the beam leaves
there), Voice inside the skull.

The body language: blind, it lives by its ears. Idle it stands still and
listens, the ears breathing and flicking one at a time, the head cocking toward
one side and then the other. Drawn (toward a thing's sound, and home) is a slow
careful walk on its toes, the head tilted, the ears turning forward. Hunting
(toward a diver's sound) drops into the crouched stalk of the concept picture,
hands forward, the ears spread like dishes, quicker. Aiming is the 1 s charge:
it drops and plants its feet wide, the head draws back and then pushes forward,
the ears snap open and quiver as the charge builds. Shooting is the 3 s burn:
a kick as it lights, then it leans into the beam, shaking with it, the ears
fully spread. Recovering: the ears fold, it shakes its head and rises.

Poses are written as rotations in the armature's axes, relative to the parent
bone (+X pitches forward, +Z turns to its left, the Listener faces -Y and its
"L" side is -X). The legs are solved, not swung: the toe of a standing foot is
held on the floor and travels backward at the clip's design speed (SPEEDS), and
the thigh and the shin reach the ankle behind it, so the feet do not slide at
that speed (CreatureRig scales the playback to the real speed).
"""
import math

import bpy
from mathutils import Euler, Matrix, Vector

FPS = 30
CLIPS = ("Aiming", "Recovering")
# m/s the stance toes are planted for: Hunting at the Listener's approach (0.6 of a
# diver's 4 m/s walk); Drawn between the drift home (1.4) and the approach.
SPEEDS = {"Drawn": 1.9, "Hunting": 2.4}
TAU = 2.0 * math.pi
REPORT = {}


# ---- small maths -------------------------------------------------------------------------

def clamp01(x):
    return 0.0 if x < 0.0 else 1.0 if x > 1.0 else x


def smooth(x):
    x = clamp01(x)
    return x * x * (3.0 - 2.0 * x)


def ease(t, a, b):
    """0 before a, 1 after b, smooth between."""
    return smooth((t - a) / (b - a)) if b > a else (1.0 if t >= b else 0.0)


def bump(t, a, b):
    if t <= a or t >= b:
        return 0.0
    return math.sin(math.pi * (t - a) / (b - a)) ** 2


def spring(t, t0, amp, freq, decay):
    """A decaying oscillation from t0: overshoot and settle."""
    if t < t0:
        return 0.0
    s = t - t0
    return amp * math.exp(-decay * s) * math.sin(TAU * freq * s)


def lerp(a, b, t):
    return a + (b - a) * t


def noise(t, seed):
    """A smooth, repeatable wobble in [-1, 1] (a sum of incommensurate sines)."""
    return (math.sin(t * 7.3 + seed) * 0.5 + math.sin(t * 13.1 + seed * 2.7) * 0.3 + math.sin(t * 23.7 + seed * 5.1) * 0.2)


# ---- the fit ------------------------------------------------------------------------------

def mesh_of(arm):
    for o in bpy.data.objects:
        if o.type == "MESH" and o.parent is arm:
            return o
    return next(o for o in bpy.data.objects if o.type == "MESH")


def refit(arm, mesh, L):
    # The shared clips leave the pose at their last key: back to rest before anything is measured or placed.
    for p in arm.pose.bones:
        p.rotation_mode = "XYZ"
        p.rotation_euler = (0, 0, 0)
        p.location = (0, 0, 0)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    # The neck from the shoulders' top to the back of the skull; the head through the skull to its crown.
    eb["Neck"].head = Vector((0.0, 0.01, 1.50))
    eb["Neck"].tail = Vector((0.0, -0.05, 1.61))
    eb["Head"].head = Vector((0.0, -0.05, 1.61))
    eb["Head"].tail = Vector((0.0, -0.14, 1.88))
    eb["Neck"].roll = eb["Head"].roll = 0.0
    # The ears from the fans' roots at the temples, outward and up along the fan.
    for s, tag in ((-1, "L"), (1, "R")):
        e = eb["Ear" + tag]
        e.head = Vector((s * 0.085, -0.12, 1.70))
        e.tail = Vector((s * 0.36, -0.10, 1.86))
        e.roll = 0.0
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)

    # The weights by region (rest coordinates; the mesh is one island).
    groups = {g.name: g for g in mesh.vertex_groups}
    for name in ("Head", "Neck", "Spine", "EarL", "EarR"):
        if name not in groups:
            groups[name] = mesh.vertex_groups.new(name=name)
    index = {g.index: g.name for g in mesh.vertex_groups}
    counts = {"skull": 0, "ear": 0, "neck": 0, "stripped": 0}
    mw = mesh.matrix_world
    for v in mesh.data.vertices:
        p = mw @ v.co
        ax = abs(p.x)
        ear = 0.0
        if p.z > 1.50:
            ear = smooth((ax - 0.085) / 0.03) * smooth((p.z - 1.50) / 0.02)
        back_line = -0.02 - max(0.0, 1.66 - p.z) * 0.5  # the back of the skull, leaning forward to the chin
        skull = ax <= 0.11 and p.z > 1.47 and (p.z >= 1.66 or p.y < back_line)
        if ear > 0.0 or skull:
            for g in list(v.groups):
                mesh.vertex_groups[g.group].remove([v.index])
            if ear > 0.0:
                groups["EarL" if p.x < 0 else "EarR"].add([v.index], ear, "REPLACE")
                counts["ear"] += 1
            if ear < 1.0:
                if skull:
                    groups["Head"].add([v.index], 1.0 - ear, "REPLACE")
                    counts["skull"] += 1
                else:
                    groups["Neck"].add([v.index], 1.0 - ear, "REPLACE")
            continue
        if ax < 0.11 and 1.44 < p.z <= 1.66:
            # The neck: from the spine at the shoulders to the skull.
            n = smooth((p.z - 1.45) / 0.12)
            for g in list(v.groups):
                mesh.vertex_groups[g.group].remove([v.index])
            groups["Neck"].add([v.index], n, "REPLACE")
            if n < 1.0:
                groups["Spine"].add([v.index], 1.0 - n, "REPLACE")
            counts["neck"] += 1
            continue
        # Anywhere else, nothing of the head or the ears (bone heat leaked them onto the chest and arms).
        stray = [g for g in v.groups if index[g.group] in ("Head", "EarL", "EarR")]
        if stray:
            for g in stray:
                mesh.vertex_groups[g.group].remove([v.index])
            if len(v.groups) == 0:
                groups["Spine"].add([v.index], 1.0, "REPLACE")
            counts["stripped"] += 1
    REPORT["weights"] = counts

    # The anchors back on the head: the beam's origin just off the lower face, the voice in the skull.
    for name, world in (("BeamOrigin", (0.0, -0.285, 1.57)), ("Voice", (0.0, -0.13, 1.72))):
        o = bpy.data.objects.get(name)
        if o is None:
            continue
        o.parent = arm
        o.parent_type = "BONE"
        o.parent_bone = "Head"
        bpy.context.view_layer.update()
        o.matrix_world = Matrix.Translation(Vector(world))
    bpy.context.view_layer.update()
    # The planted points, on each foot's tip (what CreatureRigStrides measures, as ik_kit.add_toe_anchors).
    for s in "LR":
        if bpy.data.objects.get("Toe" + s) is None:
            L.anchor("Toe" + s, tuple(arm.data.bones["Foot" + s].tail_local), arm, "Foot" + s)
    bpy.context.view_layer.update()


# ---- posing -----------------------------------------------------------------------------

LEGS = {"L": ("ThighL", "ShinL", "FootL"), "R": ("ThighR", "ShinR", "FootR")}


class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.pb = arm.pose.bones
        self.rest = {b.name: b.matrix_local.to_3x3() for b in arm.data.bones}
        bones = arm.data.bones
        self.toe = {s: bones[f].tail_local.copy() for s, (_, _, f) in LEGS.items()}
        self.foot_vec = {s: (bones[f].tail_local - bones[f].head_local) for s, (_, _, f) in LEGS.items()}
        self.worst = 0.0
        self.clamped = 0

    def reset(self):
        for p in self.pb:
            p.rotation_mode = "XYZ"
            p.rotation_euler = (0, 0, 0)
            p.location = (0, 0, 0)

    def turn(self, name, pitch=0.0, yaw=0.0, roll=0.0):
        """A rotation in the armature's axes, relative to the parent: pitch about +X
        (forward), yaw about +Z (to its left), roll about -Y (its right side down)."""
        w = (Matrix.Rotation(math.radians(yaw), 3, "Z") @ Matrix.Rotation(math.radians(roll), 3, (0, -1, 0)) @ Matrix.Rotation(math.radians(pitch), 3, "X"))
        r = self.rest[name]
        self.pb[name].rotation_euler = (r.inverted() @ w @ r).to_euler("XYZ")

    def shift(self, name, offset):
        """Move an unconnected bone (the Root) by a world offset in metres."""
        self.pb[name].location = self.rest[name].inverted() @ Vector(offset)

    def world(self, name, tail=False):
        p = self.pb[name]
        return self.arm.matrix_world @ (p.tail if tail else p.head)

    def leg(self, side, toe_offset, heel_lift=0.0, spread=0.0, stance=True):
        """Put the toe at its rest spot plus toe_offset (x, y, z in metres, -y forward),
        the foot pitched heel-up by heel_lift degrees, the ankle reached by the thigh
        and the shin in the leg's plane (a two-bone solve, the knee forward)."""
        thigh, shin, foot = LEGS[side]
        for n in LEGS[side]:
            self.pb[n].rotation_euler = (0, 0, 0)
        bpy.context.view_layer.update()
        toe = self.toe[side] + Vector(toe_offset)
        fv = self.foot_vec[side].copy()
        fv = Matrix.Rotation(math.radians(heel_lift), 3, "X") @ fv  # heel up: the ankle rises behind the toe
        ankle = toe - fv
        hip = self.world(thigh)
        knee0 = self.world(shin)
        ankle0 = self.world(foot)
        la = (knee0 - hip).length
        lb = (ankle0 - knee0).length
        target = ankle - hip
        d = target.length
        lo, hi = abs(la - lb) + 1e-4, (la + lb) * 0.999
        if d > hi or d < lo:
            if stance:
                self.clamped += 1
            d = max(lo, min(hi, d))
        # Work in the leg's plane: the (y, z) plane through the hip, turned by the spread.
        ty, tz = target.y, target.z
        dist2 = math.hypot(ty, tz)
        scale = d / max(dist2, 1e-9)
        ty, tz = ty * scale, tz * scale
        a = math.atan2(tz, ty)
        cos_k = (la * la + d * d - lb * lb) / (2 * la * d)
        k = math.acos(max(-1.0, min(1.0, cos_k)))
        # The knee forward (-y): of the two bends, the one whose knee is further forward.
        best = None
        for sgn in (1.0, -1.0):
            ka = a + sgn * k
            kn = (math.cos(ka) * la, math.sin(ka) * la)
            if best is None or kn[0] < best[0][0]:
                best = (kn, ka)
        kn, _ = best
        # Rotate the thigh so its end is at the knee, then the shin so its end is at the ankle.
        d_th0 = knee0 - hip
        want_th = Vector((0.0, kn[0], kn[1]))
        alpha = math.atan2(d_th0.y * want_th.z - d_th0.z * want_th.y, d_th0.y * want_th.y + d_th0.z * want_th.z)
        self.pb[thigh].rotation_euler = (alpha, 0, math.radians(spread))
        bpy.context.view_layer.update()
        knee = self.world(shin)
        d_sh0 = self.world(foot) - knee
        want_sh = (hip + Vector((0.0, ty, tz))) - knee
        beta = math.atan2(d_sh0.y * want_sh.z - d_sh0.z * want_sh.y, d_sh0.y * want_sh.y + d_sh0.z * want_sh.z)
        self.pb[shin].rotation_euler = (beta, 0, 0)
        bpy.context.view_layer.update()
        a_now = self.world(foot)
        d_ft0 = self.world(foot, tail=True) - a_now
        gamma = math.atan2(d_ft0.y * fv.z - d_ft0.z * fv.y, d_ft0.y * fv.y + d_ft0.z * fv.z)
        self.pb[foot].rotation_euler = (gamma, 0, 0)
        bpy.context.view_layer.update()
        if stance:
            err = (self.world(foot, tail=True) - toe)
            err.x = 0.0  # the spread is not solved in x
            self.worst = max(self.worst, err.length)

    def snapshot(self):
        return {p.name: (tuple(p.rotation_euler), tuple(p.location)) for p in self.pb}


def ears(rig, fold_back, raise_deg, twist=0.0, left=(0.0, 0.0), right=(0.0, 0.0)):
    """Both fans: folded back (negative = turned forward, cupped at what it hears),
    raised (the tips up), twisted about their own length; per-ear extras (fold, raise)."""
    rig.turn("EarL", yaw=-(fold_back + left[0]), roll=-(raise_deg + left[1]), pitch=twist)
    rig.turn("EarR", yaw=(fold_back + right[0]), roll=(raise_deg + right[1]), pitch=twist)


def arms(rig, lift, spread, elbow, wrist=0.0, swing_l=0.0, swing_r=0.0, curl=0.0):
    """The long arms: lifted forward (pitch), spread out to the sides, elbows bent,
    wrists cocked; swing is extra forward pitch per arm."""
    rig.turn("UpperArmL", pitch=-(lift + swing_l), yaw=0.0, roll=-spread)
    rig.turn("UpperArmR", pitch=-(lift + swing_r), yaw=0.0, roll=spread)
    rig.turn("LowerArmL", pitch=-elbow)
    rig.turn("LowerArmR", pitch=-elbow)
    rig.turn("HandL", pitch=-wrist, roll=-curl)
    rig.turn("HandR", pitch=-wrist, roll=curl)


def bake(arm, rig, name, seconds, pose_fn, loop):
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    frames = max(2, int(round(seconds * FPS)) + 1)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        ad.action_slot = act.slots.new(id_type="OBJECT", name=arm.name)
    except (AttributeError, TypeError):
        pass
    rig.worst = 0.0
    rig.clamped = 0
    first = None
    for i in range(frames):
        t = i / FPS
        if loop and i == frames - 1 and first is not None:
            snap = first  # the loop closes exactly
            for pbn, (rot, loc) in snap.items():
                rig.pb[pbn].rotation_euler = rot
                rig.pb[pbn].location = loc
        else:
            rig.reset()
            pose_fn(rig, t)
            if i == 0:
                first = rig.snapshot()
        for p in rig.pb:
            p.keyframe_insert("rotation_euler", frame=i + 1)
            p.keyframe_insert("location", frame=i + 1)
    act.frame_range = (1, frames)
    try:
        act.use_frame_range = True
    except AttributeError:
        pass
    ad.action = None
    rig.reset()
    REPORT[name] = {"seconds": seconds, "frames": frames, "ik_worst_m": round(rig.worst, 4), "clamped": rig.clamped}
    return act


# ---- the clips ---------------------------------------------------------------------------

def idle(rig, t):
    """3.2 s loop: still, listening. Breath, a weight shift, the head cocking toward one
    side and then the other, the ears breathing and one flicking at a time."""
    T = 3.2
    ph = t / T * TAU
    breath = math.sin(ph * 2)
    rig.shift("Root", (0.012 * math.sin(ph), 0.0, -0.035 + 0.006 * breath))
    rig.turn("Hips", roll=1.5 * math.sin(ph), yaw=1.0 * math.sin(ph))
    rig.turn("Spine", pitch=6 + 1.5 * breath, roll=-1.2 * math.sin(ph))
    # The listening: toward its left over the first half, its right over the second, a hold at each.
    cock = ease(t, 0.35, 0.75) * (1 - ease(t, 1.35, 1.7)) - 0.7 * ease(t, 1.95, 2.3) * (1 - ease(t, 2.8, 3.15))
    rig.turn("Neck", pitch=4 + 1.0 * breath, yaw=10 * cock)
    rig.turn("Head", pitch=-6 - 1.5 * breath, yaw=14 * cock, roll=-12 * cock)
    # The ears breathe; the one on the side it turns to opens toward the sound; a flick each.
    flick_l = spring(t, 0.55, 14, 7.0, 9.0)
    flick_r = spring(t, 2.15, 14, 7.0, 9.0)
    ears(rig, 14 + 3 * breath, 4 + 2 * breath,
         left=(-10 * max(0.0, cock) + flick_l, 0.6 * flick_l),
         right=(-10 * max(0.0, -cock) + flick_r, 0.6 * flick_r))
    sway = 2.0 * math.sin(ph + 0.6)
    arms(rig, lift=4 + sway, spread=4, elbow=10 + 1.5 * breath, wrist=6, curl=4 + 3 * breath)
    # The feet stay planted; the knees soft.
    for s in ("L", "R"):
        rig.leg(s, (0.0, 0.0, 0.0), heel_lift=4.0)


def gait(t, T, duty, stride, lift, side_phase):
    """One foot's toe offset and heel lift for a walk: planted and moving back at
    stride/T while in stance, forward in an arc in the swing."""
    p = (t / T + side_phase) % 1.0
    if p < duty:
        u = p / duty
        y = lerp(-stride / 2, stride / 2, u)  # -y is forward: from in front back under the body
        heel = 30.0 * ease(u, 0.7, 1.0)  # push-off: the heel lifts before the toe leaves
        return (0.0, y, 0.0), heel, True, u
    w = (p - duty) / (1 - duty)
    y = lerp(stride / 2, -stride / 2, smooth(w))
    z = lift * math.sin(math.pi * w) ** 1.2
    heel = lerp(30.0, -8.0, smooth(w * 1.4))  # toe hanging after the push, then level to land toe first
    return (0.0, y, z), heel, False, w


def walk_pose(rig, t, T, speed, duty, lift, crouch, lean, head_pitch, cock, ear_fold, ear_raise,
              arm_lift, arm_swing, elbow, spread=6):
    stride = speed * T  # a full cycle's travel; each foot's stance covers speed × duty × T
    stance = stride * duty
    ph = t / T * TAU
    # Down at each contact (twice a cycle), up at mid-stance.
    bob = 0.025 * math.cos(2 * ph)
    rig.shift("Root", (0.015 * math.sin(ph), 0.0, -crouch - 0.012 - bob))
    rig.turn("Hips", yaw=-5 * math.sin(ph), roll=2.5 * math.sin(ph))
    rig.turn("Spine", pitch=lean + 1.5 * math.cos(2 * ph), yaw=6 * math.sin(ph), roll=-2 * math.sin(ph))
    # The head steadied against the body's turn, cocked to listen.
    rig.turn("Neck", pitch=-lean * 0.45, yaw=-3 * math.sin(ph))
    rig.turn("Head", pitch=head_pitch - lean * 0.2 + 1.2 * math.cos(2 * ph + 0.8), yaw=-2 * math.sin(ph), roll=cock)
    # The ears lag the step (overlap): a small bounce half a beat behind.
    bounce = math.sin(2 * ph - 1.2)
    ears(rig, ear_fold + 2.5 * bounce, ear_raise - 2.0 * bounce)
    arms(rig, lift=arm_lift, spread=spread, elbow=elbow + 4 * math.sin(ph), wrist=8,
         swing_l=arm_swing * math.sin(ph + math.pi), swing_r=arm_swing * math.sin(ph), curl=6)
    for s, off in (("L", 0.0), ("R", 0.5)):
        toe, heel, planted, _ = gait(t, T, duty, stance, lift, off)
        rig.leg(s, toe, heel_lift=heel, stance=planted)


def drawn(rig, t):
    """0.77 s loop: careful, on its toes, the head tilted, the ears turned forward."""
    T = 23.0 / FPS  # a whole number of frames, so the loop closes on the step
    walk_pose(rig, t, T, SPEEDS["Drawn"], duty=0.58, lift=0.11, crouch=0.06, lean=10, head_pitch=-6,
              cock=10 + 3 * math.sin(t / T * TAU), ear_fold=-4, ear_raise=6, arm_lift=10, arm_swing=9, elbow=22)


def hunting(rig, t):
    """0.67 s loop: the crouched stalk — low, the torso forward, the hands forward and
    open, the ears spread wide and cupped forward, quicker and longer steps."""
    T = 20.0 / FPS
    walk_pose(rig, t, T, SPEEDS["Hunting"], duty=0.55, lift=0.09, crouch=0.2, lean=32, head_pitch=-8,
              cock=0, ear_fold=-16, ear_raise=14, arm_lift=48, arm_swing=6, elbow=30, spread=14)


def planted(rig, t, dip, crouch):
    """Feet wide and braced for a shot: the left forward, the right back."""
    rig.leg("L", (-0.07, -0.16, 0.0), heel_lift=6.0, spread=-4)
    rig.leg("R", (0.07, 0.20, 0.0), heel_lift=16.0, spread=4)


def aiming(rig, t):
    """1.25 s (the 1 s charge, a quarter second of margin that only holds): it drops
    and plants itself — a dip past the crouch and a settle — the head draws back and
    then pushes forward at the target, the ears snap open, then quiver as it builds."""
    drop = ease(t, 0.0, 0.18)
    dip = bump(t, 0.08, 0.36)
    build = ease(t, 0.3, 1.0)
    quiver = math.sin(t * TAU * 11) * build
    tremble = noise(t * 2.0, 1.3) * build
    rig.shift("Root", (0.0, 0.03 * drop, -0.02 - 0.26 * drop - 0.06 * dip + 0.004 * tremble))
    rig.turn("Hips", pitch=-4 * drop, yaw=4 * drop)
    rig.turn("Spine", pitch=8 + 22 * drop + 4 * dip + 3 * build + 0.6 * tremble, yaw=-4 * drop)
    # The head: back first (the anticipation), then forward to the target, level.
    back = bump(t, 0.02, 0.3)
    rig.turn("Neck", pitch=-6 * drop - 10 * back + 8 * build, yaw=0.5 * tremble)
    rig.turn("Head", pitch=-14 * drop - 12 * back - 6 * build + 0.8 * tremble)
    # The ears snap open (overshoot and settle), then quiver with the charge.
    snap = ease(t, 0.05, 0.2) + spring(t, 0.2, 0.25, 4.0, 7.0)
    ears(rig, 14 - 40 * snap, 4 + 16 * snap, twist=-6 * snap,
         left=(2.5 * quiver, 1.5 * quiver), right=(-2.5 * quiver, -1.5 * quiver))
    # The arms swing out wide and low, the claws open.
    open_ = ease(t, 0.04, 0.3)
    arms(rig, lift=4 + 26 * open_, spread=4 + 40 * open_, elbow=10 + 14 * open_, wrist=6 - 20 * open_,
         curl=4 - 16 * open_ + 2 * tremble)
    planted(rig, t, dip, drop)


def shooting(rig, t):
    """3.3 s (the 3 s burn, then a hold): the kick as it lights — the head and the
    shoulders thrown back, the ears blown back — then it leans into the beam, shaking
    with it, the ears spread and trembling, a shudder now and then."""
    kick = spring(t, 0.0, 1.0, 2.2, 6.0)  # + back, then forward past rest, settling
    lean = ease(t, 0.1, 0.5)
    shake = noise(t * 3.0, 4.2)
    shudder = bump(t, 1.2, 1.45) + bump(t, 2.3, 2.5)
    rig.shift("Root", (0.004 * shake, 0.03 + 0.05 * max(0.0, kick) - 0.02 * lean, -0.30 + 0.008 * shake - 0.02 * shudder))
    rig.turn("Hips", pitch=-4, yaw=4)
    rig.turn("Spine", pitch=33 - 8 * kick + 4 * lean + 1.2 * shake + 3 * shudder, yaw=-4, roll=0.8 * shake)
    rig.turn("Neck", pitch=2 - 10 * kick + 3 * lean + 0.8 * shake, yaw=0.6 * shake)
    rig.turn("Head", pitch=-20 - 6 * kick - 2 * lean + 1.0 * shake, roll=0.6 * shake)
    vib = math.sin(t * TAU * 17)
    blow = max(0.0, kick)
    ears(rig, -26 + 30 * blow + 2 * shudder, 20 - 6 * blow, twist=-6,
         left=(3 * vib, 2 * vib), right=(-3 * vib, -2 * vib))
    arms(rig, lift=30 - 10 * blow + 2 * shake, spread=44 + 6 * blow, elbow=24 + 6 * shudder, wrist=-14,
         curl=-12 + 6 * shudder)
    planted(rig, t, 0.0, 1.0)


def recovering(rig, t):
    """0.9 s, holds its end: the ears fold, it shakes its head twice, and rises back
    toward its standing listen (the end is Idle's first frame, near enough)."""
    rise = ease(t, 0.15, 0.85)
    shake = math.sin(t * TAU * 3.2) * bump(t, 0.1, 0.65)
    rig.shift("Root", (0.0, 0.03 * (1 - rise), lerp(-0.30, -0.035, rise)))
    rig.turn("Hips", pitch=-4 * (1 - rise), yaw=4 * (1 - rise))
    rig.turn("Spine", pitch=lerp(33, 6, rise) + 4 * bump(t, 0.05, 0.35), yaw=-4 * (1 - rise))
    rig.turn("Neck", pitch=lerp(2, 4, rise) + 8 * bump(t, 0.05, 0.4), yaw=10 * shake)
    rig.turn("Head", pitch=lerp(-20, -6, rise) + 10 * bump(t, 0.05, 0.4), yaw=14 * shake, roll=-6 * shake)
    fold = ease(t, 0.0, 0.3)
    ears(rig, lerp(-26, 14, fold) + 6 * bump(t, 0.2, 0.5), lerp(20, 4, fold) - 6 * bump(t, 0.2, 0.55))
    arms(rig, lift=lerp(30, 4, rise), spread=lerp(44, 4, rise), elbow=lerp(24, 10, rise), wrist=lerp(-14, 6, rise), curl=lerp(-12, 4, rise))
    rig.leg("L", (lerp(-0.07, 0.0, rise), lerp(-0.16, 0.0, rise), 0.03 * bump(t, 0.35, 0.7)), heel_lift=lerp(6, 4, rise), spread=lerp(-4, 0, rise), stance=t < 0.35 or t > 0.7)
    rig.leg("R", (lerp(0.07, 0.0, rise), lerp(0.20, 0.0, rise), 0.03 * bump(t, 0.2, 0.55)), heel_lift=lerp(16, 4, rise), spread=lerp(4, 0, rise), stance=t < 0.2 or t > 0.55)


# ---- the report -----------------------------------------------------------------------

def measure(arm, rig, name, when):
    """Where BeamOrigin sits (metres from the feet, +z up, -y forward) at a moment of a clip."""
    act = bpy.data.actions[name]
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    try:
        slots = list(act.slots)
        if slots:
            ad.action_slot = slots[0]
    except AttributeError:
        pass
    bpy.context.scene.frame_set(1 + int(round(when * FPS)))
    bpy.context.view_layer.update()
    o = bpy.data.objects.get("BeamOrigin")
    p = o.matrix_world.translation.copy() if o is not None else Vector()
    ad.action = None
    rig.reset()
    bpy.context.view_layer.update()
    return [round(p.x, 3), round(p.y, 3), round(p.z, 3)]


def animate(arm, height, L):
    bpy.context.scene.render.fps = FPS
    mesh = mesh_of(arm)
    refit(arm, mesh, L)
    rig = Rig(arm)
    bake(arm, rig, "Idle", 3.2, idle, loop=True)
    bake(arm, rig, "Drawn", 23.0 / FPS, drawn, loop=True)
    bake(arm, rig, "Hunting", 20.0 / FPS, hunting, loop=True)
    bake(arm, rig, "Aiming", 1.25, aiming, loop=False)  # it loops in Unity, but a charge ends at 1 s: no closing back to the stance
    bake(arm, rig, "Shooting", 3.3, shooting, loop=False)
    bake(arm, rig, "Recovering", 0.9, recovering, loop=False)
    REPORT["speeds"] = SPEEDS
    REPORT["beam_origin"] = {
        "rest": measure(arm, rig, "Idle", 0.0),
        "aiming_0.5": measure(arm, rig, "Aiming", 0.5),
        "aiming_1.0": measure(arm, rig, "Aiming", 1.0),
        "shooting_0.05": measure(arm, rig, "Shooting", 0.05),
        "shooting_1.5": measure(arm, rig, "Shooting", 1.5),
        "shooting_3.0": measure(arm, rig, "Shooting", 3.0),
    }
    print("LISTENER_CLIPS", REPORT)
