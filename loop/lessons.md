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
- The world is ~5x human scale (player CharacterController 8.86 m): put rig cameras at ~7.5 m eye height.
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
