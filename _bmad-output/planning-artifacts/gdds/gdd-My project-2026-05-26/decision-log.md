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

## 2026-11-10 — Core verb: video replaces photo (Correct Course)

- **Trigger:** stakeholder feedback after watching a verification-rig recording of the slice. They want video rather than photos: pull out the camera and record the gameplay "like a screen recording", save the clips to a memory card, and in a second phase edit the clips into videos. Epic 1 stood at 12/13 stories done. Full analysis: `../../sprint-change-proposal-2026-11-10.md` (16 of 16 edit proposals approved individually).
- **Video REPLACES photo** (Alexv chose this over "camcorder with a still button" and "ask the stakeholder first"). Recording becomes the core verb, and photo mode stops being a separate player action. The photo grader (Stories 1.9–1.10) becomes the per-frame engine underneath clip grading. This resolves the GDD's photo-first `[ASSUMPTION]`, which had predicted exactly this revisit.
- **Memory card = LIMITED capacity** (over "storage metaphor only" and "upgradeable capacity"). Capacity is measured in seconds of footage (~2 min starting value, tunable). A full card stops recording, and deleting a clip frees its space. It also bounds memory, which raw video needs: a 1080p frame is ≈8.3 MB.
- **Every clip is GRADED** (over "no grading" and "grade only the final edit"). **Peak coverage** replaces Timing; the per-frame Subject gate and Composition are unchanged. This keeps Pillar 2 and gives Epic 4's reputation something to measure.
- **Path: Hybrid** (over "close Epic 1 as a photo slice" and "hold everything until the spike"). Camcorder Stories 1.14–1.18 are added *inside* Epic 1, because the loop must be proven with the real verb before later epics start. The MVP's verb is redefined but its scope is not, and nothing is rolled back.
- **The Edit Suite = new Epic 9, sequenced immediately after Epic 1** (over "after Epic 3" and "fold into Epic 5"). It is promoted from the deferred "full video-editing minigame". It is numbered 9 so existing epic numbers and Jira keys stay stable. Export to a real video file (FR31) is left **OPEN** pending the stakeholder.
- **Story 1.13 SUPERSEDED** (over "implement anyway" and "delete it"). Its intent — a well-framed but mistimed attempt is worth something — and Alexv's quote are carried into Story 1.16.
- **Technical constraint logged:** the stakeholder's reference video was made with Unity Recorder, which is Editor-only, so it cannot be how a built game records itself. Story 1.14 is a spike that decides the approach by measurement (ADR-4) and proves it **in a Windows build** before any recording story starts.
- **Open with the stakeholder:** FR31 (export a file?); confirm that Record is a toggle, that clips include game sound, and the ~2 min card.
- **Next:** `gds-create-story` for 1.14; Jira sync once the Atlassian MCP is re-authenticated.

## 2026-11-10 — Stakeholder answers; Jira dropped

- **FR31 export CONFIRMED.** A finished edit must be exportable as a standard video file. This binds ADR-4: whichever recording approach Story 1.14 picks, it must come with a working export path. If replay or raw frames win for live recording, an encoder is still needed at export time, so the encoder licence question stays on the table either way.
- **Story assumptions CONFIRMED:** Record is a toggle (press to start, press again to stop), clips include the game's sound, and the memory card starts at ~2 minutes of footage.
- **Jira removed from the project** (Alexv). The planning artifacts are no longer mirrored to Jira project KAN. Removed: the "Jira Sync" section of `CLAUDE.md` and `AGENTS.md`, the Jira half of the post-edit reminder hook (its C# compile reminder is kept), the project-scope `atlassian` MCP server (`.mcp.json`, `.codex/config.toml`) and its permissions, the `jiraSync:` block in `epics.md`, and the KAN comments in `sprint-status.yaml`. The files in `_bmad-output/` are now the only tracker. The old KAN keys remain in git history (commit `1e3ab88`). This supersedes the "Jira sync once re-authenticated" next step above.
- **Next:** `gds-create-story` for 1.14.

## 2026-11-10 — The unattended loop (`loop/`)

Alexv asked for a loop that builds every story without him, analyses the game with pictures, video
and Gemini, can use Chrome, and only involves him for real blockers, skipping to independent work
meanwhile. His choices:

- **Scope: all epics.** This **overrides the GDD rule** "Do not start a later epic until Epic 1 has
  proven the loop is fun", whose proof is an uninstructed first-time-player playtest that only a human
  can run. The loop plans later epics just-in-time. In `loop/dependencies.yaml`, Epic 2 no longer waits
  for Epic 1, because that dependency existed only to enforce this rule.
- **Decisions reserved for him: decide, flag, keep going.** The loop chooses from evidence, records the
  choice and its cost to reverse in `loop/runtime/human-queue.md`, and continues. He may veto later.
- **Perceptual checks: batched.** These are never self-certified; dependent stories may build on them.
- **Permissions: none** (`--dangerously-skip-permissions`). It was recommended against, and he chose it.
  Mitigation: a PreToolUse guard hook for irreversible or outward-facing actions only.
- **Git:** one branch per unit, merged to master when it passes, with a tag before each unit.
- **Notifications:** ntfy phone push, one line per blocker or full stop; the topic stays off the public repo.
- **A supervised pilot on Story 1.14 first,** then it runs until done, waiting out usage limits.
- **Cross-review by OpenAI Codex**, read-only, as the "different LLM" CLAUDE.md recommends for code review.
