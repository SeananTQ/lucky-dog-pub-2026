---
name: codex-godot-wick-mcp
description: Wick MCP Troubleshooting Guide. Use when MCP bridge is abnormal, tool calls fail, or Godot build/runtime diagnostics are needed.
---

# Godot Wick MCP

## Quick Start

Use the Wick MCP tools first when working on Godot scenes or runtime/build diagnostics in this project.

1. Check the bridge:

```text
mcp__wick.editor_status
```

Interpretation:

```json
{"editorConnected":true,"runtimeConnected":false}
```

`editorConnected: true` means the Godot editor bridge is available. `runtimeConnected: false` is normal before a game scene is running or before the Wick.Runtime companion is connected.

2. Confirm the configured project:

```text
mcp__wick.project_info(projectGodotPath: ".../mcp-05/project.godot")
```

This repository's active Wick config is in `.mcp.json`. It points `WICK_PROJECT_PATH` at `mcp-05` and `WICK_GODOT_BIN` at the local Godot 4.6.3 mono executable.

3. Inspect scenes with Wick:

```text
mcp__wick.scene_get_tree(scenePath: ".../mcp-05/scenes/test_mcp.tscn", includeProperties: true)
```

Use this to verify node paths, exported NodePath fields, theme overrides, and scene structure after edits.

## Scene Editing

Prefer Wick scene tools for Godot-native scene CRUD when they are working:

```text
mcp__wick.scene_add_node
mcp__wick.scene_set_node_properties
mcp__wick.scene_get_node_properties
mcp__wick.scene_save
```

If a Wick tool returns `Transport closed`, first retry a small status call such as `editor_status`. If the MCP bridge is still unavailable, it is acceptable to edit `.tscn` files directly with `apply_patch`, then validate with `scene_get_tree` once Wick recovers.

When manually wiring exported C# node references in `.tscn`, match Godot's serialized form:

```text
[node name="Root" type="Control" node_paths=PackedStringArray("_label", "_button")]
script = ExtResource("1_script")
_label = NodePath("Label")
_button = NodePath("Button")
```

Add C# scripts directly under `mcp-05/scripts/` using project naming conventions. Godot may generate matching `.cs.uid` files; do not delete user or editor generated files.

## Running Scenes

Run a specific scene from the editor bridge:

```text
mcp__wick.editor_run_scene(scenePath: "res://scenes/test_mcp.tscn")
```

Then check:

```text
mcp__wick.editor_status
mcp__wick.editor_scene_tree(target: "editor")
```

`editor_run_scene` returning `success: true` proves the editor accepted the play command. `editor_scene_tree` can confirm that the scene is loaded in the editor. Runtime tools such as `runtime_query_scene_tree` require a running game with the Wick.Runtime companion; without it, expect `no_live_bridge`.

Codex cannot visually inspect the Godot window from Wick MCP alone. For visual verification, ask the user for a screenshot or use a screenshot/image workflow if one is available.

## Build Verification

Build from the active Godot project directory:

```powershell
dotnet build mcp05.csproj
```

This project uses:

```xml
<Project Sdk="Godot.NET.Sdk/4.6.3">
```

If build fails because `Godot.NET.Sdk` cannot be resolved or NuGet cannot load `https://api.nuget.org/v3/index.json`, retry the same `dotnet build` with escalated permissions/network access. That is an environment/package resolution issue, not necessarily a C# code error.

For C# diagnostics, prefer Wick build tools when available:

```text
mcp__wick.dot_net_build
mcp__wick.build_diagnose
```

## Project-Specific Notes

Active test scene:

```text
mcp-05/scenes/test_mcp.tscn
```

Active C# project:

```text
mcp-05/mcp05.csproj
```

Follow the repository's existing Godot conventions:

- C# only, no GDScript for gameplay code.
- Scene files use `snake_case.tscn`.
- C# classes and filenames use `PascalCase`.
- Use `[Export]` for editor-wired node references when the task asks for exported connections.
- Validate scene structure with Wick and C# compilation with `dotnet build`.
