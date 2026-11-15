"""Generate the camcorder REC beep placeholders (Story 1.15, decision 1g).

    python -I tools/audio/make_camcorder_beeps.py [output_dir]

Writes two 48 kHz, 16-bit, mono WAVs (default: Assets/Audio/SFX/):
  RecStartBeepPlaceholder.wav  one short tone   (start recording)
  RecStopBeepPlaceholder.wav   two short tones  (stop recording)

Start and stop differ in COUNT, not only pitch, so they can be told apart without the picture. Each tone is a
sine with a raised-cosine attack and release (no click at either end). Placeholders: replace them with
authored sounds by overwriting the files (keep the names and the .meta files, and the scene keeps its wiring).
Deterministic: the same script always writes the same bytes.
"""
import math
import os
import struct
import sys
import wave

RATE = 48000
FREQ = 2700.0        # a high, thin camcorder-style beep
AMP = 0.45           # peak, of full scale
TONE_S = 0.075       # each tone
GAP_S = 0.06         # between the stop beep's two tones
RAMP_S = 0.006       # attack and release


def tone(seconds, freq):
    n = int(round(seconds * RATE))
    ramp = int(round(RAMP_S * RATE))
    out = []
    for i in range(n):
        env = 1.0
        if i < ramp:
            env = 0.5 - 0.5 * math.cos(math.pi * i / ramp)
        elif i >= n - ramp:
            env = 0.5 - 0.5 * math.cos(math.pi * (n - 1 - i) / ramp)
        out.append(AMP * env * math.sin(2.0 * math.pi * freq * i / RATE))
    return out


def silence(seconds):
    return [0.0] * int(round(seconds * RATE))


def write(path, samples):
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(round(max(-1.0, min(1.0, s)) * 32767))) for s in samples))
    print(f"wrote {path}: {len(samples)} frames, {len(samples) / RATE * 1000:.0f} ms")


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else os.path.join("Assets", "Audio", "SFX")
    os.makedirs(out_dir, exist_ok=True)
    write(os.path.join(out_dir, "RecStartBeepPlaceholder.wav"), tone(TONE_S, FREQ))
    write(os.path.join(out_dir, "RecStopBeepPlaceholder.wav"), tone(TONE_S, FREQ) + silence(GAP_S) + tone(TONE_S, FREQ))


if __name__ == "__main__":
    main()
