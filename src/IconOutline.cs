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
/// </remarks>
public static class IconOutline
{
    private const string NodeName = "MultiplayerColorsOutline";

    /// <summary>Matches the alpha the game uses on the vote icons' outline.</summary>
    public const float Alpha = 0.7529412f;

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

        // Nothing to show unless this player actually has a variation — automatic, or forced via `tint`.
        if (PlayerTint.For(player) == null && icon.GetNodeOrNull<TextureRect>(NodeName) == null)
        {
            return;
        }

        var existing = icon.GetNodeOrNull<TextureRect>(NodeName);
        if (existing != null)
        {
            PlayerTint.ApplyOutline(existing, player, baseInk);
            return;
        }

        var source = PlayerTint.ChooseOutlineSource(outlineTexture != null);
        var texture = outlineTexture ?? fallbackTexture;
        if (texture == null)
        {
            return;
        }

        var outline = new TextureRect
        {
            Name = NodeName,
            Texture = texture,
            ShowBehindParent = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,

            // Render the silhouette exactly as the icon renders itself, or it will not line up — the map
            // marker art is not square and its parent keeps aspect.
            ExpandMode = icon.ExpandMode,
            StretchMode = icon.StretchMode,
        };

        outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // The shipped outline art is already dilated, so it wants a congruent rect. The fallback is the
        // plain icon and has to be grown to peek out from behind.
        //
        // Grown via offsets rather than Scale: this runs before layout, so Size is not reliable yet and a
        // pivot-based scale would grow from the corner. Anchors plus offsets are resolved by layout itself.
        if (source == OutlineSource.ScaledIcon)
        {
            var size = icon.Size != Vector2.Zero
                ? icon.Size
                : new Vector2(icon.OffsetRight - icon.OffsetLeft, icon.OffsetBottom - icon.OffsetTop);

            var grow = size * (PlayerTint.FallbackOutlineScale - 1f) * 0.5f;
            outline.OffsetLeft = -grow.X;
            outline.OffsetTop = -grow.Y;
            outline.OffsetRight = grow.X;
            outline.OffsetBottom = grow.Y;
        }

        // Start from the vanilla outline colour so ApplyOutline records the right base to revert to.
        outline.Modulate = new Color(0f, 0f, 0f, Alpha);

        icon.AddChild(outline);
        icon.MoveChild(outline, 0);

        PlayerTint.ApplyOutline(outline, player, baseInk);
    }
}
