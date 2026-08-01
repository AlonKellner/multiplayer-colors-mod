# Multiplayer Colors

A small Slay the Spire 2 mod. When two or more players in a co-op run pick the **same character**, it gives
each of them a slight, distinct colour variation so they're tellable apart at a glance — while still
obviously being the same character.

Nothing changes in single-player, and nothing changes when everyone picked a different character.

## What it does

The base game already does this for monsters: `NCombatRoom.RandomizeEnemyScalesAndHues()` jitters the scale
and hue of duplicate monsters so you can tell three Jaw Worms apart. It explicitly skips players. This mod
is the players' half of that idea.

When N players share a character, each gets one of four variations, assigned by ascending run slot index:

| Ordinal | Variation |
|---|---|
| 0 | Brighter |
| 1 | Darker |
| 2 | Warmer (hue +) |
| 3 | Cooler (hue −) |

Applied to:

- the character's body in **combat**
- the figure at the **rest site**
- the figure in the **shop**
- the arm sprites in the **treasure room**, including the rock-paper-scissors relic fight
- the character **portrait** in the top bar and the multiplayer party strip
- the player's **map ink** and **map pings**
- the **remote targeting line** drawn during another player's turn

Nothing else is touched — cards, energy orb, health bars and UI chrome all render normally.

## Determinism

Colours are a pure function of `(character, slot index, run roster)` — no RNG, no dependence on the local
player, and never on node or display order. Several of the game's UIs deliberately reorder the roster so the
local player comes first, so a colour keyed on display position would come out differently on every client.
Keying on `RunState.GetPlayerSlotIndex` — the game's own network-authoritative ordinal — means all four
players see the same person in the same colour.

## Tuning

All the constants live at the top of [src/PlayerTint.cs](src/PlayerTint.cs). If a variation reads too
strongly or too weakly in game, change them there; nothing else needs to move.

## Building

Requires .NET 9 and a local Slay the Spire 2 install (the project compiles against the game's own
`sts2.dll`, `0Harmony.dll` and `GodotSharp.dll`, so there's no version skew).

```bash
dotnet build MultiplayerColors.csproj      # also deploys into the game's mods/ folder
dotnet test tests/MultiplayerColors.Tests.csproj
```

`dotnet build` copies `MultiplayerColors.dll` + `MultiplayerColors.json` into the game's `mods/` folder for
the local iteration loop. Pass `-p:CopyToModsFolder=false` to skip that.

The game path is discovered per-OS in [Sts2PathDiscovery.props](Sts2PathDiscovery.props); override with
`-p:Sts2Path=...` if your install lives somewhere unusual.

This is a DLL-only mod — no `.pck`, no Godot project, no export step, and no BaseLib dependency.

## Tests

`tests/` covers the two things that can break silently:

- **`PlayerTintTests`** — the roster logic and the colour maths, including that assignment follows slot
  index rather than list order.
- **`PatchTargetTests`** — resolves every `[HarmonyPatch]` target the same way Harmony does, and checks each
  patch parameter against the target's real signature. A game update that renames one of the patched methods
  fails here instead of shipping a mod that quietly does nothing.
