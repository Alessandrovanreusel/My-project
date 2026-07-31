# Verification tooling — frame analysis + video analysis

Reusable tools for verifying a feature or story **by looking at what it actually produced**,
so defects are found and fixed here rather than by Alexv playing the game.

This extends the doctrine in `CLAUDE.md` → *Verifying Your Own Work* to the questions a
still frame cannot settle: motion smoothness, foot sliding, loop seams, timing.

---

## The order matters

**1. Frame analysis first.** Free, local, nothing leaves the machine, and it settles most
questions on its own. `ffmpeg`, `ffprobe`, `numpy` and `Pillow` are all installed.

- **Find the segment by measurement, never by guessing a timestamp.** A rig recording is
  mostly camera repositioning and empty world.
- **Verify the subject is actually on screen** before drawing any conclusion. Detect the
  character by its own colours, not by "something changed" — a motion mask also fires on
  camera moves and on a horizon line dithering.
- **Build a contact sheet** — one labelled grid of N frames, read as a single image. Far
  cheaper than N separate reads and far easier to compare poses across.
- **Measure what the eye cannot:** per-frame centroid, bbox, apparent height (a depth
  proxy), foot-line stability, and a background-strip diff to prove the camera was locked.

**2. Then Gemini**, only for what is inherently temporal.

---

## Scripts

| Script | Purpose |
|---|---|
| `gemini_motion_review.py <clip.mp4>` | Ask about motion continuity on a short clip |
| `gemini_perception_control.py` | Control test — can it see this video at all? |

Both read the API key from `C:/Users/alexv/.gemini_key`, which is **UTF-16 encoded**
(Windows PowerShell `>` writes UTF-16 — decode with `.decode("utf-16")`, not utf-8).
Free tier. Both delete the upload from Google's servers when done; `--purge` clears
leftovers from a failed run.

**Model availability on a free key:** `gemini-3-flash-preview` works.
`gemini-2.5-pro` has a free quota of literally **0**, and `gemini-2.5-flash` returns 404
*"no longer available to new users"*. The scripts fall through a candidate list.

---

## Prompt rules — these decide whether the answer is useful or fiction

Established 2026-07-31. The **same model, video and timestamp** answered a descriptive
question correctly and a leading question with pure fabrication.

- **Send a short clip in which the subject appears in EVERY frame.** Verify presence
  programmatically first. Given the full 219 s recording (~11% empty world), it reported an
  `[OBSERVED]` hyperextending knee in frames containing only sky and ground.
- **Never supply timestamps, and never name the defect you expect.** It anchors on them and
  manufactures findings to fit. Ask *"describe the motion of the feet during ground
  contact"* — not *"is the foot sliding at 13–19 s?"*.
- **Distrust its timestamps on long clips.** It judged a 219 s video to end before 190 s.
  Content descriptions were accurate; time indexing was not.
- **Say explicitly that "nothing", "none" and "I cannot tell" are correct answers.**
- **Verify every claim before repeating it to anyone.** Extract the cited frame and look at
  it. A five-minute measurement disproved the headline finding.
- **Run the control when unsure** — descriptive questions with known answers plus a trap
  about an object that does not exist. It scored 7/9 and invented neither trap object:
  perception is sound, leading prompts are what break it.

---

## What still belongs to Alexv

Mechanical questions — does the foot plant, is there a hitch, does a limb clip — are for
these tools. Whether the drunk *reads* as drunk, and whether a shot feels like a good
photograph, needs his eyes. Bring him a conclusion and the evidence, at the end, not a
checklist at the start.
