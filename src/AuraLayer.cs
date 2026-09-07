using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;

namespace MultiplayerColors;

/// <summary>
/// Builds the colour-key aura behind a tinted figure: a soft glow in a colour that names the variation
/// rather than the character, so the two stay tellable apart under transformations that destroy the tint.
/// </summary>
/// <remarks>
/// The layer is created whether or not a variation is currently active, and simply sits fully transparent
/// while it is not — the same timing argument <see cref="IconOutline" /> documents. A combat room is built
/// long before anyone types <c>tint darker</c>, so an aura created only when a variation already existed
/// would never be created at all, and <see cref="PlayerTint.Refresh" /> would have nothing to repaint.
///
/// Everything here is assignment; every decision lives in <see cref="PlayerTint" />,
/// <see cref="AuraBounds" /> or the pure helpers below, which is what makes the untestable half of this
/// feature contain no logic worth testing.
/// </remarks>
public static class AuraLayer
{
    private const string NodeName = "MultiplayerColorsAura";

    private const string ParticlesNodeName = "MultiplayerColorsAuraParticles";

    /// <summary>
    /// The aura is drawn behind its art, never in front.
    /// </summary>
    /// <remarks>
    /// Stated as a constant because the whole illusion rests on it. There is no silhouette in the shader —
    /// the figure itself does the shaping by covering the middle of the glow, leaving only the fringe
    /// around its own outline visible. Drawn in front it would wash the figure out instead.
    /// </remarks>
    public const bool DrawnBehindArt = true;

    /// <summary>Auras created so far, keyed by the art they belong to. Weak keys — a freed node drops out.</summary>
    private static readonly ConditionalWeakTable<CanvasItem, AuraHost> Attached = new();

    private sealed class AuraHost(CanvasItem art, ColorRect node, CpuParticles2D motes, Player player)
    {
        public CanvasItem Art { get; } = art;
        public ColorRect Node { get; } = node;

        /// <summary>The motes drifting around the figure, whose motion carries the same key as the glow.</summary>
        public CpuParticles2D Motes { get; } = motes;

        public Player Player { get; set; } = player;

        /// <summary>The figure box, once something managed to measure it.</summary>
        public Rect2 Bounds { get; set; }

        public AuraBoundsSource Source { get; set; } = AuraBoundsSource.None;
    }

    /// <summary>
    /// Attaches (or updates) the aura behind <paramref name="art" />. Idempotent — safe to call again on
    /// art that already has one.
    /// </summary>
    /// <param name="hint">
    /// A scene-authored box in the same space as <paramref name="art" />, used only when the art cannot
    /// measure itself: <c>%Bounds</c> in combat, <c>%Hitbox</c> at a rest site. Null where none exists.
    /// </param>
    public static void Attach(CanvasItem? art, Player? player, Control? hint = null)
    {
        if (art == null || player == null)
        {
            return;
        }

        if (Attached.TryGetValue(art, out var existing))
        {
            existing.Player = player;
            ApplyFrame(existing);
            PlayerTint.ApplyAura(existing.Node, player);
            return;
        }

        var aura = new ColorRect
        {
            Name = NodeName,

            // White, so the modulate chain reaching the shader carries nothing but the ancestor fade.
            Color = Colors.White,
            ShowBehindParent = DrawnBehindArt,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Material = AuraShader.CreateMaterial(),
        };

        var motes = new CpuParticles2D
        {
            Name = ParticlesNodeName,
            ShowBehindParent = DrawnBehindArt,
            Texture = MoteTexture(),

            // Global coordinates: a mote is placed and launched from wherever the art is at the moment it
            // spawns, and then lives its own life. Moving the art moves where the NEXT motes appear, not
            // the ones already drifting — so the treasure-room arm, which follows its player's cursor, lays
            // a trail rather than dragging a rigid cloud around with it.
            //
            // This was local until v0.1.37, and had to be: Godot transforms a particle's spawn position and
            // velocity by the emitter's transform but not its ACCELERATIONS, and the motion model had
            // accelerations in it. Now that a mote is only ever placed and launched, both of the quantities
            // that survive are ones Godot transforms, and the one it does not — ScaleAmount — is corrected
            // by hand in AuraParticles.ScaleFor.
            LocalCoords = false,

            // Off until the figure has been measured; Configure switches it on with everything else set.
            Emitting = false,
            Visible = false,
        };

        var host = new AuraHost(art, aura, motes, player);
        Attached.Add(art, host);

        art.AddChildSafely(aura);
        art.MoveChildSafely(aura, 0);
        art.AddChildSafely(motes);

        PlayerTint.ApplyAura(aura, player);
        PlayerTint.ApplyParticles(motes, player);
        Measure(host, hint);
    }

    /// <summary>
    /// Measures the figure box and sizes the aura to it, waiting for the skeleton first where there is one.
    /// </summary>
    /// <remarks>
    /// A SpineSprite builds its skeleton asynchronously and Godot runs <c>_Ready</c> children-first, so
    /// measuring straight out of a <c>_Ready</c> postfix reliably reads an empty box. The game hit the same
    /// problem with VFX and answered it with <c>RunWhenSpineReady</c>, which is reused here rather than
    /// reinvented; it also gives up quietly if the skeleton never loads.
    /// </remarks>
    private static void Measure(AuraHost host, Control? hint)
    {
        if (host.Art.GetClass() == MegaSprite.spineClassName)
        {
            host.Art.RunWhenSpineReady(new MegaSprite(host.Art), _ => Complete(host, hint));
            return;
        }

        Complete(host, hint);
    }

    private static void Complete(AuraHost host, Control? hint)
    {
        if (!GodotObject.IsInstanceValid(host.Art) || !GodotObject.IsInstanceValid(host.Node))
        {
            return;
        }

        host.Bounds = AuraBounds.Measure(host.Art, hint, out var source);
        host.Source = source;
        ApplyFrame(host);

        Diagnostics.Log(
            $"aura attached to {host.Art.Name} for {host.Player.Character?.Id} "
            + $"(bounds={host.Bounds.Size.X:F0}x{host.Bounds.Size.Y:F0} source={source}, "
            + $"variation={PlayerTint.For(host.Player)?.ToString() ?? "none"})");
    }

    /// <summary>Sizes the aura to the current spread, or collapses it when nothing could be measured.</summary>
    private static void ApplyFrame(AuraHost host)
    {
        if (!GodotObject.IsInstanceValid(host.Node))
        {
            return;
        }

        var frame = IsMeasurable(host.Bounds)
            ? Frame(host.Bounds, PlayerTint.ClampAuraSpread(PlayerTint.AuraSpread))
            : new Rect2();

        host.Node.Position = frame.Position;
        host.Node.Size = frame.Size;

        if (GodotObject.IsInstanceValid(host.Motes))
        {
            AuraParticles.SetFrame(host.Motes, frame, PlayerTint.For(host.Player));
        }
    }

    /// <summary>
    /// The rect to draw the glow in: the figure's box grown by <paramref name="spread" /> of its *smaller*
    /// side, on every edge.
    /// </summary>
    /// <remarks>
    /// A fraction of the smaller side rather than of each axis, so the halo is the same width all the way
    /// round instead of stretching with the art's aspect — a 383x1072 treasure-room arm would otherwise get
    /// a glow three times deeper above it than beside it.
    /// </remarks>
    public static Rect2 Frame(Rect2 bounds, float spread) =>
        bounds.Grow(spread * MathF.Min(bounds.Size.X, bounds.Size.Y));

    /// <summary>Whether a measured box describes a real figure, rather than one that is not there yet.</summary>
    /// <remarks>
    /// An unposed skeleton reports an empty rect, and a frame built around one puts a coloured dot at the
    /// character's origin — visibly wrong, and worse than showing nothing. The upper bound catches a
    /// measurement in the wrong coordinate space, which would tint a whole screen.
    /// </remarks>
    public static bool IsMeasurable(Rect2 bounds) =>
        float.IsFinite(bounds.Size.X) && float.IsFinite(bounds.Size.Y)
        && bounds.Size.X is > 1f and < 100_000f
        && bounds.Size.Y is > 1f and < 100_000f;

    /// <summary>
    /// Re-sizes every aura already on screen to the current spread, so <c>tint aura spread</c> takes effect
    /// without changing rooms.
    /// </summary>
    public static int RefreshSpread()
    {
        var updated = 0;
        foreach (var (art, host) in Attached)
        {
            if (!GodotObject.IsInstanceValid(art) || !GodotObject.IsInstanceValid(host.Node))
            {
                continue;
            }

            ApplyFrame(host);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// One line per live aura. Every failure mode this design has shows up as a distinct value here:
    /// <c>source=None</c> means nothing could measure the figure, <c>bounds=0x0</c> means the skeleton was
    /// never posed, <c>shader=NO</c> means the material did not take, <c>MISMATCH</c> means the colour
    /// written is not the colour that arrived.
    ///
    /// The box's POSITION is reported alongside its size, not just its extent, because the one thing that
    /// cannot be checked from outside the game is which way up a measured box is. A creature stands with
    /// its root at its feet and its art above, so in Godot coordinates a combat figure's box must sit at a
    /// NEGATIVE y. A positive one would mean the Spine runtime hands back its own y-up convention, and the
    /// aura is being drawn a whole character-height below the character.
    /// </summary>
    public static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();

        foreach (var (art, host) in Attached)
        {
            if (!GodotObject.IsInstanceValid(art) || !GodotObject.IsInstanceValid(host.Node))
            {
                continue;
            }

            var expected = PlayerTint.ExpectedColorFor(host.Node) ?? PlayerTint.DormantAura;
            var actual = AuraShader.ReadColor(host.Node);
            var matches =
                Mathf.IsEqualApprox(expected.R, actual.R)
                && Mathf.IsEqualApprox(expected.G, actual.G)
                && Mathf.IsEqualApprox(expected.B, actual.B)
                && Mathf.IsEqualApprox(expected.A, actual.A);

            lines.Add(
                $"{art.Name}[{host.Player.Character?.Id}]: "
                + $"variation={PlayerTint.For(host.Player)?.ToString() ?? "none"} "
                + $"bounds={host.Bounds.Size.X:F0}x{host.Bounds.Size.Y:F0}"
                + $"@{host.Bounds.Position.X:F0},{host.Bounds.Position.Y:F0} "
                + $"source={host.Source} spine={Describe(AuraBounds.SpineBounds(art))} "
            + $"artscale={(GodotObject.IsInstanceValid(host.Art) && host.Art.IsInsideTree() ? AuraParticles.GlobalScale(host.Art.GetGlobalTransform()) : 1f):F2} "
                + $"frame={host.Node.Size.X:F0}x{host.Node.Size.Y:F0}"
                + $"@{host.Node.Position.X:F0},{host.Node.Position.Y:F0} "
                + $"want=#{expected.ToHtml()} got=#{actual.ToHtml()} "
                + $"{(matches ? "MATCH" : "MISMATCH")} "
                + $"ancestors={(MapInkProbe.Accumulated(host.Node).IsEqualApprox(Colors.White) ? "none" : "#" + MapInkProbe.Accumulated(host.Node).ToHtml())} "
                + $"shader={(AuraShader.HasShader(host.Node) ? "yes" : "NO")} "
                + $"visible={host.Node.Visible && host.Node.IsVisibleInTree()} "
                + $"motes={DescribeMotes(host)}");
        }

        return lines;
    }

    /// <summary>
    /// The skeleton's own box, reported alongside whichever box was actually used.
    /// </summary>
    /// <remarks>
    /// Combat is the one surface with two independent measurements of the same figure — the authored
    /// %Bounds and the posed skeleton — so printing both is what makes it possible to tell, from outside,
    /// whether the Spine runtime hands its box back in Godot's y-down convention or its own y-up one. That
    /// question cannot be settled headlessly: MegaDot ships no Spine extension.
    /// </remarks>
    private static string Describe(Rect2? bounds) => bounds == null
        ? "n/a"
        : $"{bounds.Value.Size.X:F0}x{bounds.Value.Size.Y:F0}"
            + $"@{bounds.Value.Position.X:F0},{bounds.Value.Position.Y:F0}";

    /// <summary>The particle half of an aura's diag line: are they on, how many, and moving which way.</summary>
    private static string DescribeMotes(AuraHost host)
    {
        if (!GodotObject.IsInstanceValid(host.Motes))
        {
            return "gone";
        }

        if (!host.Motes.Emitting)
        {
            return "off";
        }

        return $"{host.Motes.Amount}@#{host.Motes.Color.ToHtml()} "
            + $"dir={host.Motes.Direction.X:F0},{host.Motes.Direction.Y:F0} "
            + $"v={host.Motes.InitialVelocityMax:F1} life={host.Motes.Lifetime:F1}s "
            + $"cloud={host.Motes.EmissionPoints.Length}pts "
            + $"scale={host.Motes.ScaleAmountMax:F2} "
            + $"drawn={host.Motes.ScaleAmountMax * AuraParticles.TexturePixels:F0}u "
            + $"visible={host.Motes.Visible && host.Motes.IsVisibleInTree()}";
    }

    /// <summary>
    /// A soft round dot for the motes, generated rather than shipped — this mod has no <c>.pck</c> and no
    /// assets of its own.
    /// </summary>
    /// <remarks>
    /// Without a texture Godot draws each particle as a hard-edged square, which at this size reads as
    /// grit rather than as a mote. One texture is shared by every emitter.
    /// </remarks>
    private static GradientTexture2D? _moteTexture;

    private static GradientTexture2D MoteTexture() => _moteTexture ??= new GradientTexture2D
    {
        Width = (int)AuraParticles.TexturePixels,
        Height = (int)AuraParticles.TexturePixels,
        Fill = GradientTexture2D.FillEnum.Radial,
        FillFrom = new Vector2(0.5f, 0.5f),
        FillTo = new Vector2(0.5f, 1f),
        Gradient = new Gradient
        {
            Offsets = [0f, 1f],
            Colors = [Colors.White, new Color(1f, 1f, 1f, 0f)],
        },
    };
}
