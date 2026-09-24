"""The Long Walker's own clips (mon-walker, 24 September 2026), run by
rig_generated_monster.py after the shared library: animate(arm, height, L).

Its body language: very tall and patient. It never hurries, it lopes, long legs
and long arms swinging loose and a beat behind the body, the head held level on
its prey. Standing, it breathes slowly, sways its weight and cocks its head the
way something curious and wrong would. When it catches you it lunges, its long
hands close round your chest, and it straightens up and lifts you to its face,
tilts its head at you, and lets the body drop.

Every clip is authored with IK: the feet are pinned to the ground (the walk's
feet travel backward at exactly the walking speed, so nothing slides), and the
hands follow the held diver along the same path the game holds them on
(CreatureGrab on the prefab). The IK is then baked to plain bone keys, and the
helpers are removed, so the FBX carries only FK keys as before.

  Idle      4.0 s loop  breathing, weight shift, the head tilts and twitches
  Hunting   1.03 s loop the lope at WALK_SPEED (in place; the server moves it)
  Drawn     = Hunting (the Walker's brain never uses it; kept in step)
  Grabbing  3.0 s       lunge, grip, lift to the face, hold, drop, settle
"""
import math

import bpy
from mathutils import Vector

CLIPS = ("Grabbing",)
FPS = 30

# The walk: 0.6 x a diver's 4 m/s walk (MonsterSettings.walkerSpeedFactor). The
# clip's feet move backward at this speed while planted; CreatureRig may scale the
# playback by the real speed over this one.
WALK_SPEED = 2.4
WALK_FRAMES = 32            # one cycle, two steps: 31 frames of motion, the 32nd is the first again (1.033 s)
WALK_DUTY = 0.56            # the part of the cycle a foot is planted
WALK_HIP = 1.49             # hip height while walking (rest 1.66: a slight crouch)

# The grab, in Blender's axes (x = the creature's left is -x, forward is -y, up z).
# The game's CreatureGrab numbers in Unity's (x right, y up, z forward) are the same
# points: Unity (x, y, z) = Blender (-x, -z, y). Keep the two in step.
GRIP_SECONDS = 0.35
LIFT_SECONDS = 1.3
HOLD_SECONDS = 2.0
RELEASE_SECONDS = 0.8
GRAB_FRAMES = 90            # 3.0 s: the server holds the pose for 2.8 s
GRIP_FORWARD = 0.85         # the held diver's centre, forward of the creature
LIFT_UP = 1.35              # how high the diver's feet are lifted: their eyes (1.6 m) level with its face (~3.0 m)
CHEST = 1.15                # the diver's chest over their feet, where the hands close
WRIST_OUT = 0.36            # the wrists either side of the diver's chest
WRIST_BACK = 0.15           # the wrists a little behind the diver's centre: the long claws wrap forward


def _ease(u):
    u = max(0.0, min(1.0, u))
    return u * u * (3 - 2 * u)


def _lift_ease(u):
    """0 -> 1.04 at 0.72 -> 1: the weight of a body coming up (CreatureGrab.liftEase)."""
    u = max(0.0, min(1.0, u))
    if u < 0.72:
        return 1.04 * _ease(u / 0.72)
    return 1.04 - 0.04 * _ease((u - 0.72) / 0.28)


def _frame(seconds):
    return 1 + seconds * FPS


# ---- the IK helpers ---------------------------------------------------------------

def _empty(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new("_ik_" + name, None)
    bpy.context.collection.objects.link(e)
    e.location = loc
    return e


def _key_loc(obj, frame, loc):
    obj.location = loc
    obj.keyframe_insert("location", frame=frame)


class Rig:
    """Temporary IK on the legs (ankle + toe targets), the arms (wrist targets with
    elbow poles) and damped tracks on the hands, for authoring one clip."""

    def __init__(self, arm, arms_ik=True, hands_track=False):
        self.arm = arm
        self.objs = []
        self.cons = []
        self.t = {}
        for side, s in (("L", -1), ("R", 1)):
            ankle = self._e("ankle" + side, arm.data.bones["Shin" + side].tail_local)
            toe = self._e("toe" + side, arm.data.bones["Foot" + side].tail_local)
            knee = self._e("knee" + side, (s * 0.16, -1.6, 1.0))
            self._ik("Shin" + side, ankle, 2, knee, "Thigh" + side)
            self._ik("Foot" + side, toe, 1)
            if arms_ik:
                wrist = self._e("wrist" + side, arm.data.bones["LowerArm" + side].tail_local)
                elbow = self._e("elbow" + side, (s * 1.2, 0.6, 1.7))
                self._ik("LowerArm" + side, wrist, 2, elbow, "UpperArm" + side)
            if hands_track:
                aim = self._e("aim" + side, (0, -1.2, 1.2))
                c = arm.pose.bones["Hand" + side].constraints.new("DAMPED_TRACK")
                c.target = aim
                c.track_axis = "TRACK_Y"
                c.influence = 0.0
                self.cons.append(c)
                self.t["track" + side] = c

    def _e(self, name, loc):
        e = _empty(name, tuple(loc))
        self.objs.append(e)
        self.t[name] = e
        return e

    def _ik(self, bone, target, chain, pole=None, pole_bone=None):
        pb = self.arm.pose.bones[bone]
        c = pb.constraints.new("IK")
        c.target = target
        c.chain_count = chain
        c.use_stretch = False
        if pole is not None:
            c.pole_target = pole
            c.pole_angle = self._best_pole(c, pole, pole_bone or bone)
        self.cons.append(c)
        return c

    def _best_pole(self, c, pole, joint_bone):
        """The pole angle that puts the joint (the tail of joint_bone) nearest the pole."""
        best, best_d = 0.0, 1e9
        for deg in range(-180, 180, 15):
            c.pole_angle = math.radians(deg)
            bpy.context.view_layer.update()
            joint = self.arm.matrix_world @ self.arm.pose.bones[joint_bone].tail
            head = self.arm.matrix_world @ self.arm.pose.bones[joint_bone].head
            to_pole = (Vector(pole.location) - head).normalized()
            to_joint = (joint - head).normalized()
            d = -to_pole.dot(to_joint)
            if d < best_d:
                best, best_d = math.radians(deg), d
        return best

    def bake(self, name, frames, author):
        """Bake the pose (FK author keys + the IK) to a plain action `name`."""
        arm = self.arm
        ad = arm.animation_data or arm.animation_data_create()
        ad.action = author
        try:
            if author.slots:
                ad.action_slot = author.slots[0]
        except AttributeError:
            pass
        bpy.context.view_layer.objects.active = arm
        for o in bpy.context.view_layer.objects:
            o.select_set(o == arm)
        bpy.ops.object.mode_set(mode="POSE")
        for pb in arm.pose.bones:
            if hasattr(pb, "select"):
                pb.select = True        # Blender 5: selection lives on the pose bone
            else:
                pb.bone.select = True
        bpy.ops.nla.bake(frame_start=1, frame_end=frames, step=1, only_selected=True, visual_keying=True,
                         clear_constraints=False, clear_parents=False, use_current_action=False,
                         clean_curves=False, bake_types={"POSE"})
        bpy.ops.object.mode_set(mode="OBJECT")
        baked = ad.action
        old = bpy.data.actions.get(name)
        if old is not None and old != baked:
            bpy.data.actions.remove(old)
        baked.name = name
        baked.use_fake_user = True
        baked.frame_range = (1, frames)
        try:
            baked.use_frame_range = True
        except AttributeError:
            pass
        ad.action = None
        bpy.data.actions.remove(author)
        return baked

    def remove(self):
        for pb in self.arm.pose.bones:
            for c in list(pb.constraints):
                if c in self.cons:
                    pb.constraints.remove(c)
        for o in self.objs:
            if o.animation_data and o.animation_data.action:
                bpy.data.actions.remove(o.animation_data.action)
            bpy.data.objects.remove(o, do_unlink=True)
        for pb in self.arm.pose.bones:
            pb.rotation_euler = (0, 0, 0)
            pb.location = (0, 0, 0)


def _author(arm, L, name, frames, keys):
    """The FK part of a clip (the library's action(): bones at rest at the ends, the
    keys in the bones' own axes: x pitches, y twists, z rolls; degrees and metres)."""
    return L.action(arm, name, frames, keys)


def _drop(rest_hip):
    """The Root's own offset (along the bone, = up) that puts the hips at rest_hip."""
    return rest_hip - 1.664


# ---- the clips ----------------------------------------------------------------------

def idle(arm, L):
    frames = 120
    rig = Rig(arm, arms_ik=False)
    # Feet planted a touch wider than rest, the right one a little back: a relaxed, uneven stance.
    for side, s, back in (("L", -1, -0.04), ("R", 1, 0.10)):
        ankle, toe = rig.t["ankle" + side], rig.t["toe" + side]
        a = arm.data.bones["Shin" + side].tail_local
        t = arm.data.bones["Foot" + side].tail_local
        _key_loc(ankle, 1, (a.x + s * 0.04, a.y + back, a.z))
        _key_loc(toe, 1, (t.x + s * 0.05, t.y + back, t.z))
    keys = {n: [] for n in ("Root", "Hips", "Spine", "Neck", "Head", "UpperArmL", "UpperArmR", "LowerArmL", "LowerArmR", "HandL", "HandR")}
    for f in range(1, frames + 1, 4):
        p = (f - 1) / (frames - 1)           # 0..1 over the loop
        w = math.sin(2 * math.pi * p)        # the weight shift, once per loop
        b = math.sin(2 * math.pi * 2 * p)    # the breath, twice per loop (2 s)
        wl = math.sin(2 * math.pi * p - 0.9)  # the arms a beat behind the sway
        keys["Root"].append((f, L.R(0, 0, 0), (0.03 * w, _drop(1.60) + 0.012 * b, 0)))
        keys["Hips"].append((f, L.R(0, 0, 2.5 * w), L.Z3))
        keys["Spine"].append((f, L.R(7 + 1.4 * b, 0, -2.0 * w), L.Z3))
        # The head: a slow tilt to one side, back over and down, and one small twitch.
        tilt = 9 * math.sin(2 * math.pi * p) + 3 * math.sin(2 * math.pi * 3 * p + 0.5)
        keys["Neck"].append((f, L.R(4 - 0.8 * b, 3 * math.sin(2 * math.pi * p + 1.2), 0), L.Z3))
        keys["Head"].append((f, L.R(-6 + 2 * math.sin(2 * math.pi * p + 2.0), 6 * math.sin(2 * math.pi * p + 1.4), tilt), L.Z3))
        for side, s in (("L", -1), ("R", 1)):
            keys["UpperArm" + side].append((f, L.R(-3 + 2.5 * wl * s, 0, s * -1.5 * b), L.Z3))
            keys["LowerArm" + side].append((f, L.R(-8 - 1.5 * b, 0, 0), L.Z3))
            keys["Hand" + side].append((f, L.R(4 * math.sin(2 * math.pi * p - 1.6) * s, 0, 0), L.Z3))
    # The twitch: at 2.4 s the head jerks a few degrees and settles.
    for f, extra in ((70, 0), (72, 11), (75, -3), (80, 0)):
        p = (f - 1) / (frames - 1)
        tilt = 9 * math.sin(2 * math.pi * p) + 3 * math.sin(2 * math.pi * 3 * p + 0.5)
        keys["Head"] = [k for k in keys["Head"] if k[0] != f]
        keys["Head"].append((f, L.R(-6 + 2 * math.sin(2 * math.pi * p + 2.0) - extra * 0.3, 6 * math.sin(2 * math.pi * p + 1.4) + extra * 0.4, tilt + extra), L.Z3))
    for n in keys:
        keys[n].sort(key=lambda k: k[0])
        if keys[n][-1][0] != frames:
            first = keys[n][0]
            keys[n].append((frames, first[1], first[2]))
    author = _author(arm, L, "_author_idle", frames, keys)
    rig.bake("Idle", frames, author)
    rig.remove()


def _walk_foot(phase):
    """One foot through a walk cycle, phase 0..1 (0 = the toe lands in front).
    Returns the toe (y, z) and the foot's pitch from the ground (degrees): the
    ankle sits 0.6 m up the foot from the toe at that pitch."""
    span = WALK_SPEED * ((WALK_FRAMES - 1) / FPS) * WALK_DUTY  # how far a planted toe travels (the last frame is the first again)
    front, rear = -0.95, -0.95 + span
    if phase < WALK_DUTY:
        u = phase / WALK_DUTY
        y = front + span * u                   # planted: the ground slides back at the walking speed
        z = 0.0
        pitch = 44 + 24 * _ease((u - 0.45) / 0.55)  # the heel rises from mid-stance
    else:
        u = (phase - WALK_DUTY) / (1 - WALK_DUTY)
        y = rear + (front - rear) * _ease(u)   # the swing forward
        z = 0.24 * math.sin(math.pi * u) ** 1.3
        pitch = 68 + 14 * math.sin(math.pi * min(1.0, u * 1.3)) - 24 * _ease((u - 0.55) / 0.45)
    return y, z, pitch


def walk(arm, L, name):
    frames = WALK_FRAMES
    rig = Rig(arm, arms_ik=False)
    keys = {n: [] for n in ("Root", "Hips", "Spine", "Neck", "Head", "UpperArmL", "UpperArmR", "LowerArmL", "LowerArmR", "HandL", "HandR")}
    for f in range(1, frames + 1):
        phase = (f - 1) / (frames - 1)
        for side, s, off in (("L", -1, 0.0), ("R", 1, 0.5)):
            ph = (phase + off) % 1.0
            y, z, pitch = _walk_foot(ph)
            x = s * 0.15
            toe = (x, y, z + 0.058)
            ankle = (x, y + 0.60 * math.cos(math.radians(pitch)), z + 0.058 + 0.60 * math.sin(math.radians(pitch)) - 0.058)
            _key_loc(rig.t["toe" + side], f, toe)
            _key_loc(rig.t["ankle" + side], f, ankle)
        # The body: highest over a planted foot (mid-stance at 0.28 and 0.78), lowest as the next lands.
        bob = math.cos(2 * math.pi * 2 * (phase - 0.28))
        sway = math.sin(2 * math.pi * (phase - 0.03))          # toward the planted foot
        swing = math.cos(2 * math.pi * phase)                  # +1: the left leg forward
        lag = math.cos(2 * math.pi * (phase - 0.09))           # the arms a beat behind the legs
        hand = math.cos(2 * math.pi * (phase - 0.16))          # the hands a beat behind the arms
        head = math.cos(2 * math.pi * 2 * (phase - 0.36))      # the head settles after the bob
        keys["Root"].append((f, L.R(0, 0, 0), (-0.035 * sway, _drop(WALK_HIP) + 0.04 * bob, 0)))
        keys["Hips"].append((f, L.R(2, -7 * swing, -3 * sway), L.Z3))
        keys["Spine"].append((f, L.R(9 - 1.5 * bob, 9 * lag, 2 * sway), L.Z3))
        keys["Neck"].append((f, L.R(3 + 1.2 * head, -1.5 * lag, -1 * sway), L.Z3))
        keys["Head"].append((f, L.R(-9 + 1.5 * head, -2 * lag, 0), L.Z3))
        for side, s in (("L", -1), ("R", 1)):
            a = -s * lag  # the left arm back while the left leg is forward
            keys["UpperArm" + side].append((f, L.R(16 * a, 0, s * -3), L.Z3))
            keys["LowerArm" + side].append((f, L.R(-10 - 9 * max(0.0, -s * lag) , 0, 0), L.Z3))
            keys["Hand" + side].append((f, L.R(10 * -s * hand, 0, 0), L.Z3))
    author = _author(arm, L, "_author_" + name, frames, keys)
    rig.bake(name, frames, author)
    rig.remove()


def _victim_feet(t):
    """Where the held diver's feet are, t seconds into the grab (Blender axes), for a
    diver caught 1.2 m away: CreatureGrab.HoldPose with the same numbers."""
    caught = Vector((0, -1.2, 0))
    grip = Vector((0, -GRIP_FORWARD, 0))
    lift = Vector((0, -GRIP_FORWARD, LIFT_UP))
    if t < GRIP_SECONDS:
        return caught.lerp(grip, _ease(t / GRIP_SECONDS))
    if t < LIFT_SECONDS:
        u = _lift_ease((t - GRIP_SECONDS) / (LIFT_SECONDS - GRIP_SECONDS))
        return grip + (lift - grip) * u
    if t < HOLD_SECONDS:
        return lift
    return None  # dead: the body drops


def grabbing(arm, L):
    frames = GRAB_FRAMES
    rig = Rig(arm, arms_ik=True, hands_track=True)
    # The feet stay planted where they stand (the creature does not move while it holds).
    for side, s, back in (("L", -1, 0.0), ("R", 1, 0.14)):
        a = arm.data.bones["Shin" + side].tail_local
        t = arm.data.bones["Foot" + side].tail_local
        _key_loc(rig.t["ankle" + side], 1, (a.x + s * 0.05, a.y + back, a.z))
        _key_loc(rig.t["toe" + side], 1, (t.x + s * 0.06, t.y + back, t.z))
    rest_wrist = {side: arm.data.bones["LowerArm" + side].tail_local.copy() for side in ("L", "R")}
    # The wrists: out and back a little (the reach winds up), to the diver's chest at
    # the grip, up with the diver through the lift, a tremble through the hold, out
    # and open at the kill, down to rest.
    for f in range(1, frames + 1):
        t = (f - 1) / FPS
        for side, s in (("L", -1), ("R", 1)):
            wrist = rig.t["wrist" + side]
            aim = rig.t["aim" + side]
            track = rig.t["track" + side]
            rest = rest_wrist[side]
            feet = _victim_feet(max(t, GRIP_SECONDS))
            on = Vector((s * WRIST_OUT, WRIST_BACK, CHEST)) + (feet if feet is not None else Vector((0, -GRIP_FORWARD, LIFT_UP)))
            if t < 0.10:  # the wind-up: the hands draw back and open wide
                u = _ease(t / 0.10)
                pos = rest.lerp(Vector((s * 0.62, 0.10, 1.55)), u)
            elif t < GRIP_SECONDS:  # the strike: out to where the diver will be
                u = _ease((t - 0.10) / (GRIP_SECONDS - 0.10))
                pos = Vector((s * 0.62, 0.10, 1.55)).lerp(on + Vector((s * 0.10, 0, 0.05)), u)
            elif t < HOLD_SECONDS:
                close = _ease((t - GRIP_SECONDS) / 0.08)  # the grip closes in on the chest
                pos = (on + Vector((s * 0.10, 0, 0.05))).lerp(on, close)
                if t > LIFT_SECONDS:  # the tremble of the hold
                    k = (t - LIFT_SECONDS) / (HOLD_SECONDS - LIFT_SECONDS)
                    pos = pos + Vector((0.006 * math.sin(t * 51) * (1 + k), 0.005 * math.sin(t * 43 + 1), 0.008 * math.sin(t * 57 + 2) * (1 + k)))
            elif t < HOLD_SECONDS + 0.25:  # the kill: the hands spring open and out
                u = _ease((t - HOLD_SECONDS) / 0.25)
                pos = on.lerp(on + Vector((s * 0.28, 0.12, -0.10)), u)
            else:  # down to rest, a little overshoot and settle
                u = (t - HOLD_SECONDS - 0.25) / (RELEASE_SECONDS - 0.25)
                start = on + Vector((s * 0.28, 0.12, -0.10))
                pos = start.lerp(rest + Vector((0, -0.04, -0.03)), _ease(min(1.0, u * 1.1))) if u < 1 else rest
            _key_loc(wrist, f, tuple(pos))
            # The claws: from the grip until the kill they point across the diver's chest.
            if feet is not None and t >= GRIP_SECONDS - 0.06:
                _key_loc(aim, f, tuple(Vector((s * 0.12, -0.34, CHEST + 0.05)) + feet))
            else:
                _key_loc(aim, f, tuple(pos + Vector((0, -0.4, -0.3))))
            infl = 0.0
            if GRIP_SECONDS - 0.08 <= t < HOLD_SECONDS:
                infl = _ease((t - (GRIP_SECONDS - 0.08)) / 0.10)
            elif HOLD_SECONDS <= t < HOLD_SECONDS + 0.2:
                infl = 1 - _ease((t - HOLD_SECONDS) / 0.2)
            track.influence = infl
            track.keyframe_insert("influence", frame=f)

    # The body, FK. Frames at 30 fps: 1 the catch, 4 the wind-up's top, 11.5 the grip,
    # 40 the lift done, 61 the kill, 85 settled.
    def F(sec):
        return int(round(_frame(sec)))
    keys = {
        "Root": [(1, L.Z3, (0, 0, 0)), (F(0.10), L.Z3, (0, 0.02, 0)), (F(0.33), L.Z3, (0, -0.13, 0)), (F(0.42), L.Z3, (0, -0.15, 0)),
                 (F(0.9), L.Z3, (0, -0.02, 0)), (F(1.2), L.Z3, (0, 0.01, 0)), (F(1.35), L.Z3, (0, 0.0, 0)), (F(2.0), L.Z3, (0, -0.02, 0)),
                 (F(2.3), L.Z3, (0, -0.07, 0)), (F(2.7), L.Z3, (0, -0.05, 0)), (frames, L.Z3, (0, _drop(1.60), 0))],
        "Hips": [(1, L.R(0, 0, 0), L.Z3), (F(0.10), L.R(-3, 0, 0), L.Z3), (F(0.33), L.R(12, 0, 0), L.Z3), (F(0.42), L.R(13, 0, 0), L.Z3),
                 (F(1.0), L.R(2, 0, 0), L.Z3), (F(1.3), L.R(-2, 0, 0), L.Z3), (F(2.0), L.R(0, 0, 0), L.Z3), (F(2.7), L.R(0, 0, 0), L.Z3), (frames, L.R(0, 0, 0), L.Z3)],
        "Spine": [(1, L.R(9, 0, 0), L.Z3), (F(0.10), L.R(-4, 0, 0), L.Z3), (F(0.30), L.R(26, 0, 0), L.Z3), (F(0.36), L.R(29, 0, 0), L.Z3), (F(0.45), L.R(27, 0, 0), L.Z3),
                  (F(0.95), L.R(4, 0, 0), L.Z3), (F(1.2), L.R(-5, 0, 0), L.Z3), (F(1.35), L.R(3, 0, 0), L.Z3), (F(1.9), L.R(5, 0, 1.5), L.Z3), (F(2.0), L.R(7, 0, 0), L.Z3),
                  (F(2.25), L.R(2, 0, 0), L.Z3), (F(2.65), L.R(8, 0, 0), L.Z3), (frames, L.R(7, 0, 0), L.Z3)],
        "Neck": [(1, L.R(3, 0, 0), L.Z3), (F(0.10), L.R(-3, 0, 0), L.Z3), (F(0.33), L.R(10, 0, 0), L.Z3), (F(0.9), L.R(14, 0, 0), L.Z3),
                 (F(1.3), L.R(24, 0, 0), L.Z3), (F(1.75), L.R(30, 4, 0), L.Z3), (F(1.93), L.R(36, 0, 0), L.Z3), (F(2.05), L.R(30, 0, 0), L.Z3),
                 (F(2.4), L.R(8, 0, 0), L.Z3), (F(2.8), L.R(4, 0, 0), L.Z3), (frames, L.R(4, 0, 0), L.Z3)],
        "Head": [(1, L.R(-9, 0, 0), L.Z3), (F(0.10), L.R(-12, 0, 0), L.Z3), (F(0.33), L.R(-16, 0, 0), L.Z3), (F(0.9), L.R(-8, 0, 0), L.Z3),
                 (F(1.3), L.R(-10, 0, 0), L.Z3), (F(1.45), L.R(-10, 0, 4), L.Z3), (F(1.8), L.R(-10, 6, 17), L.Z3), (F(1.93), L.R(-8, 2, 12), L.Z3),
                 (F(2.05), L.R(-6, 0, 10), L.Z3), (F(2.4), L.R(-8, 0, 2), L.Z3), (F(2.8), L.R(-6, 0, 0), L.Z3), (frames, L.R(-6, 0, 0), L.Z3)],
        "UpperArmL": [(1, L.Z3, L.Z3), (frames, L.Z3, L.Z3)],
    }
    author = _author(arm, L, "_author_grab", frames, keys)
    rig.bake("Grabbing", frames, author)
    rig.remove()


def anchors(arm, L):
    """Named points the game can find (presentation and the checks, never the rules):
    Face, where its face is (the held diver's eyes are turned toward CreatureGrab's
    faceTarget, keyed to match), and GripL/GripR, the palms that close on a diver."""
    for pb in arm.pose.bones:  # the anchors are placed on the rest pose, not what the last clip left
        pb.rotation_mode = "XYZ"
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    head = arm.data.bones["Head"]
    face = head.head_local.lerp(head.tail_local, 0.6) + Vector((0, -0.09, 0))
    L.anchor("Face", tuple(face), arm, "Head")
    for side in ("L", "R"):
        hand = arm.data.bones["Hand" + side]
        L.anchor("Grip" + side, tuple(hand.head_local.lerp(hand.tail_local, 0.3)), arm, "Hand" + side)


def animate(arm, height, L):
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.render.fps_base = 1.0
    anchors(arm, L)
    idle(arm, L)
    walk(arm, L, "Hunting")
    walk(arm, L, "Drawn")
    grabbing(arm, L)
    for a in ("Idle", "Hunting", "Drawn", "Grabbing"):
        act = bpy.data.actions.get(a)
        print("walker clip", a, act.frame_range[:] if act else None)
