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

    public override string Args => "[" + string.Join("|", Options.Select(o => o.Name)) + "|outline <px>|diag]";

    public override string Description =>
        "Multiplayer Colors: forces a player colour variation on yourself for testing, instead of only "
        + "tinting players who share a character. 'auto' restores normal behaviour, 'off' disables tinting. "
        + "'outline <px>' sets icon outline thickness, 'diag' reports what the mod has done. "
        + "With no argument, reports the current setting.";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length == 0)
        {
            return new CmdResult(success: true, Status());
        }

        var requested = args[0].Trim().ToLowerInvariant();

        if (requested == "outline")
        {
            return Outline(args);
        }

        if (requested == "diag")
        {
            return Diagnose(args);
        }
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

    /// <summary><c>tint outline [px]</c> — report or set how far outlines extend past their icon.</summary>
    private static CmdResult Outline(string[] args)
    {
        if (args.Length < 2)
        {
            return new CmdResult(success: true, $"tint outline: {PlayerTint.OutlineThickness}px "
                + $"(range {PlayerTint.MinOutlineThickness}-{PlayerTint.MaxOutlineThickness}, 0 hides it).");
        }

        if (!float.TryParse(args[1].Trim(), out var requested))
        {
            return new CmdResult(success: false, $"'{args[1]}' is not a number of pixels.");
        }

        PlayerTint.OutlineThickness = PlayerTint.ClampThickness(requested);
        var repainted = IconOutline.RefreshThickness();

        var clamped = Math.Abs(requested - PlayerTint.OutlineThickness) > 0.001f
            ? $" (clamped from {requested})"
            : string.Empty;

        return new CmdResult(
            success: true,
            $"tint outline: {PlayerTint.OutlineThickness}px{clamped} — {repainted} on screen updated.");
    }

    /// <summary><c>tint diag [on|off]</c> — what the mod has actually done, for when nothing shows up.</summary>
    private static CmdResult Diagnose(string[] args)
    {
        if (args.Length >= 2)
        {
            var toggle = args[1].Trim().ToLowerInvariant();
            if (toggle is not ("on" or "off"))
            {
                return new CmdResult(success: false, $"'{args[1]}' is not on or off.");
            }

            Diagnostics.Enabled = toggle == "on";

            // Outlines attach when a screen is built, which is almost always before anyone thinks to turn
            // logging on — so flush what already happened rather than making them reproduce it.
            var flushed = Diagnostics.Enabled ? Diagnostics.FlushToLog() : 0;

            return new CmdResult(
                success: true,
                $"tint diag: logging {toggle}."
                + (flushed > 0 ? $" Wrote {flushed} earlier line(s) to the log." : string.Empty));
        }

        var lines = new List<string>
        {
            Status(),
            $"outline thickness: {PlayerTint.OutlineThickness}px",
            $"diagnostic logging: {(Diagnostics.Enabled ? "on" : "off")}",
            $"sprites tracked: {PlayerTint.TrackedCount}",
        };

        var outlines = IconOutline.Describe();
        lines.Add(outlines.Count == 0
            ? "outlines live: none"
            : $"outlines live: {outlines.Count}");
        lines.AddRange(outlines.Select(l => "  " + l));

        var tail = Diagnostics.Tail();
        lines.Add(tail.Count == 0
            ? "no outline activity recorded yet — open the map, or enter a room with player icons"
            : "recent activity:");
        lines.AddRange(tail.Select(l => "  " + l));

        return new CmdResult(success: true, string.Join("\n", lines));
    }

    private static string Status() => PlayerTint.Override switch
    {
        TintOverride.Auto => "tint: auto — only players sharing a character are tinted.",
        TintOverride.Off => "tint: off — tinting disabled for you (local only; teammates still see you by the normal rule).",
        var forced => $"tint: {forced.ToString().ToLowerInvariant()} — forced on you. "
            + "LOCAL ONLY: teammates still see you by the normal rule, so your screens will disagree "
            + "until you run 'tint auto'.",
    };
}
