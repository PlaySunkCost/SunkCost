"""The ship's sign variants (ship audit SHIP-057, 23 September 2026).

Dan's generated Signs model is one set of three plates, and the ship hung the same
set in four places, one of them a "no diving" sign on a diving ship. This paints
new faces onto the same plates, one set per place, in the model's own style: the
worn cream plate, the orange stripes and the bolted frames stay the model's; only
the middle of each plate, where the pictogram was, is repainted.

    python tools/art/make_ship_signs.py <mesh dump>

The mesh dump is the Signs mesh in its prefab's space, written by
SunkCost.Editor.Look.ShipSignVariants.DumpMesh (one "v x y z nx ny nz u v" line per
vertex, one "f a b c" per triangle). Writes, beside the model:

    Models/Ship/Signs/Maps/Signs_BaseColor.jpg, Signs_Normal.png  (the default set,
        the "no diving" plate replaced by the dive cage's, so an unwired sign is
        never wrong; the model's own maps, as prepare_ship_part.py bakes them, are
        kept in tools/art/source: after a re-bake, copy the new Maps there first)
    Models/Ship/Signs/Variants/Signs_<Variant>_BaseColor.jpg, _Normal.png

The plates read along the prefab's +Z: a viewer in front looks toward -Z, so the
viewer's left is +X. Text is Bahnschrift, the Windows DIN face, rendered into the
texture (no font ships with the game).
"""
import os
import shutil
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SIGNS = os.path.join(ROOT, "Assets", "_Project", "Models", "Ship", "Signs")
MAPS = os.path.join(SIGNS, "Maps")
VARIANTS = os.path.join(SIGNS, "Variants")
ORIGINAL_BASE = os.path.join(MAPS, "Signs_BaseColor.jpg")
ORIGINAL_NORMAL = os.path.join(MAPS, "Signs_Normal.png")
FONT = r"C:\Windows\Fonts\bahnschrift.ttf"

BASE_RES = 2048   # the variants' colour: twice the model's, so the words stay sharp up close
NORMAL_RES = 1024
DPM = 4000        # the design canvas, pixels per metre of plate

# The model's own palette, sampled from its plates (median sRGB).
CREAM = np.array([149, 149, 147], float)
ORANGE = (171, 89, 12)
YELLOW = (202, 151, 76)
RED = (192, 33, 3)
TEAL = (1, 65, 75)
TEAL_LIGHT = (129, 186, 192)
INK = (30, 30, 31)

# The repainted middle of each plate, in the prefab's metres, left to right as read:
# (x at the reader's left, x at the reader's right, y top, y bottom). Below it the
# model's stripes, above it its corner stripes and bolts.
PLATES = [(0.378, 0.160), (0.117, -0.125), (-0.168, -0.395)]
TOP, BOTTOM = 0.376, 0.110


# ---- the mesh: which texel is where on the plates ---------------------------------

def load_mesh(path):
    verts, faces = [], []  # returns positions, normals, uvs, triangles
    with open(path) as f:
        for line in f:
            p = line.split()
            if not p:
                continue
            if p[0] == "v":
                verts.append([float(x) for x in p[1:]])
            elif p[0] == "f":
                faces.append([int(x) for x in p[1:]])
    v = np.array(verts)
    return v[:, 0:3], v[:, 3:6], v[:, 6:8], np.array(faces)


def rasterise(pos3, uv, faces, res):
    """Per texel: its point on the model, its side (1 front, 2 back, 0 neither) and its triangle.

    Two passes: the texels whose centres a triangle covers, then, for texels no
    triangle has claimed, any triangle within 0.75 texel - the decimated mesh has
    slivers along its old pictograms thinner than a texel, and a sliver's texels
    left unpainted show the old sign's edges through the new one."""
    a, b, c = pos3[faces[:, 0]], pos3[faces[:, 1]], pos3[faces[:, 2]]
    n = np.cross(b - a, c - a)
    n /= np.maximum(1e-12, np.linalg.norm(n, axis=1))[:, None]
    side = np.where(n[:, 2] > 0.7, 1, np.where(n[:, 2] < -0.7, 2, 0))
    # The old pictograms are in the geometry too, as low ridges: their steep sides
    # face sideways, and inside a plate's middle they belong to its face all the same.
    cen = (a + b + c) / 3
    middle = (cen[:, 1] > BOTTOM) & (cen[:, 1] < TOP) & np.any([(cen[:, 0] < max(l, r)) & (cen[:, 0] > min(l, r)) for l, r in PLATES], axis=0)
    side = np.where((side == 0) & middle, np.where(cen[:, 2] > 0, 1, 2), side)
    pos = np.full((res, res, 3), np.nan, np.float32)
    sid = np.zeros((res, res), np.int8)
    tid = np.full((res, res), -1, np.int32)
    bary = np.zeros((res, res, 3), np.float32)
    for conservative in (False, True):
        for t, (i, j, k) in enumerate(faces):  # every triangle, so every island is known
            q = np.array([uv[i], uv[j], uv[k]]) * res
            q[:, 1] = res - q[:, 1]  # image rows run down, v runs up
            x0, x1 = max(int(np.floor(q[:, 0].min())) - 1, 0), min(int(np.ceil(q[:, 0].max())) + 1, res - 1)
            y0, y1 = max(int(np.floor(q[:, 1].min())) - 1, 0), min(int(np.ceil(q[:, 1].max())) + 1, res - 1)
            if x1 < x0 or y1 < y0:
                continue
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
            A, B, C = q
            d = (B[1] - C[1]) * (A[0] - C[0]) + (C[0] - B[0]) * (A[1] - C[1])
            if abs(d) < 1e-12:
                continue
            l1 = ((B[1] - C[1]) * (xs - C[0]) + (C[0] - B[0]) * (ys - C[1])) / d
            l2 = ((C[1] - A[1]) * (xs - C[0]) + (A[0] - C[0]) * (ys - C[1])) / d
            l3 = 1 - l1 - l2
            if conservative:
                # A barycentric times its vertex's height over the far edge is the
                # distance in texels to that edge.
                area2 = abs(d)
                h1, h2, h3 = area2 / max(np.linalg.norm(B - C), 1e-9), area2 / max(np.linalg.norm(C - A), 1e-9), area2 / max(np.linalg.norm(A - B), 1e-9)
                m = (l1 * h1 >= -0.75) & (l2 * h2 >= -0.75) & (l3 * h3 >= -0.75)
            else:
                m = (l1 >= -0.02) & (l2 >= -0.02) & (l3 >= -0.02)  # a hair over the edge: no seams
            yy, xx = (ys - 0.5).astype(int), (xs - 0.5).astype(int)
            if conservative:
                m &= tid[yy, xx] < 0
            if not m.any():
                continue
            p = l1[..., None] * pos3[i] + l2[..., None] * pos3[j] + l3[..., None] * pos3[k]
            pos[yy[m], xx[m]] = p[m]
            sid[yy[m], xx[m]] = side[t]
            tid[yy[m], xx[m]] = t
            bary[yy[m], xx[m]] = np.stack([l1[m], l2[m], l3[m]], axis=-1)
    return pos, sid, tid, bary


# ---- drawing ------------------------------------------------------------------------

def font(px, style=b"Bold Condensed"):
    f = ImageFont.truetype(FONT, max(8, int(px)))
    f.set_variation_by_name(style)
    return f


class Plate:
    """A design canvas over one plate's repainted middle, drawn in fractions of its width."""

    def __init__(self, left, right):
        self.w = int(round((left - right) * DPM))
        self.h = int(round((TOP - BOTTOM) * DPM))
        self.img = Image.new("RGBA", (self.w, self.h), (0, 0, 0, 0))
        self.d = ImageDraw.Draw(self.img)
        self.relief = Image.new("L", (self.w, self.h), 0)  # paint thickness, for the normal map
        self.r = ImageDraw.Draw(self.relief)

    ASPECT = 1.24  # the design's height over its width: a wider plate centres it

    def X(self, f):
        return (self.w - self.unit) / 2 + f * self.unit

    def Y(self, f):
        return f * self.unit

    @property
    def unit(self):
        return min(self.w, self.h / self.ASPECT)  # every length is a fraction of the design's width

    def box(self, x0, y0, x1, y1):
        return [self.X(x0), self.Y(y0), self.X(x1), self.Y(y1)]

    def rect(self, x0, y0, x1, y1, fill, radius=0.0):
        b = self.box(x0, y0, x1, y1)
        self.d.rounded_rectangle(b, radius=radius * self.unit, fill=fill)
        self.r.rounded_rectangle(b, radius=radius * self.unit, fill=255)

    def ellipse(self, cx, cy, rad, fill=None, outline=None, width=0.0):
        b = self.box(cx - rad, cy - rad, cx + rad, cy + rad)
        self.d.ellipse(b, fill=fill, outline=outline, width=int(width * self.unit))
        self.r.ellipse(b, fill=255 if fill else None, outline=255 if outline else None, width=int(width * self.unit))

    def poly(self, pts, fill):
        q = [(self.X(x), self.Y(y)) for x, y in pts]
        self.d.polygon(q, fill=fill)
        self.r.polygon(q, fill=255)

    def line(self, pts, fill, width):
        q = [(self.X(x), self.Y(y)) for x, y in pts]
        self.d.line(q, fill=fill, width=int(width * self.unit), joint="curve")
        self.r.line(q, fill=255, width=int(width * self.unit), joint="curve")

    def text(self, s, cy, size, fill=INK, style=b"Bold Condensed", max_w=0.9):
        """A line of capitals centred at cy; shrunk to fit max_w of the width."""
        px = size * self.unit
        f = font(px, style)
        while self.d.textlength(s, font=f) > max_w * self.unit and px > 8:
            px *= 0.95
            f = font(px, style)
        tw = self.d.textlength(s, font=f)
        x = (self.w - tw) / 2
        top, bottom = f.getbbox(s)[1], f.getbbox(s)[3]
        y = self.Y(cy) - (top + bottom) / 2
        self.d.text((x, y), s, font=f, fill=fill)
        self.r.text((x, y), s, font=f, fill=255)

    def header(self, s, y0=0.04, h=0.24, bar=INK, colour=(214, 212, 204)):
        """The black band across the top with the place's name in it."""
        self.rect(0.05, y0, 0.95, y0 + h, bar + (255,), radius=0.03)
        self.text(s, y0 + h / 2, h * 0.78, fill=colour)

    # -- the pictograms, each in a frame whose centre is (cx, cy) and half-size s --

    def warning(self, cx, cy, s):
        """The model's warning: a yellow triangle in a black border. Returns the inner centre."""
        tri = [(cx, cy - s), (cx + s * 1.1, cy + s * 0.85), (cx - s * 1.1, cy + s * 0.85)]
        self.poly(tri, INK + (255,))
        k = 0.78
        inner = [(cx, cy - s * k + s * 0.08), (cx + s * 1.1 * k, cy + s * 0.85 * k - 0.0), (cx - s * 1.1 * k, cy + s * 0.85 * k)]
        self.poly(inner, YELLOW + (255,))
        return cx, cy + s * 0.2

    def mandatory(self, cx, cy, s):
        """The model's round teal sign: dark teal disc, light teal ring."""
        self.ellipse(cx, cy, s, fill=TEAL_LIGHT + (255,))
        self.ellipse(cx, cy, s * 0.86, fill=TEAL + (255,))

    def crate(self, cx, cy, s, c):
        self.rect(cx - s, cy - s, cx + s, cy + s, c + (255,), radius=s * 0.08)
        g = CREAM_T
        self.rect(cx - s * 0.8, cy - s * 0.8, cx + s * 0.8, cy + s * 0.8, g, radius=s * 0.04)
        self.rect(cx - s * 0.8, cy - s * 0.1, cx + s * 0.8, cy + s * 0.1, c + (255,))
        self.line([(cx - s * 0.75, cy + s * 0.75), (cx + s * 0.75, cy - s * 0.75)], c + (255,), s * 0.2)

    def arrow(self, cx, cy, s, direction, c):
        """A thick arrow; direction +1 points to the reader's right."""
        d = direction
        pts = [(-1.0, -0.28), (0.15, -0.28), (0.15, -0.7), (1.0, 0.0), (0.15, 0.7), (0.15, 0.28), (-1.0, 0.28)]
        self.poly([(cx + d * x * s, cy + y * s) for x, y in pts], c + (255,))

    def drop(self, cx, cy, s, c):
        """Down into a tray: what the room is for."""
        self.poly([(cx - s * 0.22, cy - s * 0.85), (cx + s * 0.22, cy - s * 0.85), (cx + s * 0.22, cy - s * 0.15),
                   (cx + s * 0.5, cy - s * 0.15), (cx, cy + s * 0.35), (cx - s * 0.5, cy - s * 0.15), (cx - s * 0.22, cy - s * 0.15)], c + (255,))
        self.line([(cx - s * 0.8, cy + s * 0.25), (cx - s * 0.8, cy + s * 0.7), (cx + s * 0.8, cy + s * 0.7), (cx + s * 0.8, cy + s * 0.25)], c + (255,), s * 0.16)

    def wheel(self, cx, cy, s, c):
        """A ship's wheel: rim, hub, eight spokes with handles."""
        import math
        for i in range(8):
            a = i * math.pi / 4
            self.line([(cx, cy), (cx + math.cos(a) * s, cy + math.sin(a) * s)], c + (255,), s * 0.13)
            self.ellipse(cx + math.cos(a) * s * 0.98, cy + math.sin(a) * s * 0.98, s * 0.12, fill=c + (255,))
        self.ellipse(cx, cy, s * 0.7, outline=c + (255,), width=s * 0.15)
        self.ellipse(cx, cy, s * 0.2, fill=c + (255,))

    def person(self, cx, cy, s, c):
        self.ellipse(cx, cy - s * 0.62, s * 0.2, fill=c + (255,))
        self.poly([(cx - s * 0.32, cy - s * 0.32), (cx + s * 0.32, cy - s * 0.32), (cx + s * 0.4, cy + s * 0.2),
                   (cx + s * 0.22, cy + s * 0.2), (cx + s * 0.2, cy + s * 0.9), (cx - s * 0.2, cy + s * 0.9),
                   (cx - s * 0.22, cy + s * 0.2), (cx - s * 0.4, cy + s * 0.2)], c + (255,))

    def couch(self, cx, cy, s, c):
        self.rect(cx - s * 0.85, cy - s * 0.35, cx + s * 0.85, cy + s * 0.05, c + (255,), radius=s * 0.1)  # back
        self.rect(cx - s * 1.0, cy - s * 0.05, cx - s * 0.7, cy + s * 0.45, c + (255,), radius=s * 0.08)  # arms
        self.rect(cx + s * 0.7, cy - s * 0.05, cx + s * 1.0, cy + s * 0.45, c + (255,), radius=s * 0.08)
        self.rect(cx - s * 0.7, cy + s * 0.1, cx + s * 0.7, cy + s * 0.4, c + (255,), radius=s * 0.05)   # seat
        self.rect(cx - s * 0.9, cy + s * 0.45, cx - s * 0.75, cy + s * 0.62, c + (255,))                 # feet
        self.rect(cx + s * 0.75, cy + s * 0.45, cx + s * 0.9, cy + s * 0.62, c + (255,))

    def tv(self, cx, cy, s, c, screen):
        self.rect(cx - s * 0.85, cy - s * 0.55, cx + s * 0.85, cy + s * 0.4, c + (255,), radius=s * 0.06)
        self.rect(cx - s * 0.7, cy - s * 0.42, cx + s * 0.7, cy + s * 0.27, screen + (255,), radius=s * 0.03)
        self.line([(cx - s * 0.5, cy + s * 0.05), (cx - s * 0.25, cy - s * 0.1), (cx, cy + s * 0.05), (cx + s * 0.25, cy - s * 0.1), (cx + s * 0.5, cy + s * 0.05)], c + (255,), s * 0.09)
        self.rect(cx - s * 0.08, cy + s * 0.4, cx + s * 0.08, cy + s * 0.6, c + (255,))
        self.rect(cx - s * 0.4, cy + s * 0.58, cx + s * 0.4, cy + s * 0.68, c + (255,))

    def lifebuoy(self, cx, cy, s):
        import math
        self.ellipse(cx, cy, s, fill=INK + (255,))
        self.ellipse(cx, cy, s * 0.92, fill=ORANGE + (255,))
        for i in range(4):  # the four cream bands
            a = math.pi / 4 + i * math.pi / 2
            da = 0.28
            pts = [(cx + math.cos(a - da) * s * 0.92, cy + math.sin(a - da) * s * 0.92), (cx + math.cos(a + da) * s * 0.92, cy + math.sin(a + da) * s * 0.92),
                   (cx + math.cos(a + da) * s * 0.5, cy + math.sin(a + da) * s * 0.5), (cx + math.cos(a - da) * s * 0.5, cy + math.sin(a - da) * s * 0.5)]
            self.poly(pts, (214, 212, 204, 255))
        self.ellipse(cx, cy, s * 0.52, fill=INK + (255,))
        self.ellipse(cx, cy, s * 0.44, fill=CREAM_T)

    def cage(self, cx, cy, s, c):
        """The dive cage going down: a barred car over a down arrow."""
        self.rect(cx - s * 0.55, cy - s * 0.85, cx + s * 0.55, cy + s * 0.2, c + (255,), radius=s * 0.06)
        self.rect(cx - s * 0.42, cy - s * 0.72, cx + s * 0.42, cy + s * 0.07, TEAL + (255,))
        for i in range(-1, 2):
            self.rect(cx + i * s * 0.25 - s * 0.05, cy - s * 0.72, cx + i * s * 0.25 + s * 0.05, cy + s * 0.07, c + (255,))
        self.poly([(cx - s * 0.12, cy + s * 0.3), (cx + s * 0.12, cy + s * 0.3), (cx + s * 0.12, cy + s * 0.5), (cx + s * 0.35, cy + s * 0.5),
                   (cx, cy + s * 0.9), (cx - s * 0.35, cy + s * 0.5), (cx - s * 0.12, cy + s * 0.5)], c + (255,))

    def gap(self, cx, cy, s, c):
        """Mind the gap: a foot over the edge of a drop."""
        self.rect(cx - s * 1.0, cy + s * 0.3, cx - s * 0.2, cy + s * 0.5, c + (255,))
        self.rect(cx + s * 0.25, cy + s * 0.3, cx + s * 1.0, cy + s * 0.5, c + (255,))
        self.person(cx - s * 0.4, cy - s * 0.35, s * 0.62, c)
        self.line([(cx - s * 0.3, cy + s * 0.15), (cx + s * 0.1, cy + s * 0.05)], c + (255,), s * 0.14)


CREAM_T = tuple(int(x) for x in CREAM) + (255,)


# ---- the designs, one list of three plates per place --------------------------------
# Each is a function drawing onto a Plate, or None to keep the model's own plate.
# Heights are fractions of the plate's width; a plate is about 1.24 widths tall.

def storage_name(p):
    p.header("STORAGE")
    p.crate(0.5, 0.66, 0.24, INK)
    p.text("SALVAGE HOLD", 1.08, 0.15)


def storage_drop(p):
    p.mandatory(0.5, 0.46, 0.36)
    p.drop(0.5, 0.48, 0.24, TEAL_LIGHT)
    p.text("DROP LOOT", 0.94, 0.17)
    p.text("INSIDE", 1.1, 0.15, style=b"SemiBold Condensed")


def storage_door(p):
    # No arrow: where the sign hangs relative to the doorway is the dressing's to
    # choose, and an arrow the wrong way is worse than none.
    p.rect(0.2, 0.12, 0.8, 0.8, INK + (255,), radius=0.03)            # the doorway
    p.rect(0.28, 0.2, 0.72, 0.8, tuple(int(c) for c in CREAM) + (255,))
    p.person(0.5, 0.5, 0.26, INK)
    for i in range(5):                                                # the sill, striped
        x = 0.2 + i * 0.12
        p.poly([(x, 0.8), (x + 0.06, 0.8), (x + 0.12, 0.72), (x + 0.06, 0.72)], ORANGE + (255,))
    p.text("KEEP DOORWAY", 0.95, 0.15)
    p.text("CLEAR", 1.1, 0.15, style=b"SemiBold Condensed")


def bridge_name(p):
    p.header("BRIDGE")
    p.text("CREW", 0.6, 0.3)
    p.text("ONLY", 0.9, 0.3)
    p.rect(0.2, 1.06, 0.8, 1.1, ORANGE + (255,))


def bridge_console(p):
    p.mandatory(0.5, 0.46, 0.36)
    p.wheel(0.5, 0.46, 0.22, TEAL_LIGHT)
    p.text("SAILING", 0.94, 0.17)
    p.text("CONSOLE", 1.1, 0.15, style=b"SemiBold Condensed")


def bridge_aboard(p):
    cx, cy = p.warning(0.5, 0.42, 0.34)
    p.person(cx, cy + 0.01, 0.17, INK)
    p.text("ALL CREW ABOARD", 0.9, 0.12)
    p.text("BEFORE SAILING", 1.06, 0.12, style=b"SemiBold Condensed")


def lounge_name(p):
    p.header("LOUNGE")
    p.couch(0.5, 0.64, 0.3, INK)
    p.text("CREW REST", 1.08, 0.15)


def lounge_tv(p):
    p.mandatory(0.5, 0.46, 0.36)
    p.tv(0.5, 0.46, 0.25, TEAL_LIGHT, TEAL)
    p.text("DIVE FEED", 0.94, 0.17)
    p.text("ON THE TV", 1.1, 0.14, style=b"SemiBold Condensed")


def lounge_muster(p):
    p.lifebuoy(0.5, 0.46, 0.33)
    p.text("MUSTER", 0.94, 0.19)
    p.text("STATION", 1.1, 0.15, style=b"SemiBold Condensed")


def dive_cage(p):
    p.header("DIVE CAGE")
    p.mandatory(0.5, 0.62, 0.27)
    p.cage(0.5, 0.62, 0.2, TEAL_LIGHT)
    p.text("MIND THE GAP", 1.08, 0.15)


VARIANTS_DESIGN = {
    "Storage": [storage_name, storage_drop, storage_door],
    "Bridge": [bridge_name, bridge_console, bridge_aboard],
    "Lounge": [lounge_name, lounge_tv, lounge_muster],
    "DiveCage": [None, dive_cage, None],  # the model's creature warning and helmet sign stay: both are the dive's
}
DEFAULT = "DiveCage"


# ---- painting ---------------------------------------------------------------------------

def grime(size=1024, seed=57):
    """The worn-paint noise the cream is broken up by: tiling fractal noise (its
    normalised value, and a rust mask from a second, coarser field)."""
    rng = np.random.default_rng(seed)

    def octave(cells):
        lattice = rng.random((cells, cells))
        t = np.arange(size) / size * cells
        i0 = np.floor(t).astype(int)
        f = t - i0
        f = f * f * (3 - 2 * f)
        i1 = (i0 + 1) % cells
        a = lattice[np.ix_(i0, i0)] * (1 - f)[None, :] + lattice[np.ix_(i0, i1)] * f[None, :]
        b = lattice[np.ix_(i1, i0)] * (1 - f)[None, :] + lattice[np.ix_(i1, i1)] * f[None, :]
        return a * (1 - f)[:, None] + b * f[:, None]

    field = sum(octave(c) * w for c, w in [(4, 1.0), (8, 0.5), (16, 0.3), (32, 0.2), (64, 0.12), (128, 0.08)])
    k = (field - field.mean()) / field.std()
    rust = sum(octave(c) * w for c, w in [(6, 1.0), (24, 0.4), (96, 0.2)])
    rust = np.clip(((rust - rust.mean()) / rust.std() - 1.2) / 1.0, 0, 1)
    fine = sum(octave(c) * w for c, w in [(64, 1.0), (128, 0.7), (256, 0.5)])
    fine = (fine - fine.mean()) / fine.std()  # the small chips and flecks
    return k, rust, fine


def sample_tile(field, x, y, metres):
    """Tiling field sampled at points in metres, one tile per `metres`."""
    h, w = field.shape[:2]
    ix = (np.floor(x / metres * w).astype(int)) % w
    iy = (np.floor(y / metres * h).astype(int)) % h
    return field[iy, ix]


def sample_canvas(arr, u, v):
    """Bilinear sample of an HxWxC array at fractional pixel coordinates."""
    h, w = arr.shape[:2]
    u = np.clip(u, 0, w - 1.001)
    v = np.clip(v, 0, h - 1.001)
    x0, y0 = np.floor(u).astype(int), np.floor(v).astype(int)
    fx, fy = (u - x0)[..., None], (v - y0)[..., None]
    a = arr[y0, x0] * (1 - fx) + arr[y0, x0 + 1] * fx
    b = arr[y0 + 1, x0] * (1 - fx) + arr[y0 + 1, x0 + 1] * fx
    return a * (1 - fy) + b * fy


def region_alpha(x, y, left, right, k_edge):
    """How much of a texel is repainted: 1 inside the plate's middle, a worn feathered edge."""
    xl, xr = min(left, right), max(left, right)
    inside = np.minimum.reduce([x - xl, xr - x, TOP - y, y - BOTTOM])  # metres to the nearest edge
    inside = inside + k_edge * 0.0025
    return np.clip(inside / 0.006, 0, 1)


def pad(img, tid, texels=8):
    """The gutters between the atlas's islands refilled from the islands' edges.

    The model's bake padded its islands with their own colours; a repainted island
    kept the old sign's colours round it, and the mip maps drew them in as thin red
    and black lines along the decimated mesh's slivers. A few texels of sharp
    dilation, then every gutter filled from its neighbourhood (push-pull), so no
    mip level reaches an old colour."""
    out = img.astype(float).copy()
    filled = tid >= 0
    for _ in range(texels):
        acc = np.zeros_like(out)
        cnt = np.zeros(filled.shape, float)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            f = np.roll(filled, (dy, dx), axis=(0, 1))
            acc += np.roll(out, (dy, dx), axis=(0, 1)) * f[..., None]
            cnt += f
        grow = (~filled) & (cnt > 0)
        out[grow] = acc[grow] / cnt[grow][:, None]
        filled = filled | grow
    # Push: masked averages down to one texel. Pull: holes take the coarser level.
    levels = [(out * filled[..., None], filled.astype(float))]
    while levels[-1][1].shape[0] > 1:
        c, w = levels[-1]
        h = c.shape[0] // 2
        c2 = c.reshape(h, 2, h, 2, -1).sum(axis=(1, 3))
        w2 = w.reshape(h, 2, h, 2).sum(axis=(1, 3))
        levels.append((c2, w2))
    fill = levels[-1][0] / np.maximum(levels[-1][1], 1e-9)[..., None]
    for c, w in reversed(levels[:-1]):
        up = np.repeat(np.repeat(fill, 2, axis=0), 2, axis=1)
        fill = np.where(w[..., None] > 0, c / np.maximum(w, 1e-9)[..., None], up)
    return np.where(filled[..., None], out, fill)


def paint(variant, designs, pos, sid, base, kfield, rust, fine):
    """The variant's colour: the model's texture with each designed plate's middle repainted."""
    out = base.copy()
    relief = np.zeros(base.shape[:2], float)
    plan = [(1, i, designs[i]) for i in range(3)] + [(2, 1, "blank")]  # and the back of the old no-diving plate
    for s, i, design in plan:
        if design is None:
            continue
        left, right = PLATES[i]
        if s == 2:
            left, right = -left, -right  # from behind the reader's left is -X
        m = sid == s
        x, y = pos[..., 0][m], pos[..., 1][m]
        # A generous box first, then the soft edge.
        sel = (x > min(left, right) - 0.01) & (x < max(left, right) + 0.01) & (y > BOTTOM - 0.01) & (y < TOP + 0.01)
        idx = np.argwhere(m)[sel]
        x, y = x[sel], y[sel]
        k = sample_tile(kfield, x, y, 0.55)
        ru = sample_tile(rust, x, y, 0.55)
        kf = sample_tile(fine, x, y, 0.55)
        a = region_alpha(x, y, left, right, k)
        # The plate's cream, worn: the grime's light and dark, its rust, its dark flecks.
        bg = CREAM[None, :] * (1 + 0.07 * np.clip(k, -3, 3))[:, None]
        bg = bg * (1 - 0.35 * ru[:, None]) + np.array([118, 78, 48.0])[None, :] * 0.35 * ru[:, None]
        fleck = np.clip((-kf - 2.1) / 0.25, 0, 1)[:, None]
        bg = bg * (1 - 0.6 * fleck) + np.array([58, 56, 53.0])[None, :] * 0.6 * fleck
        col = bg
        rel = np.zeros(len(x))
        if design != "blank":
            plate = Plate(left, right)
            design(plate)
            img = np.asarray(plate.img.filter(ImageFilter.GaussianBlur(1.2)), float)
            rimg = np.asarray(plate.relief.filter(ImageFilter.GaussianBlur(3.0)), float)[..., None] / 255.0
            # The texel's place on the canvas: the reader's left at u = 0, the top at v = 0.
            u = (left - x) / (left - right) * plate.w if s == 1 else (x - left) / (right - left) * plate.w
            v = (TOP - y) / (TOP - BOTTOM) * plate.h
            px = sample_canvas(img, u, v)
            pa = px[:, 3:4] / 255.0
            # The paint chipped where the grime is darkest, as the model's own signs are.
            chip = np.clip((kf + 2.0) / 0.2, 0, 1)[:, None] * np.clip((k + 2.6) / 0.4, 0, 1)[:, None]
            pa = pa * chip
            paint_col = px[:, :3] * (1 + 0.05 * np.clip(k, -3, 3))[:, None]
            col = bg * (1 - pa) + paint_col * pa
            rel = sample_canvas(rimg, u, v)[:, 0] * chip[:, 0]
        rows, cols = idx[:, 0], idx[:, 1]
        out[rows, cols] = out[rows, cols] * (1 - a[:, None]) + col * a[:, None]
        relief[rows, cols] = np.maximum(relief[rows, cols], rel * a)
    return np.clip(out, 0, 255), relief


def plate_normals(pos3, normals, uv, faces, variant_designs, pos, sid, tid, bary, relief):
    """The variant's normal map: flat plate where the pictogram was, the new paint's edge in.

    The model's mesh is decimated round its old pictograms and flat-shaded, so the
    old shapes are in its facets as much as in its normal map. Over a repainted
    middle every texel is given the tangent-space normal that turns its own facet
    back to the plate's face (the plate's mean normal), so neither the facets nor
    the old bake show; then the paint's thickness is embossed very lightly. The
    normals are the mesh's own, interpolated per texel as the shader does: rerun
    if the model is reimported with other normals."""
    a, b, c = pos3[faces[:, 0]], pos3[faces[:, 1]], pos3[faces[:, 2]]
    n = normals[faces].mean(axis=1)
    n /= np.maximum(1e-12, np.linalg.norm(n, axis=1))[:, None]
    ua, ub, uc = uv[faces[:, 0]], uv[faces[:, 1]], uv[faces[:, 2]]
    dp1, dp2 = b - a, c - a
    du1, du2 = (ub - ua)[:, 0], (uc - ua)[:, 0]
    dv1, dv2 = (ub - ua)[:, 1], (uc - ua)[:, 1]
    det = du1 * dv2 - du2 * dv1
    det = np.where(np.abs(det) < 1e-12, 1e-12, det)
    T = (dp1 * dv2[:, None] - dp2 * dv1[:, None]) / det[:, None]
    B = (dp2 * du1[:, None] - dp1 * du2[:, None]) / det[:, None]
    T = T - n * np.sum(n * T, axis=1)[:, None]
    T /= np.maximum(1e-12, np.linalg.norm(T, axis=1))[:, None]
    w = np.sign(np.sum(np.cross(n, T) * B, axis=1))
    w = np.where(w == 0, 1, w)
    Bt = np.cross(n, T) * w[:, None]

    out = None
    region = np.zeros(sid.shape, float)
    target = np.zeros(sid.shape + (3,), float)
    plan = [(1, i) for i in range(3) if variant_designs[i] is not None] + [(2, 1)]
    fn = np.cross(dp1, dp2)
    area = np.linalg.norm(fn, axis=1) / 2
    for s, i in plan:
        left, right = PLATES[i]
        if s == 2:
            left, right = -left, -right
        m = sid == s
        x, y = np.nan_to_num(pos[..., 0]), np.nan_to_num(pos[..., 1])
        a_ = np.where(m, region_alpha(x, y, left, right, np.zeros_like(x)), 0)
        # The plate's own face: the area-weighted normal of its facets in the middle.
        cen = (a + b + c) / 3
        inplate = (np.minimum(left, right) < cen[:, 0]) & (cen[:, 0] < np.maximum(left, right)) & (cen[:, 1] > BOTTOM) & (cen[:, 1] < TOP) & (np.abs(n[:, 2]) > 0.7) & (np.sign(n[:, 2]) == (1 if s == 1 else -1))
        P = np.sum(n[inplate] * area[inplate, None], axis=0)
        P /= np.linalg.norm(P)
        sel = (a_ > 0) & (tid >= 0)
        t = tid[sel]
        # The normal the shader interpolates at the texel (the mesh is smooth-shaded),
        # its tangent frame re-orthogonalised to it.
        w3 = bary[sel].astype(float)
        nt = np.einsum("ij,ijk->ik", w3, normals[faces[t]])
        nt /= np.maximum(1e-12, np.linalg.norm(nt, axis=1))[:, None]
        tt = T[t] - nt * np.sum(nt * T[t], axis=1)[:, None]
        tt /= np.maximum(1e-12, np.linalg.norm(tt, axis=1))[:, None]
        bt = np.cross(nt, tt) * w[t][:, None]
        target[sel] = np.stack([np.sum(tt * P, axis=1), np.sum(bt * P, axis=1), np.sum(nt * P, axis=1)], axis=-1)
        region = np.maximum(region, a_)
    # The paint's edge: a slope where its thickness changes, in texture space.
    h = relief
    dhdu = np.zeros_like(h); dhdv = np.zeros_like(h)
    dhdu[:, 1:-1] = (h[:, 2:] - h[:, :-2]) / 2
    dhdv[1:-1, :] = -(h[2:, :] - h[:-2, :]) / 2  # rows run down, v runs up
    target[..., 0] -= 0.5 * dhdu
    target[..., 1] -= 0.5 * dhdv
    target /= np.maximum(1e-9, np.linalg.norm(target, axis=-1))[..., None]
    return target, region


def flatten_normals(normal, target, region):
    enc = (target + 1) / 2 * 255
    base = normal[..., :3].astype(float)
    return np.clip(base * (1 - region[..., None]) + enc * region[..., None], 0, 255)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    pos3, normals, uv, faces = load_mesh(sys.argv[1])
    os.makedirs(VARIANTS, exist_ok=True)
    source = os.path.join(os.path.dirname(os.path.abspath(__file__)), "source")
    os.makedirs(source, exist_ok=True)
    original_path = os.path.join(source, "Signs_BaseColor_meshy.jpg")
    original_normal_path = os.path.join(source, "Signs_Normal_meshy.png")
    # The model's own maps are the source every run: kept once in tools/art/source
    # (outside Assets, so Unity never imports them), since the default maps are
    # rewritten below.
    if not os.path.exists(original_path):
        shutil.copyfile(ORIGINAL_BASE, original_path)
    if not os.path.exists(original_normal_path):
        shutil.copyfile(ORIGINAL_NORMAL, original_normal_path)
    src = Image.open(original_path).convert("RGB")
    nsrc = np.asarray(Image.open(original_normal_path).convert("RGB"))
    base_hi = np.asarray(src.resize((BASE_RES, BASE_RES), Image.LANCZOS), float)
    base_lo = np.asarray(src, float)
    kfield, rust, fine = grime()
    hi = rasterise(pos3, uv, faces, BASE_RES)
    lo = rasterise(pos3, uv, faces, NORMAL_RES)
    for name, designs in VARIANTS_DESIGN.items():
        colour, _ = paint(name, designs, hi[0], hi[1], base_hi, kfield, rust, fine)
        colour = pad(colour, hi[2])
        _, relief = paint(name, designs, lo[0], lo[1], base_lo, kfield, rust, fine)
        target, region = plate_normals(pos3, normals, uv, faces, designs, lo[0], lo[1], lo[2], lo[3], relief)
        normal = pad(flatten_normals(nsrc, target, region), lo[2])
        Image.fromarray(colour.astype(np.uint8)).save(os.path.join(VARIANTS, f"Signs_{name}_BaseColor.jpg"), quality=90)
        Image.fromarray(normal.astype(np.uint8)).save(os.path.join(VARIANTS, f"Signs_{name}_Normal.png"))
        if name == DEFAULT:
            colour_lo, _ = paint(name, designs, lo[0], lo[1], base_lo, kfield, rust, fine)
            colour_lo = pad(colour_lo, lo[2])
            Image.fromarray(colour_lo.astype(np.uint8)).save(ORIGINAL_BASE, quality=92)
            Image.fromarray(normal.astype(np.uint8)).save(ORIGINAL_NORMAL)
        print(name, "done")


if __name__ == "__main__":
    main()
