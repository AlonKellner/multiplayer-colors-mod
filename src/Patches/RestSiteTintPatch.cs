using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints a player's figure at the rest site, and their companion sitting beside them.
/// </summary>
/// <remarks>
/// <c>NRestSiteCharacter.Create</c> is the one place a rest-site figure is built, and it already carries the
/// <see cref="Player" />, so this is the natural gate. Scene children exist as soon as the scene is
/// instantiated, so they are all reachable here, before <c>_Ready</c>.
///
/// Tints the <c>SpineSprite</c> children rather than the node itself. The rest-site scenes are flat — in
/// <c>necrobinder_rest_site.tscn</c> the player (<c>Necro</c>) and their companion (<c>Osty</c>) are
/// siblings — so this still catches the companion, and the fire sprites parented under those spine nodes.
/// What it no longer catches is the mechanical UI that also lives under that root: the selection reticle,
/// the thought-bubble showing the chosen rest option, and the multiplayer confirmation icon.
///
/// Matches the filter <c>NRestSiteCharacter.GetChildSpineNodes</c> uses, but inlined rather than calling
/// that private compiler-generated iterator. It tests <c>GetClass()</c>, not the node name, which matters:
/// <c>regent_rest_site.tscn</c> names its node <c>SpineSprite2</c>.
///
/// <c>NRestSiteRoom.ExtinguishFireIfAble</c> assigns <c>Colors.DarkGray</c> to the *containers* when the
/// fire goes out; those are ancestors, so Godot's modulate multiplies it down over our tint.
/// </remarks>
[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter.Create))]
public static class RestSiteTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NRestSiteCharacter __result, Player player)
    {
        try
        {
            foreach (var child in __result.GetChildren().OfType<Node2D>())
            {
                if (child.GetClass() == "SpineSprite")
                {
                    PlayerTint.Apply(child, player);
                }
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"RestSiteTintPatch failed: {e}");
        }
    }
}
