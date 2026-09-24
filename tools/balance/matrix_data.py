"""The data behind the trophy balance matrix, shared by gen_matrix.py (the maintainers' page) and
tools/site/build_site.py (the players' matrix tool on the GitHub Pages site).

The rules come from DefaultBossRules and DefaultMobRules in BuildBeacon/BeaconConfig.cs; the trophy catalogue (every
trophy in the game, by biome), the rare set and the balance targets live here.
"""
import os
import re
from collections import OrderedDict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", "..")) + os.sep
src = open(ROOT + "BuildBeacon/BeaconConfig.cs", encoding="utf-8").read()
NL = chr(92) + "n"


def rules(name):
    a = src.index(f"public const string {name} =")
    b = src.index(";", a)
    text = "".join(re.findall(r'"([^"]*)"', src[a:b]))
    return [[p.strip() for p in line.split("|")] for line in text.split(NL) if line.strip()]


def key(m):
    return m.lower().replace(" ", "").replace("_", "")


PRETTY = {"finewood": "Fine Wood", "corewood": "Core Wood"}

# ---- Balance targets (checked when the script runs, and shown in the page's footer rows) ----
# Every creature rule gives one level, and no material may pass the top level (3): at most three trophies each.
# Construction woods and stones: level 2 from the common creature trophies of their biome, 3 with a rare one.
CONSTRUCTION = {key(m) for m in ["Wood", "Finewood", "Corewood", "Stone", "Yggdrasil Wood", "Black Marble", "Ashwood",
                                 "Grausten", "Timberwood"]}
# Powerful materials (metals, cores, crystal, Dvergr parts): at most level 2, even with rare trophies.
POWERFUL = {key(m) for m in ["Surtling Core", "Iron", "Chain", "Silver", "Crystal", "Black Metal", "Mechanical Spring",
                             "Dvergr Extractor", "Dvergr Lantern", "Flametal", "Bloodgold", "Molten Core",
                             "Charred Cogwheel", "Frostcore"]}
POWERFUL_CAP = 2
# Materials only a rare creature drops (its hide, its sinew): the creature's own trophy is the natural source, so
# they need level 1 with rare trophies, not from common ones.
RARE_DROPS = {key(m) for m in ["Bear Hide", "Bear Paw", "Scale Hide", "Morgen Sinew", "Celestial Feather",
                               "Writhan Roots"]}
# Every other material with creature rules: level 1 from common trophies; a second or third source where more
# creatures drop it or its biome is known for it.
# Rare creatures: mini-bosses, chest bosses, rare spawns, and creatures from a single cave or dungeon.
RARE = {
    "TrophyBjorn", "TrophySkeletonHildir", "TrophyAbomination", "TrophyKvastur", "TrophySGolem", "TrophyCultist",
    "TrophyUlv", "TrophyCultist_Hildir", "TrophyFrostTroll", "TrophyGoblinBrute", "TrophyGoblinBruteBrosBrute",
    "TrophyGoblinBruteBrosShaman", "TrophyBjornUndead", "TrophySerpent", "TrophyGjall", "TrophyMorgen",
    "TrophyFallenValkyrie", "TrophyBonemawSerpent", "TrophyWrithan", "TrophyMole", "TrophyBlob_Morkhalla",
}

# Every trophy in the game (item names from the game's asset bundles, display names from its English table),
# grouped by the biome where it is found. note: extra places / caveats. inferred: biome placed from the game's
# names and descriptions rather than spawn data.
B = OrderedDict()
B["Meadows"] = [
    ("TrophyEikthyr", "Eikthyr", "boss", "", False),
    ("TrophyBoar", "Boar", "", "", False),
    ("TrophyDeer", "Deer", "", "Also Black Forest", False),
    ("TrophyNeck", "Neck", "", "Also Black Forest and Swamp shores", False),
]
B["Black Forest"] = [
    ("TrophyTheElder", "The Elder", "boss", "", False),
    ("TrophyGreydwarf", "Greydwarf", "", "Also the Meadows' forest edges", False),
    ("TrophyGreydwarfBrute", "Greydwarf Brute", "", "", False),
    ("TrophyGreydwarfShaman", "Greydwarf Shaman", "", "", False),
    ("TrophyForestTroll", "Troll", "", "", False),
    ("TrophySkeleton", "Skeleton", "", "Also Swamp crypts", False),
    ("TrophyGhost", "Ghost", "", "Burial chambers", False),
    ("TrophySkeletonHildir", "Brenna", "", "Hildir's chest boss (Smouldering Tomb)", False),
    ("TrophyBjorn", "Bear", "", "", True),
]
B["Swamp"] = [
    ("TrophyBonemass", "Bonemass", "boss", "", False),
    ("TrophyBlob", "Blob", "", "", False),
    ("TrophyDraugr", "Draugr", "", "Also raids in other biomes", False),
    ("TrophyDraugrElite", "Draugr Elite", "", "", False),
    ("TrophyLeech", "Leech", "", "", False),
    ("TrophyWraith", "Wraith", "", "", False),
    ("TrophyAbomination", "Abomination", "", "", False),
    ("TrophySurtling", "Surtling", "", "Also Ashlands", False),
    ("TrophySkeletonPoison", "Rancid Remains", "", "Sunken crypts", False),
    ("TrophyKvastur", "Kvastur", "", "\"A witch's companion\": the Bog Witch's", True),
]
B["Mountains"] = [
    ("TrophyDragonQueen", "Moder", "boss", "", False),
    ("TrophyWolf", "Wolf", "", "", False),
    ("TrophyFenring", "Fenring", "", "", False),
    ("TrophyCultist", "Cultist", "", "Frost caves", False),
    ("TrophyHatchling", "Drake", "", "", False),
    ("TrophySGolem", "Stone Golem", "", "", False),
    ("TrophyUlv", "Ulv", "", "Frost caves", False),
    ("TrophyCultist_Hildir", "Geirrhafa", "", "Hildir's chest boss (frost cave)", False),
    ("TrophyFrostTroll", "Frost Troll", "", "In the game files; no known way to get it", False),
]
B["Plains"] = [
    ("TrophyGoblinKing", "Yagluth", "boss", "", False),
    ("TrophyGoblin", "Fuling", "", "", False),
    ("TrophyGoblinBrute", "Fuling Berserker", "", "", False),
    ("TrophyGoblinShaman", "Fuling Shaman", "", "", False),
    ("TrophyDeathsquito", "Deathsquito", "", "", False),
    ("TrophyLox", "Lox", "", "", False),
    ("TrophyGrowth", "Growth", "", "", False),
    ("TrophyGoblinBruteBrosBrute", "Thungr", "", "Hildir's chest boss (with Zil)", False),
    ("TrophyGoblinBruteBrosShaman", "Zil", "", "Hildir's chest boss (with Thungr)", False),
    ("TrophyBjornUndead", "Vile", "", "", True),
]
B["Ocean"] = [
    ("TrophySerpent", "Serpent", "", "Every ocean", False),
]
B["Mistlands"] = [
    ("TrophySeekerQueen", "The Queen", "boss", "", False),
    ("TrophySeeker", "Seeker", "", "", False),
    ("TrophySeekerBrute", "Seeker Soldier", "", "", False),
    ("TrophyGjall", "Gjall", "", "", False),
    ("TrophyTick", "Tick", "", "", False),
    ("TrophyDvergr", "Dvergr", "", "", False),
    ("TrophyHare", "Hare", "", "", False),
]
B["Ashlands"] = [
    ("TrophyFader", "Fader", "boss", "", False),
    ("TrophyCharredMelee", "Charred Warrior", "", "", False),
    ("TrophyCharredArcher", "Charred Marksman", "", "", False),
    ("TrophyCharredMage", "Charred Warlock", "", "", False),
    ("TrophyMorgen", "Morgen", "", "", False),
    ("TrophyAsksvin", "Asksvin", "", "", False),
    ("TrophyFallenValkyrie", "Fallen Valkyrie", "", "", False),
    ("TrophyVolture", "Volture", "", "", False),
    ("TrophyBlob_Lava", "Lava Blob", "", "", False),
    ("TrophyBonemawSerpent", "Bonemaw", "", "Ashlands ocean", False),
]
B["Deep North"] = [
    ("TrophyElaking", "Elaking", "", "A creature, not the biome's boss; the Deep North has no boss trophy yet", True),
    ("TrophyBarka", "Barka", "", "", True),
    ("TrophyBlob_Frost", "Frost Blob", "", "", True),
    ("TrophyBlob_Morkhalla", "Pulp", "", "Dungeon remains; biome not certain", True),
    ("TrophyJotunWarrior", "Krigen", "", "", True),
    ("TrophyJotunWitch", "Hexen", "", "", True),
    ("TrophyMole", "Eyeless One", "", "", True),
    ("TrophyMoose", "Moose", "", "", True),
    ("TrophySeal", "Seal", "", "", True),
    ("TrophyWrithan", "Writhan", "", "", True),
]


LEVEL_PCT = [50, 80, 90]
# What a boss trophy takes off its materials by default (the BossPercent setting): 100 is free.
BOSS_PCT = 100
