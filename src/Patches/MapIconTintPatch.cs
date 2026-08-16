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
/// entirely in alpha. The scene draws it black at 75% alpha; an active outline takes the player's colour at
/// full opacity instead, and reverts to that vanilla black when nothing is tinted.
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

                // Swap the RGB, keep the scene's 75% alpha — that softness is what keeps the character head
                // readable against the parchment. Tracked rather than assigned, so the `tint` command moves
                // it live and `tint auto` puts the vanilla black back.
                // Adopts the game's own outline node: colour, thickness and the shader that makes the
                // colour a replacement rather than a multiply. It collapses back to the scene's own size
                // and colour while nothing is tinted.
                IconOutline.Track(
                    vote.node.GetNodeOrNull<TextureRect>("Outline"),
                    vote.node,
                    vote.player,
                    vote.player.Character.MapDrawingColor);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"VoteIconTintPatch failed: {e}");
        }
    }
}

/// <summary>
/// Tints and outlines the character marker that hops between nodes on the map.
/// </summary>
/// <remarks>
/// The base game only ever shows this in single-player (<c>_isEnabled = Players.Count == 1</c>), so it can
/// never collide with a duplicate in a real run — in co-op the vote icons replace it entirely. It is handled
/// anyway so the <c>tint</c> console command's preview covers the map, outline included: this marker is the
/// only per-player map icon that exists solo, so without it there is no way to see the outline work without
/// a second player. In ordinary solo play no variation is active, so nothing here changes.
///
/// Unlike the vote icons this uses a different art family — <c>map_marker_&lt;id&gt;.png</c>, with no outline
/// counterpart — so the outline is built from the marker art itself, grown. The marker is not square and its
/// node keeps aspect, which is why <see cref="IconOutline" /> copies the parent's stretch settings.
/// </remarks>
[HarmonyPatch(typeof(NMapMarker), nameof(NMapMarker.Initialize))]
public static class MapMarkerTintPatch
{
    private static WeakReference<NMapMarker>? _marker;
    private static Player? _player;

    [HarmonyPostfix]
    public static void Postfix(NMapMarker __instance, Player player)
    {
        _marker = new WeakReference<NMapMarker>(__instance);
        _player = player;
        Apply();
    }

    /// <summary>
    /// Re-applies art and colour to the live marker. Returns false when there is no marker on screen —
    /// which is the normal case outside the map screen, and in co-op, where the marker never shows.
    /// </summary>
    public static bool Apply()
    {
        try
        {
            if (_marker == null || !_marker.TryGetTarget(out var marker)
                || !GodotObject.IsInstanceValid(marker) || _player == null)
            {
                return false;
            }

            // `tint icon character` swaps the marker's own art for the head icon the multiplayer vote
            // pins use, so the co-op look can be judged without a second player. The shipped outline
            // silhouette only exists for that head art; the marker art has none, so it grows its own.
            var useCharacter = PlayerTint.UseCharacterIconOnMap;
            marker.Texture = useCharacter ? _player.Character.IconTexture : _player.Character.MapMarker;

            PlayerTint.ApplySelf(marker, _player);

            IconOutline.Attach(
                marker,
                _player,
                outlineTexture: useCharacter ? CharacterArt.IconOutlineOrNull(_player) : null,
                fallbackTexture: marker.Texture,
                _player.Character.MapDrawingColor);

            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MapMarkerTintPatch failed: {e}");
            return false;
        }
    }
}
