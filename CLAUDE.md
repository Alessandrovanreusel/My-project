# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Communication Style

The developer on this project is a junior learning game development. Always communicate as a senior developer mentoring them: explain the "why" behind decisions, teach concepts along the way, and be patient. Don't just give code — help them understand what it does and why it's the right approach.

## Project Overview

Unity 6 (6000.3.8f1) game project using the **Universal Render Pipeline (URP)**. Early-stage development with character models and environment assets imported, but minimal custom game code. Target platform is Windows 64-bit (StandaloneWindows64).

## Unity MCP Integration

This project is configured with **MCP for Unity** (`com.coplaydev.unity-mcp`), enabling direct Unity Editor interaction from Claude Code. Use MCP tools for:
- Scene hierarchy queries and GameObject management
- Script creation/editing and compilation monitoring
- Play mode control, asset management, and test execution
- Always check `read_console` after script changes to verify compilation

## Verifying Your Own Work — REQUIRED

Before telling the developer that something works, **see it working**. A clean compile proves only
that the code parses; a check you wrote yourself proves only that the code agrees with your own
assumptions. Neither is evidence.

**You have full creative freedom in how you test.** Invent whatever rig the feature deserves —
screenshots are one technique, not the method. Sample an `AudioSource` over distance to prove a
sound really fades; log a transform every frame to prove something moved; draw gizmos; replay
recorded input; diff two renders; script a whole scenario world. Nobody is going to hand you a
procedure. Look at the feature, decide what would actually convince you it works, and build that.

**Observe the running game and capture what it produced.**
- Build a small, isolated world containing just the thing under test, script the actors and camera
  into exact positions, trigger the real behaviour, and save the result.
- Read the captures back and look at them. Where a system computes something invisible (a score, a
  hitbox, a range), **draw that value onto the captured image** — it turns "does this number look
  right?" into "is the box on him?", which you can actually answer.
- Prefer an editor `[MenuItem]` so a run is one `execute_menu_item` call and repeatable.

**Drive the real path, never a re-implementation.** Call the same method the player's input calls. A
bench that re-creates the logic tests your copy of it, not the game. Play mode over edit mode wherever
behaviour differs — Animators, physics, and pooling do not run in edit mode, and a subject frozen in
bind pose measures nothing like the animated one.

**Do not encode expected results.** Assertions bake in what you already believed and go green while
the feature is wrong. Produce evidence, then judge it. Ask: is this what a player would see? Is this
what the story asked for? If the answer is no, change the code and run it again.

**Alexv is the second opinion, not the first.** Do not hand him a list of things to go and try. Bring
him a conclusion and the evidence behind it. Say plainly which parts you verified and which you could
not — never present a structural check as though it were a played one.

Working example: `Tools > Grading > Photo Shoot (Play)` — `Assets/Scripts/Editor/PhotoShootRig.cs`
builds the world, `Assets/Scripts/PhotoMode/PhotoShootRunner.cs` drives the shutter.

**Traps already paid for — do not rediscover these:**
- A MonoBehaviour in an **Editor-only assembly** cannot be resolved on a scene object when entering
  play mode ("referenced script (Unknown) is missing"), and the rig silently does nothing while the
  scene runs on around it. Put runners in the runtime assembly behind `#if UNITY_EDITOR`.
- `ScreenCapture.CaptureScreenshot` grabs the Game View backbuffer, which does not repaint its 3D
  content while the editor runs unattended. Render the camera to a `RenderTexture` instead.
- Assets loaded **before** `EditorSceneManager.NewScene(..., Single)` have their native half unloaded:
  fields still read fine from managed memory while `== null` is simultaneously true.
- Editing `ProjectSettings/*.asset` on disk does not take effect while the editor is running — use the
  editor API.
- A rig that logs its own errors trains you to ignore errors — keep its console clean too.

## Jira Sync (BMad epics & stories) — REQUIRED

This project's planning artifacts are mirrored to Jira. **Whenever you create, update, or
re-validate epics or stories** — via any BMad/GDS workflow (`gds-create-epics-and-stories`,
`gds-create-story`, `gds-correct-course`, `gds-sprint-planning`, etc.) or by editing
`_bmad-output/planning-artifacts/epics.md` or story files — you **must also reflect those
changes in Jira** in the same session.

**Jira target (do not guess):**
- Site: `alessandrovanreusel.atlassian.net`
- Cloud ID: `5b116b91-787f-4ff7-9668-2cd92d337bcf`
- Project key: **KAN** (team-managed / next-gen software project)
- Issue types: epics → `Epic`, stories → `Story`, **story Tasks/Subtasks → `Subtask`**
- Story→Epic link: set the story's **`parent`** field to the epic's key (team-managed projects
  use `parent`, NOT a separate "Epic Link" custom field)
- Subtask→Story link: set the subtask's **`parent`** field to the story's key (issuetype name
  `Subtask`, id `10002`)
- KAN workflow transition IDs (shared by all issue types): `11`=To Do, `21`=In Progress,
  `31`=Wordt beoordeeld (review), `41`=Gereed (Done)

**Source of truth for the file↔Jira mapping:** the `jiraSync:` block in the frontmatter of
`epics.md` lists every epic/story (and the `subtasks:` sub-list) with its Jira key. Always read
it before acting so you **update existing issues instead of creating duplicates**. After any
change, update that block.

**Two standing requirements (the user explicitly asked for both — always do them):**
- **Create subtasks too.** Whenever a story has a Tasks/Subtasks breakdown, mirror each
  top-level Task as a Jira **Subtask** under that story. Set each subtask's status to match the
  story file (done Tasks → Done / `41`; deferred → leave at To Do). Only do this for stories
  that actually have a detailed task breakdown (a story file exists).
- **Plain-language comments on every issue.** Add a comment to every Epic, Story, and Subtask
  starting with **"In plain terms (for non-developers):"** followed by a 1–2 sentence,
  jargon-free explanation of what it does, so a non-developer can read the board. Base it on the
  actual GDD/epics content — never invent. Do this as part of the same sync for any new issue.

**Procedure each time epics/stories change:**
1. Confirm the Atlassian MCP tools are available (search tools for `mcp__atlassian__*`). If they
   are not, tell the user to run `/mcp` and authenticate the `atlassian` server first — do not
   silently skip the sync.
2. Read the `jiraSync:` mapping in `epics.md`.
3. For each epic/story: **create** it if it has no key yet; **update** (summary/description) the
   existing issue if its content changed; keep story `parent` links pointing at the right epic.
4. Put acceptance criteria in each story's Description (markdown `contentFormat`).
5. For each story with a task breakdown, **create/update its Subtasks** (parent = the story) and
   set their statuses to match the story file.
6. **Add the "In plain terms (for non-developers):" comment** to every new Epic/Story/Subtask.
7. Verify with a JQL check (e.g. `parent = <epicKey>` for stories, `parent = <storyKey>` for
   subtasks), then write the keys back into the `jiraSync:` block (including `subtasks:`).

Treat the file and Jira as one unit of work — never leave them out of sync at the end of a turn.

## Project Structure

- `Assets/Scenes/` - Scene files (SampleScene.unity, Video Camera Game 2.0.unity)
- `Assets/Models/` - FBX models (game scneen.fbx, main characters.fbx)
- `Assets/Scripts/` - Game scripts (ThirdPersonController.cs, ThirdPersonCamera.cs)
- `Assets/Scripts/Editor/` - Editor utilities (AddMeshColliders.cs, ColorCharacter.cs)
- `Assets/Input/` - Input configuration (InputSystem_Actions.inputactions)
- `Assets/Materials/` - Materials
- `Assets/Settings/` - URP render pipeline assets (separate Mobile and PC configurations)

## Key Technical Details

- **Render Pipeline**: URP 17.3.0 with dual renderer configs (`PC_RPAsset`, `Mobile_RPAsset`)
- **Input System**: New Input System (1.18.0) with Player action map (Move, Look, Attack, Interact, Crouch, Jump, Sprint, Previous/Next)
- **Packages**: AI Navigation, Visual Scripting, Timeline, Authentication Services
- **Version Control**: Plastic SCM (see `ignore.conf`)
- **IDE**: VS Code with Unity debugger attached via `vstuc`

## Development Commands

No custom build scripts exist. Standard Unity workflows apply:
- **Build**: File > Build Settings in Unity Editor, or use `manage_editor` MCP tool for play mode
- **Tests**: Use `run_tests` MCP tool (test framework 1.6.0 is installed) or Unity Test Runner window
- **Script compilation**: Automatic on save; verify via `read_console` MCP tool

## Architecture Notes

- **Character movement**: `ThirdPersonController.cs` uses CharacterController + PlayerInput (SendMessages mode) for WASD movement, jumping, sprinting
- **Camera**: `ThirdPersonCamera.cs` is a chase camera that follows behind the character with smooth lerp
- **Input**: PlayerInput component on "Main Characters" uses the "Player" action map from InputSystem_Actions
- **World colliders**: 16,321 MeshColliders added via editor tool (`Tools > Add MeshColliders to World`)
- **Important**: The World FBX has an embedded Blender camera that must stay disabled (it overrides Main Camera due to higher depth)
- URP shaders must be used (Standard shader will not work with this pipeline)
- Two render pipeline assets exist for quality tiers: use `PC_RPAsset` for desktop, `Mobile_RPAsset` for mobile


