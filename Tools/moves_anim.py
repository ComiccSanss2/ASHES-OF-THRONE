# Shared moves for every class of "Ashes of the Throne" on the Solus rig (Blender 4.4+/5.x):
#   MV_Roll        forward roll (souls dodge), 20 frames, in place (the controller moves the body)
#   MV_CrouchIdle  low guard, knees bent, breathing loop (40 f)
#   MV_CrouchWalk  sneaking walk loop, feet planted with leg IK (30 f)
# Only the body (hips, spine, neck, head, legs) matters: Unity plays these on a layer whose mask
# excludes the arms, so every class keeps holding its own weapon the way its own clips do.
# Run inside the Blender session holding 'Rig', then:  export_moves(r"D:\...\Art\Moves.fbx")
import bpy, math
from mathutils import Vector, Matrix

FPS = 30
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


def W(r, f, u): return Vector((-r, -f, u))       # character (right, fwd, up) → Blender (faces -Y)
def T(v): return Matrix.Translation(v)
def Rx(deg): return Matrix.Rotation(math.radians(deg), 4, 'X')   # + = pitch forward
def ease(t): t = max(0.0, min(1.0, t)); return t * t * (3 - 2 * t)


def bend(M, basis, n, deg):
    """rotates bone n (already composed parent in M) about the body's left-right axis at its head"""
    p = PARENT[n]
    Mi = M[p] @ REL[n] @ basis[n]
    h = Mi.translation.copy()
    Md = T(h) @ Rx(deg) @ T(-h) @ Mi
    basis[n] = (M[p] @ REL[n]).inverted() @ Md
    M[n] = Md


def compose(M, basis, n):
    p = PARENT[n]
    M[n] = (M[p] @ REL[n] @ basis[n]) if p else (REL[n] @ basis[n])


def rotate_onto(Mi, pivot, cur_dir, want_dir):
    q = cur_dir.normalized().rotation_difference(want_dir.normalized())
    return T(pivot) @ q.to_matrix().to_4x4() @ T(-pivot) @ Mi


def leg_ik(M, basis, side, ankle, knee_dir, foot_pitch=0.0):
    up_n, lo_n, ft_n = 'Upper_Leg_' + side, 'Lower_Leg_' + side, 'Foot_' + side
    Mi_u = M['Hips'] @ REL[up_n] @ basis[up_n]
    A = Mi_u.translation.copy()
    l1 = (REST[lo_n].translation - REST[up_n].translation).length
    l2 = (REST[ft_n].translation - REST[lo_n].translation).length
    D = ankle - A
    d = min(max(D.length, abs(l1 - l2) + 1e-3), (l1 + l2) * 0.999)
    dirv = D.normalized()
    a = (l1 * l1 - l2 * l2 + d * d) / (2 * d)
    h = math.sqrt(max(l1 * l1 - a * a, 0.0))
    pv = knee_dir - dirv * knee_dir.dot(dirv)
    pv = pv.normalized()
    E = A + dirv * a + pv * h
    Wr = A + dirv * d
    Mu = rotate_onto(Mi_u, A, Mi_u.col[1].to_3d(), E - A)
    basis[up_n] = (M['Hips'] @ REL[up_n]).inverted() @ Mu; M[up_n] = Mu
    Mi_l = Mu @ REL[lo_n] @ basis[lo_n]
    Ml = rotate_onto(Mi_l, Mi_l.translation.copy(), Mi_l.col[1].to_3d(), Wr - E)
    basis[lo_n] = (Mu @ REL[lo_n]).inverted() @ Ml; M[lo_n] = Ml
    # foot: rest orientation (flat), optional pitch, sitting on the ankle
    Mf = T(Wr) @ Rx(foot_pitch) @ REST[ft_n].to_3x3().to_4x4()
    basis[ft_n] = (Ml @ REL[ft_n]).inverted() @ Mf; M[ft_n] = Mf
    compose(M, basis, 'Toes_' + side)


def pose_frame(hips_pos, body_pitch, spine_curl, head_curl, legs):
    """hips_pos (r,f,u); legs: ('fk', hipflex, kneeflex, footflex) or ('ik', (ankleL), (ankleR), lift…)"""
    basis = {n: Matrix.Identity(4) for n in NAMES}
    M = {}
    Mh = T(W(*hips_pos)) @ Rx(body_pitch) @ REST['Hips'].to_3x3().to_4x4()
    basis['Hips'] = REL['Hips'].inverted() @ Mh
    M['Hips'] = Mh
    for n in ('Spine', 'Spine 1', 'Chest'):
        compose(M, basis, n); bend(M, basis, n, spine_curl / 3.0)
    compose(M, basis, 'Neck'); bend(M, basis, 'Neck', head_curl * 0.4)
    compose(M, basis, 'Head'); bend(M, basis, 'Head', head_curl * 0.6)
    if legs[0] == 'fk':
        _, hip, knee, foot = legs
        for s in 'LR':
            compose(M, basis, 'Upper_Leg_' + s); bend(M, basis, 'Upper_Leg_' + s, -hip)
            compose(M, basis, 'Lower_Leg_' + s); bend(M, basis, 'Lower_Leg_' + s, knee)
            compose(M, basis, 'Foot_' + s); bend(M, basis, 'Foot_' + s, -foot)
            compose(M, basis, 'Toes_' + s)
    else:
        _, al, ar, pl, pr = legs
        knee = W(0.0, 1.0, 0.0)
        leg_ik(M, basis, 'L', W(*al) + Vector((0, 0, REST['Foot_L'].translation.z)), knee + W(-0.15, 0, 0), pl)
        leg_ik(M, basis, 'R', W(*ar) + Vector((0, 0, REST['Foot_R'].translation.z)), knee + W(0.15, 0, 0), pr)
    for n in ORDER:
        if n not in M:
            compose(M, basis, n)
    return basis


def key_action(name, bases):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    ARM.animation_data.action = act
    for n in NAMES: PB[n].rotation_mode = 'QUATERNION'
    prev = {}
    for i, basis in enumerate(bases):
        for n in NAMES:
            loc, q, _ = basis[n].decompose()
            if n in prev and prev[n].dot(q) < 0: q = -q
            prev[n] = q
            PB[n].location = loc; PB[n].rotation_quaternion = q
            PB[n].keyframe_insert('location', frame=i)
            PB[n].keyframe_insert('rotation_quaternion', frame=i)
    act.frame_range = (0, len(bases) - 1)
    return {"name": name, "frames": len(bases)}


def track(keys, f):
    """keys: [(frame, value)], eased between keys"""
    for (f0, v0), (f1, v1) in zip(keys, keys[1:]):
        if f0 <= f <= f1:
            t = ease((f - f0) / max(1e-6, f1 - f0))
            return v0 + (v1 - v0) * t
    return keys[-1][1]


HIP_U = REST['Hips'].translation.z            # 0.884
CORE = 0.24                                   # the ball's centre sits this far above the hips (body space)


def build_roll():
    # (frame, value): pitch of the whole body about the ball centre, centre height, curls, legs
    # the tuck only: the 360° turn on the ground is done in game (CharacterAnimator rotates the body about the ball)
    P = [(0, 4), (3, 14), (6, 18), (12, 18), (15, 12), (17, 6), (19, 2)]
    C = [(0, HIP_U + CORE - 0.05), (3, 0.80), (6, 0.56), (9, 0.50), (12, 0.52), (15, 0.66), (17, 0.88), (19, HIP_U + CORE - 0.04)]
    SC = [(0, 8), (3, 35), (6, 52), (12, 52), (15, 34), (17, 16), (19, 6)]
    HC = [(0, 0), (3, 22), (6, 40), (12, 40), (15, 20), (17, 4), (19, 0)]
    HF = [(0, 12), (3, 70), (6, 118), (12, 122), (15, 95), (17, 55), (19, 14)]
    KF = [(0, 18), (3, 95), (6, 145), (12, 148), (15, 120), (17, 70), (19, 22)]
    FF = [(0, 0), (3, 20), (6, 35), (12, 35), (15, 15), (19, 0)]
    out = []
    for f in range(20):
        p = track(P, f)
        c = track(C, f)
        # hips = centre + R(p)·(0,0,-CORE) in character space (rotation about the left-right axis)
        a = math.radians(p)
        hips_f = -CORE * math.sin(a) * -1.0
        hips_u = c - CORE * math.cos(a)
        out.append(pose_frame((0.0, -CORE * math.sin(a), hips_u), p, track(SC, f), track(HC, f),
                              ('fk', track(HF, f), track(KF, f), track(FF, f))))
    return key_action('MV_Roll', out)


CROUCH_U = 0.54


def build_crouch_idle():
    out = []
    for f in range(41):
        br = math.sin(f / 40 * 2 * math.pi)
        out.append(pose_frame((0.0, -0.06, CROUCH_U + 0.012 * br), 16 + 1.5 * br, 14, -24 - 1.5 * br,
                              ('ik', (-0.17, 0.16, 0.0), (0.19, -0.12, 0.0), 0.0, 0.0)))
    return key_action('MV_CrouchIdle', out)


def build_crouch_walk():
    out = []
    n = 30
    stride = 0.30
    for f in range(n + 1):
        t = f / n
        feet = []
        for side, ph in (('L', 0.0), ('R', 0.5)):
            u = (t + ph) % 1.0
            if u < 0.6:            # stance: the foot slides back under the body
                k = u / 0.6
                fwd = stride - 2 * stride * k
                lift = 0.0
            else:                   # swing: lifted and brought forward
                k = (u - 0.6) / 0.4
                fwd = -stride + 2 * stride * ease(k)
                lift = 0.10 * math.sin(math.pi * k)
            feet.append(((-0.15 if side == 'L' else 0.15), fwd, lift))
        bob = math.cos(t * 4 * math.pi)
        sway = math.sin(t * 2 * math.pi)
        out.append(pose_frame((0.02 * sway, -0.05, CROUCH_U - 0.015 + 0.015 * bob), 20, 14, -26,
                              ('ik', feet[0], feet[1], 0.0, 0.0)))
    return key_action('MV_CrouchWalk', out)


def build_all():
    bpy.context.scene.render.fps = FPS
    return [build_roll(), build_crouch_idle(), build_crouch_walk()]


def export_moves(path):
    import os
    keep = [bpy.data.actions[n] for n in ('MV_Roll', 'MV_CrouchIdle', 'MV_CrouchWalk')]
    for a in list(bpy.data.actions):
        if a not in keep: bpy.data.actions.remove(a)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    ARM.animation_data.action = keep[0]
    bpy.context.scene.frame_set(0)
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects:
        o.select_set(o == ARM)
    ARM.hide_set(False)
    bpy.context.view_layer.objects.active = ARM
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions]}
