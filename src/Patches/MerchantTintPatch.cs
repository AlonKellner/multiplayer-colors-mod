using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints each player's figure in the shop.
/// </summary>
/// <remarks>
/// <c>NMerchantCharacter</c> keeps no reference to the player it was built for, so the pairing has to come
/// from <c>NMerchantRoom</c> itself. <c>AfterRoomIsLoaded</c> appends to <c>_playerVisuals</c> in exactly the
/// order it walks <c>_players</c> (index <c>i * num + j</c>, ascending), so <c>_playerVisuals[k]</c> is
/// always the figure for <c>_players[k]</c>.
///
/// Note <c>_players</c> is reordered local-player-first at the top of that method — which is precisely why
/// <see cref="PlayerTint.For" /> keys on the run's slot index rather than on this list's order. Pairing by
/// position here is fine; deriving the *colour* from it would not be.
///
/// The method also assigns <c>Modulate = (0.5, 0.5, 0.5)</c> to back-row figures. Running as a postfix and
/// multiplying into whatever is already there keeps that dimming intact.
/// </remarks>
[HarmonyPatch(typeof(NMerchantRoom), "AfterRoomIsLoaded")]
public static class MerchantTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMerchantRoom __instance)
    {
        try
        {
            var visuals = __instance.PlayerVisuals;
            var players = __instance._players;
            var count = Math.Min(visuals.Count, players.Count);
            for (var i = 0; i < count; i++)
            {
                PlayerTint.Apply(visuals[i], players[i]);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MerchantTintPatch failed: {e}");
        }
    }
}
