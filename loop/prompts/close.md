## Your task — phase CLOSE for `{unit}` (epic {epic}, attempt {attempt})

You are the final gate before this story merges to master. Be strict: anything you wave through
lands on master and other stories will build on it.

Check, by running things rather than reading:

1. `refresh_unity`, then `read_console`: no errors, and no warnings introduced by this story.
2. `run_tests` (EditMode, and PlayMode if the project has them): all pass.
3. Every acceptance criterion in `{story_file}` has either evidence you can open (paths that exist
   under `_bmad-output/verification/{unit}/` or tests) or a `human_checks` entry. Open a sample of
   the captures and confirm they show what the story says they show. Blank, uniform or near-white
   images mean the rig is broken (CLAUDE.md). **Read every queued human check's `my_read`.** A check
   whose own read says the AC is likely not met is a failing AC, not evidence. Fix it if that is
   small (making any choice by measurement, as a decision). Otherwise write `failed` with the fix as
   your `next_hint`.
4. The story file is complete: tasks ticked honestly, Dev Agent Record, review notes, File List.
5. `git status`: nothing tracked is uncommitted, and no untracked file under `Assets/`,
   `_bmad-output/planning-artifacts/` or `_bmad-output/implementation-artifacts/` that belongs to
   this story is left behind (new assets need their `.meta`).
6. The editor is back in the scene and settings it was in before the rigs ran.

If a check fails and the fix is small, fix it and re-check. If it is not small, write `failed` with
a precise `next_hint`.

Then set sprint-status for this story: `done` if no perceptual check is pending, otherwise `review`
(and repeat every pending perceptual check in `human_checks`, so the queue is complete). Commit.

{previous_failure}

**Done when:** all six checks pass, sprint-status is set as above, and it is committed on `{branch}`.
