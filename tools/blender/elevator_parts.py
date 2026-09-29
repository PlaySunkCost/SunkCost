"""The eight elevator models (Dan's round glass car and shaft, 28 September 2026):
the extra Blender steps each one needs on top of prepare_ship_part.py, in the order
the readiness check tested end to end (the numbers they produce: docs/ELEVATOR_LOOK.md).

prepare_ship_part.py calls run() for these parts instead of its generic fit: each
recipe puts the raw download into the game's frame itself (a car frame, a bend, a
fit about the part's own axis), cuts what the game replaces or needs open, decimates,
bakes with the glass and trim slots kept, and leaves the objects to export.

Frames: Blender Z up, the model's front (the doorway, the screen) facing -Y; the
export turns that into Unity +Z. Bearings `rel` are counter-clockwise from the front.
Every recipe prints `ELEV_RESULT {json}`: the numbers the Unity side is built on.
"""
import json
import math

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

PARTS = {"ElevatorCar", "CabinDoor", "CarPanel", "TubeSection", "TubeFoot", "TopCollar", "CabinHousing", "GateLeaf"}

# Slots that keep their own material through the bake and the export's rename:
# Unity gives each its own material by index (ShipModelSetup).
KEEP = {"DeckPlate", "CarGlass", "CarLight", "CabinDoorGlass", "GateLeaf_Glass", "FootTrim"}


# ---- helpers --------------------------------------------------------------------

def arrays(me):
    n = len(me.polygons)
    fc = np.empty(n * 3); me.polygons.foreach_get("center", fc); fc = fc.reshape(-1, 3)
    fn = np.empty(n * 3); me.polygons.foreach_get("normal", fn); fn = fn.reshape(-1, 3)
    fa = np.empty(n); me.polygons.foreach_get("area", fa)
    vc = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", vc); vc = vc.reshape(-1, 3)
    mi = np.empty(n, dtype=np.int64); me.polygons.foreach_get("material_index", mi)
    return fc, fn, fa, vc, mi


def verts(me):
    v = np.empty(len(me.vertices) * 3); me.vertices.foreach_get("co", v)
    return v.reshape(-1, 3)


def set_verts(me, v):
    me.vertices.foreach_set("co", np.ascontiguousarray(v, dtype=np.float64).ravel())
    me.update()


def active(ob):
    if bpy.context.object is not None and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    ob.select_set(True)
    bpy.context.view_layer.objects.active = ob


def high_copy(mesh):
    high = mesh.copy()
    high.data = mesh.data.copy()
    high.name = mesh.name + "_high"
    bpy.context.collection.objects.link(high)
    return high


def decimate(mesh, budget, res):
    f0 = len(mesh.data.polygons)
    if f0 > budget:
        active(mesh)
        mod = mesh.modifiers.new("Decimate", "DECIMATE")
        mod.ratio = budget / f0
        mod.use_collapse_triangulate = True
        bpy.ops.object.modifier_apply(modifier="Decimate")
    res["decimated"] = [f0, len(mesh.data.polygons)]
    print("decimated %d -> %d faces" % (f0, len(mesh.data.polygons)))


def delete_faces(me, mask):
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in np.nonzero(mask)[0]], context="FACES")
    bm.to_mesh(me); bm.free(); me.update()
    return int(mask.sum())


def fit_uniform(mesh, target, axis=None):
    """One scale for every axis, no quarter turn: centred on the bbox (or on a given
    raw axis), the bottom on z 0. Returns k."""
    me = mesh.data
    v = verts(me)
    lo, hi = v.min(0), v.max(0)
    k = min(t / s for t, s in zip(target, hi - lo))
    cx, cy = ((lo + hi) / 2)[:2] if axis is None else axis
    set_verts(me, np.stack([(v[:, 0] - cx) * k, (v[:, 1] - cy) * k, (v[:, 2] - lo[2]) * k], 1))
    return float(k)


def size_of(me):
    v = verts(me)
    return (v.max(0) - v.min(0)).round(4).tolist()


def base_slot(me, name):
    """Exactly one material slot, the part's own (the bake replaces it); extra slots added after."""
    while len(me.materials) > 1:
        me.materials.pop()
    if len(me.materials) == 0:
        new_material(me, name)


def new_material(me, name, nodes=True):
    m = bpy.data.materials.new(name)
    m.use_nodes = nodes
    me.materials.append(m)
    return len(me.materials) - 1


def bake(high, mesh, psp, part, size, keep=True):
    psp["bake_from"](high, mesh, part, size, 0.3, KEEP if keep else ())
    img = bpy.data.images.get(part + "_BaseColor")
    return img


def texel_colours(ob, img):
    me = ob.data
    uv = me.uv_layers.active.data
    n = len(me.polygons)
    ls = np.empty(n, dtype=np.int64); me.polygons.foreach_get("loop_start", ls)
    lt = np.empty(n, dtype=np.int64); me.polygons.foreach_get("loop_total", lt)
    uvs = np.empty(len(me.loops) * 2); uv.foreach_get("uv", uvs); uvs = uvs.reshape(-1, 2)
    W, H = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    out = np.zeros((n, 3), dtype=np.float32)
    for i in range(n):
        c = uvs[ls[i]:ls[i] + lt[i]].mean(0)
        out[i] = px[min(H - 1, max(0, int(c[1] * H))), min(W - 1, max(0, int(c[0] * W))), :3]
    return out, uvs, ls, lt


def black_share(ob, img):
    col, _, _, _ = texel_colours(ob, img)
    lum = col.max(1)
    return round(float((lum < 0.02).mean()), 3)


# ---- 1. the car ---------------------------------------------------------------------

def car(mesh, target, budget, psp, res):
    me = mesh.data
    V = verts(me)
    lo, hi = V.min(0), V.max(0); c = (lo + hi) / 2
    K = min(t / s for t, s in zip(target, hi - lo))
    X = (V[:, 0] - c[0]) * K; Y = (V[:, 1] - c[1]) * K; Z = (V[:, 2] - lo[2]) * K
    # car_frame: square the doorway to -Y, stretch the posts, the floor top on z 0.10
    t = math.radians(-0.94)
    X, Y = X * math.cos(t) - Y * math.sin(t), X * math.sin(t) + Y * math.cos(t)
    h = Z - 0.3553
    h = np.where(h < 0.931, h, np.where(h <= 2.009, 0.931 + (h - 0.931) * 1.7087, h + 0.764))
    set_verts(me, np.stack([X, Y, h + 0.10], 1))
    del V, X, Y, Z, h
    res["k"] = round(float(K), 5)
    high = high_copy(mesh)
    # car_cut: the floor grating and the roof annulus go (caps replace them)
    fc, fn, fa, vc, mi = arrays(me)
    cr = np.hypot(fc[:, 0], fc[:, 1])
    sel = ((np.abs(fc[:, 2] - 0.10) <= 0.035) & (cr <= 2.06)) | ((fc[:, 2] < 0.065) & (cr < 2.06)) | ((fc[:, 2] >= 3.385) & (fc[:, 2] <= 3.43) & (cr >= 0.70) & (cr <= 1.93))
    res["car_cut_faces"] = int(sel.sum())
    del fc, fn, fa, vc, mi, cr
    me.vertices.foreach_set("select", np.zeros(len(me.vertices), bool)); me.edges.foreach_set("select", np.zeros(len(me.edges), bool))
    me.polygons.foreach_set("select", sel)
    active(mesh)
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_mode(type="FACE"); bpy.ops.mesh.delete(type="FACE")
    bpy.ops.mesh.select_all(action="SELECT"); bpy.ops.mesh.delete_loose(); bpy.ops.object.mode_set(mode="OBJECT")
    decimate(mesh, budget, res)
    # car_caps: the floor (both faces) and the roof (both faces)
    bm = bmesh.new(); bm.from_mesh(me)

    def disc(r, z, up):
        vs = [bm.verts.new((r * math.cos(2 * math.pi * i / 64), r * math.sin(2 * math.pi * i / 64), z)) for i in range(64)]
        f = bm.faces.new(vs if up else vs[::-1]); f.normal_update()
        if (f.normal.z > 0) != up:
            f.normal_flip()
    disc(2.07, 0.100, True); disc(2.07, 0.099, False); disc(1.93, 3.40, False); disc(1.93, 3.42, True)
    bm.to_mesh(me); bm.free(); me.update()
    v = verts(me); vr = np.hypot(v[:, 0], v[:, 1])
    res["rmax"] = round(float(vr.max()), 4)
    if vr.max() > 2.405:
        raise SystemExit("car: a vertex at r %.4f, outside 2.405 (the door leaves' band)" % vr.max())
    res["z"] = [round(float(v[:, 2].min()), 4), round(float(v[:, 2].max()), 4)]
    psp["shade_smooth"](mesh)
    res["triangles"] = len(me.polygons)
    psp["unwrap_fresh"](mesh)
    # car_slots: the two dark glass slabs and the light ring
    fc, fn, fa, vc, mi = arrays(me)
    cr = np.hypot(fc[:, 0], fc[:, 1]); rel = (np.degrees(np.arctan2(fc[:, 1], fc[:, 0])) + 90 + 180) % 360 - 180
    nrr = (fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / np.maximum(cr, 1e-9)
    glass = (((rel >= 49.5) & (rel <= 61)) | ((rel >= -62.5) & (rel <= -51))) & (cr >= 2.19) & (cr <= 2.24) & (fc[:, 2] >= 0.30) & (fc[:, 2] <= 3.11) & (np.abs(nrr) > 0.8)
    light = (cr >= 0.42) & (cr <= 0.62) & (fc[:, 2] >= 3.20) & (fc[:, 2] <= 3.33) & (fn[:, 2] < -0.3)
    base_slot(me, "ElevatorCar")
    new_material(me, "CarGlass"); new_material(me, "CarLight")
    mi = np.zeros(len(me.polygons), dtype=np.int32); mi[glass] = 1; mi[light] = 2
    me.polygons.foreach_set("material_index", mi); me.update()
    res["slot_faces"] = {"CarGlass": int(glass.sum()), "CarLight": int(light.sum())}
    img = bake(high, mesh, psp, "ElevatorCar", 2048)
    res["bake_black"] = black_share(mesh, img)
    # the posts (capsules in Unity, ElevatorLook.Posts) and the six roof outlets, re-measured
    vc = verts(me); vb = np.degrees(np.arctan2(vc[:, 1], vc[:, 0])); vrr = np.hypot(vc[:, 0], vc[:, 1])
    posts = {}
    for relp in (23.56, -23.56, -56.94, 55.06, 98.56, 149.18, -151.19, -100.32):
        b = relp - 90
        d = (vb - b + 180) % 360 - 180
        m = (np.abs(d) < 6) & (vc[:, 2] > 1.2) & (vc[:, 2] < 2.4) & (vrr > 1.95) & (vrr < 2.42)
        if m.sum() < 6:
            posts["%.2f" % relp] = None
            continue
        pts = vc[m]; r_ = vrr[m]
        rin = np.percentile(r_, 2); core = r_ < rin + 0.25
        cx, cy = pts[core, 0].mean(), pts[core, 1].mean()
        posts["%.2f" % relp] = {"rel": round((math.degrees(math.atan2(cy, cx)) + 90 + 180) % 360 - 180, 2), "inner_r": round(float(rin), 3), "radial_depth": round(float(r_[core].max() - rin), 3)}
    res["posts"] = posts
    outlets = [(-0.770, -1.467, 2.924), (0.738, -1.487, 2.908), (-1.846, -0.002, 2.975), (1.846, -0.064, 2.975), (-0.928, 1.470, 3.051), (0.948, 1.439, 3.050)]
    res["outlet_nearest_vertex_m"] = [round(float(np.min(np.linalg.norm(vc - np.array(o), axis=1))), 3) for o in outlets]


# ---- 2. the car door leaf -------------------------------------------------------------

def door(mesh, target, budget, psp, res):
    me = mesh.data
    V = verts(me)
    phi = np.arctan2(V[:, 0] + 0.0002, 0.5887 - V[:, 1]); s = 0.720 * phi
    d = np.hypot(V[:, 0] + 0.0002, V[:, 1] - 0.5887) - 0.720
    th = (s - 0.00045) * 1.514 / 2.40
    r = 2.45 + 0.27 * d
    zp = (V[:, 2] + 0.9517) * 1.735
    set_verts(me, np.column_stack([r * np.sin(th), -r * np.cos(th), zp]))
    b = np.degrees(th)
    res["bend"] = {"r": [round(float(r.min()), 4), round(float(r.max()), 4)], "bearing": [round(float(b.min()), 3), round(float(b.max()), 3)], "z": [round(float(zp.min()), 4), round(float(zp.max()), 4)]}
    if not (r.min() >= 2.408 and r.max() <= 2.464 and b.min() >= -19.55 and b.max() <= 19.55 and zp.min() >= -0.001 and zp.max() <= 3.301):
        raise SystemExit("door: the bend is out of its band %s" % res["bend"])
    high = high_copy(mesh)
    decimate(mesh, budget, res)
    B_GLASS, B_BACK = 13.008, 10.12
    ZB = [0.435, 0.452, 1.450, 1.468, 1.525, 1.543, 2.882, 2.899]
    GL_Z = [(0.435, 1.468), (1.525, 2.899)]
    CUT_Z = [(0.452, 1.450), (1.543, 2.882)]
    R_GLASS = (2.4365, 2.4460); R_BACK = 2.4365; R_SLEEVE = 2.4372
    bm = bmesh.new(); bm.from_mesh(me)

    def region():
        fs = [f for f in bm.faces if abs(math.degrees(math.atan2(f.calc_center_median().x, -f.calc_center_median().y))) < 16.0 and 0.35 <= f.calc_center_median().z <= 3.0]
        vs = set(v for f in fs for v in f.verts); es = set(e for f in fs for e in f.edges)
        return list(vs) + list(es) + fs
    for bdeg in (-B_GLASS, -B_BACK, B_BACK, B_GLASS):
        t = math.radians(bdeg)
        bmesh.ops.bisect_plane(bm, geom=region(), dist=1e-5, plane_co=Vector((0, 0, 0)), plane_no=Vector((math.cos(t), math.sin(t), 0.0)))
    for zc in ZB:
        bmesh.ops.bisect_plane(bm, geom=region(), dist=1e-5, plane_co=Vector((0, 0, zc)), plane_no=Vector((0, 0, 1)))

    def inz(z, spans, eps=1e-4):
        return any(a + eps < z < b_ - eps for a, b_ in spans)
    glass = 0
    for f in bm.faces:
        c = f.calc_center_median(); rr = math.hypot(c.x, c.y); bb = math.degrees(math.atan2(c.x, -c.y))
        if f.normal.dot(Vector((c.x, c.y, 0)).normalized()) > 0.85 and R_GLASS[0] < rr < R_GLASS[1] and abs(bb) < B_GLASS and inz(c.z, GL_Z):
            f.material_index = 1; glass += 1
    kill = []
    for f in bm.faces:
        c = f.calc_center_median(); rr = math.hypot(c.x, c.y); bb = math.degrees(math.atan2(c.x, -c.y))
        if f.material_index == 0 and rr < R_BACK and abs(bb) < B_BACK - 1e-4 and inz(c.z, CUT_Z):
            kill.append(f)
    res["glass_faces"] = glass; res["back_cut_faces"] = len(kill)
    if glass == 0:
        raise SystemExit("door: no glass faces")
    bmesh.ops.delete(bm, geom=kill, context="FACES")
    bnd = [e for e in bm.edges if e.is_boundary]
    ext = bmesh.ops.extrude_edge_only(bm, edges=bnd)
    for vtx in [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]:
        bb = math.atan2(vtx.co.x, -vtx.co.y)
        vtx.co.x, vtx.co.y = R_SLEEVE * math.sin(bb), -R_SLEEVE * math.cos(bb)
    zedges = sorted(z for span in CUT_Z for z in span)
    for f in [f for f in ext["geom"] if isinstance(f, bmesh.types.BMFace)]:
        c = f.calc_center_median(); bb = math.atan2(c.x, -c.y)
        ds = {"s-": abs(math.degrees(bb) + B_BACK) * math.radians(1) * 2.43, "s+": abs(math.degrees(bb) - B_BACK) * math.radians(1) * 2.43}
        for i_, zc in enumerate(zedges):
            ds["z%d" % i_] = abs(c.z - zc)
        k = min(ds, key=ds.get)
        if k == "s-":
            want = Vector((math.cos(bb), math.sin(bb), 0))
        elif k == "s+":
            want = -Vector((math.cos(bb), math.sin(bb), 0))
        else:
            want = Vector((0, 0, 1)) if int(k[1:]) % 2 == 0 else Vector((0, 0, -1))
        if f.normal.dot(want) < 0:
            f.normal_flip()
        f.material_index = 0
    bm.to_mesh(me); bm.free(); me.update()
    base_slot(me, "CabinDoor")
    new_material(me, "CabinDoorGlass")
    # nothing of the back plate left behind the window
    fc, fn, fa, vc, mi = arrays(me)
    fr = np.hypot(fc[:, 0], fc[:, 1]); fb = np.degrees(np.arctan2(fc[:, 0], -fc[:, 1]))
    inrect = (np.abs(fb) < B_BACK - 0.05) & np.array([inz(z, CUT_Z, 0.005) for z in fc[:, 2]])
    res["metal_left_behind_glass"] = int(np.sum(inrect & (mi == 0) & (fr < R_BACK - 0.0005)))
    res["glass_area_m2"] = round(float(fa[mi == 1].sum()), 3)
    psp["shade_smooth"](mesh)
    res["triangles"] = len(me.polygons)
    if res["triangles"] > 12500:
        raise SystemExit("door: %d triangles over 12500" % res["triangles"])
    psp["unwrap_fresh"](mesh)
    img = bake(high, mesh, psp, "CabinDoor", 2048)
    res["bake_black"] = black_share(mesh, img)
    res["size"] = size_of(me)


# ---- 3. the car panel -----------------------------------------------------------------

def panel(mesh, target, budget, psp, res):
    me = mesh.data
    res["k"] = round(fit_uniform(mesh, target), 5)
    res["fitted"] = size_of(me)
    high = high_copy(mesh)
    fc, fn, fa, vc, mi = arrays(me)
    rk = np.hypot(fc[:, 0] - 0.000175, fc[:, 2] - 0.187)
    cap = (rk < 0.05175) & (fc[:, 1] < -0.16779)
    rim = (rk >= 0.05175) & (rk < 0.06825) & (fc[:, 1] < -0.15129)
    dx, dy = fc[:, 0] - 0.00485, fc[:, 1] - 0.41151; rs = np.hypot(dx, dy)
    scr = (np.abs(rs - 0.54857) < 0.005) & ((fn[:, 0] * dx + fn[:, 1] * dy) / rs > 0.8) & (fc[:, 0] >= -0.24372) & (fc[:, 0] <= 0.16428) & (fc[:, 2] >= 0.32195) & (fc[:, 2] <= 0.56395)
    gx, gy = fc[:, 0] - 0.24657, fc[:, 1] + 0.0602; rg = np.hypot(gx, gy)
    gau = (np.abs(rg - 0.02516) < 0.003) & ((fn[:, 0] * gx + fn[:, 1] * gy) / rg > 0.7) & (fc[:, 2] >= 0.32795) & (fc[:, 2] <= 0.55045) & (fc[:, 1] < -0.0552)
    base_slot(me, "CarPanel")
    for nm in ("CarPanel_Screen", "CarPanel_Gauge", "CarPanel_Cap", "CarPanel_Rim"):
        new_material(me, nm)
    mi = np.zeros(len(me.polygons), dtype=np.int32); mi[scr] = 1; mi[gau] = 2; mi[cap] = 3; mi[rim] = 4
    me.polygons.foreach_set("material_index", mi); me.update()
    res["tag_full_res"] = dict(screen=int(scr.sum()), gauge=int(gau.sum()), cap=int(cap.sum()), rim=int(rim.sum()))
    decimate(mesh, budget, res)
    mi = np.empty(len(me.polygons), dtype=np.int64); me.polygons.foreach_get("material_index", mi)
    res["tag_decimated"] = np.bincount(mi, minlength=5).tolist()
    if min(res["tag_decimated"]) == 0:
        raise SystemExit("panel: a region lost every face in the decimation %s" % res["tag_decimated"])
    psp["shade_smooth"](mesh)
    psp["unwrap_fresh"](mesh)
    img = bake(high, mesh, psp, "CarPanel", 1024, keep=False)  # every slot takes the one baked material, indices kept
    # panel_finish 1: the barrel back cut flat, filled, the fill on a dark side face's texels
    W, H = img.size; px = np.array(img.pixels[:], dtype=np.float32).reshape(H, W, 4)
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0.0587, 0), plane_no=(0, 1, 0), clear_outer=True)
    bnd = [e for e in bm.edges if e.is_boundary]
    fill = bmesh.ops.holes_fill(bm, edges=bnd, sides=0)
    tri = bmesh.ops.triangulate(bm, faces=fill["faces"])
    uvl = bm.loops.layers.uv.active
    best, bestl = None, 9.0
    for f in bm.faces:
        if abs(f.normal.x) > 0.9 and f.material_index == 0:
            u = sum((l[uvl].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
            l_ = float(px[min(H - 1, int(u.y * H)), min(W - 1, int(u.x * W)), :3].mean())
            if 0.03 < l_ < bestl:
                best, bestl = u.copy(), l_
    for f in tri["faces"]:
        f.material_index = 0
        for l in f.loops:
            l[uvl].uv = best
    res["back_open_edges_after_fill"] = sum(1 for e in bm.edges if e.is_boundary)
    bm.to_mesh(me); bm.free(); me.update()
    # the pipe on the top (Unity continues it up to the roof): where it stands
    v = verts(me); top = v[v[:, 2] > v[:, 2].max() - 0.02]
    res["pipe_top_unity_local"] = [round(float(-top[:, 0].mean()), 4), round(float(v[:, 2].max()), 4), round(float(-top[:, 1].mean()), 4)]
    # panel_finish 2: split by selection on the slot, named, one slot each
    names = {1: "CarPanel_Screen", 2: "CarPanel_Gauge", 3: "CarPanel_Cap", 4: "CarPanel_Rim"}
    parts = {}
    for s_, nm in names.items():
        active(mesh)
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="DESELECT"); bpy.ops.object.mode_set(mode="OBJECT")
        for p in me.polygons:
            p.select = (p.material_index == s_)
        bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.separate(type="SELECTED"); bpy.ops.object.mode_set(mode="OBJECT")
        new = [o for o in bpy.context.selected_objects if o is not mesh][0]
        new.name = new.data.name = nm
        parts[nm] = new
    parts["CarPanel"] = mesh

    def extrude_loop(o, pick, dy):
        b = bmesh.new(); b.from_mesh(o.data)
        loop = [e for e in b.edges if e.is_boundary and pick(e)]
        ext = bmesh.ops.extrude_edge_only(b, edges=loop)
        for vv in [g for g in ext["geom"] if isinstance(g, bmesh.types.BMVert)]:
            vv.co.y += dy
        ne = [g for g in ext["geom"] if isinstance(g, bmesh.types.BMEdge) and g.is_boundary]
        f = bmesh.ops.holes_fill(b, edges=ne, sides=0)
        b.to_mesh(o.data); b.free(); o.data.update()
        return len(loop), len(f["faces"])
    mid_r = lambda e: math.hypot((e.verts[0].co.x + e.verts[1].co.x) / 2 - 0.000175, (e.verts[0].co.z + e.verts[1].co.z) / 2 - 0.187)
    res["rim_socket(loop,fill)"] = extrude_loop(parts["CarPanel_Rim"], lambda e: mid_r(e) < 0.056, 0.025)
    res["cap_back(loop,fill)"] = extrude_loop(parts["CarPanel_Cap"], lambda e: True, 0.012)
    cap_o = parts["CarPanel_Cap"]; org = Vector((0.000175, -0.17809, 0.187))
    for vv in cap_o.data.vertices:
        vv.co -= org
    cap_o.location = org

    def set_uv(o, fu, fv):
        d_ = o.data; lay = d_.uv_layers.active.data
        co_ = verts(d_)
        lv = np.empty(len(d_.loops), dtype=np.int64); d_.loops.foreach_get("vertex_index", lv)
        u = fu(co_[lv]); vv_ = fv(co_[lv])
        lay.foreach_set("uv", np.column_stack([u, vv_]).ravel())
    sc = parts["CarPanel_Screen"]
    th = lambda p: np.arctan2(p[:, 0] - 0.00485, 0.41151 - p[:, 1])
    scv = verts(sc.data); t0, t1 = th(scv).min(), th(scv).max(); z0, z1 = scv[:, 2].min(), scv[:, 2].max()
    set_uv(sc, lambda p: (th(p) - t0) / (t1 - t0), lambda p: (p[:, 2] - z0) / (z1 - z0))
    res["screen"] = {"theta_deg": [round(math.degrees(t0), 2), round(math.degrees(t1), 2)], "z": [round(float(z0), 4), round(float(z1), 4)]}
    ga = parts["CarPanel_Gauge"]
    tg = lambda p: np.arctan2(p[:, 0] - 0.24657, -(p[:, 1] + 0.0602))
    gav = verts(ga.data); g0, g1 = tg(gav).min(), tg(gav).max()
    set_uv(ga, lambda p: (tg(p) - g0) / (g1 - g0), lambda p: (p[:, 2] - 0.32795) / 0.2225)
    # one slot per part: every face on slot 0, the rest dropped (they all hold the baked material)
    for o in parts.values():
        o.data.polygons.foreach_set("material_index", np.zeros(len(o.data.polygons), dtype=np.int32))
        while len(o.data.materials) > 1:
            o.data.materials.pop()
        o.data.update()
    res["parts"] = {nm: len(o.data.polygons) for nm, o in parts.items()}


# ---- 4. the tube section and 6. the top collar: the table's fit, a bake ---------------

def plain(mesh, target, budget, psp, res):
    me = mesh.data
    res["k"] = round(fit_uniform(mesh, target), 5)
    res["fitted"] = size_of(me)
    high = high_copy(mesh)
    decimate(mesh, budget, res)
    psp["shade_smooth"](mesh)
    res["triangles"] = len(me.polygons)
    psp["unwrap_fresh"](mesh)
    base_slot(me, mesh.name)
    img = bake(high, mesh, psp, mesh.name, 2048)
    res["bake_black"] = black_share(mesh, img)
    v = verts(me); vr = np.hypot(v[:, 0], v[:, 1])
    res["r"] = [round(float(vr.min()), 4), round(float(vr.max()), 4)]


# ---- 5. the tube foot (option A': the threshold on the sand, docs/ELEVATOR_LOOK.md A2) -------------

FOOT = {"axis": (-0.0118, 0.127), "floor": -0.2486, "yaw": -2.25, "k": 5.40, "pit_r": 2.56}
# The wall posts (phi: the fitted frame's bearing from +X) and the band of the Meshy glass pane
# kept beside each one (the glass cut's edges are split straight by foot_split_glass_edges).
FOOT_POSTS = (-143.25, -46.25, 3.25, 51.25, 95.25, 173.75)
FOOT_POST_HALF = 2.0
# The doorway's sides (DIVE-GATE-CLOSEUP-SHARDS): Meshy's metal is cleared this far past the doorway's
# half angle up to the plinth tops, so nothing pokes through the jamb faces, and the left portal post's
# remnant (the doorway cut went through it) goes from just above the plinth top.
FOOT_JAMB_CLEAR = 0.25
FOOT_PLINTH_TOP = 0.87
FOOT_PORTAL_FLOOR = 0.95


def split_along(data, pick, value, levels):
    """Split the picked faces along the iso-lines value(co) == level: every edge that crosses a
    level gets a vertex there, joined across its face. A cut by face centres afterwards follows the
    iso-line (straight where the surface is) instead of the triangles' zigzag. Returns the count."""
    bm = bmesh.new(); bm.from_mesh(data); bm.faces.ensure_lookup_table()
    faces = [bm.faces[i] for i in np.nonzero(pick)[0]]
    print("split_along: %d faces, levels %s" % (len(faces), levels), flush=True)
    made = 0
    for level in levels:
        live = [f for f in faces if f.is_valid]
        new = set()
        for e in {e for f in live for e in f.edges}:
            va, vb = value(e.verts[0].co), value(e.verts[1].co)
            if (va - level) * (vb - level) < 0:
                t = (level - va) / (vb - va)
                if 1e-4 < t < 1.0 - 1e-4:
                    new.add(bmesh.utils.edge_split(e, e.verts[0], t)[1])
        if new:   # one call: an operator's cost is the whole mesh, not the faces it splits
            bmesh.ops.connect_verts(bm, verts=list(new))
        faces = list(dict.fromkeys([f for f in live if f.is_valid] + [f for v in new for f in v.link_faces]))
        made += len(new)
    bm.to_mesh(data); bm.free(); data.update()
    return made


def foot_split_glass_edges(data, res):
    """The glass cut below picks faces by their centres. Meshy's pane and the frame round it are one
    sheet of long thin triangles, so that cut left the frame's edge as a sawtooth with 5-10 cm teeth
    down the posts and under the top ring, which a diver pressed to the car's glass sees at the bottom
    stop (DIVE-SLAB-EDGE). Split the sheet along the cut's own limits first (the radius 2.93 where
    the frame turns into the pane, the pane's top and bottom, the posts' +-2 deg): every piece's
    centre then lies on its own side and each kept edge is a straight line. Only the sheet's coarse
    triangles (over COARSE m2) are split: the sawtooth is theirs, and the dense detail's own error is
    under a millimetre."""
    COARSE = 2e-4
    fc, fn, fa, _, _ = arrays(data)
    rho = np.hypot(fc[:, 0], fc[:, 1]); phi = np.degrees(np.arctan2(fc[:, 1], fc[:, 0]))
    nrad = (fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / np.maximum(rho, 1e-9)
    sheet = (rho >= 2.80) & (rho <= 3.16) & (np.abs(nrad) > 0.5) & (fa > COARSE) & (fc[:, 2] > 0.60) & (fc[:, 2] < 3.80)
    res["glass_edge_splits"] = {"rho": split_along(data, sheet, lambda co: math.hypot(co.x, co.y), (2.93, 3.10))}
    fc, fn, fa, _, _ = arrays(data)
    rho = np.hypot(fc[:, 0], fc[:, 1]); phi = np.degrees(np.arctan2(fc[:, 1], fc[:, 0]))
    nrad = (fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / np.maximum(rho, 1e-9)
    sheet = (rho >= 2.88) & (rho <= 3.13) & (np.abs(nrad) > 0.5) & (fa > COARSE) & (fc[:, 2] > 0.60) & (fc[:, 2] < 3.80)
    res["glass_edge_splits"]["z"] = split_along(data, sheet, lambda co: co.z, (0.84, 3.60))
    posts = 0
    for p in FOOT_POSTS:
        fc, fn, fa, _, _ = arrays(data)
        rho = np.hypot(fc[:, 0], fc[:, 1]); phi = np.degrees(np.arctan2(fc[:, 1], fc[:, 0]))
        nrad = (fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / np.maximum(rho, 1e-9)
        near = (rho >= 2.88) & (rho <= 3.13) & (np.abs(nrad) > 0.5) & (fa > COARSE) & (fc[:, 2] > 0.80) & (fc[:, 2] < 3.64) & (np.abs((phi - p + 180) % 360 - 180) < 6.0)
        posts += split_along(data, near, lambda co, p=p: (math.degrees(math.atan2(co.y, co.x)) - p + 180) % 360 - 180, (-FOOT_POST_HALF, FOOT_POST_HALF))
    res["glass_edge_splits"]["posts"] = posts


def foot_split_door_edges(data, res):
    """The doorway's own cuts, picked by face centre too, left torn edges beside the jambs: teeth through
    the jamb faces, a sawtooth along the right pocket's shell at z 0.90 and r 3.85, and a torn stub under
    the left portal post's cut. Split the raw faces along those limits first (every face near one, not
    only the coarse ones: here the teeth are what a diver at the gate sees)."""
    HALF = math.degrees(math.asin(1.2 / 3.0)); J = HALF + FOOT_JAMB_CLEAR

    def near():
        fc, _, _, _, _ = arrays(data)
        rho = np.hypot(fc[:, 0], fc[:, 1]); dphi = (np.degrees(np.arctan2(fc[:, 1], fc[:, 0])) + 90 + 180) % 360 - 180
        return rho, dphi, fc[:, 2]

    def dphi_of(co):
        return (math.degrees(math.atan2(co.y, co.x)) + 90 + 180) % 360 - 180
    out = {}
    rho, dphi, z = near()
    out["jambs"] = split_along(data, (np.abs(np.abs(dphi) - J) < 2.0) & (rho >= 2.90) & (rho <= 4.10) & (z >= -0.20) & (z <= FOOT_PLINTH_TOP + 0.15), dphi_of, (-J, J))
    rho, dphi, z = near()
    out["slot_ends"] = split_along(data, (np.abs(np.abs(dphi) - 49.5) < 2.0) & (rho >= 2.60) & (rho <= 3.55) & (z >= 0.0) & (z <= 3.95), dphi_of, (-49.5, 49.5))
    rho, dphi, z = near()
    out["pocket_z"] = split_along(data, (dphi >= HALF - 1.0) & (dphi <= 41.0) & (rho >= 3.00) & (rho <= 4.30) & (np.abs(z - 0.90) < 0.10), lambda co: co.z, (0.90,))
    rho, dphi, z = near()
    out["pocket_r"] = split_along(data, (dphi >= HALF - 1.0) & (dphi <= 41.0) & (np.abs(rho - 3.85) < 0.10) & (z >= 0.0) & (z <= 2.40), lambda co: math.hypot(co.x, co.y), (3.85,))
    rho, dphi, z = near()
    out["portal"] = split_along(data, (dphi >= -41.0) & (dphi <= -HALF + 1.0) & (rho >= 3.00) & (rho <= 3.55) & (np.abs(z - FOOT_PORTAL_FLOOR) < 0.08), lambda co: co.z, (FOOT_PORTAL_FLOOR,))
    res["door_edge_splits"] = out


# FOOT-FLAT-SLAB (Dan, 29 September 2026): the plinth's trim faces (the jambs, the slots' back walls and
# ends, the plinth tops, the right plinth's closed outer face) were flat cards wearing one small square of
# the foot's texels stretched over up to a square metre (10-23 texels a metre against the foot's 40), and
# the jambs stood 0.3 m proud of the plinth's own skin beside the doorway. They are now made before the
# unwrap, so they get their own islands at the foot's texel density, and the bake reads them from pieces
# of the plinth's real rust band (FOOT_BAND, raw Meshy geometry with its straps and bolts) laid just behind
# each face: the same colour and normal detail as the rest of the foot. The jambs follow the plinth's cut
# section instead of a rectangle to r 3.95.
FOOT_BAND = (0.47, 0.83)   # the plinth's plain rust band (z, m): rust plate and grey straps, above the pipes and grilles
FOOT_BAND_STARTS = (-160.0, 62.0, -140.0, 82.0, -120.0, 102.0, -100.0, 122.0, -80.0, 142.0, -62.0, 158.0, -175.0)
FOOT_PLINTH_GROUPS = ("jambL", "jambR", "slotwallL", "slotwallR", "slotendL", "slotendR", "topL", "topR", "outerR", "capR", "skirtR")
FOOT_JAMB_TOP_R = (3.30, 3.95)   # the jamb's outer edge stays inside the old rectangle (the guards never grow)


def foot_jamb_profiles(data, half):
    """The plinth's cut section at each jamb: the outermost vertex radius per height step next to the
    cleared edge (dphi +-(half + FOOT_JAMB_CLEAR)), a running max over neighbours so the jamb covers it.
    Returns the profiles [(z, r)] per side (-1 left, +1 right) and the raw section radii."""
    v = verts(data)
    r = np.hypot(v[:, 0], v[:, 1]); d = (np.degrees(np.arctan2(v[:, 1], v[:, 0])) + 90 + 180) % 360 - 180
    levels = [-0.10] + [round(0.05 * i, 2) for i in range(0, 18)] + [FOOT_PLINTH_TOP]
    out, sections = {}, {}
    for s in (-1, 1):
        near = (np.abs(d - s * (half + FOOT_JAMB_CLEAR)) < 0.6) & (r > 3.12)
        raw = []
        for zz in levels:
            m = near & (np.abs(v[:, 2] - zz) <= 0.03)
            raw.append(float(r[m].max()) if m.any() else None)
        known = [i for i, x in enumerate(raw) if x is not None]
        prof = []
        for i, x in enumerate(raw):
            if x is None and s > 0:   # the right pocket: no skin, the closed outer face (outerR) is the edge
                x = 3.87
            elif x is None:   # a step with no section vertex: the nearer known neighbours'
                lo = max([k for k in known if k < i], default=None); hi = min([k for k in known if k > i], default=None)
                x = max(raw[k] for k in (lo, hi) if k is not None) if known else FOOT_JAMB_TOP_R[1]
            prof.append(x)
        prof = [max(prof[max(i - 1, 0):i + 2]) + 0.01 for i in range(len(prof))]
        sections[s] = [None if x is None else round(x, 3) for x in raw]
        if s > 0:   # the right plinth: its closed outer face (outerR, r 3.88) stands above the grille
            prof = [max(p, 3.88) for p in prof]
        out[s] = [(zz, min(max(p, FOOT_JAMB_TOP_R[0]), FOOT_JAMB_TOP_R[1])) for zz, p in zip(levels, prof)]
    return out, sections


def foot_band_proxies(high, targets, res):
    """Pieces of the plinth's rust band (raw Meshy faces, their UVs and material kept) laid just behind
    each plinth trim face, for the bake to read: the band window's outermost surface 3 mm behind the face,
    its relief behind that. A target is (name, T, u0, u1, v0, v1): T(u, v, h) the point at (u, v) on the
    face, h metres along its normal (h < 0: behind it)."""
    me = high.data
    fc, _, _, _, _ = arrays(me)
    rho = np.hypot(fc[:, 0], fc[:, 1]); dphi = (np.degrees(np.arctan2(fc[:, 1], fc[:, 0])) + 90 + 180) % 360 - 180
    bm = bmesh.new(); bm.from_mesh(me); bm.faces.ensure_lookup_table()
    band_h = FOOT_BAND[1] - FOOT_BAND[0]
    k = 0; made = {}; M = 0.04
    for name, T, u0, u1, v0, v1 in targets:
        U0, U1, V0, V1 = u0 - M, u1 + M, v0 - M, v1 + M
        nu = max(1, math.ceil((U1 - U0) / 1.3)); nv = max(1, math.ceil((V1 - V0) / band_h))
        tu, tv = (U1 - U0) / nu, (V1 - V0) / nv
        # handedness: the band's (along, up, out) frame is right-handed; mirror along if the face's is not
        e = 1e-3; um, vm = (U0 + U1) / 2, (V0 + V1) / 2
        J = np.array([(T(um + e, vm, 0) - T(um, vm, 0)) / e, (T(um, vm + e, 0) - T(um, vm, 0)) / e, (T(um, vm, e) - T(um, vm, 0)) / e])
        flip = np.linalg.det(J) < 0
        count = 0
        for iu in range(nu):
            for iv in range(nv):
                p0 = FOOT_BAND_STARTS[k % len(FOOT_BAND_STARTS)]; k += 1
                z0 = FOOT_BAND[0]
                cand = (fc[:, 2] >= z0 - 0.02) & (fc[:, 2] <= z0 + tv + 0.02) & (rho >= 3.30) & (rho <= 4.30)
                cand &= (dphi >= p0 - 1.0) & (dphi <= p0 + math.degrees(tu / 3.6) + 1.0)
                if cand.sum() < 50:
                    raise SystemExit("foot: band window at %.0f deg is empty" % p0)
                r_top = float(np.percentile(rho[cand], 98))
                pick = np.nonzero(cand & (rho >= r_top - 0.15))[0]
                ret = bmesh.ops.duplicate(bm, geom=[bm.faces[int(i)] for i in pick])
                bm.faces.ensure_lookup_table()
                for v in ret["geom"]:
                    if not isinstance(v, bmesh.types.BMVert):
                        continue
                    x, y, zz = v.co
                    rr = math.hypot(x, y); dd = (math.degrees(math.atan2(y, x)) + 90 + 180) % 360 - 180
                    us = math.radians(dd - p0) * r_top
                    if flip:
                        us = tu - us
                    v.co = T(U0 + iu * tu + us, V0 + iv * tv + (zz - z0), min(rr - r_top, 0.02) - 0.003)
                count += len(pick)
        made[name] = count
    bm.to_mesh(me); bm.free(); me.update()
    res["band_proxy_faces"] = made


def foot_plinth_faces(data, half, profiles):
    """The plinth's trim faces in the baked slot 0, before the unwrap, each tagged in the face attribute
    "plinth" (its FOOT_PLINTH_GROUPS index + 1). Returns the bake proxies' targets."""
    bm = bmesh.new(); bm.from_mesh(data)
    tag = bm.faces.layers.int.new("plinth")
    targets = []

    def P3(r, d, zz):
        a = math.radians(d - 90); return Vector((r * math.cos(a), r * math.sin(a), zz))

    welded = {}

    def vert(q, g):   # one vertex per corner in a group: each group unwraps as one island
        key = (g, round(q.x, 4), round(q.y, 4), round(q.z, 4))
        if key not in welded:
            welded[key] = bm.verts.new(q)
        return welded[key]

    def quad(p, want, g):
        f = bm.faces.new([vert(q, g) for q in p]); f.normal_update()
        if f.normal.dot(want) < 0:
            f.normal_flip()
        f.material_index = 0
        f[tag] = FOOT_PLINTH_GROUPS.index(g) + 1

    def radial_target(g, d, sign, r0, r1, z0, z1):
        a = math.radians(d - 90); n = Vector((-math.sin(a), math.cos(a), 0)) * sign
        targets.append((g, lambda u, v, h, d=d, n=n: P3(u, d, v) + n * h, r0, r1, z0, z1))

    UP = Vector((0, 0, 1))
    for s in (-1, 1):
        side = "R" if s > 0 else "L"
        d = s * half
        a = math.radians(d - 90); t = Vector((-math.sin(a), math.cos(a), 0)) * -s
        prof = profiles[s]
        for (za, ra), (zb, rb) in zip(prof, prof[1:]):   # the jamb: the doorway side of the plinth's cut section
            quad([P3(3.10, d, za), P3(ra, d, za), P3(rb, d, zb), P3(3.10, d, zb)], t, "jamb" + side)
        radial_target("jamb" + side, d, -s, 3.10, max(p for _, p in prof), -0.10, FOOT_PLINTH_TOP)
        # the slot's back wall (faces the tube) and its end
        lo, hi = min(d, s * 49.5), max(d, s * 49.5)
        for i in range(12):
            a0 = lo + (hi - lo) * i / 12; a1 = lo + (hi - lo) * (i + 1) / 12
            c = P3(3.10, (a0 + a1) / 2, 0); w = -Vector((c.x, c.y, 0)).normalized()
            quad([P3(3.10, a0, 0.0), P3(3.10, a1, 0.0), P3(3.10, a1, FOOT_PLINTH_TOP), P3(3.10, a0, FOOT_PLINTH_TOP)], w, "slotwall" + side)
        targets.append(("slotwall" + side, lambda u, v, h, lo=lo: P3(3.10 - h, lo + math.degrees(u / 3.10), v), 0.0, math.radians(hi - lo) * 3.10, 0.0, FOOT_PLINTH_TOP))
        a = math.radians(s * 49.5 - 90); te = Vector((-math.sin(a), math.cos(a), 0)) * -s
        quad([P3(2.65, s * 49.5, 0.0), P3(3.10, s * 49.5, 0.0), P3(3.10, s * 49.5, FOOT_PLINTH_TOP), P3(2.65, s * 49.5, FOOT_PLINTH_TOP)], te, "slotend" + side)
        radial_target("slotend" + side, s * 49.5, -s, 2.65, 3.10, 0.0, FOOT_PLINTH_TOP)

    def sector(g, r0, r1, d0, d1, zz, seg):
        for i in range(seg):
            a0 = d0 + (d1 - d0) * i / seg; a1 = d0 + (d1 - d0) * (i + 1) / seg
            quad([P3(r0, a0, zz), P3(r1, a0, zz), P3(r1, a1, zz), P3(r0, a1, zz)], UP, g)
        rm = (r0 + r1) / 2
        targets.append((g, lambda u, v, h, d0=d0, rm=rm, zz=zz: P3(v, d0 + math.degrees(u / rm), zz + h), 0.0, math.radians(d1 - d0) * rm, r0, r1))

    def wall(g, r, d0, d1, z0, z1, seg):
        for i in range(seg):
            a0 = d0 + (d1 - d0) * i / seg; a1 = d0 + (d1 - d0) * (i + 1) / seg
            c = P3(r, (a0 + a1) / 2, 0)
            quad([P3(r, a0, z0), P3(r, a1, z0), P3(r, a1, z1), P3(r, a0, z1)], Vector((c.x, c.y, 0)).normalized(), g)
        targets.append((g, lambda u, v, h, r=r, d0=d0: P3(r + h, d0 + math.degrees(u / r), v), 0.0, math.radians(d1 - d0) * r, z0, z1))
    # T7 the right plinth's top (to its closed outer face) and T7b beyond it (the half-post cut)
    sector("topR", 3.10, 3.88, half, 40.0, FOOT_PLINTH_TOP, 8)
    sector("topR", 3.10, 3.50, 40.0, 49.5, FOOT_PLINTH_TOP, 4)
    # SEAFLOOR-SEE-THROUGH (Dan, 29 September 2026): the right pocket cut hollowed the plinth under T7
    # (r 3.10-3.85) and left no outer skin between the jamb and dphi 40 above the grille: its outer face,
    # flush with T7's edge, its end at dphi 40 (where Meshy's skin, r 3.5-3.7, resumes), and a skirt under
    # T7b's edge where the half-post cut left a slit over Meshy's lower skin (z 0.55-0.87).
    wall("outerR", 3.88, half, 40.0, -0.10, FOOT_PLINTH_TOP, 8)
    a = math.radians(40.0 - 90); tc = Vector((-math.sin(a), math.cos(a), 0))
    quad([P3(3.45, 40.0, -0.10), P3(3.88, 40.0, -0.10), P3(3.88, 40.0, FOOT_PLINTH_TOP), P3(3.45, 40.0, FOOT_PLINTH_TOP)], tc, "capR")
    radial_target("capR", 40.0, 1, 3.45, 3.88, -0.10, FOOT_PLINTH_TOP)
    wall("skirtR", 3.50, 40.0, 49.5, 0.45, FOOT_PLINTH_TOP, 4)
    # T7L: the notch beside the left jamb, out to the plinth's own top edge (the jamb's section there)
    sector("topL", 3.10, profiles[-1][-1][1], -26.5, -half, FOOT_PLINTH_TOP, 3)
    bm.to_mesh(data); bm.free(); data.update()
    return targets


def foot_plinth_tags(data):
    a = data.attributes.get("plinth")
    t = np.zeros(len(data.polygons), dtype=np.int64)
    if a is not None:
        a.data.foreach_get("value", t)
    return t


def foot(mesh, target, budget, psp, res):
    data = mesh.data
    data.transform(Matrix.Scale(FOOT["k"], 4) @ Matrix.Rotation(math.radians(FOOT["yaw"]), 4, "Z") @ Matrix.Translation((-FOOT["axis"][0], -FOOT["axis"][1], -FOOT["floor"])))
    data.update()
    foot_split_glass_edges(data, res)
    foot_split_door_edges(data, res)
    fc, fn, fa, vc, mi = arrays(data)
    rho = np.hypot(fc[:, 0], fc[:, 1]); phi = np.degrees(np.arctan2(fc[:, 1], fc[:, 0])); dphi = (phi + 90 + 180) % 360 - 180
    nrad = (fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / np.maximum(rho, 1e-9); z = fc[:, 2]
    HALF = math.degrees(math.asin(1.2 / 3.0))

    def region(a, b, r0, r1, z0, z1):
        return (dphi >= a) & (dphi <= b) & (rho >= r0) & (rho <= r1) & (z >= z0) & (z <= z1)
    grille = (rho > 3.9) & (z < 0.35)
    wedge = region(-HALF, HALF, 2.60, 4.30, 0.02, 3.90)
    slots = (np.abs(dphi) > HALF) & (np.abs(dphi) <= 49.5) & (rho >= 2.65) & (rho <= 3.10) & (z > 0.02) & (z < 3.90)
    halfpost = region(38.0, 49.5, 3.10, 3.50, 0.02, 3.90) & ~grille
    pocket = (region(HALF, 40.0, 3.10, 3.85, 0.02, 2.30) | region(HALF, 40.0, 3.85, 4.20, 0.90, 2.30)) & ~grille
    wire = (np.abs(dphi) < 50) & (z > 3.30) & (rho > 2.60) & (rho < 3.30)
    threshold_keep = (fn[:, 2] > 0.9) & (np.abs(dphi) <= 14.0) & (rho < 3.55)
    debris = (np.abs(dphi) <= 40.0) & (rho > 2.78) & (rho < 4.30) & (z >= -0.06) & (z <= 0.02) & ~threshold_keep & ~grille
    near_post = np.zeros(len(fc), dtype=bool)
    for p in FOOT_POSTS:
        near_post |= np.abs((phi - p + 180) % 360 - 180) < FOOT_POST_HALF
    glass = (rho >= 2.93) & (rho <= 3.10) & (np.abs(nrad) > 0.8) & (z > 0.84) & (z < 3.60) & ~near_post & ~wedge & ~slots & ~wire
    ground = z < -0.10
    pit = (rho <= FOOT["pit_r"]) & (z >= -0.40) & (z <= 0.05)
    # DIVE-GATE-CLOSEUP-SHARDS: the plinths' metal stops just behind the jamb faces, and the left portal
    # post's torn remnant (and the frame's outer skin the slot cut left beside it) goes above the plinth top
    jamb_clear = (np.abs(dphi) <= HALF + FOOT_JAMB_CLEAR) & (rho >= 3.05) & (rho <= 3.95) & (z >= 0.02) & (z <= FOOT_PORTAL_FLOOR)
    portal = region(-40.0, -HALF, 3.05, 3.45, FOOT_PORTAL_FLOOR, 3.90)
    res["door_cuts"] = {"jamb_clear": int((jamb_clear & ~wedge).sum()), "portal": int(portal.sum())}
    res["cuts"] = delete_faces(data, wedge | slots | halfpost | pocket | wire | debris | glass | ground | pit | jamb_clear | portal)
    del fc, fn, fa, vc, mi, rho, phi, dphi, nrad, z
    # loose islands under 2000 faces, at any height
    n = len(data.polygons); nv = len(data.vertices)
    ev = np.empty(len(data.edges) * 2, dtype=np.int64); data.edges.foreach_get("vertices", ev); ev = ev.reshape(-1, 2)
    lab = np.arange(nv)
    for _ in range(5000):
        a_, b_ = lab[ev[:, 0]], lab[ev[:, 1]]; m_ = np.minimum(a_, b_)
        np.minimum.at(lab, ev[:, 0], m_); np.minimum.at(lab, ev[:, 1], m_)
        lab = lab[lab]
        if np.array_equal(lab[ev[:, 0]], lab[ev[:, 1]]):
            break
    ls = np.empty(n, dtype=np.int64); data.polygons.foreach_get("loop_start", ls)
    lv = np.empty(len(data.loops), dtype=np.int64); data.loops.foreach_get("vertex_index", lv)
    _, inv, cnt = np.unique(lab[lv[ls]], return_inverse=True, return_counts=True)
    small = cnt < 2000
    res["islands_deleted"] = [int(small.sum()), delete_faces(data, small[inv])]
    high = high_copy(mesh)
    decimate(mesh, budget, res)
    # the decimation lifts floor and lip vertices into the leaf and sill zone: back onto z 0
    vc = verts(data)
    vr = np.hypot(vc[:, 0], vc[:, 1]); vd = (np.degrees(np.arctan2(vc[:, 1], vc[:, 0])) + 90 + 180) % 360 - 180
    flat = (np.abs(vd) <= 49.5) & (vr >= 2.60) & (vr <= 3.55) & (vc[:, 2] > -0.02) & (vc[:, 2] < 0.06)
    vc[flat, 2] = 0.0
    set_verts(data, vc)
    res["flattened"] = int(flat.sum())
    # FOOT-FLAT-SLAB: the plinth's trim faces before the unwrap (their own islands), the bake reading
    # them from the plinth's rust band laid behind each (foot_band_proxies)
    profiles, sections = foot_jamb_profiles(data, HALF)
    res["jamb_profiles"] = {("R" if s > 0 else "L"): [[z_, round(r_, 3)] for z_, r_ in p] for s, p in profiles.items()}
    res["jamb_sections"] = {("R" if s > 0 else "L"): p for s, p in sections.items()}
    targets = foot_plinth_faces(data, HALF, profiles)
    foot_band_proxies(high, targets, res)
    psp["shade_smooth"](mesh)
    psp["unwrap_fresh"](mesh)
    base_slot(data, "TubeFoot")
    img = bake(high, mesh, psp, "TubeFoot", 2048)
    res["bake_black"] = black_share(mesh, img)
    tags = foot_plinth_tags(data)
    pl = np.nonzero(tags > 0)[0]
    col, uvs_, ls_, lt_ = texel_colours(mesh, img)
    fa_ = np.empty(len(data.polygons)); data.polygons.foreach_get("area", fa_)
    dens = []
    for fi in pl:
        t_ = uvs_[ls_[fi]:ls_[fi] + lt_[fi]] * 2048
        ua = 0.5 * abs(np.dot(t_[:, 0], np.roll(t_[:, 1], 1)) - np.dot(t_[:, 1], np.roll(t_[:, 0], 1)))
        dens.append(math.sqrt(ua / max(fa_[fi], 1e-9)))
    res["plinth_trim"] = {"faces": int(len(pl)), "black": round(float((col[pl].max(1) < 0.03).mean()), 3) if len(pl) else None,
                          "texels_per_m": [round(float(np.min(dens)), 1), round(float(np.median(dens)), 1), round(float(np.max(dens)), 1)] if dens else None}
    # the jambs' baked UVs by position, for their backs below
    uvl0 = data.uv_layers.active.data
    jamb_xyz, jamb_uv = [], []
    for fi in pl:
        if FOOT_PLINTH_GROUPS[tags[fi] - 1].startswith("jamb"):
            p = data.polygons[int(fi)]
            for li, vi in zip(p.loop_indices, p.vertices):
                jamb_xyz.append(data.vertices[vi].co[:]); jamb_uv.append(tuple(uvl0[li].uv))
    jamb_xyz = np.array(jamb_xyz)
    data.attributes.remove(data.attributes["plinth"])
    # foot_trim: the cut edges closed in the trim slot (Unity: the kit steel), UVs in metres
    TI = new_material(data, "FootTrim", nodes=False)
    bm = bmesh.new(); bm.from_mesh(data); uvl = bm.loops.layers.uv.active
    tfaces = []

    def P3(r, d, zz):
        a = math.radians(d - 90); return Vector((r * math.cos(a), r * math.sin(a), zz))

    def quad(p, want):
        f = bm.faces.new([bm.verts.new(q) for q in p]); f.normal_update()
        if f.normal.dot(want) < 0:
            f.normal_flip()
        f.material_index = TI
        for l in f.loops:
            co_ = l.vert.co
            l[uvl].uv = (co_.x, co_.y) if abs(f.normal.z) > 0.7 else (math.atan2(co_.y, co_.x) * math.hypot(co_.x, co_.y), co_.z)
        tfaces.append(f)
        return f
    UP = Vector((0, 0, 1))

    def sector(r0, r1, d0, d1, zz, want, seg):
        for i in range(seg):
            a0 = d0 + (d1 - d0) * i / seg; a1 = d0 + (d1 - d0) * (i + 1) / seg
            quad([P3(r0, a0, zz), P3(r1, a0, zz), P3(r1, a1, zz), P3(r0, a1, zz)], want)

    def wall(r, d0, d1, z0, z1, inward, seg):
        for i in range(seg):
            a0 = d0 + (d1 - d0) * i / seg; a1 = d0 + (d1 - d0) * (i + 1) / seg
            c = P3(r, (a0 + a1) / 2, 0); w = Vector((c.x, c.y, 0)).normalized() * (-1 if inward else 1)
            quad([P3(r, a0, z0), P3(r, a1, z0), P3(r, a1, z1), P3(r, a0, z1)], w)
    sector(2.70, 3.51, -HALF, HALF, 0.0, UP, 24)            # T1 the sill
    wall(3.51, -HALF, HALF, -0.10, 0.0, False, 24)          # the sill's front, down to the sand
    for s in (-1, 1):
        sector(2.65, 3.10, min(s * HALF, s * 49.5), max(s * HALF, s * 49.5), 0.0, UP, 12)   # the slot floors
    wall(FOOT["pit_r"], -180, 180, -0.10, 0.0, True, 96)    # T6 the pit wall (the car's base ring sinks in)
    # SEAFLOOR-SEE-THROUGH: each jamb is also the plinth's end plate seen from outside the doorway, and a
    # one-sided face seen from behind is not drawn: its back too, on the jamb's own baked texels
    backs = 0
    for s in (-1, 1):
        d = s * HALF
        a = math.radians(d - 90); t = Vector((-math.sin(a), math.cos(a), 0)) * s
        for (za, ra), (zb, rb) in zip(profiles[s], profiles[s][1:]):
            f = quad([P3(3.10, d, za), P3(ra, d, za), P3(rb, d, zb), P3(3.10, d, zb)], t)
            f.material_index = 0
            for l in f.loops:
                q = np.array(l.vert.co[:]); dist = np.linalg.norm(jamb_xyz - q, axis=1); j = int(dist.argmin())
                if dist[j] > 1e-3:
                    raise SystemExit("foot: no jamb UV at %s for the jamb's back" % (q,))
                l[uvl].uv = jamb_uv[j]
            backs += 1
    bm.to_mesh(data); bm.free(); data.update()
    res["trim_faces"] = len(tfaces)
    res["jamb_backs"] = backs
    # acceptance: nothing in the gate leaf's envelope or inside the pit
    vc = verts(data); vr = np.hypot(vc[:, 0], vc[:, 1]); vd = (np.degrees(np.arctan2(vc[:, 1], vc[:, 0])) + 90 + 180) % 360 - 180
    res["in_leaf_envelope"] = int(((vr > 2.65) & (vr < 3.00) & (np.abs(vd) <= 48.4) & (vc[:, 2] > 0.02) & (vc[:, 2] < 3.50)).sum())
    res["inside_pit_above_floor"] = int(((vr < FOOT["pit_r"] - 0.004) & (vc[:, 2] > 0.005)).sum())
    dg = bpy.context.evaluated_depsgraph_get(); bv = BVHTree.FromObject(mesh, dg)
    blk = sum(bv.ray_cast(Vector((0, 0, zz)), Vector((math.cos(math.radians(d - 90)), math.sin(math.radians(d - 90)), 0)), 12.0)[0] is not None
              for d in np.arange(-22.0, 22.01, 1.0) for zz in np.arange(0.05, 3.46, 0.1))
    res["doorway_rays_blocked"] = int(blk)
    gr = (vr > 3.9) & (vr < 4.6) & (vc[:, 2] > -0.02)
    res["grille_top_above_floor"] = round(float(vc[gr, 2].max()), 3) if gr.any() else None
    res["z"] = [round(float(vc[:, 2].min()), 3), round(float(vc[:, 2].max()), 3)]
    res["rmax"] = round(float(vr.max()), 3)
    if res["in_leaf_envelope"] or res["inside_pit_above_floor"]:
        raise SystemExit("foot: acceptance failed %s" % {k: res[k] for k in ("in_leaf_envelope", "inside_pit_above_floor")})


# ---- 7. the deck housing ----------------------------------------------------------------

HOUSING = {
    "axis": (-0.00025, 0.00034),   # circle fit on the outer wall plate (r 0.7191); the fit centres here, not on the bbox
    "z_min": -0.40259,             # raw bottom; heights below are above it
    "z_trim": 0.100,               # everything under the walkway top goes (floor slab, base ring, bottom plate)
    "z_walk": 0.140,               # the walkway top = the deck; every vertex below is lifted to it
    "r_wall_in": 0.6975,           # the wall plate's inner face is 0.6995: inside it and under z_walk is floor
    "door_bearing": -90.0, "door_half": 22.5,   # the front bay between the frame posts (plates end at -67.5 / -112.5)
    "door_r": (0.600, 0.970),      # inner pilaster 0.625 .. walkway edge 0.940
    "z_lintel": 0.620,             # the rim's underside; the hood above it stays as the header
    "band_stretch": 1.2,           # wall band z_walk..z_lintel, rim/lamps/pipe elbows untouched
    "fill_max": 400,               # holes of up to this many edges are capped (post and pipe feet)
    "jamb_r": (0.6965, 0.7300), "soffit_r": (0.615, 0.780),
    "band_r": 3.80,                # the backing band inside the wall (fitted metres): Meshy's slits closed
}


def housing_shape(data, spec):
    """Raw frame, before the fit and the bake copy: trim what would sit under the deck and the
    floor over the well, cut the entrance, cap the small holes, flatten the foot, stretch the band."""
    ax, ay = spec["axis"]; z0 = spec["z_min"]
    zt, zw, zl = spec["z_trim"], spec["z_walk"], spec["z_lintel"]
    db, dh = spec["door_bearing"], spec["door_half"]
    r0, r1 = spec["door_r"]
    co = verts(data)
    vr = np.hypot(co[:, 0] - ax, co[:, 1] - ay)
    vb = np.degrees(np.arctan2(co[:, 1] - ay, co[:, 0] - ax))
    vz = co[:, 2] - z0
    in_door = (np.abs((vb - db + 180.0) % 360.0 - 180.0) <= dh) & (vr >= r0) & (vr <= r1) & (vz < zl)
    npoly = len(data.polygons)
    cen = np.empty(npoly * 3); data.polygons.foreach_get("center", cen); cen = cen.reshape(-1, 3)
    cr = np.hypot(cen[:, 0] - ax, cen[:, 1] - ay); cz = cen[:, 2] - z0
    trim = (cz < zt) | ((cr < spec["r_wall_in"]) & (cz < zw))
    ls = np.empty(npoly, dtype=np.int64); data.polygons.foreach_get("loop_start", ls)
    lt = np.empty(npoly, dtype=np.int64); data.polygons.foreach_get("loop_total", lt)
    lv = np.empty(len(data.loops), dtype=np.int64); data.loops.foreach_get("vertex_index", lv)
    door_ = np.zeros(npoly, dtype=bool)
    for k in range(int(lt.max())):  # a face goes if ANY of its corners is in the doorway: no teeth left below the lintel
        m = lt > k
        door_[m] |= in_door[lv[ls[m] + k]]
    kill = np.nonzero(trim | door_)[0]
    bm = bmesh.new(); bm.from_mesh(data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.faces[i] for i in kill], context="FACES")
    bm.edges.ensure_lookup_table(); bm.verts.index_update()
    parent = list(range(len(bm.verts)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    bnd = [e for e in bm.edges if e.is_boundary]
    for e in bnd:
        a, b = find(e.verts[0].index), find(e.verts[1].index)
        if a != b:
            parent[a] = b
    groups = {}
    for e in bnd:
        groups.setdefault(find(e.verts[0].index), []).append(e)
    small = [es for es in groups.values() if len(es) <= spec["fill_max"]]
    filled = bmesh.ops.holes_fill(bm, edges=[e for es in small for e in es], sides=spec["fill_max"])["faces"]
    s = spec["band_stretch"]
    for v in bm.verts:
        z = max(v.co.z - z0, zw)
        if z > zl:
            z += (zl - zw) * (s - 1.0)
        elif z > zw:
            z = zw + (z - zw) * s
        v.co.z = z0 + z
    bmesh.ops.dissolve_degenerate(bm, dist=0.0005, edges=bm.edges[:])
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.calc_area() < 1e-12], context="FACES")
    bm.to_mesh(data); bm.free(); data.update()
    return {"trimmed": int(trim.sum()), "door": int((door_ & ~trim).sum()), "capped_loops": len(small), "cap_faces": len(filled)}


def housing_caps(mesh, spec, k):
    """Fitted frame (metres, axis at x = y = 0, walkway top at z = 0), on the decimated mesh before
    the unwrap: the doorway's ragged edges pulled onto its lines, then two jambs and the header's soffit."""
    zl = (spec["z_lintel"] - spec["z_walk"]) * spec["band_stretch"] * k
    db, dh = spec["door_bearing"], spec["door_half"]
    bm = bmesh.new(); bm.from_mesh(mesh.data)
    for v in bm.verts:  # the walkway, flat again after the collapse: one plane at z 0
        if v.co.z < 0.03:
            v.co.z = 0.0
    bmesh.ops.dissolve_degenerate(bm, dist=0.003, edges=bm.edges[:])
    bm.normal_update()
    down = [f for f in bm.faces if f.calc_center_median().z < 0.001 and f.normal.z < -0.5]
    bmesh.ops.reverse_faces(bm, faces=down)
    bm.normal_update()
    for v in bm.verts:
        if not v.is_boundary:
            continue
        r = math.hypot(v.co.x, v.co.y); b = math.degrees(math.atan2(v.co.y, v.co.x)); d = (b - db + 180.0) % 360.0 - 180.0
        if not (spec["door_r"][0] * k - 0.05 <= r <= spec["door_r"][1] * k + 0.05):
            continue
        if abs(d) <= dh + 0.5 and zl - 0.01 <= v.co.z <= zl + 0.15:
            v.co.z = zl
        if dh - 0.25 <= abs(d) <= dh + 2.5 and -0.01 <= v.co.z <= zl + 0.15:
            nb = math.radians(db + math.copysign(dh, d)); v.co.x, v.co.y = r * math.cos(nb), r * math.sin(nb)

    def P(r, b, z):
        return Vector((r * k * math.cos(math.radians(b)), r * k * math.sin(math.radians(b)), z))
    faces = []
    ji, jo = spec["jamb_r"]
    for sgn in (-1.0, 1.0):
        b = db + sgn * dh
        q = [P(ji, b, 0.0), P(jo, b, 0.0), P(jo, b, zl), P(ji, b, zl)]
        faces.append(bm.faces.new([bm.verts.new(p) for p in (q if sgn > 0 else q[::-1])]))  # facing into the opening
    si, so = spec["soffit_r"]
    for i in range(24):
        b0 = db - dh + 2 * dh * i / 24; b1 = db - dh + 2 * dh * (i + 1) / 24
        q = [P(si, b0, zl), P(so, b0, zl), P(so, b1, zl), P(si, b1, zl)]
        faces.append(bm.faces.new([bm.verts.new(p) for p in q[::-1]]))  # facing down
    # the backing band inside the wall's thickness, both ways, over the doorway only above the lintel
    RB = spec["band_r"]
    for i in range(288):
        b0 = -180 + 360 * i / 288; b1 = -180 + 360 * (i + 1) / 288
        off = abs(((b0 + b1) / 2 + 90 + 180) % 360 - 180)
        z0 = zl if off <= dh else 2.90
        p = [Vector((RB * math.cos(math.radians(b)), RB * math.sin(math.radians(b)), z)) for b, z in ((b0, z0), (b1, z0), (b1, 3.35), (b0, 3.35))]
        for facing_in in (True, False):
            f = bm.faces.new([bm.verts.new(q) for q in (p[::-1] if facing_in else p)]); f.normal_update()
            want = Vector((p[0].x, p[0].y, 0)).normalized() * (-1 if facing_in else 1)
            if f.normal.dot(want) < 0:
                f.normal_flip()
            faces.append(f)
    bmesh.ops.triangulate(bm, faces=faces)
    bm.normal_update()
    bm.to_mesh(mesh.data); bm.free(); mesh.data.update()
    return round(zl, 3)


def housing(mesh, target, budget, psp, res):
    data = mesh.data
    res["shape"] = housing_shape(data, HOUSING)
    k = fit_uniform(mesh, target, axis=HOUSING["axis"])
    res["k"] = round(k, 5)
    high = high_copy(mesh)
    decimate(mesh, budget, res)
    res["lintel_m"] = housing_caps(mesh, HOUSING, k)
    dg = bpy.context.evaluated_depsgraph_get(); bv = BVHTree.FromObject(mesh, dg)
    esc = 0
    for z in (2.85, 2.95, 3.05, 3.15, 3.30, 3.40):
        for bdeg in np.arange(-180, 180, 0.5):
            if abs((bdeg + 90 + 180) % 360 - 180) > 22.5 and bv.ray_cast(Vector((0, 0, z)), Vector((math.cos(math.radians(bdeg)), math.sin(math.radians(bdeg)), 0)), 20.0)[0] is None:
                esc += 1
    res["wall_rays_escaping"] = esc
    n = h = 0
    for gx in np.arange(-3.1, 3.11, 0.1):
        for gy in np.arange(-3.1, 3.11, 0.1):
            if math.hypot(gx, gy) <= 3.10:
                n += 1; h += bv.ray_cast(Vector((gx, gy, 6.0)), Vector((0, 0, -1)), 10.0)[0] is not None
    res["well_rays_down_hit"] = [n, int(h)]
    fr = [float(b) for b in np.arange(-120, -60, 0.25) if bv.ray_cast(Vector((0, 0, 1.5)), Vector((math.cos(math.radians(b)), math.sin(math.radians(b)), 0)), 20.0)[0] is None]
    res["doorway_free_at_1.5m"] = [min(fr), max(fr)] if fr else None
    psp["shade_smooth"](mesh)
    res["triangles"] = len(data.polygons)
    psp["unwrap_fresh"](mesh)
    base_slot(data, "CabinHousing")
    img = bake(high, mesh, psp, "CabinHousing", 2048)
    # the jambs take the neighbouring post's baked texels (45 % of them baked black)
    fc, fn, fa, vc, mi = arrays(data)
    fr = np.hypot(fc[:, 0], fc[:, 1]); off = (np.degrees(np.arctan2(fc[:, 1], fc[:, 0])) + 90 + 180) % 360 - 180
    jamb = (np.abs(np.abs(off) - 22.5) < 0.05) & (fr >= 3.70) & (fr <= 3.92) & (np.abs(fn[:, 2]) < 0.1)
    col, uvs, ls, lt = texel_colours(mesh, img); lum = col.max(1)
    uvl = data.uv_layers.active.data
    for sgn in (-1, 1):
        cand = np.nonzero((np.sign(off) == sgn) & (np.abs(off) > 23.0) & (np.abs(off) < 27.0) & (fr > 3.62) & (fr < 3.80) & (fc[:, 2] > 0.5) & (fc[:, 2] < 2.8) & (lum > 0.03))[0]
        if len(cand) == 0:
            continue
        j = cand[np.argsort(lum[cand])[len(cand) // 2]]
        u = uvs[ls[j]:ls[j] + lt[j]].mean(0)
        for fi in np.nonzero(jamb & (np.sign(off) == sgn))[0]:
            for li in data.polygons[int(fi)].loop_indices:
                uvl[li].uv = u
    col, uvs, ls, lt = texel_colours(mesh, img); lum = col.max(1)
    res["jamb_black"] = round(float((lum[jamb] < 0.02).mean()), 3) if jamb.any() else None
    res["bake_black"] = round(float((lum < 0.02).mean()), 3)
    # the shutters' paint: the largest orange face on the outer wall, its UV rect (ElevatorLook.ShutterAtlasRect)
    orange = (col[:, 0] > 0.30) & (col[:, 0] > 1.6 * col[:, 2]) & (col[:, 1] > 0.12) & (col[:, 1] < 0.8 * col[:, 0])
    outer = (fr > 3.84) & (fr < 4.2) & ((fn[:, 0] * fc[:, 0] + fn[:, 1] * fc[:, 1]) / fr > 0.8) & (fc[:, 2] > 0.3) & (fc[:, 2] < 2.8)
    cand = np.nonzero(orange & outer)[0]
    if len(cand):
        j = int(cand[np.argmax(fa[cand])])
        uc, vc2 = uvs[ls[j]:ls[j] + lt[j]].mean(0)
        W = img.size[0]; px = np.array(img.pixels[:], dtype=np.float32).reshape(img.size[1], W, 4)
        best = None
        for half in (0.002, 0.004, 0.006, 0.008, 0.010, 0.014):
            x0, x1 = int((uc - half) * W), int((uc + half) * W); y0, y1 = int((vc2 - half) * W), int((vc2 + half) * W)
            patch = px[max(0, y0):y1, max(0, x0):x1, :3].reshape(-1, 3)
            ok = (patch[:, 0] > 0.30) & (patch[:, 0] > 1.6 * patch[:, 2]) & (patch[:, 1] > 0.12)
            if len(patch) == 0 or ok.mean() < 0.97:
                break
            best = [round(float(uc - half), 4), round(float(vc2 - half), 4), round(2 * half, 4), round(2 * half, 4), round(float(ok.mean()), 3)]
        res["shutter_uv_rect(x,y,w,h,orange)"] = best
    v = verts(data); vr = np.hypot(v[:, 0], v[:, 1])
    res["inner_r_min"] = round(float(vr.min()), 3)
    res["top_z"] = round(float(v[:, 2].max()), 3)


# ---- 8. the shaft gate leaf -----------------------------------------------------------

def gate(mesh, target, budget, psp, res):
    me = mesh.data
    CXF, CYF, R0 = -0.0005, 0.4271, 0.5244
    CXB, CYB, RB = -0.0006, 0.4599, 0.4823
    A0, A1, Z0, Z1 = -132.693, -47.279, -0.7183, 0.7011
    fc, fn, fa, vc, mi = arrays(me)
    rf = np.hypot(fc[:, 0] - CXF, fc[:, 1] - CYF); rb = np.hypot(fc[:, 0] - CXB, fc[:, 1] - CYB)
    af = np.degrees(np.arctan2(fc[:, 1] - CYF, fc[:, 0] - CXF))
    nr = (fn[:, 0] * (fc[:, 0] - CXF) + fn[:, 1] * (fc[:, 1] - CYF)) / np.maximum(rf, 1e-9)
    pane = (af >= A0 - 0.5) & (af <= A1 + 0.5) & (fc[:, 2] >= Z0 - 0.005) & (fc[:, 2] <= Z1 + 0.005) & ((np.abs(rf - R0) < 0.004) | (np.abs(rb - RB) < 0.004)) & (np.abs(nr) > 0.9)
    res["pane_faces"] = delete_faces(me, pane)
    V = verts(me)
    lo, hi = V.min(0), V.max(0)
    va = np.degrees(np.arctan2(V[:, 1] - CYF, V[:, 0] - CXF)); vr = np.hypot(V[:, 0] - CXF, V[:, 1] - CYF)
    tmin, tmax = -152.305, -27.416
    H = math.degrees(math.asin(1.2 / 3.0)); SPAN = 1.05 * H
    L0 = math.radians(tmax - tmin) * R0
    R1 = 2.800
    ke = math.radians(SPAN) / L0 * R1
    band = (round(Z0 + 0.01, 4), round(Z1 - 0.01, 4))
    ends = (band[0] - lo[2]) + (hi[2] - band[1])
    km = (3.5 - ke * ends) / (band[1] - band[0])
    z = V[:, 2]
    phi = np.radians(-90.0 + (va - tmin) / (tmax - tmin) * SPAN)
    rho = R1 + (vr - R0) * ke
    zb0 = (band[0] - lo[2]) * ke
    zn = np.where(z < band[0], (z - lo[2]) * ke, np.where(z > band[1], zb0 + (band[1] - band[0]) * km + (z - band[1]) * ke, zb0 + (z - band[0]) * km))
    NV = np.stack([rho * np.cos(phi), rho * np.sin(phi), zn], 1)
    set_verts(me, NV)
    res["size"] = (NV.max(0) - NV.min(0)).round(4).tolist()
    if any(abs(a - b) > 0.01 for a, b in zip(res["size"], target)):
        raise SystemExit("gate: bent to %s, the table says %s" % (res["size"], target))
    bm = bmesh.new(); bm.from_mesh(me)
    hp = np.array([v.co[:] for e in bm.edges if e.is_boundary for v in e.verts]); bm.free()
    hpa = np.degrees(np.arctan2(hp[:, 1], hp[:, 0])) + 90
    inner = (hpa > 1.5) & (hpa < 23.5) & (hp[:, 2] > 0.1) & (hp[:, 2] < 3.4)
    high = high_copy(mesh)
    decimate(mesh, budget, res)
    psp["shade_smooth"](mesh)
    psp["unwrap_fresh"](mesh)
    base_slot(me, "GateLeaf")
    img = bake(high, mesh, psp, "GateLeaf", 2048)
    res["bake_black"] = black_share(mesh, img)
    # gate_add_pane: two clear sheets over the hole (the pane's outer and inner faces)
    GI = new_material(me, "GateLeaf_Glass", nodes=False)
    ga0 = float(hpa[inner].min()) - 0.3; ga1 = float(hpa[inner].max()) + 0.3; gz0 = float(hp[inner, 2].min()) - 0.01; gz1 = float(hp[inner, 2].max()) + 0.01
    r_out, r_in = R1, R1 - 0.0749 * ke
    bm = bmesh.new(); bm.from_mesh(me); uvl = bm.loops.layers.uv.active
    sheet = []
    for rr, outward in ((r_out, True), (r_in, False)):
        ring = []
        for i in range(25):
            t = math.radians(-90 + ga0 + (ga1 - ga0) * i / 24)
            ring.append((bm.verts.new((rr * math.cos(t), rr * math.sin(t), gz0)), bm.verts.new((rr * math.cos(t), rr * math.sin(t), gz1))))
        for i in range(24):
            (a0, a1), (b0, b1) = ring[i], ring[i + 1]
            f = bm.faces.new((a0, b0, b1, a1)); f.material_index = GI; f.normal_update()
            c = f.calc_center_median(); want = Vector((c.x, c.y, 0)).normalized() * (1 if outward else -1)
            if f.normal.dot(want) < 0:
                f.normal_flip()
            for l in f.loops:
                l[uvl].uv = (0.0, 0.0)
            sheet.append(f)
    bmesh.ops.triangulate(bm, faces=sheet)
    bm.to_mesh(me); bm.free(); me.update()
    res["glass"] = {"angle": [round(ga0, 3), round(ga1, 3)], "z": [round(gz0, 4), round(gz1, 4)], "r": [round(r_in, 4), round(r_out, 4)]}
    v = verts(me); vr2 = np.hypot(v[:, 0], v[:, 1])
    res["r"] = [round(float(vr2.min()), 4), round(float(vr2.max()), 4)]
    if vr2.min() < 2.60 or vr2.max() > 2.9734:
        raise SystemExit("gate: the leaf reaches r %s (car limit 2.60, tube glass 2.9734)" % res["r"])
    # gate_mirror: the left leaf, mirrored across the doorway's centre line
    left = mesh.copy(); left.data = me.copy(); left.name = left.data.name = "GateLeaf_L"
    bpy.context.collection.objects.link(left)
    lv = verts(left.data); lv[:, 0] *= -1; set_verts(left.data, lv)
    bm = bmesh.new(); bm.from_mesh(left.data); bmesh.ops.reverse_faces(bm, faces=bm.faces[:]); bm.to_mesh(left.data); bm.free(); left.data.update()
    res["right_leaf_rel_deg"] = [round(float((np.degrees(np.arctan2(v[:, 1], v[:, 0])) + 90).min()), 2), round(float((np.degrees(np.arctan2(v[:, 1], v[:, 0])) + 90).max()), 2)]


RECIPES = {"ElevatorCar": car, "CabinDoor": door, "CarPanel": panel, "TubeSection": plain, "TopCollar": plain,
           "TubeFoot": foot, "CabinHousing": housing, "GateLeaf": gate}


def run(part, src, dst, psp):
    target, _, budget = psp["TABLE"][part]
    mesh = psp["import_joined"](src, part)
    res = {"part": part, "raw_faces": len(mesh.data.polygons)}
    RECIPES[part](mesh, target, budget, psp, res)
    psp["write_maps_and_export"](mesh, part, dst, KEEP)
    # what Unity will read: the objects, their faces and slots
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=dst)
    res["exported"] = [{"name": o.name, "faces": len(o.data.polygons), "slots": [m.name for m in o.data.materials if m],
                        "location": [round(x, 4) for x in o.location]} for o in bpy.data.objects if o not in before and o.type == "MESH"]
    for o in [o for o in bpy.data.objects if o not in before]:
        bpy.data.objects.remove(o, do_unlink=True)
    print("ELEV_RESULT " + json.dumps(res, default=lambda o: o.tolist() if hasattr(o, "tolist") else str(o)), flush=True)
    return res
