# Reaper class for "Ashes of the Throne" (Blender 5.x), built on the Solus knight rig.
#
# Run in the Blender session that holds the knight rig ('Rig', 'Solus Knight') and the pack clips,
# AFTER gs_anim.py, archer_anim.py and bard_anim.py (same namespace G):
#     G['build_reaper_mesh'](); G['make_scythe_preview'](); G['build_reaper_anims'](); G['export_reaper'](path)
#
# 1. MESH  — the knight's body in blackened plate (helmet and pauldrons removed) under a deep black
#    hood with a bone skull-mask and two violet eye-lights, a tattered shoulder mantle, a long ragged
#    robe open at the front (the armoured legs show through), a sash with a silver clasp.
# 2. SCYTHE — built in Unity (one mesh per weapon tier). Here only the sockets: "ScytheSocket" on
#    hand_R (frame of the rear hand, +Z up the shaft toward the head, +X = the blade's direction)
#    with "ScytheHead" (head of the shaft), "ScytheButt" and "ScytheEdge" children. A preview mesh is made for
#    renders only (not exported).
# 3. ANIMS — the gs_anim solver with a polearm grip: the right hand holds the shaft in the middle,
#    the left hand 32 cm lower (L_OFFSET < 0). Light, fast, dancer-like: RP_Idle/Walk/Run,
#    RP_Attack1 (wide horizontal reap), RP_Attack2 (rising backhand), RP_Attack3 (pirouette),
#    RP_Heavy (overhead reap, the hook pulled back), RP_Ult (Harvest: two whirling turns then a
#    rising leap and a crushing reap), RP_Parry, RP_Block, RP_Roar.
import bpy, bmesh, math
from mathutils import Vector, Matrix

# ------------------------------------------------------------------ weapon geometry (Unity metres, scythe frame)
SCY_BUTT = -0.52          # below the left hand
SCY_HEAD = 1.00           # top of the shaft (above the right hand)
SCY_LEFT = -0.32          # left hand, lower on the shaft
BLADE_LEN = 0.88


def reaper_robe():
    P = Part('cloth', 4.0)
    rings = [
        (1.600, 0.150, 0.120, 0.130, 0.025),
        (1.540, 0.255, 0.175, 0.215, 0.030),
        (1.430, 0.292, 0.196, 0.228, 0.032),
        (1.300, 0.262, 0.200, 0.224, 0.032),
        (1.150, 0.236, 0.194, 0.220, 0.032),
        (1.020, 0.252, 0.190, 0.220, 0.030),
        (0.880, 0.296, 0.180, 0.226, 0.030),
        (0.680, 0.330, 0.190, 0.240, 0.030),
        (0.460, 0.352, 0.200, 0.256, 0.030),
        (0.240, 0.372, 0.210, 0.272, 0.030),
        (0.080, 0.392, 0.222, 0.286, 0.030),
    ]
    segs = 24

    def skip(i, j):
        th = 2 * math.pi * (j + 0.5) / segs
        c = math.cos(th)
        if i >= 5 and c > math.cos(math.radians(14 + 5 * (i - 5))): return True     # open front, wider toward the hem
        if i == len(rings) - 2 and (j * 7) % 5 in (0, 3): return True               # torn hem
        if i == len(rings) - 3 and (j * 11) % 7 == 0 and c < 0.2: return True
        return False
    loft(P, rings, segs, torso_weights, skip)
    return P


def reaper_mantle():
    P = Part('cloth', 5.0)
    rings = [
        (1.640, 0.140, 0.115, 0.130, 0.020),
        (1.580, 0.300, 0.190, 0.240, 0.030),
        (1.500, 0.392, 0.226, 0.272, 0.032),
        (1.400, 0.424, 0.240, 0.286, 0.034),
        (1.300, 0.436, 0.248, 0.294, 0.034),
        (1.220, 0.440, 0.250, 0.298, 0.034),
    ]
    segs = 24

    def skip(i, j):   # long rags hanging from the shoulders
        return i == len(rings) - 2 and j % 3 != 0
    loft(P, rings, segs, torso_weights, skip)
    return P


def reaper_face():
    """the shadow inside the hood, a bone skull-mask, two eye-lights"""
    sh = Part('shadow', 2.0)
    loft(sh, [(1.86, 0.06, 0.03, 0.02, -0.02), (1.80, 0.13, 0.07, 0.06, -0.02), (1.72, 0.15, 0.09, 0.08, -0.02),
              (1.64, 0.12, 0.08, 0.07, -0.01), (1.58, 0.07, 0.05, 0.05, 0.0)], 14, lambda p: {'Head': 1.0},
         cap_top=(0.0, -0.02, 1.88))
    mask = Part('beard', 6.0)
    # half-loft in front of the face: brow, cheekbones, a narrow jaw
    rings = [(1.835, 0.080, 0.105), (1.785, 0.098, 0.128), (1.730, 0.100, 0.132), (1.680, 0.092, 0.128),
             (1.630, 0.070, 0.112), (1.590, 0.045, 0.092)]
    grid = []
    segs = 10
    for z, rx, yf in rings:
        row = []
        for j in range(segs + 1):
            a = math.pi * j / segs
            x = -rx * math.cos(a)
            y = -(yf * math.sin(a) + 0.015)
            row.append(mask.vert((x, y, z), {'Head': 1.0}))
        grid.append(row)
    for i in range(len(grid) - 1):
        for j in range(segs):
            # eye sockets: holes in the second band
            if i == 1 and j in (3, 6): continue
            mask.face((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    eyes = Part('crystal', 8.0)
    for s in (1, -1):
        c = Vector((s * 0.036, -0.112, 1.758))
        a = eyes.vert(c + Vector((-0.016, 0, -0.01)), {'Head': 1.0}); b = eyes.vert(c + Vector((0.016, 0, -0.01)), {'Head': 1.0})
        d = eyes.vert(c + Vector((0.012, 0, 0.012)), {'Head': 1.0}); e = eyes.vert(c + Vector((-0.012, 0, 0.012)), {'Head': 1.0})
        eyes.face((a, b, d, e)); eyes.face((e, d, b, a))
    return sh, mask, eyes


def reaper_sash():
    P = Part('cloth', 8.0)
    loft(P, [(1.070, 0.264, 0.186, 0.238, 0.031), (0.990, 0.270, 0.190, 0.242, 0.031)], 22, torso_weights)
    tails = Part('cloth', 8.0)
    for k, x in enumerate((0.10, 0.16)):
        a = Vector((x, -0.19, 1.00)); b = Vector((x + 0.03, -0.20, 0.62 - 0.08 * k))
        q = [a + Vector((-0.03, 0, 0)), a + Vector((0.03, 0, 0)), b + Vector((0.03, 0, 0)), b + Vector((-0.03, 0, 0))]
        ids = [tails.vert(p, torso_weights(p)) for p in q]
        tails.face(ids); tails.face(tuple(reversed(ids)))
    clasp = Part('gold', 9.0)
    tube(clasp, (0.0, -0.167, 1.035), (0.0, -0.21, 1.02), 0.04, 0.03, 6, {'Spine': 1.0})
    return P, tails, clasp


def build_reaper_mesh():
    knight = bpy.data.objects['Solus Knight']
    for n in ('Reaper', 'ScythePreview'):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o, do_unlink=True)
    me = knight.data.copy(); me.name = 'Reaper'
    ob = bpy.data.objects.new('Reaper', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = knight.matrix_world.copy()
    ob.parent = knight.parent; ob.matrix_parent_inverse = knight.matrix_parent_inverse.copy()
    for g in knight.vertex_groups: ob.vertex_groups.new(name=g.name)
    for m in knight.modifiers:
        if m.type == 'ARMATURE':
            mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = m.object
    names = [g.name for g in knight.vertex_groups]
    drop = set(remove_helmet(me, names))
    for comp in islands(me):
        bones = {}
        for vi in comp:
            for g in me.vertices[vi].groups: bones[names[g.group]] = bones.get(names[g.group], 0) + g.weight
        top = max(bones, key=bones.get) if bones else ''
        if (len(comp) == 24 and top == 'Head') or (len(comp) == 54 and top.startswith('Upper_Arm')): drop.update(comp)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context='VERTS')
    parts = []
    robe = reaper_robe(); double_side(robe); parts.append(robe)
    mantle = reaper_mantle(); double_side(mantle); parts.append(mantle)
    hood = make_hood(); double_side(hood); parts.append(hood)
    sh, mask, eyes = reaper_face(); double_side(mask); parts += [sh, mask, eyes]
    sash, tails, clasp = reaper_sash(); parts += [sash, tails, clasp]
    add_parts(ob, bm, parts)
    bm.to_mesh(me); bm.free(); me.update()
    for o in ('Solus Knight', 'Archer', 'ArcherBow', 'GS_Sword', 'Bard', 'BardStaff', 'BardStaffRelic', 'BardStaffDragon'):
        x = bpy.data.objects.get(o)
        if x: x.hide_render = True; x.hide_viewport = True
    return {'reaper_verts': len(me.vertices), 'removed': len(drop)}


# ------------------------------------------------------------------ scythe (preview only) + sockets
def scythe_parts():
    wood = Part('leather', 6.0); steel = Part('gold', 6.0); edge = Part('beard', 6.0)
    w = {'hand_R': 1.0}
    tube(wood, (0, 0, SCY_BUTT), (0, 0, SCY_HEAD), 0.019, 0.017, 7, w)
    tube(steel, (0, 0, SCY_HEAD - 0.05), (0, 0, SCY_HEAD + 0.06), 0.03, 0.024, 7, w)
    pts = []
    for i in range(9):
        t = i / 8
        x = BLADE_LEN * t
        z = SCY_HEAD + 0.05 - 0.22 * t * t
        pts.append(Vector((x, 0, z)))
    for a, b in zip(pts[:-1], pts[1:]):
        tube(edge, a, b, 0.05 * (1 - 0.8 * (a.x / BLADE_LEN)), 0.05 * (1 - 0.8 * (b.x / BLADE_LEN)), 4, w, cap=False)
    return [wood, steel, edge]


def make_scythe_preview():
    old = bpy.data.objects.get('ScythePreview')
    if old: bpy.data.objects.remove(old, do_unlink=True)
    rp = bpy.data.objects['Reaper']
    me = bpy.data.meshes.new('ScythePreview')
    ob = bpy.data.objects.new('ScythePreview', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = rp.matrix_world.copy()
    ob.parent = rp.parent; ob.matrix_parent_inverse = rp.matrix_parent_inverse.copy()
    ob.vertex_groups.new(name='hand_R')
    mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = bpy.data.objects['Rig']
    F = hand_frame_rest('R')
    parts = scythe_parts()
    for P in parts: P.v = [F @ (Vector(p) * S) for p in P.v]
    bm = bmesh.new(); add_parts(ob, bm, parts, smooth=False); bm.to_mesh(me); bm.free(); me.update()
    add_bone('ScytheSocket', 'hand_R', F.translation, F @ (Vector((0, 0, 0.08)) * S))
    add_bone('ScytheHead', 'ScytheSocket', F @ (Vector((0, 0, SCY_HEAD)) * S), F @ (Vector((0, 0, SCY_HEAD + 0.06)) * S))
    add_bone('ScytheButt', 'ScytheSocket', F @ (Vector((0, 0, SCY_BUTT)) * S), F @ (Vector((0, 0, SCY_BUTT - 0.06)) * S))
    add_bone('ScytheEdge', 'ScytheSocket', F @ (Vector((0.10, 0, 0)) * S), F @ (Vector((0.16, 0, 0)) * S))
    return {'ok': True}


# ------------------------------------------------------------------ animation
RP_FEET = ((-0.12, 0.10, 0.0, -10.0), (0.14, -0.10, 0.0, -24.0))
RP_HIPS = ((0.0, 0.0, 0.860), -16.0, 2.0, 0.0)
# guard: shaft held diagonally across the body (left hand at the belly, right hand at the shoulder),
# the blade high over the right shoulder pointing forward-left
GUARD = ((0.18, 0.24, -0.10), (0.45, 0.10, 0.88), (-0.40, 1.0, 0.0))


def rbody(spine, hips_off=(0, 0, 0), hips_yaw=-16.0, pitch=2.0, feet=RP_FEET, sword=GUARD, head=(0, 0), roll=0.0):
    hp = RP_HIPS[0]
    return Pose(hips=((hp[0] + hips_off[0], hp[1] + hips_off[1], hp[2] + hips_off[2]), hips_yaw, pitch, roll),
                spine=spine, feet=feet, sword=sword, head=head)


def rfeet(dl=(0, 0), dr=(0, 0), yl=0.0, yr=0.0, liftl=0.0, liftr=0.0):
    (lr, lf, _, ly), (rr, rf, _, ry) = RP_FEET
    return ((lr + dl[0], lf + dl[1], liftl, ly + yl), (rr + dr[0], rf + dr[1], liftr, ry + yr))


def rguard(t=0.0, br=1.0):
    b = math.sin(t * 2 * math.pi) * br
    g, s, e = GUARD
    return rbody((10.0, 3.0 + 1.2 * b, -1.0), hips_off=(0, 0, -0.004 * b), sword=((g[0], g[1], g[2] + 0.01 * b), s, e))


def spin_frames(n, turns, sword, yaw0=0.0, hips_z=0.84, lift=0.0, lean=6.0, spine=(-12, 8, 4)):
    frames = []
    for f in range(n + 1):
        k = f / n
        yaw = yaw0 - 360.0 * turns * k
        hz = hips_z + lift * math.sin(math.pi * k)
        P = Pose(hips=((0.0, 0.0, hz), yaw - 10, lean, 4.0), spine=spine, look=False, sword=sword)
        a = math.radians(yaw)

        def foot(r, f_, yo, ph):
            c, s = math.cos(a), math.sin(a)
            rr = r * c - f_ * s; ff = r * s + f_ * c
            return (rr, ff, 0.03 * max(0.0, math.sin(4 * math.pi * k * turns + ph)) + lift * 0.6 * math.sin(math.pi * k), yaw + yo)
        P.feet = (foot(-0.13, 0.07, -10, 0.0), foot(0.13, -0.07, 10, math.pi))
        frames.append(P)
    return frames


def build_reaper_anims():
    globals()['L_OFFSET'] = SCY_LEFT * S
    out = []
    bpy.context.scene.render.fps = FPS
    out.append(key_action('RP_Idle', [rguard(f / 60.0) for f in range(61)]))

    # ---- walk / run (legs from the pack clips)
    def loco(name, clip, n_out, run):
        act = pack_action(clip)
        frames = []
        for f in range(n_out + 1):
            t = f / n_out
            base = pack_basis(act, t)
            Mb = pose_from_basis(base)
            dfwd = -(Mb['Foot_R'].translation.y - Mb['Foot_L'].translation.y)
            sw = max(-1.0, min(1.0, dfwd / 0.35))
            for nm in SPINE + ['Neck', 'Head']: base[nm] = Matrix.Identity(4)
            bob = math.sin(t * 4 * math.pi)
            if run:
                # scythe trailing low behind, held in the right hand; left arm swings
                P = Pose(base=base, hips=None, spine=(-6.0 - 5.0 * sw, 16.0, 3.0 * sw), feet=None, head=(0, -6),
                         sword=((0.26, 0.02, -0.36 + 0.01 * bob), (0.22, -0.80, 0.56), (0.0, -0.25, -1.0)),
                         pole_r=(0.6, 0.2, -0.9), blade_world=True,
                         left_free=(-0.24, 0.10 + 0.28 * sw, -0.40 + 0.08 * abs(sw)))
            else:
                g, s, e = GUARD
                P = Pose(base=base, hips=None, spine=(6.0 + 3.0 * math.sin(t * 2 * math.pi), 5.0, 1.5 * math.sin(t * 2 * math.pi)), feet=None,
                         sword=((g[0] + 0.015 * sw, g[1], g[2] + 0.012 * bob), s, e))
            frames.append(P)
        return key_action(name, frames)
    out.append(loco('RP_Walk', 'knight_walk_in_place', 40, False))
    out.append(loco('RP_Run', 'knight_run_heavy_weapon_in_place', 30, True))

    G0 = rguard()
    C = (0.0, 0.18, -0.16)
    # ---- 1: wide horizontal reap right → left (pivot on the left foot), swing 7–16
    k1 = [
        (0, G0),
        (6, rbody((-46, 6, 4), hips_off=(0.02, -0.02, -0.03), hips_yaw=-40, pitch=4, feet=rfeet(liftl=0.02), sword='ARC0')),
        (16, rbody((40, 10, -4), hips_off=(-0.02, 0.10, -0.06), hips_yaw=8, pitch=6, feet=rfeet(dl=(0.02, 0.20), yl=14))),
        (21, rbody((50, 8, -5), hips_off=(-0.02, 0.10, -0.05), hips_yaw=12, pitch=5, feet=rfeet(dl=(0.02, 0.20), yl=14), sword=None)),
        (32, G0),
    ]
    A1 = [(0.85, -0.40, -0.10), (0.95, 0.30, -0.18), (0.0, 1.0, -0.22), (-0.92, 0.40, -0.16), (-0.75, -0.30, -0.08)]
    out.append(key_action('RP_Attack1', attack('RP_Attack1', k1, 32, (6, 16), A1, (0.24, 0.28, 0.26), center=C)))

    # ---- 2: rising backhand low-left → high-right, the body turning through it, swing 7–15
    k2 = [
        (0, G0),
        (7, rbody((44, 14, -6), hips_off=(0.0, 0.02, -0.07), hips_yaw=6, pitch=10, feet=rfeet(dr=(0.03, 0.08), yr=-8), sword='ARC0', head=(0, 6))),
        (15, rbody((-40, -6, 6), hips_off=(0.02, 0.06, 0.0), hips_yaw=-44, pitch=0, feet=rfeet(dr=(0.03, 0.08), yr=-8, liftl=0.03), head=(0, -6))),
        (20, rbody((-46, -8, 6), hips_off=(0.02, 0.06, 0.01), hips_yaw=-48, pitch=-1, feet=rfeet(dr=(0.03, 0.08), yr=-8), sword=None)),
        (32, G0),
    ]
    A2 = [(-0.70, 0.10, -0.70), (-0.55, 0.80, -0.35), (0.15, 1.0, 0.15), (0.80, 0.50, 0.60), (0.75, -0.05, 0.75)]
    out.append(key_action('RP_Attack2', attack('RP_Attack2', k2, 32, (7, 15), A2, (0.24, 0.28, 0.28), center=C)))

    # ---- 3: pirouette — one full turn with the scythe stretched wide at the waist, swing 6–22
    SW3 = ((0.28, 0.24, -0.16), (0.96, 0.22, -0.06), (0.0, -1.0, 0.0))
    frames = []
    for f in range(7):   # gather: scythe drawn across to the right
        frames.append(mix(G0, rbody((-30, 8, 2), hips_off=(0, 0, -0.04), hips_yaw=-30, sword=SW3), ease(f / 6)))
    sp = spin_frames(16, 1.0, SW3, yaw0=-30.0 + 10.0, hips_z=0.82, lift=0.04)
    frames += sp[1:]
    last = sp[-1]
    endp = rbody((-30, 8, 2), hips_off=(0, 0, -0.04), hips_yaw=-30, sword=SW3)
    for f in range(1, 15):
        frames.append(mix(endp, G0, ease(f / 14)))
    out.append(key_action('RP_Attack3', frames))

    # ---- heavy: overhead reap — raise behind (charge hold 10–16), the hook comes down and is pulled back; swing 16–24
    kh = [
        (0, G0),
        (12, rbody((-8, -14, 0), hips_off=(0, -0.04, 0.02), hips_yaw=-18, pitch=-6, sword='ARC0', head=(0, -8))),
        (16, rbody((-6, -16, 0), hips_off=(0, -0.05, 0.02), hips_yaw=-18, pitch=-7, head=(0, -8))),
        (24, rbody((6, 26, 0), hips_off=(0, 0.14, -0.14), hips_yaw=-10, pitch=14, feet=rfeet(dl=(0.02, 0.30), yl=10, dr=(0, -0.04)))),
        (30, rbody((4, 18, 0), hips_off=(0, 0.04, -0.10), hips_yaw=-12, pitch=10, feet=rfeet(dl=(0.02, 0.30), yl=10, dr=(0, -0.04)), sword=None)),
        (44, G0),
    ]
    AH = [(0.10, -0.55, 0.82), (0.05, 0.15, 1.0), (0.0, 0.95, 0.30), (0.0, 0.70, -0.55)]
    out.append(key_action('RP_Heavy', attack('RP_Heavy', kh, 44, (16, 24), AH, (0.26, 0.30, 0.28), center=(0.0, 0.16, 0.02))))

    # ---- ULTIMATE "Harvest": crouch, two whirling turns (8–36), rising leap and a crushing reap (44–52)
    frames = []
    crouch = rbody((-30, 16, 2), hips_off=(0, -0.02, -0.12), hips_yaw=-30, pitch=12, sword=SW3, head=(0, 8))
    for f in range(9):
        frames.append(mix(G0, crouch, ease(f / 8)))
    sp = spin_frames(28, 2.0, SW3, yaw0=-20.0, hips_z=0.80, lift=0.10, lean=10.0, spine=(-16, 12, 6))
    frames += sp[1:]
    # leap: scythe raised high behind the head
    UP = ((0.04, -0.10, 0.36), (0.05, -0.55, 0.84), (0.0, 1.0, 0.25))
    top = rbody((-6, -18, 0), hips_off=(0, 0.04, 0.16), hips_yaw=-14, pitch=-8, sword=UP, head=(0, -12),
                feet=rfeet(dl=(0.0, 0.10), dr=(0.0, -0.05), liftl=0.22, liftr=0.16))
    start = sp[-1]
    for f in range(1, 9):
        P = mix(rbody((-20, 8, 0), hips_off=(0, 0, -0.04), hips_yaw=-30, sword=SW3), top, ease(f / 8))
        frames.append(P)
    # crush: blade arcs over and down in front, both feet land
    AU = [(0.05, -0.60, 0.80), (0.03, 0.20, 1.0), (0.0, 0.95, 0.25), (0.0, 0.75, -0.62)]
    land = rbody((6, 30, 0), hips_off=(0, 0.16, -0.18), hips_yaw=-10, pitch=16, feet=rfeet(dl=(0.02, 0.32), yl=10, dr=(0, -0.06)))
    for f in range(1, 9):
        k = swing_ease(f / 8)
        P = mix(top, land, ease(f / 8))
        P.sword = arc_sword(AU, k, 0.26, 0.30, 0.28, center=(0.0, 0.16, 0.02))
        frames.append(P)
    hold = mix(land, land, 0); hold.sword = arc_sword(AU, 1.0, 0.26, 0.30, 0.28, center=(0.0, 0.16, 0.02))
    for f in range(1, 5): frames.append(hold)
    for f in range(1, 15):
        P = mix(hold, G0, ease(f / 14)); P.sword = blend_sw(hold.sword, GUARD, ease(f / 14)); frames.append(P)
    out.append(key_action('RP_Ult', frames))

    # ---- parry: shaft snapped across in front, then turned outward
    PV = ((0.12, 0.38, 0.0), (0.85, 0.15, 0.50), (0.0, 1.0, 0.2))
    PO = ((0.18, 0.40, -0.05), (0.55, 0.45, 0.70), (0.4, 0.6, -0.6))
    kp = [(0, G0), (4, rbody((8, 6, 0), hips_off=(0, -0.03, -0.02), sword=PV)),
          (9, rbody((-16, 8, 2), hips_off=(0, -0.02, -0.03), hips_yaw=-26, sword=PO)), (20, G0)]
    out.append(key_action('RP_Parry', attack('RP_Parry', kp, 20, (99, 99), [(0, 1, 0), (0, 1, 0)], (0.3, 0.3, 0.3))))

    # ---- block (upper body loop): shaft held diagonally across the chest
    BL = ((0.12, 0.30, -0.04), (0.70, 0.25, 0.68), (0.0, 1.0, 0.1))
    out.append(key_action('RP_Block', [rbody((10, 6, 0), sword=((BL[0][0], BL[0][1], BL[0][2] + 0.008 * math.sin(f / 40 * 2 * math.pi)), BL[1], BL[2]))
                                       for f in range(41)]))

    # ---- roar / taunt: scythe raised high, blade forward
    RA = ((0.10, 0.16, 0.30), (0.10, 0.10, 1.0), (-0.2, 1.0, 0.0))
    kr = [(0, G0), (12, rbody((0, -14, 0), hips_off=(0, -0.02, 0.02), hips_yaw=-8, pitch=-8, sword=RA, head=(0, -12))),
          (32, rbody((0, -16, 0), hips_off=(0, -0.02, 0.02), hips_yaw=-8, pitch=-9, sword=RA, head=(0, -14))), (50, G0)]
    out.append(key_action('RP_Roar', attack('RP_Roar', kr, 50, (99, 99), [(0, 1, 0), (0, 1, 0)], (0.3, 0.3, 0.3))))
    ARM.animation_data.action = bpy.data.actions['RP_Idle']
    return out


# ------------------------------------------------------------------ export
KEEP_PACK_REAPER = ('knight_hurt_left', 'knight_front_attacked_fall', 'knight_left_attacked_fall', 'knight_right_attacked_fall')


def export_reaper(path, blend_path=None):
    import os
    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path, copy=True)
    keep = [a for a in bpy.data.actions if a.name.startswith('RP_')]
    tmp = []
    for k in KEEP_PACK_REAPER:
        ready = bpy.data.actions.get('Rig|' + k)
        if ready is not None: tmp.append(ready); continue
        src = next((a for a in bpy.data.actions if a.name.startswith('Rig.001|') and a.name.endswith(k)), None)
        if src is None: continue
        c = src.copy(); c.name = 'Rig|' + k
        for fc in _fcurves(c):
            for kp in fc.keyframe_points:
                kp.co.x *= 30.0 / 24.0; kp.handle_left.x *= 30.0 / 24.0; kp.handle_right.x *= 30.0 / 24.0
        tmp.append(c)
    others = [a for a in bpy.data.actions if a not in keep and a not in tmp]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.fps = 30
    ARM.animation_data.action = bpy.data.actions['RP_Idle']
    bpy.context.scene.frame_set(0)
    sel = [ARM, bpy.data.objects['Reaper']]
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects:
        if o in sel: o.hide_set(False); o.hide_viewport = False
        o.select_set(o in sel)
    bpy.context.view_layer.objects.active = ARM
    for a in others: bpy.data.actions.remove(a)
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions]}
