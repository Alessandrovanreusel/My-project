---
baseline_commit: 94ed9461d6c6c35c47c8d0375d578a8334a1d11f
---

# Story 1.12: Grade-feedback HUD

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to see why my shot scored the way it did right after I take it,
so that I can tell a great shot from a weak one and want to do better.

## Acceptance Criteria

**AC1 — On capture, a HUD shows the star rating AND the three-axis breakdown (FR4, NFR10)**
- Pressing the shutter puts an on-screen readout in front of the player carrying: the **star rating**,
  the **overall percentage**, and the **subject / composition / timing** breakdown. It appears without a
  further input and goes away on its own.
- ✅ **The payload already carries all of it — do not widen the channel.** `ShotGrade` has `Stars`,
  `Percent01`, `Subject01`, `Composition01`, `Timing01`, `MissReason` and `SubjectId`, all as plain
  fields outside any `#if UNITY_EDITOR`, and `ShotGrade.cs:55-60` says in as many words that they were
  put there *for this story*. `ShotCapturedChannel` is `EventChannel<ShotGrade>`; subscribe to it and
  you have everything. Story 1.11 deliberately left the channel type untouched so this story would be
  unaffected.
- ⚠️ **`Subject01` is line-of-sight, not size.** It is "what fraction of him the camera could actually
  see", and it is a *report*, not a multiplier — the subject check is a pass/fail gate
  (`ShotGrade.cs:78-84`). The epic calls it "subject-%". Label it as what it is ("seen 100%", "clear
  view") and never as "how big he was" — the size measurement (`HeightFraction`) lives only in the
  editor-only `GradeDetail` and is **not** available to the shipped HUD.

**AC2 — The HUD never states a number that was never measured**
- ⚠️ **This is the single most-repeated defect class in this project and the HUD is where it does the
  most damage** — it is the one readout the *player* sees. Three separate fixes already exist for it:
  `GradeMiss.Unevaluated` as the zero value (`ShotGrader.cs:10-15`), `GradeDetail.NotEvaluated`
  (`ShotGrader.cs:41-51`), and the debug overlay's own patch to stop printing axes for a rejected shot
  (`PhotoModeController.cs:469-478`).
- Concretely, in the shipped HUD:
  - A **MISS** (`grade.IsMiss`) must print the **reason** and must **not** print composition/timing/subject
    percentages. All three are hard `0f` on a miss because grading early-outs *before* scoring them —
    printing "composition 0% × timing 0%" asserts three measurements that never happened.
  - A **PLACEHOLDER** (`grade.IsPlaceholder`, from a capture with grading unconfigured) must read as
    *not graded*. It is `0%`, `1★`, `GradeMiss.Unevaluated` — a HUD that renders it as "1★ · 0%" tells
    the player they took a terrible photograph when the game never graded one.
  - A **counted shot at 0%** is a real, common state and is **not** a miss: an off-peak shot scores a
    hard 0% → 1★ (timing is the first pillar; ruled *as designed* on 2026-07-28), and all 24 shots in
    the town placement study read `counted — 0% 1★`. It must be visibly different from a miss, and
    the breakdown is what makes it different: `composition 92% × timing 0%` says "you were late",
    which is exactly the "why" NFR10 asks for.

**AC3 — A first-time player can tell a high grade from a low one and understand why (NFR10, GDD slice metric)**
- ⚠️ **This AC cannot be closed by any check you can write.** Whether a readout is legible at a glance,
  and whether a weak grade makes someone want to try again, needs eyes. Produce the evidence — the same
  shot at 5★, at a weak counted grade, at counted-0%, and at each miss — then **hand it to Alexv and
  leave the story open until he has looked** (Task 7). Do not mark it verified from a structural check.
- The GDD's slice criterion is the bar: *"Players can distinguish a high-grade from a low-grade shot and
  understand why"* (`gdd.md:388`). The information is already correct in the log line; this story is
  about whether a person reads it in the second and a half it is on screen.

**AC4 — It reads like a 2000s camcorder/digicam (informational; polish-acceptable)**
- The GDD calls the photo-mode UI *"a key art surface"* that should read like a 2000s camcorder/digicam
  (`gdd.md:322`), and `epics.md` marks this clause **polish-acceptable** — it is not a pass/fail gate.
- Aim for restrained and legible: a fixed-width feel, a thin frame, small caps, the palette already in
  use. **Do not spend the story styling**, and do not build something that must be thrown away.
  Story 1.11 made the same call for the gallery grid and it was the right amount (`GalleryView.cs:27-29`).

**AC5 — Fail-soft, fast, invisible to the gallery, and no regressions (NFR2, NFR3, NFR5, NFR8)**
- ⚠️ **THE HUD MUST NEVER APPEAR IN A STORED PHOTOGRAPH.** `GalleryService.HandleShotCaptured` calls
  `photoCamera.Render()` **synchronously inside `Capture()`**, on the same frame, driven by the same
  channel raise this HUD subscribes to (`GalleryService.cs:152-174`). Subscriber order on that channel
  is registration order, which is component/scene order — i.e. not something you control or should rely
  on. A HUD canvas that is **Screen Space – Camera on the photo camera** and is switched on by its own
  handler can therefore be baked into the thumbnail the gallery stores. See Task 1a.
- Capture-to-feedback stays under **0.2 s** (NFR2). The gallery already spends ~6 ms of that budget on a
  GPU readback (measured: mean 6.036 ms, worst 7.321 ms). The HUD is text and must cost effectively
  nothing — **format once in the handler, never in `Update`**. Measure the shutter with the HUD
  listening and report it against the 0.2 s budget; do not re-assert 1.11's number.
- Every missing reference is independently fail-soft in the established idiom: a missing channel, a
  missing config, a missing label each disables only its own function and logs **once** at `Awake`.
  Capture, grading, flash, shutter and the gallery all keep working with no HUD at all.
- Console shows **zero errors and zero new warnings**. Baseline is the two known pre-existing warnings
  (the Version Control project-link notice and `ThirdPersonCamera.cs:43` "POV Camera: Head bone not
  found!"), plus the two large-triangle mesh warnings the town scene logs on reload.
- No regression to the 1.3–1.11 flow: raise, zoom, capture, flash, shutter, grade, gallery, the
  debug overlay, **the photo-shoot rig** and **the gallery rig** all behave exactly as before.

## Tasks / Subtasks

- [x] **Task 1 — Decide the five things that gate this story (AC1, AC2, AC5) — DO THESE FIRST**

  Each of the five has a recommendation. Record the choice **and the reasoning** in the Dev Agent
  Record, including for the ones you follow — a later reader needs to know it was decided, not defaulted.

  - [x] **1a. Render mode, and how you will PROVE the HUD is on screen. (Blocks everything.)**
        - **Recommended: Screen Space – Overlay**, matching `PhotoViewfinder` and `CaptureFlash`
          (both `m_RenderMode: 0` in `SampleScene.unity`). An Overlay canvas is composited *after* every
          camera and so **cannot** be captured by `cam.Render()` — which is precisely the guarantee AC5
          needs. It also sidesteps the URP post-processing trap that cost Story 1.11 a real defect
          (a Screen Space – Camera canvas composites *before* tonemapping, so a translucent panel over
          the bright town came back as a readable forest — `GalleryView.cs:86-99`).
        - ⚠️ **The cost of that choice is that your rig cannot photograph it with `cam.Render()` either,
          and you must solve that before you build anything.** `GalleryView` went the other way for
          exactly this reason (`GalleryView.cs:15-21`) — but the gallery can never be open during a
          capture, and this HUD is on screen *only* during captures. The trade is reversed here.
        - Settle the capture technique **first**, with a control shot: use
          `ScreenCapture.CaptureScreenshotAsTexture()` inside a coroutine after
          `yield return new WaitForEndOfFrame()`, and before trusting a single HUD image, capture a frame
          you already know contains something (the viewfinder at full alpha, or the gallery open) and
          confirm it is in the picture. ⚠️ **A blank or all-white capture is a KNOWN trap, not a finding:**
          `ScreenCapture.CaptureScreenshot` (the file-writing overload) produced ten images of a UI overlay
          on blank white because the Game View does not repaint its 3D content while the editor runs
          unattended (CLAUDE.md §Traps). If the technique will not produce a trustworthy image, **say so
          plainly and escalate to Alexv** — do not substitute a readout of `Text.text` and present it as
          a picture of the screen.
        - If you choose Screen Space – Camera anyway, you must prove — by running it, with a stored
          thumbnail in your hand — that no HUD pixel reaches a gallery image, for **every** subscriber
          ordering you can produce.
  - [x] **1b. Does the HUD gain the ability to say "not now"?** **Recommended: NO.**
        `PhotoModeController.SetRaiseSuppressed` is a **bool, not a refcount**, and its own doc-comment
        plus `deferred-work.md` both name Story 1.12 as the second caller that breaks it — the first
        closer un-suppresses for everyone (`PhotoModeController.cs:290-293`). A transient readout has no
        business freezing the camera anyway: the player must be able to keep shooting *through* it, and
        chasing a better grade immediately is the loop this story exists to encourage. **Call nothing on
        `PhotoModeController` and the deferred refcount stays deferred.** If you conclude you must
        suppress, the refcount/owner-token fix is a prerequisite and lands before the HUD does.
  - [x] **1c. When does the HUD go away?** Options: a timed hold + fade (like the capture flash), hide on
        camera-lower, hide on the next capture, or a combination. **Recommended: a timed hold + fade,
        restarted by each capture, plus hide-on-lower.** The hold and fade are tunables (Task 3), not
        literals. Hide-on-lower is the cheap fix for the one overlap that can otherwise happen: the
        player captures, releases the raise, and presses Tab — an Overlay HUD draws *over* the
        Screen Space – Camera gallery, so a lingering HUD would sit on top of the gallery grid.
        ⚠️ Hiding on lower reads `PhotoModeController.IsPhotoMode`, which is `UI → PhotoMode` — already
        sanctioned (`game-architecture.md:432`) and read-only, so it does not touch 1b.
  - [x] **1d. Does `ShotGrade` gain a signed peak offset?** **Recommended: YES.**
        `Timing01` names the *axis* that cost the shot; it does not say whether the player was early or
        late, and "click sooner" is the actionable half. `ShotGrader` already reads
        `subject.PeakOffset` for the timing term, so this is one primitive `float` carried through — the
        `CapturedShot` shape is unchanged, so AC3's disk-ready seam from Story 1.11 is untouched (a
        `float` is JSON-serializable where the existing `DateTime` was not).
        - ⚠️ **Do NOT route it through `ShotGrade.Sane()`.** That helper does `NaN → 0` then `Clamp01`
          (`ShotGrade.cs:166`), which for a *signed value in seconds* would turn "never measured" into
          "dead on the peak" and clamp a 2-second miss to 1. Store it raw, keep `float.NaN` as the
          never-measured sentinel, exactly as `GradeDetail.PeakOffset` does
          (`ShotGrader.cs:74-77,117-120`) — and give it a `*Text`-style accessor so no caller can print
          a NaN.
        - The rejection is equally defensible (a smaller diff, one fewer field on a struct three
          stories read). Record whichever you choose with its reason.
  - [x] **1e. What happens to the editor debug overlay?** `PhotoModeController.OnGUI` already draws
        stars, percentage, the three axes and the grader's box for 1.5 s after every capture
        (`PhotoModeController.cs:424-493`). With the shipped HUD live, **two readouts of the same
        capture will be on screen at once**, and every verification screenshot from here on contains
        both. **Recommended: keep it, and make them not collide** — it shows the grader's *internals*
        (projected box, line-of-sight, height vs gate, peak offset) that the player HUD deliberately
        does not, and it earned its place. Put the player HUD somewhere the debug panel is not (the
        panel occupies the top-left, `Rect(12, 12, 640, 120)`), or add an editor-only toggle. Do **not**
        delete it, and do not quietly let them overlap in the evidence you hand over.

- [x] **Task 2 — Grading housekeeping this story is the assigned owner of (AC1, AC2)**
  - [x] ⚠️ **Do not write a second set of star glyphs and miss wording.** `GalleryView` already has
        `Stars(int)` and `Describe(GradeMiss)` as private statics (`GalleryView.cs:766-795`), and the
        HUD needs the same two things. Two copies means the gallery and the HUD can disagree about what
        `Occluded` is called, which is exactly the "reinventing wheels" failure this project's story
        process exists to prevent.
  - [x] **Extract them into `Assets/Scripts/Grading/GradeText.cs`** (`static class GradeText`, namespace
        `CameraGame.Grading`). Both `Gallery` and `UI` may depend on `Grading`
        (`game-architecture.md:430-432`), and the wording is a property of the grade, not of either view.
        Then **change `GalleryView` to call it** and delete its private copies.
        - ⚠️ **Keep the SHORT phrasings exactly as they are.** They are load-bearing and were rewritten
          against evidence: "something in the way" came back from a real run truncated to
          `missed — something in the`, which reads as a broken gallery (`GalleryView.cs:779-782`). The
          HUD has a full-width line and can afford a longer sentence — so expose **both**, e.g.
          `GradeText.MissShort(miss)` (what the gallery uses today, unchanged) and
          `GradeText.MissLong(miss)` (the HUD's fuller "why"). Do not "improve" the short set.
        - This is a refactor of shipped, verified code. **Re-run `Tools > Gallery > Gallery Shoot (Play)`
          afterwards** and confirm the cell captions are byte-identical to Story 1.11's evidence.
  - [x] **Settle `ShotGrade.FromPercent`.** It has **zero production callers** and returns
        `Counted == true` with an all-zero breakdown, so `ToString()` prints
        `ShotGrade(62%, 4★ — composition 0% × timing 0%)` — a grade asserting two measurements it never
        took, which is the very thing AC2 is about. `deferred-work.md` assigns the decision to **this
        story** ("delete it, or make it non-`Counted`, when Story 1.12's HUD lands"). **Recommended:
        delete it.** Confirm zero callers first (`ShotGrade.FromPercent` across `Assets/`), and remove
        the deferred entry when you do.

- [x] **Task 3 — `GradeHudConfig` ScriptableObject + asset (AC4, AC5)**
  - [x] Create `Assets/Scripts/UI/GradeHudConfig.cs`, namespace `CameraGame.UI`, with `[CreateAssetMenu]`.
        Mirror `CaptureConfig.cs` for shape (it is the smallest template in the project) and
        `GalleryConfig.cs` for the validation idiom.
  - [x] Tunables, each with a `[Tooltip]`, a `[Range]`/`[Min]` **and** a `Safe*` accessor: **hold
        seconds** (how long the readout stays at full), **fade seconds**, and the **counted / miss /
        placeholder text colours**. Anything you find yourself typing as a number in the HUD class
        belongs here (architecture §Configuration, §Data Patterns).
  - [x] ⚠️ **`[Range]` is editor-only and this project hand-authors these assets as YAML** — a value
        typed straight into the `.asset` bypasses it entirely. That is why every config here reads
        through a `Safe*` accessor rather than the raw field (`GradingConfig.cs`, `CaptureConfig.cs:22`,
        `GalleryConfig.cs`). Follow it exactly.
  - [x] Add `TryGetConfigProblem(out string)` and warn **once at `Awake`**. Silent numeric
        misconfiguration is this project's single most repeated failure mode — `cueRadius = 0` gave
        total silence with a clean console; `minCoverage = NaN`, `minVisibleSamples = 0` and
        `timingFullSeconds = 0` each disabled a gate invisibly. **Your instances this story:** a
        non-positive hold (a HUD that flickers for one frame and is never read), a negative fade, and a
        hold so long the readout is still up two captures later.
  - [x] ⚠️ **Do NOT add an `OnValidate` that repairs the fields.** `GalleryConfig` did, and the
        2026-07-30 review reproduced the consequence: `OnValidate` runs on asset load *before* any
        `Awake`, so it rewrote the bad value and the warning that existed to report it could then never
        fire — broken exactly where designers author (`GalleryConfig.cs:218-224`). Clamp at the point of
        use via `Safe*`, and let the warning describe the value the designer actually typed.
  - [x] Author `Assets/Data/UI/GradeHudConfig.asset` **as YAML** using the script's `.meta` GUID —
        Unity MCP cannot create custom ScriptableObject assets. Copy the header shape from
        `Assets/Data/Gallery/GalleryConfig.asset`. [[create-so-asset-via-yaml]]

- [x] **Task 4 — `GradeHud`: subscribe, format once, show, fade (AC1, AC2, AC5)**
  - [x] Create `Assets/Scripts/UI/GradeHud.cs`, a MonoBehaviour in `CameraGame.UI`. Serialized refs:
        `ShotCapturedChannel`, `GradeHudConfig`, the `CanvasGroup` it fades, the `Text` labels, and
        (per Task 1c) the `PhotoModeController` it watches for the lower. Resolve each **independently**
        in `Awake` into its own readiness flag and log once if missing — the independence the 1.4 and
        1.5 reviews both praised and `PhotoModeController.cs:99-110` documents.
  - [x] Subscribe in `OnEnable`, unsubscribe in `OnDisable` (architecture §Communication Patterns). A
        `GradeHud` that is destroyed or disabled must leave no live delegate on the channel asset.
        `EventChannel<T>` already snapshots handlers, isolates per-handler exceptions and clears
        subscribers on domain reload (`EventChannel.cs:30-48`) — **do not re-implement any of that.**
  - [x] **Build the strings ONCE, in the handler.** `Update` drives alpha only — the same shape as the
        capture flash (`PhotoModeController.cs:237-243`) and the viewfinder fade
        (`PhotoModeController.cs:217-224`). String interpolation in a per-frame path allocates every
        frame for a value that changes once per shutter press.
  - [x] **Fade on `Time.unscaledDeltaTime`, not `Time.deltaTime`** — matching the flash
        (`PhotoModeController.cs:241`). Capture feedback that freezes with `timeScale` would be wrong
        the day a pause menu lands, and a HUD stuck at full alpha over a paused game is worse than one
        that finishes fading.
  - [x] Drive `Canvas.enabled` as well as `CanvasGroup.alpha` when hidden, for the same reason
        `GalleryView.ApplyOpenState` does (`GalleryView.cs:466-483`): an alpha-0 canvas is still
        geometry submitted every frame, including the frame a photograph is taken on.
  - [x] **The three shapes AC2 requires**, and they must be visibly different from each other:
        - **counted:** stars, percent, and `composition X% × timing Y%` plus `seen Z%`.
        - **miss:** stars (always 1★), the reason in plain words, and **dashes** where the axes would
          be — the exact discipline the debug overlay was patched into
          (`PhotoModeController.cs:469-478`: *"the overlay must not state more than it knows"*).
        - **placeholder:** "not graded". No percentage, no stars presented as a rating.
  - [x] **A second capture while the HUD is still up must restart it cleanly** — new text, hold timer
        reset, no stale line from the previous shot. This project's bugs live almost exclusively in
        reuse and in the second cycle onward.
  - [x] One `GameLog` line at most, at `Awake`. **No logging in the capture handler** — grading already
        logs the full breakdown there (`PhotoModeController.cs:391-394`) and a second line per shutter
        press is spam.

- [x] **Task 5 — Build and wire the HUD in the scene (AC1, AC4, AC5)**
  - [x] Author the canvas in `SampleScene`, following the project's precedent — every UI here
        (`PhotoViewfinder`, `CaptureFlash`, `GalleryCanvas`) is authored in the scene, not as a prefab.
        `Assets/UI/` does not exist and this story does not create it.
  - [x] Render mode and sorting per Task 1a. If Overlay, give it an explicit `sortingOrder` relative to
        `CaptureFlash` and `PhotoViewfinder` and **say which you chose and why** — the flash pulses to
        full white for ~0.12 s at the instant the HUD appears, so whether the HUD is washed out for
        that moment or reads over the top of it is a deliberate call, not an accident. Look at it.
  - [x] Assign `ShotCapturedChannel.asset` and `GradeHudConfig.asset`. Use the built-in
        `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` fallback if no font asset is set —
        that is a built-in **engine** resource, not a project asset, so it is not the `Resources.Load`
        the architecture rules out for game data (`GalleryView.cs:58-61,263`).
  - [x] ⚠️ **If (and only if) the canvas is Screen Space – Camera**, check the camera's culling mask
        against the canvas's layer, as `GalleryView.ConfigureCanvas` does (`GalleryView.cs:246-251`).
        A camera that does not render the layer draws exactly nothing, with a clean console.
  - [x] Check `read_console` after every script change (project rule, NFR5).

- [x] **Task 6 — Verify by running it, and look at what comes out (AC1, AC2, AC5)**
  - [x] **Build a HUD rig on the proven pattern.** Add `Tools > HUD > Grade HUD Shoot (Play)` alongside
        the two that exist. Read `GalleryShootRig.cs` / `GalleryShootRunner.cs` and `PhotoShootRig.cs` /
        `PhotoShootRunner.cs` first — the **runner must live in the runtime assembly behind
        `#if UNITY_EDITOR`** (a MonoBehaviour in an Editor-only assembly cannot be resolved on a scene
        object when entering play mode; the rig silently does nothing while the scene runs on around
        it), and the **rig must restore the previously open scene via `SessionState`**
        (`PhotoShootRig.cs:66-72,131`).
  - [x] **Drive the real shutter** — `photo.Capture()`, never a re-implementation — and produce **every
        HUD state**: a 5★ money shot, a mid counted shot, a **counted-but-0%** late shot (the state that
        makes AC2 matter), and each reachable miss (`NoSubject`, `TooSmall`, `Occluded`,
        `OutsideFrustum`, `BehindCamera`), plus a placeholder capture with grading unconfigured.
  - [x] **Capture the screen for each and write the images to `_bmad-output/verification/hud/`**, plus a
        `hud.txt` recording what the grade actually was for each shot. Then **open them and look**. The
        questions only a picture answers: can you read it in the time it is up? Does the miss line say
        something a player would act on? Do the debug overlay and the player HUD collide?
        - ⚠️ **Write to `_bmad-output/verification/`, never `Temp/`.** Unity deletes `Temp/` entirely on
          editor shutdown; that cost a real handover on 2026-07-27 when photographs Alexv had been asked
          to review were gone before he opened the folder.
        - ⚠️ **Caption what the CAMERA did, never what the grade should be.** Story 1.10's captions were
          rewritten after one reading "just inside the gate" came back rejected — a rig that states its
          own expectations produces confident, wrong evidence.
  - [x] **Prove the HUD cannot reach a stored photograph (AC5).** Capture repeatedly with the gallery
        recording, then write out the **stored gallery thumbnails** and confirm no HUD pixel is in any
        of them. Do it with the HUD at full alpha and mid-fade. This is the one AC where a structural
        argument ("Overlay canvases are composited after cameras") is not enough — the gallery renders
        *inside* the same frame, so look at the file.
  - [x] **Stress it.** Ten captures in rapid succession while the HUD is still up; a capture during the
        fade; the camera lowered mid-hold; the gallery opened mid-hold; two pooled actor respawns so
        the subject id changes underneath. Confirm the readout always describes the **latest** shot and
        never a stale one.
  - [x] **Push the boundaries** — every tunable at zero, negative, huge and absent; the config asset
        missing entirely; the channel unassigned; a label unassigned; the `CanvasGroup` unassigned; a
        capture with nobody in the world. Each must fail soft, log once, and leave capture, grading,
        flash, shutter and the gallery working.
  - [x] **Measure NFR2, do not assert it.** Time the real `Capture()` over many captures with the HUD
        listening and with it disabled, and report both against the 0.2 s budget. 1.11's numbers
        (mean 6.036 ms / worst 7.321 ms with the gallery) are the baseline to compare against — the HUD
        should be lost in the noise, and if it is not, that is the finding.
  - [x] **Suspect the rig before the code.** Every shot identical, every capture blank, a suspiciously
        round number, a screenshot that is all white — treat all of these as a broken rig until proven
        otherwise (see Task 1a: a blank overlay capture is a *known* trap here). And check
        `Library/ScriptAssemblies/CameraGame.dll`'s mtime against your sources before trusting a run:
        `refresh_unity` has returned `success: true` **without recompiling**, and a shoot then ran a
        seven-minute-old assembly and produced a complete, plausible, wrong result.
  - [x] **Restore the project.** Scene, settings, time scale, any config values the rig touched — put
        everything back automatically on the way out. Never leave the editor sitting in the test world.
        Prefer an in-memory `ScriptableObject.CreateInstance<GradeHudConfig>()` for every boundary
        scenario so the shipped asset is never mutated (the pattern 1.11 used, deviation #6).
  - [x] Confirm the console is clean and **1.3–1.11 is unregressed**: a full
        `Tools > Grading > Photo Shoot (Play)` run and a full `Tools > Gallery > Gallery Shoot (Play)`
        run, both compared against their recorded evidence. The gallery run is the direct regression
        test for Task 2's `GalleryView` refactor.

- [x] **Task 7 — Hand the perceptual check to Alexv (AC3, AC4) — name it, do not bury it**
  - [x] AC3 is a **human** criterion and AC4 is a taste call. Write the handover as a short, specific
        ask — *"open these six images, tell me whether you can tell the 5★ from the counted-0% at a
        glance, and whether the miss line tells you what to do differently"* — not a list of things for
        him to go and try. Bring him a conclusion and the evidence behind it.
  - [x] **Keep the story at `review`, not `done`, until he has looked**, and state plainly in the
        handover which ACs remain unproven. Twice now the perceptual check has found what every
        structural check missed (Story 1.10's two shots tying at 5★; Story 1.11's window-resize).

### Review Findings

Code review 2026-08-07 (`94ed946..HEAD`). Three parallel layers: Blind Hunter (diff only, no project
access), Edge Case Hunter (diff + project), Acceptance Auditor (diff + spec + GDD/epics). 20 raw findings
→ 3 decisions, 8 patches, 4 deferred, 5 dismissed as disproven or noise.

Two findings were reproduced by running rather than by reading, and two were **disproven** by running —
which is the point of doing it.

**Decisions (need Alexv — the code cannot be correctly patched without his intent):**

- [x] [Review][Decision] **RESOLVED 2026-08-07 — leave it.** *The "why" line has no composition voice.*
      `whyLabel` is fed only by `GradeText.TimingAdvice`, so composition can never be the reason given
      however dominant it is. A shot at `composition 5% × timing 100%` scores 5% and its only actionable
      sentence is *"right on the moment"* — a compliment on a bad photograph. **Alexv's call: leave it.**
      The axes line already prints `composition 5%`, so the information is on screen; inventing new
      player-facing wording during a code review is the wrong moment for it. **Moved to deferred**, not
      dismissed — the NFR10 "understand why" gap is real and belongs to a story that can design the wording
      properly. `[GradeHud.cs:258]`
- [x] [Review][Decision] **RESOLVED 2026-08-07 — re-shoot the exemplar, do not expand scope.** *The town
      occlusion symptom is the most visible thing in this story's evidence.* `a_money_shot.png` awards
      **94% / 5★ with `line-of-sight 100%`** for a photograph in which the drunk is behind a pine tree and
      the grader's box is drawn over the tree (confirmed by eye during review). Pre-existing Story 1.9
      deferred item, correctly raised rather than silently fixed. **Alexv's call: Story 1.12 does not expand
      to fix occlusion; instead capture a fresh unoccluded 5★ exemplar so AC3 is judged on a fair example.**
      Tracked as a patch item below. The occlusion defect itself stays deferred as its own future story.
- [ ] [Review][Decision] **AC3 and AC4 remain unproven and only Alexv can close them — UNBLOCKED
      2026-09-10.** The exemplars are re-shot and fair; nothing structural is left. This waits only on
      Alexv looking at six pictures. Original note follows.**

**Patches (fix is unambiguous):**

- [x] [Review][Patch] `Canvas.renderMode` is never validated, so AC5's whole guarantee rests on one line of
      scene YAML — **flagged independently by all three layers, including the blind one**.
      `[RequireComponent(typeof(Canvas))]` guarantees a Canvas exists and nothing about its render mode. Flip
      `GradeHudCanvas` to Screen Space – Camera and the grade text bakes into every stored thumbnail with a
      clean console. This class guards every *other* silent-authoring hazard it owns (config ranges, missing
      font, missing labels) and leaves unguarded the one the acceptance criterion depends on. The sibling
      class sets its own (`GalleryView.cs:230`). `[GradeHud.cs:109-157]`
- [x] [Review][Patch] **The readout tells the player to fix the axis that scored full marks** —
      `GradeText.TimingAdvice` uses a hardcoded `0.1s` deadband while the shipped `GradingConfig` awards
      `timing 100%` anywhere within `timingFullSeconds: 0.5`. **Reproduced against the real function and the
      real config: 7 of 12 sampled offsets self-contradict**, e.g. `+0.15s` → axes line *"timing 100%"*,
      why line *"0.2s early — wait for it"*. Already present in the story's own run data
      (`hud.txt:345`), never photographed because the rig only targeted offsets `0`, `-1.25`, `-3.5`. Fix:
      gate on the score, not the raw offset — `if (grade.Timing01 >= 1f) return "right on the moment";`
      `[GradeText.cs:115]`
- [x] [Review][Patch] A grade with `MissReason == Unevaluated` (or `Missed(GradeMiss.None)`) is neither
      `Counted` nor `IsMiss` nor `IsPlaceholder`, so it falls through both guards into the **counted**
      branch and prints `composition 0% × timing 0% · seen 0%` in the counted colour — the exact all-zero
      `Counted` shape `FromPercent` was deleted for in this same diff. Latent, not live: `PhotoModeController.cs:379`
      only raises `Placeholder`/`Scored`/`Missed(real reason)`. `Missed_NeverReportsATimingMeasurement`
      iterates every enum member including `None` and passes, because it only checks `TimingMeasured`. Fix:
      make the fallthrough safe (branch on `grade.Counted`, not on the two negatives) and/or reject
      `None`/`Unevaluated` in `Missed`. `[GradeHud.cs:212-246, ShotGrade.cs:268]`
- [x] [Review][Patch] A **NaN colour channel** defeats both the safe accessor and the validator — `NaN < 0.05f`
      is `false`, so `Visible()` passes it through unrepaired and `ReportInvisible()` stays silent. The
      Color32 conversion of NaN yields 0, i.e. invisible or black text with a clean console. Ironic given
      `ClampFinite` two members up handles NaN explicitly and documents exactly why `Mathf.Clamp` is not
      enough. `[GradeHudConfig.cs:132-133, 211-213]`
- [x] [Review][Patch] `photoMode` unassigned silently drops the `hideOnCameraLowered` guard with **no log at
      all**, unlike every sibling reference in this class. The Overlay readout then draws over the gallery
      grid for the full hold. `[GradeHud.cs:109-157, 315]`
- [x] [Review][Patch] Reassigning `shotCapturedChannel` in the Inspector during play leaks a live delegate:
      both handlers dereference the *current* field, so unsubscribe runs against the new channel and the old
      one keeps a strong reference to a disabled `GradeHud`. Cache the subscribed channel in `OnEnable`.
      `[GradeHud.cs:163-175]`
- [x] [Review][Patch] Three lines of `Missed`'s documentation sit **outside** the closed `</summary>`, so the
      invariant they record (every miss carries NaN, and why) never reaches IntelliSense or a hover tooltip —
      the three places a future caller would look. `[ShotGrade.cs:264-267]`
- [x] [Review][Patch] **Task 7's handover ask was never written.** The task is checked off and an honest
      "⚠️ WHAT I COULD NOT PROVE" block exists (line 724), but not the short specific ask Task 7 required —
      *"open these six images, tell me whether you can tell the 5★ from the counted-0% at a glance"*. The
      three things the record says will be "called out in the handover" are scattered across the Dev Agent
      Record instead. This is the mechanism AC3 closes through. `[this file]`
- [x] [Review][Patch] **Re-shoot the 5★ exemplar with an unoccluded subject** — **DONE 2026-09-10.**
      `a_money_shot.png` is now `100 % · 5★ · right on the moment` with the drunk centred, unoccluded and
      clearly readable; `b_mid_counted` (20 %, 2★) and `c_counted_but_zero` (0 %, 1★) likewise. No shot in
      the set carries an occlusion warning.
      - **Why the first two attempts failed.** The vantage was chosen when the scenario STARTED, but the rig
        then tracks him to his peak for up to 90 s — and he walks. By the shutter he could be behind a tree
        the camera had a clear line to when it was placed. The line-of-sight probe ran at that same early
        instant, so it truthfully reported "nothing in the way" about a moment nobody photographed.
      - **Why no raycast could have caught it.** Characters are ~8.8 m tall, pines 4.9–7.6 m, and the camera
        shoots from the subject's centre height — so rays run 1–7 m up across ~18 m and cross a near pine
        near its APEX, centimetres wide, while its base fills the frame. 9/9 rays clear and `RaycastAll`
        empty were both TRUE and both answered the wrong question.
      - **Fix:** choose the vantage at the SHUTTER, and measure visibility by RENDERING — twelve directions,
        subject renderers on then off, diffed (`VisiblePixelFraction`). Old spot showed him at 0.9 % of the
        frame; the chosen one 4.1 %, against ~3.8 % predicted from geometry. The "he is hidden" warning is
        geometric too, so `e_too_far` is no longer falsely flagged.
      - ⚠️ **Retraction:** the 2026-09-09 claim that `EventActor.Bounds` was broken is **false** and was
        committed before being checked. Feet at Y≈0.00, head bone at Y=6.77, bone cloud 8.09 × 8.76 matching
        the renderer bounds. Full retraction in `deferred-work.md`.
        `[Assets/Scripts/UI/GradeHudShootRunner.cs]`

**Deferred (real, not worth acting on now):**

- [x] [Review][Defer] **The "why" line has no composition voice** — resolved as a decision above; Alexv
      chose to leave it. A shot at `composition 5% × timing 100%` gets *"right on the moment"* as its only
      advice. The axes line carries the number, so nothing on screen is false — but the actionable sentence
      never speaks to the axis that actually cost the shot. `[GradeHud.cs:258]` — deferred, reason: the
      information is already on screen and new player-facing wording should be designed by a story, not
      invented during a code review

- [x] [Review][Defer] `P0` rounding lets `Percent01 = 0.996` render as `100%` — states a perfect photograph
      for one that is not. Cosmetic, and the same `P0` convention shipped in the gallery in 1.11, so changing
      it here alone would make the two views disagree. `[GradeHud.cs:246]` — deferred, pre-existing convention
- [x] [Review][Defer] A hand-authored `holdSeconds: 0` repairs to `0.3s` — which `MinHoldSeconds`' own doc
      calls "a flicker rather than something a person reads" — while the rarer `NaN` repairs to the readable
      `2.2s` default. The comment promises a `0` "fails into something readable"; it does not. The warning
      still fires, so it is loud rather than silent. `[GradeHudConfig.cs:96]` — deferred, warning fires
- [x] [Review][Defer] `IsShowing` is a single bool but its doc claims it distinguishes "up, mid-fade, or
      already gone". It separates gone from not-gone only. Doc overclaim on a rig-facing hook.
      `[GradeHud.cs:107]` — deferred, affects only rig ergonomics
- [x] [Review][Defer] `ShotGrade.ToString()` gained `@ peak {PeakOffsetText}`, moving the recorded baseline
      three rigs diff against. Disclosed by the dev; anyone re-running an older comparison sees a diff.
      `[ShotGrade.cs:291]` — deferred, disclosed and benign

**Dismissed (5)** — recorded so a future review does not re-derive them:
`.meta` files absent (disproven: all ten are in `31edf89`); `TimingMeasured` depends on an undocumented
`GradeMiss` ordering (disproven: `ShotGrader.cs:10-15` documents `Unevaluated = 0` and why);
`hideOnCameraLowered` read raw violates the "every value through `Safe*`" rule (a bool has no unsafe
values); `GradeText.TimingAdvice` is unsanctioned new wording (sanctioned by decision 1d);
`TryGetConfigProblem` deviates from the one-problem-at-a-time idiom (deviates *toward* two standing
deferred items, and is disclosed).

## Dev Notes

### What already exists — read these before writing anything

| Thing | Where | Why it matters here |
|---|---|---|
| The payload, complete | `ShotGrade.cs:62-227` | Stars, Percent01, the three axes, MissReason, SubjectId, IsPlaceholder, Counted, IsMiss — **all of AC1's data, already shipped, deliberately outside `#if UNITY_EDITOR` for this story** (`:55-60`). |
| The channel | `ShotCapturedChannel.cs` · `Core/EventChannel.cs:30-48` | Snapshots handlers, isolates per-handler exceptions, clears on domain reload. Do not re-implement. |
| The capture path | `PhotoModeController.Capture()` — `PhotoModeController.cs:347-395` | Feedback fires first (shutter, flash), then grading, then the raise. Your handler runs inside this call. |
| The other subscriber | `GalleryService.HandleShotCaptured` — `GalleryService.cs:152-174` | Renders the camera synchronously on the same raise. The reason AC5's "never in a photograph" clause exists. |
| A player-facing readout of the same data | `GalleryView.DescribeShot` / `Stars` / `Describe` — `GalleryView.cs:735-795` | The wording and the star glyphs to share, not to copy (Task 2). Also the worked example of "stars alone are not enough". |
| The developer-facing readout | `PhotoModeController.OnGUI` — `PhotoModeController.cs:424-493` | What the shipped HUD must *not* duplicate, and the "never state more than you know" discipline to copy. |
| The fade idiom | `PhotoModeController.cs:217-243` | CanvasGroup alpha driven in `Update` via `MoveTowards`, unscaled time for the flash. |
| A config to copy | `CaptureConfig.cs` (smallest) · `GalleryConfig.cs` (the `Safe*` + `TryGetConfigProblem` idiom, **and** the `OnValidate` mistake not to repeat) | House style for tunables. |
| The rigs | `Assets/Scripts/Editor/PhotoShootRig.cs` + `PhotoMode/PhotoShootRunner.cs`; `Editor/GalleryShootRig.cs` + `Gallery/GalleryShootRunner.cs` | The pattern for Task 6, and the two regression suites Task 6 must re-run. |

### Architecture compliance (non-negotiable)

- **Location & namespace:** `Assets/Scripts/UI/`, namespace `CameraGame.UI`; the config asset in
  `Assets/Data/UI/`. Both folders are new — see Project Structure Notes below for why this is the
  sanctioned home and not a variance.
- **Dependency direction:** `UI` may depend on `PhotoMode` and `Grading`, never the reverse
  (`game-architecture.md:430-432`). Nothing in `PhotoMode`, `Grading`, `Events` or `Gallery` may learn
  that a HUD exists. **Do not add a `Gallery` ↔ `UI` edge in either direction** — the two never need to
  talk; if the HUD must yield to the gallery, it does so by reading `PhotoModeController.IsPhotoMode`
  (Task 1c), which is already a sanctioned direction.
- **One assembly.** Everything runtime goes in `CameraGame.asmdef`. Editor-only tooling goes in
  `CameraGame.Editor.asmdef` — **except a MonoBehaviour the rig puts on a scene object**, which must be
  in the runtime assembly behind `#if UNITY_EDITOR`.
- **Tunables in a ScriptableObject, never as literals** (architecture §Configuration, §Data Patterns),
  assigned via `[SerializeField]` — no `Resources.Load` for game data, no `FindObjectOfType`, no
  singletons.
- **Naming:** `PascalCase` types and methods, `_camelCase` privates, `IPascalCase` interfaces.
- **Fail-soft, never throw into `Update`** (architecture §Error Handling, NFR8). Errors are never shown
  to the player — including by this HUD, which is the one thing on screen that could.
- **Subscribe `OnEnable`, unsubscribe `OnDisable`** (architecture §Communication Patterns).
- **Cache in `Awake`; never `GetComponent`/`Camera.main` in a per-frame path.**
- **Debug/gizmo code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.** The HUD itself **ships** — it is
  not debug code, and this is the whole point of `ShotGrade` carrying the breakdown as plain fields.
- **URP only.** If a UI material is ever needed, use a URP-compatible shader; the Standard shader will
  not render.
- **Check `read_console` after every script change.** A clean console is a project rule (NFR5).
- **Verify by running, not by reading** — build the world, drive the real path, capture the output, and
  look at it. Restore the project when the rig finishes. (CLAUDE.md §Verifying Your Own Work.)
- **Jira sync is mandatory** — this story is **KAN-23** (project KAN, cloud
  `5b116b91-787f-4ff7-9668-2cd92d337bcf`; transitions 11=To Do, 21=In Progress, 31=Review, 41=Done).
  Mirror the Tasks above as Jira Subtasks under KAN-23 with a plain-language comment on each, and keep
  their status in step with the checkboxes here.
- **Git:** `_bmad-output/` is gitignored — story files are local only. Commit *and* push in the same
  session. [[always-push-changes-to-git]]

### The one genuinely new risk in this story

Every previous story could be proven by rendering a camera to a texture. **This one cannot** — the thing
under test is, by design, the layer that a camera render does not contain. That inverts this project's
most reliable verification technique, and the failure mode is seductive: a rig that reads
`Text.text == "★★★★☆  78%"` will pass happily while the label sits off-screen, behind the flash, at
alpha 0, in a font that does not render the glyph, or at 6 points on a 4K display. **Every one of those
is a real, shipped-looking bug that a structural check calls green.** Settle the screen-capture
technique in Task 1a before writing the HUD, prove it with a control shot, and if it will not work,
say so and stop — do not let a `Text.text` readout stand in for a photograph of the screen.

### Project Structure Notes

- `Assets/Scripts/UI/` and `Assets/Data/UI/` do **not** exist yet and are created by this story. This is
  the architecture's own home for this work, not a variance: the directory structure lists
  `UI/ # Viewfinder overlay, grade-feedback HUD` (`game-architecture.md:381`) and §Architectural
  Boundaries names `UI` as a tier that depends on `PhotoMode`/`Grading` (`:432`). Story 1.1's scaffold
  predates the need and simply did not create it.
- The architecture's `Assets/UI/` entry describes UI **assets**; this project authors its canvases
  directly in `SampleScene` (`PhotoViewfinder`, `CaptureFlash`, `GalleryCanvas`) and Story 1.11 confirmed
  that precedent. Follow it — do not introduce a prefab hierarchy for one canvas.
- `Assets/Scripts/Player/` exists and is empty — the `ThirdPersonController`/`ThirdPersonCamera` rename
  the architecture recommends has never been done. **Do not do it in this story**; it would bury a HUD
  diff under a project-wide rename.
- Nothing in this story needs an `.asmdef` change. `CameraGame.Editor.asmdef` already references
  `Unity.InputSystem` (added by 1.11) if the rig needs it.

### Previous Story Intelligence

From Story 1.11 (closed `done` 2026-07-30 after a three-layer code review: 13 patches applied and
re-verified by running, 3 decisions ruled on by Alexv, 10 deferred, 4 dismissed as disproven):

1. **Running it found four defects that reading it did not**, and one of them *only the real scene could
   produce* — the gallery backdrop leaked the town through URP post-processing, invisible in the rig's
   grey-plane world. **Your equivalent: verify in `SampleScene`, not only in the rig's private world.**
   A HUD that is legible over a flat grey plane may be unreadable over a bright sky.
2. **A rig that lies produces findings that are not there.** 1.11's `f_behind_wall` scenario omitted
   `wall: true` and reported "visible 100%, counted" under a caption promising a wall — one sentence
   from being written up as a reproduction of Story 1.9's deferred occlusion bug. The photograph caught
   it, because there was no wall in the picture.
3. **Silent numeric misconfiguration remains the recurring failure mode** — `cueRadius = 0`,
   `minCoverage = NaN`, `minVisibleSamples = 0`, `timingFullSeconds = 0`, `cellSize.x = 0`. Each
   disabled a feature with a clean console. Validate and warn once (Task 3).
4. **`OnValidate` can destroy the evidence its own validator needs** — the 1.11 review reproduced
   `GalleryConfig` repairing a hand-authored `0` before `Awake` could warn about it. Task 3 forbids
   repeating it.
5. **The review layers generate hypotheses, not conclusions.** Four of 1.11's findings were disproven by
   running them; six of 1.10's likewise. Reproduce before reporting.
6. **`refresh_unity` can report success without recompiling** — check the assembly's mtime before
   trusting any run.

From Story 1.10: the headline defect (two clearly different photographs tying at 5★) was invisible to
every structural check and was settled by Alexv comparing two pictures. From Story 1.9: the bug that
mattered — a 5★ photograph of nothing — was found by *looking at a picture*. From 1.6/1.7: **pooled
objects do not go null**, so a stale `ISubject` quietly becomes a different drunk; read live, at the
shutter.

### Inherited context you should not rediscover

- **An off-peak shot scores a hard 0% → 1★, identical on stars to a total miss.** Resolved as
  *designed* on 2026-07-28 (timing is the first pillar). `MissReason` and the axis breakdown are the
  only things that separate them — which is the core of AC2, and the reason this HUD is not just a
  star count.
- **`RaiseSuppressed` is a bool and cannot express two owners.** `deferred-work.md` and
  `PhotoModeController.cs:290-293` both name Story 1.12 as the caller that would break it. Task 1b's
  recommendation is to not become that caller.
- **`ShotGrade.FromPercent` is dead code** producing a `Counted` grade with an all-zero breakdown. This
  story is the assigned owner of the decision (Task 2). **Do not call it.**
- **The gallery shows only what fits on one screen (18 of 50 unreachable at the shipped cap), and a
  photograph cannot be selected or enlarged.** Both accepted by Alexv as out of scope and folded into a
  future Epic 5 "browsable gallery" story. **Not this story's problem** — do not fix them here.
- **In the real town the occlusion gate appears inert** — 24 of 24 shots reported `line-of-sight 100%`,
  including one scoring 98% / 5★ for a photograph of a tree trunk. Reproduced, undiagnosed, logged
  against Story 1.9. **Expect it in your evidence:** a HUD may legitimately read `seen 100%` beside a
  photograph of foliage. Do not chase it, and do not record it as a HUD defect. (It is worth noting
  `deferred-work.md` says this should be investigated "before Story 1.12's HUD ships, because a 5★
  awarded for a photograph of a tree is exactly what the player will screenshot and complain about" —
  raise it with Alexv as a scope question if the evidence makes it glaring, but do not silently
  expand this story to include it.)
- **The `Subject` layer (8) is excluded from `occluderMask`**, and `TagManager.asset` lost its URP
  rendering-layer *names* in Story 1.9 (verified harmless). Neither affects this story; both are logged
  so you do not rediscover them.

### Git Intelligence

- `b725270` **Story 1.11 review: 13 patches, and the safety net that was silently repairing itself** —
  the `OnValidate` trap (Task 3), the widened `try` in `TryRenderThumbnail` that exists so a later
  subscriber *(this HUD)* still runs, and the `OnDisable` suppression-release contract.
- `244e9ca` **the thumbnail width follows the window instead of being authored** — why the gallery's
  stored aspect is derived per capture; relevant if your evidence compares HUD screenshots to
  thumbnails.
- `6490b61` **wire the gallery into the scene, then let running it find four bugs** — the scene-wiring
  pattern and the four running-found defects.
- `4e603c4` **Story 1.10: five stars was a magic number, not a design decision** — the `StarScale`
  shape the HUD renders, and why `Stars` is a stored field.
- `fe3ab71` **Story 1.10 review: stop the grader reporting numbers it never measured** — the
  `NotEvaluated` / `Unevaluated` sentinel discipline that AC2 is a direct continuation of.
- `409bf76` **Story 1.9: photograph the real capture path** — the rig you are extending and the three
  Unity traps it already paid for.

### Latest technical information (verified for this project's versions)

- **Unity 6.3 LTS (6000.3.8f1), URP 17.3.0, Input System 1.18.0** — all installed and current; no
  upgrade is part of this story. This story adds **no input action** — the HUD is driven entirely by
  the capture channel.
- **uGUI `Text` (`UnityEngine.UI`) is what this project uses**, not TextMeshPro. `GalleryView` renders
  its star glyphs and captions through `Text` with `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
  and the `★`/`☆` glyphs were confirmed to render in the 1.11 run. Match it — introducing TMP for one
  HUD adds a package-level dependency and a second text stack for no proven benefit.
- **Screen Space – Overlay canvases are composited after all cameras** and are therefore absent from
  `Camera.Render()` output — the guarantee AC5 needs, and the obstacle Task 1a must solve.
- `ScreenCapture.CaptureScreenshotAsTexture()` reads the current backbuffer and **does** include Overlay
  UI, but must be called after `yield return new WaitForEndOfFrame()` in a coroutine. Treat its output
  as suspect until a control shot proves it (CLAUDE.md §Traps: the file-writing overload produced ten
  blank-white images).
- `Time.unscaledDeltaTime` is the correct clock for capture feedback; the existing flash already uses it.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.12: Grade-feedback HUD] — AC source (lines 516–534)
- [Source: _bmad-output/planning-artifacts/epics.md#FR4] — blended 3-dimension grade, 1–5 stars
- [Source: _bmad-output/planning-artifacts/epics.md#NFR2, #NFR5, #NFR8, #NFR10] — 0.2 s capture budget, clean console, fail-soft, grade legibility
- [Source: gdd.md:184-192] — the three grading dimensions the HUD must name
- [Source: gdd.md:198-211] — star boundaries, the gate, and the timing window the percentages come from
- [Source: gdd.md:322] — the 2000s camcorder/digicam art direction for the photo-mode UI
- [Source: gdd.md:387-388] — the slice success criterion, and "players understand *why*"
- [Source: game-architecture.md:381,428-446] — `UI/` as the home for the grade-feedback HUD; the dependency directions, including `Gallery → PhotoMode` and why `SetRaiseSuppressed` is general
- [Source: game-architecture.md:302-331] — Configuration and Event System patterns (SO tunables; subscribe/unsubscribe)
- [Source: game-architecture.md:579] — "the miss reason and the per-axis breakdown must survive into a release build for Story 1.12's HUD"
- [Source: game-architecture.md:615-624] — the Consistency Rules table this story is reviewed against
- [Source: Assets/Scripts/Grading/ShotGrade.cs:47-60] — the breakdown is not editor-only, and it was made so for this story
- [Source: Assets/Scripts/Grading/ShotGrade.cs:103-143,163-211] — SubjectId, HasSubject, Counted, IsMiss, `Sane()`, `Missed`, `Placeholder`, `FromPercent`
- [Source: Assets/Scripts/Grading/ShotGrader.cs:6-35] — `GradeMiss`, and why `Unevaluated` is the zero value
- [Source: Assets/Scripts/Grading/ShotGrader.cs:37-120] — `GradeDetail`, `NotEvaluated`, and the `*Text` accessors that print "n/a" rather than a fabricated number
- [Source: Assets/Scripts/Core/EventChannel.cs:30-48] — the raise/subscribe plumbing you must not re-implement
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:99-110] — independent fail-soft readiness flags
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:215-243] — the fade idiom and the unscaled-time flash
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:273-303] — `SetRaiseSuppressed`, and the bool-not-refcount warning naming this story
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:347-395] — the capture path your handler runs inside
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:424-493] — the editor debug overlay: what not to duplicate, and the "must not state more than it knows" patch
- [Source: Assets/Scripts/Gallery/GalleryService.cs:138-215] — the other subscriber, its synchronous camera render, and the widened `try` that exists so this HUD still runs
- [Source: Assets/Scripts/Gallery/GalleryView.cs:10-30,86-99] — Screen Space – Camera vs Overlay, and the URP post-processing leak
- [Source: Assets/Scripts/Gallery/GalleryView.cs:466-483] — driving `Canvas.enabled` as well as alpha
- [Source: Assets/Scripts/Gallery/GalleryView.cs:735-795] — the star glyphs and miss wording to share, and why the short phrasings are load-bearing
- [Source: Assets/Scripts/Gallery/GalleryConfig.cs] — the `Safe*` + `TryGetConfigProblem` idiom, and the `OnValidate` mistake not to repeat
- [Source: Assets/Scripts/PhotoMode/CaptureConfig.cs] — the smallest config template in the project
- [Source: Assets/Scripts/Editor/PhotoShootRig.cs:66-72,131] — the `SessionState` scene-restore contract
- [Source: Assets/Scripts/PhotoMode/PhotoShootRunner.cs:26-32] — the runtime-assembly rule for rig MonoBehaviours
- [Source: _bmad-output/implementation-artifacts/1-11-minimal-gallery.md#Review Findings] — the 13 patches, the 4 disproven findings, and the deferred items this story inherits
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — `FromPercent`, the `RaiseSuppressed` refcount, the town occlusion symptom, and the browsable-gallery items
- [Source: CLAUDE.md#Verifying Your Own Work] — build the rig, run the real path, look at the output, restore the project, and say what needs a human
- [Source: CLAUDE.md#Jira Sync] — the mandatory KAN mirror, including subtasks and plain-language comments

## Dev Agent Record

### Agent Model Used

Claude Opus 5 (1M context) — `claude-opus-5[1m]`, via Claude Code + MCP for Unity.

### Task 1 — the five decisions, and why

Recorded for all five, including the ones that follow the recommendation, so a later reader knows each
was decided rather than defaulted.

**1a. Render mode: Screen Space – Overlay (followed the recommendation).**
An Overlay canvas is composited after every camera, so it *cannot* appear in `cam.Render()` output. That
makes AC5 **structural** rather than a matter of subscriber order — which matters because
`GalleryService.HandleShotCaptured` renders the camera synchronously inside the same channel raise, on the
same frame, in an order nobody controls. It also sidesteps the URP trap that cost Story 1.11 a real defect
(a Screen Space – Camera canvas composites before tonemapping). The HUD sits on its own canvas at
`sortingOrder 100`, above `PhotoViewfinder`/`CaptureFlash` at 0 — see 1a-bis below.

**The capture technique was settled first, with control shots, before any HUD code was written.**
`ScreenCapture.CaptureScreenshotAsTexture()` after `yield return new WaitForEndOfFrame()`, with the camera
left rendering to the **screen** (both existing rigs bind a RenderTexture for the whole run; this one must
not). `GradeHudShootRunner` Phase 0 proves it every run with three control shots whose answers are already
known — world-only (must not be uniform or near-white), a full-screen pure-magenta Overlay marker (must
dominate), marker off again (must return to ~0). Measured on the recorded run: **std-dev 0.132, near-white
0.0%, marker 100.00%, released 0.00%.** The rig prints `THE TECHNIQUE IS TRUSTWORTHY` only when all three
pass, and every later phase is labelled with that verdict.

**1a-bis. Sorting order: the HUD draws OVER the capture flash.** `CaptureFlash` lives on the
`PhotoViewfinder` canvas at `sortingOrder 0`; the HUD canvas is 100. The flash pulses to full white for
~0.12 s at exactly the instant the readout appears, so this is a deliberate call, not an accident. **I
looked at it.** The HUD is not *covered* by the flash — but its panel is translucent, so for that eighth of
a second the white flash shines *through* it and the readout is visibly bleached
(`*_at_shutter.png`). Over a 2.8 s readout that is 4% of its life, and it reads as part of the shutter pop
rather than as a fault, so it was left alone. It is called out for Alexv in the handover because it is a
taste call, not a correctness one.

**1b. The HUD does NOT gain the ability to say "not now" (followed the recommendation).**
Nothing in `GradeHud` calls anything on `PhotoModeController`; it only *reads* `IsPhotoMode`, which is
UI → PhotoMode and already sanctioned. `SetRaiseSuppressed` is still a bool with exactly one caller (the
gallery), so **the deferred refcount stays deferred and this story did not become the second caller.**
Beyond avoiding that trap, freezing the camera for a transient readout would be wrong on its own terms:
the player must be able to keep shooting *through* it, because chasing a better grade immediately is the
loop this readout exists to encourage.

**1c. It goes away on a timed hold + fade, restarted by each capture, plus hide-on-lower (followed the
recommendation — with one addition).** Hold 2.2 s, fade 0.6 s, both tunables. Hide-on-lower reads
`IsPhotoMode` only.
⚠️ **The addition, and why it is not scope creep:** `RaiseCamera` is a *hold* (RMB held), so hide-on-lower
means releasing the button takes the grade off screen instantly — a player who clicks and lets go may never
read it, which is squarely against AC3. The overlap it prevents is real too (an Overlay HUD draws over the
Screen Space – Camera gallery). Both readings are defensible and the difference is a **feel** decision, so
it is a config switch (`hideOnCameraLowered`, default true) rather than a line of code. Alexv can flip it
without a recompile; it is named explicitly in the handover.

**1d. `ShotGrade` DOES gain a signed peak offset (followed the recommendation).**
`Timing01` names the axis that cost the shot but not the direction — 20% is the same number two seconds
early and two seconds late, and "click sooner" is the actionable half. One primitive `float` carried
through `ShotGrader` → `ShotGrade.Scored`; `CapturedShot`'s shape is unchanged, so Story 1.11's disk-ready
seam is untouched. **It is stored raw and does NOT go through `Sane()`** — that helper does NaN → 0 then
`Clamp01`, which for a signed value in seconds would turn "never measured" into "dead on the peak" and
clamp a two-second miss to 1. NaN is kept as the never-measured sentinel, and `TimingMeasured` /
`PeakOffsetText` also check the miss reason so a zeroed `default(ShotGrade)` — whose raw offset really is 0
— can never be reported as perfect timing. `Scored`'s parameter is **required, not defaulted**: a caller
that forgot it would silently ship "dead on the peak" on every shot.

**1e. The editor debug overlay stays, and they do not collide (followed the recommendation).**
`PhotoModeController.OnGUI` shows the grader's *internals* — the projected box, line-of-sight, height vs
the gate — which the player HUD deliberately does not. It occupies `Rect(12, 12, 640, 120)` (top-left); the
player HUD is a bottom-centred band. **Confirmed by looking at every Phase A photograph:** the two never
touch.

### Debug Log References

- `_bmad-output/verification/hud/hud.txt` — the full run: Phase 0 (capture-technique control shots),
  Phase A (every readout state), Phase B (AC5), Phase C (stress/reuse), Phase D (boundaries), Phase E (NFR2).
- `_bmad-output/verification/hud/*.png` — 9 states × 2 moments (settled + at-shutter), 3 control shots,
  4 stress frames, 3 stored gallery thumbnails.
- `_bmad-output/verification/hud-motion/motion-review.md` — the temporal half: frame analysis of the
  recorded readout, the Gemini video review, and a claim-by-claim verification of it.
  `hud_readout.mp4` + `frames/` are the recording itself.
- `_bmad-output/verification/gallery/` — re-run of `Tools > Gallery > Gallery Shoot (Play)`, the direct
  regression test for Task 2's `GalleryView` refactor.
- `_bmad-output/verification/photo-shoot/` — re-run of `Tools > Grading > Photo Shoot (Play)`.

### Completion Notes List

**What was built.** `GradeHud` (a MonoBehaviour on a Screen Space – Overlay canvas authored in
`SampleScene`) subscribes to `ShotCapturedChannel`, formats three lines **once in the handler**, shows them,
and drives alpha in `Update` on `Time.unscaledDeltaTime` — the same shape as the capture flash. It drives
`Canvas.enabled` as well as alpha when hidden, so a hidden readout is not geometry submitted on the frame a
photograph is taken. Tunables live in `GradeHudConfig` (`Assets/Data/UI/GradeHudConfig.asset`, hand-authored
YAML) behind `Safe*` accessors, with a `TryGetConfigProblem` that reports **every** problem at once (both
existing configs return on the first, and both carry a standing deferred item saying they should not) and
**no `OnValidate`**, so the warning can still describe the value the designer actually typed.

**AC1 — satisfied, verified by running.** The readout carries stars, overall percentage and the
subject/composition/timing breakdown, appears with no further input and goes away on its own. The channel
type was not widened; `ShotGrade` already carried everything except the peak offset (decision 1d).
`Subject01` is labelled **"seen"**, never as size — it is line-of-sight, and the size measurement lives only
in the editor-only `GradeDetail`.

**AC2 — satisfied, and this is the one the pictures settle.** Three visibly different shapes, all
photographed:
- `c_counted_but_zero.png` → `★☆☆☆☆ 0 %` in cream, `composition 95 % × timing 0 % · seen 100 %`,
  `3.6s late — shoot sooner`.
- `e_too_far.png` → `★☆☆☆☆ MISSED` in **salmon**, `composition — × timing — · seen —`,
  `too far away — get closer, or zoom in`.
- `i_not_graded.png` → `NOT GRADED`, dashes, `this shot was never graded`. No percentage, no stars
  presented as a rating.
Both counted-0% and every miss read 1★, and they are unmistakably different in the photographs: colour, the
word MISSED vs a percentage, dashes vs numbers, and different advice. **No miss prints an axis percentage**
— all three are hard `0f` because grading early-outs before scoring them.

**AC5 — satisfied, verified by looking at the files, not by the structural argument.** For Phase B the
readout is repainted **pure magenta** (panel and text), which turns "did a HUD pixel reach this thumbnail?"
into a count. 12 captures, at full alpha and mid-fade: **12/12 thumbnails scanned, 0 contaminated, worst
magenta fraction 0.000%.** Three thumbnails are written out so the scan can be confirmed to be scanning
photographs and not blank rectangles. The gallery-overlap case is photographed too
(`stress_gallery_over_hud.png`): the gallery is open with no readout on it.

**NFR2 — measured, not asserted.** Real `Capture()` × 40, in the real town: **HUD listening mean 39.882 ms
/ worst 85.127 ms; HUD disabled mean 40.804 ms / worst 84.934 ms.** The HUD is inside the noise (marginally
*faster* with it on) and the worst sample is identical with it off, so the cost is the town's GPU readback,
not the readout. Well inside the 0.2 s budget. ⚠️ These are much larger than Story 1.11's 6.036 ms / 7.321 ms
because **1.11 measured in a private grey-plane world and this measures in the real town** (16k+ colliders,
a real scene to read back) — the two numbers are not comparable, and the honest comparison is the
listening-vs-disabled pair above.

**Fail-soft (AC5) — every case exercised, all soft.** Hold at 0 / negative / 9999 / NaN, fade at negative /
NaN / 9999, all three colours at alpha 0, config asset missing, channel unassigned, labels unassigned,
CanvasGroup unassigned. Each disabled only its own function, logged **once** at `Awake`, and left capture,
grading, flash, shutter and the gallery working (the gallery kept storing through every one).

**Console (NFR5) — clean.** After the final regression runs the console holds only the two known
large-triangle mesh warnings. Every `[GradeHud]`/`[Grading]` line during a rig run is a scenario the rig
provokes on purpose, and `hud.txt` names them up front so a clean-console check is not confused —
"any OTHER error is a finding".

**Regressions — none.** `Tools > Grading > Photo Shoot (Play)`: **0 differing lines** against the recorded
baseline (numbers masked). `Tools > Gallery > Gallery Shoot (Play)`: the only structural difference is the
`@ peak …` clause deliberately added to `ShotGrade.ToString()`; the gallery's cell captions render
byte-identically (`ui_open.png` — "missed — out of frame", "missed — blocked", "missed — too far away",
"missed — nobody there", "0 % · TownDrunk", star glyphs intact, no truncation). 140/140 EditMode tests pass.

**Task 2 housekeeping, both done.** `GalleryView.Stars`/`Describe` extracted to
`CameraGame.Grading.GradeText` and the private copies deleted; the **short** phrasings are byte-identical
(pinned by `GradeTextTests` against the exact literals, with the truncation story in the test's comment) and
a longer `MissLong` set was added for the HUD's full-width line. `ShotGrade.FromPercent` **deleted** — zero
production callers confirmed across `Assets/` before removal, and its all-zero-breakdown `Counted` grade was
the exact thing AC2 forbids. The deferred entry is removed.

**Two defects the rig found in itself, both fixed and re-run** (recorded because "suspect the rig before
the code" earned its place again):
1. **The rig lied about an empty street.** `manager.enabled = false` disables the manager's `Update`; it
   does not despawn anything. In the real scene a drunk was already walking, so `d_nobody_there` came back
   `composition 93% of TownDrunk, seen 100%` under a caption promising nobody was there — Story 1.11's
   `f_behind_wall` failure exactly, and one sentence from being written up as a HUD defect. Fixed by
   deactivating every live actor for that shot (which is what `GradeBestSubject` actually keys on) and
   restoring them; it now genuinely produces `GradeMiss.NoSubject`.
2. **Every Phase A photograph was taken at the worst possible instant.** The rig grabbed one frame after
   the shutter, i.e. peak capture-flash, and every picture came back bleached — which I nearly read as a
   legibility defect. Now both moments are captured: `<name>.png` 0.5 s later (the frame to judge
   legibility from) and `<name>_at_shutter.png` (the flash moment, kept because the player sees it).
   Also fixed: `h_behind_you` at 1.2 heights produced `OutsideFrustum`, so there was **no** picture of the
   `BehindCamera` wording at all; at 0.05 heights it now reaches it.

**One defect in shipped code the rig found:** `GradeHudConfig`'s explanation was one fixed sentence per
field, so the run printed *"a hold at or below zero is a readout that appears for a single frame"* under a
hold of **9999** and again under **NaN** — the number was right and the advice described neither mistake.
Now the explanation follows the mistake (`WhyHold`/`WhyFade`), pinned by a test.

**Motion verification (`tools/verification/`) — frame analysis first, then video.** A still cannot answer
"does it arrive and leave cleanly" or "is it up long enough". `Tools > HUD > Grade HUD — Record the readout
(Play)` records the screen frame by frame (the existing `RigVideoFeed` films a `RenderTexture` and therefore
cannot see an Overlay canvas — the same inversion as the stills, one dimension along).
**Measured:** onset **1 frame**; steady for ~2.1 s with no flicker (flat 0.253 plateau); exit **monotonic and
linear** over 0.54 s; present **2.62 s** in total. Panel-band darkening +0.159 while up vs +0.007
before/after, measured against a control band above it so the moving town cancels out.
⚠️ **A trap paid for here:** `Time.captureFramerate` does **not** lock `Time.unscaledDeltaTime`, which is the
clock the fade runs on — the first recording played ~11% fast. The rig now measures the fade's own clock and
prints the honest encode rate (29.78 fps).
**Gemini** (`gemini-3-flash-preview`, no timestamps supplied, no defect named): its headline claim — *"visible
for approximately one second"* — is **disproven by measurement (2.62 s, off by 2.6×)**, and the readability
conclusion resting on it is therefore not usable. Everything descriptive it said *was* confirmed: instant
onset, smooth linear stutter-free exit, stable UI, good contrast over the bright trees. It also **corrected
me**: the shutter flash washes the panel for only **2 frames (~0.07 s)** and the text stays legible through
them — my earlier "bleached for 0.12 s" overstated it, because every still had been taken on exactly that
frame. Most useful result: asked with no context what the panel communicates, it read stars → 0% → breakdown
→ reason, in that order, and called it a rating for a captured photo. Full claim-by-claim verification in
`motion-review.md`.

**⚠️ WHAT I COULD NOT PROVE, AND WHAT NEEDS ALEXV (see Task 7).** AC3 and AC4 are perceptual and are
**open**. Motion analysis settles that nothing stutters and that the readout is up for 2.6 s; it cannot
settle whether 2.6 s is long enough for someone playing rather than analysing. Also: the run's Game View was **574×494**, which is small — the canvas scales with the screen so
the *layout* in the pictures is faithful, but "is the text big enough" cannot be settled from them and
`hud.txt` says so.

**⚠️ RAISED, NOT FIXED — the town occlusion symptom is now glaring.** `a_money_shot.png` scores **94% / 5★
with `line-of-sight 100%`** for a photograph in which the drunk is behind a pine tree and not visible; the
grader's box is drawn over the tree. This is the reproduced-but-undiagnosed symptom logged against Story 1.9
(24/24 shots read 100% in the town placement study), and `deferred-work.md` says it should be investigated
"before Story 1.12's HUD ships, because a 5★ awarded for a photograph of a tree is exactly what the player
will screenshot and complain about". The story instructs me to raise it as a **scope question** rather than
silently expand this story, so: **not fixed here, and it is now the most visible thing in this story's own
evidence.** Alexv's call.

### Completion note — 2026-09-10 (the re-shoot, and a retraction)

**All nine review items are now closed. The only thing left in this story is Alexv looking at six pictures.**

**I got the diagnosis wrong yesterday and want that on the record plainly.** I reported that
`EventActor.Bounds` returned a box floating 4 m above the drunk from a `SkinnedMeshRenderer` scale-chain
fault, wrote it into this file, into `deferred-work.md` and into a commit message, and escalated it to Alexv
as a decision. He approved fixing the bounds. **The bounds were never broken** — his feet are at Y≈0.00, his
head bone at Y=6.77, and the bone cloud matches the renderer box; `c_counted_but_zero.png` had been showing
him dead centre in the grader's own box the whole time. The fix he approved would have broken working code,
so it was not made and he was told why before anything was changed.

**What produced the phantom.** Two of my own mistakes compounding: the vantage was chosen when a scenario
started rather than at the shutter (and he walks, for up to 90 s of tracking), and the line-of-sight probe
measured that same early instant. It reported "nothing in the way" perfectly honestly — about a moment
nobody was photographing. Rather than measure the instant I cared about, I invented a mechanism that would
explain an impossible occlusion.

**The genuine reason no raycast could have caught it**, which is worth keeping: characters are ~8.8 m tall
and pines 4.9–7.6 m, so shooting from the subject's centre height sends every ray 1–7 m above the ground.
A pine ~4.5 m from the lens is crossed near its apex, centimetres wide, while its base fills the entire
frame. `9/9 rays clear` and an empty `RaycastAll` were both TRUE. They answer "is the line clear?"; the
question is "can the camera SEE him?".

**So the rig now measures pixels.** Twelve directions, the subject's renderers rendered on then off and
diffed, best silhouette wins — and the vantage is picked at the shutter, not at the start. The old spot
showed him at 0.9 % of the frame; the chosen one at 4.1 %, against ~3.8 % predicted from geometry with
nothing in the way. The "he is hidden" warning is geometric now too, so a deliberately-distant shot is not
falsely flagged. No shipped code changed — 153/153 EditMode tests still pass.

**Still deferred, now explained rather than mysterious:** the grader samples its occlusion rays the same
sparse way, so `line-of-sight 100 %` can still be reported past a near, wide occluder. And separately, the
characters are ~5× the environment's scale — consistent, not broken, but an art question for its own story.

### Completion note — 2026-09-09 session (closing the code review)

**8 of the 9 review items are closed; the ninth is blocked on a decision, not on work.**

The seven code patches existed in the working tree but had **never been compiled, tested or committed** —
the previous session wrote them and stopped. Compiling them surfaced two documentation defects introduced
along with them: a duplicated `<summary>` on `GradeHudConfig.ReportInvisible`, and `PlaceCamera`'s summary
orphaned onto the newly-inserted `ClearDirectionFor`, leaving `PlaceCamera` undocumented. Both repaired.

**The NaN-colour patch shipped unpinned.** `GradeHudConfigTests` covered alpha-0 and non-finite *floats* but
never a non-finite colour *channel* — the exact thing the patch fixed — so a revert of `!(a >= min)` to
`(a < min)` would have gone green. Added `SafeColours_RepairANonFiniteChannel` and
`TryGetConfigProblem_ReportsANonFiniteTextColour`, then **proved they bite** by reverting the fix and
re-running: exactly those two failed, with the right diagnosis, and nothing else did. Restored, 153/153 pass.

**What the re-shoot found instead of a clear vantage.** Three rig runs. The 2026-08-07 patch was a single ray
to the bounds centre against the grader's own mask; replacing it with 24 directions × 9 silhouette rays
against every layer but `Subject` changed nothing — it reported 9/9 clear over a photograph full of pine.
Adding the `RaycastAll` probe `deferred-work.md` had recommended since 2026-07-26 settled it in one line:
`18.74m to subject centre; RaycastAll hits NOTHING at all`. The drunk's `Bounds` are **9.18 × 8.25 × 2.30 m
centred 4.11 m up** — a `SkinnedMeshRenderer` scale-chain fault. The grader is not seeing through trees; it
is measuring a box floating above him, and reporting `line-of-sight 100 %` about empty sky. Two standing
theories died with it: the occlusion theory, and "the foliage has no collider" (16738/16738 renderers under
`game map` have one). Both recorded in `deferred-work.md`.

**Mistakes made in this session, recorded so they are not repeated.** The first vantage search was placed in
`PlaceCamera`, which the peak-tracking loop calls *every frame* — 216 linecasts per frame for up to 90 s,
which stalled a run badly enough that the editor sat in a play-mode transition until it was polled out of it.
Fixed by searching once per scenario. And triggering the gallery rig wiped `_bmad-output/verification/gallery/`
before I had checked it was gitignored and untracked; the run regenerated it and the caption baseline lives in
this file and in `GradeTextTests`, so nothing was lost — but the check belonged first.

**Not done:** the 5★ re-shoot, and therefore AC3/AC4. See the handover immediately below — it needs one
decision from Alexv and no further investigation.

## Handover — the perceptual check (AC3, AC4)

*Written 2026-08-07 during code review. Task 7 required this and it was missing: the record correctly said
AC3/AC4 were open, but never asked the actual question. This is the ask.*

**Status: AC1, AC2 and AC5 are proven. AC3 and AC4 are open and only you can close them.**

### What I need from you — about 5 minutes

Open these six images from `_bmad-output/verification/hud/` **in this order** and answer the three
questions below. Look at the bottom of each frame — that panel is the whole feature. Ignore the dark
readout in the top-left; that is the editor-only debug overlay the player never sees.

| # | Image | What it is |
|---|---|---|
| 1 | `a_money_shot.png` | a great shot — 5★ |
| 2 | `b_mid_counted.png` | a middling shot |
| 3 | `c_counted_but_zero.png` | a shot that counted but scored 0% (you were late) |
| 4 | `e_too_far.png` | a MISS — too far away |
| 5 | `f_blocked.png` | a MISS — something in the way |
| 6 | `i_not_graded.png` | grading not configured — nothing was scored |

**Q1 (AC3, the GDD's slice criterion).** Comparing #1 and #3: can you tell the great shot from the weak one
**at a glance**, without reading carefully? They both matter because they are the two a player will actually
see most.

**Q2 (AC3, NFR10 — "understand why").** Looking at #3, #4 and #5: does each one tell you what to do
differently next time, in words you would act on? Is a MISS (#4, #5) obviously a different thing from a
weak-but-counted shot (#3)?

**Q3 (AC4, polish — not a pass/fail gate).** Does it read like a 2000s camcorder/digicam? This one is taste
and it is explicitly *polish-acceptable*, so "good enough for now" is a perfectly good answer.

### Three things you should know before you look

1. **The readout is on screen for 2.8 s** (2.2 s hold + 0.6 s fade). The stills cannot tell you whether that
   is long enough for someone *playing* rather than studying a picture. If you want to feel it, press Play in
   `SampleScene`, raise the camera and take a shot. That is the only part of AC3 a picture genuinely cannot
   answer.
2. **Text size in the captures is conservative, not misleading.** The run's Game View was 574×494, which is
   small, and the completion notes said text size therefore could not be judged. That is *too* pessimistic:
   the canvas is `ScaleWithScreenSize` (ref 1920×1080, match 0.5), so the text is a fixed fraction of screen
   height. Reproducing Unity's own reported `scaleFactor` of `0.3832` confirms it — the text is **19% larger
   relative to the screen at 1080p** than in these pictures. If it reads for you here, it reads in the game.
3. **`hideOnCameraLowered` is a feel decision nobody has played yet.** It is ON. Because raising the camera is
   a *hold*, letting go of the button takes the grade off screen immediately — so a player who clicks and
   releases may never read it. Turn it OFF in `Assets/Data/UI/GradeHudConfig.asset` and the readout finishes
   its hold, but it can then sit over the gallery. Both are defensible; playing it is what decides.

### One correction to the record, and one thing to ignore (2026-09-10)

**Retracting yesterday's diagnosis.** The 2026-09-09 handover told you the exemplars were unusable because
`EventActor.Bounds` returned a box floating four metres above the drunk, from a `SkinnedMeshRenderer`
scale-chain fault. **That was wrong, and it was written up and committed before it was checked.** The bounds
are correct: his feet sit at Y≈0.00, his head bone at Y=6.77, and the bone cloud (8.09 × 8.76) matches the
renderer bounds. `c_counted_but_zero.png` shows him dead centre inside the grader's own box. Nothing needed
fixing there, and the fix you approved would have broken working code — which is why it was not made.

**What was actually happening.** Two mistakes of mine, compounding:

1. The vantage was chosen when the scenario *started*, then the rig spent up to 90 s tracking him to his
   peak. He walks. By the shutter he could be behind a tree the camera had a clear line to when it was placed.
2. The line-of-sight probe ran at that same early instant, so it honestly reported "nothing in the way" about
   a moment nobody was photographing — and I went looking for a cause of an impossible occlusion instead of
   measuring the instant I actually cared about.

**And why no raycast could ever have caught it.** The characters stand ~8.8 m tall and the pines are
4.9–7.6 m, so the camera shoots from ~4 m up and every sample ray runs 1–7 m above the ground. A pine ~4.5 m
from the lens is crossed near its **apex**, where the cone is centimetres wide, while its wide base a metre
lower fills the whole frame. Nine silhouette-spanning rays said *9/9 clear* and `RaycastAll` said *nothing at
all* — both true, both answering "is the line clear?" when the question is "can the camera **see** him?".

The rig now answers the real question: it renders the subject twice from each of twelve directions, once
with his renderers on and once off, and picks the vantage with the most subject pixels. From the old spot he
covered **0.9 %** of the frame; the chosen one gives **4.1 %**, against ~3.8 % predicted by geometry with
nothing in the way. Every exemplar below is re-shot that way and **none carries an occlusion warning**.

**The one thing to ignore while judging:** the characters are about five times the scale of the world —
8.86 m tall against 4.9–7.6 m trees and a 1.97 m car. It is internally consistent (both characters agree,
the grader's gates were tuned against it) so nothing misbehaves, but it is why he towers over the treeline.
That is an art-scale question for its own story, logged in `deferred-work.md`, and it is **not** something
the readout can be blamed for.

**Still genuinely deferred:** the grader's own occlusion test samples rays the same sparse way, so
`line-of-sight 100 %` can still be reported past a near, wide occluder. Now explained rather than mysterious,
with a working reference implementation of the honest measurement sitting in the rig.

### What happens after you answer

- **All three Q's fine** → AC3/AC4 close, story goes `done`.
- **Q1 or Q2 weak** → that is a real finding and the readout changes; this is exactly what the perceptual
  check has caught twice before (Story 1.10's two shots tying at 5★, Story 1.11's window resize).
- **Only Q3 weak** → note it and ship anyway; AC4 is polish-acceptable by the epic's own wording.

### File List

**New**

- `Assets/Scripts/Grading/GradeText.cs`
- `Assets/Scripts/UI/GradeHudConfig.cs`
- `Assets/Scripts/UI/GradeHud.cs`
- `Assets/Scripts/UI/GradeHudShootRunner.cs`
- `Assets/Scripts/Editor/GradeHudShootRig.cs` (two menu items: the shoot, and the motion recording)
- `Assets/Data/UI/GradeHudConfig.asset` (gained `strongColor`/`weakColor` on 2026-09-11)
- `Assets/Tests/EditMode/GradeTextTests.cs`
- `Assets/Tests/EditMode/GradeHudConfigTests.cs`
- (and the `.meta` file Unity generated for each of the above, plus `Assets/Scripts/UI.meta` and
  `Assets/Data/UI.meta`)

**Modified**

- `Assets/Scripts/Grading/ShotGrade.cs` — added `PeakOffset` / `TimingMeasured` / `PeakOffsetText`;
  `Scored` takes the offset; `FromPercent` deleted; `ToString` reports the peak offset.
- `Assets/Scripts/Grading/ShotGrader.cs` — passes the measured peak offset into `ShotGrade.Scored`.
- `Assets/Scripts/Gallery/GalleryView.cs` — calls `GradeText`; private `Stars`/`Describe` deleted.
- `Assets/Tests/EditMode/GradeStructTests.cs` — `Scored` call sites updated; peak-offset contracts pinned.
- `Assets/Scenes/SampleScene.unity` — `GradeHudCanvas` (Canvas Overlay `sortingOrder 100`, CanvasScaler
  ScaleWithScreenSize 1920×1080 match 0.5, CanvasGroup, `GradeHud`) with `Panel` + `RatingLabel` /
  `AxesLabel` / `WhyLabel`, all references assigned.
- `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `_bmad-output/implementation-artifacts/deferred-work.md`
- `tools/verification/gemini_motion_review.py` — now takes an optional `--prompt <file>` so a temporal
  question other than the walk-cycle one can be asked; the default is unchanged.
- `CLAUDE.md` — the traps this story paid for (Overlay vs `cam.Render()`, the capture-flash instant,
  `manager.enabled` not emptying the world).
- `_bmad-output/implementation-artifacts/1-12-grade-feedback-hud.md`

### Change Log

| Date | Change |
|------|--------|
| 2026-07-31 | Story created (`gds-create-story`). |
| 2026-08-07 | Implemented. Task 1's five decisions recorded; `GradeText` extracted and `FromPercent` deleted; `GradeHudConfig` + asset; `GradeHud` + scene canvas; `GradeHudShootRig`/`Runner` built and run in the real scene. 140/140 EditMode tests pass; photo-shoot and gallery-shoot regressions clean. Status → review. |
| 2026-08-07 | Rig self-corrections after the first run: the empty-street scenario was photographing a live subject; every Phase A frame was taken at peak capture-flash; `h_behind_you` never reached `BehindCamera`. Shipped fix: the config's problem text now describes the mistake that was made rather than one fixed sentence per field. Re-run clean. |
| 2026-08-07 | Motion verification added (`Tools > HUD > Grade HUD — Record the readout`): frame analysis of the recorded readout plus a Gemini video review, every claim of which was verified — its headline duration claim was disproven by measurement, and it corrected my own overstatement about the capture flash. `Time.captureFramerate` does not lock `unscaledDeltaTime`; the rig now measures and reports the honest encode rate. |
| 2026-08-07 | AC3/AC4 handed to Alexv and left **open** — see Task 7. Town occlusion symptom raised as a scope question, not fixed. |
| 2026-09-09 | Resumed to close the 2026-08-07 review. 8 of 9 items closed: the 7 code patches were verified by compiling and running (they had been written but never compiled, tested or committed), and two documentation defects introduced with them were repaired (a duplicated `<summary>` on `ReportInvisible`, and `PlaceCamera`'s summary orphaned onto a new method). |
| 2026-09-09 | Added the missing regression tests for the NaN-colour patch, which was shipped unpinned — nothing would have caught a revert of `!(a >= min)` to `(a < min)`. Proved they bite: reverted the fix, watched exactly those two tests fail with the right diagnosis and nothing else, restored it. **153/153 EditMode tests pass** (was 140 before the review, 151 after the patches, 153 now). |
| 2026-09-09 | **The 5★ re-shoot is blocked and the town-occlusion theory is disproven.** Ran it three times. `EventActor.Bounds` returns a box **9.18 × 8.25 × 2.30 m centred 4.11 m above the ground** for the drunk — a `SkinnedMeshRenderer` scale-chain fault (root bone lossyScale `0.01` vs renderer transform `(0.20, 0.07, 0.08)`, `localBounds` `917 × 825 × 229`). The rig therefore stands 18.74 m back and aims four metres over his head; the grader's size gate and occlusion rays use the same box, which is why every shot reads `line-of-sight 100 %` and `RaycastAll` hits nothing. Not fixed — it is `EventActor`/prefab (Story 1.6) and moves every 1.9/1.10 grading number. **AC3/AC4 escalated to Alexv with three options.** |
| 2026-09-09 | Rig hardened while diagnosing: the vantage search now runs **once per scenario** rather than once per frame (the first version ran 216 linecasts on every frame of a 90 s tracking loop and stalled the run), it can no longer fall back silently, and `DescribeLineOfSight` dumps what a camera-to-subject ray actually hits beside every scenario. Regressions re-run: `Tools > Gallery > Gallery Shoot (Play)` clean end to end including the Tab-wiring phase, captions and star glyphs intact, no truncation. |
| 2026-09-10 | **Retracted the 2026-09-09 `EventActor.Bounds` diagnosis — it was wrong and had been committed before being checked.** The bounds are correct (feet Y≈0.00, head bone Y=6.77, bone cloud 8.09 × 8.76 matching the renderer box). Alexv had approved fixing them; the fix was NOT made because it would have broken working code, and he was told so. Real cause: the vantage was chosen at scenario start while the rig then tracked the subject for up to 90 s, and the probe measured that same early instant — so it truthfully reported "nothing in the way" about a moment nobody photographed. |
| 2026-09-10 | **The 5★ re-shoot is done.** Vantage is now chosen at the shutter, and visibility is measured by RENDERING (twelve directions, subject renderers on then off, diffed) rather than by raycast — because no raycast can answer it here: at ~4 m camera height the rays cross a near 4.9 m pine at its apex while its base fills the frame, so 9/9 clear and an empty `RaycastAll` were both true and both wrong. Old vantage showed him at 0.9 % of the frame; the chosen one 4.1 %, versus ~3.8 % predicted from geometry. `a_money_shot` is now `100 % · 5★ · right on the moment`, subject centred and unoccluded; no shot carries an occlusion warning. 153/153 EditMode tests still pass; no shipped code changed. |
| 2026-09-10 | The rig's "he is hidden" warning is now geometric (expected silhouette from bounds, distance and FOV) instead of a flat threshold, so `e_too_far` — whose whole point is distance — is no longer falsely flagged. A rig that cries wolf teaches you to skim its warnings. |
| 2026-09-11 | **AC3 perceptual check run with Alexv — it found a real readout defect and it is fixed.** Shown the three panels with the world cropped away he called the MISS "obvious" but said of the 5★ and the 0% pair "it's not obvious which one is which". Cause: the miss changes colour, swaps the number for MISSED and prints dashes, while both counted shots shared one cream colour and differed only by small star glyphs and a percentage — colour was carrying the miss and doing nothing for the grade. **Fix: the counted readout is now banded by grade** (4–5★ green, 3★ the original cream, 1–2★ amber) via `GradeHudConfig.CountedColorFor`, with `strongColor`/`weakColor` added to the config and asset. Pinned by a test that asserts the bands are far enough apart to tell at a glance and that weak is not confusable with a miss. 154/154 EditMode tests pass. |
| 2026-09-11 | Also from that check, and fixed first: the rig fired the shutter on the FIRST frame of the peak window, where `DrunkStagger` is still blending in over 0.2 s — so the 5★ exemplar photographed him standing in the walk pose and was genuinely indistinguishable from the 0% shot. The rig now dwells 0.6 s into the window. Alexv's remaining points — the subject faces away, and a hard 0% for a well-composed shot is "too extreme" — are design questions logged in `deferred-work.md`, not HUD defects. |
