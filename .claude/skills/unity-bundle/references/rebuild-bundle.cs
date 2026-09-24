// Pass this as the Code argument of the Unity_RunCommand MCP tool.
// It saves pending asset edits, prints the beacon materials' colours so the values that went into the bundle are
// visible in the tool output, builds every labelled bundle for Windows x64 into Assets/Output, and prints sizes.
// The class must be named CommandScript and be internal; the tool wraps it in its own namespace.
using System.IO;
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        AssetDatabase.SaveAssets();

        foreach (var path in new[]
        {
            "Assets/Beacon/BeaconStoneLight.mat",
            "Assets/Beacon/BeaconStoneDark.mat",
            "Assets/Beacon/BeaconBandDark.mat",
            "Assets/Beacon/BeaconRune.mat",
            "Assets/Beacon/BeaconCrystal.mat",
        })
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { result.LogWarning("Missing " + path); continue; }
            result.Log(Path.GetFileNameWithoutExtension(path)
                + " albedo " + mat.GetColor("_Color").ToString()
                + " emission " + mat.GetColor("_EmissionColor").ToString()
                + " _EMISSION=" + mat.IsKeywordEnabled("_EMISSION"));
        }

        var outDir = "Assets/Output";
        Directory.CreateDirectory(outDir);
        var manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        if (manifest == null) { result.LogError("Asset bundle build failed"); return; }

        foreach (var name in manifest.GetAllAssetBundles())
            result.Log("Built bundle " + name + ": " + new FileInfo(Path.Combine(outDir, name)).Length + " bytes, assets: "
                + string.Join(", ", AssetDatabase.GetAssetPathsFromAssetBundle(name)));
    }
}
