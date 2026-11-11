# Recording spike (Story 1.14), THROWAWAY

This folder is the prototype that measured how a **built** copy of the game can record its own
gameplay. It produced the evidence behind **ADR-4** in `_bmad-output/planning-artifacts/game-architecture.md`.
It is **not game code** and is **never referenced by game code**.

> **Pinned to the old world scale (2026-11-11).** The spike's `EyeHeight 7.5` and its `-vantage 38.59,8.29,6.27`
> predate **ADR-5** (Story 2.1), which made the characters 1.77 m people with the eye at 1.65 m. The code was
> deliberately not changed; re-derive both before reusing any of it.

## The fence

- `CameraGame.Spikes.RecordingSpike.asmdef`: `autoReferenced: false`, and
  `defineConstraints: ["UNITY_EDITOR || CAMERA_RECORDING_SPIKE"]`. It compiles in the Editor and in a player
  only when that player is built with the define. **A plain build contains no spike DLL.** Proof:
  `_bmad-output/verification/1-14-decide-how-the-game-records-video/build/BuildReport-plain.txt` (Managed/ listing).
- `Editor/CameraGame.Spikes.RecordingSpike.Editor.asmdef`: Editor-only; the build and run menu items.
- `Assets/Tests/EditMode/RecordingSpikeFenceTests.cs` fails in three cases. It asks Unity's compilation
  pipeline, so it sees a reference by name or by `GUID:`:
  - any assembly other than the spike's own Editor one declares a reference to the spike;
  - the compiled game code references it;
  - a plain player build, under the current Player Settings defines, would compile it.

  Positive controls prove each check can fail. The 2026-11-10 review ran the mutations on purpose: a `GUID:`
  reference in `CameraGame.Editor` and `CAMERA_RECORDING_SPIKE` in the Standalone defines.
- The harness arms itself **only** when `-recordingSpike <mode>` is on a build's command line, or when an
  editor menu item has set the SessionState flag *in the last 120 s*. An older flag is discarded, for example
  one left behind when `EnterPlaymode` was refused, so it can never take over a normal Play press. The harness
  edits nothing in the scene and creates every helper at runtime.

Story 1.15 lifts the proven back-end into `Assets/Scripts/Recording/` on purpose, and does it as new game code.
ADR-4 lists the spike's known defects that must not be copied. Do not reference this folder from game code.
Delete it once 1.15–1.18 no longer need its evidence.

**It compiles in every Editor session** (`UNITY_EDITOR` satisfies its define constraint) against `CameraGame`.
If a later story's change to game code breaks this folder's compile, never let it block Play mode. Fix the
call minimally, or delete the folder: the evidence and the scripts that produced it outlive it.

## Rebuilding every piece of evidence

All output goes to `_bmad-output/verification/1-14-decide-how-the-game-records-video/` (git-ignored, never `Temp/`).

1. Editor, SampleScene open and clean: `Tools > Recording Spike > Survey Peak and Vantages (Play)` finds the
   peak position and renders candidate vantages (`survey/`). The chosen vantage is in `editor-extra-args.txt`.
2. `Tools > Recording Spike > Build Spike Player` builds `Builds/1-14-decide-how-the-game-records-video/spike/`
   with `CAMERA_RECORDING_SPIKE` as an **extra define for that build only** (PlayerSettings are untouched; the
   BuildReport records defines before and after). `Build Plain Player` builds the fence proof into `.../plain/`.
3. The scripts are in git, in `tools/verification/recording-spike/` (Git Bash). Their output goes to the
   evidence folder, which they find relative to the repository:
   - `run.sh control control evidence` is the frame-source control shot (tap vs ScreenCapture, same frame).
   - `battery.sh` is the measured battery: 3 rotated rounds of baseline/C/A/B plus sensitivity rows (`measure/`).
   - `evidence.sh` runs the AC3 proof (B record, in-build playback, Edit Suite trim+join export), fail-soft and
     720p. gdigrab needs an **unlocked** desktop: on the lock screen it fails with "error 8".
   - From the evidence folder: `python <scripts>/analyse.py measure`, `python <scripts>/audio_check.py measure <tag>`,
     `python <scripts>/ssim.py a.png b.png` and `python <scripts>/sheet.py "<glob>" out.png` give the tables,
     audio checks, frame comparisons and contact sheets.
   - `Tools > Recording Spike > Review - Replay Fidelity Sweep (Play)` (Editor) measures ghost-vs-live bone drift
     on every tick of two takes (`review/`), with a previous-tick control column.
4. Command line of one run (the exe quits on its own; hard timeout `-timeout`, default 240 s):
   `"My project.exe" -screen-fullscreen 0 -screen-width 1920 -screen-height 1080 -logFile <dir>/player-<tag>.log
   -recordingSpike <baseline|A|B|C|control|survey|failsoft> -spikeTag <tag> -spikeOut <dir>
   -vantage 38.59,8.29,6.27 -zoom 0.25 [-cycles N] [-prove] [-editexport] [-noexport] [-noplay]
   [-encoder h264_mf|h264_qsv|null|nullconv] [-transport tcp|pipe] [-res 720] [-fpscap 60] [-solo] [-fidelity]`

The local ffmpeg (`C:/ProgramData/chocolatey/lib/ffmpeg/tools/ffmpeg/bin/ffmpeg.exe`, gyan.dev 8.0.1, **GPL**) is
fine for private measurement and **must never ship**. No encoder binary is committed to this repository.
