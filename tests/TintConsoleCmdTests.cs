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
    public void OverrideIsLocalOnly()
    {
        // The command must not be networked: it changes only what this client draws, so forcing a colour
        // on yourself can never alter what a teammate sees.
        Assert.False(_cmd.IsNetworked);
    }
}
