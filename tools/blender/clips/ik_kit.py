"""A small kit for keying a monster's clips pose by pose (mon-lure, 24 September 2026),
for tools/blender/clips/<Kind>.py files. rig_generated_monster.py's armature is one
layout (Root, Hips, Spine, Neck, Head, arms, Thigh/Shin/Foot legs), so the kit works
for every kind built by it.

- Rig(arm): an armature-space pose from world-axis deltas per bone (the body in
  forward kinematics) and toe targets per leg (a two-bone IK on Thigh and Shin, the
  foot pitched about its toe, the knee toward the model's forward), turned into each
  bone's basis.
- key_action(rig, name, frames, pose_at): keys every frame as Euler XYZ, continuous
  frame to frame (the same rotation mode and slot handling as make_listener.action, so
  its clips and the shared ones can be mixed). pose_at(rig, i, phase) returns (fk, feet).
- Pose: fills fk and feet: rot/move/mirror per bone, stand() plants both feet.
- rest_pose(arm): every bone back to rest; call it before placing any anchor (the
  shared clips leave their last pose on the bones, and an anchor placed on a posed
  bone keeps that offset).
- add_toe_anchors(arm, L): ToeL/ToeR empties on the foot tips, which
  CreatureRigStrides measures (better than the ankle, which lifts as the heel peels).
- walk_feet(...): a walk's foot targets whose planted toe travels back at exactly the
  given ground speed, so CreatureRig's speed matching can hold the feet still.

In a clip file:
    import os, sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import ik_kit as K

Axes: the model faces -Y, its left is -X. +X on an upright bone leans it forward
and on a hanging one swings it back; +Y tilts the top toward +X; +Z turns it left.
Only Thigh/Shin/Foot are solved: a four-legged kind's front legs (the arm bones)
stay in forward kinematics.
"""
import math

import bpy
from mathutils import Euler, Matrix, Vector

FPS = 30
TWO_PI = 2.0 * math.pi


def smooth(a, b, x):
    if b == a:
        return 1.0 if x >= b else 0.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def ease(t):
    return 0.5 - 0.5 * math.cos(math.pi * max(0.0, min(1.0, t)))


def lerp(a, b, t):
    return a + (b - a) * t


def wave(phase):
    return math.sin(TWO_PI * phase)


def cwave(phase):
    return math.cos(TWO_PI * phase)


# ---- the pose solver -------------------------------------------------------------------

class Rig:
    """Armature-space pose from world-axis deltas (the body) and foot targets (the
    legs, two-bone IK), turned into each bone's basis for keying."""

    def __init__(self, arm):
        self.arm = arm
        self.bones = arm.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in self.bones}
        self.parent = {b.name: (b.parent.name if b.parent else None) for b in self.bones}
        order, seen = [], set()

        def visit(b):
            if b.name in seen:
                return
            if b.parent is not None:
                visit(b.parent)
            seen.add(b.name)
            order.append(b.name)

        for b in self.bones:
            visit(b)
        self.order = order
        self.length = {b.name: b.length for b in self.bones}
        self.toe = {s: self.bones["Foot" + s].tail_local.copy() for s in "LR"}
        self.ankle = {s: self.bones["Foot" + s].head_local.copy() for s in "LR"}
        self.worst = 0.0
        self._knee, self._ankle = {}, {}

    def has(self, name):
        return name in self.rest

    def solve(self, fk, feet):
        """fk: {bone: (euler degrees about the armature's axes, translation)};
        feet: {"L"/"R": (toe position, pitch degrees: + lifts the heel about the toe)}.
        Returns {bone: basis matrix}."""
        M, basis = {}, {}
        for name in self.order:
            parent = self.parent[name]
            P = M[parent] @ self.rest[parent].inverted() if parent else Matrix.Identity(4)
            base = P @ self.rest[name]
            leg = name[:-1] if name[-1] in "LR" else name
            side = name[-1]
            if leg in ("Thigh", "Shin", "Foot") and side in feet:
                M[name] = self._leg(name, leg, side, P, base, M, feet[side])
            else:
                rot, move = fk.get(name, ((0, 0, 0), (0, 0, 0)))
                rest_q = self.rest[name].to_quaternion()
                q = Euler(tuple(math.radians(a) for a in rot), "XYZ").to_quaternion()
                local_q = rest_q.inverted() @ q @ rest_q
                local_t = rest_q.inverted() @ Vector(move)
                B = Matrix.Translation(local_t) @ local_q.to_matrix().to_4x4()
                M[name] = base @ B
            basis[name] = base.inverted() @ M[name]
        return basis, M

    def _leg(self, name, leg, side, P, base, M, foot):
        toe, pitch = foot
        toe = Vector(toe)
        rest_vec = self.ankle[side] - self.toe[side]
        pr = Matrix.Rotation(math.radians(pitch), 3, "X")
        ankle = toe + pr @ rest_vec
        if leg == "Thigh":
            hip = base.translation.copy()
            a, b = self.length["Thigh" + side], self.length["Shin" + side]
            to = ankle - hip
            d = to.length
            self.worst = max(self.worst, d / (a + b))
            d = max(abs(a - b) + 1e-3, min(d, (a + b) * 0.999))
            u = to.normalized()
            # The knee bends forward: the model's forward (-Y) as the hips carry it
            # (the turn from the hips' rest, not the bone's own axes).
            hips_q = M["Hips"].to_quaternion() @ self.rest["Hips"].to_quaternion().inverted()
            pole = hips_q @ Vector((0, -1, 0))
            w = (pole - u * pole.dot(u))
            w = w.normalized() if w.length > 1e-6 else Vector((0, -1, 0))
            cos_a = max(-1.0, min(1.0, (a * a + d * d - b * b) / (2 * a * d)))
            knee = hip + a * (cos_a * u + math.sqrt(1 - cos_a * cos_a) * w)
            self._knee[side] = knee
            self._ankle[side] = hip + u * d
            return self._aim(base, knee - hip)
        if leg == "Shin":
            return self._aim(base, self._ankle[side] - base.translation)
        # The foot: its rest orientation turned by the pitch, at the solved ankle.
        q = pr.to_quaternion() @ self.rest[name].to_quaternion()
        return Matrix.Translation(self._ankle[side]) @ q.to_matrix().to_4x4()

    @staticmethod
    def _aim(base, direction):
        q0 = base.to_quaternion()
        cur = q0 @ Vector((0, 1, 0))
        q = cur.rotation_difference(direction.normalized()) @ q0
        return Matrix.Translation(base.translation) @ q.to_matrix().to_4x4()


def key_action(rig, name, frames, pose_at):
    """pose_at(rig, frame index 0..frames-1, phase 0..1) -> (fk, feet). Keys every frame
    1..frames (the last equals the first for a loop when pose_at is periodic)."""
    arm = rig.arm
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
    # Euler XYZ keys, like make_listener.action, so this file's clips and the shared
    # ones live side by side (a bone's rotation mode is the bone's, not the clip's).
    for pb in arm.pose.bones:
        pb.rotation_mode = "XYZ"
    previous = {}
    rig.worst = 0.0
    for i in range(frames):
        phase = i / (frames - 1) if frames > 1 else 0.0
        fk, feet = pose_at(rig, i, phase)
        basis, _ = rig.solve(fk, feet)
        for pb in arm.pose.bones:
            B = basis.get(pb.name)
            if B is None:
                continue
            loc, rot, _scale = B.decompose()
            prev = previous.get(pb.name)
            euler = rot.to_euler("XYZ", prev) if prev is not None else rot.to_euler("XYZ")
            previous[pb.name] = euler
            pb.location = loc
            pb.rotation_euler = euler
            pb.keyframe_insert("location", frame=i + 1)
            pb.keyframe_insert("rotation_euler", frame=i + 1)
    act.frame_range = (1, frames)
    try:
        act.use_frame_range = True
    except AttributeError:
        pass
    ad.action = None
    print("clip %-10s %3d frames (%.2f s), leg reach %.0f%%" % (name, frames, (frames - 1) / FPS, rig.worst * 100))
    return act


class Pose:
    """One frame's intent, filled bone by bone, in the armature's axes (the model
    faces -Y, its left is -X): +X on an upright bone leans it forward, on a hanging
    one swings it back; +Y tilts the top toward the right (+X); +Z turns it left."""

    def __init__(self):
        self.fk = {}
        self.feet = {}

    def rot(self, bone, x=0.0, y=0.0, z=0.0):
        r, t = self.fk.get(bone, ((0, 0, 0), (0, 0, 0)))
        self.fk[bone] = ((r[0] + x, r[1] + y, r[2] + z), t)

    def move(self, bone, x=0.0, y=0.0, z=0.0):
        r, t = self.fk.get(bone, ((0, 0, 0), (0, 0, 0)))
        self.fk[bone] = (r, (t[0] + x, t[1] + y, t[2] + z))

    def mirror(self, bone, x=0.0, out=0.0, z=0.0):
        """A pair: `out` opens both sides away from the body, `z` turns both inward."""
        self.rot(bone + "L", x, out, -z)
        self.rot(bone + "R", x, -out, z)

    def stand(self, rig, spread=0.0, forward=0.0, pitch=0.0):
        for s, sign in (("L", -1), ("R", 1)):
            t = rig.toe[s].copy()
            t.x += sign * spread
            t.y += forward
            self.feet[s] = (t, pitch)




def rest_pose(arm):
    """Every bone back to rest (the shared clips leave their last keyed pose on the
    bones). Call before placing anchors: an empty parented to a posed bone keeps that
    pose's offset."""
    ad = arm.animation_data
    if ad is not None:
        ad.action = None
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()


def add_toe_anchors(arm, L):
    """Empties ToeL/ToeR at each foot's tip, on the foot bone: the point that is planted,
    which CreatureRigStrides (and a Play Mode slide check) measure. Call before keying,
    with the armature at rest (rest_pose)."""
    rest_pose(arm)
    for s in "LR":
        name = "Toe" + s
        if bpy.data.objects.get(name) is None and ("Foot" + s) in arm.data.bones:
            L.anchor(name, tuple(arm.data.bones["Foot" + s].tail_local), arm, "Foot" + s)


def walk_feet(rig, phase, speed, cycle_s, duty, lift, centre=-0.06, width=1.0,
              pitch_front=-4.0, pitch_back=26.0, toe_hang=6.0, sides=(("L", 0.0), ("R", 0.5))):
    """The foot targets of a walk at `phase`: each foot planted for `duty` of the cycle,
    its toe travelling back at `speed` (m/s) while planted, the heel peeling from
    pitch_front to pitch_back degrees; then swung forward with an eased stride, lifted
    `lift` metres, the toes hanging `toe_hang` degrees. Returns {side: (toe, pitch)}."""
    feet = {}
    stride = speed * cycle_s
    travel = stride * duty
    for s, offset in sides:
        f = (phase + offset) % 1.0
        toe = rig.toe[s].copy()
        toe.x *= width
        if f < duty:
            k = f / duty
            toe.y += centre - travel / 2 + travel * k
            pitch = lerp(pitch_front, pitch_back, k ** 1.6)
        else:
            k = (f - duty) / (1 - duty)
            toe.y += centre + travel / 2 - travel * ease(k)
            toe.z += lift * math.sin(math.pi * k) ** 0.85
            pitch = lerp(pitch_back, pitch_front, smooth(0.0, 1.0, k)) + toe_hang * math.sin(math.pi * k)
        feet[s] = (toe, pitch)
    return feet
