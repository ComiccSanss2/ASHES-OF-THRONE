# Bard v2: the staff becomes an old MANDOLIN (exec after bard_anim.py in the same namespace).
#
# Instrument frame (Unity metres, like the staff frame): origin = centre of the LEFT fist on the
# neck, +Z = along the neck toward the headstock, +X = the soundboard normal (strings side),
# +Y = Z x X (across the strings). The bowl body hangs below the fist at -Z.
# The objects keep the old names ("BardStaff", "BardStaffRelic") and the bones keep theirs
# ("StaffSocket" now on hand_L, "StaffTip" = in front of the soundhole: where the notes fly from,
# "StaffButt" = the tail of the body) so the Unity side needs no renaming.
#   G['build_bard_mesh_v2'](); G['make_mandolins'](); G['build_mando_anims'](); G['export_bard'](path)
import bpy, bmesh, math
from mathutils import Vector, Matrix

NECK_TOP = 0.07
BODY_TOP = -0.20
BODY_LEN = 0.36
HOLE_Z = BODY_TOP - 0.10
STRUM_Z = BODY_TOP - 0.07
TIP = (0.12, 0.0, HOLE_Z)


SWATCH.update({'crimson': (6, 6, 58, 58), 'obsidian': (70, 6, 122, 58), 'ember': (134, 6, 186, 58)})


def mando_parts(relic=False, dragon=False):
    """relic: blackened, gold-bound; dragon: crimson-scaled bowl, obsidian neck, glowing ember strings,
    horns on the headstock and a ridge of spines down the back of the bowl"""
    wood = Part('crimson' if dragon else 'leather' if relic else 'wood', 6.0)
    dark = Part('obsidian' if dragon else 'leather', 8.0)
    gold = Part('ember' if dragon else 'gold', 8.0)
    hole = Part('ember' if dragon else 'shadow', 4.0)
    board = Part('obsidian' if dragon else 'wood', 10.0)
    w = {'hand_L': 1.0}
    X = Vector((1, 0, 0)); Y = Vector((0, 1, 0)); Z = Vector((0, 0, 1))
    # ---- bowl body: teardrop outline across Y, flat soundboard at +X, deep ribbed bowl at -X
    prof = [(0.0, 0.035), (0.03, 0.085), (0.08, 0.125), (0.15, 0.148), (0.22, 0.142), (0.29, 0.11), (0.33, 0.065), (BODY_LEN, 0.02)]
    segs = 16
    rings = []
    for d, r in prof:
        c = Vector((0, 0, BODY_TOP - d))
        ring = []
        for j in range(segs):
            a = 2 * math.pi * j / segs
            s = math.sin(a)
            depth = 0.012 if s > 0 else 0.115 * (r / 0.148) ** 0.6
            ring.append(wood.vert(c + Y * math.cos(a) * r + X * s * depth, w))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(segs):
            wood.face((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    top = wood.vert(Vector((0, 0, BODY_TOP + 0.005)), w); bot = wood.vert(Vector((0, 0, BODY_TOP - BODY_LEN - 0.004)), w)
    for j in range(segs):
        wood.face((top, rings[0][(j + 1) % segs], rings[0][j]))
        wood.face((bot, rings[-1][j], rings[-1][(j + 1) % segs]))
    # binding around the soundboard edge (gold on the relic, dark wood otherwise)
    edge = []
    for d, r in prof:
        edge.append(Vector((0.013, r, BODY_TOP - d)))
    edge = edge + [Vector((0.013, -p.y, p.z)) for p in reversed(edge)]
    for a, b in zip(edge[:-1], edge[1:]):
        tube(gold if (relic or dragon) else dark, a, b, 0.006, 0.006, 4, w, cap=False)
    # soundhole (dark oval) with a gold rosette, a bridge and a tailpiece
    hc = Vector((0.0145, 0, HOLE_Z)); hr = 0.034
    ctr = hole.vert(hc, w)
    pts = [hole.vert(hc + Y * math.cos(2 * math.pi * k / 12) * hr + Z * math.sin(2 * math.pi * k / 12) * hr * 1.2, w) for k in range(12)]
    for k in range(12): hole.face((ctr, pts[k], pts[(k + 1) % 12]))
    ros = [hc + Vector((0.001, 0, 0)) + Y * math.cos(2 * math.pi * k / 16) * (hr + 0.008) + Z * math.sin(2 * math.pi * k / 16) * (hr + 0.008) * 1.2 for k in range(16)]
    for k in range(16): tube(gold, ros[k], ros[(k + 1) % 16], 0.004, 0.004, 4, w, cap=False)
    tube(dark, (0.02, -0.045, BODY_TOP - 0.25), (0.02, 0.045, BODY_TOP - 0.25), 0.007, 0.007, 4, w)           # bridge
    tube(gold, (0.016, -0.03, BODY_TOP - BODY_LEN + 0.03), (0.016, 0.03, BODY_TOP - BODY_LEN + 0.03), 0.008, 0.008, 4, w)   # tailpiece
    # ---- neck + fretboard + frets
    tube(wood, (-0.012, 0, BODY_TOP + 0.01), (-0.008, 0, NECK_TOP), 0.024, 0.02, 7, w)
    fb = [board.vert(Vector((0.013, sy * wdt, z)), w) for z, wdt in ((BODY_TOP - 0.06, 0.027), (NECK_TOP, 0.022)) for sy in (-1, 1)]
    board.face((fb[0], fb[1], fb[3], fb[2])); board.face((fb[0], fb[2], fb[3], fb[1]))
    for k in range(7):
        z = NECK_TOP - 0.02 - k * 0.032 * (0.94 ** k)
        tube(gold, (0.016, -0.023, z), (0.016, 0.023, z), 0.0025, 0.0025, 3, w, cap=False)
    # strings (4 courses), from the tailpiece to the nut
    for sy in (-0.012, -0.004, 0.004, 0.012):
        tube(gold, (0.022, sy, BODY_TOP - BODY_LEN + 0.03), (0.019, sy, NECK_TOP), 0.0012, 0.0012, 3, w, cap=False)
    # ---- headstock tilted back, with pegs (and the relic's ember gem)
    hb = Vector((-0.004, 0, NECK_TOP)); ht = hb + (Z - X * 0.32).normalized() * 0.13
    tube(dark if dragon else wood, hb, ht, 0.026, 0.02, 6, w)
    for k in range(4):
        p = hb.lerp(ht, 0.25 + 0.2 * k)
        for sy in (-1, 1):
            tube(dark if not relic else gold, p, p + Y * sy * 0.04, 0.0055, 0.0045, 4, w)
    if dragon:
        bone = Part('beard', 6.0); spine = Part('obsidian', 8.0)
        hd = (Z - X * 0.32).normalized()
        for sy in (-1, 1):        # two swept-back horns on the headstock
            a0 = ht - hd * 0.02 + Y * sy * 0.02
            pts = [a0, a0 + hd * 0.05 + Y * sy * 0.03 - X * 0.03, a0 + hd * 0.09 + Y * sy * 0.035 - X * 0.08, a0 + hd * 0.1 + Y * sy * 0.02 - X * 0.13]
            for i in range(3): tube(bone, pts[i], pts[i + 1], 0.013 * (1 - i * 0.3), 0.013 * (1 - (i + 1) * 0.3), 5, w)
        for k in range(6):        # spines down the bowl's back ridge
            z = BODY_TOP - 0.04 - k * 0.055
            r = [0.035, 0.085, 0.125, 0.148, 0.142, 0.11, 0.065][min(6, k + 1)]
            back = Vector((-0.115 * (r / 0.148) ** 0.6, 0, z))
            tube(spine, back, back - X * 0.045 + Z * 0.025, 0.012, 0.001, 4, w)
        c = ht + hd * 0.02
        cry = Part('ember', 4.0); h = 0.03; r = 0.016
        tu = cry.vert(c + X * h, w); td = cry.vert(c - X * 0.004, w)
        mid = [cry.vert(c + Y * math.cos(k * math.pi / 3) * r + Z * math.sin(k * math.pi / 3) * r, w) for k in range(6)]
        for k in range(6): cry.face((mid[k], mid[(k + 1) % 6], tu)); cry.face((mid[(k + 1) % 6], mid[k], td))
        return [wood, dark, gold, hole, board, bone, spine, cry]
    if relic:
        cry = Part('crystal', 4.0)
        c = ht + (Z - X * 0.32).normalized() * 0.03; h = 0.04; r = 0.02
        tu = cry.vert(c + Z * h, w); td = cry.vert(c - Z * h * 0.7, w)
        mid = [cry.vert(c + Y * math.cos(k * math.pi / 3) * r + X * math.sin(k * math.pi / 3) * r, w) for k in range(6)]
        for k in range(6): cry.face((mid[k], mid[(k + 1) % 6], tu)); cry.face((mid[(k + 1) % 6], mid[k], td))
        # sun-burst inlay on the bowl's lower bout
        for k in range(8):
            a = k * math.pi / 4
            p0 = Vector((0.016, math.cos(a) * 0.05, BODY_TOP - 0.24 + math.sin(a) * 0.05))
            tube(gold, Vector((0.016, 0, BODY_TOP - 0.24)) + (p0 - Vector((0.016, 0, BODY_TOP - 0.24))) * 0.45, p0, 0.003, 0.0015, 3, w, cap=False)
        return [wood, dark, gold, hole, board, cry]
    return [wood, dark, gold, hole, board]


def mando_frame_rest():
    arm = bpy.data.objects['Rig']
    H = arm.matrix_world @ arm.data.bones['hand_L'].matrix_local
    return H @ GRIPS['L']


def make_mando_object(name, relic, dragon=False):
    old = bpy.data.objects.get(name)
    if old: bpy.data.objects.remove(old, do_unlink=True)
    bard = bpy.data.objects['Bard']
    me = bpy.data.meshes.new(name)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = bard.matrix_world.copy()
    ob.parent = bard.parent; ob.matrix_parent_inverse = bard.matrix_parent_inverse.copy()
    ob.vertex_groups.new(name='hand_L')
    mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = bpy.data.objects['Rig']
    F = mando_frame_rest()
    parts = mando_parts(relic, dragon)
    for P in parts:
        P.v = [F @ (Vector(p) * S) for p in P.v]
    bm = bmesh.new()
    add_parts(ob, bm, parts, smooth=False)
    bm.to_mesh(me); bm.free(); me.update()
    return ob


def make_mandolins():
    make_mando_object('BardStaff', False)
    make_mando_object('BardStaffRelic', True)
    make_mando_object('BardStaffDragon', False, True)
    arm = bpy.data.objects['Rig']
    # move the three helper bones onto hand_L
    for n in ('StaffTip', 'StaffButt', 'StaffSocket'):
        if n in arm.data.bones:
            bpy.context.view_layer.objects.active = arm
            bpy.ops.object.mode_set(mode='EDIT')
            eb = arm.data.edit_bones.get(n)
            if eb: arm.data.edit_bones.remove(eb)
            bpy.ops.object.mode_set(mode='OBJECT')
    F = mando_frame_rest()
    add_bone('StaffSocket', 'hand_L', F.translation, F @ (Vector((0, 0, 0.08)) * S))
    add_bone('StaffTip', 'StaffSocket', F @ (Vector(TIP) * S), F @ (Vector((TIP[0] + 0.08, 0, TIP[2])) * S))
    add_bone('StaffButt', 'StaffSocket', F @ (Vector((0, 0, BODY_TOP - BODY_LEN)) * S), F @ (Vector((0, 0, BODY_TOP - BODY_LEN - 0.06)) * S))
    return {'ok': True}


def build_bard_mesh_v2():
    """the bard mesh without the lute on the back (he plays the mandolin now); keeps the strap"""
    G0 = globals()
    orig = G0['make_lute']
    def strap_only():
        wood, dark, strap = orig()
        return Part('wood'), Part('leather'), strap
    G0['make_lute'] = strap_only
    try:
        return build_bard_mesh()
    finally:
        G0['make_lute'] = orig


# ------------------------------------------------------------------ solver
class MandoPose(StaffPose):
    def __init__(self, **kw):
        StaffPose.__init__(self)
        self.strum = 0.0          # across the strings (instrument Y), in units of ~5 cm
        self.lift = 0.0           # right hand away from the soundboard (m)
        self.__dict__.update(kw)


def solve_mando(P):
    basis = {n: (P.base[n].copy() if P.base else Matrix.Identity(4)) for n in NAMES}
    for n in FINGERS: basis[n] = FINGER_BASIS[n].copy()
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
    g, up, kn = P.staff
    SF = frame_matrix(W(*g), W(*up).normalized(), W(*kn).normalized())
    HL = SF @ GRIPS['L'].inverted()
    over = two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', HL.translation.copy(), W(*P.pole_l), HL)
    # strumming hand: a fist holding the plectrum, the plectrum pointing into the strings
    Xi = SF.col[0].to_3d(); Yi = SF.col[1].to_3d(); Zi = SF.col[2].to_3d()
    sp = SF @ (Vector((0.075 + P.lift, P.strum * 0.05, STRUM_Z)) * S)
    RF = frame_matrix(sp, -Xi, Zi - Yi * 0.3)
    HR = RF @ GRIPS['R'].inverted()
    over = max(over, two_bone(M, basis, 'Upper_Arm_R', 'Lower_Arm_R', 'hand_R', HR.translation.copy(), W(*P.pole_r), HR))
    return basis, over


def key_mando_action(name, frames):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ARM.animation_data.action = act
    for n in NAMES: PB[n].rotation_mode = 'QUATERNION'
    prev = {}; worst = 0.0
    for i, P in enumerate(frames):
        basis, over = solve_mando(P); worst = max(worst, over)
        for n in NAMES:
            loc, q, _ = basis[n].decompose()
            if n in prev and prev[n].dot(q) < 0: q = -q
            prev[n] = q
            pb = PB[n]; pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=i); pb.keyframe_insert('rotation_quaternion', frame=i)
    act.frame_range = (0, len(frames) - 1)
    return {"name": name, "frames": len(frames), "max_overreach": round(worst, 3)}


def mix_mando(a, b, t):
    P = MandoPose()
    P.base = a.base
    P.hips = None if a.hips is None else (vlerp(a.hips[0], b.hips[0], t), lerp(a.hips[1], b.hips[1], t), lerp(a.hips[2], b.hips[2], t), lerp(a.hips[3], b.hips[3], t))
    P.spine = vlerp(a.spine, b.spine, t); P.head = vlerp(a.head, b.head, t)
    P.feet = None if a.feet is None else tuple(vlerp(a.feet[k], b.feet[k], t) for k in range(2))
    P.shoulders = vlerp(a.shoulders, b.shoulders, t)
    P.staff = (vlerp(a.staff[0], b.staff[0], t), vslerp(a.staff[1], b.staff[1], t), vslerp(a.staff[2], b.staff[2], t))
    P.pole_l = vlerp(a.pole_l, b.pole_l, t); P.pole_r = vlerp(a.pole_r, b.pole_r, t)
    P.strum = lerp(a.strum, b.strum, t); P.lift = lerp(a.lift, b.lift, t)
    return P


def mando_timeline(keys, length, easing=None):
    out = []
    for f in range(length + 1):
        for i in range(len(keys) - 1):
            fa, pa = keys[i]; fb, pb = keys[i + 1]
            if fa <= f <= fb:
                t = (f - fa) / max(1, fb - fa)
                e = (easing[i] if easing and i < len(easing) and easing[i] else ease)(t)
                out.append(mix_mando(pa, pb, e)); break
        else:
            out.append(mix_mando(keys[-1][1], keys[-1][1], 0.0))
    return out


# ------------------------------------------------------------------ poses (character space r, f, u)
HOLD_G = (-0.30, 0.34, 1.30)                 # left fist on the neck
HOLD_UP = vnorm((-0.85, 0.22, 0.48))         # neck toward the upper left
HOLD_KN = vnorm((0.08, 1.0, 0.2))            # soundboard faces forward
POLE_L = (-0.7, -0.1, -0.6)
POLE_R = (0.7, -0.5, -0.5)


def mando_idle(t=0.0, br=1.0):
    b = math.sin(t * 2 * math.pi) * br
    return MandoPose(hips=((0.0, 0.0, 0.872 - 0.004 * b), -6.0, 2.0 + 0.4 * b, 0.0), spine=(3.0, 4.0 + 1.2 * b, 0.0), feet=BARD_FEET,
                     staff=((HOLD_G[0], HOLD_G[1], HOLD_G[2] + 0.008 * b), HOLD_UP, HOLD_KN),
                     strum=0.3 + 0.15 * b, lift=0.012, pole_l=POLE_L, pole_r=POLE_R, head=(0, 3))


def build_mando_anims():
    out = []
    bpy.context.scene.render.fps = FPS
    out.append(key_mando_action('BD_Idle', [mando_idle(f / 60.0) for f in range(61)]))

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
            bob = abs(sw) * 0.02 * amp
            P = MandoPose(base=base, hips=None, spine=(-4.0 * sw, 5.0 + 6 * amp, 1.5 * sw), feet=None, head=(0, -3 * amp),
                          staff=((HOLD_G[0] + 0.02 * amp, HOLD_G[1] + 0.03 * amp, HOLD_G[2] + 0.02 * amp - bob), vnorm((-0.85, 0.22 + 0.05 * sw, 0.48 + 0.1 * amp)), HOLD_KN),
                          strum=0.2 + 0.2 * sw, lift=0.02 + 0.03 * amp, pole_l=POLE_L, pole_r=POLE_R)
            frames.append(P)
        return key_mando_action(name, frames)
    out.append(loco('BD_Walk', 'knight_walk_in_place', 40, 0.3))
    out.append(loco('BD_Run', 'knight_run_heavy_weapon_in_place', 30, 1.0))

    I = mando_idle(0.0)
    fwd_feet = ((-0.13, 0.12, 0.0, -4.0), (0.14, -0.08, 0.0, -18.0))
    # ---- BD_Cast (note bolt): hand flicks up, then a sharp downstroke at frame 13, instrument pushed forward
    k = [
        (0, I),
        (8, MandoPose(**cast_body(-14, -3), staff=((-0.30, 0.30, 1.36), vnorm((-0.8, 0.3, 0.52)), HOLD_KN),
                      strum=-1.6, lift=0.07, pole_l=POLE_L, pole_r=(0.7, -0.3, -0.3), head=(0, 2))),
        (13, MandoPose(**cast_body(12, 9, hips_yaw=4.0, feet=fwd_feet), staff=((-0.26, 0.44, 1.30), vnorm((-0.72, 0.5, 0.48)), vnorm((0.2, 1.0, 0.1))),
                       strum=1.7, lift=0.01, pole_l=POLE_L, pole_r=POLE_R, head=(0, 4))),
        (16, MandoPose(**cast_body(10, 7, hips_yaw=4.0, feet=fwd_feet), staff=((-0.26, 0.43, 1.30), vnorm((-0.72, 0.5, 0.48)), vnorm((0.2, 1.0, 0.1))),
                       strum=1.9, lift=0.05, pole_l=POLE_L, pole_r=POLE_R, head=(0, 4))),
        (30, I),
    ]
    out.append(key_mando_action('BD_Cast', mando_timeline(k, 30, [ease, ease_out, ease, ease])))
    # ---- BD_Spell (hymns, crescendo): instrument lifted high, the hand raised… a great flourish at 20
    k = [
        (0, I),
        (10, MandoPose(**cast_body(-6, -10), staff=((-0.28, 0.30, 1.62), vnorm((-0.5, 0.25, 0.85)), vnorm((0.1, 1.0, -0.2))),
                       strum=-2.2, lift=0.12, pole_l=(-0.7, 0.0, -0.4), pole_r=(0.8, 0.0, -0.2), head=(0, -14))),
        (16, MandoPose(**cast_body(-6, -12), staff=((-0.28, 0.31, 1.66), vnorm((-0.48, 0.25, 0.86)), vnorm((0.1, 1.0, -0.2))),
                       strum=-2.4, lift=0.14, pole_l=(-0.7, 0.0, -0.4), pole_r=(0.8, 0.0, -0.2), head=(0, -16))),
        (20, MandoPose(**cast_body(4, 16, drop=0.08, feet=((-0.16, 0.10, 0.0, -10.0), (0.16, -0.10, 0.0, -20.0))),
                       staff=((-0.30, 0.40, 1.16), vnorm((-0.85, 0.3, 0.3)), vnorm((0.1, 1.0, 0.35))),
                       strum=2.2, lift=0.02, pole_l=POLE_L, pole_r=(0.7, -0.6, -0.4), head=(0, 6))),
        (25, MandoPose(**cast_body(4, 14, drop=0.07, feet=((-0.16, 0.10, 0.0, -10.0), (0.16, -0.10, 0.0, -20.0))),
                       staff=((-0.30, 0.40, 1.17), vnorm((-0.85, 0.3, 0.3)), vnorm((0.1, 1.0, 0.35))),
                       strum=2.6, lift=0.10, pole_l=POLE_L, pole_r=(0.7, -0.6, -0.4), head=(0, 6))),
        (40, I),
    ]
    out.append(key_mando_action('BD_Spell', mando_timeline(k, 40, [ease, ease, ease_in, ease, ease])))
    # ---- BD_Hex (curse): a fast tremolo, then the neck is pointed at the victim like a wand (frame 15)
    k = [(0, I)]
    for i, f in enumerate(range(3, 12, 2)):
        k.append((f, MandoPose(**cast_body(-18 + 2 * i, 2), staff=((-0.32, 0.30, 1.34), vnorm((-0.8, 0.28, 0.5)), HOLD_KN),
                               strum=0.9 if i % 2 == 0 else -0.6, lift=0.02, pole_l=POLE_L, pole_r=POLE_R, head=(0, 6))))
    k += [
        (15, MandoPose(**cast_body(22, 8, hips_yaw=6.0), staff=((-0.16, 0.46, 1.38), vnorm((-0.2, 1.0, 0.25)), vnorm((-0.95, 0.15, 0.2))),
                       strum=0.0, lift=0.03, pole_l=(-0.7, -0.3, -0.5), pole_r=POLE_R, head=(0, 4))),
        (19, MandoPose(**cast_body(20, 6, hips_yaw=6.0), staff=((-0.16, 0.45, 1.39), vnorm((-0.22, 1.0, 0.28)), vnorm((-0.95, 0.15, 0.2))),
                       strum=0.2, lift=0.04, pole_l=(-0.7, -0.3, -0.5), pole_r=POLE_R, head=(0, 4))),
        (34, I),
    ]
    out.append(key_mando_action('BD_Hex', mando_timeline(k, 34, [ease] * 5 + [swing_ease, ease, ease])))
    ARM.animation_data.action = bpy.data.actions['BD_Idle']
    return out


def export_bard_v3(path, blend_path=None):
    """export_bard + the dragon mandolin (temporarily patches the selection list)"""
    import os
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
    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path, copy=True)
    for a in list(bpy.data.actions):
        if a not in keep and a not in tmp: bpy.data.actions.remove(a)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.fps = 30
    ARM.animation_data.action = bpy.data.actions['BD_Idle']
    bpy.context.scene.frame_set(0)
    sel = [ARM] + [bpy.data.objects[n] for n in ('Bard', 'BardStaff', 'BardStaffRelic', 'BardStaffDragon')]
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects:
        if o in sel: o.hide_set(False); o.hide_viewport = False; o.hide_render = False
        o.select_set(o in sel)
    bpy.context.view_layer.objects.active = ARM
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions]}
