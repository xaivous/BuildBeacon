# BuildBeacon default trophy rules

These are the rules BuildBeacon ships with. They are written to `BepInEx/config/xaivous.BuildBeacon.BossRules.txt` and `xaivous.BuildBeacon.MobRules.txt` the first time the mod runs; after that, those files are what counts, and a server can change them freely (see the main page). Trophy names in the right-hand column are the names the rules files use.

## Boss trophies: materials made free (7 bosses, 55 materials)

Hang a boss trophy on a Boss Trophy Pillar or Boss Trophy Mount linked to a beacon, or set it in a Great Beacon, and every material listed for it is discounted inside that beacon's radius: **free** by default, or 95% off (20x) on servers that set `BossPercent = 95` to keep some gathering. Each boss counts once per beacon.

| Boss | Biome | Materials | Rules file name |
|---|---|---|---|
| Eikthyr | Meadows | Wood, Flint, Feathers, Acorns, Dandelion, Mushroom, Raspberries | `TrophyEikthyr` |
| The Elder | Black Forest | Fine Wood, Core Wood, Copper, Tin, Bronze, Bronze Nails, Blueberries, Thistle, Fir Cone, Pine Cone | `TrophyTheElder` |
| Bonemass | Swamp | Stone, Rock, Iron, Iron Nails, Guck, Chain, Surtling Core, Coal | `TrophyBonemass` |
| Moder | Mountains | Silver, Silver Necklace, Dragon Tear, Crystal | `TrophyDragonQueen` |
| Yagluth | Plains | Black Metal, Tar, Cloudberries, Red Jute | `TrophyGoblinKing` |
| The Queen | Mistlands | Black Marble, Yggdrasil Wood, Black Core, Refined Eitr, Sap, Wisp, Blue Jute, Majestic Carapace, Sharpening Stone | `TrophySeekerQueen` |
| Fader | Ashlands | Grausten, Warrior Trophy, Ashwood, Flametal, Bloodstone, Sulfur, Charcoal Resin, Proustite Powder, Ceramic Plate, Pot Shard, Kindled Ribs, Shield Core, Torn Spirit | `TrophyFader` |

## Creature trophies: discount levels (55 trophies, 112 rules)

Slot a creature trophy in the beacon or on a trophy rack, and each material listed for it gains a discount level inside the beacon's radius. Levels from different trophies add up, and each level takes more off:

| Level | 1 | 2 | 3 |
|---|---|---|---|
| Discount | 50% | 80% | 90% |
| Multiplier | 2x | 5x | 10x |

Level 3 is the top: no material has more than 3 creature trophies. Common building woods and stones reach level 2 from two common creatures of their biome and level 3 with a rarer one; metals, cores and other powerful materials stop at level 2. A beacon counts each creature trophy once; none of the defaults is stackable. Only a boss trophy goes past level 3.

| Creature | Materials (+1 level each) | Rules file name |
|---|---|---|
| Greydwarf | Wood, Stone, Resin, Greydwarf Eye | `TrophyGreydwarf` |
| Greydwarf Brute | Wood, Core Wood, Resin | `TrophyGreydwarfBrute` |
| Greydwarf Shaman | Fine Wood, Resin, Greydwarf Eye, Queen Bee | `TrophyGreydwarfShaman` |
| Troll | Fine Wood, Core Wood, Stone, Troll Hide | `TrophyForestTroll` |
| Bear | Wood, Fine Wood, Core Wood, Bear Hide, Bear Paw | `TrophyBjorn` |
| Skeleton | Bone Fragments | `TrophySkeleton` |
| Ghost | Ectoplasm | `TrophyGhost` |
| Brenna | Bone Fragments, Coal, Surtling Core | `TrophySkeletonHildir` |
| Boar | Leather Scraps | `TrophyBoar` |
| Deer | Deer Hide, Leather Scraps | `TrophyDeer` |
| Blob | Guck | `TrophyBlob` |
| Draugr | Ancient Bark, Iron | `TrophyDraugr` |
| Draugr Elite | Ancient Bark, Iron, Chain | `TrophyDraugrElite` |
| Leech | Bloodbag | `TrophyLeech` |
| Wraith | Chain, Ectoplasm | `TrophyWraith` |
| Abomination | Ancient Bark | `TrophyAbomination` |
| Surtling | Surtling Core, Coal | `TrophySurtling` |
| Rancid Remains | Bone Fragments | `TrophySkeletonPoison` |
| Kvastur | Guck | `TrophyKvastur` |
| Wolf | Wolf Pelt, Silver | `TrophyWolf` |
| Fenring | Fenris Claw, Wolf Pelt | `TrophyFenring` |
| Drake | Obsidian, Crystal | `TrophyHatchling` |
| Stone Golem | Stone, Crystal, Obsidian, Silver | `TrophySGolem` |
| Cultist | Fenris Claw | `TrophyCultist` |
| Ulv | Scale Hide, Wolf Pelt | `TrophyUlv` |
| Geirrhafa | Obsidian | `TrophyCultist_Hildir` |
| Fuling | Black Metal | `TrophyGoblin` |
| Fuling Shaman | Tar | `TrophyGoblinShaman` |
| Fuling Berserker | Black Metal | `TrophyGoblinBrute` |
| Lox | Lox Pelt | `TrophyLox` |
| Growth | Tar | `TrophyGrowth` |
| Thungr | Lox Pelt | `TrophyGoblinBruteBrosBrute` |
| Zil | Tar | `TrophyGoblinBruteBrosShaman` |
| Vile | Bear Hide, Bear Paw | `TrophyBjornUndead` |
| Seeker | Yggdrasil Wood | `TrophySeeker` |
| Tick | Yggdrasil Wood | `TrophyTick` |
| Seeker Soldier | Black Marble | `TrophySeekerBrute` |
| Dvergr | Black Marble, Mechanical Spring, Dvergr Extractor, Dvergr Lantern | `TrophyDvergr` |
| Gjall | Yggdrasil Wood, Black Marble | `TrophyGjall` |
| Charred Warrior | Ashwood, Charred Bone, Charred Skull, Warrior Trophy | `TrophyCharredMelee` |
| Charred Marksman | Ashwood, Charred Bone, Charred Skull | `TrophyCharredArcher` |
| Charred Warlock | Bloodgold, Charred Cogwheel | `TrophyCharredMage` |
| Morgen | Grausten, Ashwood, Morgen Sinew | `TrophyMorgen` |
| Asksvin | Grausten, Asksvin Hide, Asksvin Neck, Asksvin Pelvis, Asksvin Ribcage, Asksvin Skull | `TrophyAsksvin` |
| Lava Blob | Grausten, Flametal, Molten Core | `TrophyBlob_Lava` |
| Volture | Charred Bone, Charred Skull | `TrophyVolture` |
| Fallen Valkyrie | Celestial Feather, Bloodgold | `TrophyFallenValkyrie` |
| Moose | Timberwood, Moose Hide, Moose Sinew | `TrophyMoose` |
| Barka | Timberwood, Ice | `TrophyBarka` |
| Frost Blob | Frostcore, Ice | `TrophyBlob_Frost` |
| Seal | Seal Pelt | `TrophySeal` |
| Writhan | Writhan Roots, Timberwood | `TrophyWrithan` |
| Hexen | Ice | `TrophyJotunWitch` |
| Krigen | Frostcore | `TrophyJotunWarrior` |
| Elaking | Elaking Hair Bundle | `TrophyElaking` |
