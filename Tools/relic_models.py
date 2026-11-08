# Relic weapons + reward chest for "Ashes of the Throne" (Blender 4.4+/5.x).
#
#   exec(open(r"D:\DarkFantasy\Tools\relic_models.py").read(), G := {})
#   G['build_all']()                    # builds the "Relics" scene (preview objects + materials)
#   G['export_all'](r"D:\DarkFantasy\Assets\DarkFantasy\Art\Relics")   # JSON meshes read by the Unity builder
#
# Everything is modelled in Blender coordinates with Z up. The exporter converts to Unity
# (x, z, y) and flips the winding, so the Unity conventions are:
#   * Cinder Oath (greatsword): origin = right-hand grip point, blade along +Y, edge along ±Z
#     (same frame as the procedural greatsword, so it drops into the solved grip as is).
#   * Thornwraith (war-bow): origin = centre of the bow hand, +Y = up along the bow, +Z = toward
#     the target; the string nocks sit at (z = -0.086, y = ±0.638) like the archer's base bow,
#     so the string bones (BowTop/BowBottom) line up after a uniform fit.
#   * Reward chest: origin = floor centre, front = +Z; the lid is a separate mesh whose origin
#     is the hinge (back top edge), rotating about its X axis.
# Materials are only names (dark, edge, gold, ember, leather, darkwood, bone, rune, iron, wood,
# glow): Unity assigns its own PS2 materials (emissive for ember/rune/glow).
import bpy, bmesh, math, json, os
from mathutils import Vector

COLORS = {
    'dark': (0.07, 0.065, 0.06), 'edge': (0.62, 0.62, 0.6), 'gold': (0.62, 0.45, 0.16),
    'ember': (1.0, 0.36, 0.08), 'leather': (0.18, 0.1, 0.06), 'darkwood': (0.16, 0.09, 0.06),
    'bone': (0.72, 0.68, 0.58), 'rune': (0.55, 0.85, 1.0), 'iron': (0.22, 0.22, 0.22),
    'wood': (0.3, 0.19, 0.11), 'glow': (1.0, 0.75, 0.35),
}


class Part:
    def __init__(self, mat):
        self.mat = mat; self.v = []; self.f = []

    def vert(self, p):
        self.v.append(Vector(p)); return len(self.v) - 1

    def face(self, *idx):
        self.f.append(tuple(idx))

    def quad(self, a, b, c, d, two_sided=False):
        i = [self.vert(p) for p in (a, b, c, d)]
        self.face(*i)
        if two_sided: self.face(*reversed(i))

    def tri(self, a, b, c, two_sided=False):
        i = [self.vert(p) for p in (a, b, c)]
        self.face(*i)
        if two_sided: self.face(*reversed(i))


def outward(part, centre):
    """re-orders every face so its normal points away from 'centre' (a point or a function p → centre)"""
    nf = []
    for f in part.f:
        pts = [part.v[i] for i in f]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        mid = sum(pts, Vector()) / len(pts)
        c = centre(mid) if callable(centre) else Vector(centre)
        nf.append(tuple(reversed(f)) if n.dot(mid - c) < 0 else f)
    part.f = nf


def loft(part, rings, closed=True, cap0=False, cap1=False):
    """rings: list of lists of points (same count); quads between consecutive rings"""
    n = len(rings[0])
    ids = [[part.vert(p) for p in r] for r in rings]
    for k in range(len(rings) - 1):
        for j in range(n if closed else n - 1):
            a, b = ids[k][j], ids[k][(j + 1) % n]
            c, d = ids[k + 1][(j + 1) % n], ids[k + 1][j]
            part.face(a, b, c, d)
    if cap0: part.face(*reversed(ids[0]))
    if cap1: part.face(*ids[-1])
    return ids


def prism(dst, a, b, r0, r1, sides=8, cap0=True, cap1=True, phase=0.0):
    part = Part(dst.mat)
    a = Vector(a); b = Vector(b)
    ax = (b - a).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); w = ax.cross(u)
    ring = lambda c, r: [c + (u * math.cos(phase + 2 * math.pi * k / sides) + w * math.sin(phase + 2 * math.pi * k / sides)) * r for k in range(sides)]
    loft(part, [ring(a, r0), ring(b, r1)], cap0=cap0, cap1=cap1)
    outward(part, lambda m: a + ax * (m - a).dot(ax))
    merge(dst, part)


def gem(dst, c, r, h):
    part = Part(dst.mat)
    c = Vector(c)
    top, bot = c + Vector((0, 0, h)), c - Vector((0, 0, h))
    ring = [c + Vector((math.cos(k * math.pi / 3) * r, math.sin(k * math.pi / 3) * r, 0)) for k in range(6)]
    for k in range(6):
        part.tri(ring[k], ring[(k + 1) % 6], top); part.tri(ring[(k + 1) % 6], ring[k], bot)
    outward(part, c)
    merge(dst, part)


# ============================================================================ Cinder Oath
def greatsword():
    dark, edge, gold, ember, leather = Part('dark'), Part('edge'), Part('gold'), Part('ember'), Part('leather')
    # blade (Blender: Z = along the blade, Y = toward the cutting edge, X = flat thickness)
    z0, z1, ztip = 0.15, 1.46, 1.64
    N = 44
    def centre_y(z):    # flamberge waves, fading in near the ricasso and toward the point
        if z < 0.32: return 0.0
        k = min(1.0, (z - 0.32) / 0.12) * min(1.0, (z1 - z) / 0.15 + 0.2)
        return 0.011 * math.sin((z - 0.32) * 2 * math.pi * 5.2) * k
    def half_w(z):
        if z < 0.32: return 0.078                       # broad ricasso
        return 0.072 - 0.022 * (z - 0.32) / (z1 - 0.32) + 0.006 * math.cos((z - 0.32) * 2 * math.pi * 5.2)
    t = 0.017
    rows = []
    for i in range(N + 1):
        z = z0 + (z1 - z0) * i / N
        w, cy = half_w(z), centre_y(z)
        rows.append([Vector((0, cy + w, z)), Vector((t, cy + 0.52 * w, z)), Vector((t, cy - 0.52 * w, z)),
                     Vector((0, cy - w, z)), Vector((-t, cy - 0.52 * w, z)), Vector((-t, cy + 0.52 * w, z))])
    for i in range(N):
        r0, r1 = rows[i], rows[i + 1]
        for j in range(6):
            a, b = r0[j], r0[(j + 1) % 6]; c, d = r1[(j + 1) % 6], r1[j]
            (dark if j in (1, 4) else edge).quad(a, b, c, d)
    tip = Vector((0, 0, ztip))
    for j in range(6):
        a, b = rows[-1][j], rows[-1][(j + 1) % 6]
        (dark if j in (1, 4) else edge).tri(a, b, tip)
    for j in range(6):
        a, b = rows[0][j], rows[0][(j + 1) % 6]
        dark.tri(b, a, Vector((0, 0, z0)))
    blade_axis = lambda m: Vector((0, centre_y(m.z), min(max(m.z, z0), ztip)))
    outward(dark, blade_axis); outward(edge, blade_axis)
    # glowing ember channel on both flats (follows the waves)
    M = 34
    for side in (1, -1):
        for i in range(M):
            za = 0.2 + (1.24 - 0.2) * i / M; zb = 0.2 + (1.24 - 0.2) * (i + 1) / M
            wa = 0.011 * (1 - 0.6 * i / M); wb = 0.011 * (1 - 0.6 * (i + 1) / M)
            x = side * (t + 0.0015)
            ember.quad((x, centre_y(za) - wa, za), (x, centre_y(za) + wa, za), (x, centre_y(zb) + wb, zb), (x, centre_y(zb) - wb, zb))
    outward(ember, lambda m: Vector((0, m.y, m.z)))
    # horned crossguard: swept-up arms with a down-hooked spike under each
    prism(gold, (0, 0, 0.075), (0, 0, 0.16), 0.055, 0.05, 6)
    for s in (1, -1):
        pts = [Vector((0, 0.04 * s, 0.12)), Vector((0, 0.15 * s, 0.13)), Vector((0, 0.24 * s, 0.17)), Vector((0, 0.3 * s, 0.25))]
        rad = [0.03, 0.026, 0.02, 0.006]
        for k in range(3): prism(gold, pts[k], pts[k + 1], rad[k], rad[k + 1], 5, k == 0, k == 2)
        prism(gold, (0, 0.13 * s, 0.11), (0, 0.17 * s, 0.035), 0.016, 0.003, 4, True, True)
    gem(ember, (0.05, 0, 0.115), 0.022, 0.03); gem(ember, (-0.05, 0, 0.115), 0.022, 0.03)
    # grip with gold rings, spiked pommel cage and an ember stone
    prism(leather, (0, 0, -0.30), (0, 0, 0.075), 0.026, 0.026, 8)
    for z in (-0.2, -0.08, 0.03): prism(gold, (0, 0, z - 0.012), (0, 0, z + 0.012), 0.031, 0.031, 8)
    prism(gold, (0, 0, -0.36), (0, 0, -0.30), 0.05, 0.034, 8)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        prism(gold, (math.cos(a) * 0.04, math.sin(a) * 0.04, -0.35), (math.cos(a) * 0.065, math.sin(a) * 0.065, -0.42), 0.012, 0.002, 4)
    gem(ember, (0, 0, -0.395), 0.034, 0.045)
    return [dark, edge, gold, ember, leather], {}


# ============================================================================ Thornwraith
TIP_FWD, TIP_UP = -0.086, 0.638      # string nocks of the archer's base bow (bow frame)

def warbow():
    wood, bone, rune, leather, iron = Part('darkwood'), Part('bone'), Part('rune'), Part('leather'), Part('iron')
    # Blender: Z = up along the bow, Y = toward the target, X = across (to the archer's side)
    def centre(t):            # limb centre line from the riser (t=0) to the nock (t=1), upper limb
        z = 0.10 + (TIP_UP - 0.10) * t
        y = -0.215 * t ** 1.5
        s = max(0.0, (t - 0.72) / 0.28)
        y += 0.129 * s * s * (3 - 2 * s)   # reflex tips flick forward, ending exactly on the nock
        return Vector((0, y, z))
    N = 16
    for sgn in (1, -1):
        rows = []
        for i in range(N + 1):
            t = i / N
            c = centre(t); c.z *= sgn
            hw = 0.026 - 0.014 * t; ht = 0.019 - 0.009 * t
            rows.append([c + Vector((hw, ht, 0)), c + Vector((-hw, ht, 0)), c + Vector((-hw, -ht, 0)), c + Vector((hw, -ht, 0))])
        loft(wood, rows, cap1=True)
        # bone thorns along the belly (toward the target), bigger near the riser
        for i in range(2, N - 2, 3):
            t = i / N; c = centre(t); c.z *= sgn
            nxt = centre(t + 0.03); nxt.z *= sgn
            along = (nxt - c).normalized()
            out = Vector((0, 1, 0)) - along * along.y
            out.normalize()
            h = 0.06 * (1 - t) + 0.02
            base = c + out * 0.016
            prism(bone, base, base + out * h + along * 0.02, 0.012 * (1.2 - t), 0.001, 4)
        # crescent blade on the tip (iron), sweeping out and back from the nock
        c = centre(1.0); c.z *= sgn
        pts = [c + Vector((0, 0.01, -0.03 * sgn)), c + Vector((0, 0.07, 0.03 * sgn)), c + Vector((0, 0.05, 0.11 * sgn)), c + Vector((0, -0.02, 0.15 * sgn))]
        inner = [c + Vector((0, 0.005, 0.0)), c + Vector((0, 0.035, 0.04 * sgn)), c + Vector((0, 0.02, 0.09 * sgn)), c + Vector((0, -0.02, 0.15 * sgn))]
        for k in range(3):
            for x in (0.004, -0.004):
                iron.quad(pts[k] + Vector((x, 0, 0)), pts[k + 1] + Vector((x, 0, 0)), inner[k + 1] + Vector((x, 0, 0)), inner[k] + Vector((x, 0, 0)), two_sided=True)
    outward(wood, lambda m: centre_line_point(m))
    # riser: bone block with rune inlays on both sides, leather wrap where the fist holds it
    prism(bone, (0, 0.006, -0.13), (0, 0.006, 0.13), 0.034, 0.034, 6, phase=math.pi / 6)
    prism(leather, (0, 0.006, -0.065), (0, 0.006, 0.065), 0.038, 0.038, 6, phase=math.pi / 6)
    for sgn in (1, -1):
        for x in (0.0355, -0.0355):
            rune.quad((x, -0.006, 0.075 * sgn), (x, 0.018, 0.075 * sgn), (x, 0.018, 0.125 * sgn), (x, -0.006, 0.125 * sgn), two_sided=True)
        # skull-like knuckle guard of bone above/below the hand
        gem(bone, (0, 0.045, 0.1 * sgn), 0.028, 0.03)
    rune_spine = Part('rune')
    for sgn in (1, -1):        # thin glowing line down the back of each limb
        for i in range(1, N - 3):
            a = centre(i / N); b = centre((i + 1) / N); a.z *= sgn; b.z *= sgn
            a.y -= 0.0195 - 0.009 * i / N; b.y -= 0.0195 - 0.009 * (i + 1) / N
            rune_spine.quad(a + Vector((0.004, -0.001, 0)), a + Vector((-0.004, -0.001, 0)), b + Vector((-0.004, -0.001, 0)), b + Vector((0.004, -0.001, 0)), two_sided=True)
    return [wood, bone, rune, rune_spine, leather, iron], {'tipFwd': TIP_FWD, 'tipUp': TIP_UP}


def centre_line_point(m):
    # nearest point on the limb centre line (for outward normals)
    t = max(0.0, min(1.0, (abs(m.z) - 0.10) / (TIP_UP - 0.10)))
    z = 0.10 + (TIP_UP - 0.10) * t
    y = -0.215 * t ** 1.5
    s = max(0.0, (t - 0.72) / 0.28); y += 0.129 * s * s * (3 - 2 * s)
    return Vector((0, y, z if m.z >= 0 else -z))


# ============================================================================ chest
def chest():
    wood, iron, gold, glow = Part('wood'), Part('iron'), Part('gold'), Part('glow')
    W, D, H = 0.52, 0.32, 0.5           # half width (X), half depth (Y), body height
    def box(p, lo, hi):
        x0, y0, z0 = lo; x1, y1, z1 = hi
        c = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        for f in ((0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)):
            p.quad(*[c[i] for i in f])
        outward(p, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2))
    # body: planks (slightly uneven), open top with a glowing inside
    for k in range(5):
        z0 = k * H / 5; z1 = (k + 1) * H / 5 - 0.006
        jig = 0.004 * ((k * 7) % 3 - 1)
        p = Part('wood'); box(p, (-W - jig, -D - jig, z0), (W + jig, D + jig, z1)); merge(wood, p)
    p = Part('glow'); box(p, (-W + 0.04, -D + 0.04, H - 0.02), (W - 0.04, D - 0.04, H - 0.005)); merge(glow, p)
    # iron: corner posts, bottom rim, three straps
    for x in (-W, W):
        for y in (-D, D):
            p = Part('iron'); box(p, (x - 0.035, y - 0.035, -0.01), (x + 0.035, y + 0.035, H + 0.01)); merge(iron, p)
    for x in (-0.3, 0.0, 0.3):
        p = Part('iron'); box(p, (x - 0.035, -D - 0.012, 0.0), (x + 0.035, D + 0.012, H)); merge(iron, p)
    p = Part('iron'); box(p, (-W - 0.02, -D - 0.02, -0.01), (W + 0.02, D + 0.02, 0.05)); merge(iron, p)
    # lock plate with a skull boss on the front (+Y)
    p = Part('gold'); box(p, (-0.07, D + 0.005, H - 0.17), (0.07, D + 0.025, H - 0.02)); merge(gold, p)
    p = Part('gold'); gem(p, (0, D + 0.04, H - 0.095), 0.05, 0.05); merge(gold, p)

    # lid (separate mesh, origin at the hinge: back top edge)
    lw, li, lg = Part('wood'), Part('iron'), Part('gold')
    hinge = Vector((0, -D, H))
    R = D; seg = 8
    def arc(k, r): a = math.pi * k / seg; return Vector((0, -math.cos(a) * r, H + math.sin(a) * r * 0.85))
    for k in range(seg):
        a0, a1 = arc(k, R), arc(k + 1, R)
        lw.quad((-W, a0.y, a0.z), (W, a0.y, a0.z), (W, a1.y, a1.z), (-W, a1.y, a1.z))
    outward(lw, (0, 0, H + 0.1))
    for x in (-W, W):                    # end caps (both faces visible: seen from inside when open)
        pts = [arc(k, R) for k in range(seg + 1)]
        for k in range(seg):
            lw.tri((x, 0, H), (x, pts[k].y, pts[k].z), (x, pts[k + 1].y, pts[k + 1].z), two_sided=True)
    for k in range(seg):                 # inside of the lid
        a0, a1 = arc(k, R - 0.01), arc(k + 1, R - 0.01)
        lw.quad((-W, a1.y, a1.z), (W, a1.y, a1.z), (W, a0.y, a0.z), (-W, a0.y, a0.z))
    for x in (-W, -0.3, 0.0, 0.3, W):
        for k in range(seg):
            a0, a1 = arc(k, R + 0.012), arc(k + 1, R + 0.012)
            li.quad((x - 0.035, a0.y, a0.z), (x + 0.035, a0.y, a0.z), (x + 0.035, a1.y, a1.z), (x - 0.035, a1.y, a1.z))
    outward(li, (0, 0, H))
    # hasp hanging over the lock
    p = Part('gold'); box(p, (-0.04, D - 0.0, H - 0.04), (0.04, D + 0.03, H + 0.06)); merge(lg, p)
    lid = [lw, li, lg]
    for P in lid:
        P.v = [v - hinge for v in P.v]
    return [wood, iron, gold, glow], {'lid': lid, 'hinge': list(hinge)}


def merge(dst, src):
    base = len(dst.v)
    dst.v += src.v
    dst.f += [tuple(i + base for i in f) for f in src.f]


# ============================================================================ Blender preview objects
def to_object(name, parts, coll, offset=Vector()):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    mats = []
    for P in parts:
        if P.mat not in mats: mats.append(P.mat)
        vs = [bm.verts.new(v + offset) for v in P.v]
        for f in P.f:
            try:
                face = bm.faces.new([vs[i] for i in f])
                face.material_index = mats.index(P.mat)
            except ValueError:
                pass
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    for m in mats:
        mat = bpy.data.materials.get('Relic_' + m) or bpy.data.materials.new('Relic_' + m)
        mat.diffuse_color = (*COLORS[m], 1.0)
        me.materials.append(mat)
    return ob


def build_all():
    sc = bpy.data.scenes.get('Relics') or bpy.data.scenes.new('Relics')
    for o in list(sc.collection.objects): bpy.data.objects.remove(o, do_unlink=True)
    sp, _ = greatsword(); bp, bmeta = warbow(); cp, cmeta = chest()
    to_object('CinderOath', sp, sc.collection, Vector((-0.6, 0, 0.45)))
    to_object('Thornwraith', bp, sc.collection, Vector((0.0, 0, 0.9)))
    to_object('RewardChest', cp, sc.collection, Vector((1.1, 0, 0)))
    lid = to_object('RewardChestLid', cmeta['lid'], sc.collection, Vector((1.1, 0, 0)) + Vector(cmeta['hinge']))
    return sc


# ============================================================================ export (Unity coordinates)
def to_unity(p): return [round(p.x, 5), round(p.z, 5), round(p.y, 5)]


def part_json(P):
    v = []; t = []
    for f in P.f:                     # fan-triangulate, flat (unshared) vertices, winding flipped
        for k in range(1, len(f) - 1):
            for i in (f[0], f[k + 1], f[k]):
                t.append(len(v) // 3); v += to_unity(P.v[i])
    return {'mat': P.mat, 'v': v, 't': t}


def export_all(folder):
    os.makedirs(folder, exist_ok=True)
    out = {}
    sp, _ = greatsword()
    out['CinderOath'] = {'name': 'Cinder Oath', 'parts': [part_json(P) for P in sp]}
    bp, bm = warbow()
    out['Thornwraith'] = {'name': 'Thornwraith', 'parts': [part_json(P) for P in bp], 'tipFwd': bm['tipFwd'], 'tipUp': bm['tipUp']}
    cp, cm = chest()
    out['RewardChest'] = {'name': 'Reward Chest', 'parts': [part_json(P) for P in cp]}
    out['RewardChestLid'] = {'name': 'Reward Chest Lid', 'parts': [part_json(P) for P in cm['lid']], 'hinge': to_unity(Vector(cm['hinge']))}
    for k, d in out.items():
        with open(os.path.join(folder, k + '.json'), 'w') as fh: json.dump(d, fh, separators=(',', ':'))
    return {k: sum(len(p['t']) // 3 for p in d['parts']) for k, d in out.items()}


def preview(path, yaw=30, dist=3.2, z=0.75, lens=50):
    sc = bpy.data.scenes['Relics']
    cam = bpy.data.objects.get('RelicCam')
    if cam is None:
        cam = bpy.data.objects.new('RelicCam', bpy.data.cameras.new('RelicCam'))
    if cam.name not in sc.collection.objects: sc.collection.objects.link(cam)
    cam.data.lens = lens
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'MATERIAL'
    sc.render.resolution_x, sc.render.resolution_y = 1200, 700
    a = math.radians(yaw)
    pos = Vector((0.25 + math.sin(a) * dist, -math.cos(a) * dist, z))
    cam.location = pos
    cam.rotation_euler = (Vector((0.25, 0, z)) - pos).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = path
    with bpy.context.temp_override(scene=sc):
        bpy.ops.render.render(write_still=True, scene=sc.name)
    return path
