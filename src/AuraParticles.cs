using System.Runtime.CompilerServices;
using Godot;

namespace MultiplayerColors;

/// <summary>
/// How one variation's particles move once they exist: a steady drift in one direction, and nothing else.
/// </summary>
/// <remarks>
/// One kind of motion, four directions. This replaced a menagerie — radial pushes, an orbit, damping, a
/// solved arrival time — and every one of those had its own way of going wrong, all of which reduced to
/// the same root: motion defined relative to a centre has to reckon with what happens at the centre. A
/// drift has no centre and so has nothing to reckon with.
///
/// <paramref name="Speed" /> is a fraction of the figure's radius per second rather than a pixel count, so
/// the same description works on a combat body and on a Sovereign Blade — and survives being handed to a
/// node living in a SpineSprite's local units, which are several times larger than a screen pixel.
/// </remarks>
/// <param name="Direction">Which way the motes travel. A unit vector; Godot's Y axis points down.</param>
/// <param name="Speed">How fast, in radii per second.</param>
/// <param name="Lifetime">How long a mote lives, in seconds.</param>
/// <param name="Opacity">The variation's own default opacity. Tuned per variation, not shared.</param>
public readonly record struct ParticleMotion(
    Vector2 Direction,
    float Speed,
    float Lifetime,
    float Opacity);

/// <summary>
/// A drift of motes alongside the aura, whose direction of travel names the variation.
/// </summary>
/// <remarks>
/// Direction is the point. A black aura on a dark battlefield is the weakest of the four colours, but
/// "these are moving left" reads regardless of what the background is doing — and the two variations you
/// most need to tell apart are always the two travelling opposite ways.
///
/// Built on <c>CpuParticles2D</c> rather than <c>GpuParticles2D</c>: every knob this needs is a plain
/// property on the CPU node, and it matters that this mod compiles without the Godot SDK's source
/// generators, so it can only ever configure built-in node types — never subclass one.
/// </remarks>
public static class AuraParticles
{
    /// <summary>The mote texture's width in pixels. Shared, so the scale maths and the texture agree.</summary>
    public const float TexturePixels = 32f;

    /// <summary>
    /// The spread of the spawn cloud, as a fraction of each semi-axis. One standard deviation, so roughly
    /// two thirds of the motes are born inside it and the rest fill the aura out to its edge.
    /// </summary>
    public const float SpawnSigma = 0.55f;

    /// <summary>
    /// How many distinct spawn positions the cloud offers. Independent of the particle count — the emitter
    /// picks from these at random, so fifty motes do not need fifty points to look unrepeated.
    /// </summary>
    public const int SpawnCloudPoints = 256;

    /// <summary>The seed for the cloud. Fixed, so every client in a lobby generates the same one.</summary>
    private const ulong SpawnSeed = 0x9E3779B97F4A7C15UL;

    public const float Lifetime = 2.4f;

    /// <summary>
    /// The opacity the loudest variation is drawn at. The other three take a fraction of it.
    /// </summary>
    /// <remarks>
    /// Written as a base times a per-variation weight rather than as four independent numbers, because the
    /// two answer different questions: this one is "how present should the motes be at all", and the
    /// weights are "how do the four balance against each other". The weights hold because black over a lit
    /// battlefield needs four times what white does to read at all; the base is the dial to move when the
    /// whole effect is too much or too little, and moving it keeps that balance intact.
    /// </remarks>
    public const float MoteOpacity = 0.15f;

    /// <summary>
    /// The direction that names a variation. Every variation shares one spawn cloud, one speed and one
    /// lifetime; only the heading and the opacity weight differ.
    /// </summary>
    /// <remarks>
    /// Four headings on two axes, each the exact reverse of its opposite — brighter against darker, warmer
    /// against cooler. That pairing is deliberate: the two you most need to tell apart are the two moving
    /// in opposite directions, which is the largest difference two drifts can have.
    /// </remarks>
    public static ParticleMotion MotionFor(PlayerVariation variation) => variation switch
    {
        PlayerVariation.Brighter => new(Vector2.Right, Drift, Lifetime, MoteOpacity * 0.25f),
        PlayerVariation.Darker => new(Vector2.Left, Drift, Lifetime, MoteOpacity * 1.00f),

        // Godot's Y axis points down, so bottom-to-top is negative.
        PlayerVariation.Warmer => new(Vector2.Up, Drift, Lifetime, MoteOpacity * 0.50f),
        PlayerVariation.Cooler => new(Vector2.Down, Drift, Lifetime, MoteOpacity * 0.50f),

        _ => new(Vector2.Zero, 0f, Lifetime, 0f),
    };

    /// <summary>
    /// How fast the motes drift, in radii per second: far enough to cross the cloud's own spread in one
    /// lifetime.
    /// </summary>
    /// <remarks>
    /// Solved against the spawn cloud rather than picked, so the two stay in proportion if either is
    /// retuned. Much slower and the drift reads as stillness; much faster and a mote is gone before it has
    /// finished fading in.
    /// </remarks>
    public static float Drift => SpawnSigma / Lifetime;

    /// <summary>
    /// The heading to hand the emitter so its motes travel <paramref name="screenDirection" /> ON SCREEN,
    /// whatever the art they hang off is doing.
    /// </summary>
    /// <remarks>
    /// Godot transforms a particle's velocity by the emitter's basis at spawn, so a heading handed over
    /// raw is a heading in the art's own space — and three of the five surfaces this mod tints are not
    /// upright in it. <c>NHandImage._Ready</c> rotates each player's arm by 0, ±90° or 180° so it reaches
    /// in from a different side; <c>NRestSiteCharacter.FlipX</c> negates a spine node's <c>Scale.X</c>;
    /// <c>NSovereignBladeVfx</c> tweens its sword's rotation as it attacks.
    ///
    /// Uncorrected, left-to-right would mean a different direction for each player, which is worse than no
    /// key at all. Pre-multiplying by the inverse basis cancels the art's rotation, mirroring and scale, so
    /// what Godot draws is the heading that was asked for.
    /// </remarks>
    public static Vector2 LocalHeading(Vector2 screenDirection, Transform2D artToScreen)
    {
        // A collapsed basis has no inverse. Better the uncorrected heading than a NaN, which Godot turns
        // into particles that never move or never appear.
        var determinant = (artToScreen.X.X * artToScreen.Y.Y) - (artToScreen.X.Y * artToScreen.Y.X);
        if (MathF.Abs(determinant) < 1e-6f)
        {
            return screenDirection;
        }

        var basis = new Transform2D(artToScreen.X, artToScreen.Y, Vector2.Zero);
        var local = basis.AffineInverse().BasisXform(screenDirection);

        return local.LengthSquared() > 0f ? local.Normalized() : screenDirection;
    }

    /// <summary>Turns a motion's radius fractions into the units the emitter actually works in.</summary>
    public static ParticleMotion Scale(ParticleMotion motion, float radius) =>
        motion with { Speed = motion.Speed * radius };

    /// <summary>Where a variation's motes are born, before they start drifting. The same for all four.</summary>
    /// <remarks>
    /// An elliptical gaussian, clipped to the ellipse: dense on the figure, thinning outward, and never
    /// outside the aura it belongs to. It matches the aura's own shape by construction — the shader draws
    /// its falloff in normalised UV, which is exactly the ellipse inscribed in this frame — so the motes
    /// occupy the region that is glowing and no more.
    ///
    /// Godot's built-in shapes can do neither half: <c>Sphere</c> is uniform through a circle and
    /// <c>SphereSurface</c> is a ring, and both are round whatever the frame's aspect. So the points are
    /// generated here and handed over as <c>EmissionShapeEnum.Points</c>.
    ///
    /// Deterministic from a fixed seed, and deliberately not drawn from <c>RunState.Rng</c>: every client
    /// must generate the same cloud, and a purely cosmetic effect has no business advancing a run's stream.
    /// </remarks>
    /// <param name="halfExtents">Half the frame's width and height — the ellipse's two semi-axes.</param>
    public static Vector2[] SpawnCloud(int count, Vector2 halfExtents, float sigma, ulong seed)
    {
        var points = new Vector2[Math.Max(count, 0)];
        var state = seed;

        for (var i = 0; i < points.Length; i++)
        {
            points[i] = Draw(ref state, halfExtents, sigma);
        }

        return points;
    }

    /// <summary>One point inside the ellipse, by rejection: draw, and draw again if it landed outside.</summary>
    /// <remarks>
    /// A gaussian has infinite tails, so some draws always land outside — about a fifth at the sigma this
    /// uses. Rejection keeps the distribution's shape, where clamping would pile the whole tail onto the
    /// rim and read as a hard bright edge around an aura whose entire point is not to have one.
    ///
    /// The attempt cap is a termination guarantee, not a tuning knob: given a degenerate frame every draw
    /// could be rejected forever. The centre is the one answer that is always inside.
    /// </remarks>
    private static Vector2 Draw(ref ulong state, Vector2 halfExtents, float sigma)
    {
        for (var attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var point = new Vector2(
                Gaussian(ref state) * sigma * halfExtents.X,
                Gaussian(ref state) * sigma * halfExtents.Y);

            if (IsInside(point, halfExtents))
            {
                return point;
            }
        }

        return Vector2.Zero;
    }

    private const int MaxSpawnAttempts = 32;

    /// <summary>Whether a point lies within the ellipse with these semi-axes.</summary>
    public static bool IsInside(Vector2 point, Vector2 halfExtents)
    {
        if (halfExtents.X <= 0f || halfExtents.Y <= 0f)
        {
            return false;
        }

        var x = point.X / halfExtents.X;
        var y = point.Y / halfExtents.Y;

        return x * x + y * y <= 1f;
    }

    /// <summary>One draw from a standard normal, by the Box-Muller transform.</summary>
    private static float Gaussian(ref ulong state)
    {
        // Never exactly zero, or the logarithm below is undefined.
        var u1 = MathF.Max(NextFloat(ref state), 1e-7f);
        var u2 = NextFloat(ref state);

        return MathF.Sqrt(-2f * MathF.Log(u1)) * MathF.Cos(2f * MathF.PI * u2);
    }

    /// <summary>A deterministic uniform in [0,1). SplitMix64 — small, seedable and identical everywhere.</summary>
    private static float NextFloat(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;

        return (z >> 40) / (float)(1 << 24);
    }

    /// <summary>
    /// The particle colour: the aura's, at the variation's own opacity times the console's multiplier.
    /// </summary>
    /// <remarks>
    /// The scale is the console's dial, so a variation can be pushed up to look at it without editing the
    /// tuned value it is pushing.
    /// </remarks>
    public static Color ColorFor(PlayerVariation variation, float scale)
    {
        var colour = PlayerTint.AuraColor(variation);
        var alpha = Math.Clamp(MotionFor(variation).Opacity * scale, 0f, 1f);

        return new Color(colour.R, colour.G, colour.B, alpha);
    }

    /// <summary>
    /// The multiplier to hand <c>ScaleAmount</c> so a mote is drawn at <see cref="PlayerTint.ParticleSize" />
    /// of the figure's radius.
    /// </summary>
    /// <remarks>
    /// <c>CpuParticles2D.ScaleAmount</c> is a multiplier on the texture, NOT a size in units — which is
    /// what shipped in v0.1.30 and is why the motes came out enormous. A figure's radius is measured in the
    /// art node's own local units, and a SpineSprite is scaled around 0.28, so passing a radius-derived
    /// size straight in blew a 32px texture up by a factor of ten or more.
    ///
    /// Dividing by the texture's own resolution also decouples the two: the mote art can be made sharper
    /// without silently resizing every particle in the mod.
    ///
    /// <paramref name="globalScale" /> is there for a second asymmetry in the same property. With
    /// <c>local_coords</c> off, Godot transforms a particle's spawn position and velocity by the emitter's
    /// transform but leaves <c>ScaleAmount</c> in canvas units. A SpineSprite is scaled around 0.28, so a
    /// mote sized against a radius measured in its local units comes out several times too large unless the
    /// same factor is applied here by hand.
    /// </remarks>
    public static float ScaleFor(float radius, float texturePixels, float globalScale = 1f) =>
        texturePixels <= 0f
            ? 0f
            : PlayerTint.ClampParticleSize(PlayerTint.ParticleSize) * radius * globalScale / texturePixels;

    /// <summary>How much the art scales its own local units by, for quantities Godot will not transform.</summary>
    public static float GlobalScale(Transform2D artToScreen) =>
        0.5f * (artToScreen.X.Length() + artToScreen.Y.Length());

    /// <summary>Half the frame's smaller side — the distance speed and size are expressed against.</summary>
    public static float Radius(Rect2 frame) => 0.5f * MathF.Min(frame.Size.X, frame.Size.Y);

    /// <summary>The frame each emitter was last sized against, so <see cref="Repaint" /> can rebuild it.</summary>
    private static readonly ConditionalWeakTable<CpuParticles2D, StrongBox<Rect2>> Frames = new();

    /// <summary>Records the frame an emitter belongs to, and rebuilds it against that frame.</summary>
    public static void SetFrame(CpuParticles2D node, Rect2 frame, PlayerVariation? variation)
    {
        Frames.AddOrUpdate(node, new StrongBox<Rect2>(frame));
        Configure(node, variation, frame);
    }

    /// <summary>
    /// Rebuilds an emitter for whatever variation its player has now, against the frame it was last sized
    /// to. Driven from <see cref="PlayerTint.Refresh" />, so a <c>tint</c> command moves the motes along
    /// with everything else and <c>tint auto</c> stops them.
    /// </summary>
    public static void Repaint(CanvasItem node, PlayerVariation? variation)
    {
        if (node is CpuParticles2D emitter && Frames.TryGetValue(emitter, out var frame))
        {
            Configure(emitter, variation, frame.Value);
        }
    }

    /// <summary>Hands one emitter the numbers for a variation, or switches it off when there is none.</summary>
    public static void Configure(CpuParticles2D node, PlayerVariation? variation, Rect2 frame)
    {
        var count = PlayerTint.ClampParticleCount(PlayerTint.ParticleCount);
        var scale = PlayerTint.ClampParticleStrength(PlayerTint.ParticleStrength);

        if (variation == null || count == 0 || scale <= 0f || !AuraLayer.IsMeasurable(frame))
        {
            node.Emitting = false;
            node.Visible = false;
            return;
        }

        var radius = Radius(frame);
        var motion = Scale(MotionFor(variation.Value), radius);

        node.Position = frame.GetCenter();
        node.Amount = count;
        node.Lifetime = motion.Lifetime;

        // Started mid-life rather than empty, so walking into a room does not begin with a visible puff.
        node.Preprocess = motion.Lifetime;

        // Every variation spawns identically, inside the aura's own ellipse.
        node.EmissionShape = CpuParticles2D.EmissionShapeEnum.Points;
        node.EmissionPoints = SpawnCloud(SpawnCloudPoints, frame.Size / 2f, SpawnSigma, SpawnSeed);

        // One heading, held. No acceleration of any kind: a drift that speeds up or curves is a second
        // reading laid over the first.
        //
        // Corrected into the art's own space so that what lands on screen is the heading asked for, whether
        // or not the art is rotated or mirrored — which for the treasure-room arms and the rest-site
        // figures it routinely is.
        var artToScreen = node.IsInsideTree() ? node.GetGlobalTransform() : Transform2D.Identity;
        node.Direction = LocalHeading(motion.Direction, artToScreen);
        node.Spread = 0f;
        node.InitialVelocityMin = motion.Speed * 0.8f;
        node.InitialVelocityMax = motion.Speed * 1.2f;
        node.Gravity = Vector2.Zero;
        node.RadialAccelMin = 0f;
        node.RadialAccelMax = 0f;
        node.OrbitVelocityMin = 0f;
        node.OrbitVelocityMax = 0f;
        node.DampingMin = 0f;
        node.DampingMax = 0f;

        var drawn = ScaleFor(radius, TexturePixels, GlobalScale(artToScreen));
        node.ScaleAmountMin = drawn * 0.6f;
        node.ScaleAmountMax = drawn * 1.4f;

        node.Color = ColorFor(variation.Value, scale);
        node.ColorRamp = Ramp();

        node.Visible = true;
        node.Emitting = true;
    }

    /// <summary>
    /// The alpha curve over a mote's life, multiplied onto <c>Color</c>. In quickly, then a long fade to
    /// nothing.
    /// </summary>
    /// <remarks>
    /// One ramp for all four. Nothing converges on anything, so none of them needs a curve timed to an
    /// arrival — they appear, drift, and go.
    /// </remarks>
    private static Gradient Ramp() => new()
    {
        Offsets = [0f, 0.12f, 1f],
        Colors =
        [
            new Color(1f, 1f, 1f, 0f),
            Colors.White,
            new Color(1f, 1f, 1f, 0f),
        ],
    };
}
