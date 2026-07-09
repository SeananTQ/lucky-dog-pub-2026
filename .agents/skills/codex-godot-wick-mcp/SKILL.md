---
name: codex-godot-wick-mcp
description: General Godot 4 project workflow for using Wick MCP safely and efficiently. Use when Codex needs to inspect Godot .tscn scene trees, verify node paths or exported bindings, run or stop scenes through the Wick editor bridge, diagnose runtime exceptions, or reason about Wick MCP process and connection behavior.
---

# Codex Godot Wick MCP

Use Wick MCP for Godot scene inspection and editor/runtime validation.
Keep implementation work grounded in normal tools: read files, edit files, search with `rg`, and build/test with the project's normal commands.

Tool namespace names may differ by session, such as `mcp__wick` or `mcp__wick_2`.
Use whichever Wick MCP tools are exposed in the current thread.

If this skill folder includes a project-specific note next to `SKILL.md`, read it only when working in that project. Keep project paths, smoke-test scenes, and local MCP config details out of the generic skill body.

## Connection Model

Prefer a lazy bridge workflow:

- Starting an AI client session may start a `Wick.Server` process, but it should not need to connect to Godot immediately.
- `scene_get_tree` reads `.tscn` files directly and should not require or connect to Godot.
- `editor_status` reports current connection state and should not connect to Godot.
- `editor_connect`, `editor_run_scene`, and other live editor/runtime tools may connect to Godot.
- If `Wick.Server.exe` is killed, the current MCP transport may stay closed until the user opens, restarts, or forks a thread.

Do not treat multiple idle `Wick.Server` processes as a bug by itself. The bug is repeated Godot bridge connection spam.

## Preferred Workflow

1. Start with file inspection and `scene_get_tree` when checking `.tscn` structure, node paths, node types, or scene references.
2. Prefer direct file edits for `.cs`, `.gd`, and `.tscn` unless a project explicitly trusts Wick scene mutation tools.
3. Build with the project's normal command, for example:

   ```powershell
   dotnet build .\path\to\Project.csproj
   ```

   For non-C# Godot projects, use the project's established validation command instead.

4. Use live editor/runtime bridge tools only when needed:
   - `editor_connect target=editor` when you explicitly need the editor bridge.
   - `editor_run_scene scenePath=res://...` to launch a test scene through Godot.
   - `runtime_get_exceptions` after scene launch to check runtime errors.
   - `editor_stop` to clean up after a launched scene.

5. Report verification precisely:
   - "Build passed" only means C# compilation succeeded.
   - "Scene launched/runtime exception buffer clean" means Wick/Godot returned no captured exceptions.
   - "Visually inspected" only if an actual visual inspection happened.

## Scene Tool Guidance

Use `scene_get_tree` for reliable checks such as:

- Confirming a node exists before changing code that references it.
- Checking node type after scene edits.
- Verifying paths used by `GetNode`, exported `NodePath` bindings, or `.tscn` structure changes.

Conservative defaults:

- Treat `scene_get_tree` as the most reliable scene MCP tool.
- Prefer direct `.tscn` text edits if `scene_add_node`, `scene_save`, or other mutation tools are known flaky in the current project.
- Be cautious with `project.godot` changes; some editor/project settings may require the user to change or reload them in Godot.

## Runtime Checks

For a quick MCP smoke test, use a small known-good scene from the current project:

1. `scene_get_tree` with the scene's absolute or project-relative `.tscn` path.
2. Confirm the expected root node, node count, and key children.
3. `editor_status`; before live bridge use, `editorConnected=false, runtimeConnected=false` is often expected.
4. `editor_connect target=editor` only when intentionally testing live bridge connection.
5. `editor_run_scene scenePath=res://path/to/scene.tscn`.
6. `runtime_get_exceptions limit=20 includeEnrichment=true`; expect an empty exception list for a clean smoke test.
7. `editor_stop`.

If Godot prints `[Wick] MCP client connected` exactly when `editor_connect` or `editor_run_scene` happens, that is expected. Repeated connection spam while idle is not expected.

## Fallbacks

If a Wick tool returns `Transport closed`:

- Do not keep retrying the same MCP call in the same thread.
- Continue with direct file reads/edits and `dotnet build` where possible.
- Tell the user the current thread's Wick MCP was killed or disconnected and needs a new/restarted/forked thread to revive.

If NuGet audit warnings block Wick builds, do not modify project policy casually. For one-off verification, a command-line property such as `/p:NuGetAuditLevel=critical` may be used and should be mentioned in the result.
