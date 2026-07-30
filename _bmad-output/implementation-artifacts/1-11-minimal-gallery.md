# Story 1.11: Minimal gallery

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want my captured shots saved with their grades,
so that each catch is permanent proof I can look back on.

## Acceptance Criteria

**AC1 — A captured shot is stored, with its picture, its grade, its subject and its time (FR8, AR4, AR6)**
- A `GalleryService` subscribed to `ShotCapturedChannel` stores a `CapturedShot` for every capture:
  the **image**, the **`ShotGrade`**, the **subject identity**, and **when it was taken**. Storage is an
  in-memory `List<CapturedShot>` (AR6) — no disk I/O in this story.
- ⚠️ **The channel does not currently carry three of those four things.** `ShotCapturedChannel` is
  `EventChannel<ShotGrade>` and `ShotGrade` has no subject, no image and no timestamp
  (`ShotCapturedChannel.cs:19`, `ShotGrade.cs:62-108`). Its own doc-comment says the image is "the
  gallery's job, Story 1.11" without saying how the gallery gets it. **Task 1 decides that seam and it
  gates everything else in this story.**
- Subscribe in `OnEnable`, unsubscribe in `OnDisable` (architecture §Communication Patterns). A
  `GalleryService` that is destroyed or disabled must leave no live delegate on the channel asset.

**AC2 — The player can open the gallery and see the shots with their star grades**
- A gallery view shows the stored shots as **thumbnails with their star rating**. Opening and closing it
  is a player action, not an editor-only readout.
- ⚠️ **Stars alone are not enough to read a shot, and this is a known trap, not a nicety.** Story 1.10's
  review resolved that an off-peak shot scores a hard 0% → **1★**, identical to photographing an empty
  street: *"all 24 shots in the town study read `counted — 0% 1★`"*. `ShotGrade.MissReason` /
  `IsMiss` is what separates them. If the cell shows only stars, a missed shot and a late shot are the
  same cell. (The same finding is carried forward to Story 1.12's HUD.)
- The gallery cannot be opened while the camera is raised, and while it is open the capture and zoom
  inputs do nothing. No new way to take a photograph of the gallery UI.

**AC3 — The in-memory model leaves a disk-ready seam (AR6)**
- Epic 5 must be able to add **PNG + JSON** persistence *without reshaping `CapturedShot`*. Concretely,
  at the end of this story the following must already be true:
  - Every shot carries a **stable identifier** that can name a file. Without one, Epic 5 has to invent
    a key and rewrite the model — which is exactly the reshaping this AC forbids.
  - The timestamp is **wall-clock** (`System.DateTime`, UTC), not `Time.time`. A session-relative float
    is meaningless in a gallery that outlives the session.
  - The image is a **readable, uncompressed `Texture2D`** so `EncodeToPNG()` works on it unchanged.
  - Every other field is a primitive, an enum or a plain struct — nothing that only exists as a live
    scene reference.
- Write no save/load code. The AC is about *shape*, and the proof is a short written argument in the Dev
  Agent Record naming which field becomes which part of the PNG/JSON pair.

**AC4 — Memory stays bounded and nothing leaks (NFR3)**
- ⚠️ **This is the first story in the project that allocates something Unity's garbage collector will
  not reclaim.** A `Texture2D` holds native memory; dropping the C# reference does **not** free it.
  Every image the gallery stops owning must be explicitly destroyed.
- The number of stored shots is **bounded** and the bound is a designer-facing tunable, not a literal
  (architecture §Configuration). When the bound is reached, the oldest shot's image is destroyed.
- Thumbnail resolution is likewise a tunable. State the per-shot and worst-case totals in the Dev Agent
  Record, and **measure** the growth over a long run rather than asserting it (Task 7).

**AC5 — Fail-soft, fast, and no regressions (NFR2, NFR5, NFR8)**
- Capture-to-feedback stays under **0.2 s** (NFR2). ⚠️ Reading pixels back from the GPU is a
  **synchronise-and-stall** operation — the first thing in this project's capture path that is not pure
  arithmetic. Story 1.10's deferred list already flags that the 0.0072 ms grading figure was
  *re-asserted rather than re-measured*; do not repeat that. **Measure it** (Task 7).
- Every missing reference is independently fail-soft, in the established idiom: a missing config, a
  missing camera, a missing view or a missing channel each disables only its own function and logs
  once at `Awake`. Capture, grading, flash and shutter keep working with no gallery at all.
- Console shows **zero errors and zero new warnings**. Baseline is the two known pre-existing warnings
  (the Version Control project-link notice and `ThirdPersonCamera.cs:43` "POV Camera: Head bone not
  found!"), plus the two large-triangle mesh warnings the town scene logs on reload.
- No regression to the 1.3–1.10 photo flow: raise, zoom, capture, flash, shutter, grade, debug overlay
  and **the photo-shoot rig** all still behave exactly as before.

## Tasks / Subtasks

- [x] **Task 1 — Decide how the image, the subject and the time reach the gallery (AC1) — DO THIS FIRST, IT GATES THE STORY**
  - [x] **The problem, concretely.** The AC requires `GalleryService` to subscribe to
        `ShotCapturedChannel` and store four things. The channel delivers exactly one:
        `EventChannel<ShotGrade>`. `ShotGrade` is a `readonly struct` of floats, an enum and an int
        (`ShotGrade.cs:62-108`) — deliberately free of Unity object references. `PhotoModeController`
        is the only place that holds all four facts at the shutter instant: the camera, the best-graded
        actor, the grade, and the moment.
  - [x] **Evaluate the three routes and record the choice with its reasoning:**
        - **(a) Widen the channel payload** to a `ShotCaptured` struct carrying grade + subject + image.
          Faithful to the AC's wording, but it makes the *raiser* create a `Texture2D` and hand it to an
          unknown number of listeners, so **nobody owns it**. With Story 1.12's HUD subscribing to the
          same channel, "who destroys this texture" has no answer. Changes a shipped seam that two
          stories depend on.
        - **(b) Give the gallery a back-reference** to `PhotoModeController` and pull the image and
          identity from it. Reverses the decoupling that AR4 exists for and that
          `ShotCapturedChannel.cs:10-11` states outright.
        - **(c) — RECOMMENDED. Add `SubjectId` to `ShotGrade`; let the gallery take its own picture.**
          The channel type is unchanged (AR4 intact, 1.12 unaffected). `ShotGrader.Grade` already
          receives the `ISubject` and can read `subject.SubjectId` at line 235. The gallery handler runs
          **synchronously inside `Capture()`**, i.e. on the shutter frame, so rendering the camera there
          captures the same instant that was graded. The gallery creates the texture, so the gallery
          owns it, so AC4's destroy rule has exactly one home. The timestamp is stamped by the gallery
          (`DateTime.UtcNow`).
  - [x] ⚠️ **Whatever you choose, `SubjectId` on a miss must be honest.** A shot with no subject has no
        subject id. Use `string.Empty` and let `MissReason` explain it — do **not** invent a
        `"None"`/`"Unknown"` sentinel string that will later be indistinguishable from a real event id.
        This is the same class of mistake `GradeDetail.NotEvaluated` and `GradeMiss.Unevaluated` were
        both introduced to prevent (`ShotGrader.cs:41-51`).
  - [x] **Decide whether a MISS is stored at all**, and say why. Recommended: **store it.** The gallery
        is the player's roll of film, "where did my photo go?" is worse than a bad photo, and the GDD's
        failure model is explicitly soft. `MissReason` makes it legible. Flag it in the handover so
        Alexv can overrule it after seeing a gallery with misses in it.

- [x] **Task 2 — `CapturedShot` and the disk-ready seam (AC1, AC3)**
  - [x] Create `Assets/Scripts/Gallery/CapturedShot.cs`, namespace `CameraGame.Gallery`. The folder
        already exists (Story 1.1) and is empty.
  - [x] Fields: a **stable id**, the `Texture2D` image, the `ShotGrade`, the subject id, and a
        `System.DateTime` captured-at in **UTC**. Architecture names the shape:
        `CapturedShot { Texture2D image; ShotGrade grade; string subjectId; DateTime time }`
        (`game-architecture.md:216-219`) — the id is the addition AC3 requires.
  - [x] For the id, prefer something that is **both** unique and file-name-safe (e.g. a monotonic index
        combined with the UTC timestamp, `shot_0007_20260730T142233Z`). A bare counter resets when the
        session does and would collide the moment Epic 5 writes to a folder that already has files in it.
  - [x] Document, in the class doc-comment, exactly how Epic 5 turns one of these into a PNG plus a JSON
        sibling — which field becomes the filename, which fields become the JSON body, and why the
        image is kept readable and uncompressed. That comment is AC3's deliverable.
  - [x] ⚠️ **`DateTime` does not survive `JsonUtility`.** Note in the same comment that the JSON form is
        the ISO-8601 round-trip string (`"o"` format), so Epic 5 does not discover this by losing data.

- [x] **Task 3 — `GalleryConfig` ScriptableObject + asset (AC4, AC5)**
  - [x] Create `Assets/Scripts/Gallery/GalleryConfig.cs` with `[CreateAssetMenu]`, mirroring
        `CaptureConfig.cs` — that file is the smallest, clearest template in the project for this.
  - [x] Tunables, each with a `[Tooltip]`, a `[Range]`/`[Min]` **and** a `Safe*` accessor:
        **thumbnail width** and **height** (default 480×270 — 16:9, the aspect the camera renders), and
        **max stored shots** (default 50).
  - [x] ⚠️ **`[Range]` is editor-only and this project hand-authors these assets as YAML** — a value
        typed straight into the `.asset` file bypasses it entirely. That is why every config in this
        project reads through a `Safe*` accessor rather than the raw field
        (`GradingConfig.cs`, `CaptureConfig.cs:22`). Follow it exactly.
  - [x] Add a `TryGetConfigProblem(out string)` in the `GradingConfig` idiom and warn **once at Awake**.
        Silent numeric misconfiguration is this project's single most repeated failure mode —
        `cueRadius = 0` (1.8) gave total silence with a clean console; `minCoverage = NaN` and
        `minVisibleSamples = 0` (1.9) each disabled a gate invisibly. **Your instances this story:**
        a non-positive thumbnail dimension (an unusable image, or an exception inside `Texture2D`'s
        constructor), a non-positive max-shots (a gallery that silently stores nothing), and a
        thumbnail so large that the stated memory budget is blown by a single shot.
  - [x] Author `Assets/Data/Gallery/GalleryConfig.asset` **as YAML** using the script's `.meta` GUID —
        Unity MCP cannot create custom ScriptableObject assets. Copy the header shape from
        `Assets/Data/Grading/GradingConfig.asset`. [[create-so-asset-via-yaml]]

- [x] **Task 4 — `GalleryService`: subscribe, take the picture, store, evict (AC1, AC4, AC5)**
  - [x] Create `Assets/Scripts/Gallery/GalleryService.cs`, a MonoBehaviour in `CameraGame.Gallery`.
        Serialized refs: `ShotCapturedChannel`, `GalleryConfig`, and the `Camera` to photograph.
        Resolve each **independently** in `Awake` into its own readiness flag, and log once if missing —
        the independence the 1.4 and 1.5 reviews both praised and `PhotoModeController.cs:99-110`
        documents.
  - [x] Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
  - [x] **Take the thumbnail using the path this project has already proven.** Copy the technique from
        `PhotoShootRunner.Save()` (`PhotoShootRunner.cs:821-855`): bind a `RenderTexture` to the camera,
        `cam.Render()`, set `RenderTexture.active`, `ReadPixels` into a `Texture2D`, `Apply()`.
        - ⚠️ **`ScreenCapture.CaptureScreenshot` is the wrong tool and this is already paid for** — it
          grabs the Game View backbuffer, which does not repaint its 3D content while the editor runs
          unattended. It produced ten images of a UI overlay on blank white (CLAUDE.md §Traps).
        - ⚠️ **Save and restore `cam.targetTexture` and `RenderTexture.active`** — do not assume they
          are null. The photo-shoot rig binds its own render target for the whole run
          (`PhotoShootRunner.cs:320-322`); a gallery that clobbers it breaks the one tool this project
          verifies grading with.
        - ⚠️ **Free the `RenderTexture`.** `RenderTexture.GetTemporary`/`ReleaseTemporary`, or an
          explicit `Release()` + `Destroy()` as the rig does at `PhotoShootRunner.cs:407-411`. A leaked
          RT per capture is a leak per shutter press.
  - [x] **The picture will not contain the viewfinder, the flash, or any UI, and that is correct.** Both
        canvases in `SampleScene` are **Screen Space – Overlay** (`m_RenderMode: 0`), which is composited
        after all cameras and is never part of a camera's render. Verified: every image in
        `_bmad-output/verification/photo-shoot/` is clean 3D with only the rig's own drawn box. Do not
        "fix" this, and do not reorder the flash relative to the grade to try to include it.
  - [x] Store into a `List<CapturedShot>`. When `Count` exceeds the cap, remove the **oldest** and
        `Destroy` its `Texture2D`. Expose the list read-only (`IReadOnlyList<CapturedShot>`), matching
        `EventManager.ActiveActors` (`EventManager.cs:57`).
  - [x] Destroy every owned texture in `OnDestroy` as well. A scene reload must not strand native memory.
  - [x] One `GameLog.Info` per capture (milestone-level, matching `PhotoModeController.cs:345-351` —
        capture is user-driven and infrequent, so this is not spam). No logging in any per-frame path.

- [x] **Task 5 — The gallery view and the input to open it (AC2)**
  - [x] Add a **`Gallery`** action to the `Player` action map in `Assets/Input/InputSystem_Actions.inputactions`
        (suggested: `Tab` and gamepad north button), and a `Gallery` entry in
        `GameConstants.InputActions` — the file already carries `Capture`, `RaiseCamera` and `Zoom` for
        exactly this reason (`GameConstants.cs:9-16`).
  - [x] **Action type: `Button`.** A gallery toggle is a discrete one-shot tap, like `Capture`
        (`PhotoModeController.cs:289-293`) — under Send Messages a Button delivers exactly one call per
        press. ⚠️ The opposite rule applies to *held* inputs: `RaiseCamera` and `Zoom` are `Value`
        because a Button never delivers the release, which is the sticky-input bug fixed twice in this
        project. Do not copy the wrong one. [[input-hold-actions-must-be-value]]
  - [x] ⚠️ **The `OnGallery` handler must live on the player GameObject** — the one carrying
        `PlayerInput`. Send Messages only calls `OnXxx` on its own GameObject, never on a child or on
        the camera. `PhotoModeController`'s class doc-comment states this at `PhotoModeController.cs:16-19`.
        A handler placed on the gallery canvas will simply never fire, with a clean console.
  - [x] Keep **data and view separate**: `GalleryService` owns the list, a `GalleryView` renders it.
        Recommended shape — a thin input adapter on the player object whose only job is
        `OnGallery(InputValue) => view.Toggle();`, exactly as `OnCapture` is a thin adapter over
        `Capture()` (`PhotoModeController.cs:294-304`). That keeps the shutter path callable by the
        verification rig, which is what made Stories 1.9 and 1.10 provable.
  - [x] Build the view in the scene as a Canvas with a `GridLayoutGroup`, hidden via `CanvasGroup.alpha`
        — the same pattern as `PhotoViewfinder` (`PhotoModeController.cs:195-206`). Each cell shows the
        thumbnail (`RawImage`, which takes a `Texture2D` directly with no `Sprite` allocation) plus the
        star rating **and enough to tell a miss from a late shot** (AC2).
  - [x] **Create the cells once** and enable/disable them — never `Instantiate`/`Destroy` per open
        (architecture §Consistency Rules). A fixed pool sized to the config's max-shots is fine.
  - [x] ⚠️ **Make the gallery canvas `Screen Space – Camera` on the photo camera, with a high
        `sortingOrder`** — unlike the viewfinder. This is deliberate: an Overlay canvas cannot be
        captured by `cam.Render()`, so an Overlay gallery **cannot be photographed by any rig** and AC2
        would be provable only by asking a human to look at the screen. It is safe because the gallery
        cannot be open while a capture happens (below). Record this as a deviation from the viewfinder's
        precedent, with this reason.
  - [x] **Art direction is informational here, not an AC.** The GDD wants UI surfaces to read like a
        2000s camcorder/digicam (`gdd.md:322`), and `epics.md` marks that "polish-acceptable" on the
        Story 1.12 HUD. Do not spend this story styling — but do not build something that will have to
        be thrown away either. A plain, legible grid is the right amount.
  - [x] Gate the modes against each other: the gallery opens only in Walk mode, and while it is open
        `SetPhotoMode` is a no-op. Verify `Capture()` and `OnZoom` stay inert — both already gate on
        `IsPhotoMode`, so this should need no change to `PhotoModeController` beyond the raise guard.

- [x] **Task 6 — Wire the scene**
  - [x] Add the `GalleryService` component, assign `ShotCapturedChannel.asset`, `GalleryConfig.asset`
        and the Main Camera. Build the gallery canvas and cells. Assign the view to the input adapter
        on the player object.
  - [x] ⚠️ Assign **the same camera** `PhotoModeController` grades through. Two different cameras would
        store a picture of a different view from the one that was scored — plausible-looking,
        completely wrong, and invisible in any log.
  - [x] Check `read_console` after every script change (project rule, NFR5).

- [x] **Task 7 — Verify by running it, and look at what comes out (AC1–AC5)**
  - [x] **Build a gallery rig on the proven pattern.** Add `Tools > Gallery > Gallery Shoot (Play)`
        alongside `Tools > Grading > Photo Shoot (Play)`. Read `PhotoShootRig.cs` and
        `PhotoShootRunner.cs` first — the runner **must** live in the runtime assembly behind
        `#if UNITY_EDITOR` (an Editor-assembly MonoBehaviour cannot be resolved on a scene object
        entering play mode; the rig silently does nothing), and the rig **must** restore the previously
        open scene via `SessionState` (`PhotoShootRig.cs:66-72,131`).
  - [x] **Drive the real shutter** — `photo.Capture()`, never a re-implementation — several times across
        different framings and different moments in the lifecycle, so the set contains counted shots,
        weak shots and at least one miss.
  - [x] **Write every stored gallery image out to `_bmad-output/verification/gallery/` as a PNG**, with
        its id, stars, subject and grade **drawn onto the image**, plus a `gallery.txt` listing what the
        service holds. Then **open them and look**. The questions only a picture answers: is each
        thumbnail a real, *different* photograph (not blank, not all the same frame)? Does the picture
        match the grade beside it? Is the subject id right for what is in the frame?
        - ⚠️ **Write to `_bmad-output/verification/`, never `Temp/`.** Unity deletes `Temp/` entirely on
          editor shutdown; that cost a real handover on 2026-07-27 when photographs Alexv had been asked
          to review were gone before he opened the folder.
  - [x] **Stress it — this project's bugs live in reuse and in the second cycle onward, never the first.**
        Capture **more than the cap** so eviction actually runs, several times over. Prove the oldest
        shot is gone and its texture destroyed, not merely dereferenced: log
        `Resources.FindObjectsOfTypeAll<Texture2D>().Length` (or the profiler's allocated-memory figure)
        before, at the cap, and well past it, and show the count **flat** rather than climbing.
        Capture across at least two pooled actor respawns and confirm each shot's subject id is the live
        one — a pooled reference does not go null, it quietly becomes a different event
        (`ISubject.cs:10-13`).
  - [x] **Push the boundaries** — every tunable at zero, negative, huge and absent; the config asset
        missing entirely; the camera unassigned; the channel unassigned; the view unassigned; a capture
        with nobody in the world. Each must fail soft, log once, and leave capture/grading working.
  - [x] **Measure NFR2, do not assert it.** Time `Capture()` end to end with and without the gallery
        listening, over many captures, and report both numbers against the 0.2 s budget. The readback is
        a GPU stall — it is the only part of this story that can plausibly break the budget.
  - [x] **Photograph the gallery UI itself.** Open the gallery in play mode via the real input path and
        capture the frame (which the Screen Space – Camera decision in Task 5 makes possible), so AC2 is
        proven by a picture of the thing the player sees, not by a description of it.
  - [x] **Suspect the rig before the code.** Every shot identical, every shot blank, a suspiciously round
        number, a texture count that never moves — treat all of these as a broken rig until proven
        otherwise. And check `Library/ScriptAssemblies/CameraGame.dll`'s mtime against your sources
        before trusting a run: `refresh_unity` has returned `success: true` **without recompiling**, and
        the shoot then ran a seven-minute-old assembly and produced a complete, plausible, wrong result.
  - [x] **Restore the project.** Scene, settings, time scale, any config values the rig touched — put
        everything back automatically on the way out. Never leave the editor sitting in the test world.
  - [x] Confirm the console is clean and the **1.3–1.10 flow is unregressed**, including a full
        `Tools > Grading > Photo Shoot (Play)` run, which is the direct regression test for the
        `cam.targetTexture` handling in Task 4.

## Dev Notes

### What already exists — read these before writing anything

| Thing | Where | Why it matters here |
|---|---|---|
| The capture path | `PhotoModeController.Capture()` — `PhotoModeController.cs:304-352` | Where the channel is raised. Feedback fires first, then grading, then the raise. |
| The channel | `ShotCapturedChannel.cs` · `Core/EventChannel.cs` | `EventChannel<T>` already snapshots handlers, isolates per-handler exceptions and clears subscribers on domain reload. **Do not re-implement any of that.** |
| The payload | `ShotGrade.cs:62-168` | Percent, stars, the three axes, `MissReason`, `Counted`, `IsMiss`. `Stars` is a stored field on purpose so a gallery entry keeps the rating the player was shown even if the config is retuned later (`ShotGrade.cs:98-100`) — that decision was made *for* this story. |
| Subject identity | `ISubject.SubjectId` (`ISubject.cs:44-45`); `EventActor.cs:139` returns `definition.Id` | The id the gallery should record. Already handed to `ShotGrader.Grade` at `ShotGrader.cs:235`. |
| Render-to-texture | `PhotoShootRunner.Save()` — `PhotoShootRunner.cs:821-855` | The proven way to get pixels out of this camera. Copy it. |
| A config to copy | `CaptureConfig.cs` (small) · `GradingConfig.cs` (the `Safe*` + `TryGetConfigProblem` idiom) | The house style for tunables. |
| The viewfinder UI pattern | `PhotoModeController.cs:195-217` | CanvasGroup alpha fade, kept active so `Update` can drive it. |
| Read-only collection exposure | `EventManager.cs:57` | `IReadOnlyList<T>` over a private list. |

### Architecture compliance (non-negotiable)

- **Location & namespace:** `Assets/Scripts/Gallery/`, namespace `CameraGame.Gallery`; the config asset
  in `Assets/Data/Gallery/`. Code in `Scripts/`, data in `Data/` (architecture §Architectural Boundaries).
- **Dependency direction:** `Gallery` may depend on `Core` and `Grading`. Nothing in `PhotoMode`,
  `Grading` or `Events` may depend on `Gallery` — the channel is the only wire between them (AR4).
- **One assembly.** Everything runtime goes in `CameraGame.asmdef`. Editor-only tooling goes in
  `CameraGame.Editor.asmdef` — **except a MonoBehaviour the rig puts on a scene object**, which must be
  in the runtime assembly behind `#if UNITY_EDITOR`.
- **Tunables in a ScriptableObject, never as literals** (architecture §Configuration, §Data Patterns).
  Assigned via `[SerializeField]` — no `Resources.Load`, no `FindObjectOfType`, no singletons.
- **Naming:** `PascalCase` types and methods, `_camelCase` privates, `IPascalCase` interfaces.
- **Fail-soft, never throw into `Update`** (architecture §Error Handling, NFR8). Errors are never shown
  to the player.
- **Subscribe `OnEnable`, unsubscribe `OnDisable`** (architecture §Communication Patterns).
- **Cache in `Awake`; never `GetComponent`/`Camera.main` in a per-frame path.**
- **Debug/gizmo code behind `#if UNITY_EDITOR || DEVELOPMENT_BUILD`.** The gallery itself is *not*
  debug code — it ships.
- **URP only.** If a UI material is ever needed, use a URP-compatible shader; the Standard shader will
  not render.
- **Check `read_console` after every script change.** A clean console is a project rule (NFR5).
- **Verify by running, not by reading** — build the world, drive the real path, capture the output, and
  look at it. Restore the project when the rig finishes. (CLAUDE.md §Verifying Your Own Work.)
- **Jira sync is mandatory** — this story is **KAN-22** (project KAN, cloud
  `5b116b91-787f-4ff7-9668-2cd92d337bcf`; transitions 11=To Do, 21=In Progress, 31=Review, 41=Done).
  The Tasks above are mirrored as Jira Subtasks under KAN-22; keep their status in step with the
  checkboxes here.
- **Git:** `_bmad-output/` is gitignored — story files are local only. Commit *and* push in the same
  session. [[always-push-changes-to-git]]

### The one genuinely new risk in this story

Every story so far has allocated only managed objects or pooled GameObjects. This one allocates
**`Texture2D` and `RenderTexture`**, both of which hold native memory that the C# garbage collector
never touches. The failure mode is not a crash — it is a slow climb in memory across a play session,
with a clean console and a working game, which is precisely NFR3's "leaks no objects over an extended
session". Three rules make it tractable, and all three are in the tasks above: **one owner** (the
gallery creates every texture it stores), **explicit destruction** (on eviction *and* on `OnDestroy`),
and **a measured run** rather than an argued one.

### Project Structure Notes

- `Assets/Scripts/Gallery/` exists and is empty (created by Story 1.1's scaffold); `Assets/Data/Gallery/`
  does not exist yet and must be created alongside the config asset.
- `Assets/UI/` does not exist. The architecture lists it, but every UI in this project so far is authored
  directly in `SampleScene` (`PhotoViewfinder`, `CaptureFlash`). Follow the scene precedent rather than
  introducing a prefab hierarchy for one canvas; note the variance if you do otherwise.
- `Assets/Scripts/Player/` exists and is empty — the `ThirdPersonController`/`ThirdPersonCamera` rename
  the architecture recommends has never been done. **Do not do it in this story**; it would bury a
  gallery diff under a project-wide rename.

### Previous Story Intelligence

From Story 1.10 (closed `done` 2026-07-30 after a three-layer code review: 15 patches, 6 findings
disproven, 7 deferred):

1. **A structural check and a played check are different things, and this project has been caught by the
   gap five times.** 1.10's headline defect — two shots tying at 5★ — was invisible to every structural
   check and was settled by Alexv comparing two photographs. **Your equivalent this story: whether the
   gallery reads as a gallery.** Thumbnails, stars and a grid can all be structurally perfect and still
   be unreadable.
2. **Silent numeric misconfiguration is the recurring failure mode** — `cueRadius = 0`, `minCoverage =
   NaN`, `minVisibleSamples = 0`, `timingFullSeconds = 0`. Each disabled a feature with a clean console.
   Validate and warn once (Task 3).
3. **The review layers generate hypotheses, not conclusions.** Six of 1.10's findings were disproven by
   running the scenario; three of four "High" findings in 1.8 likewise. Reproduce before reporting.
4. **A rig that logs its own errors, or captions its own expectations, produces confident wrong
   evidence.** 1.10's captions were rewritten to describe what the *camera* did, never what the grade
   should be, after a caption reading "just inside the gate" came back rejected.
5. **`refresh_unity` can report success without recompiling** — check the assembly's mtime before
   trusting any run.

From Story 1.9: the bug that mattered (a 5★ photograph of nothing) was found by *looking at a picture*,
not by reading code. From 1.6/1.7: **pooled objects do not go null** — a stale `ISubject` silently
becomes a different drunk. Read live, at the shutter.

### Inherited context you should not rediscover

- **An off-peak shot scores a hard 0% → 1★, identical to a total miss.** Resolved as *designed* on
  2026-07-28 (timing is the first pillar). `MissReason`/`IsMiss` is the only thing that separates them —
  which is why AC2 requires the cell to show more than a star count.
- **`ShotGrade.FromPercent` is dead code** that produces a `Counted` grade with an all-zero breakdown.
  Deferred with the note "delete it or make it non-`Counted` when Story 1.12 lands". **Do not call it.**
- **In the real town, the occlusion gate appears inert** — 24 of 24 shots reported `line-of-sight 100%`,
  including one that scored 98% / 5★ for a photograph of a tree trunk. Reproduced, undiagnosed, and
  logged to `deferred-work.md` against Story 1.9. **Not this story's problem, but expect it in your
  evidence:** a gallery thumbnail may legitimately show a tree with a high grade beside it. Do not chase
  it, and do not record it as a gallery defect.
- **The `Subject` layer (8) is excluded from `occluderMask`**, and `TagManager.asset` lost its URP
  rendering-layer *names* in Story 1.9 (verified harmless). Neither affects this story; both are logged
  so you do not rediscover them.

### Git Intelligence

- `4e603c4` **Story 1.10: five stars was a magic number, not a design decision** — the current
  `ShotGrade`/`StarScale` shape you are storing. Read this before touching `ShotGrade`.
- `fe3ab71` **Story 1.10 review: stop the grader reporting numbers it never measured** — the
  `NotEvaluated` sentinel discipline that Task 1's "empty subject id on a miss" bullet follows.
- `409bf76` **Story 1.9: photograph the real capture path** — the rig you are extending, and the three
  Unity traps it paid for (editor-assembly MonoBehaviour, `CaptureScreenshot` on an unattended Game
  View, `fieldOfView` overwritten every frame by the zoom easing).
- `e9774d2` **make the photo-shoot rig clean up after itself** — the `SessionState` scene-restore
  contract your rig must copy.

### Latest technical information (verified for this project's versions)

- **Unity 6.3 LTS (6000.3.8f1), URP 17.3.0, Input System 1.18.0** — all installed and current for this
  project; no upgrade is part of this story.
- `Texture2D` created from `ReadPixels` is **readable and uncompressed by construction**, which is
  exactly what `EncodeToPNG()` needs (AC3) — no `isReadable` import setting is involved, because these
  textures are created at runtime rather than imported.
- `RawImage` accepts a `Texture` directly; `Image` would require a `Sprite`, i.e. an extra allocation per
  thumbnail for no benefit. Use `RawImage`.
- `RenderTexture.GetTemporary`/`ReleaseTemporary` pools render targets and is the cheaper choice for a
  per-capture readback than constructing a new `RenderTexture` each time — but it must still be released,
  and `RenderTexture.active` must still be restored.
- `JsonUtility` does **not** serialize `System.DateTime` (nor `Texture2D`) — relevant only to AC3's
  written seam argument, not to any code in this story.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.11: Minimal gallery] — AC source (lines 488–506)
- [Source: _bmad-output/planning-artifacts/epics.md#FR8] — captured shots saved with grade and subject identity
- [Source: _bmad-output/planning-artifacts/epics.md#AR4, #AR6] — channel decoupling; in-memory list with a disk-ready seam
- [Source: _bmad-output/planning-artifacts/epics.md#NFR2, #NFR3, #NFR5, #NFR8] — 0.2 s capture budget, no leaks, clean console, fail-soft
- [Source: gdd.md:76-80] — Pillar 3, "Every Photo Is a Trophy": the gallery is the payoff, not a debug list
- [Source: gdd.md:124-128,147-149] — the slice's "minimal gallery" scope, and the success criterion that the player *sees a graded shot in their gallery*
- [Source: gdd.md:262-266,315-322] — the gallery as the game's main "inventory"; the 2000s camcorder/digicam art direction for UI surfaces
- [Source: game-architecture.md:214-219] — Decision 6 and the `CapturedShot` shape
- [Source: game-architecture.md:222-228,428-437] — code organization and the architectural boundaries the Gallery folder sits inside
- [Source: game-architecture.md:302-331] — Configuration and Event System patterns (SO tunables; subscribe/unsubscribe)
- [Source: game-architecture.md:601-610] — the Consistency Rules table this story is reviewed against
- [Source: Assets/Scripts/Events/ShotCapturedChannel.cs:7-19] — the channel, and its own note that the image is this story's job
- [Source: Assets/Scripts/Core/EventChannel.cs:30-48] — the raise/subscribe plumbing you must not re-implement
- [Source: Assets/Scripts/Grading/ShotGrade.cs:62-168] — the payload; `Stars` as a stored field, `MissReason`, `Counted`, `IsMiss`
- [Source: Assets/Scripts/Grading/ShotGrader.cs:231-235] — `Grade(cam, subject, cfg, out detail)`, where `SubjectId` is available
- [Source: Assets/Scripts/Events/ISubject.cs:10-13,44-45] — the liveness contract and `SubjectId`
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:16-19] — Send-Messages must live on the player GameObject
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:99-110,304-352] — independent fail-soft flags; the capture path and the raise
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:289-304] — Button vs Value, and the thin-adapter pattern that makes the shutter rig-drivable
- [Source: Assets/Scripts/PhotoMode/CaptureConfig.cs] — the smallest config template in the project
- [Source: Assets/Scripts/PhotoMode/PhotoShootRunner.cs:26-32,320-322,401-415,821-855] — runtime-assembly rule, target-texture binding, restore, and the render-to-texture save
- [Source: Assets/Scripts/Editor/PhotoShootRig.cs:66-72,131] — the `SessionState` scene-restore contract
- [Source: Assets/Scripts/Events/EventManager.cs:57] — `IReadOnlyList<T>` exposure
- [Source: _bmad-output/implementation-artifacts/1-10-composition-and-timing-scoring.md#Review Findings] — the 0%/1★ decision, and `FromPercent` being dead
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — the town occlusion symptom and the other inherited items above
- [Source: CLAUDE.md#Verifying Your Own Work] — build the rig, run the real path, look at the output, restore the project
- [Source: CLAUDE.md#Jira Sync] — the mandatory KAN mirror, including subtasks and plain-language comments

## Dev Agent Record

### Agent Model Used

Claude Opus 5 (1M context) — `claude-opus-5[1m]`, via `gds-dev-story`.

### Debug Log References

- Roslyn compile check, using Unity 6000.3.8f1's own `csc` against the real Unity assemblies, both
  assemblies, all warnings enabled: `CameraGame` and `CameraGame.Editor` build with **0 errors**. The
  only warnings are `CS0649` ("field never assigned") on `[SerializeField] private` fields — the
  identical pattern already carried by `PhotoModeController`, `EventManager`, `EventActor` and
  `EventRoute`, which Unity suppresses in its own compilation. **No new class of warning introduced.**
- ⚠️ **Nothing here has been RUN.** See "What is NOT verified" below. A clean compile proves the code
  parses; it is not evidence, and this project's rules say so in as many words.

### Completion Notes List

#### Task 1 — the seam decision (AC1), and why

`ShotCapturedChannel` is `EventChannel<ShotGrade>` and delivers one of the four things AC1 requires.
**Route (c) was chosen: `SubjectId` was added to `ShotGrade`, and the gallery takes its own picture.**

- **(a) Widen the channel payload — rejected.** It makes the *raiser* create a `Texture2D` and hand it
  to an unknown number of listeners, so **nobody owns it**. With Story 1.12's HUD subscribing to the
  same channel, "who destroys this texture" has no answer — and a texture nobody destroys is precisely
  the NFR3 leak this story exists to prevent. It also changes a shipped seam two stories depend on.
- **(b) Back-reference to `PhotoModeController` — rejected.** It reverses the decoupling AR4 exists for
  and that `ShotCapturedChannel.cs:10-11` states outright.
- **(c) Chosen.** `ShotGrade` gains a `string SubjectId`, so it stays pure data and the channel *type*
  is unchanged — Story 1.12 unaffected, AR4 intact. `GalleryService.HandleShotCaptured` runs
  **synchronously inside `Capture()`**, on the shutter frame, so `photoCamera.Render()` there captures
  the same instant that was graded. The gallery creates the texture, so the gallery owns it, so AC4's
  destroy rule has exactly one home (`EvictOverflow` plus `OnDestroy`).

**`SubjectId` on a miss is honest — and slightly more honest than the story asked for.** Empty means
"there was no subject", never a `"None"` sentinel. But a shot rejected as `Occluded` or `TooSmall`
*was* measured against a real subject, so `ShotGrader` now carries the id into those misses too: "the
drunk, behind a wall" is a truer gallery entry than "nobody". Only the gates that fire before a subject
exists (`NoCamera`, `NoConfig`, `NoViewport`, `NoSubject`) leave it empty. `HasSubject` is the
reader-facing test, and the constructor normalizes null → empty so `default(ShotGrade)` is safe.

**A MISS is stored.** The gallery is the player's roll of film; "where did my photo go?" is worse than
a bad photo, the GDD's failure model is explicitly soft, and `MissReason` makes it legible. **Alexv can
overrule this after seeing a gallery with misses in it** — it is one `if` in `HandleShotCaptured`.

#### AC3 — the disk-ready seam (the written argument)

The full version lives in the `CapturedShot` class doc-comment, which is where Epic 5 will actually
read it. In brief:

| Field | Becomes | Why it already works |
|---|---|---|
| `Id` | **the filename** of both halves: `shot_0007_20260730T142233Z.png` / `.json` | ASCII, no separators or colons; a monotonic index **and** the UTC instant, so it neither restarts each session (a bare counter would overwrite yesterday's files) nor collides inside one second |
| `Image` | **the PNG body**, via `EncodeToPNG()` with no conversion | created at runtime by `ReadPixels`, so it is uncompressed and CPU-readable *by construction* — no "Read/Write Enabled" import setting is involved, because it is not an imported texture |
| `Grade` | **the JSON body** — percent, stars, the three axes, miss reason *by name*, placeholder flag | `Stars` is a stored field on `ShotGrade` on purpose, so a re-tuned `GradingConfig` cannot retroactively re-rate a photograph the player was already shown |
| `SubjectId` | a JSON string | empty = "there was no subject", never "unknown" |
| `CapturedAtUtc` | a JSON string, ISO-8601 round-trip (`ToString("o")`) | wall-clock UTC, not `Time.time`, which is meaningless in a gallery that outlives the session |

⚠️ **`JsonUtility` cannot do this unaided** — it serializes neither `DateTime` nor `Texture2D`, so
`ToJson(shot)` would silently emit an object with no time in it at all. Epic 5 must map to a small
serializable DTO. That warning is written into the struct's doc-comment so it is not discovered by
losing data.

#### AC4 — the memory numbers

At the shipped `GalleryConfig` (270px tall, ceiling 640px wide, RGB24, 50 shots):

- **per shot (ceiling):** 640 × 270 × 3 = **518,400 bytes ≈ 506 KB**
- **worst case:** × 50 = **25,920,000 bytes ≈ 24.7 MB**
- **in practice**, the width follows the window, so a 1.957 aspect stores 529×270 ≈ 418 KB/shot
  (≈ 20.4 MB for 50). The ceiling is what `BytesPerShot` budgets against, because that is what a
  wide window can actually reach.

> ⚠️ **Corrected by the 2026-07-30 code review.** This section previously read "480 × 270 × 3 =
> 388,800 bytes ≈ 380 KB / 18.5 MB", which were the figures from *before* the thumbnail width was
> made to follow the window (`thumbnailWidth` → `maxThumbnailWidth`). They were never updated, so
> the record contradicted both `GalleryConfig.BytesPerShot` and the "Thumbnails now follow the
> window" section further down this same document. The same stale 380 KB was repeated in
> `GalleryService.EvictOverflow`'s doc-comment and has been corrected there too.

`GalleryConfig.DescribeBudget()` prints exactly this at `Awake`, and `TryGetConfigProblem` refuses to
stay quiet above a 128 MB worst case. RGB24 rather than RGBA32 is deliberate: a photograph has no
transparency, and the fourth channel would cost a third more for nothing. **These are computed figures,
not measured ones** — the measurement is Task 7, and it has not run.

#### Deviations from the story's task list, each deliberate

1. **`PhotoModeController` gained `SetRaiseSuppressed(bool)`** rather than the gallery reaching into it.
   The architecture forbids PhotoMode depending on Gallery, so the controller got a *general* switch
   that knows nothing about galleries, and `GalleryView.Open`/`Close` supplies the meaning. Suppressing
   the raise makes capture and zoom inert for free, because both already gate on `IsPhotoMode` — no new
   flags in either. A raise is refused while suppressed; a **lower always goes through**, which is what
   stops this becoming the sticky-input bug this project has already fixed twice.
2. **The gallery canvas is Screen Space – Camera, unlike the viewfinder.** This is the deviation the
   story asked to have recorded, with its reason: an Overlay canvas is composited after every camera and
   therefore cannot be captured by `cam.Render()`, which would leave AC2 provable only by asking a human
   to look at a screen. `planeDistance` is `nearClipPlane + 0.1` rather than the default 100 — at this
   project's roughly 4× world scale, a canvas 100 units out would spend most of its life inside a
   building.
3. **`GalleryView` builds its own cells** (backdrop, header, grid, one cell per configured slot) at
   `Awake`, sized from the service's own `Capacity`, instead of 50 hand-authored cells. Built once, then
   enabled/disabled — never `Instantiate`/`Destroy` per open. The canvas *object* follows the scene
   precedent (authored in `SampleScene`, no prefab hierarchy); only the repeated cells are code.
4. **Two extra silent-failure guards in `GalleryView`**, both for the "opens and shows nothing, clean
   console" shape: a warning when the UI camera's culling mask excludes the canvas's layer, and explicit
   layer inheritance for the generated cells (`SetParent` does not do this).
5. **`GalleryInput` re-checks its view at press time** rather than caching a readiness flag in `Awake`.
   A view assigned or replaced after `Awake` must still work; the `Awake` error log is kept as the
   one-time wiring diagnostic.
6. **The rig never touches the shipped config asset.** Every boundary and stress scenario uses an
   in-memory `ScriptableObject.CreateInstance<GalleryConfig>()`, so there is nothing to restore and no
   way for a thrown run to leave `Assets/Data/Gallery/GalleryConfig.asset` on values nobody chose.

#### What was actually RUN, and what it showed

`Tools > Gallery > Gallery Shoot (Play)` was executed four times against the shipped code, plus a
separate check in the **real `SampleScene`**. Evidence in `_bmad-output/verification/gallery/`
(rig, 20 files) and `_bmad-output/verification/gallery-real-scene/` (the real town).

| AC | How it was proven | Result |
|---|---|---|
| AC1 | Every capture stored image + grade + subject + UTC time; ids monotonic | 8/8 stored, `529x270` (per `gallery.txt`; the width follows the window), subject `TownDrunk`, empty on `NoSubject` |
| AC1 (unsubscribe) | Disabled the service mid-run and kept firing the shutter | **0** further shots stored |
| AC2 | Photographed the gallery at 8, 30 and 50 shots + all four gate cases | header claim == cells on screen (30/30, 32/32); ★☆☆☆☆ glyphs render |
| AC2 (gating) | Raised camera, capture, zoom, all while open | open-while-raised `False`; capture 8→8; fov 60→60; raise works again after close |
| AC3 | Written argument (above) — no save/load code | shape unchanged, `Id` names the file |
| AC4 | 36 captures at a cap of 5, three rounds, counting live `Texture2D` | **flat at +5** every round; survivors ids 32–36, contiguous |
| AC5 (NFR2) | Timed the real `Capture()` 40× with and without the gallery | **mean 6.057 ms, worst 6.769 ms** (per `gallery.txt`) vs a 200 ms budget (33× margin); 0.36 ms without |
| AC5 (fail-soft) | 9 boundary scenarios: 0/negative/huge tunables, missing config/camera/channel/view | each disabled only its own function and logged once; capture kept grading throughout |
| AC5 (no regression) | Full `Tools > Grading > Photo Shoot (Play)` run | 29 photographs, verdicts unchanged; **no `targetTexture` violation** on any capture |
| Pooled identity | 3 lifecycles, same pooled instance reused | stored id agreed with the live actor every time |
| Real input path | Real `Tab` key through the scene's real `PlayerInput` | `False -> True -> False` — one toggle per press |

#### Four defects the running found, all fixed and re-verified

1. **The rig lied about the wall.** `f_behind_wall` omitted `wall: true`, so it reported `visible 100%,
   counted` under a caption promising a wall. It was one sentence away from being written up as a
   reproduction of Story 1.9's deferred "occlusion gate appears inert" finding. **The photograph caught
   it — there was no wall in the picture.** With the flag restored the pose reads `miss:Occluded,
   visible 0%`, which also tells us the occlusion gate is *fine* in an isolated world and Story 1.9's
   symptom is specific to the town.
2. **The grid ran off the bottom of the screen.** `GridLayoutGroup` flowed as many rows as it liked, so
   at 8 shots the newest row was already sliced in half and at the shipped cap of 50 the player would
   have seen about six with nothing saying so. Cells now shrink to fit, and the header says
   "showing newest N" when they cannot.
3. **Captions printed over their neighbours.** At eight columns "24 % · TownDrunk" was wider than its
   cell, producing an unreadable run-on across the whole row. Captions now wrap, scale with the cell,
   and drop the subject name (never the miss reason) when space is short.
4. **The backdrop leaked the whole town through it — and only the REAL SCENE could show this.** A
   Screen Space – Camera canvas composites *before* URP post-processing, so the 1.5% leaking through at
   alpha 0.985 was then tonemapped along with a bright HDR sky and came back as a clearly readable
   forest across the lower half of the gallery. Measured in place: at alpha 1 the same pixels sample
   exactly `(0,0,0)`. The rig's private world (grey plane, no Volume, no bright sky) looked perfect at
   0.92 *and* 0.985. The backdrop is now fully opaque.

#### Thumbnails now follow the window (Alexv's call, 2026-07-30)

The earlier hand-over flagged a fixed 480x270 thumbnail against a 1.957 window. Alexv chose to make
the thumbnail follow the window, so the config changed shape:

- **`thumbnailHeight` is now the quality dial** (270). The **width is not authored** — it is derived
  from `Camera.aspect` at the instant of the shutter, so a stored photograph frames what the player
  framed. Recomputed per capture, so resizing the window mid-session just works.
- **`thumbnailWidth` became `maxThumbnailWidth`** (640) — a *memory ceiling*, not a size, carrying
  `[FormerlySerializedAs("thumbnailWidth")]` so an existing asset migrates rather than resetting.
- The aspect is read **before** the render target is rebound. Binding a target makes Unity recompute
  `Camera.aspect` from it, so reading afterwards would derive the width from itself and always agree.
- Non-finite, zero and negative aspects fall back to 16:9 rather than being clamped: `Clamp(NaN)` is
  NaN and `RoundToInt(NaN)` is 0, which would have reached the `Texture2D` constructor as a zero width
  and thrown inside the shutter.
- **`GalleryView` cells now take their aspect from the newest stored picture**, not from a hard-coded
  16:9 — otherwise the grid would have stretched the very framing this change exists to preserve.
- The rig now shoots at **960x490 (1.959), deliberately not 16:9**, because at 16:9 a broken
  derivation and a working one both produce 480x270 and are indistinguishable.

Derived widths at 270px tall, verified against the live asset:

| window | stored | | window | stored |
|---|---|---|---|---|
| 4:3 (1.333) | 360x270 | | **1.957 (this machine)** | **528x270** |
| 16:10 (1.6) | 432x270 | | 21:9 (2.333) | 630x270 |
| 16:9 (1.778) | 480x270 — unchanged | | 32:9 (3.556) | 640x270, **clamped + warned** |

`NaN`, `Infinity`, `0` and `-3` all fall back to 480x270 without throwing.

**Verified by running:** the rig reported `camera 1.959 vs stored image 1.959 (529x270) MATCH` on all
seven roll-of-film shots; the real scene stored `528x270` (1.9556) against a camera at 1.9574 — a match
to integer rounding. **The aspect advisory is gone from the console**, which is the point: it now warns
only when the ceiling genuinely clamps, and two new boundary scenarios exercise exactly that.

Worst-case memory moved with the shape: **506 KB/shot at the ceiling, 24.7 MB for 50 shots** (up from
506 KB / 24.7 MB at the 640px ceiling), and less than that in practice because the width follows the window.

#### Console

Ordinary play session: **the two documented baseline warnings only** (Version Control project-link
notice, and `ThirdPersonCamera` "POV Camera: Head bone not found!"). **Zero errors, zero new
warnings** — the aspect advisory disappeared once the width started following the window. The errors visible after a rig run are Phase D deliberately misconfiguring the gallery to
prove each reference fails soft — they are the bench working, not the game failing.

#### Project restored

Verified after every run: `SampleScene` reopened and **not dirty**, `timeScale` 1, `Main Camera`
`targetTexture` null and culling mask restored, no rig objects left in the scene, and the shipped
`GalleryConfig.asset` untouched (every boundary scenario used a throwaway in-memory instance).

### File List

**New**
- `Assets/Scripts/Gallery/CapturedShot.cs`
- `Assets/Scripts/Gallery/GalleryConfig.cs`
- `Assets/Scripts/Gallery/GalleryService.cs`
- `Assets/Scripts/Gallery/GalleryView.cs`
- `Assets/Scripts/Gallery/GalleryInput.cs`
- `Assets/Scripts/Gallery/GalleryShootRunner.cs` (runtime assembly, behind `#if UNITY_EDITOR`)
- `Assets/Scripts/Editor/GalleryShootRig.cs`
- `Assets/Data/Gallery/GalleryConfig.asset`
- the corresponding `.meta` files for all of the above, plus `Assets/Data/Gallery.meta`

**Modified**
- `Assets/Scripts/Grading/ShotGrade.cs` — added `SubjectId` / `HasSubject`; `Scored` takes it and
  `Missed` takes it optionally; the constructor normalizes null → empty; `ToString` reports it
- `Assets/Scripts/Grading/ShotGrader.cs` — reads `subject.SubjectId` once, live, and carries it into
  every miss that actually had a subject, and into `Scored`
- `Assets/Scripts/PhotoMode/PhotoModeController.cs` — `RaiseSuppressed` and `SetRaiseSuppressed(bool)`;
  `SetPhotoMode` refuses a raise while suppressed
- `Assets/Scripts/Core/GameConstants.cs` — `InputActions.Gallery`
- `Assets/Input/InputSystem_Actions.inputactions` — `Gallery` Button action bound to `<Keyboard>/tab`
  and `<Gamepad>/buttonNorth`
- `Assets/Scripts/Editor/CameraGame.Editor.asmdef` — now references `Unity.InputSystem` (the rig builds
  a real `PlayerInput` in order to prove the Send-Messages path)
- `_bmad-output/implementation-artifacts/sprint-status.yaml` — 1.11 → in-progress

### Change Log

| Date | Change |
|------|--------|
| 2026-07-30 | Story created (`gds-create-story`). |
| 2026-07-30 | Task 1 settled on route (c): `SubjectId` added to `ShotGrade`; the gallery takes its own picture inside the shutter frame. |
| 2026-07-30 | Tasks 2–5 implemented: `CapturedShot`, `GalleryConfig` (+ asset), `GalleryService`, `GalleryView`, `GalleryInput`, the `Gallery` input action, and the raise-suppression seam on `PhotoModeController`. Both assemblies compile clean against Unity 6000.3.8f1's own Roslyn. |
| 2026-07-30 | `Tools > Gallery > Gallery Shoot (Play)` written — 7 phases: a roll of film, the gallery photographed, eviction with texture counts, boundaries, NFR2 timing, pooled-respawn identity, and a real Tab key through `PlayerInput`. **Not yet run.** |
| 2026-07-30 | **BLOCKED on Tasks 6–7:** Unity MCP was not connected, so the scene could not be wired and nothing could be run. |
| 2026-07-30 | MCP for Unity updated 9.4.6 → 10.1.0 (both halves) and Unity restarted; MCP reconnected. |
| 2026-07-30 | Task 6: `GalleryService`, `GalleryCanvas` (GalleryView) and `GalleryInput` wired into `SampleScene` against the same camera `PhotoModeController` grades through. Scene saved. |
| 2026-07-30 | Task 7: rig run 4× plus a real-scene check. **Four defects found by running and fixed:** the rig's missing `wall: true`; the grid overflowing off-screen; captions overprinting their neighbours; and the backdrop leaking the town through URP post-processing (real-scene only). |
| 2026-07-30 | All ACs verified against evidence. NFR2 measured at mean 6.057 ms / worst 6.769 ms (33× margin). AC4's texture count measured flat at +5 over 36 captures × 3 rounds. Photo-shoot regression run clean. Status → `review`. |

| 2026-07-30 | Thumbnail width now DERIVED from the window's aspect per capture (`thumbnailWidth` -> `maxThumbnailWidth`, a memory ceiling). Grid cells follow the stored picture's aspect; rig shoots at 1.959 so a broken derivation cannot hide. Real scene now stores 528x270 against a 1.9574 camera and the console is back to the two baseline warnings. |
| 2026-07-30 | `gds-code-review` run (3 parallel layers, shipping code only — rig excluded from scope at Alexv's direction). 3 decisions, 11 patches, 9 deferred, 4 dismissed as disproven. |

## Review Findings

_Reviewed 2026-07-30 by `gds-code-review` — three parallel layers (Blind Hunter, Edge Case Hunter,
Acceptance Auditor) over the shipping game code only. `GalleryShootRig.cs` and `GalleryShootRunner.cs`
were excluded as test scaffolding at Alexv's direction; the rig was used to gather evidence, not reviewed.
Every finding below was independently reproduced or structurally confirmed before being recorded — four
further findings were disproven by running them and are listed at the bottom so a future review does not
re-raise them._

**Decisions — all three ruled on by Alexv, 2026-07-30**

- [x] [Review][Decision] **RESOLVED → deferred to Epic 5.** The unreachable-shots finding below: Alexv accepted it as MVP scope for a story explicitly called "minimal gallery", on the grounds that the header is honest about the loss. A scrolling gallery is to be raised as an Epic 5 story. Recorded in `deferred-work.md`.
- [x] [Review][Decision] **RESOLVED → patch.** Blind walking: suppress movement while the gallery is open, gating `Move`/`Look` on `IsOpen` to match the existing raise-suppression seam. Chosen over a translucent backdrop (keeps the deliberate "own the screen" look) and over `Time.timeScale = 0` (which would freeze NPC events mid-performance).
- [x] [Review][Decision] **RESOLVED → patch.** Dependency direction: amend `game-architecture.md` to sanction `Gallery → PhotoMode`, and correct `SetRaiseSuppressed`'s comment to cite the amended rule truthfully. Chosen over relocating `GalleryView` under `UI/` (which would split the gallery across two folders).

**Original decision detail, for the record**

- [x] [Review][Decision] **18 of the player's 50 photographs are unreachable — the gallery has no scroll or page** — AC2 says the view shows *"the stored shots"*; the epic's Given/When/Then says *"I can see my shots"*. `LayoutGrid` shrinks cells to fit and then shows only what fits, and `Refresh` (the sole fill path) is called only from `Open()`. The dev's own capture `_bmad-output/verification/gallery/ui_open_50shots.png` reads **"GALLERY — 50 shots · showing newest 32"**, and `gallery.txt:170` confirms "cells actually enabled on screen: 32". At the config's permitted `maxStoredShots: 500` the great majority would be unreachable. The `showing newest N` header makes the loss *honest*, which is a real improvement over silent clipping — but honesty about losing 36% of the trophy case is not the same as showing it, and the GDD calls the gallery "the payoff, not a debug list". **Options:** (a) add scrolling/paging now, (b) lower `maxStoredShots` to what one screen actually holds so store and view agree, (c) accept as MVP scope for "minimal gallery" and raise it as an epic-5 story. This is a scope call, not a bug. [Assets/Scripts/Gallery/GalleryView.cs:372]
- [ ] [Review][Decision] **The player keeps walking, turning and falling behind a fully opaque gallery** — `Open()` suppresses only the camera *raise*. `IsOpen` is read by nothing outside `GalleryView` (verified by grep — the single other hit is a doc comment), and nothing touches `Move`/`Look`, `Time.timeScale`, or the action map. The backdrop is deliberately opaque (`BackdropColor`, alpha 1), so the player can hold W and walk blind into a hazard or off a ledge while reviewing photographs. Found independently by two layers. **Options:** (a) suppress movement while open, (b) make the backdrop translucent so the world stays readable, (c) pause the game. Which one is a feel decision that belongs to you. [Assets/Scripts/Gallery/GalleryView.cs:319-338]
- [ ] [Review][Decision] **`Gallery` takes a hard dependency on `PhotoMode` that the architecture does not grant, and the code comment cites a rule that does not exist** — `game-architecture.md:430-432` allows feature folders to depend on `Core` and `Player`, and allows `UI` to depend on `PhotoMode`; it never grants feature→feature. `GalleryView` lives in `Assets/Scripts/Gallery/`, `using CameraGame.PhotoMode`, with a serialized `PhotoModeController` reference wired in the scene. `PhotoModeController.SetRaiseSuppressed`'s doc-comment asserts *"The architecture allows Gallery to depend on PhotoMode"* — that half is not in the architecture document. The direction chosen may well be right (the alternative is worse), but it is an undeclared deviation presented as compliance. **Options:** (a) amend `game-architecture.md` to grant `Gallery → PhotoMode`, (b) move `GalleryView` under `Assets/Scripts/UI/` where the dependency is already sanctioned, (c) record it as an accepted deviation in the story. [Assets/Scripts/Gallery/GalleryView.cs:50]

**Patches (13) — ALL APPLIED 2026-07-30**

- [x] [Review][Patch] **The player can walk, turn and fall behind the opaque gallery** — gate `Move`/`Look` on `IsOpen` while the gallery is open (Alexv's ruling, see Decisions above). `IsOpen` is currently read by nothing outside `GalleryView`. [Assets/Scripts/Gallery/GalleryView.cs:319-338]
- [x] [Review][Patch] **`Gallery → PhotoMode` is undeclared and the comment cites a rule that does not exist** — amend `game-architecture.md` to sanction the dependency, then correct `SetRaiseSuppressed`'s doc-comment to cite the amended rule instead of asserting one that was never written (Alexv's ruling, see Decisions above). [_bmad-output/planning-artifacts/game-architecture.md:430 + Assets/Scripts/PhotoMode/PhotoModeController.cs:278]
- [x] [Review][Patch] Gallery can permanently kill the camera: `RaiseSuppressed` is never released if the view is disabled or destroyed while open — `Open()` sets it, `Close()` is the only releaser, and `GalleryView` has no `OnDisable`/`OnDestroy` (verified: neither exists in the file). The rig works around this in *its own* code (`GalleryShootRunner.cs:1067` — *"destroying it would strand that flag true — the camera could then never be raised again"*), so the shipped scene is the unprotected one. Latent today (nothing disables that canvas in normal play) but live the moment Story 1.12's HUD or a pause menu exists. Fix: `OnDisable` → `if (IsOpen) Close();`. Found by two layers. [Assets/Scripts/Gallery/GalleryView.cs:319-346]
- [x] [Review][Patch] `<Gamepad>/buttonNorth` is now bound to BOTH `Interact` and `Gallery` in the same `Player` map — confirmed in the asset: binding at line 451 → `"action": "Interact"` (pre-existing), binding at line 517 → `"action": "Gallery"` (added by this story). On a gamepad, Y/Triangle fires an interaction *and* toggles the gallery. Rebind `Gallery` to a free button. [Assets/Input/InputSystem_Actions.inputactions:517]
- [x] [Review][Patch] `OnValidate` defeats the config's own out-of-range warnings — **reproduced in the editor**: `TryGetConfigProblem` detects bad values by comparing `maxStoredShots != SafeMaxStoredShots`, while `OnValidate` assigns `maxStoredShots = SafeMaxStoredShots`. Unity runs `OnValidate` on asset import/load/domain-reload, before any `Awake`. Hand-authoring `maxStoredShots: 0` — which the file header calls *"the single most repeated failure mode"* — gives: before `OnValidate`, warning fires; after, raw is silently 1 and the warning can never fire. The comment claiming *"TryGetConfigProblem reports it at Awake FIRST"* is false in the editor (it would still hold in a player build, where `OnValidate` does not exist — i.e. it is broken exactly where designers author). Fix: report from a stored copy of the raw authored value, or drop `OnValidate` and let the warning do its job. [Assets/Scripts/Gallery/GalleryConfig.cs:218-224]
- [x] [Review][Patch] A documented invariant is false: `default(ShotGrade).SubjectId` is **null**, not `""` — **verified by running it in the editor**. The doc states *"Never null: the constructor normalizes, so `default(ShotGrade).SubjectId` is \"\" and callers never need a null check."* C# `default(T)` on a struct zero-initialises and runs no constructor. `HasSubject` and `ToString()` happen to be null-safe, so this stays invisible until a caller follows the stated contract — and `CapturedShot.cs` explicitly instructs Epic 5's author to map `SubjectId` into a JSON DTO. A false documented invariant is worse than none. [Assets/Scripts/Grading/ShotGrade.cs:119]
- [x] [Review][Patch] `TryRenderThumbnail` promises it *"returns null rather than throwing on any failure"*, but three statements sit outside its `try` — `photoCamera.aspect` (:192), the config reads (:197-200) and `RenderTexture.GetTemporary` (:211) all precede `try {` at :214. `_cameraReady` is cached in `Awake` and never revalidated, so a camera destroyed at runtime throws `MissingReferenceException` out of `HandleShotCaptured` — the shot is not stored at all, and any channel subscriber registered after the gallery (Story 1.12's HUD is named as one) never runs for that capture. Fix: widen the `try` to cover all of it. Found by two layers. [Assets/Scripts/Gallery/GalleryService.cs:187-214]
- [x] [Review][Patch] The grid never re-lays out while open — `Refresh` is called only from `Open()`, and there is no `OnRectTransformDimensionsChange`. Resize the window (or toggle fullscreen) with the gallery open and cells keep the pixel size computed against the old rect under a Constant-Pixel-Size scaler, overflowing the edges — the exact regression `LayoutGrid` was written to fix — while the header still reports a count computed against the old rect. [Assets/Scripts/Gallery/GalleryView.cs:372-382]
- [x] [Review][Patch] `LayoutGrid`'s degenerate-rect escape returns `count` without setting `_cellWidth` or `_grid.cellSize` — the other two exits both set them. On a minimized window or a not-yet-produced rect it claims every shot is on screen, leaves cells at the `Awake` size, and drives `FontFor(_cellWidth)` from a stale or zero width, so captions are mis-banded against pictures of a different size and the `shown < count` warning never fires. [Assets/Scripts/Gallery/GalleryView.cs:527-533]
- [x] [Review][Patch] `GalleryView`'s four layout tunables have no validation at all, unlike `GalleryConfig` which validates hard — `cellSize`, `minCellSize`, `spacing` and `fontSize` are plain serialized fields with no `[Min]`, no `OnValidate`, no `Safe*` accessor. `cellSize.x = 0` is clamped down but never back up to `minCellSize.x`, so the gallery opens to a backdrop and a header reading "50 shots" with no photographs and a clean console — the same silent-nothing class `GalleryConfig` exists to prevent. Negative `spacing` overlaps cells; `minCellSize.x + spacing == 0` divides by zero in the fallback. [Assets/Scripts/Gallery/GalleryView.cs:62]
- [x] [Review][Patch] The memory and verification numbers in the Dev Agent Record are stale and contradict a second set in the same record — the AC4 section states 480×270 → **380 KB/shot, 18.5 MB worst case**, but the shipped asset is `thumbnailHeight: 270`, `maxThumbnailWidth: 640`, so `BytesPerShot` is **506 KB** and worst case **24.7 MB** (which the later section and `gallery.txt` both report correctly). The same stale 380 KB is repeated in `EvictOverflow`'s doc-comment. Also: the AC1 row claims stored images were `480x270` where the run log says `529x270`, and AC5 claims "mean 6.2 ms, worst 7.3 ms" where the log says **6.057 / 6.769**. No verdict changes, but a record should not assert numbers its own evidence does not produce. [Assets/Scripts/Gallery/GalleryService.cs:277 + story Dev Agent Record]
- [x] [Review][Patch] `NoViewport` doc/code contradiction introduced in the same commit — `ShotGrader` returns `ShotGrade.Missed(GradeMiss.NoViewport, who)` (verified at :273), while both `ShotGrade.SubjectId`'s doc and the new `Missed(...)` doc state that `NoViewport` is one of the gates that *"leave it empty"*. The code is arguably the better behaviour (the subject is known by then); the docs are stale. Whichever you keep, the next reader will trust the wrong one. [Assets/Scripts/Grading/ShotGrader.cs:273]
- [x] [Review][Patch] With no `uiCamera`, `GalleryView.Awake` returns before `ApplyOpenState(false)` ever runs, and `Close()` then early-returns on `!_viewReady` — so the canvas stays enabled at alpha 1 with no way to turn it off, contradicting AC5's *"disables only its own function"*. Practical impact is likely nil (the root carries no `Graphic` and cells are never built), but the control flow is wrong and the fix is to hide before returning. [Assets/Scripts/Gallery/GalleryView.cs:270]

**Deferred (9)**

- [x] [Review][Defer] `RaiseSuppressed` is a bool, not a refcount or owner token [Assets/Scripts/PhotoMode/PhotoModeController.cs:284] — deferred, blocks Story 1.12
- [x] [Review][Defer] The cell pool is sized from storage capacity, not from what can ever be displayed [Assets/Scripts/Gallery/GalleryView.cs:182] — deferred, acceptable at the shipped cap
- [x] [Review][Defer] An `EventDefinition` with an empty `Id` captions a counted shot "nobody" [Assets/Scripts/Events/EventDefinition.cs] — deferred, pre-existing validation gap
- [x] [Review][Defer] `BytesPerPixel = 3` may understate the real reservation by roughly 2× [Assets/Scripts/Gallery/GalleryConfig.cs:88] — deferred, unmeasured hypothesis
- [x] [Review][Defer] The memory-budget guard is the one branch the boundary run never entered [Assets/Scripts/Gallery/GalleryConfig.cs:200] — deferred, verification gap not a code defect
- [x] [Review][Defer] A cap raised at runtime lets the header report shots it is not drawing [Assets/Scripts/Gallery/GalleryView.cs:378] — deferred, requires runtime cap mutation
- [x] [Review][Defer] `TryGetConfigProblem` reports only the first problem; `_warnedAspect` latches for the session [Assets/Scripts/Gallery/GalleryConfig.cs:158] — deferred, mirrors an existing `GradingConfig` item
- [x] [Review][Defer] The saved scene says World Space render mode; `ConfigureCanvas` overrides it at `Awake` [Assets/Scenes/SampleScene.unity] — deferred, authoring clarity only
- [x] [Review][Defer] `MakeId`'s `D4` index stops sorting lexicographically past 9,999 shots in one session [Assets/Scripts/Gallery/CapturedShot.cs:116] — deferred, extreme edge

**Dismissed — disproven by running them (4). Recorded so a future review does not re-raise these.**

- `minCellSize` missing from the scene YAML would deserialise to `Vector2.zero`, shrinking cells without bound — **false**. Read live from the scene object: `minCellSize = (96.00, 54.00)`. Unity runs field initializers before overlaying serialized data, so a missing key keeps the initializer.
- `GameConstants.InputActions.Gallery` is a dead constant that reads as protection it does not provide — **not a defect of this story**. Every input constant is comment-only under Send Messages (`RaiseCamera`, `Zoom`, `Capture` alike); `Gallery` follows the convention rather than breaking it.
- `RenderTexture.GetTemporary` returns a recycled buffer whose stale contents could bleed into the second and later thumbnails — **false for the shipped scene**. `Main Camera.clearFlags = Skybox`, so the target is fully cleared on every render.
- "Real `Tab` key through the scene's real `PlayerInput`" was proven only in the rig's synthetic scene — **overstated but harmless**. The shipped scene uses the same `BroadcastMessages` notification behaviour and `GalleryInput` sits on the same GameObject as `PlayerInput` and `PhotoModeController`.

### Post-review verification (2026-07-30) — what was proven by running, and what was not

All 13 patches applied, then verified against the running game rather than a clean compile.

**Rig re-run (`Tools > Gallery > Gallery Shoot (Play)`), no regression:**

| Check | Result |
|---|---|
| Console during the whole run | clean — back to the two pre-existing baseline warnings, nothing new |
| Texture count across 3 rounds × 12 captures past a cap of 5 | flat at **+5 vs baseline** — eviction still destroys, no leak |
| NFR2, real `Capture()` ×40 with the gallery listening | **mean 6.036 ms, worst 7.321 ms** vs a 200 ms budget |
| Same, gallery disabled | mean 0.331 ms — the gallery's cost is still the whole of the difference |
| Budget line printed at `Awake` | now reads the corrected **506 KB / shot, 24.7 MB worst case** |

**The three new behaviours, driven directly on the real path in play mode:**

| Step | IsOpen | RaiseSuppressed | InputSuppressed | PhotoMode |
|---|---|---|---|---|
| start | False | False | False | False |
| `Open()` | True | **True** | **True** | False |
| GameObject deactivated **while open** | False | **False** | **False** | False |
| tried to raise the camera afterwards | False | False | False | **True** ✅ |
| reactivated, `Open()`, `Close()` | back to False/False/False | | | |

- **The stranding bug is dead.** Before the patch, deactivating the view while open left `RaiseSuppressed` true forever and the camera could never be raised again. It now releases, and the camera raised successfully afterwards.
- **Sticky held input is cleared, not merely ignored.** Simulated the player holding W and moving the mouse as the gallery opens: `move=(0,1) look=(5,0)` → after `Open()`, both `(0,0)`. Under Send Messages a held key sends no further message, so without this the player would have kept walking.

**⚠️ One thing I could NOT verify, and it needs Alexv — 30 seconds:**

The **grid re-layout on window resize** patch is only half proven. The *response* is verified (`Refresh` recomputes the cell width from the live rect — measured at 223.8px on open with 8 shots). The *trigger* is not: `OnRectTransformDimensionsChange` fires when the canvas rect changes, and a Canvas **drives** its own root RectTransform, so nothing I could set from script actually resized it — `anchorMax` and `scaleFactor` both left the rect at 979×500. My first two attempts reported a confident "FAIL" that was purely my own broken rig, not the code.

**Please do this and tell me what you see:** enter play mode, take a handful of photos, press Tab to open the gallery, then **drag the window edge to make it much smaller and then much larger**. The cells should re-fit to the new window immediately. If they instead spill off the bottom/right edge until you close and reopen, the trigger is not firing and this patch is incomplete.

Until that is done, **AC2's "the player can open the gallery and see the shots" remains partly unproven under resize**, and the story is deliberately being left at `review` rather than moved to `done`.

### Window-resize check — CLOSED by Alexv, 2026-07-30

Alexv performed the outstanding human check: opened the gallery in play mode and dragged the window
smaller and larger. **The cells re-fit correctly.** The `OnRectTransformDimensionsChange` trigger fires
as intended, which was the half I could not exercise from script (a Canvas drives its own root rect, so
nothing settable from code actually resized it — two of my own attempts produced a confident, wrong
"FAIL" before I caught that the rig, not the code, was broken).

**AC2 is now fully proven, and the story moves to `done`.**

Also raised by Alexv in the same pass: **you cannot select a photograph or view it full-screen.**
Checked against both the story and `epics.md` — neither asks for it, so this is out of scope rather
than a gap. Recorded in `deferred-work.md` to be folded into the same Epic 5 "browsable gallery" story
as the deferred scrolling item.
