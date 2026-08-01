using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints a player's figure at the rest site.
/// </summary>
/// <remarks>
/// <c>NRestSiteCharacter.Create</c> is the one place a rest-site figure is built, and it already carries the
/// <see cref="Player" />, so this is the natural gate. We tint the returned node itself rather than its
/// container: <c>NRestSiteRoom.ExtinguishFireIfAble</c> assigns <c>Colors.DarkGray</c> to the *containers*
/// when the fire goes out, and Godot's modulate multiplies down the tree, so the two compose.
/// </remarks>
[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.Create))]
public static class RestSiteTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRestSiteCharacter __result, Player player)
    {
        try
        {
            PlayerTint.Apply(__result, player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"RestSiteTintPatch failed: {e}");
        }
    }
}
