---
title: Camera Game
game_type: Adventure (videography / exploration)
platforms: Windows (StandaloneWindows64)
created: 2026-05-26
updated: 2026-11-10
status: complete
---

# Camera Game - Game Design Document

**Author:** Alexv
**Game Type:** Adventure (videography / exploration)
**Target Platform(s):** Windows (StandaloneWindows64)

---

## Executive Summary

> *Revised 2026-11-10 (`../../sprint-change-proposal-2026-11-10.md`): at the stakeholder's request the core verb is now
> **video recording**, not still photography. The camera records clips to a limited memory card, every clip is graded,
> and the clips are cut into videos in the Edit Suite (Epic 9). This resolves the photo-first assumption that used to
> stand in **Assumptions and Dependencies**. Revised passages keep a dated note of their photo-era wording.*

### Core Concept

**Primary draw — _Catching the moment._** You play a small-town videographer whose camera is always pointed at the action. The town runs on its own clock — events ignite, peak, and pass whether you witness them or not — and the game is the thrill of being there at the right second and rolling while it happens, before it's gone: a robbery in progress, a husband busted cheating, lightning splitting the sky over the mountains.

The skill is the _catch_ — reading an event fast, getting into position, framing it, and keeping it framed while you record through the peak. The payoff is permanent: every clip on your memory card feeds a growing reputation as the town's eye — and becomes raw footage for the videos you cut.

**Supporting layers** (built on top of the core catch loop, never replacing it): collecting the town's nature and scenery (fish, trees, mushrooms, pets), uncovering the town's mystery through optional missions and a police-vs-criminal choice, light stealth to get shots unseen, an edit suite where clips are cut into videos, and a home base where they're reviewed and stills printed. These deepen the game, but the core catch loop must stand on its own first.

### Target Audience

- **Primary:** players who enjoy cozy-but-mischievous exploration and "capture/collect" games — fans of photography games (Pokémon Snap, TOEM, Umurangi Generation) and humorous life-sim sandboxes with an edge (Schedule 1).
- **Secondary:** completionists (gallery/collection hunters) and creator-minded players (the "become the biggest video maker in town" fantasy mirrors real content-creator culture).
- **Tone:** PG-13 dark comedy — crime and vice are comedic *subject matter*, never violence the player commits.
- **Skill profile:** accessible controls and a low mechanical barrier; depth comes from timing and framing, not reflexes or combat.

### Unique Selling Points (USPs)

1. **Filming is the verb, town chaos is the subject** — the "be there for the mayhem" fantasy, but you shoot with a camcorder, not a gun.
2. **A living town on its own clock** — moments are transient; *being there* is the skill.
3. **Skill-graded clips** — framing + catching the peak turn every clip into a score and a trophy.
4. **A Schedule-1-flavored cast** — a town of memorable lowlifes, drunks, and oddballs to catch in the act.
5. **From capture to creation** — clips become raw footage you cut into your own videos in the Edit Suite (and stills you print to decorate your home), building your reputation as the town's eye.

_(USPs 1, 3 and 5 revised 2026-11-10: they were "Photography is the verb … shoot with a camera", "Skill-graded shots — composition + timing turn every photo into a score", and "photos become physical trophies you print and decorate with (later: edit into shorts)".)_

---

## Goals and Context

### Project Goals

- **Ship something finishable** by scoping deliberately to a proven core loop plus a curated set of events — not the entire brain-dump.
- **Prove the core Catch Loop is fun** via the First Playable Slice before investing in breadth.
- **Build reusable systems** (NPC event actor, camera/recording/grading, memory card) so new content is cheap to add.
- **Developer learning goal:** grow Unity/URP + C# skills through an achievable, motivating project — practising the asset pipeline (Blender→FBX), the New Input System, animation, and gameplay systems.

### Background and Rationale

- **Origin:** a concept brain-dump (Schedule 1 art inspiration) for an amateur-videographer-in-a-wacky-town game.
- **Why this design:** the brain-dump spanned several genres; *"catching the moment"* was chosen as the primary draw because it yields a self-contained, testable loop suited to a solo/intermediate developer. Collection, mystery, comedy, and home-editing are retained as layers, not the foundation.
- **Existing foundation:** a Unity 6 / URP project with a player controller and camera (classes named `ThirdPersonController`/`ThirdPersonCamera` but first-person by behavior), a first-person POV camera, and a town map plus character/animation assets already staged. (See ADR-1 — the walk-state camera is first-person.)

---

## Core Gameplay

### Game Pillars

Three load-bearing pillars. Each survives the cut test — remove it and the game collapses into something else. Every downstream design decision must answer to these.

**Pillar 1 — Be There When It Happens (Presence & Timing).**
The town is alive and runs on its own clock. Events ignite, peak, and end on their own timers — miss the window and the moment is gone. The game rewards reading the world, moving fast, and being in the right spot at the right second.
- _Steers:_ event system needs a lifecycle (spawn → peak → despawn); the world must signal where action is; traversal must be fast enough to chase a moment.

**Pillar 2 — The Shot Is the Skill (Framing & Capture).**
Recording is an active skill, not a proximity button. Keeping the subject in frame, composing with zoom, and rolling through the peak of the action all matter — the game grades the clip.
- _Steers:_ the recording mechanic itself (aim, zoom, framing, start/stop); a scoring system; the entire moment-to-moment feel.
- _(Revised 2026-11-10: the body was "Taking a photo is an active skill … timing the click to the peak of the action … the game grades the shot". The name stands: in film language a **shot** is one continuous take.)_

**Pillar 3 — Every Clip Is a Trophy (Reward & Reputation).**
Each recorded moment is permanent proof that accumulates — a memory card of footage, a reputation, a score, and the videos you cut from it. The loop pays off: record → graded → card fills → reputation rises → bigger/rarer moments unlock.
- _Steers:_ progression; the memory card, Edit Suite and collection systems; the reputation economy; what unlocks rarer events.
- _(Revised 2026-11-10: was "Every **Photo** Is a Trophy … a gallery, a reputation, a score".)_

_Supporting layers map cleanly onto these: nature collecting → Pillars 2+3; mystery/missions → Pillar 1; stealth → Pillar 1; edit suite and home base → Pillar 3._

### Core Gameplay Loop

The loop must be satisfying on its own — in a 30-second slice, with no content or story around it — before any layers are added. It derives directly from the three pillars.

**The Catch Loop (second-to-second — the heartbeat):**

1. **Detect** — the player notices something happening nearby (Pillar 1).
2. **Position** — read the event, move fast, and find a good angle before it peaks (Pillar 1).
3. **Frame** — aim, zoom, and compose (Pillar 2).
4. **Roll** — record through the peak of the action (Pillar 2).
5. **Grade** — the game scores the clip on subject, framing, and how much of the peak it caught (Pillar 2 → 3).
6. **Bank** — the clip lands on the memory card and reputation ticks up; rising reputation unlocks bigger/rarer moments, which feed back into step 1 (Pillar 3).

_(Revised 2026-11-10: step 4 was "**Shoot** — click at the peak of the action", and step 6 was "the photo enters the gallery".)_

**Discovery model: sensory cues only (diegetic).** Events are found by reading the world — a distant siren, a gathering crowd, smoke, a splash, raised voices — not by map markers or tip-offs. This is the most immersive expression of Pillar 1 and the leanest to build (no map UI, no tip-off content system). _Design obligation:_ every event must telegraph itself clearly through audio and/or visual cues, or the player will wander. (A tip-off/police-scanner layer is noted as possible future scope, not in the core.)

**The Session Loop (per play session — the rhythm):**

Head into town → run several Catch Loops → return home → watch your clips and cut them into videos, print stills/decorate, and watch reputation & unlocks grow → head back out for rarer, bigger moments. _(2026-11-10: was "review the gallery, print/decorate".)_

The session loop is what gives the Edit Suite, home base, printing, and reputation systems their reason to exist: they are the payoff that sends the player back out for the next catch.

### Win/Loss Conditions

**No hard game-over / no death.** This is a camera, not a gun. The game deliberately has no health, no combat-death, and no fail screen — adding one would spawn a combat/survival game that competes with the three pillars for the build budget. Dangerous content (armed criminals, car chases, drug deals) is *subject matter to photograph*, not a threat to the player's life.

**Failure is soft and already baked into the mechanic:**
- **Missed moment** — the event peaks and despawns before the player shoots it. This is the core "loss," and the sting that powers the tension.
- **Weak grade** — the player got *a* shot, but not *the* shot (poor timing/composition).
- **Spotted** (in stealth situations) — criminals scatter, the event aborts, and the moment is ruined. A consequence, not a death.

**Structure: open "living town" sandbox.** A continuous, living town where events spawn over time; the player plays as long as they like, with no enforced day clock or run timer. The spine is **progression**:
- **"Winning" / completion** = reaching the top reputation tier ("the biggest video maker in town") and/or completing the town mystery.
- Open-ended play continues after that (fill the gallery, chase rare events).

_Future scope (not core): a day/time cycle could later layer on to schedule rare events; timed "scoop" challenges vs. the rival journalist could be optional content._

---

## First Playable Slice (MVP)

**Purpose:** the whole game in miniature — one thin slice that runs the *entire* Catch Loop end to end, to prove the core is fun before building any breadth. If it's fun, the rest of the game is "add content to a proven engine." This is the target for Epic 1.

### Fixed core (built regardless — this is the reusable engine)

- **Camcorder:** raise (hold RMB) → first-person viewfinder → zoom → record (LMB start/stop).
- **Clip grading:** per-frame Subject-in-frame (gate) + Composition (size/placement), plus Peak coverage → a star/% grade.
- **Memory card:** limited capacity; clips saved with their grades and watchable in-game (no progression/economy yet).
- **A small playable patch of town** to walk around in (a blocked-out area is fine before the full map is imported).

_Revised 2026-11-10: the slice's verb is now recording. The photo version — raise → zoom → capture → graded shot → gallery — was built in Stories 1.1–1.12 and is the foundation this one builds on._

### The proof-event: "The Town Drunk"

Single NPC (the **husband** model), built using **only existing animations** (the Drunk Pack: idle, idle variations, walk, run, turns). Lifecycle:

```
SPAWN     → husband appears (e.g., leaving the pub). Cue: drunken mumbling/hiccup audio + wobbly gait.
BUILD     → stumble-walks along a path, swaying (drunk walk + turns).
★ PEAK ★  → a big stagger / near-fall or slump against a wall (idle variation) = the money shot (short window).
WIND-DOWN → recovers, keeps walking.
DESPAWN   → reaches end of path (enters an alley) and is removed.
```

_Initial timings (tune via playtest): full lifecycle **~20–30 s**; peak (sway/near-fall) window **~1.5 s**; cue audible radius **~25 m**; one drunk active at a time in the slice._

This deliberately builds the reusable **"NPC event actor"** system (spawn → follow path → play state animations on a lifecycle → self-telegraph → despawn) that ~80% of future events (robberies, deals, cheating) will reuse.

### Slice success criteria (how we know the loop is fun)

A first-time player, with no instructions, **hears the cue → finds the drunk → raises the camcorder → records him through the stagger → sees a satisfying graded clip land on the memory card, watches it back → and wants to wait for the next one.** If that's true, the core is proven.

### Explicitly NOT in the slice

Reputation economy & unlocks, any other event type, civilian/ambient NPCs, stealth, missions/mystery, the Edit Suite (Epic 9), the home base (printing / decoration), day-night cycle, and multiple town districts. All are later epics.

---

## Game Mechanics

### Primary Mechanics

#### 1. Two camera states

The game switches between two states, both built on the project's existing first-person POV camera rig (see **ADR-1**):

- **Walk state (first-person):** the player explores the town in first-person, reading the world for sensory cues. _(**ADR-1:** walk is **first-person**, not third-person — it reuses the existing FP controller/camera, and "raise camera" is a mode toggle + FOV zoom on the same rig. Note: the existing `ThirdPersonController`/`ThirdPersonCamera` classes are first-person by behavior; the names are stale relative to what they do.)_
- **Viewfinder state (first-person through the camcorder; the code calls it Photo mode):** holding the camera-raise input switches to a first-person view through the lens, with **zoom** (in/out) for composing the frame. Releasing lowers the camera back to walk state and stops any recording. Raising the camera is a deliberate, skillful act — not a passive proximity check. _(2026-11-10: was "Photo state".)_

#### 2. The recording action

In viewfinder state: aim freely, zoom to compose, and press **record** to start, then again to stop. Lowering the camera or a full memory card also stops recording, and the footage so far is always kept. Each finished clip is graded and saved to the memory card. Zoom is the player's primary tool for chasing a high composition score, and it works while recording.

_(Revised 2026-11-10: this was "The camera / photo action" — capture a single graded photo with a click, saved to the gallery.)_

#### 3. Event lifecycle (makes timing a skill)

Every catchable event runs through phases on its own timer (Pillar 1):

```
SPAWN ──► BUILD ──► ★ PEAK ★ ──► WIND-DOWN ──► DESPAWN
(cues     (action    (the         (it's          (gone —
 begin)    ramps)     money shot)   ending)        missed it)
```

The **peak** is a short "money-shot" window (e.g., robber mid-swing, boat slipping under, lightning at strike). Events self-telegraph via audio/visual cues throughout (the diegetic discovery obligation).

#### 4. Clip grading model (Framing + Peak coverage)

A recorded clip is graded by sampling the shot grader on frames throughout the recording, blended into a final grade (a 1–5 star rating / percentage):

1. **Subject Capture (gate, per frame):** is the target subject within the frame, large enough, and not occluded? Frames that fail contribute nothing. A clip in which the subject never passes is a miss, and the miss reason is kept.
2. **Composition (per frame):** how well the subject is framed — its **size in frame** (prominent and not cut off, controlled via zoom/distance) and its **placement/centering**. Higher when the subject is bold and well-placed. Unchanged from the photo grader.
3. **Peak coverage:** how much of the event's **peak** window the clip contains with the subject framed — full marks for the whole window, partial credit for part of it. A well-framed clip that missed the peak still scores above zero (a designer-set floor), but far below one that caught it.

The final grade determines reputation gained and whether the clip is a "new best" for that event/subject type (Pillar 3). _Deferred to future layers (not core): focus/sharpness, exposure/lighting, bonus subjects in frame, shot angle, camera steadiness._

_Revised 2026-11-10 (`../../sprint-change-proposal-2026-11-10.md`). The photo-era model scored a single frame on Subject + Composition + **Timing**: "proximity to the event's peak window — maximal at the peak, falling off the earlier or later the click lands". With video, catching the peak becomes a question of coverage rather than of one click's timing. The per-frame Subject and Composition scoring built in Stories 1.9–1.10 is unchanged and is the engine underneath. The floor carries Alexv's Story 1.12 feedback ("too extreme to give 0") from superseded Story 1.13. How the axes blend, and whether footage without the subject lowers the grade, is decided by measuring recorded clips in Story 1.16._

#### 5. Initial tuning targets (starting values — confirm via playtest)

These are first-guess numbers so the build has something concrete to test against; expect to tune them.

- **Grade scale:** 1–5 stars (internally 0–100%). The star boundaries are designer-tunable in
  `GradingConfig` (shipped at **5★ ≥ 90% · 4★ ≥ 70% · 3★ ≥ 45% · 2★ ≥ 20%**, below that 1★).
  - *Added 2026-07-30 (Story 1.10 code review).* The mapping was previously `ceil(grade × 5)` in code,
    which silently meant "80% is a perfect photograph" and let a merely-good shot tie the money shot at
    5★ — Alexv judged them clearly different pictures. The scoring was already right (it ranked them
    100% vs 85%); the quantizer was collapsing the gap. A miss and a bad shot still both read 1★ — the
    scale starts at one and there is no 0★, which is why `ShotGrade.MissReason` exists to tell them apart.
- **Subject-capture gate:** the subject's projected box must be at least **20% of the frame HEIGHT** to count as captured (below this = failed shot), and must not be occluded.
  - *Revised 2026-07-28 (Story 1.10 code review).* This read "**≥ ~8%** of the frame", an AREA figure. When the composition bullet below was rewritten on 2026-07-27 to score on frame **height**, this gate clause was left behind — so the two sentences described two different measures of "how big is he", which is exactly the drift that produces a silent disagreement between the gate and the score. Area around a tall, thin, arms-down figure is mostly empty air: a good full-body portrait reads **4.5% by area** and **39% by height**. The gate and the prominence curve now run on the same measure. Do not "restore" the 8%.
- **Composition score:** peaks when the subject is prominent in the frame and sits **near the centre**; falls off when too small, cut off, or pushed toward a corner.
  - *Revised 2026-07-27 (Story 1.10).* This originally read "fills ~25–50% of the frame and sits near a **rule-of-thirds line**". Both halves changed against evidence:
    - **Thirds → centred.** The thirds bonus was built as specified and then disproven by looking at photographs. In matched pairs — identical subject, identical instant, identical camera position, differing *only* in where in the frame he sits — the centred shot was judged the better photograph, in the rig's empty test world and again in the real town once the "the thirds shot only looks lopsided because the rest of the frame is empty" confound was ruled out.
    - **"~25–50% of the frame" was never measurable as written.** It describes a photograph, not a quantity the code has. Prominence is now the subject's **height as a fraction of the frame**, with the sweet spot set by looking at the shoot (see `GradingConfig`); measured as *area*, a good full-body portrait reads 4.5%.
- **Timing score** *(photo era — kept as history; superseded by Peak coverage on 2026-11-10)*: full marks within **±0.5 s** of the peak, decaying to zero by **±2 s**.
- **Peak coverage:** full marks when the whole peak window (**1.5 s** for the drunk) is recorded with the subject framed. The floor for a clip that missed the peak is set by measuring recorded clips (Story 1.16). *(Added 2026-11-10.)*
- **Memory card:** **~2 minutes** of footage to start (tunable). *(Added 2026-11-10.)*
- **Camera feel:** zoom range **~1×–4×**; raise/lower transition **< 0.3 s**; record-start-to-feedback (REC + beep) **< 0.2 s**. *(2026-11-10: was capture-to-feedback.)*

### Controls and Input

Built on the New Input System (Player action map already exists). Proposed bindings:

| Action | Keyboard/Mouse | Gamepad | Notes |
|---|---|---|---|
| Move | WASD | Left stick | Walk state (existing) |
| Look | Mouse | Right stick | Both states (existing) |
| Raise camera / Viewfinder | Hold RMB | Hold LT | Enters first-person viewfinder |
| Record (start/stop) | LMB | RT | Viewfinder state only (2026-11-10: was Capture) |
| Zoom in/out | Scroll wheel | Right stick / dpad | Viewfinder state only; works while recording |
| Sprint | Shift | L3 | Chase a moment (existing) |
| Jump | Space | A | Existing |
| Interact | E | X | Home base, NPCs (later layers) |

_Note: the existing input actions (Move, Look, Sprint, Jump, Interact, Attack, Crouch) are reused; "Attack" can be repurposed/renamed to Capture, and "Aim" added for camera-raise. (Done in Stories 1.3 and 1.5. From 2026-11-10 the Capture action starts and stops recording.)_

---

## Adventure Specific Elements

### Exploration Mechanics

- Free-roam first-person traversal of an open "living town" (walk/sprint; existing controller — see ADR-1).
- **Discovery is diegetic:** players navigate toward sensory cues — audio (sirens, crowds, mumbling, splashes) and visual (smoke, gathering NPCs, lights). No map markers in the core.
- Traversal must be fast enough to **chase a moment before it peaks** (sprint matters; bikes/skateboards are possible future traversal aids).
- **Vantage points** (rooftops, hills, docks) give better angles — tying exploration directly to composition (Pillar 2).

### Story Integration

- Story is **optional and layered on top** of the sandbox — no forced critical path.
- The spine is an emergent *town reputation* arc: the more you document, the more you're pulled into the town's mystery (the oil-rig fraud / shady CEO thread).
- Delivered through optional missions and environmental storytelling rather than long cutscenes.
- The **police-vs-criminal allegiance choice** is a mid/late-game branch (scope beyond the first releases).

### Puzzle Systems

- "Puzzles" are **framing/observation challenges**, not logic/inventory puzzles: work out *where to be and when to capture* a specific graded shot.
- **Clip-bounty objectives** ("record X doing Y"): read cues, position, and time the peak — a spatial/temporal puzzle.
- **Light investigation:** assemble the mystery by recording specific subjects/locations (footage-as-evidence). _(2026-11-10: was photo-bounty / photo-as-evidence.)_

### Character Interaction

- In the core, NPCs are primarily **subjects to film**, not dialogue trees.
- **Event actors:** scripted NPC behaviours with lifecycles (the reusable NPC-event-actor system) — the drunk, robbers, the cheating husband, the fisher, etc.
- A few key NPCs act as **mission-givers/hubs** (e.g., "Mr. X," a police contact, a criminal contact) — light "show-footage / accept-bounty" interaction, not branching conversation.
- A **rival journalist** recurs as an antagonist who races you to scenes (future scope).

### Inventory and Items

- The **camera** is the core tool (with zoom; future: lenses/upgrades as progression rewards).
- The **memory card** is the main "inventory" — recorded clips with grades, organised by subject/event type and rarity, and limited by capacity: choosing what to keep is part of play.
- Later layers: **printed stills** pulled from clips (decoration items for the home base), the player's **edits** (Epic 9), and camera upgrades. The memory card's capacity is the storage resource.
- _(Revised 2026-11-10: was "The **Gallery** is the main inventory — captured shots with grades", with "printed photos" and "optional film/storage as a soft resource" as later layers.)_

### Environmental Storytelling

- The town itself tells the story: graffiti, shady businesses, the oil rig, and run-down vs. wealthy districts hint at the mystery.
- Recurring NPCs in believable routines make the town feel alive (the drunk leaving the pub, oil-rig workers on smoke breaks).
- The player's **memory card and edits become a personal record** of the town's character — a narrative artifact built by play.

---

## Progression and Balance

### Player Progression

- **Reputation ("the town's eye")** is the primary track, earned by banking graded clips — higher grades and rarer subjects pay more.
- Reputation tiers unlock: rarer/bigger events in the world, new districts, camera upgrades (zoom/lens/quality), and home-base features (printing; a room for the Edit Suite).
- **Collection progression:** a "town log" of event types and nature subjects (fish/trees/mushrooms/pets) with completion tracking and rarity tiers.
- **"New best" per subject/event** encourages re-capturing for a better grade — a key replay driver.

### Difficulty Curve

- No failure-based difficulty; difficulty = **how demanding the shot is.**
- Early events are slow with generous, obvious peaks and long windows (the drunk). Later/rarer events peak faster, are briefer, or demand better vantage/timing (lightning strike, car chase) — the skill ceiling rises.
- Rarer subjects appear less often and require more reading of the world (Pillar 1) — **scarcity, not punishment, is the challenge.**

### Economy and Resources

- **Primary "currency": Reputation** (earned from shots; "spent" implicitly as unlock thresholds).
- **Optional soft currency (future):** in-game money from selling/publishing footage (to a paper/blog), used for camera upgrades, decorations, and traversal items.
- **No hard consumables in the core** (no ammo/health). The memory card's limited capacity is the pacing resource (2026-11-10); a battery is possible later — flagged optional, not core.

---

## Level Design Framework

### Level Types

- One contiguous open town (the `game map` asset), internally zoned into **districts**: downtown, docks/waterfront, residential, the oil rig/industrial edge, and a mountain/nature fringe.
- District types map to event categories: **crime** (downtown/industrial), **drama** (residential), **nature spectacle & wildlife** (mountain/waterfront).
- The **home base** (player's house) is an interior "hub" level for review/print/decorate (and, once Epic 9 exists, a room for the Edit Suite).

### Level Progression

- Districts **unlock with reputation**, gating the world to control scope and pace the mystery's reveals (the wealthy/industrial core opens later).
- The **First Playable Slice** uses a single small district (or blocked-out patch) with the drunk event only.
- New event types and nature subjects are **seeded into districts** as the player progresses, keeping the living town fresh.

---

## Art and Audio Direction

### Art Style

- **Inspiration: Schedule 1** — stylised, slightly lo-fi/PS2-era charm; readable shapes; exaggerated, caricatured characters (the "town unc," balaclava bandits). Comedic, not gritty-realistic.
- **Stylised low-poly** with clean URP lighting; emphasis on **silhouette readability** so events/subjects telegraph at a distance (supporting the diegetic-discovery obligation).
- Character design follows the brain-dump's **2000s/streetwear** cues (baggy jeans, printed tees, beanies/balaclavas).
- The **camcorder viewfinder UI** is a key art surface (lens overlay, REC dot, running timecode, card-time-remaining counter, CARD FULL, grade feedback) — it should read like a 2000s camcorder to match the era vibe. _(2026-11-10: was "photo-mode viewfinder … camcorder/digicam".)_

### Audio and Music

- **Audio is gameplay-critical**, not just ambience: distinct, *directional* cues are the primary discovery mechanism (sirens, crowd murmur, drunken mumbling, splashes, thunder). The mix must make cues locatable.
- Reactive per-district ambience; a light, comedic/lo-fi music bed that **swells when an event peaks** (reinforcing the money-shot moment).
- Satisfying **camera SFX** (record start/stop beeps, zoom whir) — core game-feel for the recording action. _(2026-11-10: was shutter.)_

---

## Technical Specifications

### Performance Requirements

- **Target:** smooth play on a mid-range Windows desktop (StandaloneWindows64); 60 FPS at 1080p on `PC_RPAsset`.
- **Recording** must hold that target. The recording approach is chosen by measurement (architecture ADR-4, Story 1.14) and must run in a Windows build, not only in the Editor: Unity Recorder, which films the verification rigs, is Editor-only. *(Added 2026-11-10.)*
- The living-town simulation must be budgeted: **cap concurrent active event actors**, rely on the spawn/despawn lifecycle to bound cost, and use LOD/culling across the open town.
- **URP only** (project constraint); the Standard shader is not supported.

### Platform-Specific Details

- **Primary:** Windows 64-bit standalone. Mobile is **not** a current target, though a `Mobile_RPAsset` exists — avoid cheaply-avoidable mobile-hostile choices (informational only; no mobile commitment).
- **Input:** keyboard/mouse primary, gamepad supported (New Input System, Player map).
- The World FBX's embedded **Blender camera must stay disabled** on import (it overrides Main Camera via higher depth — known project constraint).

### Asset Requirements

- **Available now:** 5 character FBX (main character, bandit, CEO, husband, oil-rig worker); walk anims for each; a full **Drunk Pack** for the husband; the `game map .blend` town.
- **Needed soon:** an **FBX export of the town map** (Blender → FBX) for Unity import; an Animator set up for the husband from the Drunk Pack; camera/viewfinder UI assets; camera SFX; an audio-cue library.
- **Needed later (per event):** event-specific animations (spray-paint, grab/mug, fishing, fight) — these gate future events and should be authored/sourced per-epic.
- **Pipeline note:** the concept `.docx` is currently lock-held open in Word — close it before automated reads.

---

## Development Epics

### Epic Structure

Sequenced by dependency: **Epic 1 is the First Playable Slice**; every later epic is a layer that builds on a proven core. Detailed stories for each epic are produced by the `gds-create-epics-and-stories` step (into `epics.md`). Do **not** start a later epic until Epic 1 has proven the loop is fun.

| # | Epic | Goal | Key deliverables | Depends on | Pillars |
|---|------|------|------------------|-----------|---------|
| **1** | **First Playable Slice — The Catch Loop (MVP)** | Prove the core loop is fun | Camcorder (raise/zoom/record); clip grading (subject+composition+peak coverage); memory card + clip playback; reusable **NPC event-actor** system; the **Town Drunk** event; a small playable town patch | — | 1·2·3 |
| **9** | **The Edit Suite** | Turn clips into videos | Pick clips from the memory card, trim, arrange on a timeline, play back, save edits; later: music bed, titles, transitions, export (open) | E1 | 3 |
| **2** | **The Living Town** | A real, navigable world | Town map FBX export + import, colliders & NavMesh; district zoning; traversal polish; ambient/directional audio beds; vantage points | E1 | 1 |
| **3** | **Event Content Pack #1** | Breadth of moments | 3–5 more events reusing the event-actor system (e.g., oil-rig smoke break, cheating husband, fisher, graffiti); source/author the needed animations | E1, E2 | 1·2 |
| **4** | **Progression & Reputation** | A reason to keep playing | Reputation economy & tiers; unlock gating (events/districts); the "town log"/collection; "new best" tracking; grade→reward | E1, E3 | 3 |
| **5** | **Home Base** | Close the session loop | Player-house hub interior; clip review; printing stills from clips & decoration; a room for the Edit Suite | E1, E4 (E9 for the room) | 3 |
| **6** | **Nature & Collection Layer** | Cozy completionism | Spawnable nature subjects (fish/trees/mushrooms/butterflies/pets) with rarity; nature-spectacle events (lightning, eclipse, avalanche) | E2, E4 | 2·3 |
| **7** | **Missions & Mystery** (optional layer) | Light narrative spine | Mission-givers/hubs; clip-bounties; the town-mystery thread; footage-as-evidence investigation | E3, E4 | 1 |
| **8** | **Stealth Layer** | Tension on sensitive events | Proximity/eyeline detection; "spotted" consequence (event aborts); sneak-to-shoot | E3, E7 | 1 |

_Epic 9 is sequenced **immediately after Epic 1**, ahead of Epic 2. It is listed second in the table to show that, and numbered 9 only so the existing epic numbers and Jira keys stay stable (added 2026-11-10)._

_Deferred beyond this plan (vision, not committed): online sharing, complete character roster, deep police-vs-criminal branching, multiplayer, mobile. (The full video-editing minigame was promoted from this list to Epic 9 on 2026-11-10.)_

---

## Success Metrics

### Technical Metrics

- The slice runs at **60 FPS / 1080p** on target hardware with the drunk event active **and while recording**.
- Camera state switch (walk ↔ viewfinder) and record start/stop register with **no perceptible input lag**.
- The event-actor lifecycle (spawn→despawn) **leaks no objects** over an extended session (stable memory).
- **Zero compile errors**; clean `read_console` after script changes (project workflow).

### Gameplay Metrics

- **Slice success:** an uninstructed first-time player completes the full Catch Loop (hear cue → find → frame → record → see a graded clip on the memory card) and **voluntarily seeks the next event.**
- Players can distinguish a high-grade from a low-grade clip and understand *why* (grade feedback is legible).
- **Re-capture behaviour:** players retry events to beat their "new best" (validates the trophy/reputation loop).

---

## Out of Scope

**Out of scope for the First Playable Slice (later epics):** the reputation economy & unlocks, every event except the drunk, ambient/civilian NPCs, stealth, missions/mystery, the Edit Suite (Epic 9), the home base (printing, decoration), day-night cycle, multiple districts, and camera upgrades.

**Deferred for the foreseeable project (full-vision "someday," explicitly not committed):** online sharing/social features, the complete ~18-character roster, a deep branching police-vs-criminal questline, multiplayer, a mobile release, and a large simulated economy. These remain vision, not commitments. (The full video-editing minigame was on this list until 2026-11-10, when it was promoted to Epic 9: The Edit Suite.)

---

## Assumptions and Dependencies

- **Engine/tech:** Unity 6 (6000.3.8f1), URP 17.3.0, the New Input System, and the AI Navigation package (for NPC pathing) — all present.
- **Existing code:** the existing controller and camera (named `ThirdPersonController`/`ThirdPersonCamera` but first-person by behavior) plus the first-person POV camera are functional and reusable — **both** walk and viewfinder states build on the FP camera rig (ADR-1).
- **Asset pipeline:** Blender is available to export the map and (if needed) author/retarget animations; Mixamo or similar may source missing event animations.
- **Slice dependency:** the husband model + Drunk Pack must import and retarget cleanly into a Unity Animator; the town map (or a blocked-out patch) must be walkable with colliders.
- **Primary risk / assumption:** the developer is solo and intermediate — **scope discipline (slice-first) is the project's main risk control.**
- **Tooling:** MCP for Unity is configured for editor interaction (scene/script/test tooling).
- **[RESOLVED 2026-11-10] Video at the core.** This line used to read: *"[ASSUMPTION] Photo-first core vs. 'video maker' fantasy. The original concept's strap-line frames the player as a video maker who edits clips into shorts. This GDD makes the v1 core verb still photography (capture single graded shots), because it yields a far simpler, testable loop for a solo dev. The video-capture and video-editing fantasy is preserved as deferred future scope (see Out of Scope), not cut. If the developer wants video at the core instead, the camera mechanic and grading model would need revisiting before Epic 1."* On 2026-11-10 the stakeholder asked for exactly that: video clips rather than photos, saved to a memory card, and later edited into videos. The mechanic and grading model were revisited as this assumption predicted. Recording replaces the shutter, the photo grader became the per-frame engine of clip grading, and video editing moved from deferred scope to Epic 9. Epic 1 was 12 stories in by then, and none was rolled back. See `../../sprint-change-proposal-2026-11-10.md`.
