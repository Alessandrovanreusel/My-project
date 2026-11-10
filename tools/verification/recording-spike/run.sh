#!/bin/bash
# usage: run.sh <mode> <tag> <subdir> [extra args...]   Launches the spike build, samples the process's
# private bytes / working set from OUTSIDE once a second (Mono's Process.PrivateMemorySize64 reads 0 in the
# player), and waits for the build to quit on its own. Paths are relative to the repository, so this works
# from any clone; override EV / EXE in the environment if needed.
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../../.." && pwd)"
EV="${EV:-$ROOT/_bmad-output/verification/1-14-decide-how-the-game-records-video}"
EXE="${EXE:-$ROOT/Builds/1-14-decide-how-the-game-records-video/spike/My project.exe}"
mode=$1; tag=$2; sub=$3; shift 3
mkdir -p "$EV/$sub"
"$EXE" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile "$EV/$sub/player-$tag.log" -recordingSpike $mode -spikeTag $tag -spikeOut "$EV/$sub" -vantage 38.59,8.29,6.27 -zoom 0.25 "$@" &
pid=$!
powershell -NoProfile -ExecutionPolicy Bypass -File "$HERE/procmem.ps1" -Out "$EV/$sub/$tag-procmem.csv" &
wait $pid; rc=$?; wait
echo "exit=$rc tag=$tag"
