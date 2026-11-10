## Your task — phase RETRO for epic {epic} (attempt {attempt})

Every story of epic {epic} is merged. Run the **`gds-retrospective`** skill for it, unattended:
no party-mode discussion with Alexv. Produce the retrospective document from evidence (the story
files, their review sections, `deferred-work.md`, git history, the loop's own reports in
`loop/runtime/reports/`).

Focus on what will make the **next** epic go better:

- what kept going wrong (bugs that hid in reuse, rig failures, verification traps);
- what worked and should become the default;
- what the loop itself did badly (wasted attempts, wrong decisions Alexv would likely veto).

Return 3–10 concrete, one-line `lessons` in the result file: the orchestrator appends them to
`loop/lessons.md`, which every future session reads first.

Then **consolidate `loop/lessons.md`** (the one phase allowed to edit it). Every story appends to it
and every session reads all of it, so it must stay short enough to be read and acted on: at most
**120 lessons** after your edit. Keep the header. Merge lessons that say the same thing; group them
under topic headings (rigs and capture, Unity editor, builds, audio, git and the loop…); drop a
lesson only when something now enforces it (a test, the guard, a rig check), or when it describes
code that no longer exists. Never drop one just because it is old. List what you merged or dropped,
and why, in the retrospective document. Do not repeat this epic's new `lessons` in the file
yourself: the orchestrator appends them when your unit merges.

Set `epic-{epic}-retrospective: done` in sprint-status (no `: ` inside `last_updated`). Commit.

{previous_failure}

**Done when:** the retrospective document exists, the lessons are in the result file,
sprint-status is updated, and it is committed on `{branch}`.
