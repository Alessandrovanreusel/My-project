#!/usr/bin/env python3
"""
Build the HUD panel contact sheet — the one picture the AC3 perceptual check actually asks about.

WHY THIS EXISTS
---------------
`panels_banded.png` was hand-made once, during the 2026-09-11 session, by cropping the readout
panel out of several state captures and stacking them. It turned out to be the single most useful
artifact in story 1.12 — it is what proved the grade banding works, and what revealed that two
counted shots share one colour — and the 2026-09-12 code review found it was referenced nowhere and
reproducible by nothing, so it would silently go stale the next time a colour changed.

A decisive artifact that cannot be rebuilt is a one-off, not evidence. This rebuilds it.

WHAT IT DOES, AND WHAT IT DELIBERATELY DOES NOT
-----------------------------------------------
It stacks the bottom strip of each state capture — the strip that always contains the readout —
into one sheet with most of the world cropped away, so the bands can be compared side by side.

It does **not** try to find the panel automatically, and it measures nothing unless you tell it
exactly where the text is (`--rect`). That restraint is the whole lesson of this file.

⚠️ FIVE AUTO-DETECTORS WERE TRIED HERE AND EVERY ONE PRODUCED PLAUSIBLE WRONG NUMBERS RATHER THAN
ERRORS. Each looked exactly like a HUD defect:

  - fixed fractions of the frame       -> broke the moment the Game View size changed (961x500 to
                                          575x494) and happily measured sky and grass;
  - brightest local outlier            -> locked onto the grader's subject box, reporting the
                                          readout as near-black;
  - brightest within the bottom strip  -> locked onto sunlit white rocks along the bottom edge of
                                          `a_money_shot`, reporting the GREEN 5-star readout as
                                          grey, which reads as "the banding is broken";
  - "darker than the world above it"   -> the reference band holds different scenery, so the ratio
                                          measured tree-versus-ground, not the plate;
  - matching the authored palette      -> grass is green enough to match `strongColor`.

The honest conclusion: from a full frame, over an arbitrary world, this is not reliably automatable
for the effort it is worth. The producer knows the answer exactly — if per-colour numbers are wanted
on every run, the right fix is for `GradeHudShootRunner` to write the cropped panel itself, since it
already computes the HUD's screen rect (`HudRect()`). Until then: look at the sheet, and pass
`--rect` when a number is needed.

USAGE
-----
    python tools/verification/build_hud_panel_sheet.py
    python tools/verification/build_hud_panel_sheet.py --dir _bmad-output/verification/hud
    python tools/verification/build_hud_panel_sheet.py --rect 0.27,0.815,0.62,0.875
    python tools/verification/build_hud_panel_sheet.py --full     # photographs, not just panels

`--full` stacks the WHOLE frame for each state rather than the readout strip, so the photograph the
grade is about is visible alongside the grade. Use it whenever the question is "is this score fair?"
rather than "can these colours be told apart" — the strip cannot answer the first one at all.

`--rect` is left,top,right,bottom as fractions of the frame, bounding the BIG RATING TEXT ONLY.
Measuring the whole panel mixes in the small axes and "why" lines and drags the average off — that
is how the green 5-star readout once measured as grey (0.755, 0.737, 0.720). The rect is frame-size
dependent: check it against one capture before believing a run of numbers.

Every measurement is self-checked: if "text" and "background" come out nearly the same, the rect
missed the glyphs and the frame is reported as unmeasurable instead of being given a number.
"""

import argparse
import sys
from pathlib import Path

try:
    import numpy as np
    from PIL import Image
except ImportError:
    sys.exit("needs numpy and Pillow: python -m pip install numpy Pillow")

# Windows consoles default to cp1252 and die on anything outside it. Printing is this tool's entire
# output, so make it safe rather than decorating it with characters that can crash the run.
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# The settled frames (NOT *_at_shutter — those catch the capture flash at full white, which bleaches
# the panel and reads as a legibility defect that is not there. CLAUDE.md records that trap; do not
# "fix" this by switching to the at-shutter frames.)
PANELS = [
    ("a_money_shot.png", "5 stars   expect GREEN"),
    ("b1_three_star_cream.png", "3 stars   expect CREAM"),
    ("b_mid_counted.png", "2 stars   expect AMBER"),
    ("c_counted_but_zero.png", "1 star    expect AMBER"),
    ("f_blocked.png", "MISS      expect SALMON"),
]

# The readout is bottom-anchored. This strip is deliberately generous: it is for LOOKING at, so a
# little world around the panel costs nothing, and unlike a tight crop it cannot miss.
STRIP_TOP = 0.80

# The rig draws an editor-only debug readout across the top of every capture. The player never sees it,
# and the story's handover has always had to say "ignore the dark box in the top-left" — so --full
# crops it instead of asking a human to mentally subtract it.
DEBUG_OVERLAY_BOTTOM = 0.28

MIN_TEXT_SEPARATION = 0.15
GUTTER, MARGIN = 14, 14
BACKDROP = (24, 24, 26)


def lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def luminance(rgb):
    return 0.2126 * lin(rgb[0]) + 0.7152 * lin(rgb[1]) + 0.0722 * lin(rgb[2])


def contrast(a, b):
    l1, l2 = luminance(a), luminance(b)
    if l1 < l2:
        l1, l2 = l2, l1
    return (l1 + 0.05) / (l2 + 0.05)


def lab(rgb):
    r, g, b = (lin(c) for c in rgb)
    x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047
    y = (r * 0.2126729 + g * 0.7151522 + b * 0.0721750) / 1.00000
    z = (r * 0.0193339 + g * 0.1191920 + b * 0.9503041) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(x), f(y), f(z)
    return np.array([116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)])


def delta_e(a, b):
    return float(np.linalg.norm(lab(a) - lab(b)))


def measure(im, rect):
    """Text and background colour inside an explicitly supplied rect.

    Returns (text, background), or (None, None) when the two are so close that the rect plainly
    missed the glyphs — better to report nothing than a number that came out of the scenery.
    """
    w, h = im.size
    l, t, r, b = rect
    crop = im.crop((int(w * l), int(h * t), int(w * r), int(h * b)))
    px = (np.asarray(crop).astype(float) / 255.0).reshape(-1, 3)
    if px.size == 0:
        return None, None
    bg = np.median(px, axis=0)
    dist = np.linalg.norm(px - bg, axis=1)
    sel = px[dist > np.percentile(dist, 99.0)]
    if sel.size == 0:
        return None, None
    text = sel.mean(axis=0)
    if float(np.linalg.norm(text - bg)) <= MIN_TEXT_SEPARATION:
        return None, None
    return tuple(text), tuple(bg)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default="_bmad-output/verification/hud")
    ap.add_argument("--out", default=None)
    ap.add_argument("--rect", default=None,
                    help="left,top,right,bottom as fractions, bounding the big rating text only")
    ap.add_argument("--full", action="store_true",
                    help="whole frames instead of panel strips: the PHOTOGRAPH plus its readout")
    args = ap.parse_args()

    src = Path(args.dir)
    out = Path(args.out) if args.out else src / "panels_banded.png"

    rect = None
    if args.rect:
        try:
            rect = tuple(float(v) for v in args.rect.split(","))
            if len(rect) != 4:
                raise ValueError
        except ValueError:
            sys.exit("--rect wants four comma-separated fractions, e.g. 0.27,0.815,0.62,0.875")

    crops, frames, missing = [], [], []
    for name, label in PANELS:
        p = src / name
        if not p.exists():
            missing.append(name)
            continue
        im = Image.open(p).convert("RGB")
        w, h = im.size
        if args.full:
            # ⚠️ THE PANEL STRIP ALONE IS NOT ENOUGH TO JUDGE THE FEEDBACK, and Alexv said so on
            # 2026-09-13: "There is just panels. I don't see the pictures." The strip answers exactly
            # one question — can the BANDS be told apart — and answers nothing about whether the grade
            # is fair, which needs the photograph the grade is about. Only the editor-only debug
            # overlay is cropped (the player never sees it); everything else is the frame as shot.
            crops.append(im.crop((0, int(h * DEBUG_OVERLAY_BOTTOM), w, h)))
        else:
            crops.append(im.crop((0, int(h * STRIP_TOP), w, h)))
        frames.append((name.replace(".png", ""), label, im))

    if missing:
        print("MISSING (run Tools > HUD > Grade HUD Shoot (Play) first):")
        for m in missing:
            print("   ", m)
        print()

    if not crops:
        sys.exit("nothing to build")

    if len(crops) != len(PANELS):
        print(f"!! SHEET IS INCOMPLETE - {len(crops)} of {len(PANELS)} panels.")
        print()

    width = max(c.width for c in crops) + MARGIN * 2
    height = sum(c.height for c in crops) + GUTTER * (len(crops) - 1) + MARGIN * 2
    sheet = Image.new("RGB", (width, height), BACKDROP)
    y = MARGIN
    for c in crops:
        sheet.paste(c, (MARGIN, y))
        y += c.height + GUTTER
    sheet.save(out)
    print(f"wrote {out}  ({sheet.width}x{sheet.height}, {len(crops)} panels)")
    print()
    print("Panels, top to bottom:")
    for name, label, _ in frames:
        print(f"   {name:<24} {label}")

    if rect is None:
        print()
        print("No --rect given, so nothing was measured. LOOK at the sheet: each band should be")
        print("obviously a different colour from the ones above and below it, except the two weak")
        print("shots, which share one amber deliberately (Alexv's call, 2026-09-12).")
        print("For numbers, pass --rect bounding the big rating text, e.g. --rect 0.27,0.815,0.62,0.875")
        return

    print()
    print("=" * 78)
    print("MEASURED inside the supplied rect - check the rect before trusting these")
    print("=" * 78)
    measured = []
    for name, label, im in frames:
        text, bg = measure(im, rect)
        if text is None:
            print(f"  {label:<26} !! rect missed the text in {name} - no number reported")
            continue
        c = contrast(text, bg)
        flag = "OK" if c >= 4.5 else "BELOW 4.5:1 AA FLOOR"
        print(f"  {label:<26} text ({text[0]:.3f}, {text[1]:.3f}, {text[2]:.3f})"
              f"   contrast {c:.2f}:1  {flag}")
        measured.append((name, text))

    if len(measured) < 2:
        return

    print()
    print("=" * 78)
    print("PAIRWISE SEPARATION (CIE-Lab dE76)   >=20 clearly different | <10 hard to tell apart")
    print("=" * 78)
    for i in range(len(measured)):
        for j in range(i + 1, len(measured)):
            n1, c1 = measured[i]
            n2, c2 = measured[j]
            d = delta_e(c1, c2)
            verdict = ("clearly different" if d >= 20 else
                       "noticeable" if d >= 10 else "HARD TO TELL APART")
            print(f"  {n1:<24} vs {n2:<24} dE={d:6.2f}  {verdict}")

    print()
    print("Note: b_mid_counted and c_counted_but_zero SHARE the weak band deliberately")
    print("(Alexv's call, 2026-09-12) - a low dE for that one pair is the intended design,")
    print("not a finding. Every other pair should read as clearly different.")


if __name__ == "__main__":
    main()
