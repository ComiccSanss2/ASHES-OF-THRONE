import bpy, sys, math, os
from mathutils import Vector
args = sys.argv[sys.argv.index("--") + 1:]
outdir = args[0]
names = args[1].split("|")
os.makedirs(outdir, exist_ok=True)
sc = bpy.context.scene
arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type == 'MESH' and o.parent == arm)
for o in bpy.data.objects:
    if o.type == 'MESH': o.hide_render = o != mesh
cam = bpy.data.objects.new("SheetCam", bpy.data.cameras.new("SheetCam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.lens = 28
sc.render.engine = 'BLENDER_WORKBENCH'
sc.display.shading.light = 'STUDIO'
sc.display.shading.color_type = 'SINGLE'
sc.render.resolution_x, sc.render.resolution_y = 360, 240
c = Vector((0, 0, 2.5))
for n in names:
    act = bpy.data.actions.get(n)
    if act is None: continue
    if not arm.animation_data: arm.animation_data_create()
    arm.animation_data.action = act
    # Blender 4.4+/5.x slotted actions
    try:
        if act.slots: arm.animation_data.action_slot = act.slots[0]
    except Exception: pass
    f0, f1 = act.frame_range
    for k, fr in enumerate((f0, f0 + (f1 - f0) / 3, f0 + 2 * (f1 - f0) / 3)):
        sc.frame_set(int(fr))
        for yaw, tag in ((0, 's'), (-40, 'q')):
            a = math.radians(yaw)
            pos = c + Vector((math.cos(a) * 30, math.sin(a) * 30, 6))
            cam.location = pos
            cam.rotation_euler = (c - pos).to_track_quat('-Z', 'Y').to_euler()
            sc.render.filepath = os.path.join(outdir, f"{n.replace(' ', '_')}_{k}{tag}.png")
            bpy.ops.render.render(write_still=True)
print("done")

