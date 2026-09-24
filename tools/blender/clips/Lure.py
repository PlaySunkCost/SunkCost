"""The Lure's own skin and clips (mon-lure, 24 September 2026).

rig_generated_monster.py runs animate(arm, height, L) after the shared library,
and keeps the names in CLIPS besides the kind's own.

The Lure is a lantern-headed moth wraith (docs/reference/monsters/lure-*.png):
pale, long-limbed, digitigrade, two drooping wings down its back, a caged lantern
for a head and two antennae over it. It drifts toward light and burns a beam of it
from the lantern. Its body language is its own: light, gliding, fluttering; the
wings are how it shows intent. At a lamp it plants itself, throws its wings and
arms open and the lantern forward (Aiming), recoils as the beam leaves (Shooting),
then folds and sags (Recovering).

What this file does to the model, before the clips:
- Two wing bones per side (WingL/WingTipL, WingR/WingTipR) off the spine, and the
  back sheets weighted to them. The bone heat had weighted the wings to the thighs
  and the upper arms, so every step dragged them.
- The lantern weighted to the Head bone (it was mostly on the Neck, so a head turn
  sheared it) and the hood back to the head (the shared rule had pinned the hood
  flaps to the antenna bones); the antenna bones keep the stalks alone.
- BeamOrigin on the lantern's glass at the front, and an Eye node at the lantern's
  height, so the Lure looks from where it shoots.

The clips are built pose by pose, every frame keyed: the body in forward
kinematics, the legs solved with a two-bone IK to feet that stay planted, so a
walk moves its planted toe back at exactly the clip's authored speed (the
speed CreatureRig matches the ground speed against). Walks are in place.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ik_kit as K  # noqa: E402
from ik_kit import TWO_PI, cwave, ease, lerp, smooth, wave  # noqa: E402

CLIPS = ("Aiming", "Recovering")
FPS = K.FPS

# The ground speed a walk is authored at: the Lure drifts at half a diver's walking
# speed (MonsterSettings.LureApproachSpeedFactor 0.5 of 4 m/s).
WALK_SPEED = 2.0

# ---- the skin ------------------------------------------------------------------------

def add_wing_bones(arm, L):
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    for s, tag in ((-1, "L"), (1, "R")):
        if ("Wing" + tag) in eb:
            continue
        root = Vector((s * 0.06, 0.12, 1.68))
        mid = Vector((s * 0.22, 0.32, 1.06))
        tip = Vector((s * 0.36, 0.22, 0.50))
        w = eb.new("Wing" + tag)
        w.head, w.tail = root, mid
        w.parent = eb["Spine"]
        t = eb.new("WingTip" + tag)
        t.head, t.tail = mid, tip
        t.parent = w
        t.use_connect = True
        L.BONES["Wing" + tag] = (root.copy(), mid.copy())
        L.BONES["WingTip" + tag] = (mid.copy(), tip.copy())
    bpy.ops.object.mode_set(mode="OBJECT")
    arm.select_set(False)


ARM_BONES = ("UpperArmL", "LowerArmL", "HandL", "UpperArmR", "LowerArmR", "HandR")
LEG_BONES = ("ThighL", "ShinL", "FootL", "ThighR", "ShinR", "FootR")


def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (a + ab * t)).length


def reweight(mesh, arm):
    limbs = {b.name: (b.head_local.copy(), b.tail_local.copy()) for b in arm.data.bones}
    groups = {g.name: g for g in mesh.vertex_groups}

    def group(name):
        if name not in groups:
            groups[name] = mesh.vertex_groups.new(name=name)
        return groups[name]

    for n in ("WingL", "WingTipL", "WingR", "WingTipR", "Head", "EarL", "EarR", "Neck", "Spine"):
        group(n)
    names = {g.index: g.name for g in mesh.vertex_groups}
    wings = heads = stalks = 0
    for v in mesh.data.vertices:
        x, y, z = v.co
        ax = abs(x)
        weights = {names[g.group]: g.weight for g in v.groups}
        total = sum(weights.values())
        if total <= 0:
            continue
        weights = {k: w / total for k, w in weights.items()}

        # The antennae: the thin stalks over the hood keep their bones; anything else
        # the shared rule pinned to them goes back to the head.
        stalk = smooth(1.96, 2.04, z) * smooth(0.085, 0.12, ax)
        ear = weights.pop("EarL", 0.0) + weights.pop("EarR", 0.0)
        if ear > 0 and stalk <= 0:
            weights["Head"] = weights.get("Head", 0.0) + ear

        # The lantern and the hood: the head bone, faded in above the throat.
        # (Below the hood only the lantern, which hangs forward: the upper back stays on the spine.)
        head_zone = z > 1.64 and y < 0.20 and (y < 0.06 or z > 1.86) and ax < 0.16 + max(0.0, z - 1.78)
        if head_zone:
            h = smooth(1.64, 1.80, z)
            weights = {k: w * (1 - h) for k, w in weights.items()}
            weights["Head"] = weights.get("Head", 0.0) + h
            heads += 1
        if stalk > 0:
            weights = {k: w * (1 - stalk) for k, w in weights.items()}
            weights["EarL" if x < 0 else "EarR"] = stalk

        # The wings: the sheets behind the back, faded in below the shoulders. The
        # limbs and the torso are told apart by their distance from their bones (they
        # are thin; the sheets hang clear of them), not by a plane.
        if 0.3 < z < 1.80 and not head_zone:
            p = Vector((x, y, z))
            arm_d = min(seg_dist(p, *limbs[n]) for n in ARM_BONES)
            leg_d = min(seg_dist(p, *limbs[n]) for n in LEG_BONES)
            if z < 0.75:
                leg_d -= 0.03  # round the shins the sheets stay with the leg rather than stretch to it
            torso_d = min(seg_dist(p, *limbs[n]) for n in ("Hips", "Spine"))
            behind = smooth(-0.02, 0.05, y)
            w = behind * smooth(0.035, 0.06, arm_d) * smooth(0.045, 0.08, leg_d) * smooth(0.10, 0.16, torso_d)
            w *= 1 - smooth(1.50, 1.78, z)
            if w > 0.001:
                left = 1 - smooth(-0.05, 0.05, x)
                tip = 1 - smooth(0.90, 1.25, z)
                weights = {k: wt * (1 - w) for k, wt in weights.items()}
                for side, share in (("L", left), ("R", 1 - left)):
                    if share <= 0:
                        continue
                    weights["Wing" + side] = weights.get("Wing" + side, 0.0) + w * share * (1 - tip)
                    weights["WingTip" + side] = weights.get("WingTip" + side, 0.0) + w * share * tip
                wings += 1

        for g in list(v.groups):
            mesh.vertex_groups[g.group].remove([v.index])
        for name, w in weights.items():
            if w > 0.0005:
                group(name).add([v.index], w, "REPLACE")
    print("Lure skin: %d wing, %d lantern, stalks kept on the antennae" % (wings, heads))


def place_anchors(arm, L):
    # BeamOrigin on the lantern's glass at the front (the drawn beam and the damage
    # both leave here); Eye at the lantern's height (where it looks from).
    origin = bpy.data.objects.get("BeamOrigin")
    if origin is not None:
        bpy.data.objects.remove(origin, do_unlink=True)
    L.anchor("BeamOrigin", (0.0, -0.40, 1.87), arm, "Head")
    if bpy.data.objects.get("Eye") is None:
        L.anchor("Eye", (0.0, -0.20, 1.86), arm, "Head")


# ---- the body language -------------------------------------------------------------------

class Pose(K.Pose):
    """The kit's pose with the Lure's wings."""

    def wings(self, open_deg, back_deg, tip_open=0.0, tip_back=0.0, left=0.0, right=0.0):
        self.rot("WingL", back_deg, open_deg + left, 0)
        self.rot("WingR", back_deg, -(open_deg + right), 0)
        self.rot("WingTipL", tip_back, tip_open, 0)
        self.rot("WingTipR", tip_back, -tip_open, 0)


def stance(p, rig, crouch=0.0, spread=0.0):
    p.stand(rig, spread=spread)
    p.move("Root", z=-crouch)


def idle_pose(rig, i, phase):
    """4 s: it sways on the spot as if the current held it, breathes twice, fans its
    wings open and closed once, tilts its lantern one way then the other like a moth
    at a window, and flicks each antenna once. The feet stay planted."""
    p = Pose()
    stance(p, rig, crouch=0.035, spread=0.03)
    p.move("Root", x=0.028 * wave(phase), y=0.012 * wave(2 * phase + 0.25), z=0.012 * cwave(2 * phase))
    p.rot("Hips", 0.0, -1.2 * wave(phase), 1.5 * wave(phase + 0.1))
    breath = wave(2 * phase)
    p.rot("Spine", 3.0 + 1.6 * breath, 1.0 * wave(phase + 0.15), -1.0 * wave(phase + 0.1))
    # The lantern: curious tilts, eased in and held.
    look_l = ease((phase - 0.05) / 0.2) - ease((phase - 0.42) / 0.12)
    look_r = ease((phase - 0.55) / 0.2) - ease((phase - 0.9) / 0.1)
    p.rot("Neck", -1.5 - 0.8 * breath, 0, 6 * look_l - 6 * look_r)
    p.rot("Head", -1.0 + 2.0 * wave(phase + 0.3), -9 * look_l + 8 * look_r, 8 * look_l - 7 * look_r)
    # The arms hang forward of the body, claws loose, drifting with the sway (a beat late).
    lag = wave(phase - 0.08)
    p.mirror("UpperArm", -8 - 1.5 * breath, 4, 0)
    p.rot("UpperArmL", -1.5 * lag)
    p.rot("UpperArmR", 1.5 * lag)
    p.mirror("LowerArm", -18 - 2 * breath, 0, 4)
    p.mirror("Hand", -10 + 3 * wave(2 * phase - 0.2), 0, 6)
    # The wings: one slow fan over the loop, the tips following a little later, a shiver midway.
    fan = 0.5 - 0.5 * cwave(phase)
    shiver = 3.5 * math.sin(TWO_PI * 14 * phase) * (smooth(0.50, 0.56, phase) - smooth(0.64, 0.72, phase))
    tip = 0.5 - 0.5 * cwave(phase - 0.07)
    p.wings(4 + 16 * fan + shiver, 6 + 8 * fan, tip_open=2 + 9 * tip + shiver * 0.8, tip_back=3 * tip)
    # The antennae sway with the head and each flicks once.
    flick_l = math.exp(-((phase - 0.25) / 0.025) ** 2)
    flick_r = math.exp(-((phase - 0.75) / 0.025) ** 2)
    p.rot("EarL", -4 + 4 * wave(2 * phase - 0.3) - 20 * flick_l, 0, 5 * wave(phase))
    p.rot("EarR", -4 + 4 * wave(2 * phase - 0.3) - 20 * flick_r, 0, 5 * wave(phase))
    return p.fk, p.feet


def walk_pose(rig, phase, cycle_s, duty, drop, bob, sway, lift, lean, reach, wing_open, wing_back, flutter, antenna, urgency):
    """A gliding digitigrade walk: the planted toe moves back at WALK_SPEED, the heel
    peels up as it passes under the hips, the swing foot hangs its claws and reaches
    out ahead. The body floats: a soft bob, the hips swaying over the planted foot,
    the lantern held steady and forward, the wings fluttering."""
    p = Pose()
    p.feet = K.walk_feet(rig, phase, WALK_SPEED, cycle_s, duty, lift)
    mid = duty / 2.0
    up = cwave(2 * (phase - mid))                       # +1 at each mid-stance
    p.move("Root", x=-sway * cwave(phase - mid), y=0.0, z=-drop + bob * up)
    p.rot("Hips", 1.5 * urgency, 2.5 * cwave(phase - mid), 7 * cwave(phase))
    p.rot("Spine", lean + 1.5 * cwave(2 * (phase - mid) - 0.1), -2.0 * cwave(phase - mid - 0.05), -4.5 * cwave(phase))
    # The lantern stays level and forward: the neck and head take back the lean and the bob.
    p.rot("Neck", -lean * 0.45 - 1.2 * cwave(2 * (phase - mid) - 0.15), 0, -1.5 * cwave(phase))
    p.rot("Head", -lean * 0.35 - 1.5 * cwave(2 * (phase - mid) - 0.22), 1.5 * cwave(phase - mid - 0.1), -1.0 * cwave(phase))
    # Arms reach ahead toward the light, swinging a little against the legs, the hands trailing.
    swing = 7.0 * (1 - 0.5 * urgency)
    p.rot("UpperArmL", -reach + swing * cwave(phase - 0.06), 5, 0)
    p.rot("UpperArmR", -reach - swing * cwave(phase - 0.06), -5, 0)
    p.mirror("LowerArm", -24 - 18 * urgency, 0, 6)
    p.rot("LowerArmL", -4 * cwave(phase - 0.14))
    p.rot("LowerArmR", 4 * cwave(phase - 0.14))
    p.mirror("Hand", -12 - 6 * urgency + 4 * cwave(2 * phase - 0.3), 0, 8)
    # The wings: a flutter (four beats a cycle) over a slow lift with the bob, the tips a beat late.
    beat = wave(4 * phase)
    lateb = wave(4 * phase - 0.18)
    p.wings(wing_open + 3 * up + flutter * beat, wing_back + 2 * up, tip_open=4 + flutter * 0.9 * lateb, tip_back=2 + 2 * lateb)
    p.rot("EarL", antenna + 5 * cwave(2 * (phase - mid) - 0.3), 0, 3 * wave(phase))
    p.rot("EarR", antenna + 5 * cwave(2 * (phase - mid) - 0.3), 0, 3 * wave(phase))
    return p.fk, p.feet


def aim_base(rig, p, amount=1.0):
    """The charge: planted wide and braced, the chest drawn back, the wings thrown open
    and back like a display, the arms spread wide, the antennae laid flat, the lantern
    pushed forward on a stiff neck (CreatureRig turns it down the beam)."""
    stance(p, rig, crouch=0.10 * amount, spread=0.06 * amount)
    p.move("Root", y=0.05 * amount)
    p.rot("Hips", -4 * amount)
    p.rot("Spine", -6 * amount)
    p.rot("Neck", 12 * amount)
    p.rot("Head", -4 * amount)
    p.mirror("UpperArm", 22 * amount, 46 * amount, -10 * amount)
    p.mirror("LowerArm", -38 * amount, 0, -12 * amount)
    p.mirror("Hand", 18 * amount, 0, -22 * amount)
    p.wings(58 * amount, 26 * amount, tip_open=16 * amount, tip_back=12 * amount)
    p.rot("EarL", -32 * amount, 0, -6 * amount)
    p.rot("EarR", -32 * amount, 0, 6 * amount)


def aiming_pose(rig, i, phase):
    """0.5 s loop, held for the whole charge: the aim above, trembling with the
    gathering light (wings at 8 Hz, the body and antennae finer)."""
    p = Pose()
    aim_base(rig, p)
    tremble = wave(4 * phase)
    fine = wave(6 * phase + 0.2)
    p.wings(3.5 * tremble, 1.5 * fine, tip_open=3.0 * wave(4 * phase - 0.12), tip_back=0)
    p.rot("Spine", 0.6 * fine, 0.5 * wave(3 * phase))
    p.rot("Head", 0.4 * tremble, 0, 0.4 * fine)
    p.rot("EarL", 3 * wave(6 * phase))
    p.rot("EarR", 3 * wave(6 * phase + 0.5))
    p.mirror("Hand", 2.0 * fine, 0, 0)
    p.move("Root", z=0.004 * fine)
    return p.fk, p.feet


def shooting_pose(rig, i, phase):
    """0.6 s, once and held while the beam burns: the light leaves and throws it back —
    the lantern snaps forward, the chest and hips recoil, the wings snap wider past
    their mark and settle, the arms fling back; then a braced firing stance."""
    p = Pose()
    aim_base(rig, p)
    t = phase * 0.6
    kick = math.exp(-((t - 0.07) / 0.05) ** 2)            # the recoil's peak, 70 ms in
    settle = smooth(0.0, 0.45, t)
    over = math.sin(min(t / 0.30, 1.0) * math.pi) * (1 - settle)  # overshoot then back
    p.move("Root", y=0.05 * kick + 0.03 * settle, z=-0.02 * kick - 0.015 * settle)
    p.rot("Spine", -9 * kick - 4 * settle)
    p.rot("Neck", 8 * kick + 3 * settle)
    p.rot("Head", 5 * kick - 2 * settle)
    p.wings(14 * over + 6 * settle, 8 * over + 4 * settle, tip_open=14 * over + 6 * settle, tip_back=6 * over)
    p.mirror("UpperArm", 14 * kick + 6 * settle, 8 * kick + 4 * settle, 0)
    p.mirror("LowerArm", 10 * kick, 0, 0)
    p.rot("EarL", -14 * kick - 6 * settle)
    p.rot("EarR", -14 * kick - 6 * settle)
    return p.fk, p.feet


def recovering_pose(rig, i, phase):
    """0.9 s, once: the light spent, the wings fold down past rest and settle, the
    body sags forward and the lantern dips, a shake runs through the wings, then it
    straightens into its idle stance (the last frame is the idle's first)."""
    t = phase
    fk_idle, feet_idle = idle_pose(rig, 0, 0.0)
    # The firing pose's settled extra (the end of Shooting).
    shot_fk, _ = shooting_pose(rig, 17, 1.0)
    out = smooth(0.0, 0.35, t)          # away from the firing stance
    home = smooth(0.45, 1.0, t)         # into the idle
    sag = math.sin(math.pi * min(1.0, t / 0.75)) * (1 - home * 0.6)
    shake = math.sin(TWO_PI * 11 * t) * (smooth(0.35, 0.45, t) - smooth(0.6, 0.72, t))

    def blend(a, b, k):
        names = set(a) | set(b)
        res = {}
        for n in names:
            ra, ta = a.get(n, ((0, 0, 0), (0, 0, 0)))
            rb, tb = b.get(n, ((0, 0, 0), (0, 0, 0)))
            res[n] = (tuple(lerp(x, y, k) for x, y in zip(ra, rb)), tuple(lerp(x, y, k) for x, y in zip(ta, tb)))
        return res

    fk = blend(shot_fk, fk_idle, out * 0.55 + home * 0.45)
    p = Pose()
    p.fk = fk
    p.rot("Spine", 16 * sag)
    p.rot("Neck", 6 * sag)
    p.rot("Head", 10 * sag)
    p.move("Root", z=-0.04 * sag)
    p.mirror("UpperArm", -6 * sag, -4 * sag, 0)
    p.wings(-10 * sag + 4 * shake, -4 * sag, tip_open=-6 * sag + 5 * shake, tip_back=0)
    p.rot("EarL", 18 * sag)
    p.rot("EarR", 18 * sag)
    # The feet: from the braced stance to the idle one, stepping nothing (both planted).
    for s in "LR":
        fire_toe, _ = aim_feet(rig)[s]
        idle_toe, _ = feet_idle[s]
        k = smooth(0.3, 0.9, t)
        p.feet[s] = (fire_toe.lerp(idle_toe, k), 0.0)
    return p.fk, p.feet


def aim_feet(rig):
    p = Pose()
    aim_base(rig, p)
    return p.feet


def animate(arm, height, L):
    mesh = next(o for o in bpy.data.objects if o.type == "MESH")
    # The shared clips leave the last keyed pose on the bones: back to rest first, or an
    # anchor placed now is offset by that pose (BeamOrigin 0.3 m low, the toes in the floor).
    K.rest_pose(arm)
    add_wing_bones(arm, L)
    reweight(mesh, arm)
    place_anchors(arm, L)
    K.add_toe_anchors(arm, L)
    rig = K.Rig(arm)
    for name in ("Idle", "Drawn", "Hunting", "Shooting", "Aiming", "Recovering"):
        old = bpy.data.actions.get(name)
        if old is not None:
            bpy.data.actions.remove(old)

    K.key_action(rig, "Idle", 121, idle_pose)

    # The cycle is whole frames, and the stride is worked out from that true length.
    drawn_frames = 22
    drawn_cycle = drawn_frames / FPS
    K.key_action(rig, "Drawn", drawn_frames + 1, lambda r, i, ph: walk_pose(
        r, ph, drawn_cycle, duty=0.50, drop=0.08, bob=0.02, sway=0.03, lift=0.09, lean=7.0, reach=16.0,
        wing_open=10.0, wing_back=8.0, flutter=4.0, antenna=-2.0, urgency=0.0))

    hunting_frames = 19
    hunting_cycle = hunting_frames / FPS
    K.key_action(rig, "Hunting", hunting_frames + 1, lambda r, i, ph: walk_pose(
        r, ph, hunting_cycle, duty=0.52, drop=0.12, bob=0.016, sway=0.022, lift=0.08, lean=15.0, reach=34.0,
        wing_open=18.0, wing_back=14.0, flutter=6.0, antenna=14.0, urgency=1.0))

    K.key_action(rig, "Aiming", 16, aiming_pose)
    K.key_action(rig, "Shooting", 19, shooting_pose)
    K.key_action(rig, "Recovering", 28, recovering_pose)
    for pb in arm.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)
