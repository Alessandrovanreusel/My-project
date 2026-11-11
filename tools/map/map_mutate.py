# Story 2.2 AC1 negative control: a SCRATCH copy of the town .blend with exactly two named changes, so the map check
# can prove it is able to say "differs". Deletes one object and moves another by 1 m (both non-instanced, unanimated,
# unmodified, and the only members of their base-name group), then saves a COPY. Never saves over the original.
#   blender -b --factory-startup "<map>.blend" --python tools/map/map_mutate.py -- "<out>.blend"
import bpy, sys, collections
out = sys.argv[sys.argv.index("--") + 1:][0]
def base(n):
    p = n.split("|")
    return p[1].split(".")[0] if len(p) >= 2 and p[1] else n.split(".")[0]
groups = collections.Counter(base(o.name) for o in bpy.context.scene.objects if o.type == "MESH")
def calm(o):
    while o is not None:
        if o.animation_data or o.constraints or len(o.modifiers) or len(getattr(o, "particle_systems", [])):
            return False
        o = o.parent
    return True
pick = [o for o in bpy.context.scene.objects if o.type == "MESH" and groups[base(o.name)] == 1 and calm(o)
        and not o.children and o.name.startswith(("SuperMarket", "House Complete"))]
pick.sort(key=lambda o: o.name)
if len(pick) < 2:
    raise SystemExit("MUTATE: no suitable objects")
gone, moved = pick[0], pick[1]
print("MUTATE deleted", gone.name, "group", base(gone.name), "| moved +1 m x", moved.name, "group", base(moved.name))
bpy.data.objects.remove(gone, do_unlink=True)
moved.location.x += 1.0
bpy.ops.wm.save_as_mainfile(filepath=out, copy=True)
print("MUTATE saved copy", out)
