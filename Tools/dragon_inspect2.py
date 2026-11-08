import bpy, json, sys
out = sys.argv[sys.argv.index("--") + 1]
res = {}
for o in bpy.data.objects:
    if o.type != 'MESH' or len(o.data.vertices) < 1000: continue
    me = o.data
    d = {"uv": [u.name for u in me.uv_layers], "active_uv": me.uv_layers.active.name if me.uv_layers.active else None, "mats": []}
    counts = {}
    for p in me.polygons: counts[p.material_index] = counts.get(p.material_index, 0) + 1
    for i, m in enumerate(me.materials):
        md = {"name": m.name if m else None, "faces": counts.get(i, 0), "nodes": []}
        if m and m.node_tree:
            for n in m.node_tree.nodes:
                if n.type == 'TEX_IMAGE':
                    uvn = None
                    for l in m.node_tree.links:
                        if l.to_node == n and l.from_node.type == 'UVMAP': uvn = l.from_node.uv_map
                    md["nodes"].append([n.image.filepath if n.image else None, uvn, n.label or n.name])
                if n.type == 'BSDF_PRINCIPLED':
                    md["base"] = [round(x, 2) for x in n.inputs['Base Color'].default_value]
        d["mats"].append(md)
    res[o.name] = d
json.dump(res, open(out, "w"), indent=1)
