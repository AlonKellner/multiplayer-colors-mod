using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MultiplayerColors;

/// <summary>
/// The four colour variations handed out to players who share a character. Ordered exactly as they are
/// assigned: the lowest-slot player of a duplicated character gets <see cref="Brighter" />, the next
/// <see cref="Darker" />, and so on.
/// </summary>
public enum PlayerVariation
{
    Brighter,
    Darker,
    Warmer,
    Cooler,
}

/// <summary>
/// Decides which colour variation (if any) a player should be rendered with, and provides the two ways of
/// applying it: a multiplicative <see cref="Modulate" /> for sprites, and an HSV <see cref="Shift" /> for
/// flat colours such as map ink.
///
/// Every function here is pure and depends only on the run roster, so all four clients in a co-op game
/// compute identical colours. Nothing in this file may use RNG, the local player, or node/child ordering
/// (see <see cref="For" />).
///
/// Deliberately free of any logging: these functions are exercised by bare unit tests outside a Godot
/// host, where a single Log call takes the whole test process down.
/// </summary>
public static class PlayerTint
{
    // Sprite tints, folded into CanvasItem.Modulate. Godot allows components above 1, which is what makes
    // "brighter" possible without a shader.
    //
    // These two numbers are the whole strength dial — raise them to make the variations more obvious,
    // lower them to make them subtler. Each variation's opposite is the reciprocal, so the pairs stay
    // symmetric around neutral however they're tuned. The map ink, which uses the HSV constants below,
    // is a separate dial and has read well since v0.1.0 — it is deliberately left alone.
    //
    // Tuned against live runs: 1.12/1.08 (v0.1.0) was too weak to notice on both counts; 1.20/1.13
    // (v0.1.2) settled brightness but left the warm/cool pair still too easy to miss, so the tilt went to
    // 1.28 in v0.1.4. It runs hotter than the brightness dial on purpose — a chromatic shift reads weaker
    // than a luminance one at equal magnitude, especially on art that is already strongly coloured.
    private const float BrightnessGain = 1.20f;
    private const float ChannelTilt = 1.28f;

    private static readonly Color BrighterMul = Gray(BrightnessGain);
    private static readonly Color DarkerMul = Gray(1f / BrightnessGain);
    private static readonly Color WarmerMul = new(ChannelTilt, 1f, 1f / ChannelTilt);
    private static readonly Color CoolerMul = new(1f / ChannelTilt, 1f, ChannelTilt);

    private static Color Gray(float v) => new(v, v, v);

    // Flat-colour equivalents, for map ink, pings and targeting lines.
    //
    // The value pair below matches the sprite brightness and has read well since v0.1.0. The hue step does
    // NOT match the sprite tilt and should not be expected to: these are drawn as thin strokes on a busy
    // parchment map, where a hue difference that is obvious across a whole character sprite is easy to miss
    // entirely. Reported unnoticeable at 0.035 (~13 deg) even after the sprite tilt was dialled in, so
    // v0.1.5 took it to 0.085 (~31 deg) — which puts warmer and cooler ~61 deg apart from each other, the
    // separation that actually matters when two players are drawing on the same map.
    private const float HueStep = 0.085f;
    private const float ValueScaleUp = 1.12f;
    private const float ValueScaleDown = 0.88f;

    // Multiplying value alone leaves very dark colours untouched, so the value variations also carry a
    // small additive term. Without it a character whose MapDrawingColor is near-black would shift by
    // nothing at all.
    private const float ValueOffset = 0.05f;

    // Rotating the hue of a near-grey colour is invisible, so the hue variations lift saturation to at
    // least this much first.
    private const float MinSaturationForHueShift = 0.20f;

    // The warmest point on the hue wheel (orange). "Warmer" rotates towards it and "cooler" away, so that
    // the direction is right whatever colour the character's ink starts from - see TowardsWarm.
    private const float WarmAnchor = 0.08f;

    /// <summary>
    /// The variation for <paramref name="player" />, or <c>null</c> when this player's character is not
    /// shared with anybody in the run — in which case nothing should be tinted at all and the character
    /// renders exactly as the base game draws it.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="MegaCrit.Sts2.Core.Runs.IPlayerCollection.GetPlayerSlotIndex" />, the game's own
    /// network-authoritative per-player ordinal (it seeds <c>Rng</c> and indexes every multiplayer
    /// synchronizer's per-player array). It must NOT be keyed on node order or list position: several UIs
    /// deliberately reorder the roster so the local player comes first (NCombatRoom.PositionPlayersAndPets,
    /// NMerchantRoom.AfterRoomIsLoaded, NMultiplayerPlayerStateContainer.Initialize), so a colour derived
    /// from display position would come out different on each client.
    /// </remarks>
    public static PlayerVariation? For(Player? player)
    {
        var run = player?.RunState;
        if (player == null || run == null)
        {
            return null;
        }

        // The `tint` console command forces a variation on the local player so all four can be eyeballed
        // without four people in a lobby. Teammates stay on the normal rule, so a forced colour can be
        // compared against a real one. Local-only also means it never desyncs anybody else's view.
        if (Override != TintOverride.Auto && LocalContext.IsMe(player))
        {
            return Override == TintOverride.Off ? null : (PlayerVariation)(Override - TintOverride.Brighter);
        }

        return Resolve(run.Players, player, p => p?.Character?.Id, p => run.GetPlayerSlotIndex(p));
    }

    /// <summary>
    /// The roster logic behind <see cref="For" />, lifted off <see cref="Player" /> so it can be unit
    /// tested — a real <see cref="Player" /> can only be built through <c>ModelDb</c>, which is unavailable
    /// outside a running game.
    /// </summary>
    /// <param name="characterKey">
    /// Identifies which character a roster entry picked; entries comparing equal are duplicates of each
    /// other. <c>null</c> keys never match anything, including each other.
    /// </param>
    /// <param name="slotIndex">The run's network-authoritative per-player ordinal.</param>
    public static PlayerVariation? Resolve<T>(
        IReadOnlyList<T>? roster,
        T player,
        Func<T, object?> characterKey,
        Func<T, int> slotIndex)
        where T : class
    {
        if (roster == null || roster.Count <= 1)
        {
            return null;
        }

        var myKey = characterKey(player);
        if (myKey == null)
        {
            return null;
        }

        // Ordinal among everyone sharing this character, by ascending slot index.
        var ordinal = 0;
        var duplicates = 0;
        var mySlot = slotIndex(player);
        foreach (var other in roster)
        {
            if (other == null || !myKey.Equals(characterKey(other)))
            {
                continue;
            }

            duplicates++;
            if (slotIndex(other) < mySlot)
            {
                ordinal++;
            }
        }

        if (duplicates <= 1)
        {
            return null;
        }

        return (PlayerVariation)(ordinal % 4);
    }

    /// <summary>The per-channel multiplier to fold into a sprite's <c>Modulate</c>. Alpha is always 1.</summary>
    public static Color Modulate(PlayerVariation variation) => variation switch
    {
        PlayerVariation.Brighter => BrighterMul,
        PlayerVariation.Darker => DarkerMul,
        PlayerVariation.Warmer => WarmerMul,
        PlayerVariation.Cooler => CoolerMul,
        _ => Colors.White,
    };

    /// <summary>
    /// The HSV form of the variation, for flat colours (map ink, targeting lines) where real hue rotation
    /// looks better than a multiply. Alpha is preserved.
    /// </summary>
    public static Color Shift(PlayerVariation variation, Color color)
    {
        float h = color.H;
        float s = color.S;
        float v = color.V;

        switch (variation)
        {
            case PlayerVariation.Brighter:
                v = Mathf.Clamp(v * ValueScaleUp + ValueOffset, 0f, 1f);
                break;
            case PlayerVariation.Darker:
                v = Mathf.Clamp(v * ValueScaleDown - ValueOffset, 0f, 1f);
                break;
            case PlayerVariation.Warmer:
                h = WrapHue(h + HueStep * TowardsWarm(h));
                s = Mathf.Max(s, MinSaturationForHueShift);
                break;
            case PlayerVariation.Cooler:
                h = WrapHue(h - HueStep * TowardsWarm(h));
                s = Mathf.Max(s, MinSaturationForHueShift);
                break;
        }

        return Color.FromHsv(h, s, v, color.A);
    }

    /// <summary>
    /// Folds this player's variation into <paramref name="node" />'s modulate, leaving alpha alone.
    /// No-op when the player's character is unique in the run and no override is set.
    /// </summary>
    /// <remarks>
    /// The node's modulate at the moment of the first call is remembered as its base, and the tint is
    /// applied on top of that. This composes with the tints the base game applies itself — the 0.5 grey
    /// NCombatRoom / NMerchantRoom put on back-row players, the DarkGray NRestSiteRoom puts on the campfire
    /// containers when the fire is out, and the half-transparency NHandImage puts on remote players' arms —
    /// and, unlike a plain multiply, it stays re-computable, which is what lets <see cref="Refresh" />
    /// recolour live nodes when the console override changes.
    ///
    /// Caveat: if the game reassigns the modulate of a node we already tinted, the remembered base goes
    /// stale. None of the nodes this mod tints are written by the game after the fact — that is exactly why
    /// each patch targets the innermost art node rather than the container.
    /// </remarks>
    public static void Apply(CanvasItem? node, Player? player)
    {
        if (node == null || player == null)
        {
            return;
        }

        if (Tinted.TryGetValue(node, out var entry))
        {
            entry.Player = player;
        }
        else
        {
            entry = new TintedNode(node.Modulate, player);
            Tinted.Add(node, entry);
        }

        Repaint(node, entry);
    }

    /// <summary>Shifts a flat colour for this player, or returns it untouched when there is no variation.</summary>
    public static Color Apply(Color color, Player? player)
    {
        var variation = For(player);
        return variation == null ? color : Shift(variation.Value, color);
    }

    /// <summary>Component-wise RGB multiply that preserves <paramref name="current" />'s alpha.</summary>
    public static Color Combine(Color current, Color multiplier) => new(
        current.R * multiplier.R,
        current.G * multiplier.G,
        current.B * multiplier.B,
        current.A);

    private static float WrapHue(float h)
    {
        h %= 1f;
        return h < 0f ? h + 1f : h;
    }

    /// <summary>
    /// Which way round the hue wheel counts as "warmer" from <paramref name="h" />: <c>+1</c> if increasing
    /// hue heads towards orange, <c>-1</c> if decreasing does.
    /// </summary>
    /// <remarks>
    /// Rotating hue by a fixed signed step is NOT a warm/cool shift — which direction is warmer depends
    /// entirely on where you start. Adding to red's hue gives orange (warmer); adding to green's gives teal
    /// (cooler). Before v0.1.6 the ink always added, so for every green- or blue-inked character the
    /// variation labelled Warmer came out cool — and, worse, moved that player's map ink the opposite way
    /// from their own sprite, which uses a channel multiply and so is genuinely warm for any base colour.
    ///
    /// Picking the direction by whichever way is shorter to the warm anchor makes the two agree.
    /// </remarks>
    private static float TowardsWarm(float h) => SignedHueDistance(h, WarmAnchor) >= 0f ? 1f : -1f;

    /// <summary>Shortest signed path from <paramref name="from" /> to <paramref name="to" />, in -0.5..0.5.</summary>
    private static float SignedHueDistance(float from, float to)
    {
        var forward = WrapHue(to - from);
        return forward <= 0.5f ? forward : forward - 1f;
    }

    // ---- Console override ---------------------------------------------------------------------------
    // Backing state for the `tint` dev command. Everything below is a local testing aid: it changes only
    // what this client draws, never what is sent to anyone else.

    /// <summary>What the <c>tint</c> console command has forced, if anything.</summary>
    public static TintOverride Override { get; set; } = TintOverride.Auto;

    /// <summary>
    /// Nodes this mod has tinted, with the modulate they had before we touched them. Weak keys, so a node
    /// freed by Godot drops out on its own and nothing here keeps a room alive.
    /// </summary>
    private static readonly ConditionalWeakTable<CanvasItem, TintedNode> Tinted = new();

    private sealed class TintedNode(Color baseModulate, Player player)
    {
        public Color BaseModulate { get; } = baseModulate;
        public Player Player { get; set; } = player;
    }

    /// <remarks>
    /// Writes RGB but keeps whatever alpha the node currently has. Several of these nodes are faded in and
    /// out by tweens that animate <c>modulate:a</c> alone (the multiplayer vote icons, for one); restoring
    /// the remembered alpha would snap them to full opacity mid-fade.
    /// </remarks>
    private static void Repaint(CanvasItem node, TintedNode entry)
    {
        var variation = For(entry.Player);
        var tinted = variation == null
            ? entry.BaseModulate
            : Combine(entry.BaseModulate, Modulate(variation.Value));

        node.Modulate = new Color(tinted.R, tinted.G, tinted.B, node.Modulate.A);
    }

    /// <summary>
    /// Recolours every sprite tinted so far, so a <c>tint</c> command takes effect on what is already on
    /// screen instead of only on the next room. Returns how many nodes were repainted.
    /// </summary>
    /// <remarks>
    /// Sprites only. Flat colours (map ink, pings, targeting lines) are one-shot assignments with nothing
    /// to walk — but they are all recreated per stroke / per ping / per turn, so they pick the override up
    /// on their own the next time they're drawn.
    /// </remarks>
    public static int Refresh()
    {
        var repainted = 0;
        foreach (var (node, entry) in Tinted)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            Repaint(node, entry);
            repainted++;
        }

        return repainted;
    }
}

/// <summary>
/// What the <c>tint</c> console command has forced. <see cref="Auto" /> is the shipping behaviour (tint only
/// players who share a character); the rest are local testing overrides for the current player.
/// </summary>
/// <remarks>
/// The four variation members are ordered to match <see cref="PlayerVariation" /> so one can be cast to the
/// other by subtracting <see cref="Brighter" />.
/// </remarks>
public enum TintOverride
{
    Auto,
    Off,
    Brighter,
    Darker,
    Warmer,
    Cooler,
}
