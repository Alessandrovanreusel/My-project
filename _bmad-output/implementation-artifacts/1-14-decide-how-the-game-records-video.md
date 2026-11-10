---
baseline_commit: b343a8875f6305b6fc2cd8ba14ec7faffedb12cc
---

# Story 1.14: Decide how the game records video

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Technical spike. **It gates Stories 1.15–1.18.** Added 2026-11-10 by `sprint-change-proposal-2026-11-10.md`
(video replaces photo). It is the **first story in this project that must be proven in a Windows build**: the
tool that records the game in the Editor (Unity Recorder) is exactly the one that cannot ship.*

## Story

As a developer,
I want to know, by measuring rather than guessing, how a **built** copy of the game can record its own gameplay,
so that the camcorder stories build on an approach that actually works outside the Editor.

## Acceptance Criteria

The three ACs below are copied word for word from `epics.md` §Story 1.14 and are the contract. The notes
under each one say what they mean in this project. They add precision, and they never relax a clause. AC4 is
added in the same way Stories 1.11 and 1.12 added a no-regression AC. It writes down CLAUDE.md's standing
rules for a story that builds a player and changes build settings.

**AC1: Three approaches, prototyped against the Town Drunk in the real slice scene and measured (NFR1, NFR3)**

> **Given** Unity Recorder is Editor-only and cannot ship in a build,
> **When** recording approaches are compared — at minimum **(A)** encoding a video file while playing, **(B)** replay
> recording (saving what happened and re-rendering it), and **(C)** keeping raw frames in memory —
> **Then** each is prototyped against the Town Drunk in the real slice scene and measured for: frame rate at 1080p
> while recording (NFR1), memory and disk per second of footage, whether the game's sound is captured, playback
> quality, and licence/cost.

- **"The real slice scene" is `Assets/Scenes/SampleScene.unity`.** It is the only scene in the build list
  (`ProjectSettings/EditorBuildSettings.asset`). It has the real town patch, the real `EventManager`, and the
  real pooled `EventActor_Drunk` running `Assets/Data/Events/TownDrunk.asset`. **Do not use a private grey-plane
  world.** Story 1.12 measured the town's render plus readback at **~40 ms**, against **~6 ms** in Story 1.11's
  private world (1.12 Dev Agent Record, "NFR2 — measured, not asserted"). A private world would flatter every
  candidate by about 6× on the one cost this story exists to measure.
- **The deliverable is a 15-cell table:** five measurements × three candidates. Every cell holds either a
  measured number with its evidence path, or the words "not measurable, because …". A candidate may lose
  early. It still gets its row, and the row says what was measured before it was dropped.
- **Frame rate is measured in a Windows BUILD**, not in Editor play mode. Editor overhead, an unfocused editor
  and `RigVideoRecorder` make Editor numbers non-evidence for NFR1. Use the protocol in Task 1c: windowed
  1920×1080, the same scripted scenario every time, recording interleaved with a no-recording baseline, mean,
  p95 and p99 frame time, and the share of frames over 16.67 ms. The **recording cost is the delta against the
  baseline from the same run.**
- ⚠️ **This machine is not the "mid-range Windows desktop" NFR1 names.** It is a laptop: Intel i7-1165G7
  (4 cores / 8 threads), Intel Iris Xe plus an NVIDIA GeForce **MX350** (2 GB), 15.7 GB RAM, a 2560×1600 panel,
  ~85 GB free on C:. If the no-recording baseline already misses 60 FPS here, **the absolute NFR1 verdict
  cannot be given on this machine.** Report the delta, say so plainly, and queue it as a `human_checks` item.
  **Do not lower quality settings, resolution or render scale to manufacture a 60.** That would measure a
  different game.
- **Memory and disk per second of footage:** report RAM held per second of footage (the candidate's own
  buffer accounting, cross-checked against process private bytes) and bytes on disk per second (file size or
  `ffprobe`). For (C), 1080p30 RGBA is ≈ 1920 × 1080 × 4 × 30 ≈ **249 MB/s**, so 10 s ≈ 2.5 GB. Measure the
  10 s, then extrapolate to the ~2-minute card (≈ 30 GB), and **label the extrapolation as an extrapolation.**
- **"Whether the game's sound is captured"** means the footage carries the game's own audio as the player
  hears it: the drunk's 3D mumbling cue at the listener, plus everything else. Prove it from the audio track
  itself. It must be non-silent. Its loudness must rise as the drunk comes closer. The A/V sync marker
  (Task 2.5) must line up with the video. Report the offset in ms; for scale, ITU-R BT.1359 puts
  detectability at about +45 ms (audio early) and −125 ms (audio late).
- **Playback quality:** measure first. Check resolution, frame rate and duration as recorded; count dropped
  and duplicated frames; compute SSIM/PSNR against reference frames grabbed on the same game frames
  (Task 2.4). After that, "does it look acceptable" is **perceptual**. It becomes a `human_checks` entry and
  is never self-certified.
- **Licence/cost covers each candidate *including its export path*** (AC2): the licence of every binary and
  library it would ship, any purchase, patent notes, and the obligation that shipping creates.
  ⛔ **Do not buy anything, start a trial, create an account or accept licence terms.** Those are real
  blockers under the loop rules. Paid plugins (Latest technical information, below) are assessed **from
  vendor documentation only**. They are not prototyped, and that does not violate AC1: AC1 asks for the three
  *approaches* to be prototyped, not every product that implements them.

**AC2: ADR-4 in the architecture, with evidence, the Edit Suite path and a working export path; Alexv approves before 1.15 (FR29–FR31, AR12)**

> **Given** the measurements,
> **When** an approach is chosen,
> **Then** it is recorded as **ADR-4** in the architecture with the evidence behind it, including how the Edit Suite
> (Epic 9) would trim and join clips on top of it and how a finished edit is exported as a standard video file
> (FR31 — confirmed by the stakeholder 2026-11-10, so the chosen approach must come with a working export path)
> **And** Alexv approves the choice before Story 1.15 starts.

- **A "working export path" is demonstrated, not described.** Produce a standard video file from recorded
  footage of the drunk. Use the machinery the game would ship, and include a **trim** (cut ~2 s off each end of
  one clip) and a **join** (two clips into one file). Verify the result outside the game, in three ways:
  - `ffprobe` lists the expected streams (video plus audio, the right resolution, frame rate and duration);
  - a full decode, `ffmpeg -v error -i out.mp4 -f null -`, prints **nothing**;
  - a contact sheet of the output shows the drunk where he should be.
  MP4 with H.264 video and AAC audio is the recommended export target, because it is the most universally
  playable "standard video file". If you choose otherwise, say why.
- For (B) and (C) the export path is an **encode at export time**, and it must be demonstrated too: encode the
  10-s clip offline and time it. Their licence row is therefore the encoder's licence. This is the
  architecture's own warning (ADR-4 OPEN block): (A)'s licence question comes back even if (A) loses for live
  recording.
- ADR-4 **replaces** the "ADR-4 (Clip recording) — OPEN" block in `game-architecture.md` §Architecture
  Decision Records, and Task 7 lists the other places it must touch.
- ⚠️ **The approval clause cannot be completed by you, and you must not pretend it was.** Alexv is away while
  the loop runs. Write ADR-4 with status **`Proposed — awaiting Alexv's approval`**, never "Accepted". Queue
  the approval in the result file as **both** a `decisions` entry and a `human_checks` entry (Task 10), and
  write the same in the Dev Agent Record. The story can reach `review`. It cannot reach `done` until he
  approves. Task 1d covers what this means for Story 1.15.

**AC3: The chosen approach proven in a Windows build (a ~10 s clip of the drunk recorded AND played back there), and the prototype fenced off**

> **Given** the chosen approach,
> **When** it is proven,
> **Then** a ~10-second clip of the drunk is recorded **and** played back **in a Windows build**, not only in the
> Editor, where Unity Recorder would mask the problem
> **And** the throwaway prototype code is removed or clearly fenced off from game code.

- The build goes to `Builds/1-14-decide-how-the-game-records-video/`, which is git-ignored (`.gitignore:104-105`).
  The exe runs with no Editor and no MCP, so the harness must script itself from command-line arguments and
  **quit on its own** (Task 2.2).
- **The clip must contain the drunk's peak**, the 1.5-s stagger and near-fall that the whole game is about.
  Aim for about 5 s before the peak window to 5 s after it. Find the window from `ISubject.PeakOffset` or the
  `EventPeakedChannel` raise, never from a guessed timestamp.
- **"Played back in the build" means the game's own playback path for that approach**, for example
  `VideoPlayer` for a file, a re-render for replay, or texture upload for raw frames. Do not use `ffplay` or
  Windows Media Player. Film the build's window while it plays back, using `ffmpeg -f gdigrab`, and analyse
  that film (Task 8).
- **"Clearly fenced off"** is defined and proved in Task 9. The prototype lives in its own assembly. That
  assembly compiles only in the Editor and in builds that pass an explicit define. No game assembly references
  it. **A plain build without the define must contain no spike DLL** in `My project_Data/Managed/`. Fencing is
  recommended over removal, because the evidence must stay reproducible and Story 1.15 lifts the proven
  backend into `Scripts/Recording/` on purpose. Either choice is allowed. Record which one you made, and why.

**AC4 (cross-cutting): The spike leaves the game exactly as it found it (NFR5, NFR8, CLAUDE.md "Put the project back")**

- **No shipped behaviour changes in this story.** `PhotoModeController.Capture()` still takes a photograph,
  and the Capture action is **not** rewired to recording. That is Story 1.15. Do **not** create
  `Assets/Scripts/Recording/`, a REC indicator, `ClipRecordedChannel`, `CapturedClip`, `ClipGrader` or the
  memory card. Those belong to 1.15–1.17, and building them here would decide their designs before ADR-4 is
  approved.
- **One permitted exception:** a fix needed to make the game **build at all**. The project has never been
  built. An example is editor-only API in a runtime assembly that is missing its `#if UNITY_EDITOR`. Make each
  fix properly, keep it minimal, and list it in the Dev Agent Record with the error it fixed.
- Console clean (NFR5). The baseline is the Version Control project-link notice, `ThirdPersonCamera.cs:43`
  "POV Camera: Head bone not found!", and the two large-triangle mesh warnings the town logs on reload. The
  full EditMode suite passes (163 tests at the last run, 2026-09-27; report the real count). One
  `Tools > Grading > Photo Shoot (Play)` run must show no regression against its recorded baseline, with
  numbers masked.
- **Restored to the state you found:** the open scene (`SampleScene` not dirty, and **never saved with spike
  objects in it**), `PlayerSettings` scripting defines (`{}` today), Frame Timing Stats (`0` today), the active
  build target, the URP assets (`Assets/Settings/PC_RPAsset.asset`, `PC_Renderer.asset`), the quality level
  (`PC`) and `Time.timeScale`/`captureDeltaTime`. Record each value **before** changing it, and confirm it
  afterwards with `git diff` plus an editor read-back.
- **Fail-soft (NFR8)** holds for the prototypes too. A missing encoder, a full disk or a failed readback logs
  once and the game keeps running. A spike that crashes the player measures nothing.

## Tasks / Subtasks

> **Working order and checkpoints.** The spike is large, and a fresh session may have to pick it up halfway:
> - **Order:** 1 → 2 (build, run, baseline) → 3 (C) → 4 (A) → 5 (B) → 6 → 7 → 8 → 9 → 10.
> - **After each task:** commit, and add a dated line to the Dev Agent Record saying what was measured and
>   where the evidence is. The evidence folder is git-ignored but persists on disk, so a later attempt can
>   read it.
> - **If you run low on context**, stop at a task boundary and return `failed`, with a `next_hint` naming the
>   next task and the evidence already gathered. Do not rush the ADR on half the measurements.

- [x] **Task 1: Settle the four things that gate this story (AC1–AC3). DO THESE FIRST.**

  Each of the four comes with a recommendation. Record the choice **and the reasoning** in the Dev Agent
  Record, including where you follow the recommendation. A later reader needs to know it was decided, not
  defaulted.

  - [x] **1a. The frame source: what the footage shows, and where its pixels come from.** This blocks
        (A) and (C), and (B)'s export.
        - **Recommended: the photo camera's own output, WITHOUT the Screen Space – Overlay UI, copied from
          the frame the game has already rendered.** The precedent is `GalleryService.cs` ("THE PICTURE
          CONTAINS NO UI, AND THAT IS CORRECT"). A camcorder records the scene, not its own REC dot, and a
          grade HUD baked into footage would stay there forever, including in every Epic 9 edit and every
          export. The stakeholder's reference video, the rig recordings, also had no UI.
        - **Technique to try first:** inject a URP render pass from script, with **no asset change**. In
          `RenderPipelineManager.beginCameraRendering`, for the photo camera only, call
          `camera.GetUniversalAdditionalCameraData().scriptableRenderer.EnqueuePass(pass)`. The pass (URP 17,
          RenderGraph `RecordRenderGraph`) copies the camera colour at
          `RenderPassEvent.AfterRenderingPostProcessing` into an RT the spike owns. Then use
          `AsyncGPUReadback` on that RT. The game renders once and the copy is a GPU blit. Enqueuing from
          script keeps `PC_Renderer.asset` untouched, so there is nothing to restore.
        - **Alternatives, measured only if the first fails:**
          `ScreenCapture.CaptureScreenshotIntoRenderTexture` after `WaitForEndOfFrame` is cheap, but it bakes
          the viewfinder overlay and any HUD into the footage, which is a product decision you would then have
          to flag. A second `Camera.Render()` into an RT doubles the town's render cost: this is the ~40 ms
          path from Story 1.12.
        - ⚠️ **Never `ReadPixels` per frame.** It is a synchronous GPU→CPU stall. `GalleryService` gets away
          with it once per shutter press; at 30 fps it is fatal.
        - ⚠️ **Colour space and orientation are traps, so prove them with a control shot.** The project is
          **Linear** with HDR on (`ProjectSettings.asset` `m_ActiveColorSpace: 1`, `PC_RPAsset`
          `m_SupportsHDR: 1`). The copy target and readback format decide whether the bytes are
          display-referred. D3D readbacks can also come back vertically flipped. **Control shot:** hide the
          Overlay canvases for one frame, take your frame-source capture, take
          `ScreenCapture.CaptureScreenshotAsTexture()` on the same frame, and report SSIM plus the mean colour
          difference between them. A flipped, dark, washed-out or uniform image means the rig is broken, not
          that the approach is bad.
  - [x] **1b. One footage format for the comparison, so it compares like with like.**
        - **Recommended: 1920×1080 at 30 fps for every candidate.** The AC asks for frame rate at 1080p, and
          30 fps is both the camcorder look and half the per-second cost. The game keeps rendering at its own
          rate. Take a frame whenever game time crosses the next 1/30-s boundary, not "every other frame": a
          variable game frame rate must still produce a true-duration 30 fps clip. Log the real captured
          timestamps so duplicates and drops can be counted.
        - Audio: read `AudioSettings.outputSampleRate` and `AudioSettings.speakerMode`; do not assume them.
          `AudioManager.asset` has `m_SampleRate: 0`, which means the system default, and a 1024 DSP buffer.
          If the device is surround, downmix to stereo for the clip.
        - Add one sensitivity row for the **winner only**: 1280×720 @ 30, which shows the cost/quality trade
          Story 1.15 can pick from.
  - [x] **1c. The measurement protocol. Write it into the Dev Agent Record BEFORE the first measured run.**
        Numbers are comparable only when the protocol is identical.
        - **Release (non-Development) Windows build**, windowed:
          `"My project.exe" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile <evidence>/player-<run>.log -recordingSpike <baseline|A|B|C> -spikeOut <abs evidence dir>`.
          Confirm in the log that `Screen.width × Screen.height` really is 1920×1080. The panel uses Windows
          scaling, and Unity players are DPI-aware, but check rather than assume.
        - **The same scripted scenario every run:** load SampleScene, wait for the drunk to spawn, put the
          player at **one fixed vantage** that sees the peak (find it once in the Editor, record it, reuse
          it), raise the camera through the real path `PhotoModeController.SetPhotoMode(true)`, and keep the
          drunk framed by aiming at `EventActor.Bounds.center`.
        - **Interleave within each run:** about 10 s of baseline (camera raised, not recording), then about
          10 s recording across the peak, then about 10 s of baseline again. Do **≥ 3 runs per candidate**
          and **rotate the candidate order** between runs. A laptop throttles as it heats, so a cold baseline
          against a hot recording invents a cost that isn't there.
        - **Log every frame:** `Time.unscaledDeltaTime` is the primary series. Add the main-thread time
          from `ProfilerRecorder` if it is available in a release player (check, don't assume).
          `FrameTimingManager` needs `PlayerSettings.enableFrameTimingStats`, which is `0` today. If you turn
          it on, do it for the spike build only and restore it (AC4).
        - **Log once at the start:** `SystemInfo.graphicsDeviceName` (Optimus may pick the MX350 or the
          Iris Xe, and it changes everything), `processorType`, `batteryStatus` (**a run on battery is void,
          because the laptop must be on mains power**), the quality level name, `outputSampleRate`, and the
          encoder that was **actually** opened, by its MFT name for Media Foundation.
        - **Log every second:** process private bytes (`Process.GetCurrentProcess()` → `Refresh()` →
          `PrivateMemorySize64`) and the candidate's own buffered bytes. At stop, log bytes written to disk
          and the stop-to-clip-ready time.
        - **No gdigrab, Recorder or other capture during measured runs**, because they cost CPU and GPU
          themselves. Film separate **evidence runs**, and label every file as `measure-*` or `evidence-*`.
  - [x] **1d. How "Alexv approves before Story 1.15 starts" is honoured while he is away.** This is decided
        by the loop policy (decision log 2026-11-10, "decide, flag, keep going"), so it is not yours to
        change. Write ADR-4 as **Proposed**. Queue the approval (AC2 note). Leave the story at `review`.
        - Then **flag the gap** in the result file's `decisions`. `loop/dependencies.yaml` treats 1.14 as
          satisfied once it is merged with only *perceptual* checks pending, and this approval is a decision,
          not a perception. Recommend that 1.15 waits for the approval. If the loop goes ahead anyway, 1.15
          must build behind the narrow backend seam ADR-4 specifies (Task 7.1), so that a veto costs a backend
          swap, not a redesign. **Do not edit `loop/`** to enforce it. That is the orchestrator's and Alexv's
          call.

- [x] **Task 2: Build the spike scaffold, and make the game build at all (AC1, AC3, AC4)**
  - [x] **2.1 A fenced assembly.** Create `Assets/Spikes/RecordingSpike/` with
        `CameraGame.Spikes.RecordingSpike.asmdef`. Settings:
        - `defineConstraints: ["UNITY_EDITOR || CAMERA_RECORDING_SPIKE"]`;
        - `autoReferenced: false`;
        - references: `CameraGame`, the URP runtime and core assemblies the pass injection needs, and
          `Unity.InputSystem` only if it is used;
        - namespace `CameraGame.Spikes.RecordingSpike`.
        Put the editor-side build and rig scripts in an `Editor/` sub-folder with their own asmdef
        (`includePlatforms: ["Editor"]`, same define constraint). Add a `README.md` at the folder root that
        says: throwaway, never referenced by game code, how to rebuild every piece of evidence, and the
        exact commands. Commit `.meta` files with everything.
        - ⚠️ **Check that the `||` constraint behaves, rather than trusting it.** The proof is the
          plain build's `Managed/` listing in 9.1: no `CameraGame.Spikes.RecordingSpike.dll`. If Unity does
          not honour `||` in `defineConstraints`, use two constraint-free asmdefs (an Editor one, and a
          runtime one excluded from players through `includePlatforms`). Then pull the runtime one into the
          spike build by another documented route, and record which route you used.
        - ⚠️ `CameraGame.asmdef` references **only** `Unity.InputSystem` today. **Do not add URP or anything
          else to it** in this story. The spike references what it needs itself.
        - ⚠️ Runtime MonoBehaviours go in the **runtime** spike assembly, never an Editor one. An
          Editor-assembly component on a scene object comes back as "referenced script (Unknown) is
          missing" in play mode, and the rig silently does nothing (CLAUDE.md trap).
  - [x] **2.2 A self-bootstrapping harness, with no scene edits.** Use
        `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]`, armed only when asked:
        - in a build, by `-recordingSpike <mode>` in `System.Environment.GetCommandLineArgs()`;
        - in the Editor, by a flag the editor rig sets before entering play mode (`SessionState`, read
          through `#if UNITY_EDITOR`).
        Find `PhotoModeController`, `EventManager`, `ThirdPersonCamera` and `ThirdPersonController` by type,
        and create every helper object at runtime. In a build it must **finish by calling
        `Application.Quit()`**, with a hard timeout, such as 180 s, after which it quits regardless and logs
        why. **Never save SampleScene with spike objects in it**, and leave it un-dirtied.
  - [x] **2.3 Camera and player control.** This is rig work, so copying a little logic is allowed. The
        existing runners are `#if UNITY_EDITOR` and cannot ship.
        - Disable `ThirdPersonCamera`, because it writes Main Camera's transform every `LateUpdate`
          (`GradeHudShootRunner.cs:130-136`).
        - Call `ThirdPersonController.SetInputSuppressed(true)` (`ThirdPersonController.cs:185`).
        - Place the camera at the recorded vantage and aim it at the drunk each frame.
        - **Find the vantage once, by rendering.** The route is a fixed NavMesh path, so the peak should
          happen at about the same place each cycle. Verify that by logging the drunk's position at
          `→ Peak` over two cycles. Then choose the vantage by *rendering* a few candidates in the Editor
          and looking at them. Story 1.12's 2026-09-10 retraction found that no raycast answers "can the
          camera see him" in this town, and that a vantage picked at scenario start goes stale.
        - ⚠️ **The harness never calls `PhotoModeController.Capture()`.** Each call takes a photograph,
          which means a synchronous `GalleryService` render plus readback of about 40 ms, plus the HUD. That
          would land inside the measurement and pollute it. Raising (`SetPhotoMode`) and zoom (`SetZoom`)
          are the only real-path calls the spike needs.
        - ⚠️ Run any search or linecast **once**, never per frame (memory trap 13: a per-frame search
          stalled a 2.5-min rig).
        - ⚠️ Do not disable the `EventManager`. Disabling it despawns nothing (CLAUDE.md), and this spike
          needs the drunk's real lifecycle.
        - In the Editor, restore every component you disabled.
  - [x] **2.4 The measurement logger** (protocol in 1c). It writes a per-frame CSV, a per-second CSV and a
        `summary.json` to `-spikeOut`. The default when no argument is given is
        `Application.persistentDataPath/recording-spike/`, **never `Temp/`**. It also saves **reference
        frames**: PNGs of the frame source on a few known frame indices (one of them inside the peak). Those
        are the ground truth that (A)'s decoded frames and (B)'s re-rendered frames are compared against.
  - [x] **2.5 An A/V sync marker (a "clapperboard").** At a known instant during recording, show a
        full-frame bright quad **in the 3D view**, parented to the camera just past its near clip plane, for
        exactly one rendered frame. (An Overlay canvas would not be in the footage, per 1a.) On the same frame,
        play a short 1 kHz tone through a 2D `AudioSource` (`spatialBlend 0`, a clip made with
        `AudioClip.Create`, so no asset). Add a second marker near the end to catch drift. In the output,
        measure the offset between the bright frame and the tone onset. Say in the evidence that the marker
        frames are deliberate.
  - [x] **2.6 The first Windows build this project has ever made.** Add an editor menu item,
        `Tools/Recording Spike/Build Spike Player`, that calls `BuildPipeline.BuildPlayer` with:
        - `scenes = { "Assets/Scenes/SampleScene.unity" }`, `target = StandaloneWindows64`,
          `options = BuildOptions.None`;
        - `locationPathName = Builds/1-14-decide-how-the-game-records-video/spike/My project.exe`;
        - `extraScriptingDefines = { "CAMERA_RECORDING_SPIKE" }`. This adds the define to **this build
          only**, without touching `PlayerSettings`. Verify that by reading `PlayerSettings` defines before
          and after.

        Add a twin, `Build Plain Player` (no define, into `.../plain/`), for AC3's fence proof. Write the
        `BuildReport` summary (result, duration, total size, every error and warning message) to the evidence
        folder.
        - ⚠️ Confirm `EditorUserBuildSettings.activeBuildTarget == StandaloneWindows64` first. **Never switch
          platforms**: that reimports the whole project.
        - ⚠️ The first URP build compiles shader variants and can take a long time. The build blocks the
          Editor's main thread, so MCP calls will time out. Launch the build, then poll
          `mcpforunity://editor/state`, the BuildReport file and Editor.log. **No asset operations meanwhile**
          (memory trap 5).
        - If **player** script compilation fails (editor-only API in runtime code), fix it properly per AC4's
          exception, and list each fix.
  - [x] **2.7 The baseline run.** Run the spike build with `-recordingSpike baseline`, using the scenario
        without recording. This is the first proof that the build runs, the town loads, the drunk spawns and
        the peak fires. `GameLog.Info("Events", "TownDrunk → Peak")` lines land in the Player.log. It is also
        the 60-FPS-at-1080p baseline on this hardware, so state it plainly whatever it says.

- [x] **Task 3: Candidate (C), raw frames in memory. Prototype it FIRST, because it isolates the frame-source cost (AC1)**

  (C) is the simplest pipeline, and it is the first half of (A). Measuring it first means that (A) − (C) is
  the encoder's cost, which makes both numbers meaningful.

  - [x] 3.1 Take frames from 1a's source with `AsyncGPUReadback.RequestIntoNativeArray`, into a **pre-allocated
        ring** of `NativeArray<byte>` (`Allocator.Persistent`) sized for the clip. **Never allocate per frame.**
        One owner disposes all of them, which is `GalleryService`'s single-owner rule (`GalleryService.cs:296-330`).
        Track in-flight requests: a request that completes after its slot was reused is a corrupt frame.
  - [x] 3.2 Audio: PCM from `OnAudioFilterRead` on the **AudioListener's GameObject**. In SampleScene that is
        `Main Camera`, which has the scene's only AudioListener. This hook is shared with (A) and (B) (see
        Latest technical information for the threading rules).
  - [x] 3.3 Playback in-game: upload each frame with `Texture2D.LoadRawTextureData` / `SetPixelData` +
        `Apply(false)`, show it on a full-screen `RawImage`, and play the PCM through an `AudioClip.Create`
        clip. Measure the cost of playback too.
  - [x] 3.4 Measure per 1c. **Run record → play → record again into the same ring for ≥ 3 cycles.** In this
        project, bugs have hidden in reuse and in the second cycle, never the first (CLAUDE.md,
        `loop/lessons.md`). Extrapolate RAM to the ~2-min card against this machine's 15.7 GB.
  - [x] 3.5 Export probe: encode the buffered 10 s with (A)'s encoder (Task 4) and time it.
  - [x] 3.6 Optional variant C′, compressed frames in memory (for example
        `ImageConversion.EncodeNativeArrayToJPG` on a worker thread). Try it only if (C) is otherwise
        attractive, and note that it is still not a standard video file.

- [x] **Task 4: Candidate (A), encode a video file while playing (AC1, AC2)**
  - [x] 4.1 **Recommended prototype: an `ffmpeg.exe` child process fed raw frames over stdin.**
        - Start it with `System.Diagnostics.Process`, `RedirectStandardInput`, `UseShellExecute = false`,
          `CreateNoWindow = true`. Take the exe path from an argument, `-ffmpeg <path>`.
        - The local one is `C:/ProgramData/chocolatey/lib/ffmpeg/tools/ffmpeg/bin/ffmpeg.exe`. Point at the
          real exe, not the `chocolatey/bin` shim.
        - Write frames from a **worker thread** draining a **bounded** queue. The main thread never blocks on
          the pipe. When the queue is full, **count and log a dropped frame** rather than stall.
        - Input arguments: `-f rawvideo -pix_fmt rgba -s 1920x1080 -r 30 -i -`, plus `-vf vflip` only if
          1a's control shot shows the readback is flipped.
  - [x] 4.2 **Use LGPL-compatible encoders only:**
        - `h264_mf` (Media Foundation). On this machine it should open Intel Quick Sync's hardware MFT;
          **log which MFT it actually opened**, because a silent fallback to the software encoder changes
          every number.
        - `h264_qsv` as a second row.
        - ⛔ **Never `libx264` for any configuration you would ship.** The local ffmpeg is
          **8.0.1-essentials (gyan.dev), configured `--enable-gpl` with libx264**. That is fine for private
          measurement on this machine and **wrong as a shipping configuration** (Latest technical
          information).
        - The MX350 is expected to have **no NVENC**. NVIDIA's support matrix lists no encoder for the MX
          line. Try `h264_nvenc` once to confirm it fails to open, then record the result.
  - [x] 4.3 Audio and the container. Write PCM from `OnAudioFilterRead` to a WAV, then mux at stop
        (`-c:v copy -c:a aac`). Measure **stop-to-playable-file time**.
        - ⚠️ An MP4 is unreadable until its index (`moov`) is written at close, so a crash mid-clip loses
          everything. That breaks the spirit of "the footage so far is always kept" (FR3). Try fragmented MP4
          (`-movflags +frag_keyframe+empty_moov`) or MKV-then-remux, and say what the ADR should require.
  - [x] 4.4 In-game playback: `VideoPlayer` with `source = Url` pointing at the file, `renderMode =
        RenderTexture` shown full-screen, and audio through `VideoAudioOutputMode.AudioSource`. Measure decode
        cost while playing, and the time from `Prepare()` to the first frame.
  - [x] 4.5 **The Edit Suite and export probe (AC2): this is the "working export path" if (A) wins.**
        - **Trim** about 2 s off each end of one clip. Measure whether a stream-copy trim lands on the
          requested frame or the nearest keyframe, and what re-encoding costs.
        - **Join** two clips, by stream copy (concat) when the parameters match, otherwise by re-encode.
        - Verify the output as in AC2.
        - Note the keyframe interval (GOP) you used. A short GOP (about 0.5–1 s) makes trimming and
          `VideoPlayer` seeking cheaper at some cost in size; measure both if time allows.
  - [x] 4.6 Variant A-MF, **assessed on paper at minimum**: Media Foundation's Sink Writer driven directly
        from C# (COM interop on Mono/Windows). It needs no external exe and uses the OS-provided encoder.
        Prototype it only if (A) leads and shipping a separate ffmpeg executable is the deciding cost.
  - [x] 4.7 Licence and cost row:
        - ffmpeg's LGPL obligations when you ship it as a separate executable;
        - the OS encoders;
        - H.264 and AAC patent notes;
        - the size of the binary;
        - **Windows "N" editions** ship without Media Foundation until the Media Feature Pack is installed.
          That affects both encoding with `h264_mf` and `VideoPlayer` playback of H.264.

- [x] **Task 5: Candidate (B), replay recording (AC1, AC2)**
  - [x] 5.1 **Record at 30 Hz:**
        - the photo camera's position, rotation and `fieldOfView`;
        - for each active `EventActor`: its transform, `isActiveAndEnabled`, and per Animator layer the
          `GetCurrentAnimatorStateInfo(l).fullPathHash` and `normalizedTime`, plus transition state and the
          parameters it uses (`AnimHashes`);
        - audio. **Recommended: PCM from `OnAudioFilterRead`**, as in (C), rather than re-triggering sound
          events. Re-spatialising 3D audio from a recorded viewpoint needs a second AudioListener, which Unity
          does not allow. That is a measured finding, so record the attempt or the reasoning.

        Report bytes per second separately for pose data and for audio.
  - [x] 5.2 **Play back without altering the live game.** That is Story 1.18's AC, and it decides here
        whether (B) is viable at all.
        - Build a **ghost puppet**: an instance of the drunk's *visual* (Animator plus SkinnedMeshRenderers)
          with **no** `EventActor`, `NavMeshAgent` or cue `AudioSource`. **Never take it from the
          `EventManager` pool** (`maxConcurrent` is 1, and taking from the pool would starve the live event).
        - Drive it with `Animator.Play(hash, layer, normalizedTime)` + `Animator.Update(0)`.
        - Render it from a playback camera at the recorded poses into an RT. The live drunk is hidden from
          that camera only, and the ghost is hidden from the main camera. For example, toggle renderers in
          `RenderPipelineManager.beginCameraRendering`/`endCameraRendering`. **Add no layers**: layers are
          project settings (AC4).
        - Prove from the Player.log phase lines, with timestamps, that the live drunk's lifecycle advanced
          normally throughout playback.
  - [x] 5.3 **Fidelity:** compare re-rendered frames against 2.4's reference frames on the same timestamps,
        using SSIM per frame and bone-position drift. **List what replay structurally cannot reproduce
        today**, for example the moving player body if visible, particles, anything time-of-day,
        anything the NavMesh interpolates. Each item becomes a rule every later epic must obey
        (architecture: "everything that moves or makes sound on screen must be recordable").
  - [x] 5.4 Measure per 1c. The recording cost should be small. **The playback cost is a second render of
        the town** (the ~40 ms class of cost from Story 1.12), so measure it rather than assume it.
  - [x] 5.5 Export path: an offline re-render, followed by (A)'s encoder. Stepping a fixed
        `Time.captureDeltaTime` is acceptable here, because export is not gameplay. ⚠️ Story 1.12 paid for this:
        `Time.captureFramerate` does **not** lock `Time.unscaledDeltaTime`. Time the export of the 10-s clip.

- [x] **Task 6: Compare and decide (AC1, AC2)**
  - [x] 6.1 Build the comparison table:
        - rows: (A), (B), (C), plus any variant you measured;
        - columns: the five AC1 measurements, Edit Suite trim/join, export path, implementation risk for
          Stories 1.15–1.18, and **reversal cost**;
        - every cell carries an evidence path. The raw data stays in the CSVs.
  - [x] 6.2 **Decide from the evidence.** "Do not encode expected results" (CLAUDE.md). The architecture's
        expectation that (C) loses is a hypothesis to measure, and (A) winning is not a foregone conclusion
        either. Write the decision into the result file's `decisions` (question, options, chosen, evidence path,
        reversal cost).
  - [x] 6.3 If two candidates are close, prefer the one with fewer moving parts to ship. A separate
        executable, a licence obligation and a new cross-cutting rule each count as cost.

- [x] **Task 7: Write ADR-4 (AC2)**
  - [x] 7.1 **Replace** the "ADR-4 (Clip recording) — OPEN" block in `game-architecture.md` §Architecture
        Decision Records with:
        - **Status:** `Proposed (<date>) — awaiting Alexv's approval`.
        - **The decision**, and a one-paragraph *why*.
        - **The evidence:** the comparison table, compact, with paths.
        - **The footage format:** container, codec, resolution, fps, audio, GOP.
        - **The frame source**, and whether UI is in the footage.
        - **How the memory card counts seconds and frees footage** (AR6, NFR3), and the disk-ready seam
          format Story 1.17 keeps.
        - **How Epic 9 trims and joins**: stream copy or re-encode, keyframe interval, how the in-game edit
          preview plays.
        - **The export path (FR31)** and its exact licence obligations.
        - **The failure story (NFR8):** encoder missing, disk full, crash mid-clip.
        - **Consequences:** the cross-cutting rule if (B) wins, the Windows-N note if Media Foundation is
          involved.
        - **What Story 1.15 builds:** a **narrow backend seam** in `Scripts/Recording/`, so that a veto swaps
          the backend and not the design.
  - [x] 7.2 Update the rest of `game-architecture.md` to match:
        - Decision Summary row 9;
        - §Clip Recording: the `<footage handle per ADR-4>` type in `CapturedClip`, and the `ClipRecorder`
          line;
        - the Executive Summary bullet;
        - the frontmatter `updated:`.

        Every change carries a dated revision note, which is the project's convention. **Do not edit the
        GDD or `epics.md`.** If the decision changes their wording (for example the capacity unit or the
        export format), list it as a follow-up in the Dev Agent Record.
  - [x] 7.3 Add a short entry to `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md`:
        "<date> — ADR-4 proposed: <choice>, pending Alexv", with a pointer to the evidence.

- [x] **Task 8: Prove the chosen approach in the Windows build (AC3)**
  - [x] 8.1 Run the spike build as an **evidence run**:
        `-recordingSpike <chosen> -prove`. It records about 10 s spanning the peak, then plays it back
        through the chosen approach's in-game playback path, full window. Film the window throughout:
        `ffmpeg -f gdigrab -framerate 30 -i title="My project" -c:v libx264 -crf 18 evidence-playback.mp4`.
        libx264 is fine here, because this file is our evidence and is never shipped. Check the window title
        first, and kill the exe and ffmpeg when done. If gdigrab returns black frames from the D3D window, use
        ffmpeg's Desktop Duplication source instead (`-f lavfi -i ddagrab=...` cropped to the window). The
        same rule applies as for any capture: **a black or uniform film means the rig is broken.**
        ⚠️ **gdigrab records no audio.** Prove in-build *audio* playback differently. While the clip plays
        back, the spike's own `OnAudioFilterRead` tap writes what the listener hears to
        `evidence/playback-heard.wav`. Cross-correlate that with the clip's audio track: a clear peak at a
        small lag proves that the build played the clip's sound.
  - [x] 8.2 **Analyse the recorded clip itself:**
        - `ffprobe` shows a duration of ≈ 10 s, 1920×1080, 30 fps, and an audio stream;
        - the audio is non-silent (`astats`/`volumedetect`) and the sync-marker offset is reported;
        - **a labelled contact sheet** shows the drunk across the clip, with the peak-window frames marked
          from Player.log timestamps;
        - duplicated or dropped frames are counted by frame difference.
  - [x] 8.3 **Analyse the gdigrab film of the playback.** Show that the in-build playback reproduces the clip:
        a side-by-side contact sheet of clip frames and playback frames, playback smoothness (frame-to-frame
        difference series), and the audio cross-correlation from 8.1. ⚠️ Verify the subject is genuinely on screen
        in the frames you judge (`tools/verification/README.md`). A motion mask also fires on camera moves.
  - [x] 8.4 Prove that this was not the Editor: the build's `Managed/` folder has no `Unity.Recorder*` DLL,
        and the Player.log shows the run.
  - [x] 8.5 *(Optional, temporal questions only)* Gemini on a short slice of the playback film, with the
        subject present in every frame (verified programmatically first). Ask a descriptive prompt, give
        **no timestamps** and name **no expected defect**, and say explicitly that "nothing" and "I cannot
        tell" are correct answers. **Verify every claim on the cited frame** before writing it down.

- [x] **Task 9: Fence off the prototype, restore the project, check for regressions (AC3, AC4)**
  - [x] 9.1 Fence (or remove, with the reason recorded). Requirements:
        - the asmdef define constraint and `autoReferenced: false` (2.1);
        - the README;
        - **an EditMode test asserting that no game assembly (`CameraGame`, `CameraGame.Editor`) references
          `CameraGame.Spikes.RecordingSpike`**, a cheap structural guard;
        - build the **plain** player (2.6), list `My project_Data/Managed/`, and confirm there is no spike
          DLL. Save the listing as evidence.
  - [x] 9.2 Restore and verify every item in AC4's "Restored" list. Use an editor read-back for
        `PlayerSettings`, the build target, the quality level and the open scene, and `git diff` for the
        assets. `git status` must show only files you meant to change. Leave the untracked `.codex/` and
        `AGENTS.md` alone; they are not yours.
  - [x] 9.3 Run `refresh_unity`, then `read_console` (clean against the AC4 baseline). Run `run_tests`
        EditMode (all pass, and report the count). Run one `Tools > Grading > Photo Shoot (Play)` and diff it
        against its baseline with numbers masked. ⚠️ Memory trap 14: that rig wipes its own output folder,
        so copy the old baseline aside first.

- [x] **Task 10: Handover. Queue what needs Alexv, and never self-certify it**
  - [x] Write up each entry below as a result-file `human_checks` item **and** in the Dev Agent Record, each
        with the exact files to open and your own honest read. The four entries are listed under
        "Human checks to queue" in Dev Notes.
  - [x] Set sprint-status `1-14-decide-how-the-game-records-video` to `review`, never `done`, while the
        ADR-4 approval is pending. Then run `yaml.safe_load` on the file (`last_updated` must contain no `: `
        and no trailing `:`).

## Dev Notes

### What already exists. Read these before writing anything

- **`Assets/Scripts/PhotoMode/PhotoModeController.cs`** contains the real raise path,
  `SetPhotoMode(bool)` (:263), and `SetZoom(float)` (:327). Set zoom through `SetZoom`: `Update` drives the
  FOV from `_zoomT` every frame and would undo an outside write to `fieldOfView`. It also contains `Capture()`
  (:347), which is **unchanged in this story**, and `photoCamera`, the camera to record. Recording the camera
  that is graded is a rule carried over from `GalleryService` ("Two different cameras would store a picture
  of a different view from the one that was scored").
- **`Assets/Scripts/Gallery/GalleryService.cs`** shows how a frame is taken today: a synchronous
  `photoCamera.Render()` plus `ReadPixels` on the shutter frame (:198–267). That is fine for one photograph
  and fatal at 30 fps. It also holds the **single-owner native-memory rule** (:296–330), the template for
  whatever owns footage, and its rationale for why the picture contains no UI.
- **`Assets/Scripts/Core/RigVideoFeed.cs` and `Assets/Scripts/Editor/RigVideoRecorder.cs`** are Unity
  Recorder 5.1.7 filming the rigs. They are Editor-only (`CameraGame.Editor.asmdef` references
  `Unity.Recorder.Editor`). Read the class comment for the two silent-corruption settings
  (`FrameRatePlayback.Variable`, `ExitPlayMode = false`): it shows that a recorder can change the clock it
  is supposed to measure. Do not extend them or use them for evidence about the build.
- **The events:**
  - `EventManager.ActiveActors` (pooled, read live, never cached: the `ISubject` liveness contract), with
    `respawnDelay` 2 s.
  - `EventActor` implements `ISubject`: `Bounds`, `IsAtPeak`, `TimeToPeak`, and `PeakOffset`, which is 0
    anywhere inside the peak window, positive before it and negative after. Use `PeakOffset`, not
    `TimeToPeak`, to find the window. It also logs `"<SubjectId> → <phase>"` on each transition
    (`EventActor.cs:520`) and raises `EventPeakedChannel` at the peak's start (:507).
  - `TownDrunk.asset`: Spawn 3 s, Build 10 s, **Peak 1.5 s**, Wind-down 6 s, Despawn 4 s, so ≈ 24.5 s per
    cycle plus the 2 s respawn.
- **The player:** `ThirdPersonCamera` writes Main Camera every `LateUpdate`, so disable it while you
  script the camera. `ThirdPersonController.SetInputSuppressed(bool)` is at `ThirdPersonController.cs:185`.
- **Audio:** the scene's only `AudioListener` is on `Main Camera`. `Assets/Audio/Mixers/GameAudio.mixer` is
  the one mixer. The drunk's cue is a 3D `AudioSource` on the actor (Doppler 0, custom rolloff; Story 1.8).
- **Rig patterns to copy the shape of:** `Assets/Scripts/Editor/PhotoShootRig.cs` (it saves and restores the
  scene through `SessionState`, validates assets by GUID before touching the scene, and works in the real
  town in its placement study). `Assets/Scripts/UI/GradeHudShootRunner.cs` (real scene, chase camera
  disabled, screen capture proven with control shots, every changed value restored).
- **Logging:** `GameLog.Info/Warn/Error(cat, msg)` (`Assets/Scripts/Core/GameLog.cs`). `Debug_` is stripped
  from release builds, so do not rely on it for build evidence. Use the spike's own CSV/JSON plus
  `GameLog.Info` lines in Player.log.
- **Tooling:** `ffmpeg`/`ffprobe` 8.0.1, plus numpy and Pillow, are installed. Read
  `tools/verification/README.md` before any frame or Gemini analysis.
  `tools/verification/gemini_motion_review.py` reads the key from `C:/Users/alexv/.gemini_key` (UTF-16).
- **Tests:** `Assets/Tests/EditMode/CameraGame.Tests.EditMode.asmdef`. 163 tests passed on 2026-09-27.

### Architecture compliance (non-negotiable)

- **ADR-4 must fit the architecture's §Clip Recording, not redraw it.** That section fixes the
  responsibilities (`ClipRecorder`, `ClipGrader`, `MemoryCardService` evolving `GalleryService`,
  `CapturedClip`, `ClipRecordedChannel`) and the dependency direction: `PhotoMode → Recording → Grading` and
  `Events` (through `ISubject` only), with the memory card and UI listening on the channel, never the reverse.
  ADR-4 fills in the `<footage handle per ADR-4>` type and the backend behind `ClipRecorder`.
- **AR12:** it must work in a Windows build. **NFR1:** 60 FPS at 1080p while recording. **NFR3:** recorded
  footage is bounded by the card, with no unbounded buffer, and deleting frees it. **NFR8:** recording
  failures are fail-soft and never shown to the player as an error. **NFR4:** URP only. The ghost puppet uses
  the drunk's own URP materials.
- `CameraGame.*` namespaces and one game asmdef (AR5). The spike's separate assembly is a deliberate,
  documented exception (Project Structure Notes).
- **Grading (ADR-2) is not touched.** `ShotGrader.Grade(camera, subject, config, out detail)` is a pure static
  function, and Story 1.16 samples it per frame. Nothing in 1.14 changes it.

### The one genuinely new risk in this story

**The project has never been built, and nothing in it has ever been measured outside the Editor.** Every
earlier story's evidence came from Editor play mode. The spike therefore carries three risks that earlier
stories never had:
1. the player may not build at all (player-only compile errors, a long first URP shader compile);
2. the rig cannot be driven through MCP once it is a separate process, so the harness has to script itself
   and leave cleanly;
3. the performance numbers come from a thermally limited laptop with two GPUs.

Do Task 2 (build, run, baseline) before investing in any candidate. If the build cannot be made to work after
real effort, return `failed` with a precise `next_hint`. It is not a blocker: you can fix build errors.

### Project Structure Notes

- **New:**
  - `Assets/Spikes/RecordingSpike/**` (runtime plus `Editor/` assemblies, README, `.meta` files). This is a
    **deliberate deviation** from the `Assets/Scripts/<Feature>/` layout (architecture §Directory
    Structure): it keeps throwaway code out of the game's feature folders and makes the fence visible.
  - The one EditMode test file for the fence guard, in `Assets/Tests/EditMode/`.
- **Updated:** `_bmad-output/planning-artifacts/game-architecture.md` (ADR-4 and its touch points),
  `.../gdds/gdd-My project-2026-05-26/decision-log.md` (one entry), and `sprint-status.yaml`.
- **Possibly updated:** minimal build fixes in runtime scripts (AC4 exception).
- **Not created in this story:** `Assets/Scripts/Recording/` and `Assets/Data/Recording/` belong to
  Story 1.15+. Do not create them early.
- **Outside git:**
  - builds go to `Builds/1-14-decide-how-the-game-records-video/{spike,plain}/` (git-ignored);
  - evidence goes to `_bmad-output/verification/1-14-decide-how-the-game-records-video/` (git-ignored).
    Subfolders `measure/`, `evidence/`, `build/` and `export/` are suggested;
  - any download goes to `loop/runtime/downloads/1-14-decide-how-the-game-records-video/`, with its source
    URL and licence next to it. **Never run an installer.** No download should be needed: the local ffmpeg
    covers measurement.
- **Never commit** a binary encoder (ffmpeg.exe, DLLs) into `Assets/` or anywhere in git. The repo is
  **public** (memory: public-repo-gdd-published), and committing an LGPL or GPL binary would create
  distribution obligations for the whole repository. If the chosen approach ships an external exe, ADR-4
  specifies how the build step copies it in from outside git.

### Previous Story Intelligence (1.9–1.12, the 2026-11-10 correct course, and the loop)

- **Decide by measuring, then photograph the alternatives.** Story 1.10 settled prominence and centring by
  building both, photographing matched pairs, and judging. The rule-of-thirds bonus that "everyone knows" is
  right lost. The proposal asks for the same here ("decide by measuring").
- **The town costs about 6× the private world.** Story 1.11 measured 6.036 ms for capture in a grey-plane
  world. Story 1.12 measured **39.9 ms mean / 85.1 ms worst** for the same synchronous render plus readback in
  the real town. Any per-frame second render or synchronous readback is in that class. That is why 1a
  recommends copying the existing frame and reading it back asynchronously.
- **Frames from `cam.Render()` contain no Overlay UI, and Overlay UI is invisible to `cam.Render()` rigs**
  (CLAUDE.md, Story 1.12 Task 1a). The same fact decides what the footage shows (1a) and how you prove it
  (control shots).
- **Capture timing traps already paid for:**
  - Grabbing the frame right after an event catches transients. Story 1.12 photographed the shutter flash
    at full white on every still, so take settled frames.
  - `Time.captureFramerate` does not lock `Time.unscaledDeltaTime`; Story 1.12's first recording played
    about 11% fast. Print the honest encode rate.
- **A rig lies before the code does.** Examples: Story 1.12's "nobody there" scenario photographed a live
  drunk because disabling the manager despawns nothing; a 2026-09-09 "bounds are broken" diagnosis was the
  rig's own stale vantage; a 2026-09-12 review nearly reported a regression that was a stale asset. **Every
  case identical, every case passing, a round number, or a blank or white capture means suspect the rig.**
- **Stale assemblies run silently.** `refresh_unity` can report success without compiling (memory trap 10).
  Compare `Library/ScriptAssemblies/*.dll` timestamps with your sources before trusting a run. The *player*
  build compiles its own scripts, so this matters most for Editor-side iteration.
- **Single owner of native memory** (Story 1.11): whoever creates a `Texture2D`, an RT or a `NativeArray`
  destroys it. Widening a channel payload to carry textures was rejected because then *nobody* owns them.
  ADR-4's footage handle must have exactly one owner (the memory card, per the architecture).
- **The correct course's evidence** (proposal §1): a raw 1080p frame is ≈ 8.3 MB, so 10 s at 30 fps is ≈ 2.5 GB.
  The proposal also flagged that FFmpeg builds with libx264 are GPL, and named Media Foundation and paid
  plugins as the alternatives.
- **Stakeholder answers (2026-11-10):** Record is a **toggle**, clips **include game sound**, the card is
  **~2 min** to start, and **export (FR31) is confirmed**. ADR-4 must serve all four.
- **`loop/lessons.md`:** run reuse cycles; read values back after `refresh_unity`; Overlay UI needs
  `ScreenCapture` after `WaitForEndOfFrame`; keep `last_updated` YAML-safe; Gemini fabricates when it is
  given timestamps or an expected defect; deactivate actors to empty a street.

### Inherited context you should not rediscover

- **CLAUDE.md traps:**
  - an Editor-only MonoBehaviour on a scene object is "missing" in play mode;
  - `ScreenCapture.CaptureScreenshot` (the file overload) produces blank white while the Editor runs
    unattended, so render to an RT or use `CaptureScreenshotAsTexture` after `WaitForEndOfFrame`;
  - assets loaded before `NewScene(Single)` lose their native half;
  - `ProjectSettings/*.asset` edits on disk do not apply while the Editor runs, so use the API;
  - `Temp/` is deleted on shutdown;
  - a debug readout that outlives its instant gets read against the wrong frame, so label snapshots.
- **Memory notes:**
  - `read_console` hides `Debug.Log`, so grep `%LOCALAPPDATA%\Unity\Editor\Editor.log` (and the Player.log
    you pass with `-logFile`);
  - never run asset operations during play mode;
  - save a scene in a **separate** `execute_code` call before launching a rig (an "unsaved changes" dialog
    makes one menu call into six);
  - a rig can stall in `playmode_transition` for a long time, so poll `mcpforunity://editor/state` before
    calling it hung;
  - poll the moving object, never the anchor.
- **Unity MCP for Unity cannot create custom ScriptableObject assets.** Write the `.asset` YAML directly with
  the script's `.meta` GUID (memory: create-so-asset-via-yaml). This spike should not need any, because all
  of its config is command-line arguments.
- **Compile-check without the Editor** is possible with Unity's bundled Roslyn (memory:
  compile-check-without-unity-mcp), useful if MCP drops during long builds.
- **Code review excludes test rigs** (memory). The spike harness is a tool. Reviewers will look at
  ADR-4, any build fixes to shipped code, and the fence.

### Git Intelligence

- `HEAD` is `b343a88`. The last five commits (`755aaa9..b343a88`) are project housekeeping: Jira removed,
  stakeholder answers, the Recorder 5.1.7 bump and the unattended loop. **No game code has changed since
  Story 1.12 closed**, so the code you read above is current.
- The working tree is clean apart from the untracked `.codex/` and `AGENTS.md`. They are not yours, so do not
  stage them. **Stage by name** (`git add <path> <path>.meta`), never `-A` (the loop guard refuses it).
- Commit as you go on `loop/1-14-decide-how-the-game-records-video`, ending each message with
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never switch branches, merge, rebase, reset,
  stash or push.

### Latest technical information (verified for this project's versions)

*Versions:* Unity **6000.3.8f1**, URP **17.3.0** (RenderGraph), Input System 1.18.0, Recorder 5.1.7 (Editor
only), scripting backend **Mono** (Standalone default; `scriptingBackend` unset), API compatibility
**.NET Standard 2.1** (`apiCompatibilityLevel: 6`, which makes `System.Diagnostics.Process` available),
`com.unity.modules.video` and `screencapture` enabled, color space Linear, HDR on, vSync off
(`vSyncCount: 0`), `runInBackground: 1`, `visibleInBackground: 1`, `companyName: DefaultCompany`, so
`persistentDataPath` is `%USERPROFILE%/AppData/LocalLow/DefaultCompany/My project`.

*Web research done on 2026-11-10 for this story. Each fact carries its source. Anything marked
"unconfirmed" is a hypothesis for the spike to test, not a fact to build on.*

**Unity's own APIs**
- **`UnityEditor.Media.MediaEncoder` is Editor-only.** The docs say it is "not included in Player builds"
  (https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/media/mediaencoder). Unity has **no
  runtime video encoder**: "Runtime encoding" sits on the public roadmap as *Under consideration*. That is
  why this spike exists.
- **`AsyncGPUReadback`** offers `Request`, `RequestAsync` and `RequestIntoNativeArray`/`NativeSlice`
  (plus async forms), and `WaitAllRequests`. It "adds a few frames of latency", with no fixed count. Texture
  overloads take a `TextureFormat`/`GraphicsFormat` and convert for you. ⚠️ **A NativeArray passed to
  `RequestIntoNativeArray` cannot be disposed until its request completes.** Track in-flight slots
  (https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/rendering/asyncgpureadback). No
  trustworthy 1080p cost benchmark exists, so measure it. 1080p RGBA32 is 8,294,400 B per frame, ≈ 249 MB/s
  at 30 fps.
- **`ScreenCapture.CaptureScreenshotIntoRenderTexture`** is designed to pair with `AsyncGPUReadback` (call it
  after `WaitForEndOfFrame`). Its output is **vertically flipped on D3D**, and Unity closed that issue as "by
  design", with a `Graphics.Blit` to fix it
  (https://issuetracker.unity3d.com/issues/graphics-capturescreenshotintorendertexture-output-is-flipped-in-the-editor).
- ⛔ **`AudioRenderer` cannot capture gameplay.** It does exist in players, but "Recording mutes the live
  output, so it's not suitable for capturing audio during gameplay", and it moves mixing onto the main thread
  (https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/audiorenderer). Do not use it for
  live capture. It is acceptable only for an *offline* export render (B's export).
- **`OnAudioFilterRead` on the AudioListener's GameObject is the live game-audio tap.**
  - It runs on the **audio thread**, about every 20 ms, with interleaved floats in [−1, 1]
    (https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/monobehaviour/onaudiofilterread).
  - An effect on the listener "will be applied to all audible sounds in the scene"
    (https://docs.unity.com/en-us/engine/6000.3/manual/audio/reference/class-audio-listener.md).
    AVPro Movie Capture's own audio capture works exactly this way.
  - ⚠️ An `AudioSource` with **`bypassListenerEffects`/`bypassEffects` = true is missed.** The drunk's cue
    has both at **0** (`EventActor_Drunk.prefab:884-885`), so it will be captured. Write that rule into
    ADR-4, because every future sound must keep it.
  - No Unity page says outright that this works in a *build*. That is strongly implied but **unconfirmed**,
    and the spike settles it.
  - Rules for the callback: no Unity API calls, no allocations, copy into a preallocated ring under a short
    lock, and record the timestamps needed for A/V sync.
- **`VideoPlayer`**
  - On desktop, `url` can be any file path (https://docs.unity.com/en-us/engine/6000.3/manual/video/sources/reference).
  - H.264 is the "optimal supported" codec; WebM means VP8 + Vorbis. **Windows decoding goes through Media
    Foundation** (https://docs.unity.com/en-us/engine/6000.3/manual/video/sources/file-compatibility).
  - Audio output modes are None, AudioSource, Direct and APIOnly.
  - ⚠️ **Frame-accurate seeking is not documented.** Seeks are slow when keyframes are sparse, and issue
    reports advise about one keyframe per second. Measure it, because the Epic 9 preview depends on it.
  - A Unity 6.0-beta issue blamed Media Foundation's MP4 source for mishandling timestamps (loop glitches).
    Watch for playback stutter at the loop point.
- **Windows N/KN editions** ship without the Media Foundation codecs (including H.264) until the user installs
  the Media Feature Pack (https://support.microsoft.com/en-us/kb/3133719). That breaks **both** `h264_mf`
  encoding and `VideoPlayer` H.264 playback. ADR-4 must say what happens there (a fail-soft message or a
  VP8/WebM fallback).

**Encoding through FFmpeg**
- **keijiro/FFmpegOut.** The plugin is MIT, but its binaries package is **GPL**, and it has not been
  maintained since 2020-10. Its "H.264 Default" preset is just `-pix_fmt yuv420p`, which **silently selects
  libx264 in a GPL build**. It is video-only. Read `FFmpegPipe.cs` as reference code (it uses dedicated copy
  and pipe threads, and stdin redirect, launching from `StreamingAssets`), but **do not import it**
  (https://github.com/keijiro/FFmpegOut).
- **Licences.** FFmpeg's configure lists libx264/libx265 as GPL and libfdk_aac as non-free. BtbN's `*-lgpl`
  Windows builds exclude the GPL-only libraries, and their build scripts include nvenc (ffnvcodec), amf,
  onevpl/QSV, libvpx, svtav1 and openh264 (https://github.com/BtbN/FFmpeg-Builds). **Whether `h264_mf` is in
  the BtbN binary is unconfirmed.** If an LGPL ffmpeg becomes the shipping choice, check with
  `ffmpeg -encoders`. gyan.dev builds, the local one included, are **GPLv3** (https://www.gyan.dev/ffmpeg/builds/).
- **What LGPL compliance means when you ship ffmpeg.exe** (https://ffmpeg.org/legal.html): build without
  `--enable-gpl`/`--enable-nonfree`; ship or host the exact corresponding source; put an LGPL notice in the
  About screen and EULA; allow reverse engineering in the EULA; do not rename the binaries. A separate exe
  driven through pipes is normally a *separate program* under the GPL FAQ, so the obligations attach to the
  exe and not to the game (an inference, not legal advice).
- **Patents (flag them; do not decide them).** Via LA's H.264 licence covers end-product encoders and decoders:
  the first 100,000 units a year are free, then $0.20 per unit, and OS-provided codecs fall in a separate
  category (https://www.via-la.com/licensing-2/avc-h-264/). AAC has its own pool. The claim that an app that
  only *calls* Windows' built-in H.264 is covered by Microsoft's licence is **unconfirmed**. This is a
  business and legal question for Alexv, so put it in ADR-4 as an explicit open item, never as a settled fact.
  VP8, VP9 and AV1 are the royalty-free alternatives if it matters.

**Media Foundation directly (variant A-MF)**
- The built-in H.264 encoder MFT (`mfh264enc.dll`) supports Baseline, Main and High profiles; CBR, VBR and
  quality modes; low latency; and B-frames off by default. ⚠️ **Its input must be YUV** (NV12, I420, IYUV,
  YUY2 or YV12), **not RGB**. "If a certified hardware encoder is present, it will generally be used"
  (https://learn.microsoft.com/en-us/windows/win32/medfound/h-264-video-encoder). The Sink Writer flow is
  `MFCreateSinkWriterFromURL → AddStream → SetInputMediaType → BeginWriting → WriteSample → Finalize`
  (https://learn.microsoft.com/en-us/windows/win32/medfound/tutorial--using-the-sink-writer-to-encode-video).
  There is no maintained open-source Unity wrapper.
- **Worth measuring for (A) and (C), whichever encoder you use:** convert RGB to **NV12 on the GPU** (a compute
  shader or blit) **before** the readback. NV12 is 1.5 B/px instead of 4, so a frame shrinks from 8.3 MB to
  3.1 MB, about 62% less readback bandwidth, and it is the format the encoder wants anyway.

**Paid plugins (assess from these docs only; buying or starting a trial is a blocker)**
- **AVPro Movie Capture** (RenderHeads): Trial free and watermarked; Basic $100; Desktop $300; Ultra $500.
  It "works in editor and standalone builds", captures Unity audio in realtime, and on Windows uses Media
  Foundation (hardware H.264 and HEVC, AAC) with no bundled codecs. No published 1080p60 figures
  (https://renderheads.com/content/docs/AVProMovieCapture/articles/download.html, .../features.html,
  .../codecs.html). It is the "buy, not build" option for (A), and **any purchase is Alexv's call**.
- **NatCorder** is discontinued and replaced by **VideoKit**, which needs a subscription (recordings without
  one are downsized and watermarked). **Evereal Video Capture Pro** (€45.99) has had no release since 2021.

**Replay (B)**
- `Animator.StartRecording`/`StartPlayback` exist at runtime, but the recorded data **cannot be read or
  serialised**, so they are useless for a memory card. Record state yourself. `Animator.Play(hash, layer,
  normalizedTime)` scrubs a state.
- `NavMeshAgent.updatePosition = false` lets a script drive the transform; switching it back snaps the actor
  to the agent's position. That is irrelevant to the ghost, which has no agent.
- **Ultimate Replay 3.0** (~$35/seat; its trial is Editor-only) records transforms, animation, particles and
  audio *events* (re-triggered sounds). That is the industry norm, and it explains why PCM audio is
  recommended in 5.1.

**Sizes to expect**
- YouTube recommends **8 Mbps for 1080p at 24–30 fps** in H.264/AAC
  (https://support.google.com/youtube/answer/1722171). That is ≈ 1 MB/s, so a 2-min card holds ≈ 120 MB on disk.
- Raw (C) at 1080p30 RGBA is ≈ 30 GB per 2 min, or ≈ 11 GB even as NV12. That is the arithmetic; the spike's
  job is the measurement.
- libvpx VP9 at realtime speed 5 used ≈ 82% of a quad-core i5 to encode 1080p at about 1× realtime
  (https://developers.google.com/media/vp9/live-encoding). Software VP9 alongside the game on this 4-core
  laptop is therefore unlikely to fit, but measure it if a royalty-free path matters.
- NVENC throughput is irrelevant here, because the MX350 has no NVENC (verify in 4.2).

**A hybrid worth measuring, because the research suggests it may beat all three:**
- **(B) for the memory card and in-game playback**: tiny storage, trivial trim and join, and no realtime
  encoder during play.
- **plus an offline re-render and encode at export only**: outside the 60 FPS budget, with `AudioRenderer`
  allowed there.

Its costs are (B)'s fidelity limits and the cross-cutting "everything must be recordable" rule. If the
numbers put it ahead, it is a legitimate ADR-4 answer. AC1 says "at minimum" (A), (B) and (C).

### Verification plan: what would convince a sceptic

| Claim | Evidence | How it is judged |
|---|---|---|
| The build is real and is not the Editor | `build/BuildReport-*.txt`, `Managed/` listings, Player.log | the BuildReport result is `Succeeded`; no `Unity.Recorder*` DLL; the log shows `TownDrunk → Peak` |
| The frame source captures what the camera saw | control-shot pair plus SSIM and mean-colour diff | high SSIM, colours and orientation correct, not uniform or white |
| Recording cost per candidate | `measure/<cand>-run<N>-frames.csv`, `summary.json` | the delta against the interleaved baseline of the same run, ≥ 3 runs, order rotated, on mains power |
| Memory and disk per second | per-second CSV, file sizes, ffprobe | the candidate's own accounting agrees with private bytes; any extrapolation is labelled |
| Game sound is in the footage | audio track stats, sync-marker offset, RMS against distance | non-silent, rises as the drunk nears, offset reported in ms |
| Playback quality | SSIM/PSNR against reference frames, dup/drop counts, contact sheets | numbers first; "looks acceptable" goes to `human_checks` |
| Working export (FR31) | `export/` trimmed and joined MP4, ffprobe, the silent full decode, contact sheet | the streams are right, the decode is silent, the drunk is in the frames |
| The chosen approach works in the build | `evidence/` clip, gdigrab film, side-by-side sheet, `playback-heard.wav` | ≈ 10 s, contains the peak, plays back in the build, and the heard audio cross-correlates with the clip's audio |
| Fenced off | plain-build `Managed/` listing, guard test | no spike DLL; the test passes, and fails if you add the reference (try it once) |
| Nothing else changed | `git diff --stat`, console, test count, photo-shoot diff | only intended files changed; clean against the baseline; 0 masked diffs |

**Fail-soft scenarios the rig provokes on purpose** (say so at the top of the output so a clean-console check
can tell them apart from findings): a wrong ffmpeg path, ffmpeg killed mid-clip, and a full readback ring.
Each must log once and leave the game running.

### Human checks to queue (never self-certify)

1. **AC2: approve ADR-4.** Alexv reads the ADR and the comparison table, and approves or vetoes. Give the
   reversal cost.
2. **AC1/AC3 playback quality.** He watches `evidence/` playback (the gdigrab film) and the clip itself,
   and decides whether it looks like acceptable camcorder footage. Include your measured read (SSIM, drops,
   compression artefacts you saw).
3. **AC1 sound.** He listens to the clip: is it the game's sound, clean (no crackle or gaps), and in sync
   with the picture? Include the measured offset and your read.
4. **NFR1 absolute** (only if the laptop baseline misses 60 FPS). The recording delta was measured, but the
   60 FPS verdict on a mid-range desktop is outstanding. Say exactly what to run (the spike exe plus its
   arguments) and on what machine.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.14] — the three ACs (verbatim above), AR12, FR3, FR8, FR27–FR31, NFR1–NFR3, NFR8
- [Source: _bmad-output/planning-artifacts/sprint-change-proposal-2026-11-10.md#1. Issue Summary (Evidence 3–5), #Technical impact, #P5, #P15, #Implementation notes] — why the spike exists, the memory maths, the licence flag, the stakeholder answers
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Clip Recording (added 2026-11-10)] — responsibilities, `CapturedClip`, dependency direction
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Architecture Decision Records] — the ADR-4 OPEN block to replace, the candidates, the (B) cross-cutting consequence, the export-path warning
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md:181-183, :230-231, :342, :348, :357, :404] — the recording action, the ~2 min card, the REC UI, the beeps, the build constraint, the 60 FPS metric
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md#2026-11-10] — video replaces photo; stakeholder answers; the loop's "decide, flag, keep going" and a supervised pilot on 1.14
- [Source: _bmad-output/implementation-artifacts/1-11-minimal-gallery.md#Dev Agent Record] — single-owner native memory, the 6.036 ms private-world capture
- [Source: _bmad-output/implementation-artifacts/1-12-grade-feedback-hud.md#Completion Notes List] — the 39.9 ms town capture, Overlay versus `cam.Render`, the capture-flash and `captureFramerate` traps
- [Source: Assets/Scripts/Gallery/GalleryService.cs:10-41, :198-267, :296-330] — no UI in pictures, synchronous readback, ownership
- [Source: Assets/Scripts/Core/RigVideoFeed.cs, Assets/Scripts/Editor/RigVideoRecorder.cs] — Recorder is Editor-only, and the clock-corruption settings
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:263, :327, :347] — the real raise, zoom and capture paths
- [Source: Assets/Scripts/Events/ISubject.cs, EventActor.cs:507, :520; Assets/Data/Events/TownDrunk.asset] — the peak window, the peak raise, the phase log lines, the lifecycle timings
- [Source: CLAUDE.md#Verifying Your Own Work] — rigs, captures, restore, evidence location, traps
- [Source: tools/verification/README.md] — frame analysis first, Gemini prompt rules
- [Source: loop/lessons.md, loop/dependencies.yaml, loop/README.md] — loop pitfalls, story dependencies, what the orchestrator trusts

## Dev Agent Record

### Agent Model Used

Claude Opus 5.5 (`claude-opus-5-5`), loop DEV phase, 2026-11-10.

### Debug Log References

All evidence is under `_bmad-output/verification/1-14-decide-how-the-game-records-video/` (git-ignored; rebuild with `Assets/Spikes/RecordingSpike/README.md`).
`build/BuildReport-*.txt` · `evidence/control-*` · `survey/vantage-sheet.png` · `measure/analysis.json` and
`measure/measure-*` (battery) · `measure-battery.log` · `dev/contention-test.txt` · `evidence/evidence-B-*`
(AC3 proof) · `evidence/evidence-failsoft-*` · `evidence/edit-contact-sheet.png`, `clip-contact-sheet-peak.png`,
`playback-side-by-side.png`. Scripts: `run.sh`, `battery.sh`, `evidence.sh`, `analyse.py`, `audio_check.py`,
`ssim.py`, `sheet.py`, `procmem.ps1`.

### Completion Notes List

**Task 1: the four gating decisions (each one decided, with its reasoning)**
- **1a, frame source:** I followed the recommendation, a URP pass enqueued from script in
  `beginCameraRendering` at `AfterRenderingPostProcessing`, blitting into an sRGB RT read with
  `AsyncGPUReadback`. No asset change, no second render, no `ReadPixels`. **Control shot in the build:**
  the tap and `ScreenCapture` on the same frame, with the Overlay canvases hidden, were byte-identical
  (SSIM 1.0000, mean difference 0.00, not uniform: std 51). With the UI shown, only the screen picked up the
  viewfinder (difference 17.6), so the two paths are genuinely different. Readback rows are bottom-up
  (Texture2D order), so every encoder needs `vflip`. UI is not in the footage.
- **1b, format:** I followed the recommendation: 1080p30 for every candidate. A frame is taken whenever
  game time crosses the next 1/30 s boundary, with repeat counts so the duration stays true. Audio was read,
  not assumed: 48000 Hz Stereo. The 720p row for the winner: (B) records no frames, so 720p only changes the
  export, which took 29.5 s for 10 s (1080p: 24–42 s).
- **1c, protocol:** release build, windowed 1920×1080 (confirmed in every log), the MX350 picked, battery
  `Full` on mains (no run void), the same scripted scenario (the real `SetPhotoMode(true)` and `SetZoom(0.25)`,
  a fixed vantage aimed at `Bounds.center`), ~10 s pre-baseline, 10 s recording across the peak, 10 s
  post-baseline, then ~10 s of the candidate's own playback. **3 rounds with the candidate order rotated**,
  every frame logged (`ProfilerRecorder` "Main Thread" is valid in a release player), memory sampled once a
  second. **Revised on evidence, and flagged:** the camera tracks the drunk, and frame cost changes up to 4×
  across his cycle, even in baseline runs. The laptop also heats up run after run (pre-window: 83 fps cold,
  ~50 fps warm). The pre and post windows of one run are therefore *not* equivalent views. The reported cost
  is a **difference-in-differences**: (rec − pre) of the candidate minus (rec − pre) of the same round's
  baseline-mode run, whose recording window is the same drunk segment and camera path with nothing captured.
  Frame Timing Stats was not enabled, so there was nothing to restore. Memory: Mono's
  `Process.PrivateMemorySize64` reads **0** in this player, so private bytes come from an **external**
  PowerShell sampler, with Unity's allocator totals as a second column.
- **1d, approval:** ADR-4 is written as **Proposed**. The approval is queued as both a decision and a human
  check, and the story stops at `review`. The gap is flagged: `loop/dependencies.yaml` treats perceptual-only
  pending checks as satisfiable, and this is a decision, so **1.15 should wait for Alexv**. If the loop goes
  ahead anyway, 1.15 must build behind ADR-4's `IClipBackend` / `IFootage` seam. `loop/` was not edited.

**Task 2: scaffold and build.** I fenced the assembly (2.1). The **`||` define constraint works**: the plain
build has no spike DLL. The harness bootstraps itself (`RuntimeInitializeOnLoadMethod`, armed only by
`-recordingSpike` or SessionState) and quits on its own, with a hard timeout (2.2). The vantage was **found by
rendering** (2.3): the peak landed on the same spot twice (25.94, 0.79, 19.00). The first survey used a 1.6 m
camera and filmed only legs, because **the world is ~5× human scale** (the player's CharacterController is
8.86 m tall), so eye height is 7.5 m. I chose `survey-03` (18 m, 135°) by looking at the 16 candidates. The
logger, reference frames and clapperboard are in place (2.4, 2.5). The quad uses `UI/Default`, which ships in
the build. The **first Windows build of the project succeeded in 5 min 23 s, with 0 errors and no game-code
fix needed** (2.6). `PlayerSettings` defines were `''` before and after. The baseline (2.7): ~90 fps in the
pre-window, but only **33–52 fps in the recording window**, even with nothing recording.

**Tasks 3–5: the three candidates**, with the full table in ADR-4.
- (C) costs +30.2 ms per frame and 6.2 GB of private memory for 10 s (baseline 1.04 GB). Extrapolating, the
  2-min card would need ≈ 30 GB. It ran 3 record→play cycles in one ring with 0 drops and 0 readback errors.
  Upload costs 4–21 ms per frame. Exporting the 10 s took 29 s. C′ was skipped because (C) is not attractive.
- (A) costs +10.9 ms per frame and loses 197–220 captures in 10 s. Three findings on the way:
  1. Mono's `Process` stdin starves ffmpeg at ~6 fps. Loopback TCP keeps up at 29–30 fps in transport-only
     and conversion-only diagnostics, so I kept TCP.
  2. The **Intel hardware encoder is starved while the game renders**, through both `h264_mf` (which opened
     "Intel Quick Sync Video H.264 Encoder MFT") and `h264_qsv`: ~68 fps idle, 6.6 fps with the game running,
     even for a standalone ffmpeg encoding a file on disk. NVENC fails to open on the MX350, as expected.
     Capping the game at 60 fps did not rescue it (189 drops).
  3. `h264_mf` delivers no extradata in time for MP4, so I record MPEG-TS and remux it, and the TS survives
     a kill.

  `VideoPlayer` failed to prepare in 3 of 4 cycles. After it, the player crashed on quit (exit 139, Media
  Foundation DLLs on the stack) in 3 runs and hung in 1. The root cause was not isolated. The (A) trim/join
  probe (4.5) was not run because (A) lost. The GOP was 30. A-MF and the licence row are on paper, in ADR-4.
- (B) costs **+0.8 ms per frame (−4.2…+3.6), with 0 drops**, at 87 B per tick (2.6 KB/s) plus PCM. Audio is
  the listener PCM: re-spatialising from a recorded viewpoint would need a second AudioListener, which Unity
  forbids. The ghost (a stripped copy, never taken from the pool) re-renders with **bone drift 0.00 cm and
  SSIM 0.992**. I checked that suspiciously round 0.00 by eye: the difference image is black on him. The live
  lifecycle kept advancing during playback (`→ Spawn` and `→ Build` were logged mid-playback). It **cannot
  reproduce** shader-time sway (trees and grass differ), particles, or anything not in the track. **Playback
  runs at 17–21 fps** against 53–58 for the live game. Turning the live camera off (`-solo`) did not help
  (19.0 fps), and the cause is not isolated: this is a **risk for Story 1.18**. The export took 24–42 s.

**Tasks 6–7:** I decided **(B) replay, with MP4 made only at export**. ADR-4 replaced the OPEN block. I updated
Decision Summary row 9, §Clip Recording (the `IFootage` handle and the `ClipRecorder` line), the Executive
Summary and the frontmatter, and added the decision-log entry. **Follow-ups for the GDD and epics, which I did
not edit:** the export format (MP4 H.264+AAC); Windows N has no export; the "everything recordable" rule; the
1.18 playback-cost risk.

**Task 8: AC3, proven in the build.**
- `evidence-B` recorded two takes across the peak (peak frame 150 of 300) and played them back in the build.
  The playback reproduces the recording (SSIM 0.993 on the same frame). Playback advanced monotonically
  through every tick (0..293), but ran at **9.6 fps displayed while it filmed itself**.
- The audio heard during playback contains **both clapperboard tones 8.98 s apart, as in the clip**. The
  live game never plays them, so the build played the clip's own sound, about 45 ms after it started.
- The Edit Suite export, trim 2.00 s + join, gave **one 16.03 s MP4**: H.264 1920×1080 @ 30 and AAC 48 kHz
  stereo, with a silent full decode and the drunk in every sampled second.
- **gdigrab was impossible: the workstation was on the Windows lock screen** ("Failed to capture image
  (error 8)"; a desktop grab showed the lock screen). The build filmed itself instead: 44 + 54
  `ScreenCapture` grabs of what it displayed, plus a per-frame playback log.
- Not the Editor: this was a player log from `My project.exe`. **Deviation:** `Unity.Recorder.dll` and
  `Unity.Recorder.Base.dll` are in both builds' `Managed/` as Recorder package runtime defaults. A reflection
  check shows that neither `CameraGame` nor the spike references them.
- Gemini (8.5, optional) was skipped: there is no window film, and the still-frame questions are already
  settled by measurement.

**Task 9: fence, restore and regressions.**
- I chose **fencing over removal**, so the evidence stays reproducible and 1.15 can lift the pieces it needs.
- The plain build's `Managed/` has no spike DLL.
- Guard tests: game assemblies and asmdefs must not reference or name the spike, with a positive control.
  *The first control was wrong*: the spike's Editor assembly only uses a `const`, which C# inlines, so no
  reference is emitted. The test caught that, and it was fixed.
- **EditMode: 168/168 pass** (163 + 5 new). The console is clean against the AC4 baseline: the two
  large-triangle warnings, plus `[Pool]` warnings that existing ObjectPool tests provoke on purpose.
- `Tools > Grading > Photo Shoot (Play)`: 233 lines, **0 masked differences** from the copied-aside baseline.
- **Restore:** read back through the editor: defines `''`, frame timing off, target StandaloneWindows64,
  quality PC, `timeScale` 1, `captureDeltaTime` 0, `targetFrameRate` -1, SampleScene open and not dirty, the
  spike's SessionState flag cleared.
- **Building the player changed four settings files as a side effect:**
  - `PC_RPAsset` shader-prefiltering flags;
  - the URP global-settings resource list;
  - the Input System actions left in `preloadedAssets`, plus a Standalone batching entry in
    `ProjectSettings.asset`;
  - `UnityConnectSettings m_Enabled` 0→1.

  All four were restored from `HEAD` (preloaded assets were cleared through the API as well) and reimported.
  The editor read `PC_RPAsset` back as the restored value, and `git status` is clean for them.

**AC4 build-fix exception:** not used; the project built without touching game code.

**Fail-soft (NFR8), provoked on purpose in `evidence-failsoft`.** A wrong ffmpeg path, ffmpeg killed mid-clip,
and a full raw ring each **logged once, and the game kept running** (876, 718 and 825 frames). The partial TS
of the killed encode decodes 35 frames.

**Left for Alexv** (queued as `human_checks`; never self-certified):
1. Approve or veto ADR-4.
2. Judge playback quality.
3. Judge the sound.
4. NFR1 at 60 FPS on a mid-range desktop. Run the spike exe from
   `Builds/1-14-decide-how-the-game-records-video/spike/` with `run.sh`'s arguments: `-recordingSpike
   baseline` then `B`, three rotated rounds each, and `python analyse.py measure`.

### File List

- `Assets/Spikes.meta` (new)
- `Assets/Spikes/RecordingSpike.meta`, `Assets/Spikes/RecordingSpike/README.md` (+ `.meta`) (new)
- `Assets/Spikes/RecordingSpike/CameraGame.Spikes.RecordingSpike.asmdef` (+ `.meta`) (new)
- `Assets/Spikes/RecordingSpike/SpikeArgs.cs`, `SpikeHarness.cs`, `FrameTap.cs`, `AudioTap.cs`, `Sinks.cs`, `ReplaySpike.cs` (+ `.meta`) (new)
- `Assets/Spikes/RecordingSpike/Editor.meta`, `Editor/CameraGame.Spikes.RecordingSpike.Editor.asmdef` (+ `.meta`), `Editor/RecordingSpikeMenu.cs` (+ `.meta`) (new)
- `Assets/Tests/EditMode/RecordingSpikeFenceTests.cs` (+ `.meta`) (new)
- `_bmad-output/planning-artifacts/game-architecture.md` (ADR-4 and its touch points)
- `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md` (one entry)
- `_bmad-output/implementation-artifacts/sprint-status.yaml`, this story file
- Not changed (verified by `git status` after the restore): `Assets/Settings/*.asset`, `ProjectSettings/*.asset`, `Assets/Scenes/SampleScene.unity`, all game code.

## Change Log

| Date | Change |
|---|---|
| 2026-11-10 | Story context created by `gds-create-story` (loop CREATE phase): 4 ACs (3 verbatim from epics.md + AC4 for no regressions and restore), 10 tasks with 4 gating decisions, verification plan, human checks to queue. Status → ready-for-dev. |
| 2026-11-10 | DEV (loop): spike built and measured in a Windows build (3 rotated rounds); ADR-4 **proposed**: (B) replay + MP4 only at export; AC3 proven in the build; fence proven by the plain build plus 5 guard tests; 168/168 EditMode; restore verified. Status → review (ADR-4 approval pending Alexv). |
