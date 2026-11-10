# Lessons for loop sessions

Every loop session reads this file first. The orchestrator appends the `lessons` each phase returns
when its unit merges, and each epic's retrospective adds more. Keep entries to one line: a pitfall,
or a pattern that worked, concrete enough to act on.

## Seeded 2026-11-10, from Epic 1 (before the loop existed)

- A rig that reports every case identical, every case passing, a round number, or a blank or white capture is broken. Fix the rig before believing the result.
- Unity only sees an edit on disk after it reimports: `refresh_unity`, wait for the compile, and read the value back through the editor before you trigger a rig.
- Overlay UI is invisible to `cam.Render()`. Use `ScreenCapture.CaptureScreenshotAsTexture()` after `WaitForEndOfFrame`, with the camera rendering to the screen.
- Bugs in this project have hidden in reuse and in the second cycle, never the first. Run pooled objects through several cycles.
- `sprint-status.yaml` breaks if `last_updated` contains `: ` or a line ending in `:`. Verify it with `yaml.safe_load` after every edit.
- Gemini fabricates when it is given timestamps or the expected defect. Send short clips with the subject in every frame, ask descriptive questions, and verify every claim on the frame.
- Disabling a manager does not empty its world. To photograph an empty street, deactivate the live actors.
