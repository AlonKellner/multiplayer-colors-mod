using System.Runtime.CompilerServices;
using Godot;

namespace MultiplayerColors;

/// <summary>
/// How one variation's particles move once they exist. Every distance is a fraction of the figure's radius
/// rather than a number of pixels, so the same description works on a combat body and on a Sovereign Blade
/// — and, more to the point, survives being handed to a node living in a SpineSprite's local units, which
/// are several times larger than a screen pixel.
/// </summary>
/// <remarks>
/// There is no emission shape here, and that is the point. Every variation spawns from the same cloud
/// (<see cref="AuraParticles.SpawnCloud" />) and differs only in which way it is then pulled. Emission used
/// to vary too — the inward variation was born on the rim rather than in the cloud — and that is what
/// produced the swing-back: a particle launched at the rim reaches the middle with all the speed the pull
/// gave it, sails through, and comes back out the far side.
/// </remarks>
/// <param name="RadialAccel">Acceleration away from the centre, in radii per second squared. Negative pulls inward.</param>
/// <param name="LinearAccel">Constant acceleration in one direction, in radii per second squared. Godot's Y points down.</param>
/// <param name="Damping">Drag, in radii per second per second. What stops an arriving particle rather than letting it swing.</param>
/// <param name="Lifetime">How long a mote lives, in seconds.</param>
/// <param name="Opacity">The variation's own default opacity. Tuned per variation, not shared.</param>
public readonly record struct ParticleMotion(
    float RadialAccel,
    Vector2 LinearAccel,
    float Damping,
    float Lifetime,
    float Opacity);

/// <summary>
/// A drift of motes alongside the aura, whose <em>motion</em> names the variation.
/// </summary>
/// <remarks>
/// Colour alone is uneven: a black aura on a dark battlefield is much weaker than a white one, and red and
/// blue at low strength are the pair most easily confused. Motion is not — "pushed outward", "pulled
/// inward", "rising", "falling" are four readings no background can flatten into one another. The two
/// signals carry the same key by different means.
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
    /// The spread of the spawn cloud, as a fraction of the figure's radius. One standard deviation, so
    /// roughly two thirds of the motes are born inside this and the tail reaches well past the figure.
    /// </summary>
    public const float SpawnSigma = 0.55f;

    /// <summary>
    /// How many distinct spawn positions the cloud offers. Independent of the particle count — the emitter
    /// picks from these at random, so a hundred motes do not need a hundred points to look unrepeated.
    /// </summary>
    public const int SpawnCloudPoints = 256;

    /// <summary>The seed for the cloud. Fixed, so every client in a lobby generates the same one.</summary>
    private const ulong SpawnSeed = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// The pull that names a variation. Everything below shares one spawn cloud and one lifetime shape;
    /// only the direction of the pull differs.
    /// </summary>
    public static ParticleMotion MotionFor(PlayerVariation variation) => variation switch
    {
        // Pushed out of the cloud in every direction at once, so they stream past the figure's own outline.
        PlayerVariation.Brighter => new(
            RadialAccel: InwardPull,
            LinearAccel: Vector2.Zero,
            Damping: 0f,
            Lifetime: Lifetime,
            Opacity: 0.10f),

        // Drawn in, and stopped when they get there. The pull is solved (see InwardPull) so that a mote
        // born one sigma out reaches the middle exactly as its life ends and it finishes fading; the
        // damping is what keeps the ones born closer, which arrive early, from swinging back out again.
        PlayerVariation.Darker => new(
            RadialAccel: -InwardPull,
            LinearAccel: Vector2.Zero,
            Damping: 0.45f,
            Lifetime: Lifetime,
            Opacity: 0.40f),

        // Sparks off a fire.
        PlayerVariation.Warmer => new(
            RadialAccel: 0f,
            LinearAccel: new Vector2(0f, -0.22f),
            Damping: 0f,
            Lifetime: Lifetime,
            Opacity: 0.20f),

        // Snow. Slower than the sparks on purpose — it is the second thing separating the two linear
        // motions, after their colour.
        PlayerVariation.Cooler => new(
            RadialAccel: 0f,
            LinearAccel: new Vector2(0f, 0.10f),
            Damping: 0f,
            Lifetime: Lifetime,
            Opacity: 0.20f),

        _ => new(0f, Vector2.Zero, 0f, Lifetime, 0f),
    };

    public const float Lifetime = 2.4f;

    /// <summary>
    /// The radial pull, in radii per second squared, that carries a mote born one <see cref="SpawnSigma" />
    /// from the middle exactly to it over one <see cref="Lifetime" />.
    /// </summary>
    /// <remarks>
    /// Solved rather than picked, because this is precisely what "fade and disappear when they reach the
    /// centre" means: from <c>s = at²/2</c>, <c>a = 2s/t²</c>. Get it wrong in one direction and the motes
    /// pile up in the middle and swing; wrong in the other and they never arrive.
    /// </remarks>
    public static float InwardPull => 2f * SpawnSigma / (Lifetime * Lifetime);

    /// <summary>Turns a motion's radius fractions into the units the emitter actually works in.</summary>
    public static ParticleMotion Scale(ParticleMotion motion, float radius) => motion with
    {
        RadialAccel = motion.RadialAccel * radius,
        LinearAccel = motion.LinearAccel * radius,
        Damping = motion.Damping * radius,
    };

    /// <summary>Where a variation's motes are born, before any pull is applied. The same for all four.</summary>
    /// <remarks>
    /// A radial gaussian: angle uniform, offset normally distributed on each axis, so the cloud is dense at
    /// the figure and thins outward with no edge anywhere. Godot's built-in shapes cannot do this — Sphere
    /// is uniform through a disc and SphereSurface is a ring — so the points are generated here and handed
    /// over as <c>EmissionShapeEnum.Points</c>.
    ///
    /// Deterministic from a fixed seed, and deliberately not drawn from <c>RunState.Rng</c>: every client
    /// must generate the same cloud, and a purely cosmetic effect has no business advancing a run's RNG
    /// stream.
    /// </remarks>
    public static Vector2[] SpawnCloud(int count, float radius, float sigma, ulong seed)
    {
        var points = new Vector2[Math.Max(count, 0)];
        var spread = sigma * radius;
        var state = seed;

        for (var i = 0; i < points.Length; i++)
        {
            points[i] = new Vector2(Gaussian(ref state) * spread, Gaussian(ref state) * spread);
        }

        return points;
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
    /// Per variation rather than shared, because the four are not equally visible at equal alpha. White
    /// motes over a lit battlefield carry at a tenth; black ones need four times that to read against the
    /// same background at all.
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
    /// </remarks>
    public static float ScaleFor(float radius, float texturePixels) =>
        texturePixels <= 0f ? 0f : PlayerTint.ClampParticleSize(PlayerTint.ParticleSize) * radius / texturePixels;

    /// <summary>Half the frame's smaller side — the distance everything is expressed against.</summary>
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

        // Every variation spawns identically: one radial gaussian, then pulled. Nothing is launched, so
        // a particle only ever moves the way its own variation pulls it.
        node.EmissionShape = CpuParticles2D.EmissionShapeEnum.Points;
        node.EmissionPoints = SpawnCloud(SpawnCloudPoints, radius, SpawnSigma, SpawnSeed);

        node.InitialVelocityMin = 0f;
        node.InitialVelocityMax = 0f;
        node.RadialAccelMin = motion.RadialAccel * 0.8f;
        node.RadialAccelMax = motion.RadialAccel * 1.2f;
        node.Gravity = motion.LinearAccel;
        node.DampingMin = motion.Damping * 0.8f;
        node.DampingMax = motion.Damping * 1.2f;

        var drawn = ScaleFor(radius, TexturePixels);
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
    /// One ramp for all four now. The inward variation used to have its own, brightest at the end, which
    /// was the wrong half of the problem: what it needed was not to be bright on arrival but to be gone
    /// there, and <see cref="InwardPull" /> is what makes the fade and the arrival coincide.
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
