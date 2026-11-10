"""Per-phase frame-time statistics from a spike frames.csv (protocol 1c)."""
import sys, csv, numpy as np, json
def phases(path):
    rows = list(csv.DictReader(open(path)))
    out = {}
    for r in rows:
        out.setdefault(r['phase'], []).append((float(r['t']), float(r['dt_ms']), float(r['main_thread_ms'] or 'nan')))
    return out
def summarise(vals):
    dt = np.array([v[1] for v in vals]); mt = np.array([v[2] for v in vals])
    return dict(n=len(dt), dur_s=round(vals[-1][0]-vals[0][0], 2), mean_ms=round(dt.mean(), 2), p95_ms=round(np.percentile(dt, 95), 2),
                p99_ms=round(np.percentile(dt, 99), 2), over_16_67=round(float((dt > 16.67).mean()), 3), fps=round(1000/dt.mean(), 1),
                main_thread_mean_ms=round(float(np.nanmean(mt)), 2) if np.isfinite(mt).any() else None)
if __name__ == '__main__':
    ph = phases(sys.argv[1])
    res = {}
    for name, vals in ph.items():
        if name == 'pre':  # protocol: only the 10 s immediately before recording
            t_end = vals[-1][0]; vals = [v for v in vals if v[0] >= t_end - 10.0]
        elif name in ('rec', 'post', 'rec2', 'rec3'):  # the 10 s capture window / the 10 s after it
            t0 = vals[0][0]; vals = [v for v in vals if v[0] <= t0 + 10.0]
        if len(vals) > 5: res[name] = summarise(vals)
    for k, v in res.items(): print(f"{k:7s} {v}")
    if len(sys.argv) > 2: json.dump(res, open(sys.argv[2], 'w'), indent=1)
