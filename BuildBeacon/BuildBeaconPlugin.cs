using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace BuildBeacon
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class BuildBeaconPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "xaivous.buildbeacon";
        /// <summary>The GUID up to 0.1.x. BepInEx names the config file after the GUID, so its settings are carried
        /// over once (MigrateConfigFile).</summary>
        private const string OldPluginGuid = "com.xaivous.buildbeacon";
        public const string PluginName = "BuildBeacon";
        public const string PluginVersion = "0.2.0";
        public const string PiecePrefab = "xai_build_beacon";
        public const string BundleName = "buildbeacon";              // embedded resource, built from BuildBeaconUnity/Assets/Beacon
        public const string AssetPrefab = "xai_build_beacon_prefab";  // the asset in the bundle; cloned to PiecePrefab at registration
        public const string IconAsset = "xai_beacon_icon";
        /// <summary>The two boss trophy holders: same behaviour, their own models. Piece IDs, then the bundle's prefab and
        /// icon asset for each.</summary>
        public const string HolderPrefabPillar = "xai_beacon_bossholder_pillar", HolderPrefabWall = "xai_beacon_bossholder_wall";
        private static readonly (string id, string name, string desc, string asset, string icon)[] Holders =
        {
            (HolderPrefabPillar, "$piece_xai_bossholder_pillar", "$piece_xai_bossholder_pillar_desc", "xai_bossholder_pillar_prefab", "xai_bossholder_pillar_icon_128"),
            (HolderPrefabWall, "$piece_xai_bossholder_wall", "$piece_xai_bossholder_wall_desc", "xai_bossholder_wall_prefab", "xai_bossholder_wall_icon_128"),
        };
        /// <summary>The two creature trophy racks: Piece IDs, texts, the bundle's prefab and icon, and whether trophies fit
        /// the alcove's face (the Trophy Panel's shallow niches) or follow TrophyPlacement (the Trophy Column).</summary>
        public const string RackPrefabColumn = "xai_beacon_mobrack_column", RackPrefabWall = "xai_beacon_mobrack_wall";
        private static readonly (string id, string name, string desc, string asset, string icon, bool fitToFace)[] Racks =
        {
            (RackPrefabColumn, "$piece_xai_mobrack_column", "$piece_xai_mobrack_column_desc", "xai_mobrack_column_prefab", "xai_mobrack_column_icon_128", false),
            (RackPrefabWall, "$piece_xai_mobrack_wall", "$piece_xai_mobrack_wall_desc", "xai_mobrack_wall_prefab", "xai_mobrack_wall_icon_128", true),
        };
        /// <summary>The holder and rack prefabs' extensions, so a changed HolderRange reaches pieces placed from now on.</summary>
        private static readonly List<StationExtension> HolderExtensions = new List<StationExtension>();
        private static float s_appliedHolderRange = -1f;
        public const string CrystalMaterial = "BeaconCrystal";       // the glow effect is centered on the mesh using this material
        public const string CrystalPivot = "crystal";                // the prefab child holding the crystal model; the glow goes under it and it moves while lit

        // The Great Beacon (docs/design/great-beacon.md): a second beacon on the same crafting station, its own alcoves for
        // boss trophies.
        public const string GreatPiecePrefab = "xai_great_beacon";
        public const string GreatAssetPrefab = "xai_great_beacon_prefab";
        public const string GreatIconAsset = "xai_great_beacon_icon_128";
        /// <summary>No beacon of either kind within this of another (vanilla Piece.m_blockRadius, "Need more space").</summary>
        public const float BeaconSpacing = 6f;
        private const float StoneTextureScale = 1f;                  // triplanar tiling of the borrowed stone texture, in world units
        public const string EffectTemplatePrefab = "stone_wall_2x1"; // vanilla piece whose place/hit/destroy sounds and particles the beacon uses

        /// <summary>Torch prefabs whose lit-state effect becomes the beacon's glow; first found wins. The wisp torch is
        /// wanted: the wisp orb with its light and sparks, not a flame.</summary>
        private static readonly string[] GlowTemplatePrefabs = { "piece_groundtorch_mist", "piece_wisplure", "piece_groundtorch_blue" };
        /// <summary>Name of the lit-state child on torches that have no Fireplace (the wisp torch).</summary>
        private const string LitStateChild = "_enabled";

        /// <summary>
        /// Which vanilla material dresses each Unity material. The token is a name fragment matched against loaded
        /// materials with case, spaces and underscores ignored (worn/broken variants excluded); the beacon_materials
        /// console command lists candidates. Glow entries keep the authored emission colour on top of the vanilla
        /// material; stone entries keep the authored albedo as a tint. Unlisted materials use the "*" entry.
        /// </summary>
        private static readonly (string unityMaterial, string vanillaToken, bool glow)[] MaterialTemplates =
        {
            ("BeaconStoneDark", "blackmarble", false),   // base and cap: Mistlands black marble
            ("BeaconBandDark", "blackmarble", false),    // the dark band behind the runes
            ("BeaconStoneLight", "grausten", false),     // main pillar: Ashlands grausten
            ("BeaconCrystal", "prefab:crystal_wall_1x1/crystal_window", true),  // floating crystal: the crystal wall's glass, with our blue glow
            ("BeaconRune", "prefab:crystal_wall_1x1/crystal_window", true),     // rune glyphs: same crystal, with our blue glow
            ("HolderIron", "prefab:iron_grate/metalwall", false),               // boss holders' pole and hooks: the iron wall's metal
            ("*", "stone_mat", false),                   // anything else: plain stone wall
        };

        /// <summary>Entries that keep the vanilla material's own colour instead of the Unity albedo as tint: the Unity
        /// colour is only an editor preview (a dark grey that would darken the metal texture a second time).</summary>
        private static readonly HashSet<string> UntintedMaterials = new HashSet<string> { "HolderIron" };

        internal static ManualLogSource Log;
        internal static BeaconConfig Cfg;
        private Harmony _harmony;

        /// <summary>
        /// 0.2.0 dropped "com." from the GUID, and BepInEx names the config file after the GUID. When the new file does
        /// not exist yet but the old one does, copy the old one to the new name and reload it, so players and servers
        /// keep their settings. The old file stays as a backup; nothing is done once the new file exists.
        /// </summary>
        private void MigrateConfigFile()
        {
            var newPath = Config.ConfigFilePath;
            var oldPath = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, OldPluginGuid + ".cfg");
            if (System.IO.File.Exists(newPath) || !System.IO.File.Exists(oldPath)) return;
            try
            {
                System.IO.File.Copy(oldPath, newPath);
                Config.Reload();
                Log.LogInfo($"Settings carried over from {OldPluginGuid}.cfg to {PluginGuid}.cfg (the old file is kept as a backup)");
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Could not carry over {OldPluginGuid}.cfg ({e.Message}); starting with default settings");
            }
        }

        private void Awake()
        {
            Log = Logger;
            MigrateConfigFile(); // before anything reads or binds a setting
            Cfg = new BeaconConfig(Config);
            Cfg.SettingChanged += DiscountRules.Rebuild;
            Cfg.SettingChanged += ApplyHolderRange;
            DiscountRules.Rebuild();
            RulesFile.InitAll(); // readable rules files override the config entries where this side has authority

            PrefabManager.OnVanillaPrefabsAvailable += RegisterPiece;
            BeaconUI.Init(gameObject);
            DebugCommands.Register();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(BuildBeaconPlugin).Assembly);
            Log.LogInfo($"{PluginName} {PluginVersion} loaded");
            if (Cfg.DevMode.Value)
                Log.LogWarning("Dev mode is ON: devcommands, god and fly will be enabled on spawning into a local world");
        }

        private void RegisterPiece()
        {
            var pieceCfg = new PieceConfig
            {
                Name = "$piece_xai_beacon",
                Description = "$piece_xai_beacon_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                // Valheim 1.0 build menu: pieces are found by usage tag, not just category.
                // Without this Jötunn guesses from the prefab, and a guard_stone clone would land under Defense.
                Usage = new[] { PieceUsages.Misc, PieceUsages.Crafting },
                CraftingStation = CraftingStations.Workbench,
                Requirements = new[]
                {
                    new RequirementConfig("Stone", 20, 0, true),
                    new RequirementConfig("SurtlingCore", 1, 0, true),
                }
            };
            CustomPiece piece = null;
            var bundle = AssetUtil.LoadBundle(typeof(BuildBeaconPlugin).Assembly, BundleName);
            if (bundle == null)
            {
                Log.LogWarning($"Asset bundle '{BundleName}' not found in the plugin's resources");
            }
            else
            {
                var asset = bundle.LoadAsset<GameObject>(AssetPrefab);
                if (asset == null)
                {
                    Log.LogWarning($"Prefab '{AssetPrefab}' not found in asset bundle '{BundleName}'");
                }
                else
                {
                    // The bundle asset is persistent and Unity refuses to parent new objects under it, so clone it
                    // into Jötunn's prefab container under the piece name and modify the clone instead.
                    var prefab = PrefabManager.Instance.CreateClonedPrefab(PiecePrefab, asset);
                    pieceCfg.Icon = LoadIcon(bundle);
                    PrepareBundlePrefab(prefab);
                    piece = new CustomPiece(prefab, true, pieceCfg);
                }
            }

            if (piece == null)
            {
                Log.LogWarning("Falling back to a guard_stone clone for the beacon piece");
                piece = new CustomPiece(PiecePrefab, "guard_stone", pieceCfg);
                StripWardBehaviour(piece.PiecePrefab);
            }

            AddBeaconStation(piece.PiecePrefab, pieceCfg.Icon);
            EnsureSnapPointTags(piece.PiecePrefab);
            PieceManager.Instance.AddPiece(piece);
            // A failure here must not take the holders, racks and texts below with it.
            GameObject great = null;
            try { great = RegisterGreatBeacon(bundle); }
            catch (System.Exception e) { Log.LogError($"Great Beacon not registered: {e}"); }
            SetBeaconSpacing(piece.PiecePrefab, great);
            RegisterBossHolders(piece.PiecePrefab, bundle);
            RegisterMobRacks(piece.PiecePrefab, bundle);
            bundle?.Unload(false); // loaded objects stay alive; only the bundle's file handle is released

            AddLocalization();
            DiscountRules.ValidateAgainstObjectDB(); // item database is populated by now; flag typos in the rules
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterPiece;
        }

        /// <summary>
        /// The Great Beacon: the beacon's recipe (PrepareBundlePrefab: ring, glow under the crystal pivot, effects,
        /// materials) on its own bundle prefab, a crafting station with the beacon's station name so holders and racks link
        /// to it, and own slots for boss trophies, one per attach_N alcove. Built at a Stonecutter. Null if the bundle has
        /// no such prefab.
        /// </summary>
        private static GameObject RegisterGreatBeacon(AssetBundle bundle)
        {
            var asset = bundle != null ? bundle.LoadAsset<GameObject>(GreatAssetPrefab) : null;
            if (asset == null)
            {
                Log.LogWarning($"Prefab '{GreatAssetPrefab}' not found in asset bundle '{BundleName}'; no Great Beacon");
                return null;
            }
            var cfg = new PieceConfig
            {
                Name = "$piece_xai_great_beacon",
                Description = "$piece_xai_great_beacon_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Misc,
                Usage = new[] { PieceUsages.Misc, PieceUsages.Crafting },
                CraftingStation = CraftingStations.Stonecutter,
                Icon = LoadIcon(bundle, GreatIconAsset),
                Requirements = new[]
                {
                    new RequirementConfig("Grausten", 20, 0, true),
                    new RequirementConfig("SurtlingCore", 5, 0, true),
                    new RequirementConfig("Crystal", 1, 0, true),
                }
            };
            var prefab = PrefabManager.Instance.CreateClonedPrefab(GreatPiecePrefab, asset);
            PrepareBundlePrefab(prefab);
            var beacon = prefab.GetComponent<BeaconController>();
            beacon.m_ownSlotKind = TrophyKind.Boss;
            beacon.m_ownSlotCount = 0;
            while (prefab.transform.Find(BeaconTrophyDisplay.AttachPrefix + beacon.m_ownSlotCount) != null) beacon.m_ownSlotCount++;
            AddBeaconStation(prefab, cfg.Icon, connectionHeight: 2.6f); // the threads meet the lower alcove row
            EnsureSnapPointTags(prefab);
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, true, cfg));
            Log.LogInfo($"Great Beacon {GreatPiecePrefab} registered from {GreatAssetPrefab}: {beacon.m_ownSlotCount} boss alcoves, " +
                        $"{prefab.GetComponentsInChildren<Transform>(true).Count(t => t.CompareTag("snappoint"))} snap points, " +
                        $"{prefab.GetComponentsInChildren<Transform>(true).Count(t => t.name.Contains("_shard_"))} crystal shards");
            return prefab;
        }

        /// <summary>No beacon of either kind within BeaconSpacing of another: vanilla's block radius on both pieces, each
        /// blocked by both (Player.UpdatePlacementGhost refuses with "Need more space").</summary>
        private static void SetBeaconSpacing(params GameObject[] prefabs)
        {
            var pieces = prefabs.Where(p => p != null).Select(p => p.GetComponent<Piece>()).Where(p => p != null).ToList();
            foreach (var piece in pieces)
            {
                piece.m_blockRadius = BeaconSpacing;
                piece.m_blockingPieces = new List<Piece>(pieces);
            }
            Log.LogInfo($"Beacon spacing: {BeaconSpacing} m between {string.Join(" and ", pieces.Select(p => p.name))}");
        }

        /// <summary>
        /// Make the bundle prefab a complete Valheim piece. The Unity prefab carries the model, colliders, snap point
        /// and the vanilla ZNetView/Piece/WearNTear components; anything missing is added here so a lighter prefab
        /// still works. The radius ring and glow are borrowed from the ward prefab when the bundle has none of its own.
        /// </summary>
        private static void PrepareBundlePrefab(GameObject prefab)
        {
            // Hammer removal and repair raycasts only hit the "piece" layer.
            int pieceLayer = LayerMask.NameToLayer("piece");
            if (pieceLayer >= 0 && prefab.layer != pieceLayer)
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = pieceLayer;

            if (prefab.GetComponent<ZNetView>() == null)
            {
                var nview = prefab.AddComponent<ZNetView>();
                nview.m_persistent = true;
            }

            if (prefab.GetComponent<Piece>() == null) prefab.AddComponent<Piece>();

            var wnt = prefab.GetComponent<WearNTear>();
            if (wnt == null)
            {
                wnt = prefab.AddComponent<WearNTear>();
                wnt.m_health = 200f;
                wnt.m_materialType = WearNTear.MaterialType.Stone;
                wnt.m_noRoofWear = true;
                wnt.m_noSupportWear = true;
            }
            BorrowEffects(prefab, wnt);

            // Swap shaders before borrowing vanilla children below, so their shared vanilla materials are never touched.
            SwapToVanillaShaders(prefab);

            var beacon = prefab.GetComponent<BeaconController>() ?? prefab.AddComponent<BeaconController>();
            if (beacon.m_areaMarker == null) beacon.m_areaMarker = prefab.GetComponentInChildren<CircleProjector>(true);

            var ward = PrefabManager.Instance.GetPrefab("guard_stone");
            var wardArea = ward != null ? ward.GetComponent<PrivateArea>() : null;
            if (beacon.m_areaMarker == null && wardArea != null && wardArea.m_areaMarker != null)
            {
                var ring = Object.Instantiate(wardArea.m_areaMarker.gameObject, prefab.transform);
                ring.name = "AreaMarker";
                ring.SetActive(false);
                beacon.m_areaMarker = ring.GetComponent<CircleProjector>();
                TintRing(beacon.m_areaMarker, RingColor);
            }
            if (beacon.m_activeEffect == null)
            {
                // The glow is a torch's lit state. Fuel torches keep it in Fireplace.m_enabledObject; the wisp torch
                // (piece_groundtorch_mist) has no Fireplace on 1.0, its orb, light and sparks sit under a plain child
                // named "_enabled" that is always on. Try the Fireplace first, then the child by name.
                GameObject flame = null; string source = null;
                foreach (var name in GlowTemplatePrefabs)
                {
                    var torch = PrefabManager.Instance.GetPrefab(name);
                    if (torch == null) continue;
                    var fireplace = torch.GetComponent<Fireplace>();
                    if (fireplace != null && fireplace.m_enabledObject != null) { flame = fireplace.m_enabledObject; source = $"{name} (Fireplace)"; break; }
                    var enabled = torch.transform.Find(LitStateChild);
                    if (enabled != null) { flame = enabled.gameObject; source = $"{name} (child)"; break; }
                }
                if (flame == null && wardArea != null && wardArea.m_enabledEffect != null) { flame = wardArea.m_enabledEffect; source = "guard_stone"; }

                if (flame != null)
                {
                    // Under the crystal's pivot when the prefab has one, so the light and particles move with the
                    // crystal (BeaconController spins and bobs it while lit).
                    var pivot = prefab.transform.Find(CrystalPivot);
                    var glow = Object.Instantiate(flame, pivot != null ? pivot : prefab.transform);
                    glow.name = "ActiveEffect";
                    glow.SetActive(false);
                    // Only the light and particles are wanted. The wisp torch's lit state also holds the wisp orb
                    // itself (a "demister_ball" mesh); the beacon's crystal takes that role, so drop every mesh in
                    // the clone. This is our copy: the vanilla torch keeps its orb. DestroyImmediate, as with the ward.
                    var meshes = glow.GetComponentsInChildren<MeshRenderer>(true);
                    foreach (var mesh in meshes) Object.DestroyImmediate(mesh.gameObject);
                    bool centered = TryFindCrystalCenter(prefab, out var center);
                    glow.transform.localPosition = pivot != null || !centered ? Vector3.zero : center;
                    beacon.m_activeEffect = glow;
                    Log.LogInfo($"Beacon glow: \"{flame.name}\" from {source}, " +
                                (pivot != null ? $"on the crystal's pivot at {pivot.localPosition}" : centered ? $"centered on the crystal at {center}" : "crystal not found, left at the prefab origin") +
                                $"; lights [{string.Join(", ", glow.GetComponentsInChildren<Light>(true).Select(l => $"{l.type} {l.color} r{l.range:0.#}"))}]" +
                                (meshes.Length > 0 ? $"; removed {meshes.Length} mesh object(s) from the clone" : ""));
                }
                else Log.LogWarning("No glow template found; the beacon has no active effect");
            }

            if (prefab.GetComponentInChildren<Collider>(true) == null)
                Log.LogWarning("Beacon prefab has no collider; it cannot be hovered, hit or removed");
        }

        /// <summary>
        /// Dress the Unity-authored materials in vanilla ones, per MaterialTemplates.
        /// Stone entries: move to the game's Custom/Piece shader, copy the vanilla material (textures, normal map,
        /// grime noise, wear flags), reapply the authored albedo as tint and project triplanarly.
        /// Glow entries: copy the vanilla material on its own shader when that shader can emit, otherwise keep the
        /// authored Standard material and borrow only the vanilla textures; either way the authored emission colour
        /// is what glows. Materials named JVLmock_* are left alone: Jötunn replaces those with the real vanilla one.
        /// </summary>
        private static void SwapToVanillaShaders(GameObject prefab)
        {
            var pieceShader = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(s => s.name == "Custom/Piece");
            if (pieceShader == null)
            {
                Log.LogWarning("Custom/Piece shader not found; beacon materials keep their bundled shader");
                return;
            }

            var seen = new HashSet<Material>();
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || !seen.Add(mat)) continue;
                    if (mat.name.StartsWith("JVLmock_")) continue; // Jötunn's mock prefix; MockManager itself is internal
                    // Only Unity-authored materials. Particle, UI and vanilla shaders are left as they are;
                    // moving a particle material onto another shader breaks it, and shared vanilla ones affect the whole game.
                    if (mat.shader == null || !mat.shader.name.StartsWith("Standard")) continue;

                    var tint = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                    var emission = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                    bool authoredGlow = mat.IsKeywordEnabled("_EMISSION") && Mathf.Max(emission.r, emission.g, emission.b) > 0f;

                    var entry = MaterialTemplates.FirstOrDefault(t => t.unityMaterial == "*" || mat.name.StartsWith(t.unityMaterial));
                    if (entry.glow) DressGlow(mat, entry.vanillaToken, emission, authoredGlow);
                    else if (authoredGlow) Log.LogInfo($"Beacon material \"{mat.name}\": kept Standard (emissive, no glow template)");
                    else DressStone(mat, entry.vanillaToken, pieceShader, UntintedMaterials.Contains(entry.unityMaterial) ? Color.white : tint);
                }
            }
        }

        /// <summary>Non-emissive surface: vanilla stone on the piece shader, authored colour as tint, triplanar projection.</summary>
        private static void DressStone(Material mat, string token, Shader pieceShader, Color tint)
        {
            mat.shader = pieceShader;
            mat.DisableKeyword("_EMISSION");

            var template = FindVanillaMaterial(token, pieceShader);
            if (template == null)
            {
                if (mat.HasProperty("_AddRain")) { mat.SetFloat("_AddRain", 1f); mat.EnableKeyword("_ADDRAIN_ON"); }
                Log.LogWarning($"Beacon material \"{mat.name}\": Standard -> {pieceShader.name}; no vanilla material matching \"{token}\" on that shader, kept flat colour");
                return;
            }

            mat.CopyPropertiesFromMaterial(template);
            mat.SetColor("_Color", tint);
            DisableVertexEffects(mat, template);
            if (mat.HasProperty("_TriplanarMap"))
            {
                mat.SetFloat("_TriplanarMap", 1f);
                mat.SetFloat("_TriplanarLocalPos", 1f);
                mat.SetFloat("_TriplanarScale", StoneTextureScale);
                mat.EnableKeyword("_TRIPLANARMAP_ON");
            }
            Log.LogInfo($"Beacon material \"{mat.name}\": Standard -> {pieceShader.name} with textures from \"{template.name}\", tint {tint}");
        }

        /// <summary>
        /// Glowing surface: the vanilla material's look with the authored emission colour on top. If the vanilla
        /// shader has an emission colour and the _EMISSION keyword, the whole material is copied onto that shader;
        /// otherwise only its textures are borrowed onto the authored Standard material, whose emission is known to work.
        /// </summary>
        private static void DressGlow(Material mat, string token, Color emission, bool authoredGlow)
        {
            var template = FindVanillaMaterial(token, null);
            if (template == null)
            {
                Log.LogWarning($"Beacon material \"{mat.name}\": no vanilla material matching \"{token}\"; kept as authored");
                return;
            }

            bool templateCanEmit = template.HasProperty("_EmissionColor")
                                && template.shader.keywordSpace.keywordNames.Contains("_EMISSION");
            if (templateCanEmit)
            {
                mat.shader = template.shader;
                mat.CopyPropertiesFromMaterial(template);
                mat.renderQueue = template.renderQueue; // not a property, so not copied; transparent materials sort by it
                DisableVertexEffects(mat, template);
            }
            else
            {
                foreach (var prop in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" })
                    if (mat.HasProperty(prop) && template.HasProperty(prop)) mat.SetTexture(prop, template.GetTexture(prop));
                if (mat.HasProperty("_MainTex") && template.HasProperty("_MainTex"))
                {
                    mat.SetTextureScale("_MainTex", template.GetTextureScale("_MainTex"));
                    mat.SetTextureOffset("_MainTex", template.GetTextureOffset("_MainTex"));
                }
                if (mat.HasProperty("_Color") && template.HasProperty("_Color")) mat.SetColor("_Color", template.GetColor("_Color"));
            }

            if (authoredGlow)
            {
                mat.SetColor("_EmissionColor", emission);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            Log.LogInfo($"Beacon material \"{mat.name}\": {(templateCanEmit ? "copied" : "textures from")} \"{template.name}\" on {mat.shader.name}, keywords [{string.Join(" ", mat.shaderKeywords)}], emission {mat.GetColor("_EmissionColor")}");
        }

        /// <summary>
        /// Vanilla materials carry vertex noise, ripple and parallax tuned for their own meshes. On this low-poly model
        /// they displace faces over the runes and open seams between pillar faces. The shader gates them on keywords,
        /// which CopyPropertiesFromMaterial copies too, so the floats alone are not enough.
        /// </summary>
        private static void DisableVertexEffects(Material mat, Material template)
        {
            var vertexNoise = mat.HasProperty("_ValueNoiseVertex") ? mat.GetFloat("_ValueNoiseVertex") : 0f;
            var ripple = mat.HasProperty("_RippleDistance") ? mat.GetFloat("_RippleDistance") : 0f;
            if (mat.HasProperty("_ValueNoiseVertex")) mat.SetFloat("_ValueNoiseVertex", 0f);
            if (mat.HasProperty("_RippleDistance")) mat.SetFloat("_RippleDistance", 0f);
            mat.DisableKeyword("_VALUENOISEVERTEX_ON");
            mat.DisableKeyword("_PARALLAXMAP");
            Log.LogInfo($"Beacon material \"{mat.name}\": template \"{template.name}\" had vertex noise {vertexNoise:0.###}, ripple {ripple:0.###}, keywords [{string.Join(" ", template.shaderKeywords)}]; vertex effects and parallax disabled");
        }

        private static readonly Dictionary<string, Material> VanillaMaterialCache = new Dictionary<string, Material>();

        /// <summary>Snow overlays, LOD meshes and effect shaders are never the material a piece is "made of".</summary>
        private static bool IsOverlayOrLod(Renderer renderer, Material mat)
        {
            var shaderName = mat.shader != null ? mat.shader.name : "";
            if (shaderName.IndexOf("Snow", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (shaderName.StartsWith("Particles") || shaderName.StartsWith("Legacy") || shaderName.StartsWith("Hidden")) return true;
            var path = renderer.transform.name + "/" + (renderer.transform.parent != null ? renderer.transform.parent.name : "");
            return path.IndexOf("lod", System.StringComparison.OrdinalIgnoreCase) >= 0
                || path.IndexOf("snow", System.StringComparison.OrdinalIgnoreCase) >= 0
                || mat.name.IndexOf("lod", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static readonly string[] DamagedVariantMarkers = { "worn", "broken", "destroyed", "damaged", "half", "ruin", "old" };

        /// <summary>
        /// A loaded vanilla material whose name matches <paramref name="token"/> with case, spaces and underscores
        /// ignored, optionally restricted to <paramref name="shader"/>: an exact name first, otherwise the shortest
        /// name containing the token that is not a worn/broken variant. Logs the choice and the other candidates so a
        /// wrong pick is easy to spot and correct in MaterialTemplates.
        /// </summary>
        private static Material FindVanillaMaterial(string token, Shader shader)
        {
            var cacheKey = token + "|" + (shader != null ? shader.name : "*");
            if (VanillaMaterialCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            // "prefab:<name>" or "prefab:<name>/<material>" takes a material straight off a vanilla prefab's renderers.
            // Materials are only findable by name once something has loaded them, and a piece's material may not be
            // yet at registration; the prefab itself always is. Without an explicit material, snow overlays, LOD
            // meshes and particle systems are skipped, since a piece's first renderer is often one of those.
            if (token.StartsWith("prefab:"))
            {
                var spec = token.Substring("prefab:".Length).Split('/');
                var prefabName = spec[0];
                var wanted = spec.Length > 1 ? DiscountRules.Normalize(spec[1]) : null;

                var vanillaPrefab = PrefabManager.Instance.GetPrefab(prefabName);
                var mats = vanillaPrefab != null
                    ? vanillaPrefab.GetComponentsInChildren<Renderer>(true)
                        .Where(r => !(r is ParticleSystemRenderer))
                        .SelectMany(r => r.sharedMaterials.Where(m => m != null).Select(m => (renderer: r, mat: m)))
                        .ToList()
                    : new List<(Renderer renderer, Material mat)>();

                Material fromPrefab;
                if (wanted != null)
                {
                    fromPrefab = mats.Select(x => x.mat).FirstOrDefault(m => DiscountRules.Normalize(m.name) == wanted)
                              ?? mats.Select(x => x.mat).FirstOrDefault(m => DiscountRules.Normalize(m.name).Contains(wanted));
                }
                else
                {
                    fromPrefab = mats
                        .Where(x => shader == null || x.mat.shader == shader)
                        .Where(x => !IsOverlayOrLod(x.renderer, x.mat))
                        .OrderBy(x => x.mat.shader.name.StartsWith("Standard") ? 0 : x.mat.shader.name == "Custom/Piece" ? 1 : 2)
                        .Select(x => x.mat)
                        .FirstOrDefault();
                }

                if (fromPrefab != null)
                {
                    VanillaMaterialCache[cacheKey] = fromPrefab;
                    var others = mats.Select(x => x.mat.name).Where(n => n != fromPrefab.name).Distinct().ToList();
                    Log.LogInfo($"Vanilla material for \"{token}\": \"{fromPrefab.name}\" on {fromPrefab.shader.name}" +
                                (others.Count > 0 ? $" (prefab also uses: {string.Join(", ", others)})" : ""));
                }
                else
                {
                    Log.LogWarning(vanillaPrefab == null
                        ? $"Vanilla prefab \"{prefabName}\" not found"
                        : $"Vanilla prefab \"{prefabName}\" has no material matching \"{spec.Skip(1).FirstOrDefault() ?? "(auto)"}\"; it uses: {string.Join(", ", mats.Select(x => x.mat.name).Distinct())}");
                }
                return fromPrefab;
            }

            var key = DiscountRules.Normalize(token);
            var candidates = Resources.FindObjectsOfTypeAll<Material>()
                .Where(m => m != null && m.shader != null && (shader == null || m.shader == shader)
                            && !m.name.EndsWith("(Instance)")
                            && DiscountRules.Normalize(m.name).Contains(key))
                .GroupBy(m => m.name).Select(g => g.First())
                .ToList();

            var pick = candidates.FirstOrDefault(m => DiscountRules.Normalize(m.name) == key)
                    ?? candidates.Where(m => !DamagedVariantMarkers.Any(d => m.name.IndexOf(d, System.StringComparison.OrdinalIgnoreCase) >= 0))
                                 .OrderBy(m => m.name.Length).FirstOrDefault()
                    ?? candidates.OrderBy(m => m.name.Length).FirstOrDefault();

            if (pick != null)
            {
                VanillaMaterialCache[cacheKey] = pick;
                var others = candidates.Where(m => m != pick).Select(m => m.name).ToList();
                Log.LogInfo($"Vanilla material for \"{token}\": \"{pick.name}\"" + (others.Count > 0 ? $" (other candidates: {string.Join(", ", others)})" : ""));
            }
            return pick;
        }

        /// <summary>
        /// Sounds and particles for placing, hitting and destroying the beacon, taken from a vanilla stone piece when
        /// the prefab's own effect lists are empty. The lists hold prefab references and are only read, so sharing
        /// them with the vanilla piece is safe.
        /// </summary>
        private static void BorrowEffects(GameObject prefab, WearNTear wnt)
        {
            var template = PrefabManager.Instance.GetPrefab(EffectTemplatePrefab);
            var tPiece = template != null ? template.GetComponent<Piece>() : null;
            var tWnt = template != null ? template.GetComponent<WearNTear>() : null;
            if (tPiece == null || tWnt == null)
            {
                Log.LogWarning($"Effect template prefab '{EffectTemplatePrefab}' not found or lacks Piece/WearNTear; beacon stays silent");
                return;
            }

            var piece = prefab.GetComponent<Piece>();
            if (IsEmpty(piece.m_placeEffect)) piece.m_placeEffect = tPiece.m_placeEffect;
            if (IsEmpty(wnt.m_destroyedEffect)) wnt.m_destroyedEffect = tWnt.m_destroyedEffect;
            if (IsEmpty(wnt.m_hitEffect)) wnt.m_hitEffect = tWnt.m_hitEffect;
            if (IsEmpty(wnt.m_switchEffect)) wnt.m_switchEffect = tWnt.m_switchEffect;

            Log.LogInfo($"Beacon effects from \"{EffectTemplatePrefab}\": place [{Names(piece.m_placeEffect)}], " +
                        $"destroyed [{Names(wnt.m_destroyedEffect)}], hit [{Names(wnt.m_hitEffect)}], switch [{Names(wnt.m_switchEffect)}]");
        }

        private static bool IsEmpty(EffectList list) => list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0;

        private static string Names(EffectList list) =>
            IsEmpty(list) ? "" : string.Join(", ", list.m_effectPrefabs.Where(e => e.m_prefab != null).Select(e => e.m_prefab.name));

        /// <summary>
        /// Center of the crystal in prefab space: a child named "crystal" or "glow" wins; otherwise the (sub)mesh
        /// drawn with the crystal material. Uses mesh bounds, which work while the prefab is inactive.
        /// </summary>
        private static bool TryFindCrystalCenter(GameObject prefab, out Vector3 center)
        {
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (t == prefab.transform) continue;
                if (t.name.Equals("crystal", System.StringComparison.OrdinalIgnoreCase) || t.name.Equals("glow", System.StringComparison.OrdinalIgnoreCase))
                {
                    center = prefab.transform.InverseTransformPoint(t.position);
                    return true;
                }
            }

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                var renderer = mf.GetComponent<Renderer>();
                if (mesh == null || renderer == null) continue;

                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.StartsWith(CrystalMaterial)) continue;
                    var bounds = mesh.subMeshCount > 1 && i < mesh.subMeshCount ? mesh.GetSubMesh(i).bounds : mesh.bounds;
                    center = prefab.transform.InverseTransformPoint(mf.transform.TransformPoint(bounds.center));
                    return true;
                }
            }

            center = Vector3.zero;
            return false;
        }

        /// <summary>The icon is shipped as a plain texture in the bundle; wrap it in a sprite for the build menu.</summary>
        private static Sprite LoadIcon(AssetBundle bundle, string asset = IconAsset)
        {
            var sprite = bundle.LoadAsset<Sprite>(asset);
            if (sprite != null) return sprite;

            var tex = bundle.LoadAsset<Texture2D>(asset);
            if (tex == null)
            {
                Log.LogWarning($"Icon '{asset}' not found in asset bundle; Jötunn will render one from the prefab");
                return null;
            }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// The clone of guard_stone carries the ward's PrivateArea component, which registers the ward RPCs,
        /// owns hover/interact, and is what the game keys the ward tutorial and access checks off.
        /// Remove it so only the mesh, ring projector and glow effect remain, then hand those visuals to the beacon.
        /// </summary>
        private static void StripWardBehaviour(GameObject prefab)
        {
            var ward = prefab.GetComponent<PrivateArea>();
            var ring = ward != null ? ward.m_areaMarker : null;
            var glow = ward != null ? ward.m_enabledEffect : null;

            if (ward != null)
            {
                // DestroyImmediate: the prefab is registered with ZNetScene right after this, so a deferred Destroy is too late.
                Object.DestroyImmediate(ward);
            }

            if (ring != null) ring.gameObject.SetActive(false);
            if (glow != null) glow.SetActive(false);

            var beacon = prefab.AddComponent<BeaconController>();
            beacon.m_areaMarker = ring;
            beacon.m_activeEffect = glow;
        }

        /// <summary>
        /// Make the beacon a vanilla crafting station so the boss holders can be its extensions (placement range, the
        /// yellow connection thread, closest-station linking). Nothing is crafted here: no recipes name this station,
        /// no roof or fire rule, no build range, and a discover range of 0 so players never get a "new station"
        /// unlock message (Player.AddKnownStation queues one). Added after BeaconController on purpose: Player picks the
        /// first Interactable/Hoverable on the object, so the beacon panel, not the crafting window, opens on use.
        /// </summary>
        private static void AddBeaconStation(GameObject prefab, Sprite icon, float connectionHeight = 1.4f)
        {
            var station = prefab.GetComponent<CraftingStation>() ?? prefab.AddComponent<CraftingStation>();
            station.m_name = "$piece_xai_beacon";
            station.m_icon = icon;
            station.m_discoverRange = 0f;
            station.m_rangeBuild = 0f;
            station.m_extraRangePerLevel = 0f;
            station.m_craftRequireRoof = false;
            station.m_craftRequireFire = false;
            station.m_showBasicRecipies = false;
            station.m_hasCraftTab = false;
            station.m_canRepair = false;
            station.m_useAnimation = 0;
            station.m_craftItemEffects = new EffectList();
            station.m_craftItemDoneEffects = new EffectList();
            station.m_repairItemDoneEffects = new EffectList();

            // Where the holders' threads attach: mid-pillar, level with the alcoves.
            var point = new GameObject("station_connection").transform;
            point.SetParent(prefab.transform, false);
            point.localPosition = new Vector3(0f, connectionHeight, 0f);
            station.m_connectionPoint = point;

            var first = prefab.GetComponents<MonoBehaviour>().FirstOrDefault(c => c is Interactable);
            if (first is BeaconController) Log.LogInfo("Beacon crafting station added; the beacon panel keeps the use key");
            else Log.LogWarning($"Beacon: first Interactable is {first?.GetType().Name ?? "none"}, not BeaconController; use would open the wrong window");
        }

        /// <summary>
        /// The two boss trophy holders, each built from its bundle prefab (model, colliders, snap points, attach_trophy,
        /// ZNetView/Piece/WearNTear) and icon, with a guard_stone clone stripped of the ward as a fallback. Each gets a
        /// BossHolder and a vanilla StationExtension pointing at the beacon's station. BossHolder goes first so its hover
        /// and use win over the extension's. The extension stacks, so every holder counts as an extension and gets a thread.
        /// </summary>
        private static void RegisterBossHolders(GameObject beaconPrefab, AssetBundle bundle)
        {
            var station = beaconPrefab.GetComponent<CraftingStation>();
            var ward = PrefabManager.Instance.GetPrefab("guard_stone");
            var vanillaExtension = PrefabManager.Instance.GetPrefab("piece_workbench_ext1");
            var connection = vanillaExtension != null ? vanillaExtension.GetComponent<StationExtension>()?.m_connectionPrefab : null;
            if (connection == null) connection = PrefabManager.Instance.GetPrefab("vfx_ExtensionConnection");
            if (station == null || connection == null)
            {
                Log.LogWarning($"Boss holders not registered: station {station != null}, connection effect {connection != null}");
                return;
            }

            foreach (var h in Holders)
            {
                var cfg = new PieceConfig
                {
                    Name = h.name,
                    Description = h.desc,
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Misc,
                    Usage = new[] { PieceUsages.Misc, PieceUsages.Crafting },
                    CraftingStation = CraftingStations.Workbench,
                    Requirements = new[]
                    {
                        new RequirementConfig("Stone", 10, 0, true),
                        new RequirementConfig("Wood", 5, 0, true),
                    }
                };

                CustomPiece piece;
                var asset = bundle != null ? bundle.LoadAsset<GameObject>(h.asset) : null;
                if (asset != null)
                {
                    var prefab = PrefabManager.Instance.CreateClonedPrefab(h.id, asset);
                    cfg.Icon = LoadIcon(bundle, h.icon);
                    var wnt = prefab.GetComponent<WearNTear>();
                    if (wnt != null) BorrowEffects(prefab, wnt);
                    SwapToVanillaShaders(prefab); // materials the beacon already dressed are skipped (no longer Standard)
                    EnsureSnapPointTags(prefab);
                    CentreSnapOnlyOnWalls(prefab);
                    piece = new CustomPiece(prefab, true, cfg);
                }
                else if (ward != null)
                {
                    Log.LogWarning($"Boss holder '{h.asset}' not in the bundle; falling back to a guard_stone clone for {h.id}");
                    cfg.Icon = ward.GetComponent<Piece>()?.m_icon;
                    piece = new CustomPiece(h.id, "guard_stone", cfg);
                    StripWardForHolder(piece.PiecePrefab);
                }
                else
                {
                    Log.LogWarning($"Boss holder {h.id} not registered: no bundle prefab and no guard_stone");
                    continue;
                }

                var root = piece.PiecePrefab;
                root.AddComponent<BossHolder>();
                var ext = root.AddComponent<StationExtension>();
                ext.m_craftingStation = station;
                ext.m_maxStationDistance = Cfg.HolderRange.Value;
                HolderExtensions.Add(ext);
                ext.m_connectionPrefab = connection;
                ext.m_connectionOffset = new Vector3(0f, 1f, 0f);
                ext.m_stack = true;
                ext.m_continousConnection = false;
                PieceManager.Instance.AddPiece(piece);
                Log.LogInfo($"Boss holder {h.id} registered from {(asset != null ? h.asset : "guard_stone")}: " +
                            $"{root.GetComponentsInChildren<Transform>(true).Count(t => t.CompareTag("snappoint"))} snap points, " +
                            $"attach_trophy {(root.transform.Find(BossHolder.AttachPoint) != null ? "found" : "missing")}");
            }
            s_appliedHolderRange = Cfg.HolderRange.Value;
            Log.LogInfo($"Boss holders: link range {s_appliedHolderRange} m, thread \"{connection.name}\"");
        }

        /// <summary>
        /// HolderRange is server-synced, so a client receives the server's value after registration. Apply it to the
        /// prefabs and to loaded holders whenever it changes (SettingChanged fires for every setting, hence the check).
        /// </summary>
        private static void ApplyHolderRange()
        {
            var range = Cfg.HolderRange.Value;
            if (HolderExtensions.Count == 0 || Mathf.Approximately(range, s_appliedHolderRange)) return;
            foreach (var ext in HolderExtensions)
                if (ext != null) ext.m_maxStationDistance = range;
            BossHolder.ApplyRange();
            MobRack.ApplyRange();
            s_appliedHolderRange = range;
            Log.LogInfo($"Boss holders: link range now {range} m");
        }

        /// <summary>
        /// The two creature trophy racks, each from its bundle prefab (model, colliders, snap points, attach_0..3 and
        /// slot_0..3 with their colliders, ZNetView/Piece/WearNTear) and icon. Each gets a MobRack, a RackSlot on every
        /// slot_N (so each alcove is its own hover and use target), then a vanilla StationExtension pointing at the
        /// beacon's station: MobRack goes first so its hover and use win over the extension's, as with the holders.
        /// No guard_stone fallback: without the bundle prefab the rack is not registered.
        /// </summary>
        private static void RegisterMobRacks(GameObject beaconPrefab, AssetBundle bundle)
        {
            var station = beaconPrefab.GetComponent<CraftingStation>();
            var vanillaExtension = PrefabManager.Instance.GetPrefab("piece_workbench_ext1");
            var connection = vanillaExtension != null ? vanillaExtension.GetComponent<StationExtension>()?.m_connectionPrefab : null;
            if (connection == null) connection = PrefabManager.Instance.GetPrefab("vfx_ExtensionConnection");
            if (station == null || connection == null || bundle == null)
            {
                Log.LogWarning($"Trophy racks not registered: station {station != null}, connection effect {connection != null}, bundle {bundle != null}");
                return;
            }

            foreach (var r in Racks)
            {
                var asset = bundle.LoadAsset<GameObject>(r.asset);
                if (asset == null)
                {
                    Log.LogWarning($"Trophy rack {r.id} not registered: '{r.asset}' is not in the bundle");
                    continue;
                }
                var cfg = new PieceConfig
                {
                    Name = r.name,
                    Description = r.desc,
                    PieceTable = PieceTables.Hammer,
                    Category = PieceCategories.Misc,
                    Usage = new[] { PieceUsages.Misc, PieceUsages.Crafting },
                    CraftingStation = CraftingStations.Workbench,
                    Icon = LoadIcon(bundle, r.icon),
                    Requirements = new[]
                    {
                        new RequirementConfig("Wood", 10, 0, true),
                        new RequirementConfig("Stone", 5, 0, true),
                    }
                };
                var prefab = PrefabManager.Instance.CreateClonedPrefab(r.id, asset);
                var wnt = prefab.GetComponent<WearNTear>();
                if (wnt != null) BorrowEffects(prefab, wnt);
                SwapToVanillaShaders(prefab); // the beacon's materials are dressed already; skipped
                EnsureSnapPointTags(prefab);
                CentreSnapOnlyOnWalls(prefab);

                var rack = prefab.AddComponent<MobRack>();
                rack.m_fitToFace = r.fitToFace;
                int slots = 0, attaches = 0;
                for (int i = 0; i < MobRack.SlotCount; i++)
                {
                    var slot = prefab.transform.Find("slot_" + i);
                    if (slot != null)
                    {
                        slot.gameObject.AddComponent<RackSlot>().m_index = i;
                        slots++;
                    }
                    if (prefab.transform.Find("attach_" + i) != null) attaches++;
                }
                var ext = prefab.AddComponent<StationExtension>();
                ext.m_craftingStation = station;
                ext.m_maxStationDistance = Cfg.HolderRange.Value;
                HolderExtensions.Add(ext);
                ext.m_connectionPrefab = connection;
                ext.m_connectionOffset = new Vector3(0f, 1f, 0f);
                ext.m_stack = true;
                ext.m_continousConnection = false;
                PieceManager.Instance.AddPiece(new CustomPiece(prefab, true, cfg));
                Log.LogInfo($"Trophy rack {r.id} registered from {r.asset}: " +
                            $"{prefab.GetComponentsInChildren<Transform>(true).Count(t => t.CompareTag("snappoint"))} snap points, " +
                            $"{attaches}/{MobRack.SlotCount} attach points, {slots}/{MobRack.SlotCount} slots" +
                            (r.fitToFace ? ", trophies fit the face" : ""));
            }
        }

        private static void StripWardForHolder(GameObject prefab)
        {
            var ward = prefab.GetComponent<PrivateArea>();
            if (ward != null) Object.DestroyImmediate(ward); // before registration, as in StripWardBehaviour
            foreach (var child in new[] { "GuidePoint", "WayEffect", "AreaMarker", "PlayerBase", "InRangeIndicator" })
            {
                var t = prefab.transform.Find(child);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        }

        /// <summary>
        /// Valheim snaps to direct children tagged "snappoint". The Unity project carries Valheim's tag list, so the tag
        /// should arrive intact; set it again by name on the children meant as snap points ("snappoint_*" and the
        /// beacon's "Bottom Center") anyway, so a tag list that drifts from the game's cannot silently break snapping.
        /// The log line only appears if something had to be fixed.
        /// </summary>
        private static void EnsureSnapPointTags(GameObject prefab)
        {
            int fixedCount = 0;
            foreach (Transform child in prefab.transform)
            {
                bool snap = child.name.StartsWith("snappoint") || child.name == "Bottom Center";
                if (snap && !child.CompareTag("snappoint")) { child.gameObject.tag = "snappoint"; fixedCount++; }
            }
            if (fixedCount > 0) Log.LogInfo($"{prefab.name}: tagged {fixedCount} snap point(s) that arrived without the snappoint tag");
        }

        /// <summary>
        /// For now the wall pieces (Boss Trophy Mount, Trophy Panel) snap by their centre only: with the edge and corner
        /// points, snapping to walls did not work consistently (see docs/open-items.md, "Sunken snap points"). The other
        /// points stay in the prefab, untagged, so they are there to bring back. Other pieces are left alone.
        /// </summary>
        private static void CentreSnapOnlyOnWalls(GameObject prefab)
        {
            if (prefab.name != HolderPrefabWall && prefab.name != RackPrefabWall) return;
            int kept = 0, dropped = 0;
            foreach (Transform child in prefab.transform)
            {
                if (!child.CompareTag("snappoint")) continue;
                if (child.name == "snappoint_center" || child.name == "snappoint_centre") { kept++; continue; }
                child.gameObject.tag = "Untagged";
                dropped++;
            }
            Log.LogInfo($"{prefab.name}: snaps by its centre only ({kept} kept, {dropped} snap points switched off)");
        }

        /// <summary>
        /// The beacon's radius ring colour: a saturated blue, to tell it from the white ward and workbench rings. It
        /// multiplies the white segment, so this is the colour seen. Red is kept low: the earlier pale (0.62, 0.85, 1)
        /// read as nearly white in game, and (0.25, 0.55, 1) was too strong; this sits between them.
        /// </summary>
        private static readonly Color RingColor = new Color(0.44f, 0.7f, 1f);

        /// <summary>
        /// CircleProjector builds the ring from copies of its m_prefab segment. Give this ring its own copy of the segment
        /// with a tinted copy of its material, so only the beacon's ring changes colour (vanilla's shared material and
        /// the ward's ring are untouched). The segment's alpha is kept.
        /// </summary>
        /// <summary>The tinted ring segment, made once and shared by every beacon's ring (Jötunn refuses a second clone
        /// under the same name, and returns null).</summary>
        private static GameObject s_ringSegment;

        private static void TintRing(CircleProjector ring, Color color)
        {
            if (ring == null || ring.m_prefab == null) return;
            if (s_ringSegment != null)
            {
                ring.m_prefab = s_ringSegment;
                return;
            }
            var segment = PrefabManager.Instance.CreateClonedPrefab("xai_beacon_ring_segment", ring.m_prefab);
            if (segment == null)
            {
                Log.LogWarning("Beacon ring: could not clone the ward's ring segment; the ring keeps the ward's colour");
                return;
            }
            s_ringSegment = segment;
            int tinted = 0;
            foreach (var r in segment.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var copy = new Material(mats[i]) { name = mats[i].name + "_beacon" };
                    foreach (var prop in new[] { "_Color", "_TintColor", "_EmissionColor" })
                    {
                        if (!copy.HasProperty(prop)) continue;
                        var old = copy.GetColor(prop);
                        copy.SetColor(prop, new Color(color.r, color.g, color.b, old.a) * (prop == "_EmissionColor" ? Mathf.Max(old.r, old.g, old.b) : 1f));
                        tinted++;
                    }
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
            ring.m_prefab = segment;
            Log.LogInfo($"Beacon ring tinted {color}: {tinted} colour propert{(tinted == 1 ? "y" : "ies")} on the segment \"{segment.name}\"");
        }

        private static void AddLocalization()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new System.Collections.Generic.Dictionary<string, string>
            {
                { "piece_xai_beacon", "Build Beacon" },
                { "piece_xai_beacon_desc", "A rune-carved stone that draws on the power of slain foes. Build boss trophy holders beside it: each holder raises the beacon's level and widens its radius, and each boss trophy on them makes its materials free, or nearly free, inside that radius. Four creature trophies go in the beacon itself, and trophy racks hold more; each discounts its own materials." },
                { "piece_xai_bossholder_pillar", "Boss Trophy Pillar" },
                { "piece_xai_bossholder_pillar_desc", "A stone pillar with an iron hook that holds one boss trophy for a nearby Build Beacon. The boss's materials become free, or nearly free, inside the beacon's radius." },
                { "piece_xai_bossholder_wall", "Boss Trophy Mount" },
                { "piece_xai_bossholder_wall_desc", "A hexagonal stone mount for a wall, with an iron hook that holds one boss trophy for a nearby Build Beacon. The boss's materials become free, or nearly free, inside the beacon's radius." },
                { "xai_holder_empty", "Empty" },
                { "xai_holder_empty_hint", "Use a boss trophy on it to place it" },
                { "xai_holder_full", "It already holds a trophy" },
                { "xai_holder_boss_only", "Only boss trophies go here" },
                { "xai_holder_no_beacon", "No Build Beacon in range" },
                { "xai_holder_linked", "Linked to a Build Beacon" },
                { "xai_holder_unlinked", "No Build Beacon in range" },
                { "xai_holder_not_counted", "Not counted: the beacon already has this boss or is full" },
                { "xai_holder_take", "Take trophy" },
                { "xai_holder_place", "Place boss trophy" },
                { "xai_beacon_boss_use_holder", "Boss trophies go on a boss trophy holder next to the beacon" },
                { "piece_xai_great_beacon", "Great Beacon" },
                { "piece_xai_great_beacon_desc", "A towering beacon of carved stone whose alcoves hold the trophies of the bosses you have slain. It works like a Build Beacon, and each boss trophy set in it raises its level. Creature trophies go on trophy racks beside it." },
                { "xai_great_beacon_mob_refused", "Creature trophies go on a trophy rack next to the Great Beacon" },
                { "xai_beacon_on_holder", "on a holder" },
                { "xai_beacon_no_bosses", "No boss trophies yet" },
                { "xai_beacon_no_bosses_hint", "Build a boss trophy holder next to the beacon" },
                { "xai_beacon_open", "Open" },
                { "xai_beacon_radius", "Radius" },
                { "xai_beacon_level", "Level" },
                { "xai_beacon_boss", "Boss" },
                { "xai_beacon_mob", "Mob" },
                { "xai_beacon_boss_section", "Boss trophies" },
                { "xai_beacon_mob_section", "Mob trophies" },
                { "xai_beacon_free", "Free" },
                { "xai_beacon_tab_trophies", "Trophies" },
                { "xai_beacon_tab_discounts", "Discounts" },
                { "xai_beacon_filter_placeholder", "Filter by material or trophy..." },
                { "xai_beacon_filter_none", "Nothing matches" },
                { "xai_beacon_levels_note", "Discount levels from creature trophies add up, and each level becomes a percentage off. Grey segments are out of reach for that material. Hover a row for details." },
                { "xai_beacon_level_short", "Lv" },
                { "xai_beacon_level_tooltip", "Level {0} of {1}: {2}% off" },
                { "xai_beacon_level_multiplier", "Effective multiplier: {0}x" },
                { "xai_beacon_free_tooltip", "Free: a boss trophy makes it cost nothing" },
                { "xai_beacon_boss_tooltip", "Level {0}: {1}% off" },
                { "xai_beacon_level_over", "The trophies give {0} levels; level {1} is the top." },
                { "xai_beacon_stackable", "Stackable" },
                { "xai_beacon_mob_duplicate", "That trophy is already slotted, and a second one adds nothing" },
                { "xai_beacon_no_discounts", "No discounts yet" },
                { "xai_beacon_no_discounts_hint", "Slot trophies on the Trophies tab" },
                { "xai_beacon_hint_slots", "Boss trophies sit on holders next to the beacon: they make their materials free (or nearly) and grow it. Creature trophies give discount levels that add up." },
                { "xai_beacon_hint_picker_boss", "Choose a boss trophy from your inventory to slot." },
                { "xai_beacon_hint_picker_mob", "Choose a creature trophy from your inventory to slot." },
                { "xai_beacon_empty_boss_slot", "Empty boss slot" },
                { "xai_beacon_empty_mob_slot", "Empty mob slot" },
                { "xai_beacon_click_to_add_boss", "Click to add a boss trophy" },
                { "xai_beacon_click_to_add_mob", "Click to add a mob trophy" },
                { "xai_beacon_boss_full", "No free boss trophy slots" },
                { "xai_beacon_mob_full", "No free creature trophy slots in the beacon. Build a trophy rack for more." },
                { "xai_beacon_boss_duplicate", "That boss trophy is already slotted" },
                { "xai_beacon_not_a_trophy", "No rule for this item" },
                { "xai_beacon_busy", "Someone else is using the beacon, try again" },
                { "xai_beacon_no_boss_trophies", "You are not carrying any boss trophies" },
                { "xai_beacon_no_mob_trophies", "You are not carrying any creature trophies with a rule" },
                { "xai_beacon_all_materials", "All materials" },
                { "xai_beacon_add", "Add" },
                { "xai_beacon_remove", "Remove" },
                { "xai_beacon_back", "Back" },
                { "xai_beacon_close", "Close" },
                { "piece_xai_mobrack_column", "Trophy Column" },
                { "piece_xai_mobrack_column_desc", "A carved stone column with four alcoves for creature trophies. Linked to a nearby Build Beacon, it raises the beacon's level and adds four creature trophy slots." },
                { "piece_xai_mobrack_wall", "Trophy Panel" },
                { "piece_xai_mobrack_wall_desc", "A square stone panel for a wall, with four alcoves for creature trophies. Linked to a nearby Build Beacon, it raises the beacon's level and adds four creature trophy slots. Panels tile edge to edge." },
                { "xai_rack_empty", "Empty" },
                { "xai_rack_empty_hint", "Use a creature trophy on this alcove to place it" },
                { "xai_rack_place", "Place creature trophy" },
                { "xai_rack_take", "Take trophy" },
                { "xai_rack_mob_only", "Only creature trophies go here; boss trophies go on a boss trophy holder" },
                { "xai_rack_not_counted", "Not counted: already in this beacon's network" },
                { "xai_rack_no_level", "The beacon is at its top level; this rack adds slots but no level" },
                { "xai_rack_hint", "Look at an alcove to place or take a trophy" },
                { "xai_beacon_on_rack", "on a rack" },
                { "xai_beacon_racks_section", "On racks" },
                { "xai_beacon_not_counted", "not counted" },
                { "xai_savings_buff_off", "-{0} {1}" },
                { "xai_savings_buff_off_tooltip", "Your {0}% beacon discount has saved up a whole {2} from rounding: this piece costs {1} {2} less." },
                { "xai_savings_buff_free", "{0} free" },
                { "xai_savings_buff_free_tooltip", "Your {0}% beacon discount has saved up enough {1}: the next {2} of this piece are free." },
                { "xai_tutorial_beacon_topic", "You have built a beacon" },
                { "xai_tutorial_beacon_label", "Hugin: Build Beacon" },
                { "xai_tutorial_beacon_text", "This beacon honours those who have slain the Forsaken. Build <color=yellow>trophy holders</color> near it and hang a Forsaken trophy on each. The materials of that boss's lands will then be <color=yellow>free</color>, or nearly, to build with inside the beacon's ring.\n\nEach holder widens the ring. Lesser trophies set in the beacon itself lighten other costs." },
            });
        }

        private void Update() => RulesFile.PollAll();

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }
}
