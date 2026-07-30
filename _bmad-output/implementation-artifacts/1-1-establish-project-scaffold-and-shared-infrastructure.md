# Story 1.1: Establish project scaffold and shared infrastructure

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->
<!-- DEV: All files implemented 2026-06-04. AC3 verified 2026-06-04 via Unity MCP read_console (zero errors, clean domain reload). Status flipped to "review". -->>

## Story

As a developer,
I want the project's folder/namespace structure and shared infrastructure in place,
so that every system I build afterward drops into a known home and compiles cleanly.

## Acceptance Criteria

**AC1 — Folder & assembly structure**
- `Assets/Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}` and `Assets/Data/{Events, Grading, Camera, Channels}` folders exist.
- A single `CameraGame.asmdef` lives at `Assets/Scripts/` with root namespace `CameraGame` and `CameraGame.*` namespaces in code.
- `Editor/` is a **separate** assembly (`CameraGame.Editor.asmdef`, Editor-platform-only). Tests assemblies are set up separately *or* deferred until tests exist (no test code in this story).
- **Regression-safety:** the existing scripts (`ThirdPersonController`, `ThirdPersonCamera`, `CharacterAnimator`, `Editor/AddMeshColliders`) still compile unchanged after the asmdefs are introduced.

**AC2 — Shared infrastructure in `Core`**
- `GameLog` (categorized `Debug.Log` helper), `GameConstants` and `AnimHashes` (no magic strings), `ObjectPool<T>`, and a ScriptableObject **event-channel base type** exist in `Scripts/Core/`, compile, and are namespaced `CameraGame.Core`.

**AC3 — Clean build (NFR4, NFR5)**
- After compilation, the Unity console shows **zero compile errors** (verified via MCP `read_console`).
- No new Standard-shader/URP warnings are introduced (this story adds no shaders/materials — so the baseline warning set must be unchanged).

## Tasks / Subtasks

- [x] **Task 1 — Create folder structure (AC1)**
  - [x] Create `Assets/Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}` (keep existing `Editor/`).
  - [x] Create `Assets/Data/{Events, Grading, Camera, Channels}` (these hold SO *instances* created in later stories; empty now is fine).
  - [x] Do **NOT** move or rename the existing scripts in this story — existing scripts left untouched. ✅

- [x] **Task 2 — Create the main assembly definition (AC1, regression-safety)**
  - [x] Created `Assets/Scripts/CameraGame.asmdef` referencing `Unity.InputSystem` (protects `ThirdPersonController.cs`).
  - [x] Set `"rootNamespace": "CameraGame"`.

- [x] **Task 3 — Create the Editor assembly (AC1, build-safety)**
  - [x] Created `Assets/Scripts/Editor/CameraGame.Editor.asmdef` (`includePlatforms: ["Editor"]`, references `CameraGame`) — keeps `AddMeshColliders.cs` editor-only.

- [x] **Task 4 — Implement `Core` shared infrastructure (AC2)**
  - [x] `Scripts/Core/GameLog.cs` — static categorized logger.
  - [x] `Scripts/Core/GameConstants.cs` — static invariants (input action names; tag/layer stubs).
  - [x] `Scripts/Core/AnimHashes.cs` — cached `Animator.StringToHash` ids (seeded with `Speed`).
  - [x] `Scripts/Core/ObjectPool.cs` — generic `ObjectPool<T> where T : Component`.
  - [x] `Scripts/Core/EventChannel.cs` — `EventChannel` + generic `EventChannel<T>`.

- [x] **Task 5 — Verify clean compile (AC3)** ✅ VERIFIED 2026-06-04
  - [x] Trigger a Unity asset refresh/recompile (via MCP for Unity). — *`refresh_unity` (force, scripts, compile=request); domain reload completed clean.*
  - [x] Call MCP `read_console`; confirm **zero errors** and no *new* warnings. — *0 errors; only pre-existing Plastic SCM repo-name notice present (not a shader/URP warning, not from this story).*

- [ ] **Task 6 *(OPTIONAL, recommended)* — Tests assembly scaffold** — *Deferred (no tests authored this story; avoids an empty test asmdef).*

## Dev Notes

### What this story is — and is NOT
- **IS:** create empty homes (folders), the assembly boundaries (asmdefs), and the 5 `Core` primitives. Pure foundation.
- **IS NOT:** any gameplay behavior, any ScriptableObject *instances*, any renames of existing classes, any UI/shaders. Those are later stories. Resist scope creep.

### 🚨 Regression guardrails (READ FIRST — this is a brownfield project)
1. **Assembly reference — Input System is mandatory.** `Assets/Scripts/ThirdPersonController.cs` has `using UnityEngine.InputSystem;`. Adding `CameraGame.asmdef` at `Assets/Scripts/` makes that script part of the `CameraGame` assembly. If the asmdef does not reference `Unity.InputSystem`, you get `CS0246: The type or namespace 'InputSystem' could not be found`. **The asmdef in Task 2 already includes this reference — do not remove it.**
2. **Editor code must stay in an Editor-only assembly.** `Assets/Scripts/Editor/AddMeshColliders.cs` has `using UnityEditor;`. With no asmdef, Unity auto-compiles `Editor/` into the editor-only `Assembly-CSharp-Editor`. Once a root `CameraGame.asmdef` exists, that auto-magic stops and `Editor/` would fold into `CameraGame` (all-platforms) → `UnityEditor` is unavailable in player builds → **build break**. The `CameraGame.Editor.asmdef` in Task 3 fixes this. Verify `AddMeshColliders` still compiles after.
3. **Do NOT rename `ThirdPersonController` / `ThirdPersonCamera` / `CharacterAnimator` classes here.** The architecture suggests renaming to `Player*`/`FirstPerson*`, but that is explicitly *optional, non-blocking cleanup* (architecture §"Minor Observations"). Renaming a MonoBehaviour **class** breaks the serialized component references on prefabs/scenes (the `.meta` GUID survives a file move, but the serialized class name does not survive a class rename) — a silent regression right before Epic 1. Leave them as-is. *(Moving the files into `Scripts/Player/` is GUID-safe and allowed, but optional and not required by any AC — skip it to keep this story tight.)*
4. **`NavMeshAgent` / AI Navigation reference is NOT needed yet.** `NavMeshAgent` is in the always-available `UnityEngine.AIModule`; the `Unity.AI.Navigation` package assembly (NavMeshSurface/runtime bake) is only needed by Stories 1.2/1.7. Add that asmdef reference *when those stories need it*, not now — keep this assembly's reference list minimal.

### File: `Assets/Scripts/CameraGame.asmdef` (Task 2)
```json
{
  "name": "CameraGame",
  "rootNamespace": "CameraGame",
  "references": [
    "Unity.InputSystem"
  ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```
> `includePlatforms: []` = all platforms (correct for runtime gameplay code). `references` uses assembly *names* (Unity 6 resolves these); if the editor writes GUID refs instead, that's also fine.

### File: `Assets/Scripts/Editor/CameraGame.Editor.asmdef` (Task 3)
```json
{
  "name": "CameraGame.Editor",
  "rootNamespace": "CameraGame.Editor",
  "references": [
    "CameraGame"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "autoReferenced": true,
  "noEngineReferences": false
}
```

### File: `Assets/Scripts/Core/GameLog.cs` (Task 4) — use verbatim (architecture §Logging)
```csharp
using UnityEngine;
using Object = UnityEngine.Object;

namespace CameraGame.Core
{
    /// <summary>Thin categorized wrapper over Debug.Log*. Keeps the console clean and greppable.</summary>
    public static class GameLog
    {
        public static void Info(string cat, string msg)  => Debug.Log($"[{cat}] {msg}");
        public static void Warn(string cat, string msg)  => Debug.LogWarning($"[{cat}] {msg}");
        public static void Error(string cat, string msg, Object ctx = null) => Debug.LogError($"[{cat}] {msg}", ctx);

        // Stripped from release builds — only compiles in Editor / development builds.
        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public static void Debug_(string cat, string msg) => Debug.Log($"[{cat}:DBG] {msg}");
    }
}
```

### File: `Assets/Scripts/Core/GameConstants.cs` (Task 4)
```csharp
namespace CameraGame.Core
{
    /// <summary>True invariants (no magic strings). Tunable numbers belong in ScriptableObjects, NOT here.</summary>
    public static class GameConstants
    {
        // New Input System "Player" action map — action names (Send-Messages handler names derive from these).
        public static class InputActions
        {
            public const string Move        = "Move";
            public const string Look        = "Look";
            public const string Sprint      = "Sprint";
            public const string Jump        = "Jump";
            public const string Capture     = "Capture";      // repurposed from "Attack" in Story 1.5
            public const string RaiseCamera = "RaiseCamera";  // added in Story 1.3
            public const string Zoom        = "Zoom";         // added in Story 1.4
        }

        // Layers / tags — fill in as systems need them (kept here to kill magic strings).
        public static class Tags { /* e.g. public const string Subject = "Subject"; */ }
        public static class Layers { /* e.g. public const string Occluder = "Occluder"; */ }
    }
}
```
> Only `Move/Look/Sprint/Jump` exist in the input asset today; `Capture/RaiseCamera/Zoom` are placeholders consumed by Stories 1.3–1.5. Defining the names now keeps later stories magic-string-free.

### File: `Assets/Scripts/Core/AnimHashes.cs` (Task 4)
```csharp
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>Cached Animator parameter hashes (faster + no magic strings). Add per story as needed.</summary>
    public static class AnimHashes
    {
        public static readonly int Speed = Animator.StringToHash("Speed"); // used by CharacterAnimator
    }
}
```

### File: `Assets/Scripts/Core/ObjectPool.cs` (Task 4)
```csharp
using System.Collections.Generic;
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>
    /// Minimal prefab-based pool for Components (e.g. EventActor). Used by EventManager (Story 1.6)
    /// so gameplay never Instantiate/Destroys in a loop — satisfies the GDD no-object-leak metric (NFR3).
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _idle = new();

        public ObjectPool(T prefab, int prewarm = 0, Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < prewarm; i++) { var item = Create(); item.gameObject.SetActive(false); _idle.Push(item); }
        }

        public T Get()
        {
            T item = _idle.Count > 0 ? _idle.Pop() : Create();
            item.gameObject.SetActive(true);
            return item;
        }

        public void Return(T item)
        {
            item.gameObject.SetActive(false);
            _idle.Push(item);
        }

        private T Create() => Object.Instantiate(_prefab, _parent);
    }
}
```
> Note: Unity ships `UnityEngine.Pool.ObjectPool<T>` (closure-based). The architecture specifies a Core pool with `Get()/Return(item)` semantics for *prefab-instanced Components*; this thin class matches the `EventManager` usage in the architecture (`_pool.Get()` / `_pool.Return(actor)`) and is clearer for that case. Do not also add a second pooling utility — this is the one.

### File: `Assets/Scripts/Core/EventChannel.cs` (Task 4) — SO event-channel base (architecture §Event System)
```csharp
using System;
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>Base ScriptableObject event channel with no payload (e.g. a future EventPeaked-style ping).</summary>
    public abstract class EventChannel : ScriptableObject
    {
        public event Action Raised;
        public void Raise() => Raised?.Invoke();
    }

    /// <summary>Base ScriptableObject event channel carrying a typed payload.
    /// Concrete channels subclass this, e.g. <c>ShotCapturedChannel : EventChannel&lt;ShotGrade&gt;</c> (Story 1.5/1.11).</summary>
    public abstract class EventChannel<T> : ScriptableObject
    {
        public event Action<T> Raised;
        public void Raise(T payload) => Raised?.Invoke(payload);
    }
}
```
> Listeners subscribe in `OnEnable`, unsubscribe in `OnDisable` (architecture §Communication Patterns). Concrete channels (`ShotCapturedChannel`, `EventPeakedChannel`) and their payload types are created in later stories — do **not** create them here (their payload types like `ShotGrade` don't exist yet).

### Dependency direction (architecture §Architectural Boundaries — keep it honest from day one)
- `Core` depends on **nothing** game-specific. The 5 files above use only `UnityEngine` / BCL. ✅
- Later: feature folders may depend on `Core` and `Player`, never the reverse; `UI` depends on `PhotoMode`/`Grading`, not vice versa. (Single assembly for now, so this is a discipline rule, not compiler-enforced yet.)

### Testing standards
- No gameplay to test in this story. `ObjectPool<T>` is the one unit-testable primitive — an optional EditMode test (Task 6) of get/return/reuse is welcome but not required for "done".
- The binding quality gate here is **AC3**: clean compile via MCP `read_console`. Per the project rule, run `read_console` after the recompile and fix everything before marking done.

### References
- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.1] — acceptance criteria.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Code Organization] — namespaces, folders, single asmdef, Editor/Tests separate.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Project Structure / Directory Structure] — exact folder tree, `Scripts/Core` contents.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Logging] — `GameLog` verbatim code.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Configuration] — `GameConstants`/`AnimHashes` rationale (invariants vs SO).
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Event System] — event-channel pattern + naming.
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Entity Patterns] — pool usage (`_pool.Get()/Return()`).
- [Source: Assets/Scripts/ThirdPersonController.cs:2] — `using UnityEngine.InputSystem;` → asmdef must reference `Unity.InputSystem`.
- [Source: Assets/Scripts/Editor/AddMeshColliders.cs:2] — `using UnityEditor;` → needs Editor-only asmdef.

### Project Context Rules
- No `project-context.md` exists in this project; rules below are drawn from `CLAUDE.md` and the architecture.
- **URP only (NFR4):** never use the Standard shader. This story adds no materials/shaders — keep it that way.
- **MCP for Unity is the toolchain:** create scripts/assets and verify via MCP; after every script change call `read_console` to confirm zero compile errors before continuing (project rule, NFR5).
- **Fail-soft (NFR8):** the `GameLog.Error` + graceful-disable pattern is the project standard for missing references (relevant to later stories; `Core` here just provides the logger).
- **Jira sync (CLAUDE.md):** this story maps to Jira issue **KAN-12** (under Epic 1 = KAN-4). When its status changes, reflect it on KAN-12.

## Dev Agent Record

### Agent Model Used

Claude Opus 4.8 (1M context) — `claude-opus-4-8[1m]`

### Debug Log References

- N/A (no runtime debugging this story; scaffold only).

### Completion Notes List

- Ultimate context engine analysis completed — comprehensive developer guide created. Brownfield assembly hazards (Input System reference, Editor-only assembly) pre-identified with exact asmdef contents to prevent compile/build regressions.
- **2026-06-04 implementation:** Created the full folder scaffold, both asmdefs, and all 5 `Core` scripts exactly per spec. Existing scripts (`ThirdPersonController`, `ThirdPersonCamera`, `CharacterAnimator`, `Editor/AddMeshColliders`) left untouched — no rename/move (GUID-safe).
- `CameraGame.asmdef` references `Unity.InputSystem` so `ThirdPersonController` keeps compiling under the new assembly; `CameraGame.Editor.asmdef` is Editor-only so `AddMeshColliders` stays out of player builds.
- ✅ **AC3 VERIFIED (2026-06-04):** Unity MCP (`UnityMCP`, instance `My project@38a21361`, Unity 6000.3.8f1) reconnected. Ran `refresh_unity` (force/scripts/compile) → domain reload completed clean (`is_compiling: false`). `read_console` reported **0 errors**. Only warning present is the pre-existing Plastic SCM repo-name mismatch notice — not a Standard-shader/URP warning and not introduced by this story. Both AC3 bullets satisfied.
- Unity generated `.meta` files for all 5 `Core` scripts and both asmdefs on import (confirmed on disk) — expected, not an error. The new `Core` types compiled into the `CameraGame` assembly without breaking the existing `ThirdPersonController`/`AddMeshColliders` scripts (regression guardrails held).

### File List

New files:
- `Assets/Scripts/CameraGame.asmdef`
- `Assets/Scripts/Editor/CameraGame.Editor.asmdef`
- `Assets/Scripts/Core/GameLog.cs`
- `Assets/Scripts/Core/GameConstants.cs`
- `Assets/Scripts/Core/AnimHashes.cs`
- `Assets/Scripts/Core/ObjectPool.cs`
- `Assets/Scripts/Core/EventChannel.cs`

New folders (empty homes for later stories): `Assets/Scripts/{Player, PhotoMode, Grading, Events, Gallery}`, `Assets/Data/{Events, Grading, Camera, Channels}`

Modified: none (existing scripts intentionally untouched).

## Change Log

| Date | Change |
|------|--------|
| 2026-06-04 | Implemented project scaffold: folder structure, `CameraGame` + `CameraGame.Editor` asmdefs, and 5 `Core` infra scripts (GameLog, GameConstants, AnimHashes, ObjectPool&lt;T&gt;, EventChannel). AC1 & AC2 done; AC3 (clean-compile verification) pending Unity MCP connection. |
| 2026-06-04 | AC3 verified via Unity MCP `read_console` (0 errors, clean domain reload). Task 5 complete; all required tasks done. Status → review. |

## Review Findings

_Adversarial code review 2026-06-04 (Blind Hunter + Edge Case Hunter + Acceptance Auditor). Result: 0 AC violations, clean compile confirmed. The findings were latent robustness gaps in the spec-prescribed Core primitives, not defects in what shipped. Resolved by Alexv: 1 patch applied, 2 deferred, 9 dismissed as noise/false-positives._

### Patched (applied 2026-06-04)

- [x] [Review][Patch] **EventChannel hardened against cross-session subscription leaks & throwing subscribers** [Assets/Scripts/Core/EventChannel.cs] — added `OnDisable() => Raised = null` (drops stale delegates when the SO unloads on domain reload / play-mode exit) and rewrote `Raise()` on both `EventChannel` and `EventChannel<T>` to snapshot the delegate and invoke each handler under its own try/catch (`Debug.LogException`), so one throwing or mid-dispatch-mutating listener can no longer abort the rest. Recompiled via Unity MCP — 0 errors, 0 new warnings.

### Deferred

- [x] [Review][Defer] **ObjectPool&lt;T&gt; has latent Unity-lifecycle & misuse gaps** [Assets/Scripts/Core/ObjectPool.cs] — deferred to Story 1.6. `Get()` can hand back an instance Unity destroyed while idle (scene unload) → `MissingReferenceException`; `Return()` doesn't null-check, doesn't detect double-returns (same object handed to two callers), doesn't reset state on recycle, and accepts foreign objects; the constructor doesn't guard a null `_prefab` or negative `prewarm`; there is no `Clear()`/`Dispose()`, so the idle stack leaks across scene loads. **Defer reason:** the pool has no consumer yet — the correct guards depend on how `EventManager` (Story 1.6) actually uses it, so harden it then against the real contract rather than guessing now.
- [x] [Review][Defer] **AnimHashes.Speed duplicates CharacterAnimator's private "Speed" hash** [Assets/Scripts/Core/AnimHashes.cs] — deferred, by design. `AnimHashes.Speed` is currently unreferenced; the existing `CharacterAnimator` still uses its own `StringToHash("Speed")`. Two sources of truth, but the spec explicitly forbids touching existing scripts in this story. Consolidate when `CharacterAnimator` is wired to `Core` in a later refactor.

### Dismissed (9 — not tracked)

Notable: the claim that placing `CameraGame.asmdef` at the `Scripts/` root would break scene/prefab references — **false positive** (Unity links MonoBehaviours by `.meta` GUID, not by assembly; files weren't moved; AC3 verified a clean compile). Also dismissed: the "`Unity.InputSystem` reference is dead weight" claim (it's load-bearing for the existing `ThirdPersonController`), the asmdef `autoReferenced: true` boundary nit (negligible at this scale, matches spec), and several micro-nits that match the spec by design.
