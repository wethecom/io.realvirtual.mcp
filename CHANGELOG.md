# Changelog

All notable changes to this package.

## Fork (wethecom) — Game Creator 2 tools

### Added
- First-class **Game Creator 2** authoring over MCP (`Editor/GameCreator2/`): manage
  `InstructionList`s, set `[SerializeReference]` fields (Events/Conditions/Properties),
  copy components & GameObject subtrees, duplicate assets, clean missing scripts.
- Full **CRUD** for GC2 Name Variables: `gc2_add_name_variable` (create/update — sets
  game-object / asset / string / number / bool values), `gc2_list_name_variables` (read),
  `gc2_remove_name_variable` (delete).
- `gc2_create_quest` and `gc2_create_dialogue`.
- Sample: **Dialogue From Text** (`Samples~/DialogueFromText`) — parse a text script into a
  branching, playable Dialogue with speakers resolved by name from a Name Variables cast
  (adds a `ValueActor` variable type).

## [1.0.2] - 2026-03-10

### Changed
- Updated documentation and Asset Store links

## [1.0.1] - 2026-03-05

### Added
- PNG toolbar icons replacing emoji-based status indicators
- Version system with `McpVersion.cs` central version constant
- Prefab editing support (open, save, close, stage info)
- Asset Store Publishing Tools validation integration
- Update instructions in README
- Support disclaimer in README

### Changed
- Python server deployment switched from zip download to git clone/pull for easier updates

## [1.0.0] - 2026-03-03

### Added
- Initial release of the realvirtual MCP Server
- WebSocket bridge between Unity Editor and MCP protocol
- 90+ built-in tools for scene, GameObject, component, transform, simulation, and screenshot control
- Auto-discovery of `[McpTool]` attributed methods via reflection
- Self-contained Python 3.12 distribution (no system Python required)
- One-click setup from Unity toolbar
- Multi-instance support with automatic port allocation
- Domain reload survival with auto-reconnect
- Authentication token system for secure connections
- Toolbar status indicator (gray/yellow/green) with activity label
