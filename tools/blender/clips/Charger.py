"""The Charger's own clips (mon-charger, 24 September 2026; docs/DESIGN.md §6).

rig_generated_monster.py runs animate(arm, height, L) after the shared library:
every Charger clip is keyed here, so the shared upright clips never reach it.

A heavy, low, four-legged stone beast. Its body language: it prowls with its
head low and its weight forward, it paws the ground and trembles before it
charges, it gallops flat out with its head down like a ram, and it skids,
tosses its head and heaves for breath afterwards.

The legs are solved, not swung: every frame the body is posed first, then each
leg's ankle is put where the gait wants it by a two-bone solve in the leg's own
plane, and the paw is kept flat on the ground while it stands. A foot in stance
travels backward exactly at the speed the game moves the body (DESIGN_SPEEDS),
so at that speed nothing slides. The clips are sampled on every frame at 60 fps,
which the fast gallop needs (its feet touch the ground for a few hundredths of a
second).

Clip lengths are tied to the brain's timing (Charger.cs): the wind-up acts over
the 1.5 s tell and then holds its coiled set, the recovery covers the skid and
the winded beat.
"""
import math

import bpy
from mathutils import Vector

FPS = 60
CLIPS = ("Recovering",)
# The speed (m/s) each locomotion clip's feet are planted for: the brain's prowl
# (0.8 of a diver's 4 m/s walk) and the rush (three times the 6 m/s sprint).
DESIGN_SPEEDS = {"Hunting": 3.2, "Rushing": 18.0}

LEGS = {  # name: (upper, lower, paw, front?)
    "HL": ("ThighL", "ShinL", "FootL", False),
    "HR": ("ThighR", "ShinR", "FootR", False),
    "FL": ("UpperArmL", "LowerArmL", "HandL", True),
    "FR": ("UpperArmR", "LowerArmR", "HandR", True),
}
BODY = ("Root", "Hips", "Spine", "Neck", "Head")

TAU = 2.0 * math.pi


# ---- small maths ----------------------------------------------------------------------

def clamp01(x):
    return 0.0 if x < 0.0 else 1.0 if x > 1.0 else x


def smooth(x):
    x = clamp01(x)
    return x * x * (3.0 - 2.0 * x)


def window(t, a, b, ramp):
    """1 inside [a, b], easing in and out over `ramp` seconds, 0 outside."""
    return smooth((t - a) / ramp) * (1.0 - smooth((t - (b - ramp)) / ramp))


def bump(t, a, b):
    """A smooth 0 → 1 → 0 hump over [a, b]."""
    if t <= a or t >= b:
        return 0.0
    return math.sin(math.pi * (t - a) / (b - a)) ** 2


def ease_to(t, a, b):
    return smooth((t - a) / (b - a))


def spring(t, t0, amp, freq, decay):
    """A decaying oscillation starting at t0 (0 before): overshoot and settle."""
    if t < t0:
        return 0.0
    s = t - t0
    return amp * math.exp(-decay * s) * math.sin(TAU * freq * s)


def ang2(u, v):
    """Signed angle (radians) from u to v in the (y, z) plane, about +X."""
    return math.atan2(u[0] * v[1] - u[1] * v[0], u[0] * v[0] + u[1] * v[1])


def rot2(u, a):
    c, s = math.cos(a), math.sin(a)
    return (u[0] * c - u[1] * s, u[0] * s + u[1] * c)


def yz(v):
    return (v.y, v.z)


# ---- the pose of one frame ----------------------------------------------------------------

class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.pb = arm.pose.bones
        bones = arm.data.bones
        self.rest_ankle = {}
        self.rest_paw = {}
        self.bend = {}
        for leg, (up, lo, paw, _) in LEGS.items():
            a0 = bones[up].tail_local - bones[up].head_local
            b0 = bones[lo].tail_local - bones[lo].head_local
            self.rest_ankle[leg] = bones[paw].head_local.copy()
            self.rest_paw[leg] = yz(bones[paw].tail_local - bones[paw].head_local)
            self.bend[leg] = 1.0 if (a0.y * b0.z - a0.z * b0.y) > 0 else -1.0
        self.worst = 0.0
        self.clamped = 0

    def reset(self):
        for p in self.pb:
            p.rotation_mode = "XYZ"
            p.rotation_euler = (0, 0, 0)
            p.location = (0, 0, 0)

    roll = 0.0

    def body(self, pose):
        """pose: bone -> (rot degrees (x, y, z), loc (x, y, z)) in the bone's own axes.
        The Root's axes: y is up, z is forward; +x on any spine bone is nose down."""
        self.roll = math.radians(pose["Root"][0][2]) if "Root" in pose else 0.0
        for name, (rot, loc) in pose.items():
            p = self.pb[name]
            p.rotation_euler = tuple(math.radians(a) for a in rot)
            p.location = loc

    def world(self, name, tail=False):
        p = self.pb[name]
        return self.arm.matrix_world @ (p.tail if tail else p.head)

    def legs(self, feet):
        """feet: leg -> (forward m, up m, toe-down degrees) from the rest ankle, in the
        world (forward = -Y). Solves the upper and lower bone, then the paw."""
        bpy.context.view_layer.update()
        solved = {}
        for leg, spec in feet.items():
            fwd, up, toe = spec[:3]
            stance = spec[3] if len(spec) > 3 else True
            ub, lb, pb, front = LEGS[leg]
            H = yz(self.world(ub))
            K0 = yz(self.world(lb))
            A0 = yz(self.world(pb))
            P1 = yz(self.world(pb, tail=True))
            dA0 = (K0[0] - H[0], K0[1] - H[1])
            dB0 = (A0[0] - K0[0], A0[1] - K0[1])
            dP0 = (P1[0] - A0[0], P1[1] - A0[1])
            la = math.hypot(*dA0)
            lb = math.hypot(*dB0)
            rest = self.rest_ankle[leg]
            T = (rest.y - fwd, rest.z + up)
            D = (T[0] - H[0], T[1] - H[1])
            d = math.hypot(*D)
            lo_d, hi_d = abs(la - lb) + 1e-4, (la + lb) * 0.9995
            if (d > hi_d or d < lo_d) and stance:
                self.clamped += 1
                d = max(lo_d, min(hi_d, d))
            dn = (D[0] / max(d, 1e-9), D[1] / max(d, 1e-9))
            cos_phi = (la * la + d * d - lb * lb) / (2 * la * d)
            phi = math.acos(max(-1.0, min(1.0, cos_phi)))
            best = None
            for sgn in (1.0, -1.0):
                k_dir = rot2(dn, sgn * phi)
                K = (H[0] + k_dir[0] * la, H[1] + k_dir[1] * la)
                Tc = (H[0] + dn[0] * d, H[1] + dn[1] * d)
                c = (K[0] - H[0]) * (Tc[1] - K[1]) - (K[1] - H[1]) * (Tc[0] - K[0])
                if (c > 0) == (self.bend[leg] > 0):
                    best = (K, Tc)
            if best is None:  # a straight leg: both bends are the same
                k_dir = rot2(dn, phi)
                best = ((H[0] + k_dir[0] * la, H[1] + k_dir[1] * la), (H[0] + dn[0] * d, H[1] + dn[1] * d))
            K, Tc = best
            alpha = ang2(dA0, (K[0] - H[0], K[1] - H[1]))
            beta = ang2(dB0, (Tc[0] - K[0], Tc[1] - K[1])) - alpha
            # The paw: flat as at rest, turned by `toe` (toe down is +x for the front paws,
            # which point forward, and -x for the hind ones, which point back).
            want = rot2(self.rest_paw[leg], math.radians(toe) * (1.0 if front else -1.0))
            gamma = ang2(dP0, want) - alpha - beta
            solved[leg] = (alpha, beta, gamma, T if stance else None)
        for leg, (alpha, beta, gamma, _) in solved.items():
            ub, lb, pb, _ = LEGS[leg]
            self.pb[ub].rotation_euler = (alpha, 0, 0)
            self.pb[lb].rotation_euler = (beta, 0, 0)
            self.pb[pb].rotation_euler = (gamma, -self.roll, 0)
        bpy.context.view_layer.update()
        for leg, (_, _, _, T) in solved.items():
            if T is None:
                continue
            A = yz(self.world(LEGS[leg][2]))
            self.worst = max(self.worst, math.hypot(A[0] - T[0], A[1] - T[1]))

    def snapshot(self):
        out = {}
        for p in self.pb:
            out[p.name] = (tuple(math.degrees(a) for a in p.rotation_euler), tuple(p.location))
        return out


def bake(arm, L, name, seconds, pose_fn, loop):
    """Sample pose_fn(t) -> (body, feet) on every frame and key it."""
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    rig = Rig(arm)
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = None
    n = max(2, int(round(seconds * FPS)))
    keys = {p.name: [] for p in arm.pose.bones}
    lows = {}
    last = n if loop else n  # a loop's last frame repeats its first
    for i in range(last + 1):
        t = (i % n) / FPS if loop else i / FPS
        rig.reset()
        body, feet = pose_fn(t)
        rig.body(body)
        rig.legs(feet)
        for bone, (rot, loc) in rig.snapshot().items():
            keys[bone].append((1 + i, rot, loc))
        if MEASURE and i % 3 == 0:
            low = lowest(arm)
            for k, v in low.items():
                lows[k] = min(lows.get(k, 9.0), v)
    rig.reset()
    L.action(arm, name, n + 1, keys)
    print("clip %s: %.2f s, %d frames, worst stance ankle %.1f mm, %d stance frames out of reach" % (name, n / FPS, n + 1, rig.worst * 1000, rig.clamped))
    if MEASURE:
        print("   lowest (m):", ", ".join("%s %.3f" % kv for kv in sorted(lows.items())))


MEASURE = False  # the render script turns it on: how low the head and the body reach


def lowest(arm):
    """The lowest point of the skinned mesh by the bone that mostly carries it."""
    mesh = next(o for o in bpy.data.objects if o.type == "MESH" and o.parent == arm)
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    data = ev.to_mesh()
    groups = {g.index: g.name for g in mesh.vertex_groups}
    out = {}
    src = mesh.data.vertices
    for v, w in zip(data.vertices, src):
        z = (ev.matrix_world @ v.co).z
        best = max(w.groups, key=lambda g: g.weight, default=None)
        region = groups.get(best.group, "?") if best is not None else "?"
        region = "head" if region in ("Head", "Neck") else "feet" if region.startswith(("Foot", "Hand")) else "legs" if region.startswith(("Thigh", "Shin", "UpperArm", "LowerArm")) else "body"
        if z < out.get(region, 9.0):
            out[region] = z
    ev.to_mesh_clear()
    return out


def body_pose(root_up=0.0, root_fwd=0.0, pitch=0.0, roll=0.0, yaw=0.0, hips=0.0, spine=(0.0, 0.0, 0.0), neck=(0.0, 0.0, 0.0), head=(0.0, 0.0, 0.0)):
    """Degrees and metres. pitch > 0 is nose down, about the body's middle (the Root
    pivots at the back on the ground; its location puts the middle back). The
    neck's pitch is the head's own, in the world: the neck takes back whatever the
    body, the hips and the spine pitched, so the head only moves when it means to
    (it hangs 0.2 m over the floor at rest: every degree down is 1.7 cm at the
    snout). roll > 0 leans onto the left side."""
    pivot = Vector((0.0, 0.0, 0.7))
    root_head = Vector((0.0, 1.452, 0.0))
    a = math.radians(pitch)
    rel = (pivot.y - root_head.y, pivot.z - root_head.z)
    moved = rot2(rel, a)
    dy = rel[0] - moved[0]
    dz = rel[1] - moved[1]
    # Root local axes: x = x, y = up, z = forward (world -Y).
    loc = (0.0, root_up + dz, root_fwd - dy)
    neck_x = neck[0] - (pitch + hips + spine[0])
    return {
        "Root": ((pitch, yaw, roll), loc),
        "Hips": ((hips, 0.0, 0.0), (0, 0, 0)),
        "Spine": (spine, (0, 0, 0)),
        "Neck": ((neck_x, neck[1], neck[2]), (0, 0, 0)),
        "Head": (head, (0, 0, 0)),
    }


# ---- gaits ----------------------------------------------------------------------------------

PAW = 0.323                      # the paw bone's length, m
PAW_DOWN = math.radians(25.8)    # and its slope at rest, the toe below the ankle


def tip_rise(toe_deg):
    """How far the ankle must rise for a paw turned toe-down by this much to keep
    its tip on the floor."""
    return PAW * (math.sin(PAW_DOWN + math.radians(toe_deg)) - math.sin(PAW_DOWN))


def gait_foot(u, duty, stride, lift, reach=0.0, kick=0.0, toe_swing=30.0, peel_deg=18.0):
    """One foot over a normalised cycle u in [0, 1): stance first (from +stride/2 ahead
    to -stride/2 behind, at the body's speed), then the swing (lifted, overshooting
    back by `kick` after push-off and reaching `reach` past the landing spot).
    Returns (forward, up, toe-down degrees, in stance)."""
    u %= 1.0
    if u < duty:
        s = u / duty
        fwd = stride * (0.5 - s)
        # The heel peels off at the end of the stance; the toe stays on the floor.
        toe = peel_deg * smooth((s - 0.6) / 0.4)
        return fwd, tip_rise(toe), toe, True
    s = (u - duty) / (1.0 - duty)
    base = -0.5 * stride + stride * smooth(s)
    over = -kick * math.sin(math.pi * clamp01(s / 0.45)) + reach * math.sin(math.pi * clamp01((s - 0.45) / 0.55))
    # The toe leaves curled from the peel, folds under in flight, and opens to land flat.
    toe = peel_deg + (toe_swing - peel_deg) * smooth(s / 0.3) - (toe_swing + 6.0) * smooth((s - 0.55) / 0.4) + 6.0 * smooth((s - 0.9) / 0.1)
    up = max(lift * math.sin(math.pi * s) ** 0.9, tip_rise(toe) + 0.01 * math.sin(math.pi * s))
    return base + over, up, toe, False


def prowl(t):
    """Hunting: a heavy stalking trot, low and level, the head steady and forward.
    0.533 s a cycle with the feet planted for 3.2 m/s."""
    T = 32 / FPS
    u = t / T
    duty = 0.42
    stride = DESIGN_SPEEDS["Hunting"] * duty * T
    phases = {"HL": 0.0, "FR": 0.03, "HR": 0.5, "FL": 0.53}  # diagonal pairs, the front a touch late
    feet = {}
    for leg, ph in phases.items():
        front = LEGS[leg][3]
        f, up, toe, st = gait_foot(u - ph, duty, stride, lift=0.12 if front else 0.10, reach=0.03, kick=0.03, toe_swing=40.0)
        f += 0.03 if front else -0.02
        feet[leg] = (f, up, toe, st)
    c2 = math.cos(TAU * 2 * (u - 0.12))  # twice a cycle: lowest just after each landing
    s1 = math.sin(TAU * u)
    body = body_pose(
        root_up=-0.075 + 0.016 * c2,
        root_fwd=0.015,
        pitch=1.0 + 0.6 * math.sin(TAU * 2 * (u - 0.05)),
        roll=2.0 * s1,
        yaw=1.5 * math.sin(TAU * (u - 0.1)),
        spine=(0.5 * c2, -2.0 * math.sin(TAU * (u - 0.15)), 0.0),
        # The head rides steady; a small nod a beat after the body's (overlap).
        neck=(-2.0 + 0.8 * math.cos(TAU * 2 * (u - 0.25)), 2.0 * math.sin(TAU * (u - 0.3)), -1.2 * s1),
        head=(1.0 + 0.6 * math.cos(TAU * 2 * (u - 0.32)), 1.0 * math.sin(TAU * (u - 0.4)), 0.0),
    )
    return body, feet


def gallop(t):
    """Rushing: a flat-out transverse gallop, 0.4 s a stride with the feet planted
    for 18 m/s (each is down for 0.04 s), the spine gathering and stretching, a
    bound over each pair, the head held low and dead steady like a ram's. The
    cycle starts on the hind legs' push-off, so the blend from the coiled wind-up
    reads as the launch."""
    T = 24 / FPS
    u = t / T
    duty = 0.10
    stride = DESIGN_SPEEDS["Rushing"] * duty * T
    phases = {"HL": 0.0, "HR": 0.08, "FL": 0.47, "FR": 0.56}
    feet = {}
    for leg, ph in phases.items():
        front = LEGS[leg][3]
        f, up, toe, st = gait_foot(u - ph, duty, stride, lift=0.2 if front else 0.18, reach=0.08 if front else 0.05, kick=0.06 if front else 0.1, toe_swing=60.0 if front else 50.0, peel_deg=24.0)
        f += 0.03 if front else 0.0
        feet[leg] = (f, up, toe, st)
    # Gathered (the hind legs coming under the belly) around u 0.9, stretched around 0.4.
    flex = math.cos(TAU * (u - 0.9))
    body = body_pose(
        root_up=-0.06 + 0.05 * math.cos(TAU * 2 * (u - 0.3)),
        root_fwd=0.04 * flex,
        pitch=3.0 * math.sin(TAU * (u - 0.3)),
        roll=1.5 * math.sin(TAU * (u - 0.05)),
        yaw=1.2 * math.sin(TAU * (u - 0.2)),
        hips=-5.0 * flex,
        spine=(5.0 * flex, -1.5 * math.sin(TAU * (u - 0.25)), 0.0),
        neck=(-4.0 + 0.8 * math.sin(TAU * 2 * (u - 0.4)), 0.0, 0.0),
        head=(0.5, 0.0, 0.0),
    )
    return body, feet


# ---- the rest -------------------------------------------------------------------------------

def planted(fl=(0, 0, 0), fr=(0, 0, 0), hl=(0, 0, 0), hr=(0, 0, 0)):
    return {"FL": fl, "FR": fr, "HL": hl, "HR": hr}


def idle(t):
    """Idle (3.2 s loop): standing heavy, breathing, the weight rolling from side
    to side, the head sweeping slowly, and one snort: a dip and a toss."""
    T = 3.2
    u = t / T
    breath = math.sin(TAU * 2 * u)  # two breaths a loop
    sway = math.sin(TAU * u)
    look = math.sin(TAU * u + 0.6) * 0.7 + 0.3 * math.sin(TAU * 2 * u)
    snort = bump(t, 2.05, 2.5)
    toss = spring(t, 2.42, -5.0, 2.2, 5.0) * (1.0 - smooth((t - 3.0) / 0.2))
    body = body_pose(
        root_up=-0.025 + 0.008 * breath,
        pitch=0.4 * breath,
        roll=1.1 * sway,
        yaw=0.6 * sway,
        spine=(-0.8 * breath, 0.0, 0.0),
        neck=(-3.0 + 6.0 * snort + toss + 0.4 * breath, 9.0 * look, 1.5 * sway),
        head=(3.0 * snort - 0.6 * breath, 5.0 * look, 0.0),
    )
    return body, planted()


def windup(t):
    """Windup, the tell (1.5 s, then the coiled set holds): it notices and rears
    back with its head up, lowers the head into the line, scrapes the ground twice
    with its right front foot like a bull while a tremble builds through the whole
    body, sinks onto its haunches, and goes still and tense for the last 0.2 s (the
    aim locks) before it explodes into the rush."""
    notice = bump(t, 0.0, 0.34)
    lower = ease_to(t, 0.2, 0.9)
    sink = ease_to(t, 0.85, 1.3)
    coil = ease_to(t, 1.28, 1.5)
    # The tremble: builds from 0.25 s, strongest about 1.1 s, gone by the set.
    amp = smooth((t - 0.25) / 0.6) * (1.0 - smooth((t - 1.16) / 0.12))
    amp *= 0.6 + 0.4 * smooth((t - 0.8) / 0.3)
    shake = amp * (math.sin(TAU * 13.0 * t) + 0.45 * math.sin(TAU * 21.0 * t + 1.3))
    shake2 = amp * math.sin(TAU * 17.0 * t + 0.7)
    # After the set, a held breath: the faintest quiver (in case the rush is late).
    quiver = smooth((t - 1.5) / 0.1) * 0.3 * math.sin(TAU * 9.0 * t)
    body = body_pose(
        root_up=-0.02 - 0.015 * lower - 0.045 * sink - 0.02 * coil + 0.004 * shake2,
        root_fwd=-0.05 * notice - 0.03 * sink - 0.07 * coil,
        pitch=-2.5 * notice + 1.0 * lower - 1.2 * coil + 0.5 * shake,
        roll=3.0 * shake + 0.5 * quiver,
        yaw=1.4 * shake2,
        hips=-0.8 * sink - 0.7 * coil,  # onto the haunches: the rear drops
        spine=(0.8 * lower - 1.5 * coil, 1.2 * shake2, 0.0),
        # The head drops into the line: the neck down, the chin tucked so the horns lead.
        neck=(-18.0 * notice + 5.0 * lower + 1.0 * coil + 1.5 * shake, 2.5 * shake2, 2.0 * shake),
        head=(-8.0 * notice - 3.0 * lower - 1.0 * sink + 1.5 * shake2, 2.5 * shake, 0.0),
    )
    # The right front foot's two scrapes: lift forward, plant, drag back hard.
    fr = [0.0, 0.0, 0.0, True]
    for a in (0.3, 0.64):
        lift = bump(t, a, a + 0.14)
        out = ease_to(t, a, a + 0.12)
        drag = ease_to(t, a + 0.13, a + 0.3)
        home = ease_to(t, a + 0.3, a + 0.34)
        fr[0] += (0.13 * out - 0.2 * drag) * (1.0 - home)
        fr[1] += 0.13 * lift + 0.02 * bump(t, a + 0.29, a + 0.35)
        fr[2] += 25.0 * lift
    fr[1] = max(fr[1], tip_rise(fr[2]))
    # The hind feet gather under the body for the push; the front ones brace.
    hind = (0.07 * sink + 0.05 * coil, 0.0, 0.0)
    fl = (0.03 * sink + 0.02 * coil, 0.0, 0.0)
    fr[0] += 0.03 * sink + 0.02 * coil
    return body, planted(fl=fl, fr=tuple(fr), hl=hind, hr=hind)


def recovering(t):
    """Recovering (2.2 s, plays once): the skid (the front legs braced out, the
    body sat back and low, the head tossed up: a hit's impact or a miss's fury),
    the lurch as its weight lands forward, a dazed shake of the head, two heavy
    breaths, and the head coming back up to find its prey."""
    skid = window(t, 0.0, 0.42, 0.08)
    toss = bump(t, 0.02, 0.36)
    lurch = spring(t, 0.34, 4.0, 1.6, 4.0)
    daze = spring(t, 0.8, 1.0, 4.5, 2.2) * (1.0 - smooth((t - 1.55) / 0.2))
    breaths = math.sin(TAU * 1.25 * (t - 1.2)) * window(t, 1.2, 2.2, 0.25)
    tired = window(t, 0.75, 2.05, 0.35)
    ready = ease_to(t, 1.85, 2.2)  # into the prowl's stance, for the blend back to the hunt
    body = body_pose(
        root_up=-0.1 * skid - 0.02 * max(0.0, lurch / 4.0) - 0.02 * tired + 0.01 * breaths - 0.055 * ready,
        root_fwd=-0.05 * skid,
        pitch=-1.0 * skid + lurch + 0.8 * tired + 0.5 * breaths,
        roll=3.0 * daze,
        yaw=2.0 * daze,
        hips=-1.0 * skid,
        spine=(-1.5 * skid - 1.0 * breaths, 4.0 * daze, 0.0),
        neck=(-14.0 * toss + 0.8 * lurch + 3.0 * tired - 1.2 * breaths - 2.0 * ready, 14.0 * daze, 10.0 * daze),
        head=(-8.0 * toss + 1.5 * tired + 1.0 * ready, 10.0 * daze, 6.0 * daze),
    )
    front = (0.13 * skid, 0.0, 0.0)
    hind = (0.10 * skid, 0.0, 0.0)
    return body, planted(fl=front, fr=front, hl=hind, hr=hind)


def animate(arm, height, L):
    if MEASURE:
        Rig(arm).reset()
        bpy.context.view_layer.update()
        print("rest lowest (m):", ", ".join("%s %.3f" % kv for kv in sorted(lowest(arm).items())))
    bpy.context.scene.render.fps = FPS
    bpy.context.scene.render.fps_base = 1.0
    bake(arm, L, "Idle", 3.2, idle, loop=True)
    bake(arm, L, "Hunting", 32 / FPS, prowl, loop=True)
    bake(arm, L, "Windup", 2.1, windup, loop=False)
    bake(arm, L, "Rushing", 24 / FPS, gallop, loop=True)
    bake(arm, L, "Recovering", 2.2, recovering, loop=False)
    # Leave the bones at rest: the exporter writes the pose they hold as the model's
    # default, and the anchors (BeamOrigin, Voice) and Unity's edit-mode pose follow it.
    Rig(arm).reset()
    arm.animation_data.action = None
    bpy.context.view_layer.update()
