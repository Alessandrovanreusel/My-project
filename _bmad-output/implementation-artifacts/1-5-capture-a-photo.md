# Story 1.5: Capture a photo

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to click to capture a photo while in Photo Mode,
so that I can attempt to catch the moment.

## Acceptance Criteria

**AC1 — Capture in Photo state fires fast feedback + raises `ShotCapturedChannel` (FR3, NFR2)**
- While in **Photo** state, pressing **Capture** (LMB / gamepad RT) takes a shot: a **shutter SFX + a visual flash** occur within **< 0.2 s** of the press (capture-to-feedback budget, NFR2).
- Capture raises a **`ShotCapturedChannel`** event carrying a **placeholder `ShotGrade`** (real grading lands in Stories 1.9–1.10; this story only wires the event + payload type).
- The feedback and event fire **synchronously** in the input handler — no coroutine delay, no async, no GPU readback (keeps it inside the < 0.2 s budget).

**AC2 — Capture is a no-op in Walk state; the existing `Attack` action is repurposed (AR1, FR3)**
- The existing **`Attack`** action is **renamed/repurposed to `Capture`** (LMB + gamepad RT). No new second action — reuse the one in the Player map.
- Pressing Capture while in **Walk** state (camera lowered) does **nothing** — same Photo-only gating as Zoom (Story 1.4): the handler reads `IsPhotoMode`.
- Adding Capture does **not** retype or rebind any other existing action (Move/Look/Sprint/Jump/RaiseCamera/Zoom unchanged).

**AC3 — Fail-soft + no regressions (NFR8, NFR4, NFR5)**
- If the channel, SFX, or flash references are unassigned, the controller logs a **clear error once in `Awake`** and **degrades gracefully** — capture never throws in `Update`/the handler (NFR8). Each piece of feedback is independently guarded (a missing shutter clip must not disable the flash or the event, and vice-versa — same independent-guard lesson as Story 1.4).
- The Story 1.3 raise/lower toggle, the Story 1.4 zoom, and Move/Look/Sprint/Jump all still behave exactly as before. URP only — no Standard shader (NFR4). Console stays clean — zero errors/warnings (NFR5).

## Tasks / Subtasks

- [x] **Task 1 — Repurpose the `Attack` action to `Capture` (AC1, AC2)**
  - [x] Open `Assets/Input/InputSystem_Actions.inputactions`. In the **`Player`** map, **rename** the existing action **`Attack` → `Capture`** (the `OnCapture` Send-Messages handler derives its name from this string — it must be exactly `Capture`, matching `GameConstants.InputActions.Capture` which already = `"Capture"`).
  - [x] **Keep Action Type = `Button`** (`expectedControlType: Button`). 🚨 Capture is a **discrete tap** (one shot per press) — Button delivers exactly one `performed`/press message, which is what we want. This is the **opposite** of the hold-inputs rule (Sprint/RaiseCamera/Zoom are `Value` because they need continuous values + the release edge). See Guardrail #1.
  - [x] Bindings: **keep `<Mouse>/leftButton`** (LMB, primary). **Change the gamepad binding** from `<Gamepad>/buttonWest` to **`<Gamepad>/rightTrigger`** (RT, per AC1). **Remove** the `Attack` template's extra bindings (`<Touchscreen>/primaryTouch/tap`, `<Joystick>/trigger`, `<XRController>/{PrimaryAction}`, `<Keyboard>/enter`) — they aren't part of the PC + gamepad slice and an Enter-key "capture" is a surprising side effect. End state: Capture binds **LMB + gamepad RT only**.
  - [x] 🚨 Do **NOT** retype or rebind any other action. Save the asset and let it re-import; confirm Move/Look/Sprint/RaiseCamera/Zoom/Jump are byte-for-byte unchanged.

- [x] **Task 2 — Create the minimal `ShotGrade` result type (AC1)**
  - [x] Create `Assets/Scripts/Grading/ShotGrade.cs` in namespace **`CameraGame.Grading`**. This is the **shared result type** the channel carries; Stories 1.9–1.10 add the per-axis breakdown + the `ShotGrader` that produces real grades. **Keep it minimal — do not build grading logic here.**
  - [x] Define a `readonly struct ShotGrade` with: a normalized `Percent01` (`[0,1]`), a computed `Stars` (`1–5`, e.g. `Mathf.Clamp(Mathf.CeilToInt(Percent01 * 5), 1, 5)`), a `bool IsPlaceholder` flag, and static factories: `FromPercent(float p01)`, `Miss` (`FromPercent(0)`), and **`Placeholder`** (a clearly-temporary grade used by Story 1.5 until grading lands — set `IsPlaceholder = true`).
  - [x] Pure data only — no Unity component deps, no references to other feature folders (keeps the dependency graph clean so `Events` can reference it without a cycle).

- [x] **Task 3 — Create the `ShotCapturedChannel` event channel + asset (AC1)**
  - [x] Create `Assets/Scripts/Events/ShotCapturedChannel.cs` in namespace **`CameraGame.Events`**: `public class ShotCapturedChannel : EventChannel<ShotGrade>` with `[CreateAssetMenu(menuName = "CameraGame/Events/Shot Captured Channel", fileName = "ShotCapturedChannel")]`. 🚨 **Subclass the existing generic `EventChannel<T>` in `Core` — do not reinvent the raise/subscribe plumbing** (it already snapshots handlers, isolates per-handler exceptions, and clears subscribers on domain reload).
  - [x] Create the asset instance at `Assets/Data/Channels/ShotCapturedChannel.asset` (right-click `Data/Channels` → Create → CameraGame → Events → Shot Captured Channel). The folder already exists (empty).

- [x] **Task 4 — Create a `CaptureConfig` SO for feedback tunables (AC1, AC3)**
  - [x] Create `Assets/Scripts/PhotoMode/CaptureConfig.cs` in namespace **`CameraGame.PhotoMode`** (capture feedback is a PhotoMode concern), a `ScriptableObject` with `[CreateAssetMenu(menuName = "CameraGame/Camera/Capture Config", fileName = "CaptureConfig")]`. 🚨 **No magic numbers in the controller** — the 1.4 code review enforced exactly this rule; hard-coding `0.12f`/volume in `PhotoModeController` will be flagged.
  - [x] Fields (all `[Tooltip]`, ranges where sensible): `[Range(0.02f, 0.2f)] float flashDuration = 0.12f;` (kept under the 0.2 s budget, NFR2), `Color flashColor = Color.white;`, `[Range(0f, 1f)] float sfxVolume = 1f;`.
  - [x] Create the asset at `Assets/Data/Camera/CaptureConfig.asset` (alongside `CameraConfig.asset`). Leave defaults.

- [x] **Task 5 — Add capture to `PhotoModeController` (AC1, AC2, AC3)** — *edit the existing file; preserve all Story 1.3 + 1.4 behaviour.*
  - [x] Add serialized refs (new `[Header("Capture (Photo Mode)")]` block): `ShotCapturedChannel shotCapturedChannel;`, `CaptureConfig captureConfig;`, `AudioSource captureAudioSource;`, `AudioClip shutterClip;`, `CanvasGroup captureFlash;`.
  - [x] In `Awake`, fail-soft (mirror the existing `_zoomReady`/`_viewfinderReady` shape), with **independent** guards:
    - `_captureReady = shotCapturedChannel != null;` (the core capability — raising the event). One `GameLog.Error("PhotoMode", …, this)` if null.
    - `_shutterReady = captureAudioSource != null && shutterClip != null && captureConfig != null;` → SFX inert if false. *(Logged at `Info`, not `Warn`: leaving the clip null is an expected, supported state — keeps the console warning-free per NFR5.)*
    - `_flashReady = captureFlash != null && captureConfig != null;` → flash inert if false; init `captureFlash.alpha = 0f`, `blocksRaycasts = false`, `interactable = false`.
  - [x] Add the Send-Messages handler `public void OnCapture(InputValue value)`:
    - `if (!IsPhotoMode) return;` — AC2 no-op in Walk. *(If you keep the action as `Button`, one press = one call. If you ever switch it to `Value`, add `if (!value.isPressed) return;` to avoid a second capture on release.)*
    - Shutter: `if (_shutterReady) captureAudioSource.PlayOneShot(shutterClip, captureConfig.sfxVolume);` (synchronous — meets < 0.2 s).
    - Flash: `if (_flashReady) _flashT = 1f;` (decayed in `Update`, so the alpha pulse is frame-rate-eased rather than a one-frame pop).
    - Event: `if (_captureReady) shotCapturedChannel.Raise(ShotGrade.Placeholder);`
    - `GameLog.Info("PhotoMode", "Shot captured (placeholder grade).");` — milestone log; capture is user-driven/infrequent so this is **not** console spam.
  - [x] In `Update`, after the existing viewfinder + zoom blocks, fade the flash using the guarded `captureConfig.SafeFlashDuration` and `Time.unscaledDeltaTime`; tint via the assigned UI `Graphic` color = `captureConfig.flashColor` (set once in `Awake`). The flash pops to full immediately in `OnCapture`, then eases to 0 over `flashDuration`.
  - [x] 🚨 Do **NOT** call `ShotGrader`, `GradingConfig`, or `GalleryService` — those are Stories 1.9–1.11. This story raises a **placeholder** grade only. Keep all 1.3 viewfinder + 1.4 zoom code intact.

- [x] **Task 6 — Wire it in the scene (AC1)**
  - [x] In `Assets/Scenes/SampleScene.unity`, select **`main characters`** (the PlayerInput object carrying `PhotoModeController`). Assign `shotCapturedChannel` → the `.asset`, `captureConfig` → `CaptureConfig.asset`.
  - [x] **Audio:** add a **2D `AudioSource`** (`Spatial Blend = 0`, `Play On Awake = off`) to `main characters` and assign it to `captureAudioSource`. Added and assigned `Assets/Audio/ShutterPlaceholder.wav` as a temporary shutter SFX so AC1 has audible feedback until a final authored shutter clip replaces it.
  - [x] **Flash UI:** under the screen-space viewfinder/HUD Canvas (`PhotoViewfinder`), added a **full-screen white `Image`** named `CaptureFlash` (anchors stretched 0,0→1,1, raycastTarget off) with a `CanvasGroup` (`alpha = 0`, `Blocks Raycasts = off`, `Interactable = off`, `Ignore Parent Groups = on`). Assigned it to `captureFlash`. Default UI shader (URP-compatible — NFR4).
  - [x] Save the scene.

- [x] **Task 7 — Verify in Play mode + clean console (NFR2, NFR4, NFR5)**
  - [x] After saving scripts, call MCP **`read_console`** and confirm **zero compile errors / zero warnings**. *(Verified clean after every script change and after the play/stop cycle.)*
  - [x] Play mode: hold RMB → Photo; **click LMB → flash + shutter SFX within < 0.2 s**, console logs "Shot captured" once per click. **Human play-test by Alexv (2026-06-17) CONFIRMED:** RMB-hold + single LMB → one flash; repeated LMB clicks → one flash each (no double-capture). Review follow-up wired a temporary shutter SFX clip so capture is no longer silent.
  - [x] Click LMB in **Walk** state (no RMB held) → **nothing happens** (AC2 gating — `!IsPhotoMode` early-return). **Human play-test CONFIRMED:** LMB without RMB produces no flash and no log.
  - [x] Confirm no new action retyped/rebound (grep confirms only `Capture` exists, all other actions untouched); no materials/shaders touched (NFR4 — flash uses the default UI shader); console clean (NFR5). Story 1.3 raise/lower + 1.4 zoom code left fully intact (verified by re-reading the controller).
  - [x] *(MCP cannot synthesize a held-RMB + LMB-click. The objective parts went through MCP; the "flash feels instant / no double-capture / channel raises on click / no-op in Walk / 1.3-1.4 unregressed" confirmation is a ~30 s human play-test by Alexv — same close-out pattern as Stories 1.3 & 1.4.)*

### Review Findings

- [x] [Review][Patch] Flash alpha is not applied synchronously in `OnCapture` [Assets/Scripts/PhotoMode/PhotoModeController.cs:242] — `OnCapture` only sets `_flashT`; visible alpha waits for `Update`, which weakens AC1's "synchronously in the input handler" guarantee.
- [x] [Review][Patch] Flash fade duration is not runtime-guarded [Assets/Scripts/PhotoMode/PhotoModeController.cs:189; Assets/Scripts/PhotoMode/CaptureConfig.cs:17] — `[Range]` constrains Inspector editing, but direct asset/code changes can still leave `flashDuration` invalid and cause bad fade behavior or division by zero.
- [x] [Review][Patch] Flash can be considered ready without a visible flash graphic [Assets/Scripts/PhotoMode/PhotoModeController.cs:125] — `_flashReady` does not require a visible `Image`/graphic, so a miswired `CanvasGroup` can pass setup while producing no visual feedback.
- [x] [Review][Patch] `ShotGrade` accepts `NaN` despite its normalized `[0,1]` contract [Assets/Scripts/Grading/ShotGrade.cs:28] — `Mathf.Clamp01` does not make `NaN` safe, so future grader bugs could propagate invalid grade payloads.
- [x] [Review][Decision] Shutter SFX is not actually wired — AC1 requires shutter SFX + visual flash within < 0.2 s, but `SampleScene.unity` leaves `shutterClip: {fileID: 0}` and no audio clip exists under `Assets`; choose whether to add a temporary placeholder clip now or keep this as deferred content. **Resolved:** Alexv chose to add a temporary placeholder shutter clip now; `Assets/Audio/ShutterPlaceholder.wav` is wired into `shutterClip`.
- [x] [Review][Patch] Capture flash is dimmed by the parent viewfinder fade [Assets/Scenes/SampleScene.unity:501159; Assets/Scripts/PhotoMode/PhotoModeController.cs:242] — `CaptureFlash` is parented under `PhotoViewfinder`, so capturing immediately after raising the camera can set the child flash alpha to 1 while the parent CanvasGroup is still near 0.

## Dev Notes

  ### What this story IS — and is NOT
- **IS:** repurpose the existing `Attack` action to **`Capture`** (LMB + gamepad RT), add an `OnCapture` handler to the **existing `PhotoModeController`** (gated to Photo), play a **shutter SFX + flash** in < 0.2 s, and **raise `ShotCapturedChannel`** with a **placeholder `ShotGrade`**. Create the three small new types the event needs: `ShotGrade` (minimal), `ShotCapturedChannel`, `CaptureConfig`.
- **IS NOT:** real grading (subject gate / composition / timing → **Stories 1.9–1.10**), the gallery / shot persistence (**Story 1.11**), the grade-feedback HUD (**Story 1.12**), events/actors (**1.6–1.7**), or any new camera rig. **Reuse the one camera (ADR-1)** and the one `CameraGame.asmdef` (no asmdef change).
- This **edits** `PhotoModeController.cs` (Stories 1.3 + 1.4). Read it first and **add** to it — the viewfinder fade, the `Walk`/`Photo` enum, `OnRaiseCamera`, `OnZoom`, `IsPhotoMode`, and the `_viewfinderReady`/`_zoomReady` fail-soft flags all stay.

### 🚨 Read-first guardrails

1. **`Capture` is `Button`, NOT `Value`.** This is the **exception** to the project's "hold inputs must be `Value`" memory (`[[input-hold-actions-must-be-value]]`). That rule exists because *continuous/hold* inputs (Sprint, RaiseCamera, Zoom) need the release edge — a `Button` would latch them. Capture is the opposite: a **discrete one-shot tap**. Under `PlayerInput` Send-Messages, a `Button` action delivers exactly **one** `OnCapture` call per press — perfect. A `Value` action would fire **twice** (press *and* release), double-capturing, unless you guard `if (!value.isPressed) return;`. Keep it `Button`.
2. **`PhotoModeController` stays on `main characters`** (the PlayerInput GameObject). Send-Messages dispatches `OnCapture` **only** to scripts on the PlayerInput's own GameObject — putting it on the camera or a child means it silently never fires (same trap documented for `OnRaiseCamera`/`OnZoom` in Stories 1.3/1.4).
3. **Reuse `EventChannel<T>` — don't reinvent events.** `Core/EventChannel.cs` already provides `EventChannel<T> : ScriptableObject` with `event Action<T> Raised`, `Raise(T)`, per-handler exception isolation, and domain-reload subscriber clearing. `ShotCapturedChannel` is a **one-line subclass**. Any future listener subscribes in `OnEnable` / unsubscribes in `OnDisable` (Consistency Rule) — but **this story adds no listener** (just the raise); the HUD listener is Story 1.12.
4. **Synchronous feedback, no scheduling.** Play the shutter via `AudioSource.PlayOneShot` and trigger the flash **in the same `OnCapture` call**. No coroutine/`Invoke` delay (would blow the < 0.2 s budget, NFR2). The flash *fade-out* happens in `Update` (eased), but the flash *onset* is immediate.
5. **No magic numbers.** `flashDuration`, `flashColor`, `sfxVolume` live in `CaptureConfig` (mirrors `CameraConfig` from 1.4). The action-name string is `GameConstants.InputActions.Capture` (already `"Capture"`); the handler `OnCapture` derives from it — don't hardcode `"Capture"` anywhere.
6. **Fail-soft (NFR8), independently guarded.** Validate refs in `Awake`; missing ones log **once** and set the matching `_captureReady`/`_shutterReady`/`_flashReady` false so the handler/`Update` early-return per-feature. A missing shutter clip must **not** kill the flash or the event (the 1.4 review praised exactly this independence between viewfinder and zoom — copy it).
7. **URP only (NFR4).** The flash is a UI `Image` (default UI shader, URP-compatible). No Standard shader, no post-process stack required for the slice.

### Current state of the files/objects this story touches (read before editing)

- **`Assets/Scripts/PhotoMode/PhotoModeController.cs`** (EDIT) — 161 lines (after 1.4), namespace `CameraGame.PhotoMode`. Holds `enum CameraMode { Walk, Photo }`, `Mode`/`IsPhotoMode`, `OnRaiseCamera`, `OnZoom`, the viewfinder `CanvasGroup` fade + per-frame FOV lerp in `Update`, and the `_viewfinderReady`/`_zoomReady` fail-soft flags. **You add:** the capture serialized fields, `_captureReady`/`_shutterReady`/`_flashReady` + `_flashT`, the `Awake` validation, `OnCapture`, and the flash-fade block in `Update`. Keep everything else.
- **`Assets/Input/InputSystem_Actions.inputactions`** (EDIT) — the `Player` map currently has `Move`, `Look`, **`Attack`** (Button; bindings: `<Gamepad>/buttonWest`, `<Mouse>/leftButton`, `<Touchscreen>/primaryTouch/tap`, `<Joystick>/trigger`, `<XRController>/{PrimaryAction}`, `<Keyboard>/enter`), `Interact`, `Crouch`, `Jump`, `Previous`, `Next`, `Sprint`(Value), `RaiseCamera`(Value), `Zoom`(Value). Rename `Attack`→`Capture`, keep LMB, swap the gamepad binding to `<Gamepad>/rightTrigger`, drop the rest.
- **`Assets/Scripts/Core/EventChannel.cs`** (no change) — generic `EventChannel<T> : ScriptableObject`; `event Action<T> Raised`; `void Raise(T payload)`; clears subscribers in `OnDisable`. Subclass it; don't touch it.
- **`Assets/Scripts/Core/GameConstants.cs`** (no change) — already declares `InputActions.Capture = "Capture"` (and `Move/Look/Sprint/Jump/RaiseCamera/Zoom`). Reference it; don't redefine.
- **`Assets/Scripts/Core/GameLog.cs`** (no change) — `Info(cat,msg)`, `Warn(cat,msg)`, `Error(cat,msg,ctx=null)`, `Debug_(cat,msg)` (Editor/dev-build only). Use `Info` for the capture milestone and `Error` for the fail-soft path.
- **Empty scaffolding (already present):** `Assets/Scripts/Events/`, `Assets/Scripts/Grading/`, `Assets/Data/Channels/` exist (`.meta` only) — drop the new files there. `Assets/Data/Camera/` holds `CameraConfig.asset`; add `CaptureConfig.asset` beside it.
- **`Assets/Scripts/CameraGame.asmdef`** (no change) — name `CameraGame`, references `Unity.InputSystem`, `autoReferenced`. One assembly covers `Core`/`PhotoMode`/`Events`/`Grading`; no asmdef edit needed.

### Architecture compliance (what the dev MUST follow)
- **Capture flow (Architecture §Photo Capture & Grading):** the prescribed entry is capture → (grade) → **raise `ShotCapturedChannel`**. For this slice the grade is a **placeholder**; the grader (`ShotGrader.Grade(camera, subject, cfg) → ShotGrade`) and `GradingConfig` are **Stories 1.9–1.10**. Capture-to-feedback **< 0.2 s** is a hard requirement (NFR2). [Source: game-architecture.md#Photo Capture & Grading]
- **Event system (Architecture §Event System):** cross-system, decoupled signals use **ScriptableObject Event Channels**; `ShotCapturedChannel : EventChannel<ShotGrade>` is named in the architecture, payload = `ShotGrade` (the score, **not** the image — image persistence is the gallery's job, Story 1.11). Listeners subscribe in `OnEnable`/unsubscribe in `OnDisable`. [Source: game-architecture.md#Event System]
- **Input (AR1 / Architecture §Input):** repurpose `Attack`→`Capture` (LMB); "Capture/Zoom are no-ops unless in Photo mode"; single `Player` action map. [Source: game-architecture.md#Input]
- **Configuration (Architecture §Configuration / Consistency Rules):** tunables → ScriptableObjects, Inspector-tweakable, no recompile; **no code literals** for feel values. [Source: game-architecture.md#Configuration]
- **Code org (AR5):** namespaces `CameraGame.{Core, PhotoMode, Events, Grading}`; files under the matching `Scripts/<Folder>/`; one `CameraGame.asmdef`. Feature folders may depend on `Core` (and `Player`), never the reverse; keep `ShotGrade` a pure data type so `Events`→`Grading` is just a type reference, no cycle. [Source: game-architecture.md#Code Organization]
- **Consistency Rules:** cache refs in `Awake` (no `GetComponent` in `Update`); tunables in SO; event subs in `OnEnable`/`OnDisable`; magic strings via `GameConstants`; debug behind `GameLog.Debug_`. [Source: game-architecture.md#Consistency Rules]
- **Error handling (NFR8):** defensive + fail-soft — no fail state; validate refs in `Awake`, log clear errors, never throw into `Update`/handlers. [Source: game-architecture.md#Error Handling]

### Previous story intelligence (Stories 1.1–1.4)
- **From 1.4 (directly relevant):** `OnZoom` already shows the exact pattern to copy — Send-Messages handler, `if (!IsPhotoMode) return;` gate, a `_xxxReady` fail-soft flag validated in `Awake`, and **independent** guards so one missing ref doesn't disable unrelated features. The 1.4 review's single patch was an `OnValidate` guard for an SO with related fields — if `CaptureConfig` ever gets related numeric fields, consider the same. `CameraConfig` is the template to mirror for `CaptureConfig`.
- **From 1.3:** the viewfinder `CanvasGroup` is set non-interactive (`blocksRaycasts`/`interactable` = false) and only `alpha` is animated. A **deferred item** (tracked in `deferred-work.md`) says: *when Story 1.5 adds interactive viewfinder elements, flip those flags with the mode.* **This story's `CaptureFlash` is non-interactive** (it's a flash, not a button), so the deferred flag-flip is **still not needed** — leave it deferred unless you add a clickable reticle/shutter button (you shouldn't for this slice). Don't touch the existing viewfinder flags.
- **From 1.2:** the "sticky input" bug — a *hold* input typed `Button` misbehaves. Capture is **not** a hold, so `Button` is correct here (Guardrail #1). After any script change, call MCP `read_console` and confirm zero errors before marking done.
- **From 1.1:** `Core` scaffold (`GameLog`, `GameConstants` incl. `InputActions.Capture`, `EventChannel<T>`, `ObjectPool<T>`) and the empty `Scripts/Events`, `Scripts/Grading`, `Data/Channels` folders already exist — don't recreate them.
- **Established discipline:** existing scripts (`ThirdPersonController/Camera`, `CharacterAnimator`) are intentionally **not** renamed/moved (brownfield serialized-reference safety). Add alongside.

### Latest tech notes (Unity 6 / Input System 1.18)
- **`AudioSource.PlayOneShot(clip, volumeScale)`** is the standard one-shot SFX call — it layers over other clips on the same source without cutting them, ideal for a shutter. For UI/non-positional SFX set the source's **`Spatial Blend = 0` (2D)** so distance/3D rolloff doesn't attenuate it.
- **PlayerInput "Send Messages"** maps action name `Capture` → method `OnCapture(InputValue)` on the same GameObject. A `Button` action calls it on `performed` (press) only.
- **URP UI:** a screen-space `Canvas` + `Image` uses the default UI shader, which is URP-compatible — no special material needed for the flash.

### Testing standards (Architecture §Testing / project norm)
- Quality gates are **manual Play-mode** (shutter+flash < 0.2 s on LMB in Photo; no-op in Walk; channel raises; 1.3/1.4 + movement unregressed; Inspector re-tune of `CaptureConfig` with no recompile) plus **MCP `read_console`** for a clean console (NFR5). No EditMode/PlayMode unit test is required for "done".
- *Optional, cheap insurance:* `ShotGrade.FromPercent`→`Stars` mapping is a trivially unit-testable pure function if you want one EditMode test — but don't gold-plate.

### Project Structure Notes
- **New:** `Scripts/Grading/ShotGrade.cs` (`CameraGame.Grading`), `Scripts/Events/ShotCapturedChannel.cs` (`CameraGame.Events`), `Scripts/PhotoMode/CaptureConfig.cs` (`CameraGame.PhotoMode`), `Data/Channels/ShotCapturedChannel.asset`, `Data/Camera/CaptureConfig.asset`, and a `CaptureFlash` UI Image in the scene. No new folders, no asmdef change.
- **Edited:** `PhotoModeController.cs` (add capture), `InputSystem_Actions.inputactions` (Attack→Capture), `SampleScene.unity` (assign refs + AudioSource + flash UI).
- Paths match the Architecture's source-tree diagram (`Scripts/{Grading,Events,PhotoMode}`, `Data/{Channels,Camera}`).

### Project Context Rules
- No `project-context.md` exists; rules are drawn from CLAUDE.md and the architecture.
- **MCP for Unity is the toolchain:** create/edit scripts, create the SO assets, build the flash UI, and wire the scene through Unity MCP; after any script change call `read_console` for zero errors (project rule, NFR5).
- **URP only (NFR4):** never introduce the Standard shader.
- **Jira sync (CLAUDE.md):** this story maps to **KAN-16** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-16, mirror this Tasks/Subtasks breakdown as Jira **Subtasks** under KAN-16 (each with the **"In plain terms (for non-developers):"** comment), and update the `jiraSync:` block in `epics.md`.

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.5] — acceptance criteria (FR3 capture, < 0.2 s feedback, `ShotCapturedChannel` w/ placeholder grade; Attack→Capture, no-op in Walk).
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR3 (capture), FR4 (grading — deferred to 1.9–1.10), NFR2 (< 0.2 s feedback), NFR4 (URP), NFR5 (clean console), NFR8 (fail-soft), AR1 (extend PlayerInput; repurpose Attack; gate by mode).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Photo Capture & Grading] — capture → grade → raise channel; `ShotGrade`/`CapturedShot` types; synchronous; < 0.2 s.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Event System] — `ShotCapturedChannel : EventChannel<ShotGrade>`; subscribe in `OnEnable`/`OnDisable`; payload is the grade, not the image.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Input] — Attack→Capture (LMB); no-op unless Photo; single Player map.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Configuration] — tunables in ScriptableObjects, no recompile.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Code Organization] — namespaces/folders, one asmdef, dependency direction.
- [Source: Assets/Scripts/Core/EventChannel.cs] — generic `EventChannel<T>` to subclass.
- [Source: Assets/Scripts/Core/GameConstants.cs] — `InputActions.Capture = "Capture"` (already defined).
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs] — the file to extend; `IsPhotoMode` gate, `OnZoom`/`_zoomReady` fail-soft pattern to mirror.
- [Source: Assets/Scripts/PhotoMode/CameraConfig.cs] — the SO pattern to mirror for `CaptureConfig`.
- [Source: _bmad-output/implementation-artifacts/1-4-zoom-to-compose-in-photo-mode.md] — zoom handler pattern, fail-soft independence, `OnValidate` lesson from its code review.
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the deferred viewfinder `blocksRaycasts`/`interactable` flip (remains deferred — this story adds no interactive viewfinder UI).
- [Source: project memory `input-hold-actions-must-be-value`] — why hold inputs are `Value`; Capture is the discrete-tap exception (`Button`).

## Dev Agent Record

### Agent Model Used

claude-opus-4-8 (Opus 4.8, 1M context) — gds-dev-story workflow.

### Debug Log References

- MCP `refresh_unity` (compile) + `read_console` after every script change → **zero errors / zero warnings** throughout.
- MCP `manage_editor play` / `stop` cycle → no runtime errors; `manage_scene screenshot` (`Assets/Screenshots/story_1_5_play_check.png`) confirms Walk-mode render with no flash stuck on screen and URP intact.
- Asset YAML authored directly (MCP `manage_asset create` does not support custom ScriptableObjects) using the MonoScript GUIDs from the generated `.cs.meta` files; Unity re-imported both `.asset`s with the correct types (`get_info` confirms `assetType`).

### Completion Notes List

- **AC1 (capture fires feedback + raises channel, < 0.2 s):** `OnCapture` plays the temporary shutter SFX, sets the flash alpha immediately, and raises `ShotCapturedChannel` with `ShotGrade.Placeholder` — all **synchronously** in the input handler (no coroutine/Invoke/GPU readback), so it stays inside the < 0.2 s budget. Flash *onset* is immediate; only the *fade-out* is eased in `Update` over `CaptureConfig.SafeFlashDuration` (0.12 s default).
- **AC2 (no-op in Walk; reuse Attack):** the `Attack` action was renamed to `Capture` (kept `Button`), bound to **LMB + gamepad RT only** (dropped Touch/Joystick/XR/Enter). `OnCapture` early-returns when `!IsPhotoMode`. No other action was retyped or rebound (grep confirms only `Capture` remains; Move/Look/Sprint/RaiseCamera/Zoom/Jump untouched).
- **AC3 (fail-soft, no regressions):** three **independent** guards (`_captureReady`/`_shutterReady`/`_flashReady`) validated in `Awake` — a missing shutter clip never disables the flash or the event, and vice-versa. The missing channel logs an `Error` once; the flash logs an `Error` once if unassigned or missing a visible UI `Graphic`. Capture never throws in `Update`/the handler. All Story 1.3 viewfinder + 1.4 zoom code preserved verbatim.
- **Temporary shutter SFX:** `Assets/Audio/ShutterPlaceholder.wav` is a short generated placeholder wired to `shutterClip` so AC1 has audible feedback now. Replace it with an authored shutter clip later if desired.
- **Magic numbers:** all capture feel values live in `CaptureConfig` (`flashDuration`, `flashColor`, `sfxVolume`); none hard-coded in the controller. The action-name string comes from `GameConstants.InputActions.Capture`.
- **Scope honored:** no `ShotGrader`/`GradingConfig`/`GalleryService`/HUD/event-actor work (those are Stories 1.6–1.12). No new camera rig, no asmdef change — one `CameraGame` assembly still covers `Core`/`PhotoMode`/`Events`/`Grading`.
- **Human play-test PASSED (Alexv, 2026-06-17):** held RMB + single LMB → one flash; repeated LMB → one flash each (no double-capture); LMB without RMB → nothing; movement/zoom/raise feel unchanged; console clean. Review follow-up added placeholder shutter SFX wiring and made the flash ignore the parent viewfinder fade.

### File List

**New:**
- `Assets/Scripts/Grading/ShotGrade.cs`
- `Assets/Scripts/Events/ShotCapturedChannel.cs`
- `Assets/Scripts/PhotoMode/CaptureConfig.cs`
- `Assets/Audio/ShutterPlaceholder.wav` (temporary shutter SFX)
- `Assets/Data/Channels/ShotCapturedChannel.asset`
- `Assets/Data/Camera/CaptureConfig.asset`
- `Assets/Screenshots/story_1_5_play_check.png` (verification screenshot)

**Edited:**
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` (added capture: serialized refs, fail-soft `Awake` guards, `OnCapture` handler, immediate flash alpha, flash-fade in `Update`)
- `Assets/Input/InputSystem_Actions.inputactions` (Attack → Capture; LMB + gamepad RT only)
- `Assets/Scenes/SampleScene.unity` (added `CaptureFlash` UI Image + CanvasGroup under `PhotoViewfinder`; added 2D AudioSource to `main characters`; wired all `PhotoModeController` capture refs including placeholder shutter SFX; set `CaptureFlash` to ignore parent CanvasGroups)

### Change Log

- 2026-06-17 — Implemented Story 1.5 (Capture a photo): repurposed Attack→Capture (LMB + gamepad RT), added `ShotGrade`/`ShotCapturedChannel`/`CaptureConfig`, extended `PhotoModeController` with a synchronous, fail-soft `OnCapture` (shutter + flash + placeholder-grade channel raise), and wired the scene. All ACs implemented; console clean. Status → review.
- 2026-06-17 — Code review fixes applied: immediate flash alpha in `OnCapture`, guarded flash duration, required visible UI `Graphic` for flash readiness, `ShotGrade` NaN normalization, `CaptureFlash` ignores parent viewfinder fade, and temporary `ShutterPlaceholder.wav` is wired for AC1 shutter feedback. Status → done.
