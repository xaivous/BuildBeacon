using System.Linq;
using System.Text;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// Console helpers for matching the beacon's look to vanilla. Open the console (F5) and run:
    ///   beacon_dump [prefab]      renderers, materials, shaders and textures of a prefab (default: the beacon)
    ///   beacon_materials <text>   vanilla materials whose name contains the text, for JVLmock_ naming
    /// Output goes to the console and to the BepInEx log so it can be copied from the file.
    /// </summary>
    internal static class DebugCommands
    {
        public static void Register()
        {
            CommandManager.Instance.AddConsoleCommand(new DumpPrefabCommand());
            CommandManager.Instance.AddConsoleCommand(new FindMaterialsCommand());
            CommandManager.Instance.AddConsoleCommand(new FindPrefabsCommand());
            CommandManager.Instance.AddConsoleCommand(new TreeCommand());
            CommandManager.Instance.AddConsoleCommand(new HuginCommand());
            CommandManager.Instance.AddConsoleCommand(new CostCommand());
        }

        /// <summary>beacon_cost: what the discount code sees for the selected piece at the placement ghost.</summary>
        private sealed class CostCommand : ConsoleCommand
        {
            public override string Name => "beacon_cost";
            public override string Help => "beacon_cost - beacons covering the placement ghost and the selected piece's costs: base, discounted, with rounding savings, balances";

            public override void Run(string[] args)
            {
                var player = Player.m_localPlayer;
                if (player == null) { Console.instance.Print("No local player"); return; }
                var piece = player.InPlaceMode() ? player.GetSelectedPiece() : null;
                var pos = player.m_placementGhost != null ? player.m_placementGhost.transform.position : player.transform.position;
                var source = piece != null ? Patches.RequirementSwap.OriginalOf(piece) : null;
                var text = DiscountSavings.Describe(piece, source, pos);
                if (source != null)
                {
                    // What one discount calculation costs here; the build code runs it a few times a frame.
                    const int runs = 200;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < runs; i++) BeaconRegistry.GetEffectiveRequirements(source, pos);
                    sw.Stop();
                    text += $"\n  timing: {sw.Elapsed.TotalMilliseconds * 1000.0 / runs:0.#} us per discount calculation ({runs} runs)";
                }
                foreach (var line in text.Split('\n')) Console.instance.Print(line);
                BuildBeaconPlugin.Log.LogInfo("[diag] beacon_cost " + text);
            }
        }

        /// <summary>beacon_hugin: forget that this character saw the beacon tutorial and show it again.</summary>
        private sealed class HuginCommand : ConsoleCommand
        {
            public override string Name => "beacon_hugin";
            public override string Help => "beacon_hugin - show Hugin's Build Beacon message again (clears it from this character's seen list)";

            public override void Run(string[] args)
            {
                var player = Player.m_localPlayer;
                if (player == null) { Console.instance.Print("No local player"); return; }
                player.m_shownTutorials.Remove(Patches.TutorialPatches.Key);
                Patches.TutorialPatches.Show(player);
                Console.instance.Print("Hugin is on the way");
            }
        }

        /// <summary>beacon_tree [prefab]: the child hierarchy with components and local positions, for finding effect and light objects.</summary>
        private sealed class TreeCommand : ConsoleCommand
        {
            public override string Name => "beacon_tree";
            public override string Help => "beacon_tree [prefab] - print a prefab's child hierarchy with component types and local positions (default: xai_build_beacon)";

            public override void Run(string[] args)
            {
                var name = args.Length > 0 ? args[0] : BuildBeaconPlugin.PiecePrefab;
                var prefab = PrefabManager.Instance.GetPrefab(name);
                if (prefab == null) { Out($"Prefab '{name}' not found"); return; }
                Out($"=== {name}");
                Walk(prefab.transform, 0);
            }

            private static void Walk(Transform t, int depth)
            {
                var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c =>
                {
                    if (c is Light l) return $"Light({l.type} {l.color} r{l.range:0.#} i{l.intensity:0.#})";
                    if (c is ParticleSystem ps) return $"ParticleSystem(max {ps.main.maxParticles})";
                    return c.GetType().Name;
                });
                var p = t.localPosition;
                Out($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " (inactive)")}  @({p.x:0.##}, {p.y:0.##}, {p.z:0.##})  [{string.Join(", ", comps)}]");
                for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
            }
        }

        /// <summary>beacon_prefabs &lt;text&gt;: registered prefabs whose name contains the text, with the materials on their renderers.</summary>
        private sealed class FindPrefabsCommand : ConsoleCommand
        {
            public override string Name => "beacon_prefabs";
            public override string Help => "beacon_prefabs <text> - list registered prefabs whose name contains the text, with their renderer materials and shaders";

            public override void Run(string[] args)
            {
                if (args.Length == 0) { Out("Usage: beacon_prefabs <text>"); return; }
                if (ZNetScene.instance == null) { Out("Not in a world yet; prefabs are registered when a world loads"); return; }

                var needle = string.Join(" ", args);
                var hits = ZNetScene.instance.m_prefabs
                    .Where(p => p != null && p.name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(p => p.name)
                    .ToList();

                Out($"=== {hits.Count} prefabs matching \"{needle}\"" + (hits.Count > 50 ? " (showing 50)" : ""));
                foreach (var p in hits.Take(50))
                {
                    var mats = p.GetComponentsInChildren<Renderer>(true)
                        .SelectMany(r => r.sharedMaterials)
                        .Where(m => m != null)
                        .Select(m => $"\"{m.name}\" ({(m.shader != null ? m.shader.name : "null")})")
                        .Distinct()
                        .ToList();
                    var piece = p.GetComponent<Piece>();
                    Out($"  {p.name}" + (piece != null ? $"  piece {Localization.instance.Localize(piece.m_name)}" : "") +
                        (mats.Count > 0 ? $"  materials: {string.Join(", ", mats)}" : "  (no renderers)"));
                }
                Out("Use prefab:<name> in MaterialTemplates to borrow a prefab's material, or beacon_dump <name> for its full detail.");
            }
        }

        private static void Out(string line)
        {
            BuildBeaconPlugin.Log.LogInfo(line);
            Console.instance?.Print(line);
        }

        private sealed class DumpPrefabCommand : ConsoleCommand
        {
            public override string Name => "beacon_dump";
            public override string Help => "beacon_dump [prefab] - list renderers, materials, shaders and textures of a prefab (default: xai_build_beacon)";

            public override void Run(string[] args)
            {
                var name = args.Length > 0 ? args[0] : BuildBeaconPlugin.PiecePrefab;
                var prefab = PrefabManager.Instance.GetPrefab(name);
                if (prefab == null) { Out($"Prefab '{name}' not found"); return; }

                Out($"=== {name}");
                var shadersShown = new System.Collections.Generic.HashSet<string>();

                var wnt = prefab.GetComponent<WearNTear>();
                if (wnt != null)
                    Out($"WearNTear: health {wnt.m_health}  material {wnt.m_materialType}  requiredBiome {wnt.m_requiredBiome}  outsideBiomeDamage {wnt.m_outsideRequiredBiomeDamage}  " +
                        $"noRoofWear {wnt.m_noRoofWear}  noSupportWear {wnt.m_noSupportWear}  supports {wnt.m_supports}  staticPosition {wnt.m_staticPosition}  " +
                        $"burnable {wnt.m_burnable}  snowImmune {wnt.m_snowDamageImmune}  ashImmune {wnt.m_ashDamageImmune}  event \"{wnt.m_requiredPersistentEvent}\"");
                var piece = prefab.GetComponent<Piece>();
                if (piece != null)
                    Out($"Piece: name {piece.m_name}  category {piece.m_category}  usage {piece.m_usage}  groundPiece {piece.m_groundPiece}  clipGround {piece.m_clipGround}  " +
                        $"noInWater {piece.m_noInWater}  onlyInBiome {piece.m_onlyInBiome}  layer {LayerMask.LayerToName(prefab.layer)}");

                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Out($"{Path(prefab.transform, r.transform)}  [{r.GetType().Name}]");
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) { Out("    (null material)"); continue; }
                        Out($"    material \"{m.name}\"  shader \"{(m.shader != null ? m.shader.name : "null")}\"  keywords [{string.Join(" ", m.shaderKeywords)}]");
                        if (m.shader == null) continue;
                        if (shadersShown.Add(m.shader.name))
                            Out($"    shader \"{m.shader.name}\" declares keywords: {string.Join(" ", m.shader.keywordSpace.keywordNames)}");

                        // Every property the shader declares, with its current value on this material.
                        int count = m.shader.GetPropertyCount();
                        for (int i = 0; i < count; i++)
                        {
                            var prop = m.shader.GetPropertyName(i);
                            switch (m.shader.GetPropertyType(i))
                            {
                                case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                    var tex = m.GetTexture(prop);
                                    Out($"        {prop} (tex) = " + (tex != null ? $"\"{tex.name}\" {tex.width}x{tex.height}" : "none"));
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Color:
                                    Out($"        {prop} (color) = {m.GetColor(prop)}");
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                    Out($"        {prop} (vector) = {m.GetVector(prop)}");
                                    break;
                                case UnityEngine.Rendering.ShaderPropertyType.Int:
                                    Out($"        {prop} (int) = {m.GetInteger(prop)}");
                                    break;
                                default:
                                    Out($"        {prop} = {m.GetFloat(prop):0.###}");
                                    break;
                            }
                        }
                    }
                }
            }

            private static string Path(Transform root, Transform t)
            {
                var sb = new StringBuilder(t.name);
                for (var p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
                return sb.ToString();
            }
        }

        private sealed class FindMaterialsCommand : ConsoleCommand
        {
            public override string Name => "beacon_materials";
            public override string Help => "beacon_materials <text> - list loaded materials whose name contains the text, with their shader";

            public override void Run(string[] args)
            {
                if (args.Length == 0) { Out("Usage: beacon_materials <text>"); return; }
                var needle = string.Join(" ", args);
                var hits = Resources.FindObjectsOfTypeAll<Material>()
                    .Where(m => m != null && m.name.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .GroupBy(m => m.name).Select(g => g.First())
                    .OrderBy(m => m.name)
                    .ToList();

                Out($"=== {hits.Count} materials matching \"{needle}\"" + (hits.Count > 100 ? " (showing 100)" : ""));
                foreach (var m in hits.Take(100))
                {
                    var main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    Out($"  \"{m.name}\"  shader \"{(m.shader != null ? m.shader.name : "null")}\"" + (main != null ? $"  _MainTex \"{main.name}\"" : ""));
                }
                Out("Use a material by naming your Unity material JVLmock_<name>.");
            }
        }
    }
}
