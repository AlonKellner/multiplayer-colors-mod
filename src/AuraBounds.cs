using Godot;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

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
    /// The figure's box in <paramref name="art" />'s own local space, or an empty rect when nothing could
    /// be measured.
    /// </summary>
    /// <remarks>
    /// Always the WHOLE art, never the part of it currently on screen. Clipping to the viewport was tried
    /// in v0.1.37 to put the treasure-room arm's halo on its hand rather than halfway down a forearm, and
    /// taken back out in v0.1.38: the arm follows its player's cursor every frame, so a box measured once
    /// is stale the moment the hand moves, and a stale clip is worse than none — it frames a sliver at the
    /// fingertips and leaves the rest of the visible arm bare. Measuring the whole art gives a constant in
    /// the art's own space, which needs no re-measuring and rotates rigidly with it.
    /// </remarks>
    public static Rect2 Measure(CanvasItem art, Control? hint, out AuraBoundsSource source)
    {
        // The scene's own box, converted into the art's frame. Preferred over the skeleton because it is
        // authored per character and does not change with the pose — a lunging attack must not resize
        // somebody's aura.
        if (hint != null)
        {
            var converted = HintBounds(new Rect2(hint.Position, hint.Size), TransformInParent(art));
            if (AuraLayer.IsMeasurable(converted))
            {
                source = AuraBoundsSource.Hint;
                return converted;
            }
        }

        var skeleton = SpineBounds(art);
        if (skeleton != null && AuraLayer.IsMeasurable(skeleton.Value))
        {
            source = AuraBoundsSource.Spine;
            return skeleton.Value;
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

        source = AuraBoundsSource.None;
        return new Rect2();
    }

    /// <summary>The art's own transform within its parent. CanvasItem does not expose one; its two
    /// subclasses each spell it differently.</summary>
    private static Transform2D TransformInParent(CanvasItem art) => art switch
    {
        Node2D node => node.Transform,
        Control control => control.GetTransform(),
        _ => Transform2D.Identity,
    };

    /// <summary>
    /// A scene-authored box, expressed in the art's own coordinates rather than its parent's.
    /// </summary>
    /// <remarks>
    /// The conversion is the whole point. Combat hands over <c>NCreatureVisuals.Bounds</c>, a sibling of
    /// the art rather than an ancestor of it: on Ironclad that box is 242x278 at (-121,-278), while the
    /// art it describes sits at (5,-19) and is scaled 0.28. Applied raw — which is what v0.1.29 through
    /// v0.1.40 did — a 242x278 box lands in a frame 3.6x smaller than the one it was measured in, and the
    /// aura comes out about a third of the character's size, floating over its chest.
    ///
    /// A collapsed transform has no inverse; the unconverted box is wrong, but a box of NaNs is worse,
    /// since it fails <see cref="AuraLayer.IsMeasurable" /> and takes the aura away altogether.
    /// </remarks>
    public static Rect2 HintBounds(Rect2 hintInParent, Transform2D artInParent)
    {
        var determinant = (artInParent.X.X * artInParent.Y.Y) - (artInParent.X.Y * artInParent.Y.X);

        return MathF.Abs(determinant) < 1e-6f
            ? hintInParent
            : artInParent.AffineInverse() * hintInParent;
    }

    /// <summary>
    /// The posed skeleton's box, or <c>null</c> when this is not Spine art or its skeleton is not ready.
    /// </summary>
    /// <remarks>
    /// <c>get_bounds()</c> is bound on the SKELETON, not on the sprite — <c>MegaSkeleton.GetBounds</c>,
    /// reached through <c>MegaSprite.GetSkeleton()</c>. Probing the sprite for it, which is what v0.1.29
    /// through v0.1.40 did, always came back false: combat quietly fell through to its hint, and the rest
    /// site, the shop and the Sovereign Blade — none of which has a hint — got no aura at all.
    /// </remarks>
    public static Rect2? SpineBounds(CanvasItem art)
    {
        if (art.GetClass() != MegaSprite.spineClassName)
        {
            return null;
        }

        try
        {
            return new MegaSprite(art).GetSkeleton()?.GetBounds();
        }
        catch (Exception e)
        {
            // A skeleton that is not ready is a timing problem, not a reason to take a room down.
            Diagnostics.Log($"spine bounds unavailable for {art.Name}: {e.Message}");
            return null;
        }
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
