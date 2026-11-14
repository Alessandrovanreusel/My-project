## Your task — phase CREATE for `{unit}` (epic {epic}, attempt {attempt})

Run the **`gds-create-story`** skill for story **{unit}** so the story gets a complete,
self-contained context file in `_bmad-output/implementation-artifacts/`, in the same depth and style
as the existing story files (1.9–1.12 are good models: tasks, the "GATES THE STORY" decision tasks,
dev notes, references, verification plan).

- The story's text lives in `_bmad-output/planning-artifacts/epics.md`. Its acceptance criteria are
  the contract: do not weaken or drop any.
- Pull forward everything the previous stories learned that this one needs: their Dev Agent
  Records, review findings, `deferred-work.md` items that touch this area, `loop/lessons.md`.
- Plan the verification up front: which rig, which captures, what would convince a sceptic, and
  which ACs are perceptual (those become `human_checks` later, not self-certified passes).
- **Name the tools.** For each task, check CLAUDE.md *Toolbox* and write which skill or installed
  package it should use, or why none fits, so the dev session builds on them instead of reinventing
  them. Adopting a package in shipped code is a decision: make it a gating task.
- sprint-status: this story → `ready-for-dev`.

{previous_failure}

**Done when:** the story file exists, is ready for development by a fresh session with no other
context, sprint-status says `ready-for-dev`, and both are committed on `{branch}`.
