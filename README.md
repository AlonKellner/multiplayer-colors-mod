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

## Mod support

Modded characters work with no registration, no list to add to, and no per-character data in this mod.
Nothing is hard-coded:

- **Who counts as a duplicate** is `player.Character.Id`, so any `CharacterModel` — including BaseLib
  custom characters — participates automatically.
- **Art tints** are per-channel multipliers applied to whatever sprite the character supplies, so they work
  on art this mod has never seen.
- **Flat colours** are derived from the character's own `MapDrawingColor` / `RemoteTargetingLineColor` by
  HSV transform, not looked up.
- **Every patch** targets a game node class, never a character type.

The only per-character colours anywhere in the repo are test fixtures and the thumbnail generator.

Because `MapDrawingColor` and `RemoteTargetingLineColor` are `virtual` with a `Colors.Black` default, a
modded character that never overrides them would otherwise collapse three of the four variations onto pure
black. The flat-colour transform guards against that: value is kept inside a band that leaves headroom in
both directions, and the hue variations floor saturation and value so a hue is actually visible. All five
shipped characters sit inside those bounds, so no base-game colour is affected — there's a test pinning
that. An unset black ink comes out as `#464646` / `#202020` / `#403529` / `#402935`.

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
resync them. Art and ink need different amounts of the same shift, because map ink is drawn as thin strokes
on a busy parchment map.

The ink hue shift is **not** a fixed angle. A fixed angle is badly biased: a hue rotation sweeps an arc
whose length scales with how chromatic the colour already is, and HSV hue is not perceptually uniform
(the green sector is compressed, reds and blues fan out fast). Across the five shipped characters a fixed
31° step landed anywhere from ΔE 4.6 to 31.8 — a 6.9× spread, felt as "too weak on Silent, too strong on
Ironclad". So `SolveHueStep` searches for whatever angle puts warmer and cooler a constant *perceptual*
distance apart, measured in OKLab. The dial is `TargetHueSeparation`, in ΔE, not degrees.

Muted, dark inks can't reach the target at any sane angle — their chroma is too low for hue rotation to
move them far. Those hit `MaxHueStep` and take the best available rather than being pushed until they stop
looking like themselves.

### Staying visible on the map

Map ink can also collide with the parchment itself: the Understudy's darker variant landed at `#BD9732`,
ΔE 7.3 from the overgrowth background, and effectively vanished. `MapBackgrounds` holds the mean colour of
each act's background, sampled from the shipped textures, and the brightness variations are slid along value
until they clear `MinMapContrast` of the nearest one.

Two deliberate limits on that guard:

- **Brightness variations only.** A hue variation keeps the character's own brightness exactly. If that sits
  near the parchment, it's the colour the character chose — not this mod's to override — and moving it would
  make "warmer" quietly mean "warmer and lighter" too.
- **Never better than vanilla.** The bar is `MinMapContrast`, or the character's own untinted ink where that
  is already closer to the background. Regent ships at ΔE 14.6; the job is to avoid making that worse, not
  to improve on a base-game colour.

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
