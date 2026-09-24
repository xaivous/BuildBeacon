using System;
using BepInEx.Configuration;

namespace BuildBeacon
{
    public enum StackMode { Max, Sum }
    public enum RefundMode { PaidCost, CurrentCost }
    /// <summary>How slotted trophies sit in the beacon's alcoves. Numbered so the config accepts 1 or 2 as well as the name.</summary>
    public enum TrophyPlacement { Inset = 1, Mounted = 2 }

    public class BeaconConfig
    {
        public event Action SettingChanged;

        public ConfigEntry<bool> Enabled;
        public ConfigEntry<string> LevelPercents;
        public ConfigEntry<float> BossPercent;
        public ConfigEntry<StackMode> Stacking;
        public ConfigEntry<RefundMode> Refund;
        public ConfigEntry<bool> ShowRadius;
        public ConfigEntry<bool> ShowTrophySources;
        public ConfigEntry<bool> ShrinkGreatBeaconTrophies;
        public ConfigEntry<TrophyPlacement> Placement;
        public ConfigEntry<bool> DevMode;

        // No upgrade materials. Level = 1 + the boss holders and trophy racks linked to the beacon, up to MaxLevel;
        // radius grows with the level. The beacon's own creature slots are fixed; racks add theirs to the network.
        public ConfigEntry<float> RadiusAtLevel1;
        public ConfigEntry<float> RadiusPerLevel;
        // The Great Beacon's radius: a larger start, smaller steps (65 m + 5 m a level: 100 m at level 8).
        public ConfigEntry<float> GreatRadiusAtLevel1;
        public ConfigEntry<float> GreatRadiusPerLevel;
        public ConfigEntry<int> MaxLevel;
        // How far a boss holder may stand from its beacon: the holders' StationExtension.m_maxStationDistance.
        public ConfigEntry<float> HolderRange;
        public ConfigEntry<int> BossSlots;
        public ConfigEntry<int> BeaconMobSlots;

        // Boss rules: trophyPrefab|material, one per line. A slotted boss trophy takes BossPercent off that material
        // (100: free) within the radius. Mirrors BuildBeacon.BossRules.txt.
        public ConfigEntry<string> BossRules;

        // Mob (creature) rules: trophyPrefab|material|level, one per line, and trophyPrefab|Stackable. Levels add up
        // across a beacon's trophies and map to a percentage (LevelPercents). Mirrors BuildBeacon.MobRules.txt.
        public ConfigEntry<string> MobRules;

        // Generated from ingredient_trophy_map.csv: every build ingredient mapped to the boss whose biome it belongs to.
        public const string DefaultBossRules =
            "TrophyEikthyr|Wood\n" +
            "TrophyEikthyr|Flint\n" +
            "TrophyEikthyr|Feathers\n" +
            "TrophyEikthyr|Acorns\n" +
            "TrophyEikthyr|Dandelion\n" +
            "TrophyEikthyr|Mushroom\n" +
            "TrophyEikthyr|Raspberries\n" +
            "TrophyTheElder|Finewood\n" +
            "TrophyTheElder|Corewood\n" +
            "TrophyTheElder|Copper\n" +
            "TrophyTheElder|Tin\n" +
            "TrophyTheElder|Bronze\n" +
            "TrophyTheElder|Bronze Nails\n" +
            "TrophyTheElder|Blueberries\n" +
            "TrophyTheElder|Thistle\n" +
            "TrophyTheElder|Fir Cone\n" +
            "TrophyTheElder|Pine Cone\n" +
            "TrophyBonemass|Stone\n" +
            "TrophyBonemass|Rock\n" +
            "TrophyBonemass|Iron\n" +
            "TrophyBonemass|Iron Nails\n" +
            "TrophyBonemass|Guck\n" +
            "TrophyBonemass|Chain\n" +
            "TrophyBonemass|Surtling Core\n" +
            "TrophyBonemass|Coal\n" +
            "TrophyDragonQueen|Silver\n" +
            "TrophyDragonQueen|Silver Necklace\n" +
            "TrophyDragonQueen|Dragon Tear\n" +
            "TrophyDragonQueen|Crystal\n" +
            "TrophyGoblinKing|Black Metal\n" +
            "TrophyGoblinKing|Tar\n" +
            "TrophyGoblinKing|Cloudberries\n" +
            "TrophyGoblinKing|Red Jute\n" +
            "TrophySeekerQueen|Black Marble\n" +
            "TrophySeekerQueen|Yggdrasil Wood\n" +
            "TrophySeekerQueen|Black Core\n" +
            "TrophySeekerQueen|Refined Eitr\n" +
            "TrophySeekerQueen|Sap\n" +
            "TrophySeekerQueen|Wisp\n" +
            "TrophySeekerQueen|Blue Jute\n" +
            "TrophySeekerQueen|Majestic Carapace\n" +
            "TrophySeekerQueen|Sharpening Stone\n" +
            "TrophyFader|Grausten\n" +
            "TrophyFader|Warrior Trophy\n" +
            "TrophyFader|Ashwood\n" +
            "TrophyFader|Flametal\n" +
            "TrophyFader|Bloodstone\n" +
            "TrophyFader|Sulfur\n" +
            "TrophyFader|Charcoal Resin\n" +
            "TrophyFader|Proustite Powder\n" +
            "TrophyFader|Ceramic Plate\n" +
            "TrophyFader|Pot Shard\n" +
            "TrophyFader|Kindled Ribs\n" +
            "TrophyFader|Shield Core\n" +
            "TrophyFader|Torn Spirit";
            // The Deep North has no boss trophy yet (the Elaking is a creature; see DefaultMobRules).

        // Creature trophies give one level each to the materials that creature drops or its biome is known for.
        // Balanced with tools/balance (see docs/tools.md): no material has more than three sources, so none passes
        // level 3. Construction woods and stones reach level 2 from two common trophies of their biome and 3 with a
        // rare one; powerful materials (metals, cores, crystal, Dvergr parts) stop at level 2; drops of a single
        // creature stay at level 1. None is stackable.
        public const string DefaultMobRules =
            "TrophyGreydwarf|Wood|1\n" +
            "TrophyGreydwarf|Stone|1\n" +
            "TrophyGreydwarf|Resin|1\n" +
            "TrophyGreydwarf|Greydwarf Eye|1\n" +
            "TrophyGreydwarfBrute|Wood|1\n" +
            "TrophyGreydwarfBrute|Corewood|1\n" +
            "TrophyGreydwarfBrute|Resin|1\n" +
            "TrophyGreydwarfShaman|Finewood|1\n" +
            "TrophyGreydwarfShaman|Resin|1\n" +
            "TrophyGreydwarfShaman|Greydwarf Eye|1\n" +
            "TrophyGreydwarfShaman|Queen Bee|1\n" +
            "TrophyForestTroll|Finewood|1\n" +
            "TrophyForestTroll|Corewood|1\n" +
            "TrophyForestTroll|Stone|1\n" +
            "TrophyForestTroll|Troll Hide|1\n" +
            "TrophyBjorn|Wood|1\n" +
            "TrophyBjorn|Finewood|1\n" +
            "TrophyBjorn|Corewood|1\n" +
            "TrophyBjorn|Bear Hide|1\n" +
            "TrophyBjorn|Bear Paw|1\n" +
            "TrophySkeleton|Bone Fragments|1\n" +
            "TrophyGhost|Ectoplasm|1\n" +
            "TrophySkeletonHildir|Bone Fragments|1\n" +
            "TrophySkeletonHildir|Coal|1\n" +
            "TrophySkeletonHildir|Surtling Core|1\n" +
            "TrophyBoar|Leather Scraps|1\n" +
            "TrophyDeer|Deer Hide|1\n" +
            "TrophyDeer|Leather Scraps|1\n" +
            "TrophyBlob|Guck|1\n" +
            "TrophyDraugr|Ancient Bark|1\n" +
            "TrophyDraugr|Iron|1\n" +
            "TrophyDraugrElite|Ancient Bark|1\n" +
            "TrophyDraugrElite|Iron|1\n" +
            "TrophyDraugrElite|Chain|1\n" +
            "TrophyLeech|Bloodbag|1\n" +
            "TrophyWraith|Chain|1\n" +
            "TrophyWraith|Ectoplasm|1\n" +
            "TrophyAbomination|Ancient Bark|1\n" +
            "TrophySurtling|Surtling Core|1\n" +
            "TrophySurtling|Coal|1\n" +
            "TrophySkeletonPoison|Bone Fragments|1\n" +
            "TrophyKvastur|Guck|1\n" +
            "TrophyWolf|Wolf Pelt|1\n" +
            "TrophyWolf|Silver|1\n" +
            "TrophyFenring|Fenris Claw|1\n" +
            "TrophyFenring|Wolf Pelt|1\n" +
            "TrophyHatchling|Obsidian|1\n" +
            "TrophyHatchling|Crystal|1\n" +
            "TrophySGolem|Stone|1\n" +
            "TrophySGolem|Crystal|1\n" +
            "TrophySGolem|Obsidian|1\n" +
            "TrophySGolem|Silver|1\n" +
            "TrophyCultist|Fenris Claw|1\n" +
            "TrophyUlv|Scale Hide|1\n" +
            "TrophyUlv|Wolf Pelt|1\n" +
            "TrophyCultist_Hildir|Obsidian|1\n" +
            "TrophyGoblin|Black Metal|1\n" +
            "TrophyGoblinShaman|Tar|1\n" +
            "TrophyGoblinBrute|Black Metal|1\n" +
            "TrophyLox|Lox Pelt|1\n" +
            "TrophyGrowth|Tar|1\n" +
            "TrophyGoblinBruteBrosBrute|Lox Pelt|1\n" +
            "TrophyGoblinBruteBrosShaman|Tar|1\n" +
            "TrophyBjornUndead|Bear Hide|1\n" +
            "TrophyBjornUndead|Bear Paw|1\n" +
            "TrophySeeker|Yggdrasil Wood|1\n" +
            "TrophyTick|Yggdrasil Wood|1\n" +
            "TrophySeekerBrute|Black Marble|1\n" +
            "TrophyDvergr|Black Marble|1\n" +
            "TrophyDvergr|Mechanical Spring|1\n" +
            "TrophyDvergr|Dvergr Extractor|1\n" +
            "TrophyDvergr|Dvergr Lantern|1\n" +
            "TrophyGjall|Yggdrasil Wood|1\n" +
            "TrophyGjall|Black Marble|1\n" +
            "TrophyCharredMelee|Ashwood|1\n" +
            "TrophyCharredMelee|Charred Bone|1\n" +
            "TrophyCharredMelee|Charred Skull|1\n" +
            "TrophyCharredMelee|Warrior Trophy|1\n" +
            "TrophyCharredArcher|Ashwood|1\n" +
            "TrophyCharredArcher|Charred Bone|1\n" +
            "TrophyCharredArcher|Charred Skull|1\n" +
            "TrophyCharredMage|Bloodgold|1\n" +
            "TrophyCharredMage|Charred Cogwheel|1\n" +
            "TrophyMorgen|Grausten|1\n" +
            "TrophyMorgen|Ashwood|1\n" +
            "TrophyMorgen|Morgen Sinew|1\n" +
            "TrophyAsksvin|Grausten|1\n" +
            "TrophyAsksvin|Asksvin Hide|1\n" +
            "TrophyAsksvin|Asksvin Neck|1\n" +
            "TrophyAsksvin|Asksvin Pelvis|1\n" +
            "TrophyAsksvin|Asksvin Ribcage|1\n" +
            "TrophyAsksvin|Asksvin Skull|1\n" +
            "TrophyBlob_Lava|Grausten|1\n" +
            "TrophyBlob_Lava|Flametal|1\n" +
            "TrophyBlob_Lava|Molten Core|1\n" +
            "TrophyVolture|Charred Bone|1\n" +
            "TrophyVolture|Charred Skull|1\n" +
            "TrophyFallenValkyrie|Celestial Feather|1\n" +
            "TrophyFallenValkyrie|Bloodgold|1\n" +
            "TrophyMoose|Timberwood|1\n" +
            "TrophyMoose|Moose Hide|1\n" +
            "TrophyMoose|Moose Sinew|1\n" +
            "TrophyBarka|Timberwood|1\n" +
            "TrophyBarka|Ice|1\n" +
            "TrophyBlob_Frost|Frostcore|1\n" +
            "TrophyBlob_Frost|Ice|1\n" +
            "TrophySeal|Seal Pelt|1\n" +
            "TrophyWrithan|Writhan Roots|1\n" +
            "TrophyWrithan|Timberwood|1\n" +
            "TrophyJotunWitch|Ice|1\n" +
            "TrophyJotunWarrior|Frostcore|1\n" +
            "TrophyElaking|Elaking Hair Bundle|1";

        public BeaconConfig(ConfigFile cfg)
        {
            const string G = "1. General";
            Enabled    = ConfigUtil.Synced(cfg, G, "Enabled", true, "Master switch.");
            LevelPercents = ConfigUtil.Synced(cfg, G, "LevelPercents", "50, 80, 90",
                "Percentage off at each creature discount level, level 1 first; the number of entries is the top level. " +
                "The levels of a beacon's creature trophies add up and stop at the top level. " +
                "The default 50, 80, 90 makes each item go 2x, 5x and 10x as far.");
            // The first curve's default (five levels): move files still holding it to the new one, which the creature
            // rules are balanced for.
            if (LevelPercents.Value.Replace(" ", "") == "25,60,80,90,95")
                LevelPercents.Value = (string)LevelPercents.DefaultValue;
            BossPercent = ConfigUtil.Synced(cfg, G, "BossPercent", 100f,
                "Percentage off a slotted boss trophy takes from its materials. 100 = free, no gathering needed. " +
                "95 = each item goes 20x as far, so building still takes some gathering. Keep it at or above the top " +
                "of LevelPercents: a boss's materials take the boss's discount, never a creature's.",
                new AcceptableValueRange<float>(50f, 100f));
            Stacking   = ConfigUtil.Synced(cfg, G, "Stacking", StackMode.Max, "How overlapping beacons' creature discounts combine. Max = the beacon with the highest level counts. Sum = their levels add up, up to the top level.");
            Refund     = ConfigUtil.Synced(cfg, G, "Refund", RefundMode.PaidCost, "PaidCost = refund what was actually paid. CurrentCost = refund current discounted cost.");
            ShowRadius = ConfigUtil.Local(cfg, G, "ShowRadius", true, "Show the radius ring around beacons while holding a build tool (hammer, hoe, cultivator).");
            ShowTrophySources = ConfigUtil.Local(cfg, G, "ShowTrophySources", true,
                "In the beacon panel, hovering a material on the Discounts tab lists every trophy that can discount it, " +
                "including ones you have not slotted yet. Turn off to find them out for yourself.");
            ShrinkGreatBeaconTrophies = ConfigUtil.Local(cfg, G, "ShrinkGreatBeaconTrophies", true,
                "Size the boss trophies in the Great Beacon's alcoves to fit around the pillar: Eikthyr, the Elder and Moder " +
                "at vanilla size, Bonemass at 75%, the Queen, Yagluth and Fader at 65%. Off: all at vanilla size.");
            Placement  = ConfigUtil.Local(cfg, G, "TrophyPlacement", TrophyPlacement.Mounted,
                "How slotted trophies sit in the beacon's alcoves. " +
                "Inset (1) = scaled down to fit inside the alcove. " +
                "Mounted (2) = vanilla item stand size, back set into the alcove and the rest standing out of it.");

            const string L = "2. Radius & Slots";
            // New keys rather than new defaults on BaseRadius/RadiusPerBossTrophy: a changed default does not reach
            // config files that already hold the old values.
            RadiusAtLevel1      = ConfigUtil.Synced(cfg, L, "RadiusAtLevel1", 30f, "Radius at level 1, with no boss holders linked (m).");
            RadiusPerLevel      = ConfigUtil.Synced(cfg, L, "RadiusPerLevel", 10f, "Extra radius per level (m). Each linked boss holder adds a level.");
            GreatRadiusAtLevel1 = ConfigUtil.Synced(cfg, L, "GreatRadiusAtLevel1", 65f, "The Great Beacon's radius at level 1 (m).");
            GreatRadiusPerLevel = ConfigUtil.Synced(cfg, L, "GreatRadiusPerLevel", 5f, "The Great Beacon's extra radius per level (m). Each boss trophy in its alcoves, linked holder and linked rack adds a level.");
            MaxLevel            = ConfigUtil.Synced(cfg, L, "MaxLevel", 8, "Highest beacon level; holders beyond MaxLevel - 1 add none.", new AcceptableValueRange<int>(1, 32));
            HolderRange         = ConfigUtil.Synced(cfg, L, "HolderRange", 30f, "How far a boss holder may stand from its beacon (m). A holder links to the closest beacon within this distance.", new AcceptableValueRange<float>(2f, 100f));
            BossSlots           = ConfigUtil.Synced(cfg, L, "BossSlots", 8, "Boss trophy slots per beacon. Each boss trophy can be slotted once.", new AcceptableValueRange<int>(1, 16));
            // A new key rather than new defaults on BaseMobSlots/MobSlotsPerBoss: saved values would keep the old growth.
            BeaconMobSlots      = ConfigUtil.Synced(cfg, L, "BeaconMobSlots", 4, "Creature trophy slots in the beacon itself. Fixed: bosses do not add any; each trophy rack adds four of its own to the beacon's network.", new AcceptableValueRange<int>(0, 32));

            const string B = "3. Boss Rules";
            BossRules = ConfigUtil.Synced(cfg, B, "BossRules", DefaultBossRules,
                "Managed by BepInEx/config/" + RulesFile.BossFileName + " on the server; edit that file instead. " +
                "trophyPrefab|material, one per line. A slotted boss trophy takes BossPercent off the material within the radius (100: free). " +
                "Material matches the item's prefab name or display name, spaces and case ignored.");

            const string M = "4. Mob Rules";
            MobRules = ConfigUtil.Synced(cfg, M, "MobRules", DefaultMobRules,
                "Managed by BepInEx/config/" + RulesFile.MobFileName + " on the server; edit that file instead. " +
                "trophyPrefab|material|level, one per line, and trophyPrefab|Stackable for trophies whose every copy counts. " +
                "Levels add up across a beacon's trophies; see LevelPercents.");
            
            // Off by default: shipped on, every player's local worlds would start with devcommands, god mode and fly.
            // The release check (tools/release/release.py) refuses a true default.
            const string D = "5. Dev";
            DevMode = ConfigUtil.Local(cfg, D, "DevMode", false,
                "For mod development. When on, spawning into a world this machine hosts turns on devcommands, god mode " +
                "and debug fly. Does nothing on dedicated servers or when joining someone else's world.");

            cfg.SettingChanged += (_, __) => SettingChanged?.Invoke();
        }
    }
}
