# Greatsword animation set generator for the Solus knight rig (Blender 4.4+/5.x).
#
# Every frame is SOLVED, not hand-keyed bone by bone:
#   - the sword follows real cutting arcs around the chest (blade radial to the arc,
#     edge leading the motion), with anticipation, acceleration, follow-through, recovery;
#   - both hands are placed on the grip with analytic two-bone IK (right hand under the
#     guard, left hand 19 cm lower), wrists oriented by a grip measured from the rig's own
#     closed fists (handle axis = index→pinky knuckle line, edge aligned with the knuckles);
#   - hips/spine/chest/head carry the motion (coil → uncoil, weight shift, head stays on target);
#   - feet are planted with leg IK (steps on attacks), locomotion legs come from the pack clips.
# Output: FK actions keyed on every bone (no constraints), exported to FBX together with a
# "SwordSocket" empty parented to hand_R (Unity attaches the greatsword there).
import bpy, math
from mathutils import Vector, Matrix, Quaternion

FPS = 30
S = 1.92 / 1.85                      # Unity metres → this rig's units
ARM = bpy.data.objects['Rig']
PB = ARM.pose.bones
BN = ARM.data.bones
NAMES = [b.name for b in BN]
REST = {b.name: b.matrix_local.copy() for b in BN}
PARENT = {b.name: (b.parent.name if b.parent else None) for b in BN}
REL = {n: (REST[p].inverted() @ REST[n]) if p else REST[n].copy() for n, p in PARENT.items()}


def depth(n):
    d = 0
    while PARENT[n]:
        n = PARENT[n]; d += 1
    return d


ORDER = sorted(NAMES, key=depth)
FINGERS = [n for n in NAMES if any(k in n for k in ("Index", "Middle", "Ring", "Pinky", "Thumb"))]



def W(r, f, u):
    """character space (right, forward, up) → Blender (character faces -Y, left is +X)"""
    return Vector((-r, -f, u))


def rot(yaw=0.0, pitch=0.0, roll=0.0):
    """yaw + = turn left, pitch + = bend forward, roll + = lean right (degrees, world axes)"""
    return (Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Rotation(math.radians(pitch), 4, 'X')
            @ Matrix.Rotation(math.radians(-roll), 4, 'Y'))


def T(v):
    return Matrix.Translation(v)


def ease(t):        # smoothstep
    t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def ease_in(t, p=2.2):
    t = max(0.0, min(1.0, t)); return t ** p


def ease_out(t, p=2.2):
    t = max(0.0, min(1.0, t)); return 1 - (1 - t) ** p


def swing_ease(t):  # slow start, violent middle, quick settle (blade acceleration)
    t = max(0.0, min(1.0, t))
    return 0.5 - 0.5 * math.cos(math.pi * (t ** 1.25))


def lerp(a, b, t):
    return a + (b - a) * t


def slerp_dir(a, b, t):
    a = a.normalized(); b = b.normalized()
    d = max(-1.0, min(1.0, a.dot(b)))
    if d > 0.9995:
        return (a + (b - a) * t).normalized()
    ang = math.acos(d) * t
    c = (b - a * d).normalized()
    return (a * math.cos(ang) + c * math.sin(ang)).normalized()


def path_dir(anchors, t):
    """smooth path through unit directions (Catmull-Rom, renormalised)"""
    n = len(anchors) - 1
    x = max(0.0, min(1.0, t)) * n
    i = min(int(x), n - 1)
    u = x - i
    p0 = anchors[max(i - 1, 0)]; p1 = anchors[i]; p2 = anchors[i + 1]; p3 = anchors[min(i + 2, n)]
    u2, u3 = u * u, u * u * u
    v = 0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u2 + (-p0 + 3 * p1 - 3 * p2 + p3) * u3)
    return v.normalized()


def ortho_edge(blade, hint):
    e = hint - blade * hint.dot(blade)
    if e.length < 1e-4:
        e = Vector((0, 0, 1)) - blade * blade.z
    return e.normalized()


def frame_matrix(origin, blade, edge):
    z = blade.normalized(); x = ortho_edge(z, edge); y = z.cross(x)
    m = Matrix.Identity(4)
    m.col[0][:3] = x; m.col[1][:3] = y; m.col[2][:3] = z; m.col[3][:3] = origin
    return m


# ------------------------------------------------------------------ reference grip
def sample_basis(action, frame):
    ARM.animation_data.action = action
    bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
    return {n: PB[n].matrix_basis.copy() for n in NAMES}


def pose_from_basis(basis):
    M = {}
    for n in ORDER:
        p = PARENT[n]
        M[n] = ((M[p] @ REL[n]) if p else REL[n]) @ basis[n]
    return M


def measure_grip():
    act = bpy.data.actions[[a.name for a in bpy.data.actions if a.name.endswith('knight_idle_heavy_weapon')][0]]
    basis = sample_basis(act, act.frame_range[0])
    M = pose_from_basis(basis)
    grips = {}
    for side in ('R', 'L'):
        H = M['hand_' + side]
        def head(n): return M[n].translation.copy()
        def tail(n): return (M[n] @ Vector((0, BN[n].length, 0, 1))).to_3d()
        axis = (head('Index_1_' + side) - head('Pinky_1_' + side)).normalized()
        pts = []
        for f in ('Index', 'Middle', 'Ring', 'Pinky'):
            pts += [head(f + '_1_' + side), head(f + '_2_' + side), tail(f + '_2_' + side)]
        c = sum(pts, Vector()) / len(pts)
        handY = H.col[1].to_3d().normalized()
        sword = frame_matrix(c, axis, handY)
        grips[side] = H.inverted() @ sword            # sword frame expressed in hand space
    fingers = {n: basis[n].copy() for n in FINGERS}
    return grips, fingers


GRIPS, FINGER_BASIS = measure_grip()
L_OFFSET = -0.19 * S                                   # left hand lower on the grip


# ------------------------------------------------------------------ solver
def rotate_onto(Mi, pivot, cur_dir, want_dir):
    q = cur_dir.normalized().rotation_difference(want_dir.normalized())
    return T(pivot) @ q.to_matrix().to_4x4() @ T(-pivot) @ Mi


def two_bone(M, basis, upper, lower, end, target_pos, pole_hint, end_matrix):
    """places upper/lower so 'end' head reaches target_pos; end gets end_matrix rotation"""
    p = PARENT[upper]
    Mi_u = M[p] @ REL[upper] @ basis[upper]
    A = Mi_u.translation.copy()
    l1 = (REST[lower].translation - REST[upper].translation).length
    l2 = (REST[end].translation - REST[lower].translation).length
    D = target_pos - A
    dist = D.length
    over = max(0.0, dist - (l1 + l2) * 0.999)
    d = min(max(dist, abs(l1 - l2) + 1e-3), (l1 + l2) * 0.999)
    dirAP = D.normalized()
    a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    pv = pole_hint - dirAP * pole_hint.dot(dirAP)
    pv = pv.normalized() if pv.length > 1e-5 else Vector((0, 0, -1))
    E = A + dirAP * a + pv * h
    Wr = A + dirAP * d
    Mu = rotate_onto(Mi_u, A, Mi_u.col[1].to_3d(), E - A)
    basis[upper] = (M[p] @ REL[upper]).inverted() @ Mu
    M[upper] = Mu
    Mi_l = Mu @ REL[lower] @ basis[lower]
    Ml = rotate_onto(Mi_l, Mi_l.translation.copy(), Mi_l.col[1].to_3d(), Wr - E)
    basis[lower] = (Mu @ REL[lower]).inverted() @ Ml
    M[lower] = Ml
    if end_matrix is None:                            # free hand: follows the forearm
        He = Ml @ REL[end] @ basis[end]
    else:
        He = end_matrix.copy()
    He.translation = (Ml @ REL[end]).translation      # hand stays attached to the forearm
    basis[end] = (Ml @ REL[end]).inverted() @ He
    M[end] = He
    return over


class Pose:
    """one frame description (all optional except sword)"""
    def __init__(self, **kw):
        self.base = None          # basis dict (locomotion legs / spine from a pack clip)
        self.hips = None          # (pos(r,f,u), yaw, pitch, roll) absolute, or None = keep base
        self.spine = (0, 0, 0)    # total (yaw, pitch, roll) spread over Spine/Spine 1/Chest
        self.head = (0, 0)        # extra (yaw, pitch) on top of the counter-rotation
        self.look = True          # head counter-rotates the spine yaw (eyes on target)
        self.sword = None         # (grip(r,f,u) in chest space, blade(r,f,u), edge(r,f,u))
        self.feet = None          # ((r,f,lift,yaw) left, (r,f,lift,yaw) right) world, or None = base
        self.shoulders = (0, 0)   # shrug (deg) R, L
        self.left_free = None     # (r,f,u) chest-space target of a FREE left hand (sword one-handed)
        self.pole_r = None        # optional right elbow pole (r,f,u) chest space
        self.blade_world = False  # blade/edge given in character space (ignores torso lean)
        self.__dict__.update(kw)


SPINE = ['Spine', 'Spine 1', 'Chest']
REST_FOOT = {s: REST['Foot_' + s].to_3x3().to_4x4() for s in 'LR'}


def solve(P):
    basis = {n: (P.base[n].copy() if P.base else Matrix.Identity(4)) for n in NAMES}
    for n in FINGERS:
        basis[n] = FINGER_BASIS[n].copy()
    M = {}
    # hips
    if P.hips is not None:
        pos, yaw, pitch, roll = P.hips
        Mh = T(W(*pos)) @ rot(yaw, pitch, roll) @ REST['Hips'].to_3x3().to_4x4()
        basis['Hips'] = REL['Hips'].inverted() @ Mh
    M['Hips'] = REL['Hips'] @ basis['Hips']
    # legs
    over = 0.0
    if P.feet is not None:
        for side, (r, f, lift, fyaw) in zip('LR', P.feet):
            hyaw = P.hips[1] if P.hips else 0.0
            ankle = W(r, f, 0.0) + Vector((0, 0, REST['Foot_' + side].translation.z + lift))
            Mf = T(ankle) @ rot(fyaw) @ REST_FOOT[side]
            knee = rot(lerp(hyaw, fyaw, 0.5)).to_3x3() @ W(0.12 if side == 'R' else -0.12, 1.0, 0.0)
            two_bone(M, basis, 'Upper_Leg_' + side, 'Lower_Leg_' + side, 'Foot_' + side, ankle, knee, Mf)
            M['Toes_' + side] = M['Foot_' + side] @ REL['Toes_' + side] @ basis['Toes_' + side]
    # spine chain (yaw/pitch/roll spread evenly, rotated about each joint)
    y, p, r = P.spine
    for n in SPINE:
        par = PARENT[n]
        Mi = M[par] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        Md = T(h) @ rot(y / 3, p / 3, r / 3) @ T(-h) @ Mi
        basis[n] = (M[par] @ REL[n]).inverted() @ Md
        M[n] = Md
    # neck/head: keep the eyes level and on target
    hy = (-y * 0.85 if P.look else 0) + P.head[0]
    hp = (-p * 0.6 if P.look else 0) + P.head[1]
    for n, k in (('Neck', 0.4), ('Head', 0.6)):
        par = PARENT[n]
        Mi = M[par] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        Md = T(h) @ rot(hy * k, hp * k, -r * 0.5 * k) @ T(-h) @ Mi
        basis[n] = (M[par] @ REL[n]).inverted() @ Md
        M[n] = Md
    # shoulders (clavicles)
    for side, sh in zip('RL', P.shoulders):
        n = 'Shoulder_' + side
        Mi = M['Chest'] @ REL[n] @ basis[n]
        h = Mi.translation.copy()
        ax = Mi.col[1].to_3d().normalized()
        lift = Matrix.Rotation(math.radians(sh), 4, ax.cross(Vector((0, 0, 1))).normalized() if ax.cross(Vector((0, 0, 1))).length > 1e-4 else 'X')
        Md = T(h) @ lift @ T(-h) @ Mi
        basis[n] = (M['Chest'] @ REL[n]).inverted() @ Md
        M[n] = Md
    # sword in chest space
    Rc = (M['Chest'].to_3x3() @ REST['Chest'].to_3x3().inverted())
    ch = M['Chest'].translation.copy()
    g, b, e = P.sword
    grip = ch + Rc @ W(*g)
    Rb = Matrix.Rotation(Rc.to_euler('XYZ').z, 3, 'Z') if P.blade_world else Rc   # keep only the torso yaw
    blade = (Rb @ W(*b)).normalized()
    edge = (Rb @ W(*e)).normalized()
    SW = frame_matrix(grip, blade, edge)
    HR = SW @ GRIPS['R'].inverted()
    HL = SW @ T(Vector((0, 0, L_OFFSET))) @ GRIPS['L'].inverted()
    poleR = Rc @ W(*(P.pole_r or (0.45, -0.25, -0.85)))
    poleL = Rc @ W(-0.45, -0.25, -0.85)
    over = max(over, two_bone(M, basis, 'Upper_Arm_R', 'Lower_Arm_R', 'hand_R', HR.translation.copy(), poleR, HR))
    if P.left_free is not None:
        # one-handed carry: the left arm swings freely, elbow bent and pointing back
        tgt = ch + Rc @ W(*P.left_free)
        two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', tgt, Rc @ W(-0.35, -1.0, -0.2), None)
    else:
        over = max(over, two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', HL.translation.copy(), poleL, HL))
    return basis, over


def key_action(name, frames, loop=False):
    """frames: list of Pose; writes an FK action keyed on every bone"""
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ARM.animation_data.action = act
    for n in NAMES:
        PB[n].rotation_mode = 'QUATERNION'
    prev = {}
    worst = 0.0
    for i, P in enumerate(frames):
        basis, over = solve(P)
        worst = max(worst, over)
        for n in NAMES:
            loc, q, _ = basis[n].decompose()
            if n in prev and prev[n].dot(q) < 0: q = -q
            prev[n] = q
            pb = PB[n]
            pb.location = loc
            pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=i)
            pb.keyframe_insert('rotation_quaternion', frame=i)
    act.frame_range = (0, len(frames) - 1)
    return {"name": name, "frames": len(frames), "max_overreach": round(worst, 3)}


def pack_action(suffix):
    return bpy.data.actions[[a.name for a in bpy.data.actions if a.name.endswith(suffix)][0]]


def pack_basis(action, t01):
    a, b = action.frame_range
    return sample_basis(action, a + (b - a) * t01)


# ------------------------------------------------------------------ poses
# guard (Souls two-handed greatsword stance): grip low at the right hip, blade forward/up
GUARD_SW = ((0.10, 0.36, -0.33), (-0.28, 0.72, 0.62), (0.0, 0.3, -1.0))
STANCE_FEET = ((-0.13, 0.16, 0.0, -14.0), (0.17, -0.17, 0.0, -32.0))   # left fwd, right back
STANCE_HIPS = ((0.02, 0.0, 0.835), -22.0, 3.0, 0.0)


def guard_pose(t=0.0, breathe=1.0):
    br = math.sin(t * 2 * math.pi) * breathe
    g, b, e = GUARD_SW
    return Pose(hips=((0.02, 0.0, 0.835 - 0.006 * br), -22.0, 3.0 + 0.6 * br, 0.0),
                spine=(14.0, 4.0 + 1.5 * br, -2.0), feet=STANCE_FEET,
                sword=((g[0], g[1], g[2] + 0.012 * br), b, e))


def blend_sw(a, b, t):
    g = tuple(lerp(a[0][i], b[0][i], t) for i in range(3))
    bd = slerp_dir(Vector(a[1]), Vector(b[1]), t)
    ed = slerp_dir(Vector(a[2]), Vector(b[2]), t)
    return (g, tuple(bd), tuple(ed))


def mix(pa, pb, t):
    """interpolates two Poses (hips/spine/feet/sword)"""
    P = Pose()
    P.base = pa.base
    P.hips = None if pa.hips is None else (tuple(lerp(pa.hips[0][i], pb.hips[0][i], t) for i in range(3)),
                                           lerp(pa.hips[1], pb.hips[1], t), lerp(pa.hips[2], pb.hips[2], t), lerp(pa.hips[3], pb.hips[3], t))
    P.spine = tuple(lerp(pa.spine[i], pb.spine[i], t) for i in range(3))
    P.head = tuple(lerp(pa.head[i], pb.head[i], t) for i in range(2))
    P.feet = None if pa.feet is None else tuple(tuple(lerp(pa.feet[k][i], pb.feet[k][i], t) for i in range(4)) for k in range(2))
    P.sword = blend_sw(pa.sword, pb.sword, t)
    P.shoulders = tuple(lerp(pa.shoulders[i], pb.shoulders[i], t) for i in range(2))
    return P


def arc_sword(anchors, t, r0, r1, rmid, center=(0.0, 0.16, -0.10), tilt_fwd=0.15, sign=1.0):
    """sword on a cutting arc: hands on a circle around 'center' (chest space), blade radial,
    edge = direction of motion"""
    A = [W(*a).normalized() for a in anchors]
    u = path_dir(A, t)
    u2 = path_dir(A, min(1.0, t + 0.02)) if t < 0.98 else u + (u - path_dir(A, t - 0.02))
    motion = (u2 - u)
    if motion.length < 1e-6: motion = Vector((0, 0, -1))
    R = lerp(lerp(r0, rmid, ease(min(1, t * 2))), r1, ease(max(0, t * 2 - 1)))
    c = W(*center)
    gripB = c + u * R
    bladeB = (u + W(0, tilt_fwd, 0)).normalized()
    # back to character-space tuples for Pose.sword
    def back(v): return (-v.x, -v.y, v.z)
    return (back(gripB), back(bladeB), back(motion.normalized() * sign))


def attack(name, keys, length, swing_window, anchors, radii, center=(0.0, 0.16, -0.10)):
    """generic attack builder.
    keys: list of (frame, Pose) for body (spine/hips/feet), sword taken from anchors in the
    swing window [s0,s1] and from the keyed poses outside it"""
    frames = []
    s0, s1 = swing_window
    # sword continuity: keys on the window edges take the arc's ends, None = hold previous
    prev = GUARD_SW
    for fa, pa in keys:
        if fa == s0 or pa.sword == 'ARC0': pa.sword = arc_sword(anchors, 0.0, *radii, center=center)
        elif fa == s1: pa.sword = arc_sword(anchors, 1.0, *radii, center=center)
        elif pa.sword is None: pa.sword = prev
        prev = pa.sword
    for f in range(length + 1):
        # body: piecewise interpolation between keyed poses (eased)
        for i in range(len(keys) - 1):
            fa, pa = keys[i]; fb, pb = keys[i + 1]
            if fa <= f <= fb:
                t = (f - fa) / max(1, fb - fa)
                P = mix(pa, pb, ease(t))
                break
        else:
            P = mix(keys[-1][1], keys[-1][1], 0)
        if s0 <= f <= s1:
            t = swing_ease((f - s0) / max(1, s1 - s0))
            P.sword = arc_sword(anchors, t, *radii, center=center)
        frames.append(P)
    return frames


def body(spine, hips_off=(0, 0, 0), hips_yaw=-22.0, pitch=3.0, feet=STANCE_FEET, sword=GUARD_SW, head=(0, 0), roll=0.0):
    hp = STANCE_HIPS[0]
    return Pose(hips=((hp[0] + hips_off[0], hp[1] + hips_off[1], hp[2] + hips_off[2]), hips_yaw, pitch, roll),
                spine=spine, feet=feet, sword=sword, head=head)


def feet_step(dl=(0, 0), dr=(0, 0), yl=0.0, yr=0.0, liftl=0.0, liftr=0.0):
    (lr, lf, _, ly), (rr, rf, _, ry) = STANCE_FEET
    return ((lr + dl[0], lf + dl[1], liftl, ly + yl), (rr + dr[0], rf + dr[1], liftr, ry + yr))


# shoulder carry: grip in front of the right collarbone (chest space), blade rising back over the
# right pauldron at ~33° (character space), flat of the blade lying on the shoulder
RUN_CARRY = ((0.17, 0.19, 0.18), (0.18, -0.86, 0.42), (1.0, 0.0, 0.3))


def build_all():
    out = []
    bpy.context.scene.render.fps = FPS
    G = guard_pose()

    # ---- idle (2 s breathing loop)
    out.append(key_action('GS_Idle', [guard_pose(f / 60.0) for f in range(61)], loop=True))

    # ---- walk / run: legs and pelvis from the pack, upper body carries the sword
    def loco(name, clip, n_out, sword, spine, head_pitch=0.0):
        act = pack_action(clip)
        frames = []
        for f in range(n_out + 1):
            t = f / n_out
            base = pack_basis(act, t)
            for nm in SPINE + ['Neck', 'Head']:
                base[nm] = Matrix.Identity(4)
            P = Pose(base=base, hips=None, spine=spine(t), feet=None, sword=sword(t), head=(0, head_pitch))
            frames.append(P)
        return key_action(name, frames, loop=True)

    def walk_sw(t):
        g, b, e = GUARD_SW
        bob = math.sin(t * 4 * math.pi)
        return ((g[0] + 0.02 * math.sin(t * 2 * math.pi), g[1], g[2] + 0.015 * bob), b, e)
    out.append(loco('GS_Walk', 'knight_walk_in_place', 40, walk_sw,
                    lambda t: (6.0 + 4.0 * math.sin(t * 2 * math.pi), 6.0, 2.0 * math.sin(t * 2 * math.pi))))

    # run: greatsword resting on the right shoulder (right hand on the grip in front of the
    # shoulder, blade lying over it, tip up behind), left arm free and swinging against the legs
    RUN_SW = RUN_CARRY
    act = pack_action('knight_run_heavy_weapon_in_place')
    frames = []
    n_out = 30
    for f in range(n_out + 1):
        t = f / n_out
        base = pack_basis(act, t)
        Mb = pose_from_basis(base)
        # arm swing opposite to the legs: left hand forward when the right foot is forward
        dfwd = -(Mb['Foot_R'].translation.y - Mb['Foot_L'].translation.y)
        sw = max(-1.0, min(1.0, dfwd / 0.35))
        for nm in SPINE + ['Neck', 'Head']:
            base[nm] = Matrix.Identity(4)
        g, b, e = RUN_SW
        bob = math.sin(t * 4 * math.pi)
        P = Pose(base=base, hips=None, spine=(-6.0 - 6.0 * sw, 14.0, 3.0 * sw), feet=None, head=(0, -6),
                 sword=((g[0], g[1], g[2] + 0.012 * bob), (b[0], b[1], b[2] + 0.025 * bob), e), pole_r=(0.55, 0.15, -1.0), blade_world=True,
                 left_free=(-0.24, 0.10 + 0.26 * sw, -0.40 + 0.08 * abs(sw)))
        frames.append(P)
    out.append(key_action('GS_Run', frames, loop=True))

    # ---- light 1: rising diagonal, low-right → high-left (steps in with the left foot)
    wind1 = None
    k1 = [
        (0, G),
        (8, body((-30, 10, 4), hips_off=(0.02, -0.03, -0.03), hips_yaw=-34, pitch=6, feet=feet_step(liftl=0.02))),
        (15, body((34, 14, -4), hips_off=(-0.02, 0.10, -0.06), hips_yaw=-6, pitch=8, feet=feet_step(dl=(0, 0.22), yl=8))),
        (22, body((44, 10, -6), hips_off=(-0.02, 0.10, -0.05), hips_yaw=-2, pitch=6, feet=feet_step(dl=(0, 0.22), yl=8))),
        (42, G),
    ]
    A1 = [(0.50, -0.35, -0.62), (0.72, 0.50, -0.12), (0.0, 1.0, 0.25), (-0.55, 0.55, 0.72), (-0.55, 0.30, 0.85)]
    out.append(key_action('GS_Attack1', attack('GS_Attack1', k1, 42, (8, 22), A1, (0.28, 0.32, 0.36))))

    # ---- light 2: horizontal sweep left → right (flows out of light 1's high-left finish)
    k2 = [
        (0, G),
        (10, body((40, 4, -4), hips_off=(0, -0.02, -0.02), hips_yaw=-4, pitch=4)),
        (17, body((-36, 10, 6), hips_off=(0.02, 0.06, -0.05), hips_yaw=-38, pitch=6, feet=feet_step(dr=(0.04, 0.06), yr=-10))),
        (25, body((-52, 8, 6), hips_off=(0.02, 0.06, -0.04), hips_yaw=-44, pitch=5, feet=feet_step(dr=(0.04, 0.06), yr=-10))),
        (45, G),
    ]
    A2 = [(-0.62, -0.45, 0.45), (-0.95, 0.25, 0.10), (0.0, 1.0, -0.02), (0.95, 0.25, -0.05), (0.62, -0.60, 0.05)]
    out.append(key_action('GS_Attack2', attack('GS_Attack2', k2, 45, (10, 25), A2, (0.30, 0.30, 0.36))))

    # ---- spin: one full clockwise turn with the blade extended at chest height (loopable)
    frames = []
    n = 20
    for f in range(n + 1):
        yaw = -360.0 * f / n
        P = Pose(hips=((0.0, 0.0, 0.81), yaw - 10, 6.0, 4.0), spine=(-14, 8, 6), look=False,
                 sword=((0.30, 0.30, -0.15), (0.95, 0.25, -0.04), (0.0, -1.0, 0.0)))
        a = math.radians(yaw)
        def foot(r, f_, yo):
            v = Vector((r, f_)); c, s = math.cos(a), math.sin(a)
            # rotate (right, forward) by yaw (positive = left turn)
            rr = v.x * c - v.y * s; ff = v.x * s + v.y * c
            return (rr, ff, 0.03 * max(0.0, math.sin(4 * math.pi * f / n + (0 if yo < 0 else math.pi))), yaw + yo)
        P.feet = (foot(-0.15, 0.08, -10), foot(0.15, -0.08, 10))
        frames.append(P)
    out.append(key_action('GS_Spin', frames, loop=True))

    # ---- heavy: overhead cleave (raise behind the head = charge hold, full-body slam)
    kh = [
        (0, G),
        (16, body((-6, -14, 0), hips_off=(0, -0.04, 0.0), hips_yaw=-20, pitch=-6, sword='ARC0', head=(0, -6))),
        (20, body((-4, -16, 0), hips_off=(0, -0.05, 0.0), hips_yaw=-20, pitch=-7, head=(0, -6))),
        (27, body((6, 24, 0), hips_off=(0, 0.16, -0.14), hips_yaw=-12, pitch=14, feet=feet_step(dl=(0.02, 0.34), yl=10, dr=(0, -0.04)))),
        (36, body((6, 22, 0), hips_off=(0, 0.16, -0.13), hips_yaw=-12, pitch=12, feet=feet_step(dl=(0.02, 0.34), yl=10, dr=(0, -0.04)), sword=None)),
        (58, G),
    ]
    AH = [(0.10, -0.62, 0.78), (0.05, 0.10, 1.0), (0.0, 0.92, 0.55), (0.03, 0.98, 0.02)]
    out.append(key_action('GS_Heavy', attack('GS_Heavy', kh, 58, (20, 27), AH, (0.40, 0.48, 0.50), center=(0.0, 0.18, 0.05))))

    # ---- parry: snap the blade vertical in front, rotate it outward, back to guard
    PV = ((-0.05, 0.38, -0.05), (-0.20, 0.30, 0.93), (0.0, 1.0, 0.0))
    PO = ((0.12, 0.40, -0.05), (0.62, 0.40, 0.68), (0.4, 0.6, -0.6))
    kp = [(0, G), (4, body((8, 6, 0), hips_off=(0, -0.03, -0.02), sword=PV)),
          (9, body((-18, 8, 2), hips_off=(0, -0.02, -0.03), hips_yaw=-28, sword=PO)), (22, G)]
    out.append(key_action('GS_Parry', attack('GS_Parry', kp, 22, (99, 99), [(0, 1, 0), (0, 1, 0)], (0.3, 0.3, 0.3))))

    # ---- block (upper body loop): blade held across in front, tip up-left, edge to the enemy
    BL = ((0.05, 0.36, 0.05), (-0.85, 0.30, 0.45), (0.0, 1.0, 0.25))
    out.append(key_action('GS_Block', [body((10, 6, 0), sword=((BL[0][0], BL[0][1], BL[0][2] + 0.008 * math.sin(f / 40 * 2 * math.pi)), BL[1], BL[2]))
                                       for f in range(41)], loop=True))

    # ---- roar / taunt: sword raised high with both hands, chest arched
    RA = ((0.0, 0.18, 0.55), (0.0, 0.12, 1.0), (1.0, 0.0, 0.0))
    kr = [(0, G), (14, body((0, -16, 0), hips_off=(0, -0.02, 0.02), hips_yaw=-6, pitch=-8, sword=RA, head=(0, -12))),
          (34, body((0, -18, 0), hips_off=(0, -0.02, 0.02), hips_yaw=-6, pitch=-9, sword=RA, head=(0, -14))), (54, G)]
    out.append(key_action('GS_Roar', attack('GS_Roar', kr, 54, (99, 99), [(0, 1, 0), (0, 1, 0)], (0.3, 0.3, 0.3))))
    ARM.animation_data.action = bpy.data.actions['GS_Idle']
    return out


# ------------------------------------------------------------------ sword + socket
def make_sword():
    import bmesh
    for n in ('GS_Sword', 'SwordSocket', 'SocketTip', 'SocketEdge'):
        o = bpy.data.objects.get(n)
        if o: bpy.data.objects.remove(o)
    me = bpy.data.meshes.new('GS_Sword')
    bm = bmesh.new()
    def cyl(z0, z1, r0, r1, seg=6):
        m = Matrix.Translation((0, 0, (z0 + z1) / 2))
        bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=r0, radius2=r1, depth=z1 - z0, matrix=m)
    def Z(y): return (y + 0.03) * S
    cyl(Z(-0.36), Z(-0.26), 0.04 * S, 0.035 * S)
    cyl(Z(-0.26), Z(0.08), 0.027 * S, 0.027 * S)
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((0, 0, Z(0.11))) @ Matrix.Diagonal((0.5 * S, 0.06 * S, 0.05 * S, 1)))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=0.065 * S, radius2=0.045 * S, depth=1.32 * S,
                          matrix=Matrix.Translation((0, 0, Z(0.14) + 0.66 * S)) @ Matrix.Diagonal((1, 0.25, 1, 1)))
    bmesh.ops.create_cone(bm, cap_ends=True, segments=4, radius1=0.045 * S, radius2=0.0, depth=0.16 * S,
                          matrix=Matrix.Translation((0, 0, Z(1.46) + 0.08 * S)) @ Matrix.Diagonal((1, 0.25, 1, 1)))
    bm.to_mesh(me); bm.free()
    sw = bpy.data.objects.new('GS_Sword', me)
    bpy.context.scene.collection.objects.link(sw)
    sock = bpy.data.objects.new('SwordSocket', None)
    bpy.context.scene.collection.objects.link(sock)
    tip = bpy.data.objects.new('SocketTip', None); tip.parent = sock; tip.location = (0, 0, 1.0)
    edge = bpy.data.objects.new('SocketEdge', None); edge.parent = sock; edge.location = (0.1, 0, 0)
    for o in (tip, edge): bpy.context.scene.collection.objects.link(o)
    # constant grip: sword frame in hand_R space
    for o in (sw, sock):
        o.parent = ARM; o.parent_type = 'BONE'; o.parent_bone = 'hand_R'
    bpy.context.view_layer.update()
    H = ARM.matrix_world @ PB['hand_R'].matrix
    for o in (sw, sock):
        o.matrix_world = H @ GRIPS['R']
    return {"sword": "ok"}


KEEP_PACK = ('knight_hurt_left', 'knight_front_attacked_fall', 'knight_left_attacked_fall', 'knight_right_attacked_fall')


def export(path):
    for a in list(bpy.data.actions):
        if a.name.startswith('GS_'): continue
        if any(a.name.endswith(k) for k in KEEP_PACK): continue
        bpy.data.actions.remove(a)
    # pack clips keep their 24 fps timing when the scene is 30 fps: rescale their keys
    for a in bpy.data.actions:
        if a.name.startswith('GS_') or a.get('rescaled'): continue
        for fc in _fcurves(a):
            for kp in fc.keyframe_points:
                kp.co.x *= 30.0 / 24.0; kp.handle_left.x *= 30.0 / 24.0; kp.handle_right.x *= 30.0 / 24.0
        a['rescaled'] = 1
    import os
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.fps = 30
    ARM.animation_data.action = bpy.data.actions['GS_Idle']
    bpy.context.scene.frame_set(0)
    sel = [ARM] + [o for o in bpy.data.objects if o.type == 'MESH' and o.name != 'GS_Sword' and o.parent == ARM] + \
          [bpy.data.objects[n] for n in ('SwordSocket', 'SocketTip', 'SocketEdge')]
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects: o.select_set(o in sel)
    bpy.context.view_layer.objects.active = ARM
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions]}


def _fcurves(a):
    if hasattr(a, 'fcurves') and a.fcurves is not None and len(a.fcurves):
        return list(a.fcurves)
    out = []
    for layer in getattr(a, 'layers', []):
        for strip in layer.strips:
            for cb in strip.channelbags:
                out += list(cb.fcurves)
    return out
