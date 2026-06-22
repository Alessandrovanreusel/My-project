# Camera Video Game

A third‑person **photography game** built in **Unity 6 (URP)**. You walk around a town and
capture fleeting, photogenic *moments* at the right time and composition. This repository is an
early **"first playable slice"** (MVP): traversal, a photo‑mode camera (raise / zoom / capture),
and a reusable data‑driven **event engine** that makes town moments happen.

---

## ⚡ Quick start (TL;DR)

> **You MUST have Git LFS installed _before_ you clone.** Most of the art (models, textures, audio,
> animations) is stored in **Git LFS**. Clone without it and you'll get tiny placeholder text files
> instead of real assets — the project opens but everything is missing / pink.

```bash
git lfs install                                                   # one-time, per machine
git clone https://github.com/Alessandrovanreusel/My-project.git
cd "My-project"
git lfs pull                                                      # makes sure all art is real, not pointers
```

Then open the folder in **Unity Hub** with editor version **`6000.3.8f1`**, open
`Assets/Scenes/SampleScene.unity`, and press **Play**.

---

## ✅ Prerequisites

| You need | Details |
|---|---|
| **Unity `6000.3.8f1`** (Unity 6.3) | Install this exact version via **Unity Hub → Installs → Install Editor → Archive**. A different version may force an upgrade and break asset imports. |
| **Git** | Any recent version. |
| **Git LFS** | Required — see the warning above. Install from <https://git-lfs.com>, then run `git lfs install` once. |
| **Repo access** | If this GitHub repository is **private**, you must be invited as a collaborator before you can clone. |
| **Disk space** | A few GB once Unity builds its local `Library/` cache on first open. |

> **No extra setup needed:** all required packages (URP, Input System, AI Navigation, etc.) are
> listed in `Packages/manifest.json` and download automatically the first time Unity opens the project.

---

## 📥 Get the project

```bash
git lfs install
git clone https://github.com/Alessandrovanreusel/My-project.git
cd "My-project"
git lfs pull
```

If you cloned **before** installing Git LFS, fix it without re‑cloning:

```bash
git lfs install
git lfs pull
```

---

## ▶️ Open & run

1. Open **Unity Hub** → **Add** → select the cloned project folder.
2. Make sure it opens with editor version **`6000.3.8f1`** (Hub will warn if it's missing — install it from the Archive).
3. First open takes a few minutes while Unity imports assets and builds the `Library/` cache. This is normal, not an error.
4. In the **Project** window, open **`Assets/Scenes/SampleScene.unity`**.
5. Press **Play** (the ▶ button at the top).

---

## 🎮 Controls

Keyboard + mouse (a gamepad is also bound for every action):

| Action | Input |
|---|---|
| Move | **W A S D** / Arrow keys |
| Look around | **Mouse** |
| Sprint | Hold **Left Shift** |
| Jump | **Space** |
| **Enter Photo Mode** (raise camera) | **Hold Right Mouse Button** — release to lower |
| Zoom (while in Photo Mode) | **Mouse scroll wheel** |
| **Capture a photo** (while in Photo Mode) | **Left Mouse Button** |

---

## 🧪 What works in this slice

- **Walk the town** on a baked NavMesh world.
- **Photo Mode**: hold Right Mouse to raise the camera, scroll to zoom/compose, Left‑click to capture (shutter SFX + screen flash).
- **Event engine (Story 1.6)**: a placeholder **stub event** — a small cube near the player spawn — runs its `Spawn → Build → Peak → WindDown → Despawn` lifecycle on a loop, proving the engine that future town events (starting with the *Town Drunk*) will be built on.

The real characters/events that use this engine are still upcoming stories.

---

## 🛠️ Troubleshooting

| Symptom | Cause & fix |
|---|---|
| Models missing, materials **pink**, no animations | Git LFS wasn't active when you cloned. Run `git lfs install` then `git lfs pull`. |
| Unity Hub says the version is wrong / wants to upgrade | Install **exactly `6000.3.8f1`** from the Unity Hub **Archive** and open with it. |
| `git clone` asks for credentials / "repository not found" | The repo is private — you need a collaborator invite and to be signed in to GitHub. |
| LFS download fails or is very slow | GitHub's free LFS tier is limited (1 GB storage + 1 GB/month bandwidth). If many people clone, the quota can throttle downloads. |
| Console shows one Unity Version Control warning | Harmless infrastructure notice — safe to ignore. |

---

## 📁 Project layout (high level)

```
Assets/
  Scenes/        SampleScene.unity  ← the playable slice; open this
  Scripts/       Player controller, photo-mode camera, Core utilities, Events/ engine
  Models/        Character + world FBX models (Git LFS)
  Animations/    Animation clips, incl. the Male Drunk Pack (Git LFS)
  Data/          ScriptableObject assets (event definitions, event channels)
  Prefabs/       Reusable objects (e.g. the stub event actor)
  Settings/      URP render pipeline assets (PC + Mobile tiers)
ProjectSettings/ Unity project configuration
Packages/        Package manifest (dependencies download automatically)
```

---

## ℹ️ Notes for contributors

- **What is *not* in git (and you don't need it to run):** the `CAMERA GAME SHARED FOLDER/` of raw
  source art (Blender `.blend`, master FBX — one file exceeds GitHub's 100 MB limit). Only the
  *imported* pieces under `Assets/` are tracked. Project‑management/planning files
  (`_bmad-output/`, `.claude/`, `CLAUDE.md`) are also intentionally ignored.
- **Binary assets use Git LFS** (see `.gitattributes`): `.fbx`, `.png`/`.jpg`/`.tga`/`.psd`,
  `.wav`/`.mp3`/`.ogg`, `.blend`, etc. Keep LFS installed when adding new art.
- **Engine / pipeline:** Unity 6.3, Universal Render Pipeline (URP). Do not introduce the legacy
  Standard shader — it won't render under URP.
