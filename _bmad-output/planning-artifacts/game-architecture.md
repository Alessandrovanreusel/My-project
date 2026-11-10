---
title: 'Game Architecture'
project: 'Camera Game'
date: '2026-05-27'
author: 'Alexv'
version: '1.1'
updated: '2026-11-10'  # ADR-4 proposed by Story 1.14 (replay + export-time encode), awaiting Alexv; Clip Recording — sprint-change-proposal-2026-11-10.md
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8, 9]
status: 'complete'
engine: 'Unity 6.3 LTS (6000.3.8f1)'
platform: 'Windows (StandaloneWindows64)'

# Source Documents
gdd: '_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md'
epics: null
brief: null
---

# Game Architecture

## Executive Summary

**Camera Game** architecture targets **Unity 6.3 LTS (6000.3.8f1) + URP** on Windows
StandaloneWindows64 at 60 FPS / 1080p. The scope is **Epic 1 — the First Playable Slice
(the Catch Loop + the Town Drunk event)**, and the build is brownfield: it extends the
existing first-person controller/camera and the Drunk-Pack animations already in the project.

**Key Architectural Decisions:**

- **First-person for both walk and photo** (conscious GDD deviation; ADR-1) — reuses
  working code; "raise camera" is a mode toggle + FOV zoom on one camera rig.
- **Hybrid screen-space Frame-Read grading** (ADR-2) — frustum/occlusion gate +
  screen-space bounds composition + lifecycle peak-timing. No GPU readback. Fully tunable
  via a `GradingConfig` ScriptableObject.
- **Data-driven Event-Actor lifecycle** (ADR-3) — one generic `EventActor` + per-event
  `EventDefinition` ScriptableObjects + pooled `EventManager`. Future events are *data,
  not code* — ~80% of Epic 3 is unlocked by this single system.
- **Video recording replaces the shutter** (2026-11-10, `sprint-change-proposal-2026-11-10.md`).
  *How* a built game records itself was measured in **ADR-4** (Story 1.14): **proposed 2026-11-10,
  awaiting Alexv: replay recording, with MP4 made only at export**. The Frame-Read grader (ADR-2) becomes the per-frame engine of clip grading.
  See **Clip Recording** below.

**Project Structure:** Hybrid Unity layout (by-type at root, feature folders under
`Scripts/`) with one `CameraGame.asmdef` for the slice and `CameraGame.*` namespaces.

**Implementation Patterns:** 2 novel (Event-Actor Lifecycle, Frame-Read Grading) + 6
standard, all with concrete code. ScriptableObject event channels (`ShotCapturedChannel`,
`EventPeakedChannel`) decouple capture → gallery → (future) reputation.

**Ready for:** the next required GDS step — **`gds-create-epics-and-stories`** — which
will turn the GDD's 8 epics into developer stories that reference this architecture.

---

## Document Status

This architecture document was created through the GDS Architecture Workflow.

**Steps Completed:** 9 of 9 (Complete!)

---

## Project Context

### Game Overview

**Camera Game** — You play a small-town videographer whose camera is always pointed
at the action. The town runs on its own clock; events ignite, peak, and pass. The
game is the thrill of *being there* and getting the shot before it's gone. Core
"Catch Loop": Detect → Position → Frame → Shoot → Grade → Bank.

### Technical Scope

**Platform:** Windows (StandaloneWindows64) — 60 FPS @ 1080p on PC_RPAsset
**Genre:** Adventure (photography / exploration), no-fail living-town sandbox
**Engine:** Unity 6 (6000.3.8f1), URP 17.3.0, New Input System, AI Navigation
**Project Level:** Brownfield — existing controller/camera/animator assets staged
**Architecture focus:** Epic 1 — First Playable Slice (the Catch Loop + Town Drunk)

### Core Systems (Epic 1)

| System | Complexity | Notes |
|--------|-----------|-------|
| Camera State Machine (walk ↔ photo) | High | Two states; raise=hold RMB |
| Clip Recording & Viewfinder | High (novel) | First-person lens, zoom 1×–4×, record=LMB start/stop; approach per ADR-4 |
| Shot Grading (subject + composition) → Clip Grading (+ peak coverage) | High (novel) | Per-frame frame read, sampled while recording |
| NPC Event-Actor lifecycle | High (novel, reusable) | spawn→build→peak→wind-down→despawn |
| Memory Card | Medium | Capacity in seconds; stores graded clips; playback |
| Directional Audio Cues | Medium | Diegetic discovery; gameplay-critical |

*Revised 2026-11-10: the rows were "Photo Capture & Viewfinder (Medium) — capture=LMB",
"Shot Grading (subject + composition + timing)" and "Minimal Gallery (Low–Medium) — persist
captured shots + grades".*

### Technical Requirements

- 60 FPS / 1080p on a mid-range Windows desktop with the drunk event active
- Camera switch (walk ↔ photo) and capture with no perceptible input lag
- Event-actor lifecycle leaks no objects over an extended session (stable memory)
- Zero compile errors; clean console after script changes
- URP only — Standard shader not supported

### Complexity Drivers

1. **Shot grading** — screen-space frame analysis (subject height fraction, placement
   measured from the frame centre, cut-off penalty) blended with peak-timing proximity.
   No off-the-shelf pattern; the slice lives or dies on this feeling fair and legible.
2. **Event-actor lifecycle** — a generic, data-driven actor (state machine + NavMesh
   pathing + animation + self-telegraphing cues) reused by ~80% of future events.

### Technical Risks

- **Camera-state fork (brownfield):** existing `ThirdPersonController` /
  `ThirdPersonCamera` are wired first-person POV, but the GDD calls for
  walk = third-person chase + photo = first-person viewfinder. The walk-state camera
  must be decided before Epic 1. (Class names are stale relative to behavior.)
- **Input plumbing:** PlayerInput "Send Messages" mode; state switching + new photo
  actions need a deliberate approach.
- **Grading feel/legibility risk.**
- **Object-lifecycle leaks** → pooling required for event actors.

---

## Engine & Framework

### Selected Engine

**Unity 6.3 LTS (6000.3.8f1)** with the **Universal Render Pipeline (URP) 17.3.0**

**Rationale:** Already the project's engine (brownfield); 6.3 is the current LTS
(supported to Dec 2027); URP is a hard project constraint. No engine change is
warranted — the existing controller, camera, animator, and town assets are built
against it. Decision is to *validate and continue*, not migrate.

### Engine-Provided Architecture

| Component | Solution | Notes |
|-----------|----------|-------|
| Rendering | URP 17.3.0 | Dual assets: PC_RPAsset (target) / Mobile_RPAsset. Standard shader unsupported. |
| Physics | PhysX (3D) | Movement uses CharacterController (kinematic), not Rigidbody. |
| Audio | Unity Audio (3D spatial) | Gameplay-critical for directional cues; consider 6.3 Enhanced Audio. |
| Input | New Input System 1.18.0 | "Player" action map; PlayerInput in Send-Messages mode. |
| Animation | Mecanim / Animator | CharacterAnimator drives the "Speed" float (Idle/Walk/Run). |
| Navigation | AI Navigation (NavMesh) | For event-actor pathing (the drunk walks a route). |
| Scene Mgmt | Unity Scenes + SceneManager | SampleScene + "Video Camera Game 2.0". |
| Build | Unity Build pipeline | StandaloneWindows64. |

### AI Development Tooling (MCP)

- **MCP for Unity (CoplayDev/unity-mcp)** — already installed. Gives the AI direct
  scene/asset/script/test access inside the Editor. Free, MIT, actively maintained.
- Optional addition: **Context7** for up-to-date Unity API docs lookups.

### Remaining Architectural Decisions

These are NOT provided by the engine and must be decided in the next steps:

1. **Camera state architecture** (walk ↔ photo) — and resolving the first-person /
   third-person fork found in Project Context.
2. **Photo capture & grading approach** — how the game "reads the frame" (subject
   coverage %, placement, peak-timing).
3. **Event-actor system** — state-machine pattern, data-driven config, object pooling.
4. **Gallery persistence** — in-memory vs. disk; data model; how captured images are stored.
5. **Input handling pattern** — action-map switching vs. single map; Send-Messages vs.
   C# callbacks/events for the new photo actions.
6. **Code organization** — assembly definitions, namespaces, manager/service patterns.

---

## Architectural Decisions

### Decision Summary

| # | Category | Decision | Rationale |
|---|----------|----------|-----------|
| 1 | Walk-state camera | First-person for both walk & photo | Reuse working FP code; fastest to a fun slice (solo dev). Conscious GDD deviation. |
| 2 | Camera state mgmt | Lightweight Photo-Mode toggle (enum + guards) on one camera; zoom = FOV lerp | Two modes don't need a state-machine framework; no Cinemachine. |
| 3 | Photo grading | Hybrid screen-space "frame read" (frustum+occlusion gate · screen-bounds composition · lifecycle timing) | Cheap, deterministic, tunable; no GPU readback stalls. |
| 4 | Event-actor system | Data-driven lifecycle FSM + EventDefinition ScriptableObject + NavMesh + Animator + pooled EventManager | The ~80% reusable engine; new events = data, not code. |
| 5 | Input | Extend existing PlayerInput (Send-Messages); add Capture/RaiseCamera/Zoom; gate by mode | Least disruption to working input; one map for the slice. |
| 6 | Memory card persistence | In-memory `CapturedClip` store capped in seconds of footage (evolves GalleryService); disk-ready seam; cross-session by Epic 9 | Video needs a hard bound; editing needs clips to survive. *(Revised 2026-11-10 — was "Gallery persistence: in-memory List<CapturedShot>; defers disk to Epic 4/5".)* |
| 7 | Code organization | CameraGame.* namespaces; feature folders; one asmdef; SO event channel for ShotCaptured | Lean for solo dev; clean dependency seams without over-engineering. |
| 8 | Audio / pooling | 3D AudioSource per actor (~25m rolloff, 1 mixer); generic ObjectPool<T> | Sufficient for the slice; satisfies no-leak metric. |
| 9 | Clip recording | **ADR-4 — Proposed 2026-11-10, awaiting Alexv:** (B) replay recording; MP4 (H.264 via Media Foundation + AAC) only at export | Measured in a Windows build (Story 1.14): (B) costs +0.8 ms/frame over 3 rounds (not distinguishable from 0) and ~195 KB/s as 16-bit, (A) dropped 86–89% of its captures to a starved encoder, (C) needs ~250 MB/s of RAM. *(Added 2026-11-10; revised 2026-11-10 by Story 1.14 and its code review.)* |

### Camera & State

**Model:** Single first-person camera with two modes — `Walk` and `Photo` — owned by a
`PhotoModeController`. Hold RMB → Photo (fade in viewfinder UI, enable zoom); release →
Walk. Zoom lerps camera field-of-view (≈60° → ≈18° for 1×–4×). Raise/lower transition
< 0.3s. Capture-to-feedback < 0.2s (from 2026-11-10: record-start-to-feedback < 0.2s). (Existing `ThirdPersonController`/`ThirdPersonCamera`
provide the FP base; recommend renaming to `Player*`/`FirstPerson*` to match behavior.)

### Photo Capture & Grading

`ShotGrader` service: `Grade(camera, subject, captureTime) → ShotGrade (0–100% → 1–5★)`.
- **Subject gate:** `GeometryUtility.TestPlanesAABB` (camera frustum vs. subject bounds)
  + raycast to subject for occlusion. Fails the shot if subject not visibly in frame.
- **Composition:** project subject world-bounds → screen-space box → **height fraction**
  (gate 0.20, sweet spot 0.45–0.78) and placement vs. the **frame centre**, plus a cut-off
  penalty for the frame edge clipping the subject. *(Revised 2026-07-28 — was "coverage %,
  gate ≥8%, sweet spot 25–50%, nearest rule-of-thirds line"; see FR5/FR6 revision notes.)*
- **Timing:** from event lifecycle — full marks within ±0.5s of the peak **window**, 0 by ±2s
  beyond it (`ISubject.PeakOffset`; the peak is an interval, not an instant).
- Thresholds in a `GradingConfig` ScriptableObject (Inspector-tunable, no recompile).

### Clip Recording (added 2026-11-10)

**Model:** recording replaces the photo shutter as the player's verb (`sprint-change-proposal-2026-11-10.md`).
Holding RMB still raises the camera into the Viewfinder state; the code keeps the name `PhotoMode`. The
**Capture** action (LMB / RT) now toggles recording.

- **`ClipRecorder`** (`Scripts/Recording/`): started and stopped by `PhotoModeController`'s Capture handler.
  It is also stopped by lowering the camera or by a full memory card, and footage recorded so far is always
  kept. It captures footage in the format ADR-4 chooses: a replay track plus listener PCM behind a narrow
  `IClipBackend` seam *(ADR-4 proposed 2026-11-10 by Story 1.14, awaiting Alexv)*.
- **`ClipGrader`**: samples `ShotGrader.Grade(...)` on frames throughout the recording. The per-frame subject
  gate and composition are **unchanged and not re-implemented**. It aggregates the samples, plus **peak
  coverage** read from `ISubject`, into a `ClipGrade` (0–100% → 1–5★, with a per-axis breakdown). Tunables
  live in `GradingConfig`.
- **`MemoryCardService`** (evolves `GalleryService`, stays in `Scripts/Gallery/`): stores `CapturedClip`s,
  enforces capacity in **seconds of footage**, and frees footage on delete. It remains the **single owner**
  of any native memory, as `GalleryService` is today.
- **`CapturedClip { string Id; IFootage Footage /* ADR-4: ReplayFootage first; proposed 2026-11-10 */; Texture2D Thumbnail; ClipGrade Grade;
  string SubjectId; DateTime CapturedAtUtc; float DurationSeconds }`**: follows the same disk-ready rules as
  `CapturedShot` (a file-name-safe, unique Id; UTC wall-clock time; a small DTO for JSON, because
  `JsonUtility` handles neither `DateTime` nor textures).
- **`ClipRecordedChannel : EventChannel<…>`**: raised once per finished clip. The memory card, the grade HUD
  and (later) reputation listen to it. `ShotCapturedChannel` stays for as long as the clip stories still
  need the per-frame grade path.

**Data flow:** Capture (press) → `ClipRecorder.Start` → on each sampled frame, `ShotGrader.Grade` → Capture
(press) / lower / card full → `ClipRecorder.Stop` → `ClipGrader` → `ClipRecordedChannel.Raise` →
`MemoryCardService` stores the clip, and `GradeHud` shows its grade.

**Dependency direction:** `PhotoMode` → `Recording` → `Grading` and `Events` (through `ISubject` only). The
memory card (`Gallery`) and `UI` listen through `ClipRecordedChannel` and never the reverse.

*The names and exact shapes are finalised by Stories 1.14–1.17. This section fixes the responsibilities and
the dependency direction.*

### Event-Actor System (reusable engine)

- `EventActor` MonoBehaviour: lifecycle FSM `Spawn → Build → Peak → WindDown → Despawn`;
  each phase has duration + Animator state + telegraph cue. Exposes `SubjectBounds`, `IsAtPeak`.
- `EventDefinition` ScriptableObject: phase timings (~20–30s life, ~1.5s peak window),
  cue audio, animation set, NavMesh route, cue radius (~25m).
- Pathing: NavMeshAgent (AI Navigation). Animation: Animator (Drunk Pack).
- `EventManager`: spawns from an object pool, caps concurrency (1 for the slice), owns
  spawn timing. Pooling prevents the object leaks called out in the GDD success metrics.

### Input

PlayerInput stays in Send-Messages mode. Repurpose "Attack" → **Capture** (LMB);
add **RaiseCamera** (hold RMB) and **Zoom** (scroll). Capture/Zoom are no-ops unless in
Photo mode. Single "Player" action map for the slice; a dedicated "Photo" map is a
future refactor if handlers get crowded.

### Data Persistence (Gallery)

*2026-11-10: for clips, this is superseded by `CapturedClip` (see **Clip Recording**). The text below
remains the record of the photo-era model; its disk-ready rules carry over unchanged.*

`CapturedShot { Texture2D image; ShotGrade grade; string subjectId; DateTime time }`,
held in an in-memory `List` inside `GalleryService`. No disk/economy in the slice
(per GDD). Designed so PNG (`EncodeToPNG` → `persistentDataPath`) + JSON metadata can be
added later without reshaping the model.

### Code Organization

- Namespaces: `CameraGame.{Core, PhotoMode, Events, Grading, Gallery, Recording}` (`Recording` added 2026-11-10).
- Folders: `Assets/Scripts/{Core,PhotoMode,Events,Grading,Gallery}`; SO configs in `Assets/Data/`.
- Assemblies: start with one `CameraGame.asmdef` (+ test asmdef when tests arrive); split
  only when compile times justify it.
- Decoupling: a `ShotCaptured` ScriptableObject event channel connects capture → gallery
  (and later → reputation). Avoid heavy singletons/DI; `EventManager` is the one manager.

### Architecture Decision Records

- **ADR-1 (Camera):** Walk state is first-person, not third-person as the GDD describes.
  *Rationale:* the existing controller/camera already implement FP walking; reusing them
  is the fastest route to a testable, fun slice for a solo intermediate dev, and "raise
  camera" reads naturally as a zoom-in from an already-FP view. *Consequence:* the GDD's
  "two camera states" contrast is softened; revisit if playtests want a stronger
  exploring-vs-shooting distinction. A small GDD update is advisable.
- **ADR-2 (Grading):** Screen-space bounds math over render-texture pixel analysis.
  *Rationale:* avoids GPU readback stalls; deterministic and tunable. *Consequence:*
  coverage is approximated from bounding boxes, not exact silhouettes — acceptable for grading.
- **ADR-3 (Event system):** Data-driven (ScriptableObject) over hard-coded events.
  *Rationale:* makes the ~80% of future events cheap (data, not code). *Consequence:*
  slightly more upfront structure for the single slice event, paid back immediately in Epic 3.
- **ADR-4 (Clip recording) — Proposed (2026-11-10) — awaiting Alexv's approval.** *(Replaces the OPEN
  block of 2026-11-10; decided by measurement in Story 1.14. Evidence: `_bmad-output/verification/1-14-decide-how-the-game-records-video/`, git-ignored; how to rebuild it:
  `Assets/Spikes/RecordingSpike/README.md`.)*

  **Decision: (B) replay recording, with a standard video file made only at export.** The memory card, the
  in-game playback and the Edit Suite all work on *replay data* (what the camera and the actors did, plus
  the sound the player heard). A finished clip or edit becomes an MP4 only when the player exports it
  (FR31), by re-rendering the replay offline and encoding it once with the OS's Media Foundation H.264
  encoder.

  **Why.** On the only machine measured (a laptop: i7-1165G7, MX350 + Iris Xe, see NFR1 below),
  recording with (B) cost **+0.8 ms/frame on average over 3 rounds (−4.2…+3.6)** and lost no frames. That is
  not distinguishable from zero, but with n = 3 a cost of up to about 4 ms (a quarter of a 60 FPS frame) is
  not ruled out either. It stores about 2.6 KB/s of pose data plus the sound (the spike held it as float32,
  384 KB/s measured; 16-bit is 192 KB/s, ≈ 23 MB for the ~2-min card), and it re-rendered the drunk exactly:
  ghost-vs-live bone drift **0.000 cm on every tick of two takes, including the 15 crossfade ticks** around
  the peak (Editor play mode, two runs; a control against the previous tick reads 44–71 cm), and SSIM
  0.991–0.993 against the live frame.
  (A) cost +10.9 ms/frame, and of 229–248 capture attempts per 10-s take it dropped 197–220 (86–89%), so a
  take held only **27–32 distinct frames** (about 3 per second). The Intel hardware H.264 encoder fell from
  ~67 fps idle to 6.6 fps while the game ran, even for a standalone ffmpeg encoding a file from disk, so it is
  not the game-to-ffmpeg pipeline. The game rendered on the MX350, and the mechanism was not isolated
  (Optimus presents through the Iris Xe, and CPU-only RGBA→NV12 conversion also fell from 93 to 32 fps on
  the 4 cores). (A)'s `VideoPlayer` did not prepare within the spike's 10-s cap in 3 of 4 cycles (no error
  raised; the successful prepares took 3.6 and 9.3 s), and the player crashed or hung on quit after it in 4
  of 5 (A) runs. (C) cost +30.2 ms/frame and held 2.45 GB of frame buffers for 10 s, with private memory at
  6.2 GB against 1.04 GB for the baseline. The 2-min card would need ≈ 30 GB (*extrapolated*), so (C) is
  rejected on memory alone.

  **Evidence** (1080p30 footage, Windows **release build**, windowed 1920×1080, 3 rounds with the order rotated;
  frame cost = difference-in-differences against a baseline run of the same scripted segment, because the
  view the camera tracks changes the cost up to 4× across the drunk's cycle; tables in `measure/analysis.json`;
  the sound, SSIM and export numbers are reproduced in `review/reproduced-numbers.txt`, and the per-tick
  fidelity sweep is in `review/review-fidelity-c{1,2}-fidelity.csv`):

  | | (A) encode while playing | **(B) replay** | (C) raw frames in RAM |
  |---|---|---|---|
  | Frame-time cost while recording | +10.9 ms (6.6–13.9) | **+0.8 ms (−4.2…+3.6), n = 3** | +30.2 ms (25.0–34.6) |
  | Frames dropped / repeated (of 300) | 197–220 captures dropped; 27–32 distinct frames per take | **0 dropped**; 1–42 repeated (game < 30 fps) | 0 dropped; 129–154 repeated (game at 16 fps) |
  | Memory / disk per second | ~1 MB/s MP4 on disk; 47 MB of slots | **2.6 KB/s pose + 384 KB/s PCM as held (192 KB/s as 16-bit)** | 249 MB/s of frame buffers (2.45 GB for 10 s); 6.2 GB private |
  | Game sound in the footage | captured, but the MP4s are 4.2–6.5 s long against 10 s of sound, 3–5 s out of sync at the end marker | **yes**: non-silent, louder as he nears (r = −0.58…−0.73), A/V −60…+23 ms | yes (A/V −15…+155 ms) |
  | Playback in the build | `VideoPlayer` did not prepare within 10 s in 3 of 4 cycles; crash/hang on quit in 4 of 5 runs | **re-render, SSIM 0.991–0.993 vs live; the clip's own tones heard 8.98 s apart** | uploads the stored frames, 4–21 ms per frame |
  | Playback frame rate (laptop) | not measurable: no clip played in 3 of 4 cycles | 17–21 fps (live game in the same window: 53–58) ⚠️ | 32–39 fps |
  | Export to MP4 | the file itself (stop-to-file 19–35 s) | **10-s clip: 40–42 s re-render + encode (4.0–4.2× real time); the 16-s edit: 24 s (1.5×); 720p: 2.95×** | 10-s clip: 29 s encode |
  | Licence / cost (live recording + export) | ffmpeg as a separate LGPL exe (obligations below; the local GPL build is ≈ 99 MB, an LGPL build's size unmeasured), or A-MF (OS encoder, unbuilt); H.264/AAC patents open. Buy option: AVPro Movie Capture, $100–500, vendor docs only | **record and play: Unity APIs only, nothing to licence. Export: the same encoder question as (A)** | record and play: nothing. Export: the same encoder question as (A) |

  **Footage format.** *On the card:* a replay track at 30 Hz. Each tick holds the photo camera's position,
  rotation and FOV, and for each event actor its liveness, transform and per-Animator-layer state (full-path
  hash, normalized time, and, while in transition, the next state, its time, and the transition's progress
  and duration). That is 87 B/tick for the drunk, one actor; the track grows linearly with the actors on
  screen. Alongside it goes 48 kHz stereo PCM from `OnAudioFilterRead` on the AudioListener's GameObject; the
  spike held it as float32, and 1.15 should keep it as 16-bit. A tick also stores its *repeat count*, so a
  game running below 30 fps still yields a true-duration clip. *On export:* MP4, H.264 via Media Foundation
  (CBR 8 Mbps, GOP 30 = 1 s, NV12, Constrained Baseline as the Intel MFT produced it), AAC-LC 160 kbps
  48 kHz stereo, 1920×1080 @ 30. The encoder writes MPEG-TS, which is remuxed to MP4 at the end (`h264_mf`
  delivers no extradata in time for an MP4 header, and TS survives a kill: 35 frames decoded from a killed
  encode, with only the frame at the cut damaged).

  **Frame source and UI.** The footage is the *photo camera's* view with **no UI** (the `GalleryService`
  precedent: a camcorder films the scene, not its own REC dot). For any live frame the game needs (thumbnails,
  reference frames), use the proven technique. A URP pass enqueued from script in `beginCameraRendering` (no
  renderer-asset change) at `AfterRenderingPostProcessing` copies the camera colour into an sRGB RT, which is
  then read with `AsyncGPUReadback`. Never use `ReadPixels`. At 1920×1080 windowed on D3D11 (the only setting
  measured) it was byte-identical to the screen with the overlays hidden (SSIM 1.0000, `evidence/control-*`).
  There its rows came back bottom-up, so encoders needed `vflip`; on another graphics API, check
  `SystemInfo.graphicsUVStartsAtTop` rather than assuming it. The spike's target is a fixed 1920×1080, so a
  non-16:9 window would be stretched: size it from the camera.

  **Memory card (AR6, NFR3).** Capacity in seconds = Σ tick repeats ÷ 30. Deleting a clip disposes its track
  and PCM buffers, and `MemoryCardService` is their single owner. Keep the PCM in a pooled native or
  preallocated buffer, not a fresh managed array per take (the spike's `Snapshot()` allocated one, ~46 MB per
  2 min as float). Story 1.17's disk seam: one binary track file plus one 16-bit WAV per clip (~195 KB/s).
  Each clip records its own sample rate and channel count, because `AudioManager`'s rate is 0 ("the device's
  rate"), so clips from different devices or sessions can differ.

  **Epic 9 (trim and join).** Edits are tick ranges plus matching PCM slices, so no keyframes and no re-encode
  are needed. They are frame-accurate where the ticks at a cut have a repeat count of 1, which is every tick
  at ≥ 30 fps. Below 30 fps a tick that spans a cut must have its repeat count split, which the spike did not
  do. The in-game preview is the same replay playback, and export re-renders the edit list once.
  *Demonstrated in the build:* take 1 trimmed by exactly 2.00 s at each end (300 → 180 frames), joined with
  take 2 (301 frames), giving one 16.03 s MP4 (H.264 1920×1080 @ 30 + AAC 48 kHz). A full decode printed
  nothing, and the drunk is in every sampled second (`evidence/evidence-B-edit.mp4`, `edit-contact-sheet.png`).
  ⚠️ That export ran on the local **GPL** ffmpeg, so the *pipeline* is proven and the *shippable binary* is not
  (next item).

  **Export path (FR31) and licence: explicit open items for Alexv.**
  - ffmpeg as a *separate executable*, an **LGPL** build (BtbN `win64-lgpl`), which a build step copies in from
    outside git. **Never commit it, never ship the local gyan.dev build (GPL), and never use `libx264`.**
    LGPL obligations: offer the exact corresponding source, put the notice in About/EULA, allow reverse
    engineering in the EULA, don't rename the binaries. *Unconfirmed:* that the BtbN LGPL binary includes
    `h264_mf` (its scripts do not disable auto-detection). Run `ffmpeg -encoders` on it before relying on it.
    **The export story is gated on that check.** The AAC encoder used was ffmpeg's native `aac`.
  - Alternative **A-MF**: drive Media Foundation's Sink Writer from C# (no extra executable, OS encoder).
    This was assessed on paper only and has never been built. It would sit behind the same export seam.
  - **Patents:** H.264 (Via LA) and AAC are a business decision, not settled here. Whether calling Windows'
    own encoder is covered is unconfirmed. VP9/AV1 are the royalty-free alternatives (unmeasured).
  - **Windows N/KN:** no Media Foundation means export is unavailable there (fail-soft message). Recording
    and playback are unaffected, because (B) never touches Media Foundation (reasoned, not run on an N edition).
  - Unity Recorder's runtime DLLs (`Unity.Recorder*.dll`) ship in every build as package defaults. No game or
    spike code references them, and they cannot record in a player.

  **Failure story (NFR8).** *Measured* (`evidence/evidence-failsoft-*`), on (A)'s live encoder and (C)'s
  buffer: a missing encoder, an encoder killed mid-clip and a full buffer each logged while the game kept
  running. *Not measured, and not yet true:* (B)'s own failures. The spike's export loop waits on the main
  thread (`while (!TryAcquire) Sleep(1)`, a 60-s `Finish`, `WaitForExit()` with no timeout), throws if the
  encoder executable is missing (the same `Win32Exception` (A)'s path caught), never checks ffmpeg's exit
  code, and muxes with `-shortest`, which silently cut a 301-frame export to 298
  (`review/reproduced-numbers.txt`). The export Epic 9 ships must run off the main thread with timeouts, check
  every exit code, keep the clip on the card when export fails, and never truncate. A crash mid-recording
  loses the in-memory take unless Story 1.17 streams ticks and PCM to disk as it records.

  **Consequences (cross-cutting rule, to add under Cross-cutting Concerns when approved): everything that
  moves or makes sound on screen must be recordable.**
  - Every game `AudioSource` keeps `bypassEffects` and `bypassListenerEffects` off, or the listener tap misses
    it. The reverse holds too: the tap records **everything** the listener hears, including UI clicks,
    music and the camcorder's own REC beeps. A sound that must stay out of the footage sets
    `bypassListenerEffects = true` on purpose.
  - Every event actor exposes its transform and Animator state. The track names the actor's
    `EventDefinition`, and the ghost is built from that definition's prefab, not from a live instance, so a
    clip loaded in a later session (1.17, Epic 9) can rebuild it. The spike recorded "the first live actor"
    and cloned it.
  - The track records the state hash, time and crossfade only. Today's `DrunkAnimator` has one layer, no
    parameters, no blend trees and no IK. An event that uses any of them must add it to the track (parameters,
    layer weights, IK targets). Replay a fixed-time crossfade with `CrossFadeInFixedTime`, or by its recorded
    `durationUnit`: the spike passed seconds to the normalized `CrossFade`, which is harmless only while the
    ghost's clock is frozen.
  - **Shader-time animation is not reproduced.** Tree and grass sway differed between live and replay
    frames. Either drive it from a recordable clock, or accept the difference.
  - Anything not in the track (particles, physics props, the player's body, other managers' actors) is
    **not missing** from a replay: it appears *as it is at playback time*. Record it, or hide it from the
    playback camera.
  - A saved replay is only as stable as the content it references. Renaming an Animator state, editing a clip
    or moving a prop changes or breaks old clips and their later export, so the 1.17 disk format carries a
    content version.

  **Risks carried forward.** (1) **Replay playback is slow on this laptop** (17–21 fps against 53–58 for the
  live game in the same window). Turning the live camera off during full-screen playback did **not** help
  (19.0 fps), so the cost is not the double render and its cause was not isolated. Profile it in a
  Development build before Story 1.18. All playback numbers come from one actor. (2) Export of a 10-s clip took
  4.0–4.2× real time here (the 16-s edit 1.5×, 720p 2.95×). (3) **A/V alignment is fragile.** The measured
  (B) takes were −60…+23 ms, but recording starts its clock at the start of a frame while the PCM starts at
  the next audio callback. Synchronous work on that frame delayed the sound by 160–290 ms in the fail-soft
  runs (`failsoft-ring`, `failsoft-badpath`), and a take's PCM can be ±2 DSP blocks off its tick duration
  (−37 ms measured). 1.15 must stamp both by DSP time (`AudioSettings.dspTime`) and pad or trim the PCM to
  the tick timeline. Ticks are taken on the first game frame after each 1/30-s boundary, so they jitter by up
  to one game frame; store a timestamp per tick. (4) **NFR1's absolute 60 FPS** cannot be judged on
  this machine. Its no-recording baseline runs 33–52 fps in the recording window, so it waits for a
  mid-range desktop.

  **What Story 1.15 builds.** A *narrow backend seam* in `Scripts/Recording/`, so that a veto swaps the
  backend and not the design: `ClipRecorder` talks to an `IClipBackend` (`Begin`, `Sample` once per game
  frame, `End → IFootage`). `IFootage { float Seconds; long Bytes; void Dispose(); }` is the
  `<footage handle>` of `CapturedClip`, and `ReplayFootage` is the first implementation. Export sits behind
  `IClipExporter` (ffmpeg-LGPL or A-MF). The spike's pieces to lift: the tick recorder with repeat counts,
  the listener PCM tap and the stripped ghost, **written as new game code, not copied**. The spike version
  carries these known defects:
  - the A/V start offset and PCM-length mismatch (risk 3);
  - the ghost cloned from a live, "first found" actor (Consequences);
  - live actors hidden from the playback camera by toggling `forceRenderingOff` in
    `beginCameraRendering` only. Restore it symmetrically in `endCameraRendering`, and on dispose for pooled
    *inactive* actors too, or a drunk that despawns while hidden can come back invisible (reasoned, not
    reproduced);
  - ghost stripping that keeps `NavMeshObstacle`, `Rigidbody` and particle systems;
  - frame-tap callbacks that are silently lost when the pass does not run.

  The TCP-fed encoder process (Mono's `Process` stdin starved ffmpeg at ~6 fps of 1080p; loopback TCP kept
  pace in the transport-only diagnostic) belongs to the export story, not 1.15. Lift it only after the
  LGPL-binary check and Alexv's licence decision.

  **Reversal cost.** Before 1.15, nothing (only the spike exists). After 1.15–1.17, swapping to (A) is a new
  `IClipBackend` plus `IFootage` (a file handle), but the playback and card code that use replay data would be
  rewritten too. After Epic 9, the edit model (tick ranges) is (B)-specific as well. (A) still faces the
  encoder-starvation finding on iGPU machines. (C) is not viable at ~250 MB/s.

  *(Revised 2026-11-10 by the Story 1.14 code review. The numbers were corrected against the evidence; the
  licence/cost row was added; fidelity was re-measured on every tick; and the export failure story and the
  defects 1.15 must not inherit are now spelled out. The decision is unchanged.)*

---

## Cross-cutting Concerns

These patterns apply to ALL systems and must be followed by every implementation.

### Error Handling

**Strategy:** Defensive + fail-soft. The game has no fail state (GDD), so errors must
never crash or hard-pause gameplay. Validate required references in `Awake()`/`Start()`
and log a clear error if missing; guard the rest with null checks. Reserve try-catch for
genuinely exceptional I/O (e.g., future save/load), not gameplay loops.

- **Critical** (missing required reference, bad config) → `Debug.LogError` with context +
  disable the offending component gracefully (`enabled = false`), never throw into Update.
- **Recoverable** (subject left frame, no NavMesh path) → `Debug.LogWarning` + continue.
- Errors are NEVER shown to the player.

**Example:**
```csharp
private void Awake()
{
    _agent = GetComponent<NavMeshAgent>();
    if (definition == null)
    {
        Debug.LogError($"[Events] {name}: EventDefinition not assigned — disabling actor.", this);
        enabled = false;   // fail soft, no crash
        return;
    }
}
```

### Logging

**Format:** Unity-native `Debug.Log*` via a thin static `GameLog` helper with a category
tag. **Destination:** Unity Console (clean console after changes is a project rule).

- **ERROR**: a system can't function (missing config). **WARN**: unexpected but handled.
  **INFO**: milestones (event spawned/peaked/despawned, shot captured+grade). **DEBUG**:
  verbose per-frame diagnostics — stripped from release.
- Hot paths (grading math, Update loops): no logging except guarded DEBUG.

**Example:**
```csharp
public static class GameLog
{
    public static void Info(string cat, string msg) => Debug.Log($"[{cat}] {msg}");
    public static void Warn(string cat, string msg) => Debug.LogWarning($"[{cat}] {msg}");
    public static void Error(string cat, string msg, Object ctx = null) => Debug.LogError($"[{cat}] {msg}", ctx);

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void Debug_(string cat, string msg) => UnityEngine.Debug.Log($"[{cat}:DBG] {msg}");
}
// Usage: GameLog.Info("Events", "Drunk reached PEAK");
```

### Configuration

**Approach:** Data-driven via ScriptableObjects for anything tunable; static constants for
true invariants.

- **Balancing/tunable** (grading thresholds, event timings, camera FOV/zoom) → ScriptableObjects
  (`GradingConfig`, `EventDefinition`, `CameraConfig`) — Inspector-tweakable, no recompile.
- **Invariants** (layer/tag names, Animator param hashes, input action names) → static
  classes (`GameConstants`, `AnimHashes`) to kill magic strings.
- **Player settings** (later) → PlayerPrefs or a settings SO. No remote config.

### Event System

**Pattern:** Two tiers.
- **Cross-system, decoupled** → ScriptableObject **Event Channels** (e.g. `ShotCapturedChannel`
  carrying a `ShotGrade`; `EventPeakedChannel`). Listeners subscribe in `OnEnable`, unsubscribe
  in `OnDisable`. Typed payloads, synchronous.
- **Local, intra-system** → plain C# `event Action<...>` (e.g. `EventActor.PhaseChanged`).

**Event Naming:** `<Subject><Verb>` past tense — `ShotCaptured`, `EventPeaked`, `EventDespawned`.

**Example:**
```csharp
[CreateAssetMenu(menuName = "CameraGame/Events/Shot Captured Channel")]
public class ShotCapturedChannel : ScriptableObject
{
    public event System.Action<ShotGrade> Raised;
    public void Raise(ShotGrade grade) => Raised?.Invoke(grade);
}
// GalleryService and (later) ReputationSystem both listen to the same asset.
```

### Debug Tools

**Available Tools (Editor / development builds only):**
- `OnDrawGizmos` overlays: event cue radius, NavMesh route, peak-window state color.
- On-screen grade breakdown (subject % / composition / timing) shown after a capture.
- Spawn-point and concurrency gizmos for the EventManager.

**Activation:** All debug visuals/overlays wrapped in `#if UNITY_EDITOR || DEVELOPMENT_BUILD`
so they strip from release. On-screen overlay toggled with a key (e.g., F1). MCP `read_console`
used to verify zero compile errors after each script change (project workflow).

---

## Project Structure

### Organization Pattern

**Pattern:** Hybrid — by-type at the Unity root (engine convention), feature folders within `Scripts/`.

**Rationale:** Unity tooling expects assets grouped by type; feature subfolders under
`Scripts/` keep each system's code together. Brownfield-friendly: preserves existing
`Models/`, `Materials/`, `Settings/`, `Scenes/`, `Input/` and only adds new homes.

### Directory Structure

```
My project/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/            # GameLog, GameConstants, AnimHashes, ObjectPool<T>, event-channel base
│   │   ├── Player/          # PlayerController, FirstPersonCamera, CharacterAnimator (renamed from ThirdPerson*)
│   │   ├── PhotoMode/       # PhotoModeController (mode toggle, FOV zoom, viewfinder hookup)
│   │   ├── Grading/         # ShotGrader, ShotGrade, GradingConfig (SO)
│   │   ├── Events/          # EventActor, EventManager, EventDefinition (SO), lifecycle states, ISubject
│   │   ├── Gallery/         # GalleryService → MemoryCardService, CapturedShot (2026-11-10)
│   │   ├── Recording/       # ClipRecorder, ClipGrader, CapturedClip, ClipRecordedChannel (added 2026-11-10)
│   │   ├── Editor/          # AddMeshColliders, ColorCharacter (existing)
│   │   └── CameraGame.asmdef
│   ├── Data/                # ScriptableObject *instances*
│   │   ├── Events/          #   TownDrunk.asset (EventDefinition)
│   │   ├── Grading/         #   GradingConfig.asset
│   │   ├── Camera/          #   CameraConfig.asset
│   │   ├── Recording/       #   MemoryCardConfig.asset (added 2026-11-10)
│   │   └── Channels/        #   ShotCapturedChannel.asset, EventPeakedChannel.asset, ClipRecordedChannel.asset
│   ├── Models/              # FBX (existing: game scneen.fbx, main characters.fbx)
│   ├── Materials/           # existing
│   ├── Animations/          # Animator controllers + Drunk Pack clips
│   ├── Audio/
│   │   ├── SFX/             #   shutter, zoom-whir, drunk mumble cue
│   │   └── Mixers/          #   GameAudio.mixer
│   ├── UI/                  # Viewfinder overlay, grade-feedback HUD
│   ├── Prefabs/
│   │   ├── Events/          #   EventActor_Drunk.prefab
│   │   └── Player/
│   ├── Input/               # InputSystem_Actions (existing)
│   ├── Scenes/              # existing (SampleScene, Video Camera Game 2.0)
│   ├── Settings/            # URP assets PC_RPAsset / Mobile_RPAsset (existing)
│   └── Tests/
│       ├── EditMode/        #   ShotGrader math, lifecycle timing (EditMode.asmdef)
│       └── PlayMode/        #   capture flow, event spawn/despawn (PlayMode.asmdef)
└── _bmad-output/            # planning + architecture docs
```

### System Location Mapping

| System | Location | Responsibility |
|--------|----------|----------------|
| Player movement/camera | `Scripts/Player/` | FP controller + camera (existing, renamed) |
| Photo mode | `Scripts/PhotoMode/` | Walk↔Photo toggle, FOV zoom, viewfinder control |
| Shot grading | `Scripts/Grading/` | Subject gate, composition, timing → ShotGrade |
| Event actors | `Scripts/Events/` | Lifecycle FSM, EventManager, pooling, ISubject |
| Gallery → Memory card | `Scripts/Gallery/` | Hold recorded clips + grades, capped in seconds of footage (2026-11-10: was captured shots) |
| Clip recording | `Scripts/Recording/` | Record start/stop, clip grading, the clip data model (added 2026-11-10) |
| Shared infra | `Scripts/Core/` | Logging, constants, pooling, event-channel base |
| Tunable data | `Data/` | EventDefinition, GradingConfig, CameraConfig, channels |
| Editor tools | `Scripts/Editor/` | Mesh colliders, character color (existing) |

### Naming Conventions

**Files**
- Scripts: `PascalCase.cs` matching the class (e.g., `EventActor.cs`).
- Scenes: `PascalCase` (e.g., `TownSlice`). Prefabs: `PascalCase` with type prefix (`EventActor_Drunk`).
- ScriptableObject assets: `PascalCase` (e.g., `TownDrunk`, `GradingConfig`).
- Animation clips: `Subject_Action` (e.g., `Drunk_Idle`, `Drunk_Stagger`).

**Code Elements**

| Element | Convention | Example |
|---------|-----------|---------|
| Class / method | PascalCase | `ShotGrader`, `Grade()` |
| Private field | _camelCase | `_agent`, `_currentMode` |
| Local / parameter | camelCase | `captureTime` |
| Constant | PascalCase / UPPER_SNAKE | `MaxConcurrentEvents` |
| Interface | IPascalCase | `ISubject` |
| Namespace | `CameraGame.*` | `CameraGame.Events` |

**Events / Channels:** `<Subject><Verb>` past tense — `ShotCaptured`, `EventPeaked`, `EventDespawned`.

### Architectural Boundaries

- **Dependency direction:** `Core` depends on nothing game-specific. Feature folders
  (`PhotoMode`, `Grading`, `Events`, `Gallery`) may depend on `Core` and on `Player`,
  never the reverse. `UI` depends on `PhotoMode`/`Grading`, not vice versa.
- **`Gallery` → `PhotoMode` is sanctioned, and only in that direction** (added 2026-07-30,
  Story 1.11 code review). A screen-owning feature has to be able to say "not while I am up":
  the gallery opens only in Walk mode and, while open, suppresses the camera raise — which
  makes capture and zoom inert too, since both already gate on `IsPhotoMode`. The alternatives
  were worse. Widening `ShotCapturedChannel` to carry the state would put the knowledge in a
  payload two other stories already depend on; giving `PhotoMode` a `galleryView.IsOpen` check
  would reverse the direction outright and force `PhotoMode` to learn about every future screen.
  So `PhotoModeController` exposes a **general** `SetRaiseSuppressed(bool)` that knows nothing
  about the gallery, and the caller supplies the meaning. Story 1.12's HUD, a pause menu or a
  map screen use the same switch — **and the moment a second caller exists, that flag must
  become a refcount or an owner token**, because today the first closer un-suppresses for
  everyone (recorded in `deferred-work.md`).
  The same reasoning extends to `Gallery` → `Player` (`ThirdPersonController.SetInputSuppressed`),
  which is already covered by the "may depend on `Player`" clause above.
- **Grading ↔ Events decoupling:** Grading reads subjects through an `ISubject` interface
  (`Bounds`, `IsAtPeak`), so it never hard-references concrete event types.
- **One assembly** (`CameraGame.asmdef`) for the slice; `Editor/` and `Tests/` are
  separate assemblies. Split features into more asmdefs only when compile times justify it.
- **Data lives in `Data/`**, code in `Scripts/` — never hard-code tunable numbers in code.

---

## Implementation Patterns

These patterns ensure consistent implementation across all AI agents and hand-written code.

### Novel Patterns

#### 1. Event-Actor Lifecycle (the reusable event engine)

**Purpose:** Every catchable town event (drunk, robbery, cheating husband…) runs the same
spawn→peak→despawn lifecycle. One generic actor + per-event data = ~80% of future events
with no new code.

**Components:**
- `EventPhase` enum — `Spawn · Build · Peak · WindDown · Despawn`
- `EventActor` — MonoBehaviour FSM; drives phases on timers; exposes `ISubject`
- `EventDefinition` (ScriptableObject) — per-phase duration, animation state, cue clip, NavMesh route, cue radius
- `EventManager` — spawns/pools actors, caps concurrency, owns spawn cadence
- `ISubject` — what the grader reads (`Bounds`, `IsAtPeak`, `TimeToPeak`, `SubjectId`)

**Data flow:** `EventManager` pulls an actor from the pool → actor reads its `EventDefinition`
→ advances phases on timers, cross-fading animations and firing cues → raises `EventPeaked`
at peak → on `Despawn` returns itself to the pool (no Destroy → no leaks).

**Implementation guide:**
```csharp
public enum EventPhase { Spawn, Build, Peak, WindDown, Despawn }

[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class EventActor : MonoBehaviour, ISubject
{
    [SerializeField] private EventDefinition definition;

    private Animator _animator;
    private EventPhase _phase;
    private float _timer;

    public Bounds Bounds   => _renderer.bounds;           // for the grader
    public bool   IsAtPeak => _phase == EventPhase.Peak;
    public float  TimeToPeak { get; private set; }
    public string SubjectId => definition.Id;
    public event System.Action<EventPhase> PhaseChanged;

    private void OnEnable() => EnterPhase(EventPhase.Spawn);

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_phase != EventPhase.Peak) TimeToPeak -= Time.deltaTime;
        if (_timer <= 0f) Advance();
    }

    private void Advance()
    {
        switch (_phase)
        {
            case EventPhase.Spawn:    EnterPhase(EventPhase.Build);    break;
            case EventPhase.Build:    EnterPhase(EventPhase.Peak);     break;
            case EventPhase.Peak:     EnterPhase(EventPhase.WindDown); break;
            case EventPhase.WindDown: EnterPhase(EventPhase.Despawn);  break;
            case EventPhase.Despawn:  EventManager.Instance.Return(this); break;
        }
    }

    private void EnterPhase(EventPhase next)
    {
        _phase = next;
        var p = definition.GetPhase(next);
        _timer = p.Duration;
        _animator.CrossFade(p.AnimStateHash, 0.2f);
        if (p.Cue != null) _cueSource.PlayOneShot(p.Cue);
        PhaseChanged?.Invoke(next);
        GameLog.Info("Events", $"{SubjectId} → {next}");
    }
}
```

#### 2. Frame-Read Grading

**Purpose:** Score a photo on Subject-gate · Composition · Timing without GPU readback.

**Components:** `ShotGrader` (pure logic), `GradingConfig` (SO thresholds/curves), `ISubject`,
`ShotGrade` (result struct: percent + stars + per-axis breakdown).

**Data flow:** `PhotoModeController.Capture()` → `ShotGrader.Grade(cam, subject, cfg)` →
`ShotGrade` → raise `ShotCapturedChannel` → GalleryService stores it.

**Implementation guide:**
```csharp
public static class ShotGrader
{
    public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg)
    {
        // 1. Subject gate: must be inside the frustum AND not occluded
        var planes = GeometryUtility.CalculateFrustumPlanes(cam);
        if (!GeometryUtility.TestPlanesAABB(planes, subject.Bounds)) return ShotGrade.Miss;

        // 2. Composition: project bounds → screen box → HEIGHT fraction + distance from CENTRE
        Rect box = ScreenBounds(cam, subject.Bounds);          // clamped to cam.pixelRect
        float height = box.height / cam.pixelRect.height;
        if (height < cfg.SafeMinSubjectHeight) return ShotGrade.Missed(GradeMiss.TooSmall);
        float composition = Prominence(cfg, height)           // trapezoid, sweet spot 0.45–0.78
                          * Placement(cfg, unclampedCentre)   // 1 at frame centre
                          * Framing(cfg, framedFraction);     // cut-off penalty

        // 3. Timing: proximity to the peak WINDOW (full ±0.5s, zero by ±2s)
        float timing = TimingWindow(cfg, subject.PeakOffset);

        return ShotGrade.Scored(visible, composition, timing);
    }
}
```

> ⚠️ **This sketch was corrected on 2026-07-28 by the Story 1.10 code review; the original is preserved
> below for the record.** Five identifiers in it never existed in the shipped code, and because Story
> 1.10's References send implementers straight here, it was actively misleading:
>
> | Sketch (original) | Shipped | Why |
> |---|---|---|
> | `Screen.width/height` | `cam.pixelRect` | An offset viewport (split screen, PiP viewfinder) does not start at 0,0 — Story 1.9's review reproduced a dead-centre subject measuring 0% from exactly this. |
> | `coverage` (area) | `height` fraction | Area around a tall thin figure is mostly air: a good portrait reads 4.5% by area, 39% by height, and area breathes with the walk cycle. |
> | `cfg.MinCoverage` ≥ ~8% | `SafeMinSubjectHeight` 0.20 | Same measure change; see FR5's revision note. Read through the `Safe*` accessor — `[Range]` is editor-only and these assets are hand-authored as YAML. |
> | `cfg.ThirdsScore(...)` | distance from **centre** | The thirds bonus was built as specified, photographed in matched pairs, and judged worse than centred — twice. GDD FR6 was rewritten to match on 2026-07-27. |
> | `cfg.CoverageCurve` / `cfg.TimingCurve` (`AnimationCurve`) | plain numeric tunables | A curve cannot be validated ("this curve never reaches 1") and serialises as an un-eyeballable keyframe list in hand-written YAML. |
> | `Mathf.Abs(subject.TimeToPeak)` | `subject.PeakOffset` | The peak is a 1.5 s INTERVAL and `TimeToPeak` is 0 at its **start**, so this scored the last frame of the money shot as 1.5 s early. |
> | `ShotGrade.Miss` / `FromPercent` | `Missed(reason)` / `Scored(...)` | The miss reason and the per-axis breakdown must survive into a release build for Story 1.12's HUD. |

### Communication Patterns

**Pattern:** ScriptableObject event channels for cross-system; cached direct refs + C# events
for intra-system. Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
```csharp
private void OnEnable()  => shotCaptured.Raised += OnShotCaptured;
private void OnDisable() => shotCaptured.Raised -= OnShotCaptured;
```

### Entity Patterns

**Creation:** Prefab + object pool via `EventManager`. Never `Instantiate`/`Destroy` in the
gameplay loop.
```csharp
EventActor actor = _pool.Get();          // reuse, don't allocate
actor.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
// ... on despawn:
_pool.Return(actor);
```

### State Patterns

**Pattern:** Lightweight enum state machine (switch on an enum in `Update`). Reserve class-based
State pattern for when an entity has many complex states — overkill for the 5-phase lifecycle.
(See `EventActor.Advance()` above.)

### Data Patterns

**Access:** ScriptableObject configs assigned via `[SerializeField]` in the Inspector. No
`Resources.Load`, no magic paths. Tunable numbers live in the SO, never as code literals.
```csharp
[SerializeField] private GradingConfig gradingConfig;   // injected in Inspector
```

### Consistency Rules

| Pattern | Convention | Enforcement |
|---------|-----------|-------------|
| Component refs | Cache in `Awake`; never `GetComponent` in `Update` | Code review / CR step |
| Tunable values | In a ScriptableObject, not code literals | Code review |
| Event subscriptions | Subscribe `OnEnable`, unsubscribe `OnDisable` | Code review |
| Magic strings | Use `GameConstants` / `Animator.StringToHash` | Code review |
| Spawned objects | Pooled, never Instantiate/Destroy in loops | Code review |
| Debug code | Behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` | Compiler |

---

## Architecture Validation

### Validation Summary

| Check | Result | Notes |
|-------|--------|-------|
| Decision compatibility | ✅ Pass | All native Unity patterns; ADR-1 explicitly reconciles GDD vs. existing code |
| GDD coverage | ✅ Pass | All Epic-1 systems + Pillars 1/2/3 mapped; tuning numbers encoded as SO data |
| Pattern completeness | ✅ Pass | 6/6 standard scenarios + 2/2 novel patterns documented with code |
| Epic mapping | ✅ Pass | Epic 1 directly served; E2–E8 are additive (data, listeners, new services) |
| Document completeness | ✅ Pass | All required sections present; no placeholders/TODOs detected |

### Coverage Report

- **Systems covered:** 6/6 (Player, PhotoMode, Grading, Events, Gallery, Core)
- **Patterns defined:** 6 standard + 2 novel
- **Decisions made:** 8 (1 critical fork + 7 reasoned defaults)
- **GDD tuning targets encoded as SO config:** 0.20 height gate, 0.45–0.78 prominence sweet spot, ±0.5/±2s timing around the peak window, 60°→18° FOV, ~25 m cue radius, 1×–4× zoom *(gate/sweet-spot figures revised 2026-07-28 — they were "≥8% gate, 25–50% sweet spot" on an area measure)*

### Epic Mapping

| Epic | Architectural Support |
|------|----------------------|
| E1 — First Playable Slice | Direct — every system in this doc serves it, including Clip Recording (ADR-4) from 2026-11-10 |
| E9 — The Edit Suite *(added 2026-11-10; sequenced right after E1)* | Builds on the ADR-4 clip format and the memory card's disk seam; its first story makes clips survive between sessions |
| E2 — Living Town | Scene/Audio/UI structure ready; districts are level-design layered on top |
| E3 — Event Content Pack #1 | Reuses EventActor + EventDefinition (data, not code) |
| E4 — Progression & Reputation | `ClipRecordedChannel` already decoupled; ReputationSystem subscribes later *(2026-11-10: was `ShotCapturedChannel`)* |
| E5 — Home Base | The memory card's disk seam (cashed in by E9) is reused for printing stills *(2026-11-10: was `GalleryService`'s seam)* |
| E6 — Nature & Collection | Reuses EventActor for nature-spectacle events (lightning, eclipse) |
| E7 — Missions & Mystery | Additive — new `MissionService` listens to `ShotCapturedChannel` |
| E8 — Stealth Layer | Additive — `EventActor` already exposes phase events for spotted/abort |

### Minor Observations (non-blocking)

- **Audio "swell on peak"** (GDD Art & Audio §3) — the seam exists (`EventPeakedChannel` fires at peak); a future `AudioDirector` would subscribe and trigger an AudioMixer snapshot. Note for Epic 1 polish or Epic 2.
- **Vantage points** (rooftops/hills/docks) — level-design concern, not architecture; flagged for E2.
- **Renames** (`ThirdPerson*` → `Player*` / `FirstPersonCamera`) — recommended but optional cleanup; non-blocking.

### Document Quality Scores

- Architecture Completeness: **Complete**
- Version Specificity: **All Verified** (Unity 6.3 LTS / 6000.3.8f1, URP 17.3.0, Input System 1.18.0, MCP for Unity v9.6.x — all verified May 2026)
- Pattern Clarity: **Clear**
- AI Agent Readiness: **Ready**

### Critical Issues Found

None.

### Recommended Actions Before Implementation

1. Run **`gds-create-epics-and-stories`** next (the GDS workflow's next required step) to turn the GDD's 8 epics into developer stories that reference this architecture.
2. Then **`gds-check-implementation-readiness`** to verify GDD ↔ architecture ↔ stories alignment.
3. Optional cleanup: rename `ThirdPersonController` → `PlayerController` and `ThirdPersonCamera` → `FirstPersonCamera`.
4. Consider a one-line GDD update noting ADR-1's FP-for-both decision (GDD currently says walk = third-person).

### Validation Date

2026-05-30

---

## Development Environment

### Prerequisites

- **Unity 6.3 LTS** (`6000.3.8f1` or newer `6.3.x` patch) with the Windows Build Support module.
- **URP 17.3.0** (Package Manager — already installed).
- **AI Navigation** package (already installed — required for `NavMeshAgent` in `EventActor`).
- **New Input System 1.18.0** (already installed).
- **Node.js 18+** (only for the MCP server — already running for the existing MCP for Unity).
- **VS Code** (or Rider / Visual Studio) with the Unity debugger (`vstuc`).

### AI Tooling (MCP Servers)

This project is already configured with **MCP for Unity** (`com.coplaydev.unity-mcp`,
CoplayDev/unity-mcp, MIT, actively maintained):

| MCP Server | Purpose | Install Type |
|------------|---------|--------------|
| MCP for Unity | Scene/asset/script/test control inside the Unity Editor | UPM git package + Node.js MCP server (installed) |

**Verification:** Window → Package Manager → confirm `com.coplaydev.unity-mcp` is present.
After every script change, call MCP `read_console` to verify zero compile errors before
continuing (project workflow rule).

### Opening the Project

No external CLI needed — Unity-native:

1. Open Unity Hub → Add → select this folder (`My project/`).
2. First scene: `Assets/Scenes/Video Camera Game 2.0.unity`.
3. Build target: File → Build Settings → **PC, Mac & Linux Standalone** → Windows / x86_64.

### First Steps (kicking off Epic 1)

1. **Create the script folders & namespaces** per the Project Structure section
   (`Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}`, `Data/{Events, Grading, Camera, Channels}`).
2. **Create the ScriptableObject assets** in `Data/`: `GradingConfig`, `CameraConfig`,
   `TownDrunk` (EventDefinition), `ShotCapturedChannel`, `EventPeakedChannel`.
3. *(Optional cleanup)* Rename `ThirdPersonController` → `PlayerController` and
   `ThirdPersonCamera` → `FirstPersonCamera` so the names match behavior.
4. **Run `gds-create-epics-and-stories`** in a fresh context window to turn the GDD's
   8 epics into developer stories that reference this architecture.
5. Then `gds-check-implementation-readiness` to validate GDD ↔ Architecture ↔ Stories
   alignment before code starts.
