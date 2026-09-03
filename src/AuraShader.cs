using Godot;

namespace MultiplayerColors;

/// <summary>
/// The shader behind the colour-key aura: a soft radial falloff in a flat colour, drawn behind a figure.
/// </summary>
/// <remarks>
/// There is no silhouette here, and there deliberately cannot be one. Almost every surface this mod tints
/// is Spine skeleton art (<c>NCreatureVisuals._body</c> is <c>%Visuals</c>, whose
/// <c>GetClass() == "SpineSprite"</c>), and the dilate-the-alpha trick <see cref="OutlineShader" /> uses on
/// icon textures cannot work there — sampling outside a Spine atlas region bleeds into whatever art is
/// packed next to it. The only exact option is reparenting each figure under a <c>CanvasGroup</c>, which
/// changes how additive Spine slots composite (Ironclad's fire, its eye flame, Regent's effects) and costs
/// a render target per creature.
///
/// So this draws a plain blob and lets the figure do the shaping: the art sits in front and covers the
/// middle, and what is left visible is the fringe around its own outline. That is what turns a radial
/// gradient into something that reads as an aura hugging the border.
/// </remarks>
public static class AuraShader
{
    /// <summary>The uniform the aura colour is written to. RGB is the key colour, alpha is the strength.</summary>
    public const string ColorParameter = "aura_color";

    /// <summary>The uniform controlling how quickly the glow gives up as it moves outward.</summary>
    public const string SoftnessParameter = "softness";

    /// <summary>
    /// The falloff exponent. Above 1 the glow concentrates near the figure and thins quickly, which is what
    /// makes it read as a haze rather than as a lens flare with a visible edge.
    /// </summary>
    public const float Softness = 1.6f;

    /// <remarks>
    /// <c>COLOR</c> arrives as the <c>ColorRect</c>'s own colour multiplied by the whole modulate chain.
    /// Only its alpha is used, so the aura keeps its key colour even though it hangs beneath a node this
    /// mod has tinted — while any fade an ancestor is applying still comes through, exactly as the outline
    /// shader arranges.
    ///
    /// Deliberately NOT hinted <c>: source_color</c>, for the reason documented at length in
    /// <see cref="OutlineShader.Code" />: the hint makes Godot colour-convert the uniform on upload, so it
    /// stops agreeing with the raw values everything else in this mod is written in.
    /// </remarks>
    public const string Code = """
        shader_type canvas_item;

        uniform vec4 aura_color = vec4(1.0, 1.0, 1.0, 0.0);
        uniform float softness = 1.6;

        void fragment() {
            vec2 p = (UV - 0.5) * 2.0;
            float a = pow(clamp(1.0 - length(p), 0.0, 1.0), softness);
            COLOR = vec4(aura_color.rgb, aura_color.a * a * COLOR.a);
        }
        """;

    /// <summary>
    /// The C# mirror of the shader's falloff, so its shape is covered by tests the engine-less host can
    /// actually run. <paramref name="distance" /> is 0 at the centre of the frame and 1 at its edge.
    /// </summary>
    public static float Falloff(float distance, float softness) =>
        MathF.Pow(Math.Clamp(1f - distance, 0f, 1f), softness);

    private static Shader? _shader;

    /// <summary>A material for one aura. Each needs its own, since the colour is per player.</summary>
    public static ShaderMaterial CreateMaterial()
    {
        _shader ??= new Shader { Code = Code };

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter(SoftnessParameter, Softness);
        return material;
    }

    /// <summary>Writes the aura colour. A node without the shader is left alone rather than tinted flat.</summary>
    public static void SetColor(CanvasItem node, Color color)
    {
        if (node.Material is ShaderMaterial material)
        {
            material.SetShaderParameter(ColorParameter, color);
        }
    }

    /// <summary>The colour the node is actually carrying, read back for <c>tint diag</c>.</summary>
    public static Color ReadColor(CanvasItem node) =>
        node.Material is ShaderMaterial material
            ? material.GetShaderParameter(ColorParameter).AsColor()
            : PlayerTint.DormantAura;

    /// <summary>Whether the node is being drawn through the aura shader at all.</summary>
    public static bool HasShader(CanvasItem node) =>
        node.Material is ShaderMaterial material && material.Shader == _shader;
}
