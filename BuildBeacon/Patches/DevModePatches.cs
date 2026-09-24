using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// Dev mode (config DevMode, client-local): when the local player spawns in a world this machine hosts (single
    /// player, or a local world hosted for others), turn on dev commands, god mode and debug fly, so testing starts
    /// ready to go. Never on a dedicated server or when joining someone else's world.
    ///
    /// Valheim 1.0 facts this relies on (checked with sigdump):
    /// - Player.OnSpawned(bool) is void, so a postfix is safe.
    /// - The devcommands command only flips the static Terminal.m_cheat (plus a remote call when not the server, and
    ///   a command list refresh), so running it through the console is safe when m_cheat is off.
    /// - Player.SetGodMode(bool) is a plain setter; debug fly only has ToggleDebugFly, so check InDebugFlyMode first.
    /// </summary>
    internal static class DevModePatches
    {
        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        internal static class Player_OnSpawned
        {
            static void Postfix(Player __instance)
            {
                if (!BuildBeaconPlugin.Cfg.DevMode.Value || __instance != Player.m_localPlayer) return;

                var znet = ZNet.instance;
                if (znet == null || !znet.IsServer() || znet.IsDedicated())
                {
                    BuildBeaconPlugin.Log.LogInfo("[dev] Dev mode is on, but this is not a local world; leaving cheats off");
                    return;
                }

                if (!Terminal.m_cheat)
                {
                    if (Console.instance != null) Console.instance.TryRunCommand("devcommands", silentFail: true, skipAllowedCheck: true);
                    else Terminal.m_cheat = true;
                }
                if (!__instance.InGodMode()) __instance.SetGodMode(true);
                if (!__instance.InDebugFlyMode()) __instance.ToggleDebugFly();

                BuildBeaconPlugin.Log.LogInfo(
                    $"[dev] Local world{(ZNet.IsOpenServer() ? " (open to others)" : "")}: devcommands {Terminal.m_cheat}, " +
                    $"god {__instance.InGodMode()}, fly {__instance.InDebugFlyMode()}");
            }
        }
    }
}
