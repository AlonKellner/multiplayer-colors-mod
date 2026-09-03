# Multiplayer Colors

A small Slay the Spire 2 mod. When two or more players in a co-op run pick the **same character**, it gives
each of them a slight, distinct colour variation so they're tellable apart at a glance — while still
obviously being the same character.

Nothing changes in single-player, and nothing changes when everyone picked a different character.

## What it does

The base game already does this for monsters: `NCombatRoom.RandomizeEnemyScalesAndHues()` jitters the scale
and hue of duplicate monsters so you can tell three Jaw Worms apart. It explicitly skips players. This mod
is the players' half of that idea.

When N players share a character, each gets one of four variations, assigned by ascending network id:

| Ordinal | Variation |
|---|---|
| 0 | Brighter |
| 1 | Darker |
| 2 | Warmer (hue +) |
| 3 | Cooler (hue −) |

Applied to:

- the character's body in **combat**
- their **companions** — Osty, Byrdpip, Pael's Legion, and the Regent's Sovereign Blade — which take their
  owner's colour rather than one of their own
- the figure at the **rest site**, companion included
- the figure in the **shop**
- the arm sprites in the **treasure room**, including the rock-paper-scissors relic fight
- the character **portrait** in the top bar and the multiplayer party strip
- the little character **head icons** marking each player's vote on map nodes (and on treasure-room and
  event votes), plus the single-player map marker
- an **outline in that player's map-drawing colour** on every one of those head icons and on their row in
  the party side panel — so a line drawn on the map can be traced back to whoever drew it
- the player's **map ink** and **map pings**
- the **remote targeting line** drawn during another player's turn
- a faint **aura** behind every one of those figures, in a colour that names the variation rather than the
  character — white, black, red, blue — and a handful of **particles** whose motion says the same thing


Nothing else is touched. Anything mechanical stays vanilla: cards, Defect orbs and orb evocation, power and
intent icons, health bars, targeting arrows, selection reticles, form-VFX auras and UI chrome. That falls
out of *where* the tint is applied — always the innermost art node — rather than from a list of exclusions,
so it holds for art this mod has never seen.

## Determinism

Colours are a pure function of `(character, network id, run roster)` — no RNG, no dependence on the local
player, and never on node, list or display order.

Ordering is by `Player.NetId`, a network identity that is the same value on every client *by construction*.
It deliberately does not use `RunState.GetPlayerSlotIndex`: that list is populated once from the lobby and
looks stable, but its order is consistent only by convention, and if it ever differed the whole lobby would
disagree about who is which colour. Nothing about a colour assignment needs to depend on list position.

## The aura

The tint is a **relative** signal — "this Ironclad is a fifth brighter than that one" — and the game
destroys it routinely. `NCombatRoom.PositionPlayersAndPets` assigns `Modulate = 0.5 grey` to back-row
players, so a *brighter* player standing behind can read darker than a *darker* player standing in front.
That is the exact comparison this mod exists to support.

So each tinted figure also carries a faint glow behind it, keyed to the variation rather than to the
character: **white** for brighter, **black** for darker, **red** for warmer, **blue** for cooler. A white
halo stays a light halo and a black halo stays a dark halo however hard the figure is dimmed, because the
reading is categorical rather than relative. This is the one place in the mod where *not* deriving from the
character is the right answer.

Art only. Icons, map ink, pings and targeting lines already carry the outline key, and a second signal
there would say nothing new.

It is drawn as a soft radial falloff in a `ColorRect` **behind** the figure, sized to the figure's real
box. There is no silhouette in it — the figure does the shaping, by covering the middle and leaving only
the fringe around its own outline visible.

An exact silhouette is not available here the way it is for icons. Almost every tinted surface is Spine
skeleton art, and the dilate-the-alpha trick [src/OutlineShader.cs](src/OutlineShader.cs) uses needs a
texture: sampling outside a Spine atlas region bleeds into whatever art is packed beside it. The only exact
option is reparenting each figure under a `CanvasGroup`, which changes how additive Spine slots composite
(Ironclad's fire, its eye flame, Regent's effects) and costs a render target per creature — too much to
risk for an effect that is meant to be barely perceptible.

Measuring the figure is a search rather than a lookup, since a `Node2D` has no bounding box in general:
the Spine runtime's own `get_bounds()` first, then a `TextureRect`'s art where it is actually drawn (the
treasure-room arm is letterboxed inside a rect nearly three times its own height, so the raw rect would put
the glow's peak off the bottom of the screen), then a scene-authored box like `%Bounds` in combat. Whichever
answered is printed by `tint diag`, because "no aura" and "aura in the wrong place" have different causes.

Two dials, both live: `tint aura <0-1>` for strength and `tint aura spread <0-1>` for reach.

### Particles

Colour alone is uneven. A black aura on a dark battlefield is much weaker than a white one, and red and
blue at low strength are the pair most easily confused. So each figure also carries a handful of motes
whose **motion** says the same thing:

| Variation | Pull | Opacity |
|---|---|---|
| Brighter | radially outward | 0.10 |
| Darker | radially inward | 0.40 |
| Warmer | up, like sparks off a fire | 0.20 |
| Cooler | down, like snow | 0.20 |

Four readings no background can flatten into one another. Opacity is per variation rather than shared,
because the four are not equally visible at equal alpha: white motes over a lit battlefield carry at a
tenth where black ones need four times that to read at all.

**Every variation spawns identically and differs only in which way it is then pulled.** Nothing is
launched, so a mote only ever moves the way its own variation pulls it. That is a fix, not a tidy-up:
emission used to vary too — the inward variation was born on the rim rather than in the cloud — and a mote
launched from the rim reaches the middle carrying all the speed the pull gave it, sails through, and swings
back out the far side.

The inward pull is solved rather than picked, so the fade and the arrival coincide: from `s = at²/2`,
`a = 2σ/L²` carries a mote born one sigma out exactly to the middle over one lifetime. Motes born closer
arrive early, and a radial pull keeps pointing at the centre after they pass it, so those would oscillate —
which is what the damping is for. No other variation has a point it converges on, and none of them has any.

The spawn cloud is a **radial gaussian**: dense on the figure, thinning outward with no edge anywhere.
Godot's built-in shapes cannot do that — `Sphere` is uniform through a disc and `SphereSurface` is a ring —
so the points are generated here and handed over as `EmissionShapeEnum.Points`. Deterministic from a fixed
seed, and deliberately not drawn from `RunState.Rng`: every client must generate the same cloud, and a
cosmetic effect has no business advancing a run's RNG stream.

They are `CpuParticles2D`, not `GpuParticles2D`: at a dozen particles the GPU path buys nothing and costs a
`ParticleProcessMaterial` per figure, while every knob this needs is a plain property on the CPU node. The
mote texture is a `GradientTexture2D` built in code — this mod ships no `.pck` and no assets, and without a
texture Godot draws each particle as a hard-edged square.

Every distance in the motion table is a fraction of the figure's radius rather than a pixel count, so one
description covers a combat body and a Sovereign Blade alike. That also forces `local_coords`: Godot
transforms emission positions and velocities by the emitter's transform but *not* accelerations, and a
SpineSprite is scaled around 0.28, so in global mode the darker variation's inward pull would come out
several times stronger than the frame it was sized against.

Three more dials: `tint particles <0-4>` (a multiplier on each variation's own opacity, so "a bit more
than that" is expressible without flattening the balance between the four), `tint particles count <n>` and
`tint particles size <n>`.

Size is a fraction of the figure's radius, and the default of 1 is larger than it sounds: the mote texture
is a radial gradient, so its visible core is a fraction of its quad, and at these opacities a hundred large
soft overlapping motes read as a haze around the figure rather than as a hundred objects.

One trap worth naming, since it shipped once: `CpuParticles2D.ScaleAmount` is a *multiplier on the
texture*, not a size in units. Handing it a radius-derived size blows a 32px texture up by the figure's
radius, which on a Spine body — whose local units are several screen pixels each — makes the motes
enormous. `AuraParticles.ScaleFor` divides by the texture's own resolution, which also means the mote art
can be made sharper without silently resizing every particle in the mod.

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

tint outline        # report icon outline thickness
tint outline 3      # set it, in pixels (0-12, default 3; 0 hides the outline)
tint aura           # report aura strength and spread
tint aura 0.18      # set the strength (0-1, default 0.18; 0 hides the aura)
tint aura spread .25 # set how far it reaches past the figure
tint particles      # report particle strength and count
tint particles 1    # multiplier on every variation's own opacity (0-4, default 1)
tint particles count 100
tint particles size 1      # mote size, as a fraction of the figure's radius (default 1)
tint icon           # report which art the solo map pin uses
tint icon character # show the real co-op vote icon, to judge the multiplayer look solo
tint icon marker    # back to the normal solo pin
tint diag           # what the mod has done, plus every icon's size, colour and outline state
tint diag on        # also log routine activity as it happens
```

`tint icon character` does not imitate the co-op icon — it instantiates the game's own
`ui/multiplayer_vote_icon` scene and assigns the same two textures `NMultiplayerVoteContainer` does,
then runs the identical tint path. The icon renders exactly as it does in co-op. Only its placement
differs, unavoidably: in co-op these sit in a row under a map point, one per voting player, while
this rides the solo marker as it hops between nodes.

`tint diag` always writes its report to the game log as well as the console, so there is never
anything to transcribe by hand. It prints one line per live aura — the measured figure box, which source
measured it, the frame drawn, the colour wanted versus the colour that arrived, and whether the shader
attached, plus whether its motes are emitting and which way they are travelling — and one line per live
outline: the character's ink colour, the colour the outline
*should* be carrying, the colour it is *actually* carrying, whether the two match, and whether the
silhouette shader is attached. If an outline ever looks wrong, that line says which half is at fault.

```
```

The single-player map marker is outlined too, purely so this works solo — it is the only per-player map
icon that exists outside co-op. Like everything else it stays dormant until a variation is active, which in
single-player means until you force one here.

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
dotnet build MultiplayerColors.csproj
dotnet test tests/MultiplayerColors.Tests.csproj
scripts/check-shader.sh          # compiles every shader with the real Godot parser
```

**This mod is distributed through the Steam Workshop only — `build` deliberately does not deploy into the
game's local `mods/` folder.** A local copy silently *shadows* the Workshop one (the game logs "loaded both
via Steam and local mods directory. Disabling the Steam workshop version"), so you would publish an update
and go on testing a build nobody else has. To get a change in game, publish it:
`scripts/publish-workshop.sh "note"`. Both the build and the publish script hard-fail if a stale local copy
is found.

The game path is discovered per-OS in [Sts2PathDiscovery.props](Sts2PathDiscovery.props); override with
`-p:Sts2Path=...` if your install lives somewhere unusual.

This is a DLL-only mod — no `.pck`, no Godot project, no export step, and no BaseLib dependency.

## Tests

`tests/` covers the two things that can break silently:

- **`PlayerTintTests`** — the roster logic and the colour maths, including that assignment follows slot
  index rather than list order, the outline key, the aura's colours, falloff and framing, and each
  variation's particle motion.
- **`TintConsoleCmdTests`** — the `tint` command's parsing, and that `TintOverride` stays aligned with
  `PlayerVariation` (they're bridged by an enum cast that would silently pick the wrong colour if they
  drifted apart).
- **`PatchTargetTests`** — resolves every `[HarmonyPatch]` target the same way Harmony does, and checks each
  patch parameter against the target's real signature. A game update that renames one of the patched methods
  fails here instead of shipping a mod that quietly does nothing.
