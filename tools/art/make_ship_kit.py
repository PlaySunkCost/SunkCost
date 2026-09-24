"""The ship kit's worn textures (ship audit SHIP-058/059/060, 23 September 2026).

The code-built parts of the ship (the crew screen's frame, the buttons' bezels, the
well's grate and pedestal, the cabin's cap) were flat colours beside Dan's textured
generated models. These are tiling maps in the models' own palette (sampled from
their plates) for ShipKitMaterials:

    KitSteel   worn dark steel: the models' charcoal, scuffed to bare metal at chips,
               a little rust                                         (1 m a tile)
    KitBezel   the same steel darker and cleaner, for screen and button surrounds
    KitHazard  the models' worn orange and black stripe, 10 cm stripes (1 m a tile)
    StorageDropZone  the storage room's painted floor mark: a striped border and
               DROP ZONE, a cutout decal (2.4 x 2.0 m)

    python tools/art/make_ship_kit.py

Writes Assets/_Project/Art/Ship/Textures/<name>_BaseColor.png and _Normal.png.
Every map tiles: the noise is built on a periodic lattice.
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Ship", "Textures")
FONT = r"C:\Windows\Fonts\bahnschrift.ttf"
RES = 1024

# The generated models' palette (median sRGB off their plates and panels).
STEEL = np.array([64, 67, 72], float)  # a step over the plates' darkest: flat on a box it read black beside the models
BARE = np.array([112, 112, 108], float)
RUST = np.array([104, 62, 34], float)
ORANGE = np.array([171, 89, 12], float)
INK = np.array([30, 30, 31], float)
CREAM = np.array([200, 196, 184], float)


def octave(rng, cells, size=RES):
    lattice = rng.random((cells, cells))
    t = np.arange(size) / size * cells
    i0 = np.floor(t).astype(int)
    f = t - i0
    f = f * f * (3 - 2 * f)
    i1 = (i0 + 1) % cells
    a = lattice[np.ix_(i0, i0)] * (1 - f)[None, :] + lattice[np.ix_(i0, i1)] * f[None, :]
    b = lattice[np.ix_(i1, i0)] * (1 - f)[None, :] + lattice[np.ix_(i1, i1)] * f[None, :]
    return a * (1 - f)[:, None] + b * f[:, None]


def fbm(seed, octaves, size=RES):
    rng = np.random.default_rng(seed)
    n = sum(octave(rng, c, size) * w for c, w in octaves)
    return (n - n.mean()) / n.std()


def smooth(x, lo, hi):
    t = np.clip((x - lo) / (hi - lo), 0, 1)
    return t * t * (3 - 2 * t)


def drip(mask, length):
    """Streaks running down from a mask (rows run down), wrapping so the map still tiles."""
    acc = np.zeros_like(mask)
    for i in range(length):
        acc = np.maximum(acc, np.roll(mask, i, axis=0) * (1 - i / length))
    return acc


def normal_from_height(h, strength):
    """A tangent-space normal map (OpenGL, green up) from a height field; rows run down."""
    dx = (np.roll(h, -1, axis=1) - np.roll(h, 1, axis=1)) / 2
    dy = -(np.roll(h, -1, axis=0) - np.roll(h, 1, axis=0)) / 2
    nx, ny = -dx * strength, -dy * strength
    nz = np.ones_like(h)
    ln = np.sqrt(nx * nx + ny * ny + nz * nz)
    return np.stack([(nx / ln + 1) / 2, (ny / ln + 1) / 2, (nz / ln + 1) / 2], -1) * 255


def save(name, colour, height, strength, alpha=None):
    os.makedirs(OUT, exist_ok=True)
    rgb = np.clip(colour, 0, 255).astype(np.uint8)
    img = Image.fromarray(rgb) if alpha is None else Image.fromarray(np.dstack([rgb, np.clip(alpha * 255, 0, 255).astype(np.uint8)]))
    img.save(os.path.join(OUT, name + "_BaseColor.png"))
    Image.fromarray(np.clip(normal_from_height(height, strength), 0, 255).astype(np.uint8)).save(os.path.join(OUT, name + "_Normal.png"))
    print(name, "written")


def worn_steel(base, seed, chips, rust_amount):
    """Painted steel the way the generated models wear it: a mottled coat, small chips
    to bare metal with a dark rim, rust at the worst of them running down."""
    mottle = fbm(seed, [(4, 1.0), (8, 0.6), (16, 0.4), (32, 0.25), (64, 0.15)])
    fine = fbm(seed + 1, [(64, 1.0), (128, 0.7), (256, 0.5)])
    coarse = fbm(seed + 2, [(3, 1.0), (6, 0.5), (12, 0.25)])
    col = base[None, None, :] * (1 + 0.16 * mottle)[..., None]
    # Flaking: wider patches where the top coat has gone to a lighter undercoat, as
    # on the hull and the tower.
    patches = fbm(seed + 3, [(6, 1.0), (12, 0.6), (24, 0.4), (48, 0.3), (96, 0.2)])
    flake = smooth(patches + 0.3 * fine, 1.55 - chips, 1.75 - chips)
    under = BARE * 0.8
    col = col * (1 - 0.85 * flake[..., None]) + under[None, None, :] * (1 + 0.12 * mottle)[..., None] * 0.85 * flake[..., None]
    # Grime runs down it.
    dirt = drip(smooth(fbm(seed + 4, [(16, 1.0), (32, 0.6)]), 1.4, 2.2), 90)
    col = col * (1 - 0.22 * dirt[..., None])
    # Chips: where the fine noise peaks, more of them where the coarse field is worn.
    chip = smooth(fine + 0.5 * coarse, 2.3 - chips, 2.5 - chips)
    rim = smooth(fine + 0.5 * coarse, 1.9 - chips, 2.3 - chips) - chip
    col = col * (1 - 0.35 * rim[..., None])
    col = col * (1 - chip[..., None]) + BARE[None, None, :] * (1 + 0.15 * mottle)[..., None] * chip[..., None]
    rust = smooth(coarse + 0.4 * fine, 2.2 - rust_amount, 2.8 - rust_amount)
    streak = drip(rust, 40) * 0.35
    r = np.clip(rust + streak, 0, 1)[..., None]
    col = col * (1 - 0.8 * r) + RUST[None, None, :] * (1 + 0.2 * mottle)[..., None] * 0.8 * r
    height = 1 - chip - 0.5 * rim - 0.4 * flake + 0.08 * mottle  # the chips and flakes are dents in the paint
    return col, height, mottle, fine


def kit_steel():
    col, h, _, _ = worn_steel(STEEL, 11, chips=0.0, rust_amount=0.0)
    save("KitSteel", col, h, 2.5)


def kit_bezel():
    col, h, _, _ = worn_steel(STEEL * 0.7, 21, chips=-0.2, rust_amount=-0.6)
    save("KitBezel", col, h, 2.0)


def kit_hazard():
    """The stripe: orange and black at 45 degrees, five pairs a metre, worn through to steel."""
    y, x = np.mgrid[0:RES, 0:RES] / RES
    pairs = 5
    t = ((x + y) * pairs) % 1.0
    wobble = fbm(31, [(32, 1.0), (64, 0.5)]) * 0.012
    edge = 0.004  # anti-aliasing, in stripe fractions
    stripe = smooth(t + wobble, 0.0, edge) * (1 - smooth(t + wobble, 0.5, 0.5 + edge))
    col, h, mottle, fine = worn_steel(INK * 1.05, 33, chips=0.15, rust_amount=-0.2)
    orange = ORANGE[None, None, :] * (1 + 0.12 * mottle)[..., None]
    # The orange coat chips too, back to the black under it.
    worn = smooth(fine + 0.6 * fbm(34, [(4, 1.0), (8, 0.5)]), 1.6, 1.9)
    paint = stripe * (1 - worn)
    col = col * (1 - paint[..., None]) + orange * paint[..., None]
    grime = smooth(fbm(35, [(4, 1.0), (8, 0.5), (16, 0.3)]), 0.4, 2.0)
    col = col * (1 - 0.35 * grime[..., None])
    save("KitHazard", col, h + 0.3 * paint, 2.5)


def drop_zone():
    """The floor mark: a hazard-striped border, corner ticks and the words, as a cutout."""
    W, H = 2048, 1707  # 2.4 x 2.0 m
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ppm = W / 2.4
    band = int(0.14 * ppm)
    # The stripes of the border.
    stripes = Image.new("L", (W, H), 0)
    sd = ImageDraw.Draw(stripes)
    step = int(0.2 * ppm)
    for k in range(-H, W + H, step):
        sd.polygon([(k, 0), (k + step // 2, 0), (k + step // 2 - H, H), (k - H, H)], fill=255)
    border = Image.new("L", (W, H), 0)
    bd = ImageDraw.Draw(border)
    bd.rectangle([0, 0, W - 1, H - 1], fill=255)
    bd.rectangle([band, band, W - 1 - band, H - 1 - band], fill=0)
    black = tuple(int(c) for c in INK) + (255,)
    orange = tuple(int(c) for c in ORANGE) + (255,)
    img.paste(Image.new("RGBA", (W, H), black), (0, 0), border)
    stripe_mask = Image.fromarray((np.asarray(border, float) * np.asarray(stripes, float) / 255).astype(np.uint8))
    img.paste(Image.new("RGBA", (W, H), orange), (0, 0), stripe_mask)
    # The words, across the middle, read by someone walking in through the doorway.
    f = ImageFont.truetype(FONT, int(0.34 * ppm))
    f.set_variation_by_name(b"Bold Condensed")
    cream = tuple(int(c) for c in CREAM) + (255,)
    for text, cy, size in [("DROP ZONE", 0.42, 0.34), ("SALVAGE ONLY", 0.66, 0.16)]:
        f = ImageFont.truetype(FONT, int(size * ppm))
        f.set_variation_by_name(b"Bold Condensed" if size > 0.2 else b"SemiBold Condensed")
        tw = d.textlength(text, font=f)
        bb = f.getbbox(text)
        d.text(((W - tw) / 2, cy * H - (bb[1] + bb[3]) / 2), text, font=f, fill=cream)
    # Worn: the paint lost at the scuffs, the edges broken, the colours dulled with grime.
    a = np.asarray(img, float)
    wear = fbm(41, [(8, 1.0), (16, 0.6), (32, 0.4), (64, 0.3), (128, 0.2)], size=2048)[:H, :W]
    fine = fbm(42, [(128, 1.0), (256, 0.6)], size=2048)[:H, :W]
    keep = 1 - smooth(wear * 0.7 + fine * 0.6, 1.3, 1.6)
    a[..., 3] *= keep
    a[..., :3] *= (1 + 0.08 * wear)[..., None] * (1 - 0.25 * smooth(wear, 0.5, 2.0))[..., None]
    os.makedirs(OUT, exist_ok=True)
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(os.path.join(OUT, "StorageDropZone_BaseColor.png"))
    print("StorageDropZone written")


if __name__ == "__main__":
    kit_steel()
    kit_bezel()
    kit_hazard()
    drop_zone()
