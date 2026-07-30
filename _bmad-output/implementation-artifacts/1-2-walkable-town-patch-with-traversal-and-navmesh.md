# Story 1.2: Walkable town patch with traversal and NavMesh

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want a small patch of town I can walk and sprint around,
so that I have a place to explore and chase moments before any events exist.

## Acceptance Criteria

**AC1 — Traversal works on a collidered patch (FR12, FR13, NFR1)**
- In Play mode in the slice scene, the player can **move** (WASD / left stick) and **sprint** (Shift / L3) across the blocked-out town patch.
- The walkable surface has colliders: **no fall-through**, no getting stuck on flat ground.
- Framerate stays stable — target **60 FPS @ 1080p on `PC_RPAsset`** (NFR1) with the map loaded.

**AC2 — Embedded Blender camera stays disabled (AR8)**
- When the slice scene loads, the **only enabled `Camera` is the player's Main Camera**. Any camera embedded in the world FBX is disabled/removed so it can never override Main Camera via render depth.

**AC3 — NavMesh covers the walkable area (enables Story 1.7)**
- A **baked NavMesh** covers the walkable town patch, so a `NavMeshAgent` (the Town Drunk in Story 1.7) can path across it.
- The NavMesh agent settings (radius / height / step / slope) are **sized to this project's world scale** (see Dev Notes — the character is ~8.86 units tall, NOT 1:1 metric), so the baked surface actually matches where the player can walk.

## Tasks / Subtasks

- [x] **Task 1 — Confirm and standardize the slice scene (AC1)** — ✅ DEV: confirmed `SampleScene` is the populated, Build-Settings scene; player has all required components. Stale-stub finding documented. Optional rename skipped.
  - [x] Open `Assets/Scenes/SampleScene.unity`. **This is the real slice scene** — it is fully populated (player + `game map` + Main Camera + Directional Light + Global Volume) and is the only scene in Build Settings. *(The architecture doc names "Video Camera Game 2.0.unity" as the slice scene — that file is a 14 KB empty stub and is stale; do NOT use it. See Dev Notes.)*
  - [x] Verify the player object `main characters` has `CharacterController` + `PlayerInput` + `ThirdPersonController` + `CharacterAnimator`, and the Main Camera has `ThirdPersonCamera`. (Confirmed present at story-creation time.)
  - [ ] *(Optional, non-blocking)* If you rename the scene to match the architecture's `TownSlice` naming convention, you must also update the Build Settings scene entry. Skip unless you want the cleanup — it is not required by any AC.

- [x] **Task 2 — Verify colliders & traversal, fix fall-through (AC1)** — ✅ DONE: colliders verified (player lands & rests on surface, live); Alexv play-tested the full patch — **no fall-through**. Sprint **was broken (sticky)** and is now **fixed** (input-action type change, see below) and confirmed working.
  - [x] Confirm the `game map` geometry has colliders. They are added by the existing editor tool **`Tools > Add MeshColliders to World`** (`Assets/Scripts/Editor/AddMeshColliders.cs`), which finds the `game map` object and adds a `MeshCollider` to every child `MeshFilter` that lacks one. Re-run it if the map was re-imported and any children are missing colliders. — *Done: 1,305 colliders added (prior pass); collider integrity confirmed live — player physically lands and rests on the surface in Play mode.*
  - [x] Enter Play mode and walk + sprint the full patch. Confirm **no fall-through** and no invisible walls on the walkable ground. Note any holes (missing colliders) and re-run the tool or add colliders to those specific meshes. — *Done: Alexv walked/sprinted the patch (2026-06-05), no fall-through. (Separate "see-through walls" rendering finding logged to `deferred-work.md` — out of scope for this story.)*
  - [x] Confirm sprint actually changes speed (Shift / L3) — the controller already wires `OnSprint`; this is a verification, not new code. — *Verification caught a real bug: Sprint action was type `Button`, which under PlayerInput Messages only delivers the press, not the release → sprint stuck on. Fixed by changing the Sprint action to type `Value` (matching Move/Look) so the release event fires `OnSprint(isPressed:false)`. Re-tested by Alexv: sprint speeds up on hold and drops back on release. ✅*

- [x] **Task 3 — Ensure the embedded Blender camera is disabled (AC2, AR8)** — ✅ DEV: scene-wide Camera scan (incl. inactive) finds only `Main Camera`. No embedded camera present. AC2 satisfied.
  - [x] In the slice scene, confirm the **only** enabled camera is `Main Camera`. (At story-creation time a scene scan found exactly one Camera — the player's — so this currently passes.) — *Done: scene-wide scan (incl. inactive) found only `Main Camera`.*
  - [ ] If a re-import of `game map .fbx` reintroduced an embedded camera as a child of `game map`, **disable that GameObject** (or remove the Camera component). Do NOT touch Main Camera. Save the scene.
  - [ ] *(Why this matters: a Blender-exported camera with a higher `depth` value renders over Main Camera and the player sees the wrong view — see CLAUDE.md.)*

- [x] **Task 4 — Bake the NavMesh (AC3)** — ✅ DEV (verified 2026-06-04 from project files): NavMesh baked with the correctly-scaled **shared Humanoid agent** (radius 0.95 / height 8.86 / climb 2 / slope 45° in `ProjectSettings/NavMeshAreas.asset`). 36 MB `NavMesh-game map.asset` baked; scene `NavMeshSurface` references it by matching guid `255eb66f…`; Use Geometry = Physics Colliders, Collect Objects = Children. Auto voxel 0.3166 = 0.95/3 corroborates radius 0.95. Visual blue-overlay gap check = quick Alexv eyeball (36 MB asset implies broad coverage).
  - [x] Use the **AI Navigation package** workflow (already installed). Add a **`NavMeshSurface`** component to a scene object (recommended: the `game map` root, or a dedicated empty `Navigation` GameObject), set **Collect Objects** to gather the map geometry, and click **Bake** in the NavMeshSurface inspector. *(The legacy `Window > AI > Navigation` "Bake" tab does not apply to the package-based `NavMeshSurface` workflow — use the component.)* — *Done: `NavMeshSurface` on `game map`, `m_CollectObjects: 2` (Children), baked.*
  - [x] Set the NavMeshSurface **Use Geometry** source deliberately: since the map's walkable surface is defined by the ~16,321 **MeshColliders** added by the tool, **Physics Colliders** is the safer source (it matches exactly what the player collides with); fall back to **Render Meshes** only if the bake misses geometry. This also avoids baking over decorative meshes that have no collider. — *Done: `m_UseGeometry: 1` (Physics Colliders).*
  - [x] **Scale the agent settings to this world** before baking (see Dev Notes "World scale"): the default agent (radius 0.5, height 2, step 0.4) is far too small for an ~8.86-unit-tall character and will bake a NavMesh on ledges/steps the player cannot actually use. Read the **actual `radius` and `height` off the player's `CharacterController`** in the scene and size the bake agent (radius / height / max step / max slope) to match it — max step ≈ `stepOffset` (2). Keep these values written down — Story 1.7's drunk `NavMeshAgent` must use the same agent type or it will path differently than the bake. — *Done: edited the **shared Humanoid agent (id 0)** to r0.95 / h8.86 / climb2 / slope45 in `ProjectSettings/NavMeshAreas.asset`, so Story 1.7's drunk inherits the same dimensions automatically.*
  - [x] Confirm the baked NavMesh visibly covers the walkable patch (blue overlay in the Scene view with the NavMeshSurface selected), with no large gaps on reachable ground. — *Done: 36 MB baked asset + correctly-scaled agent; Alexv reviewed navigation and reported no coverage gaps on reachable ground. (His separate "humanoid too tall" note is a world-scale concern logged to `deferred-work.md` for `correct-course`, not a NavMesh coverage gap.)*
  - [x] Save the scene so the baked `NavMeshData` asset is referenced. — *Done: scene persists the `m_NavMeshData` guid reference.*

- [x] **Task 5 — Performance & clean-console check (AC1 / NFR1, NFR5)** — ✅ DONE: console clean live in Play mode (0 errors / 0 warnings); Alexv reports framerate "looks good" (stable) while moving — NFR1 satisfied.
  - [x] In Play mode at 1080p with `PC_RPAsset` active, sanity-check framerate is stable near 60 FPS while moving around the map (Stats panel / a frame profiler pass). Note any obvious hotspot (e.g. thousands of un-culled MeshColliders/renderers) for follow-up, but do not gold-plate — the AC is "stable", not "optimized". — *Done: Alexv confirmed FPS "looks good"/stable while moving (2026-06-05).*
  - [x] Call MCP **`read_console`** and confirm **zero errors** and no new URP/Standard-shader warnings introduced by this story (NFR4, NFR5). The pre-existing Plastic SCM repo-name notice is baseline noise, not from this story. — *Done: live `read_console` in Play mode returned 0 errors / 0 warnings (pass 3).*

- [x] **Task 6 *(verification only)* — End-to-end AC pass** — ✅ DONE: all three ACs verified (Alexv play-test 2026-06-05 + live Unity MCP checks).
  - [x] Walk + sprint the patch (AC1) · only Main Camera renders (AC2) · NavMesh covers the patch (AC3). Capture a quick Scene/Play screenshot for the story record if convenient. — *Done: walk + (fixed) sprint verified by Alexv; only Main Camera renders (live scan + POV screenshot `Assets/Screenshots/story_1_2_play_pov.png`); NavMesh covers the patch. Two out-of-scope findings (see-through walls, character too tall) logged to `deferred-work.md`.*

## Review Findings

_Code review (2026-06-05) — three parallel adversarial layers (Blind Hunter, Edge Case Hunter, Acceptance Auditor). **Result: all 3 ACs satisfied.** 0 decisions, 0 patches, 3 deferred, 9 dismissed as noise. Notably, both context-blind layers flagged the Sprint `Button`→`Value` change as *causing* sticky sprint — that is a false positive: `Value` is the correct fix (a `Button` action under PlayerInput "Send Messages" delivers only the press, not the release). Confirmed by the Acceptance Auditor reading `OnSprint`, by this story's documented root cause, and by project memory `input-hold-actions-must-be-value`._

- [x] [Review][Defer] Sprint state not reset on focus-loss / `OnDisable` [Assets/Scripts/ThirdPersonController.cs:151] — deferred, pre-existing. `_isSprinting` is only written inside `OnSprint`; there is no `OnApplicationFocus`/`OnDisable` reconciliation, so alt-tabbing away while holding Shift could (in theory) leave sprint latched on. Not introduced by this story's `Button`→`Value` change — the gap predates it. Minor; revisit during a controller hardening pass.
- [x] [Review][Defer] NavMesh agent dimensions are aggressive vs. fine voxel geometry [ProjectSettings/NavMeshAreas.asset] — deferred, theoretical. With radius 0.95 (erodes walkable strips narrower than ~1.9u), climb 2 and height 8.86 against `cellSize` 0.16667, the bake could in theory drop thin ledges or create climb/height "overhang" surfaces, and openings with 2–8.86u clearance are walkable for the player (a CharacterController) but carved out for a NavMeshAgent. Not a defect for this open, play-tested town patch (coverage confirmed, no gaps). Re-verify when **Story 1.7's** drunk `NavMeshAgent` actually paths the world.
- [x] [Review][Defer] AC2 single-camera guarantee is a point-in-time scan, not an enforced guard [Assets/Scenes/SampleScene.unity] — deferred, pre-existing. Only `Main Camera` is enabled today, so AC2 passes. But nothing *prevents* a future re-import of `game map .fbx` from reintroducing an embedded Blender camera (higher render depth would override Main Camera). The conditional guard subtask under Task 3 already covers this; keep it in mind on any map re-import.

## Dev Notes

### What this story IS — and is NOT
- **IS:** scene setup + verification on the **existing** imported world, disable any embedded camera, and **bake a NavMesh** with correctly-scaled agent settings. Mostly Editor/scene/asset work.
- **IS NOT:** writing a new controller (the existing one already does move/sprint — see below), importing the 214 MB town-map `.blend`, building districts/vantage points, or adding any event/NPC. Those are **Epic 2** (full map, AR9) and **Story 1.7** (the drunk). Resist scope creep.
- **Likely zero new runtime scripts.** AC1 is met by the existing controller; AC2/AC3 are scene + bake operations. Only add a script if you find a concrete gap (none expected).

### 🚨 Read-first guardrails (brownfield)

1. **Use `SampleScene`, not "Video Camera Game 2.0".** The architecture (`game-architecture.md` §Development Environment) says the first scene is `Video Camera Game 2.0.unity`, but that file is a **14 KB empty stub**. The actual populated, Build-Settings-registered slice scene is **`Assets/Scenes/SampleScene.unity` (16.4 MB)** — it holds the player, the `game map` (1,658 objects), Main Camera, light, and Global Volume. Build it on SampleScene. (This is a documented doc-drift; flag it if you update the architecture later.)

2. **World scale is NOT 1:1 metric — this is the single biggest gotcha for the NavMesh.** The player `CharacterController` is **~8.86 units tall** (the FP camera reads a head bone at Y≈8.5, and `ThirdPersonController.stepOffset = 2`). Everything in this project is modelled at a large scale. If you bake the NavMesh with Unity's default agent (radius 0.5 / height 2 / step 0.4), the bake will treat tiny bumps as walls and tiny ledges as walkable — it will not match where the player actually goes. **Size the bake agent to the player:** height ≈ the controller height, radius ≈ the controller radius, max step ≈ `stepOffset` (2), max slope to taste. Record the agent type/values — **Story 1.7's drunk `NavMeshAgent` must use the same agent type** or it will path differently than the bake.

3. **Colliders come from the existing editor tool, not by hand.** `Assets/Scripts/Editor/AddMeshColliders.cs` exposes `Tools > Add MeshColliders to World`. It finds the object named **`game map`** (falls back to `World`) and adds a `MeshCollider` to every child `MeshFilter` missing one. CLAUDE.md records ~16,321 colliders already added. Don't hand-add colliders across 1,658 objects — re-run the tool if geometry changed.

4. **Do not touch `ThirdPersonController` / `ThirdPersonCamera` / `CharacterAnimator` class names.** Same brownfield reason as Story 1.1: renaming a MonoBehaviour class breaks serialized component references on the player in the scene. The architecture *suggests* renaming `ThirdPerson*` → `Player*`/`FirstPersonCamera`, but that is explicitly optional, non-blocking cleanup — leave it for a dedicated refactor, not this story.

### Current state of the files/objects this story touches (read before editing)

- **`SampleScene.unity` hierarchy (verified via Unity MCP at story creation):**
  - `Main Camera` — `Camera`, `AudioListener`, `UniversalAdditionalCameraData`, **`ThirdPersonCamera`**. Tagged `MainCamera`. This is the one camera that should render.
  - `Directional Light`, `Global Volume` (URP post Volume).
  - `main characters` — `Animator`, `CharacterController`, `PlayerInput`, **`ThirdPersonController`**, **`CharacterAnimator`** (14 children = skeleton). This is the player.
  - `game map` — Transform root with **1,658 children** = the world geometry (carrying the MeshColliders).
- **`ThirdPersonController.cs`** (no changes needed): `CharacterController`-based FP movement. `walkSpeed = 7`, `sprintSpeed = 14`, `gravity = -15`, `jumpHeight = 1.2`, `stepOffset = 2`. Input via Send-Messages: `OnMove`, `OnLook`, `OnSprint`, `OnJump`. Cursor locked on Awake. **AC1's move+sprint already work** — your job is to verify them on the patch, not re-implement.
- **`ThirdPersonCamera.cs`** (no changes needed): first-person camera that snaps to the head bone `Armature/Hips/Spine/Chest/Neck/Head/Head_end` and reads `ThirdPersonController.CameraPitch`. Confirms ADR-1 (walk = first-person). The class name is stale (`ThirdPerson*` but behaviour is FP) — leave it.
- **`AddMeshColliders.cs`** (Editor tool, no changes needed): the collider workflow described above.

### Architecture compliance (what the dev MUST follow)
- **URP only (NFR4):** add no Standard-shader materials. This story adds no materials.
- **Fail-soft (NFR8):** no new error-prone runtime code expected; if you add any, validate references in `Awake`/`Start` and `Debug.LogError` + disable gracefully — never throw into `Update`.
- **Navigation = AI Navigation package** (`Unity.AI.Navigation`, already installed). Baking via the `NavMeshSurface` **component** is an Editor/asset operation — it does **not** require a code reference, so **no `CameraGame.asmdef` change is needed for this story**. (The asmdef gains a `Unity.AI.Navigation` reference only when a *runtime script* uses the package — that is Story 1.6/1.7, not 1.2. `NavMeshAgent` itself lives in the always-available `UnityEngine.AIModule`.)
- **No magic strings:** if you do add any code, input action names already exist in `CameraGame.Core.GameConstants.InputActions` (`Move`, `Sprint`, …) from Story 1.1.

### Project Structure Notes
- No new folders needed — Story 1.1 already created the `Scripts/{Core,Player,…}` and `Data/{…}` scaffold. This story works in `Assets/Scenes/` (the scene) and produces a baked `NavMeshData` asset (Unity stores it next to the scene or under a generated folder — let Unity place it, just commit it).
- The newest source art lives in the git-ignored `CAMERA GAME SHARED FOLDER`; per project memory, **the Epic 1 slice may use the already-imported map** — you do **not** need to export/import the 214 MB `.blend` for this story (that is Epic 2 / AR9).

### Previous story intelligence (Story 1.1)
- 1.1 built the `Core` scaffold (`GameLog`, `GameConstants`, `AnimHashes`, `ObjectPool<T>`, `EventChannel`) + `CameraGame.asmdef` + `CameraGame.Editor.asmdef`, and verified a clean compile via Unity MCP. Existing scripts were intentionally left untouched (no rename/move) — keep that discipline here.
- 1.1's code review hardened `EventChannel` and **deferred `ObjectPool<T>` hardening to Story 1.6** (logged in `deferred-work.md`). Not relevant to 1.2, but be aware the pool is not yet guarded.
- Established workflow rule reconfirmed: **after any script change, call MCP `read_console` and confirm zero errors before marking done.** (For 1.2 this mainly catches accidental breakage, since little/no code changes are expected.)

### Testing standards
- This is scene/asset/bake work — no unit-testable logic is introduced, so no EditMode/PlayMode tests are required for "done".
- The binding quality gates are **manual in Play mode** (traversal + no fall-through + only Main Camera renders + NavMesh visibly covers the patch) and **MCP `read_console`** for a clean console (NFR5). Capture a screenshot for the story record if convenient.

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.2] — acceptance criteria (FR12, FR13, AR8, NavMesh enables 1.7).
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR12 (playable patch), FR13 (free-roam traversal), AR8 (disable embedded camera), AR9 (map FBX export = Epic 2), NFR1 (60 FPS @ 1080p).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Architectural Decisions] — ADR-1 (walk = first-person; reuse existing controller/camera).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Engine & Framework] — Navigation = AI Navigation (NavMesh) for event-actor pathing; SampleScene + "Video Camera Game 2.0".
- [Source: Assets/Scripts/ThirdPersonController.cs] — existing FP movement/sprint (`walkSpeed`/`sprintSpeed`, `stepOffset = 2`, Send-Messages input). No changes.
- [Source: Assets/Scripts/ThirdPersonCamera.cs] — FP head-bone camera (ADR-1 confirmation).
- [Source: Assets/Scripts/Editor/AddMeshColliders.cs] — `Tools > Add MeshColliders to World` (collider workflow).
- [Source: CLAUDE.md] — embedded Blender camera must stay disabled (higher depth overrides Main Camera); ~16,321 MeshColliders added via the tool.
- [Source: project memory `shared-folder-newest-assets`] — Epic 1 slice may use the existing imported map; the 214 MB `.blend` → FBX export is Epic 2 (AR9).

### Project Context Rules
- No `project-context.md` exists; rules are drawn from CLAUDE.md and the architecture.
- **MCP for Unity is the toolchain:** do scene/asset/bake work and verification through Unity MCP; after any script change call `read_console` for zero errors (project rule, NFR5).
- **URP only (NFR4):** never introduce the Standard shader.
- **Jira sync (CLAUDE.md):** this story maps to Jira issue **KAN-13** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-13, and mirror this Tasks/Subtasks breakdown as Jira Subtasks under KAN-13.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]`

### Debug Log References

- `Tools > Add MeshColliders to World` → "Added MeshColliders to 1305 objects." (re-imported map had new uncollidered meshes).
- PhysX advisory (pre-existing, art-driven): "Detected one or more triangles where the distance between any 2 vertices is greater than 500 units … Source mesh name: House Complete.001". Large-triangle stability note from the oversized world scale — not an error, not introduced by this story.
- MCP `read_console` after scene save: zero compile errors (no scripts changed this story).
- NavMesh bake verified from project files (2026-06-04, Unity MCP offline): `ProjectSettings/NavMeshAreas.asset` Humanoid agent = radius 0.95 / height 8.86 / climb 2 / slope 45; scene `NavMeshSurface.m_AgentTypeID: 0`, `m_UseGeometry: 1`, `m_CollectObjects: 2`, `m_VoxelSize: 0.3166` (=0.95/3); `m_NavMeshData` guid `255eb66f1ad78d446a3ee7bfac8270ef` matches `Assets/Scenes/SampleScene/NavMesh-game map.asset.meta`. Baked asset is 36 MB.

### Completion Notes List

- **Done & verified via Unity MCP (no human needed):**
  - *AC2 (Task 3):* scene-wide Camera scan including inactive objects → exactly one camera (`Main Camera`, id 54250). No embedded Blender camera present; AC2 satisfied as-is.
  - *Task 1:* `SampleScene.unity` confirmed as the real slice scene (16.4 MB, in Build Settings; "Video Camera Game 2.0" is a 14 KB empty stub). Player `main characters` has CharacterController + PlayerInput + ThirdPersonController + CharacterAnimator.
  - *Task 2 (colliders):* ran the existing collider tool — 1,305 missing MeshColliders added across the re-imported `game map`.
  - *Task 4 (setup):* added `NavMeshSurface` to `game map`, configured Use Geometry = Physics Colliders, Collect Objects = Children. Scene saved.
- **Exact agent dimensions for the NavMesh bake (read from the live CharacterController, id 78166):** radius **0.95**, height **8.86**, runtime step **2** (`ThirdPersonController.Awake` sets `stepOffset = 2`; edit-time value is 0.3), slope limit **45°**. The default Humanoid agent (r0.5/h2/step0.4) is wrong for this scale — do NOT bake with it.
- **Did NOT auto-bake on purpose:** the bake needs the agent scaled, and a wrong-scale NavMesh would falsely look "done". Baking with a scaled agent + visually confirming coverage is a quick inspector task best done by the human (and editing the shared Humanoid agent makes Story 1.7's drunk agent match automatically — exactly what Task 4 wants).
- **Task 4 (NavMesh bake) now CONFIRMED DONE (2026-06-04, second dev pass):** the bake was completed since the first pass. Verified from project files (Unity MCP was offline this session) — the shared **Humanoid agent (id 0)** was scaled to radius 0.95 / height 8.86 / climb 2 / slope 45 in `NavMeshAreas.asset`, a 36 MB `NavMesh-game map.asset` was baked, and `SampleScene.unity`'s `NavMeshSurface` references it by matching guid. Editing the shared agent (rather than a custom agent type) means Story 1.7's drunk `NavMeshAgent` inherits the same dimensions automatically — exactly what AC3 wanted. AC3 satisfied; only a quick visual blue-overlay eyeball remains.
- **Remaining human-in-the-loop gates (cannot be faked autonomously, and Unity MCP is offline this session):** walk/sprint feel + no fall-through (AC1 / Task 2), 60 FPS @ 1080p + clean `read_console` (NFR1, NFR5 / Task 5), and the end-to-end AC pass + screenshot (Task 6). These need Alexv in the Unity Editor Play mode. See the handoff checklist.

- **Pass 3 (2026-06-04, Unity MCP back ONLINE — live verification):** With the Editor connected, re-verified the items the offline passes could only infer from files, and entered Play mode for real:
  - *AC2 (live):* whole-scene Camera scan incl. inactive → exactly one `Camera` = `Main Camera` (id 527722), `enabled=true`, tag `MainCamera`, depth −1. No embedded Blender camera anywhere. Play-mode screenshot (`Assets/Screenshots/story_1_2_play_pov.png`) shows Main Camera rendering the correct first-person POV over the patch.
  - *AC1 no-fall-through (live):* entered Play mode; player spawned at Y=20, fell under gravity, and **landed grounded** on the collidered surface — `velocity.y=0`, `isGrounded=true`, `collisionFlags=Below`, Y settled ~3.6. Proves collider integrity at the spawn area. Also confirmed `ThirdPersonController.Awake` ran (`stepOffset` flipped 0.3→2 at runtime).
  - *AC3 (live):* `NavMeshSurface` on `game map` reads agentTypeID 0, CollectObjects=Children, UseGeometry=Physics Colliders, voxel 0.3166 (=0.95/3), navMeshData → `Assets/Scenes/SampleScene/NavMesh-game map.asset`. Bake config confirmed in the live Editor, not just from files.
  - *Task 5 console (live):* `read_console` during Play mode → 0 errors, 0 warnings.
  - *`ThirdPersonCamera.target=null` is benign:* `Start()` auto-finds the player via `GameObject.Find("main characters")` and the head bone, with a height-offset fallback (fail-soft per NFR8). Not a bug.
  - *Alexv chose manual close-out* for the three remaining gates (full-patch walk/sprint feel, 60 FPS number, NavMesh blue-overlay gap eyeball). Story stays `in-progress` pending his play-test report.

- **Pass 4 (2026-06-05, Alexv play-test close-out):** Play-test results — full-patch walk **no fall-through** ✅; FPS "looks good"/stable ✅ (NFR1); NavMesh coverage no gaps reported ✅; **sprint was broken** (stuck on after release) → diagnosed via a temporary `OnSprint` log + the symptom asymmetry vs `Move`, then **fixed** by changing the `Sprint` input action from type `Button` → `Value` (a `Button` action under PlayerInput Messages delivers only the press; `Value` delivers press *and* release). Re-tested ✅. Debug log reverted; clean recompile (0 errors). **All ACs satisfied → Status: review.** Two out-of-scope findings (see-through walls; character too tall for world scale) logged to `deferred-work.md` for `correct-course`/future stories. *Only intentionally-unchecked items left in the task list are the optional scene-rename (Task 1) and the conditional "if a re-import reintroduced an embedded camera" guard (Task 3) — both non-AC, non-blocking.*

### File List

Modified:
- `Assets/Scenes/SampleScene.unity` — added 1,305 MeshColliders to `game map` children; added + configured a `NavMeshSurface` on `game map`; baked NavMesh (references the data asset by guid).
- `ProjectSettings/NavMeshAreas.asset` — scaled the shared **Humanoid** agent (id 0) to radius 0.95 / height 8.86 / climb 2 / slope 45 for this world's scale.
- `Assets/Input/InputSystem_Actions.inputactions` — **Sprint bug fix:** changed the `Sprint` action's `type` from `Button` to `Value` so PlayerInput (Messages) delivers the release event, not just the press. Fixes "sticky sprint" (sprint stayed on after releasing Shift). Now matches the `Move`/`Look` value-action pattern.

_(Note: `Assets/Scripts/ThirdPersonController.cs` was temporarily instrumented with a `Debug.Log` in `OnSprint` to diagnose the sprint bug, then reverted — net no change to the file.)_

Added:
- `Assets/Scenes/SampleScene/NavMesh-game map.asset` (+ `.meta`) — baked NavMeshData (~36 MB) for the walkable town patch.
- `Assets/Screenshots/story_1_2_play_pov.png` (+ `.meta`) — Play-mode first-person POV screenshot (pass 3) showing Main Camera rendering the town patch; evidence for AC2 + the story record.

_(No script files created or modified. No asmdef changes. No new ScriptableObject assets.)_

## Change Log

| Date | Change |
|------|--------|
| 2026-06-04 | Story created (ready-for-dev). Slice scene resolved to `SampleScene` (architecture's "Video Camera Game 2.0" is a stale empty stub). World-scale NavMesh gotcha (~8.86-unit character) and embedded-camera verify-and-guard (currently only Main Camera present) captured from live Unity MCP inspection. |
| 2026-06-04 | Dev (in-progress): verified slice scene + player (Task 1); confirmed only Main Camera present, AC2 done (Task 3); added 1,305 MeshColliders via the existing tool (Task 2 colliders); added + configured a NavMeshSurface on `game map` (Task 4 setup). Read exact CharacterController dims for bake agent (r0.95/h8.86/step2/slope45). Scene saved; zero compile errors. Bake + play-test gates handed to Alexv (human verification required). |
| 2026-06-04 | Dev (second pass, Unity MCP offline): **Task 4 (NavMesh bake) confirmed DONE** by inspecting project files — shared Humanoid agent scaled (r0.95/h8.86/climb2/slope45) in `NavMeshAreas.asset`, 36 MB `NavMesh-game map.asset` baked, scene `NavMeshSurface` references it (guid match). AC3 satisfied (visual gap eyeball pending). Marked Task 4 + verified subtasks of Tasks 1/3 complete; updated File List with the baked asset + `NavMeshAreas.asset`. **Remaining: Tasks 2, 5, 6 — human Play-mode gates** (walk/sprint feel, 60 FPS @ 1080p, clean console, end-to-end pass). Story stays `in-progress` pending Alexv's play-test. |
| 2026-06-04 | Dev (third pass, **Unity MCP back ONLINE**): live-verified in the Editor + Play mode — **AC2** (only `Main Camera` renders correct POV; play-mode screenshot saved), **AC1 no-fall-through at spawn** (player drops from Y=20 and lands grounded on colliders: `velocity.y=0`, `isGrounded=true`), **AC3** NavMeshSurface config, and **clean console in Play mode** (0 errors/0 warnings). Marked Task 2 collider subtask + Task 5 console subtask `[x]`. Added `story_1_2_play_pov.png` to File List. Alexv chose **manual** close-out for the 3 remaining gates (full-patch walk/sprint feel, 60 FPS @ 1080p, NavMesh blue-overlay gap). Story stays `in-progress` pending his play-test report. |
| 2026-06-05 | Dev (fourth pass, Alexv play-test close-out): play-test passed — no fall-through, FPS stable, NavMesh coverage OK. **Fixed sprint bug:** `Sprint` input action `Button` → `Value` in `InputSystem_Actions.inputactions` (Button actions only deliver the press under PlayerInput Messages → sprint stuck on; Value delivers release too). Re-tested ✅; temporary diagnostic log reverted; clean recompile. Marked Tasks 2/4/5/6 complete. **All ACs satisfied → Status: review.** Logged 2 out-of-scope findings (see-through walls, character-too-tall) to `deferred-work.md`. |
