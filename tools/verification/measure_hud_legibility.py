"""Measure the grade readout's legibility off the pixels the game actually rendered (Story 1.19, AC4).

Usage:
    python -I tools/verification/measure_hud_legibility.py <run_dir> [--title TEXT]

<run_dir> is a folder written by Tools > HUD > Grade HUD - Legibility at 1080p (Play). Its legibility/
subfolder holds, per SET, four crops of ONE frozen frame plus a JSON sidecar:
    A  the readout as shipped
    M  every glyph painted marker magenta, the outline (if any) painted marker green
    P  the labels switched off: the panel and the world each glyph is drawn on
    N  the shipped colours with the outline switched off

WHY THE MASKS COME FROM THE RENDERER, NOT FROM A DETECTOR
    build_hud_panel_sheet.py's header lists five auto-detectors that produced plausible wrong numbers.
    Here the game paints the glyphs magenta and the outline green on the same frozen frame, so "which
    pixels are text" is answered by the renderer. Coverage is solved per pixel in LINEAR light against P:
        text coverage    a = min over r,b of (M - P) / (1 - P)      (magenta has r = b = 1)
        outline coverage b = (M_g - P_g) / (1 - P_g)                 (green has g = 1)
    CORE  = a >= 0.9 and M_g small: pixels fully covered by a glyph.
    RING  = b >= 0.75 and a <= 0.1: pixels fully covered by outline and not by text.
    No morphological erosion: at 1080p the 22 px lines have ~2 px strokes, and an erosion leaves nothing
    or only anti-aliased edge pixels. The core pixel count is printed per line, and a line with fewer
    than MIN_CORE pixels gets no number at all.

WHAT IS REPORTED (WCAG relative luminance, contrast = (L1 + 0.05) / (L2 + 0.05))
    text vs outline   each core pixel of A against the mean of the RING pixels of A within 3 px of it.
                      This is the boundary the outline creates; Alexv's 4.5:1 floor applies HERE.
    text vs panel     each core pixel of A against the SAME pixel of P: what the glyph sits on.
                      Reported, not required (on bright ground no red reaches 4.5:1 against a 78 % panel,
                      which is why the outline exists).
    Both as the MEDIAN and the WORST 5 % (5th percentile) of core pixels: the world behind a translucent
    panel is not uniform, and a median hides the bright patch.
    NOT "WCAG compliance": WCAG treats a narrow border as part of the letter and a wide one as a halo.

REFUSALS (a number from a broken capture is worse than no number)
    the set caught the fade (alpha < 1 at any grab); the world moved between A and P (freeze control
    > 8/255 outside the panel); the capture is not 1920x1080 at canvas scale 1.0 (reported, flagged);
    fewer than MIN_CORE core pixels on a line.

CONTROL
    The rendered core colour is compared with the colour the label was given (dE76). A large gap means
    the mask is not reading the text, so the line is flagged.
"""
import argparse
import colorsys
import glob
import json
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

MIN_CORE = 25
RING_RADIUS = 3
FREEZE_LIMIT = 8


def lin(u8):
    c = u8.astype(np.float64) / 255.0
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lum_lin(rgb_lin):
    return 0.2126 * rgb_lin[..., 0] + 0.7152 * rgb_lin[..., 1] + 0.0722 * rgb_lin[..., 2]


def contrast(l1, l2):
    hi = np.maximum(l1, l2)
    lo = np.minimum(l1, l2)
    return (hi + 0.05) / (lo + 0.05)


def lab_of_srgb01(rgb):
    r, g, b = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    x = (r * 0.4124564 + g * 0.3575761 + b * 0.1804375) / 0.95047
    y = (r * 0.2126729 + g * 0.7151522 + b * 0.0721750)
    z = (r * 0.0193339 + g * 0.1191920 + b * 0.9503041) / 1.08883
    f = lambda t: t ** (1 / 3) if t > 0.008856 else 7.787 * t + 16 / 116
    fx, fy, fz = f(x), f(y), f(z)
    L, A, B = 116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz)
    C = (A * A + B * B) ** 0.5
    h = (np.degrees(np.arctan2(B, A)) + 360) % 360
    return L, A, B, C, h


def box_sum(a, r):
    """Sum over a (2r+1)^2 window, zero-padded, via an integral image."""
    p = np.pad(a, r)
    s = np.zeros((p.shape[0] + 1, p.shape[1] + 1))
    s[1:, 1:] = p.cumsum(0).cumsum(1)
    k = 2 * r + 1
    out = s[k:, k:] - s[:-k, k:] - s[k:, :-k] + s[:-k, :-k]
    assert out.shape == a.shape, (out.shape, a.shape)
    return out


def load(path):
    return np.asarray(Image.open(path).convert("RGB"))


def label_window(info, crop, h, pad):
    x, y, w, hh = info["rect"]
    cx0, cy0 = crop[0], crop[1]
    c0 = int(np.floor(x - cx0)) - pad
    c1 = int(np.ceil(x + w - cx0)) + pad
    r0 = int(np.floor(h - (y + hh - cy0))) - pad
    r1 = int(np.ceil(h - (y - cy0))) + pad
    return max(0, r0), min(h, r1), max(0, c0), c1


def pct(a, q):
    return float(np.percentile(a, q)) if a.size else float("nan")


def measure_set(meta, d):
    A, M, P, N = (load(os.path.join(d, meta[k])) for k in ("fileA", "fileM", "fileP", "fileN"))
    h, w = A.shape[:2]
    Al, Ml, Pl, Nl = lin(A), lin(M), lin(P), lin(N)

    eps = 1e-6
    a_r = (Ml[..., 0] - Pl[..., 0]) / np.maximum(1 - Pl[..., 0], eps)
    a_b = (Ml[..., 2] - Pl[..., 2]) / np.maximum(1 - Pl[..., 2], eps)
    alpha = np.clip(np.minimum(a_r, a_b), 0, 1)
    beta = np.clip((Ml[..., 1] - Pl[..., 1]) / np.maximum(1 - Pl[..., 1], eps), 0, 1)

    core_all = (alpha >= 0.9) & (Ml[..., 1] <= 0.12)
    ring_all = (beta >= 0.75) & (alpha <= 0.1)

    YA, YP, YN = lum_lin(Al), lum_lin(Pl), lum_lin(Nl)

    lines = []
    region_core = np.zeros((h, w), bool)
    region_ring = np.zeros((h, w), bool)
    for info in meta["labels"]:
        res = {"field": info["field"], "text": info.get("text") or "", "authored": info.get("color") or []}
        lines.append(res)
        if not info.get("rect") or not res["text"].strip():
            res["refused"] = "blank line" if info.get("rect") else "no label"
            continue
        r0, r1, c0, c1 = label_window(info, meta["crop"], h, pad=6)
        win = np.zeros((h, w), bool)
        win[r0:r1, c0:c1] = True
        core = core_all & win
        ring = ring_all & win
        region_core |= core
        region_ring |= ring
        res["core_px"] = int(core.sum())
        res["ring_px"] = int(ring.sum())
        res["core_fraction_of_rect"] = float(core.sum() / max(1, win.sum()))
        res["has_outline"] = bool(info.get("hasOutline"))
        if res["core_px"] < MIN_CORE:
            res["refused"] = f"only {res['core_px']} core pixels"
            continue

        rgb = np.median(A[core], axis=0)
        res["rendered_rgb"] = [int(round(v)) for v in rgb]
        L, a_, b_, C, hue = lab_of_srgb01([v / 255 for v in rgb])
        res["rendered_lab"] = [round(L, 1), round(C, 1), round(hue, 1)]
        res["rendered_hsv_s"] = round(colorsys.rgb_to_hsv(*[v / 255 for v in rgb])[1], 3)
        if res["authored"]:
            La, aa, ba, _, _ = lab_of_srgb01(res["authored"][:3])
            res["dE_rendered_vs_authored"] = round(((L - La) ** 2 + (a_ - aa) ** 2 + (b_ - ba) ** 2) ** 0.5, 1)

        yt = YA[core]
        res["text_L_median"] = round(float(np.median(yt)), 4)

        tp = contrast(yt, YP[core])
        res["text_vs_panel_median"] = round(float(np.median(tp)), 2)
        res["text_vs_panel_worst5"] = round(pct(tp, 5), 2)
        res["panel_L_median"] = round(float(np.median(YP[core])), 4)
        res["panel_L_p95"] = round(pct(YP[core], 95), 4)

        tn = contrast(YN[core], YP[core])
        res["no_outline_text_vs_panel_median"] = round(float(np.median(tn)), 2)
        res["no_outline_text_vs_panel_worst5"] = round(pct(tn, 5), 2)

        if res["ring_px"] >= MIN_CORE:
            ring_sum = box_sum(np.where(ring, YA, 0.0), RING_RADIUS)
            ring_cnt = box_sum(ring.astype(np.float64), RING_RADIUS)
            near = core & (ring_cnt > 0)
            local = ring_sum[near] / ring_cnt[near]
            to = contrast(YA[near], local)
            res["outline_rgb"] = [int(round(v)) for v in np.median(A[ring], axis=0)]
            res["outline_L_median"] = round(float(np.median(YA[ring])), 4)
            res["core_px_with_ring_nearby"] = int(near.sum())
            res["text_vs_outline_median"] = round(float(np.median(to)), 2)
            res["text_vs_outline_worst5"] = round(pct(to, 5), 2)
        else:
            res["text_vs_outline_median"] = None

    refusals = []
    if not meta.get("heldFullAlpha", False):
        refusals.append("caught the fade (alpha " + "/".join(f"{a:.2f}" for a in meta.get("alphaAtGrab", [])) + ")")
    if meta.get("freezeMaxDiff", 255) > FREEZE_LIMIT:
        refusals.append(f"world moved (freeze max diff {meta.get('freezeMaxDiff')}/255)")

    return {"lines": lines, "refused": refusals, "core": region_core, "ring": region_ring, "A": A}


def mask_image(A, core, ring, path):
    """The capture beside what the measurement thinks is glyph (white) and outline (green)."""
    vis = (A * 0.25).astype(np.uint8)
    vis[ring] = [0, 255, 0]
    vis[core] = [255, 255, 255]
    both = np.concatenate([A, vis], axis=0)
    Image.fromarray(both).save(path)


def fmt(v, fmt_s="{:.2f}"):
    return "-" if v is None or (isinstance(v, float) and np.isnan(v)) else fmt_s.format(v)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("run_dir")
    ap.add_argument("--title", default=None)
    args = ap.parse_args()

    d = os.path.join(args.run_dir, "legibility")
    metas = []
    for jp in sorted(glob.glob(os.path.join(d, "*.json"))):
        with open(jp, encoding="utf-8-sig") as f:
            metas.append(json.load(f))
    if not metas:
        print(f"!! no sets in {d} - nothing measured. SUSPECT THE RIG.")
        sys.exit(1)

    mask_dir = os.path.join(args.run_dir, "legibility_masks")
    os.makedirs(mask_dir, exist_ok=True)

    results = []
    rows = []
    for meta in metas:
        r = measure_set(meta, d)
        mask_image(r["A"], r["core"], r["ring"], os.path.join(mask_dir, meta["id"] + "_masks.png"))
        entry = {k: meta.get(k) for k in ("id", "source", "background", "scenario", "grade", "probe", "at1080",
                                            "screenW", "screenH", "captureW", "captureH", "scaleFactor",
                                            "freezeMaxDiff", "freezeFractionDiff", "alphaAtGrab")}
        entry["refused"] = r["refused"]
        entry["lines"] = r["lines"]
        results.append(entry)
        for ln in r["lines"]:
            rows.append((meta, r["refused"], ln))

    with open(os.path.join(args.run_dir, "legibility_results.json"), "w", encoding="utf-8") as f:
        json.dump(results, f, indent=1)

    out = []
    title = args.title or f"HUD legibility - {args.run_dir}"
    out.append(f"# {title}\n")
    not1080 = [m["id"] for m in metas if not m.get("at1080")]
    out.append(f"Sets measured: {len(metas)}. Captured at "
               f"{metas[0].get('captureW')}x{metas[0].get('captureH')}, canvas scale {metas[0].get('scaleFactor')}."
               + (f" **NOT 1080p: {', '.join(not1080)}**" if not1080 else " All sets 1920x1080 at scale 1.0."))
    out.append("")
    out.append("Contrast is WCAG relative-luminance contrast measured on rendered pixels. **text vs outline** "
               "(the 4.5:1 target) is each glyph-core pixel against the mean outline pixel within 3 px; "
               "**text vs panel** (reported, not required) is each glyph-core pixel against the same pixel "
               "with the labels off. `med` = median, `w5` = worst 5 % of core pixels. Not a WCAG compliance "
               "claim.\n")
    out.append("| set | source | line | core px | ring px | rendered rgb | s | C* | dE vs authored | "
               "text vs outline med / w5 | text vs panel med / w5 | no-outline text vs panel med / w5 | note |")
    out.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|")
    for meta, refused, ln in rows:
        note = "; ".join(refused + ([ln["refused"]] if ln.get("refused") else []))
        if ln.get("dE_rendered_vs_authored") is not None and ln["dE_rendered_vs_authored"] > 6:
            note += ("; " if note else "") + f"CONTROL: rendered colour dE {ln['dE_rendered_vs_authored']} from authored"
        to = ln.get("text_vs_outline_median")
        out.append("| {id} | {src} | {f} | {cp} | {rp} | {rgb} | {s} | {c} | {de} | {to} | {tp} | {tn} | {note} |".format(
            id=meta["id"], src=meta["source"], f=ln["field"].replace("Label", ""),
            cp=ln.get("core_px", "-"), rp=ln.get("ring_px", "-"),
            rgb=tuple(ln["rendered_rgb"]) if ln.get("rendered_rgb") else "-",
            s=fmt(ln.get("rendered_hsv_s")), c=fmt(ln["rendered_lab"][1], "{:.1f}") if ln.get("rendered_lab") else "-",
            de=fmt(ln.get("dE_rendered_vs_authored"), "{:.1f}"),
            to=(f"{to:.2f} / {ln['text_vs_outline_worst5']:.2f}" if to is not None else "no outline"),
            tp=(f"{ln['text_vs_panel_median']:.2f} / {ln['text_vs_panel_worst5']:.2f}"
                if ln.get("text_vs_panel_median") is not None else "-"),
            tn=(f"{ln['no_outline_text_vs_panel_median']:.2f} / {ln['no_outline_text_vs_panel_worst5']:.2f}"
                if ln.get("no_outline_text_vs_panel_median") is not None else "-"),
            note=note))

    # The floor, per colour state, across every background and line that was measured.
    out.append("\n## Worst case per readout state (every background, every line)\n")
    out.append("| state | sets | lowest text-vs-outline w5 | lowest text-vs-panel w5 | lowest no-outline text-vs-panel w5 |")
    out.append("|---|---|---|---|---|")
    by_state = {}
    for meta, refused, ln in rows:
        if refused or ln.get("refused") or ln.get("text_vs_panel_worst5") is None:
            continue
        key = meta["scenario"] if not meta.get("probe") else meta["probe"]
        s = by_state.setdefault(key, {"sets": set(), "to": [], "tp": [], "tn": []})
        s["sets"].add(meta["id"])
        if ln.get("text_vs_outline_worst5") is not None:
            s["to"].append(ln["text_vs_outline_worst5"])
        s["tp"].append(ln["text_vs_panel_worst5"])
        s["tn"].append(ln["no_outline_text_vs_panel_worst5"])
    for key in sorted(by_state):
        s = by_state[key]
        out.append(f"| {key} | {len(s['sets'])} | {fmt(min(s['to'])) if s['to'] else 'no outline'} | "
                   f"{fmt(min(s['tp']))} | {fmt(min(s['tn']))} |")

    refused_sets = [m["id"] for m in metas if measure_refusal(m)]
    out.append("")
    out.append(f"Sets refused (fade or moving world): {len(refused_sets)}" +
               (": " + ", ".join(refused_sets) if refused_sets else "."))
    out.append(f"Mask pictures (capture above, measured glyph core white / outline green below): {mask_dir}")

    md = os.path.join(args.run_dir, "summary.md")
    with open(md, "w", encoding="utf-8") as f:
        f.write("\n".join(out) + "\n")
    print("\n".join(out))
    print(f"\nwrote {md}")


def measure_refusal(meta):
    return (not meta.get("heldFullAlpha", False)) or meta.get("freezeMaxDiff", 255) > FREEZE_LIMIT


if __name__ == "__main__":
    main()
