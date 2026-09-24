using BepInEx.Configuration;
using Jotunn.Configs;

namespace BuildBeacon
{
    /// <summary>
    /// Thin wrapper so every mod declares server-synced config the same way.
    /// Entries marked admin-only are pushed from server to clients by Jötunn's SynchronizationManager.
    /// </summary>
    public static class ConfigUtil
    {
        public static ConfigEntry<T> Synced<T>(ConfigFile cfg, string section, string key, T def, string desc,
            AcceptableValueBase range = null)
        {
            return cfg.Bind(section, key, def,
                new ConfigDescription(desc, range, new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }

        public static ConfigEntry<T> Local<T>(ConfigFile cfg, string section, string key, T def, string desc,
            AcceptableValueBase range = null)
        {
            return cfg.Bind(section, key, def,
                new ConfigDescription(desc, range, new ConfigurationManagerAttributes { IsAdminOnly = false }));
        }
    }
}
