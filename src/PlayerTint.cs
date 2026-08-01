using Godot;
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
    // Sprite tints, applied by multiplying into CanvasItem.Modulate. Godot allows components above 1,
    // which is what makes "brighter" possible without a shader.
    private static readonly Color BrighterMul = new(1.12f, 1.12f, 1.12f);
    private static readonly Color DarkerMul = new(0.88f, 0.88f, 0.88f);
    private static readonly Color WarmerMul = new(1.08f, 1.00f, 0.90f);
    private static readonly Color CoolerMul = new(0.90f, 1.00f, 1.10f);

    // Flat-colour equivalents, tuned to read as the same shift as the multipliers above.
    // The hue step sits just under the 0.05 jitter the base game already applies to duplicate monsters
    // in NCombatRoom.RandomizeEnemyScalesAndHues, so it is known not to look strange.
    private const float HueStep = 0.035f;
    private const float ValueScaleUp = 1.12f;
    private const float ValueScaleDown = 0.88f;

    // Multiplying value alone leaves very dark colours untouched, so the value variations also carry a
    // small additive term. Without it a character whose MapDrawingColor is near-black would shift by
    // nothing at all.
    private const float ValueOffset = 0.05f;

    // Rotating the hue of a near-grey colour is invisible, so the hue variations lift saturation to at
    // least this much first.
    private const float MinSaturationForHueShift = 0.20f;

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
                h = WrapHue(h + HueStep);
                s = Mathf.Max(s, MinSaturationForHueShift);
                break;
            case PlayerVariation.Cooler:
                h = WrapHue(h - HueStep);
                s = Mathf.Max(s, MinSaturationForHueShift);
                break;
        }

        return Color.FromHsv(h, s, v, color.A);
    }

    /// <summary>
    /// Multiplies this player's variation into <paramref name="node" />'s modulate, leaving alpha alone.
    /// No-op when the player's character is unique in the run.
    /// </summary>
    /// <remarks>
    /// Multiplies rather than assigns so it composes with the tints the base game applies itself — the
    /// 0.5 grey NCombatRoom / NMerchantRoom put on back-row players, the DarkGray NRestSiteRoom puts on
    /// the campfire containers when the fire is out, and the half-transparency NHandImage puts on remote
    /// players' arms.
    /// </remarks>
    public static void Apply(CanvasItem? node, Player? player)
    {
        if (node == null)
        {
            return;
        }

        var variation = For(player);
        if (variation == null)
        {
            return;
        }

        node.Modulate = Combine(node.Modulate, Modulate(variation.Value));
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
}
