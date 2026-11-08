import bpy, json, sys
out = sys.argv[sys.argv.index("--") + 1]
objs = []
for o in bpy.data.objects:
    d = {"name": o.name, "type": o.type, "parent": o.parent.name if o.parent else None, "loc": [round(x, 2) for x in o.location], "scale": [round(x, 3) for x in o.scale]}
    if o.type == 'MESH':
        d.update(verts=len(o.data.vertices), mats=[m.name for m in o.data.materials if m], dims=[round(x, 2) for x in o.dimensions],
                 mods=[m.type for m in o.modifiers], vgroups=len(o.vertex_groups))
    if o.type == 'ARMATURE':
        d.update(bones=len(o.data.bones), bone_names=[b.name for b in o.data.bones])
    if o.animation_data:
        d["action"] = o.animation_data.action.name if o.animation_data.action else None
        d["nla"] = [[t.name, [s.action.name for s in t.strips if s.action]] for t in o.animation_data.nla_tracks]
    objs.append(d)
acts = [[a.name, [round(x) for x in a.frame_range], a.users] for a in bpy.data.actions]
mats = {}
for m in bpy.data.materials:
    tex = []
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image: tex.append(n.image.filepath)
    mats[m.name] = tex
markers = [[mk.name, mk.frame] for mk in bpy.context.scene.timeline_markers]
json.dump({"objs": objs, "actions": acts, "mats": mats, "fps": bpy.context.scene.render.fps, "markers": markers,
           "frames": [bpy.context.scene.frame_start, bpy.context.scene.frame_end]}, open(out, "w"), indent=1)
