using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints the little character head icons that mark where each player has voted — on map nodes, and also on
/// the treasure-room skip vote and event votes, which use the same container — and outlines each one in the
/// colour that player draws with, so a line on the map can be traced back to whoever drew it.
/// </summary>
/// <remarks>
/// The icon is <c>player.Character.IconTexture</c>, so two players on the same character currently place
/// indistinguishable heads on the map. This is the icon you actually see in co-op: <c>NMapMarker</c>, the
/// other candidate, is hard-gated to single-player.
///
/// The scene is two nodes: the head, and an "Outline" child drawn behind it via <c>show_behind_parent</c>.
/// That outline art is the icon's own silhouette dilated a few pixels, pure white with the shape carried
/// entirely in alpha — so a single <c>Modulate</c> recolours an exact, shape-following outline with no
/// shader and no new art. The scene tints it black at 75% alpha; we swap the colour and keep the alpha.
///
/// This one scene is instanced by nine hosts — the three map point types, the treasure-room skip vote, both
/// relic holders, both event option buttons, and the combat end-turn button — so the key appears on all of
/// them at once.
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
                // SelfModulate, not Modulate: the head must not tint its own Outline child, which we are
                // about to set to the exact colour that player draws with.
                PlayerTint.ApplySelf(vote.node, vote.player);

                var outline = vote.node.GetNodeOrNull<TextureRect>("Outline");
                if (outline == null)
                {
                    continue;
                }

                // Swap the RGB, keep the scene's 75% alpha — that softness is what keeps the character head
                // readable against the parchment. Left untouched (vanilla black) when there is no variation.
                var ink = PlayerTint.OutlineInkFor(vote.player, vote.player.Character.MapDrawingColor, outline.Modulate.A);
                if (ink != null)
                {
                    outline.Modulate = ink.Value;
                }
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
