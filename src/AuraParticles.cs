using System.Runtime.CompilerServices;
using Godot;

namespace MultiplayerColors;

/// <summary>Where a variation's particles are born.</summary>
public enum ParticleEmission
{
    /// <summary>A small blob at the middle of the figure — they leave from behind it.</summary>
    Centre,

    /// <summary>The rim of the frame — they arrive from outside and converge on the figure.</summary>
    Edge,

    /// <summary>Anywhere across the frame — they drift through it.</summary>
    Area,
}

/// <summary>
/// How one variation's particles move. Every distance is a fraction of the figure's radius rather than a
/// number of pixels, so the same description works on a combat body and on a Sovereign Blade — and, more
/// to the point, survives being handed to a node living in a SpineSprite's local units, which are several
/// times larger than a screen pixel.
/// </summary>
/// <param name="Direction">Which way particles are launched. Godot's Y axis points down.</param>
/// <param name="Spread">Half-angle around <paramref name="Direction" />, in degrees. 180 is every way at once.</param>
/// <param name="Velocity">Launch speed, in radii per second.</param>
/// <param name="RadialAccel">Acceleration away from the emitter, in radii per second squared. Negative pulls inward.</param>
/// <param name="Gravity">Constant downward acceleration, in radii per second squared. Negative floats.</param>
/// <param name="FadeIn">Brightest at the end of a particle's life rather than the start.</param>
public readonly record struct ParticleMotion(
    ParticleEmission Emission,
    Vector2 Direction,
    float Spread,
    float Velocity,
    float RadialAccel,
    float Gravity,
    bool FadeIn);

/// <summary>
/// A handful of drifting motes alongside the aura, whose <em>motion</em> names the variation.
/// </summary>
/// <remarks>
/// Colour alone is uneven: a black aura on a dark battlefield is much weaker than a white one, and red and
/// blue at low strength are the pair most easily confused. Motion is not — "thrown outward", "pulled
/// inward", "rising", "falling" are four readings no background can flatten into one another. The two
/// signals carry the same key by different means.
///
/// Built on <c>CpuParticles2D</c> rather than <c>GpuParticles2D</c>: at a dozen particles the GPU path buys
/// nothing and costs a <c>ParticleProcessMaterial</c> per figure, and every knob this needs is a plain
/// property on the CPU node. It also matters that this mod compiles without the Godot SDK's source
/// generators, so it can only ever configure built-in node types — never subclass one.
/// </remarks>
public static class AuraParticles
{
    /// <summary>How long a mote lives, in seconds. Long enough to cross the frame at these speeds.</summary>
    public const float Lifetime = 2.4f;

    /// <summary>
    /// How big a mote is drawn, as a fraction of the figure's radius. Small enough to read as a speck.
    /// </summary>
    public const float Size = 0.035f;

    /// <summary>The motion that names a variation.</summary>
    public static ParticleMotion MotionFor(PlayerVariation variation) => variation switch
    {
        // Thrown out of the middle in every direction, so they emerge from behind the figure's own outline.
        PlayerVariation.Brighter =>
            new(ParticleEmission.Centre, Vector2.Up, 180f, 0.34f, 0.04f, 0f, FadeIn: false),

        // Born on the rim, still, and drawn in. Fading IN means they arrive rather than appear: invisible
        // where they were born, brightest as they converge, gone behind the figure.
        PlayerVariation.Darker =>
            new(ParticleEmission.Edge, Vector2.Up, 180f, 0f, -0.55f, 0f, FadeIn: true),

        // Sparks off a fire: launched upward, and buoyant rather than falling back.
        PlayerVariation.Warmer =>
            new(ParticleEmission.Area, Vector2.Up, 12f, 0.24f, 0f, -0.05f, FadeIn: false),

        // Snow: barely launched at all, and settling. Slower than the sparks on purpose — it is the second
        // thing separating the two linear motions, after their colour.
        PlayerVariation.Cooler =>
            new(ParticleEmission.Area, Vector2.Down, 12f, 0.09f, 0f, 0.07f, FadeIn: false),

        _ => new(ParticleEmission.Area, Vector2.Up, 0f, 0f, 0f, 0f, FadeIn: false),
    };

    /// <summary>Turns a motion's radius fractions into the units the emitter actually works in.</summary>
    public static ParticleMotion Scale(ParticleMotion motion, float radius) => motion with
    {
        Velocity = motion.Velocity * radius,
        RadialAccel = motion.RadialAccel * radius,
        Gravity = motion.Gravity * radius,
    };

    /// <summary>The particle colour: the aura's, at the given strength. One key, read two ways.</summary>
    public static Color ColorFor(PlayerVariation variation, float strength)
    {
        var colour = PlayerTint.AuraColor(variation);
        return new Color(colour.R, colour.G, colour.B, PlayerTint.ClampParticleStrength(strength));
    }

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
    /// <remarks>
    /// Unlike an aura or an outline this is not a colour assignment — the emitter's whole configuration
    /// changes with the variation — so it is registered as its own <c>TintKind</c> and rebuilt rather than
    /// recoloured.
    /// </remarks>
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
        var strength = PlayerTint.ClampParticleStrength(PlayerTint.ParticleStrength);

        if (variation == null || count == 0 || strength <= 0f || !AuraLayer.IsMeasurable(frame))
        {
            node.Emitting = false;
            node.Visible = false;
            return;
        }

        var radius = Radius(frame);
        var motion = Scale(MotionFor(variation.Value), radius);

        node.Position = frame.GetCenter();
        node.Amount = count;
        node.Lifetime = Lifetime;

        // Started mid-life rather than empty, so walking into a room does not begin with a visible puff.
        node.Preprocess = Lifetime;

        node.Direction = motion.Direction;
        node.Spread = motion.Spread;
        node.InitialVelocityMin = motion.Velocity * 0.7f;
        node.InitialVelocityMax = motion.Velocity * 1.3f;
        node.RadialAccelMin = motion.RadialAccel * 0.8f;
        node.RadialAccelMax = motion.RadialAccel * 1.2f;
        node.Gravity = new Vector2(0f, motion.Gravity);

        switch (motion.Emission)
        {
            case ParticleEmission.Centre:
                node.EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere;
                node.EmissionSphereRadius = radius * 0.18f;
                break;

            case ParticleEmission.Edge:
                node.EmissionShape = CpuParticles2D.EmissionShapeEnum.SphereSurface;
                node.EmissionSphereRadius = radius;
                break;

            default:
                node.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
                node.EmissionRectExtents = frame.Size / 2f;
                break;
        }

        node.ScaleAmountMin = radius * Size * 0.6f;
        node.ScaleAmountMax = radius * Size * 1.4f;

        node.Color = ColorFor(variation.Value, strength);
        node.ColorRamp = Ramp(motion.FadeIn);

        node.Visible = true;
        node.Emitting = true;
    }

    /// <summary>Half the frame's smaller side — the distance everything is expressed against.</summary>
    public static float Radius(Rect2 frame) => 0.5f * MathF.Min(frame.Size.X, frame.Size.Y);

    /// <summary>
    /// The alpha curve over a particle's life, multiplied onto <c>Color</c>. Both ends reach zero, so a
    /// mote never pops into or out of existence.
    /// </summary>
    private static Gradient Ramp(bool fadeIn)
    {
        var gradient = new Gradient
        {
            Offsets = [0f, fadeIn ? 0.85f : 0.15f, 1f],
            Colors =
            [
                new Color(1f, 1f, 1f, 0f),
                Colors.White,
                new Color(1f, 1f, 1f, 0f),
            ],
        };

        return gradient;
    }
}
