using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace MultiplayerColors;

/// <summary>
/// Reads back what is actually on screen, so a colour disagreement can be settled with numbers instead of
/// theories.
/// </summary>
/// <remarks>
/// Written after two wrong explanations for "the outline and the ink look different": first that the shader
/// was colour-converting the uniform, then that the brush's soft alpha and premultiplied compositing
/// lightened the stroke. A homogeneous fill disproves both — it saturates alpha, so neither mechanism
/// applies. What was missing was the ability to compare the two colours as they actually exist at runtime
/// rather than as they are computed.
///
/// Reports, for every drawn line and every outline: the colour assigned to it, and the accumulated modulate
/// of every ancestor above it. That second number is the one no amount of reasoning about our own maths can
/// reach — if some container between the map and the screen is tinting one and not the other, only this
/// shows it.
/// </remarks>
public static class MapInkProbe
{
    private const int MaxNodes = 12;

    public static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();

        if (!Diagnostics.InGame)
        {
            // Everything below reaches a Godot static, which is fatal rather than throwable outside the game.
            lines.Add("probe unavailable outside the game");
            return lines;
        }

        try
        {
            lines.Add($"hdr_2d={ProjectSettings.GetSetting("rendering/viewport/hdr_2d", false)} "
                + $"renderer={ProjectSettings.GetSetting("rendering/renderer/rendering_method", "?")}");

            var screen = NMapScreen.Instance;
            if (screen == null)
            {
                lines.Add("map screen not open — open the map and run this again");
                return lines;
            }

            var found = new List<Line2D>();
            Collect(screen, found);

            if (found.Count == 0)
            {
                lines.Add("no drawn lines found — draw on the map first, then run this again");
            }

            foreach (var line in found)
            {
                lines.Add(
                    $"line: default=#{line.DefaultColor.ToHtml()} "
                    + $"ancestors={Describe(Accumulated(line))} "
                    + $"width={line.Width:F0} material={(line.Material == null ? "none" : line.Material.GetType().Name)} "
                    + $"visible={line.IsVisibleInTree()}");
            }
        }
        catch (Exception e)
        {
            lines.Add($"probe failed: {e.Message}");
        }

        return lines;
    }

    /// <summary>The modulate of every ancestor multiplied together — what the screen applies on top.</summary>
    public static Color Accumulated(CanvasItem node)
    {
        var total = Colors.White;

        for (Node? n = node.GetParent(); n != null; n = n.GetParent())
        {
            if (n is not CanvasItem item)
            {
                continue;
            }

            var m = item.Modulate;
            total = new Color(total.R * m.R, total.G * m.G, total.B * m.B, total.A * m.A);
        }

        return total;
    }

    private static string Describe(Color c) =>
        c.IsEqualApprox(Colors.White) ? "none" : $"#{c.ToHtml()}";

    private static void Collect(Node node, List<Line2D> into)
    {
        if (into.Count >= MaxNodes)
        {
            return;
        }

        if (node is Line2D line)
        {
            into.Add(line);
        }

        foreach (var child in node.GetChildren())
        {
            Collect(child, into);
        }
    }
}
