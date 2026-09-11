# Story 1.13: Soften the timing multiplier

Status: ready-for-dev

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want a well-composed photograph taken slightly too late to still be worth something,
so that good framing is never annihilated by one mistimed press.

## Origin

Alexv's call, twice, during Story 1.12's AC3 perceptual check (2026-09-11):

> "Even though I understand that timing it is important, it should not be a big deal the timing for me.
> I think it's too extreme to give 0 to the c_counted_but_zero.png picture."

The shot in question scored **composition 98 % × timing 0 % = 0 %, 1★** — a photograph in which the subject
is centred, whole-body, unoccluded and correctly exposed, judged worthless because the shutter fell 3.9 s
after the peak. The hard zero is `percent = composition × timing` working exactly as designed (ruled as
designed 2026-07-28), and it is defensible — a well-framed photograph of nothing happening is not a good
photograph. It has now surprised a reader twice, and the product owner has asked for it to change.

## Acceptance Criteria

**AC1 — Timing can no longer drive a counted shot to zero on its own**
- A shot that passes every gate scores above zero whenever its composition does, however badly timed.
- The shape is a floor on the timing factor, not a change to the pillars:
  `percent = composition × (floor + (1 − floor) × timing)`
- `floor` is a designer value on `GradingConfig`, **not a literal** — `timingFloor`, `[Range(0,1)]`,
  default `0.3`, reached through a `Safe*` accessor like every other tunable, because `[Range]` is
  editor-only and this project hand-authors its `.asset` YAML (`GradingConfig.cs` idiom).
- `TryGetConfigProblem` reports a non-finite or out-of-range `timingFloor`, and a floor of `1.0` — which
  would disable timing entirely and is almost certainly a typo rather than an intent.

**AC2 — Timing still dominates**
- The ordering is unchanged: a perfectly timed shot still beats a mistimed one of equal composition, and by
  a wide margin. With `floor = 0.3` the three Story 1.12 exemplars become:

  | Shot | Composition | Timing | Now | After |
  |------|-------------|--------|-----|-------|
  | `a_money_shot` | 94 % | 100 % | 94 % ★★★★★ | **94 % ★★★★★** (unchanged) |
  | `b_mid_counted` | 89 % | 23 % | 20 % ★★☆☆☆ | **41 % ★★☆☆☆** |
  | `c_counted_but_zero` | 98 % | 0 % | 0 % ★☆☆☆☆ | **29 % ★★☆☆☆** |

- Star thresholds (`0.9 / 0.7 / 0.45 / 0.2`) are **not** retuned in this story. If the distribution ends up
  wrong, that is a separate, evidence-led decision — do not adjust two things at once and lose the ability
  to attribute the result.

**AC3 — ⚠️ The "counted but 0 %" state becomes unreachable, and everything that relies on it is updated**
- This is the consequence to think about before starting. A counted shot can no longer score 0 % unless its
  **composition** is 0, so the state Story 1.12's AC2 was largely built around — *"a counted shot at 0 % is
  a real, common state and is NOT a miss"* — effectively disappears from normal play.
- The HUD's handling must **stay** (it is still correct, and composition can still be 0), but:
  - `GradeHudShootRunner`'s `c_counted_but_zero` scenario no longer produces 0 % and its caption becomes a
    lie. Rename it to what it now demonstrates, or drive composition to zero to preserve the case.
  - The 2026-07-26 placement study's headline — *all 24 shots read `counted — 0 % 1★`* — stops being
    reproducible. Leave the record, note the change.
- Nothing may claim a state it can no longer produce. This project's most-repeated defect is a readout
  asserting something nobody measured.

**AC4 — Every recorded number moves, and every rig is re-run**
- `Tools > Grading > Photo Shoot (Play)`, `Tools > Gallery > Gallery Shoot (Play)` and
  `Tools > HUD > Grade HUD Shoot (Play)` all diff against stored evidence. Re-run all three, compare, and
  **state plainly which diffs are the intended re-scoring and which are not** — an unexplained diff in this
  set is a finding, and a re-scoring that quietly hides one is worse than no run at all.
- EditMode tests that assert exact percentages will fail by construction. Update them to the new expected
  values **and add one that pins the floor itself**: a shot with timing 0 and good composition must score
  above zero, and a shot with timing 1 must be unchanged by the floor.
- `_bmad-output/verification/` is gitignored and each rig **wipes its own folder on start** — copy anything
  you still need before the first run.

## Tasks / Subtasks

- [ ] **Task 1 — `timingFloor` on `GradingConfig` (AC1)**
  - [ ] Field, `[Range(0f, 1f)]`, `[Tooltip]`, default `0.3`, plus `SafeTimingFloor` clamping NaN/∞ the way
        `ClampFinite` already does. Do **not** add an `OnValidate` that repairs the raw field — that trap is
        documented on `GalleryConfig` and cost a real defect.
  - [ ] `TryGetConfigProblem` reports out-of-range and `1.0`.
- [ ] **Task 2 — Apply it in `ShotGrader` (AC1, AC2)**
  - [ ] One line where `percent` is formed. Keep `Timing01` **unchanged** on `ShotGrade` — it is what the
        HUD prints as "timing 23 %", and the player should still see the honest timing score, not a floored
        one. Only the product changes.
  - [ ] Comment it with the reason and this story's number, in the project's style.
- [ ] **Task 3 — Update everything that assumed a hard zero (AC3)**
  - [ ] Rename or re-purpose the HUD rig's `c_counted_but_zero` scenario so its caption is true.
  - [ ] Check `GradeHud`'s counted branch and `deferred-work.md` for claims that no longer hold.
- [ ] **Task 4 — Tests (AC4)**
  - [ ] Fix the percentage assertions; add the floor contract in both directions.
- [ ] **Task 5 — Re-run all three rigs and report the diffs honestly (AC4)**
- [ ] **Task 6 — Hand Alexv the new exemplars.** He asked for this change; he should see what it did to the
      readout before it is called done. The banded colours from 1.12 mean `c_counted_but_zero` will move
      from amber-1★ to amber-2★ — visible, and worth his eye.

## Dev Notes

- **Do not touch `Timing01`, `Composition01` or `Subject01`.** They are reports. Story 1.12's HUD prints all
  three and AC2 of that story exists to stop the readout stating anything that was not measured.
- The formula belongs where `percent` is computed in `ShotGrader`, not in `ShotGrade` — the struct is data.
- `floor = 0.3` is a starting value chosen to make a perfectly-composed, badly-timed shot land at ~2★ rather
  than 1★. It is a tuning knob; expect to move it once Alexv has played it.
- Related deferred entries that this story does **not** fix: the subject's facing is not scored, and the
  scoring window starts before the stagger is visible. Both are in `deferred-work.md`.

## Dev Agent Record

### Agent Model Used

### Completion Notes List

### File List

### Change Log

| Date | Change |
|------|--------|
| 2026-09-12 | Story created from Alexv's AC3 feedback on Story 1.12. |
