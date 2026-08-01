using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace MultiplayerColors.Patches;

/// <summary>
/// Shifts the colour of a player's map ping, which uses the same per-character
/// <c>MapDrawingColor</c> as the map ink.
/// </summary>
/// <remarks>
/// <c>PingMapCoord</c> creates the VFX node, colours it, and parents it, all in locals — a postfix has no
/// handle on it, and fishing it back out of the map point's children would depend on <c>AddChildSafely</c>
/// not having deferred. So instead we capture the node as <c>NMapPingVfx.Create</c> returns it, and recolour
/// it once <c>PingMapCoord</c> has finished assigning the untinted colour.
///
/// The capture is a plain static: <c>PingMapCoord</c> runs on the Godot main thread and creates exactly one
/// ping, so the prefix/postfix pair always brackets a single <c>Create</c> call.
/// </remarks>
[HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.PingMapCoord))]
public static class MapPingTintPatch
{
    private static Player? _pendingPlayer;
    private static NMapPingVfx? _created;

    [HarmonyPrefix]
    public static void Prefix(Player player)
    {
        _pendingPlayer = player;
        _created = null;
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (_created != null && _pendingPlayer != null)
            {
                _created.Modulate = PlayerTint.Apply(_created.Modulate, _pendingPlayer);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MapPingTintPatch failed: {e}");
        }
        finally
        {
            _pendingPlayer = null;
            _created = null;
        }
    }

    /// <summary>Records the ping node created inside the <c>PingMapCoord</c> call currently in flight.</summary>
    internal static void Capture(NMapPingVfx? vfx)
    {
        if (_pendingPlayer != null)
        {
            _created = vfx;
        }
    }
}

[HarmonyPatch(typeof(NMapPingVfx), nameof(NMapPingVfx.Create))]
public static class MapPingCapturePatch
{
    [HarmonyPostfix]
    public static void Postfix(NMapPingVfx? __result) => MapPingTintPatch.Capture(__result);
}
