using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MultiplayerColors.Patches;

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

    public override string Args => "[" + string.Join("|", Options.Select(o => o.Name))
        + "|outline <px>|aura <0-1>|aura spread <0-1>|particles <0-1>|particles count <n>"
        + "|icon <marker|character>|diag]";

    public override string Description =>
        "Multiplayer Colors: forces a player colour variation on yourself for testing, instead of only "
        + "tinting players who share a character. 'auto' restores normal behaviour, 'off' disables tinting. "
        + "'outline <px>' sets icon outline thickness, 'aura' sets the strength of the glow behind "
        + "tinted art, 'particles' the strength of the motes drifting around it, 'icon' swaps the solo map "
        + "pin for the co-op head icon, 'diag' reports what the mod has done. "
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

        if (requested == "aura")
        {
            return Aura(args);
        }

        if (requested == "particles")
        {
            return Particles(args);
        }

        if (requested == "diag")
        {
            return Diagnose(args);
        }

        if (requested == "icon")
        {
            return Icon(args);
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

    /// <summary>
    /// <c>tint aura [strength]</c> / <c>tint aura spread [fraction]</c> — the two dials on the glow behind
    /// tinted art.
    /// </summary>
    /// <remarks>
    /// Both repaint what is already on screen. "Slight, and soft, not obviously perceptible" is not a
    /// number anyone can pick from a text editor, so this exists for the same reason the tint's own
    /// strength went through three live rounds before it read right: turn it up until it is unmistakable,
    /// find the placement, then bring it back down.
    /// </remarks>
    private static CmdResult Aura(string[] args)
    {
        if (args.Length >= 2 && args[1].Trim().ToLowerInvariant() == "spread")
        {
            return Spread(args);
        }

        if (args.Length < 2)
        {
            return new CmdResult(success: true, AuraStatus());
        }

        if (!float.TryParse(args[1].Trim(), out var requested))
        {
            return new CmdResult(success: false, $"'{args[1]}' is not an aura strength between 0 and 1.");
        }

        PlayerTint.AuraStrength = PlayerTint.ClampAuraStrength(requested);
        var repainted = PlayerTint.Refresh();

        var clamped = Math.Abs(requested - PlayerTint.AuraStrength) > 0.001f
            ? $" (clamped from {requested})"
            : string.Empty;

        return new CmdResult(
            success: true,
            $"tint aura: {PlayerTint.AuraStrength:F2}{clamped} — {repainted} node(s) repainted.");
    }

    private static CmdResult Spread(string[] args)
    {
        if (args.Length < 3)
        {
            return new CmdResult(success: true, AuraStatus());
        }

        if (!float.TryParse(args[2].Trim(), out var requested))
        {
            return new CmdResult(success: false, $"'{args[2]}' is not an aura spread between 0 and 1.");
        }

        PlayerTint.AuraSpread = PlayerTint.ClampAuraSpread(requested);
        var resized = AuraLayer.RefreshSpread();

        var clamped = Math.Abs(requested - PlayerTint.AuraSpread) > 0.001f
            ? $" (clamped from {requested})"
            : string.Empty;

        return new CmdResult(
            success: true,
            $"tint aura spread: {PlayerTint.AuraSpread:F2}{clamped} — {resized} aura(s) resized.");
    }

    private static string AuraStatus() =>
        $"tint aura: strength {PlayerTint.AuraStrength:F2} "
        + $"(range {PlayerTint.MinAuraStrength}-{PlayerTint.MaxAuraStrength}, 0 hides it), "
        + $"spread {PlayerTint.AuraSpread:F2} "
        + $"(range {PlayerTint.MinAuraSpread}-{PlayerTint.MaxAuraSpread}, as a fraction of the figure).";

    /// <summary>
    /// <c>tint particles [strength]</c> / <c>tint particles count [n]</c> — the two dials on the motes.
    /// </summary>
    /// <remarks>
    /// Separate from <c>tint aura</c> because they are separate signals that happen to share a colour: the
    /// glow can be right while the motes are too busy, or the other way round, and tuning them together
    /// would make each impossible to judge.
    /// </remarks>
    private static CmdResult Particles(string[] args)
    {
        if (args.Length >= 2 && args[1].Trim().ToLowerInvariant() == "count")
        {
            return ParticleCount(args);
        }

        if (args.Length < 2)
        {
            return new CmdResult(success: true, ParticleStatus());
        }

        if (!float.TryParse(args[1].Trim(), out var requested))
        {
            return new CmdResult(success: false, $"'{args[1]}' is not a particle strength between 0 and 1.");
        }

        PlayerTint.ParticleStrength = PlayerTint.ClampParticleStrength(requested);
        var rebuilt = PlayerTint.Refresh();

        var clamped = Math.Abs(requested - PlayerTint.ParticleStrength) > 0.001f
            ? $" (clamped from {requested})"
            : string.Empty;

        return new CmdResult(
            success: true,
            $"tint particles: {PlayerTint.ParticleStrength:F2}{clamped} — {rebuilt} node(s) rebuilt.");
    }

    private static CmdResult ParticleCount(string[] args)
    {
        if (args.Length < 3)
        {
            return new CmdResult(success: true, ParticleStatus());
        }

        if (!int.TryParse(args[2].Trim(), out var requested))
        {
            return new CmdResult(success: false, $"'{args[2]}' is not a number of particles.");
        }

        PlayerTint.ParticleCount = PlayerTint.ClampParticleCount(requested);
        var rebuilt = PlayerTint.Refresh();

        var clamped = requested != PlayerTint.ParticleCount ? $" (clamped from {requested})" : string.Empty;

        return new CmdResult(
            success: true,
            $"tint particles count: {PlayerTint.ParticleCount}{clamped} — {rebuilt} node(s) rebuilt.");
    }

    private static string ParticleStatus() =>
        $"tint particles: strength {PlayerTint.ParticleStrength:F2} "
        + $"(range {PlayerTint.MinParticleStrength}-{PlayerTint.MaxParticleStrength}, 0 hides them), "
        + $"count {PlayerTint.ParticleCount} "
        + $"(range {PlayerTint.MinParticleCount}-{PlayerTint.MaxParticleCount}). "
        + "Brighter throws them outward, darker draws them in, warmer rises, cooler falls.";

    /// <summary>
    /// <c>tint icon [marker|character]</c> — swap the solo map marker for the co-op head icon.
    /// </summary>
    /// <remarks>
    /// The vote icons that carry the colour key in co-op never appear in single player, so this borrows
    /// their art for the marker instead. It is the closest you can get to judging the multiplayer look
    /// without a second player.
    /// </remarks>
    private static CmdResult Icon(string[] args)
    {
        if (args.Length < 2)
        {
            return new CmdResult(
                success: true,
                $"tint icon: {(PlayerTint.UseCharacterIconOnMap ? "character" : "marker")} "
                + "(marker = the normal solo pin, character = the co-op head icon).");
        }

        var requested = args[1].Trim().ToLowerInvariant();
        if (requested is not ("marker" or "character"))
        {
            return new CmdResult(success: false, $"'{args[1]}' is not marker or character.");
        }

        PlayerTint.UseCharacterIconOnMap = requested == "character";
        var applied = MapMarkerTintPatch.Apply();

        return new CmdResult(
            success: true,
            $"tint icon: {requested}."
            + (applied ? " Map marker updated." : " No map marker on screen — open the map to see it."));
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
            AuraStatus(),
            ParticleStatus(),
            $"diagnostic logging: {(Diagnostics.Enabled ? "on" : "off")}",
            $"sprites tracked: {PlayerTint.TrackedCount}",
        };

        var outlines = IconOutline.Describe();
        lines.Add(outlines.Count == 0
            ? "outlines live: none"
            : $"outlines live: {outlines.Count}");
        lines.AddRange(outlines.Select(l => "  " + l));

        var auras = AuraLayer.Describe();
        lines.Add(auras.Count == 0
            ? "auras live: none — enter a room with a tinted figure"
            : $"auras live: {auras.Count}");
        lines.AddRange(auras.Select(l => "  " + l));

        var probe = MapInkProbe.Describe();
        lines.Add("map ink:");
        lines.AddRange(probe.Select(l => "  " + l));

        var tail = Diagnostics.Tail();
        lines.Add(tail.Count == 0
            ? "no outline activity recorded yet — open the map, or enter a room with player icons"
            : "recent activity:");
        lines.AddRange(tail.Select(l => "  " + l));

        // Always written to the game log as well as returned. A diagnostic you have to transcribe out of
        // the console by hand is no use to anyone.
        Diagnostics.Write("---- tint diag ----");
        foreach (var line in lines)
        {
            Diagnostics.Write(line);
        }

        lines.Add("(this report was also written to the game log)");

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
