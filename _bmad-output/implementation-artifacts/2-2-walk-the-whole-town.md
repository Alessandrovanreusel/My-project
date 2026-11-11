---
baseline_commit: d0506816dbf4f39afa79424e1b6efc6525bf3006
---

# Story 2.2: Walk the whole town

Status: review

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

- [x] **Task 1: Measure the town and settle the eleven things that gate this story. DO THESE FIRST.**

  Each item has a recommendation. Record the choice **and the reasoning** in the Dev Agent Record, including where
  you follow the recommendation: a later reader needs to know it was decided, not defaulted.
  - [x] **1a. Census the town in Unity, and name its parts.** Write `evidence/census/`: `census.json`,
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
  - [x] **1b. Which colliders go: "too small to be an obstacle".**
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
  - [x] **1c. Where the town ends.**
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
  - [x] **1d. The oil rig: reachable on foot, or deliberately out of bounds.**
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
  - [x] **1e. Below the world: the design.**
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
  - [x] **1f. See-through walls: measure the failure, then choose the fix.**
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
  - [x] **1g. The whole-town bake.**
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
  - [x] **1h. How input is driven.**
        - **Keys:** Story 2.1's Keyboard state events and drive control. Check
          `InputSystem.settings.editorInputBehaviorInPlayMode` and restore it after the run.
        - **Yaw:** set it on the player's transform each frame, labelled "the mouse's job" (2.1's crossing did
          this), or queue Mouse delta events.
        - **Pitch** has only one real path, `OnLook` → `_pitch`, which is private. Queue Mouse delta events for the
          wall rig, with the delta-to-degrees control from AC4.
  - [x] **1i. The player's spawn.**
        - **Recommended:** stand the player on open ground at today's spawn. 2.1 measured a clear gap at
          (0.45, ground, 3.6). Face them down the drunk's street at y = ground + 0.05: no 16 m drop, and not on the
          barrier.
        - Record the position. Rigs that used "the spawn landing" (`ScaleStudyRunner` `FromSpawn`) adapt; they are
          tools.
  - [x] **1j. Getting onto the road.**
        - From 1a's edge heights, plus eye photographs of the slab edge from the grass at 3 points, decide whether
          the slab reads as a **visible ledge** or as flat ground (an invisible wall).
        - **Recommended:** add **visible** access (ramps or steps with the road or ground material, simple meshes)
          under `TownAccess`, where the AC2 route and the parts need it. **Never edit the map FBX**, so a re-import
          cannot remove it, and Alexv can fold it into the `.blend` later (the same rule as Story 2.5's AC). If
          the NavMesh shows every part connected through the existing access near x 0, "no access added" is a
          valid result. Record it.
        - Either way, the route walk must never hit an edge that looks like flat ground.
  - [x] **1k. The frame-rate instrument.**
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

- [x] **Task 4: Scatter colliders, spawn and road access (AC2)**
  - [x] 4.1 Implement the 1b rule and its tests. Apply it through the tool, update `AddMeshColliders.cs`, save the
        scene in its **own** `execute_code` call (memory: an unsaved-changes dialog turns one menu call into six),
        and diff the YAML: only the removed components.
  - [x] 4.2 Move the player spawn (1i).
  - [x] 4.3 Add road access (1j) under `TownAccess`, or record why none is needed.

- [x] **Task 5: Edges, water and the oil rig (AC3)**
  - [x] 5.1 Add the `WorldBounds` layer, the `TownBounds` root and its barriers (1c). Remove the water's collider.
        Add any `NavMeshModifierVolume` needed under the water.
  - [x] 5.2 Decide the oil rig (1d) and record it in the result file's `decisions`. The documentation follows in
        Task 11.
  - [x] 5.3 **Edge walks:** at least 6 shore points and 8 ring bearings, walk and sprint, held 10 s, plus a jump.
        Output `edges/`, with the top-down trace and an eye photo at each contact.

- [x] **Task 6: Below the world: FallRecovery (AC3)**
  - [x] 6.1 Add `TownBoundsConfig` (script, asset, `.meta`), `FallRecovery` and `ThirdPersonController.Teleport`,
        with EditMode tests for the config (Safe*, finite checks, `TryGetConfigProblem`) and for any pure helper.
        Run `refresh_unity` and `read_console`, then **read the asset back through the editor**.
  - [x] 6.2 **Provoke the four cases, 5 cycles each, back to back** (AC3). Write `fall/fall.csv` (case, cycle,
        below-at, restored-at, seconds, restore position, grounded-after, on-NavMesh, capsule-clear) and a frame
        after each restore. Warn at the top of the rig's output that these warnings are provoked on purpose.
  - [x] 6.3 **Fail-soft:** no config, and an invalid config (NaN, ±Infinity, belowWorldY above the ground). Each
        must warn once and keep running.

- [x] **Task 7: Walls (AC4)**
  - [x] 7.1 Apply the 1f fix: `ThirdPersonCamera.cs`, plus `PlayerConfig` if a tunable is added.
  - [x] 7.2 **Wall press after:** every wall, yaw, pitch and mode from AC4, with positive controls. Write
        `walls/after/` and a contact sheet of the worst frames. **0 marker pixels.**
  - [x] 7.3 **The open-space pose is unchanged:** 1.650 up and 0.316 forward, read back in the open, at pitch 0
        and ±80.

- [x] **Task 8: The whole-town NavMesh, the paths from the pub, and the drunk (AC5, AC6)**
  - [x] 8.1 Write `Assets/Scripts/Editor/TownNavMeshBaker.cs` (`Tools/Town/Bake Town NavMesh`). Bake with the 1g
        settings and persist to the same asset. Record the time, size and tiles in `navmesh/bake.txt`. **Check the
        file size before committing.**
  - [x] 8.2 **Paths:** from the pub to every part, plus the 25 m grid, plus real-agent walks of the 3 longest
        paths. Output `navmesh/paths.png`, `paths.csv` and `grid.csv`. Explain or fix every failure.
  - [x] 8.3 **The `Warp` guard** (AC6) and its provoked case.
  - [x] 8.4 **The drunk:** a route trace of ≥ 3 pooled cycles on the new bake, `route/after/`, compared with 2.5 and
        with 2.1's numbers.

- [x] **Task 9: Walk the whole town (AC2)**
  - [x] 9.1 Build the route through every part from `parts.json`, planned on the NavMesh. Walk it once and sprint it
        once with the real drive. Write `walk/after/trace.csv`, the top-down trace, the stick and fall log, and an
        eye photo per stick.
  - [x] 9.2 **Scatter crossings after:** the same lines as 2.3, now unobstructed (±5% of the commanded speed).
  - [x] 9.3 **Suspect the rig before the code.** Treat these as a broken rig until proven otherwise (lessons):
        zero sticks over a long route on a first run, every leg identical, a speed that is exactly round, or a
        blank trace.

- [x] **Task 10: The console and the frame rate (AC5)**
  - [x] 10.1 **Frame-time tour × 3 after:** `fps/after/`. Diff it against `fps/before/` per viewpoint, against the
        spread.
  - [x] 10.2 A normal SampleScene play session with nothing scripted: the console holds only the baseline.

- [x] **Task 11: The docs (AC3, AC6)**
  - [x] 11.1 `game-architecture.md`:
        - a new subsection **"World bounds and traversal (Story 2.2)"** under §Architectural Decisions: the
          playable area, the barriers and their layer, the water, the oil-rig decision, FallRecovery, the
          collider rule, the bake (volume, voxel, size, split if any) and the map check and re-export tools;
        - a Decision Summary row **11 — World bounds**;
        - `Scripts/World/` and `Data/World/` added to §Directory Structure and §System Location Mapping;
        - the frontmatter `updated:` field.
  - [x] 11.2 `decision-log.md` (`_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/`): a dated entry
        for the oil rig and the world bounds.
  - [x] 11.3 `epics.md` §"Known seeds" → the Epic 3 bullet: **one** dated note giving the oil-rig decision and its
        consequence for the smoke-break event. **No other `epics.md` edit.**
  - [x] 11.4 `deferred-work.md`: add a dated "Resolved by Story 2.2" note to each entry this story closes (:12,
        :315, :316 and :317), and note what stays open.
  - [x] 11.5 `loop/lessons.md:61`: correct it in place if 1a disproved it, and return the new facts as lessons.

- [x] **Task 12: Regressions and restore (AC6)**
  - [x] 12.1 `Tools/Scale/Movement After (Play)`, the Photo Shoot (diffed unmasked against 2.1's
        `photo-shoot-after/`), the Town Placement Study (labelled world-dependent), and the Gallery and HUD rigs
        once each (they run, 0 exceptions).
  - [x] 12.2 `run_tests` (EditMode), and report the count. Run `read_console`, `git status` and
        `git diff --stat`: only intended files.
  - [x] 12.3 Read back every restored value (AC6). SampleScene must not be dirty and must hold no rig objects. Clear
        the SessionState flags.

- [x] **Task 13: Handover. Queue what needs Alexv, and never self-certify it**
  - [x] 13.1 Put the `human_checks` from Dev Notes §"Human checks to queue" in the result file, with exact paths
        and your own read.
  - [x] 13.2 Put the `decisions` there too: 1b (the rule, and scene-level or postprocessor), 1c, 1d, 1e, 1f, 1g, 1i
        and 1j; `importLights`; and the re-export if one was made.
  - [x] 13.3 Story status → `review`. Update `sprint-status.yaml`: the entry, and the **real** `last_updated:` key
        at ~line 40 (lesson 66). No `: ` and no trailing `:` (lesson 13). Check it with `yaml.safe_load`.

### Review Findings

Code review on 2026-11-11, loop REVIEW phase, of `5df4a9a..ee2bc07`: the story's own commits. The merge from master in
`ee2bc07` touches only `loop/`. It was done by a fresh session that did not write the code.
- **Three layers, run in this session:** Blind Hunter (diff only), Edge Case Hunter (diff + project) and Acceptance
  Auditor (diff + story).
- **What was reviewed:** the shipped code, which is:
  - runtime: `FallRecovery`, `TownBoundsConfig`, `ColliderRule`, the camera's wall cast, `ThirdPersonController.Teleport`,
    the `EventActor` Warp guard and `PlayerConfig`;
  - editor tools: `AddMeshColliders`, `TownBoundsBuilder`, `TownNavMeshBaker` and `MapCheck`, plus `tools/map/*.py`;
  - the scene, asset and `.meta` changes, and the tests.

  The rigs (`TownRig*`, `TownCensus`, `TownPhotos`) were run and extended as tools. They were not reviewed (standing
  rule).
- **Result:** about 20 raw findings, triaged into **0 new decisions, 2 patches (both applied and re-verified by
  running), 3 deferred and 10 dismissed.**

What was **run**, not read. Each scenario was played in SampleScene through the real `ThirdPersonController`,
`FallRecovery` and keyboard path, with configs staged as clones, never the asset:
- a valid playable area that does not contain the spawn;
- `minRestoreAge` 5 s, `safeSampleInterval` 0.01 s and the shipped values, each walked into the real hole 3 cycles
  (before and after the fix);
- `maxAirborneSeconds` 0.5 against ordinary jumps;
- the collider tool's **second cycle** on the processed map;
- the real re-export, to measure its stderr;
- the full `Fall Recovery` rig as a regression (4 cases × 5 cycles);
- the EditMode suite.

New rig modes: `Tools/Town/Review - FallRecovery spawn outside the area (Play)` and `… ring and backstop (Play)`, in
`TownRigRunner.Review.cs`. Evidence: `_bmad-output/verification/2-2-walk-the-whole-town/review/` (`outside-before-fix/`
vs `outside/`, `ring-before-fix/` vs `ring/`, `colliders/`, `mapcheck/`, `fall-regression/`).

**Patches (applied, re-verified by running):**
- [x] [Review][Patch] **A playable area that does not contain the spawn froze the player and flooded the console:
      one put-back and one warning per frame.** [Assets/Scripts/World/FallRecovery.cs `Update`] (edge)
      - **Reproduced** (`review/outside-before-fix/log.txt`). The config was a valid polygon, the shipped one shifted
        2 km east. The spawn sits outside it, so FallRecovery put the player back on the spawn (outside) every frame:
        - idle: **241 recoveries and 241 `[World]` warnings in 240 frames**, never grounded;
        - W held 3 s: **283 in 283 frames, moved 0 m**;
        - **629 warnings in ~7 s.** Even the hole could not be reached.
      - NFR8 asks for one warning and a game that keeps running. A polygon that is valid but wrong is the authoring
        mistake most likely when Epic 3 edits the area.
      - **Fix:** "left the playable area" fires only when there is somewhere inside to put the player back: a
        remembered safe spot, or a spawn that is inside. Otherwise one warning ("said once") names the cause. Falling
        below the world is still caught everywhere.
      - **Re-verified** (`review/outside/log.txt`): idle, **0 recoveries and 1 warning**, grounded; W held, **7.6 m
        walked, 0 recoveries**; the hole still put the player back; 2 `[World]` warnings in all.
- [x] [Review][Patch] **Two valid timing values together sent every fall back to the spawn, ~450 m from the hole.**
      [Assets/Scripts/World/FallRecovery.cs ring buffer] (edge)
      - **The cause:** the ring holds 16 safe spots, so it reaches back 16 × `safeSampleInterval` (4 s as shipped).
        When that is shorter than `minRestoreAge`, no spot is ever old enough, and `Recover` falls back to the spawn.
      - **Reproduced** in the real hole, 3 cycles per case (`review/ring-before-fix/ring.csv`):
        - `minRestoreAge` 5 s, after the player had stood 7 s on safe ground: **3 of 3 to the spawn (453 m)**;
        - `safeSampleInterval` 0.01 s: **1 of 3** (it depends on how long the fall lasted);
        - shipped values: 0 of 6.
      - **Fix:** `FallRecovery.RingSampleInterval` = max(interval, 2 × `minRestoreAge` / 16), so the ring always
        reaches back at least twice the restore age. One warning when the typed interval is lengthened. The shipped
        0.25 s / 0.5 s is unchanged. 5 EditMode cases (`TownBoundsConfigTests.RingSampleInterval_*`).
      - **Re-verified** (`review/ring/ring.csv`): **12 of 12 falls put back by the hole**:
        - restore age 5 s → the spot stood on 7 s earlier, 14 m from the drop;
        - interval 0.01 s → the lip, 11.6–11.9 m from the walk start;
        - the shipped values → 11.0–11.9 m, against 11.0–11.6 before.

**Deferred** (in `deferred-work.md` under "code review of story-2-2"):
- [x] [Review][Defer] **`maxAirborneSeconds` accepts a value shorter than a jump.** Reproduced: at 0.5 s, 3 of 3
      ordinary jumps (airborne 0.69–0.78 s) are put back; the shipped 3 s gives 0 of 6. The right floor is the
      longest legitimate time in the air, a level-design number. [TownBoundsConfig.cs `SafeMaxAirborneSeconds`]
- [x] [Review][Defer] **`MapCheck.ReexportMenu` never reads Blender's stderr, writes the FBX in place, and ignores
      the exit code.** Not reproduced: the real export wrote **0 bytes** to stderr (5 MB to stdout, which is read) in
      25 s (`review/mapcheck/stderr_volume.txt`). The menu only runs after a DIFFERS, and today it says MATCH.
      Harden it the next time the map changes. [MapCheck.cs:62-72]
- [x] [Review][Defer] **The camera's wall cast assumes the near plane's reach stays inside the 0.30 m body.** A
      start-overlapping hit is skipped. Computed with `NearPlaneReach` + margin: 0.177 m as shipped (FOV 60, 16:9),
      0.254 m on 32:9, but **0.394 m at `wideFov` 90 on 32:9**. Hypothesis, not reproduced. Revisit if `wideFov` is
      ever raised above ~80. [ThirdPersonCamera.cs `ClearNudge`]

**Dismissed (10), with the reason:**
- **"Re-running the collider tool would strip the repaired rocks"** (its ground probe could hit the rock's own, now
  real, collider): **disproven by running the second cycle.** `AddMeshColliders.Apply()` on the processed map left
  every collider bit-identical: 1,974 MeshColliders (0 null), 5,999 capsules with the same radius, height and centre,
  and kept 1,974 / added 0 / repaired 0 / removed 0 (`review/colliders/`). `TownCensus.Column` skips scatter
  colliders and capsules. The scene was reopened from disk afterwards, and the dev's `colliders/report.txt` put back.
- **"The re-export deadlocks on stderr":** see the deferral: 0 bytes measured on the real path.
- **"The bake volume ignores the map's rotation"** (`TownNavMeshBaker` subtracts only the position): the map root is
  at the origin with identity rotation and scale 1 (read back), so the volume is exact today.
- **"`TownBoundsBuilder` duplicates an inactive `TownBounds`"** (`GameObject.Find` sees active objects only): nothing
  deactivates it, and it is an editor tool run by hand.
- **"The camera's 8-hit, unsorted sphere-cast buffer can drop the nearest wall":** that needs more than 8 colliders
  inside a 0.18 m sphere swept 0.316 m. The wall rig found 0 geometric hits on 14 walls.
- **"`NothingBeneath` ignores only the CharacterController, so another collider on the player would mask a fall":**
  the player carries only the CharacterController (read back).
- **"A legitimate long fall trips the 3 s airborne backstop":** at gravity −15 that needs a 67 m drop. The highest
  ground the player reached is ~30 m (edge walks).
- **"The fall rig's 'restore age −1' rows hide a fallback to the spawn":** they are rig bookkeeping. The restore point
  is the previous cycle's kept spot (−379, −233.18), by the hole.
- **"The Warp guard leaves the agent disabled for good":** it is re-enabled at the next spawn that is on the mesh
  (read). The dev's provoked run covers it: 3 lifecycles plus a 4th spawn, one warning, 0 errors.
- **Acceptance Auditor, AC5 "every path completes":** this is met only under the chain-of-legs reading (one query
  reaches 7/18, and a single-destination agent never reached corner_nw). It is already a recorded decision in the
  batch queued for Alexv, not a code defect, so no new decision. Every other AC clause has its evidence on disk or a
  queued human check.

**Environment note (not a code finding).** After the orchestrator merged master into this branch (07:59), the
SampleScene file on disk had changed under the open editor. The first `refresh_unity` raised a modal dialog, "The open
scene(s) have been modified externally". It blocked the main thread silently: MCP timeouts, 0 % CPU. It was found
with `EnumWindows`. Its content equalled HEAD, so "Reload" was posted to it.

**Regression after the fixes** (`review/fall-regression/`, the dev's `fall/` kept as found):
- `Tools/Town/Fall Recovery (Play)`, 4 cases × 5 cycles (`review/fall-regression/summary.txt`, `log.txt`):
  - the worst time from going below or out to the restore is **0.162 s** (the dev's run: 0.160);
  - grounded within 0.159 s; on the NavMesh, capsule clear and inside on 20/20; no new UI;
  - console: 20 provoked `[World]` warnings, 0 other warnings, 0 errors.
  - The rig flags 1/20, and it is the rig's own measure. Hole cycle 0 was put back 0.136 s after leaving the
    ground, before its feet were 0.3 m below the lip. So the rig's "below → restore" is NaN, and `NaN <= 2` reads as
    a fail. Every other check of that cycle passed. The restore was faster, not slower.
- `Tools/Town/Fall Recovery - fail-soft invalid config (Play)` (`review/failsoft-invalid-config/`): **one** config
  warning at boot, 0 recoveries in a 6 s walk on open ground, the hole still recovered, 0 other warnings, 0 errors.
- EditMode **242/242** (237 + the 5 new ring-interval cases). The console holds only the provoked warnings named above.
- Restored afterwards: SampleScene is open and not dirty, with no rig object. The dev's `fall/` and
  `colliders/report.txt` evidence is put back exactly as found (`diff -r` clean against the copy taken before).

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

### Task 5 — edges, water and the oil rig (2026-11-11)

- 5.1 `Tools/Town/Build Town Bounds` (`TownBoundsBuilder.cs`), reading the ONE polygon in `TownBoundsConfig.asset`:
  layer `WorldBounds` added through the TagManager's SerializedObject and read back (= 9); scene root `TownBounds`
  with 8 BoxColliders on the playable area's edges (shore z 269.5; ring x −680 / 260, z −400, and the cut NE/SE
  corners) and 12 round the pond, no renderer, each from 2 m under the lowest ground on its edge to 6 m over the
  highest (`edges/bounds-build.txt`); a NavMeshModifierVolume (Not Walkable) over the pond. The sea's collider was
  removed by the collider rule (Task 4). No volume under the sea is needed: the bake volume is the polygon's bounding
  box, which ends at the shore (z 269.5). FallRecovery's ground casts ignore the WorldBounds layer. Scene saved in
  its own call.
- 5.2 Oil rig: **(B) out of bounds** (1d) — the shoreline barrier; recorded in the result file; the docs follow in
  Task 11.
- 5.3 `Tools/Town/Edge Walks (Play)` → `edges/` (edges.csv, trace.csv, trace.png, photos/, sheet.jpg): 6 shore points
  + 8 ring bearings (3 of which reach the shore), walk and sprint, W held 10 s after contact, then a jump at the
  barrier: **28/28 inside the playable area on every frame (0 m beyond), the shore walks end at z 268.7 standing on
  ground (never over the water), every jump lands inside.** The east and south-east hills stop the player by slope
  before the barrier (y 27–30). In play mode the only enabled camera is Main Camera (AR8 read-back).

### Task 6.1 and Task 7 — FallRecovery in the scene; the camera fix (2026-11-11)

- 6.1 `FallRecovery` (+ `TownBoundsConfig.asset`, read back through the editor) is on the player; config tests
  (`TownBoundsConfigTests`: Safe*, finite checks, problem report, polygon, the shipped asset) — 231/231. **6.2/6.3
  run after the whole-town bake** (Task 8): a safe spot must be on the NavMesh, which today covers the slice only.
- 7.1 (1f, camera-side, because the "before" failures were geometric): `ThirdPersonCamera.LateUpdate` nudges along
  the body's YAW only (deferred :317) and cuts the nudge short where a sphere cast from the body axis — radius
  `PlayerConfig.NearPlaneReach(live FOV, aspect, near)` + `PlayerConfig.cameraWallMargin` (new, 0.03 m, Safe* +
  problem + tests) — meets a surface (WorldBounds and Ignore Raycast layers excluded; no allocation per frame).
  Reach = 0.147 m at FOV 60, inside the 0.30 m body. EditMode 233/233.
- 7.2 `walls/after/` (14 walls × 5 angles × 5 pitches × 3 modes, controls per pose): **geometric check 0 on every
  wall** (before: villa white rocks 26, deck chair 12, hillside 5, log cabin 4, supermarket frame 3, pine 3). The
  marker test: 0 see-through on the house, log cabin, farm shed, street building west, police car (each control
  shows the marker). Remaining marker poses, each looked at (`contact_sheet_worst.jpg`): the supermarket's east
  face is tinted GLASS (correctly see-through); the scatter pine and the scatter rock are rig artifacts (the marker,
  sized to the collider face, shows round a visual trunk slimmer than its capsule and over a rock lower than the
  eye); **one art seam**: a thin line through a gap between two pieces of a low planter wall in the street block
  (x −125, z −89), visible from any distance — the .blend's, not the camera's; queued for Alexv.
- 7.3 Open-space pose read back on the slab: **1.6500 m up, 0.3160 m forward, 0 sideways at pitch −80, 0 and +80**,
  near clip 0.095 (before the fix the eye rose to ~1.96 m when looking up).

### Task 8 — the whole-town NavMesh (2026-11-11)

- 8.1 DONE `Tools/Town/Bake Town NavMesh` (`TownNavMeshBaker.cs`): Volume = the playable polygon's bounding box
  (x −680…260, z −400…269.5), y −8…110 (bakeYMin/Max in the config), layers Default + WorldBounds, physics
  colliders, voxel 0.1 set explicitly (cellSize 0.1 read back), the player root excluded (NavMeshModifier
  ignoreFromBuild), the pond Not Walkable. **27.2 s, 1,026 tiles, 15.53 MB** (was 0.22 MB), persisted INTO the
  existing asset (EditorUtility.CopySerialized) — **GUID unchanged** (255eb66f…); agent type 0 read 0.30 / 1.77 /
  0.35 / 45 before and after (`navmesh/bake.txt`). Spot checks: the pub, the street block, the east hill and the
  new spawn are on the mesh; the pond and the hole are not.
- 8.2 FIRST RUN (`Tools/Town/NavMesh Paths (Play)` → `navmesh/paths.csv`, `grid.csv`, `agents.csv`, log.txt):
  **7/18 parts and 39.7 % of the 25 m grid path from the pub in ONE query**; the three real agents (3.5 m/s) on the
  three longest complete paths (302 / 275 / 262 m) all ARRIVED by the agent's own test, on the mesh every frame.
  **Diagnosis (measured):** the failures are PathPartial that stop ~270–430 m from the pub whatever the target —
  Unity's single-query A* search budget on a 132k-triangle mesh, not islands: the same targets complete from a
  nearer start ((−400, −150) → corner SW 262 m; (−200, −20) → the farm 283 m; the pub → (−250, −150) 352 m). The
  density is the ~6,000 pine-trunk holes; a 1024-voxel tile changes nothing (127,635 triangles, 0 of 11 recovered;
  tested in memory, scene reopened from disk). One target is genuinely cut off: the boulder ledge
  `hill_south_boulders` fails even from 85 m away (5.3 m short).
  **Decision (8.2, attempt 2): "every path completes" = a chain of complete legs**, measured against the alternative:
  - **Pines out of the bake** (every trunk capsule `ignoreFromBuild`, rebuilt in memory and never saved): 57k
    triangles, and one query from the pub reaches 11/18 (500–750 m). That is not enough, and agents would walk through
    trunks. Rejected.
  - **A chain of single queries** (`TownRigRunner.Chain`): query toward the target, continue from where a partial
    answer stopped, then re-query EVERY leg on its own between consecutive waypoints. The chain passes only if each
    leg is `PathComplete` with its end within 0.5 m. Parts the pub cannot chain to are tried from the nearest part
    already reached. That is how events move (ADR-3: a route is waypoints). **Chosen.**
  - Result (`navmesh/log.txt`, `paths.csv`, `paths.png`): **one query 7/18; a chain from the pub 18/18.**
    `hill_south_boulders` was moved to the FOOT of the boulder pile (70.7, 5.7, −334.5). Its old point is the pile's
    top, a NavMesh island 5 m up that no path reaches (`census/parts_before_8.2.json` keeps it).
  - **Real agents.** (1) One per part from the pub, given ONLY the part as destination: 17/18 reached it.
    hill_northwest stood on its part from 224 s, but the rig's first test also required `hasPath`; the test is fixed.
    The **corner_nw agent never arrived**: its partial first path led into a dead end beside the grey strip at
    x ≈ −546 by the shore. (2) The three longest chains walked waypoint by waypoint (`navmesh/chain-walk/`):
    corner_sw (4 waypoints, 1,332 m), corner_nw (3, 904 m) and hill_northwest (2, 755 m) **all arrived**, each leg by
    the agent's own test, on the mesh every frame. So **Epic 3 keeps consecutive waypoints within ~250 m** and never
    trusts one long destination (architecture §World bounds).
  - **Grid 25 m:** 941 points on the mesh. 399 complete in one query, 488 through a chain, **54 unreached, each
    classified** (`navmesh/islands.csv`, `README.txt`):
    - 30 sit on top of an object (a roof, a white tree's crown, the tyre line, a rock), and the ground at its foot is
      reached;
    - 8 are edge patches with reached ground within 8 m;
    - 12 are the south-west ring above ~20 m, behind slopes over 45°, which the player cannot climb either
      (explained by slope, not walked);
    - 4 are inside the villa's walled enclosure, which has no opening a 0.6 m agent fits through
      (`navmesh/crop_enclosure.png`; noted for Alexv).
- 8.3 DONE (`navmesh/warp-guard/`): spawn staged over the hole, PROVOKED ON PURPOSE. 80 s: 3 full lifecycles plus a
  4th spawn on ONE pooled instance, every one in place at (−380, −229) with the agent off, **exactly one warning**
  ("… off the NavMesh … (said once)"), 0 errors, and no "Failed to create agent".
- 8.4 DONE (`route/after/analysis.txt`, `arrival.txt`; `tools/arrival.py`, EventActor's own 0.2571 m test): 3 full
  pooled cycles plus a partial 4th, one instance, on the mesh every frame. He arrives **0.72–0.76 s before despawn**
  (before, on the slice bake: 0.49–0.58; 2.1: 0.52–0.57). The peak lasts **1.45–1.50 s at x 26.30–26.32** (before:
  1.45–1.49 at x 26.09–26.12). The new mesh's corners move his line ~0.2 m, and he walks 11.6 m instead of 11.3 m.

### Task 6.2 / 6.3 — FallRecovery provoked (2026-11-11)

- 6.2 `Tools/Town/Fall Recovery (Play)` → `fall/` (fall.csv, summary.txt, frames/, frames_sheet.jpg,
  hole_fall_and_restore.mp4). PROVOKED ON PURPOSE: 4 cases × 5 cycles back to back. **Worst time from going below or
  out to the restore is 0.160 s** (under the plain 0.152–0.160; the hole 0.047–0.077 below the lip, 0.15–0.18 after
  leaving the ground; the buried block 0.016–0.031; the edge with Edge 07 disabled 0.000, the same frame). In every
  cycle the player is grounded within 0.11 s, on the NavMesh, with the capsule clear and no falling speed carried
  over (−1 m/s after the restore against −4 before). New UI canvases: 0, and the after-frames are ordinary views.
  Console: exactly 20 provoked `[World]` warnings, 0 other, 0 errors. The first run could not see the edge case,
  because FallRecovery restores in the very Update that finds the body outside. The rig now reads that instant from
  FallRecovery's record (run 1 kept in `fall-run1-edge-unobserved/`).
- 6.3 fail-soft, three menu items (`fall/failsoft-*`): no config, an invalid config (NaN / ±∞ / not a polygon), and
  **a floor of the world typed above the ground (50 m)**. Before this, a finite floor was accepted as typed, and it
  would have put a standing player back every frame. Now `SafeBelowWorldY` requires it below `bakeYMin` (+3 tests). Each
  case gives **one** config warning at boot, a 6 s walk on open ground with 0 recoveries, and the hole still recovered
  (0.16–0.18 s), with 0 other warnings and 0 errors.

### Task 4.3 / 1j — road access (2026-11-11, decision)

- **No access added.** Every part paths from the pub through the road's existing steppable runs (Task 8.2: 18/18
  chains). The real controller walked and sprinted every leg across the road (Task 9) and never stuck at the slab's
  edge. From the grass the slab reads as a visible raised ledge with a dark side face (census photos j01–j03), not as
  flat ground. So no `TownAccess` root exists, and human check 4 (ramps) does not apply. Reversal: add ramps under a
  `TownAccess` root where a future route needs them, then re-bake.

### Task 9 — walk the whole town (2026-11-11)

- `Tools/Town/Walk the Whole Town (Play)` / `- sprint (Play)` → `walk/after/walk|sprint/` (trace.csv, legs.csv,
  sticks.csv, photos/, log.txt), drawn by `tools/tour_plot.py` → `walk/after/trace.png`. The real controller is
  driven by Keyboard state events (W, or W + Left Shift). The drive control passes (0 / 6.0 / 0 m). It steers along
  each leg's NavMesh corners by setting the yaw (the mouse's job), visiting the 18 parts nearest-first from the spawn.
  Each leg is a chain of complete queries.
- **Sprint:** 18/18 legs walked to the part, 4,582 m in 657 s at a mean **6.98 m/s** (commanded 7). 0 fall-through
  frames. **FallRecovery fired 0 times.** One stick, sprinting toward corner_ne at (210.4, 130.6), lasted 1.5 s
  against a white rock (Icosphere scatter, top 1.96 m over the feet), **0.015 m from the NavMesh edge**. That is the
  path hugging the rock at the agent radius: a path issue, not open ground. The leg was re-planned and completed.
  The photo (`photos/stick_sprint_corner_ne_0.jpg`) looks along the route and does not show the rock beside the
  player.
- **Walk:** 18/18 legs walked to the part, **4,582 m in 1,146 s at a mean 4.00 m/s** (commanded 4; the controller keeps 4 m/s horizontally on slopes too, and the same detector caught the sprint's stick and walk/before's slab-edge stick, so the zero is not a blind instrument), **0 sticks, 0 fall-through frames, FallRecovery fired 0 times**. corner_nw and hill_northwest had no chain from shore_west (the strip's dead end), so both were postponed and walked later from hill_west's side.
- **Rig defects found and fixed on the way (9.3, suspect the rig).** (1) Walk run 1 (`walk/after-run1/`) flagged 27
  "fall-through" frames. Each was the probe ray, which started 2.5 m up, hitting an overhang 2.3–2.5 m above
  grounded feet (the boulders rock.006/.007/.023 and the stacked tyre line; read back in edit mode). The probe now
  starts just above the head, as the AC says. (2) Run 1 visited 16/18 parts, because corner_nw and hill_northwest
  have no chain from shore_west (the grey strip's dead end). A part with no chain from where the player stands is
  now postponed and retried later, or routed through a visited part.
- 9.2 scatter lines after: `walk/after/lines/` (drive control 0 / 6.0 / 0 m). **Grass: 4.00 / 7.00 m/s, ±0.03 %, 0 hits** (the AC's ±5 %, and no hit on a removed class). The pine trunk now STOPS the player (stick 3 s, hits on Circle). Before, they walked straight through it on a null collider. The rule keeps pines as obstacles you walk around. The bare-tree control and the slab edge still stop the player, and a tall white rock beside the tree now has its collider back. No line was walked through the 91 low rocks: their colliders are removed and the route never met one.

### Task 10 — the console and the frame rate (2026-11-11)

- 10.1 `fps/after/` against `fps/before/` (`fps/after/compare.txt`): the same 7 viewpoints, 3 rotated rounds, quality
  PC, Game view 953×476. The median of rounds is **not higher beyond the round-to-round spread at any viewpoint**
  (diffs −3.7…+6.3 ms against spreads of 5–47 ms). In the first, coolest round the diffs are −1.4…+3.3 ms. The spread
  is wide because this laptop heats from round to round (lesson 28), so the criterion is weak. That is reported as a
  difference, and NFR1's absolute 60 FPS verdict is Story 2.3's.
- 10.2 A plain SampleScene play session (~35 s, nothing scripted, drunk cycling). The console held **only the
  baseline** "POV Camera: Head bone not found!". The player stood grounded on the new spawn (0.45, 2.13, 3.60) with the
  eye 1.650 m above the feet. FallRecovery recoveries 0. Main Camera is the only camera.

### Task 12 — regressions and restore (2026-11-11)

Every rig output folder was copied aside before its run (`regression/as-found/`), harvested into `regression/`, and
put back afterwards (`tools/aside.py`). Story 2.1's `movement/after/` and the shared `photo-shoot*`, `gallery` and
`hud` folders are as found.
- `Tools/Scale/Movement After` (`regression/movement/`): walk **4.00**, sprint **7.00** m/s, jump apex **1.261 m**,
  the 0.126 m kerb at x 0.5 **climbed**, worst raise 0.158 s and lower 0.170 s. The slab edge at x 30 "did not rise",
  the same as in 2.1's own run. The run starts from the new spawn (0.45, 2.153, 3.6).
- Photo Shoot (`regression/photo-shoot-diff-vs-2.1-after.txt`, unmasked): **every shot keeps its stars and verdict**.
  w_time_early_05s moved 76→75 % and z4_money_late_12s 55→52 %, both within their star, from 0.01–0.03 s of
  peak-offset drift (lesson 42).
- Town Placement Study (`regression/photo-shoot-town-diff-vs-before.txt`, `placement_study_before_after.jpg`).
  **World-dependent, labelled, and the grader is untouched.** dir210 and dir240 now grade *Occluded*, where before
  they were 1★. The pictures show the drunk entirely behind a pine (240) and almost entirely behind one (210): the
  pines now have working trunk colliders, so the grader's rays see them (lesson 67's cause). dir270 is still counted
  with the drunk hidden behind trunk and grass. 20 of the 24 shots are unchanged.
- Gallery Shoot and Grade HUD Shoot: both **ran to the end**, with 0 exceptions. The HUD rig's two `[GradeHud]`
  warnings come from its own Phase D throwaway canvases, which `hud.txt` declares as provoked.
- 12.2 EditMode **237/237** (233 + the 4 floor-of-the-world tests). Console clean apart from the baseline head-bone
  warning. `git status`: only the untracked `.codex/` and `AGENTS.md`, which are not mine.
- 12.3 Read back in the editor: SampleScene is open, **not dirty**, and holds no `(rig)` object (12 roots, including
  `TownBounds`). No TownRig SessionState key is left. timeScale is 1. runInBackground is True, the project's own
  `runInBackground: 1`. Quality is PC. ProjectSettings are unchanged in git. `importCameras` and `importLights` are
  off. Agent 0 reads 0.30 / 1.77 / 0.35 / 45. The surface still points at `NavMesh-game map.asset`.

### Human checks and decisions (Task 13)

Queued in the result file, none self-certified:
- **AC2/AC3, the edges and the open ground** (`film/shore.mp4`, `film/hill.mp4`, their sheets, `walk/after/trace.png`,
  `edges/sheet.jpg`). My read: the shore stops the player ~3 m short of the water, with the water's edge in view. The
  **east hill stops the player at x 258 on an open slope that keeps rising**, which will likely read as an invisible
  wall. The barrier could move up to the crest, or the polygon could follow the slope.
- **AC3, the put-back** (`fall/hole_fall_and_restore.mp4`, `fall/frames_sheet.jpg`, `fall/fall.csv`): an instant
  teleport with no fade.
- **AC4, walls up close** (`walls/after/contact_sheet_worst.jpg`), and the planter-wall art seam at (−125, −89).
- Level design noted on the way: the villa's walled enclosure has no opening (`navmesh/crop_enclosure.png`).
- Decision batch: 1b, 1c, 1d, 1e, 1f, 1g, 1i, 1j, importLights, AC5's chain reading, and the floor-of-the-world rule.

### Completion Notes List

- 2026-11-11 (loop CREATE phase): story context created. Ultimate context engine analysis completed - comprehensive developer guide created.
- 2026-11-11 (loop DEV phase, attempts 1–2): all 13 tasks done. AC1: map check MATCH with a proven negative control, re-export path proven, camera and light import off. AC2: the real controller walked (4,582 m at 4.00 m/s) and sprinted (6.98 m/s) through all 18 parts with 0 falls and FallRecovery 0; the one stick was a path hugging a rock; grass no longer blocks. AC3: 28/28 edge walks inside, 20/20 provoked falls restored in ≤ 0.160 s, the oil rig out of bounds. AC4: 0 geometric see-through on 14 walls, open pose unchanged. AC5: whole-town bake 15.5 MB; 18/18 parts by a chain of complete legs (one query 7/18, measured as the search budget), real agents arrive, every grid failure classified; frame time not higher beyond the spread. AC6: Warp guard provoked, drunk on the new bake, regressions run, EditMode 237/237, editor restored. Perceptual checks queued for Alexv (edges, the put-back, walls).

### File List

- Modified: `Assets/Data/Player/PlayerConfig.asset`
- New: `Assets/Data/World/TownBoundsConfig.asset`
- Modified: `Assets/Models/game map .fbx.meta`
- Modified: `Assets/Scenes/SampleScene.unity`
- Modified: `Assets/Scenes/SampleScene/NavMesh-game map.asset`
- Modified: `Assets/Scripts/Editor/AddMeshColliders.cs`
- New: `Assets/Scripts/Editor/MapCheck.cs`
- Modified: `Assets/Scripts/Editor/RouteTraceRig.cs`
- New: `Assets/Scripts/Editor/TownBoundsBuilder.cs`
- New: `Assets/Scripts/Editor/TownCensus.cs`
- New: `Assets/Scripts/Editor/TownNavMeshBaker.cs`
- New: `Assets/Scripts/Editor/TownPhotos.cs`
- New: `Assets/Scripts/Editor/TownRig.cs`
- Modified: `Assets/Scripts/Events/EventActor.cs`
- Modified: `Assets/Scripts/Player/PlayerConfig.cs`
- Modified: `Assets/Scripts/ThirdPersonCamera.cs`
- Modified: `Assets/Scripts/ThirdPersonController.cs`
- New: `Assets/Scripts/World/ColliderRule.cs`
- New: `Assets/Scripts/World/FallRecovery.cs`
- New: `Assets/Scripts/World/TownBoundsConfig.cs`
- New: `Assets/Scripts/World/TownRigRunner.Edges.cs`
- New: `Assets/Scripts/World/TownRigRunner.Fall.cs`
- New: `Assets/Scripts/World/TownRigRunner.Paths.cs`
- New: `Assets/Scripts/World/TownRigRunner.Tour.cs`
- New: `Assets/Scripts/World/TownRigRunner.Walls.cs`
- New: `Assets/Scripts/World/TownRigRunner.cs`
- New (code review): `Assets/Scripts/World/TownRigRunner.Review.cs` (rig modes for the review's FallRecovery scenarios)
- New: `Assets/Tests/EditMode/ColliderRuleTests.cs`
- Modified: `Assets/Tests/EditMode/PlayerConfigTests.cs`
- New: `Assets/Tests/EditMode/TownBoundsConfigTests.cs`
- New: `Assets/Tests/EditMode/TownMapGuardTests.cs`
- Modified: `ProjectSettings/TagManager.asset`
- New: `_bmad-output/implementation-artifacts/2-2-walk-the-whole-town.md`
- Modified: `_bmad-output/implementation-artifacts/deferred-work.md`
- Modified: `_bmad-output/implementation-artifacts/sprint-status.yaml`
- Modified: `_bmad-output/planning-artifacts/epics.md`
- Modified: `_bmad-output/planning-artifacts/game-architecture.md`
- Modified: `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md`
- Modified: `loop/lessons.md`
- New: `tools/map/fbx_compare.py`
- New: `tools/map/map_check.py`
- New: `tools/map/map_export.py`
- New: `tools/map/map_mutate.py`
- New `.meta` files for every new asset and script above, plus `Assets/Scripts/World.meta` and `Assets/Data/World.meta`.
- Not in git (evidence): `_bmad-output/verification/2-2-walk-the-whole-town/` (census, map-check, colliders, edges, fall, walls, navmesh, walk, route, fps, regression, film, tools).

## Change Log

- 2026-11-11: Story file created by the loop CREATE phase (Claude Opus 5.5). Status → ready-for-dev.
- 2026-11-11: Implemented by the loop DEV phase (Claude Opus 5.5, attempts 1–2). Status → review.
- 2026-11-11: Code review by the loop REVIEW phase (Claude Opus 5.5): 2 patches to FallRecovery (applied and re-verified by running), 3 deferred, 10 dismissed. Status stays review (perceptual checks open).
