using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints the Regent's Sovereign Blade — the sword that orbits its owner after being forged.
/// </summary>
/// <remarks>
/// The odd one out among companions: unlike Osty, Byrdpip and Pael's Legion this is not a <c>Creature</c> at
/// all. <c>ForgeCmd.PlayCombatRoomForgeVfx</c> parents an <c>NSovereignBladeVfx</c> straight onto the
/// player's own <c>NCreature</c>, as a sibling of the visuals, so the creature-spawn hook in
/// <see cref="CombatBodyTintPatch" /> can never see it — and it appears on forge, long after the creature
/// exists, so a one-shot tint at spawn would miss it regardless. Hence its own patch.
///
/// Tints <c>_spineNode</c> (the <c>"SpineSword"</c> node) rather than the root: the blade art, its glow,
/// hilt and attached particles all hang off that, while the root's other children are the hitbox, the
/// selection reticle and the swing trail — interaction and animation, which stay untouched. Nothing in the
/// game writes <c>_spineNode.Modulate</c>, so there is nothing to conflict with.
///
/// <c>_Ready</c> assigns <c>_spineNode</c> first and <c>_owner = Card.Owner</c> as its very last statement,
/// so a postfix is guaranteed to see both populated.
/// </remarks>
[HarmonyPatch(typeof(NSovereignBladeVfx), nameof(NSovereignBladeVfx._Ready))]
public static class SovereignBladeTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NSovereignBladeVfx __instance)
    {
        try
        {
            PlayerTint.Apply(__instance._spineNode, __instance._owner);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"SovereignBladeTintPatch failed: {e}");
        }
    }
}
