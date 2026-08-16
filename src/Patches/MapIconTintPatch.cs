using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
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
    private const string PreviewNodeName = "MultiplayerColorsVoteIconPreview";
    private const string PreviewBoxName = "MultiplayerColorsVoteIconBox";

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

            if (PlayerTint.UseCharacterIconOnMap)
            {
                ShowVoteIconPreview(marker, _player);
            }
            else
            {
                ShowMapPin(marker, _player);
            }

            return true;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"MapMarkerTintPatch failed: {e}");
            return false;
        }
    }

    /// <summary>
    /// The size a real vote icon renders at, read off a live vote container when the map is open.
    /// </summary>
    /// <remarks>
    /// Measured rather than hardcoded so it tracks the game: every map point currently gives its container a
    /// 32px height, and an HBoxContainer stretches its children to fill it, so the icon's own 24px minimum
    /// is never what it draws at. Sizing the preview to that minimum is what made it look too small.
    /// </remarks>
    private static float MeasureVoteIconSize()
    {
        try
        {
            var container = NMapScreen.Instance?._mapPointDictionary.Values
                .FirstOrDefault()?.VoteContainer;

            return PlayerTint.VoteIconPreviewSize(container?.Size.Y ?? 0f);
        }
        catch (Exception)
        {
            return PlayerTint.FallbackVoteIconSize;
        }
    }

    /// <summary>The normal solo pin: the character's map marker art, with an outline grown from it.</summary>
    private static void ShowMapPin(NMapMarker marker, Player player)
    {
        var box = marker.GetNodeOrNull<HBoxContainer>(PreviewBoxName);
        if (box != null)
        {
            box.Visible = false;
        }

        marker.Texture = player.Character.MapMarker;
        IconOutline.SetBuiltOutlineVisible(marker, true);

        PlayerTint.ApplySelf(marker, player);
        IconOutline.Attach(
            marker,
            player,
            outlineTexture: null,
            fallbackTexture: marker.Texture,
            player.Character.MapDrawingColor);
    }

    /// <summary>
    /// The co-op look, previewed solo — built from the real vote-icon scene rather than imitated.
    /// </summary>
    /// <remarks>
    /// Instantiating <c>ui/multiplayer_vote_icon</c> and assigning the same two textures is exactly what
    /// <c>NMultiplayerVoteContainer.RefreshPlayerVotes</c> does, and it then goes through the same
    /// <see cref="IconOutline.Track" /> call the real vote icons do. So the head, its shipped outline
    /// silhouette, the expand and stretch modes and the draw order are the scene's own, not a reproduction
    /// that could drift from it.
    ///
    /// What still differs is placement, and cannot be otherwise: in co-op these sit in a row beneath a map
    /// point, one per voting player, while this rides the single-player marker as it hops between nodes.
    /// The icon itself renders identically; where it sits does not.
    ///
    /// The marker's own art is cleared while this is up, and the outline built for it hidden, so only the
    /// vote icon draws.
    /// </remarks>
    private static void ShowVoteIconPreview(NMapMarker marker, Player player)
    {
        // Lay it out the way the game does rather than sizing it by hand. A real vote icon is never drawn at
        // its own minimum: an HBoxContainer stretches it to the container's height, and the icon's
        // proportional expand mode then matches its width. Reproducing that with explicit sizes is what kept
        // coming out wrong, so the preview goes in a box of the same height and Godot does the arithmetic.
        var box = marker.GetNodeOrNull<HBoxContainer>(PreviewBoxName);
        if (box == null)
        {
            // A plain HBoxContainer, not the scripted vote container: identical layout behaviour without
            // running script that expects Initialize to have been called.
            box = new HBoxContainer
            {
                Name = PreviewBoxName,
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            marker.AddChild(box);
        }

        // Anchored and offset explicitly rather than via a preset, whose resize behaviour differs between
        // the anchors-only and anchors-and-offsets calls.
        var height = MeasureVoteIconSize();
        var width = height * 4f;
        box.AnchorLeft = box.AnchorRight = box.AnchorTop = box.AnchorBottom = 0.5f;
        box.OffsetLeft = -width / 2f;
        box.OffsetRight = width / 2f;
        box.OffsetTop = -height / 2f;
        box.OffsetBottom = height / 2f;

        var preview = box.GetNodeOrNull<TextureRect>(PreviewNodeName);
        if (preview == null)
        {
            preview = SceneHelper.Instantiate<TextureRect>("ui/multiplayer_vote_icon");
            preview.Name = PreviewNodeName;
            box.AddChild(preview);
        }

        box.Visible = true;
        preview.Visible = true;
        marker.Texture = null;
        IconOutline.SetBuiltOutlineVisible(marker, false);

        // The same two assignments RefreshPlayerVotes makes.
        preview.Texture = player.Character.IconTexture;
        preview.GetNode<TextureRect>("Outline").Texture = player.Character.IconOutlineTexture;
        preview.PivotOffset = preview.Size * 0.5f;

        Diagnostics.Log(
            $"vote icon preview: box={box.Size} icon={preview.Size} "
            + $"measured={height}px for {player.Character?.Id}");

        // And the same tint path the real vote icons take.
        PlayerTint.ApplySelf(preview, player);
        IconOutline.Track(
            preview.GetNodeOrNull<TextureRect>("Outline"),
            preview,
            player,
            player.Character.MapDrawingColor);
    }
}
