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

            SampleViewports(screen, lines);

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

    /// <summary>
    /// Reads the drawing viewport's actual pixels back off the GPU.
    /// </summary>
    /// <remarks>
    /// This is the measurement that settles it. The lines are drawn into a SubViewport and then composited
    /// onto the map with premultiplied alpha, where a pixel lands as <c>ink + parchment x (1 - alpha)</c> —
    /// brighter than the ink itself for anything short of fully opaque, and exactly the ink at alpha 1. Our
    /// outline is drawn with ordinary blending, so at alpha 1 the two agree exactly and below it they do
    /// not. Whether a "homogeneous" fill truly reaches alpha 1 is not something either of us can judge by
    /// eye, so read it.
    /// </remarks>
    private static void SampleViewports(Node screen, List<string> lines)
    {
        var viewports = new List<SubViewport>();
        CollectViewports(screen, viewports);

        foreach (var viewport in viewports)
        {
            var image = viewport.GetTexture()?.GetImage();
            if (image == null)
            {
                continue;
            }

            var maxAlpha = 0f;
            var opaque = 0;
            var painted = 0;
            var at = Colors.Transparent;

            for (var y = 0; y < image.GetHeight(); y += 2)
            {
                for (var x = 0; x < image.GetWidth(); x += 2)
                {
                    var px = image.GetPixel(x, y);
                    if (px.A <= 0.01f)
                    {
                        continue;
                    }

                    painted++;
                    if (px.A > 0.99f)
                    {
                        opaque++;
                    }

                    if (px.A > maxAlpha)
                    {
                        maxAlpha = px.A;
                        at = px;
                    }
                }
            }

            lines.Add(
                $"viewport {viewport.Size.X}x{viewport.Size.Y}: painted={painted} fullyOpaque={opaque} "
                + $"maxAlpha={maxAlpha:F3} at=#{at.ToHtml()}");
        }
    }

    private static void CollectViewports(Node node, List<SubViewport> into)
    {
        if (into.Count >= 4)
        {
            return;
        }

        if (node is SubViewport viewport)
        {
            into.Add(viewport);
        }

        foreach (var child in node.GetChildren())
        {
            CollectViewports(child, into);
        }
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
