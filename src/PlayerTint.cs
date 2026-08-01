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
    // entirely.
    //
    // The hue rotation is NOT a fixed angle (that was v0.1.5, and it came out badly biased — see
    // SolveHueStep). It is solved per colour so that warmer and cooler end up a constant *perceptual*
    // distance apart, measured in OKLab. This is the target, in OKLab dE x100: about 16, tuned from
    // Ironclad reading too strong at 21 and Silent too weak at 5 under the old fixed angle.
    private const float TargetHueSeparation = 16f;

    // Bounds on the solved angle. The upper one is an identity guard: past roughly 47 deg a colour stops
    // reading as a shade of the character's own, and for a muted ink no angle reaches the target anyway.
    private const float MinHueStep = 0.010f;
    private const float MaxHueStep = 0.130f;
    private const float ValueScaleUp = 1.12f;
    private const float ValueScaleDown = 0.88f;

    // Multiplying value alone leaves very dark colours untouched, so the value variations also carry a
    // small additive term. Without it a character whose MapDrawingColor is near-black would shift by
    // nothing at all.
    private const float ValueOffset = 0.05f;

    // Guards for degenerate base colours, which matter entirely for mod support: CharacterModel's
    // MapDrawingColor / RemoteTargetingLineColor are virtual and both default to Colors.Black, so a modded
    // character that never overrides them hands us a colour with no hue, no saturation and no value to
    // work with. Untreated, black ink collapses three of the four variations onto each other — darker,
    // warmer and cooler all come back pure black. All five shipped characters sit comfortably inside these
    // bounds, so none of this changes any base-game colour.
    //
    // Hue and saturation are meaningless at zero value, so the hue variations lift both into visible range.
    private const float MinSaturationForHueShift = 0.35f;
    private const float MinValueForHueShift = 0.25f;

    // The value variations need headroom on both sides, or a black base cannot get darker and a white one
    // cannot get brighter.
    private const float MinValueForShift = 0.20f;
    private const float MaxValueForShift = 0.90f;

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
                v = Mathf.Clamp(Headroom(v) * ValueScaleUp + ValueOffset, 0f, 1f);
                break;
            case PlayerVariation.Darker:
                v = Mathf.Clamp(Headroom(v) * ValueScaleDown - ValueOffset, 0f, 1f);
                break;
            case PlayerVariation.Warmer:
            case PlayerVariation.Cooler:
            {
                // Saturation and value are floored first, because the angle needed depends on them.
                s = Mathf.Max(s, MinSaturationForHueShift);
                v = Mathf.Max(v, MinValueForHueShift);

                var step = SolveHueStep(h, s, v) * TowardsWarm(h);
                h = WrapHue(variation == PlayerVariation.Warmer ? h + step : h - step);
                break;
            }
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

    /// <summary>
    /// Pulls a value into a band that leaves room to move in both directions, so brighter and darker can
    /// never land on the same colour. A no-op for anything but a near-black or near-white base.
    /// </summary>
    private static float Headroom(float v) => Mathf.Clamp(v, MinValueForShift, MaxValueForShift);

    /// <summary>
    /// The hue angle that puts warmer and cooler <see cref="TargetHueSeparation" /> apart perceptually,
    /// for a colour at this saturation and value. Bounded by <see cref="MinHueStep" /> and
    /// <see cref="MaxHueStep" />.
    /// </summary>
    /// <remarks>
    /// A fixed hue angle produces wildly different *felt* differences depending on the colour it is applied
    /// to, for two compounding reasons:
    ///
    /// 1. A hue rotation sweeps an arc whose length scales with how chromatic the colour already is
    ///    (saturation x value). Ironclad's ink is 2.6x more chromatic than Silent's, so the same angle
    ///    moved it 2.6x further through colour space.
    /// 2. HSV hue is not perceptually uniform. The green sector is compressed — a large angular span there
    ///    all still reads as "green" — while reds and blues fan out much faster.
    ///
    /// Together those made a fixed 31 deg step land anywhere from dE 4.6 (Silent) to 31.8 (Defect), a 6.9x
    /// spread across the five shipped characters, which is exactly the bias that was reported: too weak on
    /// Silent, too strong on Ironclad. Solving for the perceptual distance instead cancels both causes at
    /// once, and does it from the colour alone, so a modded character gets the same treatment for free.
    ///
    /// A muted, dark ink like Silent's cannot reach the target at any sane angle — its chroma is simply too
    /// low for hue rotation to move it far. Those hit <see cref="MaxHueStep" /> and get the best available,
    /// which is still a large improvement, rather than being pushed somewhere that stops looking green.
    /// </remarks>
    private static float SolveHueStep(float h, float s, float v)
    {
        var direction = TowardsWarm(h);
        var low = MinHueStep;
        var high = MaxHueStep;

        // 20 halvings resolves the angle far finer than any display can show. This runs once per line, ping
        // or targeting indicator, so the cost is irrelevant.
        for (var i = 0; i < 20; i++)
        {
            var mid = 0.5f * (low + high);
            var warmer = Color.FromHsv(WrapHue(h + mid * direction), s, v);
            var cooler = Color.FromHsv(WrapHue(h - mid * direction), s, v);

            if (PerceptualDistance(warmer, cooler) < TargetHueSeparation)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return 0.5f * (low + high);
    }

    /// <summary>
    /// Distance between two colours in OKLab, scaled by 100. Unlike RGB or HSV distance, equal numbers here
    /// mean roughly equal *perceived* difference, which is the whole point of using it.
    /// </summary>
    public static float PerceptualDistance(Color a, Color b)
    {
        var (l1, a1, b1) = ToOkLab(a);
        var (l2, a2, b2) = ToOkLab(b);
        var dl = l1 - l2;
        var da = a1 - a2;
        var db = b1 - b2;

        return MathF.Sqrt(dl * dl + da * da + db * db) * 100f;
    }

    /// <summary>Godot colours are sRGB, so this linearises before the OKLab matrices.</summary>
    private static (float L, float A, float B) ToOkLab(Color c)
    {
        static float Linear(float x) => x <= 0.04045f ? x / 12.92f : MathF.Pow((x + 0.055f) / 1.055f, 2.4f);

        float r = Linear(c.R), g = Linear(c.G), b = Linear(c.B);

        var l = MathF.Cbrt(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b);
        var m = MathF.Cbrt(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b);
        var s = MathF.Cbrt(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b);

        return (
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
    }

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
