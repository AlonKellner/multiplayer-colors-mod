using Xunit;

namespace MultiplayerColors.Tests;

/// <summary>
/// The <c>tint</c> command is the only way to see three of the four variations without four players in a
/// lobby, so it needs to be right before it can be used to judge whether the colours are right.
/// </summary>
/// <remarks>
/// These tests mutate <see cref="PlayerTint.Override" />, a static, so each one restores it. They run in the
/// bare host with no Godot runtime: <c>Process</c> only touches managed state and an empty node table.
/// </remarks>
public class TintConsoleCmdTests : IDisposable
{
    private readonly TintConsoleCmd _cmd = new();

    public void Dispose() => PlayerTint.Override = TintOverride.Auto;

    [Fact]
    public void CommandNameDoesNotCollideWithABaseGameCommand()
    {
        // The console keys commands by name in a dictionary, so a collision would silently replace one of
        // the game's own. "tint" is unused as of game v0.110.1.
        Assert.Equal("tint", _cmd.CmdName);
    }

    [Theory]
    [InlineData("brighter", TintOverride.Brighter)]
    [InlineData("darker", TintOverride.Darker)]
    [InlineData("warmer", TintOverride.Warmer)]
    [InlineData("cooler", TintOverride.Cooler)]
    [InlineData("off", TintOverride.Off)]
    [InlineData("auto", TintOverride.Auto)]
    public void SetsTheRequestedOverride(string arg, TintOverride expected)
    {
        PlayerTint.Override = TintOverride.Off;

        var result = _cmd.Process(null, [arg]);

        Assert.True(result.success, result.msg);
        Assert.Equal(expected, PlayerTint.Override);
    }

    [Fact]
    public void ArgumentIsCaseAndWhitespaceInsensitive()
    {
        Assert.True(_cmd.Process(null, ["  WaRmEr "]).success);
        Assert.Equal(TintOverride.Warmer, PlayerTint.Override);
    }

    [Fact]
    public void UnknownArgumentFailsAndLeavesTheOverrideAlone()
    {
        PlayerTint.Override = TintOverride.Darker;

        var result = _cmd.Process(null, ["chartreuse"]);

        Assert.False(result.success);
        Assert.Contains("chartreuse", result.msg);
        Assert.Equal(TintOverride.Darker, PlayerTint.Override);
    }

    [Fact]
    public void NoArgumentReportsWithoutChangingAnything()
    {
        PlayerTint.Override = TintOverride.Cooler;

        var result = _cmd.Process(null, []);

        Assert.True(result.success);
        Assert.Contains("cooler", result.msg);
        Assert.Equal(TintOverride.Cooler, PlayerTint.Override);
    }

    [Fact]
    public void EveryOverrideValueIsReachableFromTheCommand()
    {
        // Adding a variation to the enum without adding it here would leave it untestable in game.
        foreach (TintOverride value in Enum.GetValues<TintOverride>())
        {
            PlayerTint.Override = TintOverride.Auto;
            var result = _cmd.Process(null, [value.ToString().ToLowerInvariant()]);

            Assert.True(result.success, $"'{value}' is not accepted by the tint command");
            Assert.Equal(value, PlayerTint.Override);
        }
    }

    [Fact]
    public void ArgsStringListsEveryAcceptedValue()
    {
        // The usage line shown by `help` has to stay in step with what Process actually accepts.
        foreach (TintOverride value in Enum.GetValues<TintOverride>())
        {
            Assert.Contains(value.ToString().ToLowerInvariant(), _cmd.Args);
        }
    }

    [Fact]
    public void OverrideEnumLinesUpWithPlayerVariation()
    {
        // PlayerTint.For casts a forced TintOverride to a PlayerVariation by subtracting Brighter. If the
        // two enums ever fall out of order that cast silently yields the wrong colour.
        foreach (PlayerVariation variation in Enum.GetValues<PlayerVariation>())
        {
            var asOverride = Enum.Parse<TintOverride>(variation.ToString());
            Assert.Equal(variation, (PlayerVariation)(asOverride - TintOverride.Brighter));
        }
    }

    [Fact]
    public void SetsTheOutlineThickness()
    {
        try
        {
            var result = _cmd.Process(null, ["outline", "4"]);

            Assert.True(result.success, result.msg);
            Assert.Equal(4f, PlayerTint.OutlineThickness, 3);
        }
        finally
        {
            PlayerTint.OutlineThickness = PlayerTint.DefaultOutlineThickness;
        }
    }

    [Fact]
    public void ClampsAnOutOfRangeThickness()
    {
        try
        {
            Assert.True(_cmd.Process(null, ["outline", "500"]).success);
            Assert.Equal(PlayerTint.MaxOutlineThickness, PlayerTint.OutlineThickness, 3);
        }
        finally
        {
            PlayerTint.OutlineThickness = PlayerTint.DefaultOutlineThickness;
        }
    }

    [Fact]
    public void ReportsTheThicknessWhenGivenNoNumber()
    {
        var result = _cmd.Process(null, ["outline"]);

        Assert.True(result.success);
        Assert.Contains("outline", result.msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAThicknessThatIsNotANumber()
    {
        var before = PlayerTint.OutlineThickness;

        var result = _cmd.Process(null, ["outline", "thick"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.OutlineThickness, 3);
    }

    [Fact]
    public void OutlineIsNotMistakenForAVariation()
    {
        // "outline" shares the argument slot with the variation names; setting thickness must not silently
        // clear the variation the user is previewing.
        PlayerTint.Override = TintOverride.Warmer;
        try
        {
            _cmd.Process(null, ["outline", "2"]);
            Assert.Equal(TintOverride.Warmer, PlayerTint.Override);
        }
        finally
        {
            PlayerTint.OutlineThickness = PlayerTint.DefaultOutlineThickness;
        }
    }

    [Theory]
    [InlineData("character", true)]
    [InlineData("marker", false)]
    public void SwapsTheSoloMapIcon(string arg, bool expected)
    {
        try
        {
            var result = _cmd.Process(null, ["icon", arg]);

            Assert.True(result.success, result.msg);
            Assert.Equal(expected, PlayerTint.UseCharacterIconOnMap);
        }
        finally
        {
            PlayerTint.UseCharacterIconOnMap = false;
        }
    }

    [Fact]
    public void ReportsTheIconChoiceWhenGivenNoArgument()
    {
        var result = _cmd.Process(null, ["icon"]);

        Assert.True(result.success);
        Assert.Contains("icon", result.msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnUnknownIconChoice()
    {
        var before = PlayerTint.UseCharacterIconOnMap;

        var result = _cmd.Process(null, ["icon", "portrait"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.UseCharacterIconOnMap);
    }

    [Fact]
    public void SwappingTheIconDoesNotClearTheVariation()
    {
        PlayerTint.Override = TintOverride.Cooler;
        try
        {
            _cmd.Process(null, ["icon", "character"]);
            Assert.Equal(TintOverride.Cooler, PlayerTint.Override);
        }
        finally
        {
            PlayerTint.UseCharacterIconOnMap = false;
        }
    }

    [Fact]
    public void SetsTheAuraStrength()
    {
        try
        {
            var result = _cmd.Process(null, ["aura", "0.4"]);

            Assert.True(result.success, result.msg);
            Assert.Equal(0.4f, PlayerTint.AuraStrength, 3);
        }
        finally
        {
            PlayerTint.AuraStrength = PlayerTint.DefaultAuraStrength;
        }
    }

    [Fact]
    public void ClampsAnOutOfRangeAuraStrength()
    {
        try
        {
            Assert.True(_cmd.Process(null, ["aura", "9"]).success);
            Assert.Equal(PlayerTint.MaxAuraStrength, PlayerTint.AuraStrength, 3);
        }
        finally
        {
            PlayerTint.AuraStrength = PlayerTint.DefaultAuraStrength;
        }
    }

    [Fact]
    public void SetsTheAuraSpread()
    {
        try
        {
            var result = _cmd.Process(null, ["aura", "spread", "0.4"]);

            Assert.True(result.success, result.msg);
            Assert.Equal(0.4f, PlayerTint.AuraSpread, 3);
            Assert.Equal(PlayerTint.DefaultAuraStrength, PlayerTint.AuraStrength, 3);
        }
        finally
        {
            PlayerTint.AuraSpread = PlayerTint.DefaultAuraSpread;
        }
    }

    [Fact]
    public void ReportsTheAuraWhenGivenNoNumber()
    {
        var result = _cmd.Process(null, ["aura"]);

        Assert.True(result.success);
        Assert.Contains("aura", result.msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAnAuraStrengthThatIsNotANumber()
    {
        var before = PlayerTint.AuraStrength;

        var result = _cmd.Process(null, ["aura", "glowy"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.AuraStrength, 3);
    }

    [Fact]
    public void RejectsAnAuraSpreadThatIsNotANumber()
    {
        var before = PlayerTint.AuraSpread;

        var result = _cmd.Process(null, ["aura", "spread", "wide"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.AuraSpread, 3);
    }

    [Fact]
    public void AuraIsNotMistakenForAVariation()
    {
        PlayerTint.Override = TintOverride.Darker;
        try
        {
            _cmd.Process(null, ["aura", "0.3"]);
            Assert.Equal(TintOverride.Darker, PlayerTint.Override);
        }
        finally
        {
            PlayerTint.AuraStrength = PlayerTint.DefaultAuraStrength;
        }
    }

    [Fact]
    public void SetsTheParticleStrength()
    {
        try
        {
            var result = _cmd.Process(null, ["particles", "0.5"]);

            Assert.True(result.success, result.msg);
            Assert.Equal(0.5f, PlayerTint.ParticleStrength, 3);
        }
        finally
        {
            PlayerTint.ParticleStrength = PlayerTint.DefaultParticleStrength;
        }
    }

    [Fact]
    public void SetsTheParticleCount()
    {
        try
        {
            var result = _cmd.Process(null, ["particles", "count", "6"]);

            Assert.True(result.success, result.msg);
            Assert.Equal(6, PlayerTint.ParticleCount);
            Assert.Equal(PlayerTint.DefaultParticleStrength, PlayerTint.ParticleStrength, 3);
        }
        finally
        {
            PlayerTint.ParticleCount = PlayerTint.DefaultParticleCount;
        }
    }

    [Fact]
    public void ClampsAnOutOfRangeParticleCount()
    {
        try
        {
            Assert.True(_cmd.Process(null, ["particles", "count", "5000"]).success);
            Assert.Equal(PlayerTint.MaxParticleCount, PlayerTint.ParticleCount);
        }
        finally
        {
            PlayerTint.ParticleCount = PlayerTint.DefaultParticleCount;
        }
    }

    [Fact]
    public void ReportsTheParticlesWhenGivenNoNumber()
    {
        var result = _cmd.Process(null, ["particles"]);

        Assert.True(result.success);
        Assert.Contains("particles", result.msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsAParticleStrengthThatIsNotANumber()
    {
        var before = PlayerTint.ParticleStrength;

        var result = _cmd.Process(null, ["particles", "sparkly"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.ParticleStrength, 3);
    }

    [Fact]
    public void RejectsAParticleCountThatIsNotANumber()
    {
        var before = PlayerTint.ParticleCount;

        var result = _cmd.Process(null, ["particles", "count", "lots"]);

        Assert.False(result.success);
        Assert.Equal(before, PlayerTint.ParticleCount);
    }

    [Fact]
    public void ParticlesAreNotMistakenForAVariation()
    {
        PlayerTint.Override = TintOverride.Cooler;
        try
        {
            _cmd.Process(null, ["particles", "0.3"]);
            Assert.Equal(TintOverride.Cooler, PlayerTint.Override);
        }
        finally
        {
            PlayerTint.ParticleStrength = PlayerTint.DefaultParticleStrength;
        }
    }

    [Fact]
    public void ArgsStringMentionsEverySubcommand()
    {
        // The usage line shown by `help` is the only place these are discoverable in game.
        foreach (var sub in new[] { "outline", "icon", "aura", "particles", "diag" })
        {
            Assert.Contains(sub, _cmd.Args);
        }
    }

    [Fact]
    public void ReportsDiagnostics()
    {
        var result = _cmd.Process(null, ["diag"]);

        Assert.True(result.success, result.msg);
        Assert.False(string.IsNullOrWhiteSpace(result.msg));
    }

    [Fact]
    public void OverrideIsLocalOnly()
    {
        // The command must not be networked: it changes only what this client draws, so forcing a colour
        // on yourself can never alter what a teammate sees.
        Assert.False(_cmd.IsNetworked);
    }
}
