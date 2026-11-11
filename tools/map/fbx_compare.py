# Story 2.2 AC1 re-export proof: compare two FBX files on what decides how Unity imports them — the GlobalSettings
# (UnitScaleFactor, UpAxis, FrontAxis, CoordAxis, ...) and the full list of node name paths (Unity derives each child's
# fileID from its name and hierarchy, and the scene's colliders are keyed to those fileIDs).
#   blender -b --factory-startup --python tools/map/fbx_compare.py -- <a.fbx> <b.fbx> <out.json>
import sys, json, collections
from io_scene_fbx import parse_fbx
args = sys.argv[sys.argv.index("--") + 1:]

def find(elem, name):
    for e in elem.elems:
        if e.id == name:
            return e
    return None

def read(path):
    root, version = parse_fbx.parse(path)
    gs = {}
    g = find(root, b"GlobalSettings")
    p70 = find(g, b"Properties70") if g else None
    for p in (p70.elems if p70 else []):
        key = p.props[0].decode("utf-8", "replace")
        if key in ("UpAxis", "UpAxisSign", "FrontAxis", "FrontAxisSign", "CoordAxis", "CoordAxisSign", "OriginalUpAxis",
                   "OriginalUpAxisSign", "UnitScaleFactor", "OriginalUnitScaleFactor"):
            gs[key] = p.props[-1]
    creator = ""
    hdr = find(root, b"FBXHeaderExtension")
    for e in (hdr.elems if hdr else []):
        if e.id == b"Creator":
            creator = e.props[0].decode("utf-8", "replace")
    objs = find(root, b"Objects")
    models, kinds = {}, collections.Counter()
    for e in objs.elems:
        if e.id == b"Model":
            uid = e.props[0]
            name = e.props[1].split(b"\x00\x01")[0].decode("utf-8", "replace")
            models[uid] = name
            kinds[e.props[2].decode("utf-8", "replace")] += 1
    parent = {}
    conns = find(root, b"Connections")
    for c in conns.elems:
        if c.props[0] == b"OO" and c.props[1] in models and c.props[2] in models:
            parent[c.props[1]] = c.props[2]
    def path(uid):
        parts = []
        while uid is not None and uid in models:
            parts.append(models[uid]); uid = parent.get(uid)
        return "/".join(reversed(parts))
    paths = sorted(path(u) for u in models)
    return {"version": version, "creator": creator, "global_settings": gs, "model_kinds": dict(kinds), "paths": paths}

a, b = read(args[0]), read(args[1])
pa, pb = set(a["paths"]), set(b["paths"])
dup_a = len(a["paths"]) - len(pa); dup_b = len(b["paths"]) - len(pb)
res = {"a": {k: v for k, v in a.items() if k != "paths"}, "b": {k: v for k, v in b.items() if k != "paths"},
       "paths_a": len(a["paths"]), "paths_b": len(b["paths"]), "duplicate_paths_a": dup_a, "duplicate_paths_b": dup_b,
       "only_in_a": sorted(pa - pb)[:50], "only_in_b": sorted(pb - pa)[:50], "n_only_in_a": len(pa - pb), "n_only_in_b": len(pb - pa),
       "global_settings_equal": a["global_settings"] == b["global_settings"], "paths_equal": a["paths"] == b["paths"]}
json.dump(res, open(args[2], "w"), indent=1, default=str)
print("FBXCOMPARE", json.dumps({k: res[k] for k in ("paths_a", "paths_b", "n_only_in_a", "n_only_in_b", "global_settings_equal", "paths_equal")}))
