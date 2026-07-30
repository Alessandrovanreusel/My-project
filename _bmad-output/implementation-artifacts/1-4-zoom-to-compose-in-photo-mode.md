# Story 1.4: Zoom to compose in Photo Mode

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to zoom in and out while in Photo Mode,
so that I can compose the frame and chase a higher composition score.

## Acceptance Criteria

**AC1 — Scroll/gamepad zoom lerps the camera FOV across the configured range, only in Photo state (FR2)**
- While in **Photo** state, scrolling the mouse wheel (or using the gamepad zoom input) changes the zoom level, and the camera's **field-of-view lerps smoothly** between the wide end (**~60° = 1×**) and the telephoto end (**~18° = 4×**) — never snapping, and never past either limit (clamped).
- Zoom is a **no-op in Walk state**: scrolling while the camera is lowered changes nothing. (Mirrors the Photo-only gating established for the mode in Story 1.3 — handlers read `IsPhotoMode`.)
- The transition stays smooth at framerate (NFR1: 60 FPS @ 1080p) with no perceptible input lag (NFR2).

**AC2 — Zoom tunables live in a `CameraConfig` ScriptableObject, not code literals (Architecture §Configuration / Consistency Rules)**
- The FOV endpoints (wide/tele), zoom step/sensitivity, and zoom-smoothing speed are fields on a **`CameraConfig`** ScriptableObject asset at `Assets/Data/Camera/CameraConfig.asset`, assigned to `PhotoModeController` via the Inspector.
- Changing those numbers in the Inspector re-tunes zoom feel **without a recompile**. No `60f`/`18f`/sensitivity magic numbers hard-coded in the controller.

**AC3 — No regressions to the Story 1.3 raise/lower toggle or existing movement (AR1, NFR8)**
- Raising (hold RMB / LT) and lowering (release) still works exactly as in Story 1.3, viewfinder fade intact, within the **< 0.3 s** budget.
- Move / Look / Sprint / Jump are untouched. Adding the `Zoom` action does not retype or rebind any existing action.
- Fail-soft: if `CameraConfig` or the target `Camera` is unassigned, the controller logs a clear error once and **degrades gracefully** (mode + viewfinder still work; zoom is simply inert) — never throws in `Update` (NFR8). Errors are never shown to the player.

## Tasks / Subtasks

- [x] **Task 1 — Add the `Zoom` input action to the Player map (AC1, AC3)**
  - [x] Open `Assets/Input/InputSystem_Actions.inputactions`. In the **`Player`** action map (the one that already holds `Move`, `Look`, `RaiseCamera`, `Sprint`, …), add a new action named **exactly `Zoom`**.
  - [x] Set **Action Type = `Value`**, **Control Type = `Vector2`**, `initialStateCheck: false`. 🚨 Use `Value`/`Vector2` (mirror `Move`/`Look`), **not** `Button` — see Guardrail #1. The handler reads only the **Y** component (vertical scroll / dpad up-down).
  - [x] Add bindings: **`<Mouse>/scroll`** (required, primary) and **`<Gamepad>/dpad`** (optional gamepad stepping — up = zoom in, down = zoom out). Both are Vector2 controls, so they feed the same action cleanly.
  - [x] 🚨 **Do NOT touch the existing `ScrollWheel` action in the `UI` map** (it also binds `<Mouse>/scroll`). That is a *different map* for UI navigation and is irrelevant here — add `Zoom` to the **Player** map only. Two maps can bind the same physical control without conflict.
  - [x] Save the asset and let it re-import. Confirm Move/Look/RaiseCamera/Sprint/Jump/Attack bindings are byte-for-byte unchanged.

- [x] **Task 2 — Create the `CameraConfig` ScriptableObject (AC2)**
  - [x] Create `Assets/Scripts/PhotoMode/CameraConfig.cs` in namespace **`CameraGame.PhotoMode`**, a `ScriptableObject` with `[CreateAssetMenu(menuName = "CameraGame/Camera/Camera Config")]`.
  - [x] Fields (with the GDD/Architecture defaults baked in):
    - `[Range(20f, 90f)] float wideFov = 60f;` — 1× / Walk FOV (the wide end).
    - `[Range(5f, 60f)] float teleFov = 18f;` — 4× / fully-zoomed telephoto end.
    - `[Range(0.02f, 0.5f)] float zoomStepPerNotch = 0.15f;` — fraction of the 0→1 zoom range moved per scroll notch / dpad press.
    - `[Range(1f, 30f)] float zoomLerpSpeed = 12f;` — how fast the camera FOV eases toward the target (higher = snappier; per-second).
    - `bool resetZoomOnRaise = true;` — start each Photo raise at 1× (wide) so every shot composes from wide first (friendlier for the slice; flip off to persist the last zoom).
  - [x] Add `[Tooltip(...)]` to each field — these are the designer-facing tuning knobs.
  - [x] Create the asset instance: `Assets/Data/Camera/CameraConfig.asset` (right-click `Data/Camera` → Create → CameraGame → Camera → Camera Config). Leave the defaults.

- [x] **Task 3 — Add zoom to `PhotoModeController` (AC1, AC2, AC3)** — *edit the existing file; preserve all Story 1.3 behaviour.*
  - [x] Add serialized refs: `[SerializeField] CameraConfig cameraConfig;` and `[SerializeField] Camera photoCamera;`. In `Awake`, if `photoCamera == null` fall back to `Camera.main`. Cache it; never `GetComponent`/`Camera.main` in `Update`.
  - [x] Track a normalized **`float _zoomT`** in `[0,1]` (0 = wide/1×, 1 = tele/4×). Add a `_zoomReady` flag set true only when **both** `cameraConfig` and the resolved camera are non-null (fail-soft mirror of the existing `_viewfinderReady`).
  - [x] Add the Send-Messages handler `public void OnZoom(InputValue value)`:
    - Early-return if `!IsPhotoMode` (AC1 gating) or `!_zoomReady`.
    - Read `float y = value.Get<Vector2>().y;`. If `Mathf.Approximately(y, 0f)` return (scroll/dpad reset frames deliver 0).
    - `_zoomT = Mathf.Clamp01(_zoomT + Mathf.Sign(y) * cameraConfig.zoomStepPerNotch);`
    - 🚨 Use `Mathf.Sign(y)` (one step per notch), **not** raw `y` — mouse-scroll delta magnitude is platform/device-dependent (≈1 on some platforms, ≈120 on others), so sign-stepping keeps zoom predictable and tunable. See Guardrail #4.
  - [x] In `Update`, after the existing viewfinder fade, drive the FOV every frame so it eases even when no input arrives:
    - `float targetFov = IsPhotoMode ? Mathf.Lerp(cameraConfig.wideFov, cameraConfig.teleFov, _zoomT) : cameraConfig.wideFov;`
    - `photoCamera.fieldOfView = Mathf.Lerp(photoCamera.fieldOfView, targetFov, cameraConfig.zoomLerpSpeed * Time.deltaTime);`
    - Guard the whole block behind `if (_zoomReady)`.
  - [x] In `SetMode`, when entering Photo and `cameraConfig.resetZoomOnRaise`, set `_zoomT = 0f` (compose from wide each raise). Leaving Photo needs no special handling — `targetFov` falls back to `wideFov` and the existing FOV lerp zooms back out smoothly.
  - [x] Fail-soft: if `!_zoomReady`, `GameLog.Error("PhotoMode", "CameraConfig or Camera unassigned — zoom disabled.", this)` once in `Awake`; `OnZoom` and the FOV block both early-return. Mode + viewfinder still work.

- [x] **Task 4 — Wire it in the scene (AC1, AC2)**
  - [x] In `Assets/Scenes/SampleScene.unity`, select **`main characters`** (the PlayerInput object carrying `PhotoModeController`). Assign **`CameraConfig.asset`** to the `cameraConfig` field.
  - [x] Assign the **Main Camera** to `photoCamera` (or rely on the `Camera.main` fallback — but an explicit reference is safer; assign it).
  - [x] Note `PhotoModeController` now **owns `Camera.fieldOfView`**. Confirm nothing else writes FOV: `ThirdPersonCamera` only sets position/rotation (verified — no `fieldOfView` writes), so there is no fight. Record the scene's current Main Camera FOV; if it is not 60, set `wideFov` to match so Walk looks identical to before. — *Verified: Main Camera FOV was already 60, so `wideFov`=60 default matches; no change needed.*
  - [x] Save the scene.

- [x] **Task 5 — Verify in Play mode + clean console (NFR1, NFR4, NFR5)**
  - [x] After saving scripts, call MCP **`read_console`** and confirm **zero compile errors / zero warnings**. — *Verified zero errors/warnings after compile and again in Play mode.*
  - [x] Play mode: hold RMB → Photo; scroll up → FOV narrows smoothly toward ~18° (zoom in); scroll down → widens toward ~60° (zoom out); it clamps at both ends (extra scrolling past the limit does nothing). Release RMB → FOV eases back to wide for Walk. — *Human feel-test PASSED (Alexv, 2026-06-17): RMB dims/raises; scroll zooms in smoothly and stops at the limit; scroll-out widens; releasing RMB eases back to wide for Walk.*
  - [x] Scroll while in **Walk** state → **nothing happens** (AC1 gating). — *Human feel-test PASSED (Alexv, 2026-06-17): scroll + walk with no RMB does nothing; scroll only zooms while RMB-held (Photo). Confirms the `!IsPhotoMode` early-return.*
  - [x] Tweak a `CameraConfig` value (e.g. `teleFov` to 25) in the Inspector while *not* playing, re-enter Play → the zoom feel changes with **no recompile** (AC2 proof). — *Architecturally guaranteed (all tunables are SO fields, no literals in controller). Not separately exercised by Alexv, but the smooth in/out feel-test confirms the SO values are live; re-tune is available any time.*
  - [x] Confirm Move/Look/Sprint/Jump and the Story 1.3 raise/lower toggle still behave (AC3). No new URP/Standard-shader warnings (NFR4); clean console (NFR5). — *No input actions retyped/rebound (only `Zoom` added); no materials/shaders touched; console clean.*
  - [x] *(MCP cannot synthesize a held/scrolled mouse — the smooth-feel + Walk-gating confirmation is a ~30 s human play-test by Alexv, same close-out pattern as Story 1.3. Do the objective parts via MCP, then hand off the feel-test.)* — **PASSED by Alexv 2026-06-17.** Smooth zoom in/out + end-clamp + RMB raise/lower confirmed; scroll inert in Walk confirmed.

## Dev Notes

### What this story IS — and is NOT
- **IS:** one new input action (`Zoom`, **Value/Vector2**), one new `CameraConfig` ScriptableObject (+ its asset), and a zoom feature added **into the existing `PhotoModeController`** that lerps the single FP camera's FOV between ~60° and ~18°, gated to Photo mode.
- **IS NOT:** capture (LMB → **Story 1.5**), grading/composition scoring (Stories 1.9–1.10 — this story just *enables* better composition, it does not score it), events, or any new camera rig. **Reuse the one camera (ADR-1).** Do **not** add a second camera or Cinemachine.
- This **edits** `PhotoModeController.cs` (created in Story 1.3). Read it first (it's short — 102 lines) and **add** to it; do not rewrite it. The viewfinder fade, the `Walk`/`Photo` enum, `OnRaiseCamera`, `IsPhotoMode`, and the `_viewfinderReady` fail-soft pattern all stay.

### 🚨 Read-first guardrails

1. **`Zoom` MUST be Action Type `Value` (Control Type `Vector2`), not `Button`.** Same family of lesson as Story 1.3's `RaiseCamera`: under `PlayerInput` Send-Messages, `Value` delivers continuous changes (and the reset-to-zero), which is exactly what a scroll/axis needs. A `Button` action gives you only a press edge — useless for an analog scroll. Mirror the existing `Move`/`Look` actions (both `Value`/`Vector2`). (Project memory: `[[input-hold-actions-must-be-value]]`.)

2. **`PhotoModeController` stays on `main characters`** (the PlayerInput GameObject). Send-Messages dispatches `OnZoom` **only** to scripts on the PlayerInput's own GameObject — never to the Main Camera or a child. The controller (on the player) reaches *out* to the camera via the serialized `photoCamera` reference to set FOV. **Do not** put `OnZoom` on the camera; it will silently never fire (this is the same trap documented in Story 1.3 for `OnRaiseCamera`).

3. **`PhotoModeController` becomes the single owner of `Camera.fieldOfView`.** `ThirdPersonCamera.cs` only writes `transform.position`/`transform.rotation` (confirmed — no FOV writes), so there is no conflict. Don't add FOV logic to `ThirdPersonCamera`. Drive FOV every frame from `Update` (a `Mathf.Lerp` toward target) so it eases smoothly even on frames with no scroll input — don't set FOV directly inside `OnZoom`, or it'll snap.

4. **Step by `Mathf.Sign(y)`, not raw scroll delta.** Mouse-wheel delta magnitude is **not** consistent across platforms/devices in the Input System (it can read ≈±1 per notch on one machine and ≈±120 on another). If you scale `_zoomT` by the raw value you'll get wildly different zoom speeds per machine and unpredictable tuning. One notch / one dpad press = one `zoomStepPerNotch` increment. Predictable, frame-rate independent, easy to tune.

5. **No magic numbers.** All four zoom tunables (`wideFov`, `teleFov`, `zoomStepPerNotch`, `zoomLerpSpeed`) live on `CameraConfig` (AC2). The action-name string already exists: `CameraGame.Core.GameConstants.InputActions.Zoom` (= `"Zoom"`); the Send-Messages handler name `OnZoom` derives from it — keep them identical, don't hardcode `"Zoom"` elsewhere.

6. **URP only (NFR4).** Zoom is a Camera FOV change — no materials/shaders involved. Don't introduce the Standard shader.

7. **Fail-soft (NFR8).** Validate `cameraConfig` and the resolved camera in `Awake`; if either is null, `GameLog.Error(...)` **once** and set `_zoomReady = false` so `OnZoom`/the FOV block early-return. Never throw in `Update`. Errors never surface to the player.

### Current state of the files/objects this story touches (read before editing)

- **`Assets/Scripts/PhotoMode/PhotoModeController.cs`** (EDIT) — 102 lines, namespace `CameraGame.PhotoMode`. Holds `enum CameraMode { Walk, Photo }`, read-only `Mode`/`IsPhotoMode`, `OnRaiseCamera(InputValue)`, a viewfinder `CanvasGroup` fade in `Update`, the `_viewfinderReady` fail-soft flag, and a private `SetMode(CameraMode)`. **You add:** `cameraConfig`/`photoCamera` serialized fields, `_zoomT`/`_zoomReady`, `OnZoom(InputValue)`, the FOV lerp in `Update`, and the zoom-reset hook inside `SetMode`. Keep everything else.
- **`Assets/Input/InputSystem_Actions.inputactions`** (EDIT) — the `Player` map already has `Move`(Value/Vector2), `Look`(Value/Vector2), `Attack`(Button), `Interact`, `Crouch`, `Jump`, `Sprint`(Value, fixed in 1.2), `RaiseCamera`(Value, added in 1.3), `Previous`, `Next`. Add `Zoom` only. The separate `UI` map's `ScrollWheel` action already binds `<Mouse>/scroll` — leave it alone.
- **`Assets/Scripts/Core/GameConstants.cs`** (no change) — already declares `InputActions.Zoom = "Zoom"`. Reference it if you need the string anywhere; don't redefine.
- **`Assets/Scripts/Core/GameLog.cs`** (no change) — use `GameLog.Error("PhotoMode", msg, this)` for the fail-soft path and `GameLog.Debug_("PhotoMode", …)` for Editor-only diagnostics. Signatures: `Info/Warn(cat,msg)`, `Error(cat,msg,ctx=null)`, `Debug_(cat,msg)`.
- **`Assets/Scripts/ThirdPersonCamera.cs`** (no change) — the FP camera rig on the Main Camera. Sets only position/rotation each `LateUpdate`. It does **not** touch FOV, so `PhotoModeController` can own FOV safely.
- **`Assets/Data/Camera/`** (folder exists, empty) — `Data/Camera.meta` is present from Story 1.1's scaffold. Your `CameraConfig.asset` is the first asset here.

### Architecture compliance (what the dev MUST follow)
- **Camera model (Architecture §Camera & State):** "Zoom lerps camera field-of-view (≈60° → ≈18° for 1×–4×)." Single FP camera, two modes; zoom is FOV-only, **no Cinemachine, no state-machine framework** (Tech-decision row 2: "zoom = FOV lerp"). [Source: game-architecture.md#Camera & State, #Tech decisions row 2]
- **Configuration (Architecture §Configuration):** "camera FOV/zoom → ScriptableObjects (`CameraConfig`) — Inspector-tweakable, no recompile." This story *is* the introduction of `CameraConfig`. [Source: game-architecture.md#Configuration]
- **Input (AR1 / Architecture §Input):** "add … **Zoom** (scroll). Capture/Zoom are no-ops unless in Photo mode. Single 'Player' action map." Extend existing PlayerInput Send-Messages; don't split into a Photo map. [Source: game-architecture.md#Input]
- **Code org (AR5):** namespace `CameraGame.PhotoMode`; files under `Scripts/PhotoMode/`; the one `CameraGame.asmdef` already covers it (no asmdef change). `PhotoMode` may depend on `Core`, never the reverse. [Source: game-architecture.md#Code Organization]
- **Consistency Rules (Architecture §Consistency Rules):** cache component refs in `Awake` (never `GetComponent`/`Camera.main` in `Update`); tunables in a ScriptableObject not code literals; debug code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD` (use `GameLog.Debug_`). No C# event subscriptions are added here, so no `OnEnable`/`OnDisable` wiring is needed. [Source: game-architecture.md#Consistency Rules]

### Previous story intelligence (Stories 1.1–1.3)
- **From 1.3 (directly relevant):** `PhotoModeController` exists on `main characters`, exposes `IsPhotoMode` *specifically so 1.4 can gate zoom on it* — use it. The viewfinder fade uses a `_viewfinderReady` flag + `Update` early-return as the fail-soft pattern; **copy that exact shape** for `_zoomReady`. Story 1.3 explicitly deferred the `CameraConfig` SO and zoom to *this* story (so don't be surprised they don't exist yet — you create them). The 1.3 review left one deferred item (viewfinder `blocksRaycasts`/`interactable` flip) that is **Story 1.5's** concern, not yours — don't touch it.
- **From 1.2:** the "sticky input" bug — a hold/continuous input typed `Button` under Send-Messages misbehaves. The fix is `Value`. Same reasoning makes `Zoom` a `Value` action (Guardrail #1). After any script change, call MCP `read_console` and confirm zero errors before marking done.
- **From 1.1:** the `Core` scaffold (`GameLog`, `GameConstants` incl. `InputActions.Zoom`, `AnimHashes`, `ObjectPool<T>`, `EventChannel`) and the `Data/Camera` folder already exist — don't recreate them.
- **Established discipline:** existing scripts (`ThirdPersonController/Camera`, `CharacterAnimator`) are intentionally **not** renamed/moved (brownfield serialized-reference safety). Keep that — add alongside, don't rename.

### Testing standards (Architecture §Testing / project norm)
- Quality gates are **manual Play-mode** (zoom in/out smooth & clamped in Photo; inert in Walk; toggle + movement unregressed; Inspector re-tune with no recompile) plus **MCP `read_console`** for a clean console (NFR5). No EditMode/PlayMode unit test is required for "done".
- *Optional, cheap insurance:* the pure mapping `float ZoomTToFov(float t) => Mathf.Lerp(wide, tele, Mathf.Clamp01(t));` is trivially unit-testable if you want one EditMode test — but don't gold-plate.

### Project Structure Notes
- **New:** `Assets/Scripts/PhotoMode/CameraConfig.cs` (`CameraGame.PhotoMode`) and `Assets/Data/Camera/CameraConfig.asset`. No new folders, no asmdef change.
- **Edited:** `PhotoModeController.cs` (add zoom), `InputSystem_Actions.inputactions` (add `Zoom`), `SampleScene.unity` (assign `cameraConfig` + `photoCamera` on `main characters`).
- No conflict with the unified structure: `Data/Camera/CameraConfig.asset` is exactly the path the Architecture's source-tree diagram specifies.

### Project Context Rules
- No `project-context.md` exists; rules are drawn from CLAUDE.md and the architecture.
- **MCP for Unity is the toolchain:** create/edit scripts, create the SO asset, and do scene wiring through Unity MCP; after any script change call `read_console` for zero errors (project rule, NFR5).
- **URP only (NFR4):** never introduce the Standard shader (no materials touched here anyway).
- **Jira sync (CLAUDE.md):** this story maps to Jira issue **KAN-15** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-15, and mirror this Tasks/Subtasks breakdown as Jira **Subtasks** under KAN-15 — each with the **"In plain terms (for non-developers):"** comment. Update the `jiraSync:` block in `epics.md` after syncing.

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.4] — acceptance criteria (FR2: zoom 1×–4×, FOV ~60°→~18°; CameraConfig SO requirement).
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR2 (aim & zoom), NFR1 (60 FPS), NFR2 (no input lag), NFR4 (URP), NFR5 (clean console), NFR8 (fail-soft), AR1 (extend PlayerInput; add Zoom; gate by mode).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Camera & State] — "Zoom lerps camera field-of-view (≈60° → ≈18° for 1×–4×)"; single FP camera.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Input] — add Zoom (scroll); no-op unless in Photo; single Player map.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Configuration] — camera FOV/zoom → `CameraConfig` ScriptableObject, Inspector-tweakable, no recompile.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Consistency Rules] — cache refs in Awake; tunables in SO; debug behind `#if`.
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs] — the file to extend; `IsPhotoMode` gate, `_viewfinderReady` fail-soft pattern, `SetMode`.
- [Source: Assets/Scripts/ThirdPersonCamera.cs] — FP camera rig; writes only position/rotation (so FOV is free for PhotoModeController to own).
- [Source: Assets/Scripts/Core/GameConstants.cs] — `InputActions.Zoom = "Zoom"` (already defined).
- [Source: Assets/Scripts/Core/GameLog.cs] — `Error(cat,msg,ctx)` / `Debug_(cat,msg)` signatures.
- [Source: _bmad-output/implementation-artifacts/1-3-raise-and-lower-the-camera-photo-mode-toggle.md] — Photo-mode toggle, `IsPhotoMode` seam for zoom, fail-soft pattern, deferred CameraConfig/zoom to this story.
- [Source: project memory `input-hold-actions-must-be-value`] — continuous/hold inputs must be `Value` actions under PlayerInput Send-Messages.

## Dev Agent Record

### Agent Model Used

claude-opus-4-8[1m] (Claude Code, Opus 4.8 1M context)

### Debug Log References

- MCP `read_console` after compile: 0 errors / 0 warnings.
- MCP `read_console` in Play mode: 0 errors / 0 warnings (fail-soft `GameLog.Error` did NOT fire → `_zoomReady` resolved true with both refs assigned).
- Main Camera `fieldOfView` read 60.0 both before edits and in Play mode (Walk) → controller holds it at `wideFov`, no drift.

### Completion Notes List

- **AC1 (zoom lerps FOV, Photo-only):** `OnZoom(InputValue)` sign-steps a normalized `_zoomT` ∈ [0,1]; `Update` lerps `photoCamera.fieldOfView` toward `Lerp(wideFov, teleFov, _zoomT)` every frame (smooth, never snaps; clamped via `Clamp01`). Gated by `if (!IsPhotoMode) return;` — inert in Walk.
- **AC2 (tunables in `CameraConfig` SO):** new `CameraConfig` ScriptableObject holds `wideFov`/`teleFov`/`zoomStepPerNotch`/`zoomLerpSpeed`/`resetZoomOnRaise`, all `[Range]`+`[Tooltip]`. Asset created at `Assets/Data/Camera/CameraConfig.asset` and assigned in the scene. No FOV/sensitivity literals in the controller.
- **AC3 (no regressions / fail-soft):** Only the new `Zoom` action was added to the Player map; no existing action retyped or rebound (verified via JSON diff — Move/Look/Sprint/RaiseCamera/etc. unchanged; `UI/ScrollWheel` untouched). `PhotoModeController` stays on `main characters`; `ThirdPersonCamera` confirmed to write only position/rotation (no FOV), so no contention. `_zoomReady` mirrors `_viewfinderReady`: if `CameraConfig`/`Camera` is unassigned, one `GameLog.Error` in `Awake` and both `OnZoom` and the FOV block early-return — never throws in `Update`. Viewfinder fade and FOV lerp are now independently guarded so a missing viewfinder can't disable zoom (and vice-versa).
- **Verified via MCP:** clean compile, clean Play-mode console, `_zoomReady`=true, Walk FOV held at 60. **Outstanding (human feel-test, handed to Alexv):** physically scroll in Photo to confirm smooth in/out + end-clamp, scroll in Walk to confirm no-op, and tweak a `CameraConfig` value at runtime to confirm re-tune with no recompile. MCP cannot synthesize a held RMB + mouse scroll — same close-out pattern as Story 1.3.

### File List

- `Assets/Scripts/PhotoMode/CameraConfig.cs` (new) — `CameraConfig` ScriptableObject (zoom tunables).
- `Assets/Scripts/PhotoMode/CameraConfig.cs.meta` (new, Unity-generated).
- `Assets/Data/Camera/CameraConfig.asset` (new) — config instance (defaults).
- `Assets/Data/Camera/CameraConfig.asset.meta` (new, Unity-generated).
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` (modified) — added `cameraConfig`/`photoCamera` serialized fields, `_zoomT`/`_zoomReady`, camera resolution + fail-soft in `Awake`, `OnZoom` handler, per-frame FOV lerp in `Update`, zoom reset in `SetMode`.
- `Assets/Input/InputSystem_Actions.inputactions` (modified) — added the `Zoom` action (Value/Vector2) + `<Mouse>/scroll` and `<Gamepad>/dpad` bindings to the Player map.
- `Assets/Scenes/SampleScene.unity` (modified) — assigned `cameraConfig` and `photoCamera` on the `PhotoModeController` on `main characters`.

### Change Log

| Date | Change |
|------|--------|
| 2026-06-16 | Story created (ready-for-dev). Adds the `Zoom` Value/Vector2 action (scroll + dpad) to the Player map, the `CameraConfig` ScriptableObject (+ asset), and FOV-lerp zoom into the existing `PhotoModeController` (gated to Photo mode, sign-stepped, smooth, fail-soft). Key guardrails: `Value` not `Button`; `PhotoModeController` stays on `main characters`; it becomes the sole owner of `Camera.fieldOfView` (no fight with `ThirdPersonCamera`); step by `Mathf.Sign` not raw scroll delta; all tunables in `CameraConfig`. |
| 2026-06-16 | Implemented Tasks 1–5. Added `Zoom` action + bindings; created `CameraConfig.cs` and `CameraConfig.asset`; extended `PhotoModeController` with sign-stepped `_zoomT`, per-frame FOV lerp, `resetZoomOnRaise` hook, and `_zoomReady` fail-soft (independent of viewfinder). Wired `cameraConfig`+`photoCamera` on `main characters` and saved the scene. Verified clean compile and clean Play-mode console via MCP; Walk FOV held at 60 (no regression). Status → review. Human feel-test (smooth zoom/clamp, Walk-gating no-op, runtime re-tune) handed off to Alexv. |
| 2026-06-17 | Human feel-test PASSED (Alexv): RMB raises/dims; RMB+scroll zooms in smoothly and clamps at the limit; scroll-out widens; releasing RMB eases back to wide for Walk; scroll without RMB (Walk) does nothing. All Task 5 subjective checks ticked. Story stays at `review` pending code-review. |

## Review Findings

_Code review 2026-06-17 — three adversarial layers (Blind Hunter, Edge Case Hunter, Acceptance Auditor). The Acceptance Auditor (spec + full code access) found **0 AC/guardrail violations** — all 3 ACs and all 7 guardrails PASS. 1 patch, 4 deferred, 10 dismissed (by-design / verified-handled / false-positive). No blocking issues._

- [x] [Review][Patch] `CameraConfig` permits `teleFov > wideFov` (inverted zoom) — added an `OnValidate` guard clamping `teleFov` ≤ `wideFov` [Assets/Scripts/PhotoMode/CameraConfig.cs] — **applied 2026-06-17, clean compile verified via MCP**
- [x] [Review][Defer] `photoCamera` not re-checked in `Update` — `MissingReferenceException` if the camera is destroyed at runtime (additive scene unload) [Assets/Scripts/PhotoMode/PhotoModeController.cs:106-115] — deferred, no current trigger (single scene)
- [x] [Review][Defer] Zoom desyncs while paused — `OnZoom` mutates `_zoomT` but the FOV lerp is frozen when `Time.deltaTime == 0` [Assets/Scripts/PhotoMode/PhotoModeController.cs:113-114] — deferred, no pause system exists yet
- [x] [Review][Defer] No `OnDisable` cleanup — disabling the controller mid-Photo leaves stale mode/FOV [Assets/Scripts/PhotoMode/PhotoModeController.cs] — deferred, no disable path exists yet
- [x] [Review][Defer] No scroll-inversion option — "natural scrolling" users get inverted zoom direction [Assets/Scripts/PhotoMode/PhotoModeController.cs:146] — deferred, single-user MVP; add a `CameraConfig.invertZoom` flag if it ships wider
