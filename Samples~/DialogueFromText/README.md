# Dialogue From Text (sample)

A runtime **Game Creator 2** feature built with this fork's MCP tools — included as a
**sample** because it depends on the GC2 **Dialogue** and **Variables** modules (the rest of
the package has no GC2 dependency, so these files live in `Samples~/` and are not compiled
into the package).

It turns a plain-text script into a real, **branching, playable GC2 Dialogue** at runtime,
with speakers resolved **by name** from a Name Variables "cast".

## Files
- **`InstructionBuildDialogueFromText.cs`** — a GC2 `Instruction`. Parses an indentation
  script into a `Dialogue` on a target GameObject and (optionally) plays it.
- **`ValueActor.cs`** — a GC2 variable `Value` type that lets a Name Variable hold a Dialogue
  `Actor`. (GC2 ships `ValueQuest`, `ValueItem`, … but never shipped one for `Actor`.)
- **`ActorBook.cs`** — a small helper: `ActorBook.Get(cast, "Name")` resolves an Actor by
  variable name from a Global or Local Name Variables collection.

## Install
Copy the three `.cs` files anywhere under your project's `Assets/` (they compile into
`Assembly-CSharp`, which can see the GC2 Dialogue/Variables/Core assemblies). Requires GC2
Core + Dialogue.

## Script format
```
Thief: So you finally tracked me down.      // "Name:" → that actor speaks
Player: It wasn't hard.
? What do you do?                            // "? " → a Choice prompt
    * Demand the cache                       // "* " → a player option
        Thief: Threats don't work on me.     // indent (4 spaces) → that option's branch
    * Walk away
        Thief: Smart.
```

## The "cast" (speakers by name)
A **cast** is just a `LocalNameVariables` (or `GlobalNameVariables`) whose entries map a
speaker name to an Actor:

| Variable name | Value (`ValueActor`) |
|---|---|
| `Thief` | some Actor asset |
| `Player` | some Actor asset |

On the instruction:
- **Cast** — a GameObject whose Name Variables hold the actors (usually `Self`).
- **Shared Cast** *(optional)* — a `GlobalNameVariables` asset used as a fallback for
  speakers not found in Cast.
- **Script** — the text, or a String Name Variable holding it.
- **Skin** — the Dialogue Skin; leave empty to auto-pick one.

You can author all of this over MCP with this package's tools — e.g.
`gc2_add_name_variable` (now sets game-object / asset / string values, and updates existing
variables) to build the cast and the script variable, and `gc2_add_instruction` /
`gc2_set_instruction_game_object` to drop in and wire the instruction.

## License
MIT, consistent with this package. © 2026 wethecom. Not affiliated with or endorsed by
realvirtual GmbH or the Game Creator team.
