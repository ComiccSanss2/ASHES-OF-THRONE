# Bakes the dragon's Cycles material (two UV sets, several textures mixed) into ONE colour map
# on the UV_NO_mirror layout, for Unity.  Run in background on Dragon_2.5_Cycles.blend:
#   blender -b Dragon_2.5_Cycles.blend --python Tools/dragon_bake.py -- <out.png> [size]
import bpy, sys
args = sys.argv[sys.argv.index("--") + 1:]
out = args[0]
size = int(args[1]) if len(args) > 1 else 2048
sc = bpy.context.scene
sc.render.engine = 'CYCLES'
sc.cycles.device = 'CPU'
sc.cycles.samples = 1
sc.cycles.bake_type = 'DIFFUSE'
sc.render.bake.use_pass_direct = False
sc.render.bake.use_pass_indirect = False
sc.render.bake.use_pass_color = True
sc.render.bake.margin = 6
ob = bpy.data.objects['Cube.001']
# bake the rest pose (no deformation): disable the armature modifier
for m in ob.modifiers:
    if m.type == 'ARMATURE': m.show_render = False; m.show_viewport = False
img = bpy.data.images.new("DragonBaked", size, size, alpha=False)
img.filepath_raw = out
img.file_format = 'PNG'
me = ob.data
me.uv_layers.active = me.uv_layers['UV_NO_mirror']
for m in me.materials:
    if not m: continue
    m.use_nodes = True
    nt = m.node_tree
    n = nt.nodes.new('ShaderNodeTexImage'); n.image = img
    uvn = nt.nodes.new('ShaderNodeUVMap'); uvn.uv_map = 'UV_NO_mirror'
    nt.links.new(uvn.outputs['UV'], n.inputs['Vector'])
    for x in nt.nodes: x.select = False
    n.select = True; nt.nodes.active = n
for o in bpy.context.view_layer.objects: o.select_set(False)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.bake(type='DIFFUSE', pass_filter={'COLOR'}, use_clear=True, margin=6)
img.save()
print("BAKED", out)
