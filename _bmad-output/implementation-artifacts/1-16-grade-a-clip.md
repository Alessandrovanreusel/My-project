---
baseline_commit: 1601e7eeac64fb6fe8c44d92acda7d1177d978d1
---

# Story 1.16: Grade a clip

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-10 (`sprint-change-proposal-2026-11-10.md`). It carries the intent of superseded Story 1.13 and depends
on Story 1.15 (`loop/dependencies.yaml`: `1-16-grade-a-clip: [1-15-record-a-clip]`), which the loop merged with only
Alexv's perceptual and desktop checks pending. Context was created on 2026-11-15 by the loop CREATE phase.*

*Since Story 1.15, Record (LMB / RT) starts and stops a clip, and **nothing grades it**: the grade HUD and the gallery
still listen only to `ShotCapturedChannel`, which no player action raises any more (`deferred-work.md:360`). This
story closes that gap. While a clip records, the existing per-frame grader runs on every tick. When it stops, the
samples are blended with **peak coverage** into a `ClipGrade`, the grade travels on the clip's own channel, and the
HUD shows it.*

*What this story is **not**: it does not store clips or show CARD FULL (1.17), play them back (1.18), export them
(Epic 9), or change the per-frame grader. The subject gate and composition are **unchanged and not re-implemented**
(AC1). If something in them looks wrong while you measure clips, write it down in `deferred-work.md`. Do not fix
it here, because it would move every photo grade too.*

> **The starting point in twelve facts.** Each was read from a file on 2026-11-15. Re-check any that matter before
> relying on it.
>
> 1. **The grader is a pure static function, and its gate and scoring maths have not changed since Story 1.10.**
>    (1.11 and 1.12 added `SubjectId` and `PeakOffset` to the payload, and 2.1 edited one comment.)
>    `ShotGrader.Grade(Camera, ISubject, GradingConfig, out GradeDetail)` (`ShotGrader.cs:235`) runs the gate in
>    order (`NoCamera`, `NoSubject`, `NoConfig`, `NoViewport`, `DegenerateBounds`, `OutsideFrustum`, `BehindCamera`,
>    `TooSmall`, `Occluded`) and then scores `Composition` (`:389-450`) and `Timing` (`:462-483`).
>    `ShotGrade.Scored` multiplies them (`ShotGrade.cs:229-236`). For a clip, the per-tick **Timing01 is the wrong
>    axis**: "Timing is replaced by peak coverage at the clip level" (`sprint-change-proposal-2026-11-10.md:101`).
>    A clip uses the gate (`Counted` / `MissReason`) and `Composition01` from each sample, and computes peak coverage
>    itself.
> 2. **`ShotGrader` uses static scratch buffers** (`FrustumPlanes`, `Corners`, `CornerScreen`, `:167-173`), so it
>    is **main thread only**. Its cost was measured once, at **0.0072 ms per call** against 16,739 colliders on
>    2026-07-26 (`ShotGrader.cs:158-160`). That was before ADR-5's rescale and before Story 2.2 replaced the scatter
>    colliders and added 1,781 edge rocks and the seal. It has never been re-measured (`deferred-work.md:132`).
> 3. **The recorder samples in `LateUpdate` at execution order 10000** (`ClipRecorder.cs:51-57, 282-301`), after
>    `ThirdPersonCamera` writes the camera's final pose (-100) and after `PhotoModeController.Update` writes the FOV.
>    `ReplayBackend.Sample` takes a **30 Hz tick with a repeat count** only when a 1/30-s boundary has passed
>    (`ReplayBackend.cs:180-214`). It returns `void` today, so a caller cannot tell whether a tick was taken. `End()`
>    adds a start tick to a clip that stopped before its first sample (`:323`). `Stop` runs `End` → Info log →
>    `Stopped` (the beep) → `ClipRecordedChannel.Raise` → claim (`ClipRecorder.cs:250-280`).
> 4. **`RecordedClip` has no grade yet.** Its own comment says "Story 1.16 adds the grade to it"
>    (`RecordedClip.cs:7-8`). It is built in two places: `ClipRecorder.cs:257` and `RecordingTests.cs:299`.
> 5. **`ISubject` cannot say how long the peak window is.** It has `Bounds`, `IsAtPeak`, `TimeToPeak`, `PeakOffset`
>    and `SubjectId` (`Events/ISubject.cs`). `EventActor` is its only implementer (no test fakes). The peak's
>    length lives in `EventDefinition.GetPhase(EventPhase.Peak).duration`, which `EventActor.PeakOffset` already
>    reads, with a null guard (`EventActor.cs:189-193`). The architecture says peak coverage is "read from
>    `ISubject`" (`game-architecture.md:222-225`).
> 6. **The Town Drunk's lifecycle** (`Assets/Data/Events/TownDrunk.asset`) is Spawn 3 s → Build 10 s → **Peak 1.5 s**
>    → WindDown 6 s → Despawn 4 s, with a 2 s respawn delay: about 26.5 s per cycle. The peak is **45 ticks** at
>    30 Hz. The pooled drunk is the **same instance** on every cycle, so a 120-s clip can hold up to about five
>    lifecycles of one actor, told apart only by `EventActor.LifecycleSerial` (`EventActor.cs:215`, taken in `Begin`
>    at `:509`). `LifecycleSerial` is not on `ISubject`.
> 7. **The HUD hears only photos.** `GradeHud` subscribes to `ShotCapturedChannel` (`GradeHud.cs:206-222`) and
>    formats a `ShotGrade`. The format hard-codes "timing" (`:327-328`, `NotMeasuredAxes` at `:344`), and its "why"
>    line is `GradeText.TimingAdvice(ShotGrade)` (`GradeText.cs:152-193`). What a clip can reuse as it is:
>    `GradeHudConfig.ColorFor(bool counted, bool isMiss, int stars, float percent01)` (`GradeHudConfig.cs:248-254`,
>    built by Story 1.19 for this story), `GradeText.Percent`, `PrintsZero`, `Stars` and `MissLong`.
> 8. **⚠️ The HUD hides the moment the camera is down.** `hideOnCameraLowered` ships ON (`GradeHudConfig.cs:40-48`;
>    `Update`, `GradeHud.cs:451-455`). Lowering the camera is the most natural way to end a clip
>    (`StopReason.CameraLowered`), and by the time the channel is raised the camera is already down
>    (`PhotoModeController.SetMode`, `:645-650`, stops *after* `Mode` changes). Without a change, the grade of every
>    clip stopped by lowering is put on screen and taken off in the same frame. `deferred-work.md:202` calls this
>    switch "a feel decision nobody has played yet".
> 9. **`Capture()` (the still shutter) and `ShotCapturedChannel` are still used.** No input reaches them since 1.15,
>    but three regression rigs pull `Capture()` directly: `PhotoShootRunner`, `GalleryShootRunner` and
>    `GradeHudShootRunner*`. `GalleryService` stores photos from `ShotCapturedChannel` (`GalleryService.cs:144`).
>    `deferred-work.md:360` hands this story the decision on both.
> 10. **`GradingConfig` is the validated config pattern.** Every value goes through a `Safe*` accessor (named
>     `Default*` constants shared by the initialiser and the fallback, `GradingConfig.cs:187-200`), with resolvers
>     for fields that must agree with each other (`:301-324`). `TryGetConfigProblem` (`:345-633`) reports only the
>     **first** problem, and `deferred-work.md:129` says to accumulate all of them "when the config surface next
>     grows", which this story does. It also has an `OnValidate` that rewrites fields through `Safe*` and "rewrites
>     hand-authored YAML the moment the asset is selected" (`:670-702`). Story 1.13 said not to add new fields to
>     that kind of repair (`1-13-soften-the-timing-multiplier.md:87`).
> 11. **Occlusion rays do not see the town's pines** (lessons 67 and 169; `deferred-work.md:162-180`). A rig that
>     wants an "occluded" miss must hide the drunk behind a **building** and confirm it by rendering. A pine
>     reads 100 % line of sight.
> 12. **Rigs build these components in isolated worlds**, so a new required field that logs an Error adds an error
>     line to every run (lesson 159):
>     - `PhotoModeController`: `PhotoShootRig.cs:375`, `GalleryShootRig.cs:218`, `GradeHudShootRunner.cs:788`;
>     - `ClipRecorder`: `RecordClipShootRunner.MakeRecorder` (`:1291`);
>     - `GradeHud`: `GradeHudShootRunner.BuildThrowawayHud` (`:1496`).

## Story

As a player,
I want each clip scored on how well I kept the subject framed and whether I caught the peak,
So that filming the moment is still a skill, not just pressing record and walking away.

## Acceptance Criteria

ACs 1–4 are verbatim from `epics.md` (Story 1.16). ACs 5–6 were added by this create phase. They restate the
cross-cutting NFRs where this story bites (NFR1, NFR3, NFR5, NFR8) and CLAUDE.md's restore rule, as the epic asks
("restated in acceptance criteria where they bite"). They add requirements and weaken none. The decision is in the
result file.

1. **The per-frame grader is sampled throughout the recording.**
   **Given** a clip is being recorded,
   **When** frames are rendered,
   **Then** the existing per-frame grader (`ShotGrader`, Stories 1.9–1.10) is sampled throughout the recording, with
   its subject gate and composition unchanged and not re-implemented (FR4–FR6).
   - "Unchanged and not re-implemented" is **measurable**: every sample is a call to `ShotGrader.Grade` itself, and
     on a frame where both run, a still `Capture()` and the clip's sample agree exactly on the gate and on
     composition (Task 8, Phase 0).
   - "Throughout the recording" means every footage tick, the start tick included, so the grade describes exactly
     the frames the clip holds (Task 1b).

2. **Peak coverage and framing, blended into a `ClipGrade`, with the blend decided by measuring.**
   **Given** a finished clip,
   **When** it is graded,
   **Then** its grade rewards **peak coverage** (how much of the event's peak window the clip contains with the
   subject framed) and how well the subject was framed while recorded, blended into a 0–100% → 1–5★ `ClipGrade` with
   a per-axis breakdown (FR4, FR7)
   **And** the blend, every threshold, and whether footage without the subject lowers the grade live in
   `GradingConfig` and are decided by measuring recorded clips, as Story 1.10 decided prominence.
   - FR7: "a clip earns **full marks** when it contains the event's **whole** peak window with the subject framed,
     and partial credit for partial coverage". A clip that holds the whole window, framed throughout, must read
     **100 % coverage**. Tick quantisation must not cost it a point (Task 1c).
   - "Decided by measuring" means the candidate table, the criteria and the choice are written in the Dev Agent
     Record with their evidence (Task 1d). Alexv's judgement of the result is a **human check**.

3. **A well-framed clip that missed the peak is worth something, and catching it still wins clearly.**
   **Given** a well-framed clip that missed the peak,
   **When** it is graded,
   **Then** it still scores above zero. This is Alexv's Story 1.12 feedback, carried over from superseded Story
   1.13: *"it should not be a big deal the timing for me. I think it's too extreme to give 0"*
   **And** a clip that caught the peak with equal framing still wins by a wide margin.
   - "Above zero" is read as the player reads it: the readout prints at least "1 %" and is not red. The red follows
     the printed text (`GradeText.PrintsZero`, Story 1.19).
   - "A wide margin" is turned into a number before the measurement is looked at (Task 1d, criterion C2), so the
     choice cannot be fitted to the result.

4. **A clip that never caught him is a miss with a reason, and the HUD shows every clip's grade.**
   **Given** a clip in which the subject never passes the gate,
   **When** it is graded,
   **Then** it is a miss with a reason (no subject / too small / occluded)
   **And** the grade HUD (Story 1.12) shows the clip's stars and breakdown when recording stops (NFR10).
   - "When recording stops" covers **every** stop route: Record pressed again, the camera lowered, the length limit,
     and the audio device changing. The readout must stay up for its hold after a stop by lowering (fact 8, Task
     1g).
   - "Shows the clip's stars and breakdown" keeps the Story 1.12 and 1.19 rules:
     - four distinct states (not graded / miss / counted / a zeroed default);
     - dashes, never zeroes, on a miss;
     - colour only from `GradeHudConfig.ColorFor`;
     - every percentage through `GradeText.Percent`, in the invariant culture.
   - Whether a player *understands why* from the readout is perceptual (NFR10), so it becomes a human check.

5. **Cheap, bounded, independent, fail-soft** *(added: NFR1, NFR3, NFR5, NFR8)*.
   **Given** grading runs on every tick of every recording,
   **When** clips are recorded one after another: ten or more in a row, one spanning the drunk's despawn and
   respawn, one stopped in the frame it started, and one stopped by the length limit,
   **Then** grading allocates nothing on the managed heap per frame, its cost per tick is measured (and the
   recorder-plus-grader frame cost in the release-build bench), and each clip's grade depends only on its own ticks
   **And** a missing `GradingConfig`, camera or `EventManager`, a bad config value, or a subject that despawns or is
   destroyed mid-clip degrades with **one** clear log line. The clip is still recorded, announced and kept. Its grade
   says "not graded", or names the real miss reason. Nothing throws into `LateUpdate`, and the console stays clean
   in normal play.

6. **No regression, project restored** *(added: CLAUDE.md, NFR5)*.
   **Given** the change is complete,
   **When** the EditMode suite, the spike and bench fence tests and the existing rigs (Record Clip Shoot, Grade HUD
   Shoot, Photo Shoot, Gallery Shoot) run,
   **Then** they pass as before, or every difference is explained. In particular the per-shot verdicts in Photo
   Shoot's `shots.txt` are unchanged, because the per-frame grader is
   **And** the editor, scene, project settings, Game View size, time scale and every config value are restored, as are
   the files a Windows build re-serialises (lessons, 1-14).

## Tasks / Subtasks

- [x] **Task 1 — Settle the nine decisions that GATE THE STORY (AC1–AC5). DO THESE FIRST.**

  Each has a recommendation and the evidence that should settle it. Record every choice **with its reason and its
  evidence path** in the Dev Agent Record and in the result file's `decisions`, including the ones where you follow
  the recommendation. Alexv reviews them in one batch and may veto. Where a choice can be settled by measuring,
  measure, as Story 1.10 did for prominence.

  - [x] **1a. Where clip grading lives, who drives it, and the dependency direction.** (AC1, AC2. Blocks
        everything.)
        - **Recommended:** a plain C# **`ClipGrader`** class in `Assets/Scripts/Recording/`, owned and driven by
          `ClipRecorder` exactly as it owns `ReplayBackend`. The architecture already names it and places it there
          (`game-architecture.md:222-225, :819`).
          - `ClipRecorder` gains `[SerializeField] private GradingConfig gradingConfig;` (the same
            `Assets/Data/Grading/GradingConfig.asset` that `PhotoModeController` uses). Grading runs only when it
            is set; its readiness flag is independent of recording's.
          - `ClipRecorder.LateUpdate` calls the grader **right after** `_backend.Sample(...)`, on the frames where a
            tick was taken. Make `Sample` return the repeat count it emitted (0 when no tick was due).
            `IClipBackend.Sample` changes from `void` to `int`; `ReplayBackend` is its only implementer, and call
            sites that discard the result still compile.
          - `ClipRecorder.Stop` finishes the grade **before** `ClipRecordedChannel.Raise` (task 4.3), so the
            payload carries it. This is the architecture's data flow: "`ClipRecorder.Stop` → `ClipGrader` →
            `ClipRecordedChannel.Raise`" (`:237-239`).
        - **The pure part goes in `Assets/Scripts/Grading/`:** the `ClipGrade` struct, and the aggregation and blend
          as pure functions over plain numbers, with no Unity objects. Then EditMode tests pin the blend, and the
          measuring sweep (Task 8, Phase W) can call **the real code** instead of a Python copy of it.
        - **Dependency:** `Recording → Grading` is sanctioned by §Clip Recording (`game-architecture.md:241`), but
          §Architectural Boundaries lists only `Events` and `Core` (`:905`). Add `Grading` there (Task 9). `Grading`
          never references `Recording`.
        - *Alternative B: a `ClipGrader` MonoBehaviour in `Grading`* that listens to `ClipRecorder.Started` and
          `Stopped` and samples in its own `LateUpdate`. Rejected for three reasons:
          - it reverses the dependency (`Grading → Recording`);
          - its `LateUpdate` must run after the backend's tick on every frame, which is a second execution order to
            keep right;
          - its grade must exist before the channel raise that happens inside `Stop`.
        - *Alternative C: `PhotoModeController` samples*, since it owns `GradeBestSubject`. Rejected: it runs in
          `Update`, before the camera's final pose (fact 3), so it would grade last frame's camera.
  - [x] **1b. What one sample is, and when it is taken.** (AC1.)
        - **Recommended: one sample per footage tick, weighted by that tick's repeat count.** The grade then
          describes exactly the frames the footage holds and that 1.18 will play back.
          - Check that the seconds the grader saw (the sum of graded repeats ÷ 30) equal `IFootage.Seconds` for
            every clip.
          - When `End()` adds a start tick (`ReplayBackend.cs:323`), the grader must sample it too, so a clip
            stopped in its start frame still has one graded tick.
        - **Each tick grades every live actor.** Read `EventManager.ActiveActors` live, and skip `null` and
          `!isActiveAndEnabled` actors, as `GradeBestSubject` does (`PhotoModeController.cs:589-599`). For each,
          call `ShotGrader.Grade(recordCamera, actor, gradingConfig, out GradeDetail d)`.
          - Use the **recorder's** camera, which `PhotoModeController.Awake` already checks is its own
            (`:182-184`).
          - Keep from the `ShotGrade`: `Counted`, `MissReason`, `Composition01` and `Subject01`.
          - Read live from the actor: `IsAtPeak` (and the peak length, Task 1c).
          - The key is **(actor, `LifecycleSerial`)** (fact 6).
          - Do **not** use the sample's `Percent01` or `Timing01`: that is the photo's timing, which the clip
            replaces (fact 1).
        - *Alternative:* sample every rendered frame, weighted by `unscaledDeltaTime`. Rejected: above 30 fps it
          grades frames the footage does not contain, and below 30 fps it equals per-tick sampling anyway.
        - Cost: with `maxConcurrent` 1, that is 30 `Grade` calls per second. Measure it (Task 1i) rather than trust
          fact 2's old number.
  - [x] **1c. Peak coverage, and where the peak window's length comes from.** (AC2, FR7.)
        - **Definition, recommended:** for one lifecycle, coverage = (seconds of its peak window that the clip holds
          with the subject passing the gate) ÷ (the peak window's length), clamped to [0, 1]. "Seconds held" is the
          sum of repeats ÷ 30 over ticks where `IsAtPeak` and the sample `Counted`. It rises with the covered
          seconds and reaches 1 only for the whole window.
        - **The window's length.**
          - **Recommended:** add a read-only `float PeakDuration { get; }` to `ISubject`, because the architecture
            says coverage is "read from `ISubject`" (`:222-225`). `EventActor` returns the Peak phase's duration
            with the same null guard `PeakOffset` uses (`EventActor.cs:189-193`). A non-finite or non-positive
            length reads 0, and the grader treats 0 as "this subject has no peak to cover": coverage 0, one warning
            per session. `deferred-work.md:130` notes that a NaN duration passes `EventDefinition.IsValid`.
          - Adding a member does not break the spike: it reads `EventActor` and implements nothing (fact 5,
            `SpikeHarness.cs`).
          - *Alternative:* `ClipGrader` reads `EventActor.Definition.GetPhase(EventPhase.Peak).duration` directly.
            `Recording` already reads `Events`, so this keeps `ISubject` unchanged, but it puts grading knowledge
            outside the grading seam. Choose one and record why.
        - ⚠️ **Quantisation and clocks.** Coverage must come out exactly right at the edges:
          - The 1.5-s window is 45 ticks, but the phase boundaries fall between tick boundaries, so a count can be
            one tick off.
          - The peak runs on **scaled** time (`TimeToPeak -= Time.deltaTime`), while the clip runs on **real** time
            (`ClipRecorder` header). The two agree only at `timeScale` 1.
          - So **measure** a clip that wholly contains the window, framed throughout. If it reads 97.8 %, fix it.
            Two ways: count coverage on the actor's own clock (the span of `TimeToPeak` the framed peak ticks
            cover), or allow a one-tick tolerance taken from the tick rate (an invariant, not a tunable).
          - Do not round the printed number to hide the error.
        - ⚠️ **The visible peak lags the window** (`deferred-work.md:261`). The stagger is cross-faded in over
          0.2 s, so for about the first half-second of the window the drunk still shows his walk pose.
          - Changing the window is **out of scope**: it belongs to the event, and it would move every photo grade.
          - Measure what coverage a clip of only the window's first ~0.5 s earns, look at its frames, and record the
            finding in `decisions`, with the cost of aligning the two later. Do not fix it silently.
  - [x] **1d. The blend, the floor, and footage without the subject. DECIDE BY MEASURING.** (AC2, AC3.)
        - **Recommended starting shape.** This is Story 1.13's proposal with coverage in place of timing
          (`1-13-soften-the-timing-multiplier.md`; the GDD's "designer-set floor", `gdd.md:205`):

          `percent = framing × (floor + (1 − floor) × coverage)`

          - `framing` is the repeat-weighted mean of `Composition01` over the ticks where this lifecycle passed the
            gate ("how well the subject was framed while recorded"), multiplied by a presence term,
            `1 − absenceWeight × (1 − inShot)`.
          - `inShot` is (gated seconds) ÷ (clip seconds). `absenceWeight` 0 reads the GDD's "frames that fail
            contribute nothing" (`gdd.md:203`) as *excluded*; 1 makes empty footage count fully against the grade.
        - **New `GradingConfig` fields** (the names are suggestions): a peak floor (Story 1.13 proposed 0.3 for the
          photo), an absence weight, and anything the quantisation fix needs. Every one gets a `Safe*` accessor, a
          `Default*` constant and a tooltip that quotes the evidence, as Story 1.10's do.
        - **Stars:** reuse `SafeStarScale` (5★ ≥ 0.90, 4★ ≥ 0.70, 3★ ≥ 0.45, 2★ ≥ 0.20). A separate clip ladder is a
          further decision: take it only if the measurement shows that one ladder cannot serve both.
        - **The method** (Story 1.10's, adapted to an unattended loop):
          1. Record the scenario set once with the real recorder (Task 8, Phase S), logging every tick's sample.
          2. Sweep the candidates (floor × absence weight × framing aggregate) by feeding the logged samples into
             **the real aggregator** with throwaway `GradingConfig` instances (`ScriptableObject.CreateInstance`).
             Never use a re-implementation.
          3. Choose against the criteria below, **written down before you look at the table**.
          4. Write the chosen values into the asset, `refresh_unity`, read them back, and re-run Phase S. The live
             grades must equal the sweep's numbers for the same scenarios.
          5. Record the table, the choice, the runner-up and why it lost.
        - **Criteria, derived from the ACs.** These are decision criteria, **never asserts in a test**
          (CLAUDE.md: *Do not encode expected results*):
          - **C1 (AC3, "above zero").** The well-framed clip that missed the peak prints at least "1 %" and is not
            red. With excellent framing it earns **2★ or more**: Story 1.13's floor would have put Alexv's
            `c_counted_but_zero` photo (composition 98 %, timing 0) at 29 % and 2★, and he called a hard 0 "too
            extreme".
          - **C2 (AC3, "wins by a wide margin"; epics.md:462, "timing stays the dominant axis").** With equal
            framing, the clip that caught the whole peak beats it by **at least two stars**. Across the scenario set,
            coverage moves the grade more than the difference between two *good* framings does.
          - **C3 (FR7).** The whole peak, well framed, can reach 5★. Partial coverage lands in between, and the grade
            rises with coverage.
          - **C4 (footage without him).** Take the same peak-catching clip with about 0, 5 and 20 s of aiming away
            first, and say what each candidate does to it. Two arguments pull against each other:
            - the story's "so that" ("not just pressing record and walking away") argues that a mostly empty clip
              should not score like a tight one;
            - the memory card (1.17) already charges every second of footage against its capacity, so do not punish
              length twice.
            Choose and justify.
        - *Alternatives to evaluate in the sweep:*
          - an additive blend (`a × framing + b × coverage × framing`);
          - framing averaged only over the peak's ticks when coverage > 0;
          - no presence term at all.
  - [x] **1e. Which lifecycle a clip is "of", and the miss reason.** (AC4.)
        - **Candidates:** every (actor, `LifecycleSerial`) seen live in the clip is graded on its own ticks only.
          A respawn starts a new candidate, and two lifecycles are never merged.
        - **The clip's grade is the best candidate**, by the rule `GradeBestSubject` uses
          (`PhotoModeController.cs:609-623`): a counted candidate beats any miss, then the higher percent wins, and
          the first graded wins a tie. A candidate counts if **any** of its ticks passed the gate.
        - **The miss reason when nothing counted.** **Recommended:** the reason that held for the **most clip time**
          among ticks where a subject was live, with ties broken toward the gate closest to passing (`Occluded` >
          `TooSmall` > `BehindCamera` > `OutsideFrustum`). `NoSubject` applies when no actor was ever live.
          - The AC names no subject, too small and occluded. `OutsideFrustum` ("he was outside the frame") and
            `BehindCamera` arise too, and `GradeText.MissLong` words all of them.
          - *Alternative:* the deepest gate reached on any tick. It is more "actionable", but a 0.1-s glimpse behind
            a wall would label a 10-s out-of-frame clip "something was in the way". Run both on the miss scenarios
            (Task 8, S7), read the words, and choose.
        - **Bounded:** preallocate the candidate slots. One way is to keep one live accumulator per actor slot
          (`RecordingConfig.maxActorsPerTick`) plus one "best so far", and fold a lifecycle into the best when its
          serial changes. No allocation per tick, and no list that grows with the clip's length.
  - [x] **1f. The payload: `ClipGrade`, carried on `RecordedClip`.** (AC2, AC4.)
        - **Recommended:** a `readonly struct ClipGrade` in `Assets/Scripts/Grading/`. It is pure data with no Unity
          object references, so 1.17 and Epic 9 can map it into a JSON DTO. Fields:
          - `Percent01` and `Stars`. `Stars` is a stored field, for `ShotGrade`'s reason (`ShotGrade.cs:94-101`):
            a card entry must keep the rating the player was shown.
          - `MissReason` (`GradeMiss`). Its zero value is `Unevaluated`, so `default(ClipGrade)` is never "counted".
          - `Framing01`, `PeakCoverage01` and an in-shot share.
          - `SubjectId`: empty means nobody, never a sentinel string (`ShotGrade.cs:104-132`).
          - Whatever the advice line needs: whether the window began before the clip started, ended after it
            stopped, or passed while he was out of shot.
          - Derived `Counted`, `IsMiss` and `HasSubject`; factories in the shape of `Scored`, `Missed` and
            not-graded; a `ToString()` that prints the breakdown.
          - Every normalized float goes through a `Sane`-style guard. A signed or sentinel value is stored raw, with
            the reason written beside it (`ShotGrade.cs:145-160`).
        - **`RecordedClip` gains `ClipGrade Grade { get; }`** through its constructor, set by `ClipRecorder.Stop`
          before the raise. Update both construction sites (fact 4). This matches the architecture's
          `CapturedClip.Grade` (`game-architecture.md:229`).
        - *Alternative:* a separate `ClipGradedChannel`. Rejected: two raises per clip that every listener must
          pair up, against the documented flow (`:237-239`).
  - [x] **1g. The clip readout on the HUD, and when it hides.** (AC4, NFR10. Perceptual, so a human check follows.)
        - *Toolbox: invoke `unity:ui`, then `unity:ui-ugui`, before editing `GradeHud`.*
        - **Listening.** `GradeHud` gains a second serialized channel, `ClipRecordedChannel`, cached on subscribe
          exactly as the shot channel is (`GradeHud.cs:200-222`, the 1.12 review fix). Its handler formats the
          clip's `ClipGrade`. It **must not `Claim`** the clip, because it keeps no footage. Keep the shot path
          working: the Grade HUD Shoot rig still drives it through `Capture()` (Task 1h).
        - **Format.** A `Format(ClipGrade)` beside `Format(ShotGrade)`. It keeps the four-state rule and
          every-branch-writes-every-label (`:259-340`), and takes its colour only from `config.ColorFor(counted,
          isMiss, stars, percent01)`. **Recommended wording** (Alexv judges it in a human check):
          - rating line: `★★★★☆   82 %`, or `★☆☆☆☆   MISSED`, or `NOT GRADED`;
          - axes line: `framing 92 %   ×   peak 100 %      ·      in shot 64 %`, with dashes on a miss. A clip
            constant replaces `NotMeasuredAxes`'s hard-coded "timing";
          - why line: `GradeText.MissLong` on a miss. On a counted clip, a new `GradeText.PeakAdvice(ClipGrade)`,
            in the invariant culture, chosen from the measured facts. Examples: "caught the whole stumble",
            "stopped too soon — keep rolling", "started too late — record sooner", "he stumbled out of shot — keep
            him framed". It is empty when nothing was measured. This gives the why line the axis that actually cost
            the clip (`deferred-work.md:214`).
          - Measure that each line fits on one line at 1920×1080: the labels are 34 px and 22 px at the 1920×1080
            reference.
        - **⚠️ When the readout hides (fact 8).**
          - **Recommended:** a clip readout ignores the camera's mode and yields only to a screen owner. It hides
            when `photoMode.RaiseSuppressed` turns on (the gallery's switch; reading `PhotoModeController` from UI
            is already sanctioned), and otherwise runs its full hold.
          - Keep `hideOnCameraLowered` exactly as it is for the shot readout, which only rigs drive now. Write the
            asymmetry in both the config tooltip and the class comment.
          - *Alternatives:* switch `hideOnCameraLowered` off for every readout, which brings back the readout over
            the gallery it was added to prevent; or hide only on a raised-to-lowered edge **after** the readout
            appeared, which is subtler and needs the same measurement.
          - Measure either way: a stop by lowering leaves the readout visible for its hold, and opening the gallery
            mid-hold removes it on that frame.
        - **A missing clip channel** makes the HUD blind to the player's whole verb, so `Error` is consistent with
          the missing shot channel (`:139-143`). Then `GradeHudShootRunner.BuildThrowawayHud` gains that error line
          on every run: wire the channel there, or announce the line (fact 12). Record the choice.
  - [x] **1h. The fate of `Capture()` and `ShotCapturedChannel`.** (`deferred-work.md:360`.)
        - **Recommended: keep both, unchanged and unbound.**
          - Three regression rigs photograph the grader through `Capture()` (fact 9), and `GalleryService` still
            stores photos from the channel until Story 1.17's memory card replaces the gallery's storage.
          - Epic 5's "print stills pulled from clips" may want a still's grade.
          - Update the comments that still say "until Story 1.16 decides its fate" (the class and `Capture()`
            comments in `PhotoModeController.cs:22-27, 390-395`), and `game-architecture.md:281-283` ("for the
            regression rigs and Story 1.16").
          - Mark `deferred-work.md:360` resolved, and move the gallery half to 1.17.
        - *Alternative:* delete them. Rejected: the three regression rigs and the gallery would go with them, for
          no player-facing gain.
        - **State the interim plainly** in the Dev Agent Record. After this story a clip **shows its grade** but is
          **stored nowhere** except the recorder's single `LastClip`, until Story 1.17.
  - [x] **1i. How the cost and the allocation are measured.** (AC5, NFR1, NFR3.)
        - *Toolbox: invoke `unity-perf:csharp-zero-gc` and `unity-perf:unity-performance` before writing the per-tick
          path.*
        - **Re-measure in SampleScene now** (`deferred-work.md:132`):
          - `ShotGrader.Grade` per call;
          - the clip grader per tick;
          - zero allocation, by heap growth over ~9,000 calls of the full path with the live drunk, against a
            positive control that allocates. Lesson 163: in this Mono, `GC.GetAllocatedBytesForCurrentThread` reads
            0 around `new byte[1000]`.
        - **Re-run Story 1.15's release-build bench**, because grading now runs inside every recorded frame:
          `Tools/Recording/Build Recording Bench (Windows)`, then
          `tools/verification/run_recording_bench.ps1` and `tools/verification/recording_bench_did.py`. Report the
          mean **and** the per-round median difference-in-differences (lesson 187). The absolute 60 FPS still needs
          a mid-range desktop (1.15's human check). Do not claim it here.
        - Adopting `Unity.PerformanceTesting` in a new PlayMode test assembly (the guard `deferred-work.md:381`
          asks for) is optional, and a decision if taken. The rig measurement with a working positive control is the
          evidence either way.

- [x] **Task 2 — The peak window's length** (AC2; Task 1c)
  - [x] 2.1 If 1c chose `ISubject.PeakDuration`, add it with a doc comment in the style of `PeakOffset`'s, and
        implement it on `EventActor` with the null and non-finite guards. Change no existing signature: the spike
        calls `PeakOffset`, `IsAtPeak` and `Bounds` (`Assets/Spikes/RecordingSpike/SpikeHarness.cs`; the fence is
        `RecordingSpikeFenceTests`), and a spike compile error blocks Play mode for every session.
  - [x] 2.2 `EventActor.cs` is CRLF and `ISubject.cs` is LF. Keep each file's own line endings (the Edit tool does).
        `git ls-files --eol` tells the truth (lesson 151).

- [x] **Task 3 — `ClipGrade`, the blend, and `GradingConfig`** (AC2, AC3, AC5; Task 1d, 1f)
  - [x] 3.1 `ClipGrade` (Task 1f) and the pure aggregation and blend in `Assets/Scripts/Grading/`. The sample
        input is plain values: counted, miss reason, composition, subject seen, at peak, peak length, weight.
        Nothing that needs a scene.
  - [x] 3.2 The new `GradingConfig` fields, `Safe*` accessors, `Default*` constants, any resolver they need, and
        tooltips that quote the measured evidence. **Do not add them to `OnValidate`'s rewrite** (fact 10). Report
        them in `TryGetConfigProblem`. Cover non-finite, out of range, and the silent shapes too. Example: a floor of
        1 makes coverage meaningless, which breaks C2 for every clip.
  - [x] 3.3 **Make `TryGetConfigProblem` report every problem in one warning** (`deferred-work.md:129`), as
        `GradeHudConfig.TryGetConfigProblem` already does. Keep its rule that the clamp checks run before the
        structural ones (`GradingConfig.cs:416-432`). Mark `:129` resolved.
  - [x] 3.4 Edit `Assets/Data/Grading/GradingConfig.asset` (LF, hand-written YAML; project memory
        `create-so-asset-via-yaml.md`) with the starting values, and later with the measured ones. `refresh_unity`,
        then read the values back through `AssetDatabase.LoadAssetAtPath` before any rig run (CLAUDE.md trap). Do
        **not** select the asset in the Inspector: `OnValidate` rewrites the YAML.

- [x] **Task 4 — `ClipGrader`, and grading wired into the recorder** (AC1, AC2, AC4, AC5; Task 1a–1c, 1e)
  - [x] 4.1 `ClipGrader` in `Assets/Scripts/Recording/`: `Begin()` resets every accumulator (bugs here hide in
        the second cycle), `SampleTick(int repeats)` grades every live actor (Task 1b), and `Finish()` returns a
        `ClipGrade`. Preallocated, with no LINQ, boxing, strings, `GetComponent*` or closures per tick.
  - [x] 4.2 `ClipRecorder`:
        - the `gradingConfig` field and its readiness flag, independent of `_ready`;
        - `Begin` resets the grader;
        - `LateUpdate` grades on the frames `Sample` took a tick, and grades the tick that hits the length limit
          before `TryGetStopRequest` stops the clip;
        - with no config, clips still record and are graded "not graded", with one Error at `Awake` (the core
          verb's feedback is gone) and nothing per press. Choose the level and record it.
  - [x] 4.3 `ClipRecorder.Stop`, in order:
        1. `End()`;
        2. grade the start tick if `End` added one;
        3. `Finish()`;
        4. build the `RecordedClip` with its grade;
        5. one Info line, `[Grading] Clip graded — {grade}`, beside the existing "Clip recorded" line;
        6. `Stopped`;
        7. `Raise`;
        8. claim.

        A stop from `OnDisable` or `OnDestroy` (scene unload, leaving play mode) grades too. Actors may already be
        destroyed: `ShotGrader` guards destroyed subjects (`IsNullOrDestroyed`, `ShotGrader.cs:489-491`), and the
        grader must never throw there.
  - [x] 4.4 `RecordedClip.Grade` (Task 1f). Update its doc comment and both construction sites.
  - [x] 4.5 `PhotoModeController.Awake`: if the recorder's grading config is not the controller's, warn once ("clips
        graded against a different config than photos"), mirroring the camera check at `:182-184`. Optional, but
        cheap.
  - [x] 4.6 After every change, `refresh_unity` and `read_console`. The bench (`Assets/Bench/RecordingBench`) and
        the spike compile against `CameraGame`, and a compile error in either blocks Play mode.

- [x] **Task 5 — The clip readout** (AC4; Task 1g)
  - [x] 5.1 `GradeHud`: the clip channel, `Format(ClipGrade)`, and the hide rule. Update the class header (it says it
        "subscribes to `ShotCapturedChannel`"), the tooltips, and `GradeHudConfig.hideOnCameraLowered`'s tooltip.
  - [x] 5.2 `GradeText`: the clip axes line and `PeakAdvice`, in the invariant culture, beside `TimingAdvice`. Add a
        clip wording to `MissLong` only where the photo wording would be wrong for a clip. Example: "this shot was
        never graded" (`GradeText.cs:133`). Keep `MissShort` exactly as it is: it is the gallery's, and
        load-bearing (`:83-88`).
  - [x] 5.3 Strings are built once per clip in the handler, never in `Update` (`GradeHud.cs:224-233`). The handler
        does not log, because the grader already logged the breakdown.

- [x] **Task 6 — Scene wiring** (AC4, AC6)
  - [x] 6.1 In `SampleScene`, through MCP in the editor: assign `ClipRecorder.gradingConfig` (on `main characters`)
        and `GradeHud.clipRecordedChannel` (on `GradeHudCanvas`). **Save the scene in a separate call** before any
        rig run (project memory: a save dialog mid-rig causes six menu retries).
  - [x] 6.2 Read the wiring back through `execute_code` (the serialized fields are not null). Check that the scene
        diff holds only those two references. `SampleScene.unity` is about 14.1 MB on disk (13.7 MB in git) and CRLF, and the orchestrator merges
        master into this branch (lesson 99: the "modified externally" modal).

- [x] **Task 7 — EditMode tests** (AC2–AC5)
  - [x] 7.1 Pure logic, with no scene:
        - **the blend:** the floor (C1's shape: coverage 0 with good framing prints at least "1 %"), the absence
          weight, and NaN, ±∞, negative, −0 and huge values;
        - **coverage:** 45 framed peak ticks of a 1.5-s window read exactly 1; partial; zero-length and NaN windows;
          the quantisation case Task 1c settles;
        - **candidates:** counted beats a miss, a tie keeps the first, and two lifecycles of one actor are not
          merged;
        - **the miss reason:** the Task 1e rule, and `NoSubject` when no actor was live;
        - **reuse:** a second clip after a first starts from zero;
        - **invariants:** `default(ClipGrade)` is neither counted nor a miss; `Sane` holds on the factories; and
          `ToString` prints "n/a" or dashes, never fabricated numbers;
        - **`GradeText`:** the clip axes line and `PeakAdvice` under fr-FR, de-DE and tr-TR, on non-midpoint values
          (lesson 152: Mono rounds midpoints);
        - **`GradingConfig`:** the new `Safe*` accessors, the all-problems validator, and "the shipped asset passes
          its own validator";
        - **`RecordedClip`:** it carries the grade it was built with.
  - [x] 7.2 **Prove the tests can fail** by mutation: break the floor, the counted-beats-miss rule or the culture pin,
        watch the test go red, and restore. Record it.
  - [x] 7.3 `RecordingSpikeFenceTests` and `RecordingBenchFenceTests` stay green. EditMode tests never see `Awake`
        on a plain MonoBehaviour added with `AddComponent` (lesson 184), so test the pure parts there and the wiring
        in the rig.

- [x] **Task 8 — Measure, decide, and verify by running it: the Grade Clip rig** (AC1–AC5)

  An editor `[MenuItem]`, e.g. `Tools/Grading/Grade Clip Shoot (Play)`. Build it like `RecordClipShootRig` and
  `GradeHudShootRig`:
  - a `SessionState`-armed runner in the **runtime** assembly behind `#if UNITY_EDITOR` (CLAUDE.md trap), with
    timestamped arming that expires (lesson 37);
  - run in **SampleScene**, because the wiring is part of what is under test, at **1920×1080** through the Game
    View's built-in Full HD preset by reflection (`GradeHudShootRig.SelectFullHdGameView`, restored afterwards; not
    test-helper, which writes Alexv's preferences);
  - log the capture size and the canvas scale factor on every run;
  - output to `_bmad-output/verification/1-16-grade-a-clip/<run>/`;
  - **never** write into Story 1.15's `rig/` folder. Record Clip Shoot always writes there: park it with
    `_bmad-output/verification/1-15-record-a-clip/close-a1/swap_rig_dir.py` before any re-run (lesson 189);
  - at the top of each output, say which errors the run provokes on purpose.

  Copy the proven pieces of `RecordClipShootRunner`:
  - the vantage (`PlaceAtVantage` / `ResetVantage`: (32, y, 8) facing the drunk's route, read at the ADR-5 scale from
    `PlayerConfig`);
  - the virtual gamepad (lessons 160 and 170, plus `backgroundBehavior = IgnoreFocus` for the run, restored);
  - `Grab` (`CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, no `targetTexture`).

  To aim at a moving subject:
  - set the player's yaw each frame, as `ResetVantage` sets its rotation;
  - set the pitch through `ThirdPersonController`'s private `_pitch`, by reflection (rig only; lesson 149's
    pattern);
  - zoom with `SetZoom`.

  **Anchor every scenario to the live lifecycle** (poll `PeakOffset` / `IsAtPeak` on the actor, read live), never
  to a wall-clock schedule (lesson 42: schedule drift alone moved a photo 1–3 stars).

  - [x] **Phase 0 — controls.** Before trusting any result, prove:
        - a frame with known 3D content is not uniform or near-white;
        - a full-screen Overlay marker reads 100.00 % on and 0.00 % off (1.12, 1.15);
        - **the grader control (AC1):** on one frame, pull the still `Capture()` and take the clip's tick sample of
          the same subject. They must agree exactly on `MissReason` and `Composition01`. A difference means
          something re-implemented or re-ordered the grade.
  - [x] **Phase S — the scenario clips** (AC2–AC4). Each clip logs:
        - a per-tick CSV: tick, clip time, repeats, actor, lifecycle, `IsAtPeak`, `PeakOffset`, miss, composition,
          subject seen, `HeightFraction`, `FramedFraction`;
        - its `ClipGrade`;
        - a contact sheet of key frames, with the per-tick gate verdict and the clip's grade **drawn onto** each
          frame (CLAUDE.md: draw the invisible values).

        Scenarios:
        - **S1 the money clip:** from late Build, through the whole window, to early WindDown, with the drunk centred
          and prominent.
        - **S2 same framing, missed early:** stop about 1 s before the window opens.
        - **S3 same framing, missed late:** start after the window closes.
        - **S4 partial:** start 0.75 s into the window; separately, stop 0.75 s into it. **S4b:** only the window's
          first ~0.5 s (the visible-peak lag, Task 1c).
        - **S5 the framing ladder over the whole window:** centred, off-centre, cut off by the frame edge, and small
          but past the gate.
        - **S6 absence:** S1 preceded by about 0, 5 and 20 s of aiming away (C4).
        - **S7 misses:**
          - nobody live: **deactivate** the live actors and put them back, because disabling the manager despawns
            nobody (lesson 15);
          - too far (`TooSmall`);
          - out of frame (`OutsideFrustum`);
          - occluded behind a **building**, confirmed by rendering (fact 11).
        - **S8 across a respawn:** two lifecycles. The best one wins, with no mixing.
        - **S9 every stop route:**
          - Record again;
          - lowering the camera;
          - the length limit (set `maxClipSeconds` low, **restore it and read it back**);
          - the gallery's suppression.

          For each, grab the HUD about 0.5 s after the stop (the settled frame, 1.12's lesson) and again near the
          end of the hold, recording the readout's alpha.
        - **S10 ten consecutive clips:** each grade depends only on its own ticks.
        - **S11 a clip stopped in the frame it started:** one graded tick, no throw.
  - [x] **Phase W — the sweep and the decision** (Task 1d). Feed the logged samples through the real aggregator
        for every candidate, write the table (scenario × candidate → percent, stars, printed text and colour), apply
        C1–C4 as written beforehand, and choose. Then write the asset, refresh, read back, and re-run Phase S: each
        live grade must equal its sweep prediction. A mismatch means the rig, the log or the aggregator is wrong.
        Find which before going on.
  - [x] **Phase H — the readout through the real input** (AC4).
        - Drive the virtual gamepad: LT to raise, RT to record, then stop with RT, and again by releasing LT.
        - **Prove the presses arrived** (lesson 160) before reading anything.
        - Measure stop press → readout on screen in ms, through the real input path (Phase A of Story 1.15 is the
          template).
        - Capture the four readout states (counted strong, counted weak, a miss, not graded) at 1080p.
        - Check every line fits.
        - Open the gallery mid-hold: the readout must be gone that frame.
        - If the layout or the text length changed, re-measure contrast on one bright background with
          `tools/verification/measure_hud_legibility.py` (Story 1.19's method; the colours are unchanged, so their
          legibility rows should hold).
  - [x] **Phase E — cost and allocation** (AC5, Task 1i): per call and per tick in ms, heap growth over ~9,000 full
        sampling calls with the live drunk (0 B expected, with a working positive control), and GC counts while
        recording.
  - [x] **Phase F — fail-soft (AC5). These are provoked errors: name them at the top of the output.** On a throwaway
        recorder, or by clearing a field and restoring it:
        - no `GradingConfig`;
        - no `EventManager`;
        - the new config fields at NaN, ±∞ and out of range;
        - the drunk deactivated mid-clip;
        - the recorder disabled mid-clip;
        - play mode exited mid-clip.

        Each must give one log line, no exception, and a clip that is still announced. Then **re-check that the
        console is clean in a normal run** (NFR5).
  - [x] **Look at every capture yourself** (the Read tool shows images). Suspect the rig first if every case passes,
        every case fails identically, a number is suspiciously round, or a capture is blank.
  - [x] **Before every run:**
        - `refresh_unity`, then wait for the compile;
        - read the config values and the wiring back through the editor;
        - check that `mcpforunity://editor/state` shows `external_changes_dirty: false` (CLAUDE.md trap).
  - [x] **After every run, restore and read back:**
        - the scene is clean, with no rig objects;
        - `timeScale` is 1, and the Game View size is back;
        - the config assets match git (`maxClipSeconds` 120, the `GradingConfig` values);
        - the virtual gamepad is removed, and the input and audio settings are unchanged;
        - no rig `SessionState` keys remain, and `git status` shows only your work.

- [x] **Task 9 — Architecture, GDD, deferred work** (AC6)
  - [x] 9.1 `game-architecture.md`:
        - §Clip Recording: `ClipGrader`, `ClipGrade` and `RecordedClip.Grade` as built (`:222-225, :265-268`), and
          the `Capture()` paragraph (`:281-283`);
        - §Architectural Boundaries: add `Grading` to `Recording`'s dependencies (`:905-907`);
        - the `ISubject` member lists (`:913-914` and the pattern section near `:938`) if 1c added `PeakDuration`;
        - §Directory Structure: `Grading/` gains `ClipGrade` (`:816`);
        - the stale `ShotCapturedChannel` references this story makes wrong: the E7 row at `:1124` (missions
          listen to clip events, `sprint-change-proposal-2026-11-10.md:438`; `:432` is the same rename for
          reputation, already carried by the E4 row) and `:389`.
  - [x] 9.2 GDD (`_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md`): `:203-209` (§4) and `:229`
        (§5) say the blend and the floor "are decided by measuring recorded clips in Story 1.16". Write the chosen
        values in, each with a dated note and the evidence path, as Story 1.10 did for FR6. **Do not edit the ACs in
        `epics.md`.**
  - [x] 9.3 `deferred-work.md`:
        - mark resolved: `:360` (the gallery half moves to 1.17), `:129` (if 3.3 lands), `:132` (re-measured),
          `:202` and `:214` for the clip readout;
        - add what you defer, for example the visible peak against coverage (Task 1c), or a clip "why" line voice
          that only Alexv's look can settle.
  - [x] 9.4 Do not edit `loop/` (the orchestrator's). Do not turn `RaiseSuppressed` into a refcount: this story adds
        no second suppressor (`deferred-work.md:144`); the HUD only **reads** it.

- [x] **Task 10 — Regression, restore, close** (AC6)
  - [x] 10.1 Run the EditMode suite with `run_tests` (371/371 at `1601e7e`), the fence tests included.
  - [x] 10.2 Re-run the rigs whose code this story reaches. Choose them by diffing
        (`git diff <rig-run-commit>..HEAD -- Assets/`, then grep who calls the changed members; lesson 154).
        Expect at least:
        - **Record Clip Shoot** (`ClipRecorder`, `RecordedClip`, `IClipBackend.Sample` changed). Park its `rig/`
          folder first (lesson 189). `MakeRecorder` now builds a recorder without a grading config, so update the
          rig's list of announced lines, or give it the config.
        - **Grade HUD Shoot** (`GradeHud` changed). Point it at your folder with the one-shot `SessionState` key
          `GradeHudShoot.OutputDir`, so it never overwrites `verification/hud/`. Its Phase C "lower mid-hold" and
          "gallery mid-hold" now test the shot readout's rule, which is unchanged.
        - **Photo Shoot** (`ISubject` and `EventActor` changed under the grader). `shots.txt` verdicts must be
          unchanged. Diff stars and composition per shot **unmasked** (lesson 41), and explain any move against the
          known schedule drift of the framing shots (lesson 42).
        - **Gallery Shoot**, if `PhotoModeController` changed.
        - **A normal SampleScene play session:** record the drunk through his peak, the readout on the stop by RT and
          by lowering, the gallery opening in Walk, the console clean.

        Rigs write into their own evidence folders, so copy those aside before a re-run and diff them back (lesson
        103). Record why any rig was not re-run.
  - [x] 10.3 Task 1i's bench re-run in a release build. Afterwards, restore the five settings files a build
        re-serialises: `git show HEAD:path > path`, then back to CRLF (lessons 24 and 173).
  - [x] 10.4 `read_console` is clean, apart from known baseline lines such as the standing "Head bone not found!"
        warning, if it is still there.
  - [x] 10.5 Look at the evidence as a sceptic would (the verification plan below). Fix what it finds **before**
        queueing a human check. A human check is never a place to park a defect.
  - [x] 10.6 Commit on `loop/1-16-grade-a-clip`, staging files by name with every new asset's `.meta`. Update the
        sprint status line, the Dev Agent Record, the File List and the Change Log.

## Dev Notes

### What already exists — reuse it, do not rebuild it

| Need | Exists in | Notes |
|---|---|---|
| The per-frame gate and composition | `Grading/ShotGrader.cs` `Grade(cam, subject, cfg, out detail)` | **Call it; never copy its maths** (AC1). Main thread only (static buffers). |
| Grade payload discipline | `Grading/ShotGrade.cs` (`StarScale`, `Sane`, `Missed`, `Scored`, `HasSubject`, the `Unevaluated` zero value) | `ClipGrade` follows the same rules: a stored `Stars`, NaN-safe floats, empty `SubjectId` for nobody, no factory that builds an all-zero counted grade (`:238-250`). |
| Best-of-several rule | `PhotoModeController.GradeBestSubject` (`:573-634`) | Counted beats a miss, then the higher percent, and the first graded wins a tie. Reuse the predicate for clip candidates; consider lifting it into `Grading` so the two cannot drift. |
| Config validation | `GradingConfig` (`Safe*`, `Default*`, resolvers, `TryGetConfigProblem`), `GradeHudConfig.TryGetConfigProblem` (reports all problems) | And their tests: `GradingConfigTests.cs`, `GradeHudConfigTests.cs` (the shipped asset passes its own validator). |
| Readout colour and text | `GradeHudConfig.ColorFor(bool, bool, int, float)`; `GradeText.Percent`, `PrintsZero`, `Stars`, `MissLong` | Built by Story 1.19 for this story. Red follows the printed text. |
| Live subjects and lifecycles | `EventManager.ActiveActors`; `EventActor.LifecycleSerial`, `Definition`, `IsAtPeak`, `PeakOffset` | Read live every tick, and filter `isActiveAndEnabled`. |
| The tick clock | `ClipRecorder.LateUpdate` → `ReplayBackend.Sample` (`FootageFormat.RepeatsDue`) | Grade on the same ticks. Do not invent a second clock. |
| Channel with listener isolation | `Core/EventChannel<T>` | A throwing listener cannot abort the others. Do not re-test the base class. |
| Overlay capture, the 1080p preset, control shots, the virtual gamepad | `UI/GradeHudShootRunner*.cs`, `Editor/GradeHudShootRig.cs` (`SelectFullHdGameView`), `Recording/RecordClipShootRunner.cs` | The proven techniques. Reuse them; do not invent others. |
| Legibility measurement | `tools/verification/measure_hud_legibility.py` (Story 1.19) | Only if the readout's layout or text changes enough to matter. |
| Release-build frame cost | `Tools/Recording/Build Recording Bench (Windows)`, `tools/verification/run_recording_bench.ps1`, `recording_bench_did.py` | Re-run as is; it drives the real `ToggleRecording()`. |

### Architecture compliance

- **§Clip Recording** (`game-architecture.md:212-290`): `ClipGrader` "samples `ShotGrader.Grade(...)` on frames
  throughout the recording … unchanged and not re-implemented … aggregates the samples, plus peak coverage read from
  `ISubject`, into a `ClipGrade` … Tunables live in `GradingConfig`." The data flow is `Stop → ClipGrader →
  ClipRecordedChannel.Raise → … GradeHud shows its grade`.
- **Dependency direction:** `PhotoMode → Recording → Grading` and `Events`, plus `Core`. `UI` reads `Recording`
  through the channel and read-only state. `Grading` references neither `Recording` nor `UI`. `Recording` never
  references `PhotoMode`, `UI` or `Gallery`.
- **AR3:** no GPU readback in grading. `ShotGrader` already holds to that; the clip layer adds arithmetic only.
- **AR4:** the clip's grade travels on `ClipRecordedChannel`, synchronous and typed. The HUD listens and never
  claims.
- **AR5:** namespaces `CameraGame.Grading` and `CameraGame.Recording`, one `CameraGame.asmdef` (it references only
  `Unity.InputSystem`, and nothing here needs more). Editor tools go in `CameraGame.Editor`, tests in
  `CameraGame.Tests.EditMode`. Rig runners live in the runtime assembly behind `#if UNITY_EDITOR`.
- **Error handling:** fail-soft. A missing critical reference is one `Error` and the piece stands down; a
  recoverable problem is a `Warn`; nothing throws into `Update` or `LateUpdate`; errors are never shown to the
  player. The HUD's "not graded" is a state, not an error message.
- **Logging:** one Info milestone per clip carrying the grade and its breakdown. The per-tick path logs nothing
  except a guarded `Debug_`.
- **Configuration:** every tunable lives in `GradingConfig`, as plain numbers, not `AnimationCurve`
  (`GradingConfig.cs:17-24`). The tick rate is a footage-format invariant (`FootageFormat`), not a tunable.
- **ADR-4 and the Recordability rule:** untouched. Grading reads the live game while recording and adds nothing to
  the footage.
- **ADR-5 (world scale):** rig vantages read `PlayerConfig`. Distances are world metres.

### The genuinely new risks

1. **Coverage can be wrong at the edges by a tick**, and 2.2 % costs a star at the 5★ boundary (0.90). Task 1c
   makes the whole-window clip read exactly 100 %. Prove it on a real clip, not only in a test.
2. **The HUD hides the grade it was asked to show** (fact 8). This is the easiest way to pass every structural check
   and still show a player nothing. Phase H photographs the readout after a stop by lowering.
3. **Fitting the blend to the data.** Write C1–C4 before looking at the sweep table, and keep the runner-up in the
   record.
4. **Candidate bookkeeping across a respawn.** The same pooled instance runs every lifecycle, so a key without the
   serial merges two lifecycles silently, and a cache across clips carries one clip's grade into the next. S8 and S10
   exist for this.
5. **Breaking the photo path while touching shared code** (`ISubject`, `EventActor`, `GradeHud`, `GradingConfig`).
   Photo Shoot's verdicts are the guard (AC6).
6. **The visible peak and the scored window disagree** (`deferred-work.md:261`). It is a pre-existing design gap
   that clips make easier to see. Measure it and report it; do not fix it silently.

### Project Structure Notes

- **New:**
  - `Assets/Scripts/Grading/ClipGrade.cs`, plus any pure aggregation helper;
  - `Assets/Scripts/Recording/ClipGrader.cs`;
  - the rig: an editor `[MenuItem]` in `Assets/Scripts/Editor/` and a runner in the runtime assembly behind
    `#if UNITY_EDITOR`;
  - tests in `Assets/Tests/EditMode/`.
- **Modified:**
  - `Recording/ClipRecorder.cs` (CRLF), `RecordedClip.cs` (CRLF), `IClipBackend.cs` and `ReplayBackend.cs` (CRLF;
    `Sample` returns its repeats);
  - `Grading/GradingConfig.cs` (CRLF) and `Assets/Data/Grading/GradingConfig.asset` (LF);
  - `Grading/GradeText.cs` (CRLF);
  - `UI/GradeHud.cs` and `UI/GradeHudConfig.cs` (CRLF; tooltip only, unless the hide rule needs a field);
  - `Events/ISubject.cs` (LF) and `Events/EventActor.cs` (CRLF), if 1c adds `PeakDuration`;
  - `PhotoMode/PhotoModeController.cs` (CRLF; comments, and the optional config check);
  - `Assets/Scenes/SampleScene.unity` (CRLF; two references);
  - rigs that build these components (fact 12);
  - `game-architecture.md`, `deferred-work.md` and `sprint-status.yaml` (CRLF in the working tree), and the GDD (LF).
- **Line endings:** keep each file's own (lesson 65). The Edit tool does this. A Python edit must read with
  `newline=''`, and must never feed already-CRLF text through a `\n` conversion (lesson 97). `git ls-files --eol` is
  the truth.
- Commit every new asset's or folder's `.meta` with it, and stage by name. `git add -A <paths>` is refused (lesson
  172).

### Previous Story Intelligence

- **1.15 (the recorder this plugs into).**
  - **The design:** ticks at LateUpdate 10000, repeats and the start tick; the channel raised once per clip after
    `Stopped`; the claim rule (the HUD must not claim); two preallocated storage blocks; fail-soft with one line per
    fault.
  - **Rig lessons:**
    - drive the real input with a virtual gamepad and prove the press arrived;
    - `execute_code` runs outside the player loop, so `Begin` and `Stop` in one call make a 1-tick clip (lesson
      179);
    - Record Clip Shoot writes into the dev's `rig/` folder (lessons 181 and 189);
    - zero allocation is proven by heap growth with a positive control (lesson 163);
    - the editor renders Main Camera more than once a frame (lesson 164);
    - PlayerInput Send Messages reaches disabled components (lesson 174);
    - Unity's `==` on a destroyed object skips plain-C# cleanup (lesson 176).
  - It stayed `review` for four checks for Alexv: the camcorder look and beeps, desktop 60 FPS, the recorded sound,
    and the feel of the toggle. Those are 1.15's, not this story's.
- **1.19 (the readout's colours).**
  - Red for a miss **and** for any counted grade that *prints* "0 %", decided by `GradeText.PrintsZero`; 1–2★
    amber, 3★ cream, 4–5★ green; a 1.5-px black outline applied at `GradeHud.Awake`.
  - Pin the invariant culture everywhere (fr-FR prints U+066A).
  - `Mathf.Clamp01(-0f)` keeps the sign bit (lesson 138).
  - Legibility numbers carry over only at 1080p (lesson 139).
  - Its AC7 ("red reads as earned nothing") is still open for Alexv. This story reuses that red for clips, so
    mention it in the clip human check rather than re-asking it.
- **1.12 (the HUD itself).**
  - Capture the settled frame about 0.5 s after the event (the flash shone through).
  - `IsShowing` cannot tell hold from fade (`deferred-work.md:212`), so read the group's alpha.
  - Captions describe what the camera did, never the expected grade.
  - Alexv's look checks found what every structural check missed, twice.
- **1.13 (superseded; its intent is AC3).**
  - The floor formula and its 0.3 starting value.
  - `Timing01` stays honest and unfloored, and only the product changes. The clip equivalent is that
    `PeakCoverage01` prints the true coverage.
  - Don't retune the star thresholds in the same change, so the effect stays attributable.
- **1.10 (how a grading number was decided).**
  - Run the rig before writing scoring code.
  - Matched comparisons that differ in one variable only.
  - A first sweet spot so wide that every framing scored 83 % taught that a tunable must actually discriminate.
  - Record the table, and keep the tooltips in step with it (its review caught stale tooltip numbers).
  - Composition breathes 1–9 points with the walk cycle; averaging over ticks smooths that, which is a reason to
    prefer per-tick means.
- **1.9:** the gate order, the `NotEvaluated` sentinels, and "suspect the rig". The deleted edit-mode bench measured a
  T-pose and doubled every number, so grade in play mode only.
- **2.1 and 2.2:**
  - after a play-mode rig the MCP bridge drops for 10–60 s, so call `refresh_unity(wait_for_ready)` (lesson 59);
  - "No Unity Editor instances found" means the call was dropped, so re-send it (lesson 128);
  - an `execute_code` call over ~30 s keeps running after the client times out (lesson 122);
  - `Awake` runs on disabled components (lesson 69).

### Inherited context you should not rediscover

- In code the raised state is `CameraMode.Photo`, not "Viewfinder". Do not rename it: the spike and three rigs use
  the API.
- `Time.timeScale` is 1 in normal play. Clips follow real time and the peak follows scaled time, so do not freeze the
  world during a graded scenario.
- `EventManager.ActiveActors` is not cleared when the manager is disabled. The grader filters `isActiveAndEnabled`,
  and so must you.
- The drunk carries no colliders and sits on the `Subject` layer, which `occluderMask` (Default only) excludes. He
  never occludes himself, and subjects never occlude each other.
- The drunk's route is short (about 11.3 m, peak near x 26, z 19). From the 1.15 vantage at (32, y, 8) he is in
  view for the whole peak.
- The editor and development-build debug overlay (`PhotoModeController.OnGUI`, inside
  `#if UNITY_EDITOR || DEVELOPMENT_BUILD`) shows the last *still's* detail. It knows nothing about
  clips, and nothing requires it to.

### Git Intelligence

- Baseline `1601e7e` (the loop's lessons from 1.15), on top of the 1.15 merge `406346f`. The last eight commits are
  all Story 1.15: `ClipRecorder`, `ReplayBackend`, `RecordedClip`, `RecordingHud`, the rig and the bench.
- `GradeHud.cs`, `GradeHudConfig.cs` and `GradeText.cs` were last changed by Story 1.19. `GradingConfig.cs` was last
  changed by Story 1.10 and `ShotGrade.cs` by 1.12's review. `ShotGrader.cs`'s maths dates from 1.10; its last change
  was a comment in Story 2.1 (`a56b719`).
- This create phase changed only this file and `sprint-status.yaml`.

### Latest technical information (checked 2026-11-15)

- Unity 6000.3.8f1, URP 17.3.0, Input System 1.18.0, MCP for Unity 10.3.0. The story needs **no new engine API and
  no new package**. Everything it calls already runs in this project: `Physics.Linecast`,
  `GeometryUtility.CalculateFrustumPlanes(Camera, Plane[])`, `Camera.WorldToScreenPoint`, uGUI `Text`.
- `unity_docs` returned empty bodies for several pages on 2026-11-15 (lesson 161). `unity_reflect` works: use it
  for member existence, and measure behaviour.
- Toolbox:
  - **use:** `unity:ui` → `unity:ui-ugui` (Task 5); `unity-perf:csharp-zero-gc` and `unity-perf:unity-performance`
    (Task 4, 1i); `unity-perf:code-standards` for a design pass before review (optional); Profile Analyzer only if the
    bench needs a capture comparison;
  - `unity:physics-3d-collision`, only if the occluded scenario behaves unexpectedly (the pine trap is known; do not
    rediscover it);
  - **decision if adopted:** `Unity.PerformanceTesting` with test-helper's `[LoadScene]` in a new PlayMode
    assembly (Task 1i). `[GameViewResolution]` writes editor preferences: use the reflection preset instead;
  - **not relevant, and not to be used:** InstantReplay (ADR-4 chose replay), Cinemachine, KinoGlitch, UnityHFSM (an
    accumulator needs no state machine), Graphy (never shipped on).

### Verification plan: what would convince a sceptic

| Claim | Evidence | How judged |
|---|---|---|
| The grader is the real one, unchanged (AC1) | Phase 0 grader control: the still and the clip sample agree on a shared frame; `ShotGrader.cs` diff empty; Photo Shoot verdicts unchanged | Exact equality; an empty diff; per-shot stars unmasked |
| Every tick is graded (AC1) | Graded seconds equal `IFootage.Seconds` for every clip, the 1-tick clip (S11) included | Equal to the tick |
| Coverage is right at the edges (AC2) | S1 reads 100 % coverage; S4 lands near its covered fraction; S4b recorded | Numbers drawn onto the contact sheets; look at the frames |
| The blend was decided by measuring (AC2, AC3) | The Phase W table, the criteria written beforehand, the choice and the runner-up; the live re-run equals the sweep | Read the table against C1–C4; the frames for the ranking |
| A well-framed miss is worth something; the catch wins clearly (AC3) | S2 and S3 against S1: printed text, colour, stars | C1 and C2; Alexv's **human check** for the feel |
| A clip that never caught him is a miss with the right words (AC4) | S7: the reason per scenario, against what the frames show | Look at each frame and its words |
| The HUD shows every clip's grade (AC4) | Phase H and S9: the readout grabbed after each stop route, its alpha over the hold, stop → visible in ms | The pictures; the alpha series. **Human check** for "understand why" |
| Cheap and allocation-free (AC5) | Phase E: ms per call and per tick, 0 B over ~9,000 calls with a working control; bench DiD (mean and median) | Numbers, not thresholds |
| Independent clips (AC5) | S8 (respawn) and S10 (ten in a row): each grade from its own ticks | CSV per clip |
| Fail-soft (AC5) | Phase F: one line per provoked fault, the clip still announced, a clean normal run | Console and an `Editor.log` grep |
| No regression (AC6) | EditMode count; fence tests; re-run rigs diffed against their previous runs; restore read-back | Same results, or differences explained |

### Human checks to queue (never self-certify)

Queue these at the **end of dev**, after every automated part, each with exact files and your own measured read:

1. **AC2 and AC3: the clip ladder.**
   - **What to look at:** the scenario clips' key frames (or short rig videos) beside their grades. S1 the whole
     peak; S2 and S3 well framed but missed; S4 partial; S5 the framing ladder; S6 with empty footage.
   - **The questions:** does the ranking match his judgement? Does the missed-peak clip get "not a big deal" credit
     while the catch still clearly wins? Is the cost of empty footage right?
   - **Evidence:** the Phase W table and the contact sheets.
2. **AC4 and NFR10: the clip readout.** Shown the readout after each kind of clip, does he understand *why* it
   scored what it did, at a glance (the axes line and the advice line)? This is also where the clip states meet
   1.19's red for the first time.
3. **The hide rule** (if Task 1g changed it). Does a grade that stays up after lowering the camera feel right, and
   does it get out of the way of the gallery?
4. **The visible peak** (only if S4b shows credit for frames where nothing visibly happens). Should coverage count
   from when the stagger becomes visible?

### Project Context Rules

- MCP for Unity is the only editor channel. Never use `unity:unity-cli` or Pipeline commands to edit, run or test.
- After every script change: `refresh_unity`, then `read_console` (NFR5). Before a rig run, read the values back
  through the editor.
- Evidence goes to `_bmad-output/verification/1-16-grade-a-clip/`, never `Temp/`. Builds go to
  `Builds/1-16-grade-a-clip/`.
- Restore everything a rig or build changes, and read it back.
- Commit on `loop/1-16-grade-a-clip` only, staging by name, with `Co-Authored-By: Claude Opus 5.5
  <noreply@anthropic.com>`. Never switch branches, merge, rebase, reset, stash, clean or push.
- Perceptual ACs become `human_checks`. A measured shortfall is fixed, never queued.
- Decisions are made by evidence and recorded (question, options, choice, evidence, cost to reverse).
- Do not edit `loop/runtime/`. `approvals.txt` is readable only with the Read tool (lesson 162). It holds no
  approval for this story: none is needed, because nothing in 1.16 gates another unit.

### References

- `_bmad-output/planning-artifacts/epics.md`:
  - Story 1.16 (`:550-582`, the ACs);
  - Story 1.13 (`:458-487`, superseded; its intent);
  - FR4–FR7, FR27, NFR1–NFR3, NFR5, NFR8, NFR10, AR3, AR4.
- `_bmad-output/planning-artifacts/game-architecture.md`:
  - §Photo Capture & Grading (`:199-210`);
  - §Clip Recording (`:212-290`);
  - §Architectural Boundaries (`:886-917`);
  - §Directory Structure (`:807-848`);
  - the Frame-Read Grading pattern (`:997-1045`);
  - §Cross-cutting Concerns (error handling, logging, configuration, event system).
- `_bmad-output/planning-artifacts/sprint-change-proposal-2026-11-10.md`: `:73-75, :101-104, :124-130, :202-205,
  :244-246, :314-346, :489-490, :600-601`.
- `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md`: `:101`, `:138`, `:156`, `:199-209` (§4,
  the clip grading model), `:215-229` (§5, tuning), `:412`.
- `_bmad-output/implementation-artifacts/`:
  - `1-15-record-a-clip.md` (the Dev Agent Record and review);
  - `1-13-soften-the-timing-multiplier.md`;
  - `1-12-grade-feedback-hud.md`;
  - `1-19-a-shot-that-earns-nothing-reads-red.md`;
  - `1-10-composition-and-timing-scoring.md`;
  - `1-9-subject-capture-gate-grading.md`.
- `_bmad-output/implementation-artifacts/deferred-work.md`: `:129, :130, :132, :144, :162-180, :202, :210, :212, :214,
  :261, :264-270, :360, :381`.
- `loop/lessons.md`: the seeded section and sections 1-14, 1-19, 1-15, 2-1 and 2-2. **"Lesson N" in this file means
  line N of `loop/lessons.md`** (the orchestrator only appends, so the numbers are stable).
- Code:
  - `Grading/ShotGrader.cs`, `ShotGrade.cs`, `GradingConfig.cs`, `GradeText.cs`;
  - `Recording/ClipRecorder.cs`, `ReplayBackend.cs`, `RecordedClip.cs`, `IClipBackend.cs`, `FootageFormat.cs`;
  - `UI/GradeHud.cs`, `GradeHudConfig.cs`;
  - `Events/ISubject.cs`, `EventActor.cs`, `EventManager.cs`;
  - `PhotoMode/PhotoModeController.cs`;
  - `Recording/RecordClipShootRunner.cs`, `UI/GradeHudShootRunner*.cs`, `Editor/GradeHudShootRig.cs`;
  - `Tests/EditMode/GradingConfigTests.cs`, `GradeStructTests.cs`, `GradeTextTests.cs`, `RecordingTests.cs`.

## Dev Agent Record

### Agent Model Used

claude-opus-5-5 (loop DEV phase, attempt 1, 2026-11-15)

### Debug Log References

- Rig runs (SampleScene, real wiring, 1920x1080 through the Game View's built-in preset): `_bmad-output/verification/1-16-grade-a-clip/`
  `smoke/` (Phase 0 only, found the vantage problem), `run1/` (full, starting config), `run2/` (Phase S, the
  decision data), `run3/` (full, the chosen config), `run3e/` (Phase E with the fixed positive control). Each has
  `report.txt`, `ticks.csv` (every graded tick, round-trip floats), `clips.csv` (every clip's live grade and the
  HUD's printed words, colour, alpha and line count) and the PNG frames; `run1/sheets/` holds contact sheets with
  the live grade drawn on.
- Phase W: `run2/sweep.csv`, `run2/sweep-check.txt`, `run2/decision.md`; replication on `run3/` (same files) and
  `run3/live-vs-run2-prediction.txt`.
- Vantage survey: `vantage-survey/vantage_0..3.png`. Visible peak: `run2/sheets/S4b_visible_peak.png`.
- Release-build bench: `bench/` (`did.txt`, `bench-frames.csv`, `player.log`, `procmem.csv`, `build-result.txt`).
- Tests: `tests/mutation-run.md`. Regression rigs: `regression/`.
- Editor.log of this session: zero exceptions across every rig run; the only warnings and errors are the ones
  each rig announces as provoked.

### Decisions

#### Task 1d — the decision criteria, WRITTEN BEFORE ANY CLIP WAS RECORDED (committed 2026-11-15, before the rig existed)

Applied, as written here, to the Phase W sweep table. The table does not exist yet when this is committed; the git
history is the proof of the order.

- **Scenarios the criteria read:** S1 (whole window, centred, prominent), S2 (same framing, stopped ~1 s before the
  window opens), S3 (same framing, started after it closes), S4a/S4b' (started / stopped 0.75 s into the window), S5
  (off-centre, cut off by the edge, small but past the gate), S6 (S1 preceded by ~5 s and ~20 s of aiming away).
- **C1 (AC3, "above zero").** S2 and S3 each print at least "1 %", are not red (`GradeHudConfig.ColorFor`), and earn
  **2★ or more**.
- **C2 (AC3, "wins by a wide margin").** S1's stars minus S2's stars ≥ 2, and the same for S3. Across the set,
  coverage moves the grade more than two GOOD framings differ: |S1 − S2| > |S1 − S5 off-centre| (percent points).
- **C3 (FR7).** S1 reaches 5★. S4a and S4b' land strictly between S2 and S1. Within one framing, the grade rises with
  coverage (S2 or S3 < S4 < S1, ordered by measured coverage).
- **C4 (footage without him).** S6-5 (~5 s away first) loses **at most one star** against S1; S6-20 (~20 s away, about
  a quarter of the clip in shot) loses **at least one star and at most two**. The reasoning: the story's "so that"
  (not "pressing record and walking away") says a mostly empty clip must not score like a tight one, and the memory
  card (1.17) already charges every second against capacity, so the cost should be visible but not crushing.
- **Choosing among the candidates that pass C1–C4:**
  1. the shape `FloorTimesCoverage` over `Additive` (framing then scales the peak's credit too);
  2. framing over `GatedTicks` over `PeakTicksWhenCovered` (simpler, and smooths the walk-cycle breathing, Story 1.10);
  3. the floor closest to Story 1.13's 0.3;
  4. the smallest absence weight that passes C4.

  The runner-up is the next candidate in this order, and is recorded with why it lost.
- **If no candidate passes**, the failing criterion is reported with its numbers. I fix the rig or the scenario
  before touching a criterion. A criterion changes only with a written reason, and Alexv sees it in the decisions batch.

#### The nine gating decisions, as settled (Alexv may veto any of them in the batch)

- **1a — where clip grading lives.** As recommended: `Recording/ClipGrader` (plain C#, owned and driven by
  `ClipRecorder` like its backend; `IClipBackend.Sample` now returns the repeats it emitted) feeding a PURE
  `Grading/ClipGradeBuilder` (plain values in, `ClipGrade` out). `Recording → Grading` added to §Architectural
  Boundaries; `Grading` never references `Recording`. Reversal cost: moderate (the builder is reusable as is).
- **1b — one sample per footage tick, weighted by its repeats**, every live actor (`isActiveAndEnabled`) through
  `ShotGrader.Grade(recordCamera, actor, gradingConfig)`, keeping the gate and `Composition01` only. Measured:
  graded seconds = `IFootage.Seconds` on every clip of every run (e.g. S1 5.133 s / 76 ticks both), the start tick
  included (S11: 1 tick, 0.033 s).
- **1c — peak coverage on the SUBJECT'S OWN CLOCK, length from a new `ISubject.PeakDuration`** (`EventActor`
  returns the Peak phase's duration; NaN, ±∞ and ≤ 0 read 0 = no peak). Each tick stands for the stretch of
  `−TimeToPeak` since that lifecycle's previous tick; the stretches join end to end, so the whole framed window
  reads exactly 100 % at any tick alignment, frame rate or timeScale (EditMode tests at 6 alignments and rates;
  live: S1 100.0 % in run1, run2 and run3). The alternative (counting ticks, or a one-tick tolerance) was not
  needed. **The visible peak lags the window**: a clip of only its first ~0.5 s earns 28-29 % coverage, 49-50 %,
  3★, while its frames show the walk turning into the stagger (stagger visible from ~0.8 s;
  `run2/sheets/S4b_visible_peak.png`). Not changed (it belongs to the event and moves every photo grade);
  deferred and queued for Alexv. Cost of aligning later: a visible-peak offset on `EventDefinition`, plus a
  re-measure of photos and clips.
- **1d — the blend, decided by measuring** (criteria above, committed before any clip existed):
  `percent = framing × (floor + (1 − floor) × coverage)`, framing = mean gated composition ×
  `(1 − absenceWeight × (1 − inShot))`. **floor 0.30, absenceWeight 0.25**, one star ladder. 104 of 280 candidates
  passed C1-C4; the pre-written tie-break chose this one; runner-up absence 0.50 (lost only on "smallest absence
  that passes C4"; it would cost the ~20-s-away clip two stars instead of one). Floors ≤ 0.20 fail C1 (a well-framed
  miss drops to 1★); absence 0 fails C4 (20 s of empty footage free). Replicated: run3's independent recording
  picks the same candidate, and its live grades match run2's predictions within a point on every scenario.
  Evidence: `run2/decision.md`, `run3/decision.md`, `run3/live-vs-run2-prediction.txt`.
  **Added by measurement — the presence rule (`ClipPresence`).** run1's S8 (a clip framed continuously across a
  respawn) scored 68 % because "in shot" counted only the winning lifecycle's ticks, charging the earlier,
  well-framed lifecycle as absence. "Footage without the subject" now means ticks on which NOBODY passed the
  gate (`AnySubject`, the default; S8 → 91 %, 5★). The pass grids of the two rules are identical; the choice is
  the definition, with S8 as its evidence. Reversal cost: one constant.
- **1e — candidates and the miss reason.** One candidate per `LifecycleSerial`, never merged; best by
  `GradeRanking.Beats` (lifted unchanged from `GradeBestSubject`, which now calls it). Miss reason: **MostTime** (the
  reason that held longest, ties toward the gate closest to passing). Measured on S7 mixed (3 s out of frame, then a
  0.3 s glimpse behind a wall): MostTime says "he was outside the frame", DeepestGate "something was in the way";
  the first is what happened for 90 % of the clip. Preallocated: max(32, 8 × maxActorsPerTick) candidates.
- **1f — `ClipGrade`** (readonly struct in Grading; stored Stars; `Unevaluated` zero value; the peak window
  partitioned into before / after / out of shot / covered; `GradedSeconds`; empty SubjectId for nobody), carried on
  `RecordedClip.Grade` (constructor parameter; both construction sites updated).
- **1g — the readout.** `GradeHud` also listens to `ClipRecordedChannel` (cached subscription, never claims; a missing
  clip channel is an Error). Wording: `★★★★★   99 %` / `framing 99 %   ·   peak 100 %   ·   in shot 100 %` /
  `GradeText.PeakAdvice` ("caught the whole moment", "stopped too soon — keep rolling", "started too late — record
  sooner", "it happened out of shot — keep him framed"; praise only when the peak axis PRINTS 100 %). **Dots, not
  "×"**: a clip's percent is not framing × peak (the floor), so "×" would state a false equation. **Hide rule**: a
  clip's readout yields only to `RaiseSuppressed` (measured: visible with alpha 0.60-0.76 near the end of its hold
  after a stop by lowering; gone on the first frame the gallery renders; never shown for a clip stopped by
  suppression); the still's readout keeps `hideOnCameraLowered`. The Grade HUD rig's throwaway HUD is wired to the
  shipped clip channel (no new error line).
- **1h — `Capture()` and `ShotCapturedChannel` KEPT**, unchanged and unbound (three regression rigs, the gallery until
  1.17, Epic 5's prints). **Interim, plainly:** after this story a clip SHOWS its grade but is stored nowhere except
  the recorder's single `LastClip`, until Story 1.17.
- **1i — cost and allocation.** Rig Phase E in SampleScene with the live drunk; `Unity.PerformanceTesting` NOT adopted
  (no new test assembly; the rig with a working control is the evidence). Release-build bench re-run (see notes).
- **Rig vantage (not a story decision, recorded because it changes evidence).** Story 1.15's (32, 8) has a pine
  trunk collider 8.6 m out on the line to the drunk's early Build: the grader rightly read Occluded (smoke run,
  `vantage-survey/vantage_3.png`). The rig films from (26, 13), side-on 6 m south of the peak spot, surveyed by
  rays (81 standable vantages) and by rendering route markers.
- **Recorder config check (Task 4.5)**: done. `PhotoModeController.Awake` warns when the recorder grades against a
  different config, and validates that config there, so one bad value is one warning (the recorder does not
  validate the shared asset a second time).

### Completion Notes List

**What was built.** Every clip the player records is now graded and the HUD shows the grade.
- `ClipRecorder` grades each footage tick through the unchanged `ShotGrader`, and folds the samples in a pure builder.
- `ClipRecorder.Stop` finishes the `ClipGrade` before the clip is announced. `RecordedClip.Grade` carries it.
- `GradeHud` shows the stars, `framing · peak · in shot` and an advice line.
- Two new `GradingConfig` tunables were decided by measuring recorded clips: `clipPeakFloor` 0.30 and `clipAbsenceWeight` 0.25.
- `GradingConfig.TryGetConfigProblem` now reports every problem in one warning.

**AC1 — the per-frame grader, sampled throughout, unchanged.**
- The `ShotGrader.cs` diff is empty.
- **Grader control (Phase 0, every full run):** the still `Capture()` and the clip's tick sample were taken in the same LateUpdate. They are identical on every compared tick, counted (composition 0.9927832 = 0.9927832, run1) and miss alike (OutsideFrustum = OutsideFrustum).
- Graded seconds = footage seconds on all 46 clips of run1 and 47 of run3, the 1-tick clip included.

**AC2 — peak coverage and framing blended into a `ClipGrade`, decided by measuring.**
- **A whole framed window reads 100.0 % coverage** in every run (S1), because coverage is measured on the subject's own clock.
- Partial coverage lands near its fraction:
  - stopped 0.75 s in → 48.8-49.2 %;
  - started 0.75 s in → 50.8-51.2 %;
  - the first 0.5 s only → 27.9-28.9 %.
- The decision record is under Decisions: the criteria were committed before any measurement, then the sweep, the table and the runner-up.
- Every live grade equals the sweep's replay of its own ticks: 32/32 clips in run2 (starting config) and 38/38 in run3 (chosen config).

**AC3 — a well-framed miss is worth something; the catch wins clearly.**
- **Missed the peak, same framing (S2, S3):** prints "30 %", 2★, amber (rgb 255 224 79), not red.
- **The whole peak (S1):** 99 %, 5★, green.
- Three stars apart. Coverage moves the grade 69 points, against 11 between two good framings (S5 off-centre, 89 %).
- Whether this *feels* right is Alexv's human check.

**AC4 — a miss with a reason, and the HUD shows every clip's grade.** The S7 misses read:
- nobody live → NoSubject, "there was nobody in the frame";
- too far → TooSmall, "too far away — get closer, or zoom in";
- aimed away → OutsideFrustum, "he was outside the frame";
- behind a rig wall on the occluder layer, confirmed by rendering → Occluded, "something was in the way — move so you can see him".

Each frame was compared with its words. The readout appears after every stop route, at 1920×1080 in SampleScene:
- **Record again:** RT on the virtual gamepad. The press was proven to arrive. Stop press → readout up in 31-39 ms (1 frame).
- **Lowering the camera:** LT release, readout up in 32-48 ms. It stays up for its hold: alpha 0.60-0.76 near the end, camera down.
- **The length limit:** maxClipSeconds 3, restored to 120 and read back.
- **The audio device changing:** `AudioSettings.Reset`.
- **The gallery opened mid-hold:** the readout is gone from the first frame the gallery renders.
- **A clip stopped by the screen owner's suppression:** no readout at all.

The four states were captured at 1080p:
- counted strong (S1, green);
- counted weak (S2, amber);
- miss (S7, red 255 84 71);
- not graded (F1, grey 158 158 168).

Every label measured one line, with no overflow (`cachedTextGenerator.lineCount` and `preferredWidth`). Dashes, never zeroes, appear on misses and on not-graded clips. Colour comes only from `ColorFor`, and percentages only from `GradeText.Percent` (pinned by tests under fr-FR, de-DE and tr-TR). Whether a player understands *why* at a glance is a human check.

**AC5 — cheap, bounded, independent, fail-soft.**
- **Allocation:** 0 / 0 / 0 B and 0 collections over 9,000 full `SampleTick` calls with the live drunk (run3e). The positive control, a retained `new object()` per call, reads 81,920 B, so the instrument sees allocations. A retained `byte[64]` control reads 0 B in this Mono: a measurement quirk, recorded as a lesson.
- **Cost:**
  - `ShotGrader.Grade` costs 32-69 µs per call (7.2 µs in July, before Story 2.2's colliders);
  - `ClipGrader.SampleTick` costs 33-72 µs per tick;
  - zero gen-0 collections during a 10-s graded clip.
- **Release-build bench** (`Builds/1-16-grade-a-clip/bench`, three rounds, 44 fps on this laptop): recorder-plus-grader cost −0.05 / −0.74 / −0.42 ms per frame (mean); per-round median −0.40 / −0.68 / −0.16 ms. That is within noise, with no measurable cost.
  - ⚠️ The bench's camera frames the drunk TooSmall (all three bench clips), so in the build the grader stopped at the size gate and never ran its occlusion raycasts. The full counted path is measured only in the editor (≤ 72 µs per tick, about 0.05 ms per frame at 44 fps).
  - The absolute 60 FPS on a mid-range desktop remains 1.15's human check.
- **Bounded and independent:** ten clips in a row each match a fresh builder's replay of their own ticks. The clip across a respawn keeps two candidates and the better one wins; it does not mix them (EditMode test, plus S8 live).
- **Fail-soft** (Phase F, provoked lines announced at the top of each report). Each case gave one line, no exception, and a clip still announced:
  - no GradingConfig → one Error at Awake, NOT GRADED;
  - no EventManager → one Warning, NOT GRADED;
  - floor and absence at NaN, ±∞, 7 and −3 → the validator's single message, sane grades, no per-tick lines;
  - the drunk deactivated mid-clip → no line, graded from the earlier ticks;
  - the recorder disabled mid-clip → announced with its grade;
  - the audio device changing → its one warning, graded;
  - the drunk DESTROYED mid-clip, then play mode exited while recording → stopped by Disabled, graded and logged, no exception.
- Every run's Editor.log has zero exceptions, and outside the provoked markers no warning or error.

**AC6 — no regression, project restored.**
- EditMode 423/423 (371 before the story, plus 52 new; the spike and bench fence tests included).
- Mutation run: three deliberate breaks each turned tests red (`tests/mutation-run.md`). The ranking rule was caught only after a direct `GradeRanking` test was added.
- Regression rigs: see below.
**Regression rigs re-run (Task 10.2).** I chose them by diffing the shipped code since 1.15 and following who calls the changed members:

| Changed | Rig |
|---|---|
| `ClipRecorder`, `RecordedClip`, `IClipBackend` | Record Clip Shoot |
| `GradeHud` | Grade HUD Shoot |
| `ISubject`, `EventActor`, `GradeBestSubject` | Photo Shoot |
| `PhotoModeController` | Gallery Shoot |

Each rig's evidence folder was parked before its run and restored afterwards, then checked identical (`tools/regress.py`). The fresh runs are in `regression/`.
- **Record Clip Shoot:** the same 95-line report as 1.15's close run.
  - Lines differ only where `RecordedClip.ToString` and the log now carry the grade.
  - The provoked set is unchanged: no "GradingConfig unassigned" line, because the throwaway recorder gets the shipped config.
  - Full sampling path 0 B against a 61,440 B control. 0 exceptions.
  - Evidence: `regression/record-clip-diff.txt`.
- **Grade HUD Shoot** (output to `regression/hud/` through `GradeHudShoot.OutputDir`):
  - the still readout is still hidden on the frame after lowering;
  - Phase B: 0 of 12 thumbnails contaminated;
  - Phase D boundaries identical, apart from two live grades.
  - Evidence: `regression/hud-diff.txt`.
- **Photo Shoot:** 32/32 verdicts and star ratings unchanged.
  - Six shots move 1-2 composition points: the walk-cycle breathing.
  - `i_from_behind` went from 44 % to 23 % at the same 2★ (lesson 42's schedule drift).
  - Evidence: `regression/photo-shoot-diff.txt`.
- **Gallery Shoot:** 298/298 lines identical, digits masked. Evidence: `regression/gallery-diff.txt`.
- **A plain SampleScene play session**, no rig: the drunk through his peak, and a clip recorded and stopped by lowering. It was graded and the readout stayed up after the lowering. Console: only the standing "Head bone not found!" warning.
- **Restored after every run and read back:**
  - the scene, reopened from disk and not dirty;
  - the Game View size;
  - `maxClipSeconds` 120;
  - the `GradingConfig` values;
  - the virtual gamepad removed and the input settings restored;
  - `timeScale` 1;
  - no rig SessionState keys left.
- The five settings files the Windows build re-serialised were restored from HEAD, as CRLF.
- `git status` shows only this story's work.

**Interim, plainly (decision 1h):** a clip now shows its grade, but it is stored nowhere except the recorder's single `LastClip` until Story 1.17's memory card. Stills still reach the gallery, but only the rigs take stills.

**What is left for Alexv** is listed in the result file's human checks:
1. The clip ladder (AC2/AC3).
2. The readout's "why" at a glance (AC4, NFR10), including where the clip states meet 1.19's red.
3. The new hide rule after lowering, and its giving way to the gallery.
4. The visible peak against coverage.

### File List

New:
- `Assets/Scripts/Grading/ClipGrade.cs` (+ `.meta`): `ClipGrade`, `GradeRanking`
- `Assets/Scripts/Grading/ClipGradeBuilder.cs` (+ `.meta`): `ClipGradeBuilder`, `ClipSample`, `ClipBlend`, `ClipScoring` and the blend, framing, miss-rule and presence enums
- `Assets/Scripts/Recording/ClipGrader.cs` (+ `.meta`)
- `Assets/Scripts/Recording/GradeClipShootRunner.cs` (+ `.meta`): rig runner, `#if UNITY_EDITOR`
- `Assets/Scripts/Editor/GradeClipShootRig.cs` (+ `.meta`): `Tools/Grading/Grade Clip Shoot (Play)` and `Tools/Grading/Grade Clip Sweep`
- `Assets/Tests/EditMode/ClipGradeTests.cs` (+ `.meta`)

Modified:
- `Assets/Scripts/Events/ISubject.cs` (`PeakDuration`), `Assets/Scripts/Events/EventActor.cs`
- `Assets/Scripts/Grading/GradingConfig.cs` (clip fields, `SafeClipBlend`, the all-problems validator), `Assets/Scripts/Grading/GradeText.cs` (clip axes, `PeakAdvice`, `ClipMissLong`)
- `Assets/Scripts/Recording/ClipRecorder.cs`, `IClipBackend.cs`, `ReplayBackend.cs` (`Sample` returns its repeats), `RecordedClip.cs` (`Grade`)
- `Assets/Scripts/UI/GradeHud.cs` (the clip channel, `Format(ClipGrade)`, the hide rule), `Assets/Scripts/UI/GradeHudConfig.cs` (tooltip)
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` (comments, `GradeRanking.Beats`, the recorder-config check)
- `Assets/Scripts/Recording/RecordClipShootRunner.cs`, `Assets/Scripts/UI/GradeHudShootRunner.cs` (rigs: wire the new references, fact 12)
- `Assets/Tests/EditMode/RecordingTests.cs` (the `RecordedClip` construction site)
- `Assets/Data/Grading/GradingConfig.asset` (`clipPeakFloor` 0.3, `clipAbsenceWeight` 0.25)
- `Assets/Scenes/SampleScene.unity` (two references: `ClipRecorder.gradingConfig`, `GradeHud.clipRecordedChannel`)
- `_bmad-output/planning-artifacts/game-architecture.md`, `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/sprint-status.yaml`, this story file

Not in git (evidence): `_bmad-output/verification/1-16-grade-a-clip/`; `Builds/1-16-grade-a-clip/bench/`.

## Change Log

- 2026-11-15: Story context created by the loop CREATE phase (ready-for-dev). Six ACs: four verbatim from
  `epics.md`, plus a cost, bounds and fail-soft AC and a no-regression AC. Ten tasks, with nine gating decisions.
  The decision criteria for the blend were written before any measurement exists.
- 2026-11-15: Loop DEV phase (attempt 1) → review. Clips are graded and shown on the HUD:
  - the shipped code: `ClipGrader` (Recording), `ClipGradeBuilder` and `ClipGrade` (Grading, pure), `ISubject.PeakDuration`,
    `RecordedClip.Grade`, the clip readout, and a `GradingConfig` validator that reports every problem;
  - peak coverage measured on the subject's clock (a whole framed window reads exactly 100 %);
  - the blend decided by measuring against criteria committed before any clip existed (floor 0.30, absence weight 0.25,
    presence from any subject), and replicated on a second recording;
  - verified by the Grade Clip rig in SampleScene, its sweep, a release-build bench, EditMode 423/423 (mutation-proven) and
    four regression rigs. Four human checks are queued.
