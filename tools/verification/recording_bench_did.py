"""Story 1.15, Task 11: the recorder's frame-rate cost from a Recording Bench run, by difference-in-differences.

    python -I tools/verification/recording_bench_did.py <bench-out-dir> [procmem.csv]

bench-frames.csv (written by Assets/Bench/RecordingBench) holds one row per frame: i,t,dt_ms,phase,round,segment,
serial,recording. Each round runs the SAME 24-s segment of the drunk's lifecycle (from his spawn) once recording
(phase rec) and once not (base), order rotated. Within a round the two segments ran minutes apart on the same
machine state, so rec - base is the recorder's cost with the laptop's heat and the drunk's segment held constant;
the spread across rounds says how far to trust it. Nothing is asserted.
"""
import csv
import os
import sys

import numpy as np


def stats(dt):
    dt = np.asarray(dt)
    med = np.median(dt)
    return dict(n=len(dt), mean=dt.mean(), p95=np.percentile(dt, 95), p99=np.percentile(dt, 99),
                fps=1000.0 / dt.mean(), hitches=int((dt > 2 * med).sum()), over33=int((dt > 33.34).sum()))


def main(d, procmem=None):
    rows = list(csv.DictReader(open(os.path.join(d, "bench-frames.csv"))))
    rounds = sorted({int(r["round"]) for r in rows})
    out = []
    diffs = []
    for rd in rounds:
        seg = {}
        for ph in ("rec", "base"):
            v = [float(r["dt_ms"]) for r in rows if int(r["round"]) == rd and r["phase"] == ph]
            seg[ph] = stats(v) if v else None
        if seg["rec"] and seg["base"]:
            diff = seg["rec"]["mean"] - seg["base"]["mean"]
            diffs.append(diff)
            out.append(f"round {rd}: rec {seg['rec']['mean']:.2f} ms ({seg['rec']['fps']:.1f} fps, p95 {seg['rec']['p95']:.1f}, "
                       f"p99 {seg['rec']['p99']:.1f}, hitches {seg['rec']['hitches']}, >33 ms {seg['rec']['over33']}, n {seg['rec']['n']}) | "
                       f"base {seg['base']['mean']:.2f} ms ({seg['base']['fps']:.1f} fps, p95 {seg['base']['p95']:.1f}, "
                       f"p99 {seg['base']['p99']:.1f}, hitches {seg['base']['hitches']}, >33 ms {seg['base']['over33']}, n {seg['base']['n']}) "
                       f"=> recorder cost {diff:+.2f} ms/frame")
    if diffs:
        out.append(f"recorder cost (rec - base, per round): mean {np.mean(diffs):+.2f} ms/frame, range {min(diffs):+.2f} .. {max(diffs):+.2f}, n = {len(diffs)}")
    allv = [float(r["dt_ms"]) for r in rows]
    out.append(f"all measured frames: {stats(allv)['fps']:.1f} fps mean over {len(allv)} frames")
    rec_flag = [r for r in rows if r["phase"] == "rec" and r["recording"] != "1"]
    out.append(f"rec-phase frames where the recorder was NOT recording: {len(rec_flag)} (a stop mid-segment would show here)")
    if procmem and os.path.exists(procmem):
        pm = list(csv.DictReader(open(procmem)))
        ws = [float(x["working_set_mb"]) for x in pm]
        pv = [float(x["private_mb"]) for x in pm]
        out.append(f"process memory (outside sampler, {len(pm)} samples): working set {min(ws):.0f}-{max(ws):.0f} MB, private {min(pv):.0f}-{max(pv):.0f} MB")
    text = "\n".join(out)
    print(text)
    with open(os.path.join(d, "did.txt"), "w", encoding="utf-8") as fh:
        fh.write(text + "\n")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
