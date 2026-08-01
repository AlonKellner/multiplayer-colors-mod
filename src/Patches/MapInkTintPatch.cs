using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace MultiplayerColors.Patches;

/// <summary>
/// Shifts the colour a player draws on the map with.
/// </summary>
/// <remarks>
/// <c>CreateLineForPlayer</c> sets <c>DefaultColor = player.Character.MapDrawingColor</c> — a per-*character*
/// colour, so two players on the same character currently draw in literally identical ink. This is the most
/// clear-cut duplicate collision in the game.
///
/// Flat colours get the HSV form of the variation (<see cref="PlayerTint.Shift" />) rather than a multiply,
/// which reads much better on a single solid colour.
/// </remarks>
[HarmonyPatch(typeof(NMapDrawings), "CreateLineForPlayer")]
public static class MapInkTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(Line2D __result, Player player)
    {
        try
        {
            if (__result != null)
            {
                __result.DefaultColor = PlayerTint.ApplyToMapInk(__result.DefaultColor, player);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MapInkTintPatch failed: {e}");
        }
    }
}
