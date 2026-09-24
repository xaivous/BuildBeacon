using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>Hooks for the rounding savings (DiscountSavings) and their HUD buffs (DiscountBuffs).</summary>
    internal static class SavingsPatches
    {
        /// <summary>
        /// PlacePiece runs inside UpdatePlacement once the placement succeeded, while the discounted array (and its
        /// savings plan) is still swapped in and before ConsumeResources charges it. Commit the plan then, cheated or
        /// not: on 1.0 the no-cost cheat (the nocost command, or B with cheats on) only skips the HaveRequirements check;
        /// UpdatePlacement still calls ConsumeResources, which takes what the player carries.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        internal static class Player_PlacePiece
        {
            static void Postfix(Player __instance, Piece piece, bool cheated)
            {
                if (__instance != Player.m_localPlayer) return;
                var plan = RequirementSwap.PlanFor(piece);
                if (BuildBeaconPlugin.Cfg.DevMode.Value)
                {
                    var pos = RequirementSwap.GhostPos(__instance);
                    BuildBeaconPlugin.Log.LogInfo(
                        $"[diag] placed {(piece != null ? piece.name : "?")}: cheated {cheated}, swapped {RequirementSwap.IsSwapped(piece)}, " +
                        $"paid [{string.Join(", ", (piece != null ? piece.m_resources : new Piece.Requirement[0]).Select(r => $"{(r.m_resItem != null ? r.m_resItem.name : "?")} {r.m_amount}"))}], " +
                        $"plan [{string.Join(", ", (plan ?? new List<DiscountSavings.Step>()).Select(s => $"{s.Material} {s.Percent:0.#}% bonus {s.Bonus} streak {s.Streak} -> {s.NewBalance:0.###}"))}]\n  " +
                        DiscountSavings.Describe(piece, RequirementSwap.OriginalOf(piece), pos));
                }
                DiscountSavings.Commit(plan);
            }
        }

        /// <summary>Adds the display-only savings buffs to the local player's HUD status list (and the compendium's
        /// active effects page, which reads the same list).</summary>
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.GetHUDStatusEffects))]
        internal static class SEMan_GetHUDStatusEffects
        {
            static void Postfix(SEMan __instance, List<StatusEffect> effects)
            {
                var player = Player.m_localPlayer;
                if (player == null || effects == null || __instance != player.GetSEMan()) return;
                DiscountBuffs.AddTo(effects, player);
            }
        }
    }
}
