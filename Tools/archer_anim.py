# Archer class for "Ashes of the Throne" (Blender 4.4+/5.x), built on the Solus knight rig.
#
# Run inside the Blender session that holds the knight rig ('Rig', 'Solus Knight') AND the
# original pack clips (import Solus_The_Knight.fbx once and delete the imported objects):
#     exec(open(r"D:\DarkFantasy\Tools\gs_anim.py").read(), G := {}); G['__file__'] = ...
#     exec(open(r"D:\DarkFantasy\Tools\archer_anim.py").read(), G)
#     G['build_archer_mesh'](); G['make_bow'](); G['build_bow_anims'](); G['export_archer'](path)
#
# 1. MESH  — a copy of the knight with the helmet crest and pauldrons removed, plus a hooded
#    cowl with a cloth face mask (only the visor's eye band shows), a shoulder mantle, a ragged
#    cloak, a quiver of arrows on the back and a leather bracer on the bow arm. New parts are
#    UV-mapped onto 4 swatches painted into the free block of the archer texture
#    (Tools/archer_texture.py → Art/Archer_Base_Color.png), so one material does everything.
# 2. BOW   — recurve war-bow (+ arrow) meshes; "BowSocket/BowTip/BowFwd" bones under hand_L and
#    "NockSocket" under hand_R tell Unity where the grip, the bow axis and the string hand are.
# 3. ANIMS — solved like the greatsword set: hands placed by two-bone IK on the bow grip and the
#    string, torso turned side-on to the target when drawing, legs from the pack clips.
import bpy, bmesh, math
from mathutils import Vector, Matrix

TEX_W = 2048.0
SWATCH = {  # name: (pixel x0, y0, x1, y1) inside the 256 px block at (1760, 688)
    'cloth': (1768, 696, 1880, 808), 'leather': (1896, 696, 2008, 808),
    'wood': (1768, 824, 1880, 936), 'feather': (1896, 822, 2008, 868), 'iron': (1896, 892, 2008, 938),
}


def tri(x):
    x = x * 0.5
    return abs((x - math.floor(x)) * 2.0 - 1.0)


def sw_uv(name, a, b):
    x0, y0, x1, y1 = SWATCH[name]
    return ((x0 + (x1 - x0) * tri(a)) / TEX_W, 1.0 - (y0 + (y1 - y0) * tri(b)) / TEX_W)


class Part:
    """geometry built in WORLD space (character faces -Y, left = +X, Z up)"""
    def __init__(self, swatch, uvscale=4.0):
        self.v = []; self.f = []; self.w = []; self.swatch = swatch; self.k = uvscale; self.uvf = []

    def vert(self, p, weights):
        self.v.append(Vector(p)); self.w.append(weights); return len(self.v) - 1

    def face(self, idx, uvs=None):
        self.f.append(tuple(idx)); self.uvf.append(uvs)


def ellipse_pt(z, rx, ryf, ryb, cy, th):
    ry = ryf * (1 + math.cos(th)) / 2 + ryb * (1 - math.cos(th)) / 2
    return (rx * math.sin(th), cy - ry * math.cos(th), z)


def loft(part, rings, segs, weights_fn, skip=lambda i, j: False, cap_top=None):
    grid = []
    for i, (z, rx, ryf, ryb, cy) in enumerate(rings):
        row = []
        for j in range(segs):
            th = 2 * math.pi * j / segs
            p = ellipse_pt(z, rx, ryf, ryb, cy, th)
            row.append(part.vert(p, weights_fn(Vector(p))))
        grid.append(row)
    for i in range(len(rings) - 1):
        for j in range(segs):
            if skip(i, j): continue
            a, b = grid[i][j], grid[i][(j + 1) % segs]
            c, d = grid[i + 1][(j + 1) % segs], grid[i + 1][j]
            part.face((a, d, c, b))     # outward normals (rings go top → bottom)
    if cap_top is not None:
        t = part.vert(cap_top, weights_fn(Vector(cap_top)))
        for j in range(segs):
            part.face((t, grid[0][j], grid[0][(j + 1) % segs]))
    return grid


def tube(part, a, b, r0, r1, segs, weights, cap=True):
    a = Vector(a); b = Vector(b)
    ax = (b - a).normalized()
    ref = Vector((0, 0, 1)) if abs(ax.z) < 0.9 else Vector((1, 0, 0))
    u = ax.cross(ref).normalized(); v = ax.cross(u)
    rings = []
    for p, r in ((a, r0), (b, r1)):
        rings.append([part.vert(p + (u * math.cos(2 * math.pi * j / segs) + v * math.sin(2 * math.pi * j / segs)) * r, weights) for j in range(segs)])
    for j in range(segs):
        part.face((rings[0][j], rings[0][(j + 1) % segs], rings[1][(j + 1) % segs], rings[1][j]))
    if cap:
        part.face(tuple(reversed(rings[0]))); part.face(tuple(rings[1]))
    return rings


def bone_head(n):
    arm = bpy.data.objects['Rig']
    return arm.matrix_world @ arm.data.bones[n].head_local


def bone_tail(n):
    arm = bpy.data.objects['Rig']
    return arm.matrix_world @ arm.data.bones[n].tail_local


# ------------------------------------------------------------------ archer parts
def make_hood():
    P = Part('cloth', 6.0)
    rings = [  # z, rx, ry_front, ry_back, centre y
        (1.935, 0.070, 0.090, 0.080, 0.070),
        (1.900, 0.128, 0.170, 0.145, 0.020),
        (1.850, 0.152, 0.218, 0.192, 0.000),
        (1.780, 0.158, 0.230, 0.205, 0.000),
        (1.712, 0.155, 0.226, 0.200, 0.000),
        (1.648, 0.148, 0.205, 0.190, 0.000),
        (1.600, 0.160, 0.185, 0.195, 0.020),
        (1.565, 0.215, 0.192, 0.205, 0.030),
        (1.515, 0.262, 0.200, 0.222, 0.030),
        (1.455, 0.288, 0.210, 0.236, 0.030),
        (1.415, 0.296, 0.214, 0.242, 0.030),
    ]
    segs = 20

    def wfn(p):
        if p.z >= 1.64: return {'Head': 1.0}
        if p.z >= 1.585: return {'Head': 0.5, 'Neck': 0.5}
        side = 'Shoulder_L' if p.x > 0 else 'Shoulder_R'
        s = max(0.0, min(0.45, (abs(p.x) - 0.2) / 0.3))
        return {'Chest': 1.0 - s, side: s} if s > 0 else {'Chest': 1.0}

    def skip(i, j):
        # eye band opening: between rings 1.85 and 1.712 (i = 2, 3), front ±55°
        th = 2 * math.pi * (j + 0.5) / segs
        return i in (2, 3) and math.cos(th) > math.cos(math.radians(52))
    loft(P, rings, segs, wfn, skip, cap_top=(0.0, 0.15, 1.965))
    return P


def make_cloak():
    P = Part('cloth', 5.0)
    cols, rows = 9, 9
    grid = []
    for i in range(rows):
        t = i / (rows - 1)
        z = 1.50 - 0.86 * t
        row = []
        for j in range(cols):
            u = -1 + 2 * j / (cols - 1)
            w = 0.27 + 0.10 * t
            zz = z - (0.07 if (i == rows - 1 and j % 2 == 1) else 0.0)     # ragged hem
            y = 0.205 + 0.13 * t + 0.07 * (1 - u * u) * (0.4 + 0.6 * t)
            if zz > 1.36: wt = {'Chest': 1.0}
            elif zz > 1.12: k = (1.36 - zz) / 0.24; wt = {'Chest': 1 - k, 'Spine 1': k}
            elif zz > 0.92: k = (1.12 - zz) / 0.2; wt = {'Spine 1': 1 - k, 'Spine': k}
            else: k = min(1.0, (0.92 - zz) / 0.2); wt = {'Spine': 1 - k, 'Hips': k}
            row.append(P.vert((u * w, y, zz), wt))
        grid.append(row)
    for i in range(rows - 1):
        for j in range(cols - 1):
            P.face((grid[i][j], grid[i][j + 1], grid[i + 1][j + 1], grid[i + 1][j]))
    return P


def make_quiver():
    P = Part('leather', 5.0)
    B = Vector((0.10, 0.315, 1.04)); T_ = Vector((-0.13, 0.315, 1.58))
    w = {'Chest': 1.0}
    tube(P, B, T_, 0.058, 0.066, 10, w)
    rim = tube(P, T_ - (T_ - B).normalized() * 0.04, T_ + (T_ - B).normalized() * 0.005, 0.07, 0.072, 10, w)
    return P, B, T_


def make_quiver_arrows(B, T_):
    shafts = Part('wood', 3.0); fl = Part('feather', 3.0)
    ax = (T_ - B).normalized()
    ref = Vector((0, 1, 0)); u = ax.cross(ref).normalized(); v = ax.cross(u)
    w = {'Chest': 1.0}
    for k, (a, b) in enumerate(((0.02, 0.0), (-0.025, 0.02), (0.0, -0.028), (0.03, 0.03), (-0.03, -0.02), (0.012, 0.035))):
        base = T_ + u * a + v * b
        tip = base + (ax + u * a * 2 + v * b * 2).normalized() * (0.17 + 0.02 * (k % 3))
        tube(shafts, base - ax * 0.05, tip, 0.006, 0.006, 4, w, cap=False)
        d = (tip - base).normalized()
        for s in (u, v):
            q = [tip - d * 0.11 + s * 0.0, tip - d * 0.11 + s * 0.026, tip - d * 0.02 + s * 0.018, tip - d * 0.02]
            ids = [fl.vert(p, w) for p in q]
            fl.face(ids); fl.face(tuple(reversed(ids)))
    return shafts, fl


def make_bracer():
    P = Part('leather', 6.0)
    a = bone_head('Lower_Arm_L'); b = bone_tail('Lower_Arm_L')
    tube(P, a.lerp(b, 0.32), a.lerp(b, 0.86), 0.066, 0.058, 8, {'Lower_Arm_L': 1.0})
    return P


def double_side(P, inset=0.007):
    """adds a slightly inset back face for every face (cloth seen from inside)"""
    normals = {}
    for f in P.f:
        pts = [P.v[i] for i in f]
        n = (pts[1] - pts[0]).cross(pts[2] - pts[0])
        if n.length < 1e-9: continue
        n.normalize()
        for i in f: normals[i] = normals.get(i, Vector()) + n
    remap = {}
    for i, n in normals.items():
        remap[i] = P.vert(P.v[i] - n.normalized() * inset, P.w[i])
    for f in list(P.f):
        if all(i in remap for i in f):
            P.face(tuple(remap[i] for i in reversed(f)))


# ------------------------------------------------------------------ assemble
def islands(me):
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    seen = set(); out = []
    for v in bm.verts:
        if v.index in seen: continue
        st = [v]; comp = []; seen.add(v.index)
        while st:
            x = st.pop(); comp.append(x.index)
            for e in x.link_edges:
                o = e.other_vert(x)
                if o.index not in seen: seen.add(o.index); st.append(o)
        out.append(comp)
    bm.free()
    return out


def build_archer_mesh():
    knight = bpy.data.objects['Solus Knight']
    old = bpy.data.objects.get('Archer')
    if old: bpy.data.objects.remove(old, do_unlink=True)
    me = knight.data.copy(); me.name = 'Archer'
    ob = bpy.data.objects.new('Archer', me)
    bpy.context.scene.collection.objects.link(ob)
    ob.matrix_world = knight.matrix_world.copy()
    ob.parent = knight.parent; ob.matrix_parent_inverse = knight.matrix_parent_inverse.copy()
    for g in knight.vertex_groups: ob.vertex_groups.new(name=g.name)
    for m in knight.modifiers:
        if m.type == 'ARMATURE':
            mm = ob.modifiers.new('Armature', 'ARMATURE'); mm.object = m.object
    # ---- remove the helmet crest and the pauldrons (hood and mantle replace them)
    names = [g.name for g in knight.vertex_groups]
    drop = set()
    for comp in islands(me):
        bones = {}
        for vi in comp:
            for g in me.vertices[vi].groups: bones[names[g.group]] = bones.get(names[g.group], 0) + g.weight
        top = max(bones, key=bones.get) if bones else ''
        n = len(comp)
        if (n == 24 and top == 'Head') or (n == 54 and top.startswith('Upper_Arm')):
            drop.update(comp)
    bm = bmesh.new(); bm.from_mesh(me); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in drop], context='VERTS')
    # ---- new parts
    parts = []
    hood = make_hood(); double_side(hood); parts.append(hood)
    cloak = make_cloak(); double_side(cloak); parts.append(cloak)
    q, B, T_ = make_quiver(); parts.append(q)
    sh, fl = make_quiver_arrows(B, T_); parts += [sh, fl]
    parts.append(make_bracer())
    inv = ob.matrix_world.inverted()
    uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    dl = bm.verts.layers.deform.verify()
    gidx = {g.name: g.index for g in ob.vertex_groups}
    added = 0
    for P in parts:
        vs = []
        for p, wts in zip(P.v, P.w):
            v = bm.verts.new(inv @ p)
            for bn, wv in wts.items(): v[dl][gidx[bn]] = wv
            vs.append(v)
        bm.verts.ensure_lookup_table()
        for fi, f in enumerate(P.f):
            try:
                face = bm.faces.new([vs[i] for i in f])
            except ValueError:
                continue
            face.material_index = 0
            face.smooth = True
            for loop in face.loops:
                p = P.v[f[list(face.verts).index(loop.vert)]]
                # planar-ish projection along the dominant normal axis keeps texel density even
                n = face.normal
                if abs(n.z) > 0.7: a, b = p.x, p.y
                elif abs(n.x) > abs(n.y): a, b = p.y, p.z
                else: a, b = p.x, p.z
                loop[uvl].uv = sw_uv(P.swatch, a * P.k, b * P.k)
        added += len(P.v)
    bm.to_mesh(me); bm.free()
    me.update()
    knight.hide_render = True; knight.hide_viewport = True
    return {'archer_verts': len(me.vertices), 'added': added, 'removed': len(drop)}


def preview_material(tex_path):
    m = bpy.data.materials.get('ArcherPreview') or bpy.data.materials.new('ArcherPreview')
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    tex = next((n for n in nt.nodes if n.type == 'TEX_IMAGE'), None) or nt.nodes.new('ShaderNodeTexImage')
    tex.image = bpy.data.images.load(tex_path, check_existing=True)
    tex.image.reload()
    nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
    for name in ('Archer', 'Bow', 'Arrow'):
        o = bpy.data.objects.get(name)
        if o and o.type == 'MESH':
            o.data.materials.clear(); o.data.materials.append(m)
    return m


# ------------------------------------------------------------------ preview renders
def preview(views=((0, 1.25), (35, 1.25), (180, 1.25), (90, 1.25)), dist=3.0, out=r"D:\DarkFantasy\Logs\arch_{i}.png", frame=None, action=None, lens=55, size=(420, 620), target_z=None):
    import os
    sc = bpy.context.scene
    arm = bpy.data.objects['Rig']
    if action: arm.animation_data.action = bpy.data.actions[action]
    if frame is not None: sc.frame_set(int(frame))
    cam = bpy.data.objects.get('CamArch')
    if cam is None:
        cam = bpy.data.objects.new('CamArch', bpy.data.cameras.new('CamArch'))
        sc.collection.objects.link(cam)
    cam.data.lens = lens
    sc.camera = cam
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = size
    paths = []
    for i, (yaw, z) in enumerate(views):
        a = math.radians(yaw)
        # yaw 0 = in front of the character (character faces -Y)
        tz = target_z if target_z is not None else z
        pos = Vector((math.sin(a) * dist, -math.cos(a) * dist, z))
        cam.location = pos
        d = Vector((0, 0, tz)) - pos
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = out.format(i=i)
        bpy.ops.render.render(write_still=True)
        paths.append(sc.render.filepath)
    return paths


# ------------------------------------------------------------------ bow
# Bow frame (same convention as the greatsword grip): origin = centre of the left fist on the
# grip, +Z = up along the bow, +X = toward the target, +Y = Z × X (the arrow-rest side).
BOW_LEN = 1.30          # Unity metres, tip to tip
BRACE = 0.17            # grip → string at rest
def bow_centerline(n=12):
    """top limb centre line in bow-frame metres (x fwd, z up), from the riser to the tip"""
    pts = []
    half = BOW_LEN / 2
    for i in range(n + 1):
        t = i / n
        z = 0.09 + (half - 0.09) * t
        x = -0.15 * (t ** 1.7)
        if t > 0.82:                       # recurve: the tip flicks back toward the target
            u = (t - 0.82) / 0.18
            x += 0.07 * u * u
        pts.append((x, z))
    return pts


def bow_parts():
    """returns (Part wood, Part grip-leather, top nock (x,z), bottom nock) in bow-frame metres"""
    wood = Part('wood', 7.0); grip = Part('leather', 9.0)
    w = {'hand_L': 1.0}
    cl = bow_centerline()
    def section(x, z, wy, tx):
        return [(x + tx * 0.5, -wy * 0.5, z), (x + tx * 0.5, wy * 0.5, z), (x - tx * 0.5, wy * 0.5, z), (x - tx * 0.5, -wy * 0.5, z)]
    for sgn in (1, -1):
        rings = []
        for i, (x, z) in enumerate(cl):
            t = i / (len(cl) - 1)
            wy = 0.046 - 0.026 * t; tx = 0.032 - 0.016 * t
            rings.append([wood.vert((p[0], p[1], p[2] * sgn), w) for p in section(x, z, wy, tx)])
        for i in range(len(rings) - 1):
            for j in range(4):
                a, b = rings[i][j], rings[i][(j + 1) % 4]; c, d = rings[i + 1][(j + 1) % 4], rings[i + 1][j]
                wood.face((a, b, c, d) if sgn > 0 else (a, d, c, b))
        wood.face(tuple(rings[-1]) if sgn > 0 else tuple(reversed(rings[-1])))
    # riser (thicker, wrapped in leather where the fist holds it)
    r = [grip.vert(p, w) for p in section(0.004, -0.095, 0.036, 0.040)] + [grip.vert(p, w) for p in section(0.004, 0.095, 0.036, 0.040)]
    for j in range(4):
        grip.face((r[j], r[4 + j], r[4 + (j + 1) % 4], r[(j + 1) % 4]))
    grip.face(tuple(reversed(r[:4]))); grip.face(tuple(r[4:]))
    tip = cl[-1]
    return wood, grip, (tip[0] - 0.006, tip[1] - 0.012), (tip[0] - 0.006, -(tip[1] - 0.012))


def hand_frame_rest(side):
    """sword/bow frame of a fist in the REST pose (world)"""
    arm = bpy.data.objects['Rig']
    H = arm.matrix_world @ arm.data.bones['hand_' + side].matrix_local
    return H @ GRIPS[side]


def add_bone(name, parent, head, tail, roll_ref=None):
    arm = bpy.data.objects['Rig']
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    inv = arm.matrix_world.inverted()
    for o in bpy.context.view_layer.objects: o.select_set(False)
    arm.hide_set(False); arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.object.mode_set(mode='EDIT')
        eb = arm.data.edit_bones
        if name in eb: eb.remove(eb[name])
        b = eb.new(name)
        b.head = inv @ Vector(head); b.tail = inv @ Vector(tail)
        b.parent = eb[parent]
        b.use_deform = False
        bpy.ops.object.mode_set(mode='OBJECT')


def make_bow():
    """adds the bow to the Archer mesh (rigid on hand_L) and the socket bones Unity reads"""
    ob = bpy.data.objects['Archer']; me = ob.data
    F = hand_frame_rest('L')                       # bow frame at rest, world (rig units)
    s = S                                          # Unity metres → rig units
    def wp(p): return F @ (Vector(p) * s)
    wood, grip, top, bot = bow_parts()
    bm = bmesh.new(); bm.from_mesh(me)
    uvl = bm.loops.layers.uv.active
    dl = bm.verts.layers.deform.verify()
    gi = ob.vertex_groups['hand_L'].index
    inv = ob.matrix_world.inverted()
    for P in (wood, grip):
        vs = []
        for p in P.v:
            v = bm.verts.new(inv @ wp(p)); v[dl][gi] = 1.0; vs.append(v)
        for f in P.f:
            try: face = bm.faces.new([vs[i] for i in f])
            except ValueError: continue
            face.smooth = False
            for loop in face.loops:
                p = P.v[f[list(face.verts).index(loop.vert)]]
                loop[uvl].uv = sw_uv(P.swatch, (p[0] + p[1]) * P.k, p[2] * P.k * 0.6)
    bm.to_mesh(me); bm.free(); me.update()
    # ---- sockets: grip frame + string ends on hand_L, string pinch point on hand_R
    o = wp((0, 0, 0))
    add_bone('BowSocket', 'hand_L', o, wp((0, 0, 0.08)))
    add_bone('BowFwd', 'BowSocket', wp((0.10, 0, 0)), wp((0.18, 0, 0)))
    add_bone('BowTop', 'BowSocket', wp((top[0], 0, top[1])), wp((top[0], 0, top[1] + 0.05)))
    add_bone('BowBottom', 'BowSocket', wp((bot[0], 0, bot[1])), wp((bot[0], 0, bot[1] - 0.05)))
    FR = hand_frame_rest('R')
    add_bone('NockSocket', 'hand_R', FR.translation, FR @ (Vector((0, 0, 0.06)) * s))
    return {'bow_top': list(top), 'bow_bottom': list(bot)}


# ------------------------------------------------------------------ bow animation solver
class BowPose(Pose):
    def __init__(self, **kw):
        self.bow = None          # (grip(r,f,u), up(r,f,u), aim(r,f,u)) in CHARACTER space (absolute, rig units)
        self.nock = None         # (r,f,u) string pinch point for the right hand (drawn), or None
        self.right_free = None   # (r,f,u) free right-hand target when not on the string
        self.pole_l = (-0.35, 0.0, -1.0)
        self.pole_r = (0.55, -1.0, 0.05)
        Pose.__init__(self, **kw)


def solve_bow(P):
    basis = {n: (P.base[n].copy() if P.base else Matrix.Identity(4)) for n in NAMES}
    for n in FINGERS:
        basis[n] = FINGER_BASIS[n].copy()
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
    hy = (-(y + (P.hips[1] if P.hips else 0.0)) * 0.9 if P.look else 0) + P.head[0]
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
    g, up, aim = P.bow
    BF = frame_matrix(W(*g), W(*up).normalized(), W(*aim).normalized())
    HL = BF @ GRIPS['L'].inverted()
    over = max(over, two_bone(M, basis, 'Upper_Arm_L', 'Lower_Arm_L', 'hand_L', HL.translation.copy(), W(*P.pole_l), HL))
    if P.nock is not None:
        NF = frame_matrix(W(*P.nock), W(*up).normalized(), W(*aim).normalized())
        HR = NF @ GRIPS['R'].inverted()
        over = max(over, two_bone(M, basis, 'Upper_Arm_R', 'Lower_Arm_R', 'hand_R', HR.translation.copy(), W(*P.pole_r), HR))
    else:
        rf = P.right_free or (0.24, 0.04, 0.92)
        two_bone(M, basis, 'Upper_Arm_R', 'Lower_Arm_R', 'hand_R', W(*rf), W(*P.pole_r), None)
    return basis, over


def key_bow_action(name, frames):
    act = bpy.data.actions.get(name)
    if act: bpy.data.actions.remove(act)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    ARM.animation_data.action = act
    for n in NAMES: PB[n].rotation_mode = 'QUATERNION'
    prev = {}; worst = 0.0
    for i, P in enumerate(frames):
        basis, over = solve_bow(P); worst = max(worst, over)
        for n in NAMES:
            loc, q, _ = basis[n].decompose()
            if n in prev and prev[n].dot(q) < 0: q = -q
            prev[n] = q
            pb = PB[n]; pb.location = loc; pb.rotation_quaternion = q
            pb.keyframe_insert('location', frame=i); pb.keyframe_insert('rotation_quaternion', frame=i)
    act.frame_range = (0, len(frames) - 1)
    return {"name": name, "frames": len(frames), "max_overreach": round(worst, 3)}


def vlerp(a, b, t): return tuple(a[i] + (b[i] - a[i]) * t for i in range(len(a)))
def vnorm(v):
    l = math.sqrt(sum(x * x for x in v)) or 1.0
    return tuple(x / l for x in v)
def vslerp(a, b, t): return tuple(slerp_dir(Vector(a), Vector(b), t))


def mix_bow(a, b, t):
    """interpolates two BowPoses (positions linear, directions slerped)"""
    P = BowPose()
    P.base = a.base
    P.hips = None if a.hips is None else (vlerp(a.hips[0], b.hips[0], t), lerp(a.hips[1], b.hips[1], t), lerp(a.hips[2], b.hips[2], t), lerp(a.hips[3], b.hips[3], t))
    P.spine = vlerp(a.spine, b.spine, t); P.head = vlerp(a.head, b.head, t)
    P.feet = None if a.feet is None else tuple(vlerp(a.feet[k], b.feet[k], t) for k in range(2))
    P.shoulders = vlerp(a.shoulders, b.shoulders, t)
    P.bow = (vlerp(a.bow[0], b.bow[0], t), vslerp(a.bow[1], b.bow[1], t), vslerp(a.bow[2], b.bow[2], t))
    P.pole_l = vlerp(a.pole_l, b.pole_l, t); P.pole_r = vlerp(a.pole_r, b.pole_r, t)
    if a.nock is not None and b.nock is not None: P.nock = vlerp(a.nock, b.nock, t)
    else:
        # hand travelling between the string and a free position: blend the two targets
        pa = a.nock if a.nock is not None else (a.right_free or (0.24, 0.04, 0.92))
        pb = b.nock if b.nock is not None else (b.right_free or (0.24, 0.04, 0.92))
        p = vlerp(pa, pb, t)
        if (a.nock is not None and t < 0.25) or (b.nock is not None and t > 0.75): P.nock = p
        else: P.right_free = p
    return P


def timeline(keys, length, easing=None):
    """keys: [(frame, BowPose)] → list of BowPoses for every frame (eased segments)"""
    out = []
    for f in range(length + 1):
        for i in range(len(keys) - 1):
            fa, pa = keys[i]; fb, pb = keys[i + 1]
            if fa <= f <= fb:
                t = (f - fa) / max(1, fb - fa)
                e = (easing[i] if easing and i < len(easing) and easing[i] else ease)(t)
                out.append(mix_bow(pa, pb, e)); break
        else:
            out.append(mix_bow(keys[-1][1], keys[-1][1], 0.0))
    return out


# ------------------------------------------------------------------ archer poses (character space)
# relaxed: bow low in the left hand, top limb forward-up, right hand loose by the thigh
ARCH_FEET = ((-0.12, 0.05, 0.0, -6.0), (0.13, -0.04, 0.0, -14.0))
ARCH_HIPS = ((0.0, 0.0, 0.875), -8.0, 2.0, 0.0)
CARRY = ((-0.29, 0.10, 0.90), (-0.05, 0.38, 0.92), (0.0, 0.92, -0.38))
# aiming: side-on, bow arm straight at the target, string drawn to the jaw
AIM_G = (-0.05, 0.66, 1.56)
AIM_UP = (-0.12, 0.0, 1.0)
AIM_DIR = (0.0, 1.0, 0.0)
def aim_nock(draw):           # string point for a given draw length (metres, rig units)
    return (AIM_G[0] + 0.012, AIM_G[1] - draw, AIM_G[2] + 0.02)
SHOT_FEET = ((-0.06, 0.20, 0.0, -48.0), (0.10, -0.16, 0.0, -78.0))
QUIVER_REACH = (0.16, -0.20, 1.66)


def idle_pose(t=0.0, br=1.0):
    b = math.sin(t * 2 * math.pi) * br
    g, u, a = CARRY
    return BowPose(hips=(vlerp(ARCH_HIPS[0], (0, 0, ARCH_HIPS[0][2] - 0.005 * b), 1.0) if False else (0.0, 0.0, 0.875 - 0.005 * b), -8.0, 2.0 + 0.5 * b, 0.0),
                   spine=(4.0, 3.0 + 1.2 * b, 0.0), feet=ARCH_FEET,
                   bow=((g[0], g[1], g[2] + 0.01 * b), u, a), right_free=(0.25, 0.05, 0.93 + 0.008 * b), pole_r=(0.4, -1.0, 0.0))


def aim_pose(draw, torso=-62.0, hips=-42.0, nock=True, right=None, bow_g=AIM_G, lean=0.0):
    P = BowPose(hips=((0.02, -0.01, 0.865), hips, 1.0, lean), spine=(torso - hips, 2.0, -lean * 0.5), feet=SHOT_FEET,
                bow=(bow_g, AIM_UP, AIM_DIR), pole_l=(-0.2, -0.3, -1.0), pole_r=(0.6, -1.0, 0.25), head=(0, 0))
    if nock: P.nock = aim_nock(draw)
    else: P.right_free = right
    return P


def build_bow_anims():
    out = []
    bpy.context.scene.render.fps = FPS
    out.append(key_bow_action('BW_Idle', [idle_pose(f / 60.0) for f in range(61)]))

    def loco(name, clip, n_out, amp, bow_fwd):
        act = pack_action(clip)
        frames = []
        for f in range(n_out + 1):
            t = f / n_out
            base = pack_basis(act, t)
            Mb = pose_from_basis(base)
            dfwd = -(Mb['Foot_R'].translation.y - Mb['Foot_L'].translation.y)
            sw = max(-1.0, min(1.0, dfwd / 0.35))
            for nm in SPINE + ['Neck', 'Head']: base[nm] = Matrix.Identity(4)
            g, u, a = CARRY
            P = BowPose(base=base, hips=None, spine=(-4.0 * sw, 6.0 + 4 * amp, 1.5 * sw), feet=None, head=(0, -3 * amp),
                        bow=((g[0] - 0.02 * amp, g[1] + bow_fwd + 0.10 * amp * sw, g[2] + 0.05 * amp), vnorm((-0.05, 0.38 + 0.25 * amp, 0.92)), vnorm((0.0, 0.92, -0.38 - 0.25 * amp))),
                        right_free=(0.25 - 0.02 * amp, 0.04 - 0.16 * amp * sw, 0.93 + 0.07 * amp * abs(sw)), pole_r=(0.3, -1.0, -0.2))
            frames.append(P)
        return key_bow_action(name, frames)
    out.append(loco('BW_Walk', 'knight_walk_in_place', 40, 0.4, 0.0))
    out.append(loco('BW_Run', 'knight_run_heavy_weapon_in_place', 30, 1.0, 0.06))

    I = idle_pose(0.0)
    # ---- quick shot: raise → hand to the string → draw → loose → recover (release at frame 15)
    k = [
        (0, I),
        (6, aim_pose(0.0, torso=-40, hips=-30, nock=False, right=QUIVER_REACH, bow_g=(-0.10, 0.52, 1.42))),
        (9, aim_pose(0.10, torso=-55, hips=-38)),
        (14, aim_pose(0.60, torso=-64, hips=-42)),
        (15, aim_pose(0.62, torso=-64, hips=-42)),
        (17, aim_pose(0.0, torso=-62, hips=-42, nock=False, right=(0.22, -0.18, 1.62), bow_g=(-0.05, 0.68, 1.55))),
        (21, aim_pose(0.0, torso=-60, hips=-40, nock=False, right=(0.22, -0.10, 1.45), bow_g=(-0.06, 0.66, 1.53))),
        (30, I),
    ]
    out.append(key_bow_action('BW_Shot', timeline(k, 30, [ease, ease, ease_out, None, ease_out, ease, ease])))
    # ---- power shot: slow full draw, held at frame 24 while charging, release at frame 26
    k = [
        (0, I),
        (8, aim_pose(0.0, torso=-40, hips=-30, nock=False, right=QUIVER_REACH, bow_g=(-0.10, 0.50, 1.40))),
        (12, aim_pose(0.10, torso=-56, hips=-40)),
        (24, aim_pose(0.68, torso=-70, hips=-46, lean=-3.0)),
        (26, aim_pose(0.70, torso=-70, hips=-46, lean=-3.0)),
        (28, aim_pose(0.0, torso=-66, hips=-46, nock=False, right=(0.25, -0.24, 1.66), bow_g=(-0.04, 0.70, 1.56))),
        (32, aim_pose(0.0, torso=-62, hips=-42, nock=False, right=(0.24, -0.14, 1.48), bow_g=(-0.06, 0.66, 1.53))),
        (44, I),
    ]
    out.append(key_bow_action('BW_Charged', timeline(k, 44, [ease, ease, ease_in, None, ease_out, ease, ease])))
    ARM.animation_data.action = bpy.data.actions['BW_Idle']
    return out


# ------------------------------------------------------------------ export
KEEP_PACK_ARCHER = ('knight_hurt_left', 'knight_front_attacked_fall', 'knight_left_attacked_fall', 'knight_right_attacked_fall')


def export_archer(path, blend_path=None):
    """Saves the working .blend (optional), then exports Rig + Archer with the BW_* clips and the
    pack hit/fall clips (30 fps). WARNING: removes every other action from this session."""
    import os
    if blend_path:
        bpy.ops.wm.save_as_mainfile(filepath=blend_path, copy=True)
    keep = []
    for a in list(bpy.data.actions):
        if a.name.startswith('BW_'): keep.append(a); continue
        hit = next((k for k in KEEP_PACK_ARCHER if a.name.endswith(k)), None)
        if hit and a.name.startswith('Rig|') and a.get('rescaled') and not any(x.name.endswith(hit) for x in keep):
            keep.append(a); continue
        bpy.data.actions.remove(a)
    for a in bpy.data.actions:
        if a.name.startswith('BW_') or a.get('rescaled'): continue
        for fc in _fcurves(a):
            for kp in fc.keyframe_points:
                kp.co.x *= 30.0 / 24.0; kp.handle_left.x *= 30.0 / 24.0; kp.handle_right.x *= 30.0 / 24.0
        a['rescaled'] = 1
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.context.scene.render.fps = 30
    ARM.animation_data.action = bpy.data.actions['BW_Idle']
    bpy.context.scene.frame_set(0)
    sel = [ARM, bpy.data.objects['Archer']]
    win = bpy.context.window_manager.windows[0]
    area = next(a for a in win.screen.areas if a.type == 'VIEW_3D')
    for o in bpy.data.objects:
        o.hide_set(False) if o in sel else None
        o.select_set(o in sel)
    bpy.context.view_layer.objects.active = ARM
    with bpy.context.temp_override(window=win, area=area, screen=win.screen):
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'},
                                 add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                                 bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                                 bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                                 apply_unit_scale=True, axis_forward='-Z', axis_up='Y')
    return {"exported": path, "actions": [a.name for a in bpy.data.actions]}
