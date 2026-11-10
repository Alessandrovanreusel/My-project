# Recording spike (Story 1.14), THROWAWAY

This folder is the prototype that measured how a **built** copy of the game can record its own
gameplay. It produced the evidence behind **ADR-4** in `_bmad-output/planning-artifacts/game-architecture.md`.
It is **not game code** and is **never referenced by game code**.

## The fence

- `CameraGame.Spikes.RecordingSpike.asmdef`: `autoReferenced: false`, and
  `defineConstraints: ["UNITY_EDITOR || CAMERA_RECORDING_SPIKE"]`. It compiles in the Editor and in a player
  only when that player is built with the define. **A plain build contains no spike DLL.** Proof:
  `_bmad-output/verification/1-14-decide-how-the-game-records-video/build/BuildReport-plain.txt` (Managed/ listing).
- `Editor/CameraGame.Spikes.RecordingSpike.Editor.asmdef`: Editor-only; the build and run menu items.
- `Assets/Tests/EditMode/RecordingSpikeFenceTests.cs`: fails if `CameraGame` or `CameraGame.Editor` ever
  references the spike (with a positive control proving the check can see a reference).
- The harness arms itself **only** when `-recordingSpike <mode>` is on a build's command line, or when an
  editor menu item sets the SessionState flag. It edits nothing in the scene and creates every helper at runtime.

Story 1.15 lifts the proven back-end into `Assets/Scripts/Recording/` on purpose, and does it as new game code.
Do not reference this folder from game code. Delete it once 1.15–1.18 no longer need its evidence.

## Rebuilding every piece of evidence

All output goes to `_bmad-output/verification/1-14-decide-how-the-game-records-video/` (git-ignored, never `Temp/`).

1. Editor, SampleScene open and clean: `Tools > Recording Spike > Survey Peak and Vantages (Play)` finds the
   peak position and renders candidate vantages (`survey/`). The chosen vantage is in `editor-extra-args.txt`.
2. `Tools > Recording Spike > Build Spike Player` builds `Builds/1-14-decide-how-the-game-records-video/spike/`
   with `CAMERA_RECORDING_SPIKE` as an **extra define for that build only** (PlayerSettings are untouched; the
   BuildReport records defines before and after). `Build Plain Player` builds the fence proof into `.../plain/`.
3. In the evidence folder (Git Bash):
   - `./run.sh control control evidence` is the frame-source control shot (tap vs ScreenCapture, same frame).
   - `./battery.sh` is the measured battery: 3 rotated rounds of baseline/C/A/B plus sensitivity rows (`measure/`).
   - `./evidence.sh` runs the AC3 proof (B record, in-build playback, Edit Suite trim+join export), fail-soft and 720p.
   - `python analyse.py measure`, `python audio_check.py measure <tag>`, `python ssim.py a.png b.png` and
     `python sheet.py "<glob>" out.png` give the tables, audio checks, frame comparisons and contact sheets.
4. Command line of one run (the exe quits on its own; hard timeout `-timeout`, default 240 s):
   `"My project.exe" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile <dir>/player-<tag>.log
   -recordingSpike <baseline|A|B|C|control|survey|failsoft> -spikeTag <tag> -spikeOut <dir>
   -vantage 38.59,8.29,6.27 -zoom 0.25 [-cycles N] [-prove] [-editexport] [-noexport] [-noplay]
   [-encoder h264_mf|h264_qsv|null|nullconv] [-transport tcp|pipe] [-res 720] [-fpscap 60] [-solo]`

The local ffmpeg (`C:/ProgramData/chocolatey/lib/ffmpeg/tools/ffmpeg/bin/ffmpeg.exe`, gyan.dev 8.0.1, **GPL**) is
fine for private measurement and **must never ship**. No encoder binary is committed to this repository.
