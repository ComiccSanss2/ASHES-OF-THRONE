# Exports the BGE dragon (Assets/DragonBoss/BGE_Dragon_2.9_Animation_Only.blend) for Unity:
#   rig "a" + mesh "Cube" only, UV_NO_mirror as the only UV set (matches Art/Dragon/Dragon_Color.png,
#   baked by Tools/dragon_bake.py), and the usable baked actions as named takes (NLA strips):
#     D_Idle  ← Idel_A_Bake      (ground idle, wing stretches)       1-525
#     D_Walk  ← Walk_A_Bake.001  (ground walk cycle)                 1-79
#     D_Run   ← Run_A_Bake.001   (ground run cycle)                  1-39
#     D_Hover ← fly bone         (hovering, wings beating)           1-99
#     D_Fly   ← Fly              (wings level → beating, take-off)   0-180
#     D_Glide ← Body_1           (stretched glide)                   0-12
# The game layers the attacks on top procedurally (neck / head / jaw / tail bones).
#   blender -b BGE_Dragon_2.9_Animation_Only.blend --python Tools/dragon_export.py -- <out.fbx>
import bpy, sys
out = sys.argv[sys.argv.index("--") + 1]
arm = bpy.data.objects['a']
mesh = bpy.data.objects['Cube']
for o in list(bpy.data.objects):
    if o not in (arm, mesh): bpy.data.objects.remove(o, do_unlink=True)
me = mesh.data
if 'UV_mirror' in me.uv_layers: me.uv_layers.remove(me.uv_layers['UV_mirror'])
mat = bpy.data.materials.get('DragonSkin') or bpy.data.materials.new('DragonSkin')
me.materials.clear(); me.materials.append(mat)
for p in me.polygons: p.material_index = 0

clips = [("D_Idle", "Idel_A_Bake"), ("D_Walk", "Walk_A_Bake.001"), ("D_Run", "Run_A_Bake.001"),
         ("D_Hover", "fly bone"), ("D_Fly", "Fly"), ("D_Glide", "Body_1")]
ad = arm.animation_data or arm.animation_data_create()
ad.action = None
for t in list(ad.nla_tracks): ad.nla_tracks.remove(t)
if mesh.animation_data: mesh.animation_data.action = None
for name, an in clips:
    act = bpy.data.actions[an]
    tr = ad.nla_tracks.new(); tr.name = name
    st = tr.strips.new(name, int(act.frame_range[0]), act)
    st.name = name
    try:
        if act.slots: st.action_slot = act.slots[0]
    except Exception: pass
bpy.context.scene.render.fps = 24
for o in bpy.data.objects:
    if o is not None and o.name in bpy.context.view_layer.objects: o.select_set(o in (arm, mesh))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'},
                         add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False,
                         bake_anim_use_nla_strips=True, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0, armature_nodetype='NULL', use_armature_deform_only=False,
                         apply_unit_scale=True, axis_forward='-Z', axis_up='Y', mesh_smooth_type='FACE')
print("EXPORTED", out, [t.name for t in ad.nla_tracks])
