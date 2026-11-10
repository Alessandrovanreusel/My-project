---
title: "Sprint Change Proposal — Video Replaces Photo"
date: 2026-11-10
author: Alexv (facilitated by gds-correct-course)
trigger: stakeholder feedback
scope: Moderate
mode: Incremental — 16 of 16 edit proposals reviewed and approved individually
status: approved 2026-11-10 by Alexv — edits applied to all planning artifacts; stakeholder questions answered; Jira dropped from the project the same day
affects:
  - _bmad-output/planning-artifacts/epics.md
  - _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/gdd.md
  - _bmad-output/planning-artifacts/gdds/gdd-My project-2026-05-26/decision-log.md
  - _bmad-output/planning-artifacts/game-architecture.md
  - _bmad-output/implementation-artifacts/sprint-status.yaml
  - _bmad-output/implementation-artifacts/1-13-soften-the-timing-multiplier.md
---

> **Update, same day (2026-11-10, after approval):** the stakeholder answered both open questions (yes and yes —
> see §3), and **Jira was removed from the project**. Everything below that talks about syncing to Jira describes
> the plan as it stood when it was approved. The files in `_bmad-output/` are now the only tracker. See
> *Implementation notes* at the end.

# Sprint Change Proposal — Video Replaces Photo

## 1. Issue Summary

### Problem statement

The stakeholder has asked for the game's core verb to change from **taking photographs** to **recording
video clips**. The player should still raise their camera during play, but what it produces should be
footage of the gameplay, saved to a **memory card**. As a second phase, the player should be able to
**edit those clips into videos**.

> **Original (French):** *« Après alors le truc c'est qu'en fait on aimerait faire un baille avec des vidéos
> plus que des photos, genre y a un moyen pour que dans le gameplay tu puisses en gros sortir ta "caméra"
> mais qu'en gros ce soit plus genre un screen recording du gameplay ? Genre littéralement un peu la vidéo
> que tu viens de m'envoyer, et puis genre ça l'enregistre dans une memory card et en gros le but dans un
> deuxième temps c'est de pouvoir faire en gros des vidéos édits des différents clips que quelqu'un a pu
> faire dans le jeu. »*
>
> **Translation:** "We'd like to do something with videos rather than photos. Is there a way, during
> gameplay, to pull out your 'camera' but have it work more like a screen recording of the gameplay?
> Literally a bit like the video you just sent me. It would save to a memory card, and the goal, in a
> second phase, is to be able to make edited videos from the different clips someone has made in the game."

**Category:** a new requirement from a stakeholder that changes the game's core verb. Nothing broke. No
story failed, and the photo slice works as specified.

### Context of discovery

- It was raised after the stakeholder watched a verification-rig recording of the slice. The rigs are
  filmed with Unity Recorder through `RigVideoFeed`.
- At the time, Epic 1 had **12 of 13 stories done**, and Story 1.13 (soften the timing multiplier) was
  `ready-for-dev`.
- **The GDD predicted this decision point.** Its Assumptions section (line 409) recorded *"[ASSUMPTION]
  Photo-first core vs. 'video maker' fantasy … If the developer wants video at the core instead, the
  camera mechanic and grading model would need revisiting before Epic 1."*

### Evidence

1. **The stakeholder's own words** (above).
2. **The GDD's identity was already video.** The Core Concept casts the player as *"a small-town
   videographer"*, the win state is *"the biggest video maker in town"*, and USP 5 promised *"(later: edit
   into shorts)"*. Only the mechanic was photographic. This change brings the mechanic in line with the
   original concept.
3. **The video the stakeholder saw cannot come out of a shipped game as-is.** It was made with Unity
   Recorder (`com.unity.recorder` 5.1.7), and Recorder is **Editor-only**. `Assets/Scripts/Core/RigVideoFeed.cs`
   exists because runtime code cannot reference it. A built game needs a different way to record itself,
   and that is the largest unknown in this change.
4. **Memory needs are much larger than for photos.** A raw 1080p frame is 1920 × 1080 × 4 B ≈ 8.3 MB, so
   ten seconds at 30 fps is ≈ 2.5 GB. The memory card's limit is therefore a technical requirement as well
   as a design feature.
5. **The grading engine can be reused.** `ShotGrader.Grade(camera, subject, config, out detail)` is a pure
   static function over a camera, an `ISubject` and a config. `PhotoModeController` calls it today
   (`PhotoModeController.cs:552`), and it can be sampled on frames while recording.

## 2. Impact Analysis

### Epic impact

| Epic | Impact |
|---|---|
| **1 — First Playable Slice** (in progress) | **Goal unchanged** (prove the core loop is fun); **verb changed**. The loop has not been proven with recording, so the camcorder work goes **inside** Epic 1 as Stories 1.14–1.18. That follows the GDD rule: *"Do not start a later epic until Epic 1 has proven the loop is fun."* |
| **9 — The Edit Suite** (NEW) | The stakeholder's "second phase". It is promoted from the GDD's deferred *"full video-editing minigame"*. It is **sequenced immediately after Epic 1** and numbered 9 only so the existing epic numbers and Jira keys stay stable. |
| 2 — The Living Town | No change. |
| 3 — Event Content Pack #1 | No change. Events are data and are recorded like any other subject. |
| 4 — Progression & Reputation | Wording only: reputation is earned from graded **clips**. The seed's listener channel becomes `ClipRecordedChannel`. |
| 5 — Home Base | Wording only: review clips, **print stills pulled from clips**, decorate. The editing room moves out to Epic 9; once Epic 9 exists, the home base can host it. |
| 6 — Nature & Collection | No change. Video, if anything, suits lightning strikes better than photos do. |
| 7 — Missions & Mystery | Wording only: **clip** bounties, **footage** as evidence. |
| 8 — Stealth Layer | No change. |

No epic becomes obsolete. One epic is added.

### Story impact (Epic 1)

| Story | Status | Impact |
|---|---|---|
| 1.1 Scaffold · 1.2 Town patch · 1.3 Raise camera · 1.4 Zoom · 1.6 Event engine · 1.7 Town Drunk · 1.8 Audio cue | done | **Unchanged, fully reused.** |
| 1.5 Capture a photo | done | The shutter becomes REC start/stop (Story 1.15). The input action and feedback plumbing are reused. |
| 1.9 Subject gate · 1.10 Composition & timing | done | `ShotGrader` becomes the **per-frame engine** of clip grading (Story 1.16). "Timing" is replaced by **peak coverage** at the clip level. |
| 1.11 Minimal gallery | done | Evolves into the **memory card** (Story 1.17). The storage, eviction and single-owner native-memory rules carry over. |
| 1.12 Grade HUD | done | Reused. It shows the clip's grade when recording stops (Story 1.16). |
| **1.13 Soften the timing multiplier** | ready-for-dev → **superseded** | It changes the *photo* formula, which players no longer see. Its intent, and Alexv's quote, carry into Story 1.16. |
| **1.14–1.18** | NEW | Spike → record → grade → memory card → watch. |

### Artifact conflicts

- **GDD:** the game type; Core Concept; USPs 1/3/5; Pillar 2 body; Pillar 3 name; Catch Loop and Session Loop
  steps; the First Playable Slice; Mechanics §1/§2/§4/§5; Controls; Inventory; Puzzles/Characters;
  Progression/Economy; Level types; Art/Audio; Technical; the epics table; Success Metrics; Out of Scope; and
  the line-409 assumption. Also the decision log.
- **Architecture:** a new Clip Recording system and data model, a new channel, ADR-4 (open until Story 1.14),
  the project structure, the epic mapping, and two decision-table rows.
- **UX:** no UX document exists (N/A). The viewfinder changes (REC dot, running timecode, card time
  remaining, CARD FULL) are captured in the stories' acceptance criteria.
- **Narrative:** no narrative document exists (N/A).
- **Other:** `sprint-status.yaml`, the Story 1.13 file, the `jiraSync` block, and Jira itself.
  `CLAUDE.md` does not describe the game's mechanic, so it needs no change.

### Technical impact

- **Code (for later stories, not this proposal):** a new `Assets/Scripts/Recording/` folder.
  `PhotoModeController`'s Capture handler starts and stops recording. `GalleryService` evolves into the memory
  card. `GradeHud` listens for finished clips. `ShotGrader` is **unchanged**.
- **Tests:** the 163 EditMode tests remain valid for per-frame grading. The photo-capture and gallery tests
  will be reworked by Stories 1.15 and 1.17.
- **Verification rigs:** `PhotoShootRunner`, `GalleryShootRunner` and `GradeHudShootRunner` drive the photo
  path, so the clip stories need their own rigs. CLAUDE.md's verification rules apply unchanged.
  Recorded clips are well suited to `tools/verification/` frame analysis.
- **Builds:** Story 1.14 is the **first story in this project that must be proven in a Windows build**, not
  only in the Editor, because the tool that works in the Editor (Recorder) is the one that cannot ship.
- **Performance:** recording adds a new per-frame cost against NFR1 (60 FPS at 1080p).
- **Licensing (only if file encoding is chosen):** FFmpeg builds that include an H.264 encoder such as libx264
  are GPL. The alternatives are Windows Media Foundation or a paid capture plugin. Story 1.14 checks this.

## 3. Recommended Approach

**Selected: Hybrid — Direct Adjustment (Option 1) + MVP redefinition (Option 3).**

| Option | Verdict | Effort | Risk |
|---|---|---|---|
| 1. Direct adjustment: new stories inside Epic 1, plus a new Epic 9 | ✅ Viable — selected | High | Medium–High |
| 2. Rollback of the photo code | ❌ Not viable. The photo code *is* the foundation (grader, storage, HUD, input). Reverting it removes what the camcorder needs and simplifies nothing. | — | — |
| 3. MVP review | ✅ Selected as part of the hybrid. The slice keeps its scope (one event, one patch of town); only its verb changes. Editing stays **out** of the MVP. | — | — |

**Rationale**

- **The slice must be proven with the verb the game will actually have.** If Epic 1 closed as a photo slice,
  it would prove the fun of a mechanic the game no longer contains.
- **Nothing is thrown away.** Stories 1.1–1.12 are the foundation. The highest-risk system in the photo
  version, grading, is reused as-is, frame by frame.
- **The biggest unknown is isolated in one story.** Story 1.14 answers "how does a built game record
  itself?" by measuring, before any recording story commits to an answer. This is the same "decide by
  measuring" pattern that settled prominence in Story 1.10.
- **Epic numbers and Jira keys stay stable.** That is why the Edit Suite is numbered 9 and sequenced second.

**Effort, risk, timeline**

- **Effort: High.** There are five new stories, roughly comparable in size to Stories 1.9–1.12. Epic 1's exit
  moves from "one story left" to "five stories left".
- **Risk: Medium–High, concentrated in Story 1.14.** Once the recording approach is proven in a build, Stories
  1.15–1.18 are conventional work on systems that already exist.
- **Timeline:** Epic 9 is inserted ahead of Epic 2.

**Alternatives considered and rejected (Alexv's calls during this session)**

- *Camcorder with a still button* (keep photo and video): rejected. It means two capture actions, two
  grading paths, and a gallery holding two kinds of item.
- *Memory card as a storage metaphor only*, or *upgradeable capacity*: rejected in favour of a fixed,
  limited capacity. It makes every recording a decision, and it bounds memory, which video needs anyway.
- *No clip grading*, or *grade only the final edit*: rejected. It would remove Pillar 2 and leave Epic 4's
  reputation with nothing to measure.
- *Close Epic 1 as a photo slice*, or *hold everything until the spike*: rejected (see Rationale).
- *Edit Suite after Epic 3*, or *folded into Epic 5*: rejected. Recording → editing is the stakeholder's
  stated arc, and Epic 5 depends on Epic 4.
- *Implement 1.13 anyway*, or *delete it*: rejected in favour of superseding it and keeping its intent.

**Questions for the stakeholder — ANSWERED 2026-11-10, both yes**

1. ✅ **FR31: should a finished edit be able to leave the game as a real video file** (for example, to post
   online)? **Yes.** FR31 is confirmed. This affects ADR-4: some encoding is needed, at least at export time, so
   Story 1.14 must show a working export path for whichever approach it picks.
2. ✅ **Assumptions made in the stories:** Record is a **toggle** (press to start, press to stop), like a real
   camcorder, rather than hold-to-record. Clips **include the game's sound**. The memory card starts at **about
   2 minutes** of footage, and this is tunable. **Yes**, all confirmed.

## 4. Detailed Change Proposals

Every revised passage carries a dated *"Revised 2026-11-10"* note, following the project's existing
convention (see the FR5/FR6 revision notes), so the photo-era history stays readable.

### 4.1 Stories and requirements — `epics.md`

#### P1 — Functional requirements FR1–FR8, plus new FR27 and FR28 (approved)

| FR | OLD | NEW |
|---|---|---|
| FR1 | two camera states — **Walk** (exploration) and **Photo** (first-person viewfinder) | two camera states — **Walk** (exploration) and **Viewfinder** (first-person through the camcorder). The code name `PhotoMode` / `PhotoModeController` is unchanged. |
| FR2 | In Photo state the player can aim freely and zoom… | In **Viewfinder** state… (rename only) |
| FR3 | The player can **capture** a photo with a click (LMB / RT) while in Photo state; capture is a no-op in Walk state. | The player can **start and stop recording a clip** (LMB / RT) while in Viewfinder state. Lowering the camera or a full memory card also stops recording, and the clip so far is kept. Record is a no-op in Walk state. |
| FR4 | A captured shot is **graded** on three dimensions — Subject Capture, Composition, and Timing — blended into a 1–5 star (0–100%) grade. | A recorded clip is **graded** by sampling the per-frame shot grader (**Subject Capture** gate + **Composition**) throughout the recording, plus **Peak coverage**, blended into a 1–5 star (0–100%) grade. |
| FR5 | (subject-capture gate, as revised 2026-07-28) | Unchanged, plus: *evaluated on every sampled frame of a clip; a clip in which the subject never passes the gate is a miss with a reason.* |
| FR6 | (composition, as revised 2026-07-27) | Unchanged, plus: *evaluated on every sampled frame of a clip.* |
| FR7 | **Timing scoring**: full marks within ±0.5 s of the peak window, decaying to zero by ±2 s beyond it. | **Peak coverage**: full marks when the clip contains the event's whole **peak window** with the subject framed; partial credit for partial coverage. A well-framed clip that missed the peak still scores above zero via a designer-set floor, but far below one that caught it. *(The photo-era Timing rule is kept as a history note.)* |
| FR8 | Captured shots are **saved to a gallery** with their grade and subject identity. | Recorded clips are **saved to the memory card** with their grade, subject identity, time, duration and a thumbnail. |
| **FR27** | — | **Memory card capacity**: the card holds a limited amount of footage (designer-tunable, in seconds). The viewfinder shows the time remaining. When the card fills, recording stops (the clip so far is kept) and the viewfinder shows **CARD FULL**. Deleting a clip frees its space. |
| **FR28** | — | **Clip playback**: the player can watch any clip on the memory card in-game, with the game sound it recorded, and delete it from there. |

#### P2 — NFRs and additional requirements (approved)

- **NFR1:** add *"…with the active event running, **including while recording**."*
- **NFR2:** "capture-to-feedback < 0.2 s" becomes **"record-start-to-feedback (REC indicator + beep) < 0.2 s"**.
- **NFR3:** add *"Recorded footage is bounded by the memory card's capacity — no unbounded buffer; deleting a
  clip frees its memory."*
- **NFR10:** "a high-grade from a low-grade **shot**" becomes "…**clip**".
- **AR4:** add `ClipRecordedChannel` (record → memory card → HUD → future reputation).
- **AR6:** "In-memory `List<CapturedShot>` via `GalleryService`, disk-ready seam for Epic 5" becomes **"Memory
  card: an in-memory store of `CapturedClip` capped in seconds of footage (evolves `GalleryService`), with a
  disk-ready seam. Clips surviving between sessions lands no later than Epic 9's first story."**
- **AR12 (new):** *The clip recording approach is decided by measurement in Story 1.14 and recorded as ADR-4
  before any recording story starts. It must work in a Windows **build**: Unity Recorder is Editor-only and
  cannot ship.*
- **Epic 1 foundation:** AR1–AR8, AR10, AR11 → plus **AR12**.
- **UX Design Requirements:** add the REC indicator, running timecode, card time remaining and CARD FULL to
  the viewfinder items.

#### P3 — Epic 1 framing (approved)

- **OLD:** *"…raises the camera, composes and captures a graded shot, sees it banked in their gallery, and
  wants to wait for the next one. … building the reusable camera, grading, and event-actor systems."*
  FRs covered: FR1–FR13.
- **NEW:** *"…raises the camcorder, records him through his stagger, sees a graded clip land on their memory
  card, watches it back, and wants to wait for the next one. … building the reusable camera, recording,
  grading, and event-actor systems."* FRs covered: FR1–FR13, **FR27, FR28**.
- **Added note:** *"Revised 2026-11-10: the core verb changed from still photography to video at the
  stakeholder's request. Stories 1.1–1.12 built the photo version; their systems are the foundation the
  camcorder stories 1.14–1.18 build on — none is rolled back."*
- **FR Coverage Map:** FR27, FR28 → Epic 1; FR29–FR31 → Epic 9; "All 26 FRs" → "All 31 FRs (FR31 open)".

#### P4 — Story 1.13 superseded (approved)

- **epics.md heading:** `### Story 1.13: Soften the timing multiplier — ⛔ SUPERSEDED 2026-11-10`, plus:
  *"Not to be implemented as written: it changes the photo grade, which players no longer see once video
  replaces photo. Its intent — a well-framed but mistimed attempt is worth something, timing stays dominant —
  is carried into Story 1.16's acceptance criteria, with Alexv's original quote."*
- **Story file** `1-13-soften-the-timing-multiplier.md`: status `ready-for-dev` → `superseded`, with the same
  note.
- **sprint-status.yaml:** the line is **removed** and a comment is left in its place. The schema has no
  `superseded` status, and leaving it as `backlog` would stop `epic-1` from ever reaching `done`.
- **jiraSync:** `{ Story 1.13, key: PENDING }` → `key: NONE`. It was superseded before it was ever synced, so
  it is intentionally not created in Jira.

#### P5 — NEW Story 1.14: Decide how the game records video (approved)

> **Story 1.14: Decide how the game records video** *(technical spike — gates Stories 1.15–1.18)*
>
> As a developer,
> I want to know, by measuring rather than guessing, how a **built** copy of the game can record its own gameplay,
> So that the camcorder stories build on an approach that actually works outside the Editor.
>
> **Acceptance Criteria:**
>
> **Given** Unity Recorder is Editor-only and cannot ship in a build,
> **When** recording approaches are compared — at minimum **(A)** encoding a video file while playing, **(B)**
> replay recording (saving what happened and re-rendering it), and **(C)** keeping raw frames in memory —
> **Then** each is prototyped against the Town Drunk in the real slice scene and measured for: frame rate at
> 1080p while recording (NFR1), memory and disk per second of footage, whether the game's sound is captured,
> playback quality, and licence/cost.
>
> **Given** the measurements,
> **When** an approach is chosen,
> **Then** it is recorded as **ADR-4** in the architecture with the evidence behind it, including how the
> Edit Suite (Epic 9) would trim and join clips on top of it and how (if ever) a clip leaves the game as a
> standard video file (FR31)
> **And** Alexv approves the choice before Story 1.15 starts.
>
> **Given** the chosen approach,
> **When** it is proven,
> **Then** a ~10-second clip of the drunk is recorded **and** played back **in a Windows build**, not only in
> the Editor, where Unity Recorder would mask the problem
> **And** the throwaway prototype code is removed or clearly fenced off from game code.

#### P6 — NEW Story 1.15: Record a clip (approved)

> **Story 1.15: Record a clip**
>
> As a player,
> I want to press a button to start and stop recording while my camera is raised,
> So that I can film the moment as it happens instead of freezing one instant.
>
> **Acceptance Criteria:**
>
> **Given** I am in Viewfinder state,
> **When** I press Record (LMB / gamepad RT),
> **Then** recording starts within **< 0.2 s**, with a REC indicator, a running timecode and a start beep in a
> 2000s-camcorder style (FR3, NFR2)
> **And** pressing Record again stops it, with a stop beep.
>
> **Given** I am recording,
> **When** I lower the camera (release RaiseCamera),
> **Then** recording stops and the clip is kept. Lowering never throws footage away.
>
> **Given** I am recording with the drunk event active,
> **When** I record for the length of a full event lifecycle (~30 s),
> **Then** the game holds **60 FPS at 1080p** (NFR1), and aim and zoom keep working while recording (FR2).
>
> **Given** the existing Capture action (Story 1.5),
> **When** recording is wired,
> **Then** it reuses that action (LMB / RT), which is a no-op in Walk state
> **And** each finished clip is announced on a `ClipRecordedChannel`, so the memory card and HUD can listen
> without the recorder knowing about them (AR4).

#### P7 — NEW Story 1.16: Grade a clip (approved)

> **Story 1.16: Grade a clip**
>
> As a player,
> I want each clip scored on how well I kept the subject framed and whether I caught the peak,
> So that filming the moment is still a skill, not just pressing record and walking away.
>
> **Acceptance Criteria:**
>
> **Given** a clip is being recorded,
> **When** frames are rendered,
> **Then** the existing per-frame grader (`ShotGrader`, Stories 1.9–1.10) is sampled throughout the
> recording, with its subject gate and composition unchanged and not re-implemented (FR4–FR6).
>
> **Given** a finished clip,
> **When** it is graded,
> **Then** its grade rewards **peak coverage** (how much of the event's peak window the clip contains with
> the subject framed) and how well the subject was framed while recorded, blended into a 0–100% → 1–5★
> `ClipGrade` with a per-axis breakdown (FR4, FR7)
> **And** the blend, every threshold, and whether footage without the subject lowers the grade live in
> `GradingConfig` and are decided by measuring recorded clips, as Story 1.10 decided prominence.
>
> **Given** a well-framed clip that missed the peak,
> **When** it is graded,
> **Then** it still scores above zero. This is Alexv's Story 1.12 feedback, carried over from superseded
> Story 1.13: *"it should not be a big deal the timing for me. I think it's too extreme to give 0"*
> **And** a clip that caught the peak with equal framing still wins by a wide margin.
>
> **Given** a clip in which the subject never passes the gate,
> **When** it is graded,
> **Then** it is a miss with a reason (no subject / too small / occluded)
> **And** the grade HUD (Story 1.12) shows the clip's stars and breakdown when recording stops (NFR10).

#### P8 — NEW Story 1.17: The memory card (approved)

> **Story 1.17: The memory card**
>
> As a player,
> I want my clips saved on a memory card that can fill up,
> So that every recording is a decision and the footage I keep is footage I chose.
>
> **Acceptance Criteria:**
>
> **Given** a clip finishes recording,
> **When** the memory card receives it via `ClipRecordedChannel`,
> **Then** a `CapturedClip` (footage, grade, subject id, time, duration, thumbnail) is stored, evolving the
> photo gallery's storage from Story 1.11 (FR8, AR6).
>
> **Given** the card's capacity, measured in **seconds of footage** in a designer-tunable config (starting
> value ~2 minutes),
> **When** I am in Viewfinder state,
> **Then** the viewfinder shows the time remaining
> **And** when the card fills mid-recording, recording stops, the clip so far is kept, and the viewfinder
> shows **CARD FULL** (FR27).
>
> **Given** the memory-card view (reworked from Story 1.11's gallery view),
> **When** I open it,
> **Then** I see each clip's thumbnail, stars and duration, and I can delete one: its seconds are freed
> immediately and its memory is released, with no leak over an extended session (NFR3).
>
> **Given** the in-memory model,
> **When** it is designed,
> **Then** it keeps a disk-ready seam in the format ADR-4 chose, so clips can survive between sessions in
> Epic 9 (AR6).

#### P9 — NEW Story 1.18: Watch your clips (approved)

> **Story 1.18: Watch your clips**
>
> As a player,
> I want to play back any clip on my memory card,
> So that I can relive the catch and judge which footage is worth keeping.
>
> **Acceptance Criteria:**
>
> **Given** clips on the memory card,
> **When** I select one in the memory-card view,
> **Then** it plays back in-game exactly as I recorded it (same framing, zoom and timing), with the game
> sound that was playing (FR28).
>
> **Given** playback is running,
> **When** I watch,
> **Then** I can pause, skip and close it; the camera and player input are suppressed while I watch (the
> existing suppression seam); and closing returns me exactly where I was
> **And** watching never alters the live game: no event is skipped and no actor is teleported.
>
> **Given** a recorded clip of the drunk's stagger,
> **When** Alexv watches it back,
> **Then** he confirms it reads as "I caught it". This is a **human** check and stays open until he has done
> it. (The uninstructed first-time-player playtest of the whole slice remains Epic 1's exit check.)

### 4.2 Epics — `epics.md` and the GDD epics table

#### P10 — NEW Epic 9: The Edit Suite (approved)

> **Epic 9: The Edit Suite**
> This completes the "from capture to creation" arc the stakeholder asked for. The player picks clips from
> the memory card, trims them, arranges them on a timeline, and plays the result back as one short video
> they made. It starts as a menu screen; Epic 5 can give it a physical editing room in the home base later.
> **FRs covered:** FR29, FR30, FR31 (FR31 open — confirm with the stakeholder)
> **Depends on:** Epic 1 (clips, and the ADR-4 recording format)
> **Sequenced:** immediately after Epic 1, ahead of Epic 2. It is numbered 9 only so existing epic numbers
> and Jira keys stay stable.

- **FR29 — Clip editing:** the player can choose clips from the memory card, trim each one, arrange them in
  order on a timeline, and preview the edit as one video.
- **FR30 — Saved edits:** finished edits are saved and can be re-watched and re-opened for editing.
- **FR31 — [OPEN] Export:** a finished edit can leave the game as a standard video file playable outside it.
  Online sharing remains deferred.
- **Seeds:** the first story makes clips survive between sessions (cashing in the AR6 disk seam). After that:
  trim → timeline → playback of the edit → save/re-open. Later candidates: a music bed, title cards,
  transitions, and export (if FR31 is confirmed). ADR-4 decides how trimming and joining work (re-render
  versus re-encode).

#### P11 — Later epics, wording only (approved)

- **Epic 4 / FR17:** "Banking a graded **shot** earns Reputation" → "…a graded **clip**…". Seed:
  `ReputationSystem` subscribes to `ShotCapturedChannel` → `ClipRecordedChannel`.
- **Epic 5 / FR21:** "review the gallery, **print** photos, and **decorate** the house with them" → "review
  clips on the memory card, **print stills** pulled from clips, and **decorate** the house with them — and,
  once Epic 9 exists, give the Edit Suite a physical room". Seed: "`GalleryService`'s disk seam is cashed in
  here" → "the memory card's disk seam is cashed in by Epic 9; Epic 5 reuses it for print/decorate".
- **Epic 7 / FR24:** "photo-bounty objectives ('capture X doing Y') … photo-evidence" → "clip-bounty
  objectives ('record X doing Y') … footage as evidence". Seed: `MissionService` listens to clip events.
- **Epics 2, 3, 6, 8:** no change.
- **"Epics 2–8: Detailed Just-In-Time"** → "Epics 2–9", noting that Epic 9 is sequenced first among them.

### 4.3 GDD — `gdd.md` and `decision-log.md`

#### P12 — Identity: concept, USPs, pillars, loop (approved)

- **Frontmatter:** `game_type: Adventure (photography / exploration)` → `(videography / exploration)`;
  `updated: 2026-11-10`. The same change applies to the "Game Type" line, and a revision note is added under
  Executive Summary.
- **Core Concept:** "getting the shot before it's gone" → "rolling while it happens, before it's gone".
  "framing it, and shooting at the peak" → "framing it, and keeping it framed while you record through the
  peak". Added: "…and becomes raw footage for the videos you cut." In the supporting layers, "a home base
  where photos are reviewed/printed and videos edited" → "an edit suite where clips are cut into videos, and
  a home base where they're reviewed and stills printed".
- **USP 1:** "Photography is the verb … you shoot with a camera, not a gun" → "**Filming** is the verb … you
  shoot with a **camcorder**, not a gun".
- **USP 3:** "Skill-graded shots — composition + timing turn every photo into a score and a trophy" →
  "Skill-graded **clips** — framing + catching the peak turn every clip into a score and a trophy".
- **USP 5:** "photos become physical trophies you print and decorate with (later: edit into shorts)" →
  "clips become raw footage you cut into your own videos in the Edit Suite (and stills you print to decorate
  your home)".
- **Pillar 2:** the name is **kept** ("The Shot Is the Skill"; in film language a *shot* is one continuous
  take). The body "Taking a photo is an active skill … timing the click to the peak" becomes "Recording is an
  active skill … keeping the subject in frame, composing with zoom, and rolling through the peak — the game
  grades the clip".
- **Pillar 3:** "Every **Photo** Is a Trophy" → "Every **Clip** Is a Trophy" (a memory card of footage, a
  reputation, a score, and the videos you cut from it).
- **Catch Loop:** step 4 "**Shoot** — click at the peak" → "**Roll** — record through the peak". Step 5 grades
  the clip on subject, framing and how much of the peak it caught. Step 6 "the photo enters the gallery" →
  "the clip lands on the memory card".
- **Session Loop:** "review the gallery, print/decorate" → "watch your clips and cut them into videos,
  print stills/decorate".

#### P13 — First Playable Slice, Mechanics, Controls (approved)

- **Slice fixed core:** "Camera photo-mode … capture (LMB)" → "**Camcorder:** raise (hold RMB) → viewfinder →
  zoom → record (LMB start/stop)". "Grading" → "**Clip grading:** per-frame Subject gate + Composition, plus
  Peak coverage". "Minimal Gallery" → "**Memory card:** limited capacity; clips saved with grades and
  watchable in-game".
- **Slice success criterion:** "…raises the camera → lands a satisfying graded shot in their gallery" →
  "…raises the camcorder → records him through the stagger → sees a satisfying graded clip land on the
  memory card, watches it back → and wants to wait for the next one".
- **Not in slice:** add "the Edit Suite (Epic 9)".
- **Mechanics §1:** "Photo state" → "Viewfinder state (the code calls it Photo mode)". Releasing the camera
  also stops any recording.
- **Mechanics §2:** rewritten as "The recording action": press record to start and again to stop; lowering
  the camera or a full card also stops it; footage is always kept; each clip is graded and saved.
- **Mechanics §4:** becomes the "Clip grading model": a per-frame Subject gate, per-frame Composition, and
  Peak coverage with a floor. A revision note preserves the photo-era Timing model.
- **Mechanics §5:** adds **Peak coverage** (full marks = the whole 1.5 s peak recorded and framed; the floor
  is set by measuring in Story 1.16) and **Memory card** (~2 minutes to start, tunable). "capture-to-feedback"
  → "record-start-to-feedback (REC + beep) < 0.2 s". The Timing bullet is kept as history.
- **Controls:** "Raise camera / Photo mode" → "Raise camera / Viewfinder". "Capture | LMB | RT | Only in
  photo state" → "**Record (start/stop)** | LMB | RT | Viewfinder state only". Zoom works while recording.

#### P14 — Everything else, the assumption, and the decision log (approved)

- **Goals:** reusable systems "(NPC event actor, camera/grading, gallery)" → "(NPC event actor,
  camera/recording/grading, memory card)".
- **Inventory:** "The Gallery is the main 'inventory'" → "The **memory card** is the main 'inventory' —
  recorded clips with grades, limited by capacity (choosing what to keep is part of play)". "Optional
  film/storage soft resource" → "the card's capacity is the storage resource".
- **Puzzles, characters, environmental storytelling:** "photo-bounty", "photo-as-evidence", "show-photos",
  and "the player's gallery" → "clip-bounty", "footage-as-evidence", "show-footage", and "the player's
  memory card and edits".
- **Progression and economy:** "graded shots" → "graded clips". "(printing, later video editing)" →
  "(printing; a room for the Edit Suite)". "selling/publishing shots" → "…footage". The film/battery pacing
  resource → the card's capacity, with a battery possible later.
- **Level types:** the home base "(later: a video-editing room)" → "(and, once Epic 9 exists, a room for the
  Edit Suite)".
- **Art:** the viewfinder adds "REC dot, running timecode, card-time-remaining counter, CARD FULL".
  **Audio:** "shutter" → "record start/stop beeps".
- **Technical:** add "Recording must hold the 60 FPS target; the approach is chosen by measurement (ADR-4,
  Story 1.14) and must run in a Windows build."
- **Epics table:** row 1 deliverables → camcorder / clip grading / memory card + playback. New **row 9**
  (The Edit Suite) is placed after row 1. Row 5 "photo printing" → "printing stills from clips; a room for
  the Edit Suite".
- **Out of Scope and the deferred line:** "full video-editing minigame" is **removed** from deferred scope,
  with a note that it was promoted to Epic 9.
- **Success Metrics:** "capture → see graded shot in gallery" → "record → see a graded clip on the memory
  card". "high/low-grade shot" → "clip". 60 FPS "with the drunk event active" → "…and while recording".
- **Line 409:** `[ASSUMPTION] Photo-first core vs. "video maker" fantasy` → `[RESOLVED 2026-11-10] Video at
  the core`. It records the stakeholder request and that the mechanic and grading were revisited exactly as
  the assumption predicted.
- **decision-log.md:** a new entry, "2026-11-10 — Core verb: video replaces photo (Correct Course)",
  recording the seven decisions from this session.

### 4.4 Architecture — `game-architecture.md` (v1.0 → v1.1)

#### P15 — Clip Recording system and ADR-4 (approved)

- **Executive Summary:** add *"Video recording replaces the shutter (2026-11-10). How a built game records
  itself is decided by measurement in ADR-4 (Story 1.14). The Frame-Read grader (ADR-2) becomes the
  per-frame engine of clip grading."*
- **Core Systems:** "Photo Capture & Viewfinder (Medium)" → "Clip Recording & Viewfinder (High, novel)".
  "Minimal Gallery (Low–Medium)" → "Memory Card (Medium)".
- **Decision table:** row 6 → memory card persistence. New row 9: "Clip recording — ADR-4, must work in a
  build".
- **NEW section "Clip Recording":**
  - `ClipRecorder` (`Scripts/Recording/`), started and stopped by `PhotoModeController`'s Capture handler,
    and also stopped by lowering the camera or a full card.
  - `ClipGrader`, which aggregates per-frame `ShotGrader` samples and peak coverage into a `ClipGrade`.
  - `MemoryCardService`, which evolves `GalleryService`.
  - `CapturedClip { Id, Footage (ADR-4 handle), Thumbnail, ClipGrade, SubjectId, CapturedAtUtc,
    DurationSeconds }`, following the same disk-ready rules as `CapturedShot`.
  - `ClipRecordedChannel`.
  - The data flow, and the dependency direction: `PhotoMode` → `Recording` → `Grading`/`Events`; the memory
    card and UI listen via the channel.
- **ADR-4 (Clip recording): OPEN, decided by Story 1.14.** Candidates A/B/C, the build-only constraint, and
  the questions it must answer. *If (B) replay recording wins:* a new cross-cutting rule that everything
  that moves or makes sound on screen must be recordable.
- **Project structure:** add `Scripts/Recording/`, `Data/Recording/` and the `CameraGame.Recording`
  namespace.
- **Epic mapping:** E1 adds recording. The E4 channel and E5 seam are renamed. New **E9** row.
- **Data Persistence (Gallery):** a note marks `CapturedShot` as the photo-era model, superseded for clips.

### 4.5 Sprint tracking — `sprint-status.yaml`

#### P16 — Sprint status (approved)

```yaml
  1-12-grade-feedback-hud: done
  # 1-13 superseded 2026-11-10 by 1-16 (see sprint-change-proposal-2026-11-10.md) - not tracked
  1-14-decide-how-the-game-records-video: backlog
  1-15-record-a-clip: backlog
  1-16-grade-a-clip: backlog
  1-17-the-memory-card: backlog
  1-18-watch-your-clips: backlog
  epic-1-retrospective: optional

  # ----- Epic 9: The Edit Suite [Jira PENDING] - sequenced immediately after Epic 1 -----
  epic-9: backlog
  epic-9-retrospective: optional
```

Also update `last_updated` and the Jira mirror comment.

## 5. Implementation Handoff

**Scope classification: Moderate.** The backlog is reorganised and the plan structure survives, but there is
one architecture decision (ADR-4) that the spike must settle before implementation continues.

| Role | Who / which workflow | Responsibility |
|---|---|---|
| Product Owner / Scrum Master | Alexv, with this workflow | Apply the approved edits to epics.md, the GDD, the decision log, the architecture, sprint-status and the 1.13 file (done in this session on approval). Take the open questions (FR31; toggle/sound/2 min) to the stakeholder. |
| Developer | `gds-create-story` → `gds-dev-story` | Create the Story 1.14 context file next, then run the spike. Stories 1.15–1.18 follow in order. |
| Architect touchpoint | Alexv, at the end of Story 1.14 | Approve ADR-4 before Story 1.15 starts (Story 1.14 AC2). |
| ~~Jira sync~~ | — | **Cancelled 2026-11-10: Jira was removed from the project.** `sprint-status.yaml` and the story files are the only tracker. |

**Sequence:** 1.14 (spike; gates the rest) → 1.15 → 1.16 → 1.17 → 1.18 → Epic 1 exit playtest → Epic 1
retrospective (optional) → **Epic 9** → Epic 2 → …

**Success criteria**

1. All planning artifacts agree. "Photo" no longer appears as the player's verb in epics.md, the GDD or the
   architecture, except in dated history notes.
2. Story 1.14 delivers ADR-4 with measurements, and a clip of the drunk is recorded and played back **in a
   Windows build**.
3. **Epic 1 exit:** an uninstructed first-time player hears the cue, finds the drunk, raises the camcorder,
   records his stagger, sees a graded clip land on the memory card, watches it back, and wants the next one.
4. The perceptual checks named in Stories 1.18 and 1.16 are carried out by Alexv, not marked verified by
   tooling.

## Implementation notes (2026-11-10, after approval)

- **All 16 approved proposals are applied.** A consistency sweep caught four more spots that still used photo
  as the player's verb, and they were fixed in the spirit of P1, P3 and P11: the FR Coverage Map labels for
  FR1–FR8 and FR24, FR22's "photographable" → "filmable", the GDD's "subjects to photograph" → "subjects to
  film", and the GDD epics table's Epic 7 row (photo-bounties → clip-bounties). The architecture's v1.0 decision
  text still says "photo" where it names the code's `Photo` mode, which is intentionally unchanged.
- **`sprint-status.yaml` had not been valid YAML since at least 2026-07-31.** Every committed version fails to
  parse, because the multi-line `last_updated` log had continuation lines at column 0 and seven colon-separators
  (`: ` and an end-of-line `:`) that YAML reads as new keys. BMad tooling had been reading it as text, so nothing
  had complained. It was repaired mechanically: the continuation lines were indented, and those seven colons became
  ` -`. No words changed. It now parses, and the full 3,896-character history is preserved as one value. **For
  future log entries:** do not put `: ` (or a trailing `:`) inside `last_updated`.
- **Jira: removed from the project** (Alexv, same day). Removed: the "Jira Sync" section of `CLAUDE.md` and
  `AGENTS.md`; the Jira half of `.claude/hooks/post_edit_reminders.py` and its `.codex/` copy (the C# compile
  reminder is kept, and was tested by running the hook); the project-scope `atlassian` MCP server in `.mcp.json`
  and `.codex/config.toml`, and its permissions in `.claude/settings.json`; the `jiraSync:` block in `epics.md`;
  and the KAN comments in `sprint-status.yaml`. The old KAN keys remain in git history (commit `1e3ab88`).
- **Stakeholder answers:** FR31 (export) confirmed, so Story 1.14 AC2, ADR-4 and the Epic 9 seeds now require a
  working export path. Toggle, game sound and the ~2 min card are confirmed and noted on Stories 1.15 and 1.17.

## Appendix — Change-navigation checklist record

| Item | Status | Note |
|---|---|---|
| 1.1 Triggering story | [x] | None. Stakeholder feedback after a rig video. |
| 1.2 Core problem | [x] | New stakeholder requirement changing the core verb. |
| 1.3 Evidence | [x] | Quote; GDD line 409; Recorder is Editor-only; memory maths; grader reuse. |
| 2.1 Current epic | [x] | Epic 1 goal holds, verb changes; extended with 1.14–1.18. |
| 2.2 Epic-level changes | [x] | Epic 1 modified; Epic 9 added. |
| 2.3 Remaining epics | [x] | E4/E5/E7 wording; E2/E3/E6/E8 unchanged. |
| 2.4 Obsolete or new epics | [!] → resolved | None obsolete; Epic 9 added (promoted from deferred). |
| 2.5 Order / priority | [!] → resolved | Epic 9 sequenced immediately after Epic 1. |
| 3.1 GDD (PRD) conflicts | [!] → resolved | P12–P14. The MVP stays achievable; its verb is redefined. |
| 3.2 Architecture | [!] → resolved | P15. ADR-4 stays open until Story 1.14. |
| 3.3 UI/UX | [N/A] | No UX document; viewfinder changes are in the stories' ACs. |
| 3.4 Other artifacts | [!] → resolved | sprint-status (P16), 1.13 file (P4), rigs/tests (noted), Jira (pending re-auth). |
| 4.1 Direct adjustment | Viable | Effort High, risk Medium–High. |
| 4.2 Rollback | Not viable | The photo code is the foundation. |
| 4.3 MVP review | Viable | Verb redefined, scope unchanged, editing out of the MVP. |
| 4.4 Path | [x] | Hybrid (1 + 3). |
| 5.1–5.5 Proposal components | [x] | Sections 1–5 of this document. |
| 6.1–6.2 Review | [x] | 16 of 16 proposals reviewed individually. |
| 6.3 Approval | [x] | Approved by Alexv, 2026-11-10, with no conditions. |
| 6.4 sprint-status | [x] | Applied (P16). |
| 6.5 Handoff | [x] | Next: `gds-create-story` for 1.14. (Jira sync cancelled: Jira was removed from the project.) |
