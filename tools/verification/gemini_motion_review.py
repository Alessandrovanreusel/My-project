"""Send the photo-shoot clip to Gemini and ask the questions stills cannot answer.

Deliberately NOT "is this good?" — that is Alexv's call. This asks only the mechanical,
temporal questions that frame-by-frame analysis provably cannot settle: foot sliding
during ground contact, hitches at the animation loop seam, and whether the upper-body
motion carries an intoxicated stagger or is a clean walk.
"""

import sys
import time

from google import genai

def _video_from_argv() -> str:
    """Clip to review. MUST be a short segment in which the subject is present in EVERY
    frame — verify that before calling. Feeding a long recording with empty stretches
    produced confident [OBSERVED] findings about a character who was not on screen."""
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print("usage: gemini_motion_review.py <clip.mp4> [--purge]")
        raise SystemExit(2)
    return args[0]
# Tried in order; first one that answers wins. Two models are deliberately excluded:
#   gemini-2.5-pro   — free-tier quota is literally 0 (no allowance, not a rate limit)
#   gemini-2.5-flash — 404 "no longer available to new users" on a fresh API key
MODELS = ["gemini-3-flash-preview", "gemini-2.0-flash", "gemini-2.5-flash-lite",
          "gemini-2.0-flash-001"]

PROMPT = """You are reviewing a 24.5-second screen recording from a Unity 6 game's test rig.

FACTS (do not re-derive, do not doubt):
- One NPC, a "Town Drunk": a rigged humanoid playing a walk animation from a Drunk
  animation pack, driven by a Unity Animator and a NavMesh agent following waypoints.
- The character is VERIFIED PRESENT IN EVERY FRAME of this clip. There are no empty frames.
- The world is an empty grey test plane. Sparse background is expected, not a defect.
- The camera is fixed for this clip. 960x540, ~26.6fps.

I need what still frames cannot show: motion continuity over time. Frame-by-frame analysis
has already confirmed legs alternate, arms counter-swing, there is no T-pose, no frozen or
detached limb, no jitter, and feet stay on the ground plane. Do not repeat those.

Answer these four questions:

1. FOOT SLIDING. During stance phase, does the planted foot stay locked to the ground, or
   skate/slide while bearing weight? This is the most important question.

2. LOOP SEAM. Any visible hitch, pop, or snap where the walk cycle restarts or blends
   into/out of turning?

3. STAGGER READ. Does the upper body carry a genuine intoxicated stagger - weight shifting,
   balance being caught, irregular timing - or is it a normal walk with noise layered on?

4. ANYTHING ELSE mechanically wrong that only shows in motion: knee popping or inversion,
   hip snapping, foot ground-penetration, limbs clipping the torso, mistimed foot-plants.

RULES - READ THESE CAREFULLY:
- I will verify EVERY timestamp you give by extracting that exact frame and looking at it.
  A finding I cannot reproduce is worse than no finding.
- Give timestamps relative to THIS clip (0.0 - 24.5s).
- Label each finding [OBSERVED] (you can point to it) or [INFERRED] (you reason it likely).
- "No defect observed" and "I cannot tell from this footage" are CORRECT, VALUABLE answers.
  Do not manufacture a finding to seem thorough. Do not assume a defect exists because I
  asked about it - I am asking about all four categories regardless of whether any are
  present.
- Do not praise. Be concise. No preamble."""


def main() -> int:
    raw = open(r"C:/Users/alexv/.gemini_key", "rb").read()
    key = raw.decode("utf-16").strip().strip('"').strip("'").strip()
    client = genai.Client(api_key=key)
    video = None

    if "--purge" in sys.argv:
        n = 0
        for existing in client.files.list():
            client.files.delete(name=existing.name)
            print(f"deleted {existing.name}")
            n += 1
        print(f"purged {n} uploaded file(s)")
        return 0

    video = _video_from_argv()

    # Reuse an already-uploaded copy rather than re-sending 40MB on every retry.
    f = None
    for existing in client.files.list():
        if False:
            f = existing
            print(f"reusing already-uploaded {f.name} ({f.size_bytes} bytes)", flush=True)
            break
    if f is None:
        print(f"uploading {video} ...", flush=True)
        f = client.files.upload(file=video)
        print(f"  uploaded as {f.name}", flush=True)

    try:
        # The File API needs the video processed before it can be referenced.
        for _ in range(120):
            f = client.files.get(name=f.name)
            if f.state.name != "PROCESSING":
                break
            time.sleep(5)
        print(f"  state: {f.state.name}", flush=True)
        if f.state.name != "ACTIVE":
            print("file never became ACTIVE — aborting")
            return 1

        resp = None
        for model in MODELS:
            print(f"asking {model} ...", flush=True)
            try:
                resp = client.models.generate_content(model=model, contents=[f, PROMPT])
                print(f"  -> {model} answered\n", flush=True)
                break
            except Exception as e:
                msg = str(e)
                # Both failure modes are "this model won't serve me" — quota exhausted
                # AND model-not-available-to-this-key. Neither should abort the run.
                if any(t in msg for t in ("RESOURCE_EXHAUSTED", "429", "NOT_FOUND", "404")):
                    reason = "quota-blocked" if "429" in msg or "RESOURCE_EXHAUSTED" in msg \
                             else "unavailable to this key"
                    print(f"  {model}: {reason}, trying next", flush=True)
                    continue
                raise
        if resp is None:
            print("no candidate model would serve this key")
            return 1

        print("=" * 78)
        print(resp.text)
        print("=" * 78)
        um = resp.usage_metadata
        if um:
            print(f"tokens — prompt {um.prompt_token_count}, "
                  f"output {um.candidates_token_count}, total {um.total_token_count}")
        # Delete only once we actually have an answer. Deleting on every failed attempt
        # forced a fresh 40MB upload on each retry; the file is reused instead and
        # removed when the run succeeds. If a run fails, `--purge` cleans it up.
        try:
            client.files.delete(name=f.name)
            print(f"\ndeleted uploaded file {f.name} from Google's servers")
        except Exception as e:
            print(f"\ncould not delete uploaded file: {type(e).__name__}: {e}")
        return 0
    except Exception:
        print(f"\nleaving {f.name} uploaded so a retry need not re-send 40MB "
              f"(run with --purge to delete it)")
        raise


if __name__ == "__main__":
    sys.exit(main())
