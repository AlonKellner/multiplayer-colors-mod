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

            // SelfModulate so the head's tint does not multiply into the outline attached below.
            PlayerTint.ApplySelf(icon, player);

            IconOutline.Attach(
                icon,
                player,
                CharacterArt.IconOutlineOrNull(player),
                player.Character.IconTexture,
                player.Character.MapDrawingColor);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"PartyIconTintPatch failed: {e}");
        }
    }
}
