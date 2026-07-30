---
stepsCompleted: [1, 2, 3, 4]
workflowStatus: complete
inputDocuments:
  - _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md
  - _bmad-output/planning-artifacts/game-architecture.md
  - "CAMERA GAME SHARED FOLDER/Camera game.docx (concept context)"
  - "CAMERA GAME SHARED FOLDER/ (asset inventory context)"
jiraSync:
  site: alessandrovanreusel.atlassian.net
  project: KAN
  syncedOn: 2026-07-31
  epics:
    - { name: "Epic 1: First Playable Slice — The Catch Loop (MVP)", key: KAN-4 }
    - { name: "Epic 2: The Living Town", key: KAN-5 }
    - { name: "Epic 3: Event Content Pack #1", key: KAN-6 }
    - { name: "Epic 4: Progression & Reputation", key: KAN-7 }
    - { name: "Epic 5: Home Base", key: KAN-8 }
    - { name: "Epic 6: Nature & Collection Layer", key: KAN-9 }
    - { name: "Epic 7: Missions & Mystery", key: KAN-10 }
    - { name: "Epic 8: Stealth Layer", key: KAN-11 }
  stories:  # all parented to KAN-4 (Epic 1)
    - { name: "Story 1.1", key: KAN-12 }
    - { name: "Story 1.2", key: KAN-13 }
    - { name: "Story 1.3", key: KAN-14 }
    - { name: "Story 1.4", key: KAN-15 }
    - { name: "Story 1.5", key: KAN-16 }
    - { name: "Story 1.6", key: KAN-17 }
    - { name: "Story 1.7", key: KAN-18 }
    - { name: "Story 1.8", key: KAN-19 }
    - { name: "Story 1.9", key: KAN-20 }
    - { name: "Story 1.10", key: KAN-21 }
    - { name: "Story 1.11", key: KAN-22 }
    - { name: "Story 1.12", key: KAN-23 }
  subtasks:  # story Tasks/Subtasks mirrored as Jira Subtasks (parent = the story)
    # Story 1.1 (parent KAN-12) — created 2026-06-04
    - { name: "1.1 Task 1 — Create folder structure", key: KAN-24, parent: KAN-12 }
    - { name: "1.1 Task 2 — CameraGame.asmdef", key: KAN-25, parent: KAN-12 }
    - { name: "1.1 Task 3 — CameraGame.Editor.asmdef", key: KAN-26, parent: KAN-12 }
    - { name: "1.1 Task 4 — Core shared infrastructure", key: KAN-27, parent: KAN-12 }
    - { name: "1.1 Task 5 — Verify clean compile (Unity MCP)", key: KAN-28, parent: KAN-12 }
    - { name: "1.1 Task 6 — Tests assembly scaffold (deferred)", key: KAN-29, parent: KAN-12 }
    # Story 1.2 (parent KAN-13) — created 2026-06-04
    - { name: "1.2 Task 1 — Confirm & standardize the slice scene", key: KAN-30, parent: KAN-13 }
    - { name: "1.2 Task 2 — Verify colliders & traversal", key: KAN-31, parent: KAN-13 }
    - { name: "1.2 Task 3 — Disable embedded Blender camera (AR8)", key: KAN-32, parent: KAN-13 }
    - { name: "1.2 Task 4 — Bake the NavMesh (scaled agent)", key: KAN-33, parent: KAN-13 }
    - { name: "1.2 Task 5 — Performance & clean-console check", key: KAN-34, parent: KAN-13 }
    - { name: "1.2 Task 6 — End-to-end AC verification", key: KAN-35, parent: KAN-13 }
    # Story 1.3 (parent KAN-14) — created 2026-06-05
    - { name: "1.3 Task 1 — Add the RaiseCamera input action (Value type)", key: KAN-36, parent: KAN-14 }
    - { name: "1.3 Task 2 — Create PhotoModeController (Walk/Photo enum)", key: KAN-37, parent: KAN-14 }
    - { name: "1.3 Task 3 — Minimal viewfinder overlay + transition", key: KAN-38, parent: KAN-14 }
    - { name: "1.3 Task 4 — Fail-soft wiring + regression guard", key: KAN-39, parent: KAN-14 }
    - { name: "1.3 Task 5 — Verify in Play mode + clean console", key: KAN-40, parent: KAN-14 }
    # Story 1.4 (parent KAN-15) — created 2026-06-16
    - { name: "1.4 Task 1 — Add the Zoom input action (Value/Vector2)", key: KAN-41, parent: KAN-15 }
    - { name: "1.4 Task 2 — Create CameraConfig ScriptableObject + asset", key: KAN-42, parent: KAN-15 }
    - { name: "1.4 Task 3 — Add zoom (FOV lerp) to PhotoModeController", key: KAN-43, parent: KAN-15 }
    - { name: "1.4 Task 4 — Wire CameraConfig + Camera in the scene", key: KAN-44, parent: KAN-15 }
    - { name: "1.4 Task 5 — Verify in Play mode + clean console", key: KAN-45, parent: KAN-15 }
    # Story 1.5 (parent KAN-16) — created 2026-06-17
    - { name: "1.5 Task 1 — Repurpose the Attack action to Capture (LMB + gamepad RT)", key: KAN-46, parent: KAN-16 }
    - { name: "1.5 Task 2 — Create the minimal ShotGrade result type", key: KAN-47, parent: KAN-16 }
    - { name: "1.5 Task 3 — Create the ShotCapturedChannel event channel + asset", key: KAN-48, parent: KAN-16 }
    - { name: "1.5 Task 4 — Create a CaptureConfig ScriptableObject for feedback tunables", key: KAN-49, parent: KAN-16 }
    - { name: "1.5 Task 5 — Add capture to PhotoModeController (handler + flash fade)", key: KAN-50, parent: KAN-16 }
    - { name: "1.5 Task 6 — Wire it in the scene (refs + AudioSource + flash UI)", key: KAN-51, parent: KAN-16 }
    - { name: "1.5 Task 7 — Verify in Play mode + clean console", key: KAN-52, parent: KAN-16 }
    # Story 1.6 (parent KAN-17) — created 2026-06-18
    - { name: "1.6 Task 1 — Harden ObjectPool<T> vs EventManager contract", key: KAN-53, parent: KAN-17 }
    - { name: "1.6 Task 2 — EventPhase enum + ISubject interface", key: KAN-54, parent: KAN-17 }
    - { name: "1.6 Task 3 — EventDefinition ScriptableObject", key: KAN-55, parent: KAN-17 }
    - { name: "1.6 Task 4 — EventPeakedChannel + asset", key: KAN-56, parent: KAN-17 }
    - { name: "1.6 Task 5 — EventActor MonoBehaviour (FSM + ISubject + fail-soft)", key: KAN-57, parent: KAN-17 }
    - { name: "1.6 Task 6 — EventManager (pooled, concurrency-capped)", key: KAN-58, parent: KAN-17 }
    - { name: "1.6 Task 7 — Temporary verification harness + scene wiring", key: KAN-59, parent: KAN-17 }
    - { name: "1.6 Task 8 — Verify in Play mode + clean console", key: KAN-60, parent: KAN-17 }
    # Story 1.7 (parent KAN-18) — created 2026-06-24
    - { name: "1.7 Task 1 — Import & verify husband + Drunk Pack (Generic-rig avatar copy)", key: KAN-61, parent: KAN-18 }
    - { name: "1.7 Task 2 — Build the drunk Animator Controller", key: KAN-62, parent: KAN-18 }
    - { name: "1.7 Task 3 — Add NavMesh route-following to EventActor (+ isOnNavMesh guard)", key: KAN-63, parent: KAN-18 }
    - { name: "1.7 Task 4 — Create the EventRoute scene component", key: KAN-64, parent: KAN-18 }
    - { name: "1.7 Task 5 — Create the TownDrunk EventDefinition asset", key: KAN-65, parent: KAN-18 }
    - { name: "1.7 Task 6 — Build the EventActor_Drunk husband prefab", key: KAN-66, parent: KAN-18 }
    - { name: "1.7 Task 7 — Wire the scene: route, repoint manager, place on NavMesh", key: KAN-67, parent: KAN-18 }
    - { name: "1.7 Task 8 — Verify in Play mode + clean console", key: KAN-68, parent: KAN-18 }
    # Story 1.8 (parent KAN-19) — created 2026-07-25
    - { name: "1.8 Task 1 — Resolve the audio asset dependency (GATES THE STORY)", key: KAN-69, parent: KAN-19 }
    - { name: "1.8 Task 2 — Add a persistent event-level loopCue to EventDefinition", key: KAN-70, parent: KAN-19 }
    - { name: "1.8 Task 3 — Drive the cue from EventActor (start/stop/reset across pooling)", key: KAN-71, parent: KAN-19 }
    - { name: "1.8 Task 4 — Configure the AudioSource on EventActor_Drunk.prefab", key: KAN-72, parent: KAN-19 }
    - { name: "1.8 Task 5 — Minimal audio mixer routing (GameAudio.mixer)", key: KAN-73, parent: KAN-19 }
    - { name: "1.8 Task 6 — Confirm nothing diegetic-breaking was added", key: KAN-74, parent: KAN-19 }
    - { name: "1.8 Task 7 — Verify (headless polling + focused Play session)", key: KAN-75, parent: KAN-19 }
    # Story 1.9 (parent KAN-20) — created 2026-07-25
    - { name: "1.9 Task 1 — Solve subject discovery (GATES THE STORY)", key: KAN-76, parent: KAN-20 }
    - { name: "1.9 Task 2 — Create GradingConfig ScriptableObject + asset", key: KAN-77, parent: KAN-20 }
    - { name: "1.9 Task 3 — Write ShotGrader (frustum + coverage)", key: KAN-78, parent: KAN-20 }
    - { name: "1.9 Task 4 — Occlusion test without a Transform on ISubject", key: KAN-79, parent: KAN-20 }
    - { name: "1.9 Task 5 — Wire real grading into the capture path", key: KAN-80, parent: KAN-20 }
    - { name: "1.9 Task 6 — Verify (gizmo overlay + Play session + clean console)", key: KAN-81, parent: KAN-20 }
    # Story 1.10 (parent KAN-21) — created 2026-07-26
    - { name: "1.10 Task 1 — Decide what \"distance from the peak\" means (GATES THE STORY)", key: KAN-82, parent: KAN-21 }
    - { name: "1.10 Task 2 — Decide the prominence measure by MEASURING", key: KAN-83, parent: KAN-21 }
    - { name: "1.10 Task 3 — Extend GradingConfig (composition + timing tunables)", key: KAN-84, parent: KAN-21 }
    - { name: "1.10 Task 4 — Composition scoring (prominence, centred placement, cut-off)", key: KAN-85, parent: KAN-21 }
    - { name: "1.10 Task 5 — Timing scoring", key: KAN-86, parent: KAN-21 }
    - { name: "1.10 Task 6 — Blend, widen ShotGrade, keep the miss path honest", key: KAN-87, parent: KAN-21 }
    - { name: "1.10 Task 7 — Verify by photographing it (timing poses + human review)", key: KAN-88, parent: KAN-21 }
    # Story 1.11 (parent KAN-22) — created 2026-07-30
    - { name: "1.11 Task 1 — Decide how the image, subject and time reach the gallery (GATES THE STORY)", key: KAN-89, parent: KAN-22 }
    - { name: "1.11 Task 2 — CapturedShot and the disk-ready seam", key: KAN-90, parent: KAN-22 }
    - { name: "1.11 Task 3 — GalleryConfig ScriptableObject + asset", key: KAN-91, parent: KAN-22 }
    - { name: "1.11 Task 4 — GalleryService: subscribe, take the picture, store, evict", key: KAN-92, parent: KAN-22 }
    - { name: "1.11 Task 5 — The gallery view and the input to open it", key: KAN-93, parent: KAN-22 }
    - { name: "1.11 Task 6 — Wire the scene", key: KAN-94, parent: KAN-22 }
    - { name: "1.11 Task 7 — Verify by running it, and look at what comes out", key: KAN-95, parent: KAN-22 }
    # Story 1.12 (parent KAN-23) — created 2026-07-31
    - { name: "1.12 Task 1 — Decide the five things that gate this story", key: KAN-96, parent: KAN-23 }
    - { name: "1.12 Task 2 — Grading housekeeping this story owns (shared wording + FromPercent)", key: KAN-97, parent: KAN-23 }
    - { name: "1.12 Task 3 — GradeHudConfig ScriptableObject + asset", key: KAN-98, parent: KAN-23 }
    - { name: "1.12 Task 4 — GradeHud: subscribe, format once, show, fade", key: KAN-99, parent: KAN-23 }
    - { name: "1.12 Task 5 — Build and wire the HUD in the scene", key: KAN-100, parent: KAN-23 }
    - { name: "1.12 Task 6 — Verify by running it, and look at what comes out", key: KAN-101, parent: KAN-23 }
    - { name: "1.12 Task 7 — Hand the perceptual check to Alexv (AC3, AC4)", key: KAN-102, parent: KAN-23 }
---

# Camera Game - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for **Camera Game**, decomposing the requirements from the GDD and Architecture into implementable stories. (No standalone UX Design document exists yet; UI requirements — viewfinder overlay, grade-feedback HUD — are captured as functional requirements derived from the GDD and Architecture.)

The epic structure mirrors the GDD's validated 8-epic plan. **Epic 1 (the First Playable Slice) is the only epic that should be broken into detailed, ready-to-build stories until the core Catch Loop is proven fun.** Later epics are scoped at the requirement level here and detailed just-in-time.

## Requirements Inventory

### Functional Requirements

**Core Catch Loop (Epic 1 — MVP)**

FR1: The game has two camera states — **Walk** (exploration) and **Photo** (first-person viewfinder) — toggled by holding the raise-camera input (RMB / gamepad LT); releasing returns to Walk.
FR2: In Photo state the player can aim freely and **zoom** in/out (scroll wheel / gamepad) to compose the frame (zoom ~1×–4×, camera FOV ~60°→~18°).
FR3: The player can **capture** a photo with a click (LMB / gamepad RT) while in Photo state; capture is a no-op in Walk state.
FR4: A captured shot is **graded** on three dimensions — Subject Capture, Composition, and Timing — blended into a 1–5 star (0–100%) grade.
FR5: **Subject-capture gate** — the shot fails (near-zero) if the target subject is not within the frame, is occluded, or is **too small in the frame**. *(Revised 2026-07-28 by the Story 1.10 code review: originally "occupies below ~8% of the frame". The gate is now the subject box height as a fraction of the FRAME HEIGHT, shipped at 0.20 — the same measure FR6 scores on. The old ~8% was an AREA figure, and area around a tall thin figure is mostly empty air: a good full-body portrait measures 4.5% by area and 39% by height. FR6 was rewritten for this on 2026-07-27; this clause was left behind and is corrected here so the two describe one measure. Do not "restore" the 8%.)*
FR6: **Composition scoring** — score is higher when the subject is prominent in the frame and sits near the **centre**; lower when too small, cut off, or pushed toward a corner. *(Revised 2026-07-27 by Story 1.10: originally specified a rule-of-thirds bonus and "~25–50% of the frame". The thirds bonus was built, photographed in matched pairs, and judged worse than centred — in the empty test world and again in the real town. Prominence is now the subject's height as a fraction of the frame, since "25–50%" measured as area reads 4.5% for a good full-body portrait. See the GDD's grading section and Story 1.10's Dev Agent Record.)*
FR7: **Timing scoring** — full marks within ±0.5 s of the event's **peak window**, decaying to zero by ±2 s beyond it. *(Clarified 2026-07-28 by the Story 1.10 code review: the peak is an INTERVAL, not an instant — 1.5 s for the Town Drunk — and every frame inside it scores full marks. Distance is measured from the window's nearest edge via `ISubject.PeakOffset`, so the full-marks band is the peak duration plus 1 s. Measuring from the window's START instead, as the architecture sketch did, would score the last frame of the money shot as though it were 1.5 s early.)*
FR8: Captured shots are **saved to a gallery** with their grade and subject identity.
FR9: Every catchable event runs a **lifecycle** — Spawn → Build → Peak → Wind-Down → Despawn — advancing on its own timers.
FR10: Each event **self-telegraphs** via directional audio and/or visual cues within a cue radius (~25 m); discovery is diegetic (no map markers in core).
FR11: The **Town Drunk** event is playable end-to-end using the husband model and Drunk Pack animations: spawns (leaving the pub) → stumble-walks → peak stagger/near-fall (~1.5 s money-shot window) → recovers → despawns into an alley (~20–30 s lifecycle).
FR12: A **small playable patch of town** exists to walk around in for the slice (walkable surface with colliders).
FR13: The player explores with **free-roam traversal** (WASD/stick move, sprint to chase a moment) using the existing controller.

**The Living Town (Epic 2)**

FR14: **Vantage points** (rooftops, hills, docks) are reachable and give better composition angles.
FR15: The full **town map** is imported (Blender → FBX), zoned into **districts** (downtown, docks/waterfront, residential, oil rig/industrial, mountain/nature), with colliders and a baked NavMesh; reactive directional ambient audio per district.

**Event Content Pack #1 (Epic 3)**

FR16: **3–5 additional event types** are playable, each reusing the event-actor system as data, not new code (e.g., oil-rig smoke break, cheating husband, fisher falling in, graffiti tagger), with the animations they require sourced/authored.

**Progression & Reputation (Epic 4)**

FR17: Banking a graded shot earns **Reputation** — higher grades and rarer subjects pay more.
FR18: **Reputation tiers** unlock rarer/bigger events, new districts, camera upgrades, and home-base features.
FR19: A **"town log" collection** tracks captured event types and nature subjects with completion and rarity tiers.
FR20: **"New best" tracking** records the player's best grade per subject/event, rewarding re-capture.

**Home Base (Epic 5)**

FR21: A **home-base interior hub** lets the player review the gallery, **print** photos, and **decorate** the house with them.

**Nature & Collection Layer (Epic 6)**

FR22: **Spawnable nature subjects** (fish, trees, mushrooms, butterflies, pets) exist with per-type spawn probabilities and rarity tiers, photographable for the collection.
FR23: **Nature-spectacle events** (lightning strike, solar eclipse, avalanche) run on the event lifecycle as rare catchable moments.

**Missions & Mystery (Epic 7 — optional layer)**

FR24: A **mission system** offers photo-bounty objectives ("capture X doing Y"), mission-giver hubs, and a town-mystery thread advanced by capturing specific subjects as photo-evidence.
FR25: A mid/late-game **police-vs-criminal allegiance choice** branches available missions.

**Stealth Layer (Epic 8)**

FR26: A **stealth layer** adds proximity/eyeline detection on sensitive events; being **spotted** aborts the event (a soft consequence, never death), enabling sneak-to-shoot play.

### NonFunctional Requirements

NFR1: **Performance** — 60 FPS at 1080p on `PC_RPAsset` on a mid-range Windows desktop with the active event running.
NFR2: **Input responsiveness** — no perceptible input lag; camera raise/lower transition < 0.3 s, capture-to-feedback < 0.2 s.
NFR3: **Memory stability** — the event-actor lifecycle leaks no objects over an extended session (object pooling required; no Instantiate/Destroy in gameplay loops).
NFR4: **Render pipeline** — URP only; the Standard shader is not supported.
NFR5: **Build hygiene** — zero compile errors; clean Unity console after every script change (verify via MCP `read_console`).
NFR6: **Simulation budget** — cap concurrent active event actors (1 for the slice); use LOD/culling across the open town.
NFR7: **Audio locatability** — the mix must make directional cues clearly locatable; audio is gameplay-critical, not just ambience.
NFR8: **No fail state / fail-soft** — there is no death, health, or game-over; errors must never crash or hard-pause gameplay and are never shown to the player.
NFR9: **Platform & input** — Windows 64-bit standalone; keyboard/mouse primary, gamepad supported (New Input System, "Player" map).
NFR10: **Grade legibility** — players can distinguish a high-grade from a low-grade shot and understand *why* (legible grade feedback).

### Additional Requirements

These are technical/setup requirements drawn from the Architecture document that shape stories (especially Epic 1's foundation):

- **AR1 (Brownfield input):** Extend the existing `PlayerInput` (Send-Messages mode); repurpose "Attack" → **Capture**, add **RaiseCamera** (hold) and **Zoom** actions; gate Capture/Zoom to Photo mode.
- **AR2 (Event engine):** Data-driven `EventActor` (lifecycle FSM) + per-event `EventDefinition` ScriptableObject + pooled `EventManager` — the ~80%-reusable engine.
- **AR3 (Grading impl):** Hybrid screen-space "frame read" (frustum + occlusion gate · screen-bounds composition · lifecycle timing); no GPU readback; thresholds in a `GradingConfig` ScriptableObject.
- **AR4 (Decoupling):** ScriptableObject event channels (`ShotCapturedChannel`, `EventPeakedChannel`) connect capture → gallery → (future) reputation.
- **AR5 (Code org):** `CameraGame.*` namespaces; feature folders under `Scripts/`; one `CameraGame.asmdef` (Editor/Tests separate).
- **AR6 (Gallery persistence):** In-memory `List<CapturedShot>` via `GalleryService`, designed with a disk-ready seam for Epic 5.
- **AR7 (Camera fork — ADR-1):** Walk state is **first-person** (not third-person), reusing the existing FP controller/camera; "raise camera" = mode toggle + FOV zoom on one rig. (A small GDD note is advisable.)
- **AR8 (Import constraint):** The World FBX's embedded Blender camera must stay **disabled** on import (it overrides Main Camera via higher depth).
- **AR9 (Asset pipeline — Epic 2 blocker):** The town map currently exists only as a 214 MB `.blend`; it needs a **Blender → FBX export** before Unity import.
- **AR10 (Animator setup):** A Unity **Animator** must be built for the husband from the Drunk Pack (idle, idle variations, walk, turns, run) to drive the Town Drunk lifecycle.
- **AR11 (Project scaffold):** Create the folder/namespace structure (`Scripts/{Core, Player, PhotoMode, Grading, Events, Gallery}`, `Data/{Events, Grading, Camera, Channels}`) and the SO config assets before feature work.

### UX Design Requirements

_No standalone UX Design Specification exists yet. UI work items (first-person viewfinder/lens overlay styled like a 2000s camcorder, on-capture grade-feedback HUD showing subject %/composition/timing) are captured within the functional requirements above (FR1–FR8) and the Architecture's UI/`Scripts/PhotoMode` + `UI/` sections. If `gds-create-ux-design` is run later, dedicated UX-DRs can be folded in._

### FR Coverage Map

- FR1: **Epic 1** — Two camera states (walk ↔ photo toggle)
- FR2: **Epic 1** — Photo-mode aim & zoom composition
- FR3: **Epic 1** — Capture action (gated to photo mode)
- FR4: **Epic 1** — Blended 3-dimension shot grade
- FR5: **Epic 1** — Subject-capture gate
- FR6: **Epic 1** — Composition scoring
- FR7: **Epic 1** — Peak-timing scoring
- FR8: **Epic 1** — Save shots to gallery
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
- FR24: **Epic 7** — Mission system / photo-bounties / mystery
- FR25: **Epic 7** — Police-vs-criminal allegiance choice
- FR26: **Epic 8** — Stealth detection & "spotted" abort

_All 26 FRs are mapped. NFR1–NFR10 are cross-cutting and apply to every epic (enforced per-story in acceptance criteria and at code review). AR1–AR11 are realized primarily in Epic 1's foundation stories, with AR9 (map FBX export) gating Epic 2._

## Epic List

### Epic 1: First Playable Slice — The Catch Loop (MVP)
Prove the core loop is fun: a first-time player hears a cue, finds the Town Drunk, raises the camera, composes and captures a graded shot, sees it banked in their gallery, and wants to wait for the next one. Delivers the entire Catch Loop end-to-end on a small town patch, building the reusable camera, grading, and event-actor systems.
**FRs covered:** FR1, FR2, FR3, FR4, FR5, FR6, FR7, FR8, FR9, FR10, FR11, FR12, FR13
**Foundation (AR):** AR1–AR8, AR10, AR11
**Depends on:** — (greenfield slice on a brownfield base)

### Epic 2: The Living Town
Give the proven loop a real, navigable world: import the full town map, zone it into districts with colliders and NavMesh, place reachable vantage points, and lay down directional ambient audio so the world reads as alive and cues are locatable.
**FRs covered:** FR14, FR15
**Foundation (AR):** AR9 (map FBX export)
**Depends on:** Epic 1

### Epic 3: Event Content Pack #1
Add breadth of moments: 3–5 new event types built purely as `EventDefinition` data on the existing event-actor engine, with required animations sourced/authored (oil-rig smoke break, cheating husband, fisher, graffiti tagger).
**FRs covered:** FR16
**Depends on:** Epic 1, Epic 2

### Epic 4: Progression & Reputation
Give the player a reason to keep playing: reputation earned from graded shots, tier-based unlocks (events/districts/upgrades/home features), a town-log collection, and "new best" tracking that rewards re-capture.
**FRs covered:** FR17, FR18, FR19, FR20
**Depends on:** Epic 1, Epic 3

### Epic 5: Home Base
Close the session loop: a home-base interior hub where the player reviews the gallery, prints photos, and decorates the house with them — the payoff that sends the player back out.
**FRs covered:** FR21
**Depends on:** Epic 1, Epic 4

### Epic 6: Nature & Collection Layer
Cozy completionism: spawnable nature subjects (fish/trees/mushrooms/butterflies/pets) with rarity, and rare nature-spectacle events (lightning, eclipse, avalanche) on the event lifecycle.
**FRs covered:** FR22, FR23
**Depends on:** Epic 2, Epic 4

### Epic 7: Missions & Mystery (optional layer)
A light narrative spine: mission-giver hubs, photo-bounty objectives, the town-mystery thread advanced by photo-evidence, and a mid/late-game police-vs-criminal allegiance choice.
**FRs covered:** FR24, FR25
**Depends on:** Epic 3, Epic 4

### Epic 8: Stealth Layer
Tension on sensitive events: proximity/eyeline detection, a "spotted" consequence that aborts the moment (never death), and sneak-to-shoot play.
**FRs covered:** FR26
**Depends on:** Epic 3, Epic 7

---

## Epic 1: First Playable Slice — The Catch Loop (MVP)

Prove the core loop is fun: a first-time player hears a cue, finds the Town Drunk, raises the camera, composes and captures a graded shot, sees it banked in their gallery, and wants to wait for the next one. This epic builds the reusable camera, grading, and event-actor systems end-to-end on a small playable town patch.

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

## Epics 2–8: Detailed Just-In-Time

Per the GDD's scope discipline — *"Do not start a later epic until Epic 1 has proven the loop is fun"* — Epics 2 through 8 are intentionally **not** broken into detailed stories yet. Their goals, FR coverage, and dependencies are fixed in the **Epic List** above. Re-run `gds-create-story` / this workflow to detail each one just-in-time, once Epic 1 validates how the core systems actually feel.

**Known seeds for when those epics are detailed:**

- **Epic 2** — the town map FBX export (AR9) is the first story and a hard blocker; districts, vantage points (FR14), colliders/NavMesh, and per-district ambient audio (FR15) follow.
- **Epic 3** — each new event (oil-rig smoke break, cheating husband, fisher, graffiti) is one story = one `EventDefinition` + its animations; the engine from Story 1.6 is reused unchanged (FR16). Assets on hand: bandit, CEO, oil-rig worker FBX + a "bandit fight mode" walk.
- **Epic 4** — `ReputationSystem` subscribes to the existing `ShotCapturedChannel` (FR17–FR20); no rework of capture/grading needed.
- **Epic 5** — `GalleryService`'s disk seam (AR6) is cashed in here for print/decorate (FR21).
- **Epic 6** — nature subjects (FR22) and spectacle events (FR23) reuse the event lifecycle.
- **Epic 7** — `MissionService` listens to capture events; photo-as-evidence + allegiance branch (FR24–FR25).
- **Epic 8** — stealth detection + "spotted" abort hooks the `EventActor` phase events (FR26).
