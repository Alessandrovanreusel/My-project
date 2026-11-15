---
baseline_commit: 06342e3f85f5675754309d41c75220424f726c2d
---

# Story 1.17: The memory card

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-10 (`sprint-change-proposal-2026-11-10.md`). It depends on Story 1.15 (`loop/dependencies.yaml`:
`1-17-the-memory-card: [1-15-record-a-clip]`) and builds on Story 1.16's `ClipGrade`. The loop merged both with only
Alexv's perceptual checks pending. The context was created on 2026-11-15 by the loop CREATE phase.*

*Today a clip is recorded and graded, and then it is **stored nowhere** except the recorder's single `LastClip`, which
the next clip replaces (`PhotoModeController.cs:27-31`, `deferred-work.md:360`). This story gives the camcorder its
memory card. Every clip lands on it with its grade, subject, time, duration and a thumbnail. The card holds about two
minutes of footage, the viewfinder counts down what is left and says CARD FULL when it runs out, and the player can
open the card, see every clip and delete one to free its seconds.*

*What this story is **not**:*
- *no playback (Story 1.18);*
- *no disk I/O: clips survive between sessions in Epic 9's first story (AR6), and this story only keeps the seam ready;*
- *no export (Epic 9);*
- *no sorting by subject or rarity (GDD :295 is a later layer);*
- *no reputation (Epic 4);*
- *no change to how a clip is recorded or graded, beyond the card's limit and the thumbnail.*

*If something in recording or grading looks wrong while you work, write it in `deferred-work.md` instead of fixing
it here.*

> **The starting point in fourteen facts.** Each was read from a file on 2026-11-15. Re-check any that matter before
> relying on it.
>
> 1. **The recorder already announces every finished clip, once, with its grade.** `ClipRecorder.Stop`
>    (`Recording/ClipRecorder.cs:286-328`) runs, in order:
>    - `End()`, then grades the start tick if no tick was graded, then `Finish()` the grade;
>    - builds `RecordedClip(Id, Footage, StartedAtUtc, DurationSeconds, StopReason, Grade)`;
>    - logs the clip and its grade;
>    - raises `Stopped` (the stop beep, REC off), then `clipRecordedChannel.Raise(clip)`;
>    - claims whatever nobody claimed as `LastClip` and disposes the clip it held before.
>
>    A listener that keeps the footage must call `RecordedClip.Claim(owner)` **during** the raise
>    (`RecordedClip.cs:11-26`). `RecordedClip` has **no thumbnail** and no top-level subject: the subject is
>    `Grade.SubjectId`, which is empty when there was nobody.
> 2. **Every clip sits in a whole preallocated block, however short it is.** `ReplayBackend` preallocates
>    `PooledStorages = 2` blocks at the recorder's `Awake`, each sized for `RecordingConfig.maxClipSeconds` (120 s):
>    - ticks 3,602 × 72 B, actors 7,204 × 64 B, layers 14,408 × 32 B;
>    - PCM (5,760,000 + 8,192) frames × 2 channels × 2 B;
>    - ≈ **24.25 MB (23.1 MiB) per block** at 48 kHz stereo (`ReplayBackend.cs:89-122`, `ReplayFootage.cs:194-224`).
>
>    `ReplayFootage.Dispose()` hands the block back to the pool (`:175-182`). A one-second clip reports
>    `Bytes` ≈ 0.2 MB while it holds 23 MB (`IFootage.cs:15-19`, `deferred-work.md:383`). If a listener claims and
>    keeps clips, every later press allocates a **new** 23 MB block on the press frame (`ReplayBackend.cs:144-153`;
>    `deferred-work.md:364, :373, :374`). **A card that simply claims and holds clips would hold N × 23 MB and
>    allocate on every press.** NFR3 forbids exactly that ("Recorded footage is bounded by the memory card's
>    capacity — no unbounded buffer; deleting a clip frees its memory").
> 3. **ADR-4 already says how the card must hold footage** (`game-architecture.md:513-518`): "Capacity in seconds =
>    Σ tick repeats ÷ 30. Deleting a clip disposes its track and PCM buffers, and `MemoryCardService` is their single
>    owner. Keep the PCM in a pooled native or preallocated buffer, **not a fresh managed array per take** …
>    Story 1.17's disk seam: one binary track file plus one 16-bit WAV per clip (~195 KB/s). Each clip records its own
>    sample rate and channel count." The format also "carries a content version" (`:576-578`).
> 4. **The architecture names the pieces.** `MemoryCardService` "evolves `GalleryService`, stays in
>    `Scripts/Gallery/`: stores `CapturedClip`s, enforces capacity in seconds of footage, and frees footage on delete.
>    It remains the single owner of any native memory" (`:226-228`). Its record is
>    `CapturedClip { string Id; IFootage Footage; Texture2D Thumbnail; ClipGrade Grade; string SubjectId; DateTime
>    CapturedAtUtc; float DurationSeconds }`, with the same disk-ready rules as `CapturedShot` (`:229-232`). The tree
>    plans `MemoryCardConfig.asset` under `Data/Recording/` (`:862`), but `Data/Gallery/` already exists. Task 1j
>    settles where it goes.
> 5. **`Recording` must never reference `Gallery`** (`game-architecture.md:940-942`). Yet the recorder must stop when
>    the card fills (`:218-219, :238-240`), and the viewfinder (`UI`) must show the time left. `UI → Recording` is
>    sanctioned as read-only (`:943-947`); `UI → Gallery` is not. `StopReason` (`Recording/StopReason.cs`) has five
>    values, `PlayerPressed`, `CameraLowered`, `LengthLimit`, `AudioDeviceChanged` and `Disabled`, and **no
>    `CardFull`**. `LengthLimit`'s doc says "Story 1.17 replaces this bound with the memory card's remaining seconds;
>    the stop path stays the same" (`:17-19`).
> 6. **The card's limit can reach the backend with no backend change.** `ClipRecorder.Begin` passes
>    `RecordingConfig.MaxClipSecondsOf(config)` to `ReplayBackend.Begin(dsp, maxSeconds)`, which takes
>    `Mathf.Min(maxSeconds, _preparedSeconds)` (`ReplayBackend.cs:155`). When the clip is full, `TryGetStopRequest`
>    reports `LengthLimit` and `LateUpdate` stops it after grading that tick (`ClipRecorder.cs:342-352`).
>    ⚠️ **`FootageFormat.MaxBoundariesFor(0)` returns 1** (`FootageFormat.cs:64-71`), so a full card that still calls
>    `Begin` records a one-tick clip. The full-at-press case has to be refused **before** `Begin` reaches the backend.
>    ⚠️ **The limit is a `float`.** Passing the card's free slots through float seconds loses a slot for 4,162 of the
>    values 1–9,000 (Task 1b). Pass slots.
> 7. **The photo gallery has no delete, no seconds and no selection.** `GalleryService` (`Gallery/GalleryService.cs`)
>    stores `CapturedShot`s from `ShotCapturedChannel`, capped by a **count** (`GalleryConfig.maxStoredShots` 50),
>    evicts oldest-first and destroys each `Texture2D`, and frees everything in `OnDestroy`. `GalleryView`
>    (`Gallery/GalleryView.cs`):
>    - is a Screen Space – Camera canvas with an opaque backdrop, built once in `Awake`;
>    - opens only in Walk (`:395-417`), then calls `SetRaiseSuppressed(true)` and freezes the player;
>    - lays cells out to fit the screen (`LayoutGrid`, `:651-732`);
>    - has **no selection, no scrolling and no delete**. Every cell's `RawImage` has `raycastTarget = false`.
>
>    Its miss caption is still salmon and prints `P0` in the player's culture (`:103, :583, :762-763`).
>    `deferred-work.md:345` assigns both fixes **to this story**.
> 8. **Only regression rigs take stills now.** `Capture()` and `ShotCapturedChannel` are unbound since 1.15 and were
>    kept by 1.16's decision 1h. Three things use them: the Photo, Gallery and Grade HUD Shoot rigs, the still
>    readout in `GradeHud`, and `GalleryService`'s storage "until Story 1.17's memory card replaces the gallery's
>    storage" (`game-architecture.md:233-236`).
> 9. **Thumbnails: ADR-4 says to copy the frame the game already rendered, never to `ReadPixels`.** "For any live
>    frame the game needs (thumbnails, reference frames) … A URP pass enqueued from script in `beginCameraRendering`
>    (no renderer-asset change) at `AfterRenderingPostProcessing` copies the camera colour into an sRGB RT, which is
>    then read with `AsyncGPUReadback`. Never use `ReadPixels`." The spike measured it SSIM 1.0000 against the screen
>    with the overlays hidden (`game-architecture.md:503-511`). A second `Camera.Render()` costs about 40 ms in the
>    town (1.14). The spike's version is `Assets/Spikes/RecordingSpike/FrameTap.cs` (104 lines). It is fenced and
>    must not be referenced; write new game code modelled on it, as 1.15 did for the backend.
>    **`CameraGame.asmdef` references only `Unity.InputSystem`.** A URP pass needs
>    `Unity.RenderPipelines.Universal.Runtime` and `Unity.RenderPipelines.Core.Runtime`, which the spike's asmdef
>    already references. Adding them is a decision (Task 1e).
> 10. **Thumbnail timing is not the photo's.** A still is photographed on the shutter frame, the graded instant. A clip
>     is announced at its stop, often **after** `SetMode` has already lowered the camera (`PhotoModeController.cs:672-677`),
>     and sometimes during teardown (`OnDisable`, `OnDestroy`), when nothing renders again. The frame that best shows
>     the subject was rendered seconds earlier, while recording.
> 11. **The viewfinder's camcorder corner is top-right.** `RecordingHud` (`UI/RecordingHud.cs`) has its own Overlay
>     canvas (sorting 50, scaler 1920×1080, match 0.5). Its `RecCluster` is anchored top-right at (−64, −48), 360 × 96:
>     - `● REC` at 44 px, red (1, 0.16, 0.12);
>     - the `H:MM:SS` timecode at 36 px, white;
>     - `STBY` at 44 px, shown while raised, not recording and able to record (`:129-139`).
>
>     Every label carries a 1.5-px black outline at alpha 0.85. The timecode string is rebuilt only when the second
>     changes. The grade readout is bottom-centre (`GradeHudCanvas`, sorting 100). The editor-only debug overlay is
>     top-left (`PhotoModeController.OnGUI`, `Rect(12,12,640,120)`), so it appears in editor captures. The GDD lists
>     the "card-time-remaining counter, CARD FULL" among the camcorder viewfinder's surfaces (`gdd.md:353`).
> 12. **The input map has room.** Player map:
>     - `Gallery` (Button) is Tab and gamepad Select;
>     - `Move` (Value Vector2) is WASD, the arrow keys and the left stick;
>     - `Previous` (1, dpad-left) and `Next` (2, dpad-right) are bound and **handled by nothing**;
>     - gamepad `buttonWest` and the Delete key are unbound;
>     - the dpad is also `Zoom`.
>
>     There is no `EventSystem` in SampleScene, and the cursor is locked (`ThirdPersonController.cs:106-107`).
>     `generateWrapperCode` is 0, so no generated class has to be kept in step. `GalleryInput` lives on the player
>     because Send Messages reaches only that GameObject (`GalleryInput.cs:13-17`), and Send Messages also reaches
>     **disabled** components (lesson 174).
> 13. **The rigs that record clips run in SampleScene**, so the card will now receive their clips. Record Clip Shoot
>     (its phase B6 records ten clips) and Grade Clip Shoot (52 clips, including a full 120-s clip) would fill a 120-s
>     card part-way through and see their later presses **refused**. A refused press looks exactly like a broken rig.
>     **They also read `RecordedClip.Footage` after the raise:**
>     - Record Clip Shoot casts `(ReplayFootage)_lastRaised.Footage` and calls `TickAt` (`RecordClipShootRunner.cs:723,
>       :890, :1270`), and its B6 `alive` column checks that the footage is not disposed (`:599-601`);
>     - Grade Clip Shoot's `CompareSets` (`GradeClipShootRunner.cs:1146-1149`), `:1221` and `WriteClip` (`:500`) read it
>       too.
>
>     `EventChannel` calls listeners in subscription order (`Core/EventChannel.cs:42-46`), and the card subscribes at
>     scene load, before any rig. So a card that disposes the scratch footage inside the raise hands every rig a disposed
>     handle: `TickAt` throws, and `CompareSets` silently skips.
>
>     Grade HUD Shoot's Phase B checks that the HUD never reaches **gallery thumbnails**. Grade HUD Shoot's Phase C and
>     Grade Clip Shoot's scenario **S9** (Phase S, `GradeClipShootRunner.cs:841-853`) open the **gallery** mid-hold.
> 14. **What the deferred list hands this story** (`deferred-work.md`):
>     - `:345`: card captions coloured with `GradeHudConfig.ColorFor` and printed with `GradeText.Percent`;
>     - `:360`: the gallery half of `Capture()`'s fate;
>     - `:364` and `:373`: the card owns capacity, so no press allocates a block, and the pool is re-shaped when the
>       audio device changes rather than on the next press;
>     - `:374`: refuse to record when full, and catch an allocation failure in `Begin` with one line;
>     - `:375`: size the card in bytes as well as seconds, because PCM scales with the device's rate and channels;
>     - `:378`: when the card claims a clip, the recorder releases the clip it holds;
>     - `:383`: size the card from recorded bytes, and count held blocks separately;
>     - `:144`: `RaiseSuppressed` is a bool, so a second screen owner needs a refcount first;
>     - the Story 1.11 items "no selection" and "18 of 50 unreachable". Delete needs a selection, so this story pulls
>       part of them forward.

## Story

As a player,
I want my clips saved on a memory card that can fill up,
So that every recording is a decision and the footage I keep is footage I chose.

## Acceptance Criteria

ACs 1–4 are verbatim from `epics.md` (Story 1.17, `:584-613`). ACs 5–6 were added by this create phase. They restate
the cross-cutting NFRs where this story bites (NFR1, NFR2, NFR3, NFR5, NFR8) and CLAUDE.md's restore rule, as the epic
asks ("restated in acceptance criteria where they bite"). They add requirements and weaken none.

1. **Every clip lands on the card.**
   **Given** a clip finishes recording,
   **When** the memory card receives it via `ClipRecordedChannel`,
   **Then** a `CapturedClip` (footage, grade, subject id, time, duration, thumbnail) is stored, evolving the photo
   gallery's storage from Story 1.11 (FR8, AR6).
   - "Every clip" means every stop route: Record pressed again, the camera lowered, the card filling, the length
     limit, the audio device changing, and the recorder disabled mid-clip. It also means every grade: counted, miss
     and not graded. The card stores the footage the player chose to record; it does not judge it.
   - The card **claims** each clip during the raise, so it becomes the footage's single owner. The recorder then
     holds nothing (`deferred-work.md:378`), and **no press frame allocates a storage block** (fact 2).
   - A thumbnail that shows the subject when the clip has one is judged by **looking** at the captures (Task 11,
     Phase H), with the clip's grade drawn on each.

2. **The card has a capacity in seconds, the viewfinder shows it, and a full card stops recording.**
   **Given** the card's capacity, measured in **seconds of footage** in a designer-tunable config (starting value
   ~2 minutes, confirmed by the stakeholder 2026-11-10),
   **When** I am in Viewfinder state,
   **Then** the viewfinder shows the time remaining
   **And** when the card fills mid-recording, recording stops, the clip so far is kept, and the viewfinder shows
   **CARD FULL** (FR27).
   - "Seconds of footage" is the footage's own timeline: Σ tick repeats ÷ 30 (ADR-4). The card can never hold more
     than its capacity, whatever the sequence of clips (a test pins it).
   - The time remaining counts down live while recording, and is right against the true free time
     (capacity − stored − recorded so far) under the rounding rule Task 1g fixes. It never reads 0:00:00 while a tick
     still fits.
   - Raising the camera with a full card shows CARD FULL. Pressing Record then records nothing: no clip, no REC and no
     start beep. CARD FULL is the feedback.
   - Whether it reads as a 2000s camcorder, legibly and at a glance, is perceptual, so it becomes a **human check**.
     Legibility is still measured first, on rendered pixels at 1080p (Story 1.19's method).

3. **The card view shows every clip, and deleting one frees it at once.**
   **Given** the memory-card view (reworked from Story 1.11's gallery view),
   **When** I open it,
   **Then** I see each clip's thumbnail, stars and duration, and I can delete one: its seconds are freed immediately
   and its memory is released, with no leak over an extended session (NFR3).
   - "Each clip": every clip on the card can be reached and selected, even when they do not all fit on screen
     (fact 7).
   - "Freed immediately": the card's free time grows by the clip's duration on the frame it is deleted, and the next
     recording can use it.
   - "Memory is released": the clip's footage storage and thumbnail are freed. This is measured with an instrument
     proven by a positive control (lessons 163, 198 and 206). Texture and RenderTexture counts and the footage store's
     used bytes return to baseline.
   - "No leak over an extended session": at least 60 record/delete cycles, including deletes from the middle of the
     card, with memory flat.
   - Whether the view and the delete feel right is a **human check**.

4. **The in-memory model is ready for disk.**
   **Given** the in-memory model,
   **When** it is designed,
   **Then** it keeps a disk-ready seam in the format ADR-4 chose, so clips can survive between sessions in Epic 9
   (AR6).
   - The format is ADR-4's (fact 3):
     - one binary track file per clip, with `FootageFormat.Version` and a content version;
     - one 16-bit WAV per clip, with its own sample rate and channel count;
     - the thumbnail as a PNG;
     - a small JSON DTO for the rest. Enums go by name, and times are ISO-8601 round-trip strings.
   - Every field of `CapturedClip` and of the footage it holds has a written disk mapping. Nothing in it is a live
     scene reference: each `EventDefinition` an actor sample points at must resolve by a stable `Id` (Task 1k).
   - No file is written in this story.

5. **Bounded, cheap, fail-soft** *(added: NFR1, NFR2, NFR3, NFR5, NFR8)*.
   **Given** the card in normal play and under stress (clips of one tick, of a few seconds and of the full capacity;
   the card filled; deletes from the start, middle and end; an audio device change between clips),
   **When** clips are recorded, stored and deleted,
   **Then** total footage memory is bounded by a number stated for the shipped config, whatever the number or length
   of clips
   **And** nothing allocates per frame while recording (the time-remaining readout included). No press frame allocates
   a storage block, and the record-start feedback stays under 0.2 s (NFR2).
   **And** the stop-frame cost of storing a clip and the frame cost of the thumbnail are measured and reported
   (milliseconds, and the release-build bench if the recording path changed)
   **And** a missing config, camera, channel or card, or a bad config value (NaN, 0, negative, huge), degrades with
   **one** clear log line each. Clips are still recorded and announced. Nothing throws into `Update` or `LateUpdate`,
   and the console stays clean in normal play. A full card is a game state, not an error, and logs nothing per press.

6. **No regression, project restored** *(added: CLAUDE.md, NFR5)*.
   **Given** the change is complete,
   **When** the EditMode suite, the spike and bench fence tests and the existing rigs that still apply (Record Clip
   Shoot, Grade Clip Shoot, Grade HUD Shoot, Photo Shoot) run,
   **Then** they pass as before, or every difference is explained: a rig that now meets the card, or a phase that was
   re-pointed or retired by Task 1h, with its reason
   **And** the editor, scene, project settings, Game View size, time scale, input settings and every config value are
   restored and read back, as are the files a Windows build re-serialises (lessons 24 and 173).

## Tasks / Subtasks

- [ ] **Task 1 — Settle the eleven decisions that GATE THE STORY (AC1–AC5). DO THESE FIRST.**

  Each has a recommendation and the evidence that should settle it. Record every choice **with its reason and its
  evidence path** in the Dev Agent Record and in the result file's `decisions`, including the ones where you follow the
  recommendation. Alexv reviews them in one batch and may veto. Where a choice can be settled by measuring, measure it,
  as Story 1.10 did for prominence and Story 1.16 did for the blend.

  - [ ] **1a. Where a stored clip's footage lives.** (AC1, AC3, AC5. Blocks everything. Facts 2 and 3.)
        - **Recommended: a card-sized preallocated store, filled by copying at the claim.**
          - **What it is.** A plain C# class in `Assets/Scripts/Recording/` (for example `FootageStore`), because only
            `Recording` knows the footage layout (`ReplayStorage` is internal).
            - `MemoryCardService` (Gallery) constructs it once, sized from the card's capacity, owns it, and drops it
              in `OnDestroy`.
            - It holds packed `TickSample[]`, `ActorSample[]`, `LayerSample[]` and `short[]` PCM arrays.
            - **Its per-tick caps must match the recorder's.** The recorder sizes its scratch blocks from
              `RecordingConfig.maxActorsPerTick` and `maxAnimatorLayers` (`ClipRecorder.cs:195-197`). Give the card the
              **same** `RecordingConfig` asset (a serialized reference, read through `MaxActorsPerTickOf` and
              `MaxAnimatorLayersOf`). Size the PCM from `AudioSettings.outputSampleRate` × `ReplayBackend.ListenerChannels()`
              at `Awake`.
            - `Keep()` checks that the incoming footage fits, then copies. If the two configs disagree, "bounded by
              construction" is false, and Task 2.4's fallback would quietly keep a 23 MB block per clip. So a mismatch
              is one warning, not a silent fallback.
            - Clear vacated `ActorSample` slots after a compaction, as `ReplayStorage.Release` does, so no stale
              `EventDefinition` reference stays alive.
            - **Rigs need to resize the card.** Task 11's small capacities are `CreateInstance` configs. Give the card a
              way to take a config before its `Awake`, or an internal re-initialise that empties it and reallocates. Do
              not rely on swapping the asset at runtime.
          - **The claim.** Inside the channel raise the card claims the clip and asks the store to keep it. The store
            copies **only the recorded part** (ticks, actors, layers and the trimmed PCM) into its free tail and hands
            back a new footage handle over the store. The card then disposes the clip's scratch footage at once, so
            the block returns to the recorder's pool. **The pool therefore never grows past its two blocks.**
          - **Delete.** Disposing a stored handle frees its segment. The store compacts with `Array.Copy` (correct for
            overlapping ranges) and re-bases the handles that follow. `TickSample.ActorStart` and
            `ActorSample.LayerStart` are already **relative to the clip**, because every scratch block starts at 0,
            so they need no rewriting. Check that.
          - **Sizing is exact, not guessed.** A clip's ticks ≤ its boundaries (every tick repeats at least once, and
            `End` adds a start tick only to a clip with none). So the whole card needs at most
            capacity-slots ticks, × `maxActorsPerTick` actors, × `maxAnimatorLayers` layers. PCM is capacity-slots ×
            1,600 frames × channels at 48 kHz.
            - At the shipped 120 s that is **≈ 24.2 MB**, about one scratch block.
            - Total footage memory is ≈ 3 × 23.1 MiB ≈ 69 MiB, **bounded by construction**.
          - **What stays as it is.** The tap, the audio thread and the backend's recording path are untouched. The
            1.15 rig's proofs still hold.
          - **The one new cost.** Copying up to ~24 MB on the stop frame. That happens after the stop beep, because
            `Stopped` is raised before the channel. Measure it for 1-s, 30-s and 120-s clips in SampleScene.
          - **The read API is the same for a stored clip.** `SampleRate`, `Channels`, `HasSound`, `Boundaries`,
            `TickCount`, `TickAt`, `ActorAt`, `LayerAt` and `ReadPcm`, because Story 1.18 plays the card's clips.
            Either let `ReplayFootage` read through (arrays, base offsets) or extract an interface. A test pins that a
            stored copy reads back identical to its scratch footage, tick by tick and sample by sample, and again
            after a compaction.
          - **⚠️ After the claim, `RecordedClip.Footage` is disposed for everyone else.** It is get-only, and the card
            runs first in the raise (fact 13). Make the clip's footage reachable afterwards **through the card**: a
            lookup by clip id that returns the stored `CapturedClip`. Say so in `RecordedClip`'s and
            `ClipRecordedChannel`'s docs ("a claimer may hand the footage to its own storage; read the claimer's copy").
            Re-point every rig read listed in fact 13 (Task 12.1).
            - Listeners registered after the card see a disposed handle by design, and the HUD reads only the grade.
            - *Alternative:* keep the scratch block until the end of the frame. Rejected: the rigs hold `_lastRaised`
              across frames, so it only moves the failure.
          - **A device change must not allocate on a press either** (`deferred-work.md:373`, measured at 2.4–5.9 ms per
            block). Today the first press after a rate or channel change calls `Prepare` from
            `ReplayBackend.Begin` (`:139-142`), which orphans the pool and allocates two new blocks on the press frame.
            - Re-shape the pool when the change is seen instead. The callback can arrive off the main thread
              (`ClipRecorder.cs:355-359`), and `LateUpdate` returns early when not recording (`:332`), so handle the
              flag before that return.
            - Expect `StoragesCreated` to grow by two at the change, never on a press frame. Phase A records both.
        - *Alternative B: a chunk pool shared by recorder and card* (1.15 decision 1e noted it "suits 1.17 better").
          There is no copy and no compaction. But the tap would write across chunk boundaries on the audio thread,
          and `ReadPcm` would have to span chunks. Choose it only if A's measured stop-frame copy is a hitch a player
          would notice.
        - *Alternative C: record directly into the store's free tail.* There is no copy. But `ListenerTap.Arm` needs an
          offset (audio-thread code that 1.15's review hardened), and the backend needs a store abstraction. It is the
          fallback if A's copy is too slow and B's is too risky.
        - *Rejected:*
          - **right-sized managed arrays per clip**, because ADR-4 `:515` forbids "a fresh managed array per take";
          - **keeping whole blocks per clip**, because that is 23 MB per clip whatever its length: 24 five-second
            clips would be ~580 MB, with an allocation on every press;
          - **streaming to disk while recording**, because AR6 puts disk in Epic 9. Note in `deferred-work.md` that
            ADR-4's crash-loss line (`:553-554`) stays open.
        - **Evidence to record:** the store's capacity in bytes for the shipped config, the stop-frame ms per clip
          length, the read-back identity test, and the 60-cycle memory series (Task 11, Phases D and E).
  - [ ] **1b. The card's unit, and what "full" means.** (AC2.)
        - **Recommended: count in integer slots (1/30-s boundaries), never in float seconds.**
          - Capacity is `FootageFormat.MaxBoundariesFor(capacitySeconds)`: 3,600 for 120 s. Used is the sum of the
            stored clips' boundaries, and free is capacity minus used.
          - Read each clip's boundaries from its footage. Add `long Boundaries` to `IFootage`, or round
            `Seconds × 30`, and pin that the rounding is exact.
          - **⚠️ Hand the limit to the backend in slots, not as float seconds. This is measured, not suspected.**
            `IClipBackend.Begin(double, float maxSeconds)` takes a `float`. Run free slots through
            `(float)(free / 30.0)` and back through `MaxBoundariesFor`, which is `floor(s × 30 + 1e-6)`, and
            **4,162 of the values 1–9,000 lose a slot** (35 → 34, 39 → 38, …; measured with numpy float32 on
            2026-11-15). The 1e-6 epsilon is far smaller than float32's error near 3,600. The card would then never
            quite fill, and remaining would read 0:00:01 when no press can record that second.
          - Add a slot-based limit to the backend: `Begin(double startDspTime, long maxBoundaries)`, or a parameter
            beside the seconds one. `ReplayBackend` is its only implementer.
          - A test over every free value from 1 to the capacity must show the clip's limit equals `free` exactly,
            **through the real `Begin` path**. A test written in doubles passes while the float path fails. If you keep
            the float, `(slots + 0.5) / 30f` rounds back exactly up to 9,000 slots, but slots are clearer.
          - **The tie.** The shipped capacity (120 s) equals `maxClipSeconds` (120 s), so on an empty card a full-length
            clip reaches both limits on the same tick. The rule: the stop is `CardFull` whenever the card's free slots
            were ≤ the config's slots at `Begin`, because the card is then genuinely full. Pin that tie in a test.
        - **The card is full when it cannot hold one more tick.** That happens when any of these is true:
          - free slots < 1;
          - the store's free PCM cannot hold one tick at the **current** output format (rate × channels;
            `deferred-work.md:375`), so a device change can never overflow the store;
          - the clip count has reached its cap (1c).
        - The budget offered at a press is the most the card can hold **in slots and in bytes**, so a clip can never
          be recorded that does not fit, and nothing recorded is ever lost.
        - **Never overfill.** Pin with a test that random clip lengths, deletes and refills never take used above
          capacity, and prove it in the rig's full-card scenario.
  - [ ] **1c. A cap on the number of clips, and reaching every clip.** (AC3, AC5.)
        - **Why a cap is needed.** Seconds do not bound per-clip costs. A 120-s card could hold 3,600 one-tick clips,
          each with a thumbnail (~0.5 MB at 480 × 270 RGBA), which is ~1.8 GB.
        - **Recommended: `maxClips` in the card config, with CARD FULL when it is reached** (1b). Pick the shipped
          value by measuring what the view shows at a readable size at 1920×1080. A realistic 2-minute card holds
          10–30 clips.
        - Whatever the cap, **every clip must be reachable** (AC3). The visible window of cells follows the selection
          (newest first), so selecting past the last visible cell shifts the window. This is a minimal scroll, not
          Epic 5's paging gallery. The header says "showing N of M" when not all fit.
        - *Alternative:* charge every clip a minimum (for example 1 s). Rejected as the only bound: it makes the
          seconds on screen disagree with the footage. It can be added beside the cap if the cap alone measures badly.
  - [ ] **1d. The thumbnail: which frame, how it is taken, and who owns it.** (AC1, AC5. Facts 9 and 10.)
        - **Recommended: ADR-4's technique, taken live while recording, of the tick that best frames the subject.**
          - **How it is taken.** A URP render pass, enqueued from script in `beginCameraRendering` for the record
            camera only, at `AfterRenderingPostProcessing`, blits the camera's own colour, downscaled, into a small
            sRGB `RenderTexture` the clip owns. There is no second render, no `ReadPixels` and no renderer-asset
            change.
          - **When it fires.** `ClipRecorder.LateUpdate` (order 10000) samples and grades each tick **before** that
            frame renders. So when a tick's grade beats the clip's best so far, the recorder arms the copy for this
            very frame, and the thumbnail is exactly the frame that was graded.
          - **What "best" means.** Write it as a small pure function, and test it:
            - prefer ticks where a subject passed the gate;
            - among those, prefer the peak, then the higher composition;
            - the first tick is the fallback when nothing ever passed.
          - Rate-limit re-arming if the bench shows cost (for example only on a real improvement, or at most every
            0.25 s).
          - **Ownership.** The thumbnail rides on `RecordedClip` under the **same claim rule** as the footage. The
            claimer owns it and destroys it on delete. The recorder destroys unclaimed thumbnails as it disposes
            unclaimed footage. No unknown number of listeners shares a texture (1.11's argument,
            `GalleryService.cs:16-41`).
          - The view shows the `RenderTexture` directly through `RawImage`, with no readback. The disk seam (AC4)
            reads it back once with `AsyncGPUReadback` when Epic 9 saves the clip. Write that down.
          - Size it from the config's thumbnail height, with the width following the camera's aspect
            (`GalleryConfig.ThumbnailWidthFor` already does that maths; reuse it, do not copy it).
        - *Alternative B: the first or the last tick through the same pass.* It is simpler, but a thumbnail of an
          empty street is common: the player often lowers the camera after the drunk has gone.
        - *Alternative C: 1.11's `cam.Render()` + `ReadPixels` inside the raise at the stop.* It is proven and needs
          no URP reference. But ADR-4 says never `ReadPixels`, a second render costs ~40 ms in the town, and on a
          teardown stop nothing can render. If chosen, record it as a deviation from ADR-4.
        - **Criteria:**
          - the thumbnail shows the subject whenever the clip graded him (judged by looking at every scenario's
            thumbnail with the per-tick grade drawn on);
          - **no UI**: the control is a full-screen Overlay marker visible on screen and absent from the thumbnail;
          - colour matches the screen (the spike's SSIM method; the project is Linear with HDR);
          - the cost is measured on the frames with a copy and as a bench DiD;
          - the RenderTexture count is flat over 60 cycles.
        - **Traps the spike already paid for:**
          - rows come back bottom-up on D3D11 (`SystemInfo.graphicsUVStartsAtTop`);
          - URP can hand you the backbuffer, with nothing to copy (`FrameTap.BackbufferSkips`);
          - an `AsyncGPUReadback` NativeArray cannot be disposed before its request completes.
        - **Two edge cases to decide and test:**
          - **The camera renders more than once a frame in the editor** (lesson 164). Copy only for the record camera,
            skip SceneView and preview cameras (`camera.cameraType`), and copy once per armed frame. Then a second
            render can never overwrite the graded frame.
          - **A clip stopped before any frame rendered.** RT pressed and LT released in one input update, or a stop on
            the press frame, gives a one-tick clip that `End()` completed (`ReplayBackend.cs:320-324`). It never had a
            rendered frame. Either arm one copy for the next render or store it with a plate. Decide, and put such a
            clip in Phase H.
          - **The final tick.** A clip stopped by the card filling or the length limit stops in the same `LateUpdate`
            that sampled its last tick (`ClipRecorder.cs:344-351`). It is announced, and claimed, **before** that frame
            renders. A copy armed for that tick has not happened yet when the card takes the thumbnail. Either keep the
            pass writing into the **same RT object** the claimer now owns, so the copy still lands later that frame, or
            exclude the final tick from "best". Decide and test.
  - [ ] **1e. Adopt URP references in `CameraGame.asmdef`?** (Only if 1d needs the pass.)
        - **Recommended: add `Unity.RenderPipelines.Universal.Runtime` and `Unity.RenderPipelines.Core.Runtime`.**
          The project is URP-only (NFR4), so this couples it to nothing it does not already depend on. Invoke
          `unity:validate-urp-render-graph-renderer-feature` before writing the pass. Afterwards:
          - check that `RecordingSpikeFenceTests` and `RecordingBenchFenceTests` still pass;
          - confirm with `CompilationPipeline.GetAssemblies` that the plain player assembly still excludes the spike
            (lessons 34 and 35);
          - record the new references under §Architectural Boundaries ("One assembly", `game-architecture.md:952-953`),
            and return a lesson that supersedes lesson 80 ("CameraGame references only Unity.InputSystem"), which
            lives only in `loop/lessons.md`.
        - *Alternative:* a separate `CameraGame.Rendering` asmdef. Rejected: `CameraGame` would still have to reference
          it, so it buys nothing.
  - [ ] **1f. How the card's budget reaches the recorder, and how a full card stops it.** (AC2. Facts 5 and 6.)
        - **Recommended: a small interface owned by `Recording`, implemented by the card, and read by the recorder.**
          - Define it in `Recording`, for example
            `public interface IClipBudget { long FreeSlots { get; } bool IsFull { get; } }`, with full as defined in
            1b. `MemoryCardService` implements it.
          - `ClipRecorder` gets `[SerializeField] private MonoBehaviour clipBudget;`, cast to `IClipBudget` in
            `Awake`. A component that does not implement it is one `Error`, and the recorder then runs with no
            budget.
          - So `Recording` references no `Gallery` type, and the wiring is one visible scene reference.
          - No budget, as in the isolated rigs, means today's behaviour exactly: the config's `maxClipSeconds`.
          - *Alternative:* the card registers itself (`recorder.SetBudget(this)` in `OnEnable`/`OnDisable`). It is
            acceptable, but it is a `Gallery → Recording` **call**, where the architecture says other folders "reach it
            through `ClipRecordedChannel` or by reading" (`:941-942`). Record whichever you choose.
        - **`Begin`.**
          - When the budget is full, refuse **before** the backend (fact 6). There is no clip, no beep and no REC,
            and **no log line**: a full card is a game state, not a fault.
          - Otherwise the clip's limit, **in slots**, is `min(MaxBoundariesFor(config max), budget.FreeSlots)` (1b
            explains why it is not seconds).
          - Catch an allocation failure from the backend and refuse with one line (`deferred-work.md:374`).
        - **The stop.** Add `StopReason.CardFull`, **appended after `Disabled`**. Never reorder: the disk seam
          persists `StopReason` by name, and existing values keep their ordinals. The backend's "full" becomes
          `CardFull` when the card's budget was the binding limit at `Begin`, and `LengthLimit` otherwise.
          - *Alternative:* reuse `LengthLimit` (`StopReason.cs:17-19`). Rejected: the log, the rig and a future reader
            could not tell "the card filled" from "the clip reached its length cap". The two differ whenever capacity
            exceeds `maxClipSeconds`.
        - **For the viewfinder,** the recorder exposes read-only `HasBudget`, `RemainingSeconds` (free minus elapsed
          while recording, free otherwise) and `IsCardFull`. `UI` reads the recorder, as it already does for the
          timecode (`UI → Recording` read-only, `:943-947`). **There is no `UI → Gallery` dependency.**
        - **A refusal the HUD can hear.** `Begin` returns false silently, and `ToggleRecording` ignores the result
          (`PhotoModeController.cs:413`). For CARD FULL to re-flash on a refused press, the recorder raises a C# event
          (for example `Refused`, beside `Started` and `Stopped`, isolated the same way) or bumps a counter the HUD
          reads. Name the mechanism; the HUD still only reads and listens.
        - **Release the held clip.** After the raise, when someone else claimed the clip, the recorder disposes any
          `LastClip` it still holds and sets it to null (`deferred-work.md:378`). With the card, the recorder holds
          nothing.
  - [ ] **1g. What the viewfinder shows.** (AC2. Fact 11.)
        - **Recommended: a line under `RecordingHud`'s top-right REC/STBY corner.** This is the camcorder's own corner,
          and the readout must be on screen whenever the camera is raised, in STBY and while recording.
          - **Wording.** For example `REM 0:01:23`. Choose the wording by looking at it. Use `ClipTimecode`-style
            formatting in the invariant culture.
          - **When it shows.** Whenever the camera is raised and the recorder has a budget.
          - **While recording.** It counts down live, and its string is rebuilt only when the shown second changes
            (no allocation per frame; prove it).
          - **Rounding: choose explicitly, because two natural wishes conflict.** The timecode floors (a camcorder
            counter). Remaining must never read 0:00:00 while a tick still fits. But "timecode + remaining always equals
            the free time at the press" **cannot** hold with a floored timecode and a ceiled remaining. Example: 31
            free slots and 1 recorded gives 0 + ceil(30/30) = 1, while ceil(31/30) = 2. Brute force over free values
            1–199 and recorded values 0 to free finds 8,998 of 20,099 pairs failing (computed 2026-11-15). The two
            candidates:
            - **(a) Recommended: honest remaining.** Show `ceil((free − recorded) / 30)` whole seconds. It is never more
              than one second above the truth, and CARD FULL appears exactly when `Begin` would refuse. The sum can
              differ from the free time at the press by one second, so say so in the doc.
            - **(b) Constant sum.** Show `ceil(free / 30) − floor(recorded / 30)`. The sum is constant, but it can
              overstate the time left by up to about 2 s.
            - Pin the chosen rule, its never-0:00:00 property and its CARD FULL instant with tests.
          - **CARD FULL.** It replaces STBY and the remaining line whenever the card is full. When the card fills
            mid-clip it appears on the stop event itself (not on the next `Update`), and REC hides with the usual stop
            beep. A refused press on a full card re-flashes CARD FULL, so the press is visibly heard.
          - **Colour.** Not the grade readout's "earned nothing" red (Story 1.19 gave red that meaning). Choose
            white or amber, steady or blinking, by looking, then measure it with Story 1.19's method (text against
            outline ≥ 4.5:1 at 1080p).
          - **Space, and its own group.** ⚠️ STBY is **not** inside `RecCluster`. `Build()` makes a separate `Standby`
            rect with its own CanvasGroup (`RecordingHud.cs:198-203`), and `_recGroup` is hidden whenever the recorder
            is not recording (`:133`). So a line added to `RecCluster` would vanish in STBY.
            - Give the remaining line (and CARD FULL) their **own** rect and group.
            - Place them so they read right under both `● REC` + timecode and `STBY`: below the taller REC cluster,
              for example, at a fixed offset.
            - Keep them clear of the REC dot.
        - *Alternative:* put it on the `PhotoViewfinder` canvas. Rejected for 1.15's reason: it is Constant Pixel Size
          and fades in over 0.15 s.
        - Without a budget (the isolated rigs) there is no remaining line and no CARD FULL.
  - [ ] **1h. The gallery's storage, its view, the still path, the Gallery Shoot rig, and naming.** (AC1, AC3, AC6.
        Fact 8.)
        - **Recommended: the card replaces the gallery's storage.** The architecture says `MemoryCardService` evolves
          `GalleryService` (`:226`). 1.16 kept stills stored only "until Story 1.17's memory card replaces the
          gallery's storage".
          - **Rename, keeping the GUIDs.** `GalleryService` → `MemoryCardService` and `GalleryView` →
            `MemoryCardView`, by `git mv` of the `.cs` **and** its `.cs.meta`. The script GUID survives, so
            SampleScene's components survive and only their fields need re-wiring. `GalleryInput` can keep its name,
            and the `Gallery` action keeps its name, because `OnGallery` derives from it.
          - **The card stores clips only.** Nothing stores stills any more. `Capture()` and `ShotCapturedChannel` stay,
            unchanged and unbound, for the Photo and Grade HUD Shoot rigs and the still readout.
          - **Retire `CapturedShot` and `GalleryConfig`** if nothing else needs them. Grep first: `GradeText.cs`,
            `ShotGrade.cs` and `ThirdPersonCamera.cs` mention the gallery, mostly in comments. Their thumbnail fields
            move to the card config (1j), and `GalleryConfigTests` moves with them.
          - **Retire the Gallery Shoot rig** (its subject no longer exists). This story's Memory Card rig replaces it.
          - **Re-point the rig phases that use the gallery:**
            - Grade HUD Shoot Phase B, which checks that the HUD never reaches thumbnails, now checks clip thumbnails;
            - Grade HUD Shoot Phase C and Grade Clip Shoot scenario S9 (Phase S), which open the gallery mid-hold, now
              open the card view.
            - If a phase cannot be re-pointed, retire it with its reason. Rigs are tools, not review subjects (project
              memory), but they must compile and still mean something.
        - *Alternative B: keep `GalleryService` storing stills beside a new card.* Rejected. Only rigs take stills, so
          that is up to 25 MB for nothing. Two screen owners would also make the `RaiseSuppressed` bool a live bug
          (`deferred-work.md:144`).
        - *Alternative C: evolve the classes in place under their old names.* It is the least churn, but the names
          would lie. Choose it only if the rename's reference count proves unreasonable, and record it.
        - **The caption colour crosses a boundary.** `GradeHudConfig` (and so `ColorFor`) is in `CameraGame.UI`
          (`UI/GradeHudConfig.cs:4`), and the architecture sanctions no `Gallery → UI` direction (`:923-925`).
          - **Recommended:** let the view hold a read-only reference to the shipped `GradeHudConfig` asset (the
            palette) and record a read-only `Gallery → UI` direction in Task 13.
          - *Alternatives:* lift the colour rule (and its palette) into `Grading`, which is more churn; or move the
            view itself into `UI`. `deferred-work.md:345` allows "or the same rule".
        - **Overlay or Screen Space – Camera for the view?** 1.11 chose Screen Space – Camera only so that `cam.Render()`
          could photograph it. The Overlay capture technique has since been proven (1.12, 1.19: 100.00 % / 0.00 %
          controls). A Screen Space – Camera canvas composites **before** post-processing, which brings two
          consequences:
          - the captions are tonemapped (Task 7.4);
          - **probably the thumbnails too.** An already post-processed sRGB RT shown through `RawImage` would be
            tonemapped and graded a second time. This is a hypothesis, not reproduced; 1.11's thumbnails had the same
            exposure.
          - Measure it: compare the thumbnail's pixels as the view shows them with the RT's own pixels. If they
            differ, switching the view to **Screen Space – Overlay** removes both effects and the near-plane trap.
            It then needs the Overlay capture in the rig and an opaque backdrop that still hides the world. Decide by
            that measurement, and record it.
        - With one screen owner (the card view) the suppression bool still has one caller. Story 1.18's playback will
          be the second, so say so in `deferred-work.md`. If you keep two owners, make it a refcount or an owner token
          **first** (`game-architecture.md:933-937`).
  - [ ] **1i. Selecting and deleting in the card view: the input and the confirm.** (AC3. Fact 12.)
        - **Recommended: stay with Send Messages and add one action.**
          - `GalleryInput`, on the player, also handles `OnMove` while the view is open. Edge-detect the Vector2 into
            one grid step per push (left, right, up, down by a row).
          - Add a Button action `DeleteClip`, bound to Keyboard Delete and Backspace and to gamepad `buttonWest`
            (X / Square), with a `GameConstants.InputActions` constant. Its handler forwards to the view.
          - Both handlers are gated on `isActiveAndEnabled` (lesson 174) and on the view being open.
          - Check that the player does not walk while the view is open. `SetInputSuppressed` already zeroes movement;
            prove it with a real stick push.
        - **Confirm in two presses.** The first press marks the selected cell ("DELETE? press again"). A second press
          of Delete within a configured window deletes. Moving, closing or the window running out cancels.
          - A deleted great clip cannot be recovered, and the story's whole premise is that what you keep is a choice.
          - After a delete, the selection moves to the neighbour, and the header and the free time update at once.
        - *Alternative:* a uGUI `EventSystem` with Selectables and `InputSystemUIInputModule`. That gives mouse clicks
          and standard navigation, but needs the cursor unlocked while the view is open, an EventSystem in the scene
          and the UI action map. Consider it for Epic 5's full gallery, not here. If chosen, invoke `unity:ui-ugui`
          first.
        - *Alternative:* a hold-to-delete. Choose by feel; it is a **human check** either way.
  - [ ] **1j. The card's config: where it lives and what it holds.** (AC2, AC5.)
        - **Recommended:** `Assets/Scripts/Gallery/MemoryCardConfig.cs` (namespace `CameraGame.Gallery`) and
          `Assets/Data/Gallery/MemoryCardConfig.asset`, beside the service that reads it. Correct the architecture tree
          (`:862` plans `Data/Recording/`).
        - **Fields:**
          - `capacitySeconds` (120, the stakeholder's ~2 minutes);
          - `maxClips` (from 1c);
          - `thumbnailHeight` and `maxThumbnailWidth` (moved from `GalleryConfig`, with `ThumbnailWidthFor`);
          - `deleteConfirmSeconds`.
        - **The validated-config pattern:**
          - named `Default*` constants shared by the initialiser and the fallback;
          - `Safe*` accessors that handle non-finite values first, then clamp;
          - a `TryGetConfigProblem` that reports **every** problem in one message, in the invariant culture
            (`deferred-work.md`, 1.16);
          - **no `OnValidate`** (`GalleryConfig.cs:218-237` explains why);
          - a `DescribeBudget()` for the one Info line at `Awake`, giving capacity, store bytes, the thumbnail bytes
            worst case and the total;
          - a warning when the worst case passes a stated budget.
        - **The asset is hand-written YAML** using the script's `.meta` GUID (project memory: MCP cannot create custom
          ScriptableObject assets). Read it back through the editor before any rig.
        - `RecordingConfig.maxClipSeconds` stays the longest single clip and the scratch block's size. Update its
          tooltip, which says 1.17 replaces it. A capacity above it means a single clip stops at `LengthLimit` while
          the card still has room. Say so in both tooltips.
  - [ ] **1k. The shape of `CapturedClip` and the disk seam.** (AC1, AC4.)
        - **Recommended: the architecture's record** (`:229`):
          - `Id` (the clip's id);
          - `Footage` (the stored handle);
          - `Thumbnail` (typed `Texture`, or `RenderTexture` per 1d; the architecture's `Texture2D` was written before
            1d, so correct it);
          - `Grade` (`ClipGrade`);
          - `SubjectId` (`Grade.SubjectId`, empty for nobody);
          - `CapturedAtUtc`;
          - `DurationSeconds`;
          - `StopReason` (so a CARD FULL stop is visible later).
        - **Make `CapturedAtUtc` the recording's start** (`RecordedClip.StartedAtUtc`), when the player pressed Record,
          and say so in its doc. The two names disagree today.
        - A sealed class or a readonly struct: choose and say why. A footage handle that can be disposed argues for a
          class.
        - **Write the disk mapping in `CapturedClip`'s doc comment**, as 1.11 did for `CapturedShot`
          (`CapturedShot.cs:17-56`). That comment **is** a deliverable:
          - `Id` → the filename stem of every file;
          - footage → `<id>.track` (`FootageFormat.Version`, a content version, the tick rate, then ticks, actors and
            layers, with each actor's `EventDefinition` **by its `Id` string**, never by reference) and `<id>.wav`
            (16-bit, its own rate and channels);
          - thumbnail → `<id>.png` via `AsyncGPUReadback` (or `EncodeToPNG` if it is a `Texture2D`);
          - the rest → `<id>.json` through a DTO (`JsonUtility` handles neither `DateTime` nor textures), with
            `MissReason` and `StopReason` by name and times as `ToString("o")`.
        - **Make the seam checkable without a codec:**
          - a test that every `EventDefinition` asset in `Assets/Data/Events/` has a non-empty, unique `Id`, the key
            the track will be saved by (this also closes the deferred "empty Id captions nobody");
          - a test that `CapturedClip` holds no `UnityEngine.Object` other than the thumbnail;
          - a named content-version constant (for example `FootageFormat.ContentVersion = 1`), with its bump rule
            written beside `Version`'s (ADR-4 `:576-578`).
        - *Alternative:* also write the in-memory round-trip codec now. That is Epic 9's first story ("its first story
          makes clips survive between sessions"). Do it only if it stays small and you have the budget. It is not
          required by AC4.

- [ ] **Task 2 — The footage store** (AC1, AC3, AC5; Task 1a, 1b)
  - [ ] 2.1 Create the store type in `Recording` (1a). Allocate everything in its constructor, sized exactly as 1a
        derives. Keep `Keep(IFootage scratch) → IFootage stored` and free/compact on the stored handle's `Dispose`.
        Main thread only, like `IFootage.Dispose`.
  - [ ] 2.2 Give stored clips the same read API as `ReplayFootage` (1a), keeping the `IsDisposed`, `Seconds` and
        `Bytes` semantics. A disposed handle reads as empty, never as another clip's data.
  - [ ] 2.3 Expose the store's `CapacitySlots`, `UsedSlots`, `CapacityBytes`, `UsedBytes`, `ClipCount` and the free PCM
        at a given format (1b). Make them diagnostics a rig and the card can read.
  - [ ] 2.4 Fail-soft: a clip that cannot fit (it should not happen if 1b holds) is **kept in its scratch block**,
        claimed by the card, with one warning. It is never dropped silently, because recorded footage is always kept.

- [ ] **Task 3 — `MemoryCardConfig`, `CapturedClip` and `MemoryCardService`** (AC1–AC5; Task 1b, 1c, 1h, 1j, 1k)
  - [ ] 3.1 `MemoryCardConfig` and its asset (1j). Re-home or retire `GalleryConfig` and its tests (1h).
  - [ ] 3.2 `CapturedClip` with the written disk mapping (1k).
  - [ ] 3.3 `MemoryCardService` (renamed from `GalleryService`, 1h):
        - **Wiring:** `[SerializeField]` `clipRecordedChannel`, `config`, and the **same** `RecordingConfig` asset the
          recorder uses (1a: the store's per-tick caps).
        - **Readiness:** independent fail-soft flags, as `GalleryService` has.
        - **Subscription:** subscribe in `OnEnable` and unsubscribe in `OnDisable`.
        - **In the raise:** claim the clip, store its footage (Task 2), take its thumbnail (Task 4), and append a
          `CapturedClip`.
        - **Its surface:**
          - implements `IClipBudget` (1f);
          - `Clips` as an `IReadOnlyList<CapturedClip>`, oldest first, and a lookup by clip id (the way a rig, and
            later Story 1.18, reaches a clip's footage once the card has stored it, 1a);
          - `Delete(string id)` (or by index), which disposes the footage, destroys the thumbnail and frees the slots
            on the same frame;
          - a rig-only `ClearAll()`;
          - `OnDestroy` frees everything.
        - **Logging:** one Info milestone per stored clip and per delete; nothing per frame.
  - [ ] 3.4 Rename `IsRecording` (today it means "the gallery is storing" and collides with `ClipRecorder.IsRecording`)
        to something like `IsStoring` or `IsReady`.

- [ ] **Task 4 — Thumbnails** (AC1, AC5; Task 1d, 1e)
  - [ ] 4.1 If 1e adopts URP: add the asmdef references and invoke `unity:validate-urp-render-graph-renderer-feature`.
        Write the copy pass as new game code, modelled on `FrameTap.cs` and never referencing the spike.
  - [ ] 4.2 The "best tick" rule as a pure, tested function (1d). Arm the copy from `ClipRecorder.LateUpdate` after the
        tick is graded. Do not allocate per frame, and do not make a second render.
  - [ ] 4.3 Thumbnail ownership under the claim rule. `RecordedClip` carries it, and the recorder destroys an
        unclaimed one. `ClipGrader` stays internal (1.16 review): expose only what the thumbnail rule needs.
  - [ ] 4.4 Fail-soft: no camera, a skipped backbuffer frame, or a lost RT means a clip stored with no picture, drawn
        as a flat plate (as `GalleryView` does), with one line, not one per clip.

- [ ] **Task 5 — The recorder meets the card** (AC2, AC5; Task 1f)
  - [ ] 5.1 `IClipBudget`, the serialized budget reference, and the refuse-before-the-backend rule in `Begin`.
  - [ ] 5.2 `StopReason.CardFull` (appended), chosen when the budget was the binding limit. Release the held
        `LastClip` when someone else claims (`deferred-work.md:378`). Catch an allocation failure in `Begin`
        (`deferred-work.md:374`).
  - [ ] 5.3 Read-only `HasBudget`, `RemainingSeconds` and `IsCardFull`. Document how they behave before `Awake`,
        while recording, and with no budget.
  - [ ] 5.4 Update `RecordingConfig`, `StopReason` and `ClipRecorder` docs and tooltips that say "Story 1.17 will…"
        so that none of them still describes the future.

- [ ] **Task 6 — The viewfinder readout** (AC2; Task 1g)
  - [ ] 6.1 `RecordingHud`: the remaining line, CARD FULL, the refused-press flash, and the cluster resized. It reads
        only the recorder (and `PhotoModeController` for raised), as it does today.
  - [ ] 6.2 Its strings come from pure, invariant-culture formatters with EditMode tests: the rounding rule 1g chose,
        floor for the timecode, never 0:00:00 while a tick fits, and CARD FULL exactly when `Begin` refuses.
  - [ ] 6.3 Prove that its per-frame path allocates 0 B (1.16 Phase G's instrument).

- [ ] **Task 7 — The memory-card view** (AC3; Task 1c, 1h, 1i)
  - [ ] 7.1 Rework `GalleryView` into `MemoryCardView`:
        - **Cells:** the thumbnail; the stars; the duration (`H:MM:SS` or `M:SS`, invariant culture); and the
          percentage (`GradeText.Percent`) or the miss reason (`GradeText.MissShort`, which is load-bearing, so keep
          it byte for byte).
        - **Caption colour:** `GradeHudConfig.ColorFor`, as `deferred-work.md:345` asks.
        - **Header:** clip count, used and free time, and "showing N of M" when not all fit.
        - **Order and pooling:** newest first, with the cell pool built once from `maxClips`.
  - [ ] 7.2 Add selection (a visible frame around the chosen cell) and a window that follows it (1c), plus the two-press
        delete (1i).
  - [ ] 7.3 Keep every rule 1.11 paid for:
        - Walk-only open;
        - show first, fill second;
        - `planeDistance = near + 0.1`, which depends on `ThirdPersonCamera`'s execution order;
        - a fully opaque backdrop;
        - layers inherited by children;
        - the culling-mask warning;
        - suppression released in `OnDisable`;
        - re-layout on resize;
        - layout validation that reports every problem.
  - [ ] 7.4 Measure the caption colours **as rendered** on the opaque backdrop. This canvas composites **before**
        tonemapping, so the authored colour is not what the player sees. Use Story 1.19's method.

- [ ] **Task 8 — Input** (AC3; Task 1i)
  - [ ] 8.1 Add the `DeleteClip` action (Button) to `InputSystem_Actions.inputactions` (LF, JSON). Edit it carefully
        and read it back through the editor. Add the `GameConstants` constant.
  - [ ] 8.2 `GalleryInput`: `OnMove` and `OnDeleteClip` forwarding, gated as 1i says.

- [ ] **Task 9 — Scene wiring** (AC1–AC3)
  - [ ] 9.1 Through MCP: re-wire the renamed `MemoryCardService` and `MemoryCardView` fields, and set the recorder's
        `clipBudget` to the card. Remove references the old fields left behind.
  - [ ] 9.2 Save the scene **in a separate call** before any rig (project memory: a dialog otherwise makes the menu
        call retry six times). Check `git diff` on SampleScene, which is CRLF in the working tree, so that only the
        intended references changed.

- [ ] **Task 10 — EditMode tests** (AC1–AC5)
  - [ ] 10.1 **Accounting:**
        - the slot round trip for every free value from 1 to 3,600;
        - random clip, delete and refill sequences never exceed capacity;
        - full means "cannot hold one tick" in slots, bytes and count;
        - the budget offered never exceeds what the store can hold at the current format.
  - [ ] 10.2 **The store:**
        - a stored copy reads back identical to the scratch footage: ticks, actors, layers and PCM, including a clip
          with no sound and a one-tick clip;
        - it is still identical after compacting out a clip before it;
        - disposal is idempotent, and a disposed handle never reads another clip's data;
        - used bytes return to 0 after deleting everything.
  - [ ] 10.3 **The recorder:**
        - a full budget refuses `Begin` with no backend call and no log;
        - `CardFull` against `LengthLimit`;
        - the held `LastClip` is released when the card claims;
        - no budget behaves exactly as today.
        Build components on inactive objects as lessons 149, 184 and 209 describe, because `AddComponent` in edit mode
        runs no `Awake`.
  - [ ] 10.4 **Config:** defaults are clean, and bad values are made safe and **all** reported together in the
        invariant culture. Run under fr-FR and de-DE, by reflection, as lesson 208 describes. The shipped asset must
        pass its own validator.
  - [ ] 10.5 **The seam:** every `EventDefinition` has a non-empty, unique `Id`; `CapturedClip` holds no stray Unity
        objects; the content-version constant exists (1k).
  - [ ] 10.6 **Formatters:** the remaining rule from 1g, timecode floor, never 0:00:00 while a tick fits, and the
        invariant culture.
  - [ ] 10.7 **Prove the tests can fail by mutation.** Break one line per guarded rule, watch the test go red, then
        restore it.

- [ ] **Task 11 — Verify by running it: the Memory Card rig** (AC1–AC6)

  An editor `[MenuItem]` (for example `Tools/Memory Card/Memory Card Shoot (Play)`) in `Assets/Scripts/Editor/`, with
  its runner in the runtime assembly behind `#if UNITY_EDITOR`. It runs **in SampleScene**, so the real recorder,
  card, HUD and view are what is measured.
  - It drives **real input** through a virtual gamepad:
    - LT raises, RT records, Select opens the card view, the left stick moves the selection, and X deletes;
    - `backgroundBehavior = IgnoreFocus` is set for the run and restored afterwards (lesson 170);
    - every press is proven to have arrived (lesson 160).
  - Its output goes to `_bmad-output/verification/1-17-the-memory-card/<run>/`.
  - Overlay readouts are captured with `ScreenCapture.CaptureScreenshotAsTexture()` after `WaitForEndOfFrame`, with
    no `targetTexture` bound, at the 1080p preset (`GradeHudShootRig.SelectFullHdGameView`).
  - The card view is photographed through `cam.Render()`, because it is Screen Space – Camera.
  - Small capacities come from `CreateInstance` copies of the config. **Never edit the shipped asset.**
  - At least one run uses the **shipped** 120 s and fills it by real recording.

  - [ ] **Phase 0, controls.** Prove each before trusting any result:
        - a 3D frame that is not uniform;
        - an Overlay marker reading 100 % on and 0 % off;
        - the thumbnail control: an Overlay marker on screen that is absent from that frame's thumbnail;
        - a memory-instrument control: a retained ~23 MB `short[]` must move the counter you will use to prove
          release (lessons 198 and 206: `GC.GetTotalMemory` is blind to retained arrays here);
        - baseline counts of `Texture2D`, `RenderTexture` and the recorder's `StoragesCreated`.
  - [ ] **Phase A, store.** One clip per stop route, through real input where one exists: press, lower, card full,
        length limit, device change (`AudioSettings.Reset` + `Thread.Sleep(200)`, lesson 178; never toggle
        `m_DisableAudio`, lesson 182), and disabled. Each clip is on the card with its grade, subject, start time,
        duration and stop reason. The recorder holds nothing. `StoragesCreated` stays 2, except +2 at a format-changing
        device change, and **never on a press frame** (1a).
  - [ ] **Phase B, capacity and the viewfinder.** Grab the viewfinder:
        - with an empty card, in STBY, showing remaining;
        - mid-recording, with remaining checked against the true free time under the rule 1g chose;
        - on the frame the card fills (the stop, the beep, CARD FULL);
        - after raising with a full card;
        - after pressing Record on a full card (refused: the clip count, the beep and REC all checked);
        - after a delete, when raising again shows the freed time and recording uses it.
        Measure legibility (1.19's method, 1080p, frozen frames, glyph masks).
  - [ ] **Phase C, the view.** Open it through real input. Grab the grid (thumbnails, stars, durations, header) with
        more clips than fit, so the window follows the selection. Grab the selection moving and the two-press delete
        (marked, confirmed, cancelled by moving). Check the player did not move while the view was open.
  - [ ] **Phase D, delete and leaks.** At least 60 record/delete cycles of short clips, deleting from the start, middle
        and end. After each: the store's used bytes and slots, its clip count, `Texture2D` and `RenderTexture` counts,
        `StoragesCreated`, and the proven memory instrument. After compaction, the clips that moved read back
        identical, tick by tick and sample by sample, to a copy taken before.
  - [ ] **Phase E, cost.**
        - Stop-frame milliseconds to store 1-s, 30-s and 120-s clips.
        - The thumbnail copy's cost on the frames it fires, as an interleaved A/B (lesson 127).
        - The record-start feedback time (NFR2).
        - 0 B per frame while recording, with the remaining readout on screen (`ProfilerRecorder` "GC Allocated In
          Frame" over a baseline frame, a string control and an equal busy-wait; 1.16 Phase G).
        - If the thumbnail pass touches the recording path, rebuild and re-run the release-build bench
          (`Tools/Recording/Build Recording Bench (Windows)`, `run_recording_bench.ps1`, `recording_bench_did.py`).
          Report the mean **and** the median DiD (lesson 187), then restore the five re-serialised files (lesson 173).
  - [ ] **Phase F, fail-soft.** Provoke one fault at a time: no config, bad values (NaN, 0, negative, huge capacity,
        `maxClips` 0), no channel, no budget on the recorder, a budget component that does not implement it, and no
        thumbnail camera. Expect one line each, clips still recorded and announced. **Name the provoked lines at the
        top of the output.** A normal run must have a clean console; grep `Editor.log` from the line count taken
        before launch (lesson 203).
  - [ ] **Phase H, thumbnails.** A contact sheet of every scenario's thumbnail with the clip's grade, stop reason and
        best-tick time drawn on. Look at each one: does it show him when the clip graded him?
  - [ ] **Look at every capture yourself.** Suspect the rig before the code. Every case identical, every case
        passing, a round number, or a blank or white capture means the rig is broken until proven otherwise.
  - [ ] **Before every run:** `refresh_unity`, wait for the compile, read the config values back through the editor,
        and check `assets.external_changes_dirty` is false.
  - [ ] **After every run:** restore and read back the scene, the Game View size, the input settings, the time scale,
        the configs and `NavMesh.pathfindingIterationsPerFrame` if touched. Destroy the drunk only as the rig's last
        act (lesson 204).

- [ ] **Task 12 — The other rigs meet the card** (AC6; fact 13, Task 1h)
  - [ ] 12.1 Record Clip Shoot and Grade Clip Shoot, the two rigs that record in SampleScene:
        - **Empty the card between scenarios** with the rig-only `ClearAll()`, so a filled card never refuses one of
          their presses. Detaching the budget alone is not enough, because the card still claims and stores.
        - **Re-point every footage read** from `_lastRaised.Footage` to the card's stored copy, looked up by clip id
          (fact 13, 1a): Record Clip Shoot `:723, :890, :1270` and B6's `alive` column (`:599-601`); Grade Clip Shoot
          `CompareSets` (`:1146-1149`), `:1221` and `WriteClip` (`:500`).
        - **Prove the re-point worked:** no "skipped (no footage)" line, and the tick counts equal the graded ticks.
        - **Update Record Clip Shoot's expectations** to the card's ownership: the claim and `LastClip` (B7), and the
          storage counts (E).
  - [ ] 12.2 Re-point Grade HUD Shoot Phase B (clip thumbnails instead of gallery thumbnails), Grade HUD Shoot
        Phase C and Grade Clip Shoot scenario S9 (the card view mid-hold). Retire the Gallery Shoot rig (1h). Record
        each change and its reason.
  - [ ] 12.3 Record Clip Shoot writes into 1.15's `rig/` folder: park and restore it with
        `_bmad-output/verification/1-15-record-a-clip/close-a1/swap_rig_dir.py` (lessons 181 and 189). Copy any story's
        evidence folder aside before a re-run, and `diff -r` it back (lesson 103).

- [ ] **Task 13 — Architecture, GDD, deferred work** (AC4, AC6)
  - [ ] 13.1 `game-architecture.md`: §Clip Recording as built by 1.17 (the store, the budget interface, `CardFull`,
        thumbnails, ownership). Under §Architectural Boundaries, write in the `Gallery → Recording` direction (the card
        implements `IClipBudget`, claims, stores) and the `UI → Recording` read of the remaining time. Fix the
        directory tree: `Data/Gallery/MemoryCardConfig.asset`, `Scripts/UI/` and the `Recording`, `UI` and `World`
        folders. Note the asmdef's URP references if adopted. Update the frontmatter `updated:` in its usual form.
  - [ ] 13.2 GDD: only if a design fact changed (the clip cap, the delete confirm). Keep it a one-line dated note.
  - [ ] 13.3 `deferred-work.md`:
        - resolve `:345`, `:360`, `:364`, `:373`, `:374`, `:375`, `:378` and `:383` as far as this story does, each
          with its evidence;
        - add what the story leaves: the crash-loss line, the suppression bool before 1.18, and anything found along
          the way;
        - keep "Lesson N" meaning line N of `loop/lessons.md`.

- [ ] **Task 14 — Regression, restore, close** (AC6)
  - [ ] 14.1 Forced recompile with a clean console. Run Roslyn at `-warn:4` (only CS0649 is expected), using
        `_bmad-output/verification/1-16-grade-a-clip/close-a1/compile_warnings.py`.
  - [ ] 14.2 EditMode suite, with the spike and bench fence tests (baseline **424 of 424**).
  - [ ] 14.3 Re-run Record Clip Shoot, Grade Clip Shoot, Grade HUD Shoot and Photo Shoot. Diff each against its last
        run, and explain every difference. Photo Shoot's per-shot stars should not move: diff them unmasked
        (lesson 41).
  - [ ] 14.4 A normal SampleScene play session: raise, record two clips, open the card, delete one, record into the
        freed space. Read `Editor.log` for the session.
  - [ ] 14.5 Restore the editor and read it back. Commit by name with every `.meta`. Update `sprint-status.yaml` and
        check it with `yaml.safe_load` (lessons 13 and 66).

## Dev Notes

### What already exists — reuse it, do not rebuild it

| Need | Exists in | Notes |
|---|---|---|
| Single owner of native memory, eviction, `ClearAll`, fail-soft flags | `Gallery/GalleryService.cs` | The card evolves this, so keep its discipline: destroy every texture, free everything in `OnDestroy`, and resolve each reference independently. |
| A disk-ready record written as a doc comment | `Gallery/CapturedShot.cs:17-56` | `CapturedClip`'s mapping follows its shape, now in ADR-4's format. |
| Thumbnail sizing that follows the window | `GalleryConfig.ThumbnailWidthFor`, `WouldClampWidth`, `DescribeBudget` | Move these, do not copy them. |
| A grid that fits the screen, the opaque backdrop, the near-plane canvas, suppression | `Gallery/GalleryView.cs` (`LayoutGrid`, `ConfigureCanvas`, `Open`, `OnDisable`) | Every guard in it was a defect once (Task 7.3). |
| The claim rule | `RecordedClip.Claim`, `ClipRecorder.Stop` | The card claims **during** the raise. |
| Footage layout and read API | `Recording/ReplayFootage.cs` (`TickSample`, `ActorSample`, `LayerSample`, `ReadPcm`), `FootageFormat` | Stored clips read through the same API (1a). |
| Slot arithmetic | `FootageFormat.MaxBoundariesFor`, `SecondsFor`, `PcmFramesFor` | The card's unit (1b). Watch `MaxBoundariesFor(0) == 1`. |
| The frame copy without UI | `Assets/Spikes/RecordingSpike/FrameTap.cs` (fenced, reference only) | Model new game code on it (1d). |
| Camcorder corner, beeps, timecode | `UI/RecordingHud.cs`, `ClipTimecode` | The remaining line and CARD FULL go here (1g). |
| Caption colour and text | `GradeHudConfig.ColorFor`, `GradeText.Percent`, `Stars`, `MissShort` | Invariant culture, and red follows the printed text (1.19). |
| Validated config pattern | `GradingConfig`, `GradeHudConfig`, `RecordingConfig` (all problems in one message, no `OnValidate`) | Follow it for `MemoryCardConfig`. |
| Real input, 1080p, Overlay capture, legibility | `Recording/RecordClipShootRunner.cs` (virtual gamepad), `GradeHudShootRig.SelectFullHdGameView`, `UI/GradeHudShootRunner.Legibility.cs`, `tools/verification/measure_hud_legibility.py` | The proven techniques. |
| Zero-allocation proof | 1.16 Phase G in `Recording/GradeClipShootRunner.cs` (`ProfilerRecorder` "GC Allocated In Frame" with controls) | `GC.GetTotalMemory` is blind to retained arrays here (lessons 198 and 206). |
| Release-build frame cost | `Tools/Recording/Build Recording Bench (Windows)`, `tools/verification/run_recording_bench.ps1`, `recording_bench_did.py` | Only if the recording path changes (the thumbnail pass does). |
| Evidence parking | `_bmad-output/verification/1-15-record-a-clip/close-a1/swap_rig_dir.py`, `_bmad-output/verification/1-16-grade-a-clip/tools/regress.py` | Record Clip Shoot writes into 1.15's `rig/`. |
| Log classification and compile warnings | `_bmad-output/verification/1-16-grade-a-clip/close-a1/log_levels.py`, `compile_warnings.py` | Lessons 212 and 213. |

### Architecture compliance

- **§Clip Recording** (`game-architecture.md:212-324`): the data flow is `… → ClipRecorder.Stop → ClipGrader →
  ClipRecordedChannel.Raise → MemoryCardService stores the clip, and GradeHud shows its grade` (`:238-240`).
  `MemoryCardService` is the single owner of footage and thumbnails (`:226-228`).
- **ADR-4** (`:441-621`, Accepted): replay data on the card, PCM in a pooled or preallocated buffer, capacity as
  Σ repeats ÷ 30, the disk seam as track + WAV per clip with per-clip rate and channels and a content version, and
  thumbnails by the GPU copy (`:503-518, :576-578`).
- **Dependency direction** (`:921-954`):
  - `Recording` never references `Gallery`, `UI` or `PhotoMode`; the budget is a `Recording` interface (1f);
  - `Gallery → Recording` (channel, claim, store, `IClipBudget`, the shared `RecordingConfig`);
  - `Gallery → PhotoMode` (`SetRaiseSuppressed`) and `Gallery → Player` (`SetInputSuppressed`);
  - a new read-only `Gallery → UI` for the caption palette (`GradeHudConfig`), unless 1h lifts the rule into
    `Grading`;
  - `UI → Recording` is read-only, which covers the remaining time and CARD FULL. Add the card's directions to
    §Architectural Boundaries (Task 13).
- **AR4:** channels for cross-system traffic. Subscribe in `OnEnable` and unsubscribe in `OnDisable`.
- **AR5:** namespaces `CameraGame.Gallery`, `CameraGame.Recording` and `CameraGame.UI`, in one `CameraGame.asmdef` (plus
  URP if 1e adopts it). Editor tools go in `CameraGame.Editor`, tests in `CameraGame.Tests.EditMode`. Rig runners live
  in the runtime assembly behind `#if UNITY_EDITOR`.
- **AR6:** an in-memory card capped in seconds, with a disk-ready seam. No disk I/O.
- **NFR3:** footage bounded by capacity. No Instantiate or Destroy in gameplay loops: thumbnails are created per clip
  (not per frame) and destroyed on delete.
- **Error handling:** fail-soft. A missing critical reference is one `Error` and that piece stands down; a recoverable
  problem is one `Warn`; nothing throws into `Update` or `LateUpdate`; nothing is shown to the player except CARD FULL,
  which is a game state.
- **Logging:** Info milestones per stored or deleted clip. Nothing per frame except a guarded `Debug_`.
- **Configuration:** every tunable lives in `MemoryCardConfig`. Invariants stay in `FootageFormat`.

### The genuinely new risks

1. **Memory that looks bounded and is not.** `Bytes` is the recorded size, not what is held (fact 2). Prove the bound
   with the store's own accounting **and** an instrument that a retained 23 MB array visibly moves.
2. **The card's own rigs and the old rigs fill the card** (fact 13). A refused press looks like a broken rig. Empty
   the card between scenarios, and prove each rig still records what it thinks it records. Those rigs also read
   `RecordedClip.Footage` **after** the card has stored and disposed it. `TickAt` then throws, or a check prints
   "skipped" and looks like a pass. Re-point them to the card's copy (Task 12.1).
3. **Compaction corrupting a later clip.** Moving segments must never change what a clip reads. Phase D compares
   before and after, sample by sample.
4. **Float seconds drifting the card.** The card counts integer slots (1b).
5. **A thumbnail of the wrong thing:** an empty street, the viewfinder UI, the wrong colour space, or rows upside down.
   Phase 0's control, Phase H's contact sheet and the SSIM check cover these.
6. **A full card that logs, beeps or half-records.** Refuse before the backend (fact 6), log nothing per press, and
   count the beeps.
7. **The rename breaking references.** Grep every type name before and after, and keep the `.meta` GUIDs.
8. **The view's text and colours change under tonemapping** (Screen Space – Camera composites before
   post-processing). Measure as rendered.

### Project Structure Notes

- **New:**
  - `Assets/Scripts/Recording/` gets the footage store, `IClipBudget` and the thumbnail pass;
  - `Assets/Scripts/Gallery/` gets `MemoryCardConfig.cs` and `CapturedClip.cs`;
  - `Assets/Data/Gallery/MemoryCardConfig.asset` (hand-written YAML, LF);
  - the rig: `Assets/Scripts/Editor/MemoryCardShootRig.cs` plus a runner behind `#if UNITY_EDITOR`;
  - tests in `Assets/Tests/EditMode/`.
- **Renamed with `git mv` of `.cs` and `.cs.meta`:**
  - `Gallery/GalleryService.cs` (LF) → `MemoryCardService.cs`;
  - `Gallery/GalleryView.cs` (CRLF in the working tree) → `MemoryCardView.cs`.
- **Modified:**
  - `Recording/ClipRecorder.cs`, `RecordedClip.cs`, `StopReason.cs`, `IFootage.cs`, `ReplayFootage.cs`,
    `RecordingConfig.cs` and `FootageFormat.cs` (all CRLF), plus `IClipBackend.cs` and `ReplayBackend.cs` for the
    slot limit (1b);
  - `UI/RecordingHud.cs` (CRLF);
  - `Gallery/GalleryInput.cs` (LF);
  - `Core/GameConstants.cs` (LF);
  - `Assets/Input/InputSystem_Actions.inputactions` (LF);
  - `Assets/Scripts/CameraGame.asmdef` (if 1e adopts URP);
  - `Assets/Scenes/SampleScene.unity` (CRLF);
  - the rigs in Task 12: `Recording/RecordClipShootRunner.cs`, `Recording/GradeClipShootRunner.cs`,
    `UI/GradeHudShootRunner.cs`, `Editor/GradeHudShootRig.cs` and `Editor/GradeClipShootRig.cs`;
  - `game-architecture.md`, `deferred-work.md` and `sprint-status.yaml` (CRLF in the working tree).
- **Retired, if 1h holds:**
  - `Gallery/CapturedShot.cs`, `Gallery/GalleryConfig.cs` and `Assets/Data/Gallery/GalleryConfig.asset`, with
    `GalleryConfigTests.cs` moving to the card config;
  - `Gallery/GalleryShootRunner.cs` and `Editor/GalleryShootRig.cs`.
  - Delete each with its `.meta`, and grep for leftovers.
- **Line endings:** keep each file's own (lesson 65). The Edit tool does this. A Python edit must read with
  `newline=''` and must never feed already-CRLF text through a `\n` conversion (lesson 97). `git ls-files --eol` is the
  truth.
- **Staging:** stage by name with every `.meta`. `git add -A <paths>` is refused (lesson 172).

### Previous Story Intelligence

- **1.16 (the grade this story stores).**
  - The grade is complete before the raise. It is pure data, so it outlives the footage.
  - The clip readout yields only to `RaiseSuppressed`. That is why the card view must keep suppressing, or the readout
    sits over the view.
  - `ClipGrader` is internal: only this assembly's rig may hold it.
  - The zero-allocation instrument is `ProfilerRecorder` "GC Allocated In Frame" with string, object and busy-wait
    controls. `GC.GetTotalMemory` and `GetMonoUsedSizeLong` read 0 B for retained strings and `byte[]`.
  - Four human checks are still open, and they are 1.16's: the ladder, the readout's "why", the hide rule and the
    visible peak. A glimpse earning 2★ is flagged for Alexv. Card cells will show that 2★ too; do not re-tune it here.
- **1.15 (the recorder).**
  - Ticks are taken at `LateUpdate` 10000. The channel is raised once per clip, after `Stopped`. The claim rule.
  - Two preallocated blocks; a device change re-prepares on the next press, at 2.4–5.9 ms per block.
  - The rig traps:
    - `execute_code` runs outside the player loop, so `Begin` and `Stop` in one call make a 1-tick clip (lesson 179);
    - the editor renders Main Camera more than once a frame (lesson 164);
    - Send Messages reaches disabled components (lesson 174);
    - Unity's `==` on a destroyed object skips plain-C# cleanup, so use `ReferenceEquals` for that (lesson 176);
    - procedural clips go silent after `AudioSettings.Reset` (lesson 167);
    - toggling Disable Unity Audio crashed the editor (lesson 182).
  - Its four human checks (look and beeps, desktop 60 FPS, sound, feel) stay 1.15's.
- **1.11 (the gallery this evolves).**
  - The route-(c) argument: the listener makes and owns its own picture.
  - Thumbnails follow the window.
  - Show first, fill second; the near-clip trap (2.1 review); the opaque backdrop forest; the `OnValidate` trap.
  - Alexv noted on 2026-07-30: "you cannot select a photograph or view it full-screen". This story adds selection;
    full-screen viewing is 1.18's playback.
  - Whether a miss should be stored was left with Alexv and never ruled on. The card stores every clip (AC1). Say so in
    the record.
- **1.19 and 1.12 (the readouts).**
  - Red means "earned nothing", so CARD FULL should not borrow it.
  - Legibility is measured at 1080p on rendered pixels with glyph masks, text against outline ≥ 4.5:1 (lessons 139,
    145 and 147).
  - The uGUI `Outline` is limited to 2 px (diagonal copies).
  - Pin the invariant culture: fr-FR prints U+066A, and Mono rounds midpoints (lessons 137 and 152).
  - Capture the settled frame, not the flash.
- **2.1 and 2.2:**
  - after a play-mode rig the MCP bridge drops for 10–60 s, so call `refresh_unity(wait_for_ready)` (lesson 59);
  - "No Unity Editor instances found" means the call was dropped, so re-send it (lesson 128);
  - an `execute_code` over ~30 s keeps running after the client times out (lesson 122);
  - `Awake` runs on disabled components (lesson 69);
  - a merged master can raise the "modified externally" modal (lesson 99).

### Inherited context you should not rediscover

- In code the raised state is `CameraMode.Photo`, not "Viewfinder". Do not rename it.
- `GalleryView.Open` is a no-op while the camera is raised, so the card view can never be open while recording, and a
  delete can never race a recording (lesson 205).
- The drunk is the same pooled instance every cycle, so `EventManager` keeps a destroyed actor and never respawns
  (lesson 204).
- The grade-rig vantage (26, y, 13) is clear of pines for route x 22–29 (lesson 197). The 1.15 vantage (32, 8) has a
  trunk on the line.
- A 1080p `CaptureScreenshotAsTexture` grab makes that frame slow. Keep grabs at least 0.3 s away from stop and start
  thresholds (lesson 199).
- `loop/runtime/approvals.txt` is readable only with the Read tool (lesson 162). Nothing in this story needs an
  approval, and nothing in it gates another unit (`dependencies.yaml` has no gate on 1-17).
- The PhysX "triangles greater than 500 units" warnings at scene open are baseline (lesson 211).

### Git Intelligence

- Baseline `06342e3` (the loop's lessons from 1.16), on top of the 1.16 merge `7a77b70`. The last ten commits are
  Story 1.16: `ClipGrade`, `ClipGradeBuilder`, `ClipGrader`, the HUD's clip readout, the Grade Clip rig and the sweep.
- `GalleryService.cs`, `GalleryView.cs`, `CapturedShot.cs` and `GalleryConfig.cs` were last changed by the Story 2.1
  review (near clip) and Story 1.12 (`GradeText`). `RecordingHud.cs` and the recorder files date from 1.15, and
  `ClipRecorder.cs` and `RecordedClip.cs` from 1.16.
- This create phase changed only this file and `sprint-status.yaml`.

### Latest technical information (checked 2026-11-15)

- Unity 6000.3.8f1, URP 17.3.0 (Render Graph), Input System 1.18.0, MCP for Unity 10.3.0. `gcIncremental` is on.
- **New API, if 1d and 1e hold:** `RenderPipelineManager.beginCameraRendering`,
  `UniversalAdditionalCameraData.scriptableRenderer.EnqueuePass`, a `ScriptableRenderPass` with
  `RecordRenderGraph` and a blit, `RTHandles.Alloc(RenderTexture)` and `AsyncGPUReadback`. All of them run in the
  spike on this machine (`FrameTap.cs`). Check member existence with `unity_reflect`, because `unity_docs` returned
  empty bodies for several pages on 2026-11-15 (lesson 161).
- **Toolbox:**
  - **use:**
    - `unity:validate-urp-render-graph-renderer-feature` (Task 4);
    - `unity:ui` then `unity:ui-ugui` (Tasks 6 and 7);
    - `unity-perf:csharp-zero-gc` and `unity-perf:unity-performance` (Tasks 2 and 5, Phase E);
    - `unity-perf:code-standards` for a design pass before review (optional);
    - Profile Analyzer only if the bench needs a capture comparison;
  - **Unity.PerformanceTesting:** adopting it with test-helper's `[LoadScene]` in a new PlayMode assembly is a decision;
    1.15 and 1.16 did without it. `[GameViewResolution]` writes editor preferences, so use the reflection preset;
  - **not to be used:**
    - InstantReplay (ADR-4 chose replay);
    - Cinemachine, KinoGlitch and UnityHFSM (nothing here needs them);
    - Graphy, which is never shipped switched on (a bench-only read is fine).

### Verification plan: what would convince a sceptic

| Claim | Evidence | How judged |
|---|---|---|
| Every clip lands on the card with all its fields (AC1) | Phase A: one clip per stop route, with the card's record beside the recorder's log line | Field by field |
| The card owns the footage, and no press allocates (AC1, AC5) | `StoragesCreated` stays 2 (+2 only at a device change, never on a press) and `LastClip` is null after each claim; GC Allocated on press frames | Counts; bytes with controls |
| Capacity in seconds, never exceeded (AC2) | Tests 10.1; Phase B's full card at the shipped 120 s | Σ slots ≤ 3,600 after every step |
| The viewfinder shows the time left and CARD FULL (AC2) | Phase B captures at each state; remaining against the true free time; legibility numbers | Look; numbers. **Human check** for the camcorder read |
| A full card refuses Record cleanly (AC2, AC5) | Phase B: refused press, with clip count, beep count, REC and log lines | Counts, and an empty log |
| Every clip is reachable and deletable (AC3) | Phase C grabs with more clips than fit; the selection window; the two-press delete | Look. **Human check** for the feel |
| Delete frees seconds and memory at once, with no leak (AC3) | Phase D: 60+ cycles of store bytes, slots, texture and RT counts, and the proven instrument | Flat series with a working control |
| Compaction never corrupts a clip (AC3) | Phase D read-back identity, sample by sample | Exact equality |
| The thumbnail shows the clip (AC1) | Phase H contact sheet with grades drawn on; the UI and colour controls | Look. **Human check** |
| Disk-ready (AC4) | The written mapping; tests 10.5 | Read the mapping against ADR-4 |
| Cheap and fail-soft (AC5) | Phase E (ms, 0 B per frame, bench DiD mean and median); Phase F (one line per fault) | Numbers, not thresholds; console and `Editor.log` |
| No regression (AC6) | EditMode count, fence tests, re-run rigs diffed, restore read-back | Same results, or differences explained |

### Human checks to queue (never self-certify)

Queue these at the **end of dev**, after every automated part, each with exact files and your own measured read.
This create phase opens none: nothing is built yet.

1. **AC2, the viewfinder.** Does the remaining counter and CARD FULL read like a 2000s camcorder, at a glance, over
   bright sky and dark walls? Is the refused press's flash enough feedback? *Evidence:* Phase B captures and a short
   rig video of the card filling.
2. **AC3, the card view and delete.** Can he find a clip, see its stars and length, and delete it without fear of
   deleting the wrong one? Is two presses right, or tedious? *Evidence:* Phase C captures and a rig video of a delete.
3. **AC1, the thumbnails.** Does each thumbnail read as "my clip of him"? *Evidence:* Phase H's contact sheet.
4. **FR27, the design feel (optional).** Does a 2-minute card make keeping footage feel like a choice? This is really
   the slice playtest's question, so ask it only as a note.

### Project Context Rules

- MCP for Unity is the only editor channel. Never use `unity:unity-cli` or Pipeline commands to edit, run or test.
- After every script change: `refresh_unity`, then `read_console` (NFR5). Before a rig run, read the values back
  through the editor.
- Evidence goes to `_bmad-output/verification/1-17-the-memory-card/`, never `Temp/`. Builds go to
  `Builds/1-17-the-memory-card/`.
- Restore everything a rig or build changes, and read it back.
- Commit on `loop/1-17-the-memory-card` only, staging by name, with `Co-Authored-By: Claude Opus 5.5
  <noreply@anthropic.com>`. Never switch branches, merge, rebase, reset, stash, clean or push.
- Perceptual ACs become `human_checks`. A measured shortfall is fixed, never queued.
- Decisions are made by evidence and recorded (question, options, choice, evidence, cost to reverse).
- Do not edit `loop/runtime/`.

### References

- `_bmad-output/planning-artifacts/epics.md`:
  - Story 1.17 (`:584-613`, the ACs) and Story 1.18 (`:615-639`, what comes next);
  - FR8, FR27, FR28, NFR1–NFR3, NFR5, NFR8, AR4, AR6.
- `_bmad-output/planning-artifacts/game-architecture.md`:
  - §Clip Recording (`:212-324`);
  - §Data Persistence (`:343-351`);
  - ADR-4 (`:441-621`, especially `:503-518, :553-554, :576-578`);
  - §Cross-cutting Concerns (error handling `:719-743`, logging `:745-767`, configuration `:769-778`, events
    `:780-799`, Recordability `:801-817`);
  - §Directory Structure (`:842-883`);
  - §Architectural Boundaries (`:921-954`).
- `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md`: `:139`, `:183`, `:214`, `:241`, `:295-296`,
  `:326`, `:353`; `decision-log.md:94, :106`.
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-11-10.md`: `:69-72, :102, :127, :170-171, :181-186,
  :206-220, :348-378`.
- `_bmad-output/implementation-artifacts/`:
  - `1-16-grade-a-clip.md`, `1-15-record-a-clip.md` and `1-11-minimal-gallery.md` (their Dev Agent Records and
    reviews);
  - `1-14-decide-how-the-game-records-video.md` (Task 1a, the frame tap; the review patches `:651-660`);
  - `1-19-a-shot-that-earns-nothing-reads-red.md` and `1-12-grade-feedback-hud.md` (legibility).
- `_bmad-output/implementation-artifacts/deferred-work.md`: `:144, :147, :345, :360, :364, :373, :374, :375, :378,
  :383`, and the 1.11 items on selection and unreachable shots.
- `loop/lessons.md`: every section. **"Lesson N" in this file means line N of `loop/lessons.md`.**
- **Code:**
  - `Recording/ClipRecorder.cs`, `RecordedClip.cs`, `ReplayBackend.cs`, `ReplayFootage.cs`, `IFootage.cs`,
    `IClipBackend.cs`, `StopReason.cs`, `FootageFormat.cs`, `RecordingConfig.cs`, `ClipRecordedChannel.cs` and
    `ListenerTap.cs`;
  - `Gallery/GalleryService.cs`, `GalleryView.cs`, `GalleryInput.cs`, `CapturedShot.cs` and `GalleryConfig.cs`;
  - `UI/RecordingHud.cs`, `GradeHud.cs` and `GradeHudConfig.cs`;
  - `Grading/ClipGrade.cs` and `GradeText.cs`;
  - `PhotoMode/PhotoModeController.cs`;
  - `Spikes/RecordingSpike/FrameTap.cs`;
  - the rigs `Recording/RecordClipShootRunner.cs`, `Recording/GradeClipShootRunner.cs` and
    `UI/GradeHudShootRunner*.cs`;
  - the tests `Tests/EditMode/RecordingTests.cs`, `GalleryConfigTests.cs` and `ClipGradeTests.cs`.

## Dev Agent Record

### Agent Model Used

### Debug Log References

### Decisions

### Completion Notes List

### File List

## Change Log

- 2026-11-15: Story context created by the loop CREATE phase (status ready-for-dev).
- 2026-11-15: An independent pass checked about 60 of the file's claims against the code. Its 13 findings are applied:
  - the slot limit through float (re-measured);
  - rigs reading footage the card has disposed;
  - the rounding rules that could not all hold (re-computed);
  - the pool re-shape on a device change;
  - `Gallery → UI` for the caption palette;
  - STBY outside `RecCluster`;
  - the store's per-tick caps;
  - S9, not Phase H;
  - a refusal signal for the HUD;
  - resizing the card for rigs;
  - the final tick's thumbnail;
  - lesson 80's wording;
  - the double-tonemap hypothesis.
