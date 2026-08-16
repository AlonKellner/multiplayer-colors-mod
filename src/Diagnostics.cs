namespace MultiplayerColors;

/// <summary>
/// Diagnostic recording for the parts of this mod that cannot be unit tested — everything that touches a
/// Godot node.
/// </summary>
/// <remarks>
/// Writes go through <see cref="Sink" />, which <c>MainFile</c> points at the game logger on startup. The
/// indirection is not decoration: instantiating that logger drags in Godot statics that take the bare xUnit
/// host down with it, and the console command these back is exercised by tests. With no sink installed —
/// which is exactly the test case — recording still happens and nothing is written anywhere.
/// </remarks>
public static class Diagnostics
{
    /// <summary>Where diagnostic lines go. Left null outside the game.</summary>
    public static Action<string>? Sink { get; set; }

    /// <summary>Whether routine activity is written as it happens. Reports are written either way.</summary>
    public static bool Enabled { get; set; }

    /// <summary>
    /// True once the mod has started inside the game. Diagnostics that touch Godot statics must check this.
    /// </summary>
    /// <remarks>
    /// Not defensive clutter: the test suite exercises the console command, and reaching a Godot static from
    /// there does not throw — it takes the whole test host process down. That is precisely how this flag
    /// came to exist.
    /// </remarks>
    public static bool InGame { get; set; }

    /// <summary>The most recent lines, newest last, for reporting back through the console command.</summary>
    private static readonly Queue<string> Recent = new();

    private const int MaxRecent = 20;

    /// <summary>Records activity, and writes it out only when logging has been switched on.</summary>
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
            Write(message);
        }
    }

    /// <summary>Writes a line out regardless of whether logging is switched on.</summary>
    public static void Write(string message) => Sink?.Invoke(message);

    /// <summary>
    /// Writes everything recorded so far, for when logging is switched on after the interesting thing
    /// already happened. Returns how many lines were written.
    /// </summary>
    public static int FlushToLog()
    {
        var lines = Tail();
        foreach (var line in lines)
        {
            Write("(earlier) " + line);
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
