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
/// side by side, and therefore where identical characters are hardest to tell apart.
/// </summary>
/// <remarks>
/// <c>_characterIcon</c> is fetched via <c>GetNode</c> inside <c>_Ready</c>, so it is always present in a
/// postfix. Tinting the icon rather than the whole row leaves the health bar, nameplate and status
/// indicators reading normally.
/// </remarks>
[HarmonyPatch(typeof(NMultiplayerPlayerState), nameof(NMultiplayerPlayerState._Ready))]
public static class PartyIconTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMultiplayerPlayerState __instance)
    {
        try
        {
            PlayerTint.Apply(__instance._characterIcon, __instance.Player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"PartyIconTintPatch failed: {e}");
        }
    }
}
