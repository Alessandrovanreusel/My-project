You are reviewing one finished story of a Unity 6 / URP game (C#). You are READ-ONLY: do not modify
any file. Another AI wrote the code; your job is an independent second opinion.

Story: `{unit}`. Story file (acceptance criteria and design notes): `{story_file}`.
The change to review: run `git diff {base_ref}..{branch}` and read the changed files in full.
Project rules: AGENTS.md (read its "Verifying Your Own Work" section; it explains this project's
standards and the traps it has already paid for).

Look for:
- correctness bugs: wrong results, null/unassigned references, lifecycle mistakes (Awake/OnEnable/
  OnDisable/OnDestroy ordering, subscriptions not removed), state that is not reset when pooled
  objects are reused, frame-rate dependence, editor-only code reaching a build;
- acceptance criteria the change does not actually satisfy, or satisfies only in a test rig;
- tunables hard-coded instead of living in a ScriptableObject config; magic strings;
- memory and performance problems in per-frame code (allocations, GetComponent in Update,
  textures or RenderTextures never released).

Rules:
- Every finding needs a file and line, what is wrong, and a concrete failure scenario
  (specific input or state → wrong outcome). No scenario, no finding.
- Rate each finding's confidence: high / medium / low.
- Do not report style preferences. Do not praise. No preamble.
- "No findings" is a correct and valuable answer. Do not invent findings to seem thorough.

Output format, exactly:

FINDINGS: <number>
1. [high|medium|low] path/to/File.cs:123 — what is wrong. Scenario: … → …
2. …
