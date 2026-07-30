# Story 1.6: Event-actor lifecycle engine

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer,
I want a generic, data-driven event-actor that runs a spawn→build→peak→wind-down→despawn lifecycle from a pool,
so that ~80% of all future events become data rather than new code.

## Acceptance Criteria

**AC1 — Data-driven lifecycle FSM advancing on its own timers, exposing `ISubject` (FR9, AR2)**
- An `EventDefinition` ScriptableObject holds, **per phase**, a duration, an (optional) animation state name, and an (optional) cue clip — plus a subject `Id` and a cue radius (~25 m). Tunable in the Inspector, no recompile.
- When an `EventActor` is spawned by the `EventManager`, it advances through `Spawn → Build → Peak → WindDown → Despawn` driven **only by its own timers** (`Time.deltaTime`), with no per-phase code branches outside the actor.
- The actor exposes its state through an **`ISubject` interface** — at minimum `Bounds`, `IsAtPeak`, `TimeToPeak`, `SubjectId` — so the grader (Stories 1.9–1.10) can read it without referencing any concrete event type.

**AC2 — Pooled, concurrency-capped, leak-free (NFR3, NFR6)**
- Actors are drawn from and returned to an **object pool** — **never `Instantiate`/`Destroy` in the gameplay loop**. On reaching `Despawn`, the actor returns itself to the pool (it does not destroy itself).
- The `EventManager` caps concurrent active actors at **1** for the slice (Inspector-tunable field, default 1).
- Over an extended run (many spawn/despawn cycles), **no objects leak** — the actor instance count stays flat (one pooled instance is reused). Verified by watching the Hierarchy / Profiler across cycles.

**AC3 — Fail-soft on bad/missing config; never throws into `Update` (NFR8)**
- If an `EventActor` initializes with a **missing or invalid `EventDefinition`**, it logs **one clear error** and **disables itself gracefully** (`enabled = false`) — it never throws from `Update`, and the rest of the game keeps running (no fail state).
- Animation and NavMesh pathing are **independently fail-soft**: a missing Animator controller, an animation state that doesn't exist, an absent route, or an off-NavMesh spawn must **not** throw or spam the console — the timer FSM still runs to completion. (This is what lets the engine be verified in 1.6 *before* the real husband Animator/route lands in Story 1.7.)
- After every script change, **MCP `read_console` shows zero errors and zero warnings** (NFR5); URP untouched (NFR4).

## Tasks / Subtasks

- [x] **Task 1 — Harden `ObjectPool<T>` against the real `EventManager` contract (AC2, AC3)** — *edit the existing `Assets/Scripts/Core/ObjectPool.cs`; this is the deferred-work item from the Story 1.1 review, now unblocked because 1.6 is the first consumer.*
  - [x] `Get()`: skip/replace **destroyed** idle instances — pop in a loop and Unity-null-check (`item == null`) each popped item, calling `Create()` if it was destroyed while idle (prevents `MissingReferenceException` on `SetActive(true)` after a scene unload).
  - [x] `Return(T item)`: **null-check** (ignore + `GameLog.Warn` if null); **double-return guard** — track idle items in a `HashSet<T>` and ignore + `GameLog.Warn` if the item is already idle (same object handed to two callers is a classic pool corruption bug). `SetActive(false)` is the recycle reset; deeper per-instance reset is the actor's job (its `OnEnable` re-enters `Spawn`).
  - [x] Constructor: guard a **null `_prefab`** (log a clear `GameLog.Error` and no-op the pool) and clamp **negative `prewarm`** to 0.
  - [x] Add `Clear()` that destroys idle instances and empties the stack/set, so the idle stack doesn't leak across scene loads (the doc-comment already promises NFR3 — make it true). `EventManager.OnDestroy` calls it.
  - [x] Keep the public surface the architecture uses (`Get()`, `Return()`, constructor with `prewarm`/`parent`). Do **not** change the generic constraint (`where T : Component`).

- [x] **Task 2 — Create `EventPhase` enum and `ISubject` interface (AC1)**
  - [x] `Assets/Scripts/Events/EventPhase.cs`, namespace **`CameraGame.Events`**: `public enum EventPhase { Spawn, Build, Peak, WindDown, Despawn }`.
  - [x] `Assets/Scripts/Events/ISubject.cs`, namespace **`CameraGame.Events`**: `public interface ISubject { Bounds Bounds { get; } bool IsAtPeak { get; } float TimeToPeak { get; } string SubjectId { get; } }`. 🚨 This is the **decoupling seam** — the grader (1.9–1.10) reads subjects only through this interface, never a concrete event type (architecture §Architectural Boundaries). Keep it dependency-free.

- [x] **Task 3 — Create the `EventDefinition` ScriptableObject (AC1)**
  - [x] `Assets/Scripts/Events/EventDefinition.cs`, namespace **`CameraGame.Events`**, `[CreateAssetMenu(menuName = "CameraGame/Events/Event Definition", fileName = "EventDefinition")]`.
  - [x] Define a nested **`[System.Serializable] struct PhaseConfig`** (or class) with: `[Min(0f)] float duration`, `string animStateName` (empty = no animation this phase), `AudioClip cue` (optional). Provide a **`AnimStateHash`** computed from `animStateName` via `Animator.StringToHash` (cached on first use, or computed in `OnEnable`/`OnValidate`) — mirrors the `AnimHashes`/`CharacterAnimator` no-magic-string pattern.
  - [x] Fields: `string Id` (the `SubjectId` the actor reports), `[Min(0f)] float cueRadius = 25f` (used by Story 1.8; define now), and **five** `PhaseConfig` entries — model as either five named fields (`spawn`, `build`, `peak`, `windDown`, `despawn`) or a `PhaseConfig[5]` array. Expose `PhaseConfig GetPhase(EventPhase phase)` that maps the enum → the right config (the architecture's `definition.GetPhase(next)` call site).
  - [x] **Validation helper**: a `bool IsValid(out string reason)` (or similar) the actor calls in `Awake` — non-empty `Id`, all five phases present, no negative durations. Drives AC3's fail-soft path. Add `[Tooltip]`s on every field.
  - [x] 🚨 **NavMesh route lives in the scene, not the SO** — a route is a set of world positions/Transforms, which a project-asset SO can't reference. Leave route OUT of `EventDefinition` for 1.6; Story 1.7 decides its representation (spawn-point component / waypoint transforms). The 1.6 actor only needs timers + (optional) anim/cue.

- [x] **Task 4 — Create the `EventPeakedChannel` event channel + asset (AC1 seam)**
  - [x] `Assets/Scripts/Events/EventPeakedChannel.cs`, namespace **`CameraGame.Events`**: `public class EventPeakedChannel : EventChannel<ISubject>` with `[CreateAssetMenu(menuName = "CameraGame/Events/Event Peaked Channel", fileName = "EventPeakedChannel")]`. 🚨 **Subclass the existing generic `EventChannel<T>` in `Core`** — same one-line pattern as `ShotCapturedChannel` (Story 1.5). Do not reinvent raise/subscribe.
  - [x] Create the asset at `Assets/Data/Channels/EventPeakedChannel.asset` (the architecture names this channel; `Data/Channels/` already holds `ShotCapturedChannel.asset`). 🚨 MCP can't create custom-SO assets — author the `.asset` YAML directly using the MonoScript GUID from the generated `EventPeakedChannel.cs.meta` (same technique the 1.5 dev notes record). See [[create-so-asset-via-yaml]].
  - [x] The actor raises it **once on entering `Peak`**, passing `this` (the `ISubject`). The reference is **optional/fail-soft** — null channel = simply don't raise (Story 1.7's drunk and future audio hook it; 1.6 just provides the seam).

- [x] **Task 5 — Create the `EventActor` MonoBehaviour (AC1, AC2, AC3)**
  - [x] `Assets/Scripts/Events/EventActor.cs`, namespace **`CameraGame.Events`**, implementing **`ISubject`**. `[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]` (per architecture; both are treated fail-soft below). `using UnityEngine.AI;`.
  - [x] Serialized: `[SerializeField] EventDefinition definition;`, `[SerializeField] EventPeakedChannel eventPeaked;` (optional). Cache `Animator`, `NavMeshAgent`, and a `Renderer` (use `GetComponentInChildren<Renderer>()` — characters are multi-part; **encapsulate all child renderer bounds** so `Bounds` covers the whole subject for grading).
  - [x] `ISubject`: `Bounds Bounds` (encapsulated child-renderer bounds, fail-soft to `new Bounds(transform.position, Vector3.zero)` if none), `bool IsAtPeak => _phase == EventPhase.Peak`, `float TimeToPeak { get; private set; }`, `string SubjectId => definition != null ? definition.Id : name`.
  - [x] **`Awake` fail-soft (AC3):** validate `definition` via `IsValid(out reason)`; if invalid → `GameLog.Error("Events", $"{name}: {reason} — disabling actor.", this); enabled = false; return;`. Cache components. Resolve independent readiness flags: `_animReady = _animator.runtimeAnimatorController != null;` and treat NavMesh as fail-soft (only touch `_agent` when `_agent.isOnNavMesh`).
  - [x] **Lifecycle:** on `OnEnable` (so a pooled re-Get resets cleanly) → if `enabled`, init the FSM: set `TimeToPeak =` (Spawn.duration + Build.duration), `EnterPhase(EventPhase.Spawn)`. In `Update`: `_timer -= Time.deltaTime; TimeToPeak -= Time.deltaTime;` (decrement **continuously**, so `TimeToPeak` goes negative after the peak — grading uses `Mathf.Abs(TimeToPeak)` for the ±0.5 s/±2 s curve; **do not stop decrementing at peak**). When `_timer <= 0f`, `Advance()`.
  - [x] `Advance()`: switch on `_phase`, `Spawn→Build→Peak→WindDown→Despawn`; on `Despawn` raise the `Despawned` event (below) instead of `Destroy` or a manager singleton.
  - [x] `EnterPhase(EventPhase next)`: set `_phase`, read `definition.GetPhase(next)`, `_timer = phase.duration`; **animation fail-soft** — `if (_animReady && phase.AnimStateHash != 0) _animator.CrossFade(phase.AnimStateHash, 0.2f);`; **cue** — `if (phase.cue != null && _cueSource != null) _cueSource.PlayOneShot(phase.cue);` (a 3D `AudioSource` is Story 1.8's concern — keep optional here); if `next == Peak` raise `eventPeaked?.Raise(this)`; fire the local `PhaseChanged` event; `GameLog.Info("Events", $"{SubjectId} → {next}")` (one log per transition — infrequent, not spam).
  - [x] **Decoupled despawn (no singleton):** expose `public event System.Action<EventActor> Despawned;` and a `public event System.Action<EventPhase> PhaseChanged;`. The actor never references `EventManager` — the manager subscribes to `Despawned` and returns it to the pool. 🚨 This is a deliberate improvement over the architecture sketch's `EventManager.Instance.Return(this)` — it honors the project's "avoid heavy singletons; keep cross-system signals decoupled" rule (architecture §Code Organization).
  - [x] 🚨 **Out of scope:** no grading, no gallery, no real Drunk Animator/route, no directional-audio cue rig — those are Stories 1.7–1.11. 1.6 ships the *engine* only.

- [x] **Task 6 — Create the `EventManager` (AC2, AC3)**
  - [x] `Assets/Scripts/Events/EventManager.cs`, namespace **`CameraGame.Events`**, a `MonoBehaviour`.
  - [x] Serialized: `[SerializeField] EventActor actorPrefab;`, `[SerializeField, Min(1)] int maxConcurrent = 1;`, `[SerializeField, Min(0f)] float respawnDelay = 2f;` (tunable cadence for verification), optional `[SerializeField] Transform spawnPoint;`.
  - [x] `Awake`/`Start`: fail-soft validate `actorPrefab` (null → `GameLog.Error` + `enabled = false`). Build `var _pool = new ObjectPool<EventActor>(actorPrefab, prewarm: maxConcurrent, parent: transform);`.
  - [x] **Spawn:** when active count < `maxConcurrent` (and a respawn timer elapses), `Get()` from the pool, position it at `spawnPoint` (or `transform`), **subscribe** to its `Despawned` event, increment active count. **Return path:** the `Despawned` handler **unsubscribes**, `Return(actor)` to the pool, decrements active count, and starts the respawn delay. 🚨 Subscribe on spawn / unsubscribe on return — symmetric, so a pooled actor reused 100× never accumulates handlers (the project's `OnEnable`/`OnDisable` discipline applied to pool lifecycle).
  - [x] `OnDestroy`: call `_pool.Clear()` (Task 1) so idle instances don't leak across scene loads.
  - [x] Keep it the **one** manager — no DI container, no service locator (architecture §Code Organization).

- [x] **Task 7 — Build a temporary verification harness + wire the scene (AC1, AC2, AC3)** — *throwaway scaffolding so the engine can be exercised in Play mode before the real Town Drunk (Story 1.7) exists; clearly marked temporary, like Story 1.5's placeholder shutter clip.*
  - [x] Create a **stub `EventDefinition`** asset `Assets/Data/Events/_StubEvent.asset` (author the YAML directly with the MonoScript GUID, per [[create-so-asset-via-yaml]]): `Id = "StubEvent"`, short phase durations (e.g. 1 s each so a full lifecycle runs in ~5 s), **empty** `animStateName`s and **null** cues (so it runs clean with no Animator controller — proves AC3 fail-soft). Default `cueRadius`.
  - [x] Create a **primitive stub actor prefab** `Assets/Prefabs/Events/EventActor_Stub.prefab`: a small primitive (e.g. a Cube with a `MeshRenderer`) + `NavMeshAgent` + `Animator` (no controller assigned) + the `EventActor` component, with `definition` → `_StubEvent.asset` and `eventPeaked` → `EventPeakedChannel.asset`. (`Prefabs/Events/` is new — create it.)
  - [x] In `Assets/Scenes/SampleScene.unity` (the slice scene used since Story 1.2): add an empty GameObject **`EventManager`**, add the `EventManager` component, assign `actorPrefab` → the stub prefab, `maxConcurrent = 1`, a `spawnPoint` placed **on the baked NavMesh** (so the `NavMeshAgent` doesn't warn — fail-soft, but keep the console clean). Save the scene.
  - [x] 🚨 Note in the prefab/asset names and the Change Log that `_StubEvent`/`EventActor_Stub` are **temporary** and **Story 1.7 replaces them** with the husband prefab + `TownDrunk` EventDefinition. Don't delete the `EventManager` GameObject — 1.7 reuses it.

- [x] **Task 8 — Verify in Play mode + clean console (AC1, AC2, AC3, NFR5)**
  - [x] After every script change, MCP **`refresh_unity`** + **`read_console`** → confirm **zero errors / zero warnings** before continuing (project rule).
  - [x] Enter Play mode: confirm the console logs the lifecycle **`StubEvent → Spawn → Build → Peak → WindDown → Despawn`** in order, then respawns after `respawnDelay`, **looping** — proving the timer FSM and the manager cadence.
  - [x] **Pooling / no-leak (AC2):** in the Hierarchy, confirm **exactly one** `EventActor_Stub` instance exists and it toggles active/inactive each cycle — **no new clones accumulate** across many cycles. (Optionally watch the Profiler/`Object` count stay flat.)
  - [x] **Fail-soft (AC3):** temporarily clear the prefab's `definition` (or point it at an invalid asset) → confirm the actor logs **one** error and disables itself, and the rest of the scene (movement, raise/zoom/capture from 1.3–1.5) keeps working with no `Update` exceptions. Restore the definition after.
  - [x] Confirm **no regressions**: Story 1.3 raise/lower, 1.4 zoom, 1.5 capture, and Move/Look/Sprint/Jump all still behave. URP untouched, console clean.
  - [x] *(MCP can drive Play/stop and read the console; the "lifecycle log order + only-one-instance + fail-soft" confirmation is the close-out check — same MCP-objective-then-confirm pattern as Stories 1.3–1.5.)*

## Dev Notes

### What this story IS — and is NOT
- **IS:** the **reusable event engine** — `EventPhase`, `ISubject`, `EventDefinition` (SO), `EventActor` (timer FSM + pooled return + fail-soft anim/nav), `EventManager` (pooled, concurrency-capped spawner), and the `EventPeakedChannel` seam. Plus **hardening `ObjectPool<T>`** (its first real consumer) and a **throwaway stub** to verify the lifecycle in Play mode.
- **IS NOT:** the actual Town Drunk (husband model + Drunk-Pack Animator + NavMesh route → **Story 1.7**), the directional 3D audio cue rig (**Story 1.8**), grading (**1.9–1.10**), the gallery (**1.11**), or the HUD (**1.12**). No new camera rig; reuse ADR-1's one camera. No asmdef change — `Events` lives in the single `CameraGame.asmdef`.
- This **edits** `ObjectPool.cs` (Story 1.1's deferred hardening) and `SampleScene.unity` (add the EventManager). Everything else under `Scripts/Events/` is new.

### 🚨 Read-first guardrails
1. **Pool, never Instantiate/Destroy in the loop (NFR3, AC2).** The actor returns itself to the pool on `Despawn`; the manager calls `Return()`. The only `Instantiate` is the pool's `Create()` (prewarm + growth); the only `Destroy` is `Clear()` on scene teardown. If you find yourself calling `Destroy(actor)` per cycle, stop — that's the leak the GDD success metric forbids.
2. **No singleton.** The architecture sketch shows `EventManager.Instance.Return(this)`, but the project rule is *avoid heavy singletons; cross-system signals are decoupled*. Use the actor's `Despawned` C# event + the manager subscribing — the actor must not reference `EventManager`. (Local intra-system signals = plain C# `event Action<...>`; that's exactly what `Despawned`/`PhaseChanged` are — architecture §Event System.)
3. **`TimeToPeak` decrements continuously and goes negative after the peak.** The grader's timing curve (Story 1.10) is full within ±0.5 s and 0 by ±2 s of the peak, using `Mathf.Abs(TimeToPeak)`. If you stop decrementing at `Peak` (as the architecture's `Update` sketch does), shots *after* the peak would read as perfectly-timed. Keep counting down through every phase.
4. **Fail-soft is what makes 1.6 testable before 1.7.** Animation and NavMesh are optional in the engine: guard `CrossFade` behind `_animReady && hash != 0`; only touch the `NavMeshAgent` when `isOnNavMesh`; a missing/invalid `EventDefinition` disables the actor with one logged error. This is why the stub (no Animator controller, no route) runs with a clean console (NFR5, NFR8).
5. **`ISubject` is the decoupling boundary.** Grading depends on `ISubject`, never on `EventActor`. Keep the interface in `Events`, keep it free of grading types. (`Events`→`Grading` is already a one-way type reference via `ShotGrade` from Story 1.5; do not create the reverse.)
6. **No magic numbers / strings.** Durations, cue radius, animation **state names**, concurrency cap, respawn delay → `EventDefinition` SO or serialized `[SerializeField]` fields (Inspector-tunable). Animation names become hashes via `Animator.StringToHash` (the `AnimHashes`/`CharacterAnimator` pattern). The category string `"Events"` matches `GameLog` usage already in the codebase.
7. **URP only (NFR4); after every script change call MCP `read_console` (NFR5).** No shaders/materials touched in this story (the stub uses a default primitive material).

### Current state of the files/objects this story touches (read before editing)
- **`Assets/Scripts/Core/ObjectPool.cs`** (EDIT) — 38 lines, `CameraGame.Core`. Minimal `ObjectPool<T> where T : Component`: `_prefab`, `_parent`, `Stack<T> _idle`, ctor with `prewarm`, `Get()`, `Return()`, `Create()`. **No** null guards, **no** double-return detection, **no** destroyed-instance check, **no** `Clear()`. Its own doc-comment already says it exists for EventManager (Story 1.6) and the NFR3 no-leak metric — Task 1 makes that true. Harden it; keep the public surface.
- **`Assets/Scripts/Core/EventChannel.cs`** (no change) — provides **both** `EventChannel` (no payload) and `EventChannel<T>` (typed). Each snapshots handlers, isolates per-handler exceptions, and clears subscribers in `OnDisable` (domain reload). `EventPeakedChannel` is a one-line `EventChannel<ISubject>` subclass; `ShotCapturedChannel` (Story 1.5) is the precedent.
- **`Assets/Scripts/Core/GameLog.cs`** (no change) — `Info/Warn/Error(cat,msg,ctx)/Debug_`. Use `Info` for phase transitions, `Error` for fail-soft, `Warn` for pool misuse.
- **`Assets/Scripts/Core/GameConstants.cs`** / **`AnimHashes.cs`** (no change) — invariants/hashes home. If you ever hard-code an animator parameter, route it here instead. (No new constants strictly required for 1.6 — animation **state** names live in the `EventDefinition` data, not as code constants.)
- **`Assets/Scripts/Events/ShotCapturedChannel.cs`** (no change, reference pattern) — the exact channel-subclass shape to mirror for `EventPeakedChannel`. Note it `using CameraGame.Grading;` for `ShotGrade`; `EventPeakedChannel` instead carries `ISubject` (same assembly).
- **`Assets/Scripts/Grading/ShotGrade.cs`** (no change) — pure `readonly struct`; confirms the dependency direction (`Events` may reference `Grading` types, not vice-versa).
- **`Assets/Scripts/CharacterAnimator.cs`** (no change, pattern only) — shows the project's Animator-drive style (`StringToHash`, fail-soft null checks, `GetComponentInChildren<Animator>()`). The husband's full Animator controller is **Story 1.7 (AR10)** — not this story.
- **`Assets/Scripts/CameraGame.asmdef`** (no change) — one assembly; references `Unity.InputSystem`. It **must** also resolve `UnityEngine.AI` (NavMeshAgent) — that ships with the engine/`AI Navigation` package and is available to the default assembly; if the asmdef has explicit `references`, confirm `NavMeshAgent` resolves (it did for the player's NavMesh-adjacent code; the `AI Navigation` package is installed). Do not add new asmdefs.
- **Scene:** `Assets/Scenes/SampleScene.unity` is the slice scene (Stories 1.2–1.5 all wired here). It has a baked **NavMesh** (Story 1.2) — place the stub `spawnPoint` on it. The PlayerInput object is `main characters` (carries `PhotoModeController`); the EventManager is a **separate** new GameObject — don't attach it to the player.

### Architecture compliance (what the dev MUST follow)
- **Event-Actor Lifecycle (Architecture §Implementation Patterns → Novel #1):** `EventPhase` enum; `EventActor : MonoBehaviour, ISubject` with `[RequireComponent(NavMeshAgent, Animator)]`; `EventDefinition` SO supplying per-phase duration/anim/cue; `EventManager` pools + caps concurrency; `definition.GetPhase(next)` drives `EnterPhase`. [Source: game-architecture.md#Implementation Patterns]
- **State pattern (Architecture §State Patterns):** lightweight **enum** state machine (switch on the enum in `Update`/`Advance`) — *not* a class-based State pattern; 5 phases don't justify it. [Source: game-architecture.md#State Patterns]
- **Entity/pooling (Architecture §Entity Patterns, §Consistency Rules):** prefab + `ObjectPool` via `EventManager`; never `Instantiate`/`Destroy` in the loop. [Source: game-architecture.md#Entity Patterns]
- **Event System (Architecture §Event System):** cross-system = SO event channels (`EventPeakedChannel`); local/intra-system = plain C# `event Action<...>` (`PhaseChanged`, `Despawned`). Naming `<Subject><Verb>` past-tense (`EventPeaked`). Listeners subscribe `OnEnable`/unsubscribe `OnDisable`. [Source: game-architecture.md#Event System]
- **Boundaries (Architecture §Architectural Boundaries):** `Events` may depend on `Core`/`Player`, never the reverse; grading reads `ISubject` only. Data in `Data/`, code in `Scripts/`. [Source: game-architecture.md#Architectural Boundaries]
- **Error handling (Architecture §Error Handling, NFR8):** validate refs in `Awake`; missing config → `LogError` + `enabled = false`; recoverable (no NavMesh path) → `LogWarning` + continue; never throw into `Update`; errors never shown to the player. [Source: game-architecture.md#Error Handling]
- **Configuration (Architecture §Configuration):** tunables (durations, cue radius, concurrency, cadence) in SO/serialized fields, no code literals. [Source: game-architecture.md#Configuration]
- **Consistency Rules:** cache component refs in `Awake` (no `GetComponent` in `Update`); pooled objects; magic strings via constants/`StringToHash`; debug behind `GameLog.Debug_`. [Source: game-architecture.md#Consistency Rules]

### Previous story intelligence (Stories 1.1–1.5)
- **From 1.1 (directly relevant):** `Core` scaffold already exists — `GameLog`, `GameConstants`, `AnimHashes`, `ObjectPool<T>`, `EventChannel`/`EventChannel<T>`. **Do not recreate them.** Two **deferred-work** items land in *this* story: (a) **`ObjectPool<T>` hardening** — explicitly deferred to "Story 1.6 against the real EventManager usage" (Task 1); (b) the `AnimHashes.Speed` vs `CharacterAnimator` duplicate is a *different* later cleanup (when `CharacterAnimator` migrates) — **not** 1.6's job, leave it.
- **From 1.5:** the channel-subclass pattern (`ShotCapturedChannel : EventChannel<ShotGrade>` + asset) is the exact template for `EventPeakedChannel`. The **MCP-can't-create-custom-SO-assets** lesson is back: author `EventPeakedChannel.asset`, `_StubEvent.asset` YAML by hand using the `.cs.meta` MonoScript GUIDs ([[create-so-asset-via-yaml]]). Independent fail-soft guards (one missing ref doesn't disable unrelated features) are the praised pattern — apply to anim vs nav vs cue here.
- **From 1.4/1.3:** fail-soft `_xxxReady` flags resolved once in `Awake`, early-returns in `Update`. The 1.4 deferred note "re-validate a ref in `Update` if it can be destroyed at runtime" is **forward-looking** (additive scenes, Epic 5) — not triggered by 1.6's single scene, but the actor's `isOnNavMesh` check is the same spirit.
- **From 1.2:** the slice scene has a **baked NavMesh**, but the deferred note warns the agent dimensions are aggressive vs. the fine/large-scale world geometry, and "character too tall / world scale" is unresolved. **For 1.6 this doesn't bite** — the stub doesn't path (no route; timers only). It becomes relevant in **Story 1.7** when the drunk actually walks a route (the deferred note already flags re-verifying the NavMesh agent there). Just place the 1.6 stub spawn point on the existing NavMesh so the agent doesn't warn.
- **Established discipline:** existing scripts (`ThirdPersonController/Camera`, `CharacterAnimator`) are intentionally **not** renamed/moved (brownfield serialized-reference safety) — add alongside. After any script change, MCP `read_console` must be clean before "done".

### Latest tech notes (Unity 6.3 / AI Navigation / Input System 1.18)
- **`NavMeshAgent`** (namespace `UnityEngine.AI`, from the installed **AI Navigation** package) logs *"Failed to create agent because it is not close to the NavMesh"* on enable if its GameObject is active off-mesh. Two clean options: (a) spawn the stub on the baked NavMesh; (b) only enable/drive the agent when `_agent.isOnNavMesh`. Use both for a warning-free console (NFR5).
- **`Animator.CrossFade(int stateHashName, float normalizedTransitionDuration)`** warns if the state hash isn't in the controller — and **throws/no-ops** with no `runtimeAnimatorController`. Guard with `_animReady && hash != 0` so the engine runs animator-less in 1.6.
- **`GeometryUtility` / bounds** (for grading, 1.9) read `ISubject.Bounds` — encapsulating **all child renderers** now means the husband's whole body is gradable later without revisiting `EventActor`.
- **`AudioSource.PlayOneShot(clip)`** for cues; the **3D directional** cue source (spatial blend, ~25 m rolloff) is Story 1.8 — keep the 1.6 cue hook optional/2D-agnostic.

### Testing standards (Architecture §Testing / project norm)
- Quality gates are **manual Play-mode** verification + **MCP `read_console`** clean console (NFR5). The dedicated **Tests assembly was deferred** in Story 1.1 (Task 6), so no EditMode/PlayMode unit test is *required* for "done" — the Task 7 stub harness is the Play-mode proof.
- *Optional, cheap insurance:* the FSM phase-advance order and `EventDefinition.GetPhase` mapping are pure/deterministic and would make a clean EditMode test **if** you stand up the Tests asmdef — but that's gold-plating for 1.6; don't block on it. The architecture lists "lifecycle timing" as a future EditMode test target.

### Project Structure Notes
- **New:** `Scripts/Events/EventPhase.cs`, `ISubject.cs`, `EventDefinition.cs`, `EventPeakedChannel.cs`, `EventActor.cs`, `EventManager.cs` (all `CameraGame.Events`); `Data/Channels/EventPeakedChannel.asset`; `Data/Events/_StubEvent.asset`; `Prefabs/Events/EventActor_Stub.prefab` (+ new `Prefabs/Events/` folder).
- **Edited:** `Scripts/Core/ObjectPool.cs` (hardening), `Scenes/SampleScene.unity` (add `EventManager` GameObject + spawn point).
- Paths match the Architecture source-tree diagram (`Scripts/Events/`, `Data/{Events,Channels}`, `Prefabs/Events/`). No asmdef change.

### Project Context Rules
- No `project-context.md` exists; rules are drawn from CLAUDE.md and the architecture.
- **MCP for Unity is the toolchain:** create/edit scripts, build the stub prefab, wire the scene through Unity MCP; author custom-SO `.asset` YAML directly (MCP can't create them) — [[create-so-asset-via-yaml]]. After any script change call `read_console` for zero errors (project rule, NFR5).
- **URP only (NFR4):** never introduce the Standard shader.
- **Jira sync (CLAUDE.md):** this story maps to **KAN-17** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-17, mirror this Tasks/Subtasks breakdown as Jira **Subtasks** under KAN-17 (each with the **"In plain terms (for non-developers):"** comment), and update the `jiraSync:` block in `epics.md`.

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.6] — ACs: data-driven lifecycle FSM, `EventDefinition` SO, `EventManager`, pool, concurrency=1, no leaks, fail-soft on bad config; `ISubject` (`IsAtPeak`, `TimeToPeak`).
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR9 (event lifecycle), AR2 (data-driven EventActor + EventDefinition + pooled EventManager), NFR3 (no object leaks / pooling), NFR6 (concurrency cap 1), NFR8 (fail-soft), NFR5 (clean console), NFR4 (URP).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Implementation Patterns] — Novel Pattern #1 Event-Actor Lifecycle (full code sketch: `EventActor`, `EnterPhase`, `Advance`, `GetPhase`).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Event System] — channels vs C# events; `EventPeakedChannel`; `<Subject><Verb>` naming; subscribe/unsubscribe lifecycle.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Architectural Boundaries] — `ISubject` decoupling; dependency direction; one asmdef; data in `Data/`.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Error Handling] — validate in `Awake`, `LogError` + `enabled = false`, never throw into `Update`.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Configuration] — tunables in SO/serialized, no code literals.
- [Source: Assets/Scripts/Core/ObjectPool.cs] — the pool to harden (its doc-comment names Story 1.6 + NFR3).
- [Source: Assets/Scripts/Core/EventChannel.cs] — generic channel to subclass for `EventPeakedChannel`.
- [Source: Assets/Scripts/Events/ShotCapturedChannel.cs] — the channel-subclass + asset precedent (Story 1.5).
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — **ObjectPool<T> hardening** explicitly deferred to Story 1.6 (the action item Task 1 closes); the 1.2 NavMesh-agent note that re-verifies in Story 1.7 (not 1.6).
- [Source: _bmad-output/implementation-artifacts/1-5-capture-a-photo.md] — channel pattern, custom-SO-YAML technique, independent fail-soft guards.
- [Source: project memory `create-so-asset-via-yaml`] — author custom ScriptableObject `.asset` YAML by hand using the script's `.cs.meta` GUID (MCP can't create them).

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (claude-opus-4-8[1m]) — Claude Code dev-story workflow

### Debug Log References

- MCP `read_console` after every script change: zero compilation errors/warnings throughout. The only
  console entries observed in any session were two **pre-existing, unrelated** warnings: the Unity
  Version Control project-link notice (collab-proxy infra) and `ThirdPersonCamera.cs:43` "POV Camera:
  Head bone not found!". Neither is touched by Story 1.6. URP untouched (NFR4).
- **Verification-environment note:** this MCP `read_console` build did **not** surface `GameLog`
  `Debug.Log`/`Debug.LogError` output (it only ever returned the two warnings above), so Play-mode
  verification was done by polling live runtime **state** via the `mcpforunity://scene/gameobject/...`
  resource rather than by reading log lines. State-based checks are conclusive (see Completion Notes).
- The Editor's player loop is throttled while the Editor window is unfocused with `runInBackground`
  off (it always is under MCP-driven play), which froze `Update`. A clearly-marked **temporary**
  `Application.runInBackground = true` line was added to `EventManager.Awake` only to observe the
  lifecycle headlessly, then **removed**; `ProjectSettings/ProjectSettings.asset` was briefly toggled
  and **reverted** to its original `runInBackground: 0`. No net change to either.

### Completion Notes List

**All ACs satisfied — verified in Play mode via runtime state polling.**

- **AC1 (data-driven FSM + ISubject):** With the valid `_StubEvent` definition (1 s per phase), the
  single pooled actor advanced on its own timers. Observed `TimeToPeak` counting down from +2.0
  (= Spawn+Build) through 0 and into negative values, and `IsAtPeak == true` captured at
  `TimeToPeak ≈ -0.42` (inside the Peak window) — proving phase advance reaches Peak and that
  `TimeToPeak` **keeps decrementing past the peak** (guardrail #3). `Bounds` correctly encapsulated the
  cube renderer (center 5,20,0 / size 1,1,1); `SubjectId` reported "StubEvent" from the SO. The same
  instance was seen cycling Despawn→respawn→back to Peak, confirming the full loop.
- **AC2 (pooled, concurrency-capped, leak-free):** Across ~3 lifecycle loops there was **exactly one**
  `EventActor_Stub(Clone)` in the Hierarchy (same instance ID reused), parented under `EventManager` —
  no clones accumulated. `maxConcurrent = 1` enforced. No `Instantiate`/`Destroy` in the loop (pool
  `Get`/`Return` only).
- **AC3 (fail-soft):** Pointing the stub at a missing `EventDefinition` (definition = None) → the actor
  **did not cycle** (`TimeToPeak` stayed 0), fell back gracefully (`SubjectId` → object name, `Bounds`
  → zero-size point at its position), and the rest of the scene kept running with **no thrown
  exceptions / no new errors**. The disable path (the only place `enabled = false` is set, immediately
  after the single `GameLog.Error`) executed. Independent fail-soft confirmed via the component dump:
  `NavMeshAgent.enabled = false` / `isOnNavMesh = false` produced **no** NavMesh warning, and
  `Animator.runtimeAnimatorController = null` produced **no** animator warning — the engine ran clean
  with neither a controller nor an on-mesh agent (exactly what lets 1.6 be tested before 1.7).
- **No regressions:** scene unchanged except the added `EventManager` + `SpawnPoint`; console clean
  (only the two pre-existing warnings); URP untouched.

**Deliberate deviations (documented for the reviewer / Story 1.7):**
1. **Stub `NavMeshAgent` left disabled** (`m_Enabled: 0`) instead of relying solely on placing the
   spawn point on the baked NavMesh. The 1.6 engine never paths (no route until 1.7), and a disabled
   agent cannot emit the "not close to the NavMesh" warning — this **guarantees** the clean console
   (NFR5) regardless of the spawn coordinate. `RequireComponent` is still satisfied (the agent exists).
   **Story 1.7** should enable/configure the agent when it introduces the real route, and place the
   spawn on the baked NavMesh.
2. **Decoupled despawn (no singleton):** the actor raises a C# `Despawned` event and the manager
   subscribes/returns it to the pool — per guardrail #2, intentionally diverging from the architecture
   sketch's `EventManager.Instance.Return(this)`.
3. `SpawnPoint` is at world `(5, 20, 0)` (next to the player start `(0,20,0)`); with the agent disabled
   its exact on-mesh status doesn't matter for 1.6. **Story 1.7 should re-verify** spawn placement on
   the NavMesh when the drunk actually walks a route.

**Temporary scaffolding (per Task 7) — replaced by Story 1.7:** `Assets/Data/Events/_StubEvent.asset`
and `Assets/Prefabs/Events/EventActor_Stub.prefab` are throwaway. Do **not** delete the `EventManager`
GameObject — 1.7 reuses it (swap `actorPrefab` → husband prefab, add a `TownDrunk` EventDefinition).

### File List

**New (code — all `CameraGame.Events`):**
- `Assets/Scripts/Events/EventPhase.cs`
- `Assets/Scripts/Events/ISubject.cs`
- `Assets/Scripts/Events/EventDefinition.cs`
- `Assets/Scripts/Events/EventPeakedChannel.cs`
- `Assets/Scripts/Events/EventActor.cs`
- `Assets/Scripts/Events/EventManager.cs`

**New (assets):**
- `Assets/Data/Channels/EventPeakedChannel.asset`
- `Assets/Data/Events/_StubEvent.asset` *(temporary — Story 1.7)*
- `Assets/Prefabs/Events/EventActor_Stub.prefab` *(temporary — Story 1.7; + new `Assets/Prefabs/`, `Assets/Prefabs/Events/` folders)*

**Edited:**
- `Assets/Scripts/Core/ObjectPool.cs` — hardening (destroyed-idle skip, null/double-return guards, null-prefab + negative-prewarm guards, `Clear()`)
- `Assets/Scenes/SampleScene.unity` — added `EventManager` GameObject (with `EventManager` component, `actorPrefab` → stub, `maxConcurrent = 1`, `respawnDelay = 2`) and child `SpawnPoint`

*(Unity-generated `.meta` files accompany every new asset/script/folder above.)*

### Change Log

| Date | Change |
|------|--------|
| 2026-06-18 | Implemented the event-actor lifecycle engine (Story 1.6): hardened `ObjectPool<T>`; added `EventPhase`, `ISubject`, `EventDefinition` (SO), `EventPeakedChannel` (+ asset), `EventActor` (timer FSM, fail-soft anim/nav/cue, pooled decoupled despawn), `EventManager` (pooled, concurrency-capped spawner). Built throwaway `_StubEvent` + `EventActor_Stub` and wired `EventManager` into `SampleScene`. Verified all ACs in Play mode via runtime-state polling (clean console). Status → review. |

### Review Findings

_Three adversarial layers ran at Opus 4.8 (Blind Hunter, Edge Case Hunter, Acceptance Auditor) on commit `0d3ae89`. **The Acceptance Auditor confirmed all 3 ACs PASS, all 8 tasks delivered, and all 3 documented deviations true in the assets (disabled stub agent, decoupled `Despawned`, spawn placement); URP/NFR4 intact.** From 23 raw findings: 1 decision, 5 patches, 2 deferred, 8 dismissed (noise / PASS-confirmations). Nothing blocks 1.6's stated scope — the engine runs clean — but several items are latent traps that bite the moment Story 1.7 wires a real animator/cue/route, or the grader (1.9–1.10) starts reading subjects. "Confirmed by N layers" = independent convergence (higher confidence)._

_**Applied 2026-06-19:** the decision and all 5 patches were applied to the engine and verified with a clean Unity recompile (`read_console`: 0 errors, 0 new warnings — only the pre-existing Unity Version Control link notice remains). Lifecycle init moved from `OnEnable` to a manager-driven `Begin()` (+ a `_running` gate that also latches the single Despawn); `EnterPhase` now carries timer overshoot; `IsValid` rejects a non-positive Peak; renderers use `includeInactive`; `EventManager` stands down on a disabled pooled actor; `ISubject` documents the live-read contract. The 2 deferred items stay logged for Story 1.7 / Epic 5._

**Decision needed** (resolve before patching):

- [x] [Review][Decision → applied] `ISubject` grading-seam has no liveness/refresh contract — a grader that caches the `ISubject` from the `EventPeaked` payload reads **stale `Bounds`** after the pooled actor despawns and is reused (pooling ⇒ the reference is never null, so the usual "subject gone" guard never trips); and `_renderers` is cached once in `Awake` via `GetComponentsInChildren<Renderer>()`, which excludes inactive children and never sees renderers added at runtime (props/equipment at Peak). [Assets/Scripts/Events/EventActor.cs → Bounds] — 1.6 establishes this seam, so decide now: **(a)** grading reads subjects live via the `EventPeaked` channel at capture (don't cache the ref) + switch to `GetComponentsInChildren<Renderer>(true)` [recommended, cheap]; **(b)** add an `IsAlive`/generation token to `ISubject`; **(c)** accept as-is and revisit in Story 1.9. (Source: edge — 2 related findings.)

**Patches** (code fixes, no product decision needed):

- [x] [Review][Patch] Prewarm + `OnEnable`-driven init fires a spurious lifecycle start at boot; will misfire the Spawn cue/anim at the un-positioned prefab location once 1.7 wires them [Assets/Scripts/Events/EventActor.cs → OnEnable/EnterPhase · Assets/Scripts/Core/ObjectPool.cs → Create/Get · EventManager → Spawn] — the active stub prefab makes `Instantiate` run `Awake`+`OnEnable` (→ `EnterPhase(Spawn)` → log/cue/anim) *before* the pool's `SetActive(false)` and before the manager repositions the actor. Today: one harmless extra `StubEvent → Spawn` log at boot (stub cue is null). Fix: instantiate pooled instances inactive (inactive prefab root) and start the FSM via an explicit `manager → actor.Begin(position)` after positioning, not in `OnEnable`. Confirmed by 3 layers (blind+auditor).
- [x] [Review][Patch] Manager can't cope with a self-disabled / non-advancing pooled actor → a concurrency slot pins forever and spawning wedges [Assets/Scripts/Events/EventManager.cs → Awake · EventActor.cs → Awake/OnEnable] — if `actorPrefab` points at an invalid `EventDefinition`, the actor disables itself in `Awake` (`enabled=false`, per AC3) and `OnEnable` never fires on re-`Get()` (Unity skips `OnEnable` on a disabled component), so it never raises `Despawned`; `_activeCount` only decrements on `Despawned`, so it pins at the cap. Fix: `EventManager.Awake` validates `actorPrefab`'s definition (symmetric to the actor's own check) and disables itself with one clear `GameLog.Error` if invalid. Confirmed by 2 layers (edge+blind); Unity semantics corroborated by the auditor.
- [x] [Review][Patch] `IsValid` accepts a zero-duration Peak → `IsAtPeak` is true for a single frame, which a poll-based grader can miss [Assets/Scripts/Events/EventDefinition.cs → IsValid] — `IsValid` only rejects `< 0`; `peak.duration == 0` yields a zero-width peak window (and seeds `TimeToPeak = 0`). Fix: require `peak.duration > 0` (other phases may legitimately be 0), and have grading consume the reliable single `EventPeaked` raise rather than polling `IsAtPeak`. Confirmed by 2 layers (blind+edge).
- [x] [Review][Patch] Per-phase timer overshoot is discarded and `TimeToPeak` runs on an independent raw-`deltaTime` clock → systematic ~1–2 frame drift between `IsAtPeak` and `TimeToPeak == 0`, worse on a frame hitch [Assets/Scripts/Events/EventActor.cs → Update/EnterPhase] — `EnterPhase` resets `_timer = phase.duration` without carrying the negative remainder, and `Advance` runs at most once per frame (a big `deltaTime` skips a phase). Small for 1.6's 1 s stub, but it biases the Story 1.10 timing grader. Fix: carry the remainder (`_timer += phase.duration`) and/or derive `TimeToPeak` from accumulated phase time. Confirmed by 2 layers (blind+edge).
- [x] [Review][Patch] `Despawned` has no fire-once latch — re-entrant double-fire once a 2nd subscriber or a deferred return is added (1.7 adds subscribers) [Assets/Scripts/Events/EventActor.cs → Advance] — today's only protection is the synchronous `Return()`→`SetActive(false)`; if anything delays deactivation, the Despawn-state `Advance` re-invokes `Despawned` every frame → `HandleDespawned` runs twice → `_activeCount` double-decrements → spawn over cap. Fix: latch despawn so `Despawned` fires exactly once per lifecycle. (Source: edge.)

**Deferred** (real but not actionable in 1.6 — also logged in `deferred-work.md`):

- [x] [Review][Defer] NavMesh fail-soft guard (`isOnNavMesh`) owed but not yet written; cached `_agent` field currently unused; re-verify stub spawn placement on the baked NavMesh [Assets/Scripts/Events/EventActor.cs] — deferred, Story 1.7 owns it (vacuously fail-soft in 1.6 — no agent interaction at all).
- [x] [Review][Defer] Dispensed (active) actors aren't tracked by the pool — under additive-scene unload / `DontDestroyOnLoad` an active actor can be orphaned (dangling `Despawned` delegate + pinned `_activeCount`) [Assets/Scripts/Core/ObjectPool.cs → Clear · EventManager → OnDestroy] — deferred, not triggered by 1.6's single-scene teardown (Epic 5 additive scenes).
