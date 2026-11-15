"""Story 1.15: offline analysis of a Record Clip Shoot run.

    python -I tools/verification/record_clip_report.py _bmad-output/verification/1-15-record-a-clip/rig

- SSIM of each live frame against the ghost re-render of the same tick (d1_/d2_ pairs), plus a SHIFTED control
  (live i against ghost i+1): if the control scores like the pairs, SSIM is not telling frames apart here.
- A labelled contact sheet of the pairs (live | ghost | abs diff x4) and one of the REC captures (a*, c*).
Nothing is asserted: the numbers and the sheets are evidence to look at.
"""
import glob
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "recording-spike"))
from ssim import load, ssim  # noqa: E402

W, H = 640, 360


def label(im, text):
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, im.width, 20], fill="black")
    d.text((4, 4), text, fill="yellow")
    return im


def main(rig):
    out = []
    for tag in ("d1", "d2"):
        lives = sorted(glob.glob(os.path.join(rig, f"{tag}_live_f*.png")))
        pairs = [(lv, lv.replace("_live_", "_ghost_")) for lv in lives if os.path.exists(lv.replace("_live_", "_ghost_"))]
        if not pairs:
            continue
        rows = []
        scores, controls = [], []
        for i, (lv, gh) in enumerate(pairs):
            a, b = load(lv), load(gh)
            s = ssim(a, b)
            scores.append(s)
            c = ssim(a, load(pairs[(i + 1) % len(pairs)][1])) if len(pairs) > 1 else float("nan")
            controls.append(c)
            diff = np.clip(np.abs(a - b) * 4, 0, 255).astype(np.uint8)
            row = Image.new("RGB", (W * 3, H), "black")
            row.paste(label(Image.open(lv).convert("RGB").resize((W, H)), f"LIVE {os.path.basename(lv)}"), (0, 0))
            row.paste(label(Image.open(gh).convert("RGB").resize((W, H)), f"GHOST (replayed from the track) SSIM {s:.4f}"), (W, 0))
            row.paste(label(Image.fromarray(diff).resize((W, H)), f"|live-ghost| x4   control vs next ghost {c:.4f}"), (W * 2, 0))
            rows.append(row)
            out.append(f"{tag} {os.path.basename(lv)}: SSIM {s:.4f}  shifted control {c:.4f}  mean|diff| {np.abs(a - b).mean():.2f}")
        sheet = Image.new("RGB", (W * 3, H * len(rows)), "black")
        for i, r in enumerate(rows):
            sheet.paste(r, (0, i * H))
        path = os.path.join(rig, f"{tag}_live_vs_ghost_sheet.png")
        sheet.save(path)
        out.append(f"{tag}: pairs {len(pairs)}, SSIM mean {np.mean(scores):.4f} (min {np.min(scores):.4f}); "
                   f"shifted control mean {np.nanmean(controls):.4f} -> {path}")

    # Loudness against the drunk's distance, in 0.5-2 s windows (a 1/30-s tick is shorter than one of his mumbled
    # syllables, so per-tick RMS mostly measures the syllables). Distance from the track; windows with a marker click
    # (|sample| > 0.5) are left out.
    import csv
    import wave
    for tag in ("d1", "d2", "d3"):
        wp, tp = os.path.join(rig, f"{tag}_clip.wav"), os.path.join(rig, f"{tag}_track.csv")
        if not (os.path.exists(wp) and os.path.exists(tp)):
            continue
        w = wave.open(wp)
        n, ch, sr = w.getnframes(), w.getnchannels(), w.getframerate()
        x = np.frombuffer(w.readframes(n), dtype="<i2").reshape(-1, ch).astype(float).mean(1) / 32767
        slot, ticks = 0, []
        for r in csv.DictReader(open(tp)):
            d = np.hypot(float(r["actor_x"]) - float(r["cam_x"]), float(r["actor_z"]) - float(r["cam_z"])) if r["live"] == "True" else np.nan
            ticks.append((slot / 30.0, d))
            slot += int(r["repeats"])
        for win in (0.5, 1.0, 2.0):
            xs, ys, t = [], [], 0.0
            while t + win <= n / sr:
                seg = x[int(t * sr):int((t + win) * sr)]
                ds = [d for (s, d) in ticks if t <= s < t + win and not np.isnan(d)]
                if ds and np.abs(seg).max() < 0.5:
                    xs.append(np.mean(ds))
                    ys.append(20 * np.log10(np.sqrt((seg ** 2).mean()) + 1e-9))
                t += win
            if len(xs) > 2:
                out.append(f"{tag} loudness in {win} s windows: r(dBFS, distance) = {np.corrcoef(xs, ys)[0, 1]:+.2f} over {len(xs)} windows, "
                           f"distance {min(xs):.1f}-{max(xs):.1f} m, level {min(ys):.1f}..{max(ys):.1f} dBFS")

    caps = sorted(glob.glob(os.path.join(rig, "a*.png")) + glob.glob(os.path.join(rig, "c*.png")))
    if caps:
        cols = 3
        rows = (len(caps) + cols - 1) // cols
        sheet = Image.new("RGB", (W * cols, H * rows), "black")
        for i, f in enumerate(caps):
            sheet.paste(label(Image.open(f).convert("RGB").resize((W, H)), os.path.basename(f)), ((i % cols) * W, (i // cols) * H))
        path = os.path.join(rig, "rec_captures_sheet.png")
        sheet.save(path)
        out.append(f"REC captures sheet: {len(caps)} -> {path}")

    text = "\n".join(out)
    print(text)
    with open(os.path.join(rig, "analysis.txt"), "w", encoding="utf-8") as fh:
        fh.write(text + "\n")


if __name__ == "__main__":
    main(sys.argv[1])
