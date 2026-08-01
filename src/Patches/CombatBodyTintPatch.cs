using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints a player's body in combat.
/// </summary>
/// <remarks>
/// Hooked on <c>NCreatureVisuals._Ready</c> rather than <c>NCreature._Ready</c> for two reasons:
///
/// 1. <c>_body</c> is assigned inside <c>NCreatureVisuals._Ready</c>. <c>NCreature._Ready</c> adds the
///    visuals via <c>AddChildSafely</c>, which falls back to <c>CallDeferred</c>, so from a postfix there
///    the body node is not reliably available yet. Here it always is.
/// 2. We must tint the body, not the visuals root: <c>NCombatRoom.PositionPlayersAndPets</c> *assigns*
///    <c>Visuals.Modulate = (0.5, 0.5, 0.5)</c> for back-row players later on, which would wipe a tint put
///    on the visuals root. Tinting the child composes with that instead of fighting it.
///
/// Tinting the body also deliberately avoids the Spine *material* slot, which
/// <c>NCreatureVisuals.SetScaleAndHue</c> and <c>ApplyLiquidOverlayInternal</c> (potion splashes)
/// save and restore between them.
/// </remarks>
[HarmonyPatch(typeof(NCreatureVisuals), nameof(NCreatureVisuals._Ready))]
public static class CombatBodyTintPatch
{
    [HarmonyPostfix]
    public static void Postfix(NCreatureVisuals __instance)
    {
        try
        {
            // NCreatureVisuals is also instantiated standalone (bestiary, previews) where there is no
            // owning NCreature, and monsters get their variation from the base game already.
            if (__instance.GetParent() is not NCreature creature || !creature.Entity.IsPlayer)
            {
                return;
            }

            PlayerTint.Apply(__instance.Body, creature.Entity.Player);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"CombatBodyTintPatch failed: {e}");
        }
    }
}
