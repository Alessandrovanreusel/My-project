"""Control experiment: can Gemini actually SEE this video?

Before trusting (or dismissing) its motion findings, test perception with questions whose
answers are independently verified. No analysis, no judgement — just description.

Includes two traps for objects that do not exist in the video. Confabulating them would
explain the fabricated motion findings.
"""

import sys
import time

from google import genai

VIDEO = r"C:/Users/alexv/OneDrive/Bureau/Unity/My project/_bmad-output/verification/photo-shoot/photo-shoot.mp4"
MODELS = ["gemini-3-flash-preview", "gemini-2.0-flash", "gemini-2.5-flash-lite"]

PROMPT = """Describe what you can SEE in this video. This is a perception check, not an
analysis task. Do not evaluate quality, do not look for defects.

Answer each numbered question briefly and literally:

1. How many distinct human/humanoid characters appear in this video at any point?
2. Describe the environment: ground, sky, and anything else present in the world.
3. What is on screen at exactly 14.5 seconds? Describe the frame.
4. What is on screen at exactly 3 seconds, and at exactly 190 seconds?
5. What colour is the character's torso/shirt? What colour is the sky?
6. Describe any buildings, furniture, vehicles, trees or other scenery objects visible.
7. Is there any on-screen text, UI, HUD, or overlay anywhere in the video?
8. Does the character ever disappear from the frame entirely? If so, roughly when and
   for how much of the total runtime?
9. Does the camera ever cut or jump to a different position? Roughly how many times?

CRITICAL: "Nothing", "none", "no objects of that kind", and "the frame is empty" are
CORRECT answers where true. Several of these questions may have such an answer. Do not
invent content to fill an answer. If you genuinely cannot determine something, say so.

Be terse. One or two sentences per question. No preamble."""


def main() -> int:
    key = open(r"C:/Users/alexv/.gemini_key", "rb").read().decode("utf-16").strip().strip('"').strip("'").strip()
    client = genai.Client(api_key=key)

    print(f"uploading {VIDEO} ...", flush=True)
    f = client.files.upload(file=VIDEO)
    for _ in range(120):
        f = client.files.get(name=f.name)
        if f.state.name != "PROCESSING":
            break
        time.sleep(5)
    print(f"  {f.name}  state={f.state.name}", flush=True)
    if f.state.name != "ACTIVE":
        return 1

    try:
        resp = None
        for model in MODELS:
            print(f"asking {model} ...", flush=True)
            try:
                resp = client.models.generate_content(model=model, contents=[f, PROMPT])
                print(f"  -> {model} answered\n", flush=True)
                break
            except Exception as e:
                if any(t in str(e) for t in ("RESOURCE_EXHAUSTED", "429", "NOT_FOUND", "404")):
                    print(f"  {model} unavailable, next", flush=True)
                    continue
                raise
        if resp is None:
            print("no model available")
            return 1
        print("=" * 78)
        print(resp.text)
        print("=" * 78)
        um = resp.usage_metadata
        if um:
            print(f"tokens — prompt {um.prompt_token_count}, output {um.candidates_token_count}")
        return 0
    finally:
        try:
            client.files.delete(name=f.name)
            print(f"\ndeleted {f.name} from Google's servers")
        except Exception as e:
            print(f"\ncleanup failed: {e}")


if __name__ == "__main__":
    sys.exit(main())
