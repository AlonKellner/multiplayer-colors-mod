using Godot;
using Xunit;

namespace MultiplayerColors.Tests;

/// <summary>
/// A stand-in for a roster entry. A real <c>Player</c> can only be built through <c>ModelDb</c>, which
/// needs a running game, so the roster logic is tested through <see cref="PlayerTint.Resolve{T}" />.
/// </summary>
internal sealed record Seat(string Character, int Slot);

public class ResolveTests
{
    private static PlayerVariation? Resolve(IReadOnlyList<Seat> roster, Seat seat) =>
        PlayerTint.Resolve(roster, seat, s => s.Character, s => s.Slot);

    [Fact]
    public void SinglePlayerRun_HasNoVariation()
    {
        var solo = new Seat("ironclad", 0);
        Assert.Null(Resolve(new[] { solo }, solo));
    }

    [Fact]
    public void UniqueCharacterInFullLobby_HasNoVariation()
    {
        var roster = new[]
        {
            new Seat("ironclad", 0),
            new Seat("silent", 1),
            new Seat("defect", 2),
            new Seat("necrobinder", 3),
        };

        Assert.All(roster, seat => Assert.Null(Resolve(roster, seat)));
    }

    [Fact]
    public void FourWayDuplicate_GetsAllFourVariationsInSlotOrder()
    {
        var roster = new[]
        {
            new Seat("ironclad", 0),
            new Seat("ironclad", 1),
            new Seat("ironclad", 2),
            new Seat("ironclad", 3),
        };

        Assert.Equal(PlayerVariation.Brighter, Resolve(roster, roster[0]));
        Assert.Equal(PlayerVariation.Darker, Resolve(roster, roster[1]));
        Assert.Equal(PlayerVariation.Warmer, Resolve(roster, roster[2]));
        Assert.Equal(PlayerVariation.Cooler, Resolve(roster, roster[3]));
    }

    [Fact]
    public void AssignmentFollowsSlotIndex_NotListOrder()
    {
        // The same four seats, enumerated in the reverse of their slot order — which is exactly what the
        // local-player-first UIs do. The colours must not move.
        var bySlot = new[]
        {
            new Seat("ironclad", 0),
            new Seat("ironclad", 1),
            new Seat("ironclad", 2),
            new Seat("ironclad", 3),
        };
        var shuffled = new[] { bySlot[2], bySlot[0], bySlot[3], bySlot[1] };

        foreach (var seat in bySlot)
        {
            Assert.Equal(Resolve(bySlot, seat), Resolve(shuffled, seat));
        }
    }

    [Fact]
    public void MixedRoster_PairsAreIndependent()
    {
        var roster = new[]
        {
            new Seat("ironclad", 0),
            new Seat("silent", 1),
            new Seat("ironclad", 2),
            new Seat("silent", 3),
        };

        // Each pair restarts at Brighter; the silent pair is not pushed to Warmer/Cooler by the ironclads.
        Assert.Equal(PlayerVariation.Brighter, Resolve(roster, roster[0]));
        Assert.Equal(PlayerVariation.Brighter, Resolve(roster, roster[1]));
        Assert.Equal(PlayerVariation.Darker, Resolve(roster, roster[2]));
        Assert.Equal(PlayerVariation.Darker, Resolve(roster, roster[3]));
    }

    [Fact]
    public void PartialDuplicate_LeavesTheUniquePlayerAlone()
    {
        var roster = new[]
        {
            new Seat("ironclad", 0),
            new Seat("ironclad", 1),
            new Seat("defect", 2),
        };

        Assert.Equal(PlayerVariation.Brighter, Resolve(roster, roster[0]));
        Assert.Equal(PlayerVariation.Darker, Resolve(roster, roster[1]));
        Assert.Null(Resolve(roster, roster[2]));
    }

    [Fact]
    public void NullCharacterKey_YieldsNoVariation()
    {
        var roster = new[] { new Seat("ironclad", 0), new Seat("ironclad", 1) };
        Assert.Null(PlayerTint.Resolve<Seat>(roster, roster[0], _ => null, s => s.Slot));
    }

    [Fact]
    public void EmptyOrNullRoster_YieldsNoVariation()
    {
        var seat = new Seat("ironclad", 0);
        Assert.Null(PlayerTint.Resolve<Seat>(null, seat, s => s.Character, s => s.Slot));
        Assert.Null(PlayerTint.Resolve(Array.Empty<Seat>(), seat, s => s.Character, s => s.Slot));
    }
}

public class ModulateTests
{
    [Fact]
    public void BrighterRaisesAndDarkerLowersEveryChannel()
    {
        var brighter = PlayerTint.Modulate(PlayerVariation.Brighter);
        var darker = PlayerTint.Modulate(PlayerVariation.Darker);

        Assert.True(brighter.R > 1f && brighter.G > 1f && brighter.B > 1f);
        Assert.True(darker.R < 1f && darker.G < 1f && darker.B < 1f);
    }

    [Fact]
    public void WarmerAndCoolerPushRedAndBlueInOppositeDirections()
    {
        var warmer = PlayerTint.Modulate(PlayerVariation.Warmer);
        var cooler = PlayerTint.Modulate(PlayerVariation.Cooler);

        Assert.True(warmer.R > warmer.B);
        Assert.True(cooler.B > cooler.R);
    }

    [Fact]
    public void AllFourVariationsAreDistinct()
    {
        var seen = new HashSet<string>();
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            Assert.True(seen.Add(PlayerTint.Modulate(v).ToHtml()), $"{v} duplicates an earlier variation");
        }
    }

    [Fact]
    public void ModulatesStayShortOfARecolour()
    {
        // A ceiling rather than a target: past roughly a third off neutral the character stops reading as
        // the same character, which is the one thing these variations must not do. Both strength dials sit
        // below this deliberately — if a future retune trips this test, that is the signal to stop.
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var m = PlayerTint.Modulate(v);
            foreach (var channel in new[] { m.R, m.G, m.B })
            {
                Assert.InRange(channel, 0.70f, 1.35f);
            }
        }
    }

    [Fact]
    public void HueTiltRunsHotterThanTheBrightnessShift()
    {
        // Deliberate, and twice reported from live runs: a warm/cool shift reads weaker than a brightness
        // one at equal magnitude, so matching the two dials leaves the hue pair too easy to miss.
        var brightnessDeviation = PlayerTint.Modulate(PlayerVariation.Brighter).R - 1f;
        var hueDeviation = PlayerTint.Modulate(PlayerVariation.Warmer).R - 1f;

        Assert.True(
            hueDeviation > brightnessDeviation,
            $"hue tilt ({hueDeviation:F3}) should exceed the brightness shift ({brightnessDeviation:F3})");
    }

    [Fact]
    public void OppositeVariationsAreSymmetricAroundNeutral()
    {
        // Brighter/Darker and Warmer/Cooler are built as reciprocals of one strength constant, so they
        // stay balanced however the dial is tuned — no variation drifts further from vanilla than its pair.
        var brighter = PlayerTint.Modulate(PlayerVariation.Brighter);
        var darker = PlayerTint.Modulate(PlayerVariation.Darker);
        var warmer = PlayerTint.Modulate(PlayerVariation.Warmer);
        var cooler = PlayerTint.Modulate(PlayerVariation.Cooler);

        Assert.Equal(1f, brighter.R * darker.R, 3);
        Assert.Equal(1f, warmer.R * cooler.R, 3);
        Assert.Equal(1f, warmer.B * cooler.B, 3);
    }

    [Fact]
    public void ArtTintIsStrongerThanTheValuesThatWereReportedTooWeak()
    {
        // Two rounds of live-run feedback, pinned so a later "let's tone it down" cannot silently walk
        // back past what was already judged too weak to notice. v0.1.0 shipped 1.12 brightness / 1.08 tilt
        // (both too weak); v0.1.2 fixed brightness but its 1.13 tilt was still too weak.
        Assert.True(PlayerTint.Modulate(PlayerVariation.Brighter).R > 1.12f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Darker).R < 0.88f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Warmer).R > 1.13f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Cooler).B > 1.13f);
    }

    [Fact]
    public void CombinePreservesAlphaAndMultipliesRgb()
    {
        var result = PlayerTint.Combine(new Color(0.5f, 0.4f, 0.2f, 0.5f), new Color(2f, 0.5f, 1f));

        Assert.Equal(1.0f, result.R, 4);
        Assert.Equal(0.2f, result.G, 4);
        Assert.Equal(0.2f, result.B, 4);
        Assert.Equal(0.5f, result.A, 4);
    }
}

public class ShiftTests
{
    // Ironclad's real MapDrawingColor.
    private static readonly Color Ink = new("CB282B");

    [Fact]
    public void BrighterAndDarkerMoveValueInOppositeDirections()
    {
        Assert.True(PlayerTint.Shift(PlayerVariation.Brighter, Ink).V > Ink.V);
        Assert.True(PlayerTint.Shift(PlayerVariation.Darker, Ink).V < Ink.V);
    }

    [Fact]
    public void WarmerAndCoolerMoveHueInOppositeDirections()
    {
        var warmer = PlayerTint.Shift(PlayerVariation.Warmer, Ink);
        var cooler = PlayerTint.Shift(PlayerVariation.Cooler, Ink);

        Assert.NotEqual(Ink.H, warmer.H, 3);
        Assert.NotEqual(Ink.H, cooler.H, 3);
        Assert.NotEqual(warmer.H, cooler.H, 3);
    }

    [Fact]
    public void HueWrapsAroundZeroInsteadOfClamping()
    {
        // Pure red sits at hue 0, so the "cooler" shift has to wrap to just below 1.
        var cooler = PlayerTint.Shift(PlayerVariation.Cooler, new Color(1f, 0f, 0f));

        Assert.InRange(cooler.H, 0.9f, 1f);
        Assert.True(cooler.R > cooler.G);
    }

    [Fact]
    public void ValueStaysInRangeForWhiteAndBlack()
    {
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            foreach (var extreme in new[] { Colors.White, Colors.Black })
            {
                var shifted = PlayerTint.Shift(v, extreme);
                Assert.InRange(shifted.V, 0f, 1f);
                Assert.InRange(shifted.S, 0f, 1f);
            }
        }
    }

    [Fact]
    public void VeryDarkColoursStillBrighten()
    {
        // A pure multiply would leave near-black exactly where it was; the additive term is what stops
        // a dark map ink from getting no variation at all.
        var nearBlack = new Color(0.02f, 0.02f, 0.02f);
        Assert.True(PlayerTint.Shift(PlayerVariation.Brighter, nearBlack).V > nearBlack.V + 0.02f);
    }

    [Fact]
    public void AlphaIsPreserved()
    {
        var translucent = new Color(0.8f, 0.3f, 0.1f, 0.35f);
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            Assert.Equal(0.35f, PlayerTint.Shift(v, translucent).A, 4);
        }
    }

    [Fact]
    public void AllFourVariationsProduceDistinctColours()
    {
        var seen = new HashSet<string>();
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            Assert.True(seen.Add(PlayerTint.Shift(v, Ink).ToHtml()), $"{v} duplicates an earlier variation");
        }
    }

    [Fact]
    public void ShiftStaysShortOfADifferentColour()
    {
        // A ceiling, not a target. Map ink is drawn as thin strokes and needs a bigger shift than sprite
        // art to register at all, but it still has to look like a shade of that character's colour.
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var s = PlayerTint.Shift(v, Ink);
            Assert.True(Math.Abs(s.R - Ink.R) < 0.40f, $"{v} moved red too far");
            Assert.True(Math.Abs(s.G - Ink.G) < 0.40f, $"{v} moved green too far");
            Assert.True(Math.Abs(s.B - Ink.B) < 0.40f, $"{v} moved blue too far");
        }
    }

    [Fact]
    public void WarmerAndCoolerInkAreFarEnoughApartToTellApart()
    {
        // The number that matters is the gap between the two, not the gap from vanilla: what you're doing
        // is telling two players' lines apart on the same map. Reported unnoticeable when this was ~25 deg.
        var warmer = PlayerTint.Shift(PlayerVariation.Warmer, Ink);
        var cooler = PlayerTint.Shift(PlayerVariation.Cooler, Ink);

        var gap = Math.Abs(warmer.H - cooler.H);
        gap = Math.Min(gap, 1f - gap); // shortest way round the hue wheel

        Assert.True(gap > 0.14f, $"warmer and cooler ink are only {gap * 360f:F0} deg apart");
    }

    /// <summary>Every shipped character's <c>MapDrawingColor</c>, as of game v0.110.1.</summary>
    public static TheoryData<string, string> ShippedInks => new()
    {
        { "Ironclad", "CB282B" },
        { "Silent", "2F6729" },
        { "Defect", "0D638C" },
        { "Necrobinder", "AC0486" },
        { "Regent", "935206" },
    };

    [Theory]
    [MemberData(nameof(ShippedInks))]
    public void InkWarmsTheSameDirectionAsTheSprite(string character, string hex)
    {
        // The v0.1.6 bug: Shift rotated hue by a fixed *signed* step, but which direction is warmer depends
        // on where you start — adding to red's hue gives orange, adding to green's gives teal. So for every
        // green- or blue-inked character, "Warmer" ink came out cool, and moved that player's map ink the
        // opposite way from their own sprite (which uses a channel multiply and is warm from any base).
        var ink = new Color(hex);

        var inkWarmer = PlayerTint.Shift(PlayerVariation.Warmer, ink);
        var spriteWarmer = PlayerTint.Combine(ink, PlayerTint.Modulate(PlayerVariation.Warmer));

        Assert.True(
            Math.Sign(HueDelta(ink.H, inkWarmer.H)) == Math.Sign(HueDelta(ink.H, spriteWarmer.H)),
            $"{character}: ink warms {HueDelta(ink.H, inkWarmer.H) * 360f:+0;-0} deg but the sprite warms "
            + $"{HueDelta(ink.H, spriteWarmer.H) * 360f:+0;-0} deg — they disagree about which way is warm");
    }

    [Fact]
    public void WarmingAGreenMovesItTowardsYellowNotTeal()
    {
        // Spelled out for the case that exposed the bug: Silent's ink is green, and the only way to warm a
        // green is towards olive/yellow. Landing on teal instead is the failure this pins.
        var green = new Color("2F6729");

        var warmer = PlayerTint.Shift(PlayerVariation.Warmer, green);
        var cooler = PlayerTint.Shift(PlayerVariation.Cooler, green);

        Assert.True(HueDelta(green.H, warmer.H) < 0f, "warming a green should move it towards yellow");
        Assert.True(HueDelta(green.H, cooler.H) > 0f, "cooling a green should move it towards teal");
    }

    /// <summary>
    /// Colours a modded character could plausibly declare — including the ones the base class defaults to.
    /// <c>CharacterModel.MapDrawingColor</c> and <c>RemoteTargetingLineColor</c> are both virtual with a
    /// <c>Colors.Black</c> default, so "never overrode it" is a real case, not a hypothetical. (The
    /// Understudy, the sibling repo's character, inherits the black targeting line today.)
    /// </summary>
    public static TheoryData<string, string> DegenerateInks => new()
    {
        { "unset default (black)", "000000" },
        { "pure white", "FFFFFF" },
        { "mid grey", "808080" },
        { "near-black", "0A0A0A" },
        { "near-white", "F4F4F4" },
        { "fully saturated red", "FF0000" },
        { "very dark blue", "000033" },
    };

    [Theory]
    [MemberData(nameof(DegenerateInks))]
    public void EveryVariationIsDistinctEvenForAwkwardModColours(string description, string hex)
    {
        // Without the value/saturation guards, black ink returns pure black for darker, warmer AND cooler —
        // three of four players drawing in the same colour. This is the whole of mod-colour support.
        var ink = new Color(hex);

        var seen = new Dictionary<string, PlayerVariation>();
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var html = PlayerTint.Shift(v, ink).ToHtml(false);
            Assert.False(
                seen.TryGetValue(html, out var clash),
                $"{description}: {v} and {clash} both produce #{html}");
            seen[html] = v;
        }
    }

    [Theory]
    [MemberData(nameof(DegenerateInks))]
    public void AwkwardModColoursStillProduceValidColours(string description, string hex)
    {
        var ink = new Color(hex);
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var s = PlayerTint.Shift(v, ink);
            Assert.InRange(s.R, 0f, 1f);
            Assert.InRange(s.G, 0f, 1f);
            Assert.InRange(s.B, 0f, 1f);
            Assert.Equal(ink.A, s.A, 4);
        }
    }

    [Theory]
    [MemberData(nameof(ShippedInks))]
    public void TheDegenerateGuardsDoNotTouchShippedColours(string character, string hex)
    {
        // The guards clamp value into a band and floor saturation. Every base-game map colour already sits
        // inside those, so adding mod support must not have moved any of them.
        var ink = new Color(hex);

        Assert.InRange(ink.V, 0.20f, 0.90f);
        Assert.True(ink.S >= 0.35f, $"{character}'s ink is less saturated than the hue-shift floor");
    }

    /// <summary>Shortest signed hue distance, in -0.5..0.5.</summary>
    private static float HueDelta(float from, float to)
    {
        var forward = to - from;
        forward -= (float)Math.Floor(forward);
        return forward <= 0.5f ? forward : forward - 1f;
    }

    [Fact]
    public void InkHueShiftIsStrongerThanTheValueThatWasReportedUnnoticeable()
    {
        // Pins the v0.1.5 retune: 0.035 was too small to see on a thin map stroke, even once the sprite
        // tilt was judged perfect. The two dials are deliberately independent — do not resync them.
        var warmer = PlayerTint.Shift(PlayerVariation.Warmer, Ink);

        var step = Math.Abs(warmer.H - Ink.H);
        step = Math.Min(step, 1f - step);

        Assert.True(step > 0.035f, $"ink hue step is only {step * 360f:F0} deg");
    }
}
