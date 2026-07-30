# Story 1.9: Subject-capture gate grading

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want a shot to only count if the subject is actually well within frame,
so that the grade reflects whether I truly caught the moment.

## Acceptance Criteria

**AC1 — The subject must be visibly in frame, or the shot fails (FR5, AR3)**
- On capture, `ShotGrader` evaluates the shot against an `ISubject`. The subject must be **inside the
  camera frustum** AND **not occluded** by world geometry; otherwise the shot returns a near-zero /
  failing grade.
- Uses `GeometryUtility.TestPlanesAABB` against `ISubject.Bounds` for the frustum test, and a physics
  ray/linecast for occlusion. **No GPU readback** — see Dev Note *"No GPU readback means no pixels"*.

**AC2 — Below ~8% frame coverage the shot fails (FR5, AR3)**
- With the subject in frame, coverage is computed from **screen-space bounds**: project the subject's
  world AABB to a screen-space `Rect`, and take its area as a fraction of the camera's pixel area.
- A shot below the **~8%** coverage gate fails. The number is a `GradingConfig` field, not a literal.
- The projection must be correct for a subject **partially behind the camera** — see Dev Note
  *"The behind-the-camera projection trap"*. This is the single most likely bug in this story.

**AC3 — Thresholds live in a `GradingConfig` ScriptableObject (AR-Config)**
- `minCoverage`, the occlusion `LayerMask`, and any other gate tunable live in a `GradingConfig`
  ScriptableObject asset (Inspector-tunable, no recompile). **No grading magic numbers in code.**
- Mirrors the established `CameraConfig` / `CaptureConfig` pattern exactly.

**AC4 — Capture actually grades a real subject, and stays fail-soft + clean (NFR2, NFR5, NFR8)**
- `PhotoModeController.OnCapture` stops raising `ShotGrade.Placeholder` and raises a **real** grade.
- Capture-to-feedback stays **under 0.2 s** (NFR2) — grading is synchronous math, no coroutine, no
  readback, no `FindObjectsOfType`.
- **Fail-soft:** a missing `GradingConfig`, a missing subject source, or no live subject must **never
  throw** and must not spam. A capture with no subject in the world is a legitimate miss, not an error.
- Console shows **zero errors and zero new warnings** (NFR5). Baseline is the two known pre-existing
  warnings — the Version Control project-link notice and `ThirdPersonCamera.cs:43` "POV Camera: Head
  bone not found!". No regression to Move/Look/Sprint/Jump or the 1.3–1.8 photo flow.

## Tasks / Subtasks

- [x] **Task 1 — Solve subject discovery (AC1, AC4) — DO THIS FIRST, IT GATES EVERYTHING**
  - [x] **Nothing currently connects a capture to a subject.** `PhotoModeController.OnCapture`
        (`PhotoModeController.cs:242-266`) raises `ShotGrade.Placeholder` and has no subject reference of
        any kind. `EventManager` keeps `_activeCount` as a **private int** (`EventManager.cs:33`) and
        exposes **no accessor for its live actors**. `ShotGrader.Grade(cam, subject, cfg)` per the
        architecture takes the subject as a parameter — so somebody has to supply it, and today nobody can.
  - [x] **Recommended approach — expose live actors from `EventManager`:** keep a
        `readonly List<EventActor> _active` alongside the existing `_activeCount` (or replace the counter
        with `_active.Count`, which removes a second source of truth), add the actor in `Spawn()` and
        remove it in `HandleDespawned()`, and expose `public IReadOnlyList<EventActor> ActiveActors`.
        `PhotoModeController` gets a `[SerializeField] private EventManager eventManager;` — a cached
        direct reference, exactly the architecture's "cached direct refs for intra-system" rule.
  - [x] Why not the alternatives: a `SubjectRegistry` singleton is explicitly ruled out by the
        architecture ("No singleton, no DI container, no service locator"); `FindObjectsOfType` on every
        capture violates NFR2 and the project's no-magic-lookup convention; the `EventPeakedChannel`
        only fires **at** the peak, but the player can shoot at any point in the lifecycle.
  - [x] There is **one asmdef** (`CameraGame`) covering `PhotoMode`, `Events` and `Grading`, so this
        creates no assembly-boundary problem. `PhotoModeController` already does `using CameraGame.Events;`.
  - [x] **Grade every live actor and keep the best score.** `maxConcurrent` is 1 today (`EventManager.cs:21`),
        so this degenerates to the obvious case — but writing it as a loop now costs three lines and means
        Epic 2's busier town does not silently grade the wrong drunk.
  - [x] ⚠️ **Honour the `ISubject` liveness contract** (`ISubject.cs:10-13`): subjects are **pooled**, so a
        stale reference does not go null — it silently becomes a *different* event. Read the subject
        **live at capture time**. Never cache an `ISubject` in a field across frames.

- [x] **Task 2 — Create `GradingConfig` ScriptableObject + asset (AC3)**
  - [x] `Assets/Scripts/Grading/GradingConfig.cs`, namespace `CameraGame.Grading`. Copy the shape of
        `CaptureConfig.cs` — `[CreateAssetMenu(menuName = "CameraGame/Grading/Grading Config", fileName = "GradingConfig")]`,
        `[Tooltip]` on every field, an `OnValidate` that clamps, and `Safe*` accessors where a bad value
        would break the math.
  - [x] Fields this story needs (and **only** these — the composition/timing curves are Story 1.10):
        - `[Range(0f,1f)] public float minCoverage = 0.08f;` — the ~8% gate (GDD line 199).
        - `public LayerMask occluderMask;` — what counts as blocking. See Task 4.
        - `[Min(1)] public int occlusionSamples = 5;` — how many points on the subject to test.
        - `[Range(0f,1f)] public float minVisibleSamples = 0.2f;` — fraction that must be unblocked.
  - [x] Asset at `Assets/Data/Grading/GradingConfig.asset` (architecture source tree, line 369).
  - [x] ⚠️ **Unity MCP cannot create custom ScriptableObject assets.** Write the `.asset` YAML by hand
        using the GUID from `GradingConfig.cs.meta`. [[create-so-asset-via-yaml]] — this bit Story 1.4 and
        1.6; do not burn a session rediscovering it.

- [x] **Task 3 — Write `ShotGrader` (AC1, AC2)**
  - [x] `Assets/Scripts/Grading/ShotGrader.cs` — a **`public static class`** with
        `public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg)`. Pure logic, no
        MonoBehaviour, no state. This is the architecture's exact signature (game-architecture.md:526-528).
  - [x] Order the work cheapest-first and early-out: null guards → frustum test → coverage → occlusion.
        The occlusion raycast is by far the most expensive step and must not run for a shot that already
        failed the frustum or coverage gate.
  - [x] Frustum: `GeometryUtility.CalculateFrustumPlanes(cam, _planes)` into a **cached `static readonly
        Plane[6]`** — the allocating overload returns a fresh array per call. (Once per capture is not a
        perf crisis; do it right anyway, it is the same number of lines.)
  - [x] Coverage: project the 8 AABB corners, build a `Rect`, **clamp it to the screen** before measuring
        area — a subject half out of frame must not be credited for off-screen pixels. Divide by
        `cam.pixelWidth * cam.pixelHeight`, **not** `Screen.width/height` (correct under viewport rects and
        a second camera; `Screen.*` is the whole window).
  - [x] Return `ShotGrade.Miss` on any gate failure. On pass, return a real grade — for this story the
        passing value is a simple normalized coverage score; **Story 1.10 replaces the score body** with
        the composition × timing blend. Leave the seam obvious and commented.

- [x] **Task 4 — Occlusion without a `Transform` (AC1)**
  - [x] ⚠️ **`ISubject` deliberately exposes no `Transform` and no `Collider`** (`ISubject.cs:15-29`) — only
        `Bounds`, `IsAtPeak`, `TimeToPeak`, `SubjectId`. So you **cannot** raycast and ask "did I hit the
        subject?". Do **not** solve this by adding a `Transform` to `ISubject`; that re-couples grading to
        scene objects and breaks the seam the interface exists to protect.
  - [x] **Use a `LayerMask` instead:** linecast from `cam.transform.position` to sample points on the
        subject against `cfg.occluderMask`, where the mask **excludes the subject's own layer**. Any hit =
        blocked. No identity comparison needed.
  - [x] ⚠️ **Every object in this project is currently on layer 0 (Default)** — verified across
        `EventActor_Drunk.prefab`. A mask that excludes Default excludes the world too, and the gate would
        never fail. **You must create a `Subject` layer, put the drunk prefab's renderer hierarchy on it,
        and set `occluderMask` to Default-only.** Add the layer name to `GameConstants.Layers`, which
        exists for exactly this and is still an empty placeholder (`GameConstants.cs`).
  - [x] Sample several points, not just `Bounds.center`: a centre-only ray through a lamppost fails a shot
        that is 95% visible. Use the centre plus points toward the AABB extremes; pass if the visible
        fraction ≥ `cfg.minVisibleSamples`.
  - [x] Use `Physics.Linecast` (segment, camera → point), **not** `Physics.Raycast` with an unbounded
        length, or geometry behind the subject will count as occluding it.
  - [x] Perf note: the world has **16,321 MeshColliders** (CLAUDE.md). A handful of linecasts per capture
        is fine; a linecast per frame would not be. This runs on capture only.

- [x] **Task 5 — Wire grading into capture (AC4)**
  - [x] In `PhotoModeController`: add `[SerializeField] private GradingConfig gradingConfig;` and the
        `EventManager` reference from Task 1. Resolve a `_gradingReady` flag in `Awake` **independently**
        of `_captureReady`/`_shutterReady`/`_flashReady` — the 1.4 and 1.5 reviews both praised that
        independence, and one missing reference must never disable the others.
  - [x] Replace `shotCapturedChannel.Raise(ShotGrade.Placeholder)` (`PhotoModeController.cs:262`) with the
        real grade. If `!_gradingReady`, **keep raising `Placeholder`** rather than going silent — the
        capture feedback and the channel must survive a missing config (AC4 fail-soft).
  - [x] Grade **before** the flash/SFX or after? After. Feedback fires first so it always feels instant;
        grading is cheap but it is not the thing the player is waiting on.
  - [x] Update the `GameLog.Info` line — it currently hard-codes "(placeholder grade)"
        (`PhotoModeController.cs:265`). Log the real result, and include *why* a shot missed; a silent 0%
        is the single most confusing thing you can hand a designer.
  - [x] `ShotGrade` already carries `IsPlaceholder` and a `Stars` mapping (`ShotGrade.cs`) — **reuse it,
        do not invent a second result type.** Adding a miss-reason to the struct is allowed and useful
        (Story 1.12's HUD will want it), but keep it additive: `ShotGrade` is a `readonly struct` already
        carried by `ShotCapturedChannel`.

- [x] **Task 6 — Verify (AC1, AC2, AC3, AC4)**
  - [x] **Draw what you are grading.** Add an editor-only gizmo/`Debug` overlay behind
        `#if UNITY_EDITOR || DEVELOPMENT_BUILD` (architecture Consistency Rules) that draws the subject
        AABB and the computed screen `Rect` with its coverage %. Grading is invisible by nature — this is
        the equivalent of the Play session that closed 1.7 and 1.8, and it turns "the number looks wrong"
        into "the box is wrong".
  - [x] Cases to check by hand, in Play: subject centred and close (**pass**, high coverage); subject far
        away (**fail** on coverage); subject behind a building (**fail** on occlusion); camera pointed
        away (**fail** on frustum); **subject partially behind the camera while still visible** (must NOT
        produce a garbage coverage number — this is the trap below); no event alive at all (**miss**, no
        error, clean console).
  - [x] Confirm capture-to-feedback still feels instant (NFR2 < 0.2 s).
  - [x] `read_console` after every script change → zero errors, zero new warnings vs the two-warning
        baseline. ⚠️ MCP `read_console` does **not** surface `Debug.Log`, so `GameLog.Info` grade lines are
        invisible through it — grep `%LOCALAPPDATA%\Unity\Editor\Editor.log` instead.
        [[mcp-playmode-verification-gotchas]]
  - [x] ⚠️ **Do not run asset operations while in Play mode** — a mid-play import wedged the lifecycle for
        a whole session in Story 1.8 and convincingly mimicked a real bug.

### Review Findings

Code review 2026-07-26 (three parallel layers: Blind Hunter, Edge Case Hunter, Acceptance Auditor),
plus a runtime probe of `ShotGrader`'s projection/gate maths. 50 raw findings deduped to 32.
Probe evidence: `Temp/GradingReview/report.txt` and `timing.txt` (regenerate by re-running the review —
the probe was temporary scaffolding and has been deleted).

**Verified by running, not just reading:**
- Near-plane clipping is CORRECT for straddling and fully-behind subjects (1u straddle → sane 100%;
  3u/20u behind → `OutsideFrustum`; non-finite bounds → `BehindCamera`, no throw). This closes the
  evidence gap left when the T-pose bench was retracted.
- NFR2 measured against the real SampleScene (16,739 active colliders): **0.0072 ms/call** including
  linecasts — ~28,000× margin on the 0.2 s budget. AC4's perf claim is now measured.
- Occlusion sample distribution measured: 5 samples → 60% (3/5), 9 → 56% (5/9), 16 → 56% (9/16), and
  the reading *oscillates* 54–64% for a subject that is physically 50% occluded.

- [x] [Review][Patch] **A shot taken facing AWAY from a subject you are standing inside counts as a perfect 5★** — An AABB that encloses the camera is outside no frustum plane, so `TestPlanesAABB` passes; the forward-side corners then project to enormous coordinates that clamp to the whole viewport, giving `coverage = 100%`. REPRODUCED: `inside box, facing away` → verdict `None`, coverage 100.0000%, 5★. Reachable because `EventActor_Drunk.prefab` has **zero colliders**, so the player walks straight through the subject. Contributing cause: `VisibleFraction` linecasts from `cam.transform.position` (behind the near plane) to sample points that may themselves be behind the camera. **DECIDED 2026-07-26 (Alexv):** reject when the bounds centre is behind the near plane (return `BehindCamera`), and skip occlusion sample points behind the lens so linecasts never run backwards. Genuine close-ups must still pass — `dist 3u` should stay at 100%. [Assets/Scripts/Grading/ShotGrader.cs:113-132, 257-271]

- [x] [Review][Patch] Photo-shoot rig strands the developer and arms a latent scene-clobber: the asset guard returns AFTER `NewScene`, leaving an untitled scene open and `SessionState` still set, so the next unrelated play-mode exit force-reopens a scene and discards unsaved work [Assets/Scripts/Editor/PhotoShootRig.cs:83-100]
- [x] [Review][Patch] Occlusion sampling is biased and self-duplicating: at the shipped 5 samples all four peripheral points have `i & 4 == 0` (the −Z face only); above 9 the `& 7` wrap re-samples taken corners, so raising the `[Range(1,16)]` setting buys linecasts and re-weighting rather than precision [Assets/Scripts/Grading/ShotGrader.cs:278-288]
- [x] [Review][Patch] `minVisibleSamples = 0` silently disables the occlusion gate (`visible < 0f` is false even when every sample is blocked) and is the one config boundary `TryGetConfigProblem` does not check [Assets/Scripts/Grading/GradingConfig.cs:42,56-81]
- [x] [Review][Patch] Nothing enforces the invariant the `occluderMask` tooltip states in capitals — a mask containing the Subject layer makes every shot fail `Occluded` with a clean console; `GameConstants.Layers.Subject` was added with zero consumers [Assets/Scripts/Grading/GradingConfig.cs:56-81, Assets/Scripts/Core/GameConstants.cs]
- [x] [Review][Patch] `minCoverage` is the only tunable read raw (no `SafeMinCoverage`); `NaN` passes `<= 0f` and `>= 1f` and survives `Mathf.Clamp01`, so a hand-authored asset can disable the coverage gate entirely with no warning [Assets/Scripts/Grading/ShotGrader.cs:134, Assets/Scripts/Grading/GradingConfig.cs:56-88]
- [x] [Review][Patch] Non-finite `Bounds` slip the `DegenerateBounds` guard (`NaN <= Mathf.Epsilon` is false) and reach `TestPlanesAABB`, which emits an uncategorised engine `Invalid AABB` error — console spam with no `[Grading]` tag to grep [Assets/Scripts/Grading/ShotGrader.cs:107]
- [x] [Review][Patch] `subject == null` is plain reference equality because `ISubject` is an interface, so Unity's destroyed-object overload never runs; a destroyed `EventActor` passed to this public API throws `MissingReferenceException` on `subject.Bounds` [Assets/Scripts/Grading/ShotGrader.cs:99-102]
- [x] [Review][Patch] `i == 0` seeds the best detail by list index rather than "first subject actually graded", so a null entry at index 0 makes a real `Occluded`/`TooSmall` rejection report as `NoSubject` [Assets/Scripts/PhotoMode/PhotoModeController.cs:433]
- [x] [Review][Patch] `default(GradeDetail)` leaves `VisibleFraction = 0`, so `OcclusionTested` is true and the overlay prints "line-of-sight 0%" — "completely hidden" — for a shot that was never graded; the same mistake `GradeMiss.Unevaluated` fixed, one field over [Assets/Scripts/PhotoMode/PhotoModeController.cs:326, Assets/Scripts/Grading/ShotGrader.cs:34-48]
- [x] [Review][Patch] Coverage clamps to `[0, cam.pixelWidth]` instead of the camera's `pixelRect`. REPRODUCED with `cam.rect = (0.5,0,0.5,1)`: a subject dead-centre in the viewport has both edges clamped to 480 → width 0 → rejected `TooSmall` at 0% [Assets/Scripts/Grading/ShotGrader.cs:237-242]
- [x] [Review][WITHDRAWN] ~~The debug overlay flips y with `Screen.height` while the rect lives in camera pixel space~~ — **this finding was wrong and the code is correct.** `WorldToScreenPoint` returns WINDOW-space pixels (as does `cam.pixelRect`) and GUI space spans the whole window, so `Screen.height - r.y - r.height` is right even for a viewport-rect camera. The 960x540-vs-640x480 divergence I measured came from the probe's own bound RenderTexture — precisely the case where an OnGUI overlay is meaningless anyway. Only a clarifying comment was added. [Assets/Scripts/PhotoMode/PhotoModeController.cs:379]
- [x] [Review][Patch] A rect that collapses to zero width/height reports `TooSmall` rather than `OutsideFrustum`, and a zero-size viewport (`pixelWidth == 0`) degrades every capture to `TooSmall` with no distinguishing reason [Assets/Scripts/Grading/ShotGrader.cs:131-139,242]
- [x] [Review][Patch] The rig draws the grader's box in the wrong pixel space unless the Game View is exactly 16:9 — grading measures the Game View, then `Save()` re-renders into a fixed 960x540 target, changing horizontal FOV, and rescales by `960/Screen.width`. This undermines "is the box on him?", the check the whole story leans on [Assets/Scripts/PhotoMode/PhotoShootRunner.cs:219-240]
- [x] [Review][Patch] The rig caches an `EventActor` across up to ~4 s of yields, against the liveness contract `EventManager.ActiveActors` documents in the same change; it can aim at a recycled/despawned actor and log `NoSubject` for a pose whose intent was "standing right in front of him" [Assets/Scripts/PhotoMode/PhotoShootRunner.cs:148-191]
- [x] [Review][Patch] `Finish()` is not in a `finally` and the run never calls `ExitPlaymode()`, so any throw loses `shots.txt` and the editor sits in the test world until a human presses Stop — the exact "never leave the editor in the test world" rule in CLAUDE.md [Assets/Scripts/PhotoMode/PhotoShootRunner.cs:77-109,280-285]
- [x] [Review][Patch] `Application.runInBackground` is set and never restored (no observable effect today — the project value is already 1 — but it is the template the next rig copies) [Assets/Scripts/PhotoMode/PhotoShootRunner.cs:79]
- [x] [Review][Patch] `isDirty` is checked only on the active scene, so `NewScene(Single)` silently discards unsaved additively-loaded scenes; an untitled scene yields an empty path so the restore short-circuits; `Directory.Delete` followed immediately by `CreateDirectory` is the Windows lazy-deletion race [Assets/Scripts/Editor/PhotoShootRig.cs:71-79]
- [x] [Review][Patch] `SetPrivate`'s `case Object o` does not match null (C# type patterns exclude null), so an absent optional config logs "Unsupported type" — naming the switch instead of the missing asset [Assets/Scripts/Editor/PhotoShootRig.cs:187-193]
- [x] [Review][Patch] The rig has no straddling or behind-camera pose (closest is ~1.2 subject heights ≈ 11u, half-depth ~2.2u), so the near-plane clipping branch and the `BehindCamera` early-out have no regression coverage in the surviving rig [Assets/Scripts/PhotoMode/PhotoShootRunner.cs:58-72]
- [x] [Review][Patch] `_active` keeps entries if the manager GameObject is deactivated: pooled actors are parented to it, so their `Update` stops, `Despawned` never fires, and `HandleDespawned` never runs. `GradeBestSubject` guards `actor == null` but not `isActiveAndEnabled`, so an inactive renderer still yields plausible bounds and an empty street grades as a hit [Assets/Scripts/Events/EventManager.cs:103-126, Assets/Scripts/PhotoMode/PhotoModeController.cs:426-427]
- [x] [Review][Patch] Dev Agent Record accuracy: the File List omits `PhotoShootRig.cs`, `PhotoShootRunner.cs`, `CameraGame.Editor.asmdef`, `CLAUDE.md`, `.gitignore` and three new public methods (`Capture`, `SetPhotoMode`, `SetZoom`); the retracted T-pose table still sits under a green "✅ CLOSED — 10/10" heading referencing a deleted file, with the retraction 30 lines below; and the record both claims the occlusion case was hand-confirmed and twice states the gate had never run [this file]

- [x] [Review][Defer] **The shipped `minCoverage = 0.08` rejects the intended subject at normal portrait distance** — corroborated by the review probe: a clean ~8.86u box measures 5.19% at 20 units and 11.81% at 14 units, so the real pass threshold is ~14 units wide-angle. The animated drunk measures 4.14% at ~20u and is rejected while plainly being the subject. Knock-on effect: because gates run cheapest-first, a subject far enough away to be behind a building is rejected `TooSmall` before the occlusion linecasts ever run — so AC1's occlusion half is close to inert at the shipped tuning and has never been exercised against real world geometry (only a synthetic close cube). **Deferred 2026-07-26 (Alexv):** Story 1.10 re-tunes these exact numbers with the real composition curve, and the GDD labels 8% a starting value to confirm via playtest. Already tracked in `deferred-work.md`. [Assets/Data/Grading/GradingConfig.asset:15]
- [x] [Review][Defer] Star mapping is degenerate — the passing score is raw coverage, so real play hits (16–21%) are 1–2★ and 5★ needs ~80% coverage; a miss and a typical hit are both 1★ — deferred, Story 1.10 explicitly owns the score body
- [x] [Review][Defer] `ProjectSettings/TagManager.asset` lost every URP rendering-layer name (`Light Layer 1-7` + 24 blanks) and bumped `serializedVersion` 2→3 — deferred, Unity's own migration on `add_layer`; verified harmless today (every `m_RenderingLayerMask` in the project is 1, both scene cameras' culling masks are all-bits)
- [x] [Review][Defer] Moving the whole actor hierarchy to layer `Subject` means no subject can ever occlude another subject, and the actor has left `Default` for every other mask in the project — deferred, invisible at `maxConcurrent = 1`, an Epic 2 concern
- [x] [Review][Defer] The miss reason cannot reach a release build — `GradeDetail` is exposed only behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`, so `ShotCapturedChannel` still carries score only — deferred, Story 1.12's HUD will need to revisit `ShotGrade`
- [x] [Review][Defer] `NavMeshSurface.BuildNavMesh()` is called in edit mode and produces non-persisted `NavMeshData` that may not survive the play-mode domain reload — deferred, the story's recorded run shows animated bounds and no agent errors, but I did not independently re-run the shoot

**✅ ALL 21 PATCHES APPLIED AND VERIFIED BY RUNNING (2026-07-26).** Not "compiles clean" — the fixes were
re-measured with the probe and then driven end to end through the real game.

Probe deltas (before → after):

| Case | Before | After |
|---|---|---|
| `inside box, facing AWAY` | `None`, 100%, **5★** | **`BehindCamera`**, 0% |
| `enclosing box, facing AWAY` | `None`, 100%, 5★ | `BehindCamera`, 0% |
| `centre 1u BEHIND` | `None`, 100%, 5★ | `BehindCamera`, 0% |
| `straddling (centre +1u)` | `None`, 100% | `None`, 100% — **unchanged, legit close-up preserved** |
| `dist 3u` (genuine close-up) | `None`, 100% | `None`, 100% — unchanged |
| OFFSET viewport, dist 14 | `TooSmall`, **0.0000%**, box 0px wide | `None`, 23.62%, box 174x351 |
| `NaN`/`Infinity` bounds | `BehindCamera` + engine `Invalid AABB` error | `DegenerateBounds`, **console clean** |
| occlusion sweep 1→15 | 5 samples all on −Z face; >9 duplicated; oscillated 54–64% | matches hand-computed prediction exactly at every count; >15 clamped |
| bad configs | NaN minCoverage / minVisible=0 / mask-includes-Subject all silent | **all four caught** with actionable messages |

Real-game run (`Tools > Grading > Photo Shoot (Play)`, real animated actor, real shutter):
`l_inside_away` → **rejected `BehindCamera`** (the picture is genuinely empty — verified by eye; this exact
empty photograph previously scored 100% / 5★). `k_straddling` → counted 5★ at 100%, so the fix does not
over-reject a real nose-to-nose shot; this is also the FIRST time any rig pose exercised the near-plane
clipping branch. `f_behind_wall` → `Occluded`, 25.09% coverage, 0% line-of-sight, red box correctly marking
his position behind the wall (verified by eye). `a_close` → counted, box sits tightly on him (verified by
eye). `j_no_subject` → `NoSubject`. Rig auto-exited play mode and reopened SampleScene undirtied.

**Still NOT verified — do not treat as proven:**
- The occlusion gate has still only ever been exercised against a synthetic cube wall, never against the
  town's 16,321 real MeshColliders. At the shipped 8% tuning a subject far enough away to be behind a
  building is rejected `TooSmall` first, so this stays unreachable until the deferred retune happens.
- Whether the photographs *read* as good photographs is Alexv's call, not a structural check. The two new
  poses (`k_straddling`, `l_inside_away`) have not been human-reviewed.
- `NavMeshSurface.BuildNavMesh()` surviving the domain reload — the run above shows animated bounds and a
  clean console, which is suggestive but I did not test it adversarially.

**Dismissed as noise (4):** `_active.Clear()` suspected in `OnDisable` (it is `OnDestroy`, `EventManager.cs:119`); linecast cost per capture (measured 0.0072 ms against 16,739 colliders); corners projected twice in `TryGetScreenRect` (measured non-issue — though the comment claims an optimisation the code does not perform); orthographic projection suspected to shrink the rect (probe showed sane behaviour, and the game is perspective).

## Dev Notes

### What this story IS — and is NOT

**IS:** the *gate*. Can this shot count at all? Frustum + occlusion + a coverage floor, driven by a
tunable config, wired into the existing capture path so a real `ShotGrade` finally reaches the channel.

**IS NOT:** the *score*. The coverage sweet-spot curve (25–50%), rule-of-thirds placement, and peak-timing
falloff are **Story 1.10** — the epic splits them deliberately. Also not: the gallery (1.11), the
grade-feedback HUD (1.12), multi-subject arbitration beyond "best of the live ones", or any image capture.
`ShotGrade` already exists from 1.5 — **do not create a second result type.**

### ⚠️ The behind-the-camera projection trap — read this before writing `ScreenBounds`

`Camera.WorldToScreenPoint` does **not** fail for points behind the camera. It returns a point with
**negative `z`** and **mirrored `x`/`y`**. Projecting the 8 corners of an AABB naively and taking
min/max produces, for any subject straddling the camera plane, a `Rect` that is wildly wrong — usually
enormous, which makes a shot the player took facing *away* from the drunk sail through an 8% coverage gate.

The subject is ~8.86 units tall in a world modelled at ~4× metric scale, so its AABB is large in world
units and straddling the camera plane is **not** an edge case — it happens any time the player stands
next to the drunk, which is exactly when they are trying to photograph him.

**Handle it explicitly.** Either reject corners with `z <= 0` and treat the shot as failing the frustum
gate if too few remain, or clip the AABB against the camera's near plane before projecting. Do not simply
`Mathf.Abs` the coordinates — that produces a plausible-looking number that is silently wrong, which is
the worst possible failure mode for a grading system nobody can see.

This is the same family as Story 1.7's `CrossFade`-duration-is-normalized and Story 1.8's
`maxDistance`-is-not-where-sound-ends: **a Unity API whose behaviour does not match what its name
implies.** That is now three for three across three stories. Check the semantics, not the name.

### No GPU readback means no pixels

AR3 and the epic both say "screen-space frame read, **no GPU readback**". Concretely: no
`Texture2D.ReadPixels`, no `RenderTexture` + `AsyncGPUReadback`, no `Camera.Render()` into a target, no
replacement shaders. `ReadPixels` stalls the pipeline for milliseconds and would blow the 0.2 s
capture-to-feedback budget on its own. Everything here is **maths on `Bounds` and a matrix** plus a few
linecasts. If you find yourself reaching for a texture, you have taken a wrong turn.

### The `Bounds` you are grading is not free of surprises

`EventActor.Bounds` (`EventActor.cs:66-83`) encapsulates **every child renderer including inactive ones**
(`GetComponentsInChildren<Renderer>(true)`, deliberate since 1.6 so a prop revealed at Peak still counts).
Two consequences worth knowing before you trust a number:

1. A child renderer that has never been rendered can report stale or default bounds, which would balloon
   the AABB. Task 6's gizmo exists partly to catch this — **look at the box before believing the %**.
2. It is a **world-axis-aligned** box around a tall thin character, so it over-reports coverage for a
   figure standing diagonally to the camera. That is an accepted approximation at this stage (the
   architecture chose AABB deliberately for cheapness) — just do not be surprised when a 30% reading
   looks more like 20% on screen. Tune `minCoverage` by eye against the gizmo, not by arithmetic.
3. The skinned mesh has `updateWhenOffscreen` enabled (set in Story 1.7 to fix a freezing mesh), so its
   bounds *are* animation-accurate rather than the static authored box. Good news here — but it means
   bounds change through the stagger animation, so coverage will wobble frame to frame. Grade once at the
   captured instant; never average across frames.

### Fail-soft precedent to follow

Copy the pattern, don't invent one. `PhotoModeController.Awake` (lines 100-173) already resolves four
independent readiness flags with a tailored log level for each — `Error` where a reference is genuinely
missing, `Info` where absence is a *supported* state (no shutter clip authored yet). Grading has the same
shape: a missing `GradingConfig` is an authoring **Error**; "no event is currently alive" is an ordinary
gameplay state and must log nothing at all.

And the standing rule from the 1.7/1.8 reviews: **fail-soft must not mean invisible.** Warn once at
`Awake` for a configuration that cannot possibly work (e.g. an `occluderMask` of `Nothing`, which would
make the occlusion test a no-op and silently pass every shot). An absent subject is not that — it is valid.

### Testing standards

There is no automated test suite (Unity Test Framework 1.6.0 is installed but unused; the tests assembly
was explicitly deferred in Story 1.1 Task 6). Verification is MCP runtime polling plus a focused Play
session, as in every story so far.

**But note what just changed:** `ShotGrader` is a `static` class of pure maths with no Unity lifecycle —
the **first genuinely unit-testable code in this project**. The architecture already anticipates it
(`Tests/EditMode/ ShotGrader math`, line 386). Standing up the EditMode assembly is *not* required by this
story's ACs and should not be smuggled in — but if the gizmo work in Task 6 starts feeling slow, that is
the signal that the tests assembly has become worth its own story. Say so in the Dev Agent Record rather
than doing it silently.

## Project Structure Notes

Per the architecture's source tree (lines 362-369):

```
Assets/Scripts/Grading/     # ShotGrader.cs (NEW), GradingConfig.cs (NEW), ShotGrade.cs (exists, 1.5)
Assets/Data/Grading/        # GradingConfig.asset (NEW)
```

Code touched:
- **New:** `Assets/Scripts/Grading/ShotGrader.cs`, `Assets/Scripts/Grading/GradingConfig.cs`,
  `Assets/Data/Grading/GradingConfig.asset`
- **Modified:** `Assets/Scripts/Events/EventManager.cs` (expose live actors),
  `Assets/Scripts/PhotoMode/PhotoModeController.cs` (real grade on capture),
  `Assets/Scripts/Core/GameConstants.cs` (the `Subject` layer name),
  `Assets/Prefabs/Events/EventActor_Drunk.prefab` (layer assignment),
  `ProjectSettings/TagManager.asset` (the new layer)
- **Possibly modified:** `Assets/Scripts/Grading/ShotGrade.cs` (additive miss-reason only)

Namespace `CameraGame.Grading`, one asmdef, `GameLog` for all logging with a `"Grading"` category.

## Project Context Rules

No `project-context.md` exists in this project — the persistent-facts glob returned nothing. Governing
conventions were taken from `CLAUDE.md` and `game-architecture.md` instead:

- **URP only** — no Standard shader. (Not relevant here; do not add materials.)
- **`GameLog`** (`Assets/Scripts/Core/`) for all logging, never bare `Debug.Log`. Use a new `"Grading"`
  category. ERROR = can't function, WARN = unexpected but handled, INFO = milestones.
- **Fail-soft, never throw** — the game has no fail state (NFR8).
- **Data-driven via ScriptableObjects** for anything tunable; no magic numbers in code.
- **Cache in `Awake`, never `GetComponent`/`Camera.main` in `Update`** — `PhotoModeController` already
  caches its camera (line 104); do not add a second lookup.
- **Debug/gizmo code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`** (architecture Consistency Rules).
- **Check `read_console` after every script change** — a clean console is a project rule.
- **Jira sync is mandatory** — this story is `KAN-20` (project KAN, cloud
  `5b116b91-787f-4ff7-9668-2cd92d337bcf`; transitions 11=To Do, 21=In Progress, 31=Review, 41=Done).
  Mirror the Tasks above as Jira **Subtasks** under KAN-20 and add the "In plain terms (for
  non-developers):" comment to each.
- **Git:** `_bmad-output/` is gitignored — story files are local only. Commit *and* push in the same
  session. [[always-push-changes-to-git]]

## Previous Story Intelligence

From Story 1.8 (closed `done` 2026-07-25 after a 12-patch code review):

1. **A capability can be "working" and still wrong on a path nobody tested.** The cue was verified by ear
   and by runtime polling, yet the rolloff config sat inside the `loopCue` branch, so accent-only events
   would have been audible town-wide. **Lesson for 1.9: the gate has several entry conditions (in frame /
   occluded / too small / no subject at all). Exercise every one, not just the happy path.**
2. **Silent numeric misconfiguration is the recurring failure mode.** `[Min(0f)]` allowed `cueRadius = 0`,
   which produced total silence with a clean console. `GradingConfig.minCoverage` has exactly the same
   shape — 0 disables the gate, 1 fails every shot, and neither logs anything. Validate and warn once.
3. **Unity API names lie** (`maxDistance`, `CrossFade` duration). Your instance this story is
   `WorldToScreenPoint` behind the camera. See the Dev Note.
4. **Structural evidence is not perceptual evidence.** Two stories running, the human check found what
   green runtime polls could not. Grading has no sound and no animation — the Task 6 gizmo *is* your
   perceptual channel. Build it early, not last.
5. **The blind-review layer generates hypotheses, not conclusions.** Three of its four High findings in
   1.8 were wrong because it reasoned from comments describing a design that did not exist. Keep comments
   honest about what the code actually does today.

From Story 1.6 (the `ISubject` liveness contract) and 1.7 (the pooled-reuse Critical bug): **pooled
objects do not go null.** A stale `ISubject` becomes a different drunk, silently.

## Git Intelligence

Recent commits establishing the working patterns:

- `496c3f6` Story 1.8 review — the current `EventActor`/`PhotoModeController` shape, and the
  `TryResolve*` + warn-once validation idiom you should copy for `GradingConfig`. **Read this before
  editing either file.**
- `1bac39d` / `124468c` Story 1.8 — the custom-rolloff fix; a worked example of a Unity parameter whose
  semantics differ from its name, and of routing a tunable through a ScriptableObject instead of a prefab.
- `3d27828` Story 1.6 review — `EventManager`/`ObjectPool` hardening; the file you are about to extend
  in Task 1. Note its symmetric subscribe-on-spawn / unsubscribe-on-return discipline and keep the new
  list exactly as symmetric.

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.9: Subject-capture gate grading] — AC source (lines 433–451)
- [Source: _bmad-output/planning-artifacts/epics.md#FR5] — "the shot fails (near-zero) if the target subject is not within the frame or occupies below ~8% of the frame"
- [Source: game-architecture.md#Photo Capture & Grading] — `Grade(camera, subject, captureTime)`; `TestPlanesAABB` + raycast; thresholds in `GradingConfig` (lines 186–192)
- [Source: game-architecture.md#Frame-Read Grading] — the reference `ShotGrader` implementation sketch (lines 514–545)
- [Source: game-architecture.md ADR row 3] — "Hybrid screen-space frame read … no GPU readback stalls" (line 169)
- [Source: game-architecture.md#Source Tree] — `Scripts/Grading/`, `Data/Grading/`, `Tests/EditMode/` (lines 362–386)
- [Source: game-architecture.md#Data Patterns / Consistency Rules] — SO configs via `[SerializeField]`; debug code behind `#if` (lines 578–592)
- [Source: gdds/gdd-My project-2026-05-26/gdd.md] — "subject must occupy ≥ ~8% of the frame" (line 199); grade scale 1–5 stars (line 198)
- [Source: Assets/Scripts/Events/ISubject.cs:10-29] — the liveness contract; `Bounds`/`IsAtPeak`/`TimeToPeak`/`SubjectId`, and **no Transform**
- [Source: Assets/Scripts/Grading/ShotGrade.cs] — existing result struct: `FromPercent`, `Miss`, `Placeholder`, `Stars`
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:242-266] — the capture path to modify; `ShotGrade.Placeholder` at line 262
- [Source: Assets/Scripts/PhotoMode/CaptureConfig.cs] — the ScriptableObject pattern to mirror (tooltips, `OnValidate`, `Safe*`)
- [Source: Assets/Scripts/Events/EventManager.cs:33,60-96] — private `_activeCount`; `Spawn`/`HandleDespawned` are the hook points
- [Source: Assets/Scripts/Events/EventActor.cs:66-83] — `Bounds` from all child renderers incl. inactive
- [Source: Assets/Scripts/Core/GameConstants.cs] — empty `Layers` placeholder awaiting the `Subject` layer
- [Source: _bmad-output/implementation-artifacts/1-8-diegetic-cue-telegraphing.md#Review Findings] — validation/warn-once precedent
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — world scale (~4× metric); NavMesh/agent entanglement

## Dev Agent Record

### Agent Model Used

claude-opus-5[1m] (Claude Opus 5, 1M context) — dev-story workflow, 2026-07-25.

### Debug Log References

- Unity `read_console` after every script change — final state: zero errors, baseline warnings only.
- MCP runtime polling of `EventActor_Drunk(Clone)` for layer and lifecycle confirmation.
- ⚠️ `read_console` does **not** surface `Debug.Log`, so the `GameLog.Info` grade lines are invisible
  through MCP. For grade output, grep `%LOCALAPPDATA%\Unity\Editor\Editor.log` for `[Grading]`, or just
  read the new on-screen debug overlay. [[mcp-playmode-verification-gotchas]]

### Completion Notes List

**Task 1 — the gating unknown, resolved as the story recommended.** `EventManager` now keeps a
`List<EventActor> _active` and exposes `IReadOnlyList<EventActor> ActiveActors`. I **replaced** the old
`_activeCount` int rather than keeping both, because two sources of truth for "how many are alive" is
exactly the kind of drift that produces a silent wedge. A side benefit: the old counter needed a
`Mathf.Max(0, ...)` floor to survive a double-despawn, and `List.Remove` is naturally idempotent, so that
guard is now structural instead of arithmetic. `OnDestroy` clears the list as well as the pool, so a
scene unload cannot leave a late reader walking destroyed references.

**Task 3/4 — the behind-the-camera trap, handled by real near-plane clipping.** `TryGetScreenRect`
projects the corners that are in front of the near plane, and for every one of the box's 12 edges that
*crosses* the plane it projects the crossing point instead. Edges are enumerated by bit arithmetic
(corners differing by exactly one bit) rather than a hand-written table, so there is no 12-entry list to
get wrong. A box entirely behind the camera returns `false` → `GradeMiss.BehindCamera`, which is a
distinct outcome from `OutsideFrustum` — worth separating, because `TestPlanesAABB` genuinely can pass
for a box that lies wholly behind the camera while still intersecting the side planes.

**A finding the story did not anticipate: the drunk prefab has NO colliders at all.** Task 4's premise is
that the subject would occlude itself unless excluded by layer — but with no collider he is invisible to
`Physics.Linecast` regardless. So the `Subject` layer is not load-bearing *today*. I created it and moved
the prefab onto it anyway: the moment anyone adds a capsule collider (Epic 8's stealth layer being the
obvious candidate) a Default-only mask would start reporting the subject as occluding himself, and every
shot would fail with no visible cause. Cheap insurance against a latent trap of exactly the shape the last
two reviews kept finding.

**A verification trap I hit and corrected.** After hand-editing `ProjectSettings/TagManager.asset`, the
runtime reported the clone as `layer: 8, layerName: ""` — the object was on the right index but Unity did
not know the index had a name, because Unity caches ProjectSettings in memory and never reloaded my
on-disk edit. Re-doing it through `manage_editor add_layer` registered it properly (slot 8, matching the
prefab). **Lesson: editing ProjectSettings on disk while the editor is running does not take.** Same
family as 1.8's "if a runtime poll contradicts code you just wrote, suspect the assembly before the logic".

**The hand-written LayerMask YAML — and how it was actually proven.** `LayerMask` serialises as
`serializedVersion: 2` + `m_Bits`, not a bare int (confirmed against `m_CullingMask` in the scene). MCP
refused to patch the field at all (`Unsupported SerializedPropertyType: LayerMask`), so it could not be
verified that way. It was proven instead by the config's own validation: `TryGetConfigProblem` warns when
`occluderMask.value == 0`, and that warning did **not** fire at Awake — which it only does when the mask
parsed as non-empty. The guard written for designers ended up validating the asset pipeline.

**Scope held.** No composition curve, no timing falloff, no gallery, no HUD, no `AudioDirector`-style
extras. The passing score is raw coverage — an honest, meaningful number rather than an invented constant
— with the seam for Story 1.10 marked in a comment at the exact line that will be replaced. `ShotGrade`
was reused unchanged; the miss *reason* rides in a separate `GradeDetail` struct so the channel payload
did not have to change.

**✅ CLOSED — verified by an automated test bench, 10/10 (2026-07-26).** Alexv's Play session confirmed
the first four cases by hand, then asked for the verification to be automated rather than repeated
manually. `Assets/Scripts/Editor/GradingTestHarness.cs` (**Tools > Grading > Run Gate Tests**) now does it:
it opens a clean isolated scene, places the real drunk prefab and the camera at exact scripted positions,
runs the real `ShotGrader`, renders what the camera sees, and **draws the grader's own computed box onto
the rendered image**. That last part is what makes it objective — "does the number look right?" becomes
"is the box on him?". Output: `Temp/GradingTests/` (PNG per scenario + `report.txt`), edit-mode only, no
Play mode, fully repeatable.

| Scenario | Distance / FOV | Result |
|---|---|---|
| very close | 9.9 u · 60° | pass, 41.67% ✅ |
| close | 16.5 u · 60° | pass, 13.54% ✅ |
| medium | 49.5 u · 60° | `TooSmall`, 1.37% ✅ |
| far | 123.8 u · 60° | `TooSmall`, 0.21% ✅ |
| facing away | 20.6 u · 60° | `OutsideFrustum` ✅ |
| off frame | 9.9 u · 60° | `OutsideFrustum` ✅ |
| **occluded (wall between)** | 9.9 u · 60° | `Occluded` — coverage 41.67%, line-of-sight **0%** ✅ |
| **straddling (nose-to-nose)** | 2.9 u · 60° | pass, 100% — *sane*, not garbage ✅ |
| medium **zoomed** | 49.5 u · 18° | pass, 18.14% ✅ |
| far zoomed | 123.8 u · 18° | `TooSmall`, 2.82% ✅ |

Visual confirmation: the box sits tightly on the subject at every range, is absent when he is out of
frame, and marks his position behind the wall in the occluded case.

**The occlusion gate had never actually been exercised before this.** In the first harness run it was
still failing on *coverage* (7.64% at that distance) and returning before the linecasts ever ran — the
`line-of-sight n/a` readout is what exposed it. Moving the scenario closer let the shot clear the coverage
gate and reach occlusion, which then correctly reported 0% visibility.

**⚠️ THE ABOVE FIGURES ARE SUPERSEDED — they were measured on a T-POSE.** The edit-mode bench called
`ShotGrader` directly against a stand-in subject, and the Animator does not run outside play mode, so the
drunk stood arms-out. A T-pose AABB is far wider than the character ever is in play, and every coverage
figure above is roughly **double** the truth. The bench was replaced by a play-mode rig (below); its
numbers are the ones to trust.

**Design conclusion drawn from the bench, now disproven:** it read 18.14% at 49.5 units zoomed and I
concluded shots land from "~20 units wide-angle or ~74 units zoomed". Playing it for real gives **7.34%**
at that distance. The true ranges are **~14 units wide-angle and ~48 units zoomed**, against a 100-unit
cue radius. The "hear it before you can shoot it" relationship still holds, but with far less margin than
claimed.

**✅ Verified by playing the game — `Tools > Grading > Photo Shoot (Play)` (2026-07-26).** Alexv asked for
real play verification rather than an assertion suite, and reviewed the resulting photographs himself
("saw them and it's ok"). The rig builds a private world (drunk, camera, ground, baked NavMesh), walks the
camera to ten vantage points, pulls the **real** shutter through `PhotoModeController.Capture()`, and
saves what the camera saw with the grader's own box drawn on it. It asserts nothing and cannot pass or
fail — the photographs are the result. Output: `Temp/PhotoShoot/` + `shots.txt`.

Real (animated) results: close **counted at 16%**; mid/far/very-far rejected `TooSmall`; zoomed at 50 units
sits **on the threshold** (7.34% one run, 9.02% the next — the walk-cycle frame decides it); wall between
rejected `Occluded` at 21% coverage and **0% line-of-sight**; facing away rejected `OutsideFrustum`; empty
world rejected `NoSubject`.

**Bug the play-mode rig found that the bench could not.** Pressing the shutter in an empty world reported
a **counted** shot. `GradeMiss.None` was the enum's zero value, so the `default(GradeDetail)` returned by
`GradeBestSubject` when no actors are live was indistinguishable from a pass — the grade was correctly 0%,
but the overlay would have drawn green and Story 1.12's HUD would have read a successful photograph of
nobody. Fixed structurally (`GradeMiss.Unevaluated` is now the zero value, so no default can mean success)
plus an explicit `NoSubject` reason.

**The occlusion gate had still never actually run in the real game.** The earlier "proven" claim came from
the T-pose bench; with real animated bounds the shot was rejected `TooSmall` at ~5% before reaching the
linecasts. Moved closer, it now clears coverage at 21% and correctly reports `Occluded`.

**🔶 OPEN DESIGN QUESTION (not changed here) — the 8% gate looks too strict.** At a natural portrait
distance (~20 units) the animated drunk measures **4.14%** and is rejected, though he is plainly the
subject of the photograph — see `Temp/PhotoShoot/b_mid.png`. The GDD's ~8% likely assumed a chunkier
subject; an axis-aligned box around this tall thin character is mostly empty air, so area-based coverage
under-reads him badly. Options: drop `minCoverage` to ~3%, or measure the box's *height* fraction rather
than its area (far less sensitive to a narrow silhouette). Deferred to Story 1.10, which re-tunes these
same numbers with the real composition curve.

**Three Unity traps the play-mode rig itself hit** (all documented at the call site): an editor-assembly
MonoBehaviour cannot be resolved on a scene object when entering play mode ("The referenced script
(Unknown) is missing!" — the rig silently did nothing); `ScreenCapture.CaptureScreenshot` grabs a Game
View that does not repaint its 3D content while the editor runs unattended (ten images of the readout on
blank white); and `Camera.fieldOfView` set from outside is overwritten every frame by `Update`'s zoom
easing (a shot meant for 18° recorded 22°).

**Two Unity traps the earlier edit-mode bench hit** (kept for the record — that bench has been deleted):
1. Assets loaded *before* `EditorSceneManager.NewScene(..., Single)` have their native half unloaded. The
   `GradingConfig` then read `minCoverage = 8%` perfectly from managed memory while `cfg == null` was
   simultaneously **true** — every scenario reported `NoConfig` from a config that printed fine. Load
   assets *after* the scene swap.
2. `ShotGrader` measures coverage against `cam.pixelWidth`, which is the Game view until a `targetTexture`
   is bound. Grading before binding measured a 979px viewport while writing a 960px PNG, so the drawn box
   and the image were in different pixel spaces. Bind the render target before running the logic.

### File List

**New:**
- `Assets/Scripts/Grading/GradingConfig.cs` (+ `.meta`) — gate thresholds SO, with authoring validation
- `Assets/Scripts/Grading/ShotGrader.cs` (+ `.meta`) — the grader: frustum, near-plane-clipped coverage, occlusion
- `Assets/Data/Grading/GradingConfig.asset` (+ `.meta`) — `minCoverage 0.08`, `occluderMask` = Default, 5 samples, 0.2 visible

**Modified:**
- `Assets/Scripts/Events/EventManager.cs` — `ActiveActors` list replaces the private `_activeCount`
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` — `gradingConfig`/`eventManager` refs, `_gradingReady`,
  real grade on capture, `GradeBestSubject`, editor-only debug overlay
- `Assets/Scripts/Core/GameConstants.cs` — `Layers.Subject`
- `Assets/Prefabs/Events/EventActor_Drunk.prefab` — whole hierarchy (28 objects) to layer 8 (Subject)
- `ProjectSettings/TagManager.asset` — `Subject` layer at slot 8
- `Assets/Scenes/SampleScene.unity` — `PhotoModeController.gradingConfig` + `.eventManager` assigned

**Not modified:** `Assets/Scripts/Grading/ShotGrade.cs` (reused as-is — the miss reason rides in
`GradeDetail` instead, so the `ShotCapturedChannel` payload is unchanged).

### Change Log

| Date | Change |
|------|--------|
| 2026-07-25 | Story implemented. `EventManager` exposes live actors (replacing the private counter) so capture can find a subject at all — the story's gating unknown. New `ShotGrader` applies three gates cheapest-first: frustum (`TestPlanesAABB`), frame coverage from a **near-plane-clipped** screen projection, and occlusion via layer-masked linecasts. New `GradingConfig` SO carries every threshold plus authoring validation that warns on an empty occluder mask or a 0/1 coverage gate. `PhotoModeController` now raises a real grade with an independent `_gradingReady` flag, logs the miss *reason*, and draws an editor-only overlay of the graded box. Created the `Subject` layer and moved the drunk onto it. Compile clean: zero errors, baseline warnings only. **AC1/AC2 implemented but awaiting the human Play session (Task 6).** |
