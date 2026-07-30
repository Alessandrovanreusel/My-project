# Story 1.3: Raise and lower the camera (Photo Mode toggle)

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to raise my camera by holding a button and lower it by releasing,
so that taking a photo is a deliberate, skillful act rather than a passive proximity check.

## Acceptance Criteria

**AC1 — Hold-to-raise, release-to-lower, within the transition budget (FR1, NFR2)**
- In Play mode in the slice scene (`SampleScene`), holding the **RaiseCamera** input (RMB / gamepad LT) transitions the view into the first-person **Photo** state with the **viewfinder overlay visible**, in **< 0.3 s**.
- **Releasing** the input returns to the **Walk** state (viewfinder hidden) within the same **< 0.3 s** budget.
- The transition is the *only* thing that changes — first-person look/move continues to work in both states (ADR-1 / AR7: the walk camera is already first-person; Photo is the same rig, not a separate camera).

**AC2 — RaiseCamera action added without breaking existing input (AR1)**
- A new **`RaiseCamera`** action exists in the **"Player"** action map and drives the toggle via PlayerInput **Send Messages** (`OnRaiseCamera`).
- The existing **Move / Look / Sprint / Jump** actions still work exactly as before — no binding, type, or handler regressions.
- The action is **type `Value`** (NOT `Button`) so the release event is delivered (see 🚨 Guardrail #1 — this is the bug fixed in Story 1.2).

**AC3 — Photo-only actions are inactive in Walk state**
- While in **Walk** state (RaiseCamera not held), no Photo-only behaviour can fire (no accidental capture/zoom). For this story that means the controller **exposes the current mode** so the (future) Zoom/Capture handlers in Stories 1.4/1.5 can gate on it — and **no zoom/capture code is wired yet**.

## Tasks / Subtasks

- [x] **Task 1 — Add the `RaiseCamera` input action (AC2)** — ✅ DONE: action added (Value type), RMB + gamepad LT bound, existing actions untouched, asset re-imported clean.
  - [x] Open `Assets/Input/InputSystem_Actions.inputactions`. In the **Player** action map, add a new action named exactly **`RaiseCamera`**. — *Done (after `Sprint`).*
  - [x] Set its **Action Type = `Value`**, Control Type = `Button`. 🚨 **Do NOT use Action Type `Button`** — under PlayerInput *Send Messages*, a `Button` action delivers only the press, never the release, so the camera would stay "raised" forever (this is the exact "sticky sprint" bug fixed in Story 1.2; see Guardrail #1 and `[[input-hold-actions-must-be-value]]`). — *Done: `"type": "Value"`, `expectedControlType: Button`, `initialStateCheck: false` (matches the Sprint pattern).*
  - [x] Add bindings: **`<Mouse>/rightButton`** and **`<Gamepad>/leftTrigger`**. Verify RMB / LT are not already bound to another action *in the Player map* (Capture will take LMB later, in Story 1.5 — leave LMB/Attack alone here). — *Done: verified `Attack` uses `<Mouse>/leftButton` + `<Gamepad>/buttonWest`, so RMB/LT were free.*
  - [x] Save the asset. Confirm Move/Look/Sprint/Jump bindings are untouched. — *Done: only the new action + two bindings were added; all other actions/bindings byte-for-byte unchanged.*

- [x] **Task 2 — Create `PhotoModeController` (AC1, AC2, AC3)** — ✅ DONE: `Assets/Scripts/PhotoMode/PhotoModeController.cs` created, compiles clean, attached to `main characters`, `viewfinderRoot` assigned.
  - [x] Create `Assets/Scripts/PhotoMode/PhotoModeController.cs` in namespace **`CameraGame.PhotoMode`**. — *Done.*
  - [x] Define a `public enum CameraMode { Walk, Photo }` and expose the current mode read-only (`Mode` + `IsPhotoMode`). — *Done (Stories 1.4/1.5 gate on `IsPhotoMode`).*
  - [x] Add the Send-Messages handler `public void OnRaiseCamera(InputValue value)` reading `value.isPressed`. — *Done; mirrors `ThirdPersonController.OnSprint`. Handler name matches `GameConstants.InputActions.RaiseCamera`.*
  - [x] 🚨 **Placement on `main characters`** (the PlayerInput GameObject). — *Done: component added to `main characters` (id 553524) alongside `ThirdPersonController`. Verified live: `Mode=Walk`, `IsPhotoMode=false`, `viewfinderRoot=PhotoViewfinder`.*
  - [x] Cache any component references in `Awake`; never `GetComponent` in `Update`. — *Done: `CanvasGroup` cached in `Awake`.*

- [x] **Task 3 — Minimal viewfinder overlay + transition (AC1)** — ✅ DONE: Screen-Space-Overlay Canvas + frame created and wired; fade honours the budget.
  - [x] Add a **Screen-Space-Overlay Canvas** with a placeholder viewfinder frame. — *Done: `PhotoViewfinder` Canvas (renderMode 0) + full-screen `ViewfinderFrame` Image (black, alpha 0.3, raycastTarget off) — placeholder lens tint; styled camcorder art deferred to a UX story.*
  - [x] Give `PhotoModeController` a serialized `viewfinderRoot` and show/hide it on mode change. — *Done: `[SerializeField] GameObject viewfinderRoot`, faded via a `CanvasGroup` the script auto-adds in `Awake`.*
  - [x] Honour the **< 0.3 s** budget (NFR2). — *Done: fade runs over `transitionSeconds` (default **0.15 s**, `[Range(0,0.3)]` so it can never exceed the budget), driven by `Mathf.MoveTowards` with `Time.deltaTime`. Value exposed for Inspector tuning; CameraConfig SO deferred to Story 1.4.*

- [x] **Task 4 — Fail-soft wiring + regression guard (AC1, AC2, NFR8)** — ✅ DONE.
  - [x] Validate `viewfinderRoot` in `Awake`; if null `GameLog.Error(...)` and degrade gracefully — never throw in `Update`. — *Done: null-guard sets `_viewfinderReady=false`; `Update` early-returns. Verified live in Play mode: no errors (ref was assigned, so the guard stayed silent and the `CanvasGroup` init ran → alpha 0).*
  - [x] Confirm Move/Look/Sprint/Jump still behave (only `OnRaiseCamera` added). — *Done: the input asset only gained one action; no existing handler/binding changed. (Interactive in-Play confirmation folded into Task 5's hold-test.)*
  - [x] Keep diagnostics behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`. — *Done: mode changes log via `GameLog.Debug_` (Editor/dev-only, auto-stripped from release).*

- [x] **Task 5 — Verify in Play mode + clean console (NFR4, NFR5)** — ✅ DONE: clean console + Alexv play-test passed.
  - [x] After saving scripts, call MCP **`read_console`** and confirm **zero compile errors**. — *Done: clean compile after `refresh_unity` (0 errors); 0 errors/0 warnings in edit, compile, AND Play mode.*
  - [x] In Play mode: hold RMB → viewfinder appears (< 0.3 s); release → it disappears (< 0.3 s); repeat rapidly to confirm **no stuck-on** state. Move/Look/Sprint/Jump unaffected. — *Done: **Alexv play-tested (2026-06-05) — works.** Hold → viewfinder shows; release → hides; no stuck-on; movement controls unaffected. The `Value`-action choice is validated (no sticky behaviour).*
  - [x] Confirm no new URP/Standard-shader warnings (NFR4) and a clean console (NFR5). — *Done: 0 warnings; no materials/shaders touched (UI only).*

## Dev Notes

### What this story IS — and is NOT
- **IS:** one new input action (`RaiseCamera`, **Value** type), one new script (`PhotoModeController` with a `Walk`/`Photo` enum), and a **minimal toggleable viewfinder overlay**. The mode is exposed so later stories can gate on it.
- **IS NOT:** zoom (FOV lerp = **Story 1.4**), capture (LMB → **Story 1.5**), the `CameraConfig` ScriptableObject (introduced in **1.4**), grading, events, or any styled/final viewfinder art. Resist scope creep — a plain visible overlay is enough here.
- This is the **first runtime gameplay script** of the project. Story 1.1 built the `Core` scaffold; Story 1.2 was scene/bake only. So you are writing genuinely new code — follow the established conventions below exactly.

### 🚨 Read-first guardrails

1. **`RaiseCamera` MUST be Action Type `Value`, not `Button`.** This is the single highest-risk item. Under `PlayerInput` in **Send Messages** mode, a `Button` action fires the `On…` message only on *press* — the *release* never arrives, so `value.isPressed` is never read as `false` and the camera latches in Photo mode forever. Story 1.2 hit this exact bug with Sprint ("sticky sprint") and fixed it by switching the action to `Value`. Mirror that here. (Project memory: hold/press-and-release inputs must be `Value` actions.) Verify by rapidly tapping RMB in Play mode — the overlay must drop every time you release.

2. **`PhotoModeController` goes on the `main characters` GameObject** (the one with `PlayerInput` + `ThirdPersonController`). *Send Messages* dispatches `OnRaiseCamera` only to scripts on the PlayerInput's own GameObject — a script on the Main Camera or a child bone will **never** receive it. If you later need the camera to react, have the controller (on the player) drive the camera/overlay via references, not by putting the input handler on the camera.

3. **Do NOT rename `ThirdPersonController` / `ThirdPersonCamera` / `CharacterAnimator`.** The architecture *suggests* renaming `ThirdPerson*` → `Player*` / `FirstPerson*` to match the FP behaviour, but that is **explicitly optional, deferred cleanup** (same brownfield reason as Stories 1.1/1.2: renaming a MonoBehaviour class breaks the serialized component references on the player in the scene). Leave the names; add your new class alongside them.

4. **No magic strings.** The action-name constant **already exists**: `CameraGame.Core.GameConstants.InputActions.RaiseCamera` (= `"RaiseCamera"`). The Send-Messages handler name (`OnRaiseCamera`) is derived from that action name — keep them identical. Don't hardcode `"RaiseCamera"` elsewhere; reference the constant if you need the string.

5. **URP only (NFR4).** The viewfinder is Unity UI (Canvas/Image) — fine. Do **not** add any Standard-shader material. No 3D materials are needed for this story.

6. **Fail-soft (NFR8).** Validate references in `Awake`/`Start`, log via `CameraGame.Core.GameLog.Error(...)`, and degrade gracefully. Never throw inside `Update`. Errors are never shown to the player.

### Current state of the files/objects this story touches (read before editing)

- **`Assets/Input/InputSystem_Actions.inputactions`** — the "Player" map currently holds `Move` (Value/Vector2), `Look` (Value/Vector2), `Attack`, `Interact`, `Crouch`, `Jump`, `Sprint`, `Previous`, `Next`. After Story 1.2, **`Sprint` is type `Value`** (the fix); `Move`/`Look` are also `Value`. You are *adding* `RaiseCamera` — do not retype or rebind the others.
- **`Assets/Scripts/Core/GameConstants.cs`** (no change needed): already declares `InputActions.RaiseCamera = "RaiseCamera"` (and `Capture`, `Zoom` for later stories). It lives in namespace `CameraGame.Core`.
- **`Assets/Scripts/Core/GameLog.cs`** (no change needed): use `GameLog.Info/Warn/Error(category, msg)` and `GameLog.Debug_` (Editor/dev-only) instead of raw `Debug.Log`. Category string suggestion: `"PhotoMode"`.
- **`Assets/Scripts/ThirdPersonController.cs`** (no change needed): the FP movement/look controller on `main characters`. It reads `OnMove/OnLook/OnSprint/OnJump` via Send Messages and exposes `CameraPitch`. Your `OnRaiseCamera` handler follows the **exact same pattern** as its `OnSprint` (`public void OnSprint(InputValue value) { _isSprinting = value.isPressed; }`) — copy that shape for press/release correctness.
- **`Assets/Scripts/ThirdPersonCamera.cs`** (no change needed): the FP camera that snaps to the head bone and reads pitch. It is a single camera rig — Photo mode reuses it (ADR-1); you are *not* adding a second camera.
- **`Assets/Scripts/PhotoMode/`** — folder exists (created by Story 1.1's scaffold), currently empty. Your `PhotoModeController.cs` is the first file here.

### Architecture compliance (what the dev MUST follow)
- **Camera model (Architecture §Camera & State):** "Single first-person camera with two modes — `Walk` and `Photo` — owned by a `PhotoModeController`. Hold RMB → Photo (fade in viewfinder UI, enable zoom); release → Walk. Raise/lower transition < 0.3 s." For 1.3 you implement the toggle + viewfinder; "enable zoom" is the *seam* for Story 1.4, not work for now.
- **State pattern (Architecture §State Patterns):** use a **lightweight enum state machine** (switch/branch on the `CameraMode` enum) — do **not** build a class-based State-pattern framework for two states. That would be over-engineering and is explicitly discouraged.
- **Input (AR1 / Architecture §Input):** extend the existing `PlayerInput` (Send-Messages), single "Player" map; Photo-only actions are no-ops outside Photo mode. A dedicated "Photo" action map is a *future* refactor, not now.
- **Code org (AR5 / Architecture §Code Organization):** namespace `CameraGame.PhotoMode`; file under `Scripts/PhotoMode/`; one `CameraGame.asmdef` already covers it (no asmdef change). `PhotoMode` may depend on `Core` and `Player`, never the reverse.
- **Consistency rules (Architecture §Consistency Rules):** cache component refs in `Awake`; tunables Inspector-exposed (serialized field ok here; SO comes in 1.4); subscribe/unsubscribe events in `OnEnable`/`OnDisable` *if* you use C# events (Send-Messages needs none); debug code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.

### Previous story intelligence (Stories 1.1 & 1.2)
- **From 1.1:** the `Core` scaffold (`GameLog`, `GameConstants`, `AnimHashes`, `ObjectPool<T>`, `EventChannel`) and `CameraGame.asmdef` exist and compile clean. The folder/namespace scaffold (`Scripts/{Core,PhotoMode,Events,Grading,Gallery}`, `Data/{Camera,Channels,Events,Grading}`) is already created — **don't recreate it**. `ObjectPool<T>` is not yet hardened (deferred to Story 1.6) — irrelevant here.
- **From 1.2 (directly relevant):** the **Sprint "sticky" bug** — a hold action typed `Button` under Send-Messages never delivered its release, latching sprint on. Fixed by switching the action to **`Value`**. **`RaiseCamera` is the same kind of hold action — type it `Value` from the start.** Also reconfirmed: after any script change, call MCP `read_console` and confirm zero errors before marking done.
- **Established discipline:** existing scripts were intentionally left un-renamed/un-moved across 1.1 and 1.2 — keep that here (Guardrail #3).

### Testing standards (Architecture §Testing / project norm)
- This story's quality gates are **manual Play-mode** (hold→raise <0.3 s, release→lower <0.3 s, no stuck-on after rapid taps, Move/Look/Sprint/Jump unaffected) plus **MCP `read_console`** for a clean console (NFR5). No EditMode/PlayMode unit test is *required* for "done".
- *Optional, nice-to-have:* if you keep mode-decision logic in a tiny pure method (e.g. `CameraMode NextMode(bool isPressed)`), a one-line EditMode test is cheap insurance — but don't gold-plate.

### Project Structure Notes
- New file: `Assets/Scripts/PhotoMode/PhotoModeController.cs` (namespace `CameraGame.PhotoMode`). No new folders, no asmdef change (the existing `CameraGame.asmdef` covers `Scripts/**`).
- Scene change: `Assets/Scenes/SampleScene.unity` gains the `PhotoModeController` component on `main characters` and a viewfinder Canvas. Save the scene.
- Do **not** create `Data/Camera/CameraConfig.asset` in this story — that is Story 1.4's deliverable (it owns zoom/FOV tunables).

### Project Context Rules
- No `project-context.md` exists; rules are drawn from CLAUDE.md and the architecture.
- **MCP for Unity is the toolchain:** create/edit scripts and do scene wiring through Unity MCP; after any script change call `read_console` for zero errors (project rule, NFR5).
- **URP only (NFR4):** never introduce the Standard shader.
- **Jira sync (CLAUDE.md):** this story maps to Jira issue **KAN-14** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-14, and mirror this Tasks/Subtasks breakdown as Jira Subtasks under KAN-14 (with the "In plain terms (for non-developers):" comment on each).

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.3] — acceptance criteria (FR1, NFR2, AR1, AR7/ADR-1).
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR1 (two camera states), NFR2 (raise/lower < 0.3 s), AR1 (extend PlayerInput; add RaiseCamera/Zoom/Capture), AR7 (camera fork), NFR4/NFR5/NFR8.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Camera & State] — single FP camera, two modes, `PhotoModeController`, < 0.3 s transition.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Input] — Send-Messages, single Player map, Photo-only actions are no-ops in Walk.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#State Patterns] — lightweight enum state machine (no class-based FSM for two states).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#ADR-1] — Walk state is first-person; "raise camera" = mode toggle on one rig.
- [Source: Assets/Scripts/ThirdPersonController.cs] — `OnSprint` press/release pattern to mirror for `OnRaiseCamera`; lives on `main characters` with `PlayerInput`.
- [Source: Assets/Scripts/Core/GameConstants.cs] — `InputActions.RaiseCamera` constant (already defined).
- [Source: Assets/Scripts/Core/GameLog.cs] — categorized logging wrapper to use instead of `Debug.Log`.
- [Source: _bmad-output/implementation-artifacts/1-2-walkable-town-patch-with-traversal-and-navmesh.md] — the Sprint `Button`→`Value` fix (why `RaiseCamera` must be `Value`).
- [Source: project memory `input-hold-actions-must-be-value`] — hold inputs must be `Value` actions under PlayerInput Messages.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]`

### Debug Log References

- `refresh_unity` (force, compile) after the input-asset edit + new script → domain reload completed, `editor_state.compilation.is_compiling=false`, **0 errors / 0 warnings**.
- Live component read (`PhotoModeController` on `main characters` id 553524): `Mode=Walk(0)`, `IsPhotoMode=false`, `viewfinderRoot=PhotoViewfinder`, `transitionSeconds=0.15`.
- Play mode: `read_console` → **0 errors / 0 warnings**. `PhotoViewfinder.CanvasGroup` (auto-added by `Awake`) read live as `alpha=0, interactable=false, blocksRaycasts=false` → confirms the fail-soft init path executed and the overlay starts hidden in Walk.

### Completion Notes List

- **Implemented & verified via Unity MCP (no human needed):**
  - *Task 1:* added the `RaiseCamera` action to the Player map as **type `Value`** (`expectedControlType: Button`, `initialStateCheck: false`) with `<Mouse>/rightButton` + `<Gamepad>/leftTrigger`. Confirmed `Attack` uses LMB/buttonWest so RMB/LT were free; no existing action/binding changed. `Value` (not `Button`) is deliberate — the Story 1.2 sticky-input fix.
  - *Task 2:* `PhotoModeController` (`CameraGame.PhotoMode`) with `enum CameraMode { Walk, Photo }`, read-only `Mode`/`IsPhotoMode`, and `OnRaiseCamera(InputValue)` mirroring `OnSprint`. Attached to `main characters` (the PlayerInput GameObject — required for Send-Messages to reach the handler). Compiles clean.
  - *Task 3:* `PhotoViewfinder` Screen-Space-Overlay Canvas + full-screen `ViewfinderFrame` Image (placeholder translucent lens tint). Fade via a `CanvasGroup` (auto-added in `Awake`) over `transitionSeconds` (0.15 s, `[Range(0,0.3)]` so it can never exceed the NFR2 budget).
  - *Task 4:* fail-soft null-guard on `viewfinderRoot` (logs via `GameLog.Error`, `Update` early-returns — never throws); mode-change diagnostics via Editor-only `GameLog.Debug_`. Only `OnRaiseCamera` was added, so Move/Look/Sprint/Jump handlers are untouched.
  - *Task 5 (objective parts):* clean console across edit/compile/Play mode; init verified; no shader/material changes (UI only, URP-safe).
- **Remaining human-in-the-loop gate (cannot be synthesized via MCP):** a live **RMB hold-test** in Play mode — hold → viewfinder fades in < 0.3 s; release → fades out; rapid taps show **no stuck-on**; Move/Look/Sprint/Jump still work. The MCP toolset can drive the Editor and read state but cannot inject a held mouse button, so this ~30 s confirmation needs Alexv (same pattern as Story 1.2's play-test close-out). Story stays `in-progress` until then; flip to `review` once confirmed.

### File List

Added:
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` — Walk/Photo mode controller (enum state machine, `OnRaiseCamera` Send-Messages handler, viewfinder `CanvasGroup` fade, fail-soft).

Modified:
- `Assets/Input/InputSystem_Actions.inputactions` — added the `RaiseCamera` action (type `Value`) to the Player map + `<Mouse>/rightButton` and `<Gamepad>/leftTrigger` bindings. Existing actions unchanged.
- `Assets/Scenes/SampleScene.unity` — added `PhotoModeController` to `main characters` (`viewfinderRoot` assigned); added the `PhotoViewfinder` Canvas (`CanvasScaler`, `GraphicRaycaster`) with a `ViewfinderFrame` Image child.

### Change Log

| Date | Change |
|------|--------|
| 2026-06-05 | Story created (ready-for-dev). First runtime gameplay script of the project. Key guardrails captured: `RaiseCamera` must be a `Value` action (the Story 1.2 sticky-input lesson), `PhotoModeController` must sit on the `main characters` GameObject for Send-Messages to reach `OnRaiseCamera`, reuse the single FP rig per ADR-1, and defer zoom/`CameraConfig` to Story 1.4. |
| 2026-06-05 | Dev (in-progress): implemented Tasks 1–4 and the objective parts of Task 5. Added the `RaiseCamera` Value action + bindings; created `PhotoModeController` and the `PhotoViewfinder` overlay; wired the component onto `main characters` with the viewfinder reference. Clean compile (0 errors/0 warnings) and clean Play-mode console; verified live that the controller starts in Walk with the overlay hidden (CanvasGroup alpha 0). **One interactive gate left for Alexv:** the live RMB hold/release feel-test (MCP can't synthesize held input). Story stays `in-progress` pending that confirmation. |
| 2026-06-05 | Dev (close-out): **Alexv play-tested the RMB hold/release — works** (viewfinder shows on hold, hides on release, no stuck-on, movement unaffected). Task 5 complete; all ACs satisfied. **Status → review.** |
| 2026-06-16 | Code review (3 adversarial layers: Blind Hunter, Edge Case Hunter, Acceptance Auditor). **All 3 ACs satisfied; 0 decisions, 0 patches, 1 deferred, rest dismissed as noise.** The `Value`-type choice confirmed correct (the two "gamepad LT sticks" findings were false positives — that bug is specific to the `Button` *action type*, which `Value` fixes). Scene-placement risk dismissed: the passing RMB play-test empirically proves the component is on the PlayerInput object. **Status → done.** |

## Review Findings

_Code review 2026-06-16 — 3 adversarial layers, all ACs satisfied. See `deferred-work.md` for the deferred item's full context._

- [x] [Review][Defer] Viewfinder `CanvasGroup.blocksRaycasts` / `interactable` are set to `false` once in `Awake` and never flipped back to `true` in Photo mode [Assets/Scripts/PhotoMode/PhotoModeController.cs:65-66] — deferred. Harmless now (the viewfinder is an intentionally non-interactive placeholder overlay, `raycastTarget` off). Becomes relevant only when the overlay must capture input — i.e. **Story 1.5 (capture)**. Address there.

### Dismissed (not actionable — recorded for transparency)
- **"Gamepad LT sticks in Photo mode" (Blind + Edge)** — false positive. The sticky-release bug is specific to the `Button` *action type* under Send Messages; `RaiseCamera` is type `Value`, which delivers both press and release (trigger → 0 on release). `Value` is the correct fix and is applied. *(Recommend a 30-sec gamepad LT play-test for completeness, since only RMB was tested — but no code change is warranted.)*
- **"Component placement on `main characters` unverified" (Auditor)** — resolved by logic: the passing RMB play-test proves correct placement (else `OnRaiseCamera` would never fire).
- **"No magic-string `GameConstants` reference" (Auditor)** — no magic string exists; the Send-Messages handler name `OnRaiseCamera` is a literal Unity requires, and the derivation is already documented in a code comment. Intent honored.
- **`timeScale == 0` pause freezes the fade (Edge)** — no pause feature exists; speculative.
- **Cross-map `<Mouse>/rightButton` double-dispatch, no `enabled` guard, external `SetActive(false)` freezes fade, `Update` runs at target, `CanvasGroup` on non-Canvas silent, state flips when `_viewfinderReady` false** — all theoretical, out-of-scope, or documented fail-soft by design.
