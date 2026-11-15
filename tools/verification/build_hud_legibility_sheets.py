"""Contact sheets for Story 1.19's human check (AC7), built from a legibility run's crops.

Usage:
    python -I tools/verification/build_hud_legibility_sheets.py --after <run_dir> --candidates <run_dir>
        --red <candidate key, e.g. redC> --d <thickness, e.g. 1.5> --out <folder>

Writes:
    panels_red.png         the six readout states (5/3/1 stars, 1 %, counted 0 %, MISS, NOT GRADED) on the
                           brightest background the survey found, 1080p, cropped to the panel
    before_after.png       ONE frozen frame each: today's salmon with no outline against the chosen red
                           with the outline (from the candidate sweep, which freezes once per background);
                           amber with no outline against amber with it (the final run's N and A variants)
    sheet_<background>.png every colour over one background (channel-raised, labelled as such)

Every panel is labelled with what it is and the measured numbers from legibility_results.json, so the
picture and the measurement cannot be read apart. Crops are shown at 1:1 — the size a player sees.
"""
import argparse
import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

BACKDROP = (24, 24, 26)
INK = (235, 235, 235)
PAD = 12

STATES = [
    ("1_five_star", "5 stars (channel-raised)"),
    ("2_three_star", "3 stars (channel-raised)"),
    ("3_one_percent", "1 star, prints 1 % - earned something (channel-raised)"),
    ("4_counted_zero", "1 star, prints 0 % - earned nothing (channel-raised)"),
    ("5_missed", "MISSED (channel-raised)"),
    ("6_not_graded", "NOT GRADED (channel-raised)"),
]


def font(size):
    for f in ("C:/Windows/Fonts/consola.ttf", "C:/Windows/Fonts/arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


F = font(18)


def results(run_dir):
    p = os.path.join(run_dir, "legibility_results.json")
    if not os.path.exists(p):
        sys.exit(f"!! {p} missing - run measure_hud_legibility.py on {run_dir} first")
    with open(p, encoding="utf-8") as f:
        return {r["id"]: r for r in json.load(f)}


def numbers(entry):
    if entry is None:
        return "not measured"
    if entry.get("refused"):
        return "REFUSED: " + "; ".join(entry["refused"])
    parts = []
    for ln in entry["lines"]:
        if ln.get("text_vs_panel_worst5") is None:
            continue
        to = ln.get("text_vs_outline_worst5")
        parts.append(f"{ln['field'].replace('Label', '')}: vs outline {to:.1f}" if to is not None
                     else f"{ln['field'].replace('Label', '')}: no outline")
        parts[-1] += f", vs panel {ln['text_vs_panel_worst5']:.1f}"
    return "worst 5 %:  " + "   ".join(parts)


def numbers_no_outline(entry):
    """The N variant's numbers: the same glyphs with the outline switched off."""
    if entry is None:
        return "not measured"
    parts = [f"{ln['field'].replace('Label', '')}: vs panel {ln['no_outline_text_vs_panel_worst5']:.1f}"
             for ln in entry["lines"] if ln.get("no_outline_text_vs_panel_worst5") is not None]
    return "worst 5 %, no outline:  " + "   ".join(parts)


def stack(rows, path, title):
    """rows: list of (image, caption lines). Long captions are wrapped to the sheet's width."""
    import textwrap
    rows = [(img, [w for c in caps for w in (textwrap.wrap(c, 88) or [""])]) for img, caps in rows]
    rows.insert(0, (Image.new("RGB", (1, 1), BACKDROP), textwrap.wrap(title, 88)))
    title = ""
    w = max(img.width for img, _ in rows) + 2 * PAD
    line_h = 22
    h = PAD + line_h * 2
    for img, caps in rows:
        h += line_h * len(caps) + img.height + PAD
    sheet = Image.new("RGB", (w, h), BACKDROP)
    d = ImageDraw.Draw(sheet)
    y = PAD
    d.text((PAD, y), title, fill=INK, font=F)
    y += line_h * 2
    for img, caps in rows:
        for c in caps:
            d.text((PAD, y), c, fill=INK, font=F)
            y += line_h
        sheet.paste(img, (PAD, y))
        y += img.height + PAD
    sheet.save(path)
    print(f"wrote {path}  ({w}x{h})")


def crop(run_dir, set_id, variant):
    p = os.path.join(run_dir, "legibility", f"{set_id}_{variant}.png")
    if not os.path.exists(p):
        sys.exit(f"!! missing {p}")
    return Image.open(p).convert("RGB")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--after", required=True)
    ap.add_argument("--candidates", required=True)
    ap.add_argument("--red", required=True)
    ap.add_argument("--d", required=True)
    ap.add_argument("--out", required=True)
    args = ap.parse_args()
    os.makedirs(args.out, exist_ok=True)

    after = results(args.after)
    cand = results(args.candidates)
    backgrounds = sorted({k.split("__")[0] for k in after if "__" in k and not k.startswith("cand_")})

    for bg in backgrounds:
        rows = [(crop(args.after, f"{bg}__{key}", "A"), [label, numbers(after.get(f"{bg}__{key}"))])
                for key, label in STATES]
        stack(rows, os.path.join(args.out, f"sheet_{bg}.png"),
              f"Every readout colour over ONE frozen background ({bg}), as shipped, 1920x1080, 1:1 crop")

    bright = "bright1" if "bright1" in backgrounds else backgrounds[0]
    rows = [(crop(args.after, f"{bright}__{key}", "A"), [label, numbers(after.get(f"{bright}__{key}"))])
            for key, label in STATES]
    stack(rows, os.path.join(args.out, "panels_red.png"),
          f"The six readout states on the brightest background the survey found ({bright}), as shipped")

    cbg = next((k.split("__")[0][len("cand_"):] for k in cand if k.startswith("cand_")), "bright1")
    salmon_id = f"cand_{cbg}__salmon__d{float(args.d):.1f}__a1.00"
    red_id = f"cand_{cbg}__{args.red}__d{float(args.d):.1f}__a1.00"
    rows = [
        (crop(args.candidates, salmon_id, "N"),
         ["BEFORE: MISS in today's salmon, NO outline (the N variant of the frame below)",
          numbers_no_outline(cand.get(salmon_id))]),
        (crop(args.candidates, red_id, "A"),
         [f"AFTER: the same frozen frame, MISS in the chosen red ({args.red}) WITH the outline (d {args.d})",
          numbers(cand.get(red_id))]),
        (crop(args.after, f"{bright}__3_one_percent", "N"),
         ["BEFORE: a shot printing 1 % in amber, NO outline (N variant)",
          numbers_no_outline(after.get(f"{bright}__3_one_percent"))]),
        (crop(args.after, f"{bright}__3_one_percent", "A"),
         ["AFTER: the same frozen frame, amber WITH the outline", numbers(after.get(f"{bright}__3_one_percent"))]),
    ]
    stack(rows, os.path.join(args.out, "before_after.png"),
          "Before / after on the SAME frozen frame (pairs). Top pair from the candidate sweep, bottom pair "
          "from the final run.")


if __name__ == "__main__":
    main()
