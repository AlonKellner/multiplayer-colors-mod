using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints the arm sprites in the treasure room, including the rock-paper-scissors relic fight.
/// </summary>
/// <remarks>
/// Tints the inner <c>TextureRect</c> rather than the <c>NHandImage</c> itself, because <c>_Ready</c>
/// assigns <c>Modulate = (0.5, 0.5, 0.5, 0.5)</c> to the whole node for remote players — tinting the child
/// composes with that instead of replacing it.
///
/// This survives <c>DoFightMove</c> swapping the texture between the pointing / rock / paper / scissors
/// arms, since <c>Modulate</c> is independent of <c>Texture</c>.
///
/// The aura's frame does not: it is measured once, from wherever the first arm texture lands inside the
/// 383x1072 rect this scene gives it (which is letterboxed — see <see cref="AuraBounds.DrawnRect" />).
/// The four arms are the same shape at the same scale, so one measurement covers all of them.
/// </remarks>
[HarmonyPatch(typeof(NHandImage), nameof(NHandImage._Ready))]
public static class TreasureHandTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NHandImage __instance)
    {
        try
        {
            PlayerTint.Apply(__instance._textureRect, __instance.Player);
            AuraLayer.Attach(__instance._textureRect, __instance.Player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"TreasureHandTintPatch failed: {e}");
        }
    }
}
