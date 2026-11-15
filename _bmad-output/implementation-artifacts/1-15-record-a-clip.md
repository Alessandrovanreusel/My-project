---
baseline_commit: 1f83710d71035ac335dc7c6726b4a1bd25a92787
---

# Story 1.15: Record a clip

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-10 (`sprint-change-proposal-2026-11-10.md`). It depends on Story 1.14 (ADR-4). Record-as-a-toggle
and clips-with-game-sound were confirmed by the stakeholder on 2026-11-10. Context was created on 2026-11-15 by the
loop CREATE phase.*

*This is the first story that ships the camcorder. Before it, LMB in the viewfinder takes a **photograph**. After it,
LMB **starts and stops recording a clip**. Every system from Stories 1.1 to 1.12 is reused. The recording approach
is no longer open: **ADR-4 was approved by Alexv on 2026-11-15** (`loop/runtime/approvals.txt` line 1: "APPROVED
ADR-4 2026-11-15 — "Approve" (after a plain-language walkthrough: (B) replay recording, MP4 only at export; the
export licence and patent questions stay open)"). This create phase changed ADR-4's status in
`game-architecture.md` from *Proposed* to *Accepted*, citing that line, and added the **Recordability** rule that
ADR-4 said should be written under *Cross-cutting Concerns* once it was approved.*

*What this story is **not**: it does not grade a clip (1.16), store clips on a memory card or show time remaining or
CARD FULL (1.17), play clips back (1.18), or export them (Epic 9). It builds the recorder those four stories plug
into, and it must leave the seams they need (Task 1c, Task 2).*

> **The starting point in ten facts.** Each was read from a file on 2026-11-15. Re-check any that matter before
> relying on it.
>
> 1. **Capture is the photo shutter today.** `PhotoModeController.OnCapture` (`PhotoModeController.cs:337-340`) is a
>    thin adapter over `Capture()` (`:347-395`). `Capture()` is gated on `IsPhotoMode`, plays the shutter SFX, pops
>    the white flash, grades the best live subject and raises `ShotCapturedChannel`. `GalleryService` (stores a
>    photo) and `GradeHud` (shows the grade) are its listeners.
> 2. **Three regression rigs call `Capture()` directly, 19 times** (`GalleryShootRunner.cs` ×7, `PhotoShootRunner.cs`
>    ×1, `GradeHudShootRunner*.cs` ×11). They exist to photograph the grader through the real path. If `Capture()`
>    becomes "toggle recording", all three break silently.
> 3. **The input action is ready.** `Capture` is a **Button** action bound to `<Mouse>/leftButton` and
>    `<Gamepad>/rightTrigger` (`InputSystem_Actions.inputactions`). Under PlayerInput *Send Messages*, a Button
>    delivers exactly one `OnCapture` per press, which is what a toggle needs. `RaiseCamera` is a **Value** action
>    (LMB-right / LT) on purpose: a Button would never deliver the release (project memory: hold inputs must be
>    Value). PlayerInput lives on `main characters`, with auto-switching of control schemes on
>    (`m_NeverAutoSwitchControlSchemes: 0`).
> 4. **Every lowering of the camera already goes through one method.** `SetMode(CameraMode)`
>    (`PhotoModeController.cs:587-597`) is reached by the RMB/LT release (`OnRaiseCamera` → `SetPhotoMode(false)`),
>    by `SetRaiseSuppressed(true)` (the gallery) and by every rig's `SetPhotoMode(false)`. Stopping the recording
>    there covers all of them at once.
> 5. **The camera's final pose is written in `ThirdPersonCamera.LateUpdate`** (`[DefaultExecutionOrder(-100)]`,
>    `ThirdPersonCamera.cs:11,128`). Its FOV is written in `PhotoModeController.Update` (`:226-235`). A recorder that
>    samples in `Update`, or in a `LateUpdate` that runs before -100, records last frame's camera.
> 6. **The spike that ADR-4 measured is fenced and still compiles in every Editor session.** It lives in
>    `Assets/Spikes/RecordingSpike/` (`defineConstraints: UNITY_EDITOR || CAMERA_RECORDING_SPIKE`,
>    `autoReferenced: false`) and references `CameraGame`. It calls `SetPhotoMode`, `SetZoom`, `ActiveActors`,
>    `PeakOffset`/`IsAtPeak`/`Bounds`, `SetInputSuppressed` and `GameLog`. **If you change any of those
>    signatures, the spike stops compiling and Play mode is blocked.** Four fence tests guard it
>    (`RecordingSpikeFenceTests.cs`). Its README says it is pinned to the old world scale (`EyeHeight 7.5`,
>    `-vantage 38.59,8.29,6.27`), so do not reuse its vantages.
> 7. **There is no AudioSettings-based clock anywhere in game code**, and the spike never used `dspTime` either
>    (confirmed by grep). ADR-4 risk 3: the spike started ticks at the frame and the PCM at the next audio callback.
>    Its takes were −60…+23 ms out, and 160–290 ms out when the start frame did heavy synchronous work.
> 8. **The scene's audio.** `Main Camera` carries the only `AudioListener`. The shutter's 2D `AudioSource` on
>    `main characters` has no mixer group (`OutputAudioMixerGroup: {fileID: 0}`) and `BypassListenerEffects: 0`. The
>    drunk's cue is routed to `GameAudio.mixer`'s `SFX` group (`EventActor_Drunk.prefab:867`). The mixer has
>    `Master` and `SFX` only. Placeholder clips live in `Assets/Audio/` (`ShutterPlaceholder.wav`,
>    `SFX/HiccupPlaceholder.wav`, `SFX/MumblePlaceholder.wav`). There are no beep clips.
> 9. **The viewfinder is a Screen Space – Overlay canvas** (`PhotoViewfinder`, children `ViewfinderFrame` and
>    `CaptureFlash`, which uses Ignore Parent Groups). Its `CanvasGroup` fades over `transitionSeconds` 0.15 s on
>    raise. `GradeHudCanvas` (sorting order 100) draws the grade at the bottom of the screen. The editor-only debug
>    overlay (`PhotoModeController.OnGUI`) sits top-left. **Overlay UI is invisible to `cam.Render()`**: photograph it
>    with `ScreenCapture.CaptureScreenshotAsTexture()` after `WaitForEndOfFrame`, with no `targetTexture` bound.
> 10. **`CameraGame.asmdef` references only `Unity.InputSystem`.** A tick track and a PCM tap need nothing more. Do
>     not add URP or Recorder references for this story. Nothing in 1.15 needs a render pass, because the
>     thumbnail is 1.17's job.

## Story

As a player,
I want to press a button to start and stop recording while my camera is raised,
So that I can film the moment as it happens instead of freezing one instant.

## Acceptance Criteria

ACs 1–4 are verbatim from `epics.md` (Story 1.15). ACs 5–7 were added by this create phase. They restate
ADR-4 (now accepted) and the cross-cutting NFRs where this story bites (NFR3, NFR5, NFR8), as the epic says
("restated in acceptance criteria where they bite"). They add requirements and weaken none. The decision is
recorded in the result file.

1. **Record starts and stops, with camcorder feedback.**
   **Given** I am in Viewfinder state,
   **When** I press Record (LMB / gamepad RT),
   **Then** recording starts within **< 0.2 s**, with a REC indicator, a running timecode and a start beep in a
   2000s-camcorder style (FR3, NFR2)
   **And** pressing Record again stops it, with a stop beep.
   - *Measure* the < 0.2 s from the press, through the real input path, to the first frame showing REC and to the
     beep's first sample (Task 10, Phase A). Report the parts (input → handler, handler → frame, handler → audio)
     rather than one number. Do not assert it.
   - "In a 2000s-camcorder style" is **perceptual**, so it becomes a human check. Measure what can be measured:
     the timecode is correct, the indicator is legible at 1080p, and the two beeps differ.

2. **Lowering stops and keeps.**
   **Given** I am recording,
   **When** I lower the camera (release RaiseCamera),
   **Then** recording stops and the clip is kept. Lowering never throws footage away.
   - Cover **every** route to Walk, not just the button: the RMB/LT release, `SetRaiseSuppressed(true)` (the
     gallery's switch) and a rig's `SetPhotoMode(false)`. They all reach `SetMode` (fact 4).
   - "Kept" means a clip is raised on the channel with footage that is not disposed, and its duration matches
     what was recorded.

3. **It holds its frame rate, and aim and zoom keep working.**
   **Given** I am recording with the drunk event active,
   **When** I record for the length of a full event lifecycle (~30 s),
   **Then** the game holds **60 FPS at 1080p** (NFR1), and aim and zoom keep working while recording (FR2).
   - This laptop cannot judge the absolute 60 FPS. Its baseline with nothing recording runs at 33–52 fps in the
     drunk's segment (ADR-4 risk 4; lessons, 1-14). Measure the recorder's **cost** as a difference against an
     interleaved baseline, in a Windows **release** build (Task 11). Queue the absolute 60 FPS check for a
     mid-range desktop as a human check, worded as a hardware limit, not as a pass.
   - "Aim and zoom keep working" is measurable. Drive Look and Zoom input during a recording. The camera's
     rotation and FOV must follow, and so must the recorded track (Task 10, Phase C).

4. **Same action, gated, announced on a channel.**
   **Given** the existing Capture action (Story 1.5),
   **When** recording is wired,
   **Then** it reuses that action (LMB / RT), which is a no-op in Walk state
   **And** each finished clip is announced on a `ClipRecordedChannel`, so the memory card and HUD can listen without
   the recorder knowing about them (AR4).
   - No new input action, and no rebinding. `GameConstants.InputActions.Capture` and `OnCapture` keep their names.
   - "No-op in Walk" means nothing at all happens: no clip, no beep, no REC, and no log line beyond `Debug_`.
   - "Announced" means **exactly once** per finished clip, synchronously, through `EventChannel<T>` (which already
     isolates a throwing listener). The recorder has no reference to anything that listens.

5. **A clip is ADR-4's footage, and it is faithful** *(added: ADR-4, accepted 2026-11-15)*.
   **Given** a finished clip,
   **When** its footage is read back,
   **Then** it holds ADR-4's replay format: a **30 Hz track** of the camera the player films through (position,
   rotation, FOV) and of every live event actor (which `EventDefinition` it runs and which lifecycle of its pooled
   instance, whether it is live, its transform, and per Animator layer its state, time and transition with the
   transition's duration unit). Each tick carries a **timestamp and a repeat count**, so a game running below
   30 fps still yields a true-duration clip. Alongside the track goes the **listener's PCM as 16-bit**, with its own
   sample rate and channel count, on the same timeline, padded or trimmed to the tick duration
   **And** the track reproduces what the player saw (camera and actor poses against an independent per-frame log,
   and re-rendered frames against the live ones), and the PCM is the game sound the player heard, aligned with
   the picture, measured with an independent marker.
   - The judgement "is this the game's sound, clean and in sync" is perceptual. The numbers are not: lengths,
     offsets, non-silence, and loudness following the drunk's distance.

6. **Bounded, released, fail-soft** *(added: NFR3, NFR5, NFR8)*.
   **Given** there is no memory card yet (Story 1.17),
   **When** clips are recorded one after another, including a clip that spans the drunk's despawn and respawn, and a
   clip that reaches the length limit,
   **Then** footage memory is bounded by a designer-set limit (reaching it stops the recording and keeps the clip,
   as a full card will in 1.17), disposing a clip frees its memory, ten or more cycles do not grow memory, and
   recording allocates nothing on the managed heap per frame
   **And** a missing reference, a bad config value, an audio device change mid-clip, or the recorder being disabled
   mid-clip degrades with **one** clear log line and never throws into `Update`, crashes or hard-pauses (NFR8). The
   console stays clean in normal play (NFR5).

7. **No regression, project restored** *(added: CLAUDE.md, NFR5)*.
   **Given** the change is complete,
   **When** the EditMode suite, the spike fence tests and the existing rigs (Photo Shoot, Gallery Shoot, Grade HUD
   Shoot) run,
   **Then** they pass as before (the photo path through `Capture()` is unchanged for them)
   **And** the editor, scene, project settings, Game View size, time scale and every config value are restored, as are
   the files a Windows build re-serialises (lessons, 1-14).

## Tasks / Subtasks

- [x] **Task 1 — Settle the nine decisions that GATE THE STORY (AC1–AC6). DO THESE FIRST.**

  Each has a recommendation and the evidence that should settle it. Record every choice **with its reason and its
  evidence path** in the Dev Agent Record and in the result file's `decisions`, including the ones where you follow
  the recommendation. Alexv reviews them in one batch and may veto. Where a choice can be settled by measuring,
  measure, as Story 1.10 did for prominence.

  - [x] **1a. What the Capture input does now, and what happens to the photo shutter.** (AC1, AC4, AC7. Blocks
        everything.)
        - **Recommended: re-point only the input adapter.** `OnCapture(InputValue)` calls a new public
          `ToggleRecording()`, and **`Capture()` stays exactly as it is**: same name, same body, same channel. It is
          no longer bound to any input, so the player can no longer fire the flash, the shutter sound or
          `ShotCapturedChannel`. The 19 rig call sites (fact 2) keep working unchanged, and so does the
          per-frame grade path that Story 1.16 will sample ("`ShotCapturedChannel` stays for as long as the clip
          stories still need the per-frame grade path", `game-architecture.md` §Clip Recording).
        - **State the interim consequence plainly** in the Dev Agent Record and the human checks. Between this
          story and 1.16/1.17, stopping a clip shows **no grade** (the HUD listens to `ShotCapturedChannel`) and stores
          **no photo** (the gallery does too). That is the honest transition, not a defect. Do **not** build a
          throwaway bridge.
        - *Alternative B — stop also calls `Capture()`* so the HUD and the gallery still react. Rejected: it fires
          the white flash at every stop, shows a still frame's grade as though it were the clip's, and is
          throwaway code that 1.16 must rip out.
        - *Alternative C — rename `Capture()` to `TakePhoto()`.* Rejected: churn across three rigs for no
          behaviour change.
  - [x] **1b. Where the recorder lives, who drives it, and the dependency direction.** (AC2, AC4.)
        - **Recommended:** a `ClipRecorder` MonoBehaviour in `Assets/Scripts/Recording/` (namespace
          `CameraGame.Recording`; the folder does not exist yet). It sits on `main characters` beside
          `PhotoModeController`, or on its own child, whichever keeps the scene diff smallest. `PhotoModeController`
          gets one `[SerializeField] private ClipRecorder clipRecorder;`. It calls:
          - `ToggleRecording()`: a no-op unless `IsPhotoMode`. It starts if idle and stops with
            `StopReason.PlayerPressed` if recording.
          - In `SetMode`, on any transition out of Photo, it stops with `StopReason.CameraLowered`, so every
            lowering route stops recording (fact 4). Put the stop **before** `Mode` changes or after, but pick one
            and make sure a listener that reads `IsPhotoMode` during the raise sees a consistent state.
        - This is the direction the architecture already fixes: "`PhotoMode` → `Recording` → `Grading` and `Events`
          … The memory card (`Gallery`) and `UI` listen through `ClipRecordedChannel` and never the reverse"
          (§Clip Recording). The recorder knows nothing about PhotoMode, UI, Gallery or audio feedback.
        - **The REC indicator needs live state** (is it recording, for how long), which a channel raised once per
          finished clip cannot give. **Recommended:** the indicator (namespace `CameraGame.UI`) **reads**
          `ClipRecorder.IsRecording` and `ClipRecorder.ElapsedSeconds`, read-only, exactly as `GradeHud` reads
          `PhotoModeController.IsPhotoMode`. That is a new `UI → Recording` read. Add it to §Architectural
          Boundaries with its reason. *Alternative:* a second channel for started and stopped. It is heavier, and
          the indicator would still need the elapsed time.
        - **Who plays the beeps.** **Recommended:** the recorder raises plain C# events `Started` and
          `Stopped(StopReason)` (the architecture's local tier), and the feedback component plays the beep from
          them. Then **every** stop beeps, including a stop the recorder decides itself (the length limit, an audio
          device change), with one code path. Keep it synchronous, inside the call that started or stopped the
          recording (NFR2). It can be `PhotoModeController`, which already owns capture feedback and
          `CaptureConfig`, or a small `RecordingFeedback` component. Choose, and record why.
  - [x] **1c. The channel's payload, and who owns the footage before a memory card exists.** (AC4, AC6.)
        - The architecture leaves the payload type open ("names and exact shapes are finalised by Stories
          1.14–1.17"). **Recommended:** a reference type `RecordedClip` in `CameraGame.Recording`, holding:
          - `Id`: file-name-safe and unique, made like `CapturedShot.MakeId`;
          - `IFootage Footage`;
          - `StartedAtUtc` (`DateTime`, UTC);
          - `DurationSeconds` (the sum of the tick repeats ÷ 30);
          - `StopReason`;
          - optionally, the distinct `SubjectId`s seen live during the clip (cheap, and useful to 1.16 and 1.17).
          The channel is `ClipRecordedChannel : EventChannel<RecordedClip>`. 1.16 will add the grade to this clip or
          wrap it, and 1.17's `CapturedClip` (architecture §Clip Recording) is built **from** it by the card.
        - **Ownership is the NFR3 question**, the same one `GalleryService.cs:16-41` answered for textures: a
          resource handed to an unknown number of listeners has no owner. **Recommended: an explicit claim.** During
          the synchronous raise, a listener that keeps the footage calls `clip.Claim(owner)`. After `Raise` returns,
          if nobody claimed it, the recorder keeps it as its single `LastClip` and disposes the previous unclaimed
          one. It also disposes `LastClip` on `OnDestroy`. Story 1.17's card then claims, and the recorder holds
          nothing, which is an additive change. Bounded: at most one clip in progress plus one unclaimed.
        - *Alternative:* the recorder keeps a ring of N clips. Rejected: it becomes a second owner the day the card
          arrives, and the architecture names `MemoryCardService` the single owner.
        - *Alternative:* the payload carries no footage. Rejected: the card could reach it only through a reference
          back to the recorder, which reverses AR4.
  - [x] **1d. The clip's clock: how ticks and PCM share one timeline.** (AC5. ADR-4 risk 3, `deferred-work.md:303-304`.)
        - The spike's defects, which must not be inherited:
          - ticks timed by a float `unscaledTime`, with no stored timestamp;
          - the PCM starting at the next audio callback, unrelated to the first tick;
          - a take's PCM ±2 DSP blocks off its tick duration (−37 ms measured);
          - 160–290 ms of A/V offset when the start frame was heavy.
        - **Recommended: the audio sample clock is the clip's truth.**
          - At `Begin`, read `AudioSettings.dspTime` on the main thread as the clip's start (`t0`), and arm the tap
            to write PCM from the first sample at or after `t0`. The tap counts samples on the audio thread, and the
            callback knows its own DSP time.
          - Stamp each tick with one monotonic double-precision clock converted to the same timeline. The candidates
            are `Time.realtimeSinceStartupAsDouble` with an anchor pair taken at `Begin`, or `AudioSettings.dspTime`
            itself. **Measure** which jitters less: `dspTime` advances in DSP-buffer steps (~21 ms at 1024 samples
            and 48 kHz) on the main thread.
          - At `End`, pad with silence or trim the PCM to exactly (Σ repeats ÷ 30) × sample rate. Never use
            ffmpeg-style `-shortest` truncation (lessons, 1-14).
        - **Verify before relying on it** that reading `AudioSettings.dspTime` inside `OnAudioFilterRead` is
          permitted and meaningful in 6000.3 (Unity's own `dspTime` metronome example does this, as far as is
          known; `unity_docs` returned an empty page on 2026-11-15, so log it from the callback and look). If it is
          not, count samples from the arm instant instead.
        - **Pause and time scale** (`deferred-work.md:303`: "decide what pause does to a recording"). The game has
          no pause menu. **Recommended:** the clip follows **real time**, because the sound does. A `timeScale = 0`
          freeze, which only rigs do today, records a frozen world plus live sound, and that is truthful. A future
          pause menu is a screen owner like the gallery: it will use the suppression seam, which lowers the camera,
          which stops the recording (AC2). Write that down in `ClipRecorder`'s header so the pause-menu story finds
          it.
  - [x] **1e. How long a clip may run before the card exists, and what is preallocated.** (AC6, NFR2, NFR3.)
        - **Recommended:** a new `RecordingConfig` ScriptableObject (`Assets/Data/Recording/RecordingConfig.asset`;
          the architecture already plans `Data/Recording/`) with `maxClipSeconds`, starting at **120 s**, the ~2 min
          card the stakeholder confirmed on 2026-11-10.
          - Reaching it stops the recording with `StopReason.LengthLimit`, the stop beep, and the clip kept: the
            same behaviour FR27 gives a full card. 1.17 replaces this bound with "the card's remaining seconds",
            and the stop path stays the same.
          - Read it through a `Safe*` accessor, and report bad values in `TryGetConfigProblem`, following
            `GradingConfig` / `GradeHudConfig` (no `OnValidate`; `GradeHudConfig.cs:421-439` explains why).
        - **Preallocate at `Awake`, never at `Start`.** A 120-s stereo 16-bit clip is 120 × 48 000 × 2 × 2 B ≈
          **23 MB**. Allocating it on the press frame costs the < 0.2 s budget and churns memory every take.
          - The track is tiny: 87 B/tick for one actor, ~3,600 ticks ≈ 0.3 MB.
          - With 1c's design, at most two clip-sized buffers are live: one recording and one unclaimed.
          - *Alternative:* a pool of fixed-size PCM chunks (e.g. 0.5 s), preallocated up to the limit. It suits
            1.17's shared card capacity better, but the audio thread must take chunks without locking or
            allocating.
          - Choose by measuring memory and GC (Task 10, Phase E), and record it.
        - The tick rate (30 Hz) and the 16-bit sample format are **invariants of the footage format**, not
          tunables. Make them constants with a format version, since a change would break 1.17's persisted clips.
  - [x] **1f. Do the start/stop beeps go into the footage?** (AC1, AC5.)
        - ADR-4: "the tap records **everything** the listener hears, including … the camcorder's own REC beeps. A
          sound that must stay out of the footage sets `bypassListenerEffects = true` on purpose".
        - **Recommended: keep them out.** It is the same principle as "the footage … with **no UI** … a camcorder
          films the scene, not its own REC dot" (ADR-4, *Frame source and UI*). The beep is feedback for the
          operator, and a clip that opens with a beep every time would grate in 1.18's playback.
        - ⚠️ **Measure that the exclusion really happens. Do not assume it.** The 1.14 lesson says sources with
          `bypassListenerEffects = true` are not captured, but it was measured on a source **not** routed to a
          mixer. If you route the beeps to `GameAudio.mixer` (1g), check again: a mixer group's output may reach the
          listener's filter chain regardless of the source's flag. If it does, the beep source stays unrouted and
          you say why.
        - *Alternative:* let the beeps in, as some real camcorders do. Then they also become the A/V marker. Choose
          by evidence, and record it.
  - [x] **1g. The beeps and the REC/timecode look.** (AC1. Perceptual, so a human check follows.)
        - *Toolbox: invoke `unity:ui`, then `unity:ui-ugui`, before writing UI code. Invoke `unity:audio-setup-mixers`
          and `unity:optimize-audio` before routing or importing the beeps.*
        - **Beeps: recommended as generated placeholder WAVs**, made by a small committed script (e.g.
          `tools/audio/make_camcorder_beeps.py`, numpy plus `wave`), so they can be reproduced and replaced:
          - `Assets/Audio/SFX/RecStartBeepPlaceholder.wav`: one short tone (~60–90 ms, a few kHz, gentle attack and
            release so it does not click);
          - `RecStopBeepPlaceholder.wav`: **two** short tones, so start and stop sound different even without the
            picture.
          - Import them mono and Decompress On Load (`unity:optimize-audio`). Play them through a 2D `AudioSource`
            (spatial blend 0, Play On Awake off).
          - Do **not** use the MCP `generate_audio` tool or any online generator: they may cost money or need an
            account (a blocker under the loop's rules).
        - **Look: recommended** as a **● REC** cluster in the **top-right** of the viewfinder: a red dot, the text
          `REC`, and an elapsed timecode `H:MM:SS`, as on 2000s consumer camcorders. Top-right is chosen because
          the debug overlay is top-left and the grade HUD bottom. Settle these and record them:
          - whether the dot blinks (on unscaled time) or stays steady;
          - whether `STBY` shows while the camera is raised but not recording (a nice camcorder touch; optional);
          - the font size at 1080p (reuse the HUD's 1920×1080 CanvasScaler reference, match 0.5).
          - Do not spend the story styling (1.12 AC4's rule): readable and recognisably camcorder is enough.
        - **The timecode follows the footage**: `ElapsedSeconds` from the recorder's own timeline, not
          `Time.time`. Then the number on screen is the clip's duration. Format with
          **`CultureInfo.InvariantCulture`** (lessons, 1-19: fr-FR prints an Arabic percent sign), and only when the
          displayed second changes, so there is no string per frame. Pin the boundaries in tests: 0, 59.999, 60,
          3599.999, 3600, NaN and negative values.
        - **Visible the frame recording starts.** The viewfinder group fades in over 0.15 s, so a REC cluster inside
          it would be half-transparent if the player presses record right after raising. **Recommended:** give the
          cluster its own `CanvasGroup` with Ignore Parent Groups, as `CaptureFlash` does (Story 1.5's review fix),
          driven only by `IsRecording`.
  - [x] **1h. How AC3's frame rate is measured on this laptop.** (AC3.)
        - Lessons, 1-14:
          - "Measure FPS as a delta against an interleaved baseline in a release build, and flag the absolute 60
            FPS verdict for Alexv."
          - Frame cost follows what the camera looks at (up to 4× across the drunk's cycle).
          - The laptop heats run to run.
        - **Recommended:** a Windows **release** build carrying a small **fenced bench** (Task 11). It drives the
          real `ToggleRecording()` through the same scripted segment twice per round, once recording and once
          not, with at least 3 rounds, order rotated. It reports the recorder's cost as a difference-in-differences,
          plus raw absolute fps.
        - Start with an **editor interleaved A/B** (`ProfilerRecorder` on main-thread time and on GC allocated in
          frame, or `Unity.PerformanceTesting` `Measure.Frames()`). It gives fast feedback while you build and is
          not the evidence.
        - *Toolbox:*
          - Performance Testing 3.7.0 (`Unity.PerformanceTesting`): numbers are evidence, never thresholds;
          - Profile Analyzer 1.4.2: compare a recording capture against a baseline capture;
          - Graphy 4.0.0: an optional on-screen read in the build, never shipped switched on;
          - invoke `unity-perf:unity-performance` and `unity-perf:csharp-zero-gc` before the recorder's hot path.
        - **Adopting `Unity.PerformanceTesting` in a new PlayMode test assembly is a decision.** Record it if you
          take it. A bench assembly that ships in no plain build is a fence, as the spike's was, so prove the fence
          (Task 11.4).
  - [x] **1i. What the track records for each actor, and how it finds them.** (AC5. ADR-4 *Consequences*.)
        - ADR-4 and the 1.14 review list the spike's defects:
          - it recorded "the first live actor" and cloned it;
          - it did not name the `EventDefinition`;
          - it called `GetComponentInChildren<Animator>()` on every tick;
          - it fixed the layer count when the track was allocated;
          - it stored transition durations without their unit.
        - **Recommended:** every tick reads `EventManager.ActiveActors` **live**, never cached across a despawn
          (ISubject's liveness contract), and skips `!isActiveAndEnabled` as grading does
          (`PhotoModeController.cs:545-550`). For each actor it records:
          - its `EventDefinition`. Add a read-only `EventActor.Definition`; the field is private today
            (`EventActor.cs:25`). Keep the asset reference in memory, and its `Id` for a later disk format;
          - a **lifecycle serial**: a counter `EventActor` increments in `Begin`. The pooled drunk is the *same*
            instance on every cycle, so without it a clip spanning a respawn cannot tell two lifecycles apart;
          - liveness and transform;
          - for each of its Animator's layers, counted per actor: `fullPathHash` and `normalizedTime`, and in a
            transition the next state's hash and time plus `AnimatorTransitionInfo.normalizedTime`, `duration` and
            **`durationUnit`** (confirmed present by `unity_reflect` on 2026-11-15). Cache the Animator per lifecycle.
        - **Recordability check:** `DrunkAnimator` has one layer, no parameters, no blend trees and no IK today. If an
          actor's Animator has parameters or more than one weighted layer, warn **once**: the track does not carry
          them (the *Recordability* rule in `game-architecture.md`). That turns a silent replay difference into a
          logged one.
        - Size the per-tick actor slots from a configured maximum (`maxConcurrent` is 1 for the slice, NFR6). Warn
          once if more actors are live than the track can hold. Never allocate per tick.

- [x] **Task 2 — The footage seam and data model** (AC4, AC5, AC6; Task 1c, 1e)
  - [x] 2.1 Create `Assets/Scripts/Recording/` with the narrow seam ADR-4 fixes ("so that a veto swaps the backend
        and not the design"):
        - `IClipBackend` with `Begin(...)`, `Sample(...)` once per game frame, and `End() → IFootage`, plus an `Abort`
          if you need one;
        - `IFootage` with `float Seconds`, `long Bytes` and `void Dispose()`, idempotent;
        - `ReplayFootage : IFootage`, the first implementation: the track plus the PCM;
        - `RecordedClip` and `StopReason`.
        Keep `IFootage` free of replay specifics, so a different backend can implement it.
  - [x] 2.2 Give `ReplayFootage` a small **read API**: tick count, each tick's timestamp, repeats, camera pose and
        FOV, each tick's actors (definition, serial, liveness, pose, layer states), and PCM access (sample rate,
        channels, frames, read into a caller's buffer). The rig needs it now (Task 10, Phase D), and 1.18's playback
        will need it later. It is **read-only**: nothing outside the backend writes footage.
  - [x] 2.3 `ClipRecordedChannel : EventChannel<RecordedClip>` with `[CreateAssetMenu]`, in `Scripts/Recording/`
        (architecture §Directory Structure). Create `Assets/Data/Channels/ClipRecordedChannel.asset` and
        `Assets/Data/Recording/RecordingConfig.asset`, with their `.meta` files. **Unity MCP cannot create custom
        ScriptableObject assets**: write the `.asset` YAML using the script's `.meta` GUID (project memory,
        `create-so-asset-via-yaml.md`). Then `refresh_unity` and read the values back through
        `AssetDatabase.LoadAssetAtPath`.
  - [x] 2.4 `RecordingConfig`: `maxClipSeconds` and whatever Task 1g/1e settle (beep volumes, blink rate, maximum
        actors per tick). Each value has a `Safe*` accessor. Non-finite values fall back to the design default and
        out-of-range values are clamped, with the constants shared by the initialiser and the accessor. Problems are
        reported by `TryGetConfigProblem`, which is called once in `Awake` and logged as a warning.

- [x] **Task 3 — The tick track** (AC5, AC3; Task 1d, 1i)
  - [x] 3.1 Sample after the camera's final write. Use a `LateUpdate` with `[DefaultExecutionOrder]` later than
        `ThirdPersonCamera` (-100); the spike used 10000. **Prove the ordering**: in the rig, compare each recorded
        camera pose with the pose the camera actually rendered with (read in `RenderPipelineManager.beginCameraRendering`
        on the same frame). Expect 0 difference. A non-zero difference means the track is a frame late.
  - [x] 3.2 Record the camera `PhotoModeController` films through: the same `photoCamera` grading uses, injected,
        not `Camera.main`. GalleryService's tooltip explains why two cameras would be "plausible-looking, completely
        wrong".
  - [x] 3.3 30 Hz scheduling with **repeat counts and a timestamp per tick** (Task 1d). A tick is due at each
        1/30-s boundary of the clip's timeline. The first frame past a boundary takes one tick, whose repeat count
        is the number of boundaries crossed since the last tick (1 at ≥ 30 fps, more below). Tick 0 is taken on
        the start frame. Write the boundary arithmetic as a pure static function, so it can be tested in edit mode
        with no Unity objects.
  - [x] 3.4 Actors, per Task 1i. Add only read-only accessors to `EventActor` (`Definition`, the lifecycle serial,
        and the Animator if you need it). Change **no** existing signature (fact 6).
  - [x] 3.5 No per-frame allocation: no LINQ, no boxing, no `GetComponent*` per tick, no strings. Preallocated
        struct arrays only. *Invoke `unity-perf:csharp-zero-gc`.*

- [x] **Task 4 — The listener PCM tap** (AC5, AC6; Task 1d, 1e, 1f)
  - [x] 4.1 A `ListenerTap` MonoBehaviour with `OnAudioFilterRead`, on the **AudioListener's GameObject** (Main Camera,
        fact 8). `OnAudioFilterRead` hears the mix only from that GameObject. Hold it as a serialized reference in
        `ClipRecorder`. In `Awake`, check that it really shares a GameObject with an enabled `AudioListener`, and
        warn once if not (`deferred-work.md:303`: the spike "assumed the listener is on `Camera.main`" with no
        warning).
  - [x] 4.2 Audio-thread rules:
        - no allocation, no Unity API calls other than ones verified safe, and no lock held across a copy;
        - convert float to 16-bit with clamping, on the audio thread or in bounded batches;
        - take the channel count from the callback argument and the sample rate from
          `AudioSettings.outputSampleRate` (read on the main thread). The clip records both (ADR-4: `AudioManager`'s
          rate is 0, "the device's rate");
        - when not recording, early-out at near-zero cost. The filter runs on every audio block for the whole
          session.
  - [x] 4.3 Hand-off and length. Use volatile or `Interlocked` state between the threads. At `End`, wait for nothing
        on the main thread (no `Sleep` loops; that was a 1.14 export-path defect). Pad or trim to the tick timeline.
        Store whether the clip has sound at all, so a clip recorded with no tap still has a valid timeline (silence,
        flagged).
  - [x] 4.4 `AudioSettings.OnAudioConfigurationChanged`, a device or sample-rate change mid-clip: stop the clip
        (`StopReason.AudioDeviceChanged`), keep it, and warn once. Provoke it in the rig with `AudioSettings.Reset`
        on the current configuration, and restore the configuration.
  - [x] 4.5 Keep the spike's WAV down-mix bug out of anything shipped: it treats channel 2 as centre (`AudioTap.cs:85`).
        Only rigs write WAVs in this story. Down-mix properly, or write the native channel count.

- [x] **Task 5 — `ClipRecorder` and the PhotoModeController wiring** (AC1, AC2, AC4, AC6; Task 1a–1c, 1e)
  - [x] 5.1 `ClipRecorder`:
        - `IsRecording`, `ElapsedSeconds` (the footage timeline) and `LastClip`;
        - `Begin()` returns a bool. It is false and logs once when it cannot record, for example with no backend.
          Then no beep plays and no REC shows. *Decide:* is a camera that cannot record silent, or does it give an
          error beep? Record the choice;
        - `Stop(StopReason)` is idempotent;
        - it enforces the `maxClipSeconds` limit itself;
        - `Started` / `Stopped(StopReason)` events;
        - on stop: `End()` on the backend, build the `RecordedClip`, raise `ClipRecordedChannel` **once**, then apply
          the ownership rule (Task 1c).
        - **"Log once"** (`deferred-work.md:306`): Info lines for start and stop are milestones, which is fine; a
          missing reference logs once per session, not per press.
  - [x] 5.2 `PhotoModeController`:
        - `OnCapture` → `ToggleRecording()`, which is gated on `IsPhotoMode` (AC4) and does nothing if the recorder
          is unassigned (fail-soft; the existing readiness-flag pattern, `_recordReady`);
        - stop on any transition out of Photo in `SetMode` (Task 1b);
        - **keep `Capture()` byte-for-byte as it is** (Task 1a);
        - update the class and method doc-comments, which still say "Stories 1.4 (zoom) and 1.5 (capture) gate on
          IsPhotoMode": record now gates on it. Keep the editor debug overlay as it is.
        - ⚠️ **Three rigs build their own `PhotoModeController` in isolated worlds, with no recorder:**
          `GalleryShootRig.cs:218`, `PhotoShootRig.cs:375` and `GradeHudShootRunner.cs:788`. If a missing
          `clipRecorder` logs at `Error` level in `Awake`, like the missing `ShotCapturedChannel` does, every one of
          those runs gains an error line, and "a rig that logs its own errors trains you to ignore errors"
          (CLAUDE.md). Decide the level on its merits. A camcorder that cannot record in the real game is critical,
          so `Error` is defensible. Then either give those rigs a recorder, or add the line to their list of
          expected messages. Rigs are tools, and changing them is allowed. Record the choice.
  - [x] 5.3 Teardown (`deferred-work.md:34`: "No `OnDisable` teardown — disabling mid-Photo leaves the mode … stuck").
        If the recorder or `PhotoModeController` is disabled or destroyed mid-recording (scene unload, play-mode
        exit, a rig turning it off), stop with `StopReason.Disabled`. Decide whether to raise or to dispose silently
        during teardown, test both exits, and record the choice. Native or preallocated buffers must be released:
        no "A Native Collection has not been disposed" warning at play-mode exit, if you use `NativeArray`.
  - [x] 5.4 Mind the spike's compile (fact 6). After every change, `refresh_unity` and `read_console`: a spike compile
        error blocks Play mode for everyone.

- [x] **Task 6 — REC indicator, timecode and beeps** (AC1; Task 1f, 1g)
  - [x] 6.1 Generate the beep placeholders with the committed script, import them (Task 1g), and add a 2D
        `AudioSource` for them, with `bypassListenerEffects` set per Task 1f and a comment saying why. Route it, or
        not, per `unity:audio-setup-mixers` and the measurement in Task 1f.
  - [x] 6.2 A `RecordingHud` (name it as you see fit) in `Assets/Scripts/UI/`, on the `PhotoViewfinder` canvas:
        - REC dot, `REC` label and timecode;
        - its own `CanvasGroup` (Ignore Parent Groups);
        - it reads `ClipRecorder` state (Task 1b) in `Update`;
        - timecode text only on change, invariant culture;
        - a blink, if chosen, on unscaled time;
        - it hides at once on stop.
        Fail-soft: with no recorder, the HUD stays hidden and logs once.
  - [x] 6.3 Build the UI objects in the editor through MCP (`manage_ui` / `manage_gameobject`), or in code at
        `Awake`. Pick one and say why. Scene edits are unavoidable in this story (the recorder, the tap, the audio
        source and the wiring), so keep them few and deliberate (Task 7).

- [x] **Task 7 — Scene wiring** (AC4, AC7)
  - [x] 7.1 In `SampleScene`, through MCP in the editor: add `ClipRecorder` (wired to the channel, config, camera,
        `EventManager` and tap), `ListenerTap` on Main Camera, the beep `AudioSource`, the `RecordingHud`, and
        `PhotoModeController.clipRecorder`. **Save the scene in a separate call** before any rig run (project
        memory: a save dialog mid-rig causes six menu retries).
  - [x] 7.2 Read the wiring back through the editor (`execute_code`: `GetComponent`, serialized fields not null) before
        trusting a rig. Check the scene diff is only what you meant: `SampleScene.unity` is 16.9 MB and CRLF, and the
        orchestrator merges master into this branch (lessons, 2-2: the "modified externally" modal).
  - [x] 7.3 Leave `Video Camera Game 2.0.unity` alone unless it is in the build settings' scene list. Check that list.

- [x] **Task 8 — EditMode tests** (AC4–AC6)
  - [x] 8.1 Pure logic, no scene:
        - tick scheduling (boundaries, repeat counts at 15/30/60/144 fps, a hitch of 0.5 s, tick 0 on the start
          frame);
        - the PCM pad/trim length rule;
        - float→16-bit clamping (±1, beyond ±1, NaN);
        - timecode formatting under fr-FR, de-DE and tr-TR, at the Task 1g boundaries;
        - `RecordingConfig` `Safe*` accessors and `TryGetConfigProblem` (0, negative, NaN, ±∞, huge), plus "the
          shipped asset passes its own validator";
        - `RecordedClip` claim and dispose (idempotent, a second claim refused, `Bytes` after dispose);
        - `ClipRecordedChannel` dispatch isolation is inherited. Do not re-test the base class.
  - [x] 8.2 **Prove the tests can fail** (mutation, as 1.19 and 1.14 did): break the repeat-count rule or the
        culture pin, watch the test go red, and restore. Record it.
  - [x] 8.3 Keep `RecordingSpikeFenceTests` green. If you add a bench assembly (Task 11), give it the same four
        guards: no game assembly references it, it is not compiled into a plain player, and a positive control.

- [x] **Task 9 — Architecture, deferred work and docs** (AC7)
  - [x] 9.1 `game-architecture.md` §Clip Recording: replace "names and exact shapes are finalised by Stories 1.14–1.17"
        with what was built (types, payload, ownership rule, clock). Add the `UI → Recording` read to
        §Architectural Boundaries if Task 1b took it. Add `RecordingConfig.asset` to §Directory Structure.
        **Leave ADR-4's decision text alone.** It is accepted, and only its "What Story 1.15 builds" paragraph may
        gain a pointer to the built code.
  - [x] 9.2 `deferred-work.md`: mark `:303`, `:304` and `:306` (the 1.15 actions) resolved, with how, and `:34` if Task
        5.3 settled it. Add new entries for what you defer (for example "Capture() is unbound from input until 1.16
        decides its fate").
  - [x] 9.3 Do not edit `loop/` (the orchestrator's), and do not change `RaiseSuppressed` into a refcount. That is
        required only when a *second* suppressor appears (`deferred-work.md:144`), and this story adds none.

- [x] **Task 10 — Verify by running it: the Record Clip rig** (AC1–AC6)

  An editor `[MenuItem]`, e.g. `Tools/Recording/Record Clip Shoot (Play)`. Follow `PhotoShootRig` / `GradeHudShootRig`:
  a `SessionState`-armed runner in the **runtime** assembly behind `#if UNITY_EDITOR` (CLAUDE.md trap: an
  Editor-assembly MonoBehaviour is "Unknown" on a scene object in play mode), with timestamped arming that expires
  (lessons, 1-14). Output goes to `_bmad-output/verification/1-15-record-a-clip/<phase>/`. Run it in **SampleScene**,
  because the scene wiring is part of what is under test, at **1920×1080** (the Game View's built-in Full HD preset
  by reflection, as `GradeHudShootRig` does, restored afterwards). Log the capture size and the canvas scale factor
  on every run. Use rig-chosen vantages: read the eye height from `PlayerConfig`, **never** the spike's vantages
  (fact 6). At the top of each output, say which errors the run provokes on purpose.

  - [x] **Phase 0 — controls.** Before trusting an image, prove:
        - a frame with known 3D content is not uniform or near-white;
        - a full-screen Overlay marker dominates, then returns to zero when switched off (1.12 measured 100.00 % and
          0.00 %);
        - one live frame passes between a grab and any `timeScale = 0` freeze (lessons, 1-19).
  - [x] **Phase A — through the real input (AC1, AC4).** Drive a **virtual Gamepad** (`InputSystem.AddDevice<Gamepad>()`,
        removed afterwards) with queued state events: LT to raise, RT to record, RT to stop, LT released. In the
        editor, mouse and keyboard input only reaches the game while the Game View has focus (the Input System's
        default `editorInputBehaviorInPlayMode`, and this project has no input-settings asset), so a queued mouse
        click on an unattended editor can vanish silently. A gamepad is not subject to that rule, and it proves the
        RT binding too. **Prove the press arrived** (a log, or `OnCapture` counted) before reading any "no-op".
        Triggers are analog: queue 1.0, then 0.0, since the press point is 0.5 by default. PlayerInput auto-switches
        to the Gamepad scheme when the device speaks. After removing the device, log `currentControlScheme` and
        make sure nothing persists. Then:
        - RT in Walk: no clip, no beep, no REC (AC4);
        - press to REC visible: the first captured frame (`CaptureScreenshotAsTexture` after `WaitForEndOfFrame`)
          with REC pixels, in ms after the press;
        - press to beep: the first non-zero sample of the beep, read by an `OnAudioFilterRead` tap **on the beep
          source's own GameObject**, because the listener tap may exclude it (Task 1f). Add the output latency from
          `AudioSettings.GetDSPBufferSize`.
        Report each part. Save REC-visible frames (raised and idle, recording at 0:00:0x, stopped) for the human
        check, plus the beeps as WAVs.
  - [x] **Phase B — every way a recording ends (AC2, AC6).** Press, press; press, then release LT; press, then
        `SetRaiseSuppressed(true)` (and release it again); press with `maxClipSeconds` temporarily set to ~3 s
        (**restore the asset value and read it back**); rapid press, press, press; ten consecutive clips. For each:
        exactly one channel raise, the `StopReason`, the duration against the wall-clock time between the presses,
        the footage not disposed when kept, and the previous unclaimed clip disposed (Task 1c). Bugs in this project
        hide in the second cycle onward, so look hardest at clips 2–10.
  - [x] **Phase C — aim and zoom while recording (AC3).** While recording, drive Look (right stick) and Zoom (dpad)
        through the same virtual gamepad. Log the camera's rotation and FOV per frame and compare them with the
        track's ticks. Capture frames before and after the zoom with REC showing.
  - [x] **Phase D — fidelity (AC5).** One ~30-s clip framing the drunk through his peak, and one ~60–90-s clip that
        spans a despawn and the next spawn (respawn delay 2 s). For both:
        - **Poses:** the track against an independent per-frame log taken at render time. Expect 0 for the
          camera. For the actor, compare at the tick's own frame.
        - **Picture:** at N ticks, re-render the track through a **rig-side ghost** (a fresh instance of the drunk
          prefab with gameplay components stripped, posed with `Play(hash, layer, t)` and `Update(0)`, the
          transition by its recorded unit; ADR-4 *Consequences*), next to the live frame grabbed on that tick. Build
          a contact sheet and SSIM (`tools/verification/recording-spike/ssim.py`, `sheet.py`). Expect the
          shader-time sway of trees and grass to differ (ADR-4) and say so. The ghost is a **rig instrument**, not
          1.18's playback: keep it in the runner, and do not ship it.
        - **Sound:** write the PCM to WAV. Check it is not silent, that the drunk's cue is louder as he nears
          (correlate RMS with distance, as 1.14 did: r −0.58…−0.73), and that the PCM length equals the tick
          duration.
        - **A/V offset:** use an **independent marker**: a click scheduled with `AudioSource.PlayScheduled` at a
          known DSP time, and a visual change (e.g. the camera FOV jump, or a marker toggled) on the frame nearest
          that DSP time. Find both in the footage and report the offset. Then repeat with a deliberately heavy
          start frame (a 200 ms busy-wait in the press frame) to show the alignment no longer depends on frame work
          (ADR-4 risk 3).
        - **The beep's absence** (or presence) in the PCM, per Task 1f.
        - **The respawn clip:** two lifecycles with different serials, liveness false in between, and no stale pose
          carried over.
  - [x] **Phase E — memory and allocation (AC6).** Over the ten clips and a clip that reaches the length limit:
        `Profiler.GetTotalAllocatedMemoryLong`, `GetTotalReservedMemoryLong`, and `ProfilerRecorder` "GC Allocated In
        Frame" while recording (expect 0 B per frame) and while idle. Report footage `Bytes` against your preallocated
        capacity, before and after dispose. Look for a ramp, not just the endpoints.
  - [x] **Phase F — fail-soft (AC6). These are provoked errors: name them at the top of the output.** On a throwaway
        recorder, or by temporarily clearing a field and restoring it:
        - no channel (it records, keeps `LastClip`, logs once);
        - no tap (track only, flagged silent, logs once);
        - no `EventManager` (camera-only track);
        - `maxClipSeconds` at 0, NaN and 1e9;
        - an audio configuration reset mid-clip (Task 4.4);
        - the recorder disabled mid-clip, and play mode exited mid-clip (Task 5.3).
        Each must give one log line, no exception and a running game. Then **re-check the console is clean in a
        normal run** (NFR5).
  - [x] **Look at every capture yourself** (the Read tool shows images). Draw the invisible values onto them: the
        timecode the recorder held, the tick index, and the stop reason. Suspect the rig first if every case
        passes, every case fails identically, a number is suspiciously round, or a capture is blank.
  - [x] **Before every rig run:** call `refresh_unity`, wait for the compile, and read the config values and the
        wiring back through the editor (CLAUDE.md trap). Check that `mcpforunity://editor/state` shows
        `external_changes_dirty: false`.
  - [x] **After every run, restore and read back:** the scene is not dirty and has no rig objects; `timeScale` is 1;
        the Game View size is back; the config assets match git; the virtual gamepad is removed; the audio
        configuration is unchanged; no rig `SessionState` keys remain; and `git status` is clean apart from your
        work.

- [x] **Task 11 — AC3 in a Windows release build** (AC3, AC7; Task 1h)
  - [x] 11.1 A **fenced bench**, built only into the bench build. Copy the spike's pattern:
        - a define-constrained asmdef (e.g. `CAMERA_RECORDING_BENCH`) with `autoReferenced: false`;
        - the define set only through `BuildPlayerOptions.extraScriptingDefines`.
        The bench takes a command-line flag; loads SampleScene; places the player at a vantage with a clear view of
        the drunk's route (survey it, at the ADR-5 scale); raises the camera through `SetPhotoMode(true)`; and
        drives the **real** `ToggleRecording()` for a ~30-s segment covering the drunk's peak. It alternates with
        the same segment not recording, at least 3 rounds, order rotated, and writes a per-frame CSV in the schema
        `tools/verification/recording-spike/analyse.py` reads (`i,t,dt_ms,phase,…`).
        - The bench may reference game code. Game code must never reference the bench.
        - Do **not** extend the spike instead: it is pinned to the old scale, and its harness measures the spike's
          own `ReplayTrack`, not this story's recorder.
  - [x] 11.2 Compute the difference-in-differences. No committed script does it today (`analyse.py` does not), so add
        one under `tools/verification/` that reads the CSVs. Report the recorder's cost per round, the mean and the
        range, raw absolute fps, and frames dropped or repeated, as ADR-4 did. Also sample process memory from
        outside the build (`procmem.ps1`; Mono's `PrivateMemorySize64` reads 0).
  - [x] 11.3 Build into `Builds/1-15-record-a-clip/` (git-ignored) with the Unity MCP build tool. Run windowed at
        1920×1080 on mains power. Kill the exe when done. If the workstation is locked, `gdigrab` fails with
        "error 8" (lessons, 1-14): the bench does not need gdigrab, so do not depend on it.
  - [x] 11.4 Prove the fence:
        - list a **plain** build's `Managed/` folder and check the bench DLL is absent;
        - run the fence tests (Task 8.3);
        - check that `CompilationPipeline.GetAssemblies(PlayerWithoutTestAssemblies)` does not include the bench
          (lessons, 1-14).
  - [x] 11.5 **The first Windows build re-serialises settings**: PC_RPAsset prefilter flags, URP global settings,
        ProjectSettings preloadedAssets and batching, UnityConnectSettings (lessons, 1-14). Restore them from HEAD
        and reimport after every build. Check with `git status` / `git diff` that only your intended files changed.

- [x] **Task 12 — Regression, restore, close** (AC7)
  - [x] 12.1 Run the EditMode suite with `run_tests` (it was 297/297 on 2026-11-15), the fence tests included.
  - [x] 12.2 Re-run the rigs whose code paths this story touches. Decide by diffing (lessons, 1-19: "diff shipped code
        from each rig run's commit to HEAD, then grep who calls the changed members"). Expect at least:
        - `Tools/Grading/Photo Shoot (Play)`, `Tools/Gallery/Gallery Shoot (Play)` and
          `Tools/HUD/Grade HUD Shoot (Play)`: they call `Capture()` and `SetPhotoMode`, and `SetMode` changes;
        - a normal SampleScene play session: the drunk over 2 pooled cycles, raise and lower, the gallery opening in
          Walk.
        Rigs write into their own evidence folders, so copy those aside before a re-run and diff them back
        (lessons, 2-2). Record why any rig was not re-run.
  - [x] 12.3 `read_console` is clean: only the known baseline lines, such as the standing "Head bone not found!"
        warning, if it is still present.
  - [x] 12.4 Look at the evidence as a sceptic would (the verification plan below). Fix what it finds before queueing
        any human check. A human check is never a place to park a defect (`loop/prompts/_rules.md`).
  - [x] 12.5 Commit on `loop/1-15-record-a-clip`, staging files by name with every new asset's `.meta`. Update the
        sprint status line (Dev Agent Record, File List, Change Log).

### Review Findings

*Loop REVIEW phase, 2026-11-15 (Claude Opus 5.5). Diff `1f83710..83c74fe`, shipped code only; rigs were run, not
reviewed. Layers: Blind Hunter (diff only), Edge Case Hunter (diff + project), Acceptance Auditor (diff + this
story + game-architecture.md), and a standards / zero-GC pass (`unity-perf:code-standards`,
`unity-perf:csharp-zero-gc`, `unity-perf:unity-performance`; it confirmed the zero-allocation and audio-thread
claims line by line). Every finding below marked **reproduced** was
triggered in SampleScene play mode through the real path (`PhotoModeController.ToggleRecording`, `SetPhotoMode`,
Send Messages, `AudioSettings.Reset`, a 440 Hz tone source as the known signal) before it was fixed, and
re-run after the fix. Evidence: `_bmad-output/verification/1-15-record-a-clip/review/` (`runtime.txt` holds
every probe's before and after numbers).*

**Fixed (patch):**

- [x] [Review][Patch] **A clip stopped in the frame it started had 0 ticks and 0 s, and its readers' `TickAt(0)`
  threw** [ReplayBackend.cs `End`]. Reproduced: `ToggleRecording()` then `SetPhotoMode(false)` in one frame (RT
  pressed and LT released in the same input update) announced `clip_0001 0.000 s, 0 ticks`, replaced the held
  clip, and `TickAt(0)` threw `ArgumentOutOfRangeException`. Found by all three layers. **Fix:** `End` takes the
  start tick when none was sampled (the camera and actors as last seen, one slot). After: 1 tick, 0.0333 s.
  Test `Backend_ClipStoppedBeforeItsFirstSample_StillHasItsStartTick`, mutation-proven (red without the fix).
- [x] [Review][Patch] **A speaker-mode change mid-clip threw away the clip's whole sound** [ReplayBackend.cs
  `End`: `hasSound = armed && !mismatch`]. Reproduced: 7.47 s of tone was written (RMS 0.125, 670,936 of
  672,000 samples non-zero), then a switch to Mono with a 200 ms hitch. The clip came back `HasSound False,
  PcmFrames 0`. That contradicts AC6, `StopReason`'s "every stop keeps the footage" and the tap's own doc.
  **Fix:** `HasSound` means the tap wrote samples; what came before the change is kept and the rest is padded.
  After: `HasSound True`, PCM exactly ticks × 1600, RMS 0.125 up to the 5.167-s change. Test
  `Tap_ChannelChangeMidClip_KeepsTheSoundRecordedBeforeIt`, mutation-proven.
- [x] [Review][Patch] **A tap destroyed before the stop had its sound zeroed while the clip still claimed sound**
  [ReplayBackend.cs `End`: Unity's `_tap != null`]. Reproduced: 8.3 s of tone came back as 0 non-zero samples
  with `HasSound True`. **Fix:** a reference check (`Disarm` touches no Unity API). After: 7.57 s kept, RMS
  0.125. Test `Tap_DestroyedBeforeTheStop_KeepsTheSoundItWrote`, mutation-proven.
- [x] [Review][Patch] **A tap that heard nothing produced a clip flagged as having sound** (pure padding).
  Reproduced with the AudioListener disabled: the tap saw no block, yet the 6.8-s clip had `HasSound True` with
  326,400 frames of zeros. **Fix:** the same `HasSound` rule. After: `HasSound False`, `PcmFrames 0`, the
  timeline kept (5.9 s). Test `Tap_ThatHeardNothing_GivesAClipFlaggedSilent`, mutation-proven.
- [x] [Review][Patch] **The clip's channel count came from `speakerMode`, not from what the listener delivers**
  (Task 4.2). Reproduced on this stereo-only device (`driverCapabilities` Stereo): with Quad or 5.1 configured,
  the callback did not deliver 4 or 6 channels. Every clip would therefore mismatch on its first block, stop
  with `AudioDeviceChanged` and be silent. A mono-only output (a headset in hands-free mode) would do the same to
  the shipped Stereo setting. **Fix:** `ReplayBackend.ListenerChannels()` clamps the speaker mode to
  `driverCapabilities`. After: an 8.4-s clip under 5.1 has 2 channels, no mismatch, and sound (RMS 0.125). Not
  measured on real mono or 5.1 hardware.
- [x] [Review][Patch] **A disabled `PhotoModeController` still started clips** [PhotoModeController.cs
  `ToggleRecording`]. Reproduced: Send Messages reaches disabled components, and `OnDisable` leaves Mode at
  Photo. A control with the controller enabled showed the probe fires (a null argument does not reach
  `OnCapture(InputValue)`, so the first probe was blind). **Fix:** gate on `isActiveAndEnabled`, plus Unity's
  null check on `clipRecorder` (a recorder destroyed on its own made every press throw), and the same null
  check in `SetMode`. After: control True, disabled False.
- [x] [Review][Patch] **STBY ("ready") showed on a camera that cannot record** [RecordingHud.cs `Update`].
  Reproduced: the camera raised with the recorder disabled. **Fix:** STBY needs `CanRecord` and an enabled
  recorder. After: STBY False; control (recorder enabled) True.
- [x] [Review][Patch] **A tick sampled after the camera was destroyed replayed an older clip's camera** (the
  tick array is reused) [ReplayBackend.cs `Sample`]. Reproduced in EditMode: clip 2 showed clip 1's FOV of 42.
  **Fix:** a defined "no camera" sample (FieldOfView 0). Test
  `Backend_CameraDestroyedMidClip_NeverShowsAnOlderClipsCamera`, mutation-proven.
- [x] [Review][Patch] **Two log lines for one fault:** a recorder with no camera logged an Error at startup and
  again on the first press (AC6, "one clear log line"). **Fix:** the press adds nothing; the disabled-recorder
  line stays. Verified by reading.
- [x] [Review][Patch] **`FootageFormat` wrapped on huge times** (an unchecked `(long)` of a double above
  ~9.2e18). A huge time counted negative boundaries, so a clip never reached its limit. **Fix:** saturate. The
  backend's `Prepare` also clamps to the config's cap itself. Test
  `HugeClipTimes_Saturate_AndStillReachTheLimit` (2 cases).
- [x] [Review][Patch] **Docs that said what the code does not do:**
  - `Capture()`'s comment still called `OnCapture` its input adapter (Task 5.2; the body is untouched).
  - `ListenerTap` said "never a Sleep" (`SpinWait` can yield up to `Sleep(1)` if the audio thread is
    preempted mid-copy).
  - `IFootage.Dispose` is now marked main-thread-only.
  - `IFootage.Bytes` said "the memory the footage holds"; it is the recorded size (the standards pass).
  - `RecordingConfig` now says memory scales with the device's rate.
  - `game-architecture.md` said Recording reads Events "through ISubject only", and that "nothing is
    allocated on the press frame".

**Deferred** (with reasons, in `deferred-work.md` § "code review of story-1-15"):
- the press-frame allocations after a device change or with both blocks held (measured 2.4–5.9 ms per block);
- unbounded growth with claimers that never dispose, including an OOM escaping into `OnCapture`;
- memory scaling with the device rate;
- a sample-rate change mid-clip (hypothesis);
- config read only at Awake;
- the stale held clip once claimers exist;
- an editor Pause mid-clip (hypothesis).
- the channel count taken from the callback itself (the clamp fixes every case reproducible here);
- a PlayMode zero-allocation guard for the full path (today only the rig's Phase E measures it);
- `IClipBackend` not yet used as a seam (no runtime effect);
- `IFootage.Bytes` is the recorded size, not the memory held (doc corrected; 1.17 sizes the card).

**Dismissed:**
- *The zero-allocation EditMode test covers the camera-only path.* The rig's Phase E measured the full path
  with the live drunk: 0 B over 9,000 ticks (`rig-run3/report.txt:43`).
- *Re-entrant `Begin` from a `Stopped` handler.* Reproduced: it works, with one raise per clip and a
  consistent state. A third block is allocated on that frame, and no shipped listener does this.
- *A listener that disposes without claiming.* A contract violation, but reproduced: nothing leaks and the
  block goes back.
- *The pool accepts a block of another length.* No caller re-prepares with a different length.
- *Hot reload loses `_backend`.* Project-wide: every system's events are lost under Recompile And Continue
  Playing. Hypothesis.
- *A/V anchor phase of up to one DSP block.* Already deferred by dev.
- *Error, not Warn, for a missing channel or config while recording continues.* This matches
  `PhotoModeController`'s existing pattern for `ShotCapturedChannel`.
- *Listed-but-inactive actors are recorded as `Live = false`.* AC5 asks for liveness; there are 2 slots
  against `maxConcurrent` 1.
- *The timecode string allocates once per displayed second.* That is what the story asks for ("only when
  the displayed second changes").
- *Interlocked state machine replaced by a lock.* Not needed: the CAS protocol was re-read by two layers and
  holds.
- *Task 8.1 test gaps (a per-tick case at exactly 30 fps; `beepVolume` at ±∞).* 30 fps is covered by the
  repeat-sum test, and `beepVolume` goes through the same `ClampFinite` that the `maxClipSeconds` cases pin. The
  real gaps (zero-fill in `End`, the tap through the backend) now have tests.
- *Regression rigs (Photo, Gallery and Grade HUD Shoot) not re-run.* The shipped changes they reach are
  equivalent: `SetMode` gained `&& clipRecorder != null` after `_recordReady`, `Capture()` changed only its
  comment, and none of them calls `ToggleRecording` or reads the REC HUD. Record Clip Shoot, which does,
  was re-run in full (`review/rig-run-review/`).

#### Cross-review (loop FIX phase, 2026-11-15)

*An independent read-only reviewer on another model (`loop/runtime/results/1-15-record-a-clip/cross-review-findings-20261115-165011.md`)
read all 21 changed source files and reported one finding. Evidence:
`_bmad-output/verification/1-15-record-a-clip/fix-a1/`.*

- [x] [Cross-review][Patch] **`TapBackend` could leak its objects past the caller's `try/finally`** [RecordingTests.cs
  `TapBackend`, low]. **Confirmed by its code path.** The helper created the camera GameObject, the tap GameObject
  and the backend, then ran three `Assume` checks. An `Assume` that fails throws `InconclusiveException` out of the
  helper before the caller's `try` has started, so its `finally` never runs. The concrete input is a machine with no
  audio device (`outputSampleRate == 0`). It was **not triggered live**: this laptop always reports 48 kHz. The one
  editor-side way to remove the device, toggling *Disable Unity Audio* through `SerializedObject`, crashed the
  editor in `FMOD::System::releaseOutput` (`fix-a1/editor-crash-disableaudio-20261115.log`). Two parts of the
  finding were measured and are smaller than stated:
  - **No memory leaks.** The storage is managed arrays (`ReplayStorage`: `new short[...]`), and
    `ReplayBackend.Destroy` only drops the pool's references to it. An unreachable backend is collected by the GC
    like any other.
  - **The GameObjects do not stay in "the edit-mode scene".** They would stay in the Test Framework's own scene
    until its `RestoreSceneSetupTask` reopens SampleScene at the end of the run (seen in the run's `Editor.log`).
  - **Dismissed sub-claim: "`ListenerTap.Awake` also logs its no-listener warning each time".** In edit mode Unity
    does not call `Awake` on a plain MonoBehaviour added by `AddComponent`. Measured: a `ListenerTap` was added with
    no `AudioListener` beside it. The console showed the positive control (`GameLog.Warn` called directly) and no
    tap warning, and `OnListener` read its default `False`.

  **Fix:** the device check now runs before anything is created. The two later `Assume`s run inside a `catch` that
  destroys the backend and both GameObjects, then rethrows. **After:** EditMode 371/371, with all three `Tap_*`
  tests passed (not inconclusive), the console clean, and no `RecordingTests*` objects left in the editor. The
  `catch` path itself cannot run on this machine (it needs the missing device); that gap is in `deferred-work.md`.

#### Close gate (loop CLOSE phase, 2026-11-15)

*The final checks before merge, on HEAD `9c35300`, run in a freshly started editor. Evidence:
`_bmad-output/verification/1-15-record-a-clip/close-a1/rig-run-close/`. No shipped code, asset or setting changed in
this phase.*

- **Compile and console.** A forced recompile (`refresh_unity`, compile requested) left the console with 0 errors and 0
  warnings, and `Editor.log` holds no `warning CS`. The EditMode run adds 5 lines, all provoked by pre-existing tests
  (`ObjectPoolTests`: the three `[Pool]` lines; `TownMapGuardTests`: two PhysX "triangles greater than 500 units"
  notices). None comes from this story.
- **Tests.** EditMode **371/371** passed, the spike and bench fence tests included. The project has no PlayMode test
  assembly. The fence was also re-listed on disk: the plain build's `Managed/` holds only `CameraGame.dll`, and the
  bench build's holds `CameraGame.Bench.RecordingBench.dll` too.
- **Record Clip Shoot re-run on HEAD**, all phases (0ABCDEF), in SampleScene at 1920×1080. The dev's `rig/` evidence was
  parked first and put back byte-identical (`diff -rq`). The run reproduces the review's run on every measured line:
  - controls: luma sd 43.1, Overlay marker 100.00 % / 0.00 %;
  - A1: the press in Walk arrived and nothing happened;
  - REC on screen 21.1 ms after the press, beep at the speaker ≤ 112.1 ms;
  - timecode `0:00:02` at 2.400 s; 2 stop bursts against 1 start burst;
  - Phase B: one raise per clip on every route, B4 stopped at exactly 3.000 s, ten cycles at 2 storages;
  - Phase E: 0 B over 9,000 full sampling calls, positive control 77,824 B;
  - Phase C: track = live (yaw 331.4 → 338.6°, FOV 60 → 34.8);
  - Phase D: poses and bones 0.000 cm on every tick, transitions included; 10/10 re-renders SSIM 1.0000 (controls
    0.63–0.9996); PCM exactly the tick timeline in d1, d2 and d3; clicks sample-exact; the respawn seen as lifecycles
    4 → 5 → 6; loudness against distance r −0.97 / −0.90 in 2-s windows; the beep's positive control +9.8 dB;
  - Phase F: one line per provoked fault (only the announced ones in `Editor.log`), no exception, and no "Native
    Collection has not been disposed" warning on the play-mode exit mid-clip (F7).
  This run also covers the normal-session checks the review's changes touch: the real `PlayerInput` path, raise,
  record, lower, gallery suppression, and the drunk over pooled cycles.
- **Regression rigs not re-run (Photo, Gallery, Grade HUD Shoot), with the reason.** Shipped code changed since their
  dev re-runs (`git diff 83c74fe..HEAD -- Assets/`) in only three places they can reach. `SetMode` gained
  `&& clipRecorder != null` behind `_recordReady`, which is false in the two recorder-less worlds; the Grade HUD runner
  copies a real recorder, so the guard is true there. `Capture()` changed only its comment, and `ToggleRecording` is
  called by none of them. Every change after the review's own rig run is a comment (`37b104f..HEAD`: `IFootage.cs`
  comments and test code).
- **Human checks re-read.** No queued `my_read` says its AC is likely not met. The AC3 read gained one honest detail
  from `bench/run1/bench-frames.csv`. The **mean** DiD is +0.10 ms/frame, but the **median** frame time is higher while
  recording in all three rounds (+0.13, +1.35 and +0.85 ms, mean +0.78 ms, close to 1.14's +0.8 ms for replay). The
  runs of slow frames (> 33 ms) appear in baseline and recording segments alike (round 2's baseline had 139 of
  them), so they are not periodic recorder hitches. This does not decide AC3: the 60 FPS verdict needs the desktop.
- **Restore.** SampleScene is open and not dirty, with no rig objects. `timeScale` is 1 and the Game View size index
  is 0. `maxClipSeconds` reads 120 (it equals git). No `RecordClipShoot.*` SessionState key remains. No gamepad, input
  background behaviour `ResetAndDisableNonBackgroundDevices`, audio 48 kHz / 1024 / Stereo. `git status` is clean
  apart from `.codex/`, which is not this story's. The ignored `Assets/_Recovery/` predates the story (newest file
  2026-09-27).
- **Status stays `review`.** Four perceptual or hardware checks are open for Alexv: the AC1 look and beeps, the AC3
  desktop 60 FPS, the AC5 sound, and the optional AC1 feel.

## Dev Notes

### What already exists — reuse it, do not rebuild it

| Need | Exists in | Notes |
|---|---|---|
| Typed SO event channel with per-handler isolation and domain-reload cleanup | `Core/EventChannel.cs` (`EventChannel<T>`) | Subclass it for `ClipRecordedChannel`. Listeners subscribe in `OnEnable` and unsubscribe in `OnDisable`. |
| Raise/lower state, the suppression seam, the zoom drive | `PhotoMode/PhotoModeController.cs` | `SetMode` is the single choke point (fact 4). `SetZoom(0..1)` is how rigs zoom: writing `fieldOfView` from outside is undone every frame (`:321-330`). |
| Live subjects, pooled, with a liveness contract | `Events/EventManager.cs` `ActiveActors`, `Events/ISubject.cs` | Read live and filter `isActiveAndEnabled` (`PhotoModeController.cs:537-550`). |
| Per-frame grade path | `Grading/ShotGrader.Grade(cam, subject, config, out detail)` | **Not used by 1.15.** 1.16 samples it. Leave a place in the recorder's per-frame sample where 1.16 can hook in, but build nothing for it. |
| Capture feedback config and the pattern for independent readiness flags | `PhotoMode/CaptureConfig.cs`; `PhotoModeController.Awake` | Copy the "each piece guarded independently" pattern (`:105-111`). |
| Config validation pattern | `Grading/GradingConfig.cs`, `UI/GradeHudConfig.cs` (`Safe*` accessors plus `TryGetConfigProblem`, no `OnValidate`) | And their tests in `Assets/Tests/EditMode/`. |
| Invariant-culture formatting of player-facing numbers | `Grading/GradeText.cs` | 1.19 pinned it. Put the timecode formatter beside it, or in `Recording`, with the same discipline. |
| Overlay capture, the 1080p Game View preset, control shots | `UI/GradeHudShootRunner*.cs`, `Editor/GradeHudShootRig.cs:318-330` | The proven technique for Overlay canvases. Reuse it; do not invent another. |
| Listener tap precedent | `Player/ScaleStudyRunner.cs:1103-1111` (rig), `Spikes/RecordingSpike/AudioTap.cs` (spike) | **Write new game code, do not copy** (ADR-4: "written as new game code, not copied"). Avoid the spike's per-snapshot allocation, missing dspTime and down-mix bug. |
| Tick recorder and ghost precedent | `Spikes/RecordingSpike/ReplaySpike.cs:21-151`, `SpikeHarness.cs:444-518` | The same rule. The known defects are listed in Task 1d/1i and ADR-4 "What Story 1.15 builds". |
| Build-measurement tooling | `tools/verification/recording-spike/` (`run.sh`, `battery.sh`, `analyse.py`, `stats.py`, `audio_check.py`, `ssim.py`, `sheet.py`, `procmem.ps1`) | `run.sh` hard-codes the spike exe and the old vantage, so adapt rather than run as-is. `analyse.py` does not compute the difference-in-differences. |
| Rig video, if motion must be judged | `Core/RigVideoFeed.cs`, `Editor/RigVideoRecorder.cs`, `tools/verification/README.md` | A RenderTexture feed cannot see Overlay UI. `Time.captureFramerate` does not lock `unscaledDeltaTime` (1.12). |

### Architecture compliance

- **ADR-4 (accepted 2026-11-15).** Replay recording, a 30 Hz track plus listener PCM, behind `IClipBackend` /
  `IFootage` / `ReplayFootage`. **No encoding, no ffmpeg, no MP4** in this story: export is Epic 9, gated on the LGPL
  `h264_mf` check and Alexv's licence decision. "The TCP-fed encoder process … belongs to the export story, not 1.15."
- **The Recordability rule** (`game-architecture.md` §Cross-cutting Concerns, added 2026-11-15). Every game
  `AudioSource` keeps `bypassEffects` and `bypassListenerEffects` off, except a sound kept out of the footage on
  purpose, with a comment. Actors expose transform, Animator state and `EventDefinition`.
- **Dependency direction:** `PhotoMode → Recording → Events` (read-only accessors) and `Core`. `UI → Recording` is
  read-only (Task 1b; record it). `Gallery` and `UI` listen through `ClipRecordedChannel`. `Recording` never
  references `PhotoMode`, `UI` or `Gallery`.
- **AR5:** `CameraGame.Recording` namespace, one `CameraGame.asmdef`. Editor tools go in `CameraGame.Editor`, tests in
  `CameraGame.Tests.EditMode`. Rig runners go in the runtime assembly behind `#if UNITY_EDITOR`.
- **AR4 / §Event System:** `<Subject><Verb>` past tense (`ClipRecorded`), with typed synchronous payloads.
- **Error handling (§Cross-cutting):** fail-soft. Missing references are an `Error` once and the component stands
  down; recoverable problems are a `Warn`; nothing throws into `Update`; errors are never shown to the player.
- **Logging:** milestones (record start and stop, with duration, ticks and bytes) at Info; per-frame paths log
  nothing except a guarded `Debug_`.
- **Configuration:** tunables in `RecordingConfig` (SO); format invariants (30 Hz, 16-bit, format version) as
  constants.
- **ADR-5 (world scale):** rig vantages read `PlayerConfig`. Distances are world metres.

### The genuinely new risks

1. **Two threads.** `OnAudioFilterRead` runs on the audio thread. A race there gives a click, a short clip or a
   crash that no edit-mode test sees. Keep the shared state minimal and explicit. Prove it under the ten-clip stress
   and a device reset.
2. **A/V alignment** is the part 1.14 left unsolved (risk 3). It is invisible until 1.18 plays a clip back. Measure
   it now with an independent marker, including a heavy start frame.
3. **Silent interim.** After this story, a player's stop gives no grade and no photo until 1.16/1.17 land. That is
   correct but easy to misreport. Say it plainly (Task 1a).
4. **Breaking the spike's compile** blocks Play mode for every later session (fact 6).
5. **Measuring NFR1 on the wrong hardware.** A laptop result can only give the recorder's cost. Never present it as
   "60 FPS holds".

### Project Structure Notes

- New: `Assets/Scripts/Recording/` (`ClipRecorder`, `IClipBackend`, `IFootage`, `ReplayFootage`, a replay backend,
  `ListenerTap`, `RecordedClip`, `StopReason`, `ClipRecordedChannel`, `RecordingConfig`, plus a rig runner
  behind `#if UNITY_EDITOR`); `Assets/Scripts/UI/RecordingHud.cs`; `Assets/Data/Recording/RecordingConfig.asset`;
  `Assets/Data/Channels/ClipRecordedChannel.asset`; `Assets/Audio/SFX/Rec*BeepPlaceholder.wav`;
  `Assets/Scripts/Editor/RecordClipShootRig.cs`; tests in `Assets/Tests/EditMode/`; the bench asmdef folder
  (Task 11), e.g. `Assets/Bench/RecordingBench/`; `tools/audio/` and a DiD script under `tools/verification/`.
- Modified: `PhotoModeController.cs` (LF), `EventActor.cs` (CRLF: read-only accessors and the lifecycle serial),
  `SampleScene.unity` (CRLF, minimal), `game-architecture.md`, `deferred-work.md`, `sprint-status.yaml`.
- **Line endings:** keep each file's own. The Edit tool does this. A Python edit must read with `newline=''`
  (lessons, 2-1). `git ls-files --eol` tells you the truth; `grep -c $'\r'` lies in Git Bash (lessons, 1-19).
- Every `.meta` of a new asset or folder is committed with it. Stage by name.

### Previous Story Intelligence

- **1.14 (the spike this builds on):** everything in Task 1d and 1i, and ADR-4's "What Story 1.15 builds". It closed
  at 168 EditMode tests and never moved past `review`, because three perceptual checks are open for Alexv: playback
  quality, sound, and NFR1 on a desktop. Those belong to 1.14, not to this story. Its measured numbers:
  - (B) cost +0.8 ms/frame (−4.2…+3.6, n = 3), against a baseline of 33–52 fps in the recording window;
  - 87 B/tick and 2.6 KB/s of track; 192 KB/s of PCM as 16-bit;
  - A/V −60…+23 ms, and 160–290 ms late on heavy frames;
  - PCM −37 ms off its tick duration;
  - cue loudness against distance, r −0.58…−0.73.
- **1.19 (most recent):**
  - pin InvariantCulture for anything a player reads;
  - select 1080p with the Game View's built-in Full HD preset by reflection, not test-helper's `SetResolution`,
    which writes to Alexv's editor preferences;
  - Overlay captures need `CaptureScreenshotAsTexture` after `WaitForEndOfFrame`;
  - `timeScale = 0` right after a grab is the same instant, so let one live frame pass;
  - at close, choose rig re-runs by diffing.
- **1.12:** the flash shines through translucent UI for ~2 frames, so capture settled frames ~0.5 s later.
  `hideOnCameraLowered` (the HUD hides when the camera lowers) is an unplayed feel decision (`deferred-work.md:202`).
  Stopping by lowering now also stops recording, which does not change that.
- **1.11:** the texture-ownership argument (`GalleryService.cs:16-41`) is the template for Task 1c. The gallery
  opens only in Walk, and suppressing the raise is what makes capture inert. Recording inherits that through
  `SetMode`.
- **1.5 and 1.3:** Capture is a Button (one call per press), and RaiseCamera is a Value (both edges). The handler
  must be on the PlayerInput GameObject. Under Send Messages, a held input sends nothing new when suppression
  starts, which is why `SetInputSuppressed` zeroes held input.
- **2.1 and 2.2:**
  - after a play-mode rig run, the MCP bridge drops for 10–60 s, so call `refresh_unity(wait_for_ready)` before
    the next call;
  - "No Unity Editor instances found" during a domain reload means the call was dropped: check
    `Get-Process Unity` and re-send;
  - an `execute_code` over ~30 s times out on the client but keeps running in Unity: wait for its output file;
  - Unity calls `Awake` on disabled components: guard with `if (!enabled) return;`;
  - `[DefaultExecutionOrder]` orders `Awake` across scene objects too.

### Inherited context you should not rediscover

- `Time.timeScale` is 1 in normal play. Rigs set it to 0 to freeze frames, and must restore it.
- In the code, the raised state is `CameraMode.Photo`, not `Viewfinder`. Do not rename it (the spike and three rigs
  use the API).
- `Capture()`'s editor debug overlay holds 1.5 s on unscaled time. At ~6 fps of 1080p editor capture it can vanish
  mid-way through a multi-grab rig (lessons, 1-19).
- The town's NavMesh `CalculatePath` budget, the ring and the FallRecovery are not this story's. The drunk's route
  is short (11.3 m, peak near x 26, z 19), and its lifecycle is ~20–30 s plus a 2 s respawn delay.
- `EventManager.ActiveActors` is not cleared when the manager is disabled. Grading filters `isActiveAndEnabled`, and so
  must the recorder.

### Git Intelligence

- Baseline `1f83710` (the loop's 1.19 lessons). The last 8 commits are all Story 1.19: `GradeHud`, `GradeHudConfig`,
  `GradeText`, `GradeHudShootRunner*`.
- `PhotoModeController.cs` was last changed by Story 1.11's review (`b725270`), and `SampleScene.unity` by Story
  2.2's close (`a0691e0`).
- This create phase changed `game-architecture.md` (ADR-4 accepted, the Recordability rule) and `sprint-status.yaml`.

### Latest technical information (checked 2026-11-15)

- Unity 6000.3.8f1, URP 17.3.0, Input System 1.18.0 (`unity_reflect` was live through MCP for Unity 10.3.0).
- `UnityEngine.AnimatorTransitionInfo` has `duration`, **`durationUnit`**, `normalizedTime`, `fullPathHash`,
  `nameHash`, `userNameHash` and `anyState` (reflected).
- `UnityEngine.AudioSettings` has `dspTime`, `outputSampleRate`, `speakerMode`, `GetDSPBufferSize`,
  `GetConfiguration`, `Reset` and the event `OnAudioConfigurationChanged` (reflected). `SetDSPBufferSize` is obsolete:
  do not use it.
- `unity_docs` returned empty bodies for `AudioSettings.dspTime`, `AudioSource.bypassListenerEffects` and
  `MonoBehaviour.OnAudioFilterRead` on 2026-11-15. Treat their thread-safety and mixer interaction as **unverified,
  and measure them** (Task 1d, 1f).
- No custom Input System settings asset exists, so the defaults apply: pointer and keyboard input respects Game View
  focus in play mode, and devices reset on focus loss. A focus loss mid-recording releases RaiseCamera (it is a
  Value action, so the reset delivers the release). That lowers the camera and stops the recording, which is
  correct. It is worth one look in the build.
- Toolbox packages relevant here: `Unity.PerformanceTesting` 3.7.0, Profile Analyzer 1.4.2, Graphy 4.0.0, test-helper
  1.6.4.
  - test-helper's `[GameViewResolution]` changes editor preferences. Prefer the reflection preset.
  - **Not** relevant, and not to be used: InstantReplay (an MP4 recorder; ADR-4 chose replay, and its use is
    confined to ADR-4's scope), Cinemachine (handheld shake is not in this story; a `CinemachineBrain` on Main Camera
    would replace the player camera), KinoGlitch, UnityHFSM (a two-state recorder needs no state machine).

### Verification plan: what would convince a sceptic

| Claim | Evidence | How judged |
|---|---|---|
| Record starts in < 0.2 s with REC, timecode and beep (AC1) | Phase A: ms from the virtual-gamepad press to the REC frame and to the beep's first sample, with the parts listed; REC frames | Read the numbers; look at the frames; listen to the WAVs. The style is a **human check**. |
| A second press stops it, with a different beep (AC1) | Phase A/B logs, both beep WAVs and their spectrograms | Two distinct sounds; one raise per clip |
| Every lowering stops and keeps (AC2) | Phase B table: route, stop reason, duration against wall clock, raise count, footage alive | Every route, clips 2–10 included |
| No-op in Walk, same action (AC4) | Phase A: RT in Walk with a proven-arrived press, giving 0 clips, 0 beeps and no REC; the input asset diff is empty | A control proves the press arrived |
| Aim and zoom work while recording (AC3) | Phase C: per-frame rotation and FOV against the ticks; before/after frames | The track follows the camera |
| The recorder costs little in a build (AC3) | Task 11: DiD per round, absolute fps, dropped/repeated frames, memory | The cost is a number. The absolute 60 FPS is a **hardware check** for a desktop. |
| The footage is faithful (AC5) | Phase D: pose error at render time; ghost against live contact sheet with SSIM; WAV; RMS against distance; marker offset, normal and heavy-frame | Look at the sheet; listen to the WAV (**human check** for "clean and in sync") |
| Bounded and released (AC6) | Phase E memory series over the ten clips; GC per frame; bytes before and after dispose | No ramp; 0 B/frame while recording |
| Fail-soft (AC6) | Phase F: one log line per provoked fault, the game keeps running; a clean normal run | Console read, Editor.log grep |
| No regression (AC7) | EditMode count; fence tests; re-run rig outputs diffed against their previous runs; restore read-back | Same results, or differences explained |

### Human checks to queue (never self-certify)

Queue these at the **end of dev**, after every automated part, each with exact files and your own measured read:

1. **AC1 — the camcorder look and sound.** Do the REC cluster, the timecode and the two beeps read as a 2000s
   camcorder, and is the beep level right against the game? (REC frames, beep WAVs.)
2. **AC3 — 60 FPS at 1080p on a mid-range desktop.** Run the bench build there (the exact command line). This laptop
   can show only the recorder's cost.
3. **AC5 — the recorded sound.** Is it the game's sound, clean, and does it feel in sync with the re-rendered frames?
   (WAV plus contact sheet, or a short muxed rig clip for evidence. ffmpeg's GPL build is fine for evidence and never
   ships; lessons, 1-14.)
4. *Optional:* does the record toggle feel instant in the hand? The number is measured; the feel is his.

The ADR-4 approval is **done** (it was 1.14's gate). Do not queue it again.

### Project Context Rules

- MCP for Unity is the only editor channel. Never use `unity:unity-cli` or Pipeline commands to edit, run or test.
- Every script change: `refresh_unity`, then `read_console` (NFR5). Before a rig run, read the values back through the
  editor.
- Evidence goes to `_bmad-output/verification/1-15-record-a-clip/`, never `Temp/`. Builds go to
  `Builds/1-15-record-a-clip/`.
- Restore everything a rig or build changes, and read it back.
- Commit on `loop/1-15-record-a-clip` only, staging by name, with `Co-Authored-By: Claude Opus 5.5
  <noreply@anthropic.com>`. Never switch branches, merge, rebase, reset, stash, clean or push.
- Perceptual ACs become `human_checks`. A measured shortfall is fixed, never queued.
- Decisions are made by evidence and recorded (question, options, choice, evidence, cost to reverse).
- Do not edit `loop/runtime/`.

### References

- `_bmad-output/planning-artifacts/epics.md`: Story 1.15 (the ACs); FR2, FR3, FR27; NFR1–NFR3, NFR5, NFR8; AR4, AR6,
  AR12.
- `_bmad-output/planning-artifacts/game-architecture.md`:
  - §Clip Recording (added 2026-11-10);
  - ADR-4 (accepted 2026-11-15): *Footage format*, *Frame source and UI*, *Memory card*, *Failure story*,
    *Consequences*, *Risks carried forward*, *What Story 1.15 builds*;
  - §Cross-cutting Concerns → *Recordability*;
  - §Architectural Boundaries; §Directory Structure; ADR-5.
- `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md`: lines 179–183 (recording action; lowering
  stops recording); 231 (record-start-to-feedback < 0.2 s); 242–243 (bindings; zoom works while recording); 342 (REC
  dot, running timecode, 2000s camcorder); 348 (record start/stop beeps).
- `_bmad-output/implementation-artifacts/1-14-decide-how-the-game-records-video.md`: Dev Agent Record, review.
- `_bmad-output/implementation-artifacts/deferred-work.md:34, :144, :202, :298-308`.
- `loop/lessons.md`: sections 1-14, 1-19, 2-1, 2-2.
- `loop/runtime/approvals.txt:1` (ADR-4 approved).
- Code: `PhotoModeController.cs`, `EventActor.cs`, `EventManager.cs`, `EventChannel.cs`, `GalleryService.cs`,
  `GradeHud.cs`, `ThirdPersonCamera.cs`, `Spikes/RecordingSpike/*`, `Tests/EditMode/RecordingSpikeFenceTests.cs`.

## Dev Agent Record

### Agent Model Used

Claude Opus 5.5 (`claude-opus-5-5`), loop DEV phase, attempt 1, 2026-11-15.

### Debug Log References

- Rig: `Tools/Recording/Record Clip Shoot (Play)` (`Assets/Scripts/Editor/RecordClipShootRig.cs` →
  `Assets/Scripts/Recording/RecordClipShootRunner.cs`). Evidence: `_bmad-output/verification/1-15-record-a-clip/`
  - `rig-run3/` — the evidence run on the final recorder code, all phases (`report.txt`, captures, WAVs, tracks).
  - `rig/` — Phase D re-run after the rig's ghost-skinning fix (pictures and bones), `analysis.txt`, sheets.
  - `rig-run1/`, `rig-run2/` — earlier runs, kept because they show the rig defects found and fixed (below).
  - `survey/` — vantage survey with route markers; `bench/` — AC3 builds and the release-build bench.
  - `regression-before/` — Photo/Gallery/Grade HUD Shoot evidence copied aside before their re-runs.
- `Editor.log` holds every `[RecordClipShoot]` line; provoked faults sit between `PHASE F BEGIN` and `PHASE F END`.

### Decisions (Task 1, all settled by measuring where a measurement could settle them)

1a. **Capture input re-points to Record; `Capture()` unchanged and unbound.** `OnCapture` → `ToggleRecording()`.
    Interim consequence, stated plainly: until 1.16/1.17 a player's clip shows **no grade** and stores **no photo**.
1b. **`ClipRecorder` on `main characters`, driven by `PhotoModeController`.** `SetMode` stops the clip on every
    way out of Photo, **after** `Mode` changes (a stop listener sees the camera already down, matching
    `CameraLowered`). The REC indicator reads the recorder (new read-only `UI → Recording`, recorded in
    §Architectural Boundaries). The **beeps are played by `RecordingHud`** from the recorder's `Started` /
    `Stopped(StopReason)` C# events, so every stop beeps (length limit and device change included) through one
    synchronous path, and `PhotoModeController` stays free of recording feedback.
1c. **`RecordedClip` with an explicit `Claim(owner)`.** Listeners that keep footage claim during the raise; the
    recorder claims the rest and holds one `LastClip`, disposing the one before. Measured: B6 ten cycles, every
    previous clip disposed, storages created stays 2; B7 a claimed clip is not held by the recorder, and its
    disposal hands the block back.
1d. **Clock:** `AudioSettings.dspTime` at the press is the PCM's time zero; every PCM block is placed by the DSP
    time read **inside `OnAudioFilterRead`** (verified: scheduled clicks land at exactly their DSP time, 5 of 5,
    including after a 200-ms busy press frame). Ticks use `Time.realtimeSinceStartupAsDouble` anchored at the
    same instant, because main-thread `dspTime` steps in 21.3-ms blocks (measured sawtooth of (dsp − realtime):
    −38 … +35 ms; drift ≤ 6 ms over 30 s). Pause: the clip follows real time; a pause menu must suppress the
    raise (written in `ClipRecorder`'s header).
1e. **`RecordingConfig.maxClipSeconds` 120 s** (min 1, cap 300), reaching it stops with `LengthLimit` and keeps the
    clip (B4: exactly 3.000 s with the limit at 3). **Preallocated at `Awake`: two contiguous storage blocks**
    (23.1 MB each at 120 s, 48 kHz stereo) rather than a chunk pool: no allocation on the press frame or per frame,
    memory flat over the ten cycles (run 3: allocated 1272.5–1272.8 MB, reserved 1786.3 MB throughout; run 2: 1273.6–1274.1 MB). Tick rate, 16-bit
    and `FootageFormat.Version = 1` are constants.
1f. **Beeps kept out of the footage** (`bypassListenerEffects = true`, not routed to a mixer). Measured: 2.7 kHz
    power at the clip's start −62 … −77 dB, while the **same beep through a non-bypassed source** reads +9.8 dB in
    the clip (positive control). The beep is heard at its own source (peak 0.159).
1g. **Beeps:** generated placeholders (`tools/audio/make_camcorder_beeps.py`): start = one 75-ms 2.7-kHz tone,
    stop = two (burst counter: 1 and 2). **Look:** red `● REC` and a white `H:MM:SS` timecode, top-right,
    **steady dot** (no blink), **`STBY`** while raised and not recording, 44/36 px at the 1920×1080 reference
    with a 1.5-px dark outline. **Deviation from Task 6.2's "on the PhotoViewfinder canvas":** it has its own
    Overlay canvas (`RecordingHudCanvas`, sorting 50, CanvasScaler 1920×1080 match 0.5), because PhotoViewfinder's
    scaler is Constant Pixel Size and its group fades in over 0.15 s. UI built in code at `Awake` (one scene
    object; layout reviewable in one file).
1h. **AC3 measured in a fenced Windows release build** (`Assets/Bench/RecordingBench`, define
    `CAMERA_RECORDING_BENCH` through `extraScriptingDefines` only), same 24-s segment of the drunk's lifecycle
    recording and not, 3 rounds, order rotated, difference-in-differences. `Unity.PerformanceTesting` was **not**
    adopted: a CSV from the build answers the question, and no PlayMode test assembly was needed.
1i. **Track per actor:** `EventDefinition`, a session-wide lifecycle serial (`EventActor.LifecycleSerial`, taken in
    `Begin`), liveness, transform, and per Animator layer state/time/transition with its `DurationUnit`. Read live
    every tick; Animator cached by `EventActor`; actors and layers per tick from config (2 and 2); a Recordability
    warning once if an Animator has parameters or extra weighted layers.
5.1 A recorder that cannot record **refuses silently** (no error beep, no REC) and logs once per session.
5.2 A missing `clipRecorder` on `PhotoModeController` is an **Error** (the core verb is gone). The isolated
    Photo/Gallery Shoot worlds announce that one expected line; the Grade HUD runner's placeholder copies the recorder.
5.3 Disabled or destroyed mid-clip → the clip is **kept and announced** (`Disabled`), never disposed silently
    (F6 a throwaway recorder; F7 leaving play mode mid-clip, through `PhotoModeController.OnDisable`; no exception).

### Completion Notes List

**What was built.** `Assets/Scripts/Recording/`: `ClipRecorder`, `IClipBackend`, `IFootage`, `ReplayBackend`,
`ReplayFootage` (read API: ticks, actors, layers, PCM), `ListenerTap` (audio thread: one interlocked state,
no allocation, no lock across a copy), `RecordedClip`, `StopReason`, `ClipRecordedChannel`, `RecordingConfig`,
`FootageFormat` (tick/PCM arithmetic, 16-bit clamp, `ClipTimecode`). `UI/RecordingHud`. `EventActor` gained
read-only `Definition`, `Animator`, `LifecycleSerial`. `PhotoModeController`: `ToggleRecording`, stop in `SetMode`
and `OnDisable`. SampleScene wired (additions only). Assets: `RecordingConfig.asset`, `ClipRecordedChannel.asset`,
two beep WAVs.

**How it was verified** (Record Clip Shoot, run 3 + Phase D re-run, real SampleScene, 1920×1080, virtual gamepad
through PlayerInput):
- *Controls:* 3D frame luma sd 43 (not blank); Overlay marker 100.00 % on / 0.00 % off.
- *AC1/AC4 (real input):* RT in Walk — the Capture action arrived (+1) and nothing happened (0 starts, 0 clips, no
  beep, 0 REC pixels). Press → handler 5.6 ms, handler → first frame with REC 12.2 ms, handler → beep's first
  sample in the mix 2.0 ms (+ ≤ 85 ms of output buffers): **press → REC on screen ≈ 18 ms, press → beep at the
  speaker ≤ ~93 ms** (budget 200 ms; reported, not asserted). Timecode `0:00:02` at 2.433 s. Stop beep 2 bursts,
  start 1.
- *AC2:* every route ends with one raise and the clip kept: press/press (`PlayerPressed`), LT release and gallery
  suppression (`CameraLowered`), the limit (`LengthLimit`, 3.000 s), rapid ×3, ten cycles, a claimed clip.
- *AC3 (aim/zoom):* right stick and dpad during a clip — live yaw 331.4 → 339.2°, FOV 60 → 34.8, the track the
  same; every tick's camera equals the pose rendered on its frame (0.000 cm, 0.0000°, 0.0000 FOV) against a
  previous-tick control of 0.30° / 3.2 FOV / 8 cm actor.
- *AC5 (fidelity):* camera and actor poses 0.000 cm on every tick (806 + 737 + 236 in run 2, the same in run 3);
  **ghost bones from the track vs live bones 0.000 cm on every tick, transitions included** (control 8.5–12 cm);
  **10/10 live vs re-rendered frames pixel-identical** (SSIM 1.0000, controls 0.61–0.99) after the rig fix below;
  PCM exactly the tick duration (1,448,000 frames for 905 slots, etc.); clicks sample-exact; picture within one
  tick of the click (slot − click 0 or ±33.3 ms, heavy start included); loudness vs the drunk's distance
  r −0.74 … −0.91 in 2-s windows (1.14: −0.58 … −0.73); respawn clips show two lifecycles (e.g. 4 → 5) with 32–58
  actor-less ticks between and the new lifecycle starting at the spawn point (31.80, 19.00), no stale pose.
- *AC6:* zero allocation — heap growth over 9,000 calls of the **full sampling path with the live drunk** (9,000
  ticks, actor and layer samples) **0 B**, the recorder's `LateUpdate` 0 B, positive control (`new object()`)
  73,728 B, no GC; footage `Bytes` 0 after dispose; storages 2 throughout. Fail-soft: no channel / no config / no
  tap / no EventManager / `maxClipSeconds` 0, NaN, 1e9 / audio reset mid-clip / disabled mid-clip / play-mode exit
  mid-clip — one line per fault, no exception, the game kept running; a clip recorded after the audio reset has
  sound (click at 0.086 s).
- *EditMode:* 297 → 364 (61 recording tests, 5 bench fence tests); mutation-proven on the repeat rule (6 red).
  The culture mutation of the timecode survived: Mono prints whole-number `H:MM:SS` identically in all 7 cultures
  tried, so the invariant pin there is a safeguard, not a fix.

**Rig defects found by controls and fixed (not product defects):** an edit-mode line-of-sight check passed through
pine canopies (no colliders) → vantage surveyed visually; `GC.GetAllocatedBytesForCurrentThread` reads 0 around
`new byte[1000]` in this Mono (the first zero-allocation test and probe were blind) → heap growth with a positive
control; the shifted pose control compared a frame with itself (Main Camera renders more than once a frame in the
editor) → compare with the previous tick; a procedural click made before `AudioSettings.Reset` plays silent
afterwards → rebuilt after the reset; the ghost replayed fixed-time crossfades with the next state at time 0
(6.4 cm on transition ticks) and rendered stale skinning → normalized crossfade with the next state's time,
`forceMatrixRecalculationPerRender`.

**Left for Alexv (human checks):** the camcorder look and the beeps (AC1); 60 FPS at 1080p on a mid-range desktop
(AC3, a hardware limit here); whether the recorded sound is clean and feels in sync (AC5); optionally whether the
toggle feels instant.

**AC3 in a Windows release build (Task 11)** — `bench/run1/` (`did.txt`, `bench-frames.csv`, `bench-summary.txt`,
`procmem.csv`, `player.log`). Laptop: i7-1165G7, GeForce MX350, 1920×1080 windowed, vSync off. Same 24-s segment
of the drunk's lifecycle from each spawn, recording through the real `ToggleRecording()` and not, 3 rounds,
order rotated:
- round 0 +0.30, round 1 +1.87, round 2 −1.88 ms/frame → **recorder cost +0.10 ms/frame (−1.88 … +1.87, n = 3)**,
  not distinguishable from 0 against this laptop's noise (round 2's baseline segment alone had 80 hitches).
- Absolute: 38.0–45.6 fps per segment, 42.2 fps over 6,082 frames. **NFR1's 60 FPS cannot be judged here**; it is
  queued for a mid-range desktop. Every rec-phase frame was really recording (0 frames not); 0 gen-0 GCs in the three
  recording segments; three 24.000-s clips recorded with sound in the build; no error in `player.log`.
- Fence: the bench build's `Managed/` holds `CameraGame.Bench.RecordingBench.dll`, the **plain build's holds only
  `CameraGame.dll`**; `PlayerWithoutTestAssemblies` excludes the bench (`bench/build-result.txt`); the 4 fence tests
  pass. The builds re-serialised `PC_RPAsset`, `UniversalRenderPipelineGlobalSettings`, `GraphicsSettings`,
  `ProjectSettings` and `UnityConnectSettings`: restored from HEAD (byte-identical, CRLF) and reimported.

**Regression (Task 12)** — shipped code changed under the rigs: `PhotoModeController` (`SetMode`, `OnCapture`,
`Awake`, `OnDisable`) and `EventActor.Begin`. Re-run on HEAD, previous evidence copied to `regression-before/`:
- Photo Shoot: 15 of 16 verdicts identical; `i_from_behind` 34 % → 44 %, both 2★ (lesson 42: that shot swings on
  real-time schedule drift alone). Its one expected `[PhotoMode] ClipRecorder unassigned` error is announced first.
- Gallery Shoot: every stored grade, miss reason, the cap (5 of 5, +5 textures vs baseline) and identity across 3
  cycles identical; only timestamps and instance ids differ.
- Grade HUD Shoot (1920×1080): Phase B 12 thumbnails, 0 contaminated; pooled cycles agree; its provoked lines only.
- Normal SampleScene session: raise, record 4.6 s, lower → clip kept (`CameraLowered`); gallery opened in Walk
  suppresses the raise and Record does nothing while it is up; console: only the standing "Head bone not found!".
- EditMode 364/364. Editor restored: SampleScene clean with no rig objects, `maxClipSeconds` 120, `timeScale` 1,
  Game View index 0, no rig SessionState keys, no virtual pad, input settings and audio configuration as before.

**Human checks queued (never self-certified):**
1. **AC1 — camcorder look and sound.** `rig-run3/a2_raised_standby.png` (STBY), `a3_first_rec_frame.png`,
   `a4_recording_0002.png`, `a5_stopped.png`, `rig-run2/c2_after_look_zoom.png`; beeps
   `Assets/Audio/SFX/Rec*BeepPlaceholder.wav` and as heard at the source `rig-run3/a3_start_beep_at_source.wav`,
   `a5_stop_beep_at_source.wav`. Do they read as a 2000s camcorder, and is the beep level right against the game?
2. **AC3 — 60 FPS at 1080p on a mid-range desktop.** Build with `Tools/Recording/Build Recording Bench (Windows)`, then
   `powershell -ExecutionPolicy Bypass -File tools/verification/run_recording_bench.ps1 Builds/1-15-record-a-clip/bench/RecordingBench.exe <out> 3`
   and `python -I tools/verification/recording_bench_did.py <out> <out>/procmem.csv`.
3. **AC5 — the recorded sound.** `rig/d1_clip.wav` (30 s through the drunk's peak and a respawn; three marker clicks at
   4, 12 and 21 s), `rig/d2_clip.wav`, with `rig/d1_live_vs_ghost_sheet.png`. Is it the game's sound, clean, and does
   it feel in sync?
4. *Optional:* does the Record toggle feel instant in the hand (measured ≈ 18 ms to REC on screen)?

### File List

New:
- `Assets/Scripts/Recording/ClipRecorder.cs`, `IClipBackend.cs`, `IFootage.cs`, `ReplayBackend.cs`, `ReplayFootage.cs`,
  `ListenerTap.cs`, `RecordedClip.cs`, `StopReason.cs`, `ClipRecordedChannel.cs`, `RecordingConfig.cs`, `FootageFormat.cs`
  (and `Assets/Scripts/Recording.meta` and every `.meta`)
- `Assets/Scripts/Recording/RecordClipShootRunner.cs`, `RecordClipShootBeepProbe.cs`, `RecordClipShootBoneLog.cs`
  (rig, `#if UNITY_EDITOR`)
- `Assets/Scripts/UI/RecordingHud.cs`
- `Assets/Scripts/Editor/RecordClipShootRig.cs`, `Assets/Scripts/Editor/RecordingBenchBuild.cs`
- `Assets/Bench/RecordingBench/CameraGame.Bench.RecordingBench.asmdef`, `RecordingBench.cs` (+ `Assets/Bench.meta`,
  `Assets/Bench/RecordingBench.meta`)
- `Assets/Data/Recording/RecordingConfig.asset` (+ `Assets/Data/Recording.meta`), `Assets/Data/Channels/ClipRecordedChannel.asset`
- `Assets/Audio/SFX/RecStartBeepPlaceholder.wav`, `Assets/Audio/SFX/RecStopBeepPlaceholder.wav`
- `Assets/Tests/EditMode/RecordingTests.cs`, `Assets/Tests/EditMode/RecordingBenchFenceTests.cs`
- `tools/audio/make_camcorder_beeps.py`, `tools/verification/record_clip_report.py`,
  `tools/verification/recording_bench_did.py`, `tools/verification/run_recording_bench.ps1`

Modified:
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` (Record toggle, stop in `SetMode` and `OnDisable`, docs; `Capture()` unchanged)
- `Assets/Scripts/Events/EventActor.cs` (read-only `Definition`, `Animator`, `LifecycleSerial`)
- `Assets/Scenes/SampleScene.unity` (additions only: `ClipRecorder` on `main characters`, `ListenerTap` on Main Camera,
  `RecordingHudCanvas`, `PhotoModeController.clipRecorder`)
- `Assets/Scripts/Editor/PhotoShootRig.cs`, `Assets/Scripts/Editor/GalleryShootRig.cs`, `Assets/Scripts/UI/GradeHudShootRunner.cs`
  (rigs: the expected ClipRecorder error announced, or the recorder copied)
- `_bmad-output/planning-artifacts/game-architecture.md`, `_bmad-output/implementation-artifacts/deferred-work.md`,
  `_bmad-output/implementation-artifacts/sprint-status.yaml`, this story file

## Change Log

- 2026-11-15: Story context created by the loop CREATE phase (ready-for-dev). ADR-4 changed to Accepted in
  `game-architecture.md`, citing `loop/runtime/approvals.txt` line 1. The Recordability rule was added under
  Cross-cutting Concerns, as ADR-4 asked once it was approved.
- 2026-11-15: Loop DEV phase (attempt 1). Implemented the recorder (ADR-4 replay footage behind `IClipBackend`), the
  Record toggle, the REC indicator and beeps, the Record Clip Shoot rig, 67 EditMode tests and the fenced AC3 bench;
  verified per the Dev Agent Record. Status -> review; the perceptual and desktop checks are queued for Alexv.
- 2026-11-15: Loop REVIEW phase (attempt 1). Three review layers plus a standards and zero-GC pass. 7 defects
  reproduced in SampleScene play mode and fixed, with 7 new mutation-proven EditMode tests (364 -> 371). Record Clip
  Shoot re-run in full on the fix. 11 items deferred, 12 dismissed. Status stays review: AC1, AC3 and AC5 have
  perceptual and hardware checks queued for Alexv.
- 2026-11-15: Loop FIX phase after the cross-review (attempt 1). Its one finding was confirmed by its code path and
  fixed: the tap tests' helper now cleans up when an `Assume` fails. Its sub-claim about `ListenerTap.Awake` was
  disproven by measurement. EditMode 371/371, console clean. No shipped code changed, so no rig needed a re-run.
  Status stays review.
- 2026-11-15: Loop CLOSE phase (attempt 1). Forced recompile with a clean console, EditMode 371/371, fence re-listed
  on disk, Record Clip Shoot re-run on HEAD in a fresh editor (`close-a1/rig-run-close/`; same results as the review's
  run), the editor restored and read back, git clean. The other regression rigs were not re-run, for the reason in
  *Close gate*. The AC3 read gained the median frame-time delta. No shipped change. Status stays review: four checks
  remain open for Alexv.
