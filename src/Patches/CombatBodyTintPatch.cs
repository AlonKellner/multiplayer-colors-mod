using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace MultiplayerColors.Patches;

/// <summary>
/// Tints a player's body in combat, and their companions' — Osty, Byrdpip, Pael's Legion.
/// </summary>
/// <remarks>
/// A companion is a perfectly ordinary <c>Creature</c>: spawned via <c>PlayerCmd.AddPet&lt;T&gt;</c> through
/// the same <c>NCreature.Create</c> path as players and monsters, with its own <c>NCreatureVisuals</c>. It
/// is distinguished only by <c>PetOwner</c> being set (and <c>Player</c> being null), so resolving the owner
/// is all this needs — <c>Player ?? PetOwner</c> is the base game's own idiom for it.
///
/// Companions inherit their owner's variation rather than getting one of their own, which keeps a pet and
/// its player the same colour, and means a pet belonging to an untinted player stays untinted too. That
/// falls out of <see cref="PlayerTint.Apply" /> no-opping on a null variation; no extra branch needed.
///
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
            // owning NCreature.
            if (__instance.GetParent() is not NCreature creature)
            {
                return;
            }

            // Null for enemies and for enemy-summoned minions, which the base game already varies itself.
            var owner = creature.Entity.Player ?? creature.Entity.PetOwner;
            if (owner == null)
            {
                return;
            }

            PlayerTint.Apply(__instance.Body, owner);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"CombatBodyTintPatch failed: {e}");
        }
    }
}
