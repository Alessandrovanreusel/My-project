#!/bin/bash
# Story 1.14 evidence runs (evidence-* files; NOT measurements - gdigrab costs CPU/GPU itself).
HERE="$(cd "$(dirname "$0")" && pwd)"
EV="${EV:-$(cd "$HERE/../../.." && pwd)/_bmad-output/verification/1-14-decide-how-the-game-records-video}"
mkdir -p "$EV"; cd "$EV"   # outputs land in the evidence folder; scripts stay in git
# 1) AC3 proof: (B) records ~10 s across the peak, plays it back in the build (twice: two takes), then the
#    Edit Suite probe trims take 1, joins take 2, and exports one MP4. The window is filmed throughout.
"$HERE/run.sh" B evidence-B evidence -prove -cycles 2 -editexport -timeout 300 &
rp=$!
for i in $(seq 1 30); do tasklist //FI "IMAGENAME eq My project.exe" | grep -q "My project" && break; sleep 1; done
sleep 4
ffmpeg -hide_banner -loglevel error -y -f gdigrab -framerate 30 -i title="My project" -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -f matroska evidence/evidence-B-window.mkv &
fp=$!
wait $rp
kill -INT $fp 2>/dev/null; sleep 2; taskkill //F //IM ffmpeg.exe >/dev/null 2>&1
ffmpeg -hide_banner -loglevel error -y -i evidence/evidence-B-window.mkv -c copy evidence/evidence-B-window.mp4
# 2) fail-soft run (provokes errors ON PURPOSE: bad ffmpeg path, ffmpeg killed mid-clip, full raw ring)
"$HERE/run.sh" failsoft evidence-failsoft evidence -timeout 200 | tail -1
# 3) winner sensitivity: (B) at 1280x720 (B captures no frames while playing; 720p changes only the export)
"$HERE/run.sh" B evidence-B720 evidence -res 720 -noplay -timeout 200 | tail -1
echo "evidence done $(date +%T)"
