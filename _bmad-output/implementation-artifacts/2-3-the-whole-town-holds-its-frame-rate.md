---
baseline_commit: 7eca20d0510baa0e03912f50ea8f4c27be8106c5
---

# Story 2.3: The whole town holds its frame rate

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-10 by `plan-epic-2`; context created 2026-11-12 by the loop CREATE phase. In
`loop/dependencies.yaml` it depends on Story 2.1 only (ADR-5: people are 1.77 m, the eye is at 1.65 m, the town is
roughly real metres). Nothing depends on it, and it has no gate. It is built on master **after Story 2.2 merged**,
so the world it measures is 2.2's: the collider rule, `TownEdge`, `TownSeal`, `TownBounds` and the whole-town NavMesh.
It is the first story in this project to decide **how the town is rendered**. No document has a LOD, culling,
occlusion, batching or shadow plan yet; NFR6 only says "use LOD/culling across the open town". It takes up these
`deferred-work.md` entries and items left open:*
- *Story 2.2's AC5 note: "the absolute 60 FPS verdict is Story 2.3's" (lesson 19, `2-2-walk-the-whole-town.md:336`,
  `:1248`);*
- *"Always-animate + update-when-offscreen don't scale past `maxConcurrent 1`" (`deferred-work.md:66`). This story
  must **keep** both flags on the drunk and say why (AC3). It does not resolve the entry, because `maxConcurrent`
  is still 1;*
- *"Subjects can never occlude each other" (`deferred-work.md:104`): the drunk left `Default` for every other mask
  "with no survey of those call sites … culling". This story does the culling half of that survey.*

> **The starting point in nine facts.** All nine were read from files and evidence on 2026-11-12. Re-measure them in
> the editor and in a build (Task 1a) before relying on any of them.
> 1. **The town is heavier than `epics.md`'s preamble says.**
>    - The map is 16,738 renderers and 5,677,935 triangles. Story 2.2 added 1,782 more renderers: `TownEdge` (864
>      white boulders and 917 fillers) and `TownSeal` (one mesh). That makes about **18.5k renderers**.
>    - The scatter, by 2.2's census (`2-2-walk-the-whole-town.md:1615–1618` and `census/census.json`):
>
>      | Class | Renderers | Triangles | Height | Collider |
>      |---|---|---|---|---|
>      | Pines (`Circle`) | 6,000 | **2.0 M** | 3.2–10.4 m | trunk capsule |
>      | Grass tufts (`Cube`) | 8,000 | 624 k | **1.3–1.6 m** | none |
>      | White rocks (`Icosphere`) | 1,000 | 32 k | — | some |
>      | Stumps | 20 | — | — | — |
>
>    - **663 renderers (~0.57 M triangles) are buried** under the slice at y −68…−48. 2.2's census counts 674 below
>      y −1, which is a wider definition; recount in 1a. They have no colliders since 2.2, but they are **still drawn**
>      whenever they are in the frustum. 211 renderers make up the oil rig.
>    - `TownEdge`'s 1,781 rocks all share **one mesh** (the map's `Icosphere.002`) and **one material**
>      (`TownEdgeBuilder.cs:45`).
>    - The preamble's "each with a MeshCollider" is out of date. 2.2's collider rule left 1,974 MeshColliders plus
>      5,999 pine-trunk capsules and removed 8,765. The grass has no collider now.
> 2. **Nothing is set up for culling.**
>    - `SampleScene.unity:12` has `m_OcclusionCullingData: {fileID: 0}`, so occlusion culling was never baked.
>    - The scene has 0 `LODGroup`s.
>    - The `game map` PrefabInstance carries **0** `m_StaticEditorFlags` overrides, so its renderers are probably
>      not static. That is a reading of the YAML; confirm it in the editor.
>    - 1,790 objects carry static flags `86` (Occluder, Occludee, Batching and Reflection Probe static). They are
>      probably `TownEdge` and `TownSeal`.
>    - Fog is off (`m_Fog: 0`; the unused settings read linear fog to 300 m). Main Camera: far clip **1000**, FOV
>      60, near 0.095 (from `PlayerConfig`), and the occlusion-culling flag is on (with no data, it does nothing).
> 3. **The render settings today.**
>    - `Assets/Settings/PC_RPAsset.asset`: SRP Batcher **on**, dynamic batching off, **GPU Resident Drawer off**
>      (`m_GPUResidentDrawerMode: 0`), GPU occlusion culling off; main-light shadows **50 m**, 4 cascades, a
>      2048 shadowmap, soft shadows high; HDR on; MSAA off (`m_MSAA: 1`); render scale 1, no upscaling;
>      **`m_RequireDepthTexture: 1` and `m_RequireOpaqueTexture: 1`**, so every frame copies colour and depth.
>      Nobody has checked what reads them.
>    - Also in `PC_RPAsset`:
>      - additional lights are per-pixel, 4 per object, **with shadows** (a 2048 atlas);
>      - reflection-probe blending and box projection are on;
>      - LOD cross-fade is on (blue-noise dither), light cookies and light layers are on, lens flares are on.
>    - `PC_Renderer.asset`: `m_RenderingMode: 2`. That is **Forward+**, which the GPU Resident Drawer needs. Confirm
>      the enum with `unity_reflect`.
>      - Its one renderer feature is **SSAO**, active at **full resolution** with depth-normals as its source
>        (`PC_Renderer.asset` l.61–95). Depth-normals means an extra geometry pass over everything in view, so the
>        whole town is drawn twice at the hills.
>      - Render Graph is on (`UniversalRenderPipelineGlobalSettings.asset` l.308).
>    - Post-processing is on, from the camera and `SampleSceneProfile`: Bloom, Vignette and Tonemapping are active,
>      and Motion Blur is inactive. HDR is on and anti-aliasing is None. Nothing temporal is active, so a still pose
>      costs what a moving one does.
>    - **The build runs on D3D11.** 1.14's player log shows the D3D12 device filter rejected the MX350 (1,964 MB VRAM),
>      so the player fell back to Direct3D 11.0 (`1-14…/dev/player-dev-A.log`). Every GPU-side candidate must work on
>      D3D11.
>    - `ProjectSettings/GraphicsSettings.asset`: `m_BrgStripping: 0`. The GPU Resident Drawer needs
>      BatchRendererGroup Variants set to **Keep All**.
>    - `QualitySettings.asset`: the Standalone default is level 1, `PC`. `vSyncCount 0`, `lodBias 2`.
>    - `ProjectSettings.asset`: `runInBackground: 1`, **`enableFrameTimingStats: 0`**, `gpuSkinning: 1`,
>      `m_BuildTargetBatching: []`. Empty means the defaults, so a **build static-batches** the 1,790 static objects.
>      `fullscreenMode: 1`, default 1024 × 768, `productName: My project`, so the exe is `My project.exe`.
> 4. **What a frame costs today, as far as anyone measured: the editor only.**
>    - Story 2.2's play-mode tour at a 953 × 476 Game view (`2-2…/fps/before/summary.csv`), median of 3 rounds:
>      `slice_drunk` 28.5 ms, `plain_from_east_hill` 36.0, `street_block` 22.7, `shore_to_oil_rig` 9.2, `farm`
>      13.0, `south_hill_north` 62.5, `spawn_street` 20.8. The round-to-round spread was 5–47 ms, because the laptop
>      heats up.
>    - 2.2's edit-mode **render-only** A/B (`2-2…/close-a4/render-ab/render_ab.txt`), with renderers in view:
>
>      | Viewpoint | Map renderers in view | Triangles in view | `camera.Render()` (ms) |
>      |---|---|---|---|
>      | `plain_from_east_hill` | 15,157 | 5.36 M | 31.4 |
>      | `south_hill_north` | 14,695 | 4.84 M | 29.4 |
>      | `street_block` | 5,661 | 2.44 M | 15.8 |
>      | `spawn_street` | **1,910** | 1.21 M | **39.2** |
>      | `shore_to_oil_rig` | 236 | 1.22 M | 6.8 |
>
>    - **Two leads, both unproven.** At the hills the cost tracks the renderer count, not the triangles: the oil-rig
>      view draws 1.2 M triangles in 6.8 ms. A per-object CPU cost would fit that. `spawn_street` is the dearest view
>      with only 1,910 renderers, so something else is costing there. Candidates are the 50 m shadow pass among
>      dense pines, SSAO's full-resolution pass, the police cars, and the opaque/depth copies. Task 1a settles both.
>    - Nobody has measured a frame **in a build**, **at 1080p**, or **split into CPU and GPU time**.
> 5. **In a build, Epic 1's slice already misses 60 FPS on this laptop.**
>    - Story 1.14 measured a release build at 1920 × 1080 windowed on the MX350, at one vantage on the slice: 83 fps
>      cold, **~45–50 fps warm** (`1-14…/measure/analysis.json`).
>    - So 60 FPS cannot be shown on this machine. The AC itself says the laptop result is a *change* and that the
>      absolute verdict goes to Alexv. Decision 1i sets the laptop's relative target.
> 6. **The grader never looks at pixels.**
>    - `ShotGrader.VisibleFraction` is `Physics.Linecast` against `GradingConfig.occluderMask`, which is **Default
>      only** (`ShotGrader.cs:639–664`). The frustum gate is `GeometryUtility.TestPlanesAABB` (`:294`).
>    - `EventActor.Bounds` is the union of `r.bounds` over every child renderer (`EventActor.cs:124–141`).
>    - So **hiding renderers cannot move a verdict.** What can move one:
>      - changing a collider;
>      - moving a collider-bearing object off `Default`;
>      - changing the drunk's `updateWhenOffscreen`, which changes how his `SkinnedMeshRenderer.bounds` is computed;
>      - **lowering the camera's far clip**, because the frustum test includes the far plane;
>      - a cull distance on layer 8, which would hide him while he still grades.
>    - The drunk is on layer **8 `Subject`**. His prefab has `m_CullingMode: 0` (Always Animate,
>      `EventActor_Drunk.prefab` l.811) and `m_UpdateWhenOffscreen: 1` (l.601). Keep both (`deferred-work.md:66`).
> 7. **Per-camera culling only reaches the cameras that carry it.**
>    - The player's camera is Main Camera, which is `PhotoModeController.photoCamera`. The grader grades through it.
>    - The isolated **Photo Shoot** builds its own camera (`PhotoShootRig.cs:342`) in an empty world. It sees only
>      global changes (the pipeline asset, project settings), never a component on Main Camera.
>    - The **Town Placement Study** grades through the controller's camera in the real town
>      (`PhotoShootRig.cs:219–225`).
>    - Many rigs make their own cameras: `TownCensus.cs:172`, `TownPhotos.cs:52`, `GalleryShootRig.cs:179`,
>      `ScaleStudyRunner.cs:583/:980` and `GradeHudShootRunner.cs:1756`.
> 8. **Story 2.2's frame-rate tools are fragile.** Do not build on them as they stand.
>    - The viewpoint list (`fps/viewpoints.json`), the parts (`census/parts.json`), the walked trace
>      (`close-a4/walk/sprint/trace.csv`) and the A/B scripts (`tools/fps_ab.py`, `fps_compare.py`) all live in the
>      **git-ignored** evidence folder `_bmad-output/verification/2-2-walk-the-whole-town/`. A fresh checkout has
>      none of them.
>    - Lesson 127 says `tools/fps_ab.py`, but the file is at `…/2-2-walk-the-whole-town/tools/fps_ab.py`.
>    - The edit-mode ABBA render A/B was run ad hoc through `execute_code` and **never committed as code**.
>    - `TownRigRunner.Tour.cs:45/:99` **switches the drunk off**. This story's AC wants him on.
> 9. **Builds exist only as Story 1.14's fenced spike.**
>    - `Assets/Spikes/RecordingSpike/Editor/RecordingSpikeMenu.cs:24–67` uses `BuildPlayerOptions`,
>      `BuildOptions.None` (a release build) and `extraScriptingDefines`. It refuses unless the active target is
>      StandaloneWindows64, and writes to `Builds/<slug>/<flavour>/My project.exe`.
>    - The spike assembly's `defineConstraints` are `UNITY_EDITOR || CAMERA_RECORDING_SPIKE`, with
>      `autoReferenced: false`.
>    - `RecordingSpikeFenceTests` proves a plain build excludes it through
>      `CompilationPipeline.GetAssemblies(PlayerWithoutTestAssemblies)`.
>    - 1.14's builds on disk are from before ADR-5 and are stale. **Do not run them.**

## Story

As a player,
I want the game to stay smooth wherever I go and whatever I look at, including the view across the whole plain from the hills,
so that chasing a moment never stutters.

## Acceptance Criteria

AC1–AC4 below are copied word for word from `epics.md` §Story 2.3, and they are the contract. The notes under each
one say what it means in this project. They add precision and never relax a clause. AC5 is added the way Stories
1.11, 1.12, 1.14, 2.1 and 2.2 added one. It writes down the standing CLAUDE.md rules for a story that changes how
every system's pictures are drawn and that makes Windows builds.

**AC1: A fixed route through every part of the town, measured in a Windows release build at 1080p, holds 60 FPS; measured as a change against an interleaved baseline; the absolute verdict queued (NFR1)**

> **Given** a fixed route through every part of the town, including the widest views from the hills, with the drunk event active,
> **When** it is measured in a Windows release build at 1080p on `PC_RPAsset`,
> **Then** it holds 60 FPS (NFR1)
> **And** because this laptop is not NFR1's mid-range desktop, the result is measured as a change against a baseline run interleaved with it, and the absolute verdict is queued for Alexv.

- **"A fixed route through every part of the town".**
  - The route is a **committed file**, `tools/perf/route.json`, plus the script that made it, `tools/perf/route_build.py`.
    Never rely on the git-ignored 2.2 evidence at run time (fact 8).
  - **Source: Story 2.2's CLOSE a4 sprint walk**, `…/2-2-walk-the-whole-town/close-a4/walk/sprint/trace.csv`. The real
    controller walked it on the current world: 18 legs, 4,496 m, 0 sticks, 0 falls. Every one of 2.2's 18 parts
    (`census/parts.json`) is on it.
  - **Resample** it to poses about every 20 m. Each pose has a position, the ground y, and a yaw along the direction of
    travel, which is what a player walking it would see.
  - **A stretch** is one leg (18 of them, named `from→to`) or one look-out. Every frame and every pose is labelled
    with its stretch.
  - Record in the file where it came from: the source path, the commit, and the date.
- **"Including the widest views from the hills".** Add **look-outs** at the five hill parts (`hill_east`,
  `hill_south_boulders`, `hill_southwest`, `hill_west`, `hill_northwest`) and at 2.2's `south_hill_north` viewpoint.
  - Each look-out has 8 yaws at FOV 60.
  - Each also has **2 zoomed poses at FOV 18** facing across the plain, raised through the real
    `PhotoModeController.SetPhotoMode(true)` and `SetZoom(1)`. The viewfinder is part of the game, and zoom changes
    what is in the frustum and what LOD is chosen.
  - Also carry 2.2's 7 viewpoints (`fps/viewpoints.json`, copied into `route.json`) as stretches of their own, so this
    story's numbers can be set against 2.2's.
- **"With the drunk event active".**
  - `EventManager` runs as in normal play, and the drunk cycles through his pooled lifecycles on his own timers: about
    26.5 s per cycle (3 / 10 / 1.5 / 6 / 4 s plus a 2 s respawn).
  - The bench logs his phase on every frame and counts the lifecycles.
  - At least the slice stretch (`spawn→pub`) and `slice_drunk` have him in the frame. Show that with a capture.
  - **Never switch him off**, as 2.2's tour rig does (fact 8).
- **"Measured in a Windows release build at 1080p on `PC_RPAsset`".**
  - `BuildOptions.None`, not a Development build, for StandaloneWindows64, quality `PC` (the Standalone default).
  - Launch it windowed at 1920 × 1080: `-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile <dir>/player.log`,
    which is 1.14's precedent. The panel is 2560 × 1600 with Windows scaling, so check the resolution rather than
    assume it.
  - **The bench logs, and every summary prints:**
    - `Screen.width × height`;
    - `SystemInfo.graphicsDeviceName` and the graphics API (Optimus may pick the MX350 or the Iris Xe, and that changes
      everything; 1.14 always got the MX350 on D3D11);
    - the active pipeline asset's name and the quality level name;
    - `QualitySettings.vSyncCount` and `Application.targetFrameRate`;
    - `SystemInfo.batteryStatus`;
    - whether the Windows session was **locked**. The external sampler records whether `LogonUI` is running (lesson 29).
  - **A run is void** if it ran on battery, on a different GPU, at a different resolution, or while the session was
    locked. Say which runs were void.
  - **Uncapped:** `vSyncCount 0` and `targetFrameRate −1`, as the game ships, so the headroom is visible.
- **"Holds 60 FPS".** Decision 1i fixes the exact rule **before** any measurement. Recommended:
  - Per stretch, the 95th-percentile frame time is ≤ 16.67 ms.
  - Report beside it the median, p99, the 1 % low (the mean of the worst 1 % of frames, as fps) and the share of frames
    over 16.67 ms.
  - Frames are timed with `Time.unscaledDeltaTime`. Where they read valid, also record CPU main-thread, CPU
    render-thread and GPU time per frame (1c), so every verdict says *which side* is over budget.
- **"Measured as a change against a baseline run interleaved with it".** Report two changes, both from interleaved
  runs (lesson 28: difference-in-differences, rounds rotated):
  - **(a) After against before**, the gain from this story's culling. The **before** build is today's game plus the
    bench. The **after** build is the finished story. The run order is `before, after, after, before, before, after,
    after, before` (ABBA ×2, at least 4 runs per build). This is also AC2's evidence.
  - **(b) Each stretch against the slice, in the same run.** The slice stretch (`spawn→pub` with the drunk in view) is
    what Epic 1 shipped and what 1.14 measured. "The whole town costs no more than the slice" is a statement that means
    something on this laptop, and it carries to the desktop: if the slice holds 60 there, every stretch that costs no
    more than it does too. Decision 1i makes this the laptop target.
  - **Noise:** the run-to-run spread *within* each build's own runs. That is the A/A control.
- **"The absolute verdict is queued for Alexv".**
  - A `human_checks` entry: run the after build's bench on a mid-range desktop with one command
    (`tools/perf/bench_run.ps1 -Mode route`), and read `summary.txt`.
  - Report the laptop's absolute numbers honestly anyway, labelled as from this laptop.
  - **Never manufacture a 60** by lowering the resolution, the render scale or the quality level (1.14's rule), or by
    culling what a player would see (AC2's pop rule).

**AC2: Culling chosen by measuring, applied; before and after runs over the same route show the gain per stretch; a contact sheet shows nothing near the player popping (NFR6)**

> **Given** culling chosen by measuring (for example occlusion culling, cull distances for small ground scatter, LODs or batching),
> **When** it is applied,
> **Then** a run before it and a run after it, over the same route, show the gain on each stretch of the route
> **And** a contact sheet from that route shows nothing near the player popping in or out (NFR6).

- **"Chosen by measuring".**
  - **First find out where the time goes** (1a). Split the frame into CPU main thread, render thread and GPU, per
    stretch, in the build. Count per pose:
    - renderers in the frustum, by class;
    - the triangles;
    - shadow casters inside the shadow distance;
    - batches and SetPass calls, where the counters read valid in a release player.
  - **Then screen every candidate on its own** (1d). Use the edit-mode ABBA render A/B (lesson 131: ~1 ms precision in
    15 s; commit it as code this time) at the stretches that are over budget. Confirm the shortlist in a build. Note the
    GPU Resident Drawer "speeds up less in the Scene and Game views than in Play mode or a build" (Unity manual).
  - **Choose** the candidates whose gain beats the noise on the stretches that need it, and that break no rule below.
    Record every candidate's numbers, **including the ones not chosen**, in a decision table.
- **"(for example occlusion culling, cull distances for small ground scatter, LODs or batching)".** Each candidate's
  known costs and risks are in Dev Notes §"Candidates". The epics list is a set of examples, not a menu you must use
  up: "none of these helps here, because …" is a valid row, with evidence.
- **"A run before it and a run after it, over the same route, show the gain on each stretch".**
  - The ABBA runs from AC1 (a), one table row per stretch: before and after median and p95, the gain in ms and in %,
    the noise, and a verdict (gain beyond the noise / within it / **worse**).
  - Draw the stretches on 2.2's top-down map (`census/topdown.png` with its transform in `topdown.json`; copy both into
    this story's evidence), coloured by gain.
  - A stretch that gets **worse** beyond the noise is a finding. Fix it or explain it.
- **"A contact sheet from that route shows nothing near the player popping in or out (NFR6)".**
  - **Define "pops" before you look** (1h). Recommended: an object appears, disappears, or swaps its LOD while its
    projected height on a 1080-line frame is **≥ 2 px** at the camera's current FOV.
  - **The zoom matters.** At FOV 18 the same object is about 3.2× taller on screen than at FOV 60, so a cull distance
    that is invisible in Walk can pop in the viewfinder. Any distance-based cull must scale with the live FOV, or be
    screen-size based (an LOD).
  - **Do the arithmetic.** A 1.5 m grass tuft at FOV 60 on 1080 lines is about 1,403 / d px tall: 14 px at 100 m,
    and below 2 px only beyond ~700 m. **So most scatter here is not "small" by this rule.** Expect distance culling of
    grass to buy little before it pops.
  - **The evidence:**
    - **Pose pairs.** At every route pose, capture the before build and the after build at the same pose. The bench's
      `capture` mode does this with `ScreenCapture.CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, with the
      camera rendering to the screen (CLAUDE.md overlay trap). Never capture during a timed run.
    - Diff each pair, amplified. **Annotate** every object the culling removed or changed in view: its class,
      distance and projected height. These are computed from the same pose in the editor, by the same rule the culling
      uses.
    - Lay it out as one labelled contact sheet per group of stretches (`tools/perf/pop_sheet.py`). Its worst-case page
      lists every changed object ≥ 2 px, which should be **none**.
    - **Boundary crossings.** For each culled class, a short sequence walking across its cull boundary at the
      nearest approach on the route, at FOV 60 and at FOV 18.
    - **A positive control.** One control run with a cull distance set deliberately near (e.g. 5 m on the culled
      class) must show up on the sheet as a pop. Otherwise the sheet proves nothing.
    - **A diff with no culled object in it** (lighting, shadows, probe blending) is not a pop, but it *is* a change in
      what the player sees. List it under AC5, photograph it, and queue it for Alexv if it is visible.
  - Whether a remaining distant change *reads* as a pop to a player is perceptual. Queue a clip (`human_checks`), after
    the measured sheet shows none ≥ 2 px.

**AC3: An event actor the player can see is never culled, and the grader's verdict on him is unchanged (Photo Shoot no regression)**

> **Given** an event actor the player can see,
> **When** culling runs,
> **Then** the actor is never culled, and the grader's verdict on him is unchanged: the Photo Shoot rig shows no regression.

- **"Never culled", by construction.** Write it as rules in code, each with an EditMode test:
  - The `Subject` layer (8) has a cull distance of **0** (the far clip) in every configuration. `TownCullingConfig`'s
    `TryGetConfigProblem` rejects a config that culls it, and the runtime component forces 0 whatever the config says.
  - No actor renderer is ever inside an `LODGroup` or given a Mesh LOD.
  - If occlusion data is baked (Umbra), actor renderers either set `allowOcclusionWhenDynamic = false` or are proven
    never falsely culled by the rig below. Record which.
  - GPU occlusion culling (if chosen) touches only renderers the GPU Resident Drawer draws, and the drunk is a
    `SkinnedMeshRenderer`. Prove he is not one of them (Frame Debugger, or his pixels in the rig).
  - **Keep** `m_CullingMode: 0` (Always Animate) and `m_UpdateWhenOffscreen: 1` on the drunk
    (`deferred-work.md:66`). They fixed a mid-walk freeze, and the second one feeds his `Bounds`. Add a dated line to
    that entry: re-checked by 2.3, kept, `maxConcurrent` still 1.
- **"An event actor the player can see": prove it by rendering, with the culling on.** Build a play-mode rig,
  `Tools/Perf/Actor Visibility (Play)`, in SampleScene.
  - **What it stages:** the drunk at his peak spot and at staged positions. These include the farthest he can be seen
    from the hills, the viewfinder zoomed at FOV 18, through gaps between pines, beside the street block's walls, and
    at every cull boundary the chosen culling has.
  - **How it counts him:** his visible pixels, measured as the difference between renders with his renderers shown
    and hidden by `forceRenderingOff` in ABBA (2.2's switch). Restore them symmetrically (ADR-4 risk note:
    `game-architecture.md:519–522`).
  - **What it compares:** culling on against culling off, at the same pose. The pixel counts must match, within the
    measured render noise.
  - **Positive control:** put him on a culled layer, or give him a near cull distance, for one control pose. His
    pixels must drop to 0, so the rig is proven able to see culling. Say at the top of its output that the control is
    provoked on purpose.
  - Write a contact sheet with his box drawn on each frame.
- **"The grader's verdict on him is unchanged: the Photo Shoot rig shows no regression".** Run two rigs. Copy every
  output folder aside first: the rigs **wipe** their folders.
  - `Tools/Grading/Photo Shoot (Play)`, the isolated world. It sees only global changes, such as the pipeline asset
    or the GPU Resident Drawer (fact 7). Diff the stars and the composition per shot, **unmasked** (lesson 41), against
    2.2's `regression/photo-shoot/`.
  - `Tools/Grading/Photo Shoot — Placement Study (Town)`, the real town through the controller's camera. This is the
    one that sees per-camera culling. Diff it against 2.2's `regression/photo-shoot-town/`.
  - **Expected noise:** `i_from_behind` swings 1–3 stars on rig timing alone (lesson 42). 2.2 saw 76→75 % and 55→52 %
    drift. A move beyond that is a finding: trace it to the frame.
  - Since the grader reads physics and bounds only (fact 6), **a verdict that moves means this story changed a
    collider, a layer of a collider-bearing object, or the actor's bounds.** Find which.

**AC4: A ~30-minute session walking the route on a loop; memory sampled from outside the process stays flat (NFR3)**

> **Given** a long session (about 30 minutes of walking the route on a loop),
> **When** the game's memory is sampled from outside the process,
> **Then** it stays flat (NFR3).

- **"About 30 minutes of walking the route on a loop".**
  - Use the **after** build, in the bench's `walk` mode. It drives the **real controller** along `route.json`'s
    polyline with the Input System's own path:
    - Keyboard state events for W, or W + LeftShift (2.1/2.2's drive, which queues events into the real
      `PlayerInput`);
    - the yaw set on the player's transform each frame toward the next point, labelled "the mouse's job" (2.2's
      `TownRigRunner.Tour.cs:65/:177`).
  - That drive lives behind `#if UNITY_EDITOR` today. The bench needs its own copy inside the fenced assembly (1c).
    It drives the game; it does not re-implement it.
  - **An unfocused build window can drop queued keyboard input** (1c, "Focus"). The bench must handle it, or the soak
    is a player standing still for 30 minutes with a perfectly flat memory line.
  - **At least 30 minutes and at least 2 full loops** (lesson 12: bugs hide in the second cycle). Sprint covers the
    4.5 km loop in ~11 min, so 30 min is ~2.7 loops; walking takes ~19 min per loop. Record the gait.
  - The drunk stays active throughout, about 68 lifecycles in 30 minutes. Count them.
  - **Stuck handling:** if progress stalls for more than 3 s, log a stick with its position and move to the next
    point with `ThirdPersonController.Teleport`. Report the count. A rising count is itself a finding.
  - Count FallRecovery restores too (`FallRecovery.LastRecoveryTime`, lessons 93 and 130).
- **"Sampled from outside the process".**
  - Use `tools/verification/recording-spike/procmem.ps1`, or a copy in `tools/perf/` that takes the process name and a
    1 s interval. It records `PrivateMemorySize64`, `WorkingSet64`, `HandleCount` and the thread count from
    `Get-Process`, plus whether `LogonUI` is running.
  - It picks the **newest** process, because two `My project` processes overlapping broke 1.14's first version.
  - Lesson 26: inside this Mono player `Process.PrivateMemorySize64` reads 0. That is why the sampling is external.
  - **In-process, as a secondary record:** once a second, `Profiler.GetTotalReservedMemoryLong`,
    `GetTotalAllocatedMemoryLong`, `GetMonoUsedSizeLong` and `GC.GetTotalMemory(false)`. Once a minute, the count of
    live `GameObject`s and `EventActor`s. Use `UnityEngine.Object.FindObjectsByType`, and only once a minute: it is
    expensive.
- **"Stays flat".** Decision 1j fixes the rule and proves it can fail, **before** the real soak. Recommended:
  - After a 5-minute warm-up, the least-squares slope of private MB over the rest of the run is below a threshold.
  - The threshold is set **from the positive control**. A bench-only `leak` mode adds a known growth, for example a
    1 MB managed array kept every 10 s (6 MB/min), for 5 minutes. The same rule must flag it clearly.
  - **And** loop 2 against loop 1: private MB at matched route positions. The difference stays within the run's own
    short-term noise.
  - **And** the in-process object counts are flat: no growing `EventActor` or `GameObject` count.
  - Report a chart (private, working set, Unity reserved, against time, with the loop boundaries and the lifecycles
    marked) and the numbers.
- **Per-frame log size.** 30 minutes at ~40–60 fps is 72k–108k frames, and 1.14's per-frame log overflowed at 40,000.
  In `walk` mode, aggregate per second, or preallocate for the whole run. The soak also yields frame times while
  walking: keep them as a secondary check on AC1's pose-locked numbers.

**AC5 (cross-cutting): no regression, the project put back, the build hygiene kept, and the docs made consistent (NFR3, NFR5, NFR6, NFR8, CLAUDE.md)**

- **Epic 1's loop and Story 2.2's world still work.** Run each once on the finished story:
  - `Tools/Events/Trace Actor Route (Play)` for **≥ 3 pooled cycles**: same instance, on the NavMesh, arriving by the
    agent's own test before despawn, peak ~1.5 s. Compare with 2.2's CLOSE a4: 0.62–0.65 s before despawn, peak
    1.49–1.50 s at x 26.31–26.33.
  - `Tools/Scale/Movement After (Play)`: walk 4.00 and sprint 7.00 m/s, apex ~1.26 m, kerb climbed, raise and lower
    under 0.3 s.
  - The Gallery and HUD rigs once each. They must **run** and their captures must be **non-blank**. A pipeline change
    such as the GPU Resident Drawer can break `cam.Render()` into an RT, and a blank capture means a broken rig or a
    broken render (lesson 9).
  - If any change touched layers, colliders or static flags of collider-bearing objects: 2.2's
    `Tools/Town/Walk the Whole Town - sprint (Play)` once, and the AC4 wall check of 2.2 at its worst walls.
- **Pathfinding depends on the frame rate.** `EventActor.PendingTimeout` is 10 s of wall time, and the pathfinder's
  budget is per frame (`deferred-work.md:335`). Note in the result whether the after build's frame rate changes that
  risk. Do not change `EventActor` for it.
- **The EditMode suite passes.** It ran 254/254 at 2.2's close; report the real count. The new tests cover:
  `TownCullingConfig` (`Safe*`, finite checks, `TryGetConfigProblem`, the Subject-layer rule); any pure cull rule; the
  bench fence (`PerfBenchFenceTests`, copied from `RecordingSpikeFenceTests`, with its positive control); and any new
  file-size guard.
- **Fail-soft (NFR8).** A missing or invalid `TownCullingConfig` logs **once** and the game renders with **no**
  culling, never with broken culling and never with an exception. Provoke it. Say at the top of the rig's output that
  the warning is provoked.
- **NFR3 in the shipped code.** Nothing this story adds allocates per frame or calls `Instantiate`/`Destroy` in a
  gameplay loop.
  - Set `Camera.layerCullDistances` from a **preallocated** `float[32]`, and only when the live FOV has moved.
  - **Never read the `layerCullDistances` getter per frame**: it allocates a new array.
- **Build hygiene.**
  - The plain player (no `CAMERA_PERF_BENCH`) contains no bench assembly: the fence test, plus a listing of a plain
    build's `Managed/` folder once.
  - **Every committed file is under 50 MB.** That includes `SampleScene.unity` if layer or flag overrides grow it (14.1 MB
    today), and any occlusion data. Never add an LFS rule. The existing guard (`TownMapGuardTests`) covers the scene
    and the NavMesh; extend it to any new large asset.
- **Settings: intended against re-serialised.** A build re-serialises settings files (lesson 24):
  `PC_RPAsset` prefilter flags, URP global settings, `ProjectSettings` preloaded assets and batching, and
  `UnityConnectSettings`. This story may change some of the same files **on purpose** (the GPU Resident Drawer, BRG
  stripping, static batching). So:
  - before each build, commit or record the intended state;
  - after each build, `git diff` every settings file;
  - restore every **unintended** hunk from the recorded state and reimport;
  - list the intended changes hunk by hunk in the Dev Agent Record.
  - ProjectSettings edits go through the editor API, never on disk (CLAUDE.md).
- **Put back exactly as you found it.** Record each value before you change it, and confirm it afterwards with
  `git diff` and an editor read-back:
  - the open scene (`SampleScene`, not dirty, with no rig objects);
  - `Time.timeScale`, the quality level (`PC`), `Application.runInBackground`, `Application.targetFrameRate`,
    `QualitySettings.vSyncCount`, the Input System settings and every SessionState flag (lesson 37: stamp and expire);
  - `NavMesh.pathfindingIterationsPerFrame` if a rig touched it;
  - `PlayerSettings` (defines, frame timing stats) and the active build target. **Never switch platforms**: it reimports
    the whole project.
- **The docs are consistent** (Task 11):
  - `game-architecture.md`: a new subsection **"Rendering budget and culling (Story 2.3)"**, a Decision Summary row
    **12 — Rendering budget**, `Scripts/Bench/` and the new `World` files in §Directory Structure and §System Location
    Mapping, and the frontmatter `updated:`;
  - `deferred-work.md`: dated notes on :66 and :104, plus a new entry handing NFR1's "including while recording" to
    Story 1.15, which must re-run this bench with recording on;
  - the decision log, `loop/lessons.md` (the corrected `fps_ab.py` path in lesson 127), and this story file.
- **Untouched:** grading (`ShotGrader`, `GradingConfig`), the colliders and `ColliderRule`'s collider behaviour, the
  NavMesh and its bake, `FallRecovery`, `TownBounds`, `TownEdge`, `TownSeal`, the map FBX's contents, the capture
  path, the gallery, the HUD's behaviour, `EventActor`, the drunk's prefab flags, and the recording spike. **Tooling
  for 2.3 never edits 2.2's or 1.14's rigs**: copy what you need.

## Tasks / Subtasks

> **Working order and checkpoints.** The story is long, and a fresh session may have to pick it up halfway.
> - **Order:** 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12 → 13.
> - **Order matters in four places:**
>   - The bench and the route (Task 2) must be **final before the before build** (Task 3). If the bench changes after
>     that, the before build is stale: rebuild it (Task 3.4 says how) and re-baseline.
>   - All baselines (Task 3) come before any culling change (Task 5).
>   - The after build (Task 6) comes after the last change to the game. A later fix means rebuilding it and re-running
>     the ABBA.
>   - The soak (Task 9) runs on the final after build.
> - **After each task:** commit, and add a dated line to the Dev Agent Record saying what was measured and where the
>   evidence is.
> - **If you run low on context,** stop at a task boundary and return `failed` with a `next_hint` naming the next
>   task and where the builds are.
> - **Never leave `SampleScene` or a settings file half-edited.** Finish the task, or put the file back with
>   `git checkout -- <path>`, which is allowed because it is not a branch switch. Say which you did.
> - **Builds take minutes, and an `execute_code` call over ~30 s times out on the client but keeps running in Unity**
>   (lesson 122). Wait on the build's report file with Monitor and an until-loop. Never re-issue it, and never sleep
>   blind.

- [ ] **Task 1: Measure the frame, and settle the eleven things that gate this story. DO THESE FIRST.**

  Each item has a recommendation. Record the choice **and the reasoning** in the Dev Agent Record, including where you
  follow the recommendation: a later reader needs to know it was decided, not defaulted. 1a needs the bench (Task 2)
  for its build half. Do its editor half first, then come back.
  - [ ] **1a. Census of the frame.** Write `evidence/census/`.
        - **In the editor (edit mode, `Tools/Perf/Census (edit mode)`):**
          - Classify every renderer in the scene by base name **and** size, as `ColliderRule` does (reuse its
            inputs; do not invent a second naming scheme): pines, grass tufts, white rocks, stumps, buildings, props,
            vehicles, terrain tiles (`Plane*`, `Landscape*`), the oil rig, the buried set, `TownEdge`, `TownSeal`, and
            actors.
          - Per class: the count, the triangles, the **unique meshes and materials** (this is the instancing potential:
            are the 8,000 grass tufts 1 mesh or 8,000?), shadow-casting mode, static flags (read back in the editor,
            settling fact 2), layer and collider.
          - Per route pose: renderers in the frustum per class (`GeometryUtility.TestPlanesAABB` over every renderer's
            bounds, at FOV 60 and at FOV 18), the triangles in view, and the shadow casters within the 50 m shadow
            distance.
          - **What reads `_CameraOpaqueTexture` and `_CameraDepthTexture`?** Search the project's shaders and shader
            graphs. If nothing needs them, turning them off is a free candidate.
        - **In the before build (`bench` mode `census`):** per stretch, CPU main-thread, render-thread and GPU frame
          time, plus batches, SetPass calls, triangles and shadow casters, from whichever counters read `.Valid` in a
          release player. Settle it per stretch: **CPU-bound or GPU-bound, and on what.**
        - **Fallback if the release counters are empty:** one **Development** build for the counters only, labelled.
          Never use Development-build *times* as AC evidence.
  - [ ] **1b. The route** (AC1).
        - **Recommended:** build `route.json` from 2.2's CLOSE a4 sprint trace, resampled every ~20 m (≈ 225 poses),
          plus 6 look-outs (8 yaws at FOV 60 and 2 zoomed at FOV 18 each; ≈ 60 poses) and 2.2's 7 viewpoints.
        - Per pose: position, yaw, pitch (0 on the legs; look-outs level, or 2.2's look target), FOV, stretch, leg
          index and distance along the route.
        - The walk polyline for AC4 is the same trace, decimated to its corners.
        - Commit `route.json` and `route_build.py`. Draw the route on the top-down map.
  - [ ] **1c. The bench: how the build measures itself.**
        - **Recommended: a define-fenced runtime assembly**, 1.14's proven pattern:
          - `Assets/Scripts/Bench/CameraGame.Bench.asmdef`, `defineConstraints: ["UNITY_EDITOR || CAMERA_PERF_BENCH"]`,
            `autoReferenced: false`, referencing `CameraGame` and `Unity.InputSystem`, namespace `CameraGame.Bench`.
          - **Boot:** `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`, armed only by `-perfbench <mode>` on the
            command line or, in the editor, by a SessionState flag stamped with `EditorApplication.timeSinceStartup`
            and expired after ~120 s (lesson 37). Like `SpikeHarness`, it creates its own GameObject at runtime and
            edits nothing in the scene.
          - **Modes:**
            - `census` (1a);
            - `route`, pose-locked timing;
            - `capture`, the AC2 pose pairs;
            - `walk`, the AC4 soak and walking frame times;
            - `leak`, the AC4 positive control;
            - `actor`: in the build, the drunk's pixels at the slice poses, as a cross-check of AC3's editor rig.
          - **Args:** `-perfbench-route <path to route.json>` (so no route data ships in a plain build), `-perfbench-out
            <dir>`, `-perfbench-minutes <n>`, `-perfbench-gait walk|sprint`, `-perfbench-tag <label>`, and a hard
            `-perfbench-timeout`.
        - **`route` mode, pose-locked.** At each pose:
          1. Teleport the player there with the real `ThirdPersonController.Teleport`, and set the yaw on the
             transform.
          2. Raise or lower the camera with `PhotoModeController.SetPhotoMode` and `SetZoom` (the real path) where the
             pose asks for it.
          3. Settle about 5 frames.
          4. Time about 20 frames.

          Every run sees the same views in the same order, so before and after are comparable pose by pose. Moving the
          camera continuously would add nothing to the render cost here (no temporal effects are enabled; confirm
          there is no TAA or motion blur in the URP volume). The soak's walking frame times check that.
          - Watch FallRecovery during the teleports (2.2: a disabled controller made it warn 28 times). **Keep the
            controller enabled**, and void any pose where FallRecovery fired.
        - **Per frame, into preallocated arrays:** frame index, `Time.unscaledDeltaTime`, stretch, pose, drunk phase,
          drunk-in-view, and the FrameTimingManager values. They come back **4 frames late**, so attribute them by
          frame index.
        - **Frame timing in a release player.** Try `ProfilerRecorder` on the FrameTimingManager-backed counters (CPU
          main thread, CPU render thread, GPU frame time) first. Per Unity's docs, attaching a `ProfilerRecorder`
          enables FrameTimingManager even in release builds. Check `.Valid` and log the result.
          - Fallback: `PlayerSettings.enableFrameTimingStats = true` for bench builds only, set and restored around
            the build through the editor API, and read back.
          - GPU time may read 0 (the docs say so). Report it as unavailable, never as 0 ms.
        - **Outputs** in the `-perfbench-out` folder: `frames.csv`, `poses.csv`, `seconds.csv` (memory, in-process),
          `summary.json` (device, resolution, quality, pipeline asset, vsync, cap, battery, the drunk's lifecycle
          count, void flags) and `events.log`.
        - **Quit** with `Application.Quit()` when done or at the timeout. Log with `GameLog.Info("PerfBench", …)`.
          `Debug.Log` noise is fine in the bench; `GameLog` keeps it grep-able in `player.log`.
        - **⚠️ Focus.** An unattended build's window may not have keyboard focus.
          - The Input System's default `InputSettings.backgroundBehavior` resets and disables non-background devices
            when the app loses focus, so queued Keyboard events in `walk` mode can silently do nothing.
          - Windows 11 may also throttle a background window's process.
          - So the bench sets `InputSystem.settings.backgroundBehavior = IgnoreFocus` in its own process (bench-only,
            and the process ends with the run). It logs `Application.isFocused` per frame, and the summary reports the
            share of unfocused frames.
          - Treat a timed run that lost focus as suspect: compare it, then void it or explain it.
          - Check the name with `unity_reflect`; the input settings are Input System 1.18.0.
  - [ ] **1d. The candidates, and how each is screened** (AC2). Screen each **on its own** against today's world,
        first by the edit-mode ABBA render A/B at the stretches over budget, then (the shortlist) in a build. Dev Notes
        §"Candidates" lists what each touches.
        - **Recommended order, cheapest and least visible first:**
          1. **content that is never visible:** the 663 buried renderers under the slice (~0.57 M triangles). They are
             drawn whenever they are in the frustum. Switching their renderers off is invisible by construction, but
             prove it with photographs from the hills and the slice. Use `ColliderRule`'s "buried below y −1" class,
             through `RenderRule`;
          2. the opaque and depth texture copies (if nothing reads them);
          3. **SSAO**: its full-resolution depth-normals pass draws the scene a second time. Measure it off, at half
             resolution (`Downsample`), and with a depth-only source. It is visible, so photograph it;
          4. the **GPU Resident Drawer** (instanced drawing; it needs BRG Keep All; also try static batching off);
          5. **GPU occlusion culling** (needs the GPU Resident Drawer);
          6. **shadow work**:
             - shadow casting off for classes whose shadows do not read (prove it with photographs);
             - the cascades;
             - the additional lights' shadows;
          7. **per-layer cull distances** for whatever passes the 1h pop rule, scaled by the live FOV;
          8. **Mesh LOD** on import for classes with many triangles in view. The pines carry 2.0 M of the 5.7 M
             triangles. It changes the `.meta` only (`generateMeshLods: 0` at `game map .fbx.meta` l.59 today);
          9. **Umbra occlusion bake** (static flags plus a bake; measure the bake time and the data size);
          10. **far clip or fog**, only with photographs and as a decision for Alexv. It changes the look, the GDD wants
              subjects to "telegraph at a distance" (`gdd.md:342`), and a nearer far clip changes the grader's frustum
              test (fact 6).
        - **Post-processing** (Bloom, Vignette, Tonemapping) is the camcorder look. Measure its cost so the census is
          complete, but changing it is a look decision for Alexv, not a culling choice.
        - **Not every candidate is culling.** SSAO, the texture copies and the shadow settings are render-cost savings.
          - AC2 is met by the **culling** the evidence supports. "Culling" means not drawing what does not reach the
            picture: the buried set, cull distances, LOD, occlusion, and the GPU Resident Drawer's instancing and
            occlusion.
          - AC1 may also need non-culling savings to reach the laptop target (1i). Use them only if measured.
          - **Any candidate that changes the picture** (SSAO, shadows, LOD, probe blending) is chosen only with a photo
            pair from the eye, recorded in `decisions`, and queued as a look check for Alexv (Dev Notes §"Human
            checks", 3).
        - Record a table: candidate, stretch, gain in ms (with noise), what it changes on screen, cost, reversal cost,
          chosen or not.
  - [ ] **1e. Where the culling lives** (architecture §Configuration).
        - **Recommended:**
          - Tunables go in `TownCullingConfig`, a ScriptableObject: `Assets/Scripts/World/TownCullingConfig.cs` and
            `Assets/Data/World/TownCullingConfig.asset`, created as YAML from the script's `.meta` GUID (memory:
            create-so-asset-via-yaml). It holds the per-class cull distances at the reference FOV, the reference FOV,
            `layerCullSpherical`, and any shadow choices that are per-object rather than in the pipeline asset. Every
            field has a `Safe*` accessor, **finite checks** (lesson 72) and `TryGetConfigProblem`, copied from
            `TownBoundsConfig` and `PlayerConfig`.
          - Applied by `Assets/Scripts/World/TownCulling.cs` on **Main Camera** (the photo camera). It sets
            `layerCullDistances` in `Awake`, and again in `LateUpdate` only when the live FOV has moved past an
            epsilon, from a preallocated array (AC5). It forces the `Subject` layer to 0.
          - **Every camera that renders the town for the player** must carry it. Today that is Main Camera. Tomorrow it
            includes ADR-4's replay re-render camera (Story 1.15), so note that in the architecture.
          - Pipeline-wide settings (the GPU Resident Drawer, GPU occlusion, shadow cascades, the texture copies) stay
            in `PC_RPAsset`, which is already config. **`Mobile_RPAsset` is untouched.**
          - Per-object assignments (a culled class's layer, or its shadow-casting mode) are made by **one pure rule**
            (`Assets/Scripts/World/RenderRule.cs`, EditMode-tested) applied by an idempotent editor tool,
            `Tools/Town/Apply Render Rule`. Today's colliders are made the same way (2.2's `ColliderRule` +
            `AddMeshColliders`), and so a re-run, or a re-import of the map, can restore them.
        - **Layers:** a new layer for a culled class (free slots are 10–31; 3, 6 and 7 are empty too). Add it through the
          editor API, never by editing `TagManager.asset` on disk.
          - **Only collider-less objects may change layer.** A collider-bearing object leaving `Default` drops out of
            `GradingConfig.occluderMask` (the grader's verdicts move) **and** out of the NavMesh bake's layer mask
            (Default + WorldBounds; the next re-bake loses it). The grass tufts have no collider since 2.2, so they are
            safe; pines and rocks are not.
          - Check `deferred-work.md:103` (adding a layer re-serialises `TagManager` rendering-layer names). Diff it.
        - **Per-object overrides on the map's PrefabInstance** grow `SampleScene.unity`: 8,000 overrides are ≈ 2–4 MB.
          Measure it. An `AssetPostprocessor` on the map FBX is the alternative, but it needs a full map reimport (time
          it). After **any** map reimport, count the colliders that still resolve (lesson 79/83; `TownMapGuardTests`).
  - [ ] **1f. Actors are never culled** (AC3): the four rules in AC3's notes, where each is enforced, and its test.
  - [ ] **1g. The builds.**
        - **Recommended:** two release builds, plus the editor.
          - `Builds/2-3-the-whole-town-holds-its-frame-rate/before/` is built in Task 3 from the bench-complete tree
            before any culling. Record the commit.
          - `…/after/` is built in Task 6 from the finished story.
          - Both are made by `Tools/Perf/Build Bench Player (before)` / `(after)` in
            `Assets/Scripts/Editor/PerfBenchMenu.cs`: `BuildPlayerOptions` with `extraScriptingDefines =
            { "CAMERA_PERF_BENCH" }`, `BuildOptions.None`, the scene list from `EditorBuildSettings` (only
            SampleScene), a refusal unless the active target is StandaloneWindows64, the defines read before and
            after, and a `BuildReport-<label>.txt` (result, duration, size, `Managed/` listing).
          - Also `Tools/Perf/Build Plain Player` once, for the fence listing (AC5).
        - **Why two builds and not one with a runtime toggle:** the GPU Resident Drawer, static flags, Mesh LOD and a
          bake cannot be switched off at runtime. Per-camera cull distances could be, so the bench may also offer
          `-perfbench-culling off` for per-camera culling. That is a supplementary A/B, never a substitute.
        - **Keep both builds on disk** (git-ignored) until the story closes; the CLOSE phase may re-run them. If `before`
          is lost: write the start commit's settings files and scene back with `git show <commit>:<path> > <path>`,
          reimport, build, then restore HEAD's and read them back. That changes file contents, not branches.
  - [ ] **1h. What "pops" and "near the player" mean** (AC2): the ≥ 2 px rule at 1080 lines and the live FOV,
        or a better one you can justify with numbers. Write the arithmetic per culled class (its height, its cull
        distance, its projected height there at FOV 60 and at FOV 18). Decide it before the first contact sheet.
  - [ ] **1i. What "holds 60 FPS" means here, and the laptop target** (AC1).
        - **Recommended rule:** per stretch, p95 ≤ 16.67 ms on NFR1's desktop (Alexv's check).
        - **Recommended laptop target:** in the after build, every stretch's p95 is no higher than the slice stretch's
          p95 in the same run, beyond the A/A noise. Report the absolute laptop numbers beside it.
        - **If a stretch misses the laptop target after the best culling the evidence supports, that is a defect to
          fix, not a human check** (loop rule). Find what it costs (1a) and add or tune a candidate. Only what
          remains *after that* (the absolute desktop verdict) goes to Alexv.
  - [ ] **1j. What "flat" means** (AC4): the rule from AC4's notes, its threshold derived from the leak control, the
        warm-up length, and the loop-against-loop check. Run the leak control first.
  - [ ] **1k. Settings: intended against re-serialised** (AC5). List every settings file the chosen culling changes
        on purpose, and how each is set (editor API). Record their pre-story contents (`git show HEAD:<path>`) in the
        evidence folder.

- [ ] **Task 2: The bench, the route, the build menu and the analysis scripts (AC1, AC4, AC5)**
  - [ ] 2.1 `tools/perf/route_build.py` → `tools/perf/route.json` (1b). Draw `evidence/route/route.png` on the
        top-down map.
  - [ ] 2.2 `Assets/Scripts/Bench/` (1c): the asmdef, `PerfBench.cs` (boot and args), and the modes. Keep them small
        and readable; partial classes per mode are fine.
  - [ ] 2.3 `Assets/Scripts/Editor/PerfBenchMenu.cs` (1g): the build menus, `Tools/Perf/Bench in Editor (Play)` (arms
        the bench through SessionState for a smoke run), `Tools/Perf/Census (edit mode)` (1a), and `Tools/Perf/Render
        A/B (edit mode)`. The last is 2.2's ad hoc ABBA, now as code:
        - `camera.Render()` into a 1920 × 1080 RT, each render synced by a 1-pixel `ReadPixels`;
        - a named set of renderers toggled with `forceRenderingOff`, restored in a `finally`;
        - 10 ABBA blocks of 4 renders per state, per pose;
        - a control column (pixels changed > 0).
  - [ ] 2.4 `Assets/Tests/EditMode/PerfBenchFenceTests.cs`: a plain player excludes `CameraGame.Bench`, and with the
        define it appears (the positive control; lessons 30, 34 and 35: use resolved references from
        `CompilationPipeline.GetAssemblies`, never the asmdef text).
  - [ ] 2.5 `tools/perf/`:
        - `bench_run.ps1`: launch an exe windowed at 1080p with the bench args and the external sampler, and wait for
          exit;
        - `procmem.ps1`, if you copy and extend 1.14's;
        - `bench_stats.py`: per-stretch median, p95, p99, 1 % low, share > 16.67, CPU/GPU split, ABBA and
          difference-in-differences across runs, the slice-relative table, and the void-run filter;
        - `stretch_map.py`, `pop_sheet.py` and `soak_report.py`.
        - **Reuse** `tools/verification/recording-spike/sheet.py` and `ssim.py` where they fit; do not rewrite them.
  - [ ] 2.6 **Smoke the bench in the editor** (`Bench in Editor`, a few stretches), then in a throwaway build. Suspect
        the rig before the code (lesson 9):
        - every stretch the same time, or a round number;
        - the drunk never in view;
        - GPU time 0 everywhere;
        - a resolution that is not 1920 × 1080;
        - a pose where FallRecovery fired.
  - [ ] 2.7 `refresh_unity`, `read_console`, and `run_tests` (EditMode). Commit.

- [ ] **Task 3: Baselines on today's world, before anything changes (AC1–AC4)**
  - [ ] 3.1 Record the console baseline, the EditMode count and `git status`. Copy aside 2.2's
        `regression/photo-shoot/` and `regression/photo-shoot-town/`, and today's `_bmad-output/verification/photo-shoot*/`
        folders, into `evidence/baseline/`.
  - [ ] 3.2 Record the settings files' contents (1k).
  - [ ] 3.3 **Build `before`** (1g). Then diff every settings file and restore the re-serialised hunks (lesson 24).
        Record the commit and the build report.
  - [ ] 3.4 Write down in the Dev Agent Record how to rebuild `before` if it is lost (1g).
  - [ ] 3.5 The before build's `census` mode (1a, build half), then `route` ×2 and `capture` ×1. Check the captures
        are real (open several).
  - [ ] 3.6 The editor's `Render A/B` baseline at the over-budget poses, and the AC3 actor-visibility rig on today's
        world (culling off by definition: it is the "before" column).

- [ ] **Task 4: Screen the candidates (AC2, 1d)**
  - [ ] 4.1 Each candidate on its own, in edit mode (the Render A/B), at the over-budget poses. For the pipeline-wide
        ones (the GPU Resident Drawer, GPU occlusion), edit-mode gains understate a build's (Unity manual): carry the
        promising ones to a throwaway build and time a few stretches. Put each setting back after its screen and read it
        back.
  - [ ] 4.2 For each visible candidate (shadows off, cull distances, LOD, fog), capture photographs from the eye at 3
        poses: on and off.
  - [ ] 4.3 Fill the 1d table and choose. Record it in `decisions` with the evidence paths.

- [ ] **Task 5: Apply the chosen culling (AC2, AC3)**
  - [ ] 5.1 `TownCullingConfig` (script, asset, `.meta`) and `TownCulling` on Main Camera, if 1d chose any per-camera
        culling. EditMode tests: `Safe*`, finite checks, `TryGetConfigProblem` (including a config that culls the
        `Subject` layer), and the FOV scaling as a pure function.
  - [ ] 5.2 `RenderRule` and `Tools/Town/Apply Render Rule`, if 1d chose any per-object change. Run it twice to prove
        it is idempotent. Save the scene in its **own** `execute_code` call (memory: an unsaved-changes dialog turns one
        menu call into six), and diff the YAML: only the intended overrides.
  - [ ] 5.3 Pipeline and project settings (the GPU Resident Drawer, BRG Keep All, static batching, GPU occlusion,
        shadows, texture copies) through the editor API. Read each back.
  - [ ] 5.4 Mesh LOD or an Umbra bake, if chosen. Time it. Check the file sizes (< 50 MB). After a reimport, count the
        colliders that still resolve.
  - [ ] 5.5 `refresh_unity`, `read_console`, `run_tests`. Read every new value back **through the editor** before any
        rig (CLAUDE.md: a stale assembly or asset runs with a clean console). Commit.

- [ ] **Task 6: The after build, and the interleaved route A/B (AC1, AC2)**
  - [ ] 6.1 **Build `after`**, then the settings diff and restore as in 3.3.
  - [ ] 6.2 **ABBA ×2 route runs:** `before, after, after, before, before, after, after, before`. Each void run is
        re-run in its slot.
  - [ ] 6.3 `bench_stats.py` → `evidence/route-ab/`:
        - `per_stretch.csv` and `summary.txt`: before and after, the gain, the noise, the verdict;
        - the slice-relative table (1i);
        - the CPU/GPU split;
        - `stretch_gain.png` on the top-down map.
  - [ ] 6.4 **Judge it.** Every stretch meets the laptop target? Any stretch worse beyond the noise? If not met, go back
        to 1a and 4 for that stretch (1i: a miss is a defect, not a human check).

- [ ] **Task 7: Nothing near the player pops (AC2, NFR6)**
  - [ ] 7.1 `capture` mode in `after`, with the same poses as 3.5's before captures. `pop_sheet.py`: pairs, amplified
        diffs, and the culled-object annotations from the editor (1h).
  - [ ] 7.2 The boundary-crossing sequences per culled class, at FOV 60 and 18.
  - [ ] 7.3 **The positive control** (a deliberately near cull) must show up as a pop.
  - [ ] 7.4 **Look at the sheets yourself.** Then a short real-time clip from the after build, walking across the
        nearest cull boundary and zooming there, for Alexv. Use `ffmpeg -f gdigrab -i title="My project"`, or the build
        filming itself if the session is locked (lesson 29).

- [ ] **Task 8: Actors are never culled; no grading regression (AC3)**
  - [ ] 8.1 `Tools/Perf/Actor Visibility (Play)` with culling on against off, plus its positive control.
  - [ ] 8.2 The bench's `actor` mode in the after build: the drunk's pixels at the slice poses, against the before
        build's.
  - [ ] 8.3 The Photo Shoot and the Town Placement Study, copied aside first. Write `evidence/regression/`: unmasked
        star and composition diffs against 2.2's baselines (3.1). Explain every move.

- [ ] **Task 9: The 30-minute session (AC4)**
  - [ ] 9.1 **The leak control first** (1j): `leak` mode for 5 minutes. The flatness rule must flag it.
  - [ ] 9.2 **The soak:** `walk` mode on the after build, ≥ 30 min and ≥ 2 loops, the drunk active, sampled from
        outside every 1 s.
  - [ ] 9.3 `soak_report.py` → `evidence/soak/`: the chart, the slope, loop 2 against loop 1, the object counts, the
        lifecycle count, sticks, FallRecovery restores, and the walking frame times per stretch.

- [ ] **Task 10: Regressions (AC5)**
  - [ ] 10.1 The route trace (≥ 3 cycles), Movement After, the Gallery and HUD rigs. If layers, colliders or static
        flags changed, add 2.2's sprint tour and worst walls.
  - [ ] 10.2 Fail-soft: no `TownCullingConfig`, then an invalid one (NaN, −Infinity, a culled `Subject` layer). Each
        warns **once** and renders with no culling.
  - [ ] 10.3 A normal SampleScene play session with nothing scripted: the console holds only the baseline (the
        Version Control notice, `ThirdPersonCamera` "Head bone not found!" if it still appears, the map's
        large-triangle warnings on reload).
  - [ ] 10.4 The plain build's `Managed/` listing (the fence).

- [ ] **Task 11: The docs (AC5)**
  - [ ] 11.1 `game-architecture.md`:
        - **"Rendering budget and culling (Story 2.3)"** under §Architectural Decisions: what costs a frame here; what
          was chosen and why, with the numbers; the culling rule for new content (which classes are culled, which never
          are, and that any new camera rendering the town carries `TownCulling`); the actor rules; the bench and how to
          run it; and the laptop-relative verdict;
        - the Decision Summary **row 12**;
        - §Directory Structure and §System Location Mapping;
        - the frontmatter `updated:`.
  - [ ] 11.2 The decision log (`_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md`): a
        dated entry for the rendering budget.
  - [ ] 11.3 `deferred-work.md`: dated notes on :66 (kept, why) and :104 (the culling survey). A new entry for Story
        1.15: re-run `tools/perf/bench_run.ps1` with recording on, against NFR1's "including while recording". Note
        anything else this story found and did not fix.
  - [ ] 11.4 `loop/lessons.md`: correct lesson 127's `tools/fps_ab.py` path in place, and return the new lessons in
        the result file.
  - [ ] 11.5 **No `epics.md` edit.**

- [ ] **Task 12: Restore and settings hygiene (AC5)**
  - [ ] 12.1 `run_tests` (EditMode), and report the count. `read_console`, `git status`, `git diff --stat`: only
        intended files. Check every committed file's size (< 50 MB).
  - [ ] 12.2 Every settings file: intended hunks only (1k). Read back every restored value (AC5). SampleScene must not
        be dirty and must hold no rig objects. Clear the SessionState flags. Kill any leftover `My project.exe` and
        `ffmpeg`.

- [ ] **Task 13: Handover. Queue what needs Alexv, and never self-certify it**
  - [ ] 13.1 Put the `human_checks` from Dev Notes §"Human checks to queue" in the result file, with exact paths and
        your own read.
  - [ ] 13.2 Put the `decisions` there too: 1b, 1c, 1d (the choice and its table), 1e, 1g, 1h, 1i, 1j and 1k, and any
        visible change.
  - [ ] 13.3 Story status → `review`. Update `sprint-status.yaml`: the entry, and the **real** `last_updated:` key at
        ~line 40 (lesson 66). No `: ` and no trailing `:` (lesson 13). Check it with `yaml.safe_load`.

## Dev Notes

### What already exists. Read these before writing anything

- **The world** (Story 2.2, `game-architecture.md` §"World bounds and traversal (Story 2.2)", l.274–336):
  - The map is `Assets/Models/game map .fbx` (GUID `ed581352065e66b458bda3bb12f7759e`). In SampleScene it is the
    PrefabInstance `game map`, with `importCameras` and `importLights` 0 and embedded materials
    (`materialImportMode 2`).
  - Its colliders are scene-added MeshColliders keyed to FBX child fileIDs (lesson 79). `ColliderRule`
    (`Assets/Scripts/World/ColliderRule.cs`) decides which renderers get one: no collider for grass, scatter no
    taller than the 0.35 m step, the water, or anything buried below y −1, and a 3 m trunk capsule for pines.
  - `TownEdge` (1,781 rocks, static, Default, a MeshCollider each), `TownSeal` (`Assets/Data/World/TownSeal.asset`,
    one mesh), and `TownBounds` (invisible barriers on layer 9 `WorldBounds`).
  - The NavMesh: `Assets/Scenes/SampleScene/NavMesh-game map.asset` (16.27 MB, bake mask Default + WorldBounds).
  - `TownMapGuardTests` keeps the scene and the NavMesh under 50 MB.
- **The player:**
  - `ThirdPersonController.Teleport(Vector3)` disables the CharacterController, sets the position and re-enables it.
  - `PlayerConfig.asset`: walk 4, sprint 7, eye 1.65, nudge 0.316, near 0.095, wall margin 0.03.
  - `ThirdPersonCamera` has `[DefaultExecutionOrder(-100)]` and sets the near clip in `Awake`.
  - Main Camera is the only camera and the only `AudioListener`: far 1000, FOV 60.
  - `PhotoModeController.SetPhotoMode(bool)` / `SetZoom(float)` (`:263`, `:327`) are the real raise and zoom. FOV
    eases toward its target every frame (`:225–235`), so read the **live** `fieldOfView`, never the target.
  - `FallRecovery` is on the player; read restores from `LastFrom` / `LastRecoveryTime` (lessons 93 and 130).
- **The drunk:**
  - `EventManager` with `SpawnPoint` ≈ (31.8, 0.55, 19), "the pub". The route runs from x 28.8 → 25.9 (peak) → 22.8 →
    20.4 at z 19.
  - `Assets/Data/Events/TownDrunk.asset`: phases 3 / 10 / 1.5 / 6 / 4 s, cue radius 25 m, `maxConcurrent` 1.
  - The prefab `EventActor_Drunk.prefab`: layer 8 `Subject`, `m_CullingMode: 0`, `m_UpdateWhenOffscreen: 1`.
  - `EventActor.Bounds` is at `EventActor.cs:124–141`; `_renderers = GetComponentsInChildren<Renderer>(true)` is at
    `:234`.
- **The grader:** `ShotGrader` (`Assets/Scripts/Grading/ShotGrader.cs`): the frustum gate `:283–330`,
  `VisibleFraction` `:639–664` (physics, `occluderMask` = Default). `GradingConfig.asset` is untouched by this story.
- **Rigs to reuse.** Copy their patterns; do not edit them.

  | Menu path | Files | Use it for |
  |---|---|---|
  | `Tools/Recording Spike/Build Spike Player` | `Assets/Spikes/RecordingSpike/Editor/RecordingSpikeMenu.cs:24–67` | The build menu: `BuildPlayerOptions`, `extraScriptingDefines`, the target refusal, defines before and after, the report |
  | (spike harness) | `Assets/Spikes/RecordingSpike/SpikeHarness.cs`, `SpikeArgs.cs:51–104` | Boot by command-line arg or an expiring SessionState flag; preallocated per-frame arrays; `ProfilerRecorder("Main Thread")` (valid in release, but it equals `dt`, so it does **not** split CPU from GPU); quit on done or timeout |
  | — | `Assets/Tests/EditMode/RecordingSpikeFenceTests.cs:67` | The fence test with its positive control |
  | — | `tools/verification/recording-spike/procmem.ps1`, `run.sh`, `battery.sh`, `stats.py`, `sheet.py`, `ssim.py` | The external sampler (newest process); launching at 1080p; rotated rounds; per-phase stats; contact sheets; image diffs |
  | `Tools/Town/Walk the Whole Town - sprint (Play)` | `Assets/Scripts/World/TownRigRunner.Tour.cs` (+ `.Paths.cs` `Chain` `:64`) | The real-input drive (Keyboard state events, yaw "the mouse's job" at `:65/:177`). Do **not** copy its line that switches the drunk off (`:45/:99`) |
  | `Tools/Town/FPS Tour - before/after (Play)` | `Editor/TownRig.cs:55–56`, `World/TownRigRunner.cs:243–297` | 2.2's editor tour, for continuity only: its noise was 17–75 ms |
  | `Tools/Grading/Photo Shoot (Play)` and `— Placement Study (Town)` | `Editor/PhotoShootRig.cs:83/:163` + `PhotoMode/PhotoShootRunner.cs` | AC3's no-regression. They **wipe** their output folders |
  | `Tools/Events/Trace Actor Route (Play)` | `Editor/RouteTraceRig.cs` + `Events/RouteTraceRunner.cs` | AC5's pooled cycles |
  | `Tools/Scale/Movement After (Play)` | `Editor/ScaleStudyRig.cs` + `Player/ScaleStudyRunner.cs` | AC5's movement regression; the scene-restore pattern (`:51–141`) |
  | `Tools/HUD/Grade HUD Shoot (Play)` | `GradeHudShootRunner.cs` (`VisiblePixelFraction`) | "Visible by rendered pixels", the idea behind AC3's rig |
  | `Tools/Town/Census (edit mode)` | `Editor/TownCensus.cs` | The top-down render (`topdown.png`, 0.5 m/px, x −700…300, z −450…650) and the census grids |

  **New rigs** go in `Tools/Perf/*`. Their editor halves go in `Assets/Scripts/Editor/`, and the play-mode runners in
  the **runtime** assembly behind `#if UNITY_EDITOR` (CLAUDE.md trap: an editor-assembly MonoBehaviour is "missing" in
  play mode). The **bench** is the exception: it must run in a build, so it lives in its own define-fenced assembly.
- **Logging:** `GameLog.Info/Warn/Error`. `read_console` hides `Debug.Log`, so grep
  `%LOCALAPPDATA%\Unity\Editor\Editor.log` and the build's `player.log`.
- **Tests:** `Assets/Tests/EditMode/` (`CameraGame.Tests.EditMode.asmdef` references `CameraGame` only; add
  `CameraGame.Editor` only if a pure rule must live there, and record it). There is no PlayMode assembly. 254 at the
  last run.
- **Tooling:** ffmpeg/ffprobe 8.0.1 (a GPL build; fine for evidence, never for shipping, lesson 20), numpy and Pillow.
  Read `tools/verification/README.md` first. Gemini adds nothing to this story: pop-in is settled by per-pose diffs.
  Use it only if a temporal question arises, under its prompt rules.

### Candidates: what each touches. Measure before relying on any of this

| Candidate | What it attacks | What it touches | Known risks and costs |
|---|---|---|---|
| The buried set's renderers off | GPU and CPU: 663 renderers / ~0.57 M triangles nobody can see | `renderer.enabled` (or `shadowCastingMode` too) on those children, as scene overrides by `RenderRule` | Invisible by construction, but prove it from the slice and the hills. They are Alexv's art: disable, never delete, and record it so a future map that brings them to the surface can turn them back on |
| Opaque/depth texture copies off | GPU: two full-screen copies at 1080p | `PC_RPAsset` `m_RequireOpaqueTexture`, `m_RequireDepthTexture` | Breaks any shader that samples them. Find the readers first (1a). SSAO needs depth (normals) whatever this flag says |
| SSAO cheaper or off | GPU: a full-resolution depth-normals pass (all of the town's geometry again) plus the AO pass | `PC_Renderer.asset` SSAO feature (`Downsample`, `Source`, `Samples`, or active) | Visible: contact shading under pines and buildings. Photograph on and off |
| GPU Resident Drawer (Instanced Drawing) | CPU: per-renderer submission. Fits the hill lead (fact 4) if the scatter shares meshes (`TownEdge` does: one mesh, one material) | `PC_RPAsset` `m_GPUResidentDrawerMode`; `GraphicsSettings` BRG Variants **Keep All**; maybe `PlayerSettings` static batching off | MeshRenderers only (the drunk's `SkinnedMeshRenderer` falls back). Must work on **D3D11** on the MX350 (fact 3): it needs compute, which D3D11 has, but prove it in the build (the Frame Debugger shows "Hybrid Batch Group", or the CPU time drops). Longer builds (every BRG variant compiled). With Forward+ it turns on **Probe Atlas Blending**, which can change the lighting: diff the pictures. Verify the RT-render rigs still give non-blank images |
| GPU occlusion culling | GPU and CPU: hidden instances | the active Universal Renderer's GPU Occlusion, and `PC_RPAsset` `m_GPUResidentDrawerEnableOcclusionCullingInCameras` | Needs the GPU Resident Drawer. D3D11 support is unverified: check it in the build. Uses this frame's and the previous frame's depth. "If occlusion culling doesn't have a big effect … rendering time might increase" (Unity manual). Little to hide on an open plain seen from a hill |
| Shadow work | GPU and CPU: the shadow pass (4 cascades, 2048, 50 m) | `PC_RPAsset` shadow settings; per-object `shadowCastingMode` by `RenderRule` | Visible. Photograph it. Grass shadows may be part of the look |
| Per-layer cull distances | CPU and GPU: far small objects | a new layer for a collider-less class; `TownCulling` + `TownCullingConfig` | Only collider-less objects may change layer (1e). FOV scaling is required (zoom). The 2 px rule (1h) allows little. **Never** pines, rocks, buildings, terrain, landmarks or actors: they occlude subjects, frame vantage shots (Story 2.5) and "telegraph at a distance" (GDD) |
| Mesh LOD (on import) | GPU: triangles far away (the pines are 2.0 M) | the map FBX's `.meta`: `generateMeshLods: 0`, `maximumMeshLod: -1` today (l.59–61). Set it through `ModelImporter` (reflect the property name) and `SaveAndReimport()` | A full reimport of the 77 MB map, then count the resolving colliders. LODs are stored in the mesh's index buffer, and selection is by screen size. **Static batching and GPU instancing always draw LOD0.** Cross-fade needs the GPU Resident Drawer. Not with an `LODGroup`. Never on skinned meshes. A LOD swap is a pop under 1h |
| `LODGroup` (cull-only LOD) | CPU and GPU: screen-size culling, which is FOV-aware | a component per object (≈ 2–4 MB of scene overrides per 8,000) | The CPU cost of LOD selection over thousands of groups. The pop rule applies |
| Umbra occlusion bake | CPU and GPU: what is hidden behind static occluders | static flags on the map (scene overrides or a postprocessor), and `OcclusionCullingData` | Bake time over 3 × 3.7 km, and the data size (< 50 MB per file). Little occludes on the plain; the street block maybe. Dynamic actors can be occludees (AC3) |
| Static batching | CPU: draw calls | static flags plus Player Settings | Duplicates vertex data (memory: an AC4 baseline concern). Conflicts with Mesh LOD (always LOD0) and possibly with the GPU Resident Drawer |
| Far clip or fog | everything beyond a distance | Main Camera far clip, `RenderSettings` fog | Changes the look; the ring frames the town, and vantage views are Story 2.5's point. Only as a decision for Alexv, with photographs |

**Out of scope:** decimating or editing Alexv's map in Blender. The map is his art: never modify the FBX's contents.
Import settings in its `.meta` are allowed, recorded and reversible.

### The genuinely new risks in this story

1. **The instrument is the hard part.** The laptop heats, frames vary 2× within a day (lesson 127), Optimus can switch
   GPUs, OneDrive syncs the project folder after big writes, and the session may lock.
   - Interleave everything, and void runs that broke a condition.
   - Before a timing battery: close what you can, check free RAM, and note OneDrive's activity.
   - Never film or capture during a timed run.
2. **The build re-serialises settings that this story also changes on purpose** (AC5 notes, 1k). Diff after every
   build. Keep intended hunks, and restore the rest.
3. **A global pipeline change touches every picture in the project.** The GPU Resident Drawer, probe blending and
   shadows reach every rig's captures and every future clip. Re-run the image rigs after the change (lesson 43) and
   diff their pictures, not just their verdicts.
4. **Per-camera culling is invisible to cameras that do not carry it** (fact 7). Rigs with their own cameras will show
   an unculled town. That is fine for their purpose. But never use one as evidence that "the culling shows nothing":
   use Main Camera.
5. **Zoom magnifies.** Anything decided by distance must be judged at FOV 18 too (AC2).
6. **Long, unattended runs.** A 30-minute soak plus ABBA batteries.
   - Use `run_in_background` and Monitor until-loops on the bench's `summary.json` or the process exit.
   - Kill leftovers (`My project.exe`, `ffmpeg`) at the end.
   - MCP drops for 10–60 s after play mode (lessons 59, 73), and answers "No Unity Editor instances found" while busy
     (lesson 128). Check `Get-Process Unity` and re-send; never assume the call ran.
7. **Big diffs.** The scene grows with per-object overrides; occlusion data or a reimport can be large. Check sizes
   before committing.

### Architecture compliance (non-negotiable)

- **§Configuration:** tunables in ScriptableObjects in `Assets/Data/`, invariants in static classes. **No code
  literals** for cull distances, FOVs or thresholds in shipped code. The bench's own constants (settle frames, timed
  frames) are tooling and may be literals, documented in `route.json` or the bench.
- **`CameraGame.*` namespaces and one game asmdef** (AR5). `TownCulling`, `TownCullingConfig` and `RenderRule` go in
  `Scripts/World/` (namespace `CameraGame.World`). World → Player is allowed; Player → World is not. `TownCulling`
  reads its own camera's FOV and never references `PhotoModeController`.
- **The bench is tooling, fenced like 1.14's spike:** a define-constrained assembly, `autoReferenced: false`, nothing in
  the game references it, and a fence test. Debug code is behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` in shipped
  assemblies (§Consistency Rules). The bench's assembly is excluded by its define instead.
- **ADR-3: events are data.** No drunk-specific culling logic. The rule is **the `Subject` layer is never culled**,
  which covers every future event actor (Epic 3) for free. Write it down that way.
- **ADR-4 (Proposed):** what the player sees should be what a clip shows. A replay re-render camera must carry the same
  `TownCulling` (note it for Story 1.15). ADR-4 risk: restore `forceRenderingOff` symmetrically.
- **ADR-5's scale rule:** never scale a root carrying a `CharacterController`, `NavMeshAgent` or `AudioSource`. Nothing
  here should scale anything.
- **The map FBX is Alexv's art.** Never modify its contents. Import settings in its `.meta` are allowed, recorded and
  reversible.
- **Fail-soft (NFR8) and pooling (NFR3)** as in AC5. **URP only (NFR4).**
- **No new packages.** Memory Profiler, Profiling Core and Entities Graphics are not installed
  (`Packages/manifest.json`). The bench needs only built-in APIs: `ProfilerRecorder`, `FrameTimingManager`,
  `UnityEngine.Profiling.Profiler`. Adding a package is a decision to record, and none should be needed.
- **GDD:** subjects and landmarks must "telegraph at a distance" (`gdd.md:342`), and vantage points exist to see far
  (Story 2.5). Culling never removes what frames or hides a subject.

### Project Structure Notes

- **New:**
  - `Assets/Scripts/Bench/` (+ `.meta`): `CameraGame.Bench.asmdef`, `PerfBench.cs` and its mode files;
  - `Assets/Scripts/World/`: `TownCulling.cs`, `TownCullingConfig.cs`, `RenderRule.cs` (as 1d/1e decide), and
    `ActorVisibilityRunner.cs` (behind `#if UNITY_EDITOR`);
  - `Assets/Data/World/TownCullingConfig.asset` (+ `.meta`);
  - `Assets/Scripts/Editor/`: `PerfBenchMenu.cs`, plus the render-rule tool and the actor-visibility rig (or in
    `PerfBenchMenu.cs`);
  - `Assets/Tests/EditMode/`: `PerfBenchFenceTests.cs`, `TownCullingConfigTests.cs`, `RenderRuleTests.cs`;
  - `tools/perf/`: `route_build.py`, `route.json`, `bench_run.ps1`, `procmem.ps1` (if extended), `bench_stats.py`,
    `stretch_map.py`, `pop_sheet.py`, `soak_report.py` and a `README.md` (how to run the bench, for Alexv and for Story
    1.15);
  - `.meta` files for everything under `Assets/`.
- **Updated (as chosen):** `Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset`,
  `ProjectSettings/GraphicsSettings.asset`, `ProjectSettings.asset`, `TagManager.asset`, `SampleScene.unity`
  (`TownCulling` on Main Camera, per-object overrides) and `game map .fbx.meta` (Mesh LOD); plus `game-architecture.md`,
  the decision log, `deferred-work.md`, `loop/lessons.md`, `sprint-status.yaml` and this story file.
- **Not touched:** see AC5's "Untouched" list. `Mobile_RPAsset`, `loop/dependencies.yaml`, `loop/runtime/`, and the
  shared folder.
- **Outside git:**
  - evidence in `_bmad-output/verification/2-3-the-whole-town-holds-its-frame-rate/`, with suggested subfolders
    `census/`, `route/`, `screen/`, `route-ab/`, `capture/before|after/`, `pop/`, `actor/`, `regression/`,
    `soak/`, `leak-control/`, `settings/` and `baseline/`;
  - builds in `Builds/2-3-the-whole-town-holds-its-frame-rate/{before,after,plain,scratch}/`;
  - **never `Temp/`.**

### Previous Story Intelligence (2.2 above all; 1.14, 2.1, 1.9–1.12)

- **2.2** built the world this story measures, and an editor frame-time tour. Its lessons for an instrument:
  - play-mode editor timing is too noisy here (17–75 ms);
  - the edit-mode render A/B gets to ~1 ms;
  - round 0 after a scene load is slow in every state, so drop it or interleave it;
  - its tools were never committed.

  Its rig lessons:
  - re-run every rig that walks through new geometry;
  - read a corrector's own record (`FallRecovery.LastFrom`), not the state after `Update` (lessons 93, 130);
  - copy evidence aside before a rig wipes it;
  - SessionState options outlive a session;
  - objects staged right after `manage_editor play` can land mid-transition.
- **1.14** is the model for measuring in a build on this laptop:
  - a release build, windowed 1920 × 1080, the MX350 picked, mains power;
  - difference-in-differences with rotated rounds (the view and the heat both move the cost);
  - the external memory sampler;
  - the settings re-serialisation trap;
  - "No gdigrab, Recorder or other capture during measured runs";
  - "Do not lower quality settings, resolution or render scale to manufacture a 60".

  Its deferred method limits apply here too. Three rounds cannot balance four positions, and a battery may have run on
  a locked workstation. Use ABBA, and detect the lock.
- **2.1 (ADR-5):** the eye, near clip and speeds all come from `PlayerConfig`. Never hard-code an eye height (lesson 64).
  Measure arrival with the agent's own test (lesson 68).
- **1.9–1.12:** the Photo Shoot is the no-regression instrument. The town occludes through trees in ways rays miss, so
  measure visibility by rendering. 1.12 retracted a wrong diagnosis made from a probe taken at the wrong instant:
  measure at the moment that matters.

### Inherited context you should not rediscover

- **CLAUDE.md traps:**
  - an editor-assembly MonoBehaviour is "missing" in play mode;
  - render to an RT, or `CaptureScreenshotAsTexture` after `WaitForEndOfFrame` with the camera rendering to the screen
    for overlay UI;
  - assets loaded before `NewScene(Single)` lose their native half;
  - ProjectSettings edits on disk do not apply while the editor runs;
  - an edit on disk is invisible until Unity reimports it: `refresh_unity`, wait, read back, and check
    `mcpforunity://editor/state` `assets.external_changes_dirty`;
  - `Temp/` is deleted on shutdown;
  - disabling a manager does not empty its world;
  - capture feedback is photographed at the wrong instant by default.
- **Do not be misled by stale settings copies.** `ProjectSettings/*.private.0` files from February 2026 (for example
  `QualitySettings.asset.private.0`, with 6 default levels) are not used by Unity. Read the real `.asset` files, or
  better, the editor API.
- **Builds:** the loop rules say to build with the Unity MCP build tool. This story needs `extraScriptingDefines`, so
  build through `Tools/Perf/Build Bench Player (…)` with `execute_menu_item`, as 1.14 did, and record that.
- **Memory notes:**
  - `read_console` hides `Debug.Log`;
  - never run asset operations in play mode;
  - save a scene in its own call before a rig;
  - create SO assets as YAML from the `.meta` GUID;
  - code review excludes test rigs;
  - the MCP Play-mode gotchas.
- **Loop lessons:**
  - 9: a blank or uniform capture means a broken rig;
  - 10: read back after a refresh;
  - 12: the second cycle;
  - 13 and 66: the `sprint-status` YAML traps;
  - 19, 28 and 127: measure on this laptop as an interleaved delta;
  - 20: GPL ffmpeg;
  - 23, 30, 34 and 35: `extraScriptingDefines`, the fence check and its positive control, `GetAssemblies`;
  - 24: build re-serialisation;
  - 26: memory from outside;
  - 29: the locked workstation;
  - 37: SessionState flags expire;
  - 41 and 42: unmasked star diffs, and `i_from_behind` noise;
  - 43: re-run boot-time rigs;
  - 50: scope your greps (a recursive grep from the root times out);
  - 65: CRLF files (`newline=''`);
  - 72: finite checks;
  - 75: `UnityEngine.Object.FindObjectsByType` in `execute_code`;
  - 79 and 83: count the resolving colliders after a reimport;
  - 99: an external-scene-change modal after a master merge;
  - 93 and 130: read FallRecovery's own record, not the state after `Update`;
  - 122: long `execute_code` calls keep running;
  - 128: MCP drops calls while Unity is busy;
  - 131: the ABBA render A/B.

### Git Intelligence

- `HEAD` is `7eca20d` on `loop/2-3-the-whole-town-holds-its-frame-rate`. The last commits merged Story 2.2 (CLOSE
  attempt 4: the edge rocks, the seal, two re-bakes) and added its lessons. The world described above is current.
- The working tree is clean apart from the untracked `.codex/` and `AGENTS.md`. They are not yours, so do not stage
  them. **Stage by name** (`git add <path> <path>.meta`).
- Commit as you go, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never switch
  branches, merge, rebase, reset, stash or push.

### Latest technical information (Unity 6000.3 / URP 17.3; verify each name with `unity_reflect` before use)

- **GPU Resident Drawer** (Unity 6000.3 manual, "GPU Resident Drawer"):
  - **Requires:** Forward+ ("works only with … the Forward+ rendering path"), SRP Batcher on, Project Settings >
    Graphics > Shader Stripping > **BatchRendererGroup Variants: Keep All**, and the URP asset's GPU Resident Drawer
    set to **Instanced Drawing**. "Graphics APIs and platforms that support compute shaders, except OpenGL ES."
  - **Draws:** only GameObjects with a Mesh Renderer; anything else "falls back to drawing … without GPU instancing".
  - **Static batching:** disabling it "might speed up the GPU Resident Drawer".
  - **Builds:** "Build times are longer because Unity compiles all the BatchRendererGroup shader variants."
  - **Editor timing:** "Rendering usually speeds up less in the Scene view and the Game view, compared to Play mode or
    your final built project."
  - **Lighting:** "Probe Atlas Blending is used by default when both the Forward+ rendering path and the GPU Resident
    Drawer are used."
  - **To check it works:** the Frame Debugger shows draw calls named **Hybrid Batch Group**.
- **GPU occlusion culling** (URP manual, "Enable GPU occlusion culling in URP"):
  - Enable the GPU Resident Drawer, then turn on GPU Occlusion on the active Universal Renderer.
  - It culls with this frame's and the previous frame's depth.
  - It can cost more than it saves where little is occluded.
- **Mesh LOD** (Unity 6000.3 manual, "Introduction to Mesh LOD"):
  - "creates LODs automatically on model import and stores each LOD in the index buffer of the original mesh".
  - Selection is "depending on the size of the mesh on the screen, project-wide and per-object Mesh LOD settings".
  - Static batching and GPU instancing "always select LOD0". Cross-fade "requires … GPU Resident Drawer".
  - "Using Mesh LOD with LOD Group … is not recommended." Skinned meshes still deform LOD0.
  - The import-setting and QualitySettings names are on its sub-pages. Reflect them.
- **FrameTimingManager** (6000.3 Scripting API): "always active in Development Player builds. To use this feature in
  release Player builds … enable the **Frame Timing Stats** property".
  - "When you use ProfilerRecorder … FrameTimingManager is enabled automatically including in release builds."
  - Results come "with a fixed delay of four frames".
  - GPU timings "are reported as zero" when the GPU cannot return them.
  - `CaptureFrameTimings()` + `GetLatestTimings(count, array)`.
- **ProfilerRecorder** works in release players. Check `.Valid` for each counter, and use
  `ProfilerRecorderHandle.GetAvailable` to list what a release player offers. Older manuals mark SetPass Calls,
  Triangles and Vertices Count as available in release, and the batching counters as not. Verify on 6000.3 at
  runtime.
- **`Camera.layerCullDistances`:** 32 floats, where 0 means the far clip; `layerCullSpherical` uses distance rather
  than depth. The setter copies, and the getter allocates.
- **Builds:** `BuildPlayerOptions.extraScriptingDefines` adds a define to one build without touching `PlayerSettings`
  (lesson 23). Use `-screen-width/-screen-height/-screen-fullscreen` and `-logFile` on the command line.

### Verification plan: what would convince a sceptic

| Claim | Evidence | How it is judged |
|---|---|---|
| The instrument measures the game at 1080p on the MX350 | `route-ab/*/summary.json`, `player.log` | 1920 × 1080, MX350/D3D11, PC, vsync 0, cap −1, mains power, unlocked; void runs named |
| The instrument can tell builds apart and has a known noise | `route-ab/summary.txt` | within-build run spread per stretch (A/A); ABBA order recorded |
| We know where the time goes | `census/` | per stretch, CPU main / render / GPU and the counters; "CPU-bound on X" or "GPU-bound on Y" with numbers |
| The culling was chosen by measuring | `screen/decision_table.md`, Render A/B CSVs, photos | every candidate measured on its own, chosen or rejected with numbers |
| The gain shows per stretch | `route-ab/per_stretch.csv`, `stretch_gain.png` | gain beyond the noise where needed; no stretch worse beyond the noise |
| The laptop target is met | slice-relative table | every stretch's p95 ≤ the slice's, within the noise; the absolute numbers beside it |
| Nothing near the player pops | `pop/sheet_*.jpg`, `pop/changed_objects.csv`, crossings, control | 0 changed objects ≥ 2 px at FOV 60 and 18; the control flagged |
| Actors are never culled | `actor/actor.csv`, sheet, control | his pixels with culling on = off at every pose; the control drops to 0 |
| The grader is unchanged | `regression/photo-shoot-diff.txt`, `…-town-diff.txt` | no star or composition move beyond the known noise; every move traced |
| Memory stays flat over 30 minutes | `soak/soak.png`, `soak/report.txt`, `leak-control/` | the control flagged by the same rule; the soak's slope and loop-against-loop under it; object counts flat; ≥ 2 loops; ~68 lifecycles |
| Epic 1 and 2.2 still work | route trace, Movement After, Gallery and HUD (non-blank) | the same numbers as 2.2's close, within its noise |
| The fence holds | `PerfBenchFenceTests`, the plain build's `Managed/` listing | no `CameraGame.Bench.dll` in a plain build |
| The project is restored | `git status`, the settings diffs, read-backs | intended hunks only; no rig objects; flags cleared |

**Fail-soft scenarios to provoke on purpose.** Say so at the top of the rig's output and name each one:
- no `TownCullingConfig`, and an invalid one;
- the pop-sheet control (a near cull);
- the actor-visibility control (his layer culled);
- the leak control.

Each must log **once** and keep running. Exclude them, by name, from the clean-console check.

### Human checks to queue (never self-certify)

1. **AC1, the absolute 60 FPS on NFR1's desktop.**
   - **What Alexv does:** copy `Builds/2-3-…/after/` and `tools/perf/` to a mid-range Windows desktop and run
     `tools/perf/bench_run.ps1 -Mode route` (about N minutes, measured). Then read `summary.txt`: per stretch, p95
     against 16.67 ms. Or say this laptop is all there is.
   - **Evidence:** the laptop's own route A/B and its slice-relative table.
2. **AC2, does anything pop?** Watch the clip walking across the nearest cull boundary and zooming there, and scan the
   worst-case contact sheet. Does any change read as something popping in or out?
3. **Any visible change the chosen culling makes** (shadows, probe blending, a LOD look, fog if it was ever chosen):
   before and after photographs from the eye. Is the town's look still right?
4. **Decision batch, not a check:** 1b, 1c, 1d (the choice and its table), 1e, 1g, 1h, 1i, 1j and 1k.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.3; #Epic 2: The Living Town] — the ACs (verbatim above), the planning preamble, the sequencing
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — NFR1 (incl. "while recording"), NFR3, NFR5, NFR6, NFR8; FR14 (vantage points)
- [Source: _bmad-output/planning-artifacts/game-architecture.md l.23–24, l.100–104 (targets), l.181/l.202–203 (grader), l.274–336 (World bounds and traversal), l.361–537 (ADR-4 incl. risks l.496–522), l.539–627 (ADR-5), l.687–693 (Configuration), l.753–761 / l.820–822 (structure, boundaries), l.1007–1014 (consistency rules)]
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md:339–342 (art style, "telegraph at a distance"), :354–359 (performance), :404–406 (success metrics)]
- [Source: _bmad-output/implementation-artifacts/2-2-walk-the-whole-town.md:293–337 (AC5 frame rate), :532–538 (the 1k instrument), :1235–1248 (the render A/B), :1615–1618 (census classes); its File List and Dev Agent Record]
- [Source: _bmad-output/implementation-artifacts/1-14-decide-how-the-game-records-video.md:51–56, :205–234, :1205–1231, :1295–1303 (build, method, numbers, settings trap)]
- [Source: _bmad-output/implementation-artifacts/2-1-settle-the-players-scale-in-the-town.md (ADR-5 numbers, rigs, PlayerConfig)]
- [Source: _bmad-output/implementation-artifacts/deferred-work.md :66, :68, :103, :104, :115–201, :305–308, :335]
- [Source: _bmad-output/verification/2-2-walk-the-whole-town/ (fps/, close-a4/render-ab/, close-a4/walk/sprint/trace.csv, census/parts.json, census/topdown.png + topdown.json, regression/)]
- [Source: _bmad-output/verification/1-14-decide-how-the-game-records-video/measure/analysis.json]
- [Source: Assets/Spikes/RecordingSpike/Editor/RecordingSpikeMenu.cs:24–67; SpikeHarness.cs; SpikeArgs.cs:51–104; Assets/Tests/EditMode/RecordingSpikeFenceTests.cs; tools/verification/recording-spike/]
- [Source: Assets/Scripts/Grading/ShotGrader.cs:283–330, :639–664; Events/EventActor.cs:124–141, :234; PhotoMode/PhotoModeController.cs:225–235, :263, :327; Editor/PhotoShootRig.cs:219–225, :342; World/TownRigRunner.Tour.cs:45, :65, :99, :177]
- [Source: Assets/Settings/PC_RPAsset.asset; PC_Renderer.asset; ProjectSettings/{GraphicsSettings,QualitySettings,ProjectSettings,TagManager}.asset; Assets/Scenes/SampleScene.unity:12, l.62012–62028 (Main Camera)]
- [Source: Unity 6000.3 manual — GPU Resident Drawer; Enable GPU occlusion culling in URP; Introduction to Mesh LOD; FrameTimingManager (docs.unity.com/en-us/engine/6000.3/…)]
- [Source: CLAUDE.md#Verifying Your Own Work; tools/verification/README.md; loop/lessons.md; loop/dependencies.yaml]

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Completion Notes List

- 2026-11-12: Ultimate context engine analysis completed - comprehensive developer guide created (loop CREATE phase).

### File List

## Change Log

- 2026-11-12: Story file created by the loop CREATE phase (Claude Opus 5.5). Status → ready-for-dev.
