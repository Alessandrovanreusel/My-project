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
