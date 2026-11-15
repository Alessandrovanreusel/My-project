# Lessons for loop sessions

Every loop session reads this file first. The orchestrator appends the `lessons` each phase returns
when its unit merges, and each epic's retrospective adds more. Keep entries to one line: a pitfall,
or a pattern that worked, concrete enough to act on.

## Seeded 2026-11-10, from Epic 1 (before the loop existed)

- A rig that reports every case identical, every case passing, a round number, or a blank or white capture is broken. Fix the rig before believing the result.
- Unity only sees an edit on disk after it reimports: `refresh_unity`, wait for the compile, and read the value back through the editor before you trigger a rig.
- Overlay UI is invisible to `cam.Render()`. Use `ScreenCapture.CaptureScreenshotAsTexture()` after `WaitForEndOfFrame`, with the camera rendering to the screen.
- Bugs in this project have hidden in reuse and in the second cycle, never the first. Run pooled objects through several cycles.
- `sprint-status.yaml` breaks if `last_updated` contains `: ` or a line ending in `:`. Verify it with `yaml.safe_load` after every edit.
- Gemini fabricates when it is given timestamps or the expected defect. Send short clips with the subject in every frame, ask descriptive questions, and verify every claim on the frame.
- Disabling a manager does not empty its world. To photograph an empty street, deactivate the live actors.

## From 1-14-decide-how-the-game-records-video (2026-11-10)

- This dev machine is a laptop (i7-1165G7, Iris Xe + MX350 with no NVENC, 15.7 GB RAM), not NFR1's mid-range desktop. Measure FPS as a delta against an interleaved baseline in a release build, and flag the absolute 60 FPS verdict for Alexv.
- The local ffmpeg (chocolatey, gyan.dev 8.0.1 essentials) is a GPL build with libx264. It is fine for evidence and measurement, never a shipping encoder configuration. Use h264_mf/h264_qsv, or an LGPL build.
- UnityEngine.AudioRenderer mutes the live output, so it can never capture gameplay audio. Tap OnAudioFilterRead on the AudioListener's GameObject (Main Camera in SampleScene); AudioSources with bypassListenerEffects=true are not captured.
- ffmpeg gdigrab films video only, with no audio. Prove in-build audio playback with an OnAudioFilterRead tap and a cross-correlation against the clip's audio.
- BuildPlayerOptions.extraScriptingDefines adds a define to one build without touching PlayerSettings. Use it with an asmdef defineConstraint to fence spike code, and prove the fence by listing a plain build's Managed/ folder.
- The first Windows build re-serialises settings: PC_RPAsset prefilter flags, URP global settings, ProjectSettings preloadedAssets/batching and UnityConnectSettings. Restore them from HEAD and reimport after every build story.
- Mono's Process.StandardInput starves an ffmpeg child at ~6 fps for 1080p RGBA; loopback TCP (ffmpeg -i tcp://127.0.0.1:PORT?listen=1) keeps real time.
- Process.PrivateMemorySize64 reads 0 in this Mono player: sample process memory from outside (PowerShell Get-Process) and add Profiler.GetTotalReservedMemoryLong.
- On this laptop the Intel Quick Sync encoder (h264_mf and h264_qsv) drops from ~68 fps to ~6 fps while the game renders; test any live encoder WITH the game running.
- Frame cost follows what the tracking camera looks at (up to 4x across the drunk's cycle) and the laptop heats run to run: compare candidates by difference-in-differences against a baseline run of the same segment, with rounds rotated.
- When the workstation is locked, gdigrab fails with 'error 8'; a build can film itself with ScreenCapture.CaptureScreenshotAsTexture after WaitForEndOfFrame.
- C# inlines consts, so an assembly using only a const from another emits no reference: a reflection-based reference check needs a positive control that uses real types.
- h264_mf gives no extradata in time for an MP4 header (and +empty_moov makes it unreadable): record MPEG-TS and remux; TS also survives a killed encoder.
- Replay re-render timing: trim and join by output-frame counts (tick repeat sums), never by tick indices; below 30 fps a tick covers several frames.
- ~~The world is ~5x human scale (player CharacterController 8.86 m): put rig cameras at ~7.5 m eye height.~~ **Superseded 2026-11-11 by ADR-5 (Story 2.1):** the characters are now people, 1.77 m tall with the eye at 1.65 m, in a town of roughly real metres. Rigs read the eye height and near clip from `Assets/Data/Player/PlayerConfig.asset`; never hard-code one.
- A fence test that searches .asmdef text for an assembly name misses 'GUID:' references (the asmdef Inspector's default); ask CompilationPipeline.GetAssemblies for resolved references instead.
- CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies) honours define constraints under the current Player Settings defines: a cheap structural proof that a fenced assembly stays out of a plain build (positive control: add the define and it appears).
- PlayerSettings.SetScriptingDefineSymbols(target, "") leaves 'Standalone: ' in ProjectSettings.asset instead of {}; to restore byte-identically, ClearArray 'scriptingDefineSymbols' on SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings")).
- A SessionState arming flag for a play-mode rig outlives a refused EnterPlaymode and arms the NEXT normal Play press; stamp it with EditorApplication.timeSinceStartup and expire it.
- When a fidelity or diff metric reads exactly 0 everywhere, add a shifted control (ghost at tick i vs live at tick i-1) so the zero is proven real; here the control read 44-71 cm.
- ffmpeg -shortest silently truncates video when the PCM is a DSP block short (301 -> 298 frames); pad or trim the audio to the video timeline instead.
- The loop guard blocks the Write tool outside the project, including AppData Temp scratch folders; put scratch under a git-ignored project folder, or edit in place.
- Masking digits in a Photo Shoot shots.txt diff also masks the star ratings, so '0 masked differences' can hide a 3-star -> 1-star move. Diff per-shot stars and the composition axis unmasked as well.
- Photo Shoot framing shots a_close..i_from_behind fire on a real-time schedule, not anchored to the peak. Their PeakOffset drifts 0.03-0.4 s per run, and i_from_behind (1.3-1.7 s after the peak, on the steep timing tail) swings 1-3 stars from that alone.
- When a review changes code that boots in every Editor play session (RuntimeInitializeOnLoadMethod in an Editor-compiled spike), an earlier no-regression rig run no longer covers the merged code. Re-run it at close.

## From plan-epic-2 (2026-11-10)

- Read-only questions about the 214 MB town .blend: run headless Blender (blender.exe -b --factory-startup <file> --python <script> -- <args>). It loads in ~2-6 s, renders an orthographic top-down with Workbench in ~10 s, and never touches Alexv's live Blender session.
- Assets/Models/game map .fbx is a complete export of the shared folder's game map .blend (16,738 meshes, 5,677,935 tris, same bounds). 15,080 of those meshes are particle instances that exist only after depsgraph evaluation, so compare FBX counts against evaluated object_instances, never against bpy.data.meshes (1,658).
- The town map is a ~810 x 570 m central plain (street block, row of houses, industrial yard, lake shore, offshore structure ~250 m out) inside a ring of ~1 km mountain tiles, with no dock or pier. Check _bmad-output/verification/plan-epic-2/map_topdown.png before placing anything in the world.
- A recursive grep from the project root runs past the 2-minute tool timeout (Library/ and LFS binaries). Scope searches to _bmad-output/, loop/ or Assets/Scripts.

## From 2-1-settle-the-players-scale-in-the-town (2026-11-11)

- The player's live eye height is the code literal ThirdPersonCamera.cs:79 (feet + 8.5 m): main characters.fbx has no skeleton, so the head-bone lookup always fails (the standing 'Head bone not found!' warning).
- The Photo Shoot rig shoots from the subject's bounds-centre height at distances in subject heights; it has never used an eye height and is scale-invariant by construction.
- Assets/Scenes/SampleScene/NavMesh-game map.asset is 36 MB in plain git (no *.asset LFS rule): a finer re-bake can pass GitHub's 50 MB warning or 100 MB hard limit and break the orchestrator's push.
- The deferred 'NavMesh bake dimensions are metric' entry read SampleScene's unused built-in NavMeshSettings block (m_NavMeshData 0); the real bake is the NavMeshSurface on 'game map' with NavMeshAreas agent type 0 (0.95 / 8.86 / climb 2).
- The drunk's cue has never been measured at the listener: Story 1.8 computed its curve, and its evidence went to Temp/. Use an OnAudioFilterRead tap on the AudioListener's GameObject.
- After a play-mode rig run the MCP bridge drops for 10-60 s: call refresh_unity(wait_for_ready) and check that execute_menu_item returned success before starting a wait loop (two blind 10-minute waits lost here).
- AI Navigation 2.0.10: with overrideVoxelSize off the bake uses the agent settings' voxelSize (0.1667), not radius/3. Set the override explicitly and read cellSize back from the NavMeshData's SerializedObject.
- ~~The census's street block (cars, Casa, HH, doors) sits ~60 m underground (y~-60) under the plain.~~ **Corrected 2026-11-11 by Story 2.2:** BOTH exist. A SURFACE street block (asphalt, shops, the supermarket, white buildings) stands at x -290..-90, z -240..-5, south of the road. A separate buried set (Casa, HH, Tetto, cars, the factory) lies at y -68..-48 under the slice (x -75..25, z -94..18), and since 2.2 it has no colliders (ColliderRule: buried). The slice itself is the raised road, pines, the police car (Cube.006_*, ~2x real) and one house at x~-47.
- At person scale the road is a raised slab with 1.2-1.65 m edges, reachable only near x 0 (a 0.13 m kerb at x 0.5); a tyre line at x -223..-213 fences off the plain's west quarter. Route player rigs accordingly.
- To stage a different eye height through the REAL ThirdPersonCamera, add Armature/Hips/Spine/Chest/Neck/Head/Head_end under the player: it prefers a head bone over the PlayerConfig eye height.
- Since ADR-5, rigs read the eye height and near clip from Assets/Data/Player/PlayerConfig.asset (PhotoShootRig.ReadPlayerEye); never hard-code an eye height.
- Unity's working files are CRLF: edit them with a script that reads with newline='' and converts patterns, or one edit turns into a whole-file diff.
- sprint-status.yaml line 2 is a commented '# last_updated:'. Edit the real 'last_updated:' key (~line 40), never the first textual match.
- The grader's occlusion rays do not see the town's pines: at person scale the Town Placement Study hides the drunk in 3 of 12 directions while every grade is unchanged. Judge occlusion from the pictures.
- Measure a NavMesh actor's 'arrival' with the agent's own test (remainingDistance <= stoppingDistance), never a fixed radius: Story 2.1's 1 m 'reach' radius reported 'arrives 0.9 s early' for a drunk who vanished 0.5 m short of his last waypoint in 7 of 7 cycles.
- Unity calls Awake even on a DISABLED component: moving initialisation from Start to Awake makes it run inside rigs that switch the component off before play. Guard it with `if (!enabled) return;`.
- [DefaultExecutionOrder] does order Awake across scene objects (measured in 6000.3: GalleryView.Awake saw ThirdPersonCamera's Awake at -100). Use it when one component's Awake reads a value another writes.
- Before moving a value into a config applied at runtime, find every reader of the old source: GalleryView read Camera.nearClipPlane once in Awake, so the config value applied later in Start made the gallery open invisibly.
- Fail-soft accessors must check finiteness, not just NaN: -Infinity in a velocity term wedges a CharacterController for good (Move rejects the whole vector, and -inf + x stays -inf), even after the config is fixed.
- After a rig leaves play mode, Unity MCP can answer 'No Unity Editor instances found' for ~20 s during the domain reload. Check Get-Process Unity and retry before assuming a crash.
- To wait for N event lifecycles without sleeping, run Monitor with an until-loop that counts '[Events] TownDrunk → Spawn' in Editor.log after the line count recorded before Play; the log records every phase transition.
- execute_code compiles with CodeDom (C# 6), where `Object` is ambiguous with `object`. Write `UnityEngine.Object.FindObjectsByType<...>`.

## From 2-2-walk-the-whole-town (2026-11-12)

- SampleScene's map colliders are 16,738 prefab-instance ADDED MeshColliders keyed to FBX child fileIDs (the scene is 16.9 MB in plain git): a map re-export or importer change that renames children orphans them silently, so count resolving colliders after every map reimport.
- The runtime asmdef CameraGame references only Unity.InputSystem: NavMeshSurface, NavMeshModifier and NavMeshModifierVolume (Unity.AI.Navigation) are reachable only from CameraGame.Editor, while NavMesh, NavMeshAgent and CalculatePath are built in. The EditMode test asmdef references CameraGame only.
- game map .fbx.meta still had importCameras: 1 and importLights: 1 on 2026-11-11. AR8 held only because Blender happened to export no camera.
- UNVERIFIED LEAD: the plan-epic-2 zoom renders (evaluated .blend, Unity (x, z) = (-bx, -by)) show a SURFACE street block at about Unity x -100..-215, z -10..-170, so lesson 61's '~60 m underground' street block may be only the instancing source. Verify in Unity before relying on either.
- All 15,020 scatter MeshColliders in SampleScene referenced mesh fileIDs the FBX no longer had (null meshes): pines, rocks and grass never collided. Before trusting a collider census, count MeshCollider.sharedMesh == null (TownMapGuardTests now guards it).
- Lesson 61 corrected: the town has BOTH a surface street block (x -290..-90, z -240..-5: asphalt Object_14, shops, SuperMarket) and a buried copy at y -68..-48 under the slice; the buried set and a -6.5 m plane under spawn had colliders until Story 2.2 removed them.
- Headless Blender: a background depsgraph does not reproduce particle size randomness (instance origins identical, scales differ), and a transformed bound_box depends on the object's local frame; compare particle instances by origin and others by exact vertex bounds.
- The town FBX was exported with FBX_SCALE_UNITS and bake_anim off; the exporter defaults give UnitScaleFactor 1 and move animated objects up to 907 m (tools/map/map_export.py holds the proven settings).
- On the whole-town NavMesh (132k triangles, dense from pine-trunk holes) one NavMesh.CalculatePath returns PathPartial past ~300 m whatever the target — a search budget, not islands; test connectivity from nearer starts; tile size does not help.
- A rig marker behind a wall can be legitimately visible (the supermarket's east face is glass) and a magenta marker false-fires on the pink supermarket: use a pure green unlit marker and count a pose only with contact and control > marker.
- The loop guard scans the whole Bash command text: words like 'git clean' inside a heredoc note get the command blocked; and Windows Python cannot read Git Bash's /tmp — keep scratch under the evidence folder.
- On the whole-town NavMesh (132k triangles), ONE NavMesh.CalculatePath returns PathPartial ~270-430 m out whatever the target (the query's search budget, not islands). Chain queries from each partial end and re-query every leg; keep event waypoints within ~250 m.
- A NavMeshAgent given a far single destination usually arrives (it re-plans as it walks: 17/18), but its partial first path can lead it into a dead end and leave it there (corner_nw beside the grey strip at x -546). Chain waypoints for long routes.
- A NavMeshAgent standing on its destination can report hasPath false: test arrival as !pathPending && remainingDistance <= stoppingDistance && near the target, without hasPath.
- FallRecovery restores in the very Update that first finds the body out of bounds, so a rig reading after Update never sees it outside: read the instant from FallRecovery.LastFrom / LastRecoveryTime.
- A 'fell through' probe must start just above the head (feet + capsule height + 5 cm). Started at 2.5 m it flagged 27 frames under the boulders' overhangs and the stacked tyre line.
- The town has a long grey strip at x -548 (Sphere_4_Mat.6_0) from z -32 to the sea: it splits the NW corner off near the shore. Routes from the shore west must go round its south end (z < -32).
- OneDrive can lock a folder you shutil.rmtree: empty it and copytree(dirs_exist_ok=True) instead. The loop guard also blocks 'rm -rf dir/*' in Bash; use a Python helper inside the project.
- A Python sub-script that converts '\n' to the file's newline must not be fed text that is already CRLF (it produced '\r\r\n' and git saw a whole-file binary-ish change). Normalise to LF first.
- In Git Bash heredocs, '\\n' inside a quoted Python heredoc still reached the file as a real newline: write such fix scripts with the Write tool and use chr(92).
- After the orchestrator merges master into a loop branch, SampleScene.unity changes on disk under the open editor and the next refresh_unity raises a modal 'The open scene(s) have been modified externally' that silently blocks the main thread (MCP timeouts, ~0% CPU). Find it with EnumWindows (class #32770), check git diff HEAD on the scene is empty, then PostMessage BM_CLICK to its Reload button.
- Two individually valid tunables can combine into a broken behaviour (FallRecovery's 16-spot ring x safeSampleInterval vs minRestoreAge sent every fall to the spawn): when reviewing a config, push pairs that interact, not just single fields.
- A rig that reads another component's state in its own Start can run before that component's Start (the review rig read FallRecovery's spawn fallback as [0 0 0]); read it after the first grounded frame.
- The fall rig's summary.txt is written by tools/fall_report.py, not by the rig: after re-running Tools/Town/Fall Recovery, regenerate it (python -I tools/fall_report.py <dir>) or you will read the previous run's numbers.
- Rigs that write into a story's evidence folders overwrite the dev's evidence: copy the folder aside before a review re-run and diff -r it back afterwards.
- A session that commits its fix and then ends before writing the result file leaves evidence that may predate the commit: re-run the rigs on HEAD before reporting (here, a re-run found a hang the earlier evidence missed).
- NavMeshAgent path requests can stay pathPending for 25 s or more (the pathfinder runs pathfindingIterationsPerFrame per frame, so slower frames take longer): never gate route logic on !pathPending without a timeout.
- NavMesh.CalculatePath searches less far than an agent's own request (it gave up 139 m short where the agent found a detour) and returns PathInvalid from a start that is off the mesh. Do not swap one for the other.
- Make a timing-dependent stall deterministic in a rig by setting NavMesh.pathfindingIterationsPerFrame to 1 for that scenario, and restore it as soon as the stall has been observed.
- The town's mountain ring is walkable from the plain to the map's outer edge (flood fill at 45 deg, 6.7 km2): every ring barrier is an invisible stop wherever it is placed, so a visible edge is an art decision, not a config edit.
- Rig options in SessionState (TownRig.XLabel, TownRig.XWallsOnly) survive domain reloads and a previous session: set or erase them before every run, or a 'full' run silently runs a subset.
- Objects staged with execute_code right after manage_editor play can land during the play-mode transition and behave differently (a path request that never came back). Wait for refresh_unity(wait_for_ready) before staging probes.
- The Bash tool wraps commands in single quotes: a heredoc containing an apostrophe breaks it. Write scripts with the Write tool instead.
- Story 2.2: the mountain ring is walkable all the way to the map's edge (6.7 km2), and 1,642 of the 2,271 m ring barrier is an invisible stop on walkable ground. On this map, any AC that says 'the terrain stops them' needs a visible edge.
- A failed CLOSE has no route back to dev: the orchestrator retries CLOSE with your next_hint. Write a fix that is not small as a build plan for that retry, with its verification.
- The census grids (census/grid.json; grid_surface_y, grid_slope_deg and grid_top_kind .f32/.u8; 2 m cells over x -700..300, z -450..650) answer reachability questions offline with numpy in seconds, with no play mode.
- The loop guard pattern-matches its forbidden git verbs anywhere in a Bash command, even inside heredoc prose such as a note or commit message. Write prose with the Write tool and run it from a file.
- Renderer.bounds of a rotated mesh is the box around its rotated local box: it overstated the tilted white rock's top by ~1.1 m. Size placed rocks from their vertices (TownEdgeBuilder.TrueBounds).
- Gap checks for a jump must model the controller (a step of 0.35 m or a <=45 deg slope, plus the real arc, over a surface profile including rock tops); a flat apex or 'touches any standable facet' produced 269-2,084 false gaps.
- Growing a boulder to reach low ground flattens its exposed cap into a walkable ramp; keep it a half-buried sphere and stack fillers under it.
- The town's mountain meshes are shells whose edges hover above the plain: 45,716 m2 of the pub NavMesh island lies more than 1.77 m under the census surface, enterable on foot. Compare NavMesh y with TownCensus.Column's top y, never the top surface alone.
- 'A rock somewhere in this column' is not 'the rock stops them': classify each NavMesh contact by what is directly under it and whether NavMesh.Raycast runs straight inward from it.
- CharacterController.Move works in edit mode: a temporary CC is a cheap, faithful capsule probe for 'can the player get in there' (DestroyImmediate it before saving).
- An execute_code call over ~30 s times out on the client but keeps running in Unity: wait on its output file instead of re-issuing it (a re-issue doubled a 3-minute build).
- The town's terrain is single-sided tiles (the plain's Plane* pieces, the mountains' Landscape*) overlapping at different heights; a lip hovering over the tile beneath is a walk-under cave seen through a back-face. Tools/Town/Seal Mountain Foot closes them; measure with Tools/Town/Measure Under Mountain (NavMesh) after every re-bake.
- NavMesh.CalculateTriangulation polygons are flat chords: on convex slopes a big triangle's centroid sits 1-3.7 m under the ground. Take heights from NavMesh.SamplePosition (the detail mesh), never from triangle centroids.
- Welding CalculateTriangulation vertices by position joins stacked layers where they touch at a flush lip; confirm reachability with NavMesh.CalculatePath (PathPartial ends where the layer is really joined) before calling a pocket reachable.
- A CharacterController passes through a single-sided MeshCollider from its back face: a capsule probe that starts under a tile walks out. Start probes on the open side, and check where SamplePosition put the start when layers stack.
- Absolute frame times drift 2x within a day on this laptop (RAM pressure, OneDrive syncing the project folder after big scene and NavMesh writes). Measure new content by an interleaved hidden/shown A/B in one session (TownRig staging 'hideedge', tools/fps_ab.py).
- While Unity is busy (an auto-refresh after an on-disk script edit), MCP answers 'No Unity Editor instances found' and DROPS the call. Check Get-Process Responding and re-send; never assume the call ran.
- When a story adds world geometry late (edge rocks, a seal), re-run every rig whose scenario walks through it: Story 2.2's fall rig went 10/20 FAIL on rig assumptions (a rock now blocks the backstop walk; a faster restore preempts the 'feet below the lip' clock), not on the game.
- A rig that reads after Update never sees a state FallRecovery (or any order-100 corrector) undoes in the same Update: read the corrector's own record (LastFrom) instead of concluding 'never happened'.
- For a content's frame cost, an edit-mode ABBA render A/B (camera.Render into an RT, synced by a 1-pixel ReadPixels, toggling Renderer.forceRenderingOff) measures to ~1 ms in 15 s; the play-mode editor tour had 17-75 ms of noise. forceRenderingOff never dirties the scene.
- The cross-review walls instrument cannot set up poses against the ring's edge rocks on slopes (it finds the slope, or a neighbouring rock stops the body): 0 valid poses is an instrument limit there, not a pass.
- A Scene struct captured before EditorSceneManager.OpenScene(Single) reads path "" afterwards: copy the path string before calling a rig that reopens the scene.

## From 1-19-a-shot-that-earns-nothing-reads-red (2026-11-15)

- Unity 6000.3's Mono prints float P0 in en-US as '0 %' (with a space), rounds 0.005f up to '1 %', and never prints -0. Its fr-FR culture prints U+066A (the Arabic percent sign). Pin InvariantCulture for any player-facing percentage.
- Mathf.Clamp01(-0f) returns -0f with the sign bit kept, so ShotGrade.Percent01 can be negative zero through the real factory. A test of -0 must check the sign bit of its fixture first.
- The Grade HUD rig's evidence on disk (verification/hud, 2026-11-11) was shot at the Game View's 953x476. Legibility numbers taken from it do not carry over to 1080p, where the 22 px lines and a 1-2 px outline actually render.
- On authored colours, the Lab hue angle does not separate salmon from red (38.7 vs 36-40 degrees). Chroma and HSV saturation do (salmon C* 52.6 and s 0.55, against C* 72-105 and s 0.68-1.0 for reds).
- Setting Time.timeScale = 0 right after a control grab makes the next rendered frame the SAME simulation instant: a frozen-vs-unfrozen control reads 0/255 unless one live frame passes in between.
- PhotoModeController's editor-only debug overlay holds 1.5 s on UNSCALED time; editor captures at 1080p run ~6 fps, so it can vanish mid-way through a multi-grab rig. Judge a freeze control near the region you measure.
- At a 1920x1080 Game View the editor delivers ~0.16 s per frame with CaptureScreenshotAsTexture each frame: six grabs eat ~1 s of the HUD's 2.2 s unscaled hold, so record the alpha at every grab.
- The Standalone Game View group ships a built-in 'Full HD' 1920x1080 preset (index 3); GameView.SizeSelectionCallback(index, null) via reflection selects it and back without touching user prefs (test-helper's SetResolution adds a custom size).
- Painting uGUI text marker-magenta and its Outline pure green on the same frozen frame gives exact glyph and outline masks with no detector: solve coverage per pixel against a labels-off frame.
- python -I ignores PYTHONIOENCODING: scripts that print star glyphs must call sys.stdout.reconfigure(encoding='utf-8') themselves.
- Mid-fade, 'panel minus outline' rises above its full-alpha value because the translucent panel fades too. Test whether an element fades with a compositing model ((1-a)*P_a vs 0), not with normalised differences.
- An Overlay uGUI HUD can be pixel-checked synchronously from execute_code with no coroutine. After Awake, switch the throwaway canvas to ScreenSpaceCamera, disable its CanvasScaler, call Canvas.ForceUpdateCanvases, then cam.Render into an RT and count pixels.
- To drive a MonoBehaviour's real Awake on your own wiring from execute_code, build the GameObject inactive, set its [SerializeField] privates with FieldInfo.SetValue, then SetActive(true); invoke private handlers by reflection.
- A Monitor grepping Editor.log for 'Exception' fires on ordinary UIElements stack frames ('System.Exception&'). Match '^[A-Za-z_.]+Exception: ' for a thrown exception.
- In Git Bash, grep -c $'\r' matched every line and reported LF files as CRLF. Use 'git ls-files --eol' or count bytes in Python. The Edit tool keeps each file's own line endings.
- Mono rounds midpoints in custom formats ('0.0' turns 1.25 into 1.3). Pin culture tests on non-midpoint values, or the test pins the rounding instead of the culture.
- Before making a shipped component re-apply config on every event, check whether a rig writes that component's state directly. Here the Story 1.19 candidate sweep would have measured 1.5 px for every candidate, with no error.
- At close, decide which regression rigs to re-run by diffing shipped code from each rig run's commit to HEAD (git diff <commit>..HEAD -- Assets/), then grepping who calls the changed members; re-run every rig that reaches them and record why the others were skipped.
- Grade HUD Shoot Phase E absolute shutter times drift a lot between sessions on this laptop (dev mean 25-39 ms, close mean 58 ms, same code): judge the HUD's cost only by listening against disabled within one run.

## From 1-15-record-a-clip (2026-11-15)

- PhotoShootRig.cs:375, GalleryShootRig.cs:218 and GradeHudShootRunner.cs:788 each AddComponent<PhotoModeController> in an isolated world. Any new required [SerializeField] on PhotoModeController that logs an Error in Awake adds an error line to every one of those regression runs.
- No Input System settings asset exists in this project, so the default editorInputBehaviorInPlayMode applies: queued mouse or keyboard events need Game View focus. Drive an unattended rig's real input path with a virtual Gamepad (InputSystem.AddDevice<Gamepad>) and prove the press arrived.
- unity_docs (MCP for Unity 10.3.0) returned empty descriptions for AudioSettings.dspTime, AudioSource.bypassListenerEffects and MonoBehaviour.OnAudioFilterRead on 2026-11-15. unity_reflect works: use it for member existence, and measure the behaviour.
- loop/runtime/approvals.txt is readable only with the Read tool: the loop guard blocks any shell command that names it, even cat.
- In Unity 6000.3's Mono, GC.GetAllocatedBytesForCurrentThread reads 0 around new byte[1000] and GC.GetTotalMemory moves in 4 KB blocks: prove zero allocation by heap growth over ~9,000 calls of the full path, with a positive control that allocates.
- The editor renders Main Camera more than once per frame, so a 'previous frame's render' control can compare a frame with itself: compare against the render of the previous TICK's frame.
- cam.Render() right after posing a SkinnedMeshRenderer draws the previous skinning pass; set forceMatrixRecalculationPerRender (or let a frame pass) before photographing a posed ghost.
- A frozen replay ghost (Animator.speed 0) reproduces a crossfade exactly with the normalized CrossFade and the next state's normalized time; CrossFadeInFixedTime with offset 0 puts the next state at its start (6.4 cm bone error).
- AudioSettings.Reset invalidates clips made with AudioClip.Create (they play silent afterwards) while asset clips and OnAudioFilterRead keep working: rebuild procedural clips after a reset.
- AudioSettings.dspTime read inside OnAudioFilterRead is the block's own DSP time (PlayScheduled clicks land sample-exact in a tap); main-thread dspTime steps in DSP blocks, a -38..+35 ms sawtooth against realtime.
- Edit-mode Physics.Linecast line-of-sight passes through the town's pine canopies (no colliders): survey vantages by rendering markers on the route and looking.
- A virtual Gamepad through PlayerInput on an unattended editor needs InputSystem.settings.backgroundBehavior = IgnoreFocus for the run (restore it); prove arrival with the action's performed count.
- A Python heredoc in Git Bash mangles non-ASCII (an em-dash), so an exact-match replace silently misses: write patch scripts with the Write tool.
- The loop guard blocks 'git add -A <paths>' as bulk staging; list every file by name.
- Restoring the 5 settings files a Windows build re-serialises: git show HEAD:path > path, then convert LF back to CRLF so git status is clean.
- PlayerInput Send Messages reaches DISABLED MonoBehaviours, so a disabled controller still handles presses. Gate input handlers on isActiveAndEnabled.
- SendMessage("OnCapture", null) never reaches OnCapture(InputValue): an InputValue probe needs FormatterServices.GetUninitializedObject(typeof(InputValue)), proven first with an enabled-component control.
- Unity's == on a destroyed MonoBehaviour skips plain-C# cleanup that touches no Unity API (here, disarming the audio tap). Use ReferenceEquals for that call.
- On a stereo-only device, AudioSettings.Reset to Quad or 5.1 succeeds, but OnAudioFilterRead does not deliver 4 or 6 channels. Size listener buffers from min(speakerMode, driverCapabilities).
- To make a mid-clip device change race deterministic, call AudioSettings.Reset followed by Thread.Sleep(200) in one execute_code. The audio thread then delivers post-change blocks before the next LateUpdate.
- execute_code runs outside the player loop: Begin and Stop in one call make a 1-tick clip. Put them in separate calls to record over real frames.
- Judge recorded PCM against a known signal. A 2D 440 Hz sine at 0.25 reads RMS 0.125 per channel; a silent scene made the first lost-audio probe inconclusive.
- Record Clip Shoot writes into rig/ on top of older files. Copy rig/ aside, restore it, and drop files byte-identical to the copy before analysing a re-run, or old frames get scored as new.
- Never simulate 'no audio device' from inside the editor: toggling AudioManager m_DisableAudio through SerializedObject.ApplyModifiedPropertiesWithoutUndo re-initialises FMOD and crashed the editor in FMOD::System::releaseOutput (and the first toggle did not even change AudioSettings.outputSampleRate within the call).
- After an editor crash, the relaunch stops at a 'Recovering Scene Backups' modal (#32770, buttons Yes/No; the backup is Temp/__Backupscenes/0.backup). If the scene was clean and git shows no diff, PostMessage BM_CLICK to 'No'.
- In edit mode, AddComponent does not call Awake on a plain MonoBehaviour (no [ExecuteAlways]). An EditMode test never sees Awake's warnings or initialisation: measured with a GameLog.Warn positive control.
- A test helper that creates objects and then runs Assume or Assert must clean up itself: the caller's try/finally only starts once the helper returns, and an Assume throws InconclusiveException.
- The Test Framework runs EditMode tests in its own scene and reopens the previous scene setup at the end (RestoreSceneSetupTask in Editor.log), so objects a test leaks vanish after the run: count them from a later test in the same run, not afterwards.
- A bench's mean difference-in-differences can hide a steady cost behind runs of slow frames: report the per-round median delta too (1.15: mean +0.10 ms, median +0.78 ms in all 3 rounds).
- Record Clip Shoot's report header prints LOCAL time with a 'Z' suffix (its clip ids hold the true UTC). Use the clip ids or file mtimes when ordering evidence against commits.
- Record Clip Shoot always writes into the dev's rig/ folder: park and collect it with _bmad-output/verification/1-15-record-a-clip/close-a1/swap_rig_dir.py, then check rig/ with diff -rq.

## From 1-16-grade-a-clip (2026-11-15)

- GradeHud hides on !photoMode.IsPhotoMode (hideOnCameraLowered, on by default), and PhotoModeController.SetMode stops the recording AFTER Mode changes, so any readout raised by a stop that lowered the camera vanishes in the frame it appears. Check the hide rule whenever a new event drives the HUD.
- ISubject exposes no peak window length (the peak duration is only in EventDefinition.GetPhase(Peak)), and the drunk's lifecycles are told apart only by EventActor.LifecycleSerial, which is not on ISubject. Clip-level peak coverage needs both.
- game-architecture.md disagrees with itself on Recording's dependencies: section Clip Recording (:241) allows Recording -> Grading, but section Architectural Boundaries (:905) lists only Events and Core. Cite both when adding grading to Recording.
- SampleScene.unity is about 14.1 MB on disk and 13.7 MB in git, not the 16.9 MB that older notes (lessons line 79, story 1.15) still quote.
- Story 1.15's rig vantage (32, 8) has a pine trunk collider 8.6 m out on the line to the drunk's early Build, so the grader rightly reads him Occluded there; grade-rig vantage (26, y, 13), side-on 6 m from the peak spot, is clear by rays and by rendered markers for route x 22-29.
- In this Mono, GC.GetTotalMemory does not register retained byte[] allocations (9,000 x byte[64] = 576 KB read 0 B) while retained new object() does (~80 KB): a zero-allocation proof needs an object-allocating positive control.
- A 1080p CaptureScreenshotAsTexture grab makes that frame slow: a key frame grabbed just before a scenario's stop or start moves the stop by the slow frame (a '0.75 s in' clip covered 0.52 s). Keep grabs >= 0.3 s away from stop and start thresholds.
- Clip peak coverage measured on the subject's own clock (-TimeToPeak stretches between a lifecycle's ticks, joined end to end) is exact for a whole window at any frame rate; counting 1/30-s ticks reads 44-46 of 45.
- Tools/Grading/Grade Clip Sweep (GradeClipShootRig.SweepDir) replays a run's ticks.csv through the real ClipGradeBuilder bit-identically to the live grade: retune clip grading offline, no play mode.
- The Recording Bench build films the drunk TooSmall, so in the build the grader never runs its occlusion linecasts: its frame cost covers the cheap path only.
- One editor session's Editor.log holds every rig run of the session (a 1.15 Record Clip run from hours earlier was in it): anchor Editor.log waits to the line count taken just before launching, and match each rig's own completion line (Grade HUD/Photo/Gallery Shoot print 'Shoot finished - reopened', not 'Restored').
- EventManager keeps a destroyed actor in ActiveActors and never respawns: destroy the drunk only as a rig's last act before leaving play mode.
- GalleryView.Open is a no-op while the camera is raised: the only screen-owner stop route for a clip is PhotoModeController.SetRaiseSuppressed(true).
- In this Mono, GC.GetTotalMemory AND Profiler.GetMonoUsedSizeLong read 0 B for retained strings and byte[] (only reference-holding objects register); to prove zero allocation, count with ProfilerRecorder 'GC Allocated In Frame' over a frame that does only the work, beside a baseline frame, a string control and a busy-wait of equal length (Story 1.16 review, Grade Clip rig Phase G).
- Anything that must describe 'exactly the frames the footage holds' has to filter and cap ActiveActors exactly as ReplayBackend.SampleActors does (skip null, count the cap, then check liveness); check it live by comparing the footage's live lifecycles with the consumer's, tick by tick (Grade Clip rig Phase R, CompareSets).
- To reproduce a culture bug in an EditMode test without changing the editor's culture, invoke the real test method by reflection from execute_code under Thread.CurrentThread.CurrentCulture = fr-FR / de-DE (CodeDom is C# 6: use Func lambdas, not local functions, and UnityEngine.Object explicitly).
- A private Awake can be exercised in edit mode by building the component on an INACTIVE throwaway GameObject, wiring fields by reflection and invoking Awake by reflection, capturing lines with Application.logMessageReceived; it reproduces wiring-dependent log behaviour without a play-mode rig (destroy the object and clear the console afterwards).
- The loop guard blocks the Write tool outside the project, %TEMP% included; keep scratch files inside the evidence folder or write them through Bash.
- The PhysX warnings 'Detected one or more triangles ... greater than 500 units' for House Complete.001 and Object014.003 are logged whenever SampleScene opens in the editor; they are baseline, not a finding.
- Editor.log has no 'warning CS' line when a compile is clean, and an unchanged assembly does not re-show its warnings. To prove 'no new warnings', recompile each assembly with Unity's Roslyn at -warn:4 and ignore CS0649. The Editor and Test csproj files reference CameraGame by <ProjectReference> (add Library/ScriptAssemblies/<name>.dll), and -out needs quotes because the project path has a space (1-16 close-a1/compile_warnings.py).
- Editor.log prints no log level. Classify each message by the 'UnityEngine.Debug:LogWarning / LogError' frame that follows it, walking back to the first line of the block (1-16 close-a1/log_levels.py: a whole rig run's warnings and errors, with the rig's phase markers around them).
- A shipped change that only adds a log line for an INVALID config cannot change a regression rig that uses the shipped (valid) asset. Check that, with the shipped asset's own validator test, before re-running a rig at close.
