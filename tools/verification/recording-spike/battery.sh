#!/bin/bash
# Story 1.14 measured battery (protocol 1c, revised): >=3 runs per candidate, order rotated per round so a
# heating laptop cannot favour one candidate. Every run records the same scripted scenario. No gdigrab or
# other capture runs during these (measure-* files only).
HERE="$(cd "$(dirname "$0")" && pwd)"
EV="${EV:-$(cd "$HERE/../../.." && pwd)/_bmad-output/verification/1-14-decide-how-the-game-records-video}"
mkdir -p "$EV"; cd "$EV"   # outputs land in the evidence folder; scripts stay in git
R() { "$HERE/run.sh" "$@" -timeout 260 | tail -1; }
echo "battery start $(date +%T)"
# round 1 (reuse cycles + export probes here)
R baseline measure-base-r1 measure
R C measure-C-r1 measure -cycles 3
R A measure-A-r1 measure -cycles 2
R B measure-B-r1 measure -cycles 2
# round 2
R A measure-A-r2 measure -noexport
R B measure-B-r2 measure -noexport
R baseline measure-base-r2 measure
R C measure-C-r2 measure -noexport
# round 3
R B measure-B-r3 measure -noexport
R baseline measure-base-r3 measure
R C measure-C-r3 measure -noexport
R A measure-A-r3 measure
# sensitivity rows (labelled; not the headline numbers)
R A sens-A-qsv measure -encoder h264_qsv
R baseline sens-base-cap60 measure -fpscap 60
R A sens-A-cap60 measure -fpscap 60
R B sens-B-cap60 measure -fpscap 60 -noexport
echo "battery done $(date +%T)"
