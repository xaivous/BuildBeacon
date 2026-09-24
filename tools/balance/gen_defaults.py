"""Generate BuildBeacon/Package/DEFAULT_DISCOUNTS.md (the Thunderstore defaults list) from the default rules in
BeaconConfig.cs.

Run from anywhere:  uv run --no-project python tools/balance/gen_defaults.py
"""
import os
import re
from collections import OrderedDict

from matrix_data import LEVEL_PCT  # the LevelPercents default

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")) + os.sep
src = open(ROOT + "BuildBeacon/BeaconConfig.cs", encoding="utf-8").read()
NL = chr(92) + "n"  # the two characters backslash, n as written inside the C# string literals


def rules(name):
    a = src.index(f"public const string {name} =")
    b = src.index(";", a)
    text = "".join(re.findall(r'"([^"]*)"', src[a:b]))
    return [line for line in text.split(NL) if line.strip()]


# Readable names. Materials: the rule text, with two compound words split the way the game shows them.
MATERIAL = {"Finewood": "Fine Wood", "Corewood": "Core Wood"}
BOSS = OrderedDict([
    ("TrophyEikthyr", ("Eikthyr", "Meadows")),
    ("TrophyTheElder", ("The Elder", "Black Forest")),
    ("TrophyBonemass", ("Bonemass", "Swamp")),
    ("TrophyDragonQueen", ("Moder", "Mountains")),
    ("TrophyGoblinKing", ("Yagluth", "Plains")),
    ("TrophySeekerQueen", ("The Queen", "Mistlands")),
    ("TrophyFader", ("Fader", "Ashlands")),
])
MOB = {
    "TrophyGreydwarf": "Greydwarf", "TrophyGreydwarfBrute": "Greydwarf Brute", "TrophyGreydwarfShaman": "Greydwarf Shaman",
    "TrophyForestTroll": "Troll", "TrophyDeer": "Deer", "TrophyBoar": "Boar", "TrophySkeleton": "Skeleton",
    "TrophyDraugr": "Draugr", "TrophyGhost": "Ghost", "TrophyLeech": "Leech", "TrophyAbomination": "Abomination",
    "TrophySurtling": "Surtling", "TrophyWraith": "Wraith", "TrophyWolf": "Wolf", "TrophyFenring": "Fenring",
    "TrophyHatchling": "Drake", "TrophySGolem": "Stone Golem", "TrophyLox": "Lox", "TrophyGrowth": "Growth",
    "TrophyGoblin": "Fuling", "TrophyGoblinBrute": "Fuling Berserker", "TrophyDvergr": "Dvergr",
    "TrophyCharredMelee": "Charred Warrior", "TrophyCharredArcher": "Charred Marksman",
    "TrophyCharredMage": "Charred Warlock", "TrophyMorgen": "Morgen", "TrophyBlob_Lava": "Lava Blob",
    "TrophyAsksvin": "Asksvin", "TrophyFallenValkyrie": "Fallen Valkyrie", "TrophyMoose": "Moose",
    "TrophyBjorn": "Bear", "TrophyBlob_Frost": "Frost Blob", "TrophyUlv": "Ulv", "TrophySeal": "Seal",
    "TrophyWrithan": "Writhan", "TrophyDraugrElite": "Draugr Elite", "TrophyBlob": "Blob",
    "TrophySkeletonPoison": "Rancid Remains", "TrophyKvastur": "Kvastur", "TrophyCultist": "Cultist",
    "TrophyCultist_Hildir": "Geirrhafa", "TrophyGoblinShaman": "Fuling Shaman", "TrophyGoblinBruteBrosBrute": "Thungr",
    "TrophyGoblinBruteBrosShaman": "Zil", "TrophyBjornUndead": "Vile", "TrophySeeker": "Seeker",
    "TrophySeekerBrute": "Seeker Soldier", "TrophyTick": "Tick", "TrophyGjall": "Gjall", "TrophyVolture": "Volture",
    "TrophyBarka": "Barka", "TrophyJotunWitch": "Hexen", "TrophyJotunWarrior": "Krigen", "TrophySkeletonHildir": "Brenna",
    "TrophyElaking": "Elaking",
}


def mat(m):
    return MATERIAL.get(m, m)


boss_rules = OrderedDict()
for line in rules("DefaultBossRules"):
    trophy, material = [p.strip() for p in line.split("|")]
    boss_rules.setdefault(trophy, []).append(mat(material))

mob_rules = OrderedDict()
stackable = set()
for line in rules("DefaultMobRules"):
    parts = [p.strip() for p in line.split("|")]
    if len(parts) == 2 and parts[1].lower() == "stackable":
        stackable.add(parts[0])
        continue
    trophy, material, level = parts
    mob_rules.setdefault(trophy, []).append((mat(material), int(level)))

unknown = [t for t in list(boss_rules) if t not in BOSS] + [t for t in mob_rules if t not in MOB]
assert not unknown, unknown

out = []
out.append("# BuildBeacon default trophy rules")
out.append("")
out.append("These are the rules BuildBeacon ships with. They are written to `BepInEx/config/BuildBeacon.BossRules.txt` and "
           "`BuildBeacon.MobRules.txt` the first time the mod runs; after that, those files are what counts, and a server "
           "can change them freely (see the main page). Trophy names in the right-hand column are the names the rules "
           "files use.")
out.append("")
n_boss_mats = sum(len(v) for v in boss_rules.values())
out.append(f"## Boss trophies: materials made free ({len(boss_rules)} bosses, {n_boss_mats} materials)")
out.append("")
out.append("Hang a boss trophy on a Boss Trophy Pillar or Boss Trophy Mount linked to a beacon, or set it in a Great "
           "Beacon, and every material listed for it is discounted inside that beacon's radius: **free** by default, or "
           "95% off (20x) on servers that set `BossPercent = 95` to keep some gathering. Each boss counts once per beacon.")
out.append("")
out.append("| Boss | Biome | Materials | Rules file name |")
out.append("|---|---|---|---|")
for trophy, mats in boss_rules.items():
    name, biome = BOSS[trophy]
    out.append(f"| {name} | {biome} | {', '.join(mats)} | `{trophy}` |")
out.append("")
n_mob = sum(len(v) for v in mob_rules.values())
all_one = all(level == 1 for entries in mob_rules.values() for _, level in entries)
out.append(f"## Creature trophies: discount levels ({len(mob_rules)} trophies, {n_mob} rules)")
out.append("")
out.append("Slot a creature trophy in the beacon or on a trophy rack, and each material listed for it gains "
           + ("a discount level" if all_one else "that many discount levels")
           + " inside the beacon's radius. Levels from different trophies add up, and each level takes more off:")
out.append("")
out.append("| Level | " + " | ".join(str(i) for i in range(1, len(LEVEL_PCT) + 1)) + " |")
out.append("|---|" + "---|" * len(LEVEL_PCT))
out.append("| Discount | " + " | ".join(f"{p}%" for p in LEVEL_PCT) + " |")
out.append("| Multiplier | " + " | ".join(f"{100 / (100 - p):.2f}".rstrip("0").rstrip(".") + "x" for p in LEVEL_PCT) + " |")
out.append("")
out.append(f"Level {len(LEVEL_PCT)} is the top: no material has more than {len(LEVEL_PCT)} creature trophies. Common "
           "building woods and stones reach level 2 from two common creatures of their biome and level 3 with a rarer "
           "one; metals, cores and other powerful materials stop at level 2. A beacon counts each creature trophy once; "
           + ("none of the defaults is stackable. " if not stackable else "stackable trophies count every copy. ")
           + "Only a boss trophy goes past level 3.")
out.append("")
out.append("| Creature | " + ("Materials (+1 level each)" if all_one else "Discount levels") + " | Rules file name |")
out.append("|---|---|---|")
for trophy, entries in mob_rules.items():
    parts = [material if all_one else f"{material} +{level}" for material, level in entries]
    suffix = " (stackable)" if trophy in stackable else ""
    out.append(f"| {MOB[trophy]}{suffix} | {', '.join(parts)} | `{trophy}` |")
out.append("")
path = ROOT + "BuildBeacon/Package/DEFAULT_DISCOUNTS.md"
open(path, "w", encoding="utf-8", newline="\n").write("\n".join(out))
print(f"wrote {path}: {len(boss_rules)} bosses / {n_boss_mats} boss rules, {len(mob_rules)} creatures / {n_mob} creature rules, {len(stackable)} stackable")
