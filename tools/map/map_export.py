# Story 2.2 AC1: export the town-map .blend to FBX with the settings PROVEN to reproduce Assets/Models/game map .fbx
# (map-check/reexport-proof.md: same counts, triangles, node name paths and GlobalSettings). Headless; never saves the
# .blend.   blender -b --factory-startup "<map>.blend" --python tools/map/map_export.py -- "<out>.fbx"
#
# The current FBX was written by "Blender (stable FBX IO) - 5.0.1 - 5.14.0": FBX 7.4, UpAxis Y, FrontAxis +Z,
# CoordAxis X, UnitScaleFactor 100, 16,738 Mesh nodes and nothing else — the exporter's defaults. The one deliberate
# setting: object_types = MESH only, so no camera or light can ever be exported (AR8/AR9).
import bpy, sys
out = sys.argv[sys.argv.index("--") + 1:][0]
props = bpy.ops.export_scene.fbx.get_rna_type().properties
print("EXPORT OPTIONS", sorted(p.identifier for p in props if p.identifier != "rna_type"))
settings = dict(filepath=out, use_selection=False, object_types={"MESH"}, apply_unit_scale=True, global_scale=1.0,
                axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_UNITS", use_mesh_modifiers=True,
                mesh_smooth_type="OFF", bake_space_transform=False, bake_anim=False)
settings = {k: v for k, v in settings.items() if k in {p.identifier for p in props}}
print("EXPORT SETTINGS", {k: v for k, v in settings.items() if k != "filepath"})
bpy.ops.export_scene.fbx(**settings)
print("EXPORT WROTE", out)
