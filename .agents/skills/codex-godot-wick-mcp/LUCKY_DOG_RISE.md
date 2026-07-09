# Lucky Dog Rise Notes

Use this note only when applying `codex-godot-wick-mcp` inside the `lucky-dog-pub-2026` workspace.

## Paths

- Workspace: `G:/Workspace/godot-project/lucky-dog-pub-2026`
- Godot project: `G:/Workspace/godot-project/lucky-dog-pub-2026/lucky-dog-rise`
- Wick source: `G:/Workspace/godot-project/lucky-dog-pub-2026/wick-mcp-server/Wick`
- Codex MCP config: `G:/Workspace/godot-project/lucky-dog-pub-2026/.codex/config.toml`
- VS Code / Claude Code MCP config: `G:/Workspace/godot-project/lucky-dog-pub-2026/.mcp.json`

## Build

Use the game project's normal C# build:

```powershell
dotnet build .\lucky-dog-rise\LuckyDogRise.csproj
```

## Wick Smoke Test

Use `Scenes/TestDesktop.tscn` for a quick MCP smoke test:

1. `scene_get_tree` with `scenePath=G:/Workspace/godot-project/lucky-dog-pub-2026/lucky-dog-rise/Scenes/TestDesktop.tscn`.
2. Confirm `nodeCount=7` and root `TestDesktop / Node2D`.
3. `editor_status`; before live bridge use, expect `editorConnected=false, runtimeConnected=false` in a fresh idle thread.
4. `editor_connect target=editor`.
5. `editor_run_scene scenePath=res://Scenes/TestDesktop.tscn`.
6. `runtime_get_exceptions limit=20 includeEnrichment=true`; expect `exceptions=[]` and `totalBuffered=0`.
7. `editor_stop`.

## Project-Specific Notes

- `scene_get_tree` is the stable MCP scene tool used in this project.
- `scene_add_node` and `scene_save` have been unreliable here; edit `.tscn` text directly when needed.
- If `.godot/` is corrupted, delete `.godot/`, reopen Godot, and rebuild.
- Multiple idle `Wick.Server` processes may exist after opening/forking Codex threads; this is acceptable as long as Godot is not repeatedly printing bridge connection spam.
