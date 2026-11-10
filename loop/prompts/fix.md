## Your task — phase FIX for `{unit}` (epic {epic}, attempt {attempt})

An independent reviewer (a separate, read-only Claude session on a different model, with a fresh
context) reviewed this story. Its findings are in `{findings_path}`. The story file is `{story_file}`; the change is `git diff {base_ref}..HEAD`.

Treat every finding as a **hypothesis**. Reviewers, human or model, produce confident, plausible
findings that turn out to be wrong (CLAUDE.md), so:

1. For each finding, try to **reproduce** it: run a scenario, capture the result, or show the code
   path with a concrete input.
2. Confirmed → fix it, re-verify by running, and note it in the story's review section.
3. Disproven → record it as dismissed in the story's review section, with the evidence that
   disproved it.
4. Cannot tell → record it as a hypothesis in `deferred-work.md` with what would settle it.

{previous_failure}

**Done when:** every finding is confirmed-and-fixed, disproven, or deferred with a reason; the
console is clean; EditMode tests pass; and everything is committed on `{branch}`.
