# Unity tooling research — 2026-11-14

*Why this exists:* Alexv asked which skills and GitHub repositories would help this project, then asked for
the most important ones to be installed. This is the research behind those choices. **CLAUDE.md, *Toolbox***
is the short operating guide; this file holds the links, licences, alternatives and rejections.

*Method:* a research agent checked every candidate against the GitHub API (stars, last push and release,
licence, archived flag) and the Unity 6.3 package lists, read-only. Each install was then checked again at its
pinned version before it went in: it resolved and compiled on 6000.3.8f1 with zero errors, and EditMode stayed
275/275.

## What was installed (2026-11-14)

| Kind | Name | Version | Commit |
|---|---|---|---|
| Claude Code plugin (user scope) | `unity@anthropic-plugin-directory` (Unity Technologies) | 0.1.9-beta | skills only, no hooks or servers |
| Claude Code plugin (user scope) | `unity-perf@anthropic-plugin-directory` (community, MIT) | 1.1.0 | skills only, no hooks or servers |
| Unity package | `com.unity.test-framework.performance` | 3.7.0 | `d70620e` |
| Unity package | `com.unity.performance.profile-analyzer` | 1.4.2 | `d70620e` |
| Unity package | `com.unity.cinemachine` | 3.1.7 | `d70620e` |
| Unity package | `com.inspiaaa.unityhfsm` (git tag) | 2.3.0 | `d70620e` |
| Unity package | `jp.keijiro.kino-glitch.universal` (Keijiro registry) | 1.0.0 | `d70620e` |
| Unity package | `jp.co.cyberagent.instant-replay` + `.dependencies` (git tag) | 1.7.1 | `d70620e` |
| Unity package | `com.tayx.graphy` (git tag) | 4.0.0 | `996dd51` |
| Unity package | `com.nowsprinting.test-helper` (git tag) | 1.6.4 | `996dd51` |
| Reference project | `UnityTechnologies/PaddleGameSO`, cloned to `..\References\PaddleGameSO` | main | outside the repo |

Also on 2026-11-14: MCP for Unity 10.2.0 → 10.3.0 (`cc2980f`).

---

## Findings, by area

### 1. AI- and Claude-assisted Unity development

- **Unity official agent plugin** — https://github.com/Unity-Technologies/unity-agent-plugin. Unity's own Claude
  Code plugin, packaging https://github.com/Unity-Technologies/skills. Unity maintains it, and several skills
  match the upcoming work: `urp-postprocessing`, `validate-urp-render-graph-renderer-feature`,
  `initialize-ai-navigation`, `audio-setup-mixers`, `optimize-audio`, `ui-ugui`, `ui-uitk`. 638★, pushed
  2026-11-13, 0.1.9-beta, Unity Companion License (not OSI). **Caveats:** beta; some skills drive the editor
  through the Unity CLI and Pipeline package, a second control channel beside MCP for Unity, so this project
  uses those for knowledge only; its README warns that it edits the project directly and not every change can
  be undone. **Installed.**
- **IvanMurzak/Unity-MCP** — https://github.com/IvanMurzak/Unity-MCP. The strongest alternative MCP bridge (any
  C# method becomes a tool with one line; ships a CLI). 4,637★, 1.11.3 on 2026-11-14, Apache-2.0. Worth knowing
  about; do not switch, and never run it alongside MCP for Unity: two bridges into one editor means confusion.
- **CoderGamester/mcp-unity** — https://github.com/CoderGamester/mcp-unity. 2,017★, 1.5.0 on 2026-09-03, MIT. An
  alternative only, not an addition.
- **Nice-Wolf-Studio/unity-claude-skills** — https://github.com/Nice-Wolf-Studio/unity-claude-skills. 35 skills
  summarising the Unity 6.3 docs. 40★, MIT. Small, and its plugin is also named `unity`, so its skills collide
  with the official plugin's. The official one is the better pick.
- **Rejected:** the Unity rule in PatrickJS/awesome-cursorrules (a single prompt written for someone else's
  2021.3 game). Unity's own AI Assistant (`com.unity.ai.assistant`) is pre-release and needs a Unity AI
  subscription; a third-party guide says Unity deprecated its in-editor MCP in favour of the CLI — **not
  confirmed** in Unity's own docs.

### 2. Gameplay recording, replay and video capture

- **CyberAgent InstantReplay** — https://github.com/CyberAgentGameEntertainment/InstantReplay. Records gameplay at
  runtime and exports MP4: a rolling "last N seconds" buffer or unlimited recording, crash recovery, a UI
  Toolkit trim/library UI. On Windows it encodes H.264/AAC through Media Foundation, so no FFmpeg and nothing
  GPL; a URP `RendererFeatureFrameProvider` can record the camcorder camera without the HUD. 157★, v1.7.1 on
  2026-10-29, MIT (dependencies Apache/MIT/BSD). **Caveats:** needs System.IO.Pipelines and
  System.Threading.Channels through UnityNuGet; its own project is on 2022.3 (it compiles on 6000.3 here, but
  has not been run here); an open "readback-free encoding for Windows" issue suggests frames are still read back
  from the GPU (a performance cost); the default `ScreenshotFrameProvider` grabs the screen, which is the Game
  View backbuffer trap in CLAUDE.md. It also ships a Discord sharing module: never use it. **Installed as an
  option; ADR-4 still decides.**
- **Ultimate Replay 3.0** (Asset Store, $35) —
  https://assetstore.unity.com/packages/tools/camera/ultimate-replay-3-0-261736. A state-based replay system
  (records object state, re-renders it in the engine): the "replay-recording" route. 3.3.3 (2026-05-16), any
  render pipeline. Its trial runs in the editor only and will not build. **Not installed (paid).**
- **Unity Recorder** (already installed) — its docs say plainly that it does not work in standalone players or
  builds. Good for rig videos fed to Gemini, not for a player feature.
- **Avoid:** keijiro/FFmpegOut (no commit since 2020-10; its FFmpeg binaries are GPL-3.0); VideoKit (needs a
  videokit.ai account and access key); AVPro Movie Capture is fine only if buying; the open-source "Unity replay
  system" repos all have under 15★ and are mostly abandoned.
- **On the replay-versus-MP4 decision:** grading uses where the subject was relative to the camera, and a video
  file throws that away. A state recording keeps it, which allows re-grading and drawing the grader's box on
  replays. Many games do both: state recording for the in-game replay, MP4 for export. An MP4-only design
  should store the grading data next to each clip as metadata.

### 3. Camera, HUD and UI

- **Cinemachine 3.1.7** — https://github.com/Unity-Technologies/com.unity.cinemachine. The release for 6000.3;
  Unity Companion License. Ships handheld noise presets (`Handheld_tele_*`, `Handheld_wideangle_*`,
  `Handheld_normal_*`): zoom-dependent shake is exactly camcorder behaviour. Impulse and Timeline integration
  also suit replay cameras. **Installed.**
- **keijiro/KinoGlitchURP** — https://github.com/keijiro/KinoGlitchURP. Analog and digital glitch as URP renderer
  features (scan-line jitter, vertical jump, colour drift): tape rewind, fast-forward, signal loss. 78★, 1.0.0
  on 2026-02-22, Unlicense, Unity 6000.0+ (his own project uses 6000.3). **Installed.**
- **Cyanilux/URP_BlitRenderFeature** — https://github.com/Cyanilux/URP_BlitRenderFeature. A more flexible Full
  Screen Pass renderer feature, for a custom viewfinder look with a Fullscreen Shader Graph. 701★, MIT, tested
  on 6000.3.6f1. Pair it with Unity's URP Render Graph samples. *Not installed.*
- **Colby-O2/VNTG** — https://github.com/Colby-O2/VNTG. VHS, CRT and PSX effects for Unity 6 URP with Render
  Graph. 22★, 1.3.0, Unlicense, a one-person project distributed as a `.unitypackage`. *Not installed.*
- **Avoid:** keijiro/KinoGlitch (Built-in pipeline); saimarei/URPGlitch (unmaintained);
  UnityTechnologies/PhotoMode (2020.3, no licence). The REC indicator and timecode need no library.

### 4. Testing and verification

- **Performance Testing 3.7.0** — `com.unity.test-framework.performance`. Samples frame times and profiler
  markers in play-mode tests and in built players, and writes JSON. Use it to record numbers, not as pass/fail
  thresholds. **Installed.**
- **Profile Analyzer 1.4.2** — compares two Profiler captures side by side (A/B checks such as GPU Resident
  Drawer on versus off). **Installed.**
- **nowsprinting/test-helper** — https://github.com/nowsprinting/test-helper. NUnit attributes: `[LoadScene]`,
  `[CreateScene]`, `[TimeScale]` (fit the "put the project back" rule), `[RecordVideo]` through InstantReplay,
  plus statistics helpers. 33★, v1.6.4 on 2026-09-15, MIT. `[TakeScreenshot]` needs a visible Game View, so
  expect the backbuffer trap. **Installed.**
- **Input System `InputEventTrace`** (built into 1.18) — records real input to a file and replays it, to drive
  "the real path" in rigs. Input only: NPCs, physics and NavMesh are not deterministic, so the same input will
  not always give the same result; pausing playback adds drift.
- **Graphy v4.0.0** — https://github.com/Tayx94/graphy. FPS, 1%-low and memory overlay for standalone builds.
  2,920★, MIT, URP support since 4.0. **Installed.**
- **Also:** Project Auditor is package 1.0.2 on 6000.3 and becomes built in from 6.4.
- **Rejected:** AltTester SDK (GPL-3.0, UI-driven); DeNA/Anjin (aimed at uGUI monkey testing); Graphics Test
  Framework (experimental, and reference images bake in the expected result).

### 5. Learning and reference projects (Unity 6 + URP)

- **UnityTechnologies/PaddleGameSO** — https://github.com/UnityTechnologies/PaddleGameSO. The official
  ScriptableObject architecture demo with an e-book: event channels, runtime sets, SO enums, delegate objects.
  It matches this project's event channels directly. No licence file: read it, don't copy code. The GitHub
  copy is 2021.3; the newer Asset Store version 1.2 was made with 6000.5 and may not open cleanly in 6000.3.
  **Cloned to `..\References\PaddleGameSO`.**
- **Unity-Technologies/game-programming-patterns-demo** —
  https://github.com/Unity-Technologies/game-programming-patterns-demo. Command (suits replay), state (NPCs),
  observer, object pool. 1,738★, Unity 6000.0, no licence file.
- **UnityTechnologies/open-project-1 ("Chop Chop")** — https://github.com/UnityTechnologies/open-project-1. The
  most complete open-source game built on SO event channels. Apache-2.0, frozen since 2023 on 2020.3: a
  reference only.
- **Fantasy Kingdom in Unity 6** (Asset Store, free) — a town-scale URP scene using GPU Resident Drawer, GPU
  occlusion culling and STP; directly relevant to performance across a big town. About 1.1 GB,
  non-commercial EULA.
- **unity-atoms** — https://github.com/unity-atoms/unity-atoms. A full SO-architecture library (MIT). A
  reference; don't migrate mid-project.

### 6. Audio and NPC AI

- **Unity built-ins first:** Audio Mixer snapshots for district ambience, the Audio Random Container, and the
  `unity:audio-setup-mixers` skill. These keep the existing AudioSource-sampling rigs valid.
- **Steam Audio** — https://github.com/ValveSoftware/steam-audio. Occlusion, reverb and pathing. 3,006★, v4.8.1,
  Apache-2.0. Only if the built-ins fall short. FMOD replaces the AudioSource pipeline the audio rigs measure
  (licence terms not verified). Avoid Resonance Audio (archived).
- **Unity Behavior** (`com.unity.behavior` 1.0.17) — official visual behaviour graphs whose event channels are
  ScriptableObjects. A known build-only bug: an event channel can be null when a Send Event Message node runs
  on Start. *Not installed.*
- **Inspiaaa/UnityHFSM** — https://github.com/Inspiaaa/UnityHFSM. Hierarchical state machines in code: the
  simplest correct tool for event actors (wander → stagger → fall → get up). 1,634★, v2.3.0, MIT. **Installed.**
- **Rejected:** crashkonijn/GOAP (too much for scripted event actors); fluid-behavior-tree and NPBehave (no
  recent releases).

## Not verified

Unity 6.3 / URP 17 runtime behaviour of InstantReplay (it compiles here); the claim that Unity deprecated its
in-editor MCP; FMOD's current licence terms.
