# Blender 4.x / 5.x — esportazione FBX per Unity in stile PS2 (Le Ceneri del Trono)
# Uso: seleziona gli oggetti da esportare, apri questo file nel Text Editor di Blender e premi "Run Script".
# - applica scala/rotazione, imposta shading flat/auto-smooth, segnala i modelli fuori budget poligonale
# - esporta in //export/<nome_file>.fbx con assi corretti per Unity (+Z avanti, Y su)

import bpy
import os

BUDGET = {"character": 4500, "boss": 8000, "prop": 1200}
AUTO_SMOOTH_ANGLE = 30.0  # gradi


def tri_count(obj):
    me = obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh()
    n = sum(len(p.vertices) - 2 for p in me.polygons)
    obj.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh_clear()
    return n


def main():
    sel = [o for o in bpy.context.selected_objects]
    if not sel:
        raise RuntimeError("Seleziona almeno un oggetto")

    # 1) applica trasformazioni
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    report = []
    for o in sel:
        if o.type != "MESH":
            continue
        # 2) shading low-poly: flat su superfici dure, auto smooth a 30°
        for p in o.data.polygons:
            p.use_smooth = False
        try:
            bpy.context.view_layer.objects.active = o
            bpy.ops.object.shade_auto_smooth(angle=AUTO_SMOOTH_ANGLE * 3.14159 / 180.0)
        except Exception:
            pass
        # 3) controllo budget
        tris = tri_count(o)
        kind = o.get("ps2_kind", "prop")
        limit = BUDGET.get(kind, BUDGET["prop"])
        flag = "OK" if tris <= limit else "SOPRA BUDGET"
        report.append(f"{o.name}: {tris} tri ({kind}, limite {limit}) {flag}")

    # 4) export
    blend_name = os.path.splitext(os.path.basename(bpy.data.filepath))[0] or "asset"
    out_dir = bpy.path.abspath("//export")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, blend_name + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        path_mode="COPY",
        embed_textures=False,
    )
    report.append("Esportato: " + path)
    print("\n".join(report))
    return report


if __name__ == "__main__":
    main()
