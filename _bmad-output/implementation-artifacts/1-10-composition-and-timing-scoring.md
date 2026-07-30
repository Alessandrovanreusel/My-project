# Story 1.10: Composition and timing scoring

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want my shot scored on how well I framed it and how close to the peak I clicked,
so that skill in framing and timing is rewarded with a higher grade.

## Acceptance Criteria

**AC1 — Composition scoring (FR6)**
- A shot that passed the subject gate is scored on **how much of the frame the subject fills** and
  **where in the frame it sits**. The score peaks when the subject is prominent (the GDD's
  "~25–50% of the frame") and near a rule-of-thirds line, and falls off when the subject is **too
  small**, **cut off by the frame edge**, or **dead-centre-tiny**.
- ⚠️ "Fills ~25–50% of the frame" is a *designer's* description of a photograph, not a measurement.
  The measured quantity today is the **area of an axis-aligned box** around a tall, thin, arms-down
  figure — mostly empty air — and it reads 4–21% for shots that plainly have him as the subject
  (`deferred-work.md`, 1.9 play-mode section). **Task 2 decides which measurement the curve is
  authored against, by looking at photographs.** Whatever it decides, the gate (AC4) and this curve
  must be expressed in the *same* measure, or the two disagree about what "prominent" means.
- A big subject sitting dead-centre must **not** be punished hard — a centred close-up is a fine
  photograph. The GDD's bad case is "dead-center-**tiny**", i.e. small *and* centred.

**AC2 — Timing scoring (FR7)**
- Timing is full within **±0.5 s** of the peak and decays to **zero by ±2 s**, read from the live
  event lifecycle (`ISubject`), not from wall-clock or a captured timestamp.
- ⚠️ The peak is a **1.5 s window**, not an instant, and `TimeToPeak` is 0 at the window's *start*
  (`EventActor.cs:87-89`, `Begin` seeds it at `spawn.duration + build.duration`, `:363-364`).
  Scoring `Mathf.Abs(TimeToPeak)` naively means a shot on the **last frame of the money shot** is
  1.5 s "late" and scores ~0.33, while the identical shot 1.5 s *before* the peak scores 0. See
  Task 1 — this is the gating decision of the story.
- Both window numbers are `GradingConfig` fields, not literals.

**AC3 — Blend, stars, and a per-axis breakdown (FR4)**
- The subject-gate, composition and timing sub-scores blend into one `ShotGrade`
  (0–100% → 1–5 stars), replacing Story 1.9's raw-coverage placeholder score at the commented seam
  (`ShotGrader.cs:267-273`).
- `ShotGrade` **carries the per-axis breakdown** (subject/composition/timing) and it is available in
  a **release build** — not behind `#if UNITY_EDITOR`. Story 1.12's HUD has to show *why* a shot
  scored what it did, and today the only breakdown lives in editor-only `GradeDetail`
  (`deferred-work.md`, 1.9 review section).
- **5 stars must be reachable by a good shot and unreachable by a mediocre one.** Today the star
  mapping is degenerate: fed raw coverage, a real hit (16–21%) is 1–2★ and 5★ needs ~80% of the
  screen. Prove the new mapping with photographs (Task 7), not with arithmetic.

**AC4 — Re-tune the gate against the real curve (closes the deferred 1.9 item)**
- `minCoverage` is re-tuned so a shot at a **natural portrait distance** counts. At the shipped
  0.08 the animated drunk measures **4.14%** at ~20 units and is rejected while unmistakably being
  the subject (`Temp/PhotoShoot/b_mid.png`, 1.9).
- Because gates run cheapest-first, coverage currently rejects a distant subject *before* the
  occlusion linecasts ever run — so AC1's occlusion half of Story 1.9 is nearly inert. After the
  retune, **prove the occluded case still rejects** at a distance where the subject would otherwise
  pass.
- The retune is decided by **looking at the photographs**, not by arithmetic (`deferred-work.md`:
  "Do not tune this by arithmetic — run the photo shoot and look at the pictures").

**AC5 — Fail-soft, fast, and no regressions (NFR2, NFR5, NFR8)**
- Grading stays **synchronous maths** — no coroutine, no GPU readback, no `FindObjectsOfType`.
  Capture-to-feedback stays under 0.2 s. (1.9 measured 0.0072 ms/call against 16,739 colliders; the
  new work adds arithmetic only, so re-measure only if you add anything that isn't arithmetic.)
- A missing or nonsense `GradingConfig` **never throws and never spams**: every new tunable gets a
  `Safe*` accessor and a `TryGetConfigProblem` check, exactly like the four that exist. A curve that
  can never award a good score is an authoring mistake and must **warn once at Awake** — fail-soft
  must not mean invisible.
- Console shows **zero errors and zero new warnings**. Baseline is the two known pre-existing
  warnings (the Version Control project-link notice and `ThirdPersonCamera.cs:43` "POV Camera: Head
  bone not found!"). No regression to the 1.3–1.9 photo flow.

## Tasks / Subtasks

- [x] **Task 1 — Decide what "distance from the peak" means (AC2) — DO THIS FIRST, IT GATES THE TIMING CURVE**
  - [x] **The problem, concretely.** `TownDrunk.asset` runs Spawn 3 s → Build 10 s → **Peak 1.5 s** →
        WindDown 6 s → Despawn 4 s. `EventActor.TimeToPeak` is seeded to 13 s at `Begin` and
        decrements every frame *through and past* the peak (`EventActor.cs:384`), so it reads **0 at
        the first frame of the peak and −1.5 at the last**. The architecture sketch says
        `cfg.TimingCurve.Evaluate(Mathf.Abs(subject.TimeToPeak))` (game-architecture.md:542) — which
        makes the final frame of the money shot score like a shot 1.5 s early. The GDD's ±0.5/±2
        numbers were written for a *point-like* peak; this peak is an interval.
  - [x] **Recommended: measure from the peak WINDOW, not from its start.** Offset is 0 anywhere
        inside the peak, positive before it, negative after it:
        `before → TimeToPeak · during → 0 · after → TimeToPeak + peakDuration`.
        Then "full marks within ±0.5 s of the peak" means half a second either side of the money
        shot, which is what the GDD sentence actually describes and what a player will feel.
  - [x] **Where to compute it.** `ISubject` is the seam grading reads subjects through, and it
        already carries `IsAtPeak` and `TimeToPeak` *for this exact purpose*. Add **one** member —
        e.g. `float PeakOffset` — implemented in `EventActor` (three lines: it knows
        `definition.peak.duration`). Do **not** hand the grader an `EventActor`, an
        `EventDefinition`, or a peak duration parameter: that re-couples grading to concrete event
        types and breaks the boundary the architecture states explicitly (§Architectural Boundaries).
  - [x] **Keep `TimeToPeak` exactly as it is.** Its "counts down continuously and goes negative"
        contract is documented in two places (`ISubject.cs:23-25`, `EventActor.cs:87-89`) and Story
        1.6's review decided it. Add alongside; do not redefine.
  - [x] The cheap alternative — `IsAtPeak ? 0f : Mathf.Abs(TimeToPeak)` — needs no interface change
        and is exact *before* the peak, but over-penalises every shot after it by up to the full peak
        duration. If you take it, **say so in the Dev Agent Record and show the photographs**: at
        this tuning a shot 0.5 s after the money shot ends scores zero, and a player will read that
        as the grader being broken.
  - [x] ⚠️ **Read the subject live.** Subjects are pooled; a cached `ISubject` does not go null when
        its event ends, it silently becomes a *different* event (`ISubject.cs:10-13`). This bit the
        1.9 photo rig. Grade from `EventManager.ActiveActors` at the instant of capture, as
        `GradeBestSubject` already does (`PhotoModeController.cs:426-467`).

- [x] **Task 2 — Decide the prominence measure by MEASURING, before authoring any curve (AC1, AC4)**
  - [x] **Run `Tools > Grading > Photo Shoot (Play)` first, before writing scoring code.** It already
        photographs the real capture path at 11 vantage points and prints coverage for each
        (`Temp/PhotoShoot/shots.txt`). You need those numbers in front of you, because the GDD's
        "25–50%" does not describe anything this code currently measures.
  - [x] **Option A — keep area coverage, retune the curve to reality.** Cheapest; changes no
        measurement. But the sweet spot would have to sit around ~8–20%, which reads nothing like the
        GDD, and the number **breathes with the walk cycle** (7.34% one run, 9.02% the next at the
        same pose — 1.9's recorded shoot), so shots land either side of a threshold by animation
        frame alone.
  - [x] **Option B (recommended) — score the box's HEIGHT fraction** (`rect.height / view.height`).
        Far less sensitive to a narrow silhouette, far steadier across the animation, and closer to
        how a photographer judges "he fills the frame" — this is the fix `deferred-work.md` names
        first. The GDD's "fills ~25–50% of the frame" then maps to a subject occupying a quarter to
        half of the frame's height, which is roughly true of the photographs you are about to look at.
  - [x] **Whichever you pick, the gate and the curve must use the same measure.** If prominence
        becomes height-based, `minCoverage` becomes a height threshold too (rename it — a field
        called `minCoverage` holding a height fraction is a trap for the next reader) and AC4's
        retune is expressed in that measure. Two different definitions of "how big is he" in one
        config is exactly the kind of drift that produced 1.9's two-sources-of-truth problem in
        `EventManager`.
  - [x] Keep reporting **area coverage in `GradeDetail`** whichever you choose — the overlay and
        `shots.txt` have printed it since 1.9 and it is how the last two sessions' evidence is
        expressed. Adding the new measure alongside costs one field.
  - [x] Record the decision and the numbers behind it in the Dev Agent Record. This is a design call
        Alexv will want to see the reasoning for.

- [x] **Task 3 — Extend `GradingConfig` with the composition and timing tunables (AC1, AC2, AC5)**
  - [x] `Assets/Scripts/Grading/GradingConfig.cs`. Add the new fields to the existing class — this
        asset is deliberately "the one place that grows" (its own doc-comment says so).
  - [x] ⚠️ **Prefer plain numeric parameters over `AnimationCurve` fields.** The architecture sketch
        shows `cfg.CoverageCurve.Evaluate(...)` / `cfg.TimingCurve` (game-architecture.md:538-542),
        but three things argue against curves here: this project **hand-writes ScriptableObject
        assets as YAML** (Unity MCP cannot create custom SO assets — [[create-so-asset-via-yaml]]),
        and an `AnimationCurve` serialises as a nested keyframe list with tangents that is miserable
        to author and impossible to eyeball; a curve cannot be *validated* (there is no
        "this curve never reaches 1" check that isn't just sampling it); and a junior tuning by eye
        gets far more out of four named numbers than a curve editor. Suggested shape — a trapezoid,
        which is exactly what the GDD describes:
        - `prominenceIdealMin` / `prominenceIdealMax` — the sweet spot (full marks between them)
        - `prominenceFalloffBelow` / `prominenceFalloffAbove` — how far outside before it hits zero
        - `timingFullSeconds = 0.5f` / `timingZeroSeconds = 2f` — the GDD's ±0.5 / ±2
        - `thirdsWeight` — how much placement can pull the composition score down (see Task 4)
        If you *do* go with `AnimationCurve`, say why in the Dev Agent Record and budget for the YAML.
  - [x] Every new field gets a `[Tooltip]`, a `[Range]`/`[Min]`, and a **`Safe*` accessor** —
        `[Range]` and `OnValidate` are **both editor-only** and hand-written YAML passes through
        neither. `SafeMinCoverage` (`GradingConfig.cs:56-73`) is the worked example, including the
        `Clamp01Finite` NaN handling (`Mathf.Clamp01(NaN)` is NaN — the guard that catches nothing).
  - [x] Extend `TryGetConfigProblem` (`GradingConfig.cs:81-153`) for the new failure shapes, and keep
        its house style: name the mistake, say what it will do to the player's grades, say how to fix
        it. New cases worth catching: an inverted sweet spot (`idealMin > idealMax`), a zero-width
        falloff (nothing outside the sweet spot scores at all), `timingZeroSeconds <= timingFullSeconds`
        (the timing curve becomes a cliff or divides by zero), and any combination where the
        composition score can never reach 1 (5★ unreachable — silently, with a clean console).
  - [x] ⚠️ **Adding a field to the class does not add it to the existing asset.** Write the new keys
        into `Assets/Data/Grading/GradingConfig.asset` explicitly and then **open the asset in the
        Inspector and read the values back**. Do not assume the field initialisers show up.

- [x] **Task 4 — Composition scoring in `ShotGrader` (AC1)**
  - [x] Stays in `Assets/Scripts/Grading/ShotGrader.cs` as pure static maths. The gates in
        `Grade` are Story 1.9's and **do not change shape** — you are replacing the score body at the
        commented seam (`ShotGrader.cs:267-273`), which is one line plus the helpers it calls.
  - [x] **Prominence term:** the trapezoid from Task 3 evaluated on the Task 2 measure. Full marks
        inside the sweet spot, easing to 0 at the falloff bounds. Clamp to [0,1].
  - [x] **Placement term (rule of thirds):** distance from the subject rect's centre to the **nearest
        thirds intersection** (the four points at 1/3 and 2/3 of the viewport in each axis),
        normalised by something scale-free — the viewport diagonal or half-extent — so it behaves the
        same at any resolution. Use `cam.pixelRect` for the viewport (`view` is already computed in
        `Grade`), **never `Screen.width/height`: 1.9's review reproduced a subject dead-centre in an
        offset viewport measuring 0% because of exactly that confusion.**
  - [x] ⚠️ **Do not let placement dominate.** A large subject dead-centre is a good photograph; the
        GDD's bad case is "dead-center-**tiny**". Two ways to honour that: weight the thirds penalty
        by `thirdsWeight` so it can only shave a fraction off, and/or **fade the penalty out as
        prominence rises**. Whichever you choose, the photo shoot must show a centred close-up still
        scoring well — if a good close-up scores 2★ because it was centred, the weighting is wrong.
  - [x] **"Cut off" term:** the frame edge clipping the subject should cost composition. The
        information is already there but currently discarded — `TryGetScreenRect`
        (`ShotGrader.cs:311-392`) computes an unclamped min/max and then **clamps to the viewport**,
        keeping only the clamped rect. Return the visible fraction (clamped area ÷ unclamped area) as
        well, and let composition fall off as it drops. Change the method's outputs, not its
        near-plane clipping logic — that logic is load-bearing and was proven by measurement in 1.9
        (a straddling subject reads a sane 100%; a box behind the lens is rejected).
  - [x] Keep it allocation-free and branch-cheap: this runs once per capture, but it sits in the same
        file as a documented 0.0072 ms budget and there is no reason to spend it.

- [x] **Task 5 — Timing scoring in `ShotGrader` (AC2)**
  - [x] Read the offset decided in Task 1 from `ISubject`, take `Mathf.Abs`, and map through the
        `timingFullSeconds` / `timingZeroSeconds` window: 1 at or inside full, 0 at or beyond zero,
        smooth in between. `Mathf.InverseLerp` + `Mathf.SmoothStep` gets you there in two lines.
  - [x] **Guard the degenerate configs** rather than trusting `TryGetConfigProblem` to have run:
        `zero <= full` must not divide by zero, and a NaN offset (a subject in a weird state) must
        not produce a NaN score that then propagates into `ShotGrade` and the HUD. `ShotGrade`'s
        constructor already NaN-guards `Percent01` (`ShotGrade.cs:28`) — mirror that discipline for
        the sub-scores you add.
  - [x] There is **no capture timestamp anywhere in the codebase** and you do not need one: grading
        is synchronous inside `Capture()`, so "now" *is* the moment of the shutter. Do not introduce
        one, and do not average across frames — 1.9's Dev Notes are explicit that the skinned bounds
        wobble frame to frame and must be graded at the captured instant.

- [x] **Task 6 — Blend it, widen `ShotGrade`, and keep the miss path honest (AC3, AC5)**
  - [x] **Blend:** the architecture sketch is `FromPercent(composition * timing)`
        (game-architecture.md:544). Multiplicative is defensible — this game's first pillar is
        *being there at the right second*, so a beautifully framed shot two seconds late genuinely is
        not the shot. It also means **5★ requires both axes near 1**, so authoring the curves so they
        actually reach 1.0 in the sweet spot is not optional (AC3). If you use weights instead, put
        them in the config and justify the choice.
  - [x] **Widen `ShotGrade`** with the per-axis breakdown (subject/prominence, composition, timing)
        as additional `readonly` fields, and keep `FromPercent` / `Miss` / `Placeholder` working —
        `ShotCapturedChannel` carries this struct and Story 1.5's wiring must not break. **Not**
        behind `#if UNITY_EDITOR`: AC3 requires it in a release build.
  - [x] **A miss must stay distinguishable from a bad hit.** `Stars` is
        `Clamp(CeilToInt(Percent01 * 5), 1, 5)` (`ShotGrade.cs:24`), so a 0% miss reads as **1★** —
        the same as an ordinary weak shot. The GDD's scale is 1–5 stars, so do not invent a 0★, but
        make the miss readable some other way (an `IsMiss`/`Counted` flag, or carrying the
        `GradeMiss` reason into the struct). Story 1.12's HUD will need this and the deferred list
        already flags it.
  - [x] **Update the log line** (`PhotoModeController.cs:347`) so it prints the breakdown, not just
        the total. A bare "62%" tells a designer nothing about *which axis* cost them the shot.
  - [x] **Update the debug overlay** (`PhotoModeController.cs:379-414`) to show the three sub-scores.
        Keep its snapshot discipline — the short hold and the "▣ snapshot at capture" label exist
        because a lingering readout produced a confident, wrong bug diagnosis from a screenshot taken
        two seconds after the shot.
  - [x] **Do not touch** the gate order, `GradeDetail`'s existing fields, the `Unevaluated` zero
        values, or `GradeBestSubject`'s "first graded subject wins ties" logic. Every one of those is
        a fix from the 1.9 review with a reproduction behind it.

- [x] **Task 7 — Verify by photographing it (AC1, AC2, AC3, AC4, AC5)**
  - [x] **Extend the existing rig — do not write a second one.** `PhotoShootRig.cs` (editor, builds
        the private world) + `PhotoShootRunner.cs` (runtime assembly behind `#if UNITY_EDITOR`, walks
        the poses and pulls the real shutter) already do the hard parts: isolated scene, baked
        NavMesh, real `EventManager`, real pooled animated actor, real `PhotoModeController.Capture()`,
        render-to-texture, and a scene restore on the way out. Read both before adding to them.
  - [x] **The rig has no notion of WHEN it shoots — that is the new axis and you must add it.**
        Every pose today fires the shutter as soon as the camera settles, at an arbitrary point in
        the lifecycle. Add a *timing* pose kind that polls the live actor's peak offset each frame
        and pulls the shutter when it crosses a target: **−2 s, −0.5 s, 0 (mid-peak), +0.5 s, +2 s**
        at minimum. Re-read the actor from `manager.ActiveActors` on every poll — never hold it
        across the wait (the rig already learned this the hard way; see `PhotoShootRunner.cs:244-256`).
  - [x] **Framing poses to add:** subject on a thirds line vs dead-centre at the same distance
        (isolates placement); subject half out of the frame (cut off); subject at the new gate
        boundary; a centred close-up (must still score well — the anti-regression for Task 4's
        weighting).
  - [x] **Draw the invisible things onto the image.** The rig already draws the grader's box; add the
        **thirds lines** and print the **peak offset, the three sub-scores and the star count** into
        `shots.txt` beside each photograph. That is what turns "does 62% look right?" into "is he on
        the line, and was that the money shot?" — the same move that made 1.9's box overlay decisive.
  - [x] **Assert nothing.** The rig has no expected values by design and must keep none: an assertion
        bakes in what you already believed and goes green while the feature is wrong. Produce the
        photographs, then judge them.
  - [x] **Suspect the rig before the code.** Every pose scoring identically, every pose passing, a
        suspiciously round number, a blank capture — treat all of those as a broken rig until proven
        otherwise. 1.9 lost a session to a T-pose bench that doubled every number and produced a
        confident, wrong design conclusion.
  - [x] **Put the project back.** The rig restores the scene via `SessionState` and exits play mode
        from a `finally`; anything you add must keep that true. Output goes to `Temp/` (git-ignored),
        never `Assets/`.
  - [x] `read_console` after every script change → zero errors, zero new warnings vs the two-warning
        baseline. ⚠️ `read_console` does **not** surface `Debug.Log`, so `GameLog.Info` grade lines
        are invisible through MCP — read `shots.txt`, the overlay, or grep
        `%LOCALAPPDATA%\Unity\Editor\Editor.log`. [[mcp-playmode-verification-gotchas]]
  - [x] ⚠️ **No asset operations while in Play mode** — a mid-play import wedged the lifecycle for a
        whole session in 1.8 and convincingly mimicked a real bug.
  - [x] **Then stop and hand the photographs to Alexv.** Whether a 4★ shot *reads* as a better
        photograph than a 2★ one is a perceptual judgement — it needs eyes, and the perceptual check
        has now found what every structural check missed twice running. Name what you need him to
        look at, keep the story open until he has, and state plainly which ACs are unproven without it.

## Dev Notes

### What this story IS — and is NOT

**IS:** the *score*. Everything after the gate says "yes, this counts": how prominently and where the
subject sits in the frame, how close to the money shot the shutter fell, and the blend of the two into
a grade a player can read. Plus the retune the gate has been waiting for.

**IS NOT:** the gallery (1.11), the feedback HUD (1.12), image capture of any kind, multi-subject
arbitration beyond "best of the live ones", focus/exposure/angle scoring (the GDD defers all three
explicitly), or a rewrite of the 1.9 gates. `ShotGrade` and `ShotGrader` already exist — **grow them,
do not create a parallel result type or a second grader.**

### ⚠️ The peak is an interval, not an instant — the trap this story turns on

`TimeToPeak` reads 0 at the **start** of a 1.5 s peak window and −1.5 at its end. The GDD's ±0.5/±2
numbers assume a point. Score `Mathf.Abs(TimeToPeak)` without thinking and the *last frame of the money
shot* — the drunk mid-stagger, the exact photograph the whole event exists to produce — scores 0.33
while a shot 1.5 s before he even starts staggering scores the same. Task 1 exists to settle this before
any curve is authored.

This is the same family as the three Unity-semantics traps this project has already paid for:
`CrossFade`'s duration is normalized (1.7), `maxDistance` is not where the sound ends (1.8),
`WorldToScreenPoint` does not fail behind the camera (1.9). **The name of the thing is not its
definition — check the semantics.** That is now four for four across four stories.

### The number the GDD gives you is not the number the code measures

The GDD says the sweet spot is the subject filling **25–50% of the frame**. What the code computes is
the **area of a world-axis-aligned box** around a tall, thin figure with his arms down. That box is
mostly air, so:

- the *animated* drunk measures **4.14%** at a natural portrait distance (~20 units) — plainly the
  subject of the photograph, currently rejected outright;
- **16–21%** for a close shot the last session judged good by eye;
- the reading **breathes with the walk cycle** — 7.34% and 9.02% on two runs of the same pose, which
  decided pass/fail by animation frame alone;
- it over-reports a figure standing diagonally and under-reports one standing straight on.

Authoring a "25–50%" sweet spot against that measure would make 5★ mean "nose to nose" and every real
photograph a 1★. **This is why Task 2 comes before any scoring code, and why it is settled by looking at
photographs rather than by arithmetic.** Both `deferred-work.md` entries from 1.9 point at the same
thing from different directions.

### Fail-soft precedent to follow — copy it, don't invent one

`PhotoModeController.Awake` (`:118-207`) resolves five independent readiness flags, each with a log
level chosen to match what absence *means*: `Error` where a reference is genuinely missing, `Info`
where absence is a supported state. `GradingConfig.TryGetConfigProblem` (`:81-153`) then warns once
about values that let grading *run* while producing meaningless verdicts.

That second category is the one this story adds to, and it is the harder one to notice: a composition
curve that can never reach 1.0 doesn't crash, doesn't warn, and doesn't look wrong — it just quietly
caps every photograph the player will ever take at 3★. **Fail-soft must not mean invisible.** Warn once
at Awake for any configuration that cannot possibly award a good grade.

### Testing standards

There is still no automated test suite (Unity Test Framework 1.6.0 is installed but unused; the tests
assembly was deferred in Story 1.1 Task 6). Verification is the photo-shoot rig plus `read_console`.

`ShotGrader` remains a `static` class of pure maths — genuinely unit-testable, and the architecture
anticipates it (`Tests/EditMode/ ShotGrader math`, line 386). Standing up that assembly is **not** in
this story's ACs and must not be smuggled in. But the composition maths (trapezoid evaluation, thirds
distance, clipped-fraction) is the most test-shaped code this project has produced, and if Task 7's
rig work starts feeling slow, that is the signal the tests assembly has become worth its own story —
say so in the Dev Agent Record rather than doing it silently.

⚠️ And remember what the rig is *for*: a passing number proves the code agrees with your assumptions.
The photograph proves what the player sees. When the two disagree, the photograph wins.

## Project Structure Notes

Per the architecture's source tree (lines 362-369) — no new folders, no new assets:

```
Assets/Scripts/Grading/     # ShotGrader.cs (MODIFY), GradingConfig.cs (MODIFY), ShotGrade.cs (MODIFY)
Assets/Data/Grading/        # GradingConfig.asset (MODIFY — new keys, retuned gate)
```

Code touched:
- **Modified:** `Assets/Scripts/Grading/ShotGrader.cs` (score body + composition/timing helpers),
  `Assets/Scripts/Grading/GradingConfig.cs` (new tunables + validation),
  `Assets/Scripts/Grading/ShotGrade.cs` (per-axis breakdown, miss legibility),
  `Assets/Scripts/Events/ISubject.cs` + `Assets/Scripts/Events/EventActor.cs` (Task 1's peak offset,
  if you take the recommended route),
  `Assets/Scripts/PhotoMode/PhotoModeController.cs` (log line + overlay breakdown),
  `Assets/Data/Grading/GradingConfig.asset` (new keys + AC4 retune),
  `Assets/Scripts/PhotoMode/PhotoShootRunner.cs` + `Assets/Scripts/Editor/PhotoShootRig.cs` (timing
  and framing poses, thirds overlay)
- **New:** nothing expected. If you find yourself creating a file, check you are not building a second
  grader or a second rig.

Namespace `CameraGame.Grading`, one asmdef, `GameLog` with the `"Grading"` category.

## Project Context Rules

No `project-context.md` exists in this project — the persistent-facts glob returned nothing. Governing
conventions come from `CLAUDE.md` and `game-architecture.md`:

- **`GameLog`** for all logging, never bare `Debug.Log`. `"Grading"` category. ERROR = can't function,
  WARN = unexpected but handled, INFO = milestones.
- **Fail-soft, never throw** — the game has no fail state (NFR8). Errors are never shown to the player.
- **Data-driven via ScriptableObjects**; no tunable numbers as code literals. Constants that are
  genuinely implementation details (like `ShotGrader.SampleInset`) stay in code — the existing file
  documents that distinction at the field.
- **Cache in `Awake`, never `GetComponent`/`Camera.main` in `Update`.**
- **Debug/gizmo code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`** — *except* the per-axis
  breakdown on `ShotGrade`, which AC3 explicitly requires in a release build.
- **URP only** — not relevant here; do not add materials.
- **Check `read_console` after every script change.** A clean console is a project rule.
- **Verify by running, not by reading** — build the world, drive the real path, capture the result,
  and look at it. Restore the project when the rig finishes. (CLAUDE.md §Verifying Your Own Work.)
- **Jira sync is mandatory** — this story is **KAN-21** (project KAN, cloud
  `5b116b91-787f-4ff7-9668-2cd92d337bcf`; transitions 11=To Do, 21=In Progress, 31=Review, 41=Done).
  The Tasks above are mirrored as Jira Subtasks under KAN-21; keep their status in step with the
  checkboxes here.
- **Git:** `_bmad-output/` is gitignored — story files are local only. Commit *and* push in the same
  session. [[always-push-changes-to-git]]

## Previous Story Intelligence

From Story 1.9 (closed `done` 2026-07-26 after a 21-patch code review, every patch re-verified by
running):

1. **The bug that mattered was found by a photograph, not by a check.** A shot taken facing *away*
   from a subject you are standing inside scored **100% / 5★** — an AABB enclosing the camera is
   outside no frustum plane. Reading the code did not find it; photographing the empty picture did.
   **Lesson for 1.10: the composition score is even more invisible than the gate was. Draw it.**
2. **Two verification rigs produced confident, wrong conclusions before one worked.** An edit-mode
   bench measured a **T-pose** (Animators do not run in edit mode), roughly doubling every coverage
   number and yielding a design conclusion that real play disproved. Play mode over edit mode,
   always — and suspect the rig first.
3. **Silent numeric misconfiguration is this project's recurring failure mode.** `cueRadius = 0`
   (1.8) produced total silence with a clean console; `minCoverage = NaN` and `minVisibleSamples = 0`
   (1.9) each disabled a gate invisibly. **Your instances this story: a sweet spot that never reaches
   1.0, and `timingZeroSeconds <= timingFullSeconds`.** Validate and warn once.
4. **Two sources of truth drift.** 1.9 *replaced* `_activeCount` rather than keeping it alongside
   `ActiveActors`, and got idempotent removal for free. If prominence changes measure (Task 2), do
   not leave the old measure gating while the new one scores.
5. **The blind-review layer generates hypotheses, not conclusions.** Three of four High findings in
   1.8 and one in 1.9 were disproved by running the scenario. Reproduce before reporting.

From Story 1.6 (the `ISubject` liveness contract) and 1.7 (the pooled-reuse bug): **pooled objects do
not go null.** A stale `ISubject` becomes a different drunk, silently. Read live at capture.

## Git Intelligence

- `45a7234` **Story 1.9 review: fix a 5-star photograph of nothing, and 20 more** — the current
  `ShotGrader` / `GradingConfig` / `PhotoModeController` shape, the `Safe*` + `TryGetConfigProblem`
  validation idiom to copy, and the near-plane clipping you must not disturb. **Read this diff before
  editing any grading file.**
- `409bf76` **Story 1.9: photograph the real capture path** — the photo-shoot rig you are extending,
  and the three Unity traps it hit (editor-assembly MonoBehaviour, `CaptureScreenshot` on an
  unattended Game View, `fieldOfView` overwritten every frame by the zoom easing).
- `6f65fa5` / `e9774d2` — the edit-mode bench that measured a T-pose (deleted), and the rig
  clean-up/restore discipline that replaced it.

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.10: Composition and timing scoring] — AC source (lines 460–478)
- [Source: _bmad-output/planning-artifacts/epics.md#FR6, #FR7, #FR4] — composition, timing, blended grade
- [Source: gdds/gdd-My project-2026-05-26/gdd.md:186-201] — the grading model and the initial tuning targets (1–5 stars, ≥8% gate, 25–50% sweet spot, ±0.5/±2 s timing)
- [Source: gdds/gdd-My project-2026-05-26/gdd.md:68-74] — Pillars 1 and 2: timing and framing are *the* skill; why timing may dominate the blend
- [Source: game-architecture.md#Frame-Read Grading:514-546] — the reference `ShotGrader` sketch (note its `Screen.width` and point-peak assumptions are both wrong for this codebase)
- [Source: game-architecture.md#Photo Capture & Grading:186-192] — coverage sweet spot, thirds placement, timing from the lifecycle
- [Source: game-architecture.md#Architectural Boundaries:425-434] — grading reads subjects ONLY through `ISubject`
- [Source: game-architecture.md#Configuration:298-308] — tunables in ScriptableObjects, invariants in static classes
- [Source: Assets/Scripts/Grading/ShotGrader.cs:267-273] — the exact seam Story 1.9 left for this story
- [Source: Assets/Scripts/Grading/ShotGrader.cs:311-392] — `TryGetScreenRect`: near-plane clipping (do not disturb) and the viewport clamp that currently discards the cut-off information
- [Source: Assets/Scripts/Grading/GradingConfig.cs:56-153] — the `Safe*` accessor and `TryGetConfigProblem` patterns to extend
- [Source: Assets/Scripts/Grading/ShotGrade.cs:15-43] — the struct to widen; `Stars` at line 24 is the degenerate mapping
- [Source: Assets/Scripts/Events/ISubject.cs:15-29] — the seam; `IsAtPeak` / `TimeToPeak` and their liveness contract
- [Source: Assets/Scripts/Events/EventActor.cs:85-89,363-364,383-384] — `IsAtPeak`, `TimeToPeak` seeding and continuous decrement
- [Source: Assets/Data/Events/TownDrunk.asset] — Spawn 3 s · Build 10 s · **Peak 1.5 s** · WindDown 6 s · Despawn 4 s
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:304-350,379-414,426-467] — the capture path, the debug overlay, and `GradeBestSubject`
- [Source: Assets/Scripts/PhotoMode/PhotoShootRunner.cs] — the rig to extend; poses, real shutter, render-to-texture, box drawing
- [Source: Assets/Scripts/Editor/PhotoShootRig.cs] — the isolated world build and the scene restore contract
- [Source: _bmad-output/implementation-artifacts/1-9-subject-capture-gate-grading.md#Review Findings] — the 21 patches and 5 deferred items this story inherits
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the two 1.9 items this story closes (the 8% gate; AABB vs the subject)
- [Source: CLAUDE.md#Verifying Your Own Work] — build the rig, run the real path, look at the output, restore the project

## Dev Agent Record

### Agent Model Used

claude-opus-5[1m] (Claude Opus 5, 1M context) — `gds-dev-story`, 2026-07-26.

### Debug Log References

- `Temp/PhotoShoot/shots.txt` + 29 PNGs — the run this story's conclusions rest on. Every photograph
  carries the grader's own box, the rule-of-thirds grid, and the numbers behind the verdict.
- Console after every script change: **zero errors, zero new warnings.** Baseline is the Unity Version
  Control project-link notice plus the two pre-existing large-triangle mesh warnings the real town scene
  logs when it reloads. No `ThirdPersonCamera` head-bone warning appeared (that scene path was not
  exercised by the rig's private world).

### Completion Notes List

#### Task 1 — "distance from the peak" is measured from the peak WINDOW (the recommended route)

Added **one** member to `ISubject`: `float PeakOffset` — positive before the peak window, exactly `0`
anywhere inside it, negative after. Implemented in `EventActor` as a derived property (three lines, no new
state) that branches on the **phase** rather than on the sign of `TimeToPeak`, so an actor that has not been
`Begin()`-run yet reports "before the peak" instead of "1.5 s after it". `TimeToPeak` is untouched.

**Proven by photograph, not by argument.** The rig prints both numbers for every shot, in the frame the
shutter fired. `y_time_peak_late` — deep inside the money shot:

```
timing: PeakOffset 0.00s   TimeToPeak -1.20s   IsAtPeak True   → timing 100%, 4★
```

The architecture's sketched `Mathf.Abs(TimeToPeak)` would have read 1.20 s on that same frame → ~55%
timing → **2★ for the exact photograph the whole event exists to produce**, worse than a shot half a
second early. The two numbers sitting one line apart in `shots.txt` is the evidence.

#### Task 2 — prominence is measured as the box's HEIGHT fraction (Option B)

Settled by looking at the pictures, as the story required. The decisive pair, both plainly "he is the
subject" photographs:

| shot | area coverage (1.9's measure) | **height fraction** |
|---|---|---|
| `b_mid` — natural full-body portrait | **4.5 %** (rejected by the 8 % gate) | **39.1 %** |
| `a_close` — tight full-body | 17.6 % | 81.9 % |
| `e_far_zoomed` — best-proportioned in the set | 9.6 % | 55.2 % |

Area is unusable: it spans 4.5 %→17.6 % across shots a photographer would call the same kind of picture,
and it breathes with the walk cycle. Height spans 39 %→82 % monotonically with distance and is steady
across the animation. `minCoverage` was therefore **renamed** `minSubjectHeight` (a field named
`minCoverage` holding a height fraction is a trap for the next reader), and the gate and the curve now run
on the *same* measure. Area coverage is still reported in `GradeDetail`/`shots.txt` — two sessions of
evidence are expressed in it and it remains the honest answer to "how much of the picture is him".

#### Task 3 — plain numbers, not `AnimationCurve`

Took the story's recommended shape (a trapezoid + two weights + two timing bounds) over the architecture
sketch's `AnimationCurve`. Reasons recorded in the class doc-comment: this project hand-authors SO assets
as YAML and a curve serialises as an un-eyeballable keyframe list; a curve cannot be *validated*; four
named numbers are more tunable by hand than a curve editor.

Every new tunable has a `[Tooltip]`, a `[Range]`/`[Min]`, and a `Safe*` accessor. Two **resolvers**
(`ResolveProminenceCurve`, `ResolveTimingWindow`) sit above the accessors because a per-field accessor
cannot see its neighbour — they guarantee `falloffBelow < idealMin <= idealMax < falloffAbove` and
`zero > full` however the asset is authored, so the grader can never divide by a zero width.
`TryGetConfigProblem` grew ten non-finite checks plus the four structural ones the story named, including
the two silent "5★ is unreachable" shapes (gate above the sweet spot; falloff-below at or above the gate).

#### Task 4/5/6 — composition, timing, blend

`composition = prominence × placement × framing`, `grade = composition × timing`, per the architecture.
Placement is deliberately gentle (`thirdsWeight 0.35`, faded out entirely as the subject grows to fill the
frame) and the cut-off term is what actually punishes a subject spilling past the edge. `ShotGrade` now
carries `Subject01`/`Composition01`/`Timing01` and `MissReason` as **plain fields, not behind
`#if UNITY_EDITOR`** (AC3), with `IsMiss`/`Counted` so a 0 % miss stays distinguishable from a weak 1★.

#### Task 4's cut-off term closed a 1.9 bug that the gate had only half-fixed

`k_straddling` (nose-to-nose, box fills the frame because most of him is *outside* it) scored **100 % /
5★** in Story 1.9. It now reads `framed 1 %` → composition **16 %** → 1★.

#### AC4 — the gate retune, both halves

- `minSubjectHeight = 0.20` (frame-height fraction). Bracketed by photograph, not arithmetic:
  `s_size_4h` at **22.9 %** counts, `t_size_5h` at **18.5 %** is rejected `TooSmall`. The natural portrait
  distance (`b_mid`, 39.1 %) now counts, closing the deferred 1.9 item.
- **The occlusion half is no longer inert.** `u_wall_mid` — wall dropped in at 2.5 heights — passes the
  size gate at 40.9 % height and is then rejected `Occluded` (line-of-sight 0 %). Under the old *area*
  gate that same shot measured 5.0 % < 8 % and was rejected `TooSmall` before a single linecast ran.

#### AC3 — 5★ is reachable by a good shot and not by a mediocre one

The full ladder from the final shoot, and note that the top and the bottom of it are the **same
photograph** differing only in when the shutter fell:

| shot | composition | timing | grade | |
|---|---|---|---|---|
| `z3_money_shot` — 2 heights, on the thirds line, inside the peak | 98 % | 100 % | **98 % · 5★** | the money shot |
| `z4_money_late_12s` — identical framing, 1.2 s late | 98 % | 55 % | 54 % · 3★ | timing alone costs two stars |
| `x_time_peak_start` — centred, 2.5 heights, on the peak | 70 % | 100 % | 70 % · 4★ | good, not great |
| `q_size_3h` — centred, too far back | 46 % | 0 % | 0 % · 1★ | |
| `k_straddling` — nose-to-nose, 99 % of him outside the frame | 16 % | 0 % | 0 % · 1★ | 1.9's 5★ bug |

> ⚠️ **CORRECTED 2026-07-28 by the code review — the table above is STALE and does not match the shipped
> evidence.** It was written before the correct-course re-shoot and never updated, while the prose below it
> was. Against `_bmad-output/verification/photo-shoot/shots.txt` as it actually stands:
>
> | shot | composition | timing | grade | |
> |---|---|---|---|---|
> | `z3_money_shot` — 2 heights, dead centre, inside the peak | 100 % | 100 % | **100 % · 5★** | the money shot |
> | `x_time_peak_start` — centred, 2.5 heights, on the peak | 85 % | 100 % | **85 % · 5★** | height 40.5 %, *below* the 0.45 sweet-spot floor |
> | `z4_money_late_12s` — identical framing, 1.2 s late | 100 % | 53 % | 53 % · 3★ | timing alone costs two stars |
> | `y_time_peak_late` — deep inside the peak | 76 % | 100 % | 76 % · 4★ | |
> | `z2_time_late_2s` — 2 s after the window | 85 % | 0 % | 0 % · 1★ | |
>
> **There are two 5★ shots, not one**, and the second is the one this record labels "good, not great".
> `Stars = CeilToInt(Percent01 × 5)` makes 5★ anything at or above 80 %, so a shot below the designed
> prominence sweet spot reaches top marks on timing alone. **The claim "nothing mediocre came close" is
> not supported by the evidence file**, and AC3's "5★ unreachable by a mediocre shot" is therefore
> **unproven** pending Alexv's perceptual comparison of `x_time_peak_start.png` against `z3_money_shot.png`.

`z3` required all three axes at once: in the sweet spot for size, dead centre, and inside the peak window.

**And `z3` is Task 1's decision paying for itself in the most visible way possible.** Its readout is
`PeakOffset 0.00s · TimeToPeak -1.20s · IsAtPeak True` — under the architecture's sketched
`Mathf.Abs(TimeToPeak)` that same frame would have scored 55 % timing → 54 % → **3★**, i.e. the game's
best-possible photograph would have been graded identically to the one taken 1.2 s after the moment had
passed.

#### Two findings the photographs produced, and what changed because of them

1. **The first sweet spot (0.35–0.85) was too wide to be worth anything.** Every framing from 39 % to
   82 % scored an identical 83 % composition — prominence did no work at all, and the star rating could
   not tell a cramped shot from a well-proportioned one. Retuned to **0.45–0.78** against the pictures
   (`a_close` at 0.82 is cramped; `e_far_zoomed` 0.55 and `o_centred_closeup` 0.63 are the best-looking).
   The size ladder now reads monotonically: 22 % → 34 % → 46 % → 68 % → 83 % composition.
2. **The rig's captions encoded expectations.** `q_gate_inside — "Just INSIDE the retuned size gate"` came
   back *rejected* — a perfectly well-formed log line contradicting itself. An expectation written before
   the shot is an assertion wearing a caption's clothes. All captions now describe what the **camera** did,
   never what the grade should be.

#### Rig trap paid for this session — add it to the list

`refresh_unity` returned `success: true` with `"Refresh recovered after Unity disconnect/retry; editor is
ready"` **without having recompiled**. The shoot then ran a 7-minute-old assembly and silently produced a
complete, plausible `shots.txt` that was simply missing the two poses I had just added. Nothing errored.
**Check `Library/ScriptAssemblies/CameraGame.dll`'s mtime against the source's before trusting a run** —
the absence of a pose is much harder to notice than a wrong number. `Assets/Refresh` via
`execute_menu_item` forced the compile that `refresh_unity` would not.

#### Perceptual check — Alexv's verdict, 2026-07-26

Everything else in this record is *structural*: the numbers do what the ACs describe and the boxes land on
the subject. Three questions needed human eyes. His answers:

1. **`z3_money_shot` (5★) vs `z4_money_late_12s` (3★)** — same framing, 1.2 s apart. **Confirmed:** the
   5★ frame reads as the better photograph. ⇒ **AC2 and the timing half of AC3 are verified.**
2. **`m_thirds` (98 %) vs `n_centred` (68 %)** — "**better to be centered in the picture**".
   ⇒ **AC1's rule-of-thirds half is DISPROVEN as tuned.** The grader currently pays 30 percentage points
   of composition for a placement he likes *less*. The sign of the placement term is wrong for this game,
   not merely its weight.
3. **`o_centred_closeup` (83 %) vs `n_centred` (68 %)** — the closer, centred shot looks better.
   ⇒ **The prominence curve is verified, including its direction:** the shot the code ranks higher is the
   one he prefers, and both of his answers point the same way — he wants the subject **large and centred**,
   a portrait aesthetic rather than a photojournalistic one.

**⚠️ Known confound before anyone acts on (2).** The rig's world is a featureless grey plane, so
`m_thirds` puts the subject in the corner of an otherwise *empty* frame. The rule of thirds earns its keep
when the negative space holds something — the street he is staggering into, a building, context. That
judgement may not survive contact with the real town. This is exactly the "suspect the rig" rule applied
to a perceptual result: the photograph is honest, but the *world* it was taken in is not representative.

**This contradicts GDD FR6** ("higher when the subject … sits near a rule-of-thirds line"), so it is a
correct-course decision, not a tuning tweak. Nothing has been changed in the code or the asset on the
strength of it yet.

#### The placement study — re-shot in the real town to remove that confound

Alexv chose to re-test before changing FR6. New editor entry point
**`Tools > Grading > Photo Shoot — Placement Study (Town)`** (output `Temp/PhotoShootTown/`): it opens the
real `SampleScene`, drives the scene's **own** `PhotoModeController`, `EventManager` and route rather than
rebuilt stand-ins, disables the walk camera/controller so the rig owns the transform, and never saves —
the play-mode-exit hook reopens the scene from disk, discarding every mutation.

The runner gained a **matched-pair** shutter: two captures from one frozen camera position on
**consecutive frames**, dead centre then on a thirds intersection. Consecutive frames is the whole point —
the drunk walks several units between ordinary poses, so a pair assembled from two normal shots compares
two different photographs and is worthless.

**The first attempt (four compass points) was itself a broken rig, and it says so in the pictures:** two of
the four photographed a pine tree with the drunk invisible behind it. Widened to a **12-direction sweep**
so that enough vantage points come back usable, then judged by eye. Two clean pairs resulted —
`dir090_*` (town buildings and road behind him) and `dir270_*` (mountains, road, a car). Both read
composition **83 % centred vs 98 % thirds**, identical to the private world, so the *scoring* is
scene-independent; only the judgement was ever in question.

#### Alexv's verdict on the town pairs, and the correct-course that followed (2026-07-27)

**"The centred one for both."** The confound was removed and the judgement did not change. AC1's
rule-of-thirds clause is therefore **wrong for this game**, not merely mis-weighted, and the code was
built to a specification that the photographs disprove.

**Changed, in this order — design first, then code:**
1. **GDD grading section** and **epics.md FR6** rewritten: composition now peaks when the subject is
   prominent and sits near the **centre**, falling off when too small, cut off, or pushed toward a corner.
   Both carry a dated note explaining what changed and why, so the next reader does not "fix" it back.
   (The same edit also retired "~25–50% of the frame", which was never a quantity this code measured.)
2. **`thirdsWeight` → `centreWeight`**, and the placement term reversed: it now measures distance from the
   frame centre, normalized by the centre-to-corner distance. The `centringRelief` fade was **deleted** —
   it existed solely to stop a frame-filling subject being punished for being centred, and centred is now
   the thing being rewarded.
3. **Both composition guides re-drawn.** The debug overlay and the rig's saved images drew a
   rule-of-thirds grid; they now mark the frame centre. A guide showing lines that nothing scores against
   is worse than no guide — it is an invitation to diagnose a "wrong" score against the wrong reference.
4. **`z3_money_shot` re-aimed to dead centre.** It was on a thirds intersection because that *was* best
   placement under the old rule; a pose encoding "the best possible shot" has to move with the definition
   or it silently stops being the money shot.

**Effect on the grades** (same photographs, re-shot): `z3_money_shot` — well-sized, centred, inside the
peak — now scores composition **100 %** and **100 % / 5★** overall; the matched placement pair reads
**centred 100 % vs off-centre 87 %**. The ordering is reversed and the star outcomes for a good,
well-timed shot are unchanged, so this makes the grader agree with the player's eye without making five
stars easier to reach.

#### ⚠️ Placement and prominence are NOT independent — the rig had to be fixed twice to see it

The pose pair that isolates placement took three attempts, and both failures produced confident,
well-formed, wrong evidence:

1. **Two ordinary poses 1.8 s apart** (`m_thirds` / `n_centred`). The drunk walks in 1.8 s, so the two
   shots differed in HEIGHT as well as placement — 43.4 % vs 39.2 % — and prominence swamped the term the
   pair existed to measure. It reported the *off-centre* shot as the better composition immediately after
   the rule had been reversed to say the opposite.
2. **A matched pair at 2.5 heights** (consecutive frames, frozen camera). Still wrong, and for a reason
   that is not a defect at all: **a rectilinear projection stretches toward the frame edges, so re-aiming
   the same subject from centre to a thirds point genuinely makes him project larger** — 40.4 % became
   45.8 % between two consecutive frames. At 2.5 heights that straddles the sweet spot's floor (0.45), so
   the off-centre half cleared it and the centred half did not.
3. **A matched pair at 2 heights.** Both halves land inside the sweet spot, prominence is 1.0 for each,
   and placement is left as the only term that can differ: **100 % vs 87 %**.

The coupling is real and correct — an off-centre subject *is* bigger on screen — but it means the two
axes cannot be isolated by re-aiming alone, and any future comparison has to keep both halves inside the
prominence plateau. Worth remembering: "a controlled comparison" that is not actually controlled is
indistinguishable from a finding.

**⚠️ Finding logged to `deferred-work.md`, not fixed here:** all 24 town shots reported
`line-of-sight 100 %`, including ones where the subject is completely hidden behind a tree or a hill —
one scored **98 % / 5★ for a photograph of a tree trunk**. Ruled out: the world is on `Default`, which is
in `occluderMask`, and it does carry MeshColliders. Cause undiagnosed; recorded as a **reproduced symptom,
not a diagnosed defect**. It belongs to Story 1.9's occlusion gate, which Story 1.10 does not touch.

### File List

Modified:
- `Assets/Scripts/Events/ISubject.cs` — added `PeakOffset` (Task 1)
- `Assets/Scripts/Events/EventActor.cs` — `PeakOffset` implementation
- `Assets/Scripts/Grading/GradingConfig.cs` — `minCoverage` → `minSubjectHeight`; composition + timing
  tunables; `Safe*` accessors; two resolvers; extended `TryGetConfigProblem`
- `Assets/Scripts/Grading/ShotGrade.cs` — per-axis breakdown, `MissReason`, `Scored`/`Missed`
- `Assets/Scripts/Grading/ShotGrader.cs` — height-fraction gate; `Composition`/`Timing`; `GradeDetail`
  gains `HeightFraction`/`FramedFraction`/`PeakOffset`; `TryGetScreenRect` returns the framed fraction
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` — breakdown in the log line and the debug overlay,
  thirds grid, miss-reason seeding
- `Assets/Scripts/PhotoMode/PhotoShootRunner.cs` — timing poses, framing poses, size ladder, thirds
  overlay, config read-back, richer per-shot log
- `Assets/Scripts/Editor/PhotoShootRig.cs` — wires `GradingConfig` into the runner
- `Assets/Data/Grading/GradingConfig.asset` — new keys + the AC4 retune
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 1.10 → in-progress → review

New: none.

### Change Log

| Date | Change |
|------|--------|
| 2026-07-26 | Story created (`gds-create-story`). |
| 2026-07-26 | Task 1: `ISubject.PeakOffset` — timing measured from the peak WINDOW, not its start. |
| 2026-07-26 | Task 2: prominence measured as frame-HEIGHT fraction; `minCoverage` → `minSubjectHeight`. |
| 2026-07-26 | Tasks 3–6: composition + timing tunables, scoring, per-axis breakdown on `ShotGrade`. |
| 2026-07-26 | AC4: gate retuned to 0.20 height fraction; occlusion half of the 1.9 gate proven live again. |
| 2026-07-26 | Task 7: rig gains timing poses, a size ladder, thirds overlay and a config read-back. |
| 2026-07-26 | Retuned the sweet spot 0.35–0.85 → 0.45–0.78 after the shoot showed it was too flat to score. |
| 2026-07-26 | Added the money-shot pose after two shoots produced no 5★ at all — AC3 was true by arithmetic and unphotographed. |
| 2026-07-27 | Rig output moved `Temp/` → `_bmad-output/verification/`: Unity deletes `Temp/` on editor shutdown, and a set of photographs Alexv had been asked to review was gone by the time he opened the folder. `CLAUDE.md` amended. |
| 2026-07-27 | Placement study re-shot in the real town; Alexv judged **centred** better in both matched pairs. |
| 2026-07-27 | **Correct-course:** GDD + epics.md FR6 rewritten (thirds → centred, and "~25–50% of frame" retired); `thirdsWeight` → `centreWeight` with the placement term reversed; composition guides and the money-shot pose re-aimed to match. |
| 2026-07-26 | Status → review. Perceptual check (does 5★ *look* better than 2★) outstanding with Alexv. |
| 2026-07-28 | Code review (3 layers). 14 patches applied, compiled and re-shot; 6 findings disproven and dropped; 7 deferred. Correct-course completed in FR5/FR7/GDD gate clause/architecture sketch, which the 2026-07-27 pass had left behind. |
| 2026-07-30 | AC3 perceptual check CLOSED — Alexv judged `z3_money_shot` the better photograph. Root cause was the star quantizer, not the scoring: `CeilToInt(grade × 5)` hardcoded "80% is perfect". Star boundaries moved into `GradingConfig` (5★ ≥ 0.90). Re-shot: `x_time_peak_start` 85% 5★ → 85% 4★ (grade identical), `z3` now the only 5★ in 29 photographs. |
| 2026-07-30 | Status → done. All five ACs verified; residual aesthetic question on `p_cut_off`/`k_straddling` and the town occlusion symptom logged to `deferred-work.md`. |

## Review Findings

Code review 2026-07-28 (`gds-code-review`, three parallel layers: Blind Hunter, Edge Case Hunter,
Acceptance Auditor). Scope was the **shipping game code only** — `PhotoShootRunner.cs` and
`PhotoShootRig.cs` were excluded at Alexv's direction as test scaffolding that ships nothing.
Every finding below was re-verified against the source or the evidence files before being recorded;
six raised findings were dismissed as disproven and are listed at the end.

### Decisions — resolved by Alexv, 2026-07-28

- [x] [Review][Decision] **Every off-peak shot is literally 0% / 1★, indistinguishable in stars from a total miss** — The blend is `composition × timing` and `Timing()` returns a hard `0f` beyond `timingZeroSeconds` (2 s). So a beautifully composed shot 2 s off the peak scores `0%` → `1★` — the same star count as photographing an empty street. This is not hypothetical: **all 24 shots in `_bmad-output/verification/photo-shoot-town/shots.txt` read `timing 0%` → `counted — 0% 1★`**, including the ones whose composition scored 83–98%. AC3 says a miss must stay distinguishable from a bad hit; structurally it is (`MissReason`/`IsMiss`), but Story 1.12's HUD will show `1★` for both. **RESOLVED — accepted as designed.** Timing is the first pillar; a late shot genuinely is not the shot, and `MissReason`/`IsMiss` keep a miss structurally distinct from a bad hit. No code change. **Carry to Story 1.12:** the HUD must not lead with the star count alone, or 1★-late and 1★-miss will read identically to the player.
- [x] [Review][Decision] **A counted 0% shot loses the tie-break to an earlier subject's miss** [`PhotoModeController.cs:504`] — `if (!graded || g.Percent01 > best.Percent01)`. Story 1.10 created a state 1.9 could not produce: a shot that passes every gate and still scores exactly `0%`. If a rejected actor is graded first and a counted-but-late actor second, `0 > 0` is false, so the miss wins and the player is told `SHOT FAILED: TooSmall` for a photograph that counted. **Invisible today — `maxConcurrent` is 1 in both code and scene — and goes live in Epic 2 (The Living Town).** The fix is unambiguous (rank `Counted` above a miss regardless of percentage), but Task 6 explicitly forbids touching this logic, so it needs your call rather than a silent patch. **RESOLVED — patch now, deviating from Task 6 with Alexv's explicit authorisation (2026-07-28). Tracked as Patch 14 below.**
- [x] [Review][Decision] **`Subject01` is measured and reported but never weighed** [`ShotGrade.cs:88-90`] — `Scored(visible, composition, timing)` stores line-of-sight and then `Percent01 = composition × timing`. A subject 21% visible behind a fence (just clearing `minVisibleSamples` 0.2) grades identically to one in the clear. The struct documents this as deliberate ("a REPORT, not a multiplier — the subject check is a GATE"), which is coherent, but the gate is a 20% threshold rather than a binary fact. Confirm this is intended or fold visibility into the score. **RESOLVED — keep as a gate.** The occlusion threshold already answers "does this count"; scoring the partial view as well would double-punish a subject the gate accepted. No code change; the doc-comment already states this correctly.
- [x] [Review][Decision] **A shot below the designed sweet spot still reaches 5★** — ✅ **RESOLVED 2026-07-30. Alexv compared the two photographs and judged `z3_money_shot` the better picture.** Diagnosis: the scoring was never wrong — it already ranked them **100% vs 85%**. The fault was `Stars = CeilToInt(Percent01 × 5)`, which hardcodes "80% is a perfect photograph" and collapsed a fifteen-point gap into one rating. (My first proposed fix — excluding the 0.2 s animation blend from the peak window — would **not** have worked: it moves `PeakOffset` to 0.20 s, still inside the ±0.5 s full-marks band, so the grade and the rating would both have been unchanged. Caught by doing the arithmetic before implementing.) **Fixed** by moving the star boundaries into `GradingConfig` as four tunable thresholds (5★ ≥ 0.90 · 4★ ≥ 0.70 · 3★ ≥ 0.45 · 2★ ≥ 0.20), resolved into a guaranteed-descending `StarScale`, with `Safe*` accessors, non-finite/clamped/ordering validation and a "5★ unreachable" check. **Verified by re-shooting:** `x_time_peak_start` went `85% 5★ → 85% 4★` — grade *identical*, only the rating moved, which is exactly what isolates it to the quantizer — and `z3_money_shot` is now the **only 5★ in twenty-nine photographs**. All eighteen other star ratings held, including four whose grades drifted with the walk cycle without crossing a boundary. Original held text follows.
  ⏸ *(was: HELD pending the perceptual check below.)* Alexv will decide after comparing the photographs; if `x_time_peak_start` genuinely reads as 5★-worthy then nothing is wrong and only the tuning notes overstate the case. — `Stars = Ceil(Percent01 × 5)` makes 5★ anything at or above 80%. `x_time_peak_start` (height **40.5%**, below the `prominenceIdealMin` floor of 0.45) scores composition 85% × timing 100% → **85% / 5★** in the shipped evidence. `b_mid` at 39.1% would likewise reach 5★ on a well-timed frame. Either the sweet-spot floor or the star mapping is doing less work than the tuning notes assume. Decide which you want to move.
- [ ] [Review][Decision] **The perceptual check is outstanding for the tuning that actually shipped** — Your recorded verdicts are dated 2026-07-26 and 2026-07-27 and were given on the pre- and mid-correct-course shots (`m_thirds`/`n_centred`; `z3` at 98%). The evidence set in `_bmad-output/verification/photo-shoot/` is dated **2026-07-28** with different poses and different grades (`z3` now 100%, `x_time_peak_start` now 5★). **Nobody has judged the final ladder as photographs.** The story's own Change Log still says this is outstanding. See "What I need from you" below.

### Patches — all applied and compiled 2026-07-28

- [x] [Review][Patch] **The correct-course was incomplete — three design clauses still describe the pre-1.10 design** [`epics.md:131`, `gdd.md` gate bullet, `game-architecture.md:534-539,96,189-190,613`] — FR6 and the GDD composition bullet got careful dated revision notes; the *gate* clause did not. `epics.md` FR5 and the GDD both still say the subject must occupy "**≥ ~8% of the frame**" (an area measure) while the shipped gate is `minSubjectHeight: 0.2` on a **height** fraction. `game-architecture.md`'s grading sketch is stale in five places (`MinCoverage`, `CoverageCurve`, `ThirdsScore`, `ShotGrade.Miss`, `Mathf.Abs(TimeToPeak)`) and the story's own References send the next implementer straight to it. FR7 likewise still reads "±0.5 s of the peak", written for a point peak. **This is the highest-value fix here: a reader reconciling code to FR5 will conclude the gate is 2.5× too strict and revert it.**
- [x] [Review][Patch] **`HeightFraction` and `FramedFraction` report fabricated measurements on every unmeasured path** [`ShotGrader.cs:95-96,415`] — `Fail()` uses the 4-arg constructor, so `heightFraction` defaults to `0f` and `framedFraction` to `1f`. Confirmed in the shipped run: `g_looking_away — height 0.0% framed 100%` for a photograph facing the wrong way. `VisibleFraction` and `PeakOffset` both got explicit `NotEvaluated`/`NaN` sentinels for exactly this reason, documented twice in the same file; these two fields did not.
- [x] [Review][Patch] **The debug overlay prints the axes and the size line unconditionally, including on misses** [`PhotoModeController.cs:425-430`] — Only the centre guide is gated on `hit`. A rejected shot therefore prints `SHOT FAILED: Occluded`, then `composition 0% × timing 0% · subject seen 0%`, then `line-of-sight 10%` — two adjacent readouts contradicting each other about the same measurement, on the readout this project has already been burned by once.
- [x] [Review][Patch] **`TryGetConfigProblem` validates raw fields while the grader runs on `Safe*`/resolved ones** [`GradingConfig.cs:357-411`] — Both directions fail. *False alarm:* `prominenceIdealMax: 2.0` in YAML fires "prominenceFalloffAbove (1.15) is not above prominenceIdealMax (2)" and tells you to raise a value that needs no raising — `Safe` clamps idealMax to 1.0 and the resolved curve is fine. *Missed alarm:* `timingFullSeconds: 70` / `timingZeroSeconds: 90` passes the raw `90 <= 70` test with a clean console, but both clamp to the 60 s ceiling and `ResolveTimingWindow` hands the grader **60 / 60.001** — precisely the 1 ms cliff the check exists to prevent. The file's own rule applies verbatim: "a warning that names a fallback the code does not actually use is worse than no warning".
- [x] [Review][Patch] **`timingFullSeconds <= 0` is unwarned** [`GradingConfig.cs:404`] — Permitted by `[Min(0f)]` and by `ClampFinite(…, 0f, 60f, …)`. At 0 the GDD's ±0.5 s full-marks band vanishes: only a shutter pulled exactly inside the peak window scores full timing. The file carries explicit, symmetric `<= 0` warnings for `minSubjectHeight` and `minVisibleSamples` — and a comment calling that symmetry out — so this is a gap in an established pattern, not a new idea.
- [x] [Review][Patch] **Silent clamp ceilings are undocumented and unwarned, and `OnValidate` destroys hand-authored values on mere selection** [`GradingConfig.cs:170,179,184,433-448`] — `SafeProminenceFalloffAbove` caps at **4**, the two timing accessors at **60**. Neither bound appears in a tooltip, both contradict the `[Min(0f)]` attributes that imply no upper bound, and `TryGetConfigProblem` checks only NaN/Infinity — never "this value was clamped". Worse, `OnValidate` now writes eight more fields back through `Safe*`, so opening the asset in the Inspector once silently rewrites `prominenceFalloffAbove: 10` to `4` and dirties the file. Given that this project hand-authors these assets as YAML, the value is destroyed by the act of looking at it.
- [x] [Review][Patch] **The placement term measures the clamped rect, so spilling off-frame earns a centring bonus** [`ShotGrader.cs:363-364`] — `rect` is viewport-clamped by `TryGetScreenRect`. A subject projecting to x ∈ [0.4, 1.4] has a true centre of 0.9 but a clamped centre of 0.7 — the further he spills out of frame, the more centred the placement term believes he is. `framedFraction` penalises this in a *different* term, but the placement measurement itself is reading the wrong geometry. The unclamped `minX/maxX/minY/maxY` are right there and are used two lines later for `unclampedArea`.
- [x] [Review][Patch] **`EventActor.PeakOffset` returns 0 — full timing marks — for an un-begun actor, which is the opposite of what its comment claims** [`EventActor.cs:106,112`] — `_phase` defaults to `Spawn` (the enum's zero value) and `TimeToPeak` to `0f`, so `Mathf.Max(0f, 0f)` returns **0**, which `ISubject.PeakOffset`'s own contract defines as "inside the peak window" → `timing = 1.0`. The comment claims this branch reports "before the peak"; it reports *at* the peak. **Verified not reachable today** — `EventManager.Spawn()` calls `Begin(route)` before `_active.Add(actor)` — but `ISubject` is public API with no such guarantee. Same property: `definition.GetPhase(EventPhase.Peak).duration` can throw on a null `PhaseConfig` (it is a class, and `IsValid` tests it for null); the `definition != null` guard covers the wrong half of the expression. Latent only because `Awake` disables actors with invalid definitions.
- [x] [Review][Patch] **The Dev Agent Record's AC3 ladder contradicts the evidence file it cites** — The record claims `z3` is "the only 5★ in twenty-nine photographs" and "nothing mediocre came close", with `x_time_peak_start` at `70% / 4★`. `_bmad-output/verification/photo-shoot/shots.txt` says `x_time_peak_start — counted — 85% 5★` and `z3_money_shot — 100% 5★`: **two 5★ shots, not one**, and the second is the shot the record itself labels "good, not great". The table was written before the correct-course re-shoot and never corrected, while the prose below it was. AC3's headline proof is currently resting on stale numbers.
- [x] [Review][Patch] **Three tooltip numbers do not match the evidence they cite** [`GradingConfig.cs:80,95-96`] — "a value of 1.15 means a subject filling the whole frame still keeps **half** of his prominence" — the actual value is `InverseLerp(1.15, 0.78, 1.0)` = **0.405**, i.e. 40%. And `o_centred_closeup.png at 0.66` appears as **68.1%** in `shots.txt` and **0.63** in the Dev Agent Record — three values for one measurement. These tooltips *are* the tuning rationale a designer reads.
- [x] [Review][Patch] **Stale terminology, and a code comment citing a path this same commit banned** [`ShotGrader.cs:203,288,88-89`] — Comments still say "failed the frustum or the **coverage** gate" and "rejected at the **coverage** gate" after the gate became height-based, and `Coverage01` still exists as a live, differently-defined field, so "the coverage gate" is now actively misleading rather than merely stale. Separately the Gate-2 comment cites `Temp/PhotoShoot/b_mid.png` while the same commit rewrote `CLAUDE.md` to state that Unity deletes `Temp/` on shutdown; the evidence now lives at `_bmad-output/verification/photo-shoot/b_mid.png`.
- [x] [Review][Patch] **Eight fallback constants are duplicated as bare literals inside the `Safe*` accessors** [`GradingConfig.cs:161-184`] — Only `minSubjectHeight` got a named `DefaultMinSubjectHeight`. Change `prominenceIdealMax = 0.78f` to something else and the corrupt-asset fallback silently keeps restoring 0.78.
- [x] [Review][Patch] **A non-finite `cam.pixelRect` slips past the `NoViewport` gate** [`ShotGrader.cs:218`] — `NaN <= 0f` is false, so the gate passes; `heightFraction` becomes NaN, `NaN < SafeMinSubjectHeight` is false so the size gate passes too, and `Sane()` converts the NaN composition to 0 — yielding a *counted* hit at 0% for a camera that cannot render. `bounds` gets an explicit `IsFinite` check twelve lines earlier with a comment about this exact failure mode; `view` did not get the same treatment. (Hypothesis — I could not construct a NaN `pixelRect` to reproduce; the downstream propagation is traced.)
- [x] [Review][Patch] **Rank a counted shot above a miss in the subject tie-break** [`PhotoModeController.cs:504`] — promoted from the decision above with Alexv's explicit authorisation to deviate from Task 6. Change the predicate so a shot that passed every gate always beats a rejected one regardless of percentage, keeping "first graded wins ties" within each class. Record the Task 6 deviation in the Dev Agent Record.

### Deferred

- [x] [Review][Defer] **All 24 town shots report line-of-sight 100%, including subjects behind trees** [`ShotGrader.VisibleFraction`] — deferred, pre-existing (Story 1.9's occlusion gate). Reproduced and confirmed in `_bmad-output/verification/photo-shoot-town/shots.txt`. AC4's claim that "the occlusion half is no longer inert" is proven in the **private rig world only** (`u_wall_mid` correctly rejects `Occluded`); in the real town the gate appears inert. Ruled out: the world is layer 0 (Default), which is in `occluderMask`; `Subject` is layer 8 and correctly excluded. **Root cause undiagnosed** — I could not run Unity this session. Leading hypothesis worth testing first: `Tools > Add MeshColliders to World` only walks `MeshFilter`s under a root named `"game map"` or `"World"`, so any foliage outside that root has no collider at all for the linecast to hit.
- [x] [Review][Defer] **`TryGetConfigProblem` reports only the first problem, once at Awake** [`GradingConfig.cs:250-415`] — deferred. This diff added ten checks to a first-wins chain, and `minSubjectHeight <= 0` returns before every composition-curve and five-stars-unreachable check. A designer with three mistakes needs three play-mode cycles to find them.
- [x] [Review][Defer] **A NaN phase duration passes `EventDefinition.IsValid`** [`EventDefinition.cs:122`] — deferred, pre-existing (Story 1.6). `NaN < 0f` is false, so validation passes; `PeakOffset` then returns NaN and every post-peak shot scores 0% with a clean console.
- [x] [Review][Defer] **The "5★ unreachable" validation family does not cover the sweet-spot-vs-cut-off interaction** [`GradingConfig.cs:385-401`] — deferred, hypothesis. `heightFraction` is viewport-clamped to 1.0, so a sweet spot authored very high (e.g. `prominenceIdealMin: 0.95`) forces `framedFraction < 1`, dragging composition below 1.0 permanently — silent, clean console, caps every photograph. Same class the story asked to be warned about.
- [x] [Review][Defer] **The NFR2 performance claim was re-asserted, not re-measured** [`ShotGrader.cs:123-124`] — deferred. "Story 1.10 added arithmetic only… so that measurement still stands" is a sound argument (confirmed: no new linecast, allocation or readback) but the 0.0072 ms figure was not re-taken.
- [x] [Review][Defer] **`ShotGrade.FromPercent` is now dead code and self-contradicting** [`ShotGrade.cs:95-96`] — deferred. Zero production callers. It yields `Counted == true` with an all-zero breakdown, so `ToString()` would emit `ShotGrade(62%, 4★ — composition 0% × timing 0%)`, contradicting `Percent01`'s own documented invariant. Delete it or make it not `Counted` when Story 1.12 lands.
- [x] [Review][Defer] **The placement penalty is aspect-distorted** [`ShotGrader.cs:363-368`] — deferred, design. `cx`/`cy` are normalized independently, so on a 16:9 frame the same pixel offset costs ~1.8× more vertically than horizontally. Standard practice for normalized viewport space, but undocumented.

### Dismissed (raised by a review layer, disproven on verification)

- **"A counted 0% shot is reported as `NoSubject`"** — disproven. The `!graded` clause at `PhotoModeController.cs:504` guarantees the first graded subject always replaces the seed. This was the 1.9 review's fix and it holds.
- **"`default(ShotGrade)` reads as a counted 0% shot"** — disproven. `GradeMiss.Unevaluated` is the enum's zero value, so a zero-initialised `ShotGrade` has `Counted == false` and `IsMiss == false`. The structural guard works.
- **"`b_mid` at 0.39 is capped at 4★ forever"** — disproven by arithmetic. `InverseLerp(0.15, 0.45, 0.391)` = 0.803 → `CeilToInt(0.803 × 5)` = **5**, not 4. The real issue is the opposite one, recorded as a decision above.
- **"The `minCoverage` → `minSubjectHeight` rename needs a migration"** — disproven. Exactly one `GradingConfig.asset` exists, it carries all eight new keys with no stale `minCoverage`, and a whole-`Assets/` grep returns zero live references to the old name.
- **"Missing keys in a pre-1.10 asset would zero the prominence curve"** — dismissed. Raised as an explicit hypothesis; Unity leaves absent keys at their C# field initialisers, and the only asset in the project is complete.
- **"`ShotGrade.Miss` was deleted against Task 6's 'keep `Miss` working'"** — dismissed. Nothing breaks (the only surviving mention is a comment), and a parameterless `Miss` would have to carry `MissReason.None`, i.e. "counted" — the removal is the correct call.

### Verification of the review patches — run 2026-07-28, not just applied

All 14 patches were applied **and then run**. The assembly was confirmed rebuilt before anything was
trusted (`CameraGame.dll` 13:05:53 → **13:42:41**, 85504 → 89600 bytes) — the trap this story itself
recorded, where `refresh_unity` reports success without recompiling. `Assets/Refresh` plus an explicit
compile request was what actually forced it.

**Console:** zero errors, zero new warnings. The full photo-shoot run produced exactly the documented
baseline (Version Control project-link notice + the two large-triangle mesh warnings the town scene logs
on reload); the real scene in play mode produced the notice + `ThirdPersonCamera` head-bone warning. No
`GradingConfig` warning on the shipped config.

**Photo shoot re-run** (29 photographs, `_bmad-output/verification/photo-shoot/`), diffed against the
pre-patch `shots.txt`:

| Patch | Evidence |
|---|---|
| Unmeasured-value sentinels | `g_looking_away`, `h_off_to_side`, `l_inside_away` now read `height n/a  framed n/a  area n/a`, where they previously asserted `height 0.0%  framed 100%`. Shots rejected *after* projection (`c_far`, `t_size_5h` TooSmall; `f_behind_wall`, `u_wall_mid` Occluded) still report their real measurements — the sentinel fires only where nothing was measured. |
| Placement from the unclamped centre | **Isolated cleanly on `p_cut_off`:** height `100.0 → 100.0`, framed `23 → 23`, box identical — every input to prominence and framing bit-identical between runs — and composition still moved **17% → 16%**. The only term that can have changed is placement. |
| No regression | `z3_money_shot` 100%·5★, `x_time_peak_start` 85%·5★, `m_placement_centred` 100%, `m_placement_thirds` 87%, `k_straddling` 16% — all unchanged. |
| `timingFullSeconds <= 0` warning | Authored `timingFullSeconds: 0` into the asset, entered play, and the new warning fired verbatim. Genuinely new: 0 is in range, so `OnValidate` leaves it and the old validator had no check. Asset restored (md5 `2cc1e893…` matches the original, and `git diff` against HEAD is empty). |

**A control worth recording:** `m_placement_thirds`'s height drifted 56.1% → 59.3% between runs yet its
composition stayed at 87%, because both values sit inside the `[0.45, 0.78]` plateau. That independently
confirms the sweet spot behaves as a trapezoid rather than a slope.

**Two things this run did NOT prove, stated plainly:**

1. **My own prediction about `k_straddling` was wrong.** I expected the placement patch to lower it; it did
   not move. Its box centre is `(0.500, 0.500)` — nose-to-nose, the frame cuts him symmetrically on every
   side, so the clamped and unclamped centres coincide and placement was never the lever. The cut-off term
   already handled that case. Recorded because the reasoning, not the patch, was at fault.
2. **The raw-vs-`Safe` validator fix (P4) could not be reproduced in the editor, and the finding behind it
   is weaker than all three review layers claimed.** `OnValidate` fires on **asset import**, not merely on
   Inspector selection, so it normalises the in-memory object before `Awake` ever runs: authoring
   `timingFullSeconds: 70 / timingZeroSeconds: 90` on disk produced a config whose fields already read
   `60 / 60` at validation time, and the *existing* structural check caught the resulting cliff on its own.
   The patch is still correct and still closes the hole **in a player build**, where `OnValidate` never
   runs and the serialized 70/90 would reach the grader with the raw check silent — but that path was not
   exercised here. Treat P4 as reasoned, not demonstrated.

### AC3's perceptual half — CLOSED 2026-07-30

**Alexv's verdict on the final tuning: "B looks better"** — `z3_money_shot` over `x_time_peak_start`,
shown as a matched pair with the numbers hidden from the filenames. That is the judgement AC3's second
clause needed and it had never been given on the shipped tuning (his earlier verdicts, 2026-07-26 and
-27, were on the pre- and mid-correct-course shots).

**Why the two photographs differ, for the record:** both score `timing 100%` because both are inside the
peak window, but `EnterPhase` starts every phase with a 0.2 s `CrossFadeInFixedTime`. `x_time_peak_start`
fired at `PeakOffset 0.00s` — the literal first frame of the peak — so the animator was still showing the
*walk* pose and the stagger had not visibly begun. `z3` fired 1.2 s in, mid-stagger. Worth knowing: the
first fraction of the peak window is a run-in, not the money shot. **Not acted on**, because it turned out
not to be the cause of the star tie (see the resolved decision above), but it is real and it is the reason
the two pictures look so different.

⇒ **AC3 is now verified in full**: five stars is reachable by a good shot (`z3`, the only one in the set)
and not by a mediocre one, judged by eye and then proven by re-shooting.

**Still unproven and needing eyes at some point** (unchanged, and not blocking): AC1's cut-off and corner
halves — whether 1★ is the right verdict for `p_cut_off` (17%) and `k_straddling` (16%) as *photographs*.
Structurally correct and arithmetically verified; the aesthetic call has not been made.

### Superseded — what I originally needed from you (kept for the record)

The structural work is verified: I confirmed AC1, AC2, AC3's release-build and miss-legibility clauses,
AC4's gate bracket, AC5's accessors/validation/synchronous-maths, Task 1's `ISubject` boundary and all
four of Task 6's "do not touch" items — several by re-deriving shipped grades by hand from `shots.txt`
and matching them exactly.

**What no check can settle is whether the ladder reads correctly as photographs**, and the verdicts on
file were given on a tuning that has since changed. Please open
`_bmad-output/verification/photo-shoot/` and answer one question:

> `x_time_peak_start.png` (**85% · 5★**) versus `z3_money_shot.png` (**100% · 5★**).
> Both are dead centre and inside the peak; `z3` is closer (48.1% of frame height vs 40.5%).
> **Do these deserve the same star rating?** If `x` looks clearly worse than `z3`, the prominence
> floor is too generous and 5★ is too easy — which is the AC3 clause the stale table claimed to have proven.

Until you have, **AC3's "5 stars unreachable by a mediocre shot" is unproven**, and so is AC1's
cut-off/corner half (`p_cut_off.png` at 17%, `k_straddling.png` at 16% — structurally right, but whether
1★ is the right verdict for a deliberately-cropped shot needs your eye).
