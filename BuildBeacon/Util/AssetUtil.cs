using System.IO;
using System.Reflection;
using Jotunn.Utils;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>Load an AssetBundle shipped next to the plugin DLL (Assets/&lt;name&gt;).</summary>
    public static class AssetUtil
    {
        public static AssetBundle LoadBundle(Assembly asm, string bundleName)
        {
            var dir = Path.GetDirectoryName(asm.Location);
            var path = Path.Combine(dir, "Assets", bundleName);
            if (File.Exists(path)) return AssetBundle.LoadFromFile(path);
            // Fallback: embedded resource
            return AssetUtils.LoadAssetBundleFromResources(bundleName, asm);
        }
    }
}
