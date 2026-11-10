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
`loop/lessons.md`, which every future session reads first. Do not write `loop/lessons.md` yourself.

Set `epic-{epic}-retrospective: done` in sprint-status (no `: ` inside `last_updated`). Commit.

{previous_failure}

**Done when:** the retrospective document exists, the lessons are in the result file,
sprint-status is updated, and it is committed on `{branch}`.
