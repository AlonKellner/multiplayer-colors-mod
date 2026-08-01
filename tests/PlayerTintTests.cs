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
    public void ModulatesAreSubtle()
    {
        // Every channel stays within 25% of neutral: clearly visible side by side, still the same
        // character rather than a recolour. The upper bound tracks BrightnessGain in PlayerTint —
        // if you raise the strength dial past this, decide deliberately that it's still "subtle".
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var m = PlayerTint.Modulate(v);
            foreach (var channel in new[] { m.R, m.G, m.B })
            {
                Assert.InRange(channel, 0.75f, 1.25f);
            }
        }
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
    public void ArtTintIsStrongerThanTheOriginalV0_1_1Values()
    {
        // Guards the v0.1.2 retune: the first release's art tint was too easy to miss in a live game
        // (reported after a real 2-player run), while the map ink was already right.
        Assert.True(PlayerTint.Modulate(PlayerVariation.Brighter).R > 1.12f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Darker).R < 0.88f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Warmer).R > 1.08f);
        Assert.True(PlayerTint.Modulate(PlayerVariation.Cooler).B > 1.10f);
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
    public void ShiftIsSubtle()
    {
        // Still recognisably the same ink: no channel moves by more than a quarter.
        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var s = PlayerTint.Shift(v, Ink);
            Assert.True(Math.Abs(s.R - Ink.R) < 0.25f);
            Assert.True(Math.Abs(s.G - Ink.G) < 0.25f);
            Assert.True(Math.Abs(s.B - Ink.B) < 0.25f);
        }
    }
}
