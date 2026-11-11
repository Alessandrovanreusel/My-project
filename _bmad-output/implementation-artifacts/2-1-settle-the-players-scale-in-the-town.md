---
baseline_commit: 85382ae34ee7869a9bb22abfe5db31293e4c7005
---

# Story 2.1: Settle the player's scale in the town

Status: review

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

*Decide by measuring, as in Story 1.14, and record the result as **ADR-5**. Added 2026-11-10 by `plan-epic-2`.
This is the first story of Epic 2. **Every later Epic 2 story places things in the world at the scale this one
settles.** In `loop/dependencies.yaml`, 2.2, 2.3 and 2.4 wait for it, and 2.5–2.7 wait for those. The story
takes up two `deferred-work.md` entries: "Character (humanoid) is too tall relative to the world" and
"NavMesh bake dimensions are metric while world/agent are ~4×". It differs from 1.14 in one way: the decision
is **applied in this same story**, and the whole of Epic 1's loop is then re-verified at the new scale.*

> **The starting point in six facts.** All six were measured from files on 2026-11-10. Re-measure them in
> the editor (Task 1a) before relying on any of them.
> 1. **The characters are the outliers, not the town.** The map `.blend` is `METRIC` with `scale_length 1.0`
>    (`plan-epic-2/blend_inventory.json`), and `game map .fbx` imports at file scale 1.0. A shipping
>    container is 5.8 m long, which is real size. The player's `CharacterController` is **8.86 m** tall, and
>    the drunk stands about 8.2–8.75 m. So the factor is about 5×. This town is also **not internally
>    consistent**: cars are 2.6 m long (small for a car) and oil barrels are 1.8 m tall (large).
> 2. **The live eye height is a code literal.** `main characters.fbx` has no skeleton, so
>    `ThirdPersonCamera.cs:40`'s head-bone lookup fails. That is the standing console warning "POV Camera:
>    Head bone not found!". The camera therefore uses the fallback at `:79`, `target.position +
>    Vector3.up * 8.5f`, plus a hard-coded `transform.forward * 1f` nudge at `:67`.
> 3. **Two scale factors are in circulation.** `TownDrunk.cueRadius = 100` was converted at **4×** (25 m × 4,
>    Story 1.8). Story 1.12, `epics.md` and `loop/lessons.md:33` say **5×**. ADR-5 measures one factor and
>    retires the other.
> 4. **The grading and camera configs hold no distances.** `GradingConfig.asset` contains only fractions of
>    frame height and seconds. `CameraConfig.asset` contains only FOVs. The Photo Shoot rig places its camera
>    in **subject heights**, at the subject's **bounds-centre** height, so it is scale-invariant by
>    construction. It has never used an eye height.
> 5. **Nobody has ever measured the cue from the game's audio output.** Story 1.8 polled the `AudioSource`
>    settings, computed volumes from the curve formula and listened by ear. Nothing was recorded at the
>    listener.
> 6. **`Assets/Scenes/SampleScene/NavMesh-game map.asset` is 36 MB in plain git, not LFS.** `.gitattributes`
>    has no `*.asset` rule. GitHub warns above 50 MB, rejects files above 100 MB, and the orchestrator
>    pushes master. A finer re-bake can cross both limits.

## Story

As a player,
I want to be the right size for the town, with doors, cars and trees at a person's scale around me,
so that the town reads as a real place, and climbing onto a roof or a hill shows me something I could not see from the street.

## Acceptance Criteria

The four ACs below are copied word for word from `epics.md` §Story 2.1, and they are the contract. The notes
under each one say what it means in this project. They add precision and never relax a clause. AC5 is
added in the same way Stories 1.11, 1.12 and 1.14 added one. It writes down the standing CLAUDE.md rules for
a story that changes the world every other system is measured in.

**AC1: Three options compared, photographed from the player's eye at the same spots, measured, and recorded as ADR-5**

> **Given** the player, the Town Drunk and the town as they are now (characters about 8.86 m tall, beside cars
> about 2.6 m long, containers about 5.8 m long and trees 4.9–7.6 m tall),
> **When** the options are compared, at minimum **(A)** scale the characters to the town, **(B)** scale the town
> to the characters and **(C)** keep both as they are,
> **Then** each option is photographed from the player's eye at the same spots (beside a car, at a house door,
> under a tree, in the street block)
> **And** each is measured for: eye height against those objects; how long it takes to cross the plain and to
> reach the drunk's route, walking and sprinting; and every tuned number it would move. Those numbers include
> movement, jump, gravity, camera eye height, the NavMesh agent, the drunk's speed and cue radius, distances in
> the grading and camera configs, and the rigs' camera heights
> **And** the choice is recorded as **ADR-5** in the architecture, with that evidence and the cost of reversing it.

- **"As they are now" is measured first, in play mode** (Task 1a). The six facts above came from reading
  YAML and FBX headers. Animators do not run in edit mode, and a skinned mesh in bind pose has different
  bounds. Re-measure the player's and the drunk's real heights, the player's eye point, and the reference
  objects' world-space bounds in Unity. The census sizes in `plan-epic-2/map_census.json` are Blender
  `Object.dimensions`: they are in local axes with no stated up axis, and its only door reads 0.0 × 0.1 ×
  0.1. They are leads, not measurements.
- **"At minimum" (A), (B) and (C).** You may add an option if the evidence asks for one, for example (A) at
  a different target height. Every option gets its own row, including one that loses early. Each cell holds
  a measured number with its evidence path, or the words "not measurable, because …" (the 1.14 rule).
- **"The same spots".** Choose **one concrete instance** of each spot inside the slice area, near the
  drunk's street (z ≈ 19, x 0–60): a car, a house door, a tree, and a view down the street block. Record
  each one's object name, world position and the player's standing position and yaw in `spots.json`. Reuse
  that file for every option and for AC4's before-and-after pictures.
  - Under (B) the town is scaled about the map root's origin, (0, 0, 0). Each spot's position is multiplied
    by the same factor.
- **"Photographed from the player's eye"** means the camera is **where the game puts it**: feet, plus that
  option's eye height, plus its forward nudge. Use FOV 60, the Walk state's wide angle, with the pitch level
  at 0° and no hand-picked angles. Under (A) and (C) the camera's pose comes from the real
  `ThirdPersonCamera`; under (B) the player is unchanged in a scaled town.
  - **A person has to be in the picture for scale.** Pose the drunk beside each object, live and animated,
    playing `DrunkIdle` in play mode. A T-posed subject measures nothing (CLAUDE.md).
  - Add a **witness frame** for each spot: a fixed side view, with the eye height, the character's height
    and the object's height drawn on as labelled lines with their numbers (CLAUDE.md: draw the invisible
    values onto the capture). The eye view shows what a player sees, and the witness frame proves the numbers.
- **"Eye height against those objects".** Make one table per option with:
  - the eye height;
  - the car's roof height;
  - the door's head (lintel) height;
  - the tree's lowest canopy and its top;
  - the house's eave and ridge;
  - the street's width.
  Add the ratios eye ÷ object. Each number comes from the world-space renderer bounds of the specific
  instance in `spots.json`, measured in Unity.
- **Crossing times.** Measure them with the **real controller, driven through the real input path**
  (Task 1d), never by setting fields.
  - **Crossing the plain:** one fixed, recorded straight line of about 810 m across it. The map's
    centre plain is about 810 × 570 m (`plan-epic-2/map_topdown.png`).
  - **Reaching the drunk's route:** from the player's spawn, which drops from (0, 20, 0) to about Y 3.6, and
    from two recorded far points on the plain, to the route's midpoint.
  - Measure walking and sprinting for every option that can be staged. Today's 5 / 10 m/s puts about 162 s /
    81 s on the plain line, but that is arithmetic, so measure it.
  - If a cell has to be computed rather than walked, label it **computed**.
- **"Every tuned number it would move".** Dev Notes §"Tuned-number inventory" is the starting list,
  compiled from files on 2026-11-10. **Read each value back through the editor before writing it down**, then
  fill in a column per option. "None, because …" is a valid entry. The grading and camera configs hold no
  distances, so say that rather than inventing entries.
- **ADR-5** goes in `game-architecture.md` §Architecture Decision Records, after ADR-4 (Task 6 lists every
  place it touches).
  - This AC has **no approval clause**, unlike 1.14's AC2. Write ADR-5 as **"Accepted (<date>) — decided by
    measurement in Story 2.1 under the loop's 'decide, flag, keep going'; Alexv may veto"**.
  - Record it as a `decisions` entry, with its reversal cost, in the result file.
  - **Do not add a gate** for it (Task 1f explains why).

**AC2: The chosen option applied: values in config, the NavMesh re-baked at the characters' size, the drunk's ~20–30 s route and ~1.5 s peak kept (FR11)**

> **Given** the chosen option,
> **When** it is applied,
> **Then** every value it moves lives in config, not in code literals
> **And** the NavMesh is re-baked with an agent the size of the characters
> **And** the drunk still takes about 20–30 s over his route, with his ~1.5 s peak (FR11).

- **"In config, not in code literals".** Some values live in code today and would move under any rescale.
  They must be **moved into config** as part of the change:
  - `ThirdPersonCamera.cs:79` (8.5 m, the eye height);
  - `ThirdPersonCamera.cs:67` (the 1 m forward nudge);
  - `ThirdPersonController.cs:207` (the −2 m/s ground-stick velocity), **if** the chosen option moves it.

  Config here means a ScriptableObject in `Assets/Data/`, a serialized field on a scene or prefab component,
  or ProjectSettings. Task 1g recommends where each one goes.
  - Bring the code **defaults** that would contradict the new scale (field initialisers such as
    `EventDefinition.cs:71/:79` and `ThirdPersonController.cs:13–34`) into line. Otherwise the next freshly
    added component is born at the old scale.
- **The rigs' camera heights are on the AC1 list.** Absolute heights in rigs must **read the game's config**
  (the eye height, the near clip) and must not become a new literal such as `1.6f`. Then the next rescale
  moves them automatically.
- **"An agent the size of the characters".**
  - Edit agent type **0, "Humanoid"**. The scene's `NavMeshSurface`, the drunk's `NavMeshAgent` and both
    private rigs' surfaces all already use it.
  - Do it **through the editor API**. ProjectSettings edits on disk do not take effect while the editor runs
    (CLAUDE.md).
  - Set the drunk prefab's `NavMeshAgent` radius and height to the same body; today's 1 / 8 does not even
    match the bake's 0.95 / 8.86.
  - Then re-bake the existing whole-map surface. ⚠️ The asset-size trap is fact 6 above (Task 7.4).
- **"About 20–30 s over his route, with his ~1.5 s peak".** The lifecycle timers are data, and they do not
  change with scale: spawn 3, build 10, peak **1.5**, wind-down 6, despawn 4, so 24.5 s plus a 2 s respawn.
  What scale changes is **the distance he covers**, which is his speed × 20 s of walking phases.
  - Today he walks 60 m at about 3.3 m/s (`route-trace/route-trace.csv`). That reads as an 8.8 m giant
    shuffling, but at person size it is a jog.
  - So the criterion is: he **reaches the end of his route close to the end of his lifecycle**. He must not
    arrive early and walk in place, or arrive late and vanish mid-street (deferred-work, 1.7 review). He
    walks at a speed his walk animation supports (Task 7.5), and the peak still lasts 1.5 s, proven from a
    trace.

**AC3: Epic 1's loop still works at the new scale, through the real code paths (FR10, FR13, NFR2)**

> **Given** Epic 1's loop after the change,
> **When** it is run through the real code paths,
> **Then** the drunk completes several pooled lifecycles on the NavMesh
> **And** his cue is audible out to the design's ~25 m at the new scale, and silent beyond it (FR10)
> **And** a well-framed capture of his peak still grades high and a miss still misses: the Photo Shoot rig, moved
> to the new eye height, shows no regression
> **And** raising the camera still takes under 0.3 s (NFR2), and walk, sprint and jump still work (FR13).

- **"Several pooled lifecycles"** means **at least 3 consecutive cycles** in SampleScene, about 26.5 s each.
  Use `Tools/Events/Trace Actor Route (Play)` with `seconds` around 90–100 (it is 30 today). For each cycle
  report:
  - the spawn position;
  - `on_navmesh` and `has_path` all along the walk;
  - the distance walked;
  - the time at which he reached the last waypoint, compared with the time he despawned;
  - the peak's measured duration;
  - the pooled instance ID, which must be the same each cycle.

  Bugs in this project hide in the second cycle onward (`loop/lessons.md`).
- **"Audible out to ~25 m … and silent beyond it" is measured from the game's own audio output**, for the
  first time (fact 5).
  - **The instrument:** tap `OnAudioFilterRead` on the AudioListener's GameObject (Main Camera). Use a new,
    small runtime rig component behind `#if UNITY_EDITOR` in the `CameraGame` assembly. The spike's
    `AudioTap.cs` shows the technique, but its assembly is fenced, so do not reference it.
  - **The sweep:** hold the listener for at least 1 s at each of 0.5, 1, 2, 4, 8, 12, 16, 20, 24, 25, 26, 30,
    40 and 60 m from the live drunk, and log RMS in dBFS.
  - **The controls:**
    - a **floor** with the drunk *deactivated*, not with the manager disabled (lessons: disabling a manager
      despawns nothing);
    - a near-source positive control;
    - a comparison of the measured curve's shape with `EventActor.GetRolloffCurve`. A flat curve, or one that
      reads the same at every distance, means the rig is broken.
  - **The thresholds:** state "silent" numerically before the run, for example within 1 dB of the floor.
    Report the level at 20–24 m in dB above the floor. Whether that is *audible to a person* goes in the
    human check, so do not certify it.
- **"The Photo Shoot rig, moved to the new eye height, shows no regression".** The rig has never been at eye
  height (fact 4), so "moved" is a change to the rig, and that alone moves numbers. Never mix the two
  changes in one comparison (Task 2.3):
  - **(i) The unchanged Photo Shoot** (subject-relative poses) before and after the scale change. Its
    verdicts should not move. Diff each shot's **stars and composition axis unmasked** (lesson 41).
    Framing shots drift with real-time timing, `i_from_behind` most of all (lesson 42), so two old-scale runs
    define the noise first.
  - **(ii) New eye-height scenarios.** These are new poses, not moved ones: the money shot and two clean
    misses (too small, occluded), taken from the eye height the game config holds. Shoot them **at the old
    scale first** (eye 8.5) for their own baseline, then again at the new scale.
  - ⚠️ `k_straddling` (0.12 h) and `l_inside_away` (0.05 h) put the camera 0.21 m and 0.09 m from a 1.75 m
    subject. That is inside the rig's fixed 0.3 m near clip (`PhotoShootRig.cs:344`). Make the rig use the
    **game camera's** near clip, and look at those two verdicts specifically.
  - **The Town Placement Study** will grade differently. Cars and trees really do block a person-sized
    drunk; at 8.8 m nothing blocked him. That is the world working, not the grader regressing. Photograph
    it, label it as such, and do not "fix" it.
- **"Raising the camera still takes under 0.3 s"** has never been measured by a rig. It holds by construction:
  `PhotoModeController.cs:33` declares `transitionSeconds = 0.15` with `[Range(0, 0.3)]`, and the fade is
  `MoveTowards` on `Time.deltaTime`. Alexv's 2026-06-05 play-test confirmed it.
  - Measure it now in SampleScene: call `SetPhotoMode(true)` (`:263`), the method the RaiseCamera input
    calls, and log unscaled time each frame until the viewfinder's alpha reaches 1.
  - Then lower it the same way. Do at least 10 raises and 10 lowers, and report the worst.
- **"Walk, sprint and jump still work".** Drive them through the real input path (Task 1d): W, W+Shift and
  Space, with a no-input control. Log position and velocity every frame, and report:
  - the measured walk and sprint speeds against the configured ones;
  - the jump's apex, against the expected `v = √(2·g·h)` from the configured `jumpHeight` and `gravity`;
  - a step up a real kerb at `stepOffset`;
  - a landing.

  **How it feels is perceptual**, so it goes to `human_checks`.

**AC4: Alexv's before-and-after look (human check, open until he has done it)**

> **Given** before-and-after pictures from the player's eye at the same spots,
> **When** Alexv looks at them,
> **Then** he confirms the town now reads as a place where a person stands. This is a **human** check, and it stays
> open until he has done it.

- "Before" is option (C), shot in Task 2.4 **before anything changes**. "After" is the applied option, shot
  in Task 9 from the same `spots.json`. Build one side-by-side contact sheet, one row per spot, labelled.
  Put in your own honest read and the eye-ratio numbers.
- ⚠️ **Never write AC4 as verified.** The story can reach `review` with it queued (loop rules).

**AC5 (cross-cutting): no other regression, the project put back, and the docs made consistent (NFR5, NFR8, CLAUDE.md)**

- **The console is clean** against the baseline:
  - the Version Control project-link notice;
  - `ThirdPersonCamera.cs:43` "POV Camera: Head bone not found!", which stays unless you deliberately remove
    the head-bone lookup;
  - the two large-triangle mesh warnings that the town logs on reload.

  A new warning about the agent, the bake or the config is a finding.
- **The full EditMode suite passes.** It ran 168 tests at the last run (2026-11-10); report the real count.
  No current test encodes a scale value, so the suite cannot catch a scale break. That is why the rigs carry
  this story.
- **No other shipped behaviour changes:**
  - Grading (`ShotGrader`, `GradingConfig`) is untouched.
  - Capture, the gallery and the HUD are untouched.
  - The recording spike `Assets/Spikes/RecordingSpike/**` gets at most one README note (Task 6.4).
- **Put back exactly as you found it.** Record each value before changing it, and confirm it afterwards with
  `git diff` and an editor read-back:
  - the open scene, with `SampleScene` saved **only** with the intended changes (diff its YAML);
  - `Time.timeScale` and `Application.runInBackground`;
  - the quality level (`PC`);
  - the Input System settings, if a rig changed them;
  - SessionState flags.

  The NavMesh agent type and the re-bake are *intended* changes; read them back. If you make a build (none
  is required), restore its re-serialised files (`loop/lessons.md:24`).
- **Fail-soft (NFR8).** A missing or invalid config logs **once**, and the game keeps running on safe
  defaults. It never throws.
- **The docs are consistent with ADR-5:**
  - the stale "~4× metric" comments and tooltips (Dev Notes, last block of the inventory);
  - the `deferred-work.md` entries this story resolves;
  - `loop/lessons.md:33` ("put rig cameras at ~7.5 m eye height");
  - a one-line note in the spike README (Task 6).

## Tasks / Subtasks

> **Working order and checkpoints.** The story is large, and a fresh session may have to pick it up halfway.
> - **Order:** 1 → 2 (baselines at the OLD scale, before anything changes) → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11.
> - **Tasks 2–6 change no shipped file.** Each option is staged at runtime in a rig that is never saved.
>   Everything permanent happens in Task 7, after ADR-5 is written.
> - **After each task:** commit, and add a dated line to the Dev Agent Record saying what was measured and
>   where the evidence is. The evidence folder is git-ignored, but it stays on disk for a later attempt.
> - **If you run low on context**, stop at a task boundary and return `failed` with a `next_hint` naming the
>   next task. Do not rush ADR-5 on half the measurements, and **never leave Task 7 half-applied**. Either
>   finish it or revert its files to the branch's last commit with `git checkout -- <path>` (allowed: it is
>   not a branch switch). Say which in the hint.

- [x] **Task 1: Settle the seven things that gate this story (AC1–AC3). DO THESE FIRST.**

  Each item has a recommendation. Record the choice **and the reasoning** in the Dev Agent Record, including
  where you follow the recommendation: a later reader needs to know it was decided, not defaulted.

  - [x] **1a. Measure the starting point in play mode.** Write `evidence/start/start.json`.
        - **Player:** `CharacterController` height, radius, center, skinWidth, and the *effective*
          stepOffset (Awake overrides it to 2). The model's world-space renderer bounds (top, and the
          eye-sphere height; the census put the eyes at ~7.56 m and a top part at ~8.16 m). The camera's
          actual world position relative to the feet, read in `LateUpdate` after `ThirdPersonCamera`.
        - **Drunk:** `EventActor.Bounds` (the `ISubject` box the grader uses) and the head and foot bone
          world heights, while animated, in `DrunkIdle` and in `DrunkWalk`.
        - **The reference objects** at the `spots.json` instances: world-space bounds of the car, the door
          (find the door mesh by looking at it, since the census's `Porta.001` is degenerate), the tree, the
          house and the street.
        - **The factor:** report it as **one number**, character height ÷ the target human height (1b), and
          state which of 4× or 5× it retires.
  - [x] **1b. Choose the target human height and the references that count.**
        - **Recommended:** characters at about **1.75–1.80 m**, with the eye at the model's own eye point
          scaled (about 0.93 of stature).
        - Judge first by the **door** (a human door head is ~2.0–2.1 m), then the container (2.59 m tall
          real), then the street width. Cars and barrels disagree with each other in this town, so record
          what each ratio says instead of averaging them away.
        - The player and the drunk may differ by a few centimetres, as people do. Record both.
  - [x] **1c. How each option is staged, without damaging the project.**
        - **The rig:** an editor `[MenuItem]` opens `SampleScene`, mutates it at runtime, **never saves it**,
          and reopens the developer's scene on exit. Store the path in `SessionState`, as in
          `PhotoShootRig.BuildPlacementStudy` (`Assets/Scripts/Editor/PhotoShootRig.cs:164`).
        - **(C):** as is.
        - **(A):** scale the characters' *visuals* at runtime and set the controller's, camera's and agent's
          values to the candidate numbers.
        - **(B):** set the `game map` root's `localScale` to the factor at runtime, and multiply the route
          waypoints, the spawn point and the spots by it. Uniform scale on MeshColliders is supported by
          PhysX; measure how long the first frame takes, and keep the colliders. The player stays at today's
          size.
        - ⚠️ **Under (B), the pictures from the eye should come out almost identical to (A)'s.** The two
          options are similar triangles. Whatever differs between them comes from settings that do not
          scale: the near and far clip, shadow distance and cascades, fog and lights. If (A) and (B) differ
          beyond those, suspect the rig. If they are pixel-identical even in shadows, suspect the rig too,
          because shadow distance 50 m cannot cover the same share of a 5× town. This makes (A) against (B)
          mostly a question of **cost**, not of pictures. Say so in ADR-5.
        - **Where to apply a scale for real** (applies to Task 7; stage it the same way here). **Recommended:
          never scale a root that carries a `CharacterController`, `NavMeshAgent` or `AudioSource`.** Scale the
          visual, and author the components in metres.
          - **Player:** the importer's Scale Factor (`globalScale`) on `main characters.fbx`. It has no
            skeleton and no working clips, so nothing breaks.
          - **Drunk:** the prefab's `Armature` transform, 0.01 × s. **Not** `husband_rigged.fbx`'s importer:
            the drunk is **Generic** (`animationType: 2`) with separately extracted `.anim` clips (rotation
            only, per the 1.7 notes), so an import scale would desync the bind pose from the clips. Read the
            clips back to confirm "rotation only" before you rely on it.
          - Verify every component's effective values by reading them back. Whether the `CharacterController`
            capsule and the `NavMeshAgent` radius and height multiply by transform scale is **not** to be taken
            from memory. `AudioSource` distances never scale.
  - [x] **1d. How input is driven: the real path, end to end.**
        - **Recommended:** queue Input System state events on the existing keyboard device
          (`InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.W, Key.LeftShift))` and
          similar). That goes **PlayerInput (Send Messages) → `OnMove`/`OnSprint`/`OnJump`**
          (`ThirdPersonController.cs:134/:142/:152`), exactly as a key press does.
        - In the editor the Game View may not have focus. If events are ignored, set
          `InputSystem.settings.editorInputBehaviorInPlayMode` to all-input-to-Game-View for the run, and
          restore it. Check the property and enum names with `unity_reflect` first. There is no
          `.inputsettings` asset, so the settings are in memory only, but restore them anyway.
        - **Prove the drive with controls before you trust any number:**
          - no keys held → no movement;
          - W → forward at walk speed;
          - releasing it stops. Sprint and Move are Value actions (memory: input-hold-actions-must-be-value),
            so release must be delivered.
        - **Fallback**, only if the device path cannot be made to work: `SendMessage("OnMove", …)` with a
          reflected `InputValue`. Record why. *Never* write `_moveInput` directly.
  - [x] **1e. How the cue is heard.** Use a listener-side tap, as specified under AC3. Its data is RMS per DSP
        block plus the listener-to-drunk distance sampled on the main thread, with timestamps from
        `AudioSettings.dspTime`.
        - Make sure **nothing else plays** during the sweep. The shutter is 2D and silent unless captured,
          and there is no ambience yet. Check: list every playing `AudioSource` at the start of the run.
        - Read whether `AudioManager`'s Rolloff Scale (1) affects a Custom curve. Do not assume it.
  - [x] **1f. Does ADR-5 need Alexv's sign-off before 2.2 starts? Decided: no gate.**
        - The AC has no "approves before" clause. A gate would hold back **all** of Epic 2 while he is away.
          His standing choice is "decide, flag, keep going", and AC4 is a batched perceptual check that
          dependents may build on.
        - The cost is that a veto after 2.2–2.4 merge means re-placing their work. Put that in the
          `decisions` entry's reversal cost.
        - There is also evidence for his direction: he noted "humanoid too tall" on 2026-06-05, which points
          at the characters.
        - **Do not edit `loop/dependencies.yaml`.**
  - [x] **1g. Where the moved values live.**
        - **Recommended:** a new `PlayerConfig` ScriptableObject (`Assets/Scripts/Player/PlayerConfig.cs`,
          namespace `CameraGame.Player`; asset `Assets/Data/Player/PlayerConfig.asset`). It holds the walk
          and sprint speeds, jump height, gravity, step offset, ground-stick velocity, **eye height above
          the feet** and the **camera forward nudge**.
        - `ThirdPersonController` and `ThirdPersonCamera` reference it, the way `PhotoModeController`
          references `CameraConfig`.
        - Validate it in `Awake`, warning once with `GameLog.Warn`, and fail soft. One `TryGetConfigProblem`
          pattern exists in `GradingConfig` and `GalleryConfig`; copy its shape.
        - Why a ScriptableObject and not more scene fields: the architecture's §Configuration rule ("Balancing
          /tunable → ScriptableObjects"), and the **isolated** rigs (Photo Shoot, Gallery) have no player in
          their world but can load an asset by GUID. That makes the eye height one source of truth for the
          game and every rig.
        - **Keep the `CharacterController`'s own dimensions on the component.** It is Unity's serialized
          physics shape; do not mirror it in the config. Add an EditMode test or an `OnValidate` warning that
          the eye height is below the controller's height and that the NavMesh agent matches the controller
          within tolerance. That is a structural invariant, not an expected result.
        - Create the `.asset` as YAML using the script's `.meta` GUID (memory: create-so-asset-via-yaml; MCP
          cannot create custom ScriptableObject assets).
        - **The alternative** is serialized fields on the two components. It is acceptable if the config
          migration proves risky. Record which you chose.

- [x] **Task 2: Baselines at the OLD scale, before anything changes (AC3, AC4, AC5)**
  - [x] 2.1 **Record the console baseline, the EditMode count and `git status`.** Copy
        `_bmad-output/verification/photo-shoot/` to
        `_bmad-output/verification/2-1-settle-the-players-scale-in-the-town/photo-shoot-before-1/` **before** the
        first run, because the rig wipes its own output folder (1.14, memory trap 14).
  - [x] 2.2 **Run `Tools/Grading/Photo Shoot (Play)` twice at the old scale**, copying each run aside
        (`photo-shoot-before-1/`, `-2/`). Diff each shot's stars and composition between the two runs, unmasked:
        that is the noise floor. Then run the Town Placement Study once (`photo-shoot-town-before/`).
  - [x] 2.3 **Add the eye-height scenarios (AC3 (ii)) to `PhotoShootRunner.cs` now**, and shoot them at the old
        scale (eye from config = 8.5): `z3_money_shot_eye`, `c_far_eye` and `f_wall_eye`. Do not touch the
        existing poses.
        - The rig's camera near clip now reads the game camera's value.
        - The idle camera (`:437`, `new Vector3(0f, 8f, 30f)`) reads the eye height.
        - Re-run once to prove that the **existing** shots are unchanged by this rig edit.
  - [x] 2.4 **"Before" pictures (AC4) and option (C)'s row (AC1).** Build the comparison rig
        `Tools/Scale/Compare Options (Play)`: `Assets/Scripts/Editor/ScaleStudyRig.cs`, plus
        `Assets/Scripts/Player/ScaleStudyRunner.cs` behind `#if UNITY_EDITOR` in the runtime assembly
        (CLAUDE.md trap: an editor-assembly MonoBehaviour is "missing" in play mode).
        - It takes an option argument, stored in `SessionState` with an expiry stamp (lesson 37).
        - Shoot (C) at every spot: the eye view plus the witness frame, with the drunk posed.
        - Control shots come first: one frame with known 3D content, which must not be uniform or white.
  - [x] 2.5 **Route trace at the old scale.** Run it for at least 3 cycles (`seconds` ≈ 95). Add per-frame columns
        for the drunk's foot-bone world positions, so foot sliding can be measured (Task 7.5). Save it as
        `route/before-trace.csv`.
  - [x] 2.6 **Cue sweep at the old scale** (Task 1e instrument), with the radius 100 sweep distances scaled by
        the factor so the curves can be compared: `cue/before-sweep.csv` plus a plot.
  - [x] 2.7 **Raise timing and walk/sprint/jump at the old scale** (Task 1d instrument): `movement/before-*.csv`.

- [x] **Task 3: Stage and photograph (A) and (B) (AC1)**
  - [x] 3.1 **(A) at the candidate values from 1b**, at every spot: eye view plus witness. The drunk's visual is
        scaled, posed and animated.
  - [x] 3.2 **(B) with the map root scaled by the factor**, at every scaled spot, and the player at today's size.
        Record the first-frame cost of re-scaling 16k MeshColliders and any new console line. Large-triangle
        warnings above 500 units will multiply, and that is a finding for (B)'s row.
  - [x] 3.3 **One contact sheet**, `options/contact-sheet.png`: rows are spots, columns are (C), (A) and (B),
        all labelled with eye height and ratios. **Look at every image.** Run the (A)-versus-(B) similarity
        check from 1c.

- [x] **Task 4: Measure crossing times and the tuned-number inventory (AC1)**
  - [x] 4.1 **Crossing times**, measured by the real controller through the real input (1d). Do this for (C)
        at today's speeds, for (A) at **two** speed sets, and for (B) at today's speeds and at speeds × the
        factor:
        - **(A)'s speed sets:** (i) today's 5 / 10 m/s kept in world metres, which leaves the travel times
          unchanged; (ii) a human-paced set, for example 4 / 7 m/s.
        - **Routes:** the plain line, plus spawn and two far points to the route's midpoint.
        - **Output:** `crossing/crossing.csv`, plus a top-down trace drawn on `plan-epic-2/map_topdown.png`'s
          frame.
  - [x] 4.2 **Tuned-number inventory.** Copy Dev Notes §"Tuned-number inventory" into
        `inventory/tuned-numbers.md`, re-read every value through the editor (`execute_code` read-backs), and
        add (A) and (B) columns giving the value each option would need.
        - Mark every **code literal**.
        - For each option, count how many values move and how many files that touches.
  - [x] 4.3 **Derived design checks**, labelled **computed**. They tell Alexv what the option means for play:
        - The distance at which a person-sized subject passes the 0.20 size gate. At 60° FOV the frame height
          at distance d is 2·d·tan 30° ≈ 1.155·d. A 1.75 m subject therefore passes out to ≈ 7.6 m wide and
          ≈ 27.6 m at the 18° tele end, against today's ≈ 38 m and ≈ 138 m for an 8.8 m subject.
        - The cue's audible area in world terms: (100 → 25)² is **16× smaller** under (A).
        - Today's **relative** numbers (cue radius ÷ zoom gate distance) are preserved under (A) and (B), and
          only (C) keeps them in world terms.

- [x] **Task 5: Compare and decide (AC1)**
  - [x] 5.1 **Write `decision/comparison.md`.**
        - **Rows:** the options.
        - **Columns:** the pictures (a link to the sheet row); eye ÷ door, car, tree and house; crossing walk
          and sprint; the number of tuned values moved and the code literals among them; the NavMesh re-bake
          cost (time and asset size, if baked); precision and render risks (for (B): coordinates out to
          ~11 km, the far clip ~5000, 5× shadow distance and its blur, the large-triangle warnings); the
          Story 2.2 consequence (its AR9 check compares bounds, which (B) breaks); **reversal cost**.
        - **Every cell** carries an evidence path, or "not measurable, because …".
  - [x] 5.2 **Decide.** Do not encode the expected winner: measure all three, then judge. If two options tie, the
        tie-break is fewer moving parts and the smaller reversal cost. Write the choice into the result
        file's `decisions`.

- [x] **Task 6: Write ADR-5 and its touch points (AC1)**
  - [x] 6.1 **Add ADR-5 to `game-architecture.md` §Architecture Decision Records, after ADR-4**, with the status
        from AC1's note. It contains:
        - the decision and a one-paragraph why;
        - a compact evidence table with paths;
        - **the scale rule for all future content:** target heights; roots unscaled; components in metres;
          where the scale goes for skinned Generic models (the armature) and for unskinned ones (the
          importer); and the same rule for Epic 3's bandit, CEO and oil-rig worker models in
          `Assets/Models/`;
        - **the design constants Epic 2 builds on:** character height, eye height, controller radius and
          step, jump apex and air time, walk and sprint speeds, the NavMesh agent (radius, height, climb,
          slope, voxel), the cue radius and falloff;
        - the numbers moved, as a before → after table;
        - **consequences:**
          - occlusion by cars and trees is now real, which is why 2.5's vantage points matter;
          - the cue's audible area is 16× smaller in world terms;
          - the rigs;
          - the 1.14 spike is pinned to the old scale (`EyeHeight 7.5`, the `-vantage` argument);
          - ADR-4 replays are scale-bound once 1.17 persists them. None exist yet, so this change costs replay
            nothing today;
        - **reversal cost.**
  - [x] 6.2 **Update the touch points**, each with a dated revision note:
        - a Decision Summary row **10 — World scale**;
        - row 8 ("3D AudioSource per actor (~25m rolloff…)");
        - §Event-Actor System ("cue radius (~25m)");
        - the frontmatter `updated:`;
        - an Executive Summary bullet if it fits.

        Add a `decision-log.md` entry (`_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/`).
        **Do not edit `epics.md`.** List any wording there that ADR-5 makes stale as a follow-up in the Dev
        Agent Record.
  - [x] 6.3 **Update `deferred-work.md`** with a dated "Resolved by Story 2.1 / ADR-5" note on the entries this
        story closes: lines ~:13, ~:20 and ~:60. Note on :60 that its premise was the scene's unused built-in
        NavMesh block (`SampleScene.unity:99–121`, `m_NavMeshData: 0`), not the real bake. Also close :61
        (agent speed), :62 (the peak fires past `WP1`) if the re-laid route fixes it, and :67 (the head-bone
        literal) in part. Leave the see-through-walls entry (:12) to Story 2.2.
  - [x] 6.4 **Mark `loop/lessons.md:33` superseded in place**, naming the new eye height and ADR-5, and return the
        new fact as a lesson. Add one line to `Assets/Spikes/RecordingSpike/README.md` saying its vantage and
        `EyeHeight` predate ADR-5. **Do not edit the spike's code.**

- [x] **Task 7: Apply the chosen option (AC2).** The sub-tasks below are written for (A), the option the prior
      evidence points at. If (B) or (C) wins, the equivalent list is your Task 4.2 table: apply that instead
      and note the substitution here.
  - [x] 7.1 **Config (1g).** Write `PlayerConfig` (script, asset and `.meta`) and wire it into the two components.
        Move the literals (`ThirdPersonCamera.cs:67/:79`, and `ThirdPersonController.cs:207` if it moves).
        Migrate the scene's current values. Run `refresh_unity` and `read_console`, then **read every value back
        through the editor**.
  - [x] 7.2 **Characters.** Scale the player through the importer and the drunk through the prefab `Armature`
        (1c). Set the player's controller to the new body: height, radius, center, skinWidth ≈ 10% of radius,
        and stepOffset through config.
        - Read back, in play mode and animated: the player's height and eye point, and the drunk's
          `EventActor.Bounds` height, which must be close to the target.
        - The deactivated child in the player's prefab instance stays deactivated.
  - [x] 7.3 **Camera.** Set the eye height from the measured, scaled eye point. Scale the forward nudge and the
        Main Camera near clip **together with the controller radius**. Then the camera's geometry against a wall
        is *no worse* than today's in body proportions. Today the near plane reaches nudge 1.0 + near 0.3 −
        radius 0.95 = **0.35 m beyond the capsule**, about 4% of body height.
        - Photograph "pressed against a wall" at the old and the new scale.
        - **The see-through-walls fix itself is Story 2.2's**; do not attempt it here.
        - Far clip 1000 and shadow distance 50 stay unless the evidence says otherwise.
  - [x] 7.4 **NavMesh.**
        - Set agent type 0 through
          `SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings"))`, field
          `m_Settings.Array.data[0].agentRadius / agentHeight / agentClimb / agentSlope`. This is the same
          technique as lesson 36. Verify the singleton name with `unity_reflect`, and read it back with
          `NavMesh.GetSettingsByID(0)` and on disk after `AssetDatabase.SaveAssets()`.
        - `agentClimb` equals the controller's stepOffset.
        - Set the drunk prefab's agent radius and height to match.
        - **Re-bake the existing surface** (the `game map` root: Collect Children, Physics Colliders) and time
          it.
        - ⚠️ A bake made in edit mode with `BuildNavMesh()` is not persisted on its own (deferred-work ~:106).
          Save the `NavMeshData` to `Assets/Scenes/SampleScene/NavMesh-game map.asset` and save the scene's
          reference.
        - ⚠️ **Asset size:** the voxel size defaults to radius ÷ 3. At a ~0.3 m radius that is ~0.1 m, against
          0.3167 today, which is ~10× the voxels. Check the file size **before committing**. If it is over
          50 MB, use a voxel override (fewer voxels per radius) or bound the surface's volume to the slice
          patch plus the drunk's route. The whole-town bake is Story 2.2's AC. Record the choice as a
          decision.
        - **Never add an LFS rule.** LFS quota is a money question, so it would be a blocker.
  - [x] 7.5 **The drunk.**
        - **Measure `DrunkWalk`'s stride speed at the new scale** from the route trace's foot-bone columns: the
          horizontal speed of the planted foot during ground contact is the slide (agent speed minus stride
          speed). Set the agent speed (and its acceleration and stopping distance, proportionally) to minimise
          slide, or record why not.
        - **Re-lay the route** so its walked length ≈ speed × 20 s (the build, wind-down and despawn phases
          walk; spawn and peak stand). Put `WP1_peakSpot` where he really is when Peak starts, about speed ×
          10 s from spawn.
        - **Recommended:** keep the peak spot where it is today, ≈ (25.9, 0.8, 19). The Town Placement Study,
          the HUD vantages and later Epic 2 stories stay anchored to it.
        - Keep the route straight. A corner makes the `hasPath` deferral (~:49) due, and then it must be fixed.
        - `TownDrunk.asset`: `cueRadius` **25** (the design value), `cueFalloffStart` about one body height
          (≈ 2). Update the code defaults and tooltips.
  - [x] 7.6 **Rigs.**
        - `GradeHudShootRunner.cs:1634` (`Vector3.up * 6f`), `GalleryShootRunner.cs:268` and
          `PhotoShootRunner.cs:437` (`(0, 8, 30)`), and the rigs' fixed near clip, all read config.
        - Distances in subject heights stay as they are.
        - Wall depth 0.4 and ground ×20 are scale-safe at person size; check them, do not change them.
  - [x] 7.7 **Stale comments and tooltips.** Fix `ShotGrader.cs:511–512`, `EventActor.cs:367–371`,
        `EventDefinition.cs:67–69/:76–80`, `GalleryView.cs:236` and `GradeHudShootRunner.cs:1841`, so that the
        code describes the scale ADR-5 set.
  - [x] 7.8 **Save `SampleScene` in a separate `execute_code` call** (memory: an unsaved-changes dialog turns one
        menu call into six). Diff its YAML. Expect changes only to the player and its controller, the camera,
        the route, the spawn point, and the surface's NavMesh reference. **No rig objects.**

- [x] **Task 8: Verify Epic 1's loop at the new scale (AC3)**
  - [x] **Before any run:** `refresh_unity`, wait for the compile, check `mcpforunity://editor/state`
        (`assets.external_changes_dirty`), and **read the moved values back through the editor**. A stale
        assembly runs with a clean console (CLAUDE.md).
  - [x] 8.1 **Route trace for at least 3 cycles** (`route/after-trace.csv`), with the per-cycle table from AC3.
        Also compute the slide speed against the before-trace.
  - [x] 8.2 **Cue sweep** at 0.5–60 m (`cue/after-sweep.csv`): the floor, the controls, a plot overlaid on the
        computed curve, and the verdicts at 25 m and beyond.
  - [x] 8.3 **Photo Shoot after**: the unchanged scenarios, diffed against both before-runs, and the eye-height
        scenarios against their old-scale baseline. Copy aside first. Then the Town Placement Study, labelled
        as world-dependent.
  - [x] 8.4 **Raise timing**: 10 raises and 10 lowers, worst case under 0.3 s. **Walk, sprint, jump and a kerb
        step**: measured against config.
  - [x] 8.5 **Foot sliding over time is a temporal question.** Settle it by measurement first (8.1). If you want a
        second opinion, send Gemini a short clip with the drunk in every frame, no timestamps and no named
        defect (`tools/verification/README.md`), and verify every claim on its frame.
  - [x] 8.6 **Suspect the rig before the code.** If every case is identical, every case passes, a number is
        suspiciously round, or a capture is blank or white, fix the rig first (lessons).

- [x] **Task 9: Before-and-after pictures for Alexv (AC4)**
  - [x] 9.1 Shoot the applied option at every spot in `spots.json` with the same rig and the same poses as 2.4.
        Build `before-after/sheet.png`: (C) on the left, after on the right, one row per spot, with the eye
        ratios. Look at it yourself and write your honest read.

- [x] **Task 10: Restore, console, tests, regressions (AC5)**
  - [x] 10.1 Run `read_console` and `run_tests` (EditMode), and report the count. Check `git status` and
        `git diff --stat`: only intended files.
  - [x] 10.2 Read back every restored value (AC5 list). SampleScene is not dirty and has no rig objects.
        Clear the SessionState flags.
  - [x] 10.3 Re-run the Gallery and HUD rigs once each, to prove they still **run** after their literal
        changes: no exceptions, outputs not empty. Their HUD vantages in the real town *will* differ, because
        occlusion is now real. That is not a regression; note it.

- [x] **Task 11: Handover. Queue what needs Alexv, and never self-certify it**
  - [x] 11.1 Put the `human_checks` (Dev Notes §"Human checks to queue") in the result file with exact paths and
        your own read. Put the `decisions` there too: ADR-5's choice (1f), the target height (1b), the config
        home (1g), the NavMesh voxel and volume (7.4), the drunk's speed and route (7.5), and the speed set
        (4.1).
  - [x] 11.2 Story status → `review`, never `done` while AC4 is open. Update the `sprint-status.yaml` entry and
        `last_updated` (no `: ` and no trailing `:`), then check it with `yaml.safe_load`.

### Review Findings

Code review on 2026-11-11, loop REVIEW phase, of `85382ae..00cafac`. It was done by a fresh session that did not
write the code.
- **Three parallel layers:** Blind Hunter (diff only), Edge Case Hunter (diff + project) and Acceptance Auditor
  (diff + story + ADR-5).
- **What was reviewed:** the shipped code and ADR-5. The rigs were run and modified as tools; they were not
  reviewed (standing rule).
- **Result:** about 45 raw findings, triaged into **0 decisions, 11 patches (all applied and re-verified by
  running), 4 deferred and 17 dismissed**.
- **ADR-5's decision, (A) characters scaled to the town, is unchanged by the review.**

What was **run**, not read: SampleScene played through the real `ThirdPersonController`, `ThirdPersonCamera` and
`GalleryView` paths, with bad configs injected at runtime or set in memory and restored. In addition:
- two 7-cycle pooled route traces (190 s each), before and after the route fix;
- a gallery capture pair with a control;
- positive and negative controls for the new player-vs-agent check;
- a disabled-camera sentinel test;
- the Grade HUD Shoot rig re-run;
- the full EditMode suite.

Evidence: `_bmad-output/verification/2-1-settle-the-players-scale-in-the-town/review/`
(`findings-reproduced.md`, `gallery-near/`, `route-7cycles*/`).

**Patches (applied, re-verified by running):**
- [x] [Review][Patch] **An infinite gravity or ground-stick passed the fail-soft accessors and wedged the player
      for good.** [Assets/Scripts/Player/PlayerConfig.cs `SafeGravity`/`SafeGroundStickVelocity`] (blind+edge)
      - **Reproduced:** with `gravity = -Infinity`, the player froze in mid-air (y 5.467, velocity 0) with no
        console line. He stayed frozen after the good asset was put back: `_verticalVelocity` stays -Infinity,
        and `CharacterController.Move` rejects every move.
      - `groundStickVelocity = -Infinity` also froze a grounded player, with W held through the real input path.
      - The cause: these two checked only NaN, while every other field rejects ±Infinity.
      - **Fix:** a finite check, plus 6 new test cases (±Infinity).
      - **Re-verified:** `-Infinity` in the asset now logs one warning ("… is not finite — using -15"). The player
        walks at 4 m/s, and when lifted 2 m he falls and lands.
- [x] [Review][Patch] **The gallery opened invisibly when `PlayerConfig.cameraNearClip` was more than 0.1 m above
      the scene camera's near clip.** [Assets/Scripts/ThirdPersonCamera.cs] (blind+edge)
      - The cause: `GalleryView.Awake` places its canvas at `nearClipPlane + 0.1`, read from the serialized
        camera, and the config's near clip was written only later, in `Start`.
      - **Reproduced** at 0.3, the pre-ADR-5 value: planeDistance 0.195, inside the near plane. The "open" frame
        was pixel-identical to the closed one (mean luma 142.5) while input stayed suppressed. The control at
        0.095 drew the backdrop (mean 16.0).
      - **Fix:** the near clip is applied in `Awake` under `[DefaultExecutionOrder(-100)]`. A *disabled* component
        returns early (Unity calls `Awake` even on a disabled component, and the rigs switch the camera off),
        and the head snap stays in `Start`.
      - **Re-verified:** at 0.3, planeDistance 0.4 and the backdrop is drawn (mean 16.0); `gallery-near/sheet.png`
        is the before state.
      - Camera pose still 1.650 m up and 0.316 m forward of the feet. The head-bone warning is logged once.
      - A disabled camera left a sentinel near clip (0.5) and pose untouched.
- [x] [Review][Patch] **The drunk never reached the end of his route. He vanished mid-stride 0.45–0.51 m short in
      7 of 7 cycles.** [Assets/Scenes/SampleScene.unity `DrunkRoute/WP3_alley`] (edge)
      - The record's "reaches the end at 23.6 s, 0.9 s before despawn" came from `tools/route.py`'s 1 m radius.
        By `EventActor`'s own test (remaining ≤ stopping distance, 0.257) he never arrived.
      - At despawn: x 20.25–20.31, still walking at 0.53–0.57 m/s (`route-7cycles-before-fix/arrival.txt`).
      - This was AC2's "arrive late and vanish mid-street", if only by half a metre.
      - **Fix:** `WP3_alley` moved 19.8 → **20.4** (y 0.9705, ground + 0.1 like the other waypoints), placed from
        the measured worst-case position 0.5 s before despawn.
      - **Re-verified, 7 pooled cycles:** same instance; on the NavMesh throughout; 11.31–11.32 m walked. He
        arrives by the agent's own test **0.52–0.57 s before despawn** and is braking 0.08 m from it when he
        vanishes. The peak is unchanged: 1.43–1.49 s at x 26.10–26.12 (`route-7cycles/arrival.txt`,
        `analysis.txt`).
      - The scene diff is that one transform.
- [x] [Review][Patch] **Nothing checked the player's CharacterController against NavMesh agent type 0**, which Task
      1g asked for. Only the drunk's agent and the config's eye/step were checked.
      [Assets/Scripts/ThirdPersonController.cs `TryGetAgentProblem`] (auditor+edge)
      - **Fix:** a check at `Awake` and `OnValidate`, using `PlayerConfig.AgentMatchesBody`.
      - **Positive control:** a 2.5 m / 0.4 m capsule gives exactly one clear warning.
      - **Negative:** the shipped 1.77 / 0.30 gives none, in every run.
- [x] [Review][Patch] **The camera's own config overrode the controller's, contradicting "the camera can never
      disagree with the body it rides on".** [ThirdPersonCamera.cs `Cfg`] (blind+auditor+edge)
      - **Fix:** the controller's config, the one the body check validates, wins. The camera's field is now only
        the fallback.
      - Today both point at the same asset, so behaviour is unchanged: pose read back at 1.650 / 0.316.
- [x] [Review][Patch] **"Tuning the asset during play takes effect at once" overclaimed.** `stepOffset` and the
      config check are applied at Awake only. The comment now says so. [ThirdPersonController.cs] (blind+edge)
- [x] [Review][Patch] **Stale comments the diff made stale.** [CharacterAnimator.cs Speed thresholds 7 → 5.5;
      EventActor.cs "8/100" → "2/25", the same 0.08 ratio] (auditor)
- [x] [Review][Patch] **Tests that would NullReferenceException instead of failing, and a half-asserted test.**
      [Assets/Tests/EditMode/PlayerConfigTests.cs] (blind+edge)
      - `Assert.IsNotNull` was added on the config, prefab, agent and agent type.
      - The eye half of the eye/near test now asserts the report, with a +Infinity case.
- [x] [Review][Patch] **"Findings for Story 2.2 are in ADR-5's consequences" was not true for the spawn.** The
      police-tape spawn (y 3.47) is now in ADR-5's consequences. (auditor)
- [x] [Review][Patch] **ADR-5's "0.37 radii past the capsule, as before" used a measure the story did not define.**
      The story's measure is body height: 4% → 6.3%. In metres it went 0.35 → 0.111, about 3× less. ADR-5 now
      states all three. (auditor)
- [x] [Review][Patch] **ADR-5's route row and "numbers moved" carried the wrong arrival claim and route.** Both are
      corrected to the measured 11.3 m / 0.52–0.57 s. The Dev Agent Record lines are annotated below.

**Deferred** (in `deferred-work.md` under "code review of story-2-1"):
- [x] [Review][Defer] **`EventActor.Begin` ignores `Warp`'s bool.** An off-mesh spawn would replay from the last
      despawn point with no warning. This predates 2.1; the bounded bake makes it easier to reach. Hypothesis: the
      shipped spawn is on the mesh in 7 of 7 cycles. [EventActor.cs:325]
- [x] [Review][Defer] **The bounded bake's Volume mode collects every collider in the box, and the voxel is pinned
      at 0.1.** Recorded as a 7.4 decision, made on an estimate. This is Story 2.2's whole-town bake. [SampleScene
      NavMeshSurface]
- [x] [Review][Defer] **Looking up lifts the near plane to ~2.05 m on a 1.77 m body.** The pitched nudge belongs to
      the see-through-walls family, which is Story 2.2's. Hypothesis, not reproduced. [ThirdPersonCamera.cs LateUpdate]
- [x] [Review][Defer] **The dead player Animator's Run threshold (5.5) is a second copy of the speeds.** Wire it to
      `PlayerConfig` when a rigged player lands. [CharacterAnimator.controller]

**Dismissed (17), with the reason:**
- **"The NavMesh bake is stale":** disproven by running. The play-mode triangulation bounds are exactly the new
  Volume, the surface's build settings read r 0.30 / h 1.77 / climb 0.35 / voxel 0.1, and the drunk is on the
  mesh.
- **".meta files are missing":** the review diff excluded them; all are committed.
- **"The drunk is scaled twice":** his prefab does not reference `main characters.fbx`.
- **"The SpawnPoint misfits":** it is local to EventManager (5, 20, 0); in world space it is (31.8, 0.55, 19).
- **"Other EventDefinitions keep 100/8":** `TownDrunk.asset` is the only one.
- **The FBX `humanDescription.globalScale` 0.002:** the importer wrote it, and there is no Avatar.
- **The re-flowed mixer-group line in the prefab:** serialisation noise.
- **"A stopping distance under the radius jitters":** 7 cycles show a smooth brake (0.23 m/s at despawn, 0.08 m
  out).
- **"Peak passes WP1 only 0.07 s before Peak":** he stands at x 26.11, 0.21 m from the peak spot, in every cycle.
- **"The body problem warns but keeps the bad value":** the story's fail-soft rule for eye-above-body is "log once
  and keep running", and that holds. Picking which of two authored values is wrong would be a guess.
- **Huge finite values (walk 1e38, jump > 1e37 overflows):** no designer types these. The realistic non-finite
  case, Infinity, is fixed.
- **`TryGetConfigProblem` reports the first problem only:** that is documented behaviour.
- **NaN body height, and ignoring the centre and lossyScale:** Unity clamps the capsule, and ADR-5 rule 2 forbids
  scaling a CharacterController root.
- **`groundStickVelocity` 0 is accepted:** it is a legitimate, if flickery, choice.
- **The near plane reaches through walls:** that is Story 2.2's (deferred-work "see-through walls"), and it
  shrank 3× in metres.
- **The rigs' 8.5 / 0.3 fallbacks and runner literals:** rigs are not review subjects.
- **Eye 1.65 rather than the measured 1.52:** the recorded decision 1b.

## Dev Notes

### What already exists. Read these before writing anything

- **The player** is `main characters` in `SampleScene.unity`, a PrefabInstance of `main characters.fbx` at
  (0, 20, 0) with no scale override. It carries:
  - a `CharacterController` (YAML ≈ l.327748–327771): height 8.86, radius 0.95, center (0, 4.43, 0),
    skinWidth 0.08, stepOffset 0.3, which `ThirdPersonController.Awake` (`:82`) overrides with its own
    `stepOffset`, 2;
  - `ThirdPersonController` (scene values ≈ l.327710–327715): walk **5**, sprint **10**, stepOffset 2, jump 1.2,
    gravity −15, mouse 0.15. The code defaults are 7 / 14 / 2 / 1.2 / −15, at `:13–34`;
  - `PlayerInput` (Send Messages): handlers `OnMove` `:134`, `OnJump` `:142`, `OnSprint` `:152`, `OnLook` `:161`,
    and `SetInputSuppressed` `:185`.

  The Animator has no Avatar, and `CharacterAnimator.controller` targets `Armature/…` paths that this model
  lacks, so it drives nothing. Its Speed thresholds (0.1 and 7) sit between walk and sprint; keep them
  consistent if the speeds change.
- **`ThirdPersonCamera.cs`** (global namespace, `Assets/Scripts/`) writes Main Camera every `LateUpdate`:
  the eye height fallback at `:79` (8.5) and the forward nudge at `:67` (1). Disable it while a rig scripts the
  camera. Main Camera has near 0.3 and far 1000, and is the scene's only `AudioListener`.
- **`PhotoModeController.cs`:**
  - `SetPhotoMode(bool)` `:263` is the real raise path, with `transitionSeconds` 0.15 at `:33` and the fade at
    `:219–222`;
  - `SetZoom` `:327`;
  - `Capture()` `:347`;
  - `CameraConfig.asset` holds FOV 60 → 18 and no distances.
- **The drunk:**
  - **`EventActor_Drunk.prefab`**:
    - root scale 1, pooled at (0, 20, 12);
    - `Armature` scale **0.01** (`:505`); `HusbandBody` is skinned, with `m_UpdateWhenOffscreen: 1`;
    - Animator: `m_ApplyRootMotion: 0`, culling Always Animate;
    - `NavMeshAgent` (`:822–843`): enabled 0 (`Begin` enables it), type 0, radius 1, height 8, speed 3.5,
      acceleration 8, angular 120, stopping 1.5;
    - cue `AudioSource` with min 1 / max 500 on the prefab, which `EventActor.Begin` (`:366–389`) overwrites
      from the definition with a **Custom** rolloff curve. The ratio is clamped at `:548`.
  - **`TownDrunk.asset`** (`Assets/Data/Events/`): `cueRadius 100`, `cueFalloffStart 8`, phases 3 / 10 / 1.5 / 6 / 4
    s. Only build, wind-down and despawn advance along the route.
  - **The route** is scene objects under `DrunkRoute` at (0, 0, 0). `WP0_leavePub` x 45, `WP1_peakSpot` 30,
    `WP2_recover` 15 and `WP3_alley` 0, all at y 2.3, z 19. `SpawnPoint` is at world (60, 2.3, 19), under
    `EventManager` at (5, 20, 0) (respawn 2 s, max 1).
    - Measured 2026-08-07: the peak at x ≈ 25.9 (t 13.0–14.5 s), x 0 reached at 22.4 s, ≈ 61 m walked at a
      mean of 3.27 m/s.
    - There is **no pub** on the map, despite the waypoint names.
  - `ISubject` / `EventActor`: `Bounds`, `IsAtPeak`, `PeakOffset`. There is a phase log `"TownDrunk → <phase>"`
    to grep in `Editor.log`, and `EventPeakedChannel` raised at the peak's start.
- **NavMesh:**
  - `NavMeshSurface` on the `game map` root (scene ≈ l.492179–492210): agent 0, `CollectObjects` Children,
    `UseGeometry` Physics Colliders, voxel 0.3167 (auto = r/3), tile 256, min region 2.
  - `ProjectSettings/NavMeshAreas.asset`, agent 0 "Humanoid": radius 0.95, height 8.86, climb 2, slope 45.
  - Data: `NavMesh-game map.asset`, 36 MB, plain git, 2026-06-04, ~491k verts.
  - The scene's **built-in** `NavMeshSettings` block (l.99–121: 0.5 / 2 / 0.4) is **unused**
    (`m_NavMeshData: 0`).
- **Rigs, with their menu paths:**

  | Menu path | Files | What it does |
  |---|---|---|
  | `Tools/Grading/Photo Shoot (Play)` | `Editor/PhotoShootRig.cs:83` + `PhotoMode/PhotoShootRunner.cs` | Private grey-plane world. Poses in subject heights at bounds-centre height (`:797–802`). Fixed near 0.3 / far 5000 (`PhotoShootRig.cs:344–345`). Its NavMesh bake follows agent type 0. Output in `verification/photo-shoot/`, which it **wipes**. |
  | `Tools/Grading/Photo Shoot — Placement Study (Town)` | `PhotoShootRig.cs:163` | Real town, never saved. The pattern to copy for the scale study. |
  | `Tools/Events/Trace Actor Route (Play)` | `Editor/RouteTraceRig.cs:47` + `Events/RouteTraceRunner.cs` | Per-frame CSV of the agent. `seconds` = 30 today. |
  | `Tools/HUD/Grade HUD Shoot (Play)` | `GradeHudShootRig.cs:112` + `UI/GradeHudShootRunner.cs` | Real town, picks its vantage by rendered pixels. Literal at `:1634`. |
  | `Tools/Gallery/Gallery Shoot (Play)` | `GalleryShootRig.cs:77` + `Gallery/GalleryShootRunner.cs` | Private world. Literal at `:268`. |

  The recording spike's rigs (`Tools/Recording Spike/*`) are fenced throwaway code: **do not modify them**.
- **Logging:** `GameLog.Info/Warn/Error`. `read_console` hides `Debug.Log`, so grep
  `%LOCALAPPDATA%\Unity\Editor\Editor.log`.
- **Tests:** `Assets/Tests/EditMode/` (168 tests). There is no PlayMode assembly.
- **Tooling:** ffmpeg/ffprobe 8.0.1, numpy and Pillow are installed; read `tools/verification/README.md`
  first. Blender runs headless for read-only map questions (lessons, plan-epic-2).

### Tuned-number inventory: starting list, compiled from files on 2026-11-10, to verify in the editor (AC1, Task 4.2)

Today's values are option (C). "CODE" marks a code literal, which AC2 says must move into config if the
chosen option moves it.

| # | Item | Where | Today (C) | Scale-tied? |
|---|---|---|---|---|
| 1 | Player CC height / radius / center / skin / step | scene CC; step overridden in `ThirdPersonController.Awake` `:82` | 8.86 / 0.95 / (0, 4.43, 0) / 0.08 / 0.3 → **2** | yes |
| 2 | Walk / sprint speed | scene `ThirdPersonController`; defaults `:13/:16` | **5 / 10** m/s (defaults 7 / 14) | travel time |
| 3 | Jump height / gravity | scene; defaults `:30/:34` | 1.2 m / −15 m/s² (air time ≈ 0.8 s) | yes / feel |
| 4 | Ground-stick velocity | `ThirdPersonController.cs:207` **CODE** | −2 m/s | weakly |
| 5 | **Eye height** | `ThirdPersonCamera.cs:79` **CODE**, the live path | 8.5 m above the feet | yes |
| 6 | Camera forward nudge | `ThirdPersonCamera.cs:67` **CODE** | 1 m | yes |
| 7 | Main Camera near / far | scene ≈ l.59223 | 0.3 / 1000 | near yes, far for (B) |
| 8 | Player model scale | `main characters.fbx` (file scale 0.01, `globalScale` 1) | eye spheres ≈ 7.56 m, CC 8.86 | yes |
| 9 | Player Animator Speed thresholds | `CharacterAnimator.controller` `:68/:93/:146/:199` | 0.1 / 7 (dead today) | follows speeds |
| 10 | Drunk visual scale | prefab `Armature` `:505` | 0.01 (≈ 8.2–8.75 m tall) | yes |
| 11 | Drunk agent radius / height / speed / accel / stop | prefab `:822–843` | 1 / 8 / 3.5 / 8 / 1.5 | yes |
| 12 | Cue radius / falloff start | `TownDrunk.asset:17–18`; defaults `EventDefinition.cs:71/:79` | 100 / 8 (25 m × **4**) | yes |
| 13 | Route and spawn | scene `DrunkRoute`, `SpawnPoint` | 60 m straight, z 19 | yes (length = speed × 20 s) |
| 14 | Phase durations | `TownDrunk.asset` | 3 / 10 / 1.5 / 6 / 4 s | no (time) |
| 15 | NavMesh agent type 0 | `NavMeshAreas.asset` | r 0.95, h 8.86, climb 2, slope 45 | yes |
| 16 | NavMesh surface voxel / tile / min region | scene `NavMeshSurface` | 0.3167 auto / 256 / 2 | yes |
| 17 | Shadow distance / cascades | `PC_RPAsset.asset:57` (Mobile also 50) | 50 m / 4 | for (B) |
| 18 | Fog / DoF | scene RenderSettings; `DefaultVolumeProfile` | fog off (linear 0–300), DoF off | for (B), if enabled |
| 19 | Physics gravity | `DynamicsManager.asset` | −9.81 (unused by the player) | for (B) |
| 20 | Grading config | `GradingConfig.asset` | **none**: frame fractions and seconds only | no |
| 21 | Camera config | `CameraConfig.asset` | **none**: FOV and zoom only | no |
| 22 | `ShotGrader` near epsilon | `ShotGrader.cs:176` CODE | 0.001 m | negligible |
| 23 | Photo Shoot rig | `PhotoShootRig.cs:331/:344–345`; `PhotoShootRunner.cs:437/:831` | ground ×20, near 0.3 / far 5000, idle cam (0, 8, 30), wall 0.4; poses in subject heights | near and idle cam yes |
| 24 | Gallery rig | `GalleryShootRig.cs:168/:181–182`; `GalleryShootRunner.cs:268/:1178` | same pattern | idle cam yes |
| 25 | HUD rig | `GradeHudShootRunner.cs:1634/:1691/:1942` | eye `+6f`, overlap 0.05, wall 0.4 | eye yes |
| 26 | Route-trace rig | `RouteTraceRunner.cs:35` | 30 s (one cycle) | no (needs ≥ 3 cycles) |
| 27 | Recording spike | `SpikeHarness.cs:947/:969`; `-vantage 38.59,8.29,6.27` | EyeHeight 7.5, survey 18 / 28 | yes, but **fenced, not modified** |
| 28 | Stale scale comments | `ShotGrader.cs:511–512`, `EventActor.cs:367–371`, `EventDefinition.cs:67–69`, `GalleryView.cs:236`, `GradeHudShootRunner.cs:1841`; `lessons.md:33` | "~4× metric" / "~5×" | docs |

### Architecture compliance (non-negotiable)

- **§Configuration.** Tunables live in ScriptableObjects in `Assets/Data/`; invariants live in static classes.
  "**Data lives in `Data/`**, code in `Scripts/` — never hard-code tunable numbers in code" (§Architectural
  Boundaries). AC2 is this rule applied.
- **`CameraGame.*` namespaces and one game asmdef** (AR5). New runtime code goes in `Assets/Scripts/<Feature>/`.
  `Player/` exists and is empty; it is the architecture's home for player code. Editor rigs go in
  `Assets/Scripts/Editor/`, and their runtime runners in the runtime assembly behind `#if UNITY_EDITOR`.
- **Do not rename `ThirdPersonController` or `ThirdPersonCamera`.** The architecture *recommends* renaming
  them to `Player*`/`FirstPerson*`, but that is not this story's job, and a rename churns every reference
  for no measured gain.
- **Grading (ADR-2) is not touched.** `ShotGrader.Grade` is dimensionless by design. If the grade changes
  at the new scale anywhere except where the *world* changed (occlusion), that is a finding about the rig or
  the scale change, never a reason to retune `GradingConfig`.
- **ADR-3: the events are data.** Retuning the drunk is an edit to `TownDrunk.asset` and to the prefab, never
  new code in `EventActor`.
- **Fail-soft (NFR8)** and **pooling (NFR3)**. Nothing in this story adds `Instantiate`/`Destroy` to a
  gameplay loop.
- **URP only (NFR4).** No material or shader changes are expected.
- **Cross-cutting rule pending in ADR-4** (proposed, not yet approved): everything that moves or makes sound
  must be recordable. A scale change does not affect that, and nothing here adds a non-recordable source.

### The one genuinely new risk in this story

**This story changes the world that every other system, rig, lesson and piece of evidence was measured in.**
Every earlier number was taken at a scale of about 5×: 7.5 m rig eye heights, "two subject-heights back is
18.7 m", cue radius 100, the spike's vantage, a 0.2 gate that passes at 38 m. After this story, every one of
them is either still true by construction (subject-relative) or stale. So:
1. **Baseline everything at the old scale with the new instruments first** (Task 2). Comparing a new rig at
   the new scale with an old rig at the old scale measures two changes at once.
2. **Expect real behaviour to change where the world is involved.** A person-sized drunk is hidden by a 1.7 m
   car, and that is correct. Do not "fix" the grader or the rig to bring back the old numbers.
3. **Leave the record honest.** Stale comments, the stale lesson and the deferred entries must say what is
   true now (AC5). Otherwise the next session builds on 7.5 m.

### Project Structure Notes

- **New:**
  - `Assets/Scripts/Player/PlayerConfig.cs` + `Assets/Data/Player/PlayerConfig.asset` (if 1g is followed);
  - `Assets/Scripts/Editor/ScaleStudyRig.cs` + `Assets/Scripts/Player/ScaleStudyRunner.cs`;
  - a cue-sweep runner and a movement/raise runner, which may share `ScaleStudyRig`'s menu as
    `Tools/Scale/*` items, or sit beside the existing rigs in `Events/` and `PhotoMode/`;
  - EditMode tests for the config validation and invariants;
  - `.meta` files for everything.
- **Updated:**
  - `ThirdPersonCamera.cs`, `ThirdPersonController.cs`, `SampleScene.unity` and `NavMesh-game map.asset`;
  - `ProjectSettings/NavMeshAreas.asset`, `main characters.fbx.meta`, `EventActor_Drunk.prefab` and
    `TownDrunk.asset`;
  - `EventDefinition.cs` (defaults and tooltips), `RouteTraceRunner.cs` (cycles and foot columns),
    `PhotoShootRunner.cs` / `PhotoShootRig.cs`, `GalleryShootRunner.cs`, `GradeHudShootRunner.cs`;
  - comments in `ShotGrader.cs`, `EventActor.cs` and `GalleryView.cs`;
  - `game-architecture.md`, `decision-log.md`, `deferred-work.md`, `loop/lessons.md:33`, the spike README
    (one line) and `sprint-status.yaml`.
- **Not touched:** grading code and config, capture, gallery, HUD behaviour, the spike's code, `epics.md`,
  `loop/dependencies.yaml` and `loop/runtime/`.
- **Outside git:** evidence in `_bmad-output/verification/2-1-settle-the-players-scale-in-the-town/`, with
  suggested subfolders `start/`, `options/`, `crossing/`, `inventory/`, `decision/`, `route/`, `cue/`,
  `movement/`, `photo-shoot-before-1/`, `-2/`, `photo-shoot-after/`, `photo-shoot-town-before|after/` and
  `before-after/`. **Never `Temp/`.**

### Previous Story Intelligence (1.2, 1.7, 1.8, 1.9–1.12, 1.14)

- **1.2** baked the NavMesh with the shared agent type 0, edited to the giant (0.95 / 8.86 / climb 2), over the
  whole `game map` (Collect Children, Physics Colliders). It called world scale "the single biggest gotcha"
  (1.2 Dev Notes guardrail 2), and Alexv logged "humanoid too tall" in that story's play-test.
  `Assets/Screenshots/story_1_2_play_pov.png` is an old "before" from the player's eye.
- **1.7** relaid the route straight along z 19 (`d079239`). Its review found the agent speed is Unity's
  default 3.5, so the drunk foot-slides with root motion off. It also found the peak fires past `WP1` because
  timers own the phases, and that arrival-early and arrival-late edges are unhandled. All three are this
  story's to settle at the new scale (Task 7.5).
- **1.8** set `cueRadius` 100 by arithmetic (25 m × 4) and replaced Logarithmic rolloff with a Custom curve.
  Under Logarithmic, `maxDistance` holds a −22 dB floor to infinity, and shrinking the radius *raises* that
  floor. It verified the result by polling settings, computing the curve, and Alexv's ear. Its evidence went
  to `Temp/` and is gone. **Measure from the output this time.**
- **1.9–1.12.** The Photo Shoot is the no-regression instrument. 1.12 retracted a committed "bounds are
  broken" diagnosis: the drunk really is 8.8 m, and the rig's stale vantage made it look broken. 1.12 also
  showed that the town occludes through trees, so a ray-based check can be wrong where a render is right.
  When visibility matters, measure it by rendering.
- **1.14** is the model for "decide by measuring": a recommendation per gating item, a table where every
  cell has evidence or "not measurable, because …", difference-in-differences against an interleaved
  baseline, and an ADR with a reversal cost. Its spike survey's first pass shot from **1.6 m and saw only the
  drunk's legs** (`1-14-…/survey/first-pass-1.6m/NOTE.txt`). That is direct evidence of the mismatch this
  story fixes. ADR-4 is still *Proposed*; 1.15 is gated on it and has not started, so no recorded replay
  exists to break.

### Inherited context you should not rediscover

- **CLAUDE.md traps:**
  - an editor-assembly MonoBehaviour is "missing" on a scene object in play mode;
  - render to an RT, or use `CaptureScreenshotAsTexture` after `WaitForEndOfFrame`, never the file
    overload;
  - assets loaded before `NewScene(Single)` lose their native half;
  - ProjectSettings edits on disk do not apply while the editor runs;
  - `Temp/` is deleted on shutdown;
  - an edit on disk is invisible to a rig until Unity reimports it;
  - a debug readout that outlives its instant gets read against the wrong frame;
  - disabling a manager does not empty its world.
- **Memory notes:**
  - `read_console` hides `Debug.Log`;
  - never run asset operations during play mode (it wedged a lifecycle in 1.8);
  - save a scene in a **separate** call before a rig;
  - poll the object that moves, never the anchor;
  - a rig can sit in `playmode_transition` for a long time, so poll the editor state before calling it hung;
  - hold inputs must be Value actions.
- **Loop lessons:**
  - 10: read back after refresh;
  - 12: second cycle onward;
  - 13: `last_updated` YAML;
  - 14: Gemini prompt rules;
  - 15: deactivate actors to empty a street;
  - 33: superseded by this story;
  - 37: SessionState arming flags expire;
  - 41–42: unmasked star diffs, Photo Shoot timing drift;
  - 47–50: headless Blender, the map census, the plain's layout, recursive grep times out (scope searches to
    `Assets/Scripts`, `_bmad-output/` and `loop/`).
- **Code review excludes test rigs** (memory). Reviewers look at the shipped changes and ADR-5. Rigs are
  tools, to be run.

### Git Intelligence

- `HEAD` is `85382ae` on `loop/2-1-settle-the-players-scale-in-the-town`. The last five commits are loop
  bookkeeping and Epic 2 planning: `e449cd3` planned 2.1–2.7, and `c018a64`/`85382ae` merged it and added
  lessons. **No game code has changed since Story 1.14 closed**, and 1.14 itself only added the fenced
  spike. The code above is current.
- The working tree is clean apart from the untracked `.codex/` and `AGENTS.md`. They are not yours, so do not
  stage them. **Stage by name** (`git add <path> <path>.meta`).
- Commit as you go, ending each message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never
  switch branches, merge, rebase, reset, stash or push.

### Latest technical information (this project's versions, so verify before you rely on it)

- **Versions:** Unity **6000.3.8f1**, URP **17.3.0**, AI Navigation **2.0.10**, Input System **1.18.0**, Test
  Framework 1.6.0.
- **Character Controller** (Unity Manual, "Character Controller component reference", fine-tuning): use
  around 2 m for a human-like character. Skin width about 10% of the radius. Step offset between about 0.1
  and 0.4 for a 2 m human. Min move distance near 0. Check the page if a number matters.
- **AI Navigation 2.x.** Agent types live in the `NavMeshProjectSettings` singleton (`NavMeshAreas.asset`).
  `NavMesh.GetSettingsByID(id)` returns a **copy**, and writing to it persists nothing. `NavMeshSurface` has
  `overrideVoxelSize`/`voxelSize`, `overrideTileSize`, `collectObjects`, `useGeometry` and `BuildNavMesh()`.
  The voxel size defaults to radius ÷ 3; a finer voxel is more precise and slower to bake. Check the editor
  helper that persists a bake with `unity_reflect` before using it.
- **Transform scale and components:** verify by reading back, never from memory.
  - the `CharacterController` capsule against the transform's scale;
  - `NavMeshAgent` radius and height against the transform's scale (its speed and stopping distance are
    world units);
  - `AudioSource` min/max distance never scale;
  - `AudioManager` Rolloff Scale against a Custom curve.
- **Input System 1.18.** `InputSystem.QueueStateEvent(device, state)` and `KeyboardState(params Key[])`.
  `InputSystem.settings.editorInputBehaviorInPlayMode` and `backgroundBehavior` govern whether input reaches
  an unfocused Game View. Check the exact names with `unity_reflect`.
- **Generic animation.** Clips animate bone local transforms under the armature, so a scale on the
  `Armature` transform scales the result uniformly. An importer scale changes the bind pose but not
  separately extracted clips.

### Verification plan: what would convince a sceptic

| Claim | Evidence | How it is judged |
|---|---|---|
| The starting point is what the files say | `start/start.json` (play-mode read-backs) | heights, eye point and factor measured animated; any disagreement with the six facts is written down |
| Each option was really staged | `options/` eye + witness frames, control shot | not uniform or white; the drunk is visible beside each object; (A) ≈ (B) except non-scaling settings |
| Eye height against objects | witness frames with drawn lines, ratio table | numbers on the picture match the table |
| Crossing times are real | `crossing/crossing.csv`, top-down trace | the drive control passes (no input → no motion); times ≈ distance ÷ measured speed |
| Every tuned number listed | `inventory/tuned-numbers.md` | every value read back through the editor; code literals marked |
| The decision follows the evidence | `decision/comparison.md`, ADR-5 | every cell has a path or "not measurable, because …"; reversal cost stated |
| Values live in config | `git diff` on the scripts; grep for the old literals | `8.5f`, `forward * 1f` and the rig eye literals are gone; values read back from the asset |
| NavMesh re-baked at body size | `NavMesh.GetSettingsByID(0)` read-back, asset size, bake time | agent equals the body; asset under 50 MB, or a recorded decision |
| Drunk lifecycle intact | `route/after-trace.csv`, per-cycle table | ≥ 3 cycles, on mesh throughout, same instance, reaches the end near despawn, peak 1.5 s |
| Foot sliding | slide speed before and after | reported in m/s; the visual read goes to `human_checks` |
| Cue to ~25 m and silent beyond | `cue/after-sweep.csv` + plot + floor control | the curve tracks the formula; within the stated threshold of the floor beyond 25 m |
| No grading regression | Photo Shoot diffs (stars and composition, unmasked) against both before-runs; eye scenarios against their own baseline | no verdict change beyond the measured noise; any change traced |
| Raise < 0.3 s; walk, sprint, jump | `movement/after-*.csv` | worst raise and lower < 0.3 s; measured speeds and apex match config |
| Project restored | `git status`, read-backs, scene YAML diff | only intended changes; SampleScene has no rig objects |

**Fail-soft scenarios to provoke on purpose** (say so at the top of the rig's output): run the game with
`PlayerConfig` unassigned, and with an invalid one (eye height above the controller's height). Each must log
**once** and keep running. Remove these from the console before the clean-console check.

### Human checks to queue (never self-certify)

1. **AC4: the town reads as a place where a person stands.** Alexv looks at `before-after/sheet.png` (and the
   option sheet). Include your honest read and the eye ratios.
2. **FR13 feel at the new eye height.** Does walking and sprinting feel right, and does the jump? Say what was
   measured (speeds, apex, air time) and exactly what to play: SampleScene, walk the street, sprint to the
   drunk, jump a kerb.
3. **FR11: the drunk reads as a drunk at his new speed, with no visible foot sliding.** Give the slide
   measurement and a short clip path.
4. **FR10: the cue by ear.** Is it audible from ~20–25 m in the street, and gone beyond? Give the measured dB
   at each distance.
5. **Decision batch, not a check:** ADR-5's choice and its reversal cost, plus 1b, 1g, 7.4, 7.5 and 4.1, go
   in `decisions`.

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 2.1; #Epic 2: The Living Town] — ACs (verbatim above); the three planning facts; sequencing
- [Source: _bmad-output/planning-artifacts/epics.md#Requirements Inventory] — FR10, FR11, FR13, FR14, FR15, NFR2, NFR3, NFR5, NFR8, AR9
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Architecture Decision Records] — ADR-4's form (the model for ADR-5)
- [Source: _bmad-output/planning-artifacts/game-architecture.md#Decision Summary; #Configuration; #Architectural Boundaries; #Event-Actor System] — touch points; the config rule
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md:156, :259] — lifecycle 20–30 s, peak ~1.5 s, cue ~25 m; vantage points
- [Source: _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md#2026-11-10 — The unattended loop] — decide, flag, keep going; batched perceptual checks
- [Source: _bmad-output/implementation-artifacts/deferred-work.md ~:12, :13, :20, :48–53, :59–63, :67, :106, :188–201] — the scale, NavMesh, route and camera entries
- [Source: _bmad-output/implementation-artifacts/1-2-walkable-town-patch-with-traversal-and-navmesh.md#Dev Notes; #Dev Agent Record] — the bake, the controller, "the biggest gotcha"
- [Source: _bmad-output/implementation-artifacts/1-7-the-town-drunk-event.md; 1-8-diegetic-cue-telegraphing.md] — route, agent, cue curve, the 25 × 4 conversion
- [Source: _bmad-output/implementation-artifacts/1-14-decide-how-the-game-records-video.md#Tasks; #Verification plan] — the decide-by-measuring template
- [Source: _bmad-output/verification/plan-epic-2/ (map_topdown.png, map_census.json, blend_inventory.json)] — the plain, object sizes, metric `.blend`
- [Source: _bmad-output/verification/photo-shoot-baseline-1-14/shots.txt; route-trace/route-trace.csv] — the old-scale baselines
- [Source: Assets/Scripts/ThirdPersonCamera.cs:40, :67, :79; ThirdPersonController.cs:13–34, :82, :134–185, :207] — the literals and the input handlers
- [Source: Assets/Scripts/PhotoMode/PhotoModeController.cs:33, :219–222, :263; PhotoShootRunner.cs:437, :797–835; Editor/PhotoShootRig.cs:164, :331–345] — the raise path; rig geometry
- [Source: Assets/Scripts/Events/EventActor.cs:366–389, :548; EventDefinition.cs:62–80; Assets/Data/Events/TownDrunk.asset; Assets/Prefabs/Events/EventActor_Drunk.prefab:505, :822–843] — cue and agent
- [Source: ProjectSettings/NavMeshAreas.asset; Assets/Scenes/SampleScene.unity (NavMeshSurface, DrunkRoute)] — the bake
- [Source: CLAUDE.md#Verifying Your Own Work; tools/verification/README.md; loop/lessons.md; loop/dependencies.yaml] — rigs, restore, evidence location, Gemini rules, dependencies

## Dev Agent Record

### Agent Model Used

Claude Opus 5.5 (`claude-opus-5-5`), loop DEV phase, attempt 1 (2026-11-10/11).

### Debug Log References

Evidence root (git-ignored): `_bmad-output/verification/2-1-settle-the-players-scale-in-the-town/`. Every rig run writes a
`log.txt` beside its output; `failsoft/editor-log-excerpt*.txt` hold the Editor.log windows of the fail-soft runs.
Rigs: `Tools/Scale/*` (`Assets/Scripts/Editor/ScaleStudyRig.cs` + `Assets/Scripts/Player/ScaleStudyRunner.cs`),
`Tools/Grading/Photo Shoot (Play)` (+ Placement Study), `Tools/Events/Trace Actor Route (Play)` (now 95 s),
`Tools/Gallery/Gallery Shoot (Play)`, `Tools/HUD/Grade HUD Shoot (Play)`. Analysis scripts: `tools/*.py` in the evidence root.

### Completion Notes List

- 2026-11-10 (loop CREATE phase): story context created. Ultimate context engine analysis completed - comprehensive developer guide created.
- **2026-11-11 Task 1 (gating decisions)**: full record with evidence in `decision/task1-decisions.md`.
  - 1a: `start/start.json`, measured in play mode. Player 8.86 m (model 8.864); camera 8.50 up and 1.00 forward
    of the feet; the model's eye spheres at 7.58 (0.855 of stature); spawn lands at y 3.609. Drunk idle 8.34 /
    walk 8.43 m.
  - 1a references: the police car's roof 2.56 / length 9.46; door head 2.81 above its deck; pine canopy 0.65 /
    top 6.93; eave 5.63 / ridge 10.31; street 13.18 m. **F = 5.0**, which retires 4×.
  - 1b: 1.77 m stature, **eye 1.65 m** (0.93 × stature, not the model's cartoon-head eye spheres; reasons in the
    file). The references disagree and were recorded, not averaged: door ~1.4× real, police car ~2×, road, house
    and pines ~1×.
  - 1c: followed the recommendation. (A) stages the eye as a "head bone" marker, so the REAL ThirdPersonCamera
    places the camera. The drunk's clips were read back: rotation-only, constant-1 scale curves, none on `Armature`.
  - 1d: Keyboard state events, followed. The drive control passes in every run.
  - 1e: listener tap, followed. "Silent" was fixed before the run as within 1 dB of the deactivated floor.
    Rolloff Scale reads 1, a no-op.
  - 1f: no gate, followed; `loop/dependencies.yaml` untouched.
  - 1g: the `PlayerConfig` SO, followed, plus `cameraNearClip` (the rigs need it).
- **Task 2 (old-scale baselines)**:
  - Photo Shoot run 1 vs run 2 (`photo-shoot-before-1/`, `-2/`): only `i_from_behind` moves (timing noise,
    lesson 42); run 1 also matches the 1.14 baseline on every other shot.
  - Eye-height poses added and shot at 8.5 m (`photo-shoot-before-eye/`): money shot 5★ 100%, `c_far_eye`
    TooSmall, `f_wall_eye` Occluded. The rig edit moved no existing shot.
  - Town study at the old scale (`photo-shoot-town-before/`): 24/24 counted.
  - Route trace at the old scale (`route/before-trace.csv`, `before-analysis.txt`): 3 full cycles plus a
    partial, same pooled instance, 60 m. He reaches the end at 21.7 s, 2.8 s before despawn. Peak 1.37–1.49 s at
    x 25.9 (4 m past WP1). Planted-foot slide +0.65 m/s.
  - Cue at the old scale (`cue/before/`): tracks the curve within ~2–3 dB; silent from 100 world m, which is
    **20 person-m, not 25**.
  - Movement at the old scale (`movement/before/`): walk 5.00 / sprint 10.00 m/s measured; jump apex 1.29,
    air 0.81 s; raise worst 0.171 s, lower worst 0.170 s.
- **Task 3**: `options/C|A|B/` and `options/contact-sheet.png`, each with eye views and witness frames.
  - Similarity (`options/similarity.txt`): \|A−B\| 5–9 grey levels against \|A−C\| 33–47. Not pixel-identical,
    as similar triangles predict.
  - (B)'s 16,738 colliders re-scale in 60 ms with no new console line.
- **Task 4**: `crossing/crossing.csv` (measured rows plus labelled computed rows) and `crossing/topdown-traces.png`.
  - Plain 810 m: (C) and (A) at 5 / 10 m/s 161.9 / 81.0 s; (A) at 4 / 7 m/s 202.4 / 115.6 s; (B) at 25 / 50 m/s
    161.9 / 80.9 s; (B) at 5 / 10 m/s 809 / 405 s computed.
  - Inventory: `inventory/tuned-numbers.md`. Derived checks (computed): `inventory/derived-checks.md`.
  - Route fixes: the spawn route needed waypoints (a pine sits on the straight line). The far-west start moved
    inside a tyre line that fences the west quarter off at person scale. First runs kept as `crossing/*-run1*`.
- **Task 5**: `decision/comparison.md`. **(A) chosen**, speeds 4 / 7.
- **Task 6**: ADR-5 written (Accepted, Alexv may veto), with touch points: Decision Summary row 10, row 8, the
  Event-Actor cue, frontmatter `updated`, an Executive Summary bullet. Also `decision-log.md`,
  `deferred-work.md` (:13, :20, :60, :61, :62 resolved, :67 partly; :12 left to 2.2), `loop/lessons.md:33`
  superseded in place, and one note in the spike README. `epics.md` is untouched; follow-ups are below.
- **Task 7 (applied)**:
  - `PlayerConfig` (walk 4, sprint 7, step 0.35, jump 1.2, gravity −15, stick −2, eye 1.65, nudge 0.316,
    near 0.095), wired into both components. The 8.5 / 1 / −2 literals are gone.
  - Player importer `globalScale` 0.2: model 1.773 m. CharacterController 1.77 / 0.30 / (0, 0.885, 0) / 0.03.
  - Drunk `Armature` 0.002, agent r 0.3 h 1.77. Agent speed **0.6 m/s**, set from the stride measured at the new
    size (`route/stride-probe-trace.csv`: +2.9 m/s slide at 3.5). Acceleration and stopping scaled to 1.37 / 0.26.
  - NavMesh agent 0 is 0.30 / 1.77 / 0.35 / 45, read back.
  - Slice-bounded re-bake at voxel 0.1: 1.5 s, **0.22 MB**, was 34 MB (`navmesh/bake.txt`).
  - Route re-laid to 11.5 m: spawn x 31.8, waypoints 28.8 / 25.9 / 22.8 / 19.8. *(Review 2026-11-11: `WP3_alley`
    moved to 20.4, so 11.3 m walked; see Review Findings.)*
  - Cue 25 / 2 m.
  - Rigs read the eye height and near clip from config. Stale "4×" comments rewritten. Animator Run threshold
    7 → 5.5 (that Animator is dead).
  - Scene diff checked: only the intended changes. No deactivated child exists in the player hierarchy (searched).
  - Pressed against a wall (`wall/wall-old-vs-after.png`): the near plane stops 0.36 m (old) / 0.12 m (new) short
    of the surface it faces, 0.38 / 0.40 capsule radii, so it is no worse in body proportions.
- **Task 8 (verified at the new scale)**:
  - Route (`route/after-analysis.txt`): 3 full cycles plus a partial, same pooled instance, on the NavMesh
    throughout. 11.5 m walked; he reaches the end at 23.6 s, 0.9 s before despawn. Peak 1.45–1.49 s at x 26.1.
    *(Corrected by the 2026-11-11 review: "reaches" used a 1 m radius. By the agent's own arrival test he never
    arrived, and vanished mid-stride 0.45–0.51 m short. With `WP3_alley` at 20.4 he now arrives 0.52–0.57 s before
    despawn, over 7 cycles.)*
    Slide ±0.15 m/s per foot, +0.02 median.
  - Cue (`cue/after/`): tracks the curve within 1.2 dB. −53.9 dBFS at 20 m and −68.6 at 24 m. Exactly
    digital silence (= the deactivated floor) from 25 m.
  - Photo Shoot after (`photo-shoot-after/diff.txt`): no verdict moved beyond the noise. Eye-height poses are
    identical to their old-scale baseline. `k_straddling` and `l_inside_away` are unchanged with near 0.095.
  - Town study after (`photo-shoot-town-after/before-after-12dirs.png`): grades identical, 0/24 moved. But the
    pines now fully hide him in 3 of 12 directions (2 before). The grader's occlusion rays do not see the pines
    (Story 1.12), so this is world-dependent and **not a grader regression**. Not fixed.
  - Movement after (`movement/after/`): walk 4.00 / sprint 7.00 m/s measured. Jump apex 1.26, air 0.81 s. A real
    0.126 m kerb at x 0.5 is climbed. The 1.65 m slab edge blocks him (the finding). Raise worst 0.166 s, lower
    worst 0.161 s.
  - A real-time clip of one lifecycle from a person-height camera beside the route (`Tools/Scale/Film Drunk Walk (Play)`):
    `film/after/drunk-walk-after-realtime.mp4`. The rig captured at ~8.4 fps, so it is choppy; that is the rig, not the game.
    A pine hides him for part of the walk.
  - Foot sliding was settled by measurement; Gemini was not used, since it is optional and the look goes to
    Alexv.
- **Task 9**: `before-after/sheet.png`. My read: before is an 8.5 m eye over a carpet of pines with a giant's face
  filling the frame; after is a person on a road, at a porch, in a pine wood. The police car and the porch door
  read oversized (~2× / ~1.4×).
- **Task 10**:
  - EditMode **185/185** (168 + 17 new `PlayerConfigTests`).
  - Clean console in a normal SampleScene play session: only the baseline head-bone warning.
  - Gallery and HUD rigs run with 0 exceptions. Their logged errors are their own provoked fail-soft cases.
  - Fail-soft: no PlayerConfig → one warning, runs on defaults (walk 4.00 measured). Eye above body → one
    runtime warning, game runs.
  - Restored and read back: SampleScene clean with no rig objects; timeScale 1; runInBackground true (as at
    start); quality PC; input settings at their originals; SessionState keys cleared. No build was made.
- **Findings for Story 2.2** (in ADR-5's consequences):
  - The road is a raised slab with 1.2–1.65 m edges, reachable only near x 0.
  - A tyre line fences off the plain's west quarter.
  - The police car is ~2× real; the porch door ~1.4×; the bus-shelter roof ~9 m up.
  - The census "street block" is buried ~60 m underground.
  - The player now spawns standing on the 1.4 m police-tape barrier (y 3.47).
- **Follow-ups (not done here)**:
  - `epics.md` wording that ADR-5 makes stale: the "~5×" / "8.86 m" planning facts in §Epic 2 and Story 2.1's
    own preamble. It was not edited, per the story.
  - The whole-town NavMesh bake is Story 2.2's AC.
  - The grader's blindness to pine occlusion (Story 1.12) now matters more at person scale.
- **Open for Alexv**: AC4 (before/after look), FR13 feel, the drunk's walk, and the cue by ear. Queued as human
  checks; never self-certified.

### File List

New:
- `Assets/Scripts/Player/PlayerConfig.cs` (+ `.meta`)
- `Assets/Data/Player.meta`, `Assets/Data/Player/PlayerConfig.asset` (+ `.meta`)
- `Assets/Scripts/Player/ScaleStudyRunner.cs` (+ `.meta`), `Assets/Scripts/Editor/ScaleStudyRig.cs` (+ `.meta`)
- `Assets/Tests/EditMode/PlayerConfigTests.cs` (+ `.meta`)

Modified:
- `Assets/Scripts/ThirdPersonController.cs`, `Assets/Scripts/ThirdPersonCamera.cs`
- `Assets/Scripts/CharacterAnimator.cs` (comment only; review 2026-11-11)
- `Assets/Scenes/SampleScene.unity`, `Assets/Scenes/SampleScene/NavMesh-game map.asset`
- `ProjectSettings/NavMeshAreas.asset`
- `Assets/Models/main characters.fbx.meta`
- `Assets/Prefabs/Events/EventActor_Drunk.prefab`, `Assets/Data/Events/TownDrunk.asset`
- `Assets/Animations/CharacterAnimator.controller`
- `Assets/Scripts/Events/EventDefinition.cs`, `Assets/Scripts/Events/EventActor.cs` (comment)
- `Assets/Scripts/Grading/ShotGrader.cs` (comment only), `Assets/Scripts/Gallery/GalleryView.cs` (comment only)
- Rigs:
  - `Assets/Scripts/PhotoMode/PhotoShootRunner.cs`, `Assets/Scripts/Editor/PhotoShootRig.cs`
  - `Assets/Scripts/Events/RouteTraceRunner.cs`, `Assets/Scripts/Editor/RouteTraceRig.cs`
  - `Assets/Scripts/Gallery/GalleryShootRunner.cs`, `Assets/Scripts/Editor/GalleryShootRig.cs`
  - `Assets/Scripts/UI/GradeHudShootRunner.cs`, `Assets/Scripts/Editor/GradeHudShootRig.cs`
- `Assets/Spikes/RecordingSpike/README.md` (one note, no code)
- `_bmad-output/planning-artifacts/game-architecture.md`, `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md`
- `_bmad-output/implementation-artifacts/deferred-work.md`, `_bmad-output/implementation-artifacts/sprint-status.yaml`
- `loop/lessons.md`
- `_bmad-output/implementation-artifacts/2-1-settle-the-players-scale-in-the-town.md`

## Change Log

- 2026-11-10: Story file created by the loop CREATE phase (Claude Opus 5.5). Status → ready-for-dev.
- 2026-11-11: Implemented by the loop DEV phase (Claude Opus 5.5). ADR-5 accepted, option (A) applied: people are 1.77 m with the eye at 1.65 m, F = 5.0; verified at the new scale. Status → review (AC4 and the feel/sound checks are queued for Alexv).
- 2026-11-11: Code review by the loop REVIEW phase (Claude Opus 5.5): 0 decisions, 11 patches applied and re-verified by running, 4 deferred, 17 dismissed. Fixed: an infinite gravity or ground-stick wedging the player, the gallery opening invisibly under a raised near clip, the drunk vanishing short of his route end (`WP3_alley` 19.8 → 20.4), and the missing player-vs-NavMesh-agent check. ADR-5's route and near-clip rows corrected. Status stays `review` (AC4 and the perceptual checks are open for Alexv).
