# Story 2.2 (AC1, AR9): what would an FBX export of the town-map .blend contain, and what does the FBX Unity imports
# contain? Read-only, headless — never Alexv's live Blender, never a save of the .blend.
#
#   blender -b --factory-startup "<map>.blend" --python tools/map/map_check.py -- "<map>.fbx" "<out.json>"
#
# 1. The .blend, EVALUATED: depsgraph object_instances with particle and collection instances realised (never
#    bpy.data.meshes — 1,658 of them, lesson 48).
# 2. The FBX, imported into a fresh empty scene so nothing from the .blend leaks into its numbers.
# Both are grouped by base name (particle instances by their instanced object's base name, "Cube" for
# "Plane|Cube.012_M18_0|Dupli|7"), with per group: count, triangles, and the union of world bounds (Blender metres).
# The comparison and the verdict are made by the caller (Assets/Scripts/Editor/MapCheck.cs), which adds Unity's column.
import bpy, json, sys
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:]
fbx_path, out = args[0], args[1]


def base(name):
    parts = name.split("|")
    if len(parts) >= 2 and parts[1]:
        return parts[1].split(".")[0]
    return name.split(".")[0]


def add(groups, key, tris, corners, origin=None):
    """Per group: count and triangles over everything; exact vertex bounds over NON-instanced objects ("min"/"max");
    and over particle/collection INSTANCES the bounds of their origins ("origin_min"/"origin_max"). Instances are
    compared by origin because a background depsgraph does not reproduce the particles' random size (measured on this
    map: every instance origin identical to 0.000 m, scale 0.106 uniform vs 0.106-0.110 in the exported FBX)."""
    g = groups.setdefault(key, {"count": 0, "triangles": 0, "instances": 0, "min": [1e18] * 3, "max": [-1e18] * 3,
                                "origin_min": [1e18] * 3, "origin_max": [-1e18] * 3})
    g["count"] += 1
    g["triangles"] += tris
    if origin is not None:
        g["instances"] += 1
        for k in range(3):
            g["origin_min"][k] = min(g["origin_min"][k], origin[k])
            g["origin_max"][k] = max(g["origin_max"][k], origin[k])
        return
    for c in corners:
        for k in range(3):
            g["min"][k] = min(g["min"][k], c[k])
            g["max"][k] = max(g["max"][k], c[k])


_verts = {}


def local_verts(me):
    """The mesh's vertices as an (n, 4) homogeneous array, cached per mesh."""
    v = _verts.get(me.name)
    if v is None:
        co = np.empty(len(me.vertices) * 3, dtype=np.float64)
        me.vertices.foreach_get("co", co)
        v = np.c_[co.reshape(-1, 3), np.ones(len(me.vertices))]
        _verts[me.name] = v
    return v


def world_corners(me, m):
    """EXACT world bounds from the transformed vertices — a transformed bound_box depends on the object's local
    frame, and the FBX round trip may bake a rotation differently, which moved the union by up to 1.6 m."""
    v = local_verts(me)
    if len(v) == 0:
        return []
    w = v @ np.array(m).T
    return [w[:, :3].min(axis=0).tolist(), w[:, :3].max(axis=0).tolist()]


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def volatile(ob):
    """True when the object's evaluated geometry depends on the frame or on modifier evaluation settings (animated,
    constrained, parented to something animated, or modified) — its bounds can move between this evaluation and the
    export without the map having changed. Measured on this map: Cube_M9L_0 (animated) 0.13 m, Cylinder (Solidify)
    0.06 m, same triangles."""
    o = ob.original if hasattr(ob, "original") else ob
    while o is not None:
        if o.animation_data or o.constraints or len(o.modifiers) > 0:
            return True
        o = o.parent
    return False


def evaluated():
    dg = bpy.context.evaluated_depsgraph_get()
    groups, cache = {}, {}
    for oi in dg.object_instances:
        ob = oi.object
        if ob.type != "MESH":
            continue
        name = (oi.instance_object or ob).name if oi.is_instance else ob.name
        key = base(name) if not oi.is_instance else ob.name.split(".")[0]
        me = ob.data
        t = cache.get(me.name)
        if t is None:
            t = cache[me.name] = tri_count(me)
        m = oi.matrix_world
        add(groups, key, t, None if oi.is_instance else world_corners(me, m), list(m.translation) if oi.is_instance else None)
        if not oi.is_instance and volatile(ob):
            groups[key]["volatile"] = groups[key].get("volatile", 0) + 1
    return groups


blend = evaluated()

new = bpy.data.scenes.new("fbx_probe")
for w in bpy.context.window_manager.windows:
    w.scene = new
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=fbx_path)
imported = [o for o in bpy.data.objects if o not in before]
fbx, cameras, lights = {}, [], []
for o in imported:
    if o.type == "CAMERA":
        cameras.append(o.name)
    if o.type == "LIGHT":
        lights.append(o.name)
    if o.type != "MESH":
        continue
    inst = "|Dupli|" in o.name or o.name.count("|") >= 2
    add(fbx, base(o.name), tri_count(o.data), None if inst else world_corners(o.data, o.matrix_world),
        list(o.matrix_world.translation) if inst else None)

def as_list(groups):
    return [dict(name=k, count=g["count"], triangles=g["triangles"], instances=g["instances"], volatile=g.get("volatile", 0),
                 min=g["min"], max=g["max"], origin_min=g["origin_min"], origin_max=g["origin_max"]) for k, g in sorted(groups.items())]


with open(out, "w", encoding="utf-8") as f:
    json.dump({"blend": bpy.data.filepath, "fbx": fbx_path, "blend_groups": as_list(blend), "fbx_groups": as_list(fbx),
               "fbx_cameras": cameras, "fbx_lights": lights,
               "blend_cameras": [o.name for o in bpy.data.scenes[0].objects if o.type == "CAMERA"],
               "blend_lights": [o.name for o in bpy.data.scenes[0].objects if o.type == "LIGHT"]}, f)
print("MAPCHECK WROTE", out)
