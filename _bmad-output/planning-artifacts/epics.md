---
stepsCompleted: [1, 2, 3, 4]
workflowStatus: complete
inputDocuments:
  - _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md
  - _bmad-output/planning-artifacts/game-architecture.md
  - "CAMERA GAME SHARED FOLDER/Camera game.docx (concept context)"
  - "CAMERA GAME SHARED FOLDER/ (asset inventory context)"
# jiraSync removed 2026-11-10 - Jira was dropped from the project. The former epic/story/subtask -> KAN key
# mapping is in git history, e.g. git show 1e3ab88:_bmad-output/planning-artifacts/epics.md
---

# Camera Game - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for **Camera Game**, decomposing the requirements from the GDD and Architecture into implementable stories. (No standalone UX Design document exists yet; UI requirements — viewfinder overlay, grade-feedback HUD — are captured as functional requirements derived from the GDD and Architecture.)

The epic structure mirrors the GDD's validated 8-epic plan. **Epic 1 (the First Playable Slice) is the only epic that should be broken into detailed, ready-to-build stories until the core Catch Loop is proven fun.** Later epics are scoped at the requirement level here and detailed just-in-time.

## Requirements Inventory

### Functional Requirements

**Core Catch Loop (Epic 1 — MVP)**

*Revised 2026-11-10 (`sprint-change-proposal-2026-11-10.md`): at the stakeholder's request the core verb changed from still photography to **video recording**. FR1–FR8 below were rewritten for clips, and FR27–FR28 were added. Each revised FR keeps a note of its photo-era wording.*

FR1: The game has two camera states — **Walk** (exploration) and **Viewfinder** (first-person through the camcorder) — toggled by holding the raise-camera input (RMB / gamepad LT); releasing returns to Walk. *(Revised 2026-11-10: "Photo" state renamed **Viewfinder** now that video replaces photo. The code name `PhotoMode` / `PhotoModeController` is unchanged.)*
FR2: In Viewfinder state the player can aim freely and **zoom** in/out (scroll wheel / gamepad) to compose the frame (zoom ~1×–4×, camera FOV ~60°→~18°).
FR3: The player can **start and stop recording a clip** (LMB / gamepad RT) while in Viewfinder state. Lowering the camera or a full memory card also stops recording, and the clip so far is kept. Record is a no-op in Walk state. *(Revised 2026-11-10: was "The player can **capture** a photo with a click (LMB / gamepad RT) while in Photo state; capture is a no-op in Walk state.")*
FR4: A recorded clip is **graded** by sampling the per-frame shot grader — **Subject Capture** (gate) and **Composition** — throughout the recording, plus **Peak coverage**, blended into a 1–5 star (0–100%) grade. *(Revised 2026-11-10: was "A captured shot is graded on three dimensions — Subject Capture, Composition, and Timing". The per-frame grader built in Stories 1.9–1.10 is unchanged and becomes the engine underneath clip grading.)*
FR5: **Subject-capture gate** — the shot fails (near-zero) if the target subject is not within the frame, is occluded, or is **too small in the frame**. *(Revised 2026-07-28 by the Story 1.10 code review: originally "occupies below ~8% of the frame". The gate is now the subject box height as a fraction of the FRAME HEIGHT, shipped at 0.20 — the same measure FR6 scores on. The old ~8% was an AREA figure, and area around a tall thin figure is mostly empty air: a good full-body portrait measures 4.5% by area and 39% by height. FR6 was rewritten for this on 2026-07-27; this clause was left behind and is corrected here so the two describe one measure. Do not "restore" the 8%.)* *(2026-11-10: the gate is evaluated on every sampled frame of a clip; a clip in which the subject never passes it is a miss, with the reason.)*
FR6: **Composition scoring** — score is higher when the subject is prominent in the frame and sits near the **centre**; lower when too small, cut off, or pushed toward a corner. *(Revised 2026-07-27 by Story 1.10: originally specified a rule-of-thirds bonus and "~25–50% of the frame". The thirds bonus was built, photographed in matched pairs, and judged worse than centred — in the empty test world and again in the real town. Prominence is now the subject's height as a fraction of the frame, since "25–50%" measured as area reads 4.5% for a good full-body portrait. See the GDD's grading section and Story 1.10's Dev Agent Record.)* *(2026-11-10: evaluated on every sampled frame of a clip.)*
FR7: **Peak coverage** — a clip earns full marks when it contains the event's whole **peak window** with the subject framed, and partial credit for partial coverage. A well-framed clip that missed the peak still scores above zero via a designer-set floor, but far below one that caught it. *(Revised 2026-11-10. This replaces the photo-era **Timing scoring**: "full marks within ±0.5 s of the event's peak window, decaying to zero by ±2 s beyond it", clarified 2026-07-28 to measure from the window's nearest edge via `ISubject.PeakOffset`, because the peak is an INTERVAL — 1.5 s for the Town Drunk — not an instant. With video, catching the peak becomes a question of coverage rather than of one click's timing. The "above zero" floor carries the intent of superseded Story 1.13.)*
FR8: Recorded clips are **saved to the memory card** with their grade, subject identity, time, duration and a thumbnail. *(Revised 2026-11-10: was "Captured shots are **saved to a gallery** with their grade and subject identity.")*
FR9: Every catchable event runs a **lifecycle** — Spawn → Build → Peak → Wind-Down → Despawn — advancing on its own timers.
FR10: Each event **self-telegraphs** via directional audio and/or visual cues within a cue radius (~25 m); discovery is diegetic (no map markers in core).
FR11: The **Town Drunk** event is playable end-to-end using the husband model and Drunk Pack animations: spawns (leaving the pub) → stumble-walks → peak stagger/near-fall (~1.5 s money-shot window) → recovers → despawns into an alley (~20–30 s lifecycle).
FR12: A **small playable patch of town** exists to walk around in for the slice (walkable surface with colliders).
FR13: The player explores with **free-roam traversal** (WASD/stick move, sprint to chase a moment) using the existing controller.
FR27: **Memory card capacity** — the card holds a limited amount of footage (designer-tunable, in seconds). The viewfinder shows the time remaining. When the card fills, recording stops (the clip so far is kept) and the viewfinder shows **CARD FULL**. Deleting a clip frees its space. *(Added 2026-11-10.)*
FR28: **Clip playback** — the player can watch any clip on the memory card in-game, with the game sound it recorded, and delete it from there. *(Added 2026-11-10.)*

**The Living Town (Epic 2)**

FR14: **Vantage points** (rooftops, hills, docks) are reachable and give better composition angles.
FR15: The full **town map** is imported (Blender → FBX), zoned into **districts** (downtown, docks/waterfront, residential, oil rig/industrial, mountain/nature), with colliders and a baked NavMesh; reactive directional ambient audio per district.

**Event Content Pack #1 (Epic 3)**

FR16: **3–5 additional event types** are playable, each reusing the event-actor system as data, not new code (e.g., oil-rig smoke break, cheating husband, fisher falling in, graffiti tagger), with the animations they require sourced/authored.

**Progression & Reputation (Epic 4)**

FR17: Banking a graded clip earns **Reputation** — higher grades and rarer subjects pay more. *(2026-11-10: was "graded shot".)*
FR18: **Reputation tiers** unlock rarer/bigger events, new districts, camera upgrades, and home-base features.
FR19: A **"town log" collection** tracks captured event types and nature subjects with completion and rarity tiers.
FR20: **"New best" tracking** records the player's best grade per subject/event, rewarding re-capture.

**Home Base (Epic 5)**

FR21: A **home-base interior hub** lets the player review clips on the memory card, **print stills** pulled from clips, and **decorate** the house with them. Once Epic 9 exists, it also gives the Edit Suite a physical room. *(Revised 2026-11-10: was "review the gallery, **print** photos, and **decorate** the house with them".)*

**Nature & Collection Layer (Epic 6)**

FR22: **Spawnable nature subjects** (fish, trees, mushrooms, butterflies, pets) exist with per-type spawn probabilities and rarity tiers, filmable for the collection. *(2026-11-10: was "photographable".)*
FR23: **Nature-spectacle events** (lightning strike, solar eclipse, avalanche) run on the event lifecycle as rare catchable moments.

**Missions & Mystery (Epic 7 — optional layer)**

FR24: A **mission system** offers clip-bounty objectives ("record X doing Y"), mission-giver hubs, and a town-mystery thread advanced by recording specific subjects as footage evidence. *(2026-11-10: was photo-bounty / photo-evidence.)*
FR25: A mid/late-game **police-vs-criminal allegiance choice** branches available missions.

**Stealth Layer (Epic 8)**

FR26: A **stealth layer** adds proximity/eyeline detection on sensitive events; being **spotted** aborts the event (a soft consequence, never death), enabling sneak-to-shoot play.

**The Edit Suite (Epic 9)** *(added 2026-11-10)*

FR29: **Clip editing** — the player can choose clips from the memory card, trim each one, arrange them in order on a timeline, and preview the edit as one video.
FR30: **Saved edits** — finished edits are saved and can be re-watched and re-opened for editing.
FR31: **Export** — a finished edit can leave the game as a standard video file playable outside it. Online sharing remains deferred. *(Confirmed by the stakeholder 2026-11-10. This binds ADR-4: whatever format clips use inside the game, there must be a working path to encode a standard video file, at least at export time.)*

### NonFunctional Requirements

NFR1: **Performance** — 60 FPS at 1080p on `PC_RPAsset` on a mid-range Windows desktop with the active event running, **including while recording**. *(2026-11-10.)*
NFR2: **Input responsiveness** — no perceptible input lag; camera raise/lower transition < 0.3 s, **record-start-to-feedback (REC indicator + beep) < 0.2 s**. *(Revised 2026-11-10: was "capture-to-feedback < 0.2 s", meaning shutter + flash.)*
NFR3: **Memory stability** — the event-actor lifecycle leaks no objects over an extended session (object pooling required; no Instantiate/Destroy in gameplay loops). Recorded footage is bounded by the memory card's capacity — no unbounded buffer; deleting a clip frees its memory. *(2026-11-10.)*
NFR4: **Render pipeline** — URP only; the Standard shader is not supported.
NFR5: **Build hygiene** — zero compile errors; clean Unity console after every script change (verify via MCP `read_console`).
NFR6: **Simulation budget** — cap concurrent active event actors (1 for the slice); use LOD/culling across the open town.
NFR7: **Audio locatability** — the mix must make directional cues clearly locatable; audio is gameplay-critical, not just ambience.
NFR8: **No fail state / fail-soft** — there is no death, health, or game-over; errors must never crash or hard-pause gameplay and are never shown to the player.
NFR9: **Platform & input** — Windows 64-bit standalone; keyboard/mouse primary, gamepad supported (New Input System, "Player" map).
NFR10: **Grade legibility** — players can distinguish a high-grade from a low-grade clip and understand *why* (legible grade feedback). *(2026-11-10: was "shot".)*

### Additional Requirements

These are technical/setup requirements drawn from the Architecture document that shape stories (especially Epic 1's foundation):

- **AR1 (Brownfield input):** Extend the existing `PlayerInput` (Send-Messages mode); repurpose "Attack" → **Capture**, add **RaiseCamera** (hold) and **Zoom** actions; gate Capture/Zoom to Photo mode.
- **AR2 (Event engine):** Data-driven `EventActor` (lifecycle FSM) + per-event `EventDefinition` ScriptableObject + pooled `EventManager` — the ~80%-reusable engine.
- **AR3 (Grading impl):** Hybrid screen-space "frame read" (frustum + occlusion gate · screen-bounds composition · lifecycle timing); no GPU readback; thresholds in a `GradingConfig` ScriptableObject.
- **AR4 (Decoupling):** ScriptableObject event channels (`ShotCapturedChannel`, `EventPeakedChannel`, and from 2026-11-10 `ClipRecordedChannel`) connect record → memory card → HUD → (future) reputation.
- **AR5 (Code org):** `CameraGame.*` namespaces; feature folders under `Scripts/`; one `CameraGame.asmdef` (Editor/Tests separate).
- **AR6 (Memory card persistence):** An in-memory store of `CapturedClip` capped in seconds of footage (evolves `GalleryService`), designed with a disk-ready seam. Clips surviving between sessions lands no later than Epic 9's first story. *(Revised 2026-11-10: was "Gallery persistence: in-memory `List<CapturedShot>` via `GalleryService`, designed with a disk-ready seam for Epic 5".)*
- **AR7 (Camera fork — ADR-1):** Walk state is **first-person** (not third-person), reusing the existing FP controller/camera; "raise camera" = mode toggle + FOV zoom on one rig. (A small GDD note is advisable.)
- **AR8 (Import constraint):** The World FBX's embedded Blender camera must stay **disabled** on import (it overrides Main Camera via higher depth).
- **AR9 (Asset pipeline — Epic 2 blocker):** The town map currently exists only as a 214 MB `.blend`; it needs a **Blender → FBX export** before Unity import. *(2026-11-10, Epic 2 planning: measured as already met. `Assets/Models/game map .fbx` matches the shared folder's `game map .blend`: 16,738 meshes, 5,677,935 triangles, identical bounds, no camera. See `_bmad-output/verification/plan-epic-2/blend_vs_fbx.json`. Story 2.2 keeps a re-runnable check for the next time the `.blend` changes.)*
- **AR10 (Animator setup):** A Unity **Animator** must be built for the husband from the Drunk Pack (idle, idle variations, walk, turns, run) to drive the Town Drunk lifecycle.
- **AR11 (Project scaffold):** Create the folder/namespace structure (`Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}`, `Data/{Events, Grading, Camera, Channels}`) and the SO config assets before feature work.
- **AR12 (Clip recording approach):** Decided by measurement in Story 1.14 and recorded as **ADR-4** before any recording story starts. It must work in a Windows **build**: Unity Recorder (`com.unity.recorder`), which films the verification rigs, is Editor-only and cannot ship. *(Added 2026-11-10.)*

### UX Design Requirements

_No standalone UX Design Specification exists yet. UI work items (first-person viewfinder/lens overlay styled like a 2000s camcorder — from 2026-11-10 with a REC indicator, running timecode, card time remaining and CARD FULL — and the grade-feedback HUD showing subject %/composition/timing) are captured within the functional requirements above (FR1–FR8) and the Architecture's UI/`Scripts/PhotoMode` + `UI/` sections. If `gds-ux` is run later, dedicated UX-DRs can be folded in._

### FR Coverage Map

- FR1: **Epic 1** — Two camera states (walk ↔ viewfinder toggle)
- FR2: **Epic 1** — Viewfinder aim & zoom composition
- FR3: **Epic 1** — Record start/stop (gated to the viewfinder)
- FR4: **Epic 1** — Blended clip grade (per-frame grader + peak coverage)
- FR5: **Epic 1** — Subject-capture gate (per frame)
- FR6: **Epic 1** — Composition scoring (per frame)
- FR7: **Epic 1** — Peak coverage scoring
- FR8: **Epic 1** — Save clips to the memory card

_(FR1–FR8 labels revised 2026-11-10: they were walk ↔ photo toggle, photo-mode zoom, capture action, 3-dimension shot grade, peak-timing scoring, save shots to gallery.)_
- FR9: **Epic 1** — Event lifecycle FSM
- FR10: **Epic 1** — Diegetic cue self-telegraphing
- FR11: **Epic 1** — The Town Drunk proof-event
- FR12: **Epic 1** — Small playable town patch
- FR13: **Epic 1** — Free-roam traversal (existing controller)
- FR14: **Epic 2** — Vantage points
- FR15: **Epic 2** — Full town map, districts, colliders, NavMesh, ambient audio
- FR16: **Epic 3** — 3–5 additional events via the event-actor system
- FR17: **Epic 4** — Reputation earned from graded shots
- FR18: **Epic 4** — Reputation-tier unlocks
- FR19: **Epic 4** — Town-log collection tracking
- FR20: **Epic 4** — "New best" per-subject tracking
- FR21: **Epic 5** — Home-base hub: review, print, decorate
- FR22: **Epic 6** — Spawnable nature subjects with rarity
- FR23: **Epic 6** — Nature-spectacle events
- FR24: **Epic 7** — Mission system / clip-bounties / mystery
- FR25: **Epic 7** — Police-vs-criminal allegiance choice
- FR26: **Epic 8** — Stealth detection & "spotted" abort
- FR27: **Epic 1** — Memory card capacity *(added 2026-11-10)*
- FR28: **Epic 1** — Clip playback *(added 2026-11-10)*
- FR29: **Epic 9** — Clip editing (trim, timeline, preview) *(added 2026-11-10)*
- FR30: **Epic 9** — Saved edits *(added 2026-11-10)*
- FR31: **Epic 9** — Export as a video file *(added 2026-11-10; confirmed by the stakeholder the same day)*

_All 31 FRs are mapped. NFR1–NFR10 are cross-cutting and apply to every epic (enforced per-story in acceptance criteria and at code review). AR1–AR12 are realized primarily in Epic 1's foundation stories, with AR9 (map FBX export) gating Epic 2 and AR12 (the recording approach) gating Stories 1.15–1.18._

## Epic List

### Epic 1: First Playable Slice — The Catch Loop (MVP)
Prove the core loop is fun: a first-time player hears a cue, finds the Town Drunk, raises the camcorder, records him through his stagger, sees a graded clip land on their memory card, watches it back, and wants to wait for the next one. Delivers the entire Catch Loop end-to-end on a small town patch, building the reusable camera, recording, grading, and event-actor systems.
*Revised 2026-11-10 (`sprint-change-proposal-2026-11-10.md`): the core verb changed from still photography to video at the stakeholder's request. Stories 1.1–1.12 built the photo version; their systems are the foundation the camcorder stories 1.14–1.18 build on — none is rolled back.*
**FRs covered:** FR1, FR2, FR3, FR4, FR5, FR6, FR7, FR8, FR9, FR10, FR11, FR12, FR13, FR27, FR28
**Foundation (AR):** AR1–AR8, AR10, AR11, AR12
**Depends on:** — (greenfield slice on a brownfield base)

### Epic 9: The Edit Suite
This completes the "from capture to creation" arc the stakeholder asked for. The player picks clips from the memory card, trims them, arranges them on a timeline, and plays the result back as one short video they made. It starts as a menu screen; Epic 5 can give it a physical editing room in the home base later.
**FRs covered:** FR29, FR30, FR31
**Depends on:** Epic 1 (clips, and the ADR-4 recording format)
**Sequenced:** immediately after Epic 1, ahead of Epic 2. It is listed here, out of numeric order, to show that sequence; it is numbered 9 only so existing epic numbers stay stable. *(Added 2026-11-10 — promoted from the GDD's deferred "full video-editing minigame".)*

### Epic 2: The Living Town
Give the proven loop a real, navigable world: import the full town map, zone it into districts with colliders and NavMesh, place reachable vantage points, and lay down directional ambient audio so the world reads as alive and cues are locatable.
**FRs covered:** FR14, FR15
**Foundation (AR):** AR9 (map FBX export)
**Depends on:** Epic 1
**Stories:** 2.1–2.7, detailed 2026-11-10. Story 2.1 settles the player's scale against the town (ADR-5) before anything is placed in it.

### Epic 3: Event Content Pack #1
Add breadth of moments: 3–5 new event types built purely as `EventDefinition` data on the existing event-actor engine, with required animations sourced/authored (oil-rig smoke break, cheating husband, fisher, graffiti tagger).
**FRs covered:** FR16
**Depends on:** Epic 1, Epic 2

### Epic 4: Progression & Reputation
Give the player a reason to keep playing: reputation earned from graded clips, tier-based unlocks (events/districts/upgrades/home features), a town-log collection, and "new best" tracking that rewards re-capture.
**FRs covered:** FR17, FR18, FR19, FR20
**Depends on:** Epic 1, Epic 3

### Epic 5: Home Base
Close the session loop: a home-base interior hub where the player reviews their clips, prints stills pulled from clips, and decorates the house with them — and, once Epic 9 exists, a physical room for the Edit Suite — the payoff that sends the player back out. *(Revised 2026-11-10: was "reviews the gallery, prints photos".)*
**FRs covered:** FR21
**Depends on:** Epic 1, Epic 4 (and Epic 9 for the editing room)

### Epic 6: Nature & Collection Layer
Cozy completionism: spawnable nature subjects (fish/trees/mushrooms/butterflies/pets) with rarity, and rare nature-spectacle events (lightning, eclipse, avalanche) on the event lifecycle.
**FRs covered:** FR22, FR23
**Depends on:** Epic 2, Epic 4

### Epic 7: Missions & Mystery (optional layer)
A light narrative spine: mission-giver hubs, clip-bounty objectives, the town-mystery thread advanced by footage as evidence, and a mid/late-game police-vs-criminal allegiance choice.
**FRs covered:** FR24, FR25
**Depends on:** Epic 3, Epic 4

### Epic 8: Stealth Layer
Tension on sensitive events: proximity/eyeline detection, a "spotted" consequence that aborts the moment (never death), and sneak-to-shoot play.
**FRs covered:** FR26
**Depends on:** Epic 3, Epic 7

---

## Epic 1: First Playable Slice — The Catch Loop (MVP)

Prove the core loop is fun: a first-time player hears a cue, finds the Town Drunk, raises the camcorder, records him through his stagger, sees a graded clip land on their memory card, watches it back, and wants to wait for the next one. This epic builds the reusable camera, recording, grading, and event-actor systems end-to-end on a small playable town patch.

*Revised 2026-11-10 (`sprint-change-proposal-2026-11-10.md`): video replaces photo as the core verb. Stories 1.1–1.12 built and proved the photo version, and every system they built is reused. Stories 1.14–1.18 turn it into a camcorder: 1.14 decides how a built game records itself (and gates the rest), then record → grade → memory card → watch. Story 1.13 is superseded.*

**Slice success criterion (GDD):** an uninstructed first-time player completes the full Catch Loop and *voluntarily* seeks the next event.

**Story sequencing:** each story below is completable using only the stories before it — no forward dependencies. Cross-cutting NFRs (60 FPS @ 1080p, no perceptible input lag, zero compile errors / clean console, URP only, fail-soft) apply to every story and are restated in acceptance criteria where they bite.

### Story 1.1: Establish project scaffold and shared infrastructure

As a developer,
I want the project's folder/namespace structure and shared infrastructure in place,
So that every system I build afterward drops into a known home and compiles cleanly.

**Acceptance Criteria:**

**Given** the brownfield Unity project,
**When** I create the script structure,
**Then** `Assets/Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}` and `Assets/Data/{Events, Grading, Camera, Channels}` folders exist
**And** a single `CameraGame.asmdef` is present with `CameraGame.*` namespaces, and Editor/Tests remain (or are set up as) separate assemblies.

**Given** the `Core` folder,
**When** I add shared infrastructure,
**Then** `GameLog` (categorized Debug.Log helper), `GameConstants`/`AnimHashes` (no magic strings), `ObjectPool<T>`, and a ScriptableObject event-channel base type exist and compile.

**Given** any script change in this story,
**When** Unity finishes compiling,
**Then** the console shows **zero compile errors** (verified via MCP `read_console`) and no Standard-shader/URP warnings are introduced (NFR4, NFR5).

### Story 1.2: Walkable town patch with traversal and NavMesh

As a player,
I want a small patch of town I can walk and sprint around,
So that I have a place to explore and chase moments before any events exist.

**Acceptance Criteria:**

**Given** the existing first-person controller,
**When** I enter Play mode in the slice scene,
**Then** I can move (WASD/stick) and sprint (Shift/L3) across a blocked-out town patch with colliders, with no fall-through and stable framerate (NFR1).

**Given** the imported world geometry,
**When** the scene loads,
**Then** any embedded Blender camera is **disabled** so it never overrides the Main Camera (AR8).

**Given** the town patch,
**When** I bake navigation,
**Then** a NavMesh covers the walkable area so future event actors can path on it (enables Story 1.7).

### Story 1.3: Raise and lower the camera (Photo Mode toggle)

As a player,
I want to raise my camera by holding a button and lower it by releasing,
So that taking a photo is a deliberate, skillful act rather than a passive proximity check.

**Acceptance Criteria:**

**Given** I am in Walk state,
**When** I hold the RaiseCamera input (RMB / gamepad LT),
**Then** the view transitions into the first-person Photo state (viewfinder overlay visible) in **< 0.3 s** (FR1, NFR2)
**And** releasing the input returns me to Walk state within the same transition budget.

**Given** the New Input System "Player" map (Send-Messages mode),
**When** I add the RaiseCamera action,
**Then** it is wired without breaking existing Move/Look/Sprint/Jump actions (AR1)
**And** the walk camera is first-person per ADR-1 (AR7).

**Given** I am in Walk state,
**When** I am not holding RaiseCamera,
**Then** Photo-only actions are inactive (no accidental capture/zoom).

### Story 1.4: Zoom to compose in Photo Mode

As a player,
I want to zoom in and out while in Photo Mode,
So that I can compose the frame and chase a higher composition score.

**Acceptance Criteria:**

**Given** I am in Photo state,
**When** I scroll the wheel (or use the gamepad zoom),
**Then** the camera field-of-view lerps smoothly between ~60° (1×) and ~18° (4×) within the configured range (FR2)
**And** zoom does nothing while in Walk state.

**Given** zoom tuning values,
**When** they are defined,
**Then** they live in a `CameraConfig` ScriptableObject (Inspector-tunable, no recompile), not as code literals.

### Story 1.5: Capture a photo

As a player,
I want to click to capture a photo while in Photo Mode,
So that I can attempt to catch the moment.

**Acceptance Criteria:**

**Given** I am in Photo state,
**When** I press Capture (LMB / gamepad RT),
**Then** a shot is taken and capture-to-feedback (shutter SFX + visual flash) occurs in **< 0.2 s** (FR3, NFR2)
**And** capture raises a `ShotCapturedChannel` event (with a placeholder grade until grading lands in Stories 1.9–1.10).

**Given** the existing "Attack" action,
**When** I wire capture,
**Then** "Attack" is repurposed/renamed to **Capture** and is a **no-op in Walk state** (AR1, FR3).

### Story 1.6: Event-actor lifecycle engine

As a developer,
I want a generic, data-driven event-actor that runs a spawn→build→peak→wind-down→despawn lifecycle from a pool,
So that ~80% of all future events become data rather than new code.

**Acceptance Criteria:**

**Given** an `EventDefinition` ScriptableObject (per-phase duration, animation state, cue clip, NavMesh route, cue radius),
**When** an `EventActor` is spawned by the `EventManager`,
**Then** it advances through `Spawn → Build → Peak → WindDown → Despawn` on its own timers, exposing `IsAtPeak` and `TimeToPeak` via an `ISubject` interface (FR9, AR2).

**Given** the `EventManager`,
**When** events spawn and despawn,
**Then** actors are drawn from and returned to an object **pool** (never Instantiate/Destroy in the loop), concurrency is capped at **1** for the slice, and an extended session **leaks no objects** (NFR3, NFR6).

**Given** a missing/invalid `EventDefinition`,
**When** an actor initializes,
**Then** it logs a clear error and disables itself gracefully — never throwing into Update (NFR8 fail-soft).

### Story 1.7: The Town Drunk event

As a player,
I want a drunk NPC to stumble out of the pub, sway, nearly fall, and wander off,
So that I have a real, telegraphed moment to try to catch.

**Acceptance Criteria:**

**Given** the husband model and the Male Drunk Pack,
**When** I set up the Animator,
**Then** idle, idle-variation, walk, turn, and run states drive the drunk's lifecycle phases (AR10).

**Given** a `TownDrunk` EventDefinition on the event engine (Story 1.6) and a baked NavMesh (Story 1.2),
**When** the event runs,
**Then** the drunk spawns (e.g., leaving the pub), stumble-walks a route, hits a **peak stagger/near-fall (~1.5 s money-shot window)**, recovers, and despawns into an alley over a **~20–30 s** lifecycle (FR11).

**Given** the peak phase,
**When** it begins,
**Then** an `EventPeakedChannel` event fires (so grading and future audio can hook the peak).

### Story 1.8: Diegetic cue telegraphing

As a player,
I want to hear and see the drunk before I can see him clearly,
So that I discover events by reading the world, not by map markers.

**Acceptance Criteria:**

**Given** the active drunk event,
**When** I am within ~25 m,
**Then** a **directional 3D audio cue** (drunken mumbling/hiccup) is clearly locatable and falls off with distance (FR10, NFR7)
**And** the cue begins at spawn and persists through the lifecycle.

**Given** the audio mix,
**When** multiple sounds play,
**Then** the cue remains distinct enough to navigate toward (gameplay-critical audio, NFR7)
**And** no map marker or UI waypoint is used (diegetic discovery, FR10).

### Story 1.9: Subject-capture gate grading

As a player,
I want a shot to only count if the subject is actually well within frame,
So that the grade reflects whether I truly caught the moment.

**Acceptance Criteria:**

**Given** a captured shot and an `ISubject` target,
**When** `ShotGrader` evaluates it,
**Then** the subject must be inside the camera frustum AND not occluded, else the shot scores near-zero / fails (FR5, AR3).

**Given** the subject is in frame,
**When** coverage is computed from screen-space bounds,
**Then** a shot below the subject-height gate fails (shipped at **0.20** of frame height; originally specified as ~8% frame *coverage* — see FR5’s 2026-07-28 revision note), using a screen-space "frame read" with **no GPU readback** (AR3).

**Given** grading thresholds,
**When** they are defined,
**Then** they live in a `GradingConfig` ScriptableObject (Inspector-tunable), not code literals.

### Story 1.10: Composition and timing scoring

As a player,
I want my shot scored on how well I framed it and how close to the peak I clicked,
So that skill in framing and timing is rewarded with a higher grade.

**Acceptance Criteria:**

**Given** a shot that passed the subject gate,
**When** composition is scored,
**Then** the score peaks when the subject is prominent in the frame (measured as his height fraction) and sits near the **centre**, and falls off when too small, cut off, or pushed toward a corner (FR6, revised 2026-07-27 — see the note on FR6 above).

**Given** the event lifecycle's peak time,
**When** timing is scored,
**Then** it is full within **±0.5 s** of the peak and decays to zero by **±2 s** (FR7).

**Given** subject-gate, composition, and timing sub-scores,
**When** they are blended,
**Then** a final `ShotGrade` (0–100% → 1–5 stars, with a per-axis breakdown) is produced and attached to the captured shot, replacing the Story 1.5 placeholder (FR4).

### Story 1.11: Minimal gallery

As a player,
I want my captured shots saved with their grades,
So that each catch is permanent proof I can look back on.

**Acceptance Criteria:**

**Given** a `GalleryService` subscribed to `ShotCapturedChannel`,
**When** a graded shot is captured,
**Then** a `CapturedShot` (image, grade, subject id, time) is stored in an in-memory list (FR8, AR4, AR6).

**Given** captured shots exist,
**When** I open the gallery view,
**Then** I can see my shots with their star grades.

**Given** the in-memory model,
**When** it is designed,
**Then** it leaves a **disk-ready seam** (PNG + JSON metadata) for Epic 5 without reshaping the data model (AR6).

### Story 1.12: Grade-feedback HUD

As a player,
I want to see why my shot scored the way it did right after I take it,
So that I can tell a great shot from a weak one and want to do better.

**Acceptance Criteria:**

**Given** a shot is captured and graded,
**When** the result appears,
**Then** an on-capture HUD shows the star rating plus the subject-%, composition, and timing breakdown, readable at a glance (FR4, NFR10).

**Given** the slice success criterion,
**When** a first-time player captures a high vs. low grade,
**Then** the feedback is legible enough that they understand *why* and are motivated to try again (NFR10, GDD gameplay metric).

**Given** the viewfinder/HUD styling,
**When** rendered,
**Then** it reads like a 2000s camcorder/digicam to match the GDD art direction (informational; polish-acceptable).

---

### Story 1.13: Soften the timing multiplier — ⛔ SUPERSEDED 2026-11-10

> **Not to be implemented as written.** It changes the photo grade formula, which players no longer see once
> video replaces photo (`sprint-change-proposal-2026-11-10.md`). Its intent — a well-framed but mistimed
> attempt is worth something, and timing stays the dominant axis — is carried into **Story 1.16**'s acceptance
> criteria, with Alexv's original quote. The text below is kept as the record of that feedback.

As a player,
I want a well-composed photograph taken slightly too late to still be worth something,
So that good framing is never annihilated by one mistimed press.

*Added 2026-09-12 from Alexv's AC3 perceptual check of Story 1.12: "it should not be a big deal the timing
for me. I think it's too extreme to give 0 to the c_counted_but_zero.png picture."*

**Acceptance Criteria:**

**Given** a shot that passed every gate,
**When** its timing score is zero,
**Then** it still scores above zero if its composition did — via a designer-set floor on the timing factor
(`percent = composition × (floor + (1 − floor) × timing)`), not a change to the pillars.

**Given** the same change,
**When** two shots of equal composition are compared,
**Then** the well-timed one still wins by a wide margin — timing remains the dominant axis.

**Given** the floor,
**When** grading runs,
**Then** the "counted but 0 %" state becomes unreachable in normal play, and every rig caption, test and
record that asserted it is updated rather than left claiming a state the game can no longer produce.

---

### Story 1.14: Decide how the game records video

*Technical spike — **gates Stories 1.15–1.18**. Added 2026-11-10 (`sprint-change-proposal-2026-11-10.md`).*

As a developer,
I want to know, by measuring rather than guessing, how a **built** copy of the game can record its own gameplay,
So that the camcorder stories build on an approach that actually works outside the Editor.

**Acceptance Criteria:**

**Given** Unity Recorder is Editor-only and cannot ship in a build,
**When** recording approaches are compared — at minimum **(A)** encoding a video file while playing, **(B)** replay
recording (saving what happened and re-rendering it), and **(C)** keeping raw frames in memory —
**Then** each is prototyped against the Town Drunk in the real slice scene and measured for: frame rate at 1080p
while recording (NFR1), memory and disk per second of footage, whether the game's sound is captured, playback
quality, and licence/cost.

**Given** the measurements,
**When** an approach is chosen,
**Then** it is recorded as **ADR-4** in the architecture with the evidence behind it, including how the Edit Suite
(Epic 9) would trim and join clips on top of it and how a finished edit is exported as a standard video file
(FR31 — confirmed by the stakeholder 2026-11-10, so the chosen approach must come with a working export path)
**And** Alexv approves the choice before Story 1.15 starts.

**Given** the chosen approach,
**When** it is proven,
**Then** a ~10-second clip of the drunk is recorded **and** played back **in a Windows build**, not only in the
Editor, where Unity Recorder would mask the problem
**And** the throwaway prototype code is removed or clearly fenced off from game code.

### Story 1.15: Record a clip

*Added 2026-11-10. Depends on Story 1.14 (ADR-4). Record-as-a-toggle and clips-with-game-sound were confirmed by the stakeholder on 2026-11-10.*

As a player,
I want to press a button to start and stop recording while my camera is raised,
So that I can film the moment as it happens instead of freezing one instant.

**Acceptance Criteria:**

**Given** I am in Viewfinder state,
**When** I press Record (LMB / gamepad RT),
**Then** recording starts within **< 0.2 s**, with a REC indicator, a running timecode and a start beep in a
2000s-camcorder style (FR3, NFR2)
**And** pressing Record again stops it, with a stop beep.

**Given** I am recording,
**When** I lower the camera (release RaiseCamera),
**Then** recording stops and the clip is kept. Lowering never throws footage away.

**Given** I am recording with the drunk event active,
**When** I record for the length of a full event lifecycle (~30 s),
**Then** the game holds **60 FPS at 1080p** (NFR1), and aim and zoom keep working while recording (FR2).

**Given** the existing Capture action (Story 1.5),
**When** recording is wired,
**Then** it reuses that action (LMB / RT), which is a no-op in Walk state
**And** each finished clip is announced on a `ClipRecordedChannel`, so the memory card and HUD can listen without the
recorder knowing about them (AR4).

### Story 1.16: Grade a clip

*Added 2026-11-10. Carries the intent of superseded Story 1.13.*

As a player,
I want each clip scored on how well I kept the subject framed and whether I caught the peak,
So that filming the moment is still a skill, not just pressing record and walking away.

**Acceptance Criteria:**

**Given** a clip is being recorded,
**When** frames are rendered,
**Then** the existing per-frame grader (`ShotGrader`, Stories 1.9–1.10) is sampled throughout the recording, with its
subject gate and composition unchanged and not re-implemented (FR4–FR6).

**Given** a finished clip,
**When** it is graded,
**Then** its grade rewards **peak coverage** (how much of the event's peak window the clip contains with the subject
framed) and how well the subject was framed while recorded, blended into a 0–100% → 1–5★ `ClipGrade` with a
per-axis breakdown (FR4, FR7)
**And** the blend, every threshold, and whether footage without the subject lowers the grade live in
`GradingConfig` and are decided by measuring recorded clips, as Story 1.10 decided prominence.

**Given** a well-framed clip that missed the peak,
**When** it is graded,
**Then** it still scores above zero. This is Alexv's Story 1.12 feedback, carried over from superseded Story 1.13:
*"it should not be a big deal the timing for me. I think it's too extreme to give 0"*
**And** a clip that caught the peak with equal framing still wins by a wide margin.

**Given** a clip in which the subject never passes the gate,
**When** it is graded,
**Then** it is a miss with a reason (no subject / too small / occluded)
**And** the grade HUD (Story 1.12) shows the clip's stars and breakdown when recording stops (NFR10).

### Story 1.17: The memory card

*Added 2026-11-10.*

As a player,
I want my clips saved on a memory card that can fill up,
So that every recording is a decision and the footage I keep is footage I chose.

**Acceptance Criteria:**

**Given** a clip finishes recording,
**When** the memory card receives it via `ClipRecordedChannel`,
**Then** a `CapturedClip` (footage, grade, subject id, time, duration, thumbnail) is stored, evolving the photo
gallery's storage from Story 1.11 (FR8, AR6).

**Given** the card's capacity, measured in **seconds of footage** in a designer-tunable config (starting value
~2 minutes, confirmed by the stakeholder 2026-11-10),
**When** I am in Viewfinder state,
**Then** the viewfinder shows the time remaining
**And** when the card fills mid-recording, recording stops, the clip so far is kept, and the viewfinder shows
**CARD FULL** (FR27).

**Given** the memory-card view (reworked from Story 1.11's gallery view),
**When** I open it,
**Then** I see each clip's thumbnail, stars and duration, and I can delete one: its seconds are freed immediately and
its memory is released, with no leak over an extended session (NFR3).

**Given** the in-memory model,
**When** it is designed,
**Then** it keeps a disk-ready seam in the format ADR-4 chose, so clips can survive between sessions in Epic 9 (AR6).

### Story 1.18: Watch your clips

*Added 2026-11-10.*

As a player,
I want to play back any clip on my memory card,
So that I can relive the catch and judge which footage is worth keeping.

**Acceptance Criteria:**

**Given** clips on the memory card,
**When** I select one in the memory-card view,
**Then** it plays back in-game exactly as I recorded it (same framing, zoom and timing), with the game sound that was
playing (FR28).

**Given** playback is running,
**When** I watch,
**Then** I can pause, skip and close it; the camera and player input are suppressed while I watch (the existing
suppression seam); and closing returns me exactly where I was
**And** watching never alters the live game: no event is skipped and no actor is teleported.

**Given** a recorded clip of the drunk's stagger,
**When** Alexv watches it back,
**Then** he confirms it reads as "I caught it". This is a **human** check and stays open until he has done it.
(The uninstructed first-time-player playtest of the whole slice remains Epic 1's exit check.)

---

## Epic 2: The Living Town

Give the proven loop a real, navigable world. The whole town map becomes walkable end to end, is zoned into districts that each sound like their own place, gains vantage points you can climb to, and carries directional ambience that never buries an event's cue. The world should read as alive, and cues should stay locatable.

*Planned 2026-11-10 by the unattended loop (`plan-epic-2`), which may plan later epics before Epic 1's playtest (decision log, 2026-11-10). Three facts measured while planning shaped these stories. The evidence is in `_bmad-output/verification/plan-epic-2/`, made by headless Blender scripts that only read the map:*

- *The FBX export that AR9 asked for already exists. `Assets/Models/game map .fbx` matches the newest `game map .blend` in the shared folder exactly: 16,738 meshes, 5,677,935 triangles, the same 3,144 × 3,732 m bounds, and no camera (`blend_vs_fbx.json`). `SampleScene` already holds the whole map, and Epic 1 only ever used its centre. So no story exports the map. Story 2.2 instead keeps a check that can be re-run whenever the `.blend` changes.*
- *The map is a central plain of about 810 × 570 m inside a ring of mountain tiles, each about 1 km across. On the plain are a small street block, a row of houses, an industrial yard (a factory, containers, trucks, oil barrels) and a lake shore. A structure stands out on the water about 250 m from the shore, probably the oil rig (`map_topdown.png`, `zoom_plain_900m.jpg`, `zoom_water_500m.jpg`). All five GDD districts can be zoned on this, though downtown and residential are thin. Nothing on the map is a dock or a pier.*
- *The characters are about 5× the town's scale. The player is 8.86 m tall, beside cars about 2.6 m long, containers about 5.8 m long and trees 4.9–7.6 m tall. This is logged in `deferred-work.md`, and Alexv noted "humanoid too tall" on 2026-06-05. A giant sees over every roof, which makes FR14's rooftop vantage points pointless. And if the scale changed later, every distance placed in this epic would have to be placed again. So Story 2.1 settles the scale first.*

**Story sequencing:** Story 2.1 settles the scale that every later story places things in. Each story after it builds only on the stories before it. The cross-cutting NFRs apply as they do in Epic 1, and NFR1, NFR3, NFR7 and NFR8 are restated where they bite.

### Story 2.1: Settle the player's scale in the town

*Added 2026-11-10. Decided by measuring, as Story 1.14 was, and recorded as **ADR-5**. It comes first because every later story in this epic places things in the world at the scale this one settles. It takes up two `deferred-work.md` entries: "Character (humanoid) is too tall relative to the world" and "NavMesh bake dimensions are metric while world/agent are ~4×".*

As a player,
I want to be the right size for the town, with doors, cars and trees at a person's scale around me,
So that the town reads as a real place, and climbing onto a roof or a hill shows me something I could not see from the street.

**Acceptance Criteria:**

**Given** the player, the Town Drunk and the town as they are now (characters about 8.86 m tall, beside cars about 2.6 m long, containers about 5.8 m long and trees 4.9–7.6 m tall),
**When** the options are compared, at minimum **(A)** scale the characters to the town, **(B)** scale the town to the characters and **(C)** keep both as they are,
**Then** each option is photographed from the player's eye at the same spots (beside a car, at a house door, under a tree, in the street block)
**And** each is measured for: eye height against those objects; how long it takes to cross the plain and to reach the drunk's route, walking and sprinting; and every tuned number it would move. Those numbers include movement, jump, gravity, camera eye height, the NavMesh agent, the drunk's speed and cue radius, distances in the grading and camera configs, and the rigs' camera heights
**And** the choice is recorded as **ADR-5** in the architecture, with that evidence and the cost of reversing it.

**Given** the chosen option,
**When** it is applied,
**Then** every value it moves lives in config, not in code literals
**And** the NavMesh is re-baked with an agent the size of the characters
**And** the drunk still takes about 20–30 s over his route, with his ~1.5 s peak (FR11).

**Given** Epic 1's loop after the change,
**When** it is run through the real code paths,
**Then** the drunk completes several pooled lifecycles on the NavMesh
**And** his cue is audible out to the design's ~25 m at the new scale, and silent beyond it (FR10)
**And** a well-framed capture of his peak still grades high and a miss still misses: the Photo Shoot rig, moved to the new eye height, shows no regression
**And** raising the camera still takes under 0.3 s (NFR2), and walk, sprint and jump still work (FR13).

**Given** before-and-after pictures from the player's eye at the same spots,
**When** Alexv looks at them,
**Then** he confirms the town now reads as a place where a person stands. This is a **human** check, and it stays open until he has done it.

### Story 2.2: Walk the whole town

*Added 2026-11-10. Planning found that the map in Unity already is the newest `.blend` (AR9), so this story checks that instead of exporting it. It also takes up the `deferred-work.md` entry "See-through walls".*

As a player,
I want to walk out of the slice patch to anywhere in the town (the street block, the houses, the industrial yard, the shore and up into the hills) without falling through, getting stuck or walking off the world,
So that the whole town is mine to explore and to chase moments in.

**Acceptance Criteria:**

**Given** the newest town-map `.blend` in the shared folder,
**When** a re-runnable editor check compares it with `Assets/Models/game map .fbx`,
**Then** it reports whether the two match on mesh count, triangle count and bounds, and names anything that differs
**And** if the map has changed, it is re-exported over `Assets/Models/game map .fbx` in place, keeping the file's GUID so the scene's references survive
**And** camera import is switched off, so no Blender camera can ever override Main Camera (AR8, AR9).

**Given** the player,
**When** the real controller walks and sprints a route through every part of the town, driven by the same input methods the player's keys call and drawn as a trace on a top-down map,
**Then** they never fall through the ground and never stick on open ground
**And** nothing too small to be an obstacle (grass, flowers, pebbles) stops them like an invisible wall (FR13, FR15).

**Given** the edges of the town (the mountain ring and the water),
**When** the player walks into them,
**Then** the terrain or the shoreline stops them: they never walk on the water's surface or off the world
**And** if they ever end up below the world, they are put back on the last safe ground within ~2 s, and no error is shown (NFR8)
**And** the oil rig out on the water is either reachable on foot or deliberately out of bounds. This story decides which and records it, because Epic 3's oil-rig event needs to know.

**Given** the player pressed against any wall in the town,
**When** they look at it,
**Then** the view never shows what is behind the wall.

**Given** the NavMesh, baked over the whole town at the agent size ADR-5 set,
**When** an agent paths from the pub to a point in every part of the town,
**Then** every path completes (drawn on the top-down map), so Epic 3's events can be staged anywhere (FR15)
**And** the console stays clean, and the frame rate is no lower than at the start of the story (NFR1, NFR5).

### Story 2.3: The whole town holds its frame rate

*Added 2026-11-10. The slice already loads the whole map: 16,738 renderers, each with a MeshCollider, and 5.7 M triangles. 15,080 of those renderers are ground scatter from Blender particle systems. Once the whole town is open to the player, they can look across all of it.*

As a player,
I want the game to stay smooth wherever I go and whatever I look at, including the view across the whole plain from the hills,
So that chasing a moment never stutters.

**Acceptance Criteria:**

**Given** a fixed route through every part of the town, including the widest views from the hills, with the drunk event active,
**When** it is measured in a Windows release build at 1080p on `PC_RPAsset`,
**Then** it holds 60 FPS (NFR1)
**And** because this laptop is not NFR1's mid-range desktop, the result is measured as a change against a baseline run interleaved with it, and the absolute verdict is queued for Alexv.

**Given** culling chosen by measuring (for example occlusion culling, cull distances for small ground scatter, LODs or batching),
**When** it is applied,
**Then** a run before it and a run after it, over the same route, show the gain on each stretch of the route
**And** a contact sheet from that route shows nothing near the player popping in or out (NFR6).

**Given** an event actor the player can see,
**When** culling runs,
**Then** the actor is never culled, and the grader's verdict on him is unchanged: the Photo Shoot rig shows no regression.

**Given** a long session (about 30 minutes of walking the route on a loop),
**When** the game's memory is sampled from outside the process,
**Then** it stays flat (NFR3).

### Story 2.4: Districts you can hear

*Added 2026-11-10. Districts are zoned on the map as it is now. Planning found thin versions of downtown and residential, so building out district content is left to Alexv's work on the map, and this story adds none.*

As a player,
I want each part of town to sound like its own place, and to hear the change as I walk from one part to the next,
So that I always know where I am without a map, and the town feels alive.

**Acceptance Criteria:**

**Given** the town map,
**When** it is zoned,
**Then** it holds the GDD's districts as far as the map supports them: downtown (the street block), residential (the houses), industrial (with the oil rig), the waterfront, and the mountains
**And** each district is a data asset holding its name, its bounds and its GDD event category (crime, drama or nature), so that Epic 3 can seed events into districts and Epic 4 can unlock them (FR15)
**And** a top-down capture shows every district's bounds, plus a walk through all of them coloured by the district the game resolved at each point.

**Given** the player walks from one district into another,
**When** they cross the boundary,
**Then** the new district's ambient bed fades in and the old one fades out over a few seconds, set in config
**And** there is never a hard cut, never silence and never two beds at full volume, measured along the walk from the game's own audio output.

**Given** a spot that belongs to no district,
**When** the player stands there,
**Then** a default bed plays, and nothing errors (NFR8).

**Given** the ambient beds,
**When** they are made,
**Then** placeholders follow the project's synthesised-placeholder convention (Story 1.8), and no asset is used whose licence would have to be accepted
**And** districts are heard, not labelled: no on-screen name or marker is added, per the GDD's rule against map markers (FR10).

**Given** a walk through every district,
**When** Alexv listens,
**Then** he confirms that each one sounds like its own place. This is a **human** check.

### Story 2.5: Climb to a better angle

*Added 2026-11-10. This story delivers FR14.*

As a player,
I want rooftops, hills and a spot on the waterfront that I can get up onto,
So that when something happens below, I can frame it from above, clear of the cars, trees and walls that block the view at street level.

**Acceptance Criteria:**

**Given** the town,
**When** vantage points are placed,
**Then** every district has at least one, and among them are a rooftop, a hill and a spot on the waterfront (FR14)
**And** each is reached on foot with the existing controls (walk, sprint, jump), with no new climbing mechanic
**And** any access the map lacks (stairs, a ramp, a jetty) is added under its own scene root, never by editing the map FBX. A re-import of the map then cannot remove it, and Alexv can fold it into the `.blend` later.

**Given** each vantage point,
**When** a scripted walk drives the real controller from the street up to it,
**Then** the player arrives without getting stuck or falling through, shown as a trace on the top-down map.

**Given** the drunk's peak spot, and a stand-in subject in each district's open spaces (where Epic 3 will stage events),
**When** the real grader frames each subject from the nearest vantage point and from street level on the same bearing,
**Then** the vantage point sees the subject unoccluded and, with zoom, above the size gate, more often than the street view does
**And** each of those frames is photographed with the grader's box drawn on it.

**Given** the town at eye level,
**When** Alexv looks around and climbs up,
**Then** he confirms that each vantage point reads as somewhere you can climb, and that the view from up there is a better shot. These are **human** checks.

### Story 2.6: Sounds to steer by

*Added 2026-11-10. This story delivers FR15's "reactive directional ambient audio" and puts NFR7 to the test across the whole town.*

As a player,
I want the town's landmarks to sound from where they are (surf along the shore, machinery at the yard and the rig, traffic on the main road, wind in the hills), and an event's cue to cut through all of them,
So that I can find my way by ear, and the moment I am hunting is never buried.

**Acceptance Criteria:**

**Given** each district,
**When** its landmark sounds play,
**Then** they are 3D and directional: as the player turns, the stereo balance of the game's output follows each source's bearing
**And** their level falls with distance, measured from the game's own audio output, the way Story 1.8 measured the drunk's cue.

**Given** the drunk's cue, within its radius, among the ambience,
**When** both are playing,
**Then** the ambience ducks under the cue by an amount set in config, so the cue stays the most prominent sound the player can locate at every point of a walk toward him (NFR7)
**And** the ducking releases smoothly after he despawns.

**Given** every district's sounds playing at once,
**When** voice count and audio CPU are measured,
**Then** they stay within budget, and the frame rate does not regress (NFR1).

**Given** a walk through the town with the drunk active,
**When** Alexv listens,
**Then** he confirms that he can tell where landmarks are by ear, and that the drunk's cue still cuts through. This is a **human** check (NFR7).

### Story 2.7: The town swells at the peak

*Added 2026-11-10. The GDD's Art and Audio direction asks for this, and the architecture's validation flagged it "for Epic 1 polish or Epic 2". No epic had taken it, so it sits here.*

As a player,
I want a light, lo-fi music bed under the town that swells when an event I can hear reaches its peak,
So that the money-shot moment feels like one, and I know to keep rolling.

**Acceptance Criteria:**

**Given** an event peaks within its cue radius of the player,
**When** `EventPeakedChannel` fires,
**Then** the music bed swells over about 1 s, holds through the peak window and settles back after it, with the ambience dipping under it
**And** the curve and levels live in config (GDD Art & Audio)
**And** the swell is driven by the channel, not by polling the event.

**Given** an event peaks beyond its cue radius,
**When** the player cannot hear it,
**Then** nothing swells. The music must never give away an event the player could not have heard (FR10).

**Given** several pooled lifecycles in a row,
**When** each one peaks,
**Then** each swell plays once and settles fully, never stacking and never sticking.

**Given** a recording of the game's audio output across a lifecycle,
**When** Alexv listens,
**Then** he confirms that the swell feels like a payoff, not an intrusion. This is a **human** check.

---

## Epics 3–9: Detailed Just-In-Time

Per the GDD's scope discipline — *"Do not start a later epic until Epic 1 has proven the loop is fun"* — the epics below are intentionally **not** broken into detailed stories yet. Their goals, FR coverage, and dependencies are fixed in the **Epic List** above. Re-run `gds-create-story` / this workflow to detail each one just-in-time, once Epic 1 validates how the core systems actually feel. **Epic 9 is sequenced first among them**, immediately after Epic 1 (2026-11-10). *(Epic 2 was detailed above on 2026-11-10 by the unattended loop, which Alexv allowed to plan later epics ahead of Epic 1's playtest. This heading read "Epics 2–9" until then.)*

**Known seeds for when those epics are detailed:**

- **Epic 9** *(added 2026-11-10)* — the first story makes clips survive between sessions (cashing in the memory card's disk seam, AR6). After that: trim a clip → arrange clips on a timeline → play back the edit → save and re-open an edit (FR29–FR30). Then export to a standard video file (FR31 — confirmed by the stakeholder 2026-11-10, so it is committed, not a candidate). Later candidates: a music bed, title cards, transitions. ADR-4 (Story 1.14) decides how trimming and joining work (re-render versus re-encode).
- **Epic 2** — the town map FBX export (AR9) is the first story and a hard blocker; districts, vantage points (FR14), colliders/NavMesh, and per-district ambient audio (FR15) follow. *(Detailed 2026-11-10 as Stories 2.1–2.7 above. Planning measured that the export already exists, so the scale question took the first slot and Story 2.2 checks the map rather than exporting it.)*
- **Epic 3** — each new event (oil-rig smoke break, cheating husband, fisher, graffiti) is one story = one `EventDefinition` + its animations; the engine from Story 1.6 is reused unchanged (FR16). Assets on hand: bandit, CEO, oil-rig worker FBX + a "bandit fight mode" walk.
- **Epic 4** — `ReputationSystem` subscribes to `ClipRecordedChannel` (FR17–FR20); no rework of recording/grading needed. *(2026-11-10: was `ShotCapturedChannel`.)*
- **Epic 5** — the memory card's disk seam (AR6) is cashed in by Epic 9; Epic 5 reuses it to print stills and decorate (FR21). *(2026-11-10: was "`GalleryService`'s disk seam is cashed in here".)*
- **Epic 6** — nature subjects (FR22) and spectacle events (FR23) reuse the event lifecycle.
- **Epic 7** — `MissionService` listens to clip events; footage-as-evidence + allegiance branch (FR24–FR25). *(2026-11-10: was capture events / photo-as-evidence.)*
- **Epic 8** — stealth detection + "spotted" abort hooks the `EventActor` phase events (FR26).
