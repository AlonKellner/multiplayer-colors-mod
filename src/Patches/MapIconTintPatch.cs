using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints the little character head icons that mark where each player has voted — on map nodes, and also on
/// the treasure-room skip vote and event votes, which use the same container.
/// </summary>
/// <remarks>
/// The icon is <c>player.Character.IconTexture</c>, so two players on the same character currently place
/// indistinguishable heads on the map. This is the icon you actually see in co-op: <c>NMapMarker</c>, the
/// other candidate, is hard-gated to single-player.
///
/// Tinting the icon root also carries to its "Outline" child, which is the same character art in silhouette
/// — they should move together.
///
/// Safe to run on every refresh: <see cref="PlayerTint.Apply" /> remembers each node's original modulate and
/// recomputes from it, so re-tinting an icon that survived the refresh is a no-op rather than a compounding
/// multiply. The fade tweens here animate <c>modulate:a</c> only, and Apply leaves alpha alone.
/// </remarks>
[HarmonyPatch(typeof(NMultiplayerVoteContainer), nameof(NMultiplayerVoteContainer.RefreshPlayerVotes))]
public static class VoteIconTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMultiplayerVoteContainer __instance)
    {
        try
        {
            foreach (var vote in __instance._votes)
            {
                PlayerTint.Apply(vote.node, vote.player);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"VoteIconTintPatch failed: {e}");
        }
    }
}

/// <summary>
/// Tints the character marker that hops between nodes on the map.
/// </summary>
/// <remarks>
/// The base game only ever shows this in single-player (<c>_isEnabled = Players.Count == 1</c>), so it can
/// never collide with a duplicate in a real run. It is tinted anyway so that the <c>tint</c> console
/// command's preview covers the map as well as the character art — otherwise the one place you can force a
/// variation solo is the one place you could not see it.
/// </remarks>
[HarmonyPatch(typeof(NMapMarker), nameof(NMapMarker.Initialize))]
public static class MapMarkerTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMapMarker __instance, Player player)
    {
        try
        {
            PlayerTint.Apply(__instance, player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MapMarkerTintPatch failed: {e}");
        }
    }
}
