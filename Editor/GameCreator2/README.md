# Game Creator 2 tools (fork addition)

> These files are an **addition** in this fork of
> [`game4automation/io.realvirtual.mcp`](https://github.com/game4automation/io.realvirtual.mcp).
> They are **not** part of the upstream realvirtual MCP Server. Everything else in
> this package is the unmodified upstream work (MIT, © realvirtual GmbH).

Custom MCP tools that let an AI agent author **Game Creator 2** data and copy
components/GameObjects over MCP — the things the generic `component_get` /
`component_set` tools can't do, because GC2 stores most of its logic in
`[SerializeReference]` polymorphic fields.

They self-register via the package's `McpToolRegistry` (which scans all assemblies
for `[McpTool]` methods), so no further wiring is needed. They live in the
`io.realvirtual.mcp.editor` assembly (which already references Newtonsoft.Json and
the runtime assembly).

## Tools

`Gc2InstructionListTools.cs`
- `gc2_add_instruction` — append a GC2 `Instruction` to an `InstructionList` field (e.g. `CarEntry.onEnter`, `Trigger.m_Instructions`); optionally set scalar/enum fields.
- `gc2_list_instructions` — list a list's instructions (read-only; safe alternative to `component_get`).
- `gc2_remove_instruction` — remove by index, or clear with `index: -1`.
- `gc2_set_instruction_game_object` — set a `PropertyGetGameObject` field to `self`, `player`, or a scene object by path.
- `gc2_set_managed_reference` — set ANY `[SerializeReference]` field by property path to a named type (e.g. a `Trigger`'s `m_Event` to an `Event` subclass).

`McpComponentCopyTool.cs`
- `component_copy` — copy a whole component (values + object refs) between GameObjects via Unity's native CopyComponent/Paste.
- `game_object_copy` — copy a whole GameObject subtree into a target parent (preserving local transform) via Object.Instantiate.

## Requirements
- This package (provides the `[McpTool]` attribute, `ToolHelpers`, `McpTypeResolver`).
- **Game Creator 2** in the project (the GC2 tools operate on GC2 types; matched by
  name, so no hard compile dependency, but only useful with GC2 present).

## Gotchas
- Call your MCP `editor_save_scene` after any write tool (they mark the scene dirty).
- Ambiguous type names like `Trigger` make the resolver throw — pass the
  fully-qualified `GameCreator.Runtime.VisualScripting.Trigger`.
- Tool names are the method names in `snake_case`; acronyms split
  (`Gc2SetInstructionGameObject` → `gc2_set_instruction_game_object`).

## License
MIT, consistent with this package. These additions: © 2026 wethecom. Not affiliated
with or endorsed by realvirtual GmbH or the Game Creator team.
