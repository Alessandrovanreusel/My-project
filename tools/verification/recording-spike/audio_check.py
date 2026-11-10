"""Audio evidence for a spike take: non-silence, loudness vs the drunk's distance, and the A/V sync offset
measured from the clapperboard tone (1 kHz, started on the same game frame the white quad was captured)."""
import sys, csv, json, re, wave, numpy as np
D, tag = sys.argv[1], sys.argv[2]
w = wave.open(f'{D}/{tag}-c1-audio.wav'); rate = w.getframerate(); ch = w.getnchannels()
x = np.frombuffer(w.readframes(w.getnframes()), dtype=np.int16).reshape(-1, ch).astype(np.float64).mean(1) / 32768
rows = [r for r in csv.DictReader(open(f'{D}/{tag}-frames.csv')) if r['phase'] == 'rec']
t0 = float(rows[0]['t'])
win = int(rate * 0.5); rms = []; dist = []
for k in range(0, len(x) - win, win):
    ts, te = k / rate, (k + win) / rate
    seg = x[k:k + win]
    # notch the clapper tone out of the loudness measure (it is not game sound)
    f = np.fft.rfft(seg); fr = np.fft.rfftfreq(len(seg), 1 / rate); f[(fr > 950) & (fr < 1050)] = 0
    rms.append(20 * np.log10(np.sqrt(np.mean(np.fft.irfft(f, len(seg)) ** 2)) + 1e-9))
    ds = [float(r['dist_m']) for r in rows if ts <= float(r['t']) - t0 < te and r['dist_m']]
    dist.append(np.mean(ds) if ds else np.nan)
rms, dist = np.array(rms), np.array(dist); ok = np.isfinite(dist)
corr = np.corrcoef(rms[ok], dist[ok])[0, 1]
# tone onset: 1 kHz energy in 5 ms hops
hop = int(rate * 0.005); n = int(rate * 0.02); e = []
for k in range(0, len(x) - n, hop):
    seg = x[k:k + n] * np.hanning(n); f = np.abs(np.fft.rfft(seg)); fr = np.fft.rfftfreq(n, 1 / rate)
    e.append(f[(fr > 900) & (fr < 1100)].sum() / (f.sum() + 1e-9))
e = np.array(e); onsets = []
above = e > 0.5
for k in range(1, len(above)):
    if above[k] and not above[k - 1] and (not onsets or k * hop / rate - onsets[-1] > 1.0): onsets.append(k * hop / rate)
s = json.load(open(f'{D}/{tag}-summary.json')) if True else {}
marks = [(int(m.group(1)), float(m.group(2))) for m in re.finditer(r'vf=(\d+) t=([\d.]+)', s.get('c1_markers', ''))]
res = dict(tag=tag, seconds=round(len(x) / rate, 2), peak_dbfs=round(20 * np.log10(np.abs(x).max() + 1e-9), 1),
           rms_dbfs_range=[round(float(np.nanmin(rms)), 1), round(float(np.nanmax(rms)), 1)],
           loudness_vs_distance_corr=round(float(corr), 2), dist_range_m=[round(float(np.nanmin(dist)), 1), round(float(np.nanmax(dist)), 1)],
           tone_onsets_s=[round(o, 3) for o in onsets], markers=marks,
           av_offset_ms=[round((o - vf / 30.0) * 1000, 1) for o, (vf, _) in zip(onsets, marks)])
print(json.dumps(res))
