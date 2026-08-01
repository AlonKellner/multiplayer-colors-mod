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
- the little character **head icons** marking each player's vote on map nodes (and on treasure-room and
  event votes), plus the single-player map marker
- the player's **map ink** and **map pings**
- the **remote targeting line** drawn during another player's turn

Nothing else is touched — cards, energy orb, health bars and UI chrome all render normally.

## Determinism

Colours are a pure function of `(character, slot index, run roster)` — no RNG, no dependence on the local
player, and never on node or display order. Several of the game's UIs deliberately reorder the roster so the
local player comes first, so a colour keyed on display position would come out differently on every client.
Keying on `RunState.GetPlayerSlotIndex` — the game's own network-authoritative ordinal — means all four
players see the same person in the same colour.

## Testing all four variations solo

You need four people sharing a character to see all four colours naturally. The `tint` dev console command
forces one on yourself instead:

```
tint                # report the current setting
tint brighter       # force a variation on yourself
tint darker
tint warmer
tint cooler
tint off            # no tint at all
tint auto           # back to normal (only players sharing a character get tinted)
```

It recolours what's already on screen, so you don't have to change rooms to see the effect. It applies to
**you only** — teammates stay on the normal rule, so you can hold a forced colour next to a real one — and
it's local, so it never changes what anybody else sees.

## Tuning

The strength dial is two constants at the top of [src/PlayerTint.cs](src/PlayerTint.cs):
`BrightnessGain` for brighter/darker and `ChannelTilt` for warmer/cooler. Each variation's opposite is built
as the reciprocal, so the pairs stay symmetric around vanilla however you tune them. Flat colours (map ink,
pings, targeting lines) have their own constants below those, and they are genuinely independent — don't
resync them. Art and ink need different amounts of the same shift: `HueStep` runs far hotter than
`ChannelTilt` because map ink is drawn as thin strokes on a busy parchment map, where a hue difference that
is obvious across a whole character sprite disappears completely.

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
- **`TintConsoleCmdTests`** — the `tint` command's parsing, and that `TintOverride` stays aligned with
  `PlayerVariation` (they're bridged by an enum cast that would silently pick the wrong colour if they
  drifted apart).
- **`PatchTargetTests`** — resolves every `[HarmonyPatch]` target the same way Harmony does, and checks each
  patch parameter against the target's real signature. A game update that renames one of the patched methods
  fails here instead of shipping a mod that quietly does nothing.
