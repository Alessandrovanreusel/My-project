---
baseline_commit: 91d3f8b8192e4f1e5d2bfb14b7dac05665cb760c
---

# Story 1.19: A shot that earns nothing reads red

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Added 2026-11-14 from Alexv's answer to Story 1.12's fourth look question (R4, asked 2026-09-27/28): "I think
missed and 0% should be red". He chose a **dark text outline** to keep red readable. On 2026-11-14 he confirmed he
still wants it. Context created 2026-11-15 by the loop CREATE phase.*

*It does not depend on any video work and is **not** held by the ADR-4 gate (`loop/dependencies.yaml` lists no
dependency for it). **Story 1.16 (Grade a clip) inherits it**: the architecture says "`GradeHud` shows its [the
clip's] grade" (`game-architecture.md:239`), so whatever colour rule this story builds is the one clips will use.
Build it so a `ClipGrade` can call it (Task 1c).*

> **The starting point in eight facts.** All were read from files or measured in the editor on 2026-11-15. Re-check
> the ones marked *(re-measure)* before relying on them.
> 1. **Today's readout colours** (`Assets/Data/UI/GradeHudConfig.asset`, LF line endings): strong green
>    `(0.55, 0.93, 0.62)` for 4–5★, cream `(0.94, 0.94, 0.90)` for 3★, amber `(1.00, 0.88, 0.31)` for 1–2★,
>    **salmon `(1.00, 0.55, 0.45)` for a MISS**, grey `(0.62, 0.62, 0.66)` for NOT GRADED. A counted **0 %** shot is
>    1★ and therefore **amber** today (`GradeHudConfig.CountedColorFor`, `GradeHudConfig.cs:175-178`).
> 2. **The panel** behind the text is `SampleScene` → `GradeHudCanvas/Panel`, an `Image` of
>    `(0.043, 0.047, 0.055, 0.78)`. 78 % was Alexv's call on 2026-09-27, to lift the amber over 4.5:1.
> 3. **The labels** are three legacy uGUI `Text` components (`RatingLabel` 34 px, `AxesLabel` 22 px, `WhyLabel`
>    22 px, all at the 1920×1080 reference size), font `LegacyRuntime.ttf`. **None has an Outline or Shadow** (each
>    GameObject is RectTransform + CanvasRenderer + Text). The canvas is Screen Space – Overlay, `CanvasScaler`
>    ScaleWithScreenSize 1920×1080, match 0.5.
> 4. **How the percentage is printed** (measured with `execute_code` in this editor, Mono, 2026-11-15):
>    `CurrentCulture` is **en-US**, and Unity's Mono prints `P0` **with a space**: `0 %`, `1 %`, `100 %`.
>    `0.004 → "0 %"`, `0.0049 → "0 %"`, **`0.005 → "1 %"`** (it rounds the float's shortest form half away from
>    zero), `0.006 → "1 %"`, `0.0149 → "1 %"`, `0.015 → "2 %"`, **`-0f → "0 %"`** (no minus sign), `-0.004 → "0 %"`,
>    `0.995 → "100 %"`. InvariantCulture prints identically. ⚠️ **fr-FR prints `0 ٪` with U+066A, the Arabic
>    percent sign**: Mono's culture data, not a typo. `LegacyRuntime.ttf` may not even contain that glyph.
>    Raw output: `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/create/` (see Task 1c).
> 5. **`−0` is a real value here.** `Mathf.Clamp01(-0f)` keeps the sign bit (measured: sign bit 1), and
>    `ShotGrade.Sane` is `Clamp01` after a NaN guard (`ShotGrade.cs:212`). So
>    `ShotGrade.Scored(1, 1, -0f, …)` produces `Percent01 == -0f` through the real factory.
> 6. **What was measured before** (epics.md, 2026-09-27/28 session): over the brightest background a red readout
>    appears on (the 0 % shot over sunny grass, panel at 78 %), a clear red reaches **about 2.7–3.0:1** and pure
>    red **2.0:1**, against Alexv's **4.5:1** floor. Today's salmon MISSED measures only **~3.5–3.7:1** on bright
>    ground (`e_too_far`, `g_out_of_frame`); it was only ever measured on the dark `f_blocked` background (5.6:1).
>    Those raw numbers are not on disk: re-measure them as your baseline (Task 2).
> 7. **The current HUD evidence** (`_bmad-output/verification/hud/`, dated 2026-11-11, after ADR-5) was captured at
>    **953×476**, not 1080p. At that size the 22 px reference text is about 10 px tall and a 1 px outline would be
>    0.44 px. **Measure at 1920×1080** (Task 1e). In that run `b_mid_counted` graded **14 % 1★**, so it is amber, not
>    the 2★ the 1.12 handover describes.
> 8. **The toolbox has two things this story uses** (CLAUDE.md *Toolbox*): the skills `unity:ui` → `unity:ui-ugui`
>    for the HUD, and test-helper 1.6.4, whose `GameViewControlHelper.SetResolution` (assembly
>    `TestHelper.RuntimeInternals`, editor-only by define constraint) can set the Game View to 1920×1080 for the
>    rig. Nothing else in the toolbox fits: there is no new system to state-machine, no renderer feature, no audio.

## Story

As a player,
I want a miss and a shot that scored 0 % to both show in red, and the readout to stay easy to read on any
background,
so that "this earned me nothing" is obvious at a glance, while a weak shot that earned something still
looks different from it.

## Acceptance Criteria

*The seven ACs are copied word for word from `epics.md#Story 1.19`. The notes under each one are guidance for
building it. They add no new requirement, and none of them weakens the AC above it.*

**AC1 — A miss is red**
**Given** a missed shot,
**When** the grade readout shows,
**Then** it is drawn in a clearly red hue, not today's salmon (`GradeHudConfig.missColor`).

- "Clearly red" is judged on the **rendered** pixels, not on the authored value, and Alexv has the last word
  (AC7). Measured on the authored colours (`create/red_candidates.txt`), **hue angle does not separate salmon from
  red**: salmon is Lab h 38.7°, pure red 40.0°. What separates them is **chroma** (salmon C* 52.6, the reds 72–105)
  and **saturation** (salmon HSV s 0.55, the reds 0.68–1.0). Choose by those.

**AC2 — A counted 0 % shot uses the same red, decided by the printed text**
**Given** a counted shot whose readout prints "0 %",
**When** the grade readout shows,
**Then** it uses the same red as a miss
**And** the choice follows the printed text, not the raw number: the label rounds, so 0.4 % prints "0 %" and
is red, while a shot printing "1 %" keeps its grade band
**And** the words still say which it was: a miss prints MISSED and dashes, a 0 % shot prints its breakdown
and the timing advice.

- The colour and the text must come from **one** formatting call, so they cannot disagree (Task 1c). Fact 4 shows
  why this matters: `0.005` prints `1 %` in this runtime, so a rule written as `Percent01 < 0.005f` would paint a
  shot red while it prints `1 %`.
- The wording is **unchanged**. `GradeHud.Format` already prints MISSED and dashes for a miss and the breakdown
  plus `GradeText.TimingAdvice` for a counted shot (`GradeHud.cs:286-320`). Only the colour changes.

**AC3 — A shot that earned something keeps its band**
**Given** a counted shot that earned something (it prints "1 %" or more),
**When** the grade readout shows,
**Then** its grade band is unchanged: 4–5★ green, 3★ cream, 1–2★ amber (Alexv confirmed a weak shot keeps
amber).

- Do not touch `strongColor`, `countedColor` or `weakColor`, nor the star thresholds (`StarScale`,
  `GradingConfig`). A shot printing `1 %` is 1★ and stays **amber**.

**AC4 — Every readout colour stays readable on the brightest real backgrounds, thanks to a thin dark outline**
**Given** every readout colour (green, cream, amber, red and the not-graded grey),
**When** it is drawn over the brightest real backgrounds the game shows behind the HUD,
**Then** it stays readable because all readout text (rating, breakdown and hint lines) carries a thin dark
outline
**And** this is measured off real captures, as rendered rather than as authored, including the bright-ground
miss scenarios that were never measured, and reported both as text against its outline and as text
against the panel background.

- Measure at **1920×1080**, the shipping resolution (fact 7). Use the frozen-frame mask technique in Task 1e.
- **Both numbers, every colour, every label line**, at the brightest backgrounds found (Task 6.4), plus
  `e_too_far` and `g_out_of_frame`. Report the median **and** the worst 5 % of glyph pixels: the world behind a
  translucent panel is not uniform, and a median hides the bright patch.
- **The target is text against outline ≥ 4.5:1** (Alexv's floor from Story 1.12, applied to the boundary the
  outline creates). Text against panel is **reported, not required**: on bright ground no red reaches 4.5:1
  against the panel (fact 6). That is the reason the outline exists. ⚠️ **Do not call this "WCAG compliant".** WCAG's
  own guidance treats a *narrow* border as part of the letter and a *wide* one as a halo. Report the two numbers
  the AC asks for, say which one the 4.5:1 target applies to, and leave the perceptual verdict to AC7.

**AC5 — The validator and tests guard the colours and the "prints 0 %" rule**
**Given** the grade-HUD config validator (`TryGetConfigProblem`) and its tests,
**When** the colours change,
**Then** red stays clearly apart from amber, cream and green (the ΔE floor of 25), the shipped asset passes
its own validator, and the "prints 0 %" rule is pinned by tests at 0, 0.4 %, 0.6 % and −0.

- The three pairs already exist (`GradeHudConfig.cs:319-321`; `Validator_ReportsEveryPairAPlayerMustTellApart`).
  Keep them. Their **wording** becomes false after this story, so update it (Task 4.5).
- The −0 test must **prove its fixture is −0** (check the sign bit) before it asserts anything. Otherwise it
  silently tests +0 (fact 5).
- `ShippedAsset_PassesItsOwnValidator` must still pass, now with the outline fields in the asset.

**AC6 — Nothing else regresses**
**Given** the change is complete,
**When** the existing rigs and the EditMode suite run,
**Then** nothing else regresses and the editor is restored.

- "The existing rigs" means at minimum `Tools > HUD > Grade HUD Shoot (Play)`, `Tools > Grading > Photo Shoot
  (Play)` and `Tools > Gallery > Gallery Shoot (Play)` (Task 8). "Restored" includes the **Game View resolution**
  this story's rig changes.

**AC7 — Alexv confirms (human check)**
**Given** the new captures,
**When** Alexv looks at them,
**Then** he confirms red reads as "this earned nothing" and the outline still looks like a camcorder overlay
rather than a heavy box. This is a **human** check and stays open until he has done it.

- **Never mark AC7 verified.** Queue it in `human_checks` with the evidence (Task 9). The story can move to
  `review` with it open.

## Tasks / Subtasks

- [x] **Task 1 — Settle the six decisions that GATE THE STORY (AC1–AC5). DO THESE FIRST.**

  Each has a recommendation and the evidence that should settle it. Record every choice **with its reason and its
  evidence path** in the Dev Agent Record and in the result file's `decisions`, including the ones where you follow
  the recommendation. Alexv reviews them in one batch and may veto.

  - [x] **1a. How the outline is drawn.** *Toolbox: invoke `unity:ui`, then `unity:ui-ugui`, before writing the HUD
        code.*
        - **Recommended: uGUI's built-in `UnityEngine.UI.Outline`** (`com.unity.ugui` 2.0.0, already used; confirmed
          present by `execute_code` on 2026-11-15). It is a `BaseMeshEffect` that draws four copies of the text mesh
          at (±x, ±y) in `effectColor` **behind** the original (`Outline.cs` → `Shadow.ApplyShadowZeroAlloc`), with 5×
          the vertices. That is a few thousand vertices, rebuilt once per shutter press. It works with legacy `Text`
          and with the `★`/`☆` glyphs, and it fades with the `CanvasGroup` (`useGraphicAlpha`, default on).
        - **Known limits, so check them on the captures:** the four copies are diagonal, so a thick outline leaves
          notches at a glyph's horizontal and vertical extremes. At 1–2 px this is invisible. `effectDistance` is in
          canvas units, so the CanvasScaler scales it with the screen. Its setter clamps to ±600 but **lets NaN
          through** (`Shadow.cs:52-66`), so a NaN thickness must be stopped in the config (1b).
        - **Alternative A — a custom 8-direction mesh effect** (copies on the axes too). It gives a rounder outline at
          larger thicknesses. Take it only if the captures show notches at the chosen thickness.
        - **Alternative B — migrate the three labels to TextMeshPro** and use the SDF outline. ⚠️ The `unity:ui-ugui`
          skill says "use TextMeshPro for all text", and this project deliberately does not (Story 1.12 *Latest
          technical information*: "introducing TMP for one HUD adds … a second text stack for no proven benefit").
          The project has no `Assets/TextMesh Pro` essentials. The `★`/`☆` glyphs would need a font asset that
          contains them, and the gallery would still be legacy `Text`. **Reject unless 1a's recommendation fails on
          the captures.** If you do take it, it is a design decision: record it.
  - [x] **1b. Where the outline's colour and thickness live, and who adds the component.**
        - **Recommended: two new tunables on `GradeHudConfig`**, read through `Safe*` accessors like every other value
          there: `outlineColor` (start near-black, e.g. `(0, 0, 0, 0.9–1)`) and `outlineDistance` (start ~1.5 canvas
          units, so ~1.5 px at 1080p). **`GradeHud.Awake` applies them**: for each label that exists, take its `Outline`
          or add one, then set `effectColor` and `effectDistance` from the config.
        - Why the code, not the scene: it is the same call `GradeHud.Awake` already makes for the canvas render mode
          ("the code owns the property rather than the scene asset", `GradeHud.cs:123-135`). It keeps
          `SampleScene.unity` (16.9 MB, CRLF) **untouched**, which avoids the "scene modified externally" modal after
          an orchestrator merge (lessons, 2-2). It also covers Phase D's throwaway HUDs, which build their labels in
          code (`GradeHudShootRunner.BuildThrowawayHud`). The cost: the outline shows only in Play mode, not in the
          Scene view. Say so in the tooltip.
        - ⚠️ **Fail-soft, not silent-nothing.** `outlineDistance = 0`, negative or NaN, and an `outlineColor` with alpha
          ≈ 0, are the project's signature failure: a feature that is switched off by a hand-typed value, with a clean
          console. Repair them in the `Safe*` accessor (fall back to the design default for non-finite, clamp for
          out-of-range; constants referenced by both initialiser and accessor, `GradeHudConfig.cs:78-86`) **and**
          report them in `TryGetConfigProblem`. **No `OnValidate`** (`GradeHudConfig.cs:421-439` explains why).
        - Bounds to choose, measure and record: a minimum thickness that is still visible at 1080p, and a maximum
          beyond which the outline stops being "thin" (AC7's "heavy box").
  - [x] **1c. The "prints 0 %" rule: one source of truth for the text and the colour.**
        - **Recommended: add to `GradeText` (`Assets/Scripts/Grading/GradeText.cs`):**
          - `public static string Percent(float percent01)`. It sanitises first (`if (!(p > 0f)) p = 0f;`, which maps
            NaN, negatives and **−0** to +0; then clamp to 1) and formats `P0` with **`CultureInfo.InvariantCulture`**.
            The editor prints byte-identically to today (`0 %`, fact 4), and the readout no longer depends on the
            player's locale (fr-FR prints U+066A, fact 4).
          - `public static bool PrintsZero(float percent01) => Percent(percent01) == Percent(0f);` It is defined **by
            the printed string**, which is literally what AC2 says, so no rounding mode or threshold is written down a
            second time.
        - **Recommended: put the colour choice in one pure function on `GradeHudConfig`**, taking primitives so 1.16's
          `ClipGrade` can call it without knowing about `ShotGrade`. For example
          `Color ColorFor(bool counted, bool isMiss, int stars, float percent01)`, plus a `ColorFor(ShotGrade)`
          convenience. Order: not counted and not a miss → placeholder grey; miss → red; counted and
          `GradeText.PrintsZero` → red; otherwise `CountedColorFor(stars)`. `GradeHud.Format` calls it in every branch
          instead of picking colours itself, and prints the rating with `GradeText.Percent`.
        - **Test it EditMode, through the real factory** (`ShotGrade.Scored`, `ShotGrade.Missed`,
          `ShotGrade.Placeholder`), never a hand-built struct (Task 3.3).
        - The four AC cases, measured on 2026-11-15: 0 → `0 %` red; 0.004 → `0 %` red; 0.006 → `1 %`, 1★ amber;
          −0 → `0 %` red. Note that **0.005 prints `1 %`** in this runtime. Do not write a test that expects 0.5 % to be
          red.
        - Rejected alternative: a numeric threshold (`percent01 < 0.005f`). It writes the rounding rule down a second
          time, and it is wrong in this runtime (0.005 → `1 %`).
  - [x] **1d. Which red.** Decide by measuring, in this order:
        1. **Shortlist by the authored numbers** (`create/red_candidates.txt`, computed 2026-11-15). The ΔE floor of
           25 against green, cream and amber is met by every candidate (ΔE 72–135). Against a pure-black outline, the
           authored contrast is **5.25:1 (pure red) to 7.1:1**, and salmon is 9.3:1 because it is light. Try
           `(1.00, 0.25, 0.20)`, `(1.00, 0.30, 0.24)` and `(1.00, 0.33, 0.28)` first: saturated enough to read as red
           (HSV s 0.72–0.80, C* 77–87), and light enough to keep more margin over the outline than pure red.
        2. **Photograph the shortlist** with the outline, on the brightest background and on a dark one, and measure
           text against outline as rendered (Task 6). Anti-aliasing and a translucent outline both lower the rendered
           number below the authored one.
        3. **Choose** the reddest candidate that clears 4.5:1 text-against-outline as rendered on every measured
           background. Record the losers' numbers too.
        - **Optional, your call:** a validator line that warns when `missColor` drifts back towards salmon, e.g. HSV
          saturation below a floor you derive from the measurements (salmon is 0.55). It is an authoring guard, not a
          test of an expected result. If you add it, derive the floor from the numbers and pin it with a test that
          fails on today's salmon.
        - **Colour blindness (report it, it is not an AC):** `deferred-work.md:287` logs 5★ green against MISS salmon
          at **ΔE 15.5 under deuteranopia**. On the authored numbers a saturated red **improves** that pair to
          ΔE 35–46, because it is darker than salmon. Re-run the Vienot simulation on the rendered colours
          (`_bmad-output/verification/review-1-12-banding/cvd.py` has the matrices) for red against green and red
          against amber, and add the numbers to that deferred entry.
  - [x] **1e. How AC4 is measured: frozen frames, masks, 1920×1080.** The 1.12 tooling cannot answer AC4 as written.
        `build_hud_panel_sheet.py` measures "text vs background" inside a hand-given rectangle, and its own header
        lists five auto-detectors that produced plausible wrong numbers. It also has no notion of an outline. Its
        header names the right fix: the runner knows where the HUD is. **Recommended technique:**
        - **Resolution.** Before entering Play, set the Game View to **1920×1080**, either with test-helper's
          `GameViewControlHelper.SetResolution` (reference `TestHelper.RuntimeInternals` from
          `CameraGame.Editor.asmdef`) or the same reflection on `UnityEditor.GameView`. **Record the previous size
          first and put it back**: test-helper has no getter, so read `selectedSizeIndex` by reflection either way. At
          run time, log the captured texture's size **and** the canvas `scaleFactor`. If they are not 1920×1080 and
          1.0, the run is not at 1080p: say so and do not report its numbers as 1080p. (Using test-helper in an
          editor-only rig is a tooling choice, not shipped code, but record it.)
        - **Freeze the frame.** After the shutter, wait for the settled instant (~0.5 s, past the flash: CLAUDE.md
          trap), check `hudGroup.alpha == 1` (the HUD is in its hold, not its fade; `IsShowing` cannot tell,
          `deferred-work.md:212`), then set `Time.timeScale = 0` (record and restore). The HUD's hold runs on unscaled
          time (`GradeHud.cs:390`), so you have its 2.2 s hold for the variants.
        - **Capture variants of the same frozen frame**, each after `WaitForEndOfFrame` with
          `ScreenCapture.CaptureScreenshotAsTexture()`, with the camera rendering to the screen (CLAUDE.md trap):
          - **A**, the readout as shipped.
          - **M**, the same frame with every label's colour set to the marker magenta `(1, 0, 1)` and the outline
            disabled. This gives the glyph-fill mask.
          - **P**, the labels disabled (`Text.enabled = false`). This gives the panel-and-world pixels behind each
            glyph.
          - **N**, the shipped colours with the outline disabled. This is the *without outline* picture for Alexv and
            for the before/after.
          Put every label property back exactly afterwards.
        - **Controls before trusting a number:** outside the HUD's rect, A and P must be ~identical (proves the
          frame was frozen; report the max per-pixel difference); the mask must be non-empty and a plausible fraction
          of each label rect; one frame must have known 3D content (the existing Phase 0 controls).
        - **Measure offline**, so the maths can be iterated without re-running Play mode. The runner writes the PNGs
          plus a JSON sidecar with each label's screen rect (from its `RectTransform`), the scenario, the grade, the
          colour state, the resolution and the scale factor. A Python script computes, per label line and colour:
          - **text**: A over the mask's **core**, the pixels at least ~90 % marker in M (fully covered by a glyph);
          - **outline**: A over the ring around the mask (dilated mask minus mask) where A is darker than P;
          - **panel**: P over the mask, i.e. what the glyphs sit on.
          It then reports contrast text-vs-outline and text-vs-panel, as the median and the worst 5 %. Write it as
          `tools/verification/measure_hud_legibility.py` (numpy + Pillow, run with `python -I`).
          ⚠️ **Do not erode the mask morphologically.** At 1080p the 22 px lines have strokes about 2 px wide, and a
          1 px erosion can leave nothing, or only anti-aliased edge pixels that read as a dimmer colour. Report the
          core pixel count per line, and refuse to print a number when it is too small to mean anything.
        - ⚠️ **While `Time.timeScale` is 0, the rig must wait on unscaled time.** Several runner loops count
          `Time.deltaTime` (e.g. the actor waits in Phase A), and those never advance while the frame is frozen.
          Unfreeze before returning to any of them.
        - **Which backgrounds.**
          - (i) Every Phase A state, shot with the real shutter.
          - (ii) The **brightest** real backgrounds: a survey that, with the HUD hidden, turns the rig camera through
            headings and positions around the drunk's route and measures the luminance inside the panel rect, then
            keeps the top few. Record where they are.
          - (iii) At each of those, **every colour over the identical frozen background**. Do this by raising the
            real `ShotCapturedChannel` with grades built by the real factories (5★, 3★, 1★ that prints `1 %`, a
            counted `0 %`, a MISS, `ShotGrade.Placeholder`). That drives the real `GradeHud` handler, Format and
            colour choice. Only the grader is bypassed, and the grader is not under test here. **Label these
            captures "channel-raised"** in every caption; the Phase A states stay real-shutter.
          - (iv) Optional: a labelled **synthetic** white card behind the panel, as the upper bound of "any
            background".
  - [x] **1f. Scope: what this story does NOT change (record it, so it is not mistaken for an oversight).**
        - **The gallery.** `GalleryView` has its own salmon `MissTextColor` `(1, 0.55, 0.45)` and prints `P0` itself
          (`GalleryView.cs:103, :583, :762-763`). AC1–AC4 are about the grade readout. **Recommended: leave the
          gallery alone** and log in `deferred-work.md` that its miss caption is still salmon, for Story 1.17, which
          reworks that view into the memory card. Making it red now would mean re-proving the gallery's legibility on
          its own opaque backdrop, for a view about to be replaced.
        - **The P0 "100 %" rounding** (`deferred-work.md:210`: 0.996 prints `100 %`). The AC says "the label rounds".
          Switching to floor would make 0.6 % print `0 %` and change which shots are red. Leave it deferred. Do not
          fold it into `GradeText.Percent`.
        - **The "why" line has no composition voice** (`deferred-work.md:214`). AC2 keeps the wording. Leave it
          deferred.
        - **The panel's 78 % opacity.** Alexv chose the outline over a ~90 % panel. Do not raise the panel opacity
          to pass a number.

- [x] **Task 2 — Baseline on HEAD, before changing any code (AC4, AC6)**
  - [x] 2.1 Copy `_bmad-output/verification/hud/` to
        `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/baseline-1-12-hud/` **before any rig
        run**. The HUD rig writes into `hud/` (`GradeHudShootRig.cs:59`), which is the evidence Story 1.12 closed on
        (lessons, 2-2: "Rigs that write into a story's evidence folders overwrite the dev's evidence").
  - [x] 2.2 Give the rig an output directory (a `SessionState` key, set **and** erased before every run, per the
        lessons), so this story writes under its own evidence folder and never into `hud/` again.
  - [x] 2.3 Record: the EditMode count (run all), the console baseline (CLAUDE.md lists the known warnings, plus
        `ThirdPersonCamera` head-bone), and `git status`.
  - [x] 2.4 Build the measurement (Task 1e) **first**, run it on HEAD at 1920×1080, and keep the result as
        `before/`. That gives today's salmon and amber, with no outline, measured as rendered at the shipping
        resolution, **including `e_too_far` and `g_out_of_frame`**: the bright-ground miss scenarios AC4 says were
        never measured. This is the "before" half of every comparison, and the control that proves the measurement
        can report a low number. Expect salmon around 3.5–3.7:1 against the panel on bright ground (fact 6). If it
        reads far higher or lower, suspect the rig before believing either.

- [x] **Task 3 — `GradeText.Percent` / `PrintsZero` (AC2, AC5)**
  - [x] 3.1 Implement as in 1c, in `Assets/Scripts/Grading/GradeText.cs`. Pure static, no allocation beyond the
        returned string. It is called once per shutter press, never per frame. `using System.Globalization;`.
  - [x] 3.2 Comment *why* the predicate is defined by the string (AC2's own words; 0.005 prints `1 %`) and why the
        culture is pinned (fr-FR's U+066A).
  - [x] 3.3 Tests in `Assets/Tests/EditMode/GradeTextTests.cs`:
        - the AC's four cases, through `ShotGrade.Scored`: 0, 0.4 % (e.g. composition 1 × timing 0.004), 0.6 %, and
          −0 (timing `-0f`). **Assert the −0 fixture's sign bit is set** before asserting the text.
        - `Percent` never prints a minus sign, and prints `0 %` for NaN, ±∞ (if `Sane` lets them through) and
          negatives.
        - **The agreement property:** sweep `percent01` from 0 to 0.03 in steps of 0.0001 and assert, for each value,
          `PrintsZero(p) == (Percent(p) == Percent(0f))`, and that the colour `ColorFor` picks is red **iff** the text
          printed is the zero string. This pins *agreement*, not a number you expected.
        - Run the same agreement under `CultureInfo.CurrentCulture` set to en-US, fr-FR and invariant (restore it in
          `finally`), to prove the pin holds.

- [x] **Task 4 — `GradeHudConfig`: red, outline, `ColorFor`, validator (AC1, AC3, AC4, AC5)**
  - [x] 4.1 `missColor` → the red chosen in 1d, in **both** the C# initialiser and
        `Assets/Data/UI/GradeHudConfig.asset`. **Keep the field name** (`missColor`): the AC names it, and a rename
        needs `[FormerlySerializedAs]` and touches the asset for nothing (`deferred-work.md:290` is the same trade
        for `countedColor`). Rewrite its tooltip: it now colours **both** a MISS and a counted shot that prints `0 %`,
        "anything that earned nothing".
  - [x] 4.2 Add `outlineColor` and `outlineDistance` with tooltips, named constants, `Safe*` accessors and bounds
        (1b). Add both to the asset YAML by hand (memory: *Create SO assets via YAML*; the file is LF). Then
        `refresh_unity` and **read the values back** with `AssetDatabase.LoadAssetAtPath` + `execute_code` (CLAUDE.md
        trap: a stale asset runs with a clean console).
  - [x] 4.3 Add `ColorFor(...)` (1c). Keep `CountedColorFor` as it is (AC3). Update its doc comment where it talks
        about the 0 % shot being amber.
  - [x] 4.4 `TryGetConfigProblem`:
        - keep all six pairs;
        - add the outline checks (distance non-finite, ≤ 0 or above the max; colour invisible);
        - optionally, the "drifted back to salmon" check (1d);
        - optionally, a cheap **authored** text-vs-outline contrast warning for each readout colour, labelled as
          authored-only. It is not AC4's measurement: the panel and the world are scene data
          (`deferred-work.md:285`).
        Report every problem in one message, as today.
  - [x] 4.5 **Fix the wording that this story makes false.** `WhyMiss` says a counted shot and a miss "both can read
        1 star, so colour is most of what separates them" (`GradeHudConfig.cs:404-406`). After this story a counted 0 %
        and a miss **share** red on purpose: colour now separates *earned something* from *earned nothing*, and the
        words (MISSED and dashes against the breakdown) separate a miss from a 0 % shot. Re-read every comment and
        tooltip in `GradeHudConfig.cs`, `GradeHud.cs` and `CountedColorFor` that mentions salmon, "the miss colour",
        or the 0 % shot being amber, and correct it. The 2026-09-27 review spent a whole pass correcting six false
        comments in this file. Do not add a seventh.
  - [x] 4.6 Tests in `Assets/Tests/EditMode/GradeHudConfigTests.cs`:
        - `ColorFor` for every state through the real factories;
        - the outline `Safe*` accessors under 0, negative, NaN, ±∞ and alpha 0;
        - the validator reports each outline mistake and names the field;
        - `ShippedAsset_PassesItsOwnValidator` still green.
        **Prove the new tests bite by mutation:** break `PrintsZero` (e.g. a `< 0.005f` threshold), then the outline
        repair, then the asset's red (set it back to salmon, if you added the salmon check). Watch the right test fail
        each time, and restore. Story 1.12's reviews found tests that could not fail.

- [x] **Task 5 — `GradeHud`: use the rule, apply the outline (AC1–AC4)**
  - [x] 5.1 `Format` takes every colour from `config.ColorFor(...)` and prints the rating's percentage with
        `GradeText.Percent`. Keep "every branch writes every label" (`GradeHud.cs:254-256`) and the four-state
        structure (`:258-284`). Do not log on the shutter (`:231-232`).
  - [x] 5.2 `Awake` applies the outline to each existing label (1b), after `ApplyFont` and with the same per-label
        null tolerance. A missing label still disables only its own line.
  - [x] 5.3 Do **not** touch the render mode guard, the subscribe/unsubscribe caching, `Show`, the unscaled
        countdown, or `hideOnCameraLowered`. They are proven, and AC6 says nothing else regresses.

- [x] **Task 6 — The rig: measure what AC4 asks, at 1080p (AC4, AC6)**
  - [x] 6.1 Extend `GradeHudShootRig` / `GradeHudShootRunner` (do not fork them): the output directory (2.2), the
        1920×1080 Game View with read-back and restore (1e), and a new legibility phase that captures A/M/P/N per
        state and writes the JSON sidecar. Restore `timeScale`, every label colour, `enabled` flag and outline after
        each variant. The runner stays in the runtime assembly behind `#if UNITY_EDITOR` (CLAUDE.md trap).
  - [x] 6.2 Phase D boundaries: add `outlineDistance` 0, negative, NaN and huge, and `outlineColor` at alpha 0, as
        provoked cases. List them by name at the top of the rig's output, so the clean-console check can tell them from
        findings (CLAUDE.md: "A rig that logs its own errors trains you to ignore errors").
  - [x] 6.3 `tools/verification/measure_hud_legibility.py` (1e). Every number it prints says what it measured
        (label, colour state, background, median and worst 5 %). It refuses to print a number when the mask is empty
        or the freeze control failed. Write a `summary.md` beside the captures.
  - [x] 6.4 The brightest-background survey (1e(ii)) and the every-colour-on-one-background sheet (1e(iii)).
  - [x] 6.5 Update `tools/verification/build_hud_panel_sheet.py`: its labels say `expect SALMON` and
        `1 star expect AMBER` for `c_counted_but_zero`. After this story that panel is **red**. Add the `--dir` of
        this story's captures, and keep the script's lesson text.
  - [x] 6.6 **Name the toolbox decision:** Performance Testing / Profile Analyzer / Graphy are **not** needed. The
        outline adds vertices to a mesh rebuilt once per shutter press, and Phase E already measures the shutter
        (NFR2). Gemini is **not** needed either: nothing here is temporal except the fade, which frame analysis
        settles (7.4).

- [x] **Task 7 — Run it, look at it, tune it (AC1, AC2, AC4)**
  - [x] 7.1 Before any rig run: `refresh_unity`, wait for the compile, `read_console`, and read `missColor`,
        `outlineColor` and `outlineDistance` back through the editor (fact: a stale assembly or asset runs with a clean
        console).
  - [x] 7.2 Run the shortlist (1d) through the rig. **Open the captures yourself** (Read tool), at full size and
        cropped to the panel. Check that:
        - the outline is visible and thin;
        - `★`/`☆` still render (not boxes);
        - the axes and why lines (22 px) are still legible;
        - there are no notches;
        - MISSED and `0 %` are both red and still say different things.
  - [x] 7.3 Choose the red and the thickness by the numbers **and** the pictures. If any colour is below 4.5:1 text
        against outline as rendered, **fix it now** (thicker or darker outline, or a lighter red within "clearly
        red"). Never park a measured failure in a human check (loop rule). Re-measure after every change.
  - [x] 7.4 Re-run `Tools/HUD/Grade HUD — Record the readout (Play)` once and do frame analysis of the fade
        (`tools/verification/README.md`). The outline should fade with the text and leave no dark ghost after the text
        has gone. Use frame analysis only, no Gemini.
  - [x] 7.5 Build the evidence for Alexv:
        - `panels_red.png`: the five bands plus NOT GRADED, at 1080p, cropped to the panel;
        - `before_after.png`: the same frozen frame, salmon with no outline against red with the outline, and amber
          with no outline against amber with the outline;
        - one sheet per bright background with every colour on it (channel-raised, labelled).
        Confirm none is blank or white before handing it over.

- [x] **Task 8 — Regressions and restore (AC6)**
  - [x] 8.1 EditMode: all green. Report before → after counts.
  - [x] 8.2 `Tools > HUD > Grade HUD Shoot (Play)`, full run, with:
        - every Phase A state's grade and wording unchanged (diff `hud.txt` **unmasked** for stars and percentages:
          lessons, "masking digits also masks the star ratings");
        - Phase B still 0 HUD pixels in a stored photograph;
        - Phase C stress unchanged;
        - Phase D every boundary logs once and clears;
        - Phase E shutter time reported against 0.2 s.
  - [x] 8.3 `Tools > Grading > Photo Shoot (Play)` and `Tools > Gallery > Gallery Shoot (Play)` run to the end with no
        exception. The gallery's captions are byte-identical (it is untouched, 1f).
  - [x] 8.4 Console: zero errors and zero new warnings outside the named provoked cases.
  - [x] 8.5 **Restore and read back:**
        - SampleScene open and **not dirty**;
        - no `(rig)` objects;
        - `Time.timeScale` 1;
        - the **Game View size** back to what it was;
        - `GradeHudConfig.asset` holds exactly the committed values;
        - no SessionState rig key left;
        - `git status` shows only intended files.
        After any build or rig that touches settings, check `ProjectSettings/` and the URP assets against HEAD
        (lessons, 1-14).

- [x] **Task 9 — Records and the human check (AC7)**
  - [x] 9.1 Dev Agent Record: decisions 1a–1f with evidence, the measured table (every colour × line × background,
        both contrasts, before and after), the final red and outline values, the mutation results, and the File List.
  - [x] 9.2 `deferred-work.md`:
        - the gallery's salmon (1f), for 1.17;
        - the CVD numbers added to the `:287` entry;
        - close or update any entry this story resolves.
  - [x] 9.3 Queue AC7 in `human_checks` (see *Human checks to queue* below). Never mark it verified.
  - [x] 9.4 sprint-status → `review` when Tasks 1–8 are done, with a `last_updated` entry that follows the YAML
        rules (no `: ` and no trailing `:`; continuation lines indented; verify with `yaml.safe_load`; edit the real
        key near line 40, not the commented line 2).

### Review Findings

Code review on 2026-11-15, loop REVIEW phase (attempt 1), of `91d3f8b..270ffed`. A fresh session that did not
write the code ran it.

**How the review was run.**
- **Layers:** Blind Hunter (diff only), Edge Case Hunter (diff + project) and Acceptance Auditor (diff + story +
  evidence summaries). The `unity-perf:code-standards` lens was applied as a condensed pass over the hard rules: the
  three layers had already covered its four perspectives for ~200 shipped lines, and a play-mode probe settled their
  claims.
- **What was reviewed:** the shipped code (`GradeText`, `GradeHudConfig`, `GradeHud`, the asset) and its tests. The
  rigs (`GradeHudShootRunner*`, `GradeHudShootRig`, `tools/verification/*.py`) were run as tools, not reviewed.
- **How findings were checked:** every finding was reproduced in Play mode before it was reported. The probe built
  throwaway HUDs through the real `GradeHud.Awake` and `Format`, using a private channel and copies of the shipped
  config. It pushed 2,302 shots through the real handler under six cultures, and rendered the outline to count its
  pixels.
- **Evidence:** `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/review/` (`probe/*.txt`,
  `probe/*.png`, `mutation_results.md`, `regression-hud/`).

**Patches, all applied and re-verified:**
- [x] [Review][Patch] **A reused `Outline` was never switched on** [Assets/Scripts/UI/GradeHud.cs `OutlineLabel`].
  - *Symptom:* an `Outline` the label already carried, unticked in the Inspector or arriving disabled on a cloned
    HUD, was configured but left disabled. `Outline.ModifyMesh` returns at `!IsActive()`, so that line drew with no
    outline and a clean console.
  - *Reproduced:* 1,324 outline pixels on a fresh label, **0** on one that carried a disabled `Outline`, and the
    clone inherited it (`probe/R1_fresh_outline.png`, `probe/R2_pre_disabled_outline.png`).
  - *Fixed:* `outline.enabled = true`. After the fix, a fresh label, a pre-disabled one and its clone each show 1,324
    (`probe/F1_after_fix_*.png`, `probe/probe_after_fix.txt`).
  - *Found by:* blind + edge.
- [x] [Review][Patch] **"The whole readout prints in one culture" was false** [Assets/Scripts/Grading/GradeText.cs
  `TimingAdvice`; GradeHud.cs comment].
  - *Symptom:* the why line still used `{x:0.0}` in the player's culture. Through the real `GradeHud`, fr-FR, de-DE
    and tr-TR printed "2,4s late" under an invariant "92 %" breakdown.
  - *Fixed:* `TimingAdvice` pins `InvariantCulture`; en-US output is byte-identical. Every culture now prints "2.4s
    late".
  - *Test:* `TimingAdvice_DoesNotFollowThePlayersLocale`, five cultures, mutation-proven.
  - *Found by:* blind.
- [x] [Review][Patch] **Three validator messages described consequences uGUI does not produce**
  [Assets/Scripts/UI/GradeHudConfig.cs `WhyOutlineDistance`, the `outlineColor` check].
  - A negative thickness was said to draw "no outline at all". It draws its mirror image: 4,402 dark pixels at −2,
    4,402 at +2 (`probe/R4`, `probe/R5`).
  - ±∞ was said to reach uGUI "unclamped". The setter clamps it to 600, so four ghost copies land at the screen edge
    (`probe/R6`).
  - A NaN colour channel with a valid alpha was said to be "not drawn". It is drawn, with that channel at 0.
  - *Fixed:* each message now matches the value typed, and the tests pin 0, −2, NaN, ±∞ and the NaN channel.
  - *Found by:* edge + auditor.
- [x] [Review][Patch] **A test assertion could not fail** [Assets/Tests/EditMode/GradeHudConfigTests.cs
  `ColorFor_IsRedExactlyWhenTheReadoutPrintsZero`].
  - *Symptom:* it compared `Percent(p) == Percent(0)` against `PrintsZero(p)`, which is defined as exactly that.
  - *Fixed:* replaced by an independent oracle that reads the number back out of the printed text.
  - *Mutation:* a `< 0.01f` threshold now fails on that line at exactly 0.005 ("1 %") (`review/mutation_results.md`).
  - *Found by:* blind.
- [x] [Review][Patch] **Six comments and tooltips contradicted the evidence or had gone stale** [GradeHudConfig.cs,
  GradeHud.cs]. This is Task 4.5's risk again. Each one now says what was measured:
  - `MaxOutlineDistance` said "2 is the last thickness that is still one outline", while its own photograph shows
    the "·" split at 2.
  - The `outlineDistance` tooltip did not say that 1.0 measured 4.36:1 for the red.
  - The `missColor` tooltip said "2 degrees". It is 1.3° in Lab; in HSV, salmon sits at 11° against the validator's
    15° window.
  - "Every 0 % shot is 1★" depends on the star thresholds.
  - `ReportConfusable` still called a counted shot reading like a miss a failure.
  - The `ApplyOutline` doc credited `useGraphicAlpha` with the CanvasGroup fade. The CanvasGroup fades the whole
    renderer; `useGraphicAlpha` follows the text colour's own alpha.
  - *Found by:* auditor + edge.

**Deferred, logged in `deferred-work.md` under "code review of story-1-19":**
- [x] [Review][Defer] **Outline tunables apply once at Awake, not live** [GradeHud.cs `ApplyOutline`] — deferred.
  - *Reproduced:* the text colour followed a Play-mode edit on the next shot; the outline stayed at 1.5.
  - *Why deferred:* the candidate sweep writes thicknesses onto the components and depends on a shot not overwriting
    them. The tooltip and the code now say it applies at Awake.
- [x] [Review][Defer] **The guard bands are looser than the measurements** [GradeHudConfig.cs `MinOutlineDistance`,
  `MaxOutlineDistance`, `MinOutlineAlpha`] — deferred.
  - Inside the range with a clean console: 1.0 gives the red 4.36:1, 2.0 splits the smallest glyphs, and alpha 0.85
    gives 4.23:1. Repairs of 0 and −2 land on 1.0.
  - *Why deferred:* narrowing the range needs photographs between 1.5 and 2, and Alexv's call on "thin". The numbers
    are now in the tooltips.
- [x] [Review][Defer] **A `Shadow` authored on a label stacks under the code-added `Outline`** [GradeHud.cs
  `OutlineLabel`] — deferred.
  - *Reproduced:* the order is Shadow then Outline, so the outline copies the shadow too, giving a heavier, smeared
    grey halo (`probe/R3_pre_shadow_plus_outline.png`).
  - *Why deferred:* no label carries a `Shadow` today.
- [x] [Review][Defer] **Below 1080p the outline thins** [CanvasScaler match 0.5] — deferred, unmeasured. At 1280×720,
  1.5 units become 1.0 px and the minimum becomes 0.67 px. 1080p is the shipping target.

**Dismissed (6):**
- *A NOT GRADED or MISSED branch could disagree with `ColorFor`* (blind, hypothesis). The probe shows
  `Missed(Unevaluated)` and `default` both read NOT GRADED in grey; the conditions are identical.
- *The culture cases in the agreement test are redundant* (blind). They guard the pin against regressing, and
  mutation M2 shows they bite.
- *Overlapping translucent outline copies darken mid-fade* (edge, hypothesis). Not reproduced. The dev's frame
  analysis measured the outline tracking the group-alpha model and matching the world once gone; what is left is
  perceptual and belongs to AC7.
- *The gallery and other readouts disagree with the HUD* (edge). Already deferred to Story 1.17 by the dev.
- *`b1_three_star_cream` renders amber, and `build_hud_panel_sheet.py` labels it CREAM* (auditor). That is a rig
  scenario and label. Cream is covered by the channel-raised sets, and Alexv's `panels_red.png` is labelled
  correctly.
- *"Sets refused: 0" sits beside `a_money_shot` dropping its no-outline numbers* (auditor). This is disclosed per
  variant.

**Re-verification:**
- EditMode **290 → 297**, all green. Mutation proof: with `TimingAdvice` unpinned and `PrintsZero` turned into a
  threshold, 10 tests failed, each the one meant to catch it.
- Console: no error, and no warning from this change. The `[Pool]` lines are provoked on purpose by
  `ObjectPoolTests`, and the PhysX large-triangle advisory is pre-existing.
- `Tools > HUD > Grade HUD Shoot (Play)`, full run on the fixed code (`56a8aa0`), written to
  `review/regression-hud/` through the one-shot output key, so Story 1.12's `hud/` and the dev's folders are
  untouched.
  - **Phase A:** every label agrees, unmasked, with its own grade (stars, percentage and wording), and the counted
    0 % shot reads red with the outline (`review/regression-hud_c_counted_but_zero_panel.png`). Grades moved by the
    usual live-pose variance (89 → 88 %, 51 → 48 %).
    - `f_blocked` caught the drunk between cycles (`NoSubject`, "there was nobody in the frame"), which is correct
      wording for that grade.
    - `h_behind_you` graded `OutsideFrustum`, as on HEAD's own baseline.
  - **Phase B:** 0 of 12 thumbnails contaminated.
  - **Phase D:** prints the corrected messages for 0 and −2, and every boundary still applies (1.5, or 1.0 for
    0 and −2). The NaN and ∞ wording was tightened after this run, text only, so that it claims nothing that was
    not rendered. The 297 tests ran on the final wording.
  - **Phase E:** mean 30.1 ms with the HUD listening against 27.1 ms with it disabled (worst 47.8 ms), against the
    200 ms budget.
- **Editor restored and read back:** SampleScene open and not dirty, no rig objects, `timeScale` 1, culture en-US,
  Game View index 0, no `GradeHudShoot.*` SessionState keys, and the asset holds the committed values. `git status`
  shows only the intended files.

## Dev Notes

### What already exists. Read these before writing anything

| What | Where | Why it matters here |
|---|---|---|
| The readout | `Assets/Scripts/UI/GradeHud.cs` (423 lines, LF) | `Format` `:252-326` picks colour per state. `SetColor` `:366-371` colours all three labels. `ApplyFont` `:343-359` is the per-label pattern to copy for the outline. The render-mode guard `:127-135` is the "code owns it" precedent. |
| Its config | `Assets/Scripts/UI/GradeHudConfig.cs` (441 lines, LF) | Colours `:48-76`. Constants and `Safe*` `:78-178`. `ClampFinite`/`Visible` `:193-220`. ΔE `:237-261`. `TryGetConfigProblem` `:273-325`. `WhyMiss` `:404-406` (becomes false). Why there is no `OnValidate` `:421-439`. |
| The asset | `Assets/Data/UI/GradeHudConfig.asset` (22 lines, LF) | Hand-authored YAML. Add the outline keys. Script GUID `51ab917e34438ad4e8b2b303fa70d132`. |
| Wording | `Assets/Scripts/Grading/GradeText.cs` (144 lines, LF) | `Stars`, `MissLong`, `TimingAdvice`: unchanged. `Percent`/`PrintsZero` go here (Grading is reachable from both UI and Gallery). |
| The grade | `Assets/Scripts/Grading/ShotGrade.cs` | `Scored` `:229-235` (percent = composition × timing). `Missed` `:281`. `Placeholder` `:289`. `Sane` `:212` keeps −0. `Counted` `:180`. `IsMiss` `:184`. `StarScale.StarsFor` `:36-44`. |
| HUD tests | `Assets/Tests/EditMode/GradeHudConfigTests.cs`, `GradeTextTests.cs` | Fixtures are `CreateInstance` (C# defaults); `ShippedAsset_PassesItsOwnValidator` `:260-310` reads every real `GradeHudConfig` asset. |
| The HUD rig | `Assets/Scripts/Editor/GradeHudShootRig.cs` (menus `:109`, `:112`; `OutputDir` `:59`; scene restore via `SessionState` `:63, :90-93, :159, :234`) and `Assets/Scripts/UI/GradeHudShootRunner.cs` (2083 lines) | Phase 0 capture-technique controls `:261`. Phase R recording `:367`. Phase A states `:592-615` + `d_nobody_there` `:659` + `i_not_graded` `:801`. `CaptureAndPhotograph` `:813` (hidden vs shown frame, settled 0.5 s `:847`). Phase B `:901`. Phase C `:1000`. Phase D `:1156-1226`. Phase E `:1232`. `HudRect()` `:1348`. `_peakDwell` `:1652`. |
| Panel sheet | `tools/verification/build_hud_panel_sheet.py` | Read its header: five auto-detectors that lied. Its `PANELS` labels become wrong (6.5). |
| CVD maths | `_bmad-output/verification/review-1-12-banding/cvd.py` | Vienot matrices, ready to reuse (1d). |
| Red shortlist | `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/create/red_candidates.{py,txt}` | Authored-only numbers, computed for this story. |
| The scene | `SampleScene.unity`: `GradeHudCanvas` l.212143, `RatingLabel` l.223203, `AxesLabel` l.359674, `WhyLabel` l.61065, `Panel` l.437877 (Image `&2063247044`, colour alpha 0.78) | Read-only for this story if you follow 1b. |

### Architecture compliance (non-negotiable)

- **Tunables in a ScriptableObject, not code literals** (`game-architecture.md:685-694`, Consistency Rules
  `:1005-1014`). The outline colour and thickness go on `GradeHudConfig`. The ΔE floor stays a named constant.
- **Dependency direction** (`:818-821`): `UI` → `Grading`/`PhotoMode`, never the reverse. `GradeText` (Grading) must
  not reference `GradeHudConfig` (UI). The colour choice lives in UI and calls the Grading formatter.
- **Cache component refs in `Awake`; never `GetComponent` in `Update`.** The outline is set up once, in `Awake`.
- **Fail-soft must not mean invisible** (NFR8 + the project's repeated failure): every new value is repaired and
  reported. Nothing throws.
- **Format once per shutter press, never in `Update`** (`GradeHud.cs:226-229`).
- **The HUD stays Screen Space – Overlay** and never reaches a stored photograph. Phase B proves it again (AC6).
- **Shipped code is not debug code.** The outline and the colours ship. Nothing new goes behind `#if UNITY_EDITOR`
  except the rig.

### The genuinely new risks in this story

1. **A colour rule and a text rule that disagree.** The bug is a shot printing `1 %` in red, or `0 %` in amber. The
   fix is one formatter (1c), and the guard is the agreement sweep (3.3).
2. **Measuring the wrong thing with confidence.** A 953×476 capture, a mask that caught the outline, a background
   that moved between variants, or a fade frame read as the hold. Each produces a clean-looking number. The controls
   in 1e exist for exactly these.
3. **A silent-nothing outline.** A thickness of 0 or NaN, or an alpha-0 colour, in a hand-typed asset. Repair it and
   report it (1b).
4. **Comments that become false** (4.5). This file has a history of them.
5. **Overwriting Story 1.12's evidence** (2.1).

### Project Structure Notes

- New: `tools/verification/measure_hud_legibility.py`, plus the evidence under
  `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/` (git-ignored, never `Temp/`).
- Modified (expected):
  - `Assets/Scripts/Grading/GradeText.cs`
  - `Assets/Scripts/UI/GradeHudConfig.cs`
  - `Assets/Scripts/UI/GradeHud.cs`
  - `Assets/Data/UI/GradeHudConfig.asset`
  - `Assets/Tests/EditMode/GradeTextTests.cs`, `GradeHudConfigTests.cs`
  - `Assets/Scripts/UI/GradeHudShootRunner.cs`, `Assets/Scripts/Editor/GradeHudShootRig.cs`
  - possibly `Assets/Scripts/Editor/CameraGame.Editor.asmdef` (only if you use test-helper)
  - `tools/verification/build_hud_panel_sheet.py`
  - `_bmad-output/implementation-artifacts/deferred-work.md`, `sprint-status.yaml`, this file
- **Not expected:** `SampleScene.unity`, `GalleryView.cs`, `ShotGrade.cs`, `ShotGrader.cs`, `GradingConfig*`.
  Touching any of them means a decision went a different way. Record why.
- The C# files and the asset are LF. Keep them LF (lessons: a CRLF/LF flip becomes a whole-file diff). Stage by name,
  with `.meta` for any new asset.

### Previous Story Intelligence

- **Story 1.12** (the readout this story changes), in its *Change Log*, *Review Findings* and handover:
  - Banding came from Alexv's own check (5★ and 0 % "not obvious which one is which").
  - The amber was re-tuned twice by **measuring the rendered colour**, not the authored one ("chosen from the
    authored colour and never re-measured" was a real defect on 2026-09-27).
  - Tests that could not fail were proven by mutation.
  - `ShippedAsset_PassesItsOwnValidator` exists because `CreateInstance` fixtures test the C# defaults while the
    player sees the asset.
  - The panel went 72 → 78 % and Alexv saw no difference.
  - The 2026-11-14 Change Log entry is the origin of this story.
- **Story 1.11**: the `OnValidate` trap (it silently repairs the very values the validator should report), and the
  gallery's short wording is load-bearing. Neither file is touched here.
- **Stories 2.1 / 2.2** (scale, whole town): every rig reads the eye height from `PlayerConfig`. The HUD rig's
  scenarios are in subject heights and scale-invariant. Since 2.2 the pines have working trunk colliders, so some
  vantages now grade *Occluded* where they used to count. `f_blocked` / `e_too_far` / `g_out_of_frame` may land
  on different backgrounds than in 1.12. That is fine: measure what is there, and record it.
- **Story 1.14**: the build-hygiene lessons (restore settings re-serialised by a build) only matter if you choose to
  build. This story does not need a Windows build: the HUD is identical in the editor at 1920×1080.

### Inherited context you should not rediscover

- An off-peak shot scores a hard **0 % → 1★** (timing is the first pillar; as designed). It is common: the town
  placement study had 24 of 24 shots at `counted — 0% 1★`. After this story those read **red**. That is the intent:
  they earned nothing. 1.16's peak-coverage floor will make a counted 0 % rarer for clips, and the rule still holds.
- Two readouts show at once in the editor: the editor-only debug overlay (top-left, `PhotoModeController.OnGUI`) and
  this HUD (bottom). Do not recolour the debug overlay. It is not the player's readout.
- The grader's occlusion test is ray-based and sparse. A readout can say `seen 100 %` beside foliage. That is not a
  HUD defect.
- `hideOnCameraLowered` is still an unplayed feel decision (`deferred-work.md:202`). Leave it.

### Git Intelligence

- `4f3d858` **Add Story 1.19** (2026-11-14): the epics text and the 1.12 Change Log correction. Written from a
  separate worktree. sprint-status's `last_updated` was deliberately left for the loop.
- `996dd51` / `d70620e` **toolbox packages** (2026-11-14): test-helper 1.6.4 is installed. Nothing in the game uses
  it yet.
- The 1.12 history (`git log -- Assets/Scripts/UI/`): banding `636b4cc`, the 2026-09-12 and 2026-09-27 review patches.
  Read them if a comment's history is unclear.

### Latest technical information (measured in this project on 2026-11-15)

- **Unity 6000.3.8f1, Mono (Standalone has no IL2CPP override in `ProjectSettings`), `com.unity.ugui` 2.0.0.**
  `UnityEngine.UI.Outline` resolves from the `UnityEngine.UI` assembly.
- **Percent formatting in this runtime**: see fact 4. Unity's Mono en-US uses the pattern `n %` (with a space), unlike
  .NET Core's `n%`, and does **not** print `-0`. Do not assume another runtime's behaviour. Re-measure with
  `execute_code` if you doubt it.
- **Windows culture on this machine:** en-US (`Get-Culture`), percent pattern 1.
- **`ScreenCapture.CaptureScreenshotAsTexture()`** after `WaitForEndOfFrame`, with no `targetTexture` on the camera,
  is the only technique that sees an Overlay canvas (CLAUDE.md trap; Story 1.12 Phase 0 measured 100.00 % / 0.00 %
  on its controls). Re-run Phase 0's controls at 1920×1080.
- **test-helper 1.6.4**: `TestHelper.GameViewControlHelper.SetResolution(uint w, uint h, string name)` in assembly
  `TestHelper.RuntimeInternals` (define-constrained to the editor/test context). It has no getter for the current size.

### Verification plan: what would convince a sceptic

| Claim | Evidence | How it is judged |
|---|---|---|
| A miss and a printed `0 %` are the same red; `1 %` keeps its band | EditMode tests through the real factories (0, 0.4 %, 0.6 %, −0) + the agreement sweep + `panels_red.png` | tests green, and **bite** under mutation; the picture shows MISSED and `0 %` red, `1 %` amber |
| The red reads as red, not salmon | rendered colour off the captures (chroma and saturation), `before_after.png` | rendered C* / s clearly above salmon's; then Alexv (AC7) |
| Every colour is readable on the brightest real backgrounds | `measure_hud_legibility.py` output: every colour × line × background, text-vs-outline and text-vs-panel, median and worst 5 %, before and after | text-vs-outline ≥ 4.5:1 everywhere as rendered (fix it if not); text-vs-panel reported; controls passed (frozen, mask non-empty, 1080p, scale 1.0) |
| The bright-ground misses are covered | `e_too_far`, `g_out_of_frame` rows + the brightest-background survey | present in both `before/` and `after/` |
| The validator guards it | `GradeHudConfigTests` + `ShippedAsset_PassesItsOwnValidator`; Phase D outline boundaries in the rig log | each mistake named once at Awake; the readout still shows; it clears |
| Nothing else regressed | HUD rig Phases A–E diff (unmasked), Photo Shoot, Gallery Shoot, EditMode before → after | same grades and wording; 0 HUD pixels in stored photos; shutter under 0.2 s; captions byte-identical |
| The outline fades with the text | frame analysis of the readout recording | no outline pixels after the text is gone |
| The editor is back | read-backs listed in 8.5 | each one stated with its value |

**Fail-soft scenarios to provoke on purpose** (name them at the top of the rig output): outline distance 0, negative,
NaN, huge; outline colour alpha 0; plus 1.12's existing Phase D list.

### Human checks to queue (never self-certify)

1. **AC7: does red read as "this earned nothing", and does the outline still look like a camcorder overlay rather
   than a heavy box?**
   - **Look at:**
     - `panels_red.png`, the five bands plus NOT GRADED at 1080p;
     - `before_after.png`, salmon without outline against red with outline, on the same frozen frame;
     - one bright-background sheet.
   - **Ask:**
     - Is red unmistakably "nothing earned"?
     - Do the MISSED and `0 %` panels still say different things?
     - Is the outline thin enough?
   - **Optional, for feel only:** play SampleScene, take a late shot and a missed one.
   - **Give him:** your own measured read of every number, so he is the second opinion, not the first.
2. **Decision batch, not a check:** 1a–1f, the chosen red and thickness, and test-helper in the rig if used.

### Project Context Rules

No `project-context.md` exists. The binding rules are `CLAUDE.md` (*Verifying Your Own Work*, the traps, *Toolbox*),
`loop/lessons.md`, and `loop/prompts/_rules.md` for loop sessions. The ones that bite here:
- see it working;
- measure as rendered;
- restore the editor, including the Game View size;
- evidence goes to `_bmad-output/verification/<story>/`, never `Temp/`;
- MCP for Unity is the only editor channel (never `unity:unity-cli`);
- `refresh_unity` and read back before every rig run;
- perceptual verdicts are Alexv's.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.19 (lines 641-702)] — the ACs (verbatim above) and the 2026-09-27/28 measurements
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.16; game-architecture.md:225-245] — 1.16 reuses the readout for `ClipGrade`
- [Source: _bmad-output/planning-artifacts/epics.md#NFR2, NFR5, NFR8, NFR10] — shutter budget, clean console, fail-soft, legibility
- [Source: _bmad-output/planning-artifacts/game-architecture.md:685-694, :818-840, :1005-1014] — configuration, boundaries, consistency rules
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md:342] — the viewfinder UI is a key art surface that should read like a 2000s camcorder
- [Source: _bmad-output/implementation-artifacts/1-12-grade-feedback-hud.md#Change Log (2026-09-11 … 2026-11-14); #Handover; #Latest technical information] — banding, re-tunes, 78 % panel, the origin of this story
- [Source: _bmad-output/implementation-artifacts/deferred-work.md:202, :210, :212, :214, :285-290] — `hideOnCameraLowered`, P0 rounding, `IsShowing`, the composition voice, validator limits, ΔE76, CVD, placeholder pair, channel clamp, naming
- [Source: Assets/Scripts/UI/GradeHud.cs; GradeHudConfig.cs; Grading/GradeText.cs; Grading/ShotGrade.cs; Data/UI/GradeHudConfig.asset] — line numbers in *What already exists*
- [Source: Assets/Scripts/Editor/GradeHudShootRig.cs; UI/GradeHudShootRunner.cs; tools/verification/build_hud_panel_sheet.py] — the rig and sheet to extend
- [Source: Library/PackageCache/com.unity.ugui@bb329a87fcdc/Runtime/UGUI/UI/Core/VertexModifiers/Outline.cs, Shadow.cs] — how the outline is drawn and clamped
- [Source: Library/PackageCache/com.nowsprinting.test-helper@949a279d2435/RuntimeInternals/GameViewControlHelper.cs] — `SetResolution`
- [Source: CLAUDE.md#Verifying Your Own Work; #Toolbox; loop/lessons.md] — rigs, restore, evidence, traps

## Dev Agent Record

### Agent Model Used

Claude Opus 5.5 (`claude-opus-5-5`), loop DEV phase, attempt 1, 2026-11-15.

### Debug Log References

- Evidence index: `_bmad-output/verification/1-19-a-shot-that-earns-nothing-reads-red/README.md`.
- Runs (each with `hud.txt`, `summary.md`, `legibility_results.json`, crops, masks): `before/` (HEAD), `candidates/`
  (the sweep), `after/` (shipped values), `motion/` (fade), `regression/hud/` (full shoot), `regression/photo-shoot-after`,
  `regression/gallery-after`, `mutation/mutation_results.md`.
- Commits: `7048fa7` (code + rig + measurement), `a46b7c0` (chosen red and bound, control fixes, mutation proof),
  `c3a6a47` (refusals per variant, sheets, deferred work), and this record.

### Completion Notes List

**What was built.**
- `GradeText.Percent` (invariant culture; NaN, negatives and −0 print `0 %`) and `GradeText.PrintsZero`, defined by the
  printed string.
- `GradeHudConfig.ColorFor(bool counted, bool isMiss, int stars, float percent01)` plus `ColorFor(ShotGrade)`.
  `missColor` is red E `(1, 0.33, 0.28)`. New `outlineColor (0,0,0,1)` and `outlineDistance 1.5` with `Safe*` repair,
  bounds 1–2 and validator reports. Two authoring guards were added to the validator: authored text-vs-outline 4.5:1, and
  "missColor still reads as red" (HSV saturation ≥ 0.65, hue within 15° of red).
- `GradeHud`: every colour comes from `ColorFor` and every percentage (rating and axes) from `GradeText.Percent`. Awake adds
  a uGUI `Outline` to each label that exists.
- The rig gained a one-shot `SessionState` output folder, the built-in Full HD Game View preset (restored on exit) and
  Phase L, a new partial file with:
  - frozen A/M/P/N variants;
  - a 384-pose brightest-background survey;
  - channel-raised every-colour sets;
  - a candidate sweep.

  It also gained outline boundaries in Phase D.
- `tools/verification/measure_hud_legibility.py` and `build_hud_legibility_sheets.py` are new.

**Decisions (each also in the result file's `decisions`; Alexv may veto):**
- **1a — outline technique:** uGUI's built-in `Outline` (recommended). At the shipped 1.5 px there are no notches and the
  ★/☆ glyphs render (`candidates/thickness_compare.png`, `after/legibility/*_A.png`). Alternative A (8-direction effect)
  is not needed at 1.5 px, and is logged as the route if a thicker outline is ever wanted. TextMeshPro was rejected.
- **1b — where it lives:**
  - `outlineColor`/`outlineDistance` on `GradeHudConfig`, applied by `GradeHud.Awake` (code, not scene).
    `SampleScene.unity` is untouched.
  - Bounds: min 1 (one pixel at 1080p). Max 2, measured: at 2 px the "·" separator already splits into four dots, and at
    3 px every line doubles.
  - Outline alpha 1, chosen over 0.85: 0.85 costs 0.4–0.5 of contrast (red E 4.70 → 4.23 worst 5 % on the brightest
    background).
- **1c — one source of truth:** `GradeText.Percent`/`PrintsZero` and `GradeHudConfig.ColorFor`, as recommended.
  - *Extra, recorded:* the axes line also prints through `GradeText.Percent`, so the whole readout is in one culture. It is
    byte-identical in en-US; the Phase A wording diff proves it.
- **1d — which red:** **red E `(1, 0.33, 0.28)`**, rendered **(255, 85, 71)**: HSV s 0.722, Lab C* 77.3 (salmon rendered
  0.549 / 52.6).
  - Choice: the reddest shortlisted red that clears 4.5:1 text-vs-outline at 1.5 px on every measured background *with a
    margin run-to-run noise will not erase*.
  - Worst 5 %, brightest background, 1.5 px: pure 3.84, B 4.33, C 4.51–4.55, **E 4.70**, salmon 6.17.
  - At 2 px every red but pure clears (B 4.83, C 5.04, E 5.22), but the outline shows notches. The thickness was decided
    by the pictures, the red by the numbers.
  - Colour blindness (Viénot, rendered colours):
    - red vs 5★ green under deuteranopia ΔE 35.8 (salmon on the same method 18.3);
    - red vs amber 29.7 under deuteranopia (salmon 37.1), still clearly different.
- **1e — measurement:** as recommended, with these changes, each found by running:
  - **Game View by reflection, not test-helper.** It selects the BUILT-IN Full HD preset, because test-helper's
    `SetResolution` adds a custom size to Alexv's editor preferences. test-helper is not used, and `CameraGame.Editor.asmdef`
    is untouched.
  - **M paints the outline marker green**, an exact outline mask in place of "A darker than P".
  - **The freeze control is judged within 160 px of the panel, with the whole-frame difference box logged.** The editor-only
    debug overlay expires on unscaled time mid-freeze, and refused `a_money_shot` for pixels nowhere near the panel.
  - **A positive control:** an unfrozen frame one live frame before the freeze. The first version read 0/255 on all 10
    states because it froze the same simulation instant. Fixed, it reads 106–165/255 wherever the drunk is in view.
  - **Refusals per variant:** a fade on N drops only the no-outline numbers.
- **1f — scope:** gallery (still salmon, still culture-dependent `P0`), `P0` 100 % rounding, the why-line voice and the 78 %
  panel are all unchanged; the gallery item is logged for Story 1.17.
- **6.6 — toolbox:** Performance Testing, Profile Analyzer and Graphy were not needed: Phase E measures the shutter with the
  HUD listening against disabled in the same run (mean 25.0 ms against 39.0 ms; HUD cost lost in the noise; worst 60 ms
  against the 200 ms budget). Gemini was not needed: the fade was settled by frame analysis.

**AC4 — measured off real captures at 1920×1080, as rendered** (worst 5 % of glyph-core pixels, lowest line, every
background; `after/summary.md`, `before/summary.md`):

| readout state | before: text vs panel, no outline | after: **text vs outline** (target 4.5) | after: text vs panel (reported) |
|---|---|---|---|
| 5★ green (channel-raised, 4 backgrounds) | 4.34 | **6.34** | 4.29 |
| 3★ cream | 5.36 | **7.52** | 5.31 |
| 1★ printing 1 %, amber | 4.68 | **7.55** | 4.65 |
| counted 0 % (amber → red) | 4.68 | **4.64** | 1.94 |
| MISS (salmon → red) | 2.74 | **4.68** | 1.92 |
| NOT GRADED grey | 2.34 | **5.17** | 2.28 |
| `e_too_far` (real shutter, bright ground) | 3.59 | **5.00** | 2.56 |
| `g_out_of_frame` (real shutter, bright ground) | 3.63 | **4.99** | 2.58 |
| `c_counted_but_zero` (real shutter) | 6.18 | **5.01** | 2.53 |
| `f_blocked` / `a_money_shot` (real shutter) | 6.51 / 5.63 | **5.58 / 7.82** | 4.70 / 5.66 |

- Every readout colour clears 4.5:1 against its outline on every measured background. The red's margin is the smallest
  (4.64–4.68 on the brightest survey background), so it is logged as a tuning constraint in `deferred-work.md`.
- Text-vs-panel is reported, not required: on bright ground no red reaches 4.5:1 against the 78 % panel. That is why the
  outline exists.
- This is not a "WCAG compliant" claim.
- Brightest backgrounds: survey spot 4, `(28.7, 2.1, 14.0)`, heading 270, pitch −12, mean L 0.46.
- Controls passed:
  - freeze 0/255 on all 34 sets;
  - 1080p and scale 1.0;
  - masks non-empty (363–1,330 core px per line);
  - rendered colour within ΔE 0.5 of authored.

**AC1–AC3 on rendered pixels:** a MISS and a counted 0 % are the same (255, 85, 71). The words still differ: MISSED and
dashes against `0 %`, a breakdown and the timing advice. A shot printing `1 %` stays amber (255, 224, 79).

**AC5:** EditMode **254 → 290**, all green. The agreement sweep runs 0–3 % in steps of 0.01 % under en-US, fr-FR and
invariant cultures. Mutation proof (`mutation/mutation_results.md`), 19 of 290 failing, each on the right tests, then
restored to 290/290:
- a `< 0.01f` threshold in `PrintsZero` fails the sweep at exactly 0.005;
- removing the outline repair fails 11 tests;
- the shipped asset set back to salmon fails `ShippedAsset_PassesItsOwnValidator`.

**AC6 — nothing else regresses:**
- **Phase A wording:** byte-identical for every miss whose grade matched HEAD. Every counted label agrees, unmasked, with
  its own grade (stars and percentage) on HEAD's template (`regression/phase_a_wording_diff.txt`). `h_behind_you` graded
  BehindCamera here and OutsideFrustum on HEAD; Story 1.12's own baseline also had OutsideFrustum. That is the grader's
  call on a live pose (no grading code changed), and each label matches its grade.
- **Phase B:** 0 of 12 thumbnails contaminated.
- **Phase C:** alpha 1.00 after a mid-fade capture, hidden when the camera is lowered, not over the gallery, and pooled
  cycles agree.
- **Phase D:** each outline boundary reported once and repaired (0 → 1, −2 → 1, NaN → 1.5, 999 → 2, alpha 0 → opaque),
  and the readout still clears in 2.8 s.
- **Phase E:** as above.
- **Photo Shoot and Gallery Shoot:** both ran to the end with no exception. The gallery's headers are identical and its
  pictures still show the salmon `missed —` captions. Its folders were restored byte-identical.
- **Fade:** the outline brightens with the group alpha (measured 0.011 → 0.057 → 0.174 → 0.425 → 0.759 against the
  fading model 0 → 0.076 → 0.226 → 0.481 → 0.759; a ghost would stay at 0). The pixels equal the world to 4 decimals once
  it has gone (`motion/analysis/`).
- **Console:** no error or warning outside the named provoked cases and the pre-existing PhysX large-triangle advisory.
- **Editor restored and read back:**
  - SampleScene open and not dirty, with no rig objects;
  - `timeScale` 1;
  - Game View index 0 (Free Aspect);
  - no `GradeHudShoot.*` SessionState keys;
  - the asset holds the committed values;
  - ProjectSettings, URP assets and Packages unchanged;
  - `hud/` and `hud-motion/` (Story 1.12's evidence) never written.

**Left for Alexv:** AC7 (human check, below; never self-certified) and the decision batch above.

### File List

- `Assets/Scripts/Grading/GradeText.cs` (modified)
- `Assets/Scripts/UI/GradeHudConfig.cs` (modified)
- `Assets/Scripts/UI/GradeHud.cs` (modified)
- `Assets/Data/UI/GradeHudConfig.asset` (modified)
- `Assets/Tests/EditMode/GradeTextTests.cs` (modified)
- `Assets/Tests/EditMode/GradeHudConfigTests.cs` (modified)
- `Assets/Scripts/UI/GradeHudShootRunner.cs` (modified)
- `Assets/Scripts/UI/GradeHudShootRunner.Legibility.cs` (new) and `.meta` (new)
- `Assets/Scripts/Editor/GradeHudShootRig.cs` (modified)
- `tools/verification/measure_hud_legibility.py` (new)
- `tools/verification/build_hud_legibility_sheets.py` (new)
- `tools/verification/build_hud_panel_sheet.py` (modified)
- `_bmad-output/implementation-artifacts/deferred-work.md` (modified)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` (modified)
- `_bmad-output/implementation-artifacts/1-19-a-shot-that-earns-nothing-reads-red.md` (this file)
- Not changed, as the story expected: `SampleScene.unity`, `GalleryView.cs`, `ShotGrade.cs`, `ShotGrader.cs`,
  `GradingConfig*`, `CameraGame.Editor.asmdef`.

## Change Log

| Date | Change |
|------|--------|
| 2026-11-15 | Story context created by the loop CREATE phase (`gds-create-story`). 7 ACs (verbatim from epics.md), 9 tasks with 6 gating decisions. Facts measured for it: Mono's `P0` output in this editor (0.005 → `1 %`, −0 → `0 %`, fr-FR → U+066A), `Clamp01(-0f)` keeps the sign, uGUI `Outline` present, and an authored-colour shortlist of reds (`create/red_candidates.txt`). Status → ready-for-dev. |
| 2026-11-15 | Loop DEV phase (`gds-dev-story`). All 9 tasks done. Anything that earned nothing now reads red: a MISS and a counted shot printing `0 %` share red E `(1, 0.33, 0.28)`, decided from the printed text through `GradeText.Percent`/`PrintsZero` and `GradeHudConfig.ColorFor`. A shot printing `1 %` keeps amber. Every readout line has a 1.5 px black uGUI Outline applied in `GradeHud.Awake` from the config. Measured at 1920x1080 on frozen frames with renderer-made masks: every colour clears 4.5:1 against its outline on every measured background (red 4.64 at worst). The red and thickness were chosen by a sweep, the bound of 2 px by photographs. EditMode 254 -> 290, mutation-proven. HUD, Photo and Gallery shoots show no regression. Editor restored. Status -> review; AC7 queued for Alexv. |
| 2026-11-15 | Loop REVIEW phase (`gds-code-review`, attempt 1), by a fresh session: Blind Hunter, Edge Case Hunter and Acceptance Auditor, plus a play-mode probe through the real `GradeHud`. 5 patches applied and re-verified: a reused disabled `Outline` is now switched on (0 → 1,324 outline px); `TimingAdvice` is pinned to the invariant culture (fr-FR printed "2,4s late"); the validator messages for negative, infinite and NaN-channel values now match what uGUI does; a tautological test assertion is replaced by an independent oracle; six comments and tooltips that contradicted the evidence are corrected. 4 deferred (outline applied only at Awake, guard bands looser than the measurements, Shadow stacking, sub-1080p), 6 dismissed. EditMode 290 → 297, mutation-proven; HUD shoot re-run on the fixed code; editor restored. Status stays `review`: AC7 is still Alexv's. |
| 2026-11-15 | Loop CLOSE phase (attempt 1), a fresh session. No code changed. **Checked by running:** a forced recompile left a clean console. EditMode passed 297/297; the only warnings are the `[Pool]` lines `ObjectPoolTests` provokes on purpose and the old PhysX large-triangle advisory. `Tools > HUD > Grade HUD Shoot (Play)` was re-run on HEAD `140ec75` at 1920×1080 into `close/hud/`. **Results of that run:** Phase 0 controls read magenta 100.00 % then 0.00 %. In Phase A, the counted 0 % shot and all six misses render (255, 85, 71); shots printing 15 % and 43 % render amber (255, 224, 79); 89 % renders green; NOT GRADED is grey (`close/close_panels.png`). Phase B found 0 of 12 thumbnails contaminated. In Phase C the alpha returns to 1.00 and the pooled cycles agree. In Phase D every outline boundary logs once, with the HEAD wording, and is repaired. In Phase E the mean with the HUD listening (58.2 ms) is within noise of the mean with it disabled (57.6 ms); the worst sample is 115.6 ms against the 200 ms budget, on a slower laptop day. Every message in the console is one `hud.txt` names as provoked. AC4 re-read from `after/summary.md`: all 102 colour × line × background rows are at or above 4.5:1 text against outline (red 4.64 at worst). **Not re-run, by reasoning:** the Photo Shoot and Gallery Shoot. The only shipped changes after their runs (`a46b7c0`) are in `GradeHud`, `GradeHudConfig` and `GradeText.TimingAdvice`, which only `GradeHud` calls. **Editor read back:** SampleScene open and not dirty, `timeScale` 1, Game View index 0, no `GradeHudShoot.*` keys, asset values as committed. Status stays `review`: AC7 is Alexv's. |
