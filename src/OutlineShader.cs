using System.Runtime.CompilerServices;
using Godot;

namespace MultiplayerColors;

/// <summary>
/// The shader that turns any character art into a solid silhouette in the player's colour.
/// </summary>
/// <remarks>
/// <c>Modulate</c> cannot do this. It multiplies, so it only works when the source art is already pure
/// white — which the game's shipped <c>_outline.png</c> is, but a character's ordinary icon is not.
/// Multiplying full-colour art just tints it, which is why the grown-icon fallback rendered as "the same
/// icon, larger" rather than an outline.
///
/// Replacing RGB outright while carrying alpha through needs a fragment shader, so this is that shader,
/// built from an inline string — no <c>.pck</c>, no asset, nothing to ship.
/// </remarks>
public static class OutlineShader
{
    /// <summary>The uniform the outline colour is written to.</summary>
    public const string ColorParameter = "outline_color";

    /// <summary>The measured alpha range of the outline texture, stretched to fill 0..1 at draw time.</summary>
    public const string MinAlphaParameter = "alpha_min";

    public const string MaxAlphaParameter = "alpha_max";

    /// <remarks>
    /// <c>COLOR</c> arrives as the texture multiplied by the modulate chain, so taking its alpha keeps both
    /// the silhouette shape and any fade an ancestor is applying — which is what lets the multiplayer vote
    /// icons still fade in and out with their head.
    ///
    /// Deliberately NOT hinted <c>: source_color</c>. That hint makes Godot colour-convert the uniform on
    /// upload, while <c>Modulate</c> — the thing this has to match — is passed through raw. The two then
    /// disagree, and the outline comes out the right hue at the wrong shade. Raw in, raw out.
    ///
    /// <c>MODULATE</c> is not a canvas_item built-in in this engine version (verified: the shader fails to
    /// compile with "Unknown identifier in expression: 'MODULATE'"), which is why the ancestor fade is
    /// carried through <c>COLOR.a</c> rather than read directly.
    /// </remarks>
    public const string Code = """
        shader_type canvas_item;

        uniform vec4 outline_color = vec4(0.0, 0.0, 0.0, 0.75);
        uniform float alpha_min = 0.0;
        uniform float alpha_max = 1.0;

        void fragment() {
            float raw = texture(TEXTURE, UV).a;
            float span = max(alpha_max - alpha_min, 0.0001);
            float norm = clamp((raw - alpha_min) / span, 0.0, 1.0);

            // COLOR.a is the texture's alpha times whatever an ancestor is fading by. Rescaling it by
            // norm/raw swaps the texture's own curve for the normalised one while leaving that fade
            // untouched, so the vote icons still fade in and out with their head.
            float faded = raw > 0.0001 ? COLOR.a * (norm / raw) : 0.0;

            COLOR = vec4(outline_color.rgb, faded * outline_color.a);
        }
        """;

    private static Shader? _shader;

    /// <summary>Alpha ranges already measured, keyed by texture, so each is scanned once.</summary>
    private static readonly ConditionalWeakTable<Texture2D, StrongBox<(float Min, float Max)>> Ranges = new();

    /// <summary>
    /// The lowest and highest alpha present. Returns the identity range 0..1 when there is nothing to
    /// measure, so an unmeasurable texture is simply left alone.
    /// </summary>
    public static (float Min, float Max) AlphaRange(IReadOnlyCollection<float> alphas)
    {
        if (alphas.Count == 0)
        {
            return (0f, 1f);
        }

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var a in alphas)
        {
            min = MathF.Min(min, a);
            max = MathF.Max(max, a);
        }

        return (min, max);
    }

    /// <summary>
    /// Stretches <paramref name="raw" /> so that <paramref name="min" /> becomes fully transparent and
    /// <paramref name="max" /> fully opaque.
    /// </summary>
    /// <remarks>
    /// A flat texture — every pixel the same alpha — has no range to stretch, so it is passed through
    /// untouched rather than divided by zero.
    /// </remarks>
    public static float NormalizeAlpha(float raw, float min, float max)
    {
        var span = max - min;
        return span <= 0.0001f ? raw : Math.Clamp((raw - min) / span, 0f, 1f);
    }

    /// <summary>
    /// Measures a texture's alpha range, decompressing it if needed. Cached per texture — the scan is a few
    /// thousand pixels, which is cheap once and wasteful every time an icon is built.
    /// </summary>
    public static (float Min, float Max) MeasureAlphaRange(Texture2D? texture)
    {
        if (texture == null)
        {
            return (0f, 1f);
        }

        if (Ranges.TryGetValue(texture, out var cached))
        {
            return cached.Value;
        }

        var range = Measure(texture);
        Ranges.AddOrUpdate(texture, new StrongBox<(float, float)>(range));
        return range;
    }

    private static (float Min, float Max) Measure(Texture2D texture)
    {
        try
        {
            var image = texture.GetImage();
            if (image == null)
            {
                return (0f, 1f);
            }

            if (image.IsCompressed() && image.Decompress() != Error.Ok)
            {
                return (0f, 1f);
            }

            var alphas = new List<float>(image.GetWidth() * image.GetHeight());
            for (var y = 0; y < image.GetHeight(); y++)
            {
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    alphas.Add(image.GetPixel(x, y).A);
                }
            }

            return AlphaRange(alphas);
        }
        catch (Exception)
        {
            // An unreadable texture just means no rescaling; never worth taking a room down for.
            return (0f, 1f);
        }
    }

    /// <summary>A material for one outline node. Each needs its own, since the colour is per player.</summary>
    public static ShaderMaterial CreateMaterial()
    {
        _shader ??= new Shader { Code = Code };
        return new ShaderMaterial { Shader = _shader };
    }

    /// <summary>
    /// Writes the colour, via the shader when the node has one and falling back to <c>Modulate</c> when it
    /// does not — that fallback is correct for the game's own outline art, which is pure white.
    /// </summary>
    public static void SetColor(CanvasItem node, Color color)
    {
        if (node.Material is ShaderMaterial material)
        {
            material.SetShaderParameter(ColorParameter, color);
            return;
        }

        node.Modulate = color;
    }

    /// <summary>
    /// Tells the shader the alpha range of the texture it is drawing, so it can stretch it to fill 0..1 —
    /// at least one pixel fully transparent, at least one fully opaque.
    /// </summary>
    public static void SetAlphaRange(CanvasItem node, Texture2D? texture)
    {
        if (node.Material is not ShaderMaterial material)
        {
            return;
        }

        var (min, max) = MeasureAlphaRange(texture);
        material.SetShaderParameter(MinAlphaParameter, min);
        material.SetShaderParameter(MaxAlphaParameter, max);
    }

    /// <summary>
    /// The colour the node is actually carrying right now, read back from wherever it was written. Used by
    /// <c>tint diag</c> to prove the value that arrived matches the value intended.
    /// </summary>
    public static Color ReadColor(CanvasItem node) =>
        node.Material is ShaderMaterial material
            ? material.GetShaderParameter(ColorParameter).AsColor()
            : node.Modulate;

    /// <summary>Whether the node is being drawn through the silhouette shader rather than a plain multiply.</summary>
    public static bool HasShader(CanvasItem node) =>
        node.Material is ShaderMaterial material && material.Shader == _shader;
}
