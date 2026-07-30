# Camera Game GDD — Decision Log

Chronological record of every design decision, change, and version transition.

---

## 2026-05-26 — Workflow initiated

- **Intent:** Create (new GDD)
- **Project:** Camera Game (Unity 6 / URP, StandaloneWindows64)
- **Source input:** Concept brain-dump provided by Alexv (matches `CAMERA GAME SHARED FOLDER/Camera game.docx`)
- **Game type match (proposed):** Adventure — story-driven exploration + uncovering a town mystery, narrative missions, police/criminal choice architecture. Signature mechanic is photography/videography (a USP, not a standard genre bucket). Light stealth (sneak missions) and light sim (video editing, house decoration) layers noted. Medium complexity.
- **Developer context:** Junior learning game dev; ambitious scope. Priority: define a small first-playable slice within the full vision.
- **Existing project state:** Third-person controller + chase camera already implemented; assets (5 character models, walk/locomotion animations, town map .blend) staged in `CAMERA GAME SHARED FOLDER/`, not yet imported.
- **Workspace:** `_bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/`

## 2026-05-26 — Direction confirmed

- **Game type:** CONFIRMED Adventure (story-driven exploration). Photography/videography = signature USP mechanic.
- **Working mode:** Facilitative — design-critical sections walked one at a time before drafting.
- **needs_narrative flag:** SET (Adventure genre guide is `<narrative-workflow-recommended>`). Offer `gds-create-narrative` at Finalize.
- **Next:** Walk Core Fantasy/Vision → Game Pillars.

## 2026-05-26 — Core Fantasy / Vision locked

- **Primary draw:** CONFIRMED "Catching the moment" — reaction-based photography of live, transient town events (crime, drama, nature spectacle). Chosen by user over "collecting the world," "uncovering the mystery," and "comedy & vibes."
- **Rationale:** Yields a clear, testable core loop (presence → frame → capture → reward) that stands alone without requiring massive content (collecting), deep branching (mystery), or hard-to-engineer humor (comedy). Best fit for a solo/intermediate dev targeting a first playable slice.
- **Supporting layers (kept, not cut):** nature/scenery collection; town mystery + optional missions + police/criminal choice; light stealth; home-base video editing & photo decoration.
- **Next:** Game Pillars — 3 proposed (Presence & Timing / Framing & Capture / Reward & Reputation), pending user pressure-test.

## 2026-05-26 — Game Pillars locked

- **Pillars (3) CONFIRMED** by user (chose "lock all three"):
  1. **Be There When It Happens** (Presence & Timing) — town runs on its own clock; events spawn → peak → despawn on timers.
  2. **The Shot Is the Skill** (Framing & Capture) — photography is graded on frame/composition/zoom/timing, not proximity.
  3. **Every Photo Is a Trophy** (Reward & Reputation) — captures accumulate into gallery/score/reputation that unlocks rarer events.
- **Cut-test rationale:** each pillar, if removed, breaks the game (static photo-tour / depthless verb / no replay reason). Supporting layers map onto the three (collecting→P2+P3, mystery/missions/stealth→P1, home base→P3).
- **Next:** Core Gameplay Loop — define the second-to-second and session-level loop derived from the pillars.

## 2026-05-26 — Core Gameplay Loop locked

- **Catch Loop (heartbeat) CONFIRMED:** Detect → Position → Frame → Shoot → Grade → Bank → (rep unlocks rarer events) → loop.
- **Session Loop CONFIRMED:** town outing → multiple catches → return home → review/print/decorate + reputation growth → back out for rarer moments. Justifies the home-base/printing/reputation systems as the payoff that drives re-engagement.
- **Discovery model DECIDED: sensory cues only (diegetic)** — chosen by user over "tip-offs then close in" and "map blips/directional nudge." Rationale: purest expression of Pillar 1 and leanest build for a first slice (no map UI, no tip-off pipeline). **Design obligation logged:** every event must self-telegraph via audio/visual cues. Tip-off/police-scanner = future scope, not core.
- **Next:** Primary Mechanics — detail the camera/photo mechanic (aim, zoom, framing, grading) and the event lifecycle, derived from the loop.

## 2026-05-26 — Primary Mechanics locked

- **Two camera states CONFIRMED:** Walk (third-person chase, existing) ↔ Photo (first-person viewfinder, reuses existing FP POV camera) via hold-to-raise. Raising the camera is a deliberate act.
- **Event lifecycle CONFIRMED:** Spawn → Build → ★Peak★ → Wind-down → Despawn; peak = short money-shot window; events self-telegraph (diegetic obligation).
- **Shot grading model DECIDED: "Timing + Composition"** — chosen by user over "Timing-focused (lean)" and "Full photography sim." Three scored dimensions: (1) Subject Capture = gate, (2) Composition = size-in-frame + placement (zoom is the player's tool), (3) Timing = proximity to peak. Blends to a 1–5 star / % grade → drives reputation + "new best" per event type.
- **Deferred (future layers, not core):** focus/sharpness, exposure/lighting, bonus subjects, shot angle.
- **Controls drafted** on existing New Input System map: Hold RMB/LT = raise camera; LMB/RT = capture; scroll/stick = zoom. "Attack" action repurposed → Capture; add "Aim" for raise.
- **Next:** Win/Loss conditions & session structure (is there failure? how does a session start/end?).

## 2026-05-26 — Win/Loss & session structure locked

- **No hard game-over / no death GUARDRAIL CONFIRMED** (user accepted). Camera, not a gun. No health/combat-death/fail screen — protects build budget for the pillars. Dangerous content = subject matter, not player threat.
- **Soft failure model:** missed moment (event despawns) / weak grade / spotted (event aborts). The missed-moment sting is the core tension.
- **Session structure DECIDED: open "living town" sandbox** — chosen by user over "day-cycle structure" and "timed scored runs." Continuous living town, events spawn over time, no enforced clock/timer. Spine = progression. "Win"/completion = top reputation tier and/or mystery solved; open-ended play continues after.
- **Future scope (not core):** day/time cycle to schedule rare events; optional timed "scoop" challenges vs. rival journalist.
- **Next:** First Playable Slice — carve the minimal buildable vertical slice from the full vision (critical for solo/intermediate dev).

## 2026-05-26 — First Playable Slice (MVP) locked

- **Asset inventory checked** (`CAMERA GAME SHARED FOLDER/`): 5 character FBX (main character, bandit, ceo, husband, oil rig worker); mostly walk-only anims EXCEPT the **husband** who has a full **Drunk Pack** (idle, idle variations, walk, run, turns). Map = `game map .blend` (needs FBX export). `.docx` is lock-held open in Word.
- **Slice fixed core CONFIRMED:** camera photo-mode (raise/zoom/capture) + grading (subject/composition/timing) + minimal gallery + small town patch. = the reusable engine; target of Epic 1.
- **Proof-event DECIDED: "The Town Drunk"** — chosen by user over "Lightning strike" and "Bandit crime (graffiti/mugging)." Rationale: uses ONLY existing animations (Drunk Pack), and forces building the reusable **NPC event actor** system (~80% of future events reuse it). Lowest content risk. Lifecycle: spawn(leave pub, mumble cue) → stumble-walk → ★sway/near-fall/slump peak★ → recover → despawn.
- **Slice success criterion:** uninstructed player hears cue → finds drunk → raises camera → lands a graded shot → wants the next one.
- **Explicitly OUT of slice:** reputation economy, other events, ambient NPCs, stealth, missions/mystery, home base (print/video-edit/decorate), day-night cycle, multiple districts.
- **Next:** Decide process for remaining full-vision sections (facilitate each vs. batch-draft + review).

## 2026-05-26 — Process choice + full-vision sections batch-drafted

- **Process DECIDED:** user chose "batch-draft the rest, I'll review" (over one-at-a-time / skip-to-epics).
- **Batch-drafted from locked decisions + brain-dump** (pending user review): Target Audience, USPs, Project Goals, Background; all Adventure-Specific elements (exploration/story/puzzle/character/inventory/env-storytelling); Progression & Balance (reputation as primary currency, scarcity-not-punishment difficulty, no hard consumables in core); Level Design (one open town zoned into districts, home-base hub, districts unlock by reputation); Art (stylised low-poly, Schedule-1, silhouette readability, 2000s digicam UI) & Audio (directional cues gameplay-critical); Technical (60fps/1080p PC target, URP-only, cap concurrent event actors, Blender-cam-disabled constraint); Asset Requirements (have: 5 chars + walks + Drunk Pack + map.blend; need: map FBX export, husband Animator, viewfinder UI, SFX/cues; later: per-event anims); Success Metrics; Out of Scope (slice-level + foreseeable-project deferrals).
- **Assumptions made for user to confirm/correct:** district list (downtown/docks/residential/industrial-oil-rig/mountain); reputation = currency; deferred full ~18-char roster, video-editing minigame, online sharing, police/criminal deep branch, multiplayer, mobile.
- **Next (capstone):** Development Epics — derive epic list from slice → layers; Epic 1 = the First Playable Slice. Then validation pass.

## 2026-05-26 — Development Epics locked + validation pass + GDD complete

- **Epics CONFIRMED (8, dependency-sequenced):** E1 First Playable Slice (Catch Loop MVP) → E2 The Living Town → E3 Event Content Pack #1 → E4 Progression & Reputation → E5 Home Base → E6 Nature & Collection → E7 Missions & Mystery (optional) → E8 Stealth. Rule: don't start a later epic until E1 proves the loop. Detailed stories deferred to `gds-create-epics-and-stories` (epics.md).
- **Validation pass run** against `gdd-validation-checklist.md`. Result: strong. Traceability (Q-3) intact; core gameplay concrete (Q-4); Out-of-Scope explicit (Q-5); Adventure genre/game-type sections present (G-1/G-2); template complete, no _TBD_ left (S-4).
  - **Fixed — Q-2 Measurability (medium):** added "Initial tuning targets" (grade scale, capture gate ≥8%, composition 25–50%, timing ±0.5/±2s, zoom 1×–4×) + drunk timings (lifecycle 20–30s, peak ~1.5s, cue radius ~25m).
  - **Fixed — D-2 Input fidelity (medium):** added explicit `[ASSUMPTION]` that v1 core is still-photography while the "video maker"/editing fantasy is deferred (not cut).
  - **Open warnings (low, acceptable):** D-1 mild engine terms (NavMesh/Animator/colliders) in Technical/Asset/Epic sections — acceptable per checklist's Technical-Specs allowance; S-1 minor hyphenation drift ("NPC event actor"); S-2 epics.md not yet generated (expected — produced by the CE step).
- **STATUS: GDD marked `complete`.** Ready for downstream: narrative (flagged) → architecture → epics/stories → readiness.
- **Recommended next:** `gds-create-narrative` (needs_narrative flag set) OR `gds-game-architecture` (required). Then `gds-create-epics-and-stories`, `gds-check-implementation-readiness`.
