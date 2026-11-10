"""Story 1.14: per-run and per-candidate tables from measure/*. Frame-time windows per protocol 1c (revised):
'rec' = the 10 s capture window; the SAME window of baseline-mode runs is the control (same drunk segment,
same camera path, nothing captured). Memory = external process sampler (procmem.csv) during the rec window."""
import csv, json, glob, os, re, sys, numpy as np
D = sys.argv[1] if len(sys.argv) > 1 else 'measure'
def frames(tag):
    rows = list(csv.DictReader(open(f'{D}/{tag}-frames.csv')))
    return rows
def window(rows, phase, first=None, last=None):
    v = [r for r in rows if r['phase'] == phase]
    if not v: return None
    t0 = float(v[0]['t']); t1 = float(v[-1]['t'])
    if first: v = [r for r in v if float(r['t']) <= t0 + first]
    if last: v = [r for r in v if float(r['t']) >= t1 - last]
    dt = np.array([float(r['dt_ms']) for r in v])
    return dict(n=len(dt), mean=dt.mean(), p95=np.percentile(dt, 95), p99=np.percentile(dt, 99), over=(dt > 16.67).mean(), fps=1000 / dt.mean(), t0=float(v[0]['t']))
def summ(tag):
    txt = open(f'{D}/{tag}-summary.json').read()
    try: return json.loads(txt)
    except Exception: return json.loads(re.sub(r'": ,', '": null,', txt))
def mem(tag):
    p = f'{D}/{tag}-procmem.csv'
    if not os.path.exists(p): return None
    r = list(csv.DictReader(open(p)))
    pm = [float(x['private_mb']) for x in r]; ws = [float(x['working_set_mb']) for x in r]
    return dict(private_max=max(pm), private_min=min(pm), ws_max=max(ws)) if pm else None
out = {}
for f in sorted(glob.glob(f'{D}/*-summary.json')):
    tag = os.path.basename(f)[:-13]
    rows = frames(tag); s = summ(tag)
    res = dict(mode=s.get('mode'), battery=s.get('battery'), gpu=s.get('gpu'), screen=s.get('screen'), cap=s.get('fps_cap'))
    for ph, kw in (('pre', dict(last=10)), ('rec', dict(first=10)), ('post', dict(first=10)), ('play', {})):
        w = window(rows, ph, **kw)
        if w: res[ph] = {k: round(float(v), 2) for k, v in w.items()}
    for k in ('c1_captures', 'c1_out_frames', 'c1_dup_frames', 'c1_dropped', 'c1_readback_errors', 'c1_peak_vf', 'c1_stop_to_ready_ms',
              'c1_file_bytes', 'c1_encoder_line', 'c1_frames_written', 'c1_ffmpeg_finish_ms', 'c1_audio_peak', 'c1_markers',
              'c1_pose_bytes', 'c1_ring_used_bytes', 'c1_ffmpeg_ready_ms', 'c1_ffmpeg_spawn_ms', 'c1_vp_prepare_ms', 'c1_vp_first_frame_ms',
              'c1_vp_info', 'c1_raw_upload_ms_mean', 'bone_drift', 'export_ms', 'export_frames', 'alloc_ms',
              'c2_dropped', 'c2_readback_errors', 'c3_dropped', 'c3_readback_errors', 'c2_out_frames', 'c3_out_frames', 'c3_ring_ready', 'c2_ring_ready', 'c2_file_bytes', 'c2_ticks'):
        if k in s: res[k] = s[k]
    res['mem'] = mem(tag)
    out[tag] = res
json.dump(out, open(f'{D}/analysis.json', 'w'), indent=1)
# compact table
print(f"{'run':22s} {'pre fps':>8s} {'rec fps':>8s} {'rec p95':>8s} {'rec p99':>8s} {'>16.7':>6s} {'post fps':>8s} {'play fps':>8s} {'drop':>5s} {'dup':>5s} {'privMB':>7s}")
for t, r in out.items():
    g = lambda ph, k: r.get(ph, {}).get(k, float('nan'))
    m = r['mem'] or {}
    print(f"{t:22s} {g('pre','fps'):8.1f} {g('rec','fps'):8.1f} {g('rec','p95'):8.1f} {g('rec','p99'):8.1f} {g('rec','over'):6.2f} {g('post','fps'):8.1f} {g('play','fps'):8.1f} {str(r.get('c1_dropped','')):>5s} {str(r.get('c1_dup_frames','')):>5s} {m.get('private_max', float('nan')):7.0f}")
