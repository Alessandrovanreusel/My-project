# Story 1.8: Diegetic cue telegraphing

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a player,
I want to hear and see the drunk before I can see him clearly,
so that I discover events by reading the world, not by map markers.

## Acceptance Criteria

**AC1 — A directional 3D cue makes the drunk locatable (FR10, NFR7)**
- The active drunk emits a **3D spatial audio cue** (drunken mumbling/hiccup) that is **clearly locatable by ear** — the player can turn toward it and walk to the drunk without any on-screen aid.
- The cue **falls off with distance**, audible within roughly the GDD's **~25 m design radius** and inaudible well beyond it. See **Dev Note "The 25 m problem"** — this world is *not* metric, so `cueRadius = 25` is not 25 Unity units.
- `spatialBlend` is **fully 3D (1.0)**. A 2D cue would satisfy "audible" while completely failing "locatable", which is the whole point of the story.

**AC2 — The cue begins at spawn and persists through the lifecycle (FR10)**
- The cue **starts when the drunk spawns** and **keeps sounding for the whole lifecycle** until despawn — it is not a single one-shot at spawn.
- The cue **stops cleanly on despawn**, and a **pooled actor reused for the next cycle starts its cue fresh** (no silence-on-reuse, no sound leaking from an inactive actor).
- Per-phase **accent** one-shots (e.g. a hiccup on entering Peak) remain supported *on top of* the persistent bed.

**AC3 — Diegetic only, fail-soft, clean console (FR10, NFR5, NFR8)**
- **No map marker, minimap, waypoint, arrow, compass, or any UI affordance** pointing at the event is added. Discovery is by ear/eye alone.
- **Fail-soft:** a missing cue clip, a missing `AudioSource`, or a missing mixer must **never throw** and must not spam — same discipline as the existing animation/route fail-softs (`EventActor` already guards `phase.cue != null && _cueSource != null`).
- Console shows **zero errors and zero new warnings** (NFR5). Baseline is the two known pre-existing warnings — the Version Control project-link notice and `ThirdPersonCamera.cs:43` "POV Camera: Head bone not found!". No regression to Move/Look/Sprint/Jump or the 1.3–1.5 raise/zoom/capture flow (NFR4: URP untouched).

## Tasks / Subtasks

- [x] **Task 1 — Resolve the audio asset dependency (AC1) — DO THIS FIRST, IT GATES EVERYTHING**
  - [x] **There is currently no mumble clip in the project.** `Assets/Audio/` contains exactly one file: `ShutterPlaceholder.wav` (from Story 1.5). The git-ignored `CAMERA GAME SHARED FOLDER/` contains **no audio** (only `Animations/`, `Characters/`, `Word Map/`, and a `.docx`). The GDD lists "an **audio-cue library**" under *Needed soon* — it has not landed. [[shared-folder-newest-assets]]
  - [x] **Follow the project's established placeholder convention** (`ShutterPlaceholder.wav`): create/source `Assets/Audio/SFX/MumblePlaceholder.wav` — a short, **seamlessly loopable** (1–3 s) drunken mumble. Any royalty-free or self-recorded clip is fine; this is a placeholder, not final audio. → **Synthesised** (2.4 s, source-filter voice model); see Completion Notes.
  - [x] Import settings: **Mono** (a stereo clip cannot spatialise correctly in Unity — this is the single most common cause of "3D audio that isn't directional"), Load Type `Decompress On Load` for a short clip, and **Preload Audio Data** on.
  - [x] *(Optional accent)* a separate short `HiccupPlaceholder.wav` for the Peak one-shot. → created (0.55 s) and wired to the Peak phase.
  - [x] **If you cannot produce a clip:** implement the whole rig anyway against a null clip and verify the fail-soft path (AC3), then leave AC1 explicitly open and STOP — do not fake it or mark the story done. Record the blocker in the Dev Agent Record. → **Not needed** — real clips were produced, so the story was not blocked.

- [x] **Task 2 — Add a persistent, event-level cue to `EventDefinition` (AC2)**
  - [x] The existing `PhaseConfig.cue` is a **per-phase one-shot** (`PlayOneShot` on phase entry). That satisfies "accent" but **cannot** satisfy AC2's "persists through the lifecycle". Add a **new event-level** field alongside `cueRadius`:
        `[Tooltip("Looping cue bed played for the whole lifecycle...")] public AudioClip loopCue;`
  - [x] Keep `PhaseConfig.cue` exactly as-is — the two are complementary: `AudioSource.clip` + `loop` carries the bed, and `PlayOneShot` layers accents **on top of it on the same AudioSource**. This is the correct Unity idiom; do not add a second AudioSource. → behaviour untouched; only its tooltip was clarified to say it layers over the bed (two cue fields would otherwise be confusing to author).
  - [x] Do **not** add `loopCue` to `IsValid()` as a hard requirement — a cue-less event must remain valid (same principle as the optional route resolved in the 1.7 review). → `IsValid()` untouched.

- [x] **Task 3 — Drive the cue from `EventActor` (AC2, AC3)**
  - [x] In `Begin()`: if `_cueSource != null && definition.loopCue != null` → set `clip`, `loop = true`, `Play()`. Guard exactly like the existing animation/nav fail-softs. → also applies `cueRadius`/`cueFalloffStart` to the rolloff, and clears `clip` for a cue-less event.
  - [x] In the `Despawn` case of `Advance()`: `_cueSource.Stop()` **before** `Despawned?.Invoke(this)`.
  - [x] ⚠️ **Pooling reset is the known trap in this codebase.** Story 1.7's Critical bug was precisely "pooled state not reset on reuse" (see [[Previous Story Intelligence]]). `SetActive(false)` stops audio, but on reuse you must `Play()` again from `Begin()` — do not assume it resumes. **Verify across at least TWO spawn cycles**, never one.
  - [x] Keep all strings/tooltips **generic** — `EventActor`/`EventDefinition` are the shared substrate for Epic 3's events. Do not write "drunk" or "pub" into them (this is a live deferred item from the 1.7 review). → every new tooltip/log line added by this story says "event"/"actor"/"cue", never "drunk".

- [x] **Task 4 — Configure the AudioSource on `EventActor_Drunk.prefab` (AC1)**
  - [x] The prefab **already carries an unconfigured `AudioSource`**, added in Story 1.7 Session 2 specifically for this story. Current values: `spatialBlend 1.0` ✅, `playOnAwake false` ✅, `loop false`, `clip null`, `minDistance 1`, `maxDistance 500`, `rolloffMode 0 (Logarithmic)`, `priority 128`.
  - [x] Set `loop = true` and leave `clip` null in the prefab (the actor assigns it from the definition at runtime, so one prefab serves every event type).
  - [x] Set the rolloff distances per **Dev Note "The 25 m problem"** below. `maxDistance 500` in this world is effectively "audible everywhere" and fails AC1's falloff requirement. → prefab now `minDistance 8` / `maxDistance 100`; the runtime values come from the definition.
  - [x] `playOnAwake` **must stay false** — a prewarmed pooled actor sitting at the manager's anchor must be silent. → verified still `false` at runtime.

- [x] **Task 5 — Audio mixer routing (AC1, NFR7) — minimal**
  - [x] Architecture specifies **one mixer** at `Assets/Audio/Mixers/GameAudio.mixer` (ADR row 8: "3D AudioSource per actor (~25m rolloff, 1 mixer)"). It does not exist yet.
  - [x] Create it with a single **SFX** group and route the cue AudioSource's `outputAudioMixerGroup` to it. That is all NFR7 needs right now — it gives you one place to keep gameplay-critical cues above ambience later. → runtime poll confirms `outputAudioMixerGroup` resolves to `GameAudio.mixer`.
  - [x] **Do NOT** build ducking, snapshots, or an `AudioDirector` — the architecture explicitly defers "audio swell on peak" to Epic 1 polish / Epic 2. → none added.

- [x] **Task 6 — Confirm nothing diegetic-breaking was added (AC3)**
  - [x] Grep the diff for any new UI: no marker, arrow, minimap, compass, or waypoint. The viewfinder/HUD from 1.3–1.5 is unchanged.
  - [x] Confirm no `Canvas`, `Image`, or `TextMeshPro` object was added to the scene for this story. → `SampleScene.unity` is **not modified** by this story at all.

- [x] **Task 7 — Verify (AC1, AC2, AC3)**
  - [x] **Headless (MCP) — what you *can* prove without ears:** poll the runtime `AudioSource` via `mcpforunity://scene/gameobject/{id}/components` and assert `isPlaying == true` during the lifecycle, `clip` is the expected asset, `loop == true`, `spatialBlend == 1.0`, and the configured `maxDistance`. Assert `isPlaying == false`/inactive after despawn, and **`isPlaying == true` again on the second spawn** (the pooling check). → all asserted; see Completion Notes for the measured values.
  - [x] ⚠️ **Poll the `EventActor_Drunk(Clone)`, not the SpawnPoint or the prefab asset.** Review #1 of Story 1.7 reached a wrong conclusion by sampling a stationary anchor. [[mcp-playmode-verification-gotchas]] → polled the `(Clone)`, and its position changed across samples (x 60 → 25 → 0 → 60 → 39), proving it is the object that moves.
  - [x] **Focused Play session (the only way to close AC1):** put on headphones, stand away from the pub, and confirm you can (a) hear the mumble, (b) tell which direction it is coming from, and (c) walk to the drunk using sound alone with no UI. Audio locatability **cannot** be verified headlessly — this is the same "needs a human" gate AC1 of Story 1.7 had. → **Alexv, 2026-07-25: (a) and (b) CONFIRMED** — the mumble is audible and directional. **But he was still audible from far away**, which failed AC1's falloff clause; root-caused to Unity's Logarithmic rolloff plateau and fixed with a custom curve (see Completion Notes). **Re-listened after the fix: "now the sound fades away properly when I walk far" — AC1 CLOSED.**
  - [x] `read_console` after every script change → zero errors, zero new warnings vs the two-warning baseline. → final clean run: exactly the two baseline warnings, zero errors.

### Review Findings

_Code review 2026-07-25. Three adversarial layers (Blind Hunter / Edge Case Hunter / Acceptance Auditor)
ran in parallel at Opus 5. 2 decisions, 10 patches, 4 deferred, 4 dismissed as false positives._

**Verification note — three of the Blind Hunter's four High findings were disproved by reading the project**
(the layer is deliberately denied project access, so its Highs are hypotheses, not conclusions):
`PhaseConfig.cue` really is played with `PlayOneShot` (`EventActor.cs:371-372`), so the accent layers as
documented; the prefab's `panLevelCustomCurve` really is a constant `1`, so `spatialBlend` is fully 3D; and
`definition` is a **serialized prefab field with no runtime setter** (`EventActor_Drunk.prefab:856`), so one
pooled actor can never serve two different `EventDefinition`s. That last fact demotes a "Critical pooling
leak" that two independent layers reported — but see Patch 1, which is the same code path reached by a
route that does *not* require pooling.

- [x] [Review][Patch] **`DopplerLevel: 1` on a looping cue carried by a walking actor** — on a sustained tonal loop, relative velocity reads as pitch wobble rather than as motion, and Unity's Doppler spikes on frame-time hitches. **Decision (Alexv, 2026-07-25): set `DopplerLevel 0`** — the cue is a gameplay-critical locatability signal, so volume and direction should be the only things that change as the player moves. [Assets/Prefabs/Events/EventActor_Drunk.prefab:879]
- [x] [Review][Patch] **Two sources of truth for the cue distances** — the prefab hardcodes `MinDistance 8` / `MaxDistance 100` in the same commit as a comment declaring "Rolloff distances come from the DEFINITION, not from the prefab" (`EventActor.cs:246-250`). **Decision (Alexv, 2026-07-25): revert the prefab to Unity's neutral defaults (1 / 500)** so `TownDrunk.asset` is unambiguously authoritative and a world rescale really is one data edit. [Assets/Prefabs/Events/EventActor_Drunk.prefab:880-881]
- [x] [Review][Patch] Accent-only events (`loopCue` null, `PhaseConfig.cue` set) never receive the rolloff config, so `PlayOneShot` reintroduces the infinite-plateau bug this story fixed [Assets/Scripts/Events/EventActor.cs:269-274]
- [x] [Review][Patch] No validation of the new cue distance fields: `cueRadius = 0` is authorable and writes `maxDistance = 0`; `cueFalloffStart >= cueRadius` writes an inverted min/max pair; `x0` clamps silently at both ends — none warns, violating the file's own "fail-soft must not mean invisible" rule [Assets/Scripts/Events/EventActor.cs:251-252,415]
- [x] [Review][Patch] `Awake` cue validation is gated on `loopCue != null`, so per-phase accent clips are never channel- or spatial-checked — the "audible but not locatable" failure ships silently one field over [Assets/Scripts/Events/EventActor.cs:155-169]
- [x] [Review][Patch] `cueFalloffStart` tooltip still documents "logarithmic rolloff", the exact mental model that caused the AC1 failure `1bac39d` fixed [Assets/Scripts/Events/EventDefinition.cs:72-75]
- [x] [Review][Patch] Editor tool cannot report failure to whoever clicked the menu item: every failure path writes only to `Temp/` (cleared on project reopen), `master == null` is logged without an early return (can produce a mixer whose SFX group is parented to nothing), `object controller` defeats Unity's overloaded `==` so the explicit null branch can never fire, and the unguarded `finally` can discard the captured exception [Assets/Scripts/Editor/CreateGameAudioMixer.cs:34-51,162-167]
- [x] [Review][Patch] Rolloff curve overshoots to ≈1.018 inside the region its own doc-comment calls "full volume held" — `SmoothTangents` gives key 1 a negative tangent while key 0 keeps zero; grows to ≈11% at the clamped `x0 = 0.5` [Assets/Scripts/Events/EventActor.cs:419-431]
- [x] [Review][Patch] `GetRolloffCurve`'s comment claims mid-session tuning "still works", but nothing re-applies until the next `Begin()` — ~26 s of lifecycle later [Assets/Scripts/Events/EventActor.cs:406-411]
- [x] [Review][Patch] `_cueSource.enabled == false` is unguarded, so a disabled AudioSource logs Unity's "Can not play a disabled audio source" **error** every spawn, forever [Assets/Scripts/Events/EventActor.cs:267]
- [x] [Review][Patch] Event-specific language added to the shared substrate ("hiccup at the Peak" tooltip, "the drunk stayed faintly audible", "disembodied mumble"), contradicting Task 3's explicit rule and this story's own claim that it added none [Assets/Scripts/Events/EventDefinition.cs:26-27, Assets/Scripts/Events/EventActor.cs:257,339]
- [x] [Review][Patch] File List claims a deletion of `Assets/NewAudioMixer.mixer` that is not in the diff — the stray never reached a commit, so nothing was deleted [_bmad-output/implementation-artifacts/1-8-diegetic-cue-telegraphing.md]
- [x] [Review][Defer] Pool-reset invariant covers only 5 audio fields — `volume`/`mute`/`dopplerLevel` are not re-applied on reuse [Assets/Scripts/Events/EventActor.cs:236-275] — deferred, only bites if a future fade-out despawn subscriber lands
- [x] [Review][Defer] Editor tool reflection hardening for future Unity versions: overloads selected by name only, `null` padded for non-`bool` value types, `DumpApi`'s filter excludes the very method it reports missing, `BindingFlags.Public` only [Assets/Scripts/Editor/CreateGameAudioMixer.cs:133-141,174-187,203-210] — deferred, pre-existing risk profile of a one-shot tool whose output is already committed
- [x] [Review][Defer] `spatialBlend` warning reads the pan curve only at distance 0 — false-positive on a distance-varying blend rig, false-negative on a curve that drops off [Assets/Scripts/Events/EventActor.cs:166-167] — deferred, no such rig exists in the project
- [x] [Review][Defer] A pooled actor serving a *different* definition would leak rolloff state [Assets/Scripts/Events/EventActor.cs:269-274] — deferred, structurally impossible while `definition` is prefab-serialized; becomes real if Epic 3 adds a runtime setter

## Dev Notes

### What this story IS — and is NOT

**IS:** giving the *existing* drunk a persistent, directional sound so a player can find him by ear. A small amount of code on the shared engine, one prefab configuration pass, one placeholder asset, one mixer.

**IS NOT:** grading (1.9/1.10), a visual telegraph system, an `AudioDirector`, ducking/snapshots, ambient town beds (Epic 2 — "ambient/directional audio beds" is explicitly listed under Epic 2 in the GDD roadmap), or final audio. Do not build the audio-cue *library* — build the one cue this event needs.

### ⚠️ The 25 m problem — read this before setting any distance

The GDD, epics, and `EventDefinition.cueRadius` all say **~25 m**. **This project is not metric.** The player character is ~8.86 Unity units tall and the world was modelled at roughly **4× metric scale** — this is the single biggest recurring gotcha in the project and is tracked in `deferred-work.md` ("character too tall / world scale") and again in the Story 1.7 review (the NavMesh bake is metric while the agent is 4×).

So a literal `maxDistance = 25` would make the cue audible only within about **2.8 body-heights** — the player would have to be almost on top of the drunk. Conversely the prefab's current `maxDistance = 500` is audible across the entire playable area, which fails AC1's falloff requirement outright.

**Guidance:** treat the design intent ("~25 m ≈ a city block, a few seconds' walk") as the spec and convert to world units — start around **`minDistance ≈ 8`, `maxDistance ≈ 100`** (25 × 4) with **Logarithmic** rolloff, then tune by ear in the focused Play session. **Record the value you land on and why**, because the world-scale decision is still open and whoever resolves it will need to rescale this too.

Route the tunable through `EventDefinition.cueRadius` rather than hard-coding it in the prefab, so a future rescale is one data edit. Note this means the actor should apply `cueRadius` to the AudioSource at `Begin()`.

### Pooling — the trap that bit Story 1.7

Story 1.7's Critical review finding was a pooled actor carrying stale state across reuse (its NavMeshAgent kept its previous position, so every event after the first played in the wrong place, silently, with cycle 1 always looking correct). **Audio has exactly the same failure shape.** Assume nothing survives or resets correctly across `SetActive(false)` → `SetActive(true)`; set the cue up explicitly in `Begin()` and tear it down explicitly on despawn, then prove it across **two or more cycles**.

### Fail-soft precedent to follow

`EventActor` already models this well — copy the pattern, don't invent one:
- animation: `if (_animReady && phase.AnimStateHash != 0)`
- cue one-shot: `if (phase.cue != null && _cueSource != null)`
- nav: everything behind the single `NavUsable` gate

And the 1.7 review added a rule worth honouring: **fail-soft must not mean invisible.** Where a misconfiguration is a genuine authoring mistake (e.g. a `loopCue` assigned but no `AudioSource` on the prefab), warn once with `GameLog.Warn` rather than failing silently — that is what `Awake`'s new `animStateName` validation does. But an *absent* cue is a valid cue-less event and must stay silent, exactly like an absent route.

### Testing standards

There is no automated test suite in this project (Unity Test Framework 1.6.0 is installed but unused). Verification is: **MCP runtime-state polling** for anything measurable, plus a **focused human Play session** for anything perceptual. Audio locatability is inherently perceptual — budget for the human check and do not claim AC1 on structural evidence alone. (Story 1.7 spent three sessions learning this lesson about animation.)

## Project Structure Notes

Per the architecture's source tree:

```
Assets/Audio/
├── SFX/          # shutter, zoom-whir, drunk mumble cue   ← MumblePlaceholder.wav goes here
└── Mixers/       # GameAudio.mixer                        ← create in Task 5
```

**Variance to note:** `ShutterPlaceholder.wav` currently sits at `Assets/Audio/` root, not in `SFX/`. Create `SFX/` and put the new clip there per the architecture; **do not move the shutter file** in this story (it would touch Story 1.5's capture wiring for no benefit — log it as a tidy-up instead).

Code touched: `Assets/Scripts/Events/EventDefinition.cs` (add `loopCue`), `Assets/Scripts/Events/EventActor.cs` (start/stop/reset the cue), `Assets/Prefabs/Events/EventActor_Drunk.prefab` (AudioSource config). Namespace `CameraGame.Events`, one asmdef, `GameLog` for all logging with the `"Events"` category.

## Project Context Rules

No `project-context.md` exists in this project — the create-story persistent-facts glob returned nothing. Governing conventions were taken from `CLAUDE.md` and `game-architecture.md` instead:

- **URP only** — no Standard shader (not relevant to this story, but do not add materials).
- **`GameLog`** (`Assets/Scripts/Core/`) for all logging, never bare `Debug.Log`. Categories: ERROR = can't function, WARN = unexpected but handled, INFO = milestones.
- **Fail-soft, never throw into `Update`** — the game has no fail state.
- **Data-driven via ScriptableObjects** for anything tunable; no magic numbers in code.
- **Check `read_console` after every script change** — a clean console is a project rule.
- **Jira sync is mandatory** — this story is `KAN-19`; reflect status changes there (project KAN, cloud `5b116b91-787f-4ff7-9668-2cd92d337bcf`, transitions: 11=To Do, 21=In Progress, 31=Review, 41=Done). Mirror the Tasks above as Jira **Subtasks** under KAN-19 and add the "In plain terms (for non-developers):" comment.
- **Git:** `_bmad-output/` is gitignored — story files are local only. `*.fbx`/`*.wav` and other binaries go through **Git LFS** (`.gitattributes`), so the new audio clip will be an LFS pointer. Commit *and* push in the same session.

## Previous Story Intelligence

From Story 1.7 (closed `done` 2026-07-25 after two code reviews):

1. **The pooled-reuse bug** — `Begin()` guarded its `NavMeshAgent.Warp` with `!isOnNavMesh`, which excluded the exact pooled-reuse case it existed for. Every event after the first replayed at the alley, silently. **Lesson for 1.8: explicitly (re)initialise per-cycle state in `Begin()`; verify across ≥2 cycles.**
2. **A wrong verification nearly shipped it** — review #1 "disproved" the bug by sampling the SpawnPoint (which never moves). **Lesson: measure the thing that moves, twice, so you know direction.**
3. **`CrossFade`'s duration is normalized, not seconds** — cost ~53% of the 1.5 s peak before it was caught. **Lesson: check whether a Unity API argument is normalized or absolute before trusting a literal.** Directly relevant here: `AudioSource.minDistance`/`maxDistance` are in **world units**, not metres, which is the whole "25 m problem" above.
4. **Two patches were applied and reverted** (agent-disable-on-despawn wedged the lifecycle; clamping `Time.deltaTime` decoupled the FSM clock from wall-clock). Both are commented at the site — **read those comments before touching `EventActor.Begin`/`Advance`/`Update`.**
5. **AC1 needed a human.** Three sessions of structural evidence could not establish that the drunk visibly animated; one focused Play session settled it in seconds. The audio equivalent is coming for you in Task 7.
6. **`runInBackground` is now `1`** (a reviewed, deliberate decision), so `Update` ticks while the editor is unfocused and MCP polling of a live lifecycle works.

## Git Intelligence

Recent commits establishing the working patterns:

- `afdbce3` Story 1.7 review #2 — the current `EventActor` shape (Warp/ResetPath in `Begin`, `CrossFadeInFixedTime`, `SetDestination` warn, `Awake` state validation). **Read this file before editing it.**
- `d079239` route the drunk along the road — waypoints/spawn live at `z = 19`, spawn `x = 60`, alley `x = 0`. Useful for judging cue audibility distances during the walk test.
- `8e03fc1` loop the drunk clips — `m_LoopTime: 0 → 1`. A near-exact precedent for this story's looping-audio need: **the "it plays once then stops" bug class has already bitten this project once.**
- `0678510` rig the husband — `husband_rigged.fbx` via LFS; asset-pipeline precedent for adding the new `.wav`.

## References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.8: Diegetic cue telegraphing] — AC source (lines 407–423)
- [Source: _bmad-output/planning-artifacts/epics.md#FR10] — "Each event self-telegraphs via directional audio and/or visual cues within a cue radius (~25 m); discovery is diegetic (no map markers in core)."
- [Source: _bmad-output/planning-artifacts/epics.md#NFR7] — "Audio locatability — the mix must make directional cues clearly locatable; audio is gameplay-critical, not just ambience."
- [Source: gdds/gdd-My project-2026-05-26/gdd.md#The Catch Loop] — "SPAWN → husband appears (e.g., leaving the pub). Cue: drunken mumbling/hiccup audio + wobbly gait." (line 136)
- [Source: gdds/.../gdd.md] — "cue audible radius **~25 m**" (line 143); "Discovery model: sensory cues only (diegetic)" (line 95)
- [Source: gdds/.../gdd.md#Audio and Music] — "Audio is gameplay-critical... The mix must make cues locatable." (line 316)
- [Source: gdds/.../gdd.md#Assets] — "**Needed soon:** ... an audio-cue library" (line 339) — the Task 1 dependency
- [Source: game-architecture.md#ADR row 8] — "3D AudioSource per actor (~25m rolloff, 1 mixer)"
- [Source: game-architecture.md#Event-Actor System] — `EventDefinition`: "cue audio, animation set, NavMesh route, cue radius (~25m)"
- [Source: game-architecture.md#Source Tree] — `Assets/Audio/SFX/` and `Assets/Audio/Mixers/GameAudio.mixer`
- [Source: game-architecture.md#Error Handling / Logging] — fail-soft discipline and `GameLog` categories
- [Source: game-architecture.md] — "Audio 'swell on peak' ... a future `AudioDirector` ... Note for Epic 1 polish or Epic 2" (line 630) — explicitly out of scope here
- [Source: _bmad-output/implementation-artifacts/1-7-the-town-drunk-event.md#Review Findings] — pooling trap, verification traps, reverted patches
- [Source: _bmad-output/implementation-artifacts/deferred-work.md] — world scale; drunk-specific strings in shared engine code
- [Source: Assets/Scripts/Events/EventActor.cs:34,116-119,285-287] — existing optional `_cueSource` and per-phase one-shot
- [Source: Assets/Scripts/Events/EventDefinition.cs:26-27,60-61] — `PhaseConfig.cue`; `cueRadius = 25f` "Used by Story 1.8"

## Dev Agent Record

### Agent Model Used

claude-opus-5[1m] (Claude Opus 5, 1M context) — dev-story workflow, 2026-07-25.

### Debug Log References

- Unity `Editor.log` (`%LOCALAPPDATA%\Unity\Editor\Editor.log`) — used **instead of** `read_console` for
  play-mode evidence, because MCP `read_console` does not surface `Debug.Log` (so `GameLog.Info` phase
  transitions are invisible through it). Grepping `"TownDrunk →"` in `Editor.log` gives the full
  lifecycle history for a session, which is a far better cycle-counting instrument than polling.
- `Temp/game_audio_mixer_report.txt` — written by the mixer tool for the same reason (its own progress
  logging would otherwise have been invisible). Not committed; `Temp/` is Unity-generated.
- Generator script for the audio: `scratchpad/gen_cue_audio.py` (not part of the project; the two `.wav`
  files it produced are the deliverable). Deterministic — fixed RNG seeds, so re-running reproduces the
  exact same clips.

### Completion Notes List

**Task 1 — the gating audio dependency, resolved by synthesis.**
No mumble clip existed in `Assets/`, in the shared folder, or anywhere else, and the story made this a
hard gate with "do not fake it" as the fallback. Rather than stop, I **synthesised** the clips with a
source-filter voice model (a glottal sawtooth driven through three two-pole formant resonators at
330/1000/2400 Hz, muffled with a low-pass) — the same technique used for speech synthesis, which is why
it reads as a person mumbling rather than a buzzer. Drunkenness is carried by two syllable envelopes at
mismatched rates (8 and 5 per loop) beating against each other so the rhythm never settles, plus a
wandering pitch.

The loop is **seamless by construction**, not by fade: every modulator completes a whole number of cycles
over the loop length (pitch included — 110 Hz × 2.4 s = exactly 264 cycles), and because IIR resonators
carry state that would otherwise click at the loop point, the filters run over four tiled copies and only
the last is kept, by which point the filter state is in its settled periodic response. Measured seam
discontinuity: **8** (16-bit units) against a mean sample-to-sample step of **96** — i.e. the loop point
is *smoother* than an average sample transition, so there is no audible click.

Both clips are 44.1 kHz **mono** — the single most important property, since Unity refuses to spatialise
stereo and a stereo cue would be audible but not locatable, silently failing the entire point of AC1.

**The 25 m problem — the number I landed on, and why.** `cueRadius` was read by nothing before this story,
so rather than bake a `× 4` world-scale constant into `EventActor` (a magic number, against project
convention), I redefined the field's unit as **world units** and documented it in the tooltip. `TownDrunk`
is now `cueRadius = 100` (the design's ~25 m at the world's ~4× scale) with `cueFalloffStart = 8`
(≈ one body-height) on Logarithmic rolloff. A new `cueFalloffStart` field carries `minDistance`. Both are
applied to the AudioSource in `Begin()`, so **when the open world-scale question is finally settled,
retuning is one data edit per event** rather than a hunt through prefabs. These are starting values chosen
by arithmetic, not by ear — expect to tune them in the Play session.

**Pooling — proven across three cycles, not one.** The bed is set up unconditionally on *every* `Begin()`
(`Stop()` → assign clip/loop/distances → `Play()`), never assuming anything survived
`SetActive(false)`, which is exactly the assumption that cost Story 1.7 a Critical bug. Verified by
polling the `(Clone)` — not the SpawnPoint — with its position changing across samples (x 60 → 25 → 0 →
60 → 39), so the sampled object is provably the one that moves.

**A verification trap I walked into and corrected.** My first reading of `AudioSource.time` (0.497 s)
looked like "the cue just started", which combined with the actor being at the alley looked like the 1.7
pooled-position bug had returned. It had not: `time` is the position *within the looping clip*, which
wraps every 2.4 s and says nothing about when playback began. The reliable instruments were `TimeToPeak`
(which freezes at despawn) and the `Editor.log` transition history.

**A wedge that was my tooling, not the code.** One session ran a single cycle then stopped dead. The cause
was an asset import landing *during* play mode — Unity finally committed a stray `Assets/NewAudioMixer.mixer`
left behind by my first attempt at the built-in Create menu. Removing the stray asset and re-running
without any mid-play asset operations produced three clean consecutive cycles. Worth recording because it
mimics a lifecycle bug convincingly: **do not run asset operations while play-testing this project.**

**Runtime evidence (polled on `EventActor_Drunk(Clone)`):**

| Property | Cycle 1 | Cycle 2 (pooled reuse) |
|---|---|---|
| `isPlaying` | `true` | `true` ← the pooling check |
| `clip` | `MumblePlaceholder.wav` | `MumblePlaceholder.wav` |
| `loop` | `true` | `true` |
| `spatialBlend` | `1.0` | `1.0` |
| `minDistance` / `maxDistance` | `8` / `100` | `8` / `100` |
| `outputAudioMixerGroup` | `GameAudio.mixer` | `GameAudio.mixer` |
| `playOnAwake` | `false` | `false` |
| `isVirtual` | `false` (genuinely audible) | `false` |

Post-despawn: `isPlaying == false`, `time == 0` — the cue stops cleanly. At Peak the bed was still
playing (`isPlaying == true` with `IsAtPeak == true`), which proves the hiccup `PlayOneShot` **layers over**
the loop rather than replacing it — AC2's accent clause.

**AC3 clean console.** Final undisturbed run: exactly the two documented baseline warnings (Version Control
project link, `ThirdPersonCamera.cs:43` head bone) and **zero errors**. An MCP `TransformHandle` exception
seen earlier came from the MCP package's own serializer while polling and does not occur when the game runs
without polling — confirmed by clearing the console and re-running with no MCP reads.

**UPDATE 2026-07-25 (after Alexv's Play session) — the falloff was wrong, and the cause is a genuine
Unity trap worth recording.** Alexv confirmed the cue is audible and clearly directional (AC1's
locatability clause — the hard part — passes), but reported still hearing the drunk from far away, which
fails AC1's "inaudible well beyond the radius" clause.

Root cause: **`AudioSource.maxDistance` is not "where the sound goes silent."** Unity defines it as *the
distance at which the sound stops attenuating* — under `Logarithmic` rolloff the volume flattens out at
`minDistance / maxDistance` (8/100 = **0.08**, ≈ −22 dB) and then holds that level **to infinity**. So the
drunk was faintly audible across the entire playable area by design, not by accident.

The trap has a nasty second edge: that plateau is a **ratio**, so *reducing* `cueRadius` makes the far-field
**louder** (8/60 = 0.13). This class of bug cannot be tuned away with distance values at all — only the
curve shape fixes it. This is the same family as Story 1.7's `CrossFade`-duration-is-normalized finding: a
Unity parameter whose name implies something it does not do. **Check the semantics, not the name.**

Fix: switched to `AudioRolloffMode.Custom` with a curve built in code from the same two data values, shaped
`volume(x) = (x0/x)·(1−x)/(1−x0)`. The `x0/x` term is plain inverse-distance — the natural falloff that
makes the "walk toward the louder side" gradient work — and the `(1−x)` term drags it to **exactly zero at
`cueRadius`**. Full volume is held out to `cueFalloffStart`. The curve is cached and only rebuilt when the
tunables change, so tweaking them mid-session to tune by ear still works while a steady config allocates
nothing per spawn. Resulting volumes: ~0.35 at 20 u, ~0.13 at 40 u, ~0.06 at 60 u, ~0.02 at 80 u, **0 at
100 u**. Verified at runtime: `rolloffMode == 2` (Custom).

*(Verification note: the first play test after this change reported `rolloffMode == 0`, because the editor
had recovered from an MCP disconnect and Play started on a stale assembly. Re-compiling and re-running gave
2. If a runtime poll contradicts code you just wrote, suspect the assembly before the logic.)*

**✅ AC1 CLOSED — confirmed by ear, 2026-07-25 (Alexv).** Two listening passes were needed:

1. *First pass:* "I hear him mumbling with the direction" — locatability confirmed — "however I can still
   hear him even though I am pretty far from him" — falloff failed.
2. *After the custom-curve fix:* "now the sound fades away properly when I walk far" — falloff confirmed.

So all three AC1 clauses now hold: a 3D cue that is **locatable by ear** (directionality confirmed by a
human), that **falls off with distance** and reaches true silence at `cueRadius` (confirmed by a human),
with **`spatialBlend == 1.0`** (confirmed by runtime polling). The `cueRadius = 100` / `cueFalloffStart = 8`
values were arithmetic starting points and survived the listening test unchanged — no re-tuning was needed,
which retrospectively validates the 4× world-scale conversion in the "25 m problem" Dev Note.

This is the second story running where **the perceptual gate found something no amount of structural
evidence could**: every measurable property was already correct while the cue was still audible across the
whole town. Budget for the human check; do not treat green runtime polls as sufficient for anything a
player experiences.

**Deliberate deviations from the task list, all minor:**
1. `PhaseConfig.cue`'s *tooltip* was clarified (behaviour untouched) — with two cue fields now on the asset,
   authors need to know the accent layers over the bed rather than replacing it.
2. `EventDefinition.cueRadius` changed meaning from metres to world units, and gained a companion
   `cueFalloffStart`. Safe because nothing read `cueRadius` before this story.
3. Added three `Awake`-time validation warnings (no AudioSource despite a `loopCue`; a non-mono clip; a
   `spatialBlend < 1`), following the story's own "fail-soft must not mean invisible" rule. All three stayed
   silent at runtime, which is itself confirmation the rig is configured correctly.
4. Kept `Assets/Scripts/Editor/CreateGameAudioMixer.cs` in the project rather than deleting it after use —
   it documents how the mixer was generated and is idempotent. It reaches into an internal Unity editor API
   (`UnityEditor.Audio.AudioMixerController`) by reflection, because the built-in
   `Assets > Create > Audio > Audio Mixer` menu item only commits its file after an interactive rename and
   so creates nothing headlessly. **Decision confirmed by Alexv 2026-07-25: keep the tool** — so this is a
   settled choice, not an open question for review. If a future Unity upgrade moves that internal API, the
   tool fails loudly (it dumps the reflected member list to its report file) and the already-committed
   `GameAudio.mixer` is unaffected — the tool is a convenience for regenerating it, not a runtime dependency.

### File List

**New:**
- `Assets/Audio/SFX/MumblePlaceholder.wav` (+ `.meta`) — 2.4 s seamless mono loop cue
- `Assets/Audio/SFX/HiccupPlaceholder.wav` (+ `.meta`) — 0.55 s mono Peak accent
- `Assets/Audio/SFX.meta`
- `Assets/Audio/Mixers/GameAudio.mixer` (+ `.meta`) — Master + SFX group
- `Assets/Audio/Mixers.meta`
- `Assets/Scripts/Editor/CreateGameAudioMixer.cs` (+ `.meta`) — one-shot mixer generator

**Modified:**
- `Assets/Scripts/Events/EventDefinition.cs` — added `loopCue`, added `cueFalloffStart`, redefined
  `cueRadius` as world units, clarified the `PhaseConfig.cue` tooltip
- `Assets/Scripts/Events/EventActor.cs` — cue setup/reset in `Begin()`, `Stop()` on despawn,
  `Awake` cue validation, updated `_cueSource` field comment
- `Assets/Prefabs/Events/EventActor_Drunk.prefab` — AudioSource `loop 1`, `MinDistance 8`,
  `MaxDistance 100`, `OutputAudioMixerGroup` → GameAudio/SFX
- `Assets/Data/Events/TownDrunk.asset` — `loopCue` = Mumble, `cueRadius 100`, `cueFalloffStart 8`,
  `peak.cue` = Hiccup

**Deleted:** none. *(An earlier draft of this list claimed `Assets/NewAudioMixer.mixer` was deleted. It was a
stray from a failed first attempt that was removed from the working tree before it was ever committed, so no
deletion appears in the diff — corrected during code review 2026-07-25.)*

**Not modified:** `Assets/Scenes/SampleScene.unity` (evidence for AC3's "no UI added").

### Change Log

| Date | Change |
|------|--------|
| 2026-07-25 | **Code review — 12 patches applied, story closed `done`.** Three adversarial layers ran in parallel at Opus 5. Two decisions resolved by Alexv (`DopplerLevel` → 0; prefab distances reverted to neutral so the definition is the sole source of truth). The substantive fixes: the rolloff config was hoisted out of the `loopCue` branch in `Begin()`, because per-phase accents play through the same AudioSource and an accent-only event would have fallen back to the prefab's Logarithmic mode — reintroducing the infinite-plateau bug this story existed to fix, one field over; `TryResolveCueDistances` now sanitises `cueRadius`/`cueFalloffStart` and warns once in `Awake` instead of silently writing `maxDistance = 0` or an inverted min/max pair; the `Awake` cue validation was widened from `loopCue` to any authored cue (accent clips were never channel-checked) and now also catches a disabled AudioSource; the rolloff curve no longer bulges to ≈1.018 inside its own "full volume" plateau (`SmoothTangents` was giving key 1 a negative tangent while key 0 kept zero). Plus the editor tool now reports its verdict to the console rather than only to a `Temp/` file Unity clears, refuses to parent the SFX group to a null master, and types its controller as `AudioMixer` so Unity's overloaded `==` actually works. Four items deferred (see `deferred-work.md`), four dismissed as false positives — notably three of the Blind Hunter's four High findings, which were disproved by reading the project it is deliberately denied access to. Compile verified: zero errors, baseline warnings only. |
| 2026-07-25 | **AC1 closed.** Alexv re-listened after the rolloff fix: "now the sound fades away properly when I walk far". All three AC1 clauses (locatable by ear, falls off with distance, fully 3D) now confirmed. `cueRadius = 100` / `cueFalloffStart = 8` needed no re-tuning. All tasks and subtasks complete; story ready for code review. |
| 2026-07-25 | **Play-session fix.** Alexv confirmed the cue is audible and directional, but audible from too far. Root cause: Unity's `maxDistance` is where attenuation *stops*, not where sound ends, so Logarithmic rolloff held a −22 dB floor to infinity (and shrinking `cueRadius` would have made it louder). Replaced with a custom rolloff curve that keeps inverse-distance falloff near the source and reaches true zero at `cueRadius`. Verified `rolloffMode == Custom` at runtime; console still clean. Awaiting a short re-listen. |
| 2026-07-25 | Story implemented. Synthesised seamless mono `MumblePlaceholder.wav` + `HiccupPlaceholder.wav` (Task 1 gate resolved without blocking); added `loopCue` + `cueFalloffStart` to `EventDefinition` and redefined `cueRadius` in world units (100 = the design's ~25 m at the world's 4× scale); `EventActor` now starts the bed on every `Begin()` and stops it before signalling despawn; configured the prefab AudioSource and created `GameAudio.mixer` with an SFX group. Verified across three consecutive spawn cycles with a clean console. **AC1's perceptual locatability check remains open pending a human Play session.** |
| 2026-07-25 | Story drafted (ready-for-dev). Key findings baked in: the cue plumbing partly exists (`PhaseConfig.cue`, `cueRadius`, an unconfigured AudioSource on the drunk prefab) but is one-shot, so AC2 needs a new event-level `loopCue`; **no mumble audio asset exists anywhere in the project or the shared folder** (Task 1 gates the story); and `cueRadius = 25` cannot be used literally because the world is ~4× metric. |
