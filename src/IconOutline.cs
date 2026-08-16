using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MultiplayerColors;

/// <summary>
/// Builds the outline layer for a character icon that does not already have one.
/// </summary>
/// <remarks>
/// The multiplayer vote icon ships its own <c>Outline</c> child, so it only needs recolouring. The party
/// panel's icon and the single-player map marker have nothing, so the layer gets built here — copying the
/// game's own idiom: a <c>TextureRect</c> drawn behind its parent via <c>ShowBehindParent</c>, filling the
/// same rect.
///
/// The layer is created whether or not a variation is currently active, and simply sits fully transparent
/// while it is not. That matters for timing: <c>NMapMarker.Initialize</c> runs once when the map screen is
/// built, long before anyone types <c>tint warmer</c>, so an outline created only when a variation already
/// existed would never be created at all — and <c>Refresh</c> would have nothing to repaint.
/// </remarks>
public static class IconOutline
{
    private const string NodeName = "MultiplayerColorsOutline";

    /// <summary>
    /// How opaque an active outline is drawn. Fully opaque: the point of the outline is to be read at a
    /// glance as a colour key, and any extra transparency only muddies it against the map.
    /// </summary>
    /// <remarks>
    /// This is not the silhouette's shape. The shader multiplies by the texture's own alpha, so the outline
    /// still traces the character exactly and still keeps its antialiased edge — this only removes the
    /// additional dimming the game applies to its own vote-icon outline. That vanilla 75% is still what a
    /// dormant outline reverts to; it just is not what an active one wears.
    /// </remarks>
    public const float Alpha = 1f;

    /// <summary>
    /// Outlines this mod created, with the icon each belongs to, so thickness changes can be re-applied to
    /// what is already on screen. Weak keys — a freed node drops out on its own.
    /// </summary>
    private static readonly ConditionalWeakTable<TextureRect, OutlineHost> Attached = new();

    private sealed record OutlineHost(TextureRect Icon, Player Player);

    /// <summary>
    /// Registers an outline the game already ships — the multiplayer vote icon's — so that thickness reaches
    /// it too. Its colour is handled by the caller; only geometry is managed here.
    /// </summary>
    public static void Track(TextureRect? outline, TextureRect? icon, Player? player, Color baseInk)
    {
        if (outline == null || icon == null || player == null)
        {
            return;
        }

        // First adoption only: capture the colour the scene gave it, before the shader takes over, so
        // there is something vanilla to revert to.
        var firstTime = !Attached.TryGetValue(outline, out _);
        var dormant = firstTime ? outline.Modulate : PlayerTint.DormantOutline;

        Attached.AddOrUpdate(outline, new OutlineHost(icon, player));

        if (firstTime)
        {
            InstallShader(outline);
        }

        ApplyThickness(outline, player);
        PlayerTint.ApplyOutline(outline, player, baseInk, Alpha, dormant);
    }

    /// <summary>
    /// Swaps the node onto the silhouette shader and neutralises its own modulate.
    /// </summary>
    /// <remarks>
    /// Modulate is left white deliberately: the colour now lives in a shader uniform, and keeping modulate
    /// neutral means an ancestor's fade still multiplies through untouched — which is what keeps the
    /// multiplayer vote icons fading in and out with their head.
    /// </remarks>
    private static void InstallShader(TextureRect outline)
    {
        outline.Material = OutlineShader.CreateMaterial();
        outline.Modulate = Colors.White;
    }

    /// <summary>
    /// Attaches (or updates) an outline behind <paramref name="icon" />, coloured with the player's map ink.
    /// Idempotent — safe to call again on a node that already has one.
    /// </summary>
    /// <param name="outlineTexture">
    /// The character's pre-dilated outline art, or null when it ships none — a modded character need not.
    /// </param>
    /// <param name="fallbackTexture">The plain icon, grown to stand in when there is no outline art.</param>
    public static void Attach(
        TextureRect? icon,
        Player? player,
        Texture2D? outlineTexture,
        Texture2D? fallbackTexture,
        Color baseInk)
    {
        if (icon == null || player == null)
        {
            return;
        }

        var existing = icon.GetNodeOrNull<TextureRect>(NodeName);
        if (existing != null)
        {
            ApplyThickness(existing, player);
            PlayerTint.ApplyOutline(existing, player, baseInk, Alpha, PlayerTint.DormantOutline);
            return;
        }

        var texture = outlineTexture ?? fallbackTexture;
        if (texture == null)
        {
            Diagnostics.Log($"outline skipped for {player.Character?.Id}: no icon or outline texture");
            return;
        }

        var outline = new TextureRect
        {
            Name = NodeName,
            Texture = texture,
            ShowBehindParent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,

            // Render the silhouette exactly as the icon renders itself, or it will not line up — the map
            // marker art is not square and its node keeps aspect.
            ExpandMode = icon.ExpandMode,
            StretchMode = icon.StretchMode,

        };

        outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        icon.AddChild(outline);
        icon.MoveChild(outline, 0);
        Attached.AddOrUpdate(outline, new OutlineHost(icon, player));

        InstallShader(outline);
        ApplyThickness(outline, player);

        // Dormant means invisible: outlines this mod created did not exist in the vanilla game.
        PlayerTint.ApplyOutline(outline, player, baseInk, Alpha, PlayerTint.DormantOutline);

        Diagnostics.Log(
            $"outline attached to {icon.Name} for {player.Character?.Id} "
            + $"(source={(outlineTexture != null ? "shipped" : "grown icon")}, "
            + $"thickness={PlayerTint.OutlineThickness}px, variation={PlayerTint.For(player)?.ToString() ?? "none"})");
    }

    /// <summary>
    /// One line per live outline: what colour it should be carrying, what it is actually carrying, and
    /// whether the two agree. This is the check that the colour written is the colour that arrived.
    /// </summary>
    public static IReadOnlyList<string> Describe()
    {
        var lines = new List<string>();

        foreach (var (outline, host) in Attached)
        {
            if (!GodotObject.IsInstanceValid(outline) || !GodotObject.IsInstanceValid(host.Icon))
            {
                continue;
            }

            var player = host.Player;
            var variation = PlayerTint.For(player);
            var expected = variation == null
                ? PlayerTint.DormantOutline
                : PlayerTint.OutlineInk(variation.Value, player.Character.MapDrawingColor, Alpha);

            var actual = OutlineShader.ReadColor(outline);
            var rgbMatches =
                Mathf.IsEqualApprox(expected.R, actual.R)
                && Mathf.IsEqualApprox(expected.G, actual.G)
                && Mathf.IsEqualApprox(expected.B, actual.B);

            lines.Add(
                $"{host.Icon.Name}: ink=#{player.Character.MapDrawingColor.ToHtml(false)} "
                + $"want=#{expected.ToHtml()} got=#{actual.ToHtml()} "
                + $"rgb={(rgbMatches ? "MATCH" : "MISMATCH")} "
                + $"shader={(OutlineShader.HasShader(outline) ? "yes" : "NO")} "
                + $"visible={outline.Visible && outline.IsVisibleInTree()}");
        }

        return lines;
    }

    /// <summary>
    /// Re-applies the current thickness to every outline already on screen. Called after
    /// <c>tint outline</c> changes it, so the effect is visible without changing rooms.
    /// </summary>
    public static int RefreshThickness()
    {
        var updated = 0;
        foreach (var (outline, host) in Attached)
        {
            if (!GodotObject.IsInstanceValid(outline) || !GodotObject.IsInstanceValid(host.Icon))
            {
                continue;
            }

            ApplyThickness(outline, host.Player);
            updated++;
        }

        return updated;
    }

    /// <summary>
    /// Grows the outline past its icon by <see cref="PlayerTint.OutlineThickness" /> pixels — or leaves it
    /// exactly congruent when the player has no variation.
    /// </summary>
    /// <remarks>
    /// Collapsing to zero when dormant is what keeps the game's own vote-icon outline vanilla: that one
    /// exists with or without this mod, so while nothing is tinted it has to sit at the size the scene gave
    /// it. Outlines this mod created are transparent when dormant, so their geometry is moot either way.
    ///
    /// Grown via offsets rather than Scale: this can run before layout, where Size is not yet reliable and a
    /// pivot-based scale would grow from the corner. Anchors plus offsets are resolved by layout itself.
    /// </remarks>
    private static void ApplyThickness(TextureRect outline, Player player)
    {
        var grow = PlayerTint.For(player) == null
            ? 0f
            : PlayerTint.ClampThickness(PlayerTint.OutlineThickness);

        outline.OffsetLeft = -grow;
        outline.OffsetTop = -grow;
        outline.OffsetRight = grow;
        outline.OffsetBottom = grow;
    }
}
