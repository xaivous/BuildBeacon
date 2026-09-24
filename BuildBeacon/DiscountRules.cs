using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BuildBeacon
{
    public enum TrophyKind { None, Boss, Mob }

    public sealed class BossRule
    {
        public string Trophy;
        public string Material;   // as written in the rule, for display
        public string Key;        // normalised for matching; "*" = any
    }

    /// <summary>What one beacon does to one material, for the Discounts tab.</summary>
    public sealed class MaterialDiscount
    {
        public string Name;       // display name
        public ItemDrop Item;     // for the icon; null for "all materials" or a material no item matches
        public bool Boss;         // a boss trophy covers it: Percent is then BossPercent and Level 0
        public bool Free => Boss && Percent >= 100f; // the boss takes all of it off
        public int Level;         // creature discount level, summed over the trophies; may exceed the top level
        public float Percent;     // what the boss or the level takes off
        /// <summary>The trophies responsible: boss trophies when a boss covers it, otherwise the creature trophies with the copies
        /// the beacon counts (1 unless the trophy is stackable).</summary>
        public readonly List<(string trophy, int count)> Sources = new List<(string trophy, int count)>();
    }

    public sealed class MobRule
    {
        public string Trophy;
        public string Material;
        public string Key;
        public int Level;         // discount levels this trophy gives the material
    }

    /// <summary>
    /// Parsed rule tables plus radius/slot math.
    /// Boss rules take BossPercent off a material (free at 100, the default). Mob (creature) rules give a material
    /// discount levels; the levels of
    /// a beacon's trophies add up and map to a percentage off (LevelPercents), capped at the top level. A beacon counts
    /// each trophy once unless the rules mark it Stackable.
    /// A trophy listed in both tables is treated as a boss trophy.
    /// </summary>
    public static class DiscountRules
    {
        public const string AnyMaterial = "*";

        public static List<BossRule> BossRules = new List<BossRule>();
        public static List<MobRule> MobRules = new List<MobRule>();
        /// <summary>Normalised names of trophies whose every slotted copy counts ("Trophy | Stackable" lines).</summary>
        public static HashSet<string> StackableTrophies = new HashSet<string>();

        /// <summary>
        /// Percentage off at each discount level, level 1 first; its length is the top level. The defaults make the
        /// effective multiplier, 100 / (100 - percent), 2x, 5x and 10x; a boss trophy goes past them (BossPercent).
        /// </summary>
        public static readonly float[] DefaultLevelPercents = { 50f, 80f, 90f };
        public static float[] LevelPercents = DefaultLevelPercents;
        public static int TopLevel => LevelPercents.Length;

        /// <summary>What a boss trophy takes off its materials (the BossPercent setting).</summary>
        public static float BossPercent => Math.Max(0f, Math.Min(100f, BuildBeaconPlugin.Cfg.BossPercent.Value));

        /// <summary>True when a boss trophy makes its materials cost nothing (BossPercent 100).</summary>
        public static bool BossMakesFree => BossPercent >= 100f - Epsilon;

        public static void Rebuild()
        {
            var cfg = BuildBeaconPlugin.Cfg;

            BossRules = RulesFile.ParseLines(cfg.BossRules.Value, RulesFile.NormalizeBossLine, out var badBoss)
                .Select(l => l.Split('|'))
                .Select(p => new BossRule { Trophy = p[0], Material = p[1], Key = Normalize(p[1]) })
                .ToList();
            foreach (var bad in badBoss) BuildBeaconPlugin.Log.LogWarning($"Ignoring malformed boss rule: \"{bad}\"");

            var mobLines = RulesFile.ParseLines(cfg.MobRules.Value, RulesFile.NormalizeMobLine, out var badMob)
                .Select(l => l.Split('|')).ToList();
            MobRules = mobLines
                .Where(p => p.Length == 3)
                .Select(p => new MobRule
                {
                    Trophy = p[0], Material = p[1], Key = Normalize(p[1]),
                    Level = int.Parse(p[2], CultureInfo.InvariantCulture),
                })
                .ToList();
            StackableTrophies = new HashSet<string>(mobLines.Where(p => p.Length == 2).Select(p => Normalize(p[0])));
            foreach (var bad in badMob) BuildBeaconPlugin.Log.LogWarning($"Ignoring malformed mob rule: \"{bad}\"");
            LevelPercents = ParseLevelPercents(cfg.LevelPercents.Value);
            BuildTrophyIndex();

            KeyCache.Clear();
            s_maxLevel.Clear();
            s_maxLevelAll = -1;
            s_contributors.Clear();
            s_contributorsAll = null;
            BuildBeaconPlugin.Log.LogInfo(
                $"Loaded {BossRules.Select(r => r.Trophy).Distinct(StringComparer.OrdinalIgnoreCase).Count()} boss trophies " +
                $"({BossRules.Count} materials at {BossPercent.ToString("0.#", CultureInfo.InvariantCulture)}% off), {MobRules.Count} creature rules ({StackableTrophies.Count} stackable trophies), " +
                $"levels {string.Join("/", LevelPercents.Select(v => v.ToString("0.#", CultureInfo.InvariantCulture)))}%");
            ValidateAgainstObjectDB(); // no-op before the item database exists; the plugin calls it again once it does
        }

        // ---- Trophy classification ----

        // The rules indexed by normalised trophy name, built with the tables (Rebuild). Every lookup by trophy goes
        // through these: scanning the rules and normalising both names for each one cost ~365 string allocations per
        // trophy per material, and the build code asks many times a frame (and once per unknown piece on every
        // inventory change).
        private static Dictionary<string, List<BossRule>> s_bossByTrophy = new Dictionary<string, List<BossRule>>();
        private static Dictionary<string, List<MobRule>> s_mobByTrophy = new Dictionary<string, List<MobRule>>();
        private static Dictionary<string, int> s_bossOrder = new Dictionary<string, int>();
        private static readonly List<BossRule> NoBossRules = new List<BossRule>();
        private static readonly List<MobRule> NoMobRules = new List<MobRule>();

        /// <summary>Normalised trophy names, by prefab name. Trophy names come from a fixed set (item prefabs and rule
        /// lines), so this stays small.</summary>
        private static readonly Dictionary<string, string> s_trophyKeys = new Dictionary<string, string>();

        private static void BuildTrophyIndex()
        {
            var boss = new Dictionary<string, List<BossRule>>();
            var order = new Dictionary<string, int>();
            for (int i = 0; i < BossRules.Count; i++)
            {
                var key = TrophyKey(BossRules[i].Trophy);
                if (!boss.TryGetValue(key, out var list)) boss[key] = list = new List<BossRule>();
                list.Add(BossRules[i]);
                if (!order.ContainsKey(key)) order[key] = i;
            }
            var mob = new Dictionary<string, List<MobRule>>();
            foreach (var r in MobRules)
            {
                var key = TrophyKey(r.Trophy);
                if (!mob.TryGetValue(key, out var list)) mob[key] = list = new List<MobRule>();
                list.Add(r);
            }
            s_bossByTrophy = boss;
            s_mobByTrophy = mob;
            s_bossOrder = order;
        }

        /// <summary>A trophy name normalised for matching (see <see cref="Normalize"/>), cached.</summary>
        public static string TrophyKey(string prefab)
        {
            if (prefab == null) return "";
            if (!s_trophyKeys.TryGetValue(prefab, out var key)) s_trophyKeys[prefab] = key = Normalize(prefab);
            return key;
        }

        /// <summary>Trophy names match like materials: case, spaces and underscores ignored, so TrophyBlob_Lava == TrophyBlobLava.</summary>
        private static bool TrophyMatches(string ruleTrophy, string prefab) => TrophyKey(ruleTrophy) == TrophyKey(prefab);

        public static bool IsBossTrophy(string prefab) => s_bossByTrophy.ContainsKey(TrophyKey(prefab));

        /// <summary>Position of a boss trophy in progression, from the order of the boss rules file (Eikthyr first).
        /// Non-boss trophies sort last.</summary>
        public static int BossOrder(string prefab) => s_bossOrder.TryGetValue(TrophyKey(prefab), out var i) ? i : int.MaxValue;

        public static bool IsMobTrophy(string prefab) => !IsBossTrophy(prefab) && s_mobByTrophy.ContainsKey(TrophyKey(prefab));

        public static TrophyKind KindOf(string prefab) =>
            IsBossTrophy(prefab) ? TrophyKind.Boss : IsMobTrophy(prefab) ? TrophyKind.Mob : TrophyKind.None;

        /// <summary>The boss rules for a trophy, in rules order; empty when it has none. Do not modify.</summary>
        public static List<BossRule> BossRulesFor(string prefab) =>
            s_bossByTrophy.TryGetValue(TrophyKey(prefab), out var list) ? list : NoBossRules;

        /// <summary>The creature rules for a trophy, in rules order; empty when it has none. Do not modify.</summary>
        public static List<MobRule> MobRulesFor(string prefab) =>
            s_mobByTrophy.TryGetValue(TrophyKey(prefab), out var list) ? list : NoMobRules;

        public static bool IsStackable(string prefab) => StackableTrophies.Contains(TrophyKey(prefab));

        /// <summary>How many of the slotted copies of a trophy a beacon counts: all when stackable, else one.</summary>
        public static int CountedCopies(string prefab, int slotted) => slotted <= 0 ? 0 : IsStackable(prefab) ? slotted : 1;

        /// <summary>"50, 80, 90" to percentages (clamped to 0-100); the defaults when it does not parse.</summary>
        private static float[] ParseLevelPercents(string text)
        {
            var list = new List<float>();
            foreach (var part in (text ?? "").Split(','))
            {
                if (!float.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    BuildBeaconPlugin.Log.LogWarning($"LevelPercents \"{text}\" is not a list of numbers; using {string.Join(", ", DefaultLevelPercents)}");
                    return DefaultLevelPercents;
                }
                list.Add(Math.Max(0f, Math.Min(100f, v)));
            }
            return list.Count > 0 ? list.ToArray() : DefaultLevelPercents;
        }

        /// <summary>The percentage a discount level takes off: 0 below level 1, the top level's above it.</summary>
        public static float PercentForLevel(int level) =>
            level <= 0 ? 0f : LevelPercents[Math.Min(level, LevelPercents.Length) - 1];

        // ---- Validation against the item database ----

        /// <summary>
        /// Warn about rule trophies and materials that do not match any item. Runs once the ObjectDB is populated
        /// and again after each rules reload, so a typo in a rules file shows up in the log straight away.
        /// </summary>
        public static void ValidateAgainstObjectDB()
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0) return;

            var known = new HashSet<string>();
            foreach (var go in ObjectDB.instance.m_items)
            {
                var drop = go != null ? go.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                foreach (var key in MaterialKeys(drop)) known.Add(key);
            }

            var trophies = BossRules.Select(r => r.Trophy).Concat(MobRules.Select(r => r.Trophy))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var t in trophies)
                if (!known.Contains(Normalize(t)))
                    BuildBeaconPlugin.Log.LogWarning($"Rule trophy \"{t}\" does not match any item; it can never be slotted.");

            var materials = BossRules.Select(r => (r.Material, r.Key)).Concat(MobRules.Select(r => (r.Material, r.Key)))
                .Where(m => m.Key != AnyMaterial)
                .GroupBy(m => m.Key).Select(g => g.First());
            foreach (var m in materials)
                if (!known.Contains(m.Key))
                    BuildBeaconPlugin.Log.LogWarning($"Rule material \"{m.Material}\" does not match any item's prefab or display name; the rule has no effect.");
        }

        // ---- Radius and slots, driven by slotted boss trophy count ----

        /// <summary>Beacon level from the number of linked boss holders: 1 + holders, capped at MaxLevel.</summary>
        public static int LevelFor(int holderCount) =>
            1 + Math.Max(0, Math.Min(holderCount, BuildBeaconPlugin.Cfg.MaxLevel.Value - 1));

        public static float RadiusFor(int level) =>
            BuildBeaconPlugin.Cfg.RadiusAtLevel1.Value + (level - 1) * BuildBeaconPlugin.Cfg.RadiusPerLevel.Value;

        public static float GreatRadiusFor(int level) =>
            BuildBeaconPlugin.Cfg.GreatRadiusAtLevel1.Value + (level - 1) * BuildBeaconPlugin.Cfg.GreatRadiusPerLevel.Value;


        // ---- Material matching ----

        /// <summary>Lowercase, no whitespace or underscores, so "Fine Wood", "FineWood" and "finewood" all match.</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (!char.IsWhiteSpace(c) && c != '_') sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        private static readonly Dictionary<ItemDrop, string[]> KeyCache = new Dictionary<ItemDrop, string[]>();

        /// <summary>Keys a requirement item can be matched by: its prefab name and its localised display name.</summary>
        public static string[] MaterialKeys(ItemDrop item)
        {
            if (item == null) return Array.Empty<string>();
            if (KeyCache.TryGetValue(item, out var keys)) return keys;

            var list = new List<string> { Normalize(item.name) };
            bool cacheable = true;
            try
            {
                if (Localization.instance != null)
                {
                    var norm = Normalize(Localization.instance.Localize(item.m_itemData.m_shared.m_name));
                    if (norm.Length > 0 && !norm.StartsWith("$") && !list.Contains(norm)) list.Add(norm);
                }
                else cacheable = false;
            }
            catch { cacheable = false; }

            keys = list.ToArray();
            if (cacheable) KeyCache[item] = keys;
            return keys;
        }

        private static bool Matches(string ruleKey, string[] itemKeys) =>
            ruleKey == AnyMaterial || Array.IndexOf(itemKeys, ruleKey) >= 0;

        // ---- Discount evaluation for one requirement ----

        /// <summary>True if any slotted boss trophy has a rule for this material (it then takes BossPercent off).</summary>
        public static bool BossCovers(IReadOnlyDictionary<string, int> trophies, ItemDrop item)
        {
            var keys = MaterialKeys(item);
            foreach (var kv in trophies)
            {
                if (kv.Value <= 0) continue;
                foreach (var r in BossRulesFor(kv.Key))
                    if (Matches(r.Key, keys)) return true;
            }
            return false;
        }

        /// <summary>
        /// The discount level a beacon's creature trophies give one material: each counted trophy's rule levels added
        /// up (a stackable trophy counts every copy). Not capped here; PercentForLevel caps at the top level.
        /// </summary>
        private static readonly Dictionary<ItemDrop, int> s_maxLevel = new Dictionary<ItemDrop, int>();
        private static int s_maxLevelAll = -1;

        /// <summary>
        /// The highest creature discount level a material can reach from the rules: one copy of every creature trophy
        /// with a rule for it (as MobLevel adds them), capped at the top level; a stackable trophy with a rule for it
        /// takes it to the top, since its copies add up without limit. Boss rules, and trophies that are boss trophies,
        /// are ignored. <paramref name="item"/> null means the "all materials" rules only (the Discounts tab's "*" row).
        /// Worked out once per material and kept until the rules or the level table change (Rebuild clears it).
        /// </summary>
        public static int MaxMobLevel(ItemDrop item)
        {
            if (item == null)
            {
                if (s_maxLevelAll < 0) s_maxLevelAll = SumMaxLevel(r => r.Key == AnyMaterial);
                return s_maxLevelAll;
            }
            if (s_maxLevel.TryGetValue(item, out var cached)) return cached;
            var keys = MaterialKeys(item);
            return s_maxLevel[item] = SumMaxLevel(r => Matches(r.Key, keys));
        }

        private static readonly Dictionary<ItemDrop, List<(string prefab, bool boss)>> s_contributors =
            new Dictionary<ItemDrop, List<(string prefab, bool boss)>>();
        private static List<(string prefab, bool boss)> s_contributorsAll;

        /// <summary>
        /// Every trophy that can discount a material, from the rules: boss trophies that free it (progression order),
        /// then creature trophies with a rule for it (rules order), each once. <paramref name="item"/> null means the
        /// "all materials" rules only. Cached per material until the rules change (Rebuild clears it).
        /// </summary>
        public static IReadOnlyList<(string prefab, bool boss)> ContributorsFor(ItemDrop item)
        {
            if (item == null)
            {
                if (s_contributorsAll == null) s_contributorsAll = CollectContributors(k => k == AnyMaterial);
                return s_contributorsAll;
            }
            if (s_contributors.TryGetValue(item, out var cached)) return cached;
            var keys = MaterialKeys(item);
            return s_contributors[item] = CollectContributors(k => Matches(k, keys));
        }

        private static List<(string prefab, bool boss)> CollectContributors(Func<string, bool> applies)
        {
            var list = new List<(string prefab, bool boss)>();
            bool Listed(string trophy) => list.Any(x => TrophyMatches(x.prefab, trophy));
            foreach (var r in BossRules)
                if (applies(r.Key) && !Listed(r.Trophy)) list.Add((r.Trophy, true));
            foreach (var r in MobRules)
                if (applies(r.Key) && !IsBossTrophy(r.Trophy) && !Listed(r.Trophy)) list.Add((r.Trophy, false));
            return list;
        }

        private static int SumMaxLevel(Func<MobRule, bool> applies)
        {
            int level = 0;
            foreach (var r in MobRules)
            {
                if (!applies(r) || IsBossTrophy(r.Trophy)) continue;
                if (IsStackable(r.Trophy)) return TopLevel;
                level += r.Level;
            }
            return Math.Min(level, TopLevel);
        }

        public static int MobLevel(IReadOnlyDictionary<string, int> trophies, ItemDrop item)
        {
            var keys = MaterialKeys(item);
            int level = 0;
            foreach (var kv in trophies)
            {
                int copies = CountedCopies(kv.Key, kv.Value);
                if (copies == 0) continue;
                foreach (var r in MobRulesFor(kv.Key))
                    if (Matches(r.Key, keys)) level += r.Level * copies;
            }
            return level;
        }

        /// <summary>Tolerance for float maths on costs: 10 * (1 - 20/100f) is 8.0000001, which must round to 8, not 9.</summary>
        public const float Epsilon = 1e-4f;

        /// <summary>
        /// The cost before rounding: the base with the percentage taken off. It may be a fraction of an item (95% off
        /// 2 Wood is 0.1); the rounding savings (DiscountSavings) turn what rounding up overpays into cheaper pieces
        /// later, so the percentage averages out to its true value.
        /// </summary>
        public static float ExactMobCost(int baseAmount, float percent) =>
            Math.Max(0f, Math.Min(baseAmount, baseAmount * (1f - Math.Min(percent, 100f) / 100f)));

        /// <summary>Round a cost up to whole items.</summary>
        public static int RoundCost(float exact) => (int)Math.Ceiling(exact - Epsilon);

        // ---- Display helpers ----

        public static string MaterialName(string material) =>
            material == AnyMaterial ? Localization.instance.Localize("$xai_beacon_all_materials") : material;

        private static string LevelShort => Localization.instance.Localize("$xai_beacon_level_short");

        /// <summary>"Wood +1 Lv"</summary>
        public static string Describe(MobRule r) => $"{MaterialName(r.Material)} +{r.Level} {LevelShort}";

        /// <summary>"Lv 2", or "Lv 5+" when the summed level is past the top.</summary>
        public static string LevelText(int level) =>
            $"{LevelShort} {Math.Min(level, TopLevel)}{(level > TopLevel ? "+" : "")}";

        /// <summary>
        /// Boss trophy: "Free: Fine Wood, Core Wood, ..." ("-95%: ..." below BossPercent 100). Creature trophy: one "Material +n Lv" per line, then
        /// "Stackable" when every copy counts.
        /// </summary>
        public static string DescribeTrophy(string prefab)
        {
            if (IsBossTrophy(prefab))
            {
                var mats = BossRulesFor(prefab).Select(r => MaterialName(r.Material)).Distinct().ToList();
                return $"{BossAmount}: {string.Join(", ", mats)}";
            }
            var lines = MobRulesFor(prefab).Select(Describe).ToList();
            if (IsStackable(prefab)) lines.Add(Localization.instance.Localize("$xai_beacon_stackable"));
            return string.Join("\n", lines);
        }

        public static string ItemDisplayName(string prefab)
        {
            var drop = ItemDropFor(prefab);
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : null;
        }

        // ---- Per-material breakdown (Discounts tab) ----

        private static Dictionary<string, ItemDrop> s_itemByKey;
        private static ObjectDB s_itemByKeyDb;

        /// <summary>The item a rule's material names: prefab name first, then display name, matched like the rules
        /// match requirements. Null for "*" or when nothing matches.</summary>
        public static ItemDrop ItemForMaterialKey(string key)
        {
            if (key == AnyMaterial || ObjectDB.instance == null || ObjectDB.instance.m_items == null) return null;
            if (s_itemByKey == null || s_itemByKeyDb != ObjectDB.instance)
            {
                if (ObjectDB.instance.m_items.Count == 0) return null;
                var map = new Dictionary<string, ItemDrop>();
                var drops = ObjectDB.instance.m_items.Where(go => go != null).Select(go => go.GetComponent<ItemDrop>()).Where(d => d != null).ToList();
                foreach (var d in drops) { var k = Normalize(d.name); if (!map.ContainsKey(k)) map[k] = d; }
                foreach (var d in drops) foreach (var k in MaterialKeys(d)) if (!map.ContainsKey(k)) map[k] = d;
                s_itemByKey = map;
                s_itemByKeyDb = ObjectDB.instance;
            }
            return s_itemByKey.TryGetValue(key, out var drop) ? drop : null;
        }

        /// <summary>
        /// Every material the slotted trophies affect, one entry per material. Boss rules take BossPercent off and win
        /// over any creature discount; creature levels add up per counted copy, as MobLevel does when building. Boss materials first,
        /// then discounted ones, each by name.
        /// </summary>
        public static List<MaterialDiscount> Breakdown(IReadOnlyDictionary<string, int> trophies)
        {
            var rows = new Dictionary<string, MaterialDiscount>();
            MaterialDiscount RowFor(string key, string material)
            {
                var item = ItemForMaterialKey(key);
                var id = item != null ? item.name : key;
                if (!rows.TryGetValue(id, out var row))
                {
                    row = new MaterialDiscount
                    {
                        Item = item,
                        Name = item != null ? Localization.instance.Localize(item.m_itemData.m_shared.m_name) : MaterialName(material),
                    };
                    rows[id] = row;
                }
                return row;
            }
            void AddSource(MaterialDiscount row, string trophy, int count)
            {
                if (!row.Sources.Any(s => s.trophy == trophy)) row.Sources.Add((trophy, count));
            }

            var slotted = trophies.Where(kv => kv.Value > 0).ToList();
            var freeAll = new List<string>(); // boss trophies with an "all materials" rule
            foreach (var kv in slotted)
                foreach (var r in BossRulesFor(kv.Key))
                {
                    var row = RowFor(r.Key, r.Material);
                    row.Boss = true;
                    AddSource(row, kv.Key, kv.Value);
                    if (r.Key == AnyMaterial && !freeAll.Contains(kv.Key)) freeAll.Add(kv.Key);
                }
            foreach (var kv in slotted)
            {
                int copies = CountedCopies(kv.Key, kv.Value);
                foreach (var r in MobRulesFor(kv.Key))
                {
                    var row = RowFor(r.Key, r.Material);
                    if (row.Boss) continue;
                    row.Level += r.Level * copies;
                    AddSource(row, kv.Key, copies);
                }
            }
            if (freeAll.Count > 0)
                foreach (var row in rows.Values.Where(x => !x.Boss))
                {
                    row.Boss = true;
                    row.Level = 0;
                    row.Sources.Clear();
                    foreach (var t in freeAll) row.Sources.Add((t, 1));
                }

            foreach (var row in rows.Values) row.Percent = row.Boss ? BossPercent : PercentForLevel(row.Level);
            return rows.Values.OrderBy(r => r.Boss ? 0 : 1).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>What a boss trophy does to its materials: "Free", or "-95%" below BossPercent 100.</summary>
        public static string BossAmount =>
            BossMakesFree
                ? Localization.instance.Localize("$xai_beacon_free")
                : $"-{BossPercent.ToString("0.#", CultureInfo.InvariantCulture)}%";

        /// <summary>How much further each item goes at a discount: 100 / (100 - percent), "20" at 95%, "∞" at 100%.</summary>
        public static string MultiplierText(float percent) =>
            percent >= 100f ? "∞" : (100f / (100f - percent)).ToString("0.##", CultureInfo.InvariantCulture);

        public static ItemDrop ItemDropFor(string prefab)
        {
            if (ObjectDB.instance == null) return null;
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            return go != null ? go.GetComponent<ItemDrop>() : null;
        }
    }
}
