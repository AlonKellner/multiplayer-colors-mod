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

        void fragment() {
            COLOR = vec4(outline_color.rgb, COLOR.a * outline_color.a);
        }
        """;

    private static Shader? _shader;

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
