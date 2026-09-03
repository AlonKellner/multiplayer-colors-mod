using Godot;

namespace MultiplayerColors;

/// <summary>Where a measured figure box came from. Reported by <c>tint diag</c>.</summary>
public enum AuraBoundsSource
{
    /// <summary>Nothing usable was found; no aura is attached.</summary>
    None,

    /// <summary>The Spine runtime's own <c>get_bounds()</c> — the posed skeleton's box.</summary>
    Spine,

    /// <summary>A Control the patch handed over, e.g. <c>%Bounds</c> in combat or <c>%Hitbox</c> at a rest site.</summary>
    Hint,

    /// <summary>Where a TextureRect's art is actually drawn inside its rect, once stretch mode is accounted for.</summary>
    DrawnRect,
}

/// <summary>
/// Measures the box to hang a figure's aura on.
/// </summary>
/// <remarks>
/// A Node2D has no bounding box in general, so this is a search rather than a lookup — and it reports which
/// source answered, because "the aura did not appear" and "the aura appeared in the wrong place" have
/// completely different causes and the diag line has to tell them apart.
/// </remarks>
public static class AuraBounds
{
    /// <summary>
    /// The method the Spine runtime binds for a posed skeleton's extent. Present in the shipped
    /// <c>libspine_godot</c>, but probed rather than assumed — a Spine build without it should cost us an
    /// aura, not a room.
    /// </summary>
    private const string SpineBoundsMethod = "get_bounds";

    /// <summary>
    /// The figure's box in <paramref name="art" />'s own local space, or an empty rect when nothing could
    /// be measured.
    /// </summary>
    public static Rect2 Measure(CanvasItem art, Control? hint, out AuraBoundsSource source)
    {
        // The tight, posed skeleton box. Preferred wherever it exists: it is the only source that describes
        // the figure rather than the space the scene reserved for it.
        if (art.HasMethod(SpineBoundsMethod))
        {
            var bounds = art.Call(SpineBoundsMethod).AsRect2();
            if (AuraLayer.IsMeasurable(bounds))
            {
                source = AuraBoundsSource.Spine;
                return bounds;
            }
        }

        if (art is TextureRect rect)
        {
            var drawn = DrawnRect(rect.Size, rect.Texture?.GetSize() ?? Vector2.Zero, rect.StretchMode);
            if (AuraLayer.IsMeasurable(drawn))
            {
                source = AuraBoundsSource.DrawnRect;
                return drawn;
            }
        }

        // A scene-authored box, in the hint's own space. Only usable when the two share a transform, which
        // is why the patch picks the hint rather than this doing a tree walk to find one.
        if (hint != null && AuraLayer.IsMeasurable(new Rect2(hint.Position, hint.Size)))
        {
            source = AuraBoundsSource.Hint;
            return new Rect2(hint.Position, hint.Size);
        }

        source = AuraBoundsSource.None;
        return new Rect2();
    }

    /// <summary>
    /// Where a <c>TextureRect</c>'s art actually lands inside its rect, once its stretch mode is taken into
    /// account.
    /// </summary>
    /// <remarks>
    /// Not pedantry: <c>hand_image.tscn</c>'s TextureRect is 383x1072 with <c>expand_mode = 1</c> and
    /// <c>stretch_mode = 5</c> (keep-aspect-centred), so the arm is letterboxed inside a rect nearly three
    /// times its own height. Framing the raw rect would put the glow's peak far below the visible hand.
    ///
    /// Modes that fill or clip to the whole rect return it unchanged, which is also the answer when there is
    /// no texture to measure against.
    /// </remarks>
    public static Rect2 DrawnRect(Vector2 controlSize, Vector2 textureSize, TextureRect.StretchModeEnum stretch)
    {
        var whole = new Rect2(Vector2.Zero, controlSize);

        if (textureSize.X <= 0f || textureSize.Y <= 0f)
        {
            return whole;
        }

        switch (stretch)
        {
            case TextureRect.StretchModeEnum.Keep:
                return new Rect2(Vector2.Zero, textureSize);

            case TextureRect.StretchModeEnum.KeepCentered:
                return new Rect2((controlSize - textureSize) / 2f, textureSize);

            case TextureRect.StretchModeEnum.KeepAspect:
            case TextureRect.StretchModeEnum.KeepAspectCentered:
            {
                var scale = MathF.Min(controlSize.X / textureSize.X, controlSize.Y / textureSize.Y);
                var size = textureSize * scale;
                var position = stretch == TextureRect.StretchModeEnum.KeepAspectCentered
                    ? (controlSize - size) / 2f
                    : Vector2.Zero;

                return new Rect2(position, size);
            }

            default:
                // Scale, Tile and KeepAspectCovered all cover the rect (the last by clipping to it).
                return whole;
        }
    }
}
