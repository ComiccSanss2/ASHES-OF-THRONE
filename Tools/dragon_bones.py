# Lists the exported dragon rig (bones with rest positions in Unity-like metres), mesh size and takes.
#   blender -b --python Tools/dragon_bones.py -- <Dragon.fbx> <out.json>
import bpy, sys, json
args = sys.argv[sys.argv.index("--") + 1:]
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=args[0])
res = {"objects": [], "bones": [], "actions": []}
for o in bpy.data.objects:
    d = {"name": o.name, "type": o.type, "dims": [round(x, 3) for x in o.dimensions], "loc": [round(x, 3) for x in o.location],
         "rot": [round(x, 3) for x in o.rotation_euler], "scale": [round(x, 4) for x in o.scale], "parent": o.parent.name if o.parent else None}
    res["objects"].append(d)
    if o.type == 'ARMATURE':
        for b in o.data.bones:
            h = o.matrix_world @ b.head_local; t = o.matrix_world @ b.tail_local
            res["bones"].append({"n": b.name, "p": b.parent.name if b.parent else None,
                                 "h": [round(x, 3) for x in h], "t": [round(x, 3) for x in t], "deform": b.use_deform})
for a in bpy.data.actions:
    res["actions"].append([a.name, [round(x, 1) for x in a.frame_range]])
json.dump(res, open(args[1], "w"), indent=0)
print("OK", len(res["bones"]))
