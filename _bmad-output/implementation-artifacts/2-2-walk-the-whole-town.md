---
baseline_commit: d0506816dbf4f39afa79424e1b6efc6525bf3006
---

# Story 2.2: Walk the whole town

Status: in-progress

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-10 by `plan-epic-2`; context created 2026-11-11 by the loop CREATE phase. It builds on Story 2.1
(ADR-5: people are 1.77 m, the eye is at 1.65 m, the town is roughly real metres). In `loop/dependencies.yaml`,
**Story 2.5 (vantage points) waits for this story**, because it walks the player and stages subjects across the
whole town. The story takes up these `deferred-work.md` entries:*
- *"See-through walls" (:12);*
- *three items from the Story 2.1 review: `EventActor.Begin` ignores `Warp`'s bool (:315), the bounded bake's
  collect mode and voxel (:316), and looking up lifting the near plane (:317);*
- *the level-design leftovers ADR-5 hands to this story: the raised road slab, the tyre line, the player spawning
  on the police-tape barrier, and the street block that sits underground.*

> **The starting point in seven facts.** All seven were read from files on 2026-11-11. Re-measure them in the
> editor (Task 1a) before relying on any of them.
> 1. **The map in Unity is the newest `.blend` as of planning.**
>    - `CAMERA GAME SHARED FOLDER/Word Map/game map .blend` is dated 2026-05-26, and `Assets/Models/game map .fbx`
>      (76.8 MB, LFS) is dated 2026-06-04.
>    - Planning measured them equal: 16,738 meshes (15,080 of them particle instances), 5,677,935 triangles,
>      the same 3,144 × 3,732 × 285 m bounds, and no camera (`plan-epic-2/blend_vs_fbx.json`).
>    - So the check is expected to say **match**. Its negative control (AC1) is what proves it could say otherwise.
> 2. **Camera import is still on.** `game map .fbx.meta` has `importCameras: 1` and `importLights: 1`. The FBX holds
>    no camera today: all 16,738 imported objects are meshes. Switching the flag off therefore changes nothing you
>    can see, but it forces a full reimport of a 77 MB model with 16,738 meshes. Time it.
> 3. **The map's colliders live in the scene, one per renderer, keyed to FBX children.**
>    - `SampleScene.unity` holds **16,738 MeshColliders** (`--- !u!64`). They are prefab-instance *added
>      components* on the `game map` PrefabInstance (`&2035259070`, l.355907). Each one is tied to an FBX child by
>      `targetCorrespondingSourceObject` fileID.
>    - `Tools/Add MeshColliders to World` (`Assets/Scripts/Editor/AddMeshColliders.cs`) added them, to *every*
>      MeshFilter with no exceptions.
>    - **So every blade of scatter blocks the player.** Particle scatter by base name: Cube 8,077, Circle 6,012 and
>      Icosphere 1,009. The pines are "Circle" too (`Circle.002_M17_0`), so a name is not a class.
>    - **So a re-export that renames children orphans their colliders silently.**
>    - The scene is **16.9 MB in plain git**, mostly those colliders.
>    - One FBX child is deactivated by a scene override (`m_IsActive: 0` on fileID `65702495048144492`,
>      l.355955). Identify it, and keep it deactivated.
> 4. **The NavMesh covers the slice only.**
>    - It is the `NavMeshSurface` on `game map` (l.492176): Collect **Volume**, centre (20, 2.5, 20), size
>      (200, 25, 120), layer mask *everything*, Physics Colliders, voxel override 0.1, tile 256, min region 2,
>      ignore agents on.
>    - Agent type 0 "Humanoid" is radius 0.30, height 1.77, climb 0.35, slope 45.
>    - `NavMesh-game map.asset` is **0.22 MB, in plain git** (`.gitattributes` has no `*.asset` rule).
>    - Story 2.1 *estimated* a whole-map bake at voxel 0.1 at 100–360 MB but never ran it. That estimate covered
>      the whole ~10 km² mountain ring, not just the playable town.
> 5. **ADR-5 left level-design debt here** (measured by Story 2.1, ADR-5 §Consequences):
>    - The road (`Plane.004_M3_0`) is a raised slab whose edges stand **1.2–1.65 m** above the grass. You can
>      only get onto it near x 0, where there is a 0.13 m kerb at x 0.5.
>    - A line of tyres (`tire.068_Colore_0`, x −223…−213, z 3…236) fences off the land beyond it. It is passable
>      only via the road.
>    - The police car at spawn is ~2× real size.
>    - The player starts at (0, 20, 0) and **lands standing on the 1.4 m police-tape barrier** (y 3.47).
> 6. **The camera has never been checked against the town's walls.**
>    - In Walk the camera sits **0.316 m forward** of the body axis with near clip **0.095**, so the near plane's
>      centre is **0.111 m past a 0.30 m capsule** before its corners are counted.
>    - The nudge follows the *pitch* (`ThirdPersonCamera.cs:121`). Looking up 80° lifts the near plane to
>      ~2.05 m on a 1.77 m body (deferred :317).
>    - The see-through report (deferred :12) is from the 8.86 m giant era.
> 7. **There is collidable geometry under the world.**
>    - Story 2.1 found the census's "street block" (Casa, Tetto, cars, containers, the factory, oil barrels)
>      ~60 m underground under the slice (`loop/lessons.md:61`). It has colliders. A player who fell through the
>      plain would land there and walk around underground.
>    - ⚠️ But the planning renders, which use the *evaluated* `.blend`, show a street block **on the surface**
>      about 100–215 m from spawn (Dev Notes §"Leads"). The buried set may only be the instancing source.
>      **Settle which is true in Task 1a.** The whole story's list of "parts of the town" depends on it.

## Story

As a player,
I want to walk out of the slice patch to anywhere in the town (the street block, the houses, the industrial yard, the shore and up into the hills) without falling through, getting stuck or walking off the world,
so that the whole town is mine to explore and to chase moments in.

## Acceptance Criteria

AC1–AC5 below are copied word for word from `epics.md` §Story 2.2, and they are the contract. The notes under
each one say what it means in this project. They add precision and never relax a clause. AC6 is added the way
Stories 1.11, 1.12, 1.14 and 2.1 added one. It writes down the standing CLAUDE.md rules for a story that
changes the world every other system is measured in.

**AC1: A re-runnable check of the map against the newest `.blend`; re-export in place if it changed; camera import off (AR8, AR9)**

> **Given** the newest town-map `.blend` in the shared folder,
> **When** a re-runnable editor check compares it with `Assets/Models/game map .fbx`,
> **Then** it reports whether the two match on mesh count, triangle count and bounds, and names anything that differs
> **And** if the map has changed, it is re-exported over `Assets/Models/game map .fbx` in place, keeping the file's GUID so the scene's references survive
> **And** camera import is switched off, so no Blender camera can ever override Main Camera (AR8, AR9).

- **"A re-runnable editor check".** An editor `[MenuItem]`, `Tools/Town/Check Map Against .blend`, that does not
  enter play mode.
  - **How it runs.** It calls **headless Blender** as a subprocess (lesson 47): `blender.exe -b --factory-startup
    "<blend>" --python tools/map/map_check.py -- "<fbx>" "<out.json>"`, with a timeout.
    - Never Alexv's live Blender session, never the Blender MCP, and never a save of the `.blend`.
    - Blender lives at `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe` on this machine. Allow an
      `EditorPrefs` override.
  - **Fail-soft (NFR8).** No Blender, no `.blend` (the shared folder is git-ignored and absent on other
    machines) or a timeout gives **one** clear warning and a `CANNOT CHECK (<reason>)` verdict. It never throws.
- **"Compares it".** Measure what an export of the `.blend` *would* contain: the depsgraph-evaluated
  `object_instances`, with particle and collection instances realised. Never count `bpy.data.meshes` (1,658;
  lesson 48). Then measure what the FBX *does* contain, imported into a fresh empty Blender scene.
  `plan-epic-2/compare_fbx.py` already does both and is the starting point.
  - **Recommended third column:** what *Unity* imported. Count the MeshFilters on the model asset, sum the
    triangles with `GetIndexCount(sub) / 3` (not `.triangles`, which allocates), and take the renderer bounds,
    converted with (bx, by, bz) = (−ux, −uz, uy). It catches an importer-side change, such as scale, that a
    Blender-to-Blender comparison cannot see.
- **"Names anything that differs".** Group by **base name** (the text before the first `.`). Per group, report
  the count, the triangles and the union of the bounds. List every group whose count or triangle total differs,
  or whose bounds differ by more than 1 cm, plus the totals.
  - Output: a JSON report and a readable `report.md` under the evidence folder, plus **one** console line:
    `MATCH`, `DIFFERS (n names)` or `CANNOT CHECK (reason)`.
- **Prove the check can fail.** A check that has only ever said "match" proves nothing.
  - Make a **scratch copy** of the `.blend` with a headless script that deletes one named object and moves another
    by 1 m. Save it with `bpy.ops.wm.save_as_mainfile(filepath=<evidence>/mutated.blend, copy=True)`, never over
    the original.
  - Run the check against it. It must name **exactly** those two groups.
  - Delete the 214 MB scratch file afterwards, and say so in the log.
- **"If the map has changed, it is re-exported … in place, keeping the file's GUID".**
  - **Same path, same file.** Overwrite `Assets/Models/game map .fbx` itself; never delete-and-add, and never
    import under a new name. Unity then keeps the `.meta`, and with it the GUID.
  - **The GUID alone does not keep the scene's colliders.** They are keyed to the children's fileIDs, which
    Unity derives from the children's names and hierarchy (fact 3). The re-export must reproduce both.
  - **Prove the export settings on a scratch export first**, written *outside* `Assets/` (the evidence folder).
    Compare it with the current FBX on three things:
    1. the check's numbers;
    2. the full list of node name paths;
    3. the FBX `GlobalSettings` (UnitScaleFactor, UpAxis, FrontAxis, CoordAxis), read with Blender's bundled
       `io_scene_fbx.parse_fbx`.

    Identical on all three means Unity imports it identically under the same `.meta`. Then write the proven
    settings into `tools/map/map_export.py` and a `Tools/Town/Re-export Map from .blend` item.
  - **Overwrite the real FBX only if the check says the map changed** (fact 1 says it has not). If you do:
    - re-read that every scene collider still resolves;
    - check the `NavMeshSurface` is still on the root and the deactivated child is still inactive;
    - run the drunk route trace.
  - A re-export adds a ~77 MB LFS object. Record that as a decision.
- **"Camera import is switched off".** Set `importCameras = false` through `ModelImporter`, then
  `SaveAndReimport()`, and time it.
  - **Recommended in the same reimport:** `importLights = false`. The `.blend` has one light, and a re-export
    could bring it in. Record that as a decision.
  - **Read back afterwards:**
    - 16,738 renderers under the scene's map;
    - every scene MeshCollider still resolving (count them, and look for "missing" components);
    - the deactivated child still inactive;
    - in play mode, Main Camera is the only enabled `Camera` (`Camera.allCameras`).
  - Add an **EditMode test** that `AssetImporter.GetAtPath("Assets/Models/game map .fbx")` has
    `importCameras == false`. It is a structural invariant, not an expected result, and it stops a silent revert.

**AC2: The real controller walks and sprints a route through every part of the town without falling through, sticking on open ground, or being stopped by scatter (FR13, FR15)**

> **Given** the player,
> **When** the real controller walks and sprints a route through every part of the town, driven by the same input methods the player's keys call and drawn as a trace on a top-down map,
> **Then** they never fall through the ground and never stick on open ground
> **And** nothing too small to be an obstacle (grass, flowers, pebbles) stops them like an invisible wall (FR13, FR15).

- **"Every part of the town"** is the list Task 1a writes to `parts.json`, from the Unity census and not from the
  leads below. At minimum it holds:
  - the street block, the row of houses, the industrial yard and the shore;
  - the hills, on at least two sides of the ring;
  - the land beyond the tyre line;
  - the far corners of the plain.

  Each entry is a Unity position on walkable ground. If a part the epic names does not exist on the surface, say
  so with the evidence (fact 7). Do not invent it.
- **"The real controller … driven by the same input methods the player's keys call".** Use Story 2.1's drive
  (`ScaleStudyRunner.cs` `Keys()` / `DriveControl()`):
  - `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift))` goes PlayerInput
    (Send Messages) → `OnMove` / `OnSprint` / `OnJump` (`ThirdPersonController.cs:148/:166/:156`), exactly as a
    key press does.
  - Run the **drive control** first: no keys → no motion; W → walk speed; release → stop.
  - **Steering is the mouse's job** (1h). Never write `_moveInput` and never move the transform to make progress.
- **The route.** Plan it with the whole-town NavMesh (AC5), whose agent *is* the player's body:
  `NavMesh.CalculatePath` between consecutive parts gives the corners, and the real controller steers along them.
  Walk the whole route once and sprint it once.
  - ⚠️ NavMesh corners hug obstacles at exactly the agent radius (0.30, the capsule's own radius). A controller
    that cuts a corner will graze it.
  - Steer with a short lookahead. Classify a stick at a carved corner, within ~0.1 m of the NavMesh edge, as a
    path issue: re-plan and re-walk that leg.
  - List every such stick anyway, so none is hidden.
- **"Drawn as a trace on a top-down map".** Use a **Unity** top-down capture of the playable area (Task 1a): an
  orthographic camera rendered to a RenderTexture, with `topdown.json` holding the world → pixel transform.
  - Colour the trace by gait. Mark sticks red and falls magenta, and label the parts.
  - The planning images are Blender renders with Unity (x, z) = (−bx, −by), so they are leads, not the canvas.
- **"Never fall through the ground".**
  - Each frame, compare the feet with the surface straight beneath them (a downward ray from above the head). Feet
    more than 0.3 m below that surface mean the player fell through.
  - Over the whole route, FallRecovery (AC3) must fire **zero** times.
- **"Never stick on open ground".**
  - **A stick** is horizontal speed below 50% of the commanded speed for more than 0.3 s while a move key is held.
  - **Log what was touched** with a rig-side `OnControllerColliderHit` recorder on the player: the collider, its
    renderer's height above the ground, the hit normal and the point. Photograph each stick from the eye.
  - **"Open ground"** means the NavMesh says walkable and the eye photo shows nothing in the way. A stick there is
    a defect. A stick on a visible obstacle the path clipped is a path or rig issue: fix the route, and say which
    it was.
- **"Nothing too small to be an obstacle (grass, flowers, pebbles)".** The route alone may never cross every kind
  of scatter, so test it directly:
  - Walk and sprint straight lines through the **densest patch of each scatter class** from 1b.
  - Pass: speed stays at the commanded value within ±5%, and no hit lands on a class the rule removed.
  - **Positive control:** the same lines on today's world (Task 2, "walk before") show the hits, which proves the
    instrument can see them.

**AC3: The edges stop the player; falling below the world puts them back within ~2 s with no error; the oil rig is decided (NFR8)**

> **Given** the edges of the town (the mountain ring and the water),
> **When** the player walks into them,
> **Then** the terrain or the shoreline stops them: they never walk on the water's surface or off the world
> **And** if they ever end up below the world, they are put back on the last safe ground within ~2 s, and no error is shown (NFR8)
> **And** the oil rig out on the water is either reachable on foot or deliberately out of bounds. This story decides which and records it, because Epic 3's oil-rig event needs to know.

- **"Walks into them".** Drive the real controller (as AC2):
  - into the shoreline at **at least 6 points** along it;
  - into the ring at **at least 8 bearings**;
  - at walk and at sprint, holding W for 10 s after contact;
  - plus one jump at each kind of barrier.

  **Pass:** the player stays inside the playable bounds (1c), never stands on water (no grounded frame whose
  ground is the water, or over the water region), and never leaves the map's outer bounds.
- **"The terrain or the shoreline stops them".** Terrain steeper than the controller's `slopeLimit` (45°) stops
  them by itself. Everywhere else, an **invisible boundary** stops them (1c):
  - BoxColliders under a new scene root **`TownBounds`**, on a new **`WorldBounds`** layer. Add the layer through
    the editor API and read it back. The `TagManager` migration is harmless (deferred :103).
  - The boundaries have no renderer. They are not in `GradingConfig.occluderMask` (Default only).
  - Put them where the world explains them: at the water's edge, and at the ring's measured limit. Never across
    open plain.
- **"Never walk on the water's surface".**
  - The water must carry **no collider the player can stand on**. Find the water renderer(s) in 1a and read their
    collider. Remove it under the 1b rule (water is not an obstacle; it is a no-go).
  - Whatever is *under* the water (a lake bed, if any) must be unreachable (the shoreline barrier) and not
    walkable for agents (a `NavMeshModifierVolume` set to Not Walkable, if the bake picks it up).
- **"Below the world … put back on the last safe ground within ~2 s, and no error is shown".** This is a new
  shipped component (1e). **Provoke it, at least 5 cycles in a row of each case**, because bugs here hide in the
  second cycle:
  1. the player placed 1 m below the plain's surface (a stand-in for falling through);
  2. a real hole, if 1a finds one;
  3. landing on the buried block at y ≈ −60;
  4. walking off the outer edge with that boundary segment disabled for that test only (the backstop).

  For each cycle, measure **from the instant the feet go below the surface, or out of bounds, to the restore
  frame**. The worst case must be ≲ 2 s. After the restore:
  - the player is grounded within 0.5 s;
  - they are on the NavMesh and not inside geometry (`Physics.CheckCapsule`);
  - no falling speed carries over.

  The Unity console must hold no Error or Exception. **Nothing is shown to the player:** capture a frame after
  the restore with `ScreenCapture.CaptureScreenshotAsTexture()` after `WaitForEndOfFrame`. That shot shows any
  overlay UI, which `cam.Render()` cannot see. Compare it with a normal frame: no new UI.
- **"The oil rig … reachable on foot or deliberately out of bounds".** This is decision 1d. Measure, decide and
  record it in three places:
  - the result file's `decisions`;
  - a new architecture subsection "World bounds and traversal (Story 2.2)";
  - a `decision-log.md` entry.

  Also add **one dated note on the Epic 3 seed in `epics.md` §"Known seeds"**. That is where Epic 3's planner
  looks, and it is the **only** edit to `epics.md` this story makes.
  - The AC has no approval clause. **Do not add a gate.**

**AC4: Pressed against any wall, the view never shows what is behind it**

> **Given** the player pressed against any wall in the town,
> **When** they look at it,
> **Then** the view never shows what is behind the wall.

- **"Any wall in the town".** Sample every kind of wall the census finds on the surface, **at least 12 walls**:
  - the house walls, outside, and inside if a house can be entered;
  - the street block's buildings and the industrial yard's sheds or containers;
  - the bus shelter, a car's side, a pine trunk, a rock and a mountain face;
  - the road slab's edge.

  A `TownBounds` barrier is *not* a wall in the town. It is invisible by design, so leave it out and say so.
- **"Pressed against".** Walk into the wall through the real input until the controller stops (W held, speed
  ≈ 0):
  - at 0°, ±30° and ±60° to the wall's normal;
  - into an inside corner, wherever one exists.
- **"When they look at it".**
  - **Pitch:** −80° (up), −45°, 0°, +45° and +80° (down). Drive it through the real path, Mouse delta → `OnLook`
    → `_pitch`, with a control that proves N units of delta move the pitch by N × 0.15°.
  - **Modes:** Walk at FOV 60, then raised (`PhotoModeController.SetPhotoMode(true)`) at FOV 60 and zoomed to 18
    (`SetZoom`).
- **The detector: draw "behind" so you can count it.**
  - Place a magenta, unlit marker just behind the wall, on its far side, facing the player. Render the eye view to
    an RT and count the marker's pixels. The pass is **0 marker pixels**.
  - **Positive control, per wall:** the same pose with the wall's renderers switched off must show the marker.
  - **Second, geometric check:** linecasts from the body axis at eye height to the near plane's centre and four
    corners. Any hit means the near plane is past a surface.
- **"Never shows"** has to hold *after the fix*, and the fix must leave the open-space camera exactly where it is
  (1f): 1.650 m up and 0.316 m forward of the feet in the open, read back. The Photo Shoot, the HUD rig and every
  earlier eye-height measurement depend on that pose.

**AC5: The NavMesh baked over the whole town; a path from the pub to every part completes; console clean; frame rate not lower (FR15, NFR1, NFR5)**

> **Given** the NavMesh, baked over the whole town at the agent size ADR-5 set,
> **When** an agent paths from the pub to a point in every part of the town,
> **Then** every path completes (drawn on the top-down map), so Epic 3's events can be staged anywhere (FR15)
> **And** the console stays clean, and the frame rate is no lower than at the start of the story (NFR1, NFR5).

- **"Baked over the whole town at the agent size ADR-5 set".**
  - Agent type 0 is unchanged: 0.30 / 1.77 / climb 0.35 / slope 45. Read it back before and after.
  - The bake **volume is the playable town** from 1c, not the whole 3 × 3.7 km map. Its y range runs from just
    below the lowest walkable ground (1a) up to the highest reachable ground. It must stay well above the buried
    block at y ≈ −60, so no underground island is baked; 2.1 bounded y for the same reason.
  - **A re-runnable `Tools/Town/Bake Town NavMesh`.** It bakes and persists to the **same**
    `Assets/Scenes/SampleScene/NavMesh-game map.asset` path, so the GUID and the scene reference are kept. It
    reports the bake time, the tile count and the file size.
  - ⚠️ **Every file you commit must be under 50 MB.** GitHub warns at 50 MB, rejects 100 MB, and the orchestrator
    pushes master. **Never add an LFS rule**: LFS quota is a money question, so it would be a blocker. If one
    surface cannot fit, split the town into several surfaces. The limit is per file.
  - Re-decide the 2.1 bake's open questions (deferred :316): the collect mode, the layer mask, the voxel, and what
    must be **excluded** (1g).
- **"From the pub".** The map has no pub (2.1 Dev Notes). **The pub is the drunk's spawn point**,
  `EventManager/SpawnPoint` at world ≈ (31.8, 0.55, 19). `WP0_leavePub` is the first waypoint after it.
- **"To a point in every part of the town … every path completes".**
  - Use `NavMesh.CalculatePath` with agent type 0 to each entry in `parts.json`. A path passes when its status is
    `PathComplete` **and** its last corner lies within 0.5 m of the target.
  - **Coverage, so "staged anywhere" means something:** take a 25 m grid over the playable area, sample each point
    onto the NavMesh, and path to it from the pub. Report the share that completes, and draw the failures. Each
    failure is either **explained** (an island the player cannot reach either, such as a rooftop, removed by min
    region or not) or **fixed**.
  - **"An agent paths":** also walk a real `NavMeshAgent` (agent type 0; speed ~3.5 to keep the run short) along
    the 3 longest paths to arrival. A computed path is not an agent that got there.
- **"Drawn on the top-down map".** One colour per part, failures in red, the pub marked.
- **"The console stays clean"** against the baseline:
  - the Version Control project-link notice;
  - `ThirdPersonCamera.cs:93` "POV Camera: Head bone not found!";
  - the town's two large-triangle mesh warnings on reload.

  Anything new is a finding: from the bake, the bounds, FallRecovery in normal play, the reimport, or
  "Failed to create agent".
- **"The frame rate is no lower than at the start of the story".** Use one instrument (1k): the same tour, the
  same editor layout, three rounds **at the start (Task 2) and three at the end**.
  - **Pass:** the median frame time per viewpoint after is not higher than before by more than the round-to-round
    spread you measured.
  - This laptop is not NFR1's desktop (lesson 19), so report it as a *difference*. The absolute 60 FPS verdict is
    Story 2.3's.

**AC6 (cross-cutting): Epic 1's loop still works on the new world, the project is put back, and the docs are consistent (NFR3, NFR5, NFR8, CLAUDE.md)**

- **The drunk on the whole-town bake.**
  - Run `Tools/Events/Trace Actor Route (Play)` for **at least 3 pooled cycles** (lesson 12).
  - Same pooled instance every cycle, on the NavMesh throughout.
  - He arrives by the agent's own test (remaining ≤ stopping distance; lesson 68) before he despawns.
  - The peak lasts ~1.5 s at x ≈ 26.1.
  - Compare with 2.1's `review/route-7cycles/arrival.txt`: he arrives 0.52–0.57 s before despawn, and the peak
    is 1.43–1.49 s.
- **The `Warp` guard (deferred :315).** In `EventActor.Begin` (`EventActor.cs:325`), check `Warp`'s bool.
  - On failure, warn **once** and stand the actor down, the way `SetDestinationToCurrentWaypoint` already does. It
    must never replay from the last despawn point.
  - Provoke it in a rig with the spawn moved off the mesh, and say at the top of the output that it was provoked.
  - It is engine hardening, not new behaviour: events stay data (ADR-3).
- **The player's own loop.** Re-run `Tools/Scale/Movement After (Play)`, Story 2.1's rig. Its kerb test sits at
  the slice, so it also proves the slice still walks. Expect:
  - walk 4.00 and sprint 7.00 m/s;
  - a jump apex of ~1.26 m;
  - the 0.126 m kerb at x 0.5 climbed;
  - raise and lower under 0.3 s.
- **No grading regression.** Run `Tools/Grading/Photo Shoot (Play)` once and diff stars and composition,
  unmasked (lesson 41), against 2.1's `photo-shoot-after/`.
  - Run the **Town Placement Study** once. It is world-dependent: fewer scatter colliders change what occludes,
    so photograph it and label it, and do not "fix" the grader.
  - Run the Gallery and HUD rigs once each, to prove they still **run**.
- **The EditMode suite passes.** It ran 191 at the last run; report the real count.
- **Fail-soft (NFR8).**
  - A missing or invalid `TownBoundsConfig` logs **once**, and the game runs on safe defaults.
  - FallRecovery never throws.
  - The map check with no Blender or no `.blend` warns once.
- **Put back exactly as you found it.** Record each value before you change it, and confirm it afterwards with
  `git diff` and an editor read-back:
  - the open scene;
  - `SampleScene`, saved **only** with the intended changes. Diff its YAML: removed colliders, `TownBounds` and
    `TownAccess`, the player spawn, `FallRecovery`, the surface's settings and modifiers. **No rig objects.**
  - `Time.timeScale`, `Application.runInBackground`, the quality level (PC), the Input System settings and the
    SessionState flags.
- **The docs are consistent** (Task 11):
  - the architecture's World bounds subsection, Decision Summary row and directory map;
  - the decision log;
  - `deferred-work.md` (the entries this story resolves);
  - `loop/lessons.md:61`, corrected in place if 1a disproves it;
  - the Epic 3 seed note.
- **Untouched:** grading (`ShotGrader`, `GradingConfig`), capture, the gallery, the HUD and the recording spike.

## Tasks / Subtasks

> **Working order and checkpoints.** The story is large, and a fresh session may have to pick it up halfway.
> - **Order:** 1 → 2 (baselines on today's world, before anything changes) → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11
>   → 12 → 13.
> - **Order matters in three places:**
>   - The bounds (Task 5) and the collider rule (Task 4) change what the bake sees, so the bake (Task 8) comes
>     after them.
>   - The route walk (Task 9) plans on the bake, so it comes after Task 8.
>   - The frame-rate baseline (Task 2) must be taken before *anything* changes.
> - **After each task:** commit, and add a dated line to the Dev Agent Record saying what was measured and where
>   the evidence is.
> - **If you run low on context,** stop at a task boundary and return `failed` with a `next_hint` naming the next
>   task.
> - **Never leave `SampleScene` half-edited.** Either finish the task, or revert the scene to the branch's last
>   commit with `git checkout -- Assets/Scenes/SampleScene.unity`. That is allowed: it is not a branch switch. Say
>   which you did.

- [ ] **Task 1: Measure the town and settle the eleven things that gate this story. DO THESE FIRST.**

  Each item has a recommendation. Record the choice **and the reasoning** in the Dev Agent Record, including where
  you follow the recommendation: a later reader needs to know it was decided, not defaulted.
  - [ ] **1a. Census the town in Unity, and name its parts.** Write `evidence/census/`: `census.json`,
        `parts.json`, `topdown.png` and `topdown.json`.
        - **Top-down:** render the playable area with an orthographic camera to a RenderTexture. Static geometry
          is fine in edit mode. Store the world → pixel transform. Every later trace is drawn on this image.
        - **Surface or buried (fact 7):** list what sits on the surface in each candidate part, and what sits
          below y −20. Settle whether the surface street block the zoom renders show exists in Unity, and where.
          If `loop/lessons.md:61` is wrong or incomplete, correct it in place (Task 11) and return the corrected
          fact as a lesson.
        - **Water:** the water renderer(s), their collider, their surface y, and whether any geometry lies under
          them (a lake bed).
        - **The oil rig:** its position, its deck height, its distance from the nearest shore point, and what the
          long straight object at x ≈ −549 is and whether it connects to the rig (Dev Notes §"Leads").
        - **The ring:** a slope map of the ring's inner faces (the share of ground over 45°); seams and holes
          between the overlapping mountain tiles (the dark spots in `map_topdown.png` are leads); and the outer
          edge, where the world ends.
        - **The road slab:** its edge height above the ground beside it, every 2 m along both sides.
        - **The lowest walkable ground** in the playable area, which sets the below-world height in 1e.
        - **The deactivated child** (fileID `65702495048144492`): what it is.
  - [ ] **1b. Which colliders go: "too small to be an obstacle".**
        - **Classify** every collider-bearing renderer into classes by base name **and** size: height above the
          local ground, and footprint. Photograph one of each class from the eye, with a person posed beside it.
        - **Recommended rule:** no collider for ground scatter a person walks *through* or *over* (grass,
          flowers, pebbles), **whatever its height**. Keep the collider on anything a person walks *around*:
          pines, solid bushes, rocks taller than the 0.35 m step, props, buildings. The water loses its collider
          (AC3).
        - **Write the rule as one pure function**, EditMode-tested. Use it in a removal tool **and** in
          `Tools/Add MeshColliders to World`, so a re-run never re-adds what was removed. The tool stays the
          way colliders are made.
        - **Recommended: keep the colliders as scene-level components** (today's mechanism) and delete the
          removed classes' components. **Fallback**, only if the re-export proof (AC1) shows fileIDs are not
          stable: an `AssetPostprocessor` for this one FBX that adds colliders by the same rule at import, with
          every scene-level collider removed. Record which you chose.
        - **Report:** removed and kept, per class, with counts and photos. Measure the effect on occlusion
          separately (AC6, Town Placement Study).
  - [ ] **1c. Where the town ends.**
        - **Recommended:** the playable area is the plain plus the ring's inner slopes, up to where the terrain
          itself is too steep or to the ring's crest. The outer tiles are scenery.
        - **The shore:** the barrier sits at the water's edge, so the player can stand on the shore and look out.
        - **The barriers:** invisible BoxColliders under `TownBounds`, on the `WorldBounds` layer, at least 3 m
          above the ground so no agent or player can stand on top of one. Give a rectangle or polygon of the
          playable area to `TownBoundsConfig`, for the bake volume and the off-world check.
        - **No gating:** every part is open. The GDD's reputation-gated districts are Epic 4.
        - **No markers:** a boundary shows nothing on screen, neither a message nor a glow. The GDD is diegetic,
          with no map markers (FR10). The world itself (the water's edge, the steep ground) has to explain where
          the player stops.
  - [ ] **1d. The oil rig: reachable on foot, or deliberately out of bounds.**
        - **Measure:** its distance from shore; any existing connection; and the size gate, *computed*: a 1.77 m
          subject passes the 0.20 gate out to ≈ 7.6 m at FOV 60 and ≈ 27.6 m at 18° (ADR-5's derived checks). At
          ~260 m from shore, nothing on the rig can be graded from the shore.
        - **The options:**
          - **(A) Reachable:** a walkway under a new `TownAccess` root, NavMesh-connected.
          - **(B) Out of bounds:** the shoreline barrier, with Epic 3's smoke break staged where the player can
            get within ~27 m (the industrial yard or the shore).
        - **Tie-break:** fewer moving parts and the smaller reversal cost, which means (B), **unless** 1a finds an
          existing walkable connection.
        - **Record it** where AC3 says. No gate.
  - [ ] **1e. Below the world: the design.**
        - **`FallRecovery`** goes in `Assets/Scripts/World/FallRecovery.cs` (namespace `CameraGame.World`), on
          the player. **`TownBoundsConfig`** is a ScriptableObject (`Assets/Scripts/World/TownBoundsConfig.cs` +
          `Assets/Data/World/TownBoundsConfig.asset`, created as YAML from the script's `.meta` GUID; memory:
          create-so-asset-via-yaml). Every field has a `Safe*` accessor and `TryGetConfigProblem`, copied from
          `PlayerConfig`'s shape, including the **finite** checks (lesson 72). It holds:
          - `belowWorldY`, between the lowest walkable ground (1a) and the buried block, for example the lowest
            walkable ground minus 10 and above −55;
          - the playable area;
          - `maxAirborneSeconds`, a backstop for falling with nothing beneath (≈ 3 s);
          - the safe-ground sample interval (≈ 0.25 s);
          - the minimum age of the sample it restores to (≈ 0.5 s), so it never restores to the lip of the hole.
        - **"Safe ground"** means: grounded; a downward ray hits within 0.3 m; the slope is at most 45°; inside the
          playable area; and `NavMesh.SamplePosition` finds the NavMesh within 0.5 m. Before the first safe
          sample, use the spawn point projected onto the NavMesh.
        - **The restore** goes through a new **`ThirdPersonController.Teleport(Vector3 position)`**. It disables
          the CharacterController, sets the position, re-enables it, and zeroes `_verticalVelocity` and
          `_jumpRequested`. The controller owns its motion state, so nothing else writes its transform while it
          runs.
        - **Logging:** `GameLog.Warn("World", …)` once per recovery, with both positions. A hole is a level defect
          worth finding. Nothing goes on screen.
        - **Dependency direction:** World → Player is allowed (§Architectural Boundaries). Player never references
          World.
  - [ ] **1f. See-through walls: measure the failure, then choose the fix.**
        - Build the AC4 wall rig here, and run it on today's code first (Task 2.4, "before"). Classify every
          failure: **near plane past the surface** (the geometric check fires) or **a single-sided face seen
          from behind** (the marker shows and the geometric check does not).
        - **Recommended camera-side fix**, if the failures are geometric:
          1. Put the nudge on the **yaw only**, so looking up never lifts the eye (deferred :317).
          2. **SphereCast** from the body axis at eye height along the nudge, with a radius equal to the near
             plane's half-diagonal plus a margin. Compute the half-diagonal from the camera's *live* FOV, aspect
             and near clip, so zoom is covered. Pull the camera back to the hit.
        - In the open the result must be **bit-for-bit today's pose**. Any new tunable, such as the margin, goes
          in `PlayerConfig` with `Safe*`, `TryGetConfigProblem` and tests.
        - **Single-sided faces** matter only where the player can stand behind them. Fix materials (Render Face:
          Both) only where you measured a failure. The map's materials are embedded (`materialImportMode: 2`), so
          remapping them is the cost. Record it.
  - [ ] **1g. The whole-town bake.**
        - **Collect:** Volume = the playable area (1c).
        - **Layer mask:** Default plus `WorldBounds`, so the barriers carve the edge.
        - **Exclusions:** a `NavMeshModifier` with `ignoreFromBuild` on the player root. Once the spawn is on the
          ground (1i), the capsule sits inside the volume and would carve a hole at spawn. Exclude anything else
          that is not world: rig objects are never in the saved scene anyway.
        - **Voxel:** start at **0.1** (radius ÷ 3, today's value) and measure the size. If it is over 50 MB:
          - try **0.15**, and prove the narrow gaps still pass: the 1.07 m gap between the barrier and the police
            car at spawn (2.1), and gaps between pines;
          - or split into several surfaces, each under 50 MB.
        - **Record:** the time, the size, the tiles and the choice.
  - [ ] **1h. How input is driven.**
        - **Keys:** Story 2.1's Keyboard state events and drive control. Check
          `InputSystem.settings.editorInputBehaviorInPlayMode` and restore it after the run.
        - **Yaw:** set it on the player's transform each frame, labelled "the mouse's job" (2.1's crossing did
          this), or queue Mouse delta events.
        - **Pitch** has only one real path, `OnLook` → `_pitch`, which is private. Queue Mouse delta events for the
          wall rig, with the delta-to-degrees control from AC4.
  - [ ] **1i. The player's spawn.**
        - **Recommended:** stand the player on open ground at today's spawn. 2.1 measured a clear gap at
          (0.45, ground, 3.6). Face them down the drunk's street at y = ground + 0.05: no 16 m drop, and not on the
          barrier.
        - Record the position. Rigs that used "the spawn landing" (`ScaleStudyRunner` `FromSpawn`) adapt; they are
          tools.
  - [ ] **1j. Getting onto the road.**
        - From 1a's edge heights, plus eye photographs of the slab edge from the grass at 3 points, decide whether
          the slab reads as a **visible ledge** or as flat ground (an invisible wall).
        - **Recommended:** add **visible** access (ramps or steps with the road or ground material, simple meshes)
          under `TownAccess`, where the AC2 route and the parts need it. **Never edit the map FBX**, so a re-import
          cannot remove it, and Alexv can fold it into the `.blend` later (the same rule as Story 2.5's AC). If
          the NavMesh shows every part connected through the existing access near x 0, "no access added" is a
          valid result. Record it.
        - Either way, the route walk must never hit an edge that looks like flat ground.
  - [ ] **1k. The frame-rate instrument.**
        - **The tour:** a fixed camera tour of at least 6 viewpoints, including the slice with the drunk active
          and the widest view across the plain from the hills. 5 s per viewpoint.
        - **Per frame:** unscaled delta. Add `ProfilerRecorder` physics markers if they read non-zero.
        - **Rounds:** 3 at the start, 3 at the end. Same Game view size, quality level PC, and nothing else
          running.
        - **Report:** medians and the round-to-round spread per viewpoint.

- [x] **Task 2: Baselines on today's world, before anything changes (AC2, AC4, AC5, AC6)**
  - [x] 2.1 Record the console baseline, the EditMode count (191) and `git status`.
  - [x] 2.2 **Frame-time tour × 3** (1k): `fps/before/`.
  - [x] 2.3 **Walk before:** the scatter crossing lines from AC2 and one straight line onto the road slab's edge,
        on today's world: `walk/before/`. This is AC2's positive control.
  - [x] 2.4 **Wall press before** (AC4, 1f): `walls/before/`.
  - [x] 2.5 **The drunk on today's slice bake:** a route trace of ≥ 3 cycles, `route/before/`.
  - [x] 2.6 **Copy aside** `_bmad-output/verification/photo-shoot/` (the rig wipes its own folder; 1.14), then run
        the Town Placement Study once: `photo-shoot-town-before/`.

- [x] **Task 3: The map check, the re-export path and camera import off (AC1)**
  - [x] 3.1 Write `tools/map/map_check.py` and `Assets/Scripts/Editor/MapCheck.cs` (`Tools/Town/Check Map Against
        .blend`). Run it on the real pair, which should give MATCH, then on the mutated scratch copy, which must
        name exactly the two changed groups. Then provoke "no Blender" and "no .blend": one warning each.
  - [x] 3.2 **The re-export proof** on a scratch export outside `Assets/`: the numbers, the node name paths and the
        GlobalSettings, all identical. Write `tools/map/map_export.py` and `Tools/Town/Re-export Map from .blend`
        with the proven settings. **Do not overwrite the real FBX unless 3.1 said DIFFERS.**
  - [x] 3.3 Set `importCameras = false` (and `importLights = false`, if you follow the recommendation), reimport
        and time it. Then do the read-backs from AC1. Add the EditMode importer test.

- [ ] **Task 4: Scatter colliders, spawn and road access (AC2)**
  - [x] 4.1 Implement the 1b rule and its tests. Apply it through the tool, update `AddMeshColliders.cs`, save the
        scene in its **own** `execute_code` call (memory: an unsaved-changes dialog turns one menu call into six),
        and diff the YAML: only the removed components.
  - [x] 4.2 Move the player spawn (1i).
  - [ ] 4.3 Add road access (1j) under `TownAccess`, or record why none is needed.

- [ ] **Task 5: Edges, water and the oil rig (AC3)**
  - [ ] 5.1 Add the `WorldBounds` layer, the `TownBounds` root and its barriers (1c). Remove the water's collider.
        Add any `NavMeshModifierVolume` needed under the water.
  - [ ] 5.2 Decide the oil rig (1d) and record it in the result file's `decisions`. The documentation follows in
        Task 11.
  - [ ] 5.3 **Edge walks:** at least 6 shore points and 8 ring bearings, walk and sprint, held 10 s, plus a jump.
        Output `edges/`, with the top-down trace and an eye photo at each contact.

- [ ] **Task 6: Below the world: FallRecovery (AC3)**
  - [ ] 6.1 Add `TownBoundsConfig` (script, asset, `.meta`), `FallRecovery` and `ThirdPersonController.Teleport`,
        with EditMode tests for the config (Safe*, finite checks, `TryGetConfigProblem`) and for any pure helper.
        Run `refresh_unity` and `read_console`, then **read the asset back through the editor**.
  - [ ] 6.2 **Provoke the four cases, 5 cycles each, back to back** (AC3). Write `fall/fall.csv` (case, cycle,
        below-at, restored-at, seconds, restore position, grounded-after, on-NavMesh, capsule-clear) and a frame
        after each restore. Warn at the top of the rig's output that these warnings are provoked on purpose.
  - [ ] 6.3 **Fail-soft:** no config, and an invalid config (NaN, ±Infinity, belowWorldY above the ground). Each
        must warn once and keep running.

- [ ] **Task 7: Walls (AC4)**
  - [ ] 7.1 Apply the 1f fix: `ThirdPersonCamera.cs`, plus `PlayerConfig` if a tunable is added.
  - [ ] 7.2 **Wall press after:** every wall, yaw, pitch and mode from AC4, with positive controls. Write
        `walls/after/` and a contact sheet of the worst frames. **0 marker pixels.**
  - [ ] 7.3 **The open-space pose is unchanged:** 1.650 up and 0.316 forward, read back in the open, at pitch 0
        and ±80.

- [ ] **Task 8: The whole-town NavMesh, the paths from the pub, and the drunk (AC5, AC6)**
  - [ ] 8.1 Write `Assets/Scripts/Editor/TownNavMeshBaker.cs` (`Tools/Town/Bake Town NavMesh`). Bake with the 1g
        settings and persist to the same asset. Record the time, size and tiles in `navmesh/bake.txt`. **Check the
        file size before committing.**
  - [ ] 8.2 **Paths:** from the pub to every part, plus the 25 m grid, plus real-agent walks of the 3 longest
        paths. Output `navmesh/paths.png`, `paths.csv` and `grid.csv`. Explain or fix every failure.
  - [ ] 8.3 **The `Warp` guard** (AC6) and its provoked case.
  - [ ] 8.4 **The drunk:** a route trace of ≥ 3 pooled cycles on the new bake, `route/after/`, compared with 2.5 and
        with 2.1's numbers.

- [ ] **Task 9: Walk the whole town (AC2)**
  - [ ] 9.1 Build the route through every part from `parts.json`, planned on the NavMesh. Walk it once and sprint it
        once with the real drive. Write `walk/after/trace.csv`, the top-down trace, the stick and fall log, and an
        eye photo per stick.
  - [ ] 9.2 **Scatter crossings after:** the same lines as 2.3, now unobstructed (±5% of the commanded speed).
  - [ ] 9.3 **Suspect the rig before the code.** Treat these as a broken rig until proven otherwise (lessons):
        zero sticks over a long route on a first run, every leg identical, a speed that is exactly round, or a
        blank trace.

- [ ] **Task 10: The console and the frame rate (AC5)**
  - [ ] 10.1 **Frame-time tour × 3 after:** `fps/after/`. Diff it against `fps/before/` per viewpoint, against the
        spread.
  - [ ] 10.2 A normal SampleScene play session with nothing scripted: the console holds only the baseline.

- [ ] **Task 11: The docs (AC3, AC6)**
  - [ ] 11.1 `game-architecture.md`:
        - a new subsection **"World bounds and traversal (Story 2.2)"** under §Architectural Decisions: the
          playable area, the barriers and their layer, the water, the oil-rig decision, FallRecovery, the
          collider rule, the bake (volume, voxel, size, split if any) and the map check and re-export tools;
        - a Decision Summary row **11 — World bounds**;
        - `Scripts/World/` and `Data/World/` added to §Directory Structure and §System Location Mapping;
        - the frontmatter `updated:` field.
  - [ ] 11.2 `decision-log.md` (`_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/`): a dated entry
        for the oil rig and the world bounds.
  - [ ] 11.3 `epics.md` §"Known seeds" → the Epic 3 bullet: **one** dated note giving the oil-rig decision and its
        consequence for the smoke-break event. **No other `epics.md` edit.**
  - [ ] 11.4 `deferred-work.md`: add a dated "Resolved by Story 2.2" note to each entry this story closes (:12,
        :315, :316 and :317), and note what stays open.
  - [ ] 11.5 `loop/lessons.md:61`: correct it in place if 1a disproved it, and return the new facts as lessons.

- [ ] **Task 12: Regressions and restore (AC6)**
  - [ ] 12.1 `Tools/Scale/Movement After (Play)`, the Photo Shoot (diffed unmasked against 2.1's
        `photo-shoot-after/`), the Town Placement Study (labelled world-dependent), and the Gallery and HUD rigs
        once each (they run, 0 exceptions).
  - [ ] 12.2 `run_tests` (EditMode), and report the count. Run `read_console`, `git status` and
        `git diff --stat`: only intended files.
  - [ ] 12.3 Read back every restored value (AC6). SampleScene must not be dirty and must hold no rig objects. Clear
        the SessionState flags.

- [ ] **Task 13: Handover. Queue what needs Alexv, and never self-certify it**
  - [ ] 13.1 Put the `human_checks` from Dev Notes §"Human checks to queue" in the result file, with exact paths
        and your own read.
  - [ ] 13.2 Put the `decisions` there too: 1b (the rule, and scene-level or postprocessor), 1c, 1d, 1e, 1f, 1g, 1i
        and 1j; `importLights`; and the re-export if one was made.
  - [ ] 13.3 Story status → `review`. Update `sprint-status.yaml`: the entry, and the **real** `last_updated:` key
        at ~line 40 (lesson 66). No `: ` and no trailing `:` (lesson 13). Check it with `yaml.safe_load`.

## Dev Notes

### What already exists. Read these before writing anything

- **The map:** `Assets/Models/game map .fbx` (GUID `ed581352065e66b458bda3bb12f7759e`). In `SampleScene` it is a
  PrefabInstance named `game map` (l.355907) at the origin with no scale. Its importer settings: `globalScale` 1,
  `useFileScale` 1, `addColliders` 0, `importCameras` **1**, `importLights` **1**, `materialImportMode` 2.
- **Colliders:**
  - The 16,738 scene-added MeshColliders (fact 3), made by `Assets/Scripts/Editor/AddMeshColliders.cs:6`, which
    walks every MeshFilter under `game map` or `World`.
  - Every one of the 16,738 map renderers has one (deferred :182). They sit on layer 0 Default, which is
    `GradingConfig.occluderMask`.
  - Note: the grader's occlusion rays do not see the pines in practice (lesson 67). Judge occlusion from pictures.
- **NavMesh:** the surface on `game map` (fact 4). The data is `Assets/Scenes/SampleScene/NavMesh-game map.asset`.
  `ProjectSettings/NavMeshAreas.asset` holds agent 0. **The runtime assembly `CameraGame.asmdef` does not reference
  `Unity.AI.Navigation`:**
  - `NavMesh`, `NavMeshAgent`, `NavMeshPath` and `NavMesh.CalculatePath` are `UnityEngine.AI`, built in, and need
    no reference.
  - `NavMeshSurface`, `NavMeshModifier` and `NavMeshModifierVolume` live in `Unity.AI.Navigation`, which only
    `CameraGame.Editor` references. Use them from editor code, or add the reference deliberately and record it.
- **The player:**
  - `main characters`, a PrefabInstance of `main characters.fbx` (importer `globalScale` 0.2), at (0, 20, 0).
  - `CharacterController`: 1.77 / 0.30 / centre (0, 0.885, 0) / skin 0.03 / step 0.35 (from config) / slope 45.
  - `ThirdPersonController.cs`:
    - `Awake` `:78–108` (warnings, then step written to the controller at `:96`);
    - the handlers `OnMove :148`, `OnJump :156`, `OnSprint :166` and `OnLook :175`, and
      `SetInputSuppressed :199`;
    - `_verticalVelocity` `:63` and `_pitch` `:67` (clamped ±80 at `:134`), both private;
    - `ApplyGravity :215` and `Move :280–315`.
  - `PlayerConfig.cs` (`Assets/Data/Player/PlayerConfig.asset`): walk 4, sprint 7, step 0.35, jump 1.2, gravity
    −15, ground-stick −2, eye 1.65, nudge 0.316, near 0.095. The `Safe*` accessors are at `:75–88`, plus
    `TryGetConfigProblem :100`, `TryGetBodyProblem :120` and `AgentMatchesBody :135`. **Copy its shape for
    `TownBoundsConfig`.**
  - `ThirdPersonCamera.cs` has `[DefaultExecutionOrder(-100)]`:
    - `Awake` returns early when the component is disabled (lesson 69) and applies the near clip;
    - the head-bone lookup is at `:90`, with its warning at `:93`;
    - `LateUpdate :111–122` places the camera at `GetHeadPosition() + transform.forward * ForwardNudge`. That is
      a **pitched** forward, which is the deferred :317 issue.
    - Main Camera: near 0.095 (from config), far 1000. It is the scene's only `AudioListener`.
- **Photo mode:** `PhotoModeController.SetPhotoMode(bool)` and `SetZoom` (Story 2.1 used them; raise is 0.15 s).
  `CameraConfig.asset` holds FOV 60 → 18.
- **The drunk:**
  - `EventManager` (scene l.117631) has `SpawnPoint` at world ≈ (31.8, 0.55, 19). **That is "the pub".**
  - `DrunkRoute` (l.204829) has waypoints at x 28.8, 25.9 (peak), 22.8 and 20.4, at z 19.
  - `EventActor.Begin` warps at `EventActor.cs:325` and **ignores the bool** (deferred :315). `NavUsable` is at
    `:60`, the off-mesh warning at `:341`, and `SetDestinationToCurrentWaypoint` at `:592`.
  - Agent: 0.30 / 1.77, speed 0.6.
- **Rigs to reuse.** Copy their patterns; do not re-derive them.

  | Menu path | Files | Use it for |
  |---|---|---|
  | `Tools/Scale/*` | `Editor/ScaleStudyRig.cs` + `Player/ScaleStudyRunner.cs` | The scene-restore pattern: it opens SampleScene from disk, never saves it, and keeps the return path in SessionState with an expiry stamp (`:51–141`; lesson 37). The real drive: `Keys()`, `DriveControl()` (`:806`) and the crossing loop with its stuck detector (`:712–803`). `GroundY`, `Teleport` and `Settle`. `Movement After` is AC6's regression. |
  | `Tools/Events/Trace Actor Route (Play)` / `- 7 cycles` | `Editor/RouteTraceRig.cs:47/:51` + `Events/RouteTraceRunner.cs` | The drunk's pooled cycles. Its output folders point at 2.1's evidence: add 2.2 output paths, do not overwrite 2.1's. |
  | `Tools/Grading/Photo Shoot (Play)` (+ Placement Study) | `Editor/PhotoShootRig.cs` + `PhotoMode/PhotoShootRunner.cs` | The no-regression check. It **wipes** `verification/photo-shoot/`, so copy that folder aside first. |
  | `Tools/HUD/Grade HUD Shoot (Play)`, `Tools/Gallery/Gallery Shoot (Play)` | `GradeHudShootRig.cs` / `GalleryShootRig.cs` | Run once each. `GradeHudShootRunner.VisiblePixelFraction` is a working "visible by rendered pixels" measurement: reuse its idea for the wall marker. |

  **New rigs** go in `Tools/Town/*`: `Assets/Scripts/Editor/TownRig.cs`, with its runner in the **runtime**
  assembly behind `#if UNITY_EDITOR`, for example `Assets/Scripts/World/TownRigRunner.cs` (CLAUDE.md trap: an
  editor-assembly MonoBehaviour is "missing" in play mode). The map check and the bake tool are editor-only and
  never enter play mode.
- **Logging:** `GameLog.Info/Warn/Error`. `read_console` hides `Debug.Log`, so grep
  `%LOCALAPPDATA%\Unity\Editor\Editor.log`.
- **Tests:** `Assets/Tests/EditMode/`, 191 at the last run; there is no PlayMode assembly. The test asmdef
  references **`CameraGame` only**. Pure rules that live in the editor assembly need `CameraGame.Editor` added to
  `CameraGame.Tests.EditMode.asmdef`'s references (it is editor-only, so that is allowed). Otherwise, put the pure
  rule in the runtime assembly. Record which.
- **Tooling:** headless Blender 5.0.1 is at `C:\Program Files\Blender Foundation\Blender 5.0\blender.exe` and loads
  the map in ~2–6 s (lesson 47). ffmpeg/ffprobe 8.0.1, numpy and Pillow are installed; read
  `tools/verification/README.md` first. `plan-epic-2/compare_fbx.py`, `map_topdown.py` and `map_zoom.py` are
  working headless scripts to start from.

### Leads on the town's geography. Measure before relying on any of them

These are from the planning renders of the *evaluated* `.blend` (`plan-epic-2/zoom_plain_900m.jpg` and others).
They are converted with **Unity (x, z) = (−bx, −by)**, the transform Story 2.1's `tools/crossing.py` `topx()`
uses, and are good to about ±10 m.

| Lead | Approx. Unity position | Note |
|---|---|---|
| Main road (the raised slab) | runs along z ≈ −2 … −10, x ≈ −600 … 205 | 2.1's "plain line" ran on it at z −8.5 |
| Spawn, police cars | (0 … 15, z ≈ −4) | the slice |
| The drunk's route | x 20.4–31.8, z 19 | south of the road, on grass |
| The one surface house 2.1 found (`House Complete.001`) | (−47, 6, 19) | the row of house plots continues to x ≈ −130 at z ≈ 20 |
| A **surface** street block (H-shaped roads, a large dark building, orange-roofed houses) | x ≈ −100 … −215, z ≈ −10 … −170 | the large building is ≈ (−113, −89). Compare with lesson 61's buried block at y ≈ −60 under the slice |
| An industrial-looking group (white sheds, a red structure, a grid) | x ≈ −340 … −480, z ≈ 65 … 140 | **beyond the tyre line** (x −223…−213) |
| A rectangular stone enclosure, with a path to the road | x ≈ −190 … −255, z ≈ 160 … 230 | |
| The shoreline | z ≈ 270–275 | water beyond it |
| The oil rig | ≈ (−355 … −390, z ≈ 535) | ~260 m off the shore |
| A long straight object (a pole line? a pipe? a pier?) | x ≈ −545, z ≈ −85 … 535 | from north of the road out into the water. **Check whether it reaches the rig** |
| The plain's edges (the ring's foot) | x ≈ 205 (one side), z ≈ −295 (the far side from the water) | the ring tiles are ~1 km each |
| A boulder pile in the hills | ≈ (76, −332) | |
| Dark spots on the outer tiles (holes?) | ≈ (−730, −820) and others in `map_topdown.png` | candidates for AC3's "real hole" |

### The genuinely new risks in this story

1. **The scene's 16,738 colliders are keyed to FBX child fileIDs** (fact 3). A careless re-export, or an importer
   change that renames children, orphans them all with a clean-looking console. Then the town is walk-through and
   the NavMesh bakes over nothing. **After every reimport, count the colliders that still resolve.**
2. **Big binary diffs.** The scene shrinks when scatter colliders go; the NavMesh asset grows with the town; an FBX
   re-export is ~77 MB of LFS.
   - Check every file's size **before committing**: under 50 MB, or the orchestrator's push breaks.
   - Never add an LFS rule.
   - Add an EditMode guard that `NavMesh-game map.asset` and `SampleScene.unity` are each under 50 MB on disk. It is
     a structural invariant, and it fails before the push does.
3. **The camera change runs in every play session and every rig.** The open-space pose must be unchanged (AC4).
   Re-run the rigs that look through it after the fix, not before (lesson 43).
4. **The world changes under every earlier measurement.** Fewer colliders mean different occlusion; a whole-town
   bake means a different mesh under the drunk.
   - Baseline first (Task 2), with the same instruments.
   - Expect world-dependent numbers to move. Label them, and do not "fix" the grader to bring old numbers back.
5. **Long editor operations:** a full reimport of the map, a whole-town bake, and a long rig run.
   - After a play-mode run, the MCP bridge drops for 10–60 s (lessons 59, 73). Call `refresh_unity` with
     `wait_for_ready`, and check that `execute_menu_item` returned success **before** starting a wait.
   - Wait with Monitor and an until-loop on `Editor.log` (lesson 74). Never sleep blind.
   - Never run asset operations during play mode (memory: it wedged a lifecycle in 1.8).

### Architecture compliance (non-negotiable)

- **§Configuration:** tunables live in ScriptableObjects in `Assets/Data/`; invariants live in static classes. The
  below-world height, the playable area and the recovery timings go in `TownBoundsConfig`. Any camera margin goes
  in `PlayerConfig`. **No new code literals** for world numbers.
- **`CameraGame.*` namespaces and one game asmdef** (AR5). `Scripts/World/` is a **new feature folder**; add it to
  the architecture's directory map (Task 11). World → Player is allowed; Player → World is not.
- **Do not rename `ThirdPersonController` or `ThirdPersonCamera`.** Adding `Teleport` is the only API change to the
  controller.
- **ADR-3: events are data.** The `Warp` guard is engine hardening, logged once (NFR8). Do not add drunk-specific
  logic.
- **ADR-5's scale rule:** never scale a root that carries a `CharacterController`, `NavMeshAgent` or
  `AudioSource`. New geometry (`TownAccess` ramps or walkways) is modelled at real size, in metres.
- **The map FBX is Alexv's art.** Never modify its contents from Unity. Additions go under their own scene roots
  (`TownBounds`, `TownAccess`) so a re-import cannot remove them.
- **Fail-soft (NFR8) and pooling (NFR3).** Nothing in this story adds `Instantiate`/`Destroy` to a gameplay loop.
  FallRecovery allocates nothing per frame (a fixed ring buffer).
- **URP only (NFR4).** Any material for `TownAccess` is URP Lit or reuses the map's materials.
- **ADR-4 (pending):** everything that moves or makes sound must be recordable. A teleport is a transform change,
  and the replay records transforms, so there is nothing to add. Note it in the architecture subsection.

### Project Structure Notes

- **New:**
  - `Assets/Scripts/World/` (+ `.meta`): `TownBoundsConfig.cs`, `FallRecovery.cs` and `TownRigRunner.cs` (behind
    `#if UNITY_EDITOR`);
  - `Assets/Data/World/` (+ `.meta`): `TownBoundsConfig.asset`;
  - `Assets/Scripts/Editor/`: `MapCheck.cs`, `TownNavMeshBaker.cs`, `TownRig.cs`, and the collider-rule tool
    (or extend `AddMeshColliders.cs`);
  - `tools/map/`: `map_check.py` and `map_export.py`;
  - EditMode tests: the config, the collider rule, the importer invariant and the file-size guard;
  - `.meta` files for everything under `Assets/`.
- **Updated:**
  - `ThirdPersonController.cs` (`Teleport`), `ThirdPersonCamera.cs` (the 1f fix), and `PlayerConfig.cs` and its
    asset if a tunable is added;
  - `EventActor.cs` (the `Warp` guard) and `AddMeshColliders.cs`;
  - `Assets/Models/game map .fbx.meta`, `SampleScene.unity`, `NavMesh-game map.asset`, and
    `ProjectSettings/TagManager.asset` (the layer);
  - `RouteTraceRig.cs` / `RouteTraceRunner.cs` (2.2 output paths);
  - `game-architecture.md`, `decision-log.md`, `epics.md` (the one seed note), `deferred-work.md`,
    `loop/lessons.md`, `sprint-status.yaml` and this story file.
- **Not touched:** grading code and config, capture, the gallery, HUD behaviour, the spike,
  `loop/dependencies.yaml`, `loop/runtime/`, and the `.blend` in the shared folder. The map FBX is untouched unless
  AC1 finds the map changed.
- **Outside git:** evidence in `_bmad-output/verification/2-2-walk-the-whole-town/`, with suggested subfolders
  `census/`, `map-check/`, `colliders/`, `edges/`, `fall/`, `walls/before|after/`, `navmesh/`, `walk/before|after/`,
  `route/before|after/`, `fps/before|after/`, `photo-shoot-*` and `regression/`. **Never `Temp/`.**

### Previous Story Intelligence (2.1 above all; 1.2, 1.7, 1.9–1.12, 1.14)

- **2.1 (ADR-5)** is the base this story stands on:
  - **Its tools:** the real-input drive with its controls, the scene-restore pattern, `GroundY`, the stuck
    detector, the route trace's arrival test (lesson 68), and the 7-cycle run.
  - **Its findings:** the slab, the tyres, the spawn, the buried block and the police car.
  - **Its decisions this story revisits:** the bounded bake (deferred :316) and the near-plane geometry
    (deferred :317).
  - **What 2.1's review taught:** a fixed "reach" radius hid a drunk who never arrived. Measure arrival with the
    agent's own test. An infinite value wedged the controller for good, so make every `Safe*` accessor finite.
- **1.2** baked the original whole-map NavMesh and play-tested the slice. Alexv's play-test found "see-through
  walls" (deferred :12). `Assets/Screenshots/story_1_2_play_pov.png` is an old eye-view.
- **1.7** relaid the drunk's route. Its deferred items name the `hasPath` and `pathStatus` gaps (:48–49) that bite
  when Epic 3 authors routes across the town. This story *proves the mesh*; it does not change `EventActor`'s
  arrival logic beyond the `Warp` guard. Say so in Epic 3's seed if the path tests show corners.
- **1.9–1.12:**
  - The Photo Shoot is the no-regression instrument.
  - The town occludes through trees in ways rays miss (deferred :162–201). **Measure visibility by rendering.**
    This is why the wall detector counts marker pixels rather than trusting rays alone.
  - 1.12 retracted a committed wrong diagnosis made from a probe taken at the wrong instant. Measure at the moment
    that matters.
- **1.14** is the model for deciding by measuring:
  - a recommendation per gating item;
  - every cell with evidence, or "not measurable, because …";
  - difference-in-differences on this heating laptop (lesson 28).

### Inherited context you should not rediscover

- **CLAUDE.md traps:**
  - an editor-assembly MonoBehaviour is "missing" in play mode;
  - render to an RT, or `CaptureScreenshotAsTexture` after `WaitForEndOfFrame` for overlay UI, never the file
    overload;
  - assets loaded before `NewScene(Single)` lose their native half;
  - ProjectSettings edits on disk do not apply while the editor runs, so use the editor API (the layer, the
    agent);
  - an edit on disk is invisible to a rig until Unity reimports it: call `refresh_unity`, wait, and read back;
  - `Temp/` is deleted on shutdown;
  - disabling a manager does not empty its world.
- **Memory notes:**
  - `read_console` hides `Debug.Log`;
  - never run asset operations in play mode;
  - save a scene in its own call before a rig;
  - poll the object that moves;
  - hold inputs must be Value actions;
  - create SO assets as YAML from the `.meta` GUID;
  - code review excludes test rigs.
- **Loop lessons:**
  - 9: a blank or uniform capture means a broken rig;
  - 10: read back after a refresh;
  - 12: bugs hide in the second cycle;
  - 13 and 66: the `sprint-status` YAML traps;
  - 37: SessionState flags expire;
  - 41: unmasked star diffs;
  - 43: re-run rigs after code that boots in every play session changes;
  - 47–50: headless Blender, the census, the plain, scoping greps (a recursive grep from the root times out);
  - 56: the NavMesh asset's size limit;
  - 59 and 73: the MCP bridge drops after play mode;
  - 60: set the voxel override explicitly and read the `cellSize` back;
  - 61: the buried block (verify it);
  - 62: the slab and the tyres;
  - 65: Unity's files are CRLF, so edit with `newline=''`;
  - 67: grader rays miss the pines;
  - 68: the agent's own arrival test;
  - 69: `Awake` runs on disabled components;
  - 72: finite checks;
  - 74: Monitor waits on `Editor.log`;
  - 75: in `execute_code`, write `UnityEngine.Object.FindObjectsByType`.

### Git Intelligence

- `HEAD` is `d050681` on `loop/2-2-walk-the-whole-town`. The last five commits merged Story 2.1 (ADR-5 applied,
  reviewed, closed) and added its lessons. The code described above is current: `PlayerConfig`, the camera's
  `Awake` near clip, the drunk at 0.6 m/s, and the slice-bounded bake.
- The working tree is clean apart from the untracked `.codex/` and `AGENTS.md`. They are not yours, so do not
  stage them. **Stage by name** (`git add <path> <path>.meta`).
- Commit as you go, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never
  switch branches, merge, rebase, reset, stash or push.

### Latest technical information (this project's versions, so verify before you rely on it)

- **Versions:** Unity **6000.3.8f1**, URP **17.3.0**, AI Navigation **2.0.10**, Input System **1.18.0**, Test
  Framework 1.6.0, and Blender **5.0.1** headless.
- **CharacterController teleport.** Physics auto-sync of transforms is off by default in recent projects, so
  setting `transform.position` on a controlled object can be undone by the next `Move`. Disable the controller,
  set the position, and re-enable it (or call `Physics.SyncTransforms()`). Verify with a read-back the next frame.
  The default `slopeLimit` is 45°; read the player's value.
- **AI Navigation 2.x:**
  - `NavMeshSurface.collectObjects` (All, Volume, Children, MarkedWithModifier), `layerMask`, `useGeometry`,
    `overrideVoxelSize`/`voxelSize`, `overrideTileSize`/`tileSize`, `minRegionArea` and `BuildNavMesh()`.
  - `NavMeshModifier.ignoreFromBuild`, and `NavMeshModifierVolume` with an area.
  - `NavMesh.GetSettingsByID` returns a **copy**.
  - A `BuildNavMesh()` in edit mode is not persisted by itself. Persist it as 2.1 did (its Dev Agent Record,
    Task 7.4, and `navmesh/bake.txt`): save the `NavMeshData` asset and the scene reference.
  - Check every name with `unity_reflect` before use.
- **`ModelImporter`:** `importCameras`, `importLights` and `SaveAndReimport()`. Child fileIDs come from the
  object names and hierarchy, so the same names in the same hierarchy give the same IDs. Verify by counting
  resolving colliders after the reimport rather than trusting this.
- **Blender FBX:**
  - Export is `bpy.ops.export_scene.fbx(...)`, the bundled `io_scene_fbx` add-on, enabled under
    `--factory-startup`. List its options in the session you use (`bpy.ops.export_scene.fbx.get_rna_type()`) and
    never hard-code them from memory.
  - Import (`bpy.ops.import_scene.fbx`) worked headless on this Blender for planning.
  - `io_scene_fbx.parse_fbx.parse(path)` reads `GlobalSettings`.
- **Physics queries:** `Physics.queriesHitBackfaces` is false by default, so a ray from *under* a one-sided terrain
  mesh does not hit it. Do not build "below the world" detection on an upward ray without setting it for that
  query.

### Verification plan: what would convince a sceptic

| Claim | Evidence | How it is judged |
|---|---|---|
| The check compares the right things and can fail | `map-check/real/report.md` (MATCH), `map-check/mutated/report.md` | the mutated run names exactly the 2 changed groups; no-Blender and no-.blend give one warning each |
| The re-export path keeps the scene's references | `map-check/reexport-proof.md` | scratch export = the current FBX on numbers, node paths and GlobalSettings |
| Camera import is off, nothing broke | importer read-back, collider count, `Camera.allCameras` | `importCameras` false; 16,738 renderers; colliders resolving = expected (16,738 − removed); one camera |
| The parts are real | `census/topdown.png` + `parts.json` + eye photos | every part is a surface place you can see in a photo; buried versus surface settled |
| Scatter no longer blocks | `walk/before` against `walk/after` lines, the hit log | before shows the hits (the control); after holds ±5% speed with no hits on removed classes |
| The whole route walks | `walk/after/trace.png`, the stick log, eye photos | 0 falls; every stick explained (visible obstacle) or fixed; walk and sprint both complete |
| The edges hold | `edges/` traces and eye photos | inside bounds always; never on water; never off the map |
| Below the world → put back ≲ 2 s, silently | `fall/fall.csv` (4 cases × 5 cycles), post-restore frames, console | worst ≲ 2 s; grounded ≤ 0.5 s after; on the NavMesh; no Error; no new UI in the frame |
| Walls never show what is behind them | `walls/before` against `walls/after`, controls, contact sheet | after: 0 marker pixels on every pose; each wall's control shows the marker; the open-space pose unchanged |
| The whole-town NavMesh paths everywhere | `navmesh/paths.png`, `paths.csv`, `grid.csv`, the agent walks | every part `PathComplete` with the end within 0.5 m; grid failures explained; 3 agents arrive |
| The bake is shippable | `navmesh/bake.txt`, file sizes | every committed file < 50 MB; no LFS rule added |
| The frame rate is not lower | `fps/before` against `fps/after` | median per viewpoint not higher beyond the measured spread |
| Epic 1 still works | route trace ≥ 3 cycles; Movement After; Photo Shoot diff | same instance, on the mesh, arrives before despawn, peak ~1.5 s; 4/7 m/s, apex ~1.26, raise < 0.3 s; no verdict moved beyond noise |
| Project restored | `git status`, read-backs, scene YAML diff | only intended changes; no rig objects |

**Fail-soft scenarios to provoke on purpose.** Say so at the top of the rig's output, and name each one:
- no `TownBoundsConfig`, and an invalid one;
- the drunk's spawn off the mesh (the `Warp` guard);
- the map check with no Blender and with no `.blend`;
- every FallRecovery case.

Each must log **once** and keep running. Exclude them, by name, from the clean-console check.

### Human checks to queue (never self-certify)

1. **AC2 and AC3, how the edges and the open ground feel.** Does walking the town feel open, with no invisible
   walls on ground that looks open? Do the shoreline and hill boundaries read as the edge of the world, not a
   glass wall?
   - **Evidence:** the top-down traces; eye photos at each barrier; a short real-time clip along the shoreline
     and up a hill (2.1's `Film` rig pattern).
   - **What Alexv plays:** SampleScene; walk from spawn to the shore, along it, and up a hill.
2. **AC3, the put-back.** Is an instant teleport after falling through acceptable to a player? There is no fade.
   - **Evidence:** a clip of a provoked fall and its restore, and `fall/fall.csv`.
3. **AC4, walls up close.** Do walls look solid when you are pressed against them and look up or down?
   - **Evidence:** the `walls/after` contact sheet.
4. **Only if access was added (1j):** do the ramps or steps read as part of the town?
   - **Evidence:** eye photos.
5. **Decision batch, not a check:** 1b (the rule, and scene-level or postprocessor), 1c, 1d (the oil rig), 1e, 1f,
   1g, 1i and 1j; `importLights`; any re-export.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.2; #Epic 2: The Living Town; #Known seeds (Epic 3)] — the ACs (verbatim above), planning facts, sequencing, where the oil-rig note goes
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR13, FR14, FR15, NFR1, NFR3, NFR5, NFR8, AR8, AR9
- [Source: _bmad-output/planning-artifacts/game-architecture.md#ADR-5 (World scale)] — design constants, the scale rule, the consequences handed to 2.2
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Configuration; #Architectural Boundaries; #Directory Structure; #Error Handling] — config rule, dependency direction, fail-soft
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md:259, :323–331] — vantage points; one contiguous town, districts, reputation unlocks (Epic 4)
- [Source: _bmad-output/implementation-artifacts/2-1-settle-the-players-scale-in-the-town.md#Dev Agent Record; #Review Findings] — the tools, the findings, the bounded bake, the arrival lesson
- [Source: _bmad-output/implementation-artifacts/deferred-work.md :12, :48–53, :103, :162–201, :315–318] — see-through walls, route edges, TagManager, occlusion by rendering, the four 2.1 review items
- [Source: _bmad-output/verification/plan-epic-2/ (blend_vs_fbx.json, compare_fbx.py, map_topdown.png, zoom_*.jpg, map_census.json)] — the AR9 evidence and the geography leads
- [Source: _bmad-output/verification/2-1-settle-the-players-scale-in-the-town/ (navmesh/bake.txt, crossing/, decision/task1-decisions.md, review/route-7cycles/arrival.txt, tools/crossing.py)] — the bake, crossings, references, arrival numbers, the top-down transform
- [Source: Assets/Scripts/ThirdPersonController.cs:63, :67, :78–108, :148–199, :215, :280; ThirdPersonCamera.cs:90–93, :101–134; Player/PlayerConfig.cs:75–137] — the controller, the camera, the config pattern
- [Source: Assets/Scripts/Editor/AddMeshColliders.cs; Editor/ScaleStudyRig.cs:51–141; Player/ScaleStudyRunner.cs:712–824; Editor/RouteTraceRig.cs:47–55] — colliders, the restore pattern, the drive
- [Source: Assets/Scripts/Events/EventActor.cs:60, :325, :341, :592] — the Warp guard
- [Source: Assets/Scenes/SampleScene.unity l.355907 (map PrefabInstance), l.355955 (deactivated child), l.492176 (NavMeshSurface), l.117631 (EventManager), l.204829 (DrunkRoute); Assets/Models/game map .fbx.meta] — the scene facts
- [Source: CLAUDE.md#Verifying Your Own Work; tools/verification/README.md; loop/lessons.md; loop/dependencies.yaml] — rigs, restore, evidence location, Gemini rules, dependencies

## Dev Agent Record

### Agent Model Used

Claude Opus 5.5 (`claude-opus-5-5`), loop DEV phase, 2026-11-11.

### Debug Log References

Evidence root: `_bmad-output/verification/2-2-walk-the-whole-town/` (git-ignored, on disk). Analysis scripts in its
`tools/` (grid.py, census_analyse.py, wide.py, playable.py, sheet.py).

### Task 1 — measurements and decisions (2026-11-11)

**1a Census (Unity, edit mode, `Tools/Town/Census (edit mode)`, `TownCensus.cs`)** → `census/` (census.json,
topdown.png + topdown.json, the 2 m grid `grid_*.f32/u8`, slope_map.png, wide_height.png (whole map at 10 m),
road_edges.csv, rig.json, parts.json, photos/ with sheet_a.jpg and sheet_b.jpg).
- 16,738 renderers, 16,738 MeshColliders, 492 classes. Scatter families (particle instances "Plane|<mesh>|Dupli|n"):
  **Circle = 6,000 pine trees** (3.2–10.4 m), **Cube = 8,000 grass tufts** (1.3–1.6 m), **Icosphere = 1,000 white
  rocks** (886 stick out 1.25–1.5 m above the ground, 91 at most 0.35 m), Cylinder = 20 red stumps at the white
  trees' bases (0.82 m), _10_tree 10 white trees, _11_tree 50 bare trees (photos c01–c06).
- **Fact 7 settled: BOTH exist.** A SURFACE street block (asphalt "Object_14", shops, SuperMarket, white buildings)
  stands at x −290…−90, z −240…−5, south of the road (topdown.png, a09). Lesson 61's buried copy (Casa, HH, Tetto,
  cars, Factory…) is a separate set at y −68…−48 under the slice (x −75…25, z −94…18). 674 renderers lie entirely
  below y −1, all with colliders, including a dome (Icosphere_Colore_0, top −6.2) and a flat plane (Plane.017,
  y −6.5) right under the spawn, and the scatter that sank into the hole below.
- **Water:** `Plane001` + `Plane001.001` (material Water.001), both with MeshColliders, surface y ≈ 1.04 (wavy,
  −6…9). The plain (y ≈ 0.85) continues 0.2 m under the water at the shore; a lake bed exists under the sea
  (y −46…7, 22,347 cells). The water's edge runs straight at z 269.5–273.5 from x −610 to 212.
- **A dark pond** (`Plane.010`, Material.007, x −593…−505, z −106…−21, bottom y −4.82) reads as water from the eye
  (a02) — treated as water (a no-go with a barrier), see 1c.
- **A real hole** in the plain at x −382…−376, z −232…−217 (no collider at all in the column; a01 shows a dark
  patch). Kept as found (it is the .blend's) and used as AC3 case 2. A second gap at the shore (x −158, z 283)
  lies beyond the shore barrier.
- **The oil rig:** 211 renderers, x −436…−341, z 492…563, decks y 45–88 on legs from the sea floor; ~220 m from
  the shore (z 269.5). Nearest dry ground is the west ring's coast (−522, 544), 86 m away, outside the playable area.
- **The long object at x ≈ −548** is `Sphere_4_Mat.6_0`, a flat grey strip 11 m wide from z −32 to 473 that runs
  into the sea (a03, a07). Its end is 113 m from the rig: **no walkable connection.**
- **Ring:** mostly walkable slopes (slope_map.png: grey) with steep patches (red); the inner faces reach 20–45 m
  within ~70–100 m of the plain's edge; the ring tiles rise to ~200 m and several OUTER tiles have holes (black in
  wide_height.png). Slope alone does not stop a player anywhere along the plain's edge.
- **Road slab** (`Plane.004_M3_0` + `.001`, z −8.7…4.45): edge 1.1–1.2 m (median) above the grass on both sides,
  steppable (≤ 0.35 m) in 7 runs per side (e.g. x −28…2, 56…96, −370…−330, 202…284; road_edges.csv). From the
  grass it reads as a visible raised ledge with a dark side face (j01–j03).
- **Lowest walkable ground** in the playable area (1c): −5.0 (south-east hill hollows) and the pond's −4.82; the
  rest of the plain is −0.1…1.0 (1st–50th percentile).
- **The deactivated child** (FBX fileID 65702495048144492): no sub-asset of today's FBX has that fileID (68,837
  checked; 0 cameras, 0 lights). It is an orphaned scene override, most likely the old embedded Blender camera
  (CLAUDE.md). Left exactly as is.
- **parts.json:** 18 parts on walkable ground (pub, house row, street block, log cabins, farm = the "industrial
  yard", villa = the stone enclosure, beyond the tyre line, shore W/E, the plain's 4 corners, and hills S, SW, E, W,
  NW).

**1b Collider rule** (decision): no collider for (1) **grass** (scatter family Cube) whatever its height; (2) any
other **scatter** instance that sticks up no more than the step height (0.35 m) above the ground under it;
(3) **water** (material Water*); (4) **buried** geometry whose top is below y −1 m (the 674 renderers under the
plain, which only a player who fell through could reach). Everything else keeps its collider: pines, white/bare
trees, stumps, rocks taller than a step, props, buildings, terrain, the pond. Scene-level components kept (today's
mechanism); the rule is one pure function (`CameraGame.World.ColliderRule`).
Reason for (4): with them, a player who falls through near spawn lands on a hidden floor at y −6.5 and is grounded
above any sane below-world height; without them, they fall and are put back.

**1c Where the town ends** (decision): playable area = the plain plus the ring's inner slopes, the polygon
(−680, 269.5) → (210, 269.5) → (210, 215) → (260, 165) → (260, −320) → (215, −340) → (200, −400) → (−680, −400)
(`decision/playable_polygon.json`). Every edge sits on a hillside except the north edge, which is the water's edge
(z 269.5, ≤ 4 m short of the water). The NE and SE corners are cut to keep out a gully (y −12) and a valley that
leaves the ring. Plus a barrier ring around the pond. No gating, no markers.

**1d Oil rig** (decision): **(B) deliberately out of bounds.** No existing walkable connection (the strip ends
113 m short; the decks stand 45–88 m above the sea on legs). From the shore (220 m) nothing on it can be graded
(the 0.20 gate reaches ≈ 27.6 m at 18°). Epic 3's smoke break goes where the player can get within ~27 m.

**1e Below the world** (decision): `FallRecovery` restores the newest safe sample older than 0.5 s when (a) the
feet go below `belowWorldY` = −15 (lowest walkable −5 minus 10, well above the buried block), (b) the body leaves
the playable polygon, (c) airborne with nothing beneath (a downward sphere cast finds no surface above
`belowWorldY`) for 0.15 s, or (d) airborne for more than 3 s (backstop). (c) is what keeps a fall through a hill
(y up to ~50) under ~2 s.

**1h Input:** Story 2.1's Keyboard state events for W/Shift/Space (PlayerInput → OnMove/OnSprint/OnJump), yaw set
on the transform each frame ("the mouse's job"), pitch through queued Mouse delta events (OnLook → _pitch).

**1i Spawn** (decision): stand the player on open ground at today's spawn, (0.45, ground + 0.05, 3.6), facing the
drunk's street; not on the 1.4 m barrier.

**1b revised by measurement — THE SCATTER COLLIDERS NEVER WORKED (2026-11-11, decision).** All 15,020 scatter
MeshColliders (every pine, grass tuft, white rock and stump: families Circle, Cube, Icosphere, Cylinder) reference
mesh fileIDs that no longer exist in the FBX (it holds 1,658 shared meshes; the scene's colliders point at
per-instance ones a later reimport deduplicated). They load with `sharedMesh == null` and collide with nothing:
the player walks straight THROUGH pines and rocks today (walk/before: `pine_trunk` 16 m at 4.00 m/s, 0 hits), and
this is why the grader's rays never saw the pines (lesson 67). Only the 60 white/bare trees' and the 1,658
non-scatter pieces' colliders resolve. So the rule is applied as: **remove** grass, low scatter, water and buried
colliders; **repair** the kept scatter (assign the MeshFilter's mesh), except pines: a pine's mesh hangs its
lowest foliage tier at 1.2–1.36 m out to a 4 m radius, so a full-mesh collider would make every one of ~6,000
pines (4–9 m apart; see `fps/before/vp_plain_from_east_hill.jpg`) a 7–8 m chest-high wall and turn the plain into
a maze. **Pines get a trunk collider** (a CapsuleCollider round the trunk, the usual game choice); rocks and stumps
get their repaired MeshCollider. An EditMode guard checks that every map collider reference in SampleScene
resolves. Reversal: re-run the tool with the rule changed.

### Task 2 — baselines on today's world (2026-11-11)

- 2.1: EditMode 191/191; console baseline = only "POV Camera: Head bone not found!"; the working tree held only
  the untracked `.codex/` and `AGENTS.md`.
- 2.2 `fps/before/` (`Tools/Town/FPS Tour - before`): 7 viewpoints × 5 s × 3 rotated rounds, Game view 953×476,
  quality PC. Medians of rounds (ms): slice_drunk 28.5, plain_from_east_hill 36.0, street_block 22.7,
  shore_to_oil_rig 9.2, farm 13.0, south_hill_north 62.5, spawn_street 20.8. Frame time climbs every round
  (south hill 28.6 → 62.5 → 76.0): the laptop heats (lesson 28), so the spread is wide (5–47 ms).
- 2.3 `walk/before/` (`Tools/Town/Walk Lines - before`): drive control 0 / 6.0 / 0 m. grass_0 (7 tufts) and
  pine_trunk walked at a steady 4.00 / 7.00 m/s with 0 hits (the null colliders); instrument controls: a bare
  tree (resolving collider) took 27 hits and slowed to 3.0 m/s, and the slab edge at x 30 stopped the player
  (stick 3 s, 286 hits on the road slab).
- 2.5 `route/before/` (`Tools/Events/Trace Actor Route - 2.2 before`): 3 full pooled cycles + a partial fourth,
  one instance, on the NavMesh throughout, peak 1.48 s at x 26.1 (`analysis.txt`).
- 2.6 `photo-shoot-town-before/`: Town Placement Study on today's world; the photo-shoot folders as found are
  copied to `aside/`.
- 2.4 `walls/before/` (`Tools/Town/Wall Press - before`): drive control and pitch control (+100 mouse units →
  −15.000°) pass. Rig fixes on the way: the marker is sized to the wall's measured extent (it showed round a
  narrow trunk), a pose counts only with contact and control > marker, and the marker is pure green (the pink
  supermarket's highlights passed a magenta threshold).
  **Result (today's camera):** marker test — house east face 6 valid poses, log cabin 21, farm shed 70: **0 show
  the marker**, each wall's control shows it (132k / 103k / 518k px). The supermarket's east "wall" is a tinted
  GLASS façade (its "pink panels" turned green with a green marker: they were the marker seen through the glass),
  so it is not a wall and is excluded. **Geometric check (near plane past a surface):** fires on today's code at the
  villa's white-rock wall (26 poses), a deck chair by the house (12), the hillside under rock.024 (5), the log cabin
  (4), the supermarket frame (3) and the solo pine (3). **1f classification: the failures are geometric** (near
  plane past the surface; no single-sided face showed the marker), so the camera-side fix applies. Rig-invalid
  wall picks (bus-shelter pole, bare tree, hillside south: no contact) are rerouted for the after run.
- **Task 2 done.** Baselines are on disk before anything in the world changed.

### Task 3 — the map check, the re-export path, camera import off (2026-11-11)

- 3.1 `Tools/Town/Check Map Against .blend` (`MapCheck.cs` + `tools/map/map_check.py`, headless Blender 5.0.1,
  ~40–60 s): **MATCH** on the real pair — .blend (evaluated) and FBX both 486 groups, 16,738 meshes, 5,677,935
  triangles, no camera, no light; Unity's column 486 / 16,738 / 5,677,793 (its importer drops 142 degenerate
  triangles in 4 groups; bounds agree after (bx, by, bz) = (−ux, −uz, uy)) → `map-check/real/report.md`.
  Instrument findings on the way: a transformed bound_box depends on the object's local frame (exact vertex bounds
  used instead); a background depsgraph does not reproduce particle size randomness (every instance origin
  identical to 0.000 m, scale 0.106 vs 0.106–0.110), so instances are compared by origin; two groups with an
  animated (Cube_M9L_0, 0.13 m) or Solidify-modified (Cylinder, 0.06 m) member are reported EXPLAINED, never a
  count/triangle difference. **Negative control:** a scratch copy with House Complete deleted and SuperMarket moved
  1 m → `DIFFERS (2 names: House Complete, SuperMarket_Colore_0)`, exactly those two (`map-check/mutated/`); the
  216 MB scratch .blend was deleted afterwards (logged in mutate-log.txt). No Blender and no .blend → one warning
  each, `CANNOT CHECK (...)`.
- 3.2 **Re-export proof** (`map-check/reexport-proof.md`): a scratch export outside Assets/ equals the current FBX
  on the check's numbers, all 16,738 node name paths and the GlobalSettings, with the exporter's defaults +
  MESH only + `FBX_SCALE_UNITS` + `bake_anim = False` (found in three exports). Written into
  `tools/map/map_export.py` and `Tools/Town/Re-export Map from .blend` (refuses unless the check says DIFFERS).
  **The real FBX was not overwritten** (MATCH).
- 3.3 `importCameras` and `importLights` off (`Tools/Town/Switch Map Camera and Light Import Off`):
  SaveAndReimport **53.2 s**; read back 16,738 renderers under the scene map, MeshColliders resolving 1,718 / null
  15,020 before AND after (nothing new broke), the orphaned override still targets nothing; the .meta diff is the two
  flags. EditMode guards added (`TownMapGuardTests`: camera import off, light import off, SampleScene and the NavMesh
  asset each < 50 MB): 195/195. The play-mode "only Main Camera" read-back is logged by every town rig run.

### Task 4 — colliders and spawn (2026-11-11)

- 4.1 `ColliderRule` (+ `Trunk` for pines) with `ColliderRuleTests` (pure); applied by `Tools/Town/Apply Collider Rule
  to the Map` — and by `Tools/Add MeshColliders to World`, the same pass, so a re-run never re-adds what was
  removed. On SampleScene (1.7 s): kept 1,045, **repaired 929** (909 rocks taller than a step + 20 stumps, their
  MeshCollider now holds the MeshFilter's mesh), **removed 8,765** (7,996 grass, 91 low rocks, the buried set
  (674 renderers incl. the dome and the −6.5 m plane under spawn), the sea, and two oil-rig legs whose first
  material is the water), **5,999 pine trunk capsules** (3 m, radius = 90th-percentile distance of the lowest
  metre's vertices, clamped 0.25–1.2 m). Read back: 1,974 MeshColliders, **0 without a mesh** (was 15,020).
  Scene saved in its own call; object counts HEAD → saved: MeshCollider 16,738 → 1,974, CapsuleCollider 0 → 5,999,
  stripped GameObject refs −8,765, every other object type unchanged (`colliders/report.txt`); the file went
  16.9 → 7.8 MB. New EditMode guard: every map MeshCollider reference in SampleScene resolves to a mesh the FBX
  holds (its logic finds 15,020 broken in the HEAD scene, 0 now). EditMode 217/217.
- 4.2 Spawn (1i): the player now stands at (0.45, 2.153, 3.6), yaw 58.8° (toward the drunk's peak), on the road
  slab's north edge in 2.1's measured gap — capsule clear (CheckCapsule), no 16 m drop, not on the barrier.
- 4.3 Road access (1j): decided after the whole-town bake (Task 8), from whether every part paths through the
  road's existing steppable runs (census: 7 per side). Recorded there.

### Completion Notes List

- 2026-11-11 (loop CREATE phase): story context created. Ultimate context engine analysis completed - comprehensive developer guide created.

### File List

## Change Log

- 2026-11-11: Story file created by the loop CREATE phase (Claude Opus 5.5). Status → ready-for-dev.
