using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MultiplayerColors;

/// <summary>
/// <c>tint [auto|off|brighter|darker|warmer|cooler]</c> — forces a colour variation onto the current player
/// so all four can be compared without getting four people into a lobby.
/// </summary>
/// <remarks>
/// Registration is automatic: <c>DevConsole</c>'s constructor concatenates
/// <c>ReflectionHelper.GetSubtypesInMods&lt;AbstractConsoleCmd&gt;()</c> onto the built-in command list, so
/// subclassing and being present in the assembly is all it takes.
///
/// <c>DebugOnly</c> is left at its default <c>true</c>, which is honest about what this is — and costs
/// nothing, because the console enables debug commands when <c>ModManager.IsRunningModded()</c>, which is
/// necessarily true wherever this command exists at all.
///
/// <c>IsNetworked</c> is false: the override only changes what this client draws. It never reaches the other
/// players, so their view of you stays on the normal rule.
/// </remarks>
public class TintConsoleCmd : AbstractConsoleCmd
{
    private static readonly (string Name, TintOverride Value)[] Options =
    [
        ("auto", TintOverride.Auto),
        ("off", TintOverride.Off),
        ("brighter", TintOverride.Brighter),
        ("darker", TintOverride.Darker),
        ("warmer", TintOverride.Warmer),
        ("cooler", TintOverride.Cooler),
    ];

    public override string CmdName => "tint";

    public override string Args => "[" + string.Join("|", Options.Select(o => o.Name)) + "]";

    public override string Description =>
        "Multiplayer Colors: forces a player colour variation on yourself for testing, instead of only "
        + "tinting players who share a character. 'auto' restores normal behaviour, 'off' disables tinting. "
        + "With no argument, reports the current setting.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
        {
            return new CmdResult(success: true, Status());
        }

        var requested = args[0].Trim().ToLowerInvariant();
        var match = Options.FirstOrDefault(o => o.Name == requested);
        if (match.Name == null)
        {
            return new CmdResult(success: false, $"Unknown tint '{args[0]}'. Expected one of: {Args}.");
        }

        PlayerTint.Override = match.Value;
        var repainted = PlayerTint.Refresh();

        // Flat colours are redrawn from scratch each time, so they pick the override up on their own; only
        // say something when there was genuinely nothing on screen to recolour.
        var note = repainted == 0
            ? " (nothing on screen to recolour yet — it will apply as art is drawn)"
            : $" ({repainted} sprite{(repainted == 1 ? "" : "s")} recoloured)";

        return new CmdResult(success: true, Status() + note);
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length > 1)
        {
            return base.GetArgumentCompletions(player, args);
        }

        return CompleteArgument(
            Options.Select(o => o.Name),
            [],
            args.Length == 1 ? args[0] : string.Empty);
    }

    private static string Status() => PlayerTint.Override switch
    {
        TintOverride.Auto => "tint: auto — only players sharing a character are tinted.",
        TintOverride.Off => "tint: off — tinting disabled for you.",
        var forced => $"tint: {forced.ToString().ToLowerInvariant()} — forced on you.",
    };
}
