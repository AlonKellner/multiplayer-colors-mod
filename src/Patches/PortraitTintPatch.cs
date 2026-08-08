using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
// Not a typo: the top-bar namespace really is spelled with a lowercase "sts2" in the game assembly.
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints the character portrait in the top bar. The icon child is added with <c>AddChildSafely</c>, which
/// can defer, so we tint the portrait node itself — nothing else writes its modulate.
/// </summary>
[HarmonyPatch(typeof(NTopBarPortrait), nameof(NTopBarPortrait.Initialize))]
public static class TopBarPortraitTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NTopBarPortrait __instance, Player player)
    {
        try
        {
            PlayerTint.Apply(__instance, player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"TopBarPortraitTintPatch failed: {e}");
        }
    }
}

/// <summary>
/// Tints the character icon in the multiplayer party strip — the one place every player in the run is shown
/// side by side — and outlines it in that player's map-drawing colour, so the strip doubles as the legend
/// for who owns which colour on the map.
/// </summary>
/// <remarks>
/// <c>_characterIcon</c> is fetched via <c>GetNode</c> inside <c>_Ready</c>, so it is always present in a
/// postfix. Tinting the icon rather than the whole row leaves the health bar, nameplate and status
/// indicators reading normally.
///
/// Unlike the vote icons, this scene ships no outline node — the icon is a bare <c>TextureRect</c> and there
/// is no border, panel or background anywhere in the row — so one has to be built. It copies the game's own
/// idiom exactly: a <c>TextureRect</c> child with <c>ShowBehindParent</c>, full-rect anchors and the
/// character's pre-dilated outline silhouette.
/// </remarks>
[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
public static class PartyIconTintPatch
{
    private const string OutlineNodeName = "MultiplayerColorsOutline";

    /// <summary>Matches the alpha the game uses on the vote icons' outline.</summary>
    private const float OutlineAlpha = 0.7529412f;

    [HarmonyPostfix]
    public static void Postfix(NMultiplayerPlayerState __instance)
    {
        try
        {
            var icon = __instance._characterIcon;
            var player = __instance.Player;
            if (icon == null || player == null)
            {
                return;
            }

            // SelfModulate so the head's tint does not multiply into the outline we add below.
            PlayerTint.ApplySelf(icon, player);

            var ink = PlayerTint.OutlineInkFor(player, player.Character.MapDrawingColor, OutlineAlpha);
            if (ink == null)
            {
                return;
            }

            AddOutline(icon, player, ink.Value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"PartyIconTintPatch failed: {e}");
        }
    }

    private static void AddOutline(TextureRect icon, Player player, Color ink)
    {
        // Named and checked so a re-_Ready cannot stack duplicates on top of each other.
        if (icon.GetNodeOrNull<TextureRect>(OutlineNodeName) != null)
        {
            return;
        }

        var outlineTexture = LoadOutlineTexture(player);
        var source = PlayerTint.ChooseOutlineSource(outlineTexture != null);

        var outline = new TextureRect
        {
            Name = OutlineNodeName,
            Texture = outlineTexture ?? player.Character.IconTexture,
            ShowBehindParent = true,
            Modulate = ink,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        outline.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        // The shipped outline art is already dilated, so it wants a congruent rect. The fallback is the
        // plain icon, which has to be grown to peek out from behind.
        //
        // Grown via negative offsets rather than Scale: this runs inside _Ready, before layout, so Size is
        // not reliable yet and a pivot-based scale would grow from the corner. Anchors plus offsets are
        // resolved by the layout pass itself.
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

        icon.AddChild(outline);
        icon.MoveChild(outline, 0);
    }

    /// <summary>
    /// The character's <c>_outline.png</c>, or null when it has none — a modded character need not ship one,
    /// and asking the cache for a missing texture is not guaranteed to fail quietly.
    /// </summary>
    private static Texture2D? LoadOutlineTexture(Player player)
    {
        try
        {
            return player.Character.IconOutlineTexture;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
