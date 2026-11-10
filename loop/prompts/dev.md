## Your task — phase DEV for `{unit}` (epic {epic}, attempt {attempt})

Run the **`gds-dev-story`** skill on `{story_file}` and implement the story completely.

- Work through the tasks in order. A task marked as gating the story (a decision) comes first:
  settle it by measuring, record it under `decisions`, and write the outcome into the story file.
- Verify every AC the way CLAUDE.md demands: a rig that drives the real code path in play mode,
  captures you have actually looked at, values drawn on the images, motion checked with frame
  analysis and (for temporal questions) Gemini. Compile-clean and tests-green are the floor, not
  the evidence.
- Fill in the story file's Dev Agent Record (what was built, how it was verified, evidence paths,
  what is left for Alexv), and its File List.
- sprint-status: this story → `review` when the implementation is complete and verified.

{previous_failure}

**Done when:** every task is complete, every non-perceptual AC has evidence you looked at, every
perceptual AC is written up as a `human_checks` entry, the console is clean, EditMode tests pass,
the editor is restored, sprint-status says `review`, and everything is committed on `{branch}`.
