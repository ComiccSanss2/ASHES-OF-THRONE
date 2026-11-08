# Bard class for "Ashes of the Throne" (Blender 5.x), built on the Solus knight rig.
#
# Run in the Blender session that holds the knight rig ('Rig', 'Solus Knight') and the pack clips
# (knight_greatsword.blend + import Solus_The_Knight.fbx once, delete the imported objects):
#     G = {}; exec(gs_anim.py, G); exec(archer_anim.py, G); exec(bard_anim.py, G)
#     G['build_bard_mesh'](); G['make_staffs'](); G['build_bard_anims'](); G['export_bard'](path)
#
# 1. MESH  — the knight body (helmet removed) under a long hooded brown robe: deep hood with a
#    shadowed face and a grey beard, shoulder mantle, wide bell sleeves, leather belt and pouch,
#    a lute slung on the back. Robe skirt panels follow the legs (front slit).
# 2. STAFF — gnarled wooden staff with a claw head cradling a glowing crystal ("BardStaff"), and the
#    relic version ("BardStaffRelic": blackened wood, golden sun-ring, ember crystal). Both rigid on
#    hand_R. "StaffSocket" (grip) and "StaffTip" (crystal centre) bones tell Unity where the magic
#    comes from.
# 3. ANIMS — BD_Idle/BD_Walk/BD_Run (staff carried upright), BD_Cast (thrust: bolt), BD_Spell (staff
#    raised, then struck on the ground: hymns/crescendo), BD_Hex (sweep and point: curse).
import bpy, bmesh, math
from mathutils import Vector, Matrix

SWATCH.update({
    'cloth': (1768, 696, 1880, 808), 'leather': (1896, 696, 2008, 808), 'wood': (1768, 824, 1880, 936),
    'gold': (1894, 820, 1948, 866), 'shadow': (1958, 820, 2010, 866),
    'crystal': (1894, 894, 1948, 940), 'beard': (1958, 894, 2010, 940),
})


def smooth01(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a))); return t * t * (3 - 2 * t)


# ------------------------------------------------------------------ body parts (world, rig units)
def torso_weights(p):
    """weights for robe / mantle vertices: spine chain by height, legs below the hips"""
    z = p.z; ax = abs(p.x)
    side = 'L' if p.x > 0 else 'R'
    if z > 1.36:
        s = smooth01(0.17, 0.34, ax) * 0.55
        w = {'Chest': 1.0 - s}
        if s > 0: w['Shoulder_' + side] = s
        if z > 1.44 and ax > 0.27:
            u = smooth01(0.27, 0.40, ax) * 0.35
            w = {k: v * (1 - u) for k, v in w.items()}; w['Upper_Arm_' + side] = u
        return w
    if z > 1.18: k = (1.36 - z) / 0.18; return {'Chest': 1 - k, 'Spine 1': k}
    if z > 1.00: k = (1.18 - z) / 0.18; return {'Spine 1': 1 - k, 'Spine': k}
    if z > 0.88: k = (1.00 - z) / 0.12; return {'Spine': 1 - k, 'Hips': k}
    # skirt: hips at the waist, more and more leg toward the hem
    wl = smooth01(-0.07, 0.07, p.x)
    k = smooth01(0.88, 0.40, z) * 0.85
    lo = smooth01(0.62, 0.20, z)
    w = {'Hips': 1.0 - k}
    for s, share in (('L', wl), ('R', 1 - wl)):
        if share < 1e-3: continue
        w['Upper_Leg_' + s] = k * share * (1 - 0.6 * lo)
        if lo > 0: w['Lower_Leg_' + s] = k * share * 0.6 * lo
    return w


def make_robe():
    P = Part('cloth', 4.0)
    rings = [  # z, rx, ry_front, ry_back, centre y
        (1.600, 0.150, 0.120, 0.130, 0.025),
        (1.540, 0.255, 0.160, 0.215, 0.030),
        (1.430, 0.300, 0.172, 0.232, 0.032),
        (1.300, 0.262, 0.172, 0.228, 0.032),
        (1.150, 0.232, 0.168, 0.222, 0.032),
        (1.020, 0.252, 0.172, 0.222, 0.030),
        (0.880, 0.300, 0.182, 0.226, 0.030),
        (0.700, 0.330, 0.192, 0.236, 0.030),
        (0.500, 0.350, 0.202, 0.248, 0.030),
        (0.300, 0.368, 0.212, 0.262, 0.030),
        (0.120, 0.390, 0.224, 0.276, 0.030),
    ]
    segs = 22

    def skip(i, j):
        th = 2 * math.pi * (j + 0.5) / segs
        return i >= 7 and math.cos(th) > math.cos(math.radians(14))   # front slit below the knees
    loft(P, rings, segs, torso_weights, skip)
    return P


def make_mantle():
    P = Part('cloth', 5.0)
    rings = [
        (1.640, 0.140, 0.115, 0.130, 0.020),
        (1.580, 0.300, 0.190, 0.240, 0.030),
        (1.500, 0.390, 0.225, 0.270, 0.032),
        (1.400, 0.420, 0.238, 0.282, 0.034),
        (1.330, 0.428, 0.244, 0.288, 0.034),
    ]
    loft(P, rings, 24, torso_weights)
    return P


def make_hood():
    P = Part('cloth', 6.0)
    rings = [
        (1.955, 0.085, 0.065, 0.120, 0.075),
        (1.905, 0.160, 0.170, 0.205, 0.035),
        (1.830, 0.192, 0.232, 0.232, 0.010),
        (1.750, 0.200, 0.252, 0.232, 0.000),
        (1.670, 0.198, 0.250, 0.228, 0.010),
        (1.610, 0.190, 0.214, 0.222, 0.020),
        (1.555, 0.262, 0.205, 0.240, 0.030),
        (1.480, 0.310, 0.215, 0.255, 0.032),
    ]
    segs = 22

    def wfn(p):
        if p.z >= 1.64: return {'Head': 1.0}
        if p.z >= 1.585: return {'Head': 0.5, 'Neck': 0.5}
        return torso_weights(p)

    def skip(i, j):   # deep face opening
        th = 2 * math.pi * (j + 0.5) / segs
        return i in (1, 2, 3) and math.cos(th) > math.cos(math.radians(50))
    loft(P, rings, segs, wfn, skip, cap_top=(0.0, 0.14, 1.985))
    # hood rim: a thick rolled edge around the opening
    rim = Part('cloth', 6.0)
    return P


def make_face():
    """the shadow inside the hood, a long grey beard and moustache"""
    sh = Part('shadow', 2.0)
    loft(sh, [(1.86, 0.06, 0.03, 0.02, -0.02), (1.80, 0.13, 0.10, 0.06, -0.02), (1.72, 0.15, 0.13, 0.08, -0.02),
              (1.64, 0.12, 0.12, 0.07, -0.01), (1.58, 0.07, 0.06, 0.05, 0.0)], 14, lambda p: {'Head': 1.0},
         cap_top=(0.0, -0.02, 1.88))
    bd = Part('beard', 7.0)
    rings = [(1.665, 0.090, 0.135, -0.04, 0.0), (1.610, 0.085, 0.150, -0.06, 0.0), (1.540, 0.070, 0.165, -0.085, 0.0),
             (1.470, 0.045, 0.172, -0.11, 0.0), (1.410, 0.012, 0.176, -0.14, 0.0)]
    # beard: half-loft in front of the chin (ry_back negative keeps it a thick slab)
    grid = []
    segs = 10
    for z, rx, yf, yb, _ in rings:
        row = []
        for j in range(segs + 1):
            a = math.pi * j / segs                    # 0 → π across the front
            x = -rx * math.cos(a)
            y = -(yf * math.sin(a) + 0.02) - (0.0 if j in (0, segs) else 0.0)
            row.append(bd.vert((x, y + 0.03, z), {'Head': 1.0}))
        grid.append(row)
    for i in range(len(grid) - 1):
        for j in range(segs):
            bd.face((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    tip = bd.vert((0.0, -0.13, 1.37), {'Head': 1.0})
    for j in range(segs): bd.face((grid[-1][j], grid[-1][j + 1], tip))
    # moustache: two drooping wedges
    for s in (1, -1):
        a = bd.vert((0.0, -0.165, 1.70), {'Head': 1.0}); b = bd.vert((s * 0.075, -0.15, 1.665), {'Head': 1.0})
        c = bd.vert((s * 0.10, -0.135, 1.60), {'Head': 1.0}); d = bd.vert((s * 0.02, -0.17, 1.675), {'Head': 1.0})
        bd.face((a, d, c, b) if s > 0 else (a, b, c, d))
    return sh, bd


def make_sleeves():
    out = []
    for s in 'LR':
        P = Part('cloth', 5.0)
        a = bone_head('Upper_Arm_' + s); e = bone_head('Lower_Arm_' + s); h = bone_head('hand_' + s)
        tube(P, a + (a - e).normalized() * 0.02, e, 0.088, 0.094, 10, {'Upper_Arm_' + s: 1.0}, cap=False)
        tube(P, e, e.lerp(h, 0.55), 0.094, 0.115, 10, {'Lower_Arm_' + s: 1.0}, cap=False)
        tube(P, e.lerp(h, 0.55), h + (h - e).normalized() * 0.035, 0.115, 0.150, 10, {'Lower_Arm_' + s: 1.0}, cap=False)
        out.append(P)
    return out


def make_belt():
    P = Part('leather', 8.0)
    loft(P, [(1.065, 0.262, 0.184, 0.236, 0.031), (1.000, 0.268, 0.188, 0.240, 0.031)], 22, torso_weights)
    G = Part('gold', 9.0)
    tube(G, (0.0, -0.165, 1.052), (0.0, -0.205, 1.032), 0.035, 0.035, 8, {'Spine': 1.0})   # buckle
    pouch = Part('leather', 8.0)
    loft(pouch, [(1.00, 0.06, 0.035, 0.035, 0.0), (0.86, 0.07, 0.045, 0.045, 0.0)], 8,
         lambda p: {'Hips': 1.0}, cap_top=None)
    for i, v in enumerate(pouch.v): pouch.v[i] = v + Vector((-0.24, -0.06, 0.0))
    return P, G, pouch


def make_lute():
    """a small lute slung diagonally on the back (wood body, dark neck, gold rose)"""
    wood = Part('wood', 6.0); dark = Part('leather', 6.0)
    base = Vector((0.14, 0.30, 1.02)); top = Vector((-0.20, 0.30, 1.62))
    ax = (top - base).normalized(); side = ax.cross(Vector((0, 1, 0))).normalized(); depth = Vector((0, 1, 0))
    w = {'Chest': 1.0}
    prof = [(0.0, 0.03), (0.06, 0.12), (0.16, 0.165), (0.27, 0.16), (0.36, 0.12), (0.43, 0.06), (0.46, 0.03)]
    rings = []
    segs = 12
    for d, r in prof:
        c = base + ax * d
        ring = []
        for j in range(segs):
            a = 2 * math.pi * j / segs
            back = 0.075 if math.sin(a) > 0 else 0.018      # rounded bowl behind, flat soundboard in front
            ring.append(wood.vert(c + side * math.cos(a) * r + depth * math.sin(a) * r * back / 0.165, w))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(segs):
            wood.face((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    tube(dark, base + ax * 0.44, top, 0.024, 0.02, 6, w)
    pb = top; pt = top + (ax - depth * 0.6).normalized() * 0.12
    tube(dark, pb, pt, 0.024, 0.018, 6, w)
    strap = Part('leather', 8.0)
    # strap across the chest, from the right shoulder to the left hip
    pts = [Vector((-0.20, 0.22, 1.56)), Vector((-0.24, 0.0, 1.50)), Vector((-0.12, -0.19, 1.36)), Vector((0.10, -0.19, 1.16)), Vector((0.22, -0.05, 1.06)), Vector((0.14, 0.27, 1.04))]
    for a, b in zip(pts[:-1], pts[1:]):
        n = (b - a).cross(Vector((0, 0, 1))).normalized() if abs((b - a).normalized().z) < 0.9 else Vector((1, 0, 0))
        q = [a - Vector((0, 0, 0.022)), a + Vector((0, 0, 0.022)), b + Vector((0, 0, 0.022)), b - Vector((0, 0, 0.022))]
        ids = [strap.vert(p + (p - Vector((0, 0.03, p.z))).normalized() * 0.012, torso_weights(p)) for p in q]
        strap.face(ids); strap.face(tuple(reversed(ids)))
    return wood, dark, strap


# ------------------------------------------------------------------ assemble the bard
def remove_helmet(me, names):
    """deletes the knight's helmet (vertices of the head above the jaw line)"""
    drop = []
    for v in me.vertices:
        best = max(v.groups, key=lambda g: g.weight, default=None)
        if best is None: continue
        bn = names[best.group]
        if bn in ('Head', 'Jaw', 'Neck') or bn.startswith(('Eye', 'Nose', 'Upper_LIp', 'Lower_Lip', 'Eyebrow', 'muscle', 'tongue')):
            drop.append(v.index)
    return drop


def add_parts(ob, bm, parts, smooth=True):
    inv = ob.matrix_world.inverted()
    uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    dl = bm.verts.layers.deform.verify()
    gidx = {g.name: g.index for g in ob.vertex_groups}
    for P in parts:
        vs = []
        for p, wts in zip(P.v, P.w):
            v = bm.verts.new(inv @ Vector(p))
            for bn, wv in wts.items():
                if wv > 1e-4: v[dl][gidx[bn]] = wv
            vs.append(v)
        bm.verts.ensure_lookup_table()
        for f in P.f:
            try: face = bm.faces.new([vs[i] for i in f])
            except ValueError: continue
            face.smooth = smooth
            n = face.normal
            for loop in face.loops:
                p = P.v[f[list(face.verts).index(loop.vert)]]
                if abs(n.z) > 0.7: a, b = p.x, p.y
                elif abs(n.x) > abs(n.y): a, b = p.y, p.z
                else: a, b = p.x, p.z
                loop[uvl].uv = sw_uv(P.swatch, a * P.k, b * P.k)


def build_bard_mesh():
    knight = bpy.data.objects['Solus Knight']
    for n in ('Bard', 'BardStaff', 'BardStaffRelic'):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    me = knight.data.copy(); me.name = 'Bard'
    ob = bpy.data.objects.new('Bard', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = knight.matrix_world.copy()
    ob.parent = knight.parent; ob.matrix_parent_inverse = knight.matrix_parent_inverse.copy()
    for g in knight.vertex_groups: ob.vertex_groups.new(name=g.name)
    for m in knight.modifiers:
        if m.type == 'ARMATURE':
            mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = m.object
    names = [g.name for g in knight.vertex_groups]
    drop = set(remove_helmet(me, names))
    for comp in islands(me):        # pauldrons: the mantle covers the shoulders
        bones = {}
        for vi in comp:
            for g in me.vertices[vi].groups: bones[names[g.group]] = bones.get(names[g.group], 0) + g.weight
        top = max(bones, key=bones.get) if bones else ''
        if (len(comp) == 24 and top == 'Head') or (len(comp) == 54 and top.startswith('Upper_Arm')): drop.update(comp)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context='VERTS')
    parts = []
    robe = make_robe(); double_side(robe); parts.append(robe)
    mantle = make_mantle(); double_side(mantle); parts.append(mantle)
    hood = make_hood(); double_side(hood); parts.append(hood)
    sh, bd = make_face(); parts += [sh, bd]
    for s in make_sleeves(): double_side(s); parts.append(s)
    belt, buckle, pouch = make_belt(); parts += [belt, buckle, pouch]
    lw, ld, strap = make_lute(); parts += [lw, ld, strap]
    add_parts(ob, bm, parts)
    bm.to_mesh(me); bm.free(); me.update()
    knight.hide_render = True; knight.hide_viewport = True
    for o in ('Archer', 'ArcherBow', 'GS_Sword'):
        x = bpy.data.objects.get(o)
        if x: x.hide_render = True; x.hide_viewport = True
    return {'bard_verts': len(me.vertices), 'removed': len(drop)}


# ------------------------------------------------------------------ staffs
# Staff frame (like the sword grip): origin = centre of the right fist, +Z = up the shaft,
# +X = the knuckles' facing. Unity metres; the shaft runs from -STAFF_LOW (butt) to +STAFF_HIGH.
STAFF_LOW = 1.12
STAFF_HIGH = 0.66
CRYSTAL_Z = STAFF_HIGH + 0.20


def staff_parts(relic=False):
    wood = Part('leather' if relic else 'wood', 6.0)
    wrap = Part('leather', 9.0); gold = Part('gold', 8.0); cry = Part('crystal', 4.0)
    w = {'hand_R': 1.0}
    # gnarled shaft: rings with a slow twist and knots
    n = 16; segs = 7
    rings = []
    for i in range(n + 1):
        t = i / n
        z = -STAFF_LOW + (STAFF_LOW + STAFF_HIGH) * t
        r = 0.022 + 0.006 * math.sin(t * 19.0) ** 8 + 0.004 * (1 - t)
        off = Vector((0.006 * math.sin(t * 7.0), 0.006 * math.cos(t * 5.0), 0))
        ring = []
        for j in range(segs):
            a = 2 * math.pi * j / segs + t * 2.2
            ring.append(wood.vert(Vector((math.cos(a) * r, math.sin(a) * r, z)) + off, w))
        rings.append(ring)
    for i in range(n):
        for j in range(segs):
            wood.face((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    wood.face(tuple(reversed(rings[0])))
    # leather grip and gold bands
    tube(wrap, (0, 0, -0.12), (0, 0, 0.12), 0.028, 0.028, 8, w)
    for z in (-0.14, 0.14, STAFF_HIGH - 0.02):
        tube(gold, (0, 0, z - 0.012), (0, 0, z + 0.012), 0.031, 0.031, 8, w)
    tube(gold, (0, 0, -STAFF_LOW - 0.02), (0, 0, -STAFF_LOW + 0.05), 0.02, 0.028, 6, w)       # ferrule
    # head: four curling claws around the crystal
    claws = Part('leather' if relic else 'wood', 6.0)
    for k in range(4):
        a = k * math.pi / 2 + math.pi / 4
        pts = []
        for i in range(7):
            t = i / 6
            rad = 0.03 + 0.07 * math.sin(t * math.pi * 0.85)
            z = STAFF_HIGH + 0.36 * t
            pts.append(Vector((math.cos(a) * rad, math.sin(a) * rad, z)))
        for p0, p1 in zip(pts[:-1], pts[1:]):
            tube(claws, p0, p1, 0.012, 0.010, 5, w, cap=False)
    # crystal: long octahedron (8 faces), slightly tilted
    c = Vector((0, 0, CRYSTAL_Z)); h = 0.12; r = 0.045
    tipU = cry.vert(c + Vector((0, 0, h)), w); tipD = cry.vert(c - Vector((0, 0, h * 0.8)), w)
    mid = [cry.vert(c + Vector((math.cos(k * math.pi / 3) * r, math.sin(k * math.pi / 3) * r, 0.01)), w) for k in range(6)]
    for k in range(6):
        cry.face((mid[k], mid[(k + 1) % 6], tipU)); cry.face((mid[(k + 1) % 6], mid[k], tipD))
    if relic:
        # golden sun ring around the crystal with rays
        ring_r = 0.13
        prev = None
        segs2 = 16
        pts = [Vector((math.cos(2 * math.pi * j / segs2) * ring_r, 0.0, CRYSTAL_Z + math.sin(2 * math.pi * j / segs2) * ring_r)) for j in range(segs2)]
        for j in range(segs2):
            tube(gold, pts[j], pts[(j + 1) % segs2], 0.009, 0.009, 4, w, cap=False)
            if j % 2 == 0:
                d = (pts[j] - c).normalized()
                tube(gold, pts[j], pts[j] + d * 0.06, 0.008, 0.002, 4, w, cap=False)
    return [wood, wrap, gold, claws, cry]


def staff_frame_rest():
    arm = bpy.data.objects['Rig']
    H = arm.matrix_world @ arm.data.bones['hand_R'].matrix_local
    return H @ GRIPS['R']


def make_staff_object(name, relic):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    bard = bpy.data.objects['Bard']
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = bard.matrix_world.copy()
    ob.parent = bard.parent; ob.matrix_parent_inverse = bard.matrix_parent_inverse.copy()
    ob.vertex_groups.new(name='hand_R')
    mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = bpy.data.objects['Rig']
    F = staff_frame_rest()
    parts = staff_parts(relic)
    for P in parts:
        P.v = [F @ (Vector(p) * S) for p in P.v]
    bm = bmesh.new()
    add_parts(ob, bm, parts, smooth=False)
    bm.to_mesh(me); bm.free(); me.update()
    return ob


def make_staffs():
    make_staff_object('BardStaff', False)
    r = make_staff_object('BardStaffRelic', True)
    F = staff_frame_rest()
    add_bone('StaffSocket', 'hand_R', F.translation, F @ (Vector((0, 0, 0.08)) * S))
    add_bone('StaffTip', 'StaffSocket', F @ (Vector((0, 0, CRYSTAL_Z)) * S), F @ (Vector((0, 0, CRYSTAL_Z + 0.08)) * S))
    add_bone('StaffButt', 'StaffSocket', F @ (Vector((0, 0, -STAFF_LOW)) * S), F @ (Vector((0, 0, -STAFF_LOW - 0.06)) * S))
    return {'ok': True}


# ------------------------------------------------------------------ staff animation solver
class StaffPose(Pose):
    def __init__(self, **kw):
        Pose.__init__(self)
        self.staff = None        # (grip(r,f,u), shaft up(r,f,u), knuckles(r,f,u)) character space, absolute
        self.left_on = None      # metres along the shaft for a left hand on the staff, or None
        self.left_free = (-0.24, 0.05, 0.95)
        self.pole_l = (-0.5, -0.6, -0.6)
        self.pole_r = (0.55, -0.6, -0.5)
        self.fingers_open = 0.0  # 0 = left fist relaxed, 1 = spread casting hand
        self.__dict__.update(kw)


def solve_staff(P):
    basis = {n: (P.base[n].copy() if P.base else Matrix.Identity(4)) for n in NAMES}
    for n in FINGERS:
        basis[n] = FINGER_BASIS[n].copy()
        if n.endswith('_L') and P.fingers_open > 0 and P.left_on is None:
            basis[n] = Matrix.Identity(4).lerp(FINGER_BASIS[n], 1.0 - P.fingers_open) if hasattr(Matrix, 'lerp') else Matrix.Identity(4)
    M = {}
    if P.hips is not None:
        pos, yaw, pitch, roll = P.hips
        Mh = T(W(*pos)) @ rot(yaw, pitch, roll) @ REST['Hips'].to_3x3().to_4x4()
        basis['Hips'] = REL['Hips'].inverted() @ Mh
    M['Hips'] = REL['Hips'] @ basis['Hips']
    if P.feet is not None:
        for side, (r, f, lift, fyaw) in zip('LR', P.feet):
            hyaw = P.hips[1] if P.hips else 0.0
            ankle = W(r, f, 0.0) + Vector((0, 0, REST['Foot_' + side].translation.z + lift))
            Mf = T(ankle) @ rot(fyaw) @ REST_FOOT[side]
            knee = rot(lerp(hyaw, fyaw, 0.5)).to_3x3() @ W(0.12 if side == 'R' else -0.12, 1.0, 0.0)
            two_bone(M, basis, 'Upper_Leg_' + side, 'Lower_Leg_' + side, 'Foot_' + side, ankle, knee, Mf)
            M['Toes_' + side] = M['Foot_' + side] @ REL['Toes_' + side] @ basis['Toes_' + side]
    y, p, r = P.spine
    for n in SPINE:
        par = PARENT[n]
        Mi = M[par] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        Md = T(h) @ rot(y / 3, p / 3, r / 3) @ T(-h) @ Mi
        basis[n] = (M[par] @ REL[n]).inverted() @ Md
        M[n] = Md
    hy = (-(y + (P.hips[1] if P.hips else 0.0)) * 0.85 if P.look else 0) + P.head[0]
    hp = (-p * 0.6 if P.look else 0) + P.head[1]
    for n, k in (('Neck', 0.4), ('Head', 0.6)):
        par = PARENT[n]
        Mi = M[par] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        Md = T(h) @ rot(hy * k, hp * k, -r * 0.5 * k) @ T(-h) @ Mi
        basis[n] = (M[par] @ REL[n]).inverted() @ Md
        M[n] = Md
    for side, sh in zip('RL', P.shoulders):
        n = 'Shoulder_' + side
        Mi = M['Chest'] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        ax = Mi.col[1].to_3d().normalized()
        c = ax.cross(Vector((0, 0, 1)))
        lift = Matrix.Rotation(math.radians(sh), 4, c.normalized() if c.length > 1e-4 else 'X')
        Md = T(h) @ lift @ T(-h) @ Mi
        basis[n] = (M['Chest'] @ REL[n]).inverted() @ Md
        M[n] = Md
    over = 0.0
    g, up, kn = P.staff
    SF = frame_matrix(W(*g), W(*up).normalized(), W(*kn).normalized())
    HR = SF @ GRIPS['R'].inverted()
    over = max(over, two_bone(M, basis, 'Upper_Arm_R', 'Lower_Arm_R', 'hand_R', HR.translation.copy(), W(*P.pole_r), HR))
    if P.left_on is not None:
        HL = SF @ T(Vector((0, 0, P.left_on * S))) @ GRIPS['L'].inverted()
        over = max(over, two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', HL.translation.copy(), W(*P.pole_l), HL))
    else:
        two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', W(*P.left_free), W(*P.pole_l), None)
    return basis, over


def key_staff_action(name, frames):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ARM.animation_data.action = act
    for n in NAMES: PB[n].rotation_mode = 'QUATERNION'
    prev = {}; worst = 0.0
    for i, P in enumerate(frames):
        basis, over = solve_staff(P); worst = max(worst, over)
        for n in NAMES:
            loc, q, _ = basis[n].decompose()
            if n in prev and prev[n].dot(q) < 0: q = -q
            prev[n] = q
            pb = PB[n]; pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=i); pb.keyframe_insert('rotation_quaternion', frame=i)
    act.frame_range = (0, len(frames) - 1)
    return {"name": name, "frames": len(frames), "max_overreach": round(worst, 3)}


def mix_staff(a, b, t):
    P = StaffPose()
    P.base = a.base
    P.hips = None if a.hips is None else (vlerp(a.hips[0], b.hips[0], t), lerp(a.hips[1], b.hips[1], t), lerp(a.hips[2], b.hips[2], t), lerp(a.hips[3], b.hips[3], t))
    P.spine = vlerp(a.spine, b.spine, t); P.head = vlerp(a.head, b.head, t)
    P.feet = None if a.feet is None else tuple(vlerp(a.feet[k], b.feet[k], t) for k in range(2))
    P.shoulders = vlerp(a.shoulders, b.shoulders, t)
    P.staff = (vlerp(a.staff[0], b.staff[0], t), vslerp(a.staff[1], b.staff[1], t), vslerp(a.staff[2], b.staff[2], t))
    P.pole_l = vlerp(a.pole_l, b.pole_l, t); P.pole_r = vlerp(a.pole_r, b.pole_r, t)
    if a.left_on is not None and b.left_on is not None: P.left_on = lerp(a.left_on, b.left_on, t)
    else: P.left_free = vlerp(a.left_free, b.left_free, t)
    return P


def staff_timeline(keys, length, easing=None):
    out = []
    for f in range(length + 1):
        for i in range(len(keys) - 1):
            fa, pa = keys[i]; fb, pb = keys[i + 1]
            if fa <= f <= fb:
                t = (f - fa) / max(1, fb - fa)
                e = (easing[i] if easing and i < len(easing) and easing[i] else ease)(t)
                out.append(mix_staff(pa, pb, e)); break
        else:
            out.append(mix_staff(keys[-1][1], keys[-1][1], 0.0))
    return out


# ------------------------------------------------------------------ bard poses (character space)
BARD_FEET = ((-0.13, 0.04, 0.0, -8.0), (0.14, -0.02, 0.0, -16.0))
CARRY_G = (0.30, 0.13, 1.20)
CARRY_UP = (0.03, 0.06, 1.0)
CARRY_KN = (0.0, 1.0, 0.0)


def bard_idle(t=0.0, br=1.0):
    b = math.sin(t * 2 * math.pi) * br
    return StaffPose(hips=((0.0, 0.0, 0.872 - 0.004 * b), -6.0, 2.0 + 0.4 * b, 0.0), spine=(3.0, 4.0 + 1.2 * b, 0.0), feet=BARD_FEET,
                     staff=((CARRY_G[0], CARRY_G[1], CARRY_G[2] + 0.006 * b), CARRY_UP, CARRY_KN),
                     left_free=(-0.25, 0.08, 0.96 + 0.01 * b), pole_r=(0.6, -0.5, -0.6), head=(0, 2))


def cast_body(yaw, pitch, hips_yaw=-6.0, drop=0.0, lean=0.0, feet=BARD_FEET):
    return dict(hips=((0.0, 0.0, 0.872 - drop), hips_yaw, 2.0 + pitch * 0.3, lean), spine=(yaw - hips_yaw, pitch, -lean * 0.5), feet=feet)


def build_bard_anims():
    out = []
    bpy.context.scene.render.fps = FPS
    out.append(key_staff_action('BD_Idle', [bard_idle(f / 60.0) for f in range(61)]))

    def loco(name, clip, n_out, amp):
        act = pack_action(clip)
        frames = []
        for f in range(n_out + 1):
            t = f / n_out
            base = pack_basis(act, t)
            Mb = pose_from_basis(base)
            dfwd = -(Mb['Foot_R'].translation.y - Mb['Foot_L'].translation.y)
            sw = max(-1.0, min(1.0, dfwd / 0.35))
            for nm in SPINE + ['Neck', 'Head']: base[nm] = Matrix.Identity(4)
            up = vnorm((0.03, 0.06 + 0.35 * amp + 0.05 * sw, 1.0))
            P = StaffPose(base=base, hips=None, spine=(-4.0 * sw, 5.0 + 6 * amp, 1.5 * sw), feet=None, head=(0, -3 * amp),
                          staff=((0.30 - 0.03 * amp, 0.15 + 0.10 * amp + 0.05 * sw, 1.21 + 0.03 * amp), up, CARRY_KN),
                          left_free=(-0.25 + 0.03 * amp, 0.08 + (0.12 + 0.12 * amp) * sw, 0.97 + 0.08 * amp * abs(sw)),
                          pole_r=(0.6, -0.5, -0.6))
            frames.append(P)
        return key_staff_action(name, frames)
    out.append(loco('BD_Walk', 'knight_walk_in_place', 40, 0.3))
    out.append(loco('BD_Run', 'knight_run_heavy_weapon_in_place', 30, 1.0))

    I = bard_idle(0.0)
    # ---- BD_Cast (bolt): staff drawn back, then thrust at the target; release at frame 13
    k = [
        (0, I),
        (8, StaffPose(**cast_body(-22, -2), staff=((0.34, -0.06, 1.42), vnorm((0.1, -0.45, 1.0)), (0.0, 1.0, 0.3)),
                      left_free=(-0.12, 0.42, 1.36), pole_l=(-0.6, -0.4, -0.6))),
        (13, StaffPose(**cast_body(14, 10, hips_yaw=4.0, lean=0.0, feet=((-0.13, 0.12, 0.0, -4.0), (0.14, -0.08, 0.0, -18.0))),
                       staff=((0.20, 0.42, 1.36), vnorm((-0.08, 1.0, 0.42)), (0.0, -0.2, 1.0)),
                       left_free=(-0.30, 0.0, 1.12), pole_r=(0.7, -0.3, -0.6))),
        (16, StaffPose(**cast_body(12, 8, hips_yaw=4.0, feet=((-0.13, 0.12, 0.0, -4.0), (0.14, -0.08, 0.0, -18.0))),
                       staff=((0.21, 0.40, 1.37), vnorm((-0.06, 1.0, 0.5)), (0.0, -0.2, 1.0)),
                       left_free=(-0.29, 0.01, 1.08), pole_r=(0.7, -0.3, -0.6))),
        (30, I),
    ]
    out.append(key_staff_action('BD_Cast', staff_timeline(k, 30, [ease, ease_out, ease, ease])))
    # ---- BD_Spell (hymns, crescendo): staff raised high, glow, then struck on the ground at frame 20
    k = [
        (0, I),
        (10, StaffPose(**cast_body(-6, -8), staff=((0.17, 0.16, 1.80), vnorm((0.02, 0.10, 1.0)), CARRY_KN),
                       left_free=(-0.36, 0.20, 1.72), pole_l=(-0.7, 0.0, -0.4), pole_r=(0.7, 0.0, -0.4), head=(0, -12))),
        (16, StaffPose(**cast_body(-6, -10), staff=((0.17, 0.17, 1.84), vnorm((0.0, 0.08, 1.0)), CARRY_KN),
                       left_free=(-0.38, 0.22, 1.78), pole_l=(-0.7, 0.0, -0.4), pole_r=(0.7, 0.0, -0.4), head=(0, -14))),
        (20, StaffPose(**cast_body(4, 14, drop=0.07, feet=((-0.16, 0.10, 0.0, -10.0), (0.16, -0.10, 0.0, -20.0))),
                       staff=((0.24, 0.30, 1.20), vnorm((0.0, 0.04, 1.0)), CARRY_KN),
                       left_free=(-0.46, 0.18, 0.98), pole_l=(-0.7, -0.2, -0.6))),
        (25, StaffPose(**cast_body(4, 12, drop=0.06, feet=((-0.16, 0.10, 0.0, -10.0), (0.16, -0.10, 0.0, -20.0))),
                       staff=((0.24, 0.30, 1.21), vnorm((0.0, 0.04, 1.0)), CARRY_KN),
                       left_free=(-0.45, 0.16, 1.0), pole_l=(-0.7, -0.2, -0.6))),
        (40, I),
    ]
    out.append(key_staff_action('BD_Spell', staff_timeline(k, 40, [ease, ease, ease_in, ease, ease])))
    # ---- BD_Hex (curse): staff swept from the right, then pointed; release at frame 15
    k = [
        (0, I),
        (9, StaffPose(**cast_body(-30, 2), staff=((0.40, 0.08, 1.38), vnorm((0.75, 0.15, 0.62)), (0.0, 1.0, 0.0)),
                      left_free=(-0.05, 0.40, 1.30), pole_l=(-0.6, -0.3, -0.6))),
        (15, StaffPose(**cast_body(26, 8, hips_yaw=6.0), staff=((0.02, 0.44, 1.36), vnorm((-0.45, 0.85, 0.4)), (0.0, -0.3, 1.0)),
                       left_free=(-0.22, 0.30, 1.42), pole_l=(-0.7, -0.2, -0.5), pole_r=(0.6, -0.4, -0.6))),
        (19, StaffPose(**cast_body(24, 6, hips_yaw=6.0), staff=((0.04, 0.42, 1.38), vnorm((-0.42, 0.85, 0.45)), (0.0, -0.3, 1.0)),
                       left_free=(-0.24, 0.26, 1.38), pole_l=(-0.7, -0.2, -0.5), pole_r=(0.6, -0.4, -0.6))),
        (34, I),
    ]
    out.append(key_staff_action('BD_Hex', staff_timeline(k, 34, [ease, swing_ease, ease, ease])))
    ARM.animation_data.action = bpy.data.actions['BD_Idle']
    return out


# ------------------------------------------------------------------ export
KEEP_PACK_BARD = ('knight_hurt_left', 'knight_front_attacked_fall', 'knight_left_attacked_fall', 'knight_right_attacked_fall')


def export_bard(path, blend_path=None):
    """Exports Rig + Bard + both staffs with the BD_* clips and the pack hit/fall clips (30 fps).
    Uses a temporary copy of the pack hit clips rescaled to 30 fps; other actions are left alone."""
    import os
    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path, copy=True)
    keep = [a for a in bpy.data.actions if a.name.startswith('BD_')]
    tmp = []
    for k in KEEP_PACK_BARD:
        src = next((a for a in bpy.data.actions if a.name.startswith('Rig.001|') and a.name.endswith(k)), None)
        if src is None: continue
        c = src.copy(); c.name = 'Rig|' + k
        for fc in _fcurves(c):
            for kp in fc.keyframe_points:
                kp.co.x *= 30.0 / 24.0; kp.handle_left.x *= 30.0 / 24.0; kp.handle_right.x *= 30.0 / 24.0
        tmp.append(c)
    hidden = []
    for a in bpy.data.actions:
        if a not in keep and a not in tmp:
            hidden.append((a, a.use_fake_user))
    # FBX "all actions" exports every action: park the others in a dummy library-free state
    stash = {}
    for a, fu in hidden:
        stash[a.name] = a
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.fps = 30
    ARM.animation_data.action = bpy.data.actions['BD_Idle']
    bpy.context.scene.frame_set(0)
    sel = [ARM, bpy.data.objects['Bard'], bpy.data.objects['BardStaff'], bpy.data.objects['BardStaffRelic']]
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects:
        if o in sel: o.hide_set(False); o.hide_viewport = False
        o.select_set(o in sel)
    bpy.context.view_layer.objects.active = ARM
    # temporarily remove the other actions (re-created from the saved blend if needed)
    removed = []
    for a, fu in hidden:
        removed.append(a.name)
        bpy.data.actions.remove(a)
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions], "removed": len(removed)}
