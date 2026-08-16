namespace MultiplayerColors;

/// <summary>
/// Diagnostic logging for the parts of this mod that cannot be unit tested — everything that touches a
/// Godot node. Off by default; switched on with <c>tint diag on</c>.
/// </summary>
/// <remarks>
/// Deliberately routed through here rather than calling the logger directly, so that no type the test suite
/// touches ever references <c>MainFile</c>. Instantiating the game's logger drags in Godot statics that take
/// the bare xUnit host down with them.
/// </remarks>
public static class Diagnostics
{
    /// <summary>Whether to write diagnostic lines to the game log.</summary>
    public static bool Enabled { get; set; }

    /// <summary>The most recent lines, newest last, for reporting back through the console command.</summary>
    private static readonly Queue<string> Recent = new();

    private const int MaxRecent = 20;

    public static void Log(string message)
    {
        lock (Recent)
        {
            Recent.Enqueue(message);
            while (Recent.Count > MaxRecent)
            {
                Recent.Dequeue();
            }
        }

        if (Enabled)
        {
            MainFile.Logger.Info(message);
        }
    }

    /// <summary>
    /// Writes everything recorded so far to the game log, for when logging is switched on after the
    /// interesting thing already happened. Returns how many lines were written.
    /// </summary>
    public static int FlushToLog()
    {
        var lines = Tail();
        foreach (var line in lines)
        {
            MainFile.Logger.Info("(earlier) " + line);
        }

        return lines.Count;
    }

    /// <summary>The recent lines, oldest first. Empty when nothing has happened yet.</summary>
    public static IReadOnlyList<string> Tail()
    {
        lock (Recent)
        {
            return Recent.ToList();
        }
    }
}
