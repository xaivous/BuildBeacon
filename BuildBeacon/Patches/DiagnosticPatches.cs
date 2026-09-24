using System;
using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// WearNTear destroys pieces silently. These patches log why a beacon takes damage or dies, with the call
    /// stack, so a "it breaks right after placement" report can be read straight from the BepInEx log.
    /// They only fire for objects carrying a BeaconController, only log, and only with VerboseLogging on (a stack trace
    /// per hit is costly and means nothing to players).
    /// </summary>
    internal static class DiagnosticPatches
    {
        private static bool IsBeacon(WearNTear wnt) => wnt != null && wnt.GetComponent<BeaconController>() != null;

        private static bool Verbose => BuildBeaconPlugin.Cfg != null && BuildBeaconPlugin.Cfg.VerboseLogging.Value;

        private static string Describe(HitData hit) =>
            hit == null
                ? "no HitData (wear tick, support or environment damage)"
                : $"HitData total {hit.GetTotalDamage():0.#} (blunt {hit.m_damage.m_blunt:0.#}, pickaxe {hit.m_damage.m_pickaxe:0.#}, fire {hit.m_damage.m_fire:0.#}), toolTier {hit.m_toolTier}, attacker {hit.m_attacker}";

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
        internal static class WearNTear_ApplyDamage
        {
            static void Prefix(WearNTear __instance, float damage, HitData hitData)
            {
                if (!Verbose || !IsBeacon(__instance)) return;
                float health = __instance.m_nview != null && __instance.m_nview.IsValid()
                    ? __instance.m_nview.GetZDO().GetFloat(ZDOVars.s_health, __instance.m_health)
                    : float.NaN;
                BuildBeaconPlugin.Log.LogWarning(
                    $"[diag] beacon ApplyDamage {damage:0.##} (health {health:0.#}/{__instance.m_health:0.#}); {Describe(hitData)}\n{Environment.StackTrace}");
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
        internal static class WearNTear_Destroy
        {
            static void Prefix(WearNTear __instance, HitData hitData, bool blockDrop)
            {
                if (!Verbose || !IsBeacon(__instance)) return;
                BuildBeaconPlugin.Log.LogWarning(
                    $"[diag] beacon Destroy (blockDrop {blockDrop}); {Describe(hitData)}\n{Environment.StackTrace}");
            }
        }

        /// <summary>
        /// Verbose logging: how long the local player's inventory change handling takes, vanilla work included, when it takes
        /// more than <see cref="SlowInventoryMs"/>. It re-checks every unknown piece's requirements, which our discount
        /// swap once made take seconds inside a beacon's radius.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnInventoryChanged))]
        internal static class Player_OnInventoryChanged
        {
            private const double SlowInventoryMs = 2.0;

            static void Prefix(out long __state) => __state = System.Diagnostics.Stopwatch.GetTimestamp();

            static void Postfix(Player __instance, long __state)
            {
                if (__instance != Player.m_localPlayer || !BuildBeaconPlugin.Cfg.VerboseLogging.Value) return;
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - __state) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (ms > SlowInventoryMs)
                    BuildBeaconPlugin.Log.LogInfo($"[diag] inventory change took {ms:0.#} ms (vanilla included)");
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
        internal static class WearNTear_Remove
        {
            static void Prefix(WearNTear __instance, bool blockDrop)
            {
                if (!IsBeacon(__instance)) return;
                BuildBeaconPlugin.Log.LogWarning($"[diag] beacon Remove (blockDrop {blockDrop})\n{Environment.StackTrace}");
            }
        }
    }
}
