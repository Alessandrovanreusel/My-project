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


