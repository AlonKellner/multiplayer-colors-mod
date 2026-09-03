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
        return ClipToView(Locate(art, hint, out source), ViewInLocalSpace(art));
    }

    /// <summary>
    /// The viewport in <paramref name="art" />'s own coordinates, or an unbounded rect when it cannot be
    /// worked out — a node not yet in the tree, or a collapsed transform.
    /// </summary>
    /// <remarks>
    /// Uses <c>GetGlobalTransformWithCanvas</c> rather than <c>GetGlobalTransform</c>: combat runs under a
    /// Camera2D, so canvas coordinates and screen coordinates are not the same thing, and clipping against
    /// the wrong one would trim figures that are perfectly visible.
    /// </remarks>
    private static Rect2 ViewInLocalSpace(CanvasItem art)
    {
        if (!art.IsInsideTree())
        {
            return Unbounded;
        }

        var toScreen = art.GetGlobalTransformWithCanvas();
        var determinant = (toScreen.X.X * toScreen.Y.Y) - (toScreen.X.Y * toScreen.Y.X);

        return MathF.Abs(determinant) < 1e-6f
            ? Unbounded
            : toScreen.AffineInverse() * art.GetViewportRect();
    }

    /// <summary>Large enough to clip nothing, small enough not to overflow when intersected.</summary>
    private static readonly Rect2 Unbounded = new(-1e6f, -1e6f, 2e6f, 2e6f);

    private static Rect2 Locate(CanvasItem art, Control? hint, out AuraBoundsSource source)
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
    /// Narrows a measured box to the part of it that is actually on screen.
    /// </summary>
    /// <remarks>
    /// An aura is a halo, and a halo belongs around what you are looking at. Most art is wholly visible and
    /// this changes nothing — but the treasure-room arm is 377x1072 and reaches in from off the edge of the
    /// screen, with only its hand end in view. Framed whole, the aura's peak sits halfway down a forearm
    /// nobody can see and the hand gets the tail: an alpha of about 0.009 where the aura's own strength is
    /// 0.18. Clipped, the halo lands on the hand.
    ///
    /// Falls back to the full box rather than to nothing when the intersection is empty or a sliver, since
    /// this is measured once and a figure may be off screen at that moment.
    /// </remarks>
    /// <param name="viewInLocal">The visible viewport, expressed in the art's own coordinates.</param>
    public static Rect2 ClipToView(Rect2 bounds, Rect2 viewInLocal)
    {
        var visible = bounds.Intersection(viewInLocal);

        return AuraLayer.IsMeasurable(visible) ? visible : bounds;
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
