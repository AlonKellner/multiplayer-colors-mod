using Godot;
using Xunit;

namespace MultiplayerColors.Tests;

/// <summary>
/// A stand-in for a roster entry. A real <c>Player</c> can only be built through <c>ModelDb</c>, which
/// needs a running game, so the roster logic is tested through <see cref="PlayerTint.Resolve{T}" />.
/// </summary>
/// <remarks>
/// <c>NetId</c> stands in for the player's network id — the thing that is identical on every client.
/// </remarks>
internal sealed record Seat(string Character, ulong NetId);

public class ResolveTests
{
    private static PlayerVariation? Resolve(IReadOnlyList<Seat> roster, Seat seat) =>
        PlayerTint.Resolve(roster, seat, s => s.Character, s => s.NetId);

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
    public void FourWayDuplicate_GetsAllFourVariationsInIdOrder()
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
    public void EveryClientAgreesEvenIfTheirRostersAreOrderedDifferently()
    {
        // The reported bug: players saw different colours from each other. Assignment used to be keyed on
        // position in RunState.Players, which is only consistent by convention. Keyed on the network id
        // instead, two clients holding the same players in different orders cannot disagree.
        var seats = new[]
        {
            new Seat("ironclad", 7701),
            new Seat("ironclad", 4402),
            new Seat("silent", 9903),
            new Seat("ironclad", 1104),
        };

        var clientA = seats.ToArray();
        var clientB = new[] { seats[2], seats[0], seats[3], seats[1] };

        foreach (var seat in seats)
        {
            Assert.Equal(Resolve(clientA, seat), Resolve(clientB, seat));
        }
    }

    [Fact]
    public void AssignmentIsOrderedByNetworkIdNotByPosition()
    {
        // Lowest id first, regardless of where each sits in the list.
        var roster = new[]
        {
            new Seat("ironclad", 500),
            new Seat("ironclad", 100),
            new Seat("ironclad", 300),
        };

        Assert.Equal(PlayerVariation.Brighter, Resolve(roster, roster[1])); // id 100
        Assert.Equal(PlayerVariation.Darker, Resolve(roster, roster[2]));   // id 300
        Assert.Equal(PlayerVariation.Warmer, Resolve(roster, roster[0]));   // id 500
    }

    [Fact]
    public void AssignmentFollowsNetworkId_NotListOrder()
    {
        // The same four seats, enumerated out of id order — which is exactly what the
        // local-player-first UIs do to their own copies. The colours must not move.
        var byId = new[]
        {
            new Seat("ironclad", 0),
            new Seat("ironclad", 1),
            new Seat("ironclad", 2),
            new Seat("ironclad", 3),
        };
        var shuffled = new[] { byId[2], byId[0], byId[3], byId[1] };

        foreach (var seat in byId)
        {
            Assert.Equal(Resolve(byId, seat), Resolve(shuffled, seat));
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
        Assert.Null(PlayerTint.Resolve<Seat>(roster, roster[0], _ => null, s => s.NetId));
    }

    [Fact]
    public void EmptyOrNullRoster_YieldsNoVariation()
    {
        var seat = new Seat("ironclad", 0);
        Assert.Null(PlayerTint.Resolve<Seat>(null, seat, s => s.Character, s => s.NetId));
        Assert.Null(PlayerTint.Resolve(Array.Empty<Seat>(), seat, s => s.Character, s => s.NetId));
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

    /// <summary>The perceptual gap between a colour's warmer and cooler variants.</summary>
    private static float HueSeparation(Color ink) => PlayerTint.PerceptualDistance(
        PlayerTint.Shift(PlayerVariation.Warmer, ink),
        PlayerTint.Shift(PlayerVariation.Cooler, ink));

    [Theory]
    [MemberData(nameof(ShippedInks))]
    public void WarmerAndCoolerInkAreFarEnoughApartToTellApart(string character, string hex)
    {
        // The number that matters is the gap between the two, not the gap from vanilla: the job is telling
        // two players' lines apart on the same map.
        Assert.True(HueSeparation(new Color(hex)) > 5f, $"{character}'s warmer and cooler ink are too close");
    }

    [Fact]
    public void HueSeparationIsConsistentAcrossCharacters()
    {
        // The bias this replaced: a fixed hue angle gave dE 4.6 on Silent and 31.8 on Defect — a 6.9x
        // spread, so the same setting felt too weak on one character and too strong on another. Solving for
        // perceptual distance instead should keep every character within a narrow band.
        var separations = ShippedInks
            .Select(row => (Character: (string)row[0], Separation: HueSeparation(new Color((string)row[1]))))
            .ToList();

        var strongest = separations.MaxBy(x => x.Separation);
        var weakest = separations.MinBy(x => x.Separation);

        Assert.True(
            strongest.Separation / weakest.Separation < 2.5f,
            $"{strongest.Character} ({strongest.Separation:F1}) is {strongest.Separation / weakest.Separation:F1}x "
            + $"the shift of {weakest.Character} ({weakest.Separation:F1}) — the old fixed-angle bias is back");
    }

    [Theory]
    [MemberData(nameof(ShippedInks))]
    public void HueSeparationHitsTheTarget(string character, string hex)
    {
        // At the v0.1.9 target every shipped character can reach it, including Silent — which previously
        // capped out at 7.7 and is now the character the target is anchored on.
        Assert.InRange(HueSeparation(new Color(hex)), 6.5f, 7.5f);
    }

    [Fact]
    public void InkHueShiftCameDownFromTheVersionReportedTooStrong()
    {
        // Every character was reported too strong at dE 16 (v0.1.8). Pinned so a later retune has to make
        // that call deliberately rather than drift back.
        Assert.True(HueSeparation(Ink) < 12f, "Ironclad's ink hue shift is back near the level called way too much");
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

    /// <summary>Character map inks, including a modded one, for the background-visibility checks.</summary>
    public static TheoryData<string, string> InksIncludingModded
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var row in ShippedInks)
            {
                data.Add((string)row[0], (string)row[1]);
            }

            data.Add("Understudy", "F0C040");
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(InksIncludingModded))]
    public void BrightnessVariationsStayVisibleAgainstTheMap(string character, string hex)
    {
        // The reported bug: the Understudy's darker ink landed at #BD9732, dE 7.3 from the overgrowth
        // parchment, and effectively vanished. The bar is the guard's own, or the character's vanilla ink
        // where that is already closer to the background (Regent ships at 14.6).
        var ink = new Color(hex);
        var required = Math.Min(14f, PlayerTint.MapBackgroundDistance(ink));

        foreach (var v in new[] { PlayerVariation.Brighter, PlayerVariation.Darker })
        {
            var guarded = PlayerTint.MapInkFor(v, ink);
            Assert.True(
                PlayerTint.MapBackgroundDistance(guarded) >= required - 0.01f,
                $"{character}'s {v} map ink (#{guarded.ToHtml(false)}) is only "
                + $"{PlayerTint.MapBackgroundDistance(guarded):F1} from the map background");
        }
    }

    [Fact]
    public void TheUnderstudysDarkerInkIsNoLongerInvisible()
    {
        // Straight regression on the exact report.
        var before = PlayerTint.Shift(PlayerVariation.Darker, new Color("F0C040"));
        var after = PlayerTint.MapInkFor(PlayerVariation.Darker, new Color("F0C040"));

        Assert.True(PlayerTint.MapBackgroundDistance(before) < 10f, "the original collision should still reproduce");
        Assert.True(PlayerTint.MapBackgroundDistance(after) >= 14f, "the guard should have moved it clear");
    }

    [Theory]
    [MemberData(nameof(InksIncludingModded))]
    public void HueVariationsKeepTheCharactersOwnBrightness(string character, string hex)
    {
        // The map-visibility guard is deliberately brightness-only. If a character's chosen brightness sits
        // near the parchment that is their colour, not a bug to correct — and correcting it here would make
        // "warmer" silently mean "warmer and lighter".
        var ink = new Color(hex);

        foreach (var v in new[] { PlayerVariation.Warmer, PlayerVariation.Cooler })
        {
            Assert.Equal(
                PlayerTint.Shift(v, ink).V,
                PlayerTint.MapInkFor(v, ink).V,
                3);
        }
    }

    [Fact]
    public void PerceptualDistanceIsZeroForIdenticalColoursAndGrowsWithDifference()
    {
        var red = new Color("CB282B");

        Assert.Equal(0f, PlayerTint.PerceptualDistance(red, red), 3);
        Assert.True(
            PlayerTint.PerceptualDistance(red, new Color("CB4A2B"))
            < PlayerTint.PerceptualDistance(red, new Color("2BCB28")),
            "a small shift should measure closer than a large one");
    }

    [Fact]
    public void PerceptualDistanceRatesEqualRgbStepsByHowTheyLook()
    {
        // The reason OKLab is worth the ~25 lines: identical RGB deltas on dark vs bright colours are not
        // equally visible, and it is exactly that mismatch which biased the old fixed-angle shift.
        var onDark = PlayerTint.PerceptualDistance(new Color("101010"), new Color("303030"));
        var onBright = PlayerTint.PerceptualDistance(new Color("DFDFDF"), new Color("FFFFFF"));

        Assert.True(onDark > onBright, "a step in dark tones should read as a bigger change than the same step in bright ones");
    }
}

/// <summary>
/// The colour key: each player's character icon is outlined in the colour that player draws with, so a line
/// on the map can be traced back to whoever drew it.
/// </summary>
/// <remarks>
/// Only the colour derivation and the decision rules are covered here. Everything that touches a node —
/// reading the vote icon's Outline child, writing SelfModulate, building the panel's outline layer — needs a
/// Godot engine the bare test host does not have, and is verified in game. The point of splitting it this
/// way is that the untestable half ends up containing no decisions at all.
/// </remarks>
public class OutlineTests
{
    /// <summary>The alpha the vote icon's Outline node carries in the shipped scene.</summary>
    private const float SceneAlpha = 0.7529412f;

    [Theory]
    [MemberData(nameof(ShiftTests.ShippedInks), MemberType = typeof(ShiftTests))]
    public void OutlineInkMatchesTheInkTheModDrawsWith(string character, string hex)
    {
        // The entire feature rests on this. An outline in any colour other than the one that player's pen
        // draws with is not a key — it is a second, contradictory signal.
        var ink = new Color(hex);

        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            // Against the ink AS IT RENDERS, not as it is assigned. The game's brush texture is a 90% grey
            // rather than white, and Godot multiplies it into the line's colour, so a stroke always draws
            // about a tenth darker than the value handed to it. Matching the assigned value made the outline
            // reliably too bright.
            var pen = PlayerTint.MapInkFor(v, ink);
            var rendered = PlayerTint.AsRendered(pen);
            var outline = PlayerTint.OutlineInk(v, ink, SceneAlpha);

            Assert.Equal(rendered.R, outline.R, 4);
            Assert.Equal(rendered.G, outline.G, 4);
            Assert.Equal(rendered.B, outline.B, 4);
        }
    }

    [Fact]
    public void TheRenderTintMatchesTheMeasuredBrush()
    {
        // Measured off res://images/packed/vfx/trail2.png: every fully-opaque pixel is exactly
        // (0.9020, 0.8892, 0.9020). Pinned so a future guess cannot quietly replace a measurement.
        Assert.Equal(0.9020f, PlayerTint.InkRenderTint.R, 4);
        Assert.Equal(0.8892f, PlayerTint.InkRenderTint.G, 4);
        Assert.Equal(0.9020f, PlayerTint.InkRenderTint.B, 4);
    }

    [Fact]
    public void AsRenderedReproducesTheColourMeasuredOnScreen()
    {
        // The exact case from the diag: a line assigned #3a8033 read back from the drawing viewport as
        // #35722e. This predicts #34722e — one 8-bit step apart, which is rounding in the render pipeline,
        // not a modelling error. Asserting the predicted value rather than the observed one, since that is
        // what this function can actually promise.
        var rendered = PlayerTint.AsRendered(new Color("3a8033"));

        Assert.Equal("34722e", rendered.ToHtml(false));

        // And within one step of what was measured on screen.
        var measured = new Color("35722e");
        Assert.True(Math.Abs(rendered.R - measured.R) <= 1f / 255f);
        Assert.True(Math.Abs(rendered.G - measured.G) <= 1f / 255f);
        Assert.True(Math.Abs(rendered.B - measured.B) <= 1f / 255f);
    }

    [Fact]
    public void AsRenderedLeavesAlphaAlone()
    {
        Assert.Equal(0.5f, PlayerTint.AsRendered(new Color(0.5f, 0.5f, 0.5f, 0.5f)).A, 4);
    }

    [Fact]
    public void OutlineInkKeepsTheGivenAlpha()
    {
        // The shipped outline is black at 75% alpha, and that softness is what keeps the head readable
        // against parchment. We swap the colour, not the transparency.
        var ink = new Color("CB282B");

        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            Assert.Equal(SceneAlpha, PlayerTint.OutlineInk(v, ink, SceneAlpha).A, 4);
        }
    }

    [Fact]
    public void OutlinesAreDrawnFullyOpaque()
    {
        // No extra dimming: the outline is a colour key and has to read at a glance. This is not the
        // silhouette's shape — the shader multiplies by the texture's own alpha, so the outline still traces
        // the character and still keeps its antialiased edge.
        Assert.Equal(1f, IconOutline.Alpha, 4);
    }

    [Fact]
    public void OutlineInkIsOpaqueWhenAskedToBe()
    {
        Assert.Equal(1f, PlayerTint.OutlineInk(PlayerVariation.Warmer, new Color("CB282B"), 1f).A, 4);
    }

    [Theory]
    [MemberData(nameof(ShiftTests.ShippedInks), MemberType = typeof(ShiftTests))]
    public void OutlineInkIsDistinctForEveryVariation(string character, string hex)
    {
        AssertFourDistinctOutlines(character, hex);
    }

    [Theory]
    [MemberData(nameof(ShiftTests.DegenerateInks), MemberType = typeof(ShiftTests))]
    public void OutlineInkStaysDistinctForAwkwardModColours(string description, string hex)
    {
        // Inherits MapInkFor's degenerate-colour guards. Pins that they keep reaching the outline: an
        // outline that comes out identical for two players is worse than no outline at all.
        AssertFourDistinctOutlines(description, hex);
    }

    private static void AssertFourDistinctOutlines(string label, string hex)
    {
        var ink = new Color(hex);
        var seen = new Dictionary<string, PlayerVariation>();

        foreach (PlayerVariation v in Enum.GetValues<PlayerVariation>())
        {
            var html = PlayerTint.OutlineInk(v, ink, SceneAlpha).ToHtml(false);
            Assert.False(
                seen.TryGetValue(html, out var clash),
                $"{label}: {v} and {clash} both outline as #{html}");
            seen[html] = v;
        }
    }

    [Fact]
    public void UsesTheShippedOutlineTextureWhenAvailable()
    {
        Assert.Equal(OutlineSource.CharacterOutline, PlayerTint.ChooseOutlineSource(outlineAvailable: true));
    }

    [Fact]
    public void FallsBackToAScaledIconWhenNoOutlineTextureExists()
    {
        // character_icon_<id>_outline.png is a convention path, so a modded character may simply not have
        // one. The store page promises modded characters work automatically, so there has to be a rule.
        Assert.Equal(OutlineSource.ScaledIcon, PlayerTint.ChooseOutlineSource(outlineAvailable: false));
    }

    [Fact]
    public void TheFallbackScaleGrowsTheIconEnoughToShowButNotEnoughToLookWrong()
    {
        // The shipped outline art is the silhouette dilated ~4px on 85px, i.e. about 1.09x.
        Assert.InRange(PlayerTint.FallbackOutlineScale, 1.03f, 1.15f);
    }

    [Theory]
    [InlineData(32f, 32f)]   // a measured container wins
    [InlineData(40f, 40f)]
    [InlineData(0f, PlayerTint.FallbackVoteIconSize)]    // nothing measured yet
    [InlineData(0.5f, PlayerTint.FallbackVoteIconSize)]  // measured before layout ran
    public void VoteIconPreviewMatchesTheContainerItWouldSitIn(float measured, float expected)
    {
        // A real vote icon is stretched to the height of the HBoxContainer holding it — 32px on every map
        // point — not left at its own 24px minimum. Sizing the preview to the minimum made it visibly
        // smaller than the co-op icon it is meant to be showing.
        Assert.Equal(expected, PlayerTint.VoteIconPreviewSize(measured), 3);
    }

    [Fact]
    public void TheVoteIconFallbackMatchesTheShippedMapPoints()
    {
        // All three map point scenes give their vote container a 32px height.
        Assert.Equal(32f, PlayerTint.FallbackVoteIconSize, 3);
    }

    [Fact]
    public void ThicknessDefaultsToThreePixels()
    {
        Assert.Equal(3f, PlayerTint.DefaultOutlineThickness, 3);
    }

    [Fact]
    public void TheShaderReplacesColourAndKeepsAlpha()
    {
        // Modulate multiplies, so it can only tint art — it cannot flatten a full-colour icon into a solid
        // silhouette, which is why the grown-icon fallback came out as "the same icon but larger". The
        // shader assigns RGB outright and carries alpha through, which is the only way to get a silhouette
        // out of arbitrary character art.
        var code = OutlineShader.Code;

        Assert.Contains("shader_type canvas_item", code);
        Assert.Contains($"uniform vec4 {OutlineShader.ColorParameter} =", code);

        // RGB comes from the uniform, alpha from what was already there.
        Assert.Contains($"{OutlineShader.ColorParameter}.rgb", code);
        Assert.Contains("COLOR.a", code);
    }

    [Fact]
    public void TheShaderParameterNameMatchesTheShaderSource()
    {
        // The C# side sets this by string; if the two drift the outline silently never changes colour.
        Assert.Contains($"uniform vec4 {OutlineShader.ColorParameter} =", OutlineShader.Code);
    }

    [Theory]
    [InlineData(new[] { 0.2f, 0.5f, 0.9f }, 0.2f, 0.9f)]
    [InlineData(new[] { 0f, 1f }, 0f, 1f)]
    [InlineData(new[] { 0.4f }, 0.4f, 0.4f)]
    public void AlphaRangeIsTheMinimumAndMaximumPresent(float[] alphas, float min, float max)
    {
        var range = OutlineShader.AlphaRange(alphas);

        Assert.Equal(min, range.Min, 4);
        Assert.Equal(max, range.Max, 4);
    }

    [Fact]
    public void AlphaRangeOfNothingIsTheIdentityRange()
    {
        // No pixels to measure means no rescaling — 0..1 leaves the texture exactly as it is.
        var range = OutlineShader.AlphaRange([]);

        Assert.Equal(0f, range.Min, 4);
        Assert.Equal(1f, range.Max, 4);
    }

    [Theory]
    [InlineData(0.2f, 0.2f, 0.9f, 0f)]      // the least opaque pixel becomes fully transparent
    [InlineData(0.9f, 0.2f, 0.9f, 1f)]      // the most opaque pixel becomes fully blocking
    [InlineData(0.55f, 0.2f, 0.9f, 0.5f)]   // and the midpoint lands halfway
    [InlineData(0.5f, 0.4f, 0.4f, 0.5f)]    // a flat texture is left alone rather than divided by zero
    public void NormalisingAlphaStretchesTheRangeToFillZeroToOne(
        float raw, float min, float max, float expected)
    {
        Assert.Equal(expected, OutlineShader.NormalizeAlpha(raw, min, max), 4);
    }

    [Fact]
    public void NormalisedAlphaNeverLeavesZeroToOne()
    {
        // Values outside the measured range can only come from a texture whose range was measured
        // elsewhere, but clamping costs nothing and a negative alpha would render as garbage.
        Assert.Equal(0f, OutlineShader.NormalizeAlpha(0.1f, 0.2f, 0.9f), 4);
        Assert.Equal(1f, OutlineShader.NormalizeAlpha(1f, 0.2f, 0.9f), 4);
    }

    [Fact]
    public void TheShaderNormalisesAlphaWithUniformsTheCSharpCanSet()
    {
        var code = OutlineShader.Code;

        Assert.Contains($"uniform float {OutlineShader.MinAlphaParameter}", code);
        Assert.Contains($"uniform float {OutlineShader.MaxAlphaParameter}", code);
    }

    [Fact]
    public void TheColourUniformIsNotHintedAsASourceColour()
    {
        // A ": source_color" hint makes Godot colour-convert the uniform on upload, while Modulate — the
        // thing this has to match — is passed through raw. With the hint the outline came out the right hue
        // at the wrong shade, which is exactly the "does not match the map drawing color" report.
        Assert.DoesNotContain("source_color", OutlineShader.Code);
    }

    [Theory]
    [InlineData(-10f, PlayerTint.MinOutlineThickness)]
    [InlineData(0f, 0f)]
    [InlineData(4f, 4f)]
    [InlineData(999f, PlayerTint.MaxOutlineThickness)]
    public void ThicknessIsClampedToARangeThatCannotLookBroken(float requested, float expected)
    {
        // Zero is allowed — it is how you turn the outline off without turning the tint off.
        Assert.Equal(expected, PlayerTint.ClampThickness(requested), 3);
    }

    [Fact]
    public void AnInactiveOutlineIsFullyTransparent()
    {
        // Outlines this mod creates did not exist in the vanilla game, so with no variation they have to
        // disappear rather than fall back to some colour. This is what keeps ordinary solo play untouched.
        Assert.Equal(0f, PlayerTint.DormantOutline.A, 4);
    }
}

/// <summary>
/// The aura: a faint coloured glow behind each tinted figure, keyed to the variation rather than to the
/// character — white, black, red, blue.
/// </summary>
/// <remarks>
/// The tint is a *relative* signal ("this Ironclad is a fifth brighter than that one") and any transform
/// the game applies on top destroys it: NCombatRoom.PositionPlayersAndPets assigns Modulate = 0.5 grey to
/// back-row players, so a brighter player in the back can read darker than a darker player in the front.
/// The aura is the absolute signal underneath it — a white halo stays light and a black halo stays dark
/// however the figure is dimmed.
///
/// As with <see cref="OutlineTests" />, only the colour derivation and the geometry are covered here.
/// Building the ColorRect, measuring a live skeleton and assigning the shader uniform all need a Godot
/// engine the bare host does not have, and are verified in game through <c>tint diag</c>.
/// </remarks>
public class AuraTests
{
    private static IEnumerable<PlayerVariation> AllVariations => Enum.GetValues<PlayerVariation>();

    [Fact]
    public void AuraColoursAreTheFourNamedColours()
    {
        Assert.Equal(Colors.White, PlayerTint.AuraColor(PlayerVariation.Brighter));
        Assert.Equal(Colors.Black, PlayerTint.AuraColor(PlayerVariation.Darker));

        var warm = PlayerTint.AuraColor(PlayerVariation.Warmer);
        var cool = PlayerTint.AuraColor(PlayerVariation.Cooler);

        Assert.True(warm.R > 0.8f && warm.G < 0.4f && warm.B < 0.4f, $"warmer should read as red, got #{warm.ToHtml(false)}");
        Assert.True(cool.B > 0.8f && cool.R < 0.4f, $"cooler should read as blue, got #{cool.ToHtml(false)}");
    }

    [Theory]
    [MemberData(nameof(ShiftTests.ShippedInks), MemberType = typeof(ShiftTests))]
    public void AuraColourDoesNotDependOnTheCharacter(string character, string hex)
    {
        // The whole reason the aura exists: it has to survive transformations that destroy a relative
        // signal, so it cannot itself be derived from the character's own colour the way map ink is.
        _ = new Color(hex);

        foreach (var v in AllVariations)
        {
            Assert.Equal(PlayerTint.AuraColor(v), PlayerTint.AuraColor(v));
        }

        Assert.Equal(Colors.White, PlayerTint.AuraColor(PlayerVariation.Brighter));
    }

    [Fact]
    public void EveryPairOfAuraColoursIsPerceptuallyDistinct()
    {
        // Two players whose auras look alike is worse than no aura at all: it reads as a signal and says
        // nothing. Measured in OKLab, the same scale the ink hue solver is tuned against.
        var colours = AllVariations.Select(PlayerTint.AuraColor).ToList();

        for (var i = 0; i < colours.Count; i++)
        {
            for (var j = i + 1; j < colours.Count; j++)
            {
                var distance = PlayerTint.PerceptualDistance(colours[i], colours[j]);
                Assert.True(distance > 30f, $"auras {i} and {j} are only dE {distance:F1} apart");
            }
        }
    }

    [Fact]
    public void BrighterAndDarkerAurasSitAtOppositeEndsOfLightness()
    {
        var brighter = PlayerTint.AuraColor(PlayerVariation.Brighter);
        var darker = PlayerTint.AuraColor(PlayerVariation.Darker);

        Assert.Equal(1f, brighter.V, 3);
        Assert.Equal(0f, darker.V, 3);
    }

    [Fact]
    public void WarmerAurasAreRedderAndCoolerAurasBluer()
    {
        // Must agree with the direction the sprite multiplier already moves: WarmerMul raises red and drops
        // blue, CoolerMul does the reverse. An aura that disagreed with the tint would be a contradiction,
        // not a reinforcement.
        var warm = PlayerTint.AuraColor(PlayerVariation.Warmer);
        var cool = PlayerTint.AuraColor(PlayerVariation.Cooler);

        Assert.True(warm.R > warm.B, "the warmer aura is not red-dominant");
        Assert.True(cool.B > cool.R, "the cooler aura is not blue-dominant");
        Assert.True(warm.R > cool.R, "the warmer aura is not redder than the cooler one");
        Assert.True(cool.B > warm.B, "the cooler aura is not bluer than the warmer one");
    }

    [Fact]
    public void AnAuraCarriesTheRequestedStrengthAsItsAlpha()
    {
        foreach (var v in AllVariations)
        {
            var colour = PlayerTint.AuraFor(v, 0.25f);
            var basis = PlayerTint.AuraColor(v);

            Assert.Equal(0.25f, colour.A, 4);
            Assert.Equal(basis.R, colour.R, 4);
            Assert.Equal(basis.G, colour.G, 4);
            Assert.Equal(basis.B, colour.B, 4);
        }
    }

    [Fact]
    public void AnAuraAtZeroStrengthIsInvisible()
    {
        // How `tint aura 0` turns the aura off without turning the tint off.
        Assert.Equal(0f, PlayerTint.AuraFor(PlayerVariation.Warmer, 0f).A, 4);
    }

    [Fact]
    public void AnInactiveAuraIsFullyTransparent()
    {
        // Auras did not exist in the vanilla game, so with no variation they disappear rather than fall
        // back to a colour of their own — the same rule as an outline this mod created.
        Assert.Equal(0f, PlayerTint.DormantAura.A, 4);
    }

    [Theory]
    [InlineData(-1f, PlayerTint.MinAuraStrength)]
    [InlineData(0f, 0f)]
    [InlineData(0.3f, 0.3f)]
    [InlineData(99f, PlayerTint.MaxAuraStrength)]
    public void AuraStrengthIsClampedToZeroThroughOne(float requested, float expected)
    {
        Assert.Equal(expected, PlayerTint.ClampAuraStrength(requested), 4);
    }

    [Fact]
    public void DefaultAuraStrengthIsSubtle()
    {
        // "Slight, and soft, not obviously perceptible" is the whole brief. A default that reads as a glow
        // effect rather than a hint would fail it, and the console dial exists for the tuning pass.
        Assert.InRange(PlayerTint.DefaultAuraStrength, 0.08f, 0.30f);
    }

    [Fact]
    public void TheFalloffIsFullAtTheCentreAndZeroAtTheFrameEdge()
    {
        // Full in the middle, where the figure covers it, and gone by the frame edge — that occlusion is
        // what turns a plain radial gradient into a halo that hugs the silhouette.
        Assert.Equal(1f, AuraShader.Falloff(0f, AuraShader.Softness), 4);
        Assert.Equal(0f, AuraShader.Falloff(1f, AuraShader.Softness), 4);
        Assert.Equal(0f, AuraShader.Falloff(2f, AuraShader.Softness), 4);
    }

    [Fact]
    public void TheFalloffNeverLeavesZeroToOne()
    {
        for (var d = -1f; d <= 2f; d += 0.05f)
        {
            Assert.InRange(AuraShader.Falloff(d, AuraShader.Softness), 0f, 1f);
        }
    }

    [Fact]
    public void TheFalloffDecreasesMonotonicallyOutward()
    {
        var previous = float.MaxValue;
        for (var d = 0f; d <= 1f; d += 0.05f)
        {
            var value = AuraShader.Falloff(d, AuraShader.Softness);
            Assert.True(value <= previous + 0.0001f, $"falloff rose at d={d:F2}");
            previous = value;
        }
    }

    [Fact]
    public void TheFalloffIsSoftRatherThanAHardEdge()
    {
        // A linear ramp already looks like a lens flare at this size; the exponent is what makes it read as
        // a haze. Halfway out it should have given up well over half its strength.
        Assert.True(
            AuraShader.Falloff(0.5f, AuraShader.Softness) < 0.5f,
            "the falloff is not softened at all — softness is doing nothing");
    }

    [Fact]
    public void TheFrameIsConcentricWithTheArt()
    {
        var bounds = new Rect2(new Vector2(-121f, -278f), new Vector2(242f, 278f));

        var frame = AuraLayer.Frame(bounds, 0.25f);

        Assert.Equal(bounds.GetCenter().X, frame.GetCenter().X, 3);
        Assert.Equal(bounds.GetCenter().Y, frame.GetCenter().Y, 3);
    }

    [Fact]
    public void TheFrameGrowsByTheSameAmountOnEveryEdge()
    {
        // Grown by a fraction of the SMALLER side, so the halo looks the same width on a 242px character
        // box and on a 383px hand rather than stretching with the art's aspect.
        var bounds = new Rect2(new Vector2(0f, 0f), new Vector2(200f, 400f));

        var frame = AuraLayer.Frame(bounds, 0.25f);
        var margin = 0.25f * 200f;

        Assert.Equal(bounds.Position.X - margin, frame.Position.X, 3);
        Assert.Equal(bounds.Position.Y - margin, frame.Position.Y, 3);
        Assert.Equal(bounds.Size.X + 2f * margin, frame.Size.X, 3);
        Assert.Equal(bounds.Size.Y + 2f * margin, frame.Size.Y, 3);
    }

    [Theory]
    [InlineData(-1f, PlayerTint.MinAuraSpread)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(99f, PlayerTint.MaxAuraSpread)]
    public void SpreadIsClampedToARangeThatCannotLookBroken(float requested, float expected)
    {
        Assert.Equal(expected, PlayerTint.ClampAuraSpread(requested), 4);
    }

    [Fact]
    public void DegenerateBoundsAreRejectedRatherThanDrawn()
    {
        // A skeleton that has not been posed yet reports an empty box. Drawing a frame around it would put
        // a coloured dot at the character's origin, which is worse than nothing — and the diag line saying
        // bounds=0x0 is how that gets noticed.
        Assert.False(AuraLayer.IsMeasurable(new Rect2()));
        Assert.False(AuraLayer.IsMeasurable(new Rect2(0f, 0f, 0.5f, 300f)));
        Assert.False(AuraLayer.IsMeasurable(new Rect2(0f, 0f, -200f, 300f)));
        Assert.False(AuraLayer.IsMeasurable(new Rect2(0f, 0f, float.NaN, 300f)));
        Assert.False(AuraLayer.IsMeasurable(new Rect2(0f, 0f, 1e6f, 300f)));

        Assert.True(AuraLayer.IsMeasurable(new Rect2(-121f, -278f, 242f, 278f)));
    }

    [Fact]
    public void KeepAspectCentredArtIsMeasuredWhereItIsActuallyDrawn()
    {
        // hand_image.tscn's TextureRect is 383x1072 with expand_mode = 1 and stretch_mode = 5, so the arm
        // is letterboxed inside a rect far taller than itself. Framing the raw rect would put the glow's
        // peak off the bottom of the screen instead of around the hand.
        var drawn = AuraBounds.DrawnRect(
            new Vector2(383f, 1072f),
            new Vector2(383f, 383f),
            TextureRect.StretchModeEnum.KeepAspectCentered);

        Assert.Equal(383f, drawn.Size.X, 3);
        Assert.Equal(383f, drawn.Size.Y, 3);
        Assert.Equal(0f, drawn.Position.X, 3);
        Assert.Equal((1072f - 383f) / 2f, drawn.Position.Y, 3);
    }

    [Fact]
    public void ArtThatFillsItsRectIsMeasuredAsTheWholeRect()
    {
        var size = new Vector2(100f, 250f);

        var stretched = AuraBounds.DrawnRect(size, new Vector2(64f, 64f), TextureRect.StretchModeEnum.Scale);

        Assert.Equal(new Rect2(Vector2.Zero, size), stretched);
    }

    [Fact]
    public void ArtWithNoTextureFallsBackToTheWholeRect()
    {
        var size = new Vector2(100f, 250f);

        Assert.Equal(
            new Rect2(Vector2.Zero, size),
            AuraBounds.DrawnRect(size, Vector2.Zero, TextureRect.StretchModeEnum.KeepAspectCentered));
    }

    [Fact]
    public void TheShaderReplacesColourAndKeepsTheAncestorFade()
    {
        var code = AuraShader.Code;

        Assert.Contains("shader_type canvas_item", code);
        Assert.Contains($"uniform vec4 {AuraShader.ColorParameter} =", code);

        // RGB comes from the uniform; COLOR.a carries whatever an ancestor is fading by, so a figure
        // tweened out takes its aura with it.
        Assert.Contains($"{AuraShader.ColorParameter}.rgb", code);
        Assert.Contains("COLOR.a", code);
    }

    [Fact]
    public void TheShaderDeclaresEveryUniformTheCSharpSetsByName()
    {
        // Both are written by string from C#; a name that drifts means the uniform silently keeps its
        // default and the aura never changes.
        Assert.Contains($"uniform vec4 {AuraShader.ColorParameter} =", AuraShader.Code);
        Assert.Contains($"uniform float {AuraShader.SoftnessParameter} =", AuraShader.Code);
    }

    [Fact]
    public void TheCSharpFalloffMirrorsTheOneInTheShader()
    {
        // AuraShader.Falloff is what the tests above actually exercise; the shader is what the player sees.
        // Nothing but this stops the two drifting into different curves.
        Assert.Contains("pow(clamp(1.0 - length(p), 0.0, 1.0), softness)", AuraShader.Code);
    }

    [Fact]
    public void TheShaderDefaultSoftnessMatchesTheCSharpConstant()
    {
        // The uniform's default is what a material carries if SetShaderParameter is ever missed, so the two
        // should not be able to disagree about what "soft" means.
        Assert.Contains(
            $"uniform float {AuraShader.SoftnessParameter} = {AuraShader.Softness:0.0}",
            AuraShader.Code);
    }

    [Fact]
    public void TheAuraUniformIsNotHintedAsASourceColour()
    {
        // Same trap the outline shader documents: ": source_color" makes Godot colour-convert on upload
        // while Modulate is passed through raw, so the two disagree about what the colour is.
        Assert.DoesNotContain("source_color", AuraShader.Code);
    }

    [Fact]
    public void TheAuraIsDrawnBehindTheArtItBelongsTo()
    {
        // Stated as a constant rather than left to the node code, because the whole illusion depends on it:
        // in front, the aura would wash the figure out instead of haloing it.
        Assert.True(AuraLayer.DrawnBehindArt);
    }
}
