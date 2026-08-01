using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MultiplayerColors.Patches;

/// <summary>
/// Shifts the colour of the targeting line drawn for a remote player in combat. Like the map ink, it comes
/// from a per-character colour (<c>RemoteTargetingLineColor</c> / <c>RemoteTargetingLineOutline</c>) and so
/// is identical for two players on the same character.
/// </summary>
/// <remarks>
/// Only the two <c>DefaultColor</c> values are shifted. <c>Initialize</c> also multiplies each gradient
/// stop by the unshifted colour; re-multiplying those in a postfix would compound the tint, and the
/// gradients are a brightness ramp rather than the line's identity colour, so leaving them alone keeps the
/// line's overall hue driven by <c>DefaultColor</c>.
/// </remarks>
[HarmonyPatch(typeof(NRemoteTargetingIndicator), nameof(NRemoteTargetingIndicator.Initialize))]
public static class RemoteTargetingTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRemoteTargetingIndicator __instance, Player player)
    {
        try
        {
            if (__instance._line != null)
            {
                __instance._line.DefaultColor = PlayerTint.Apply(__instance._line.DefaultColor, player);
            }

            if (__instance._lineBack != null)
            {
                __instance._lineBack.DefaultColor = PlayerTint.Apply(__instance._lineBack.DefaultColor, player);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"RemoteTargetingTintPatch failed: {e}");
        }
    }
}
