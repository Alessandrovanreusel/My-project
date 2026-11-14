## Your task — phase REVIEW for `{unit}` (epic {epic}, attempt {attempt})

Run the **`gds-code-review`** skill on this story's changes: `git diff {base_ref}..HEAD`, against the
story file `{story_file}`.

You did not write this code and you have no memory of writing it. Review it as a sceptic.

- CLAUDE.md: reviewing is not only reading. **Build a world that exercises the change and run it.**
  Stress it (many actors, long runs, pooled objects reused several times: bugs here hide in reuse
  and in the second cycle), push every tunable to zero, negative, huge and missing.
- **Reproduce before reporting.** A finding you cannot trigger is a hypothesis, so label it so.
- Review shipped code only. Verification rigs are tools you run and modify, not subjects of the
  review (project memory).
- Apply the fixes for confirmed findings, re-verify by running, and record deferred and dismissed
  findings with reasons in the story file's review section and `deferred-work.md`.
- **Use the review skills that fit the change**: `unity-perf:code-standards` for design and standards,
  `unity-perf:unity-performance` and `unity-perf:csharp-zero-gc` for per-frame code,
  `unity:validate-urp-render-graph-renderer-feature` for renderer features. Their findings follow the
  same reproduce-before-reporting rule. Hand-rolling what an installed package already does (CLAUDE.md,
  *Toolbox*) is worth a finding: ask whether the package was considered.
- The skill's interactive triage questions follow the decide-and-flag rule.

{previous_failure}

**Done when:** every confirmed finding is fixed and re-verified (or deferred with a reason), the
review is written into the story file, the console is clean, EditMode tests pass, and everything is
committed on `{branch}`.
