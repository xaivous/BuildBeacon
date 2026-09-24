# BuildBeacon

Hang your boss trophies around a rune-carved beacon, and the materials of every biome you have conquered become free
to build with nearby.

Building a big base means hauling the same stone, wood and metal back and forth long after it has stopped being a
challenge. BuildBeacon rewards progress instead: once you have beaten a biome's boss, its building materials cost
nothing inside your beacon's radius, or a twentieth of their price on servers that want some gathering to stay (see
[Free, or 95% off](#free-or-95-off-the-servers-choice)). Creature trophies take half or more off the rest. The discount is real: it shows in
the build menu, in the piece's cost readout, and it is exactly what leaves your inventory when you place the piece.

Built on Jötunn for Valheim 1.0.

## Requirements

- BepInExPack Valheim
- Jötunn 2.30 or newer

Both are Thunderstore dependencies and install automatically with a mod manager. In multiplayer, the server and every
player need the mod (see [Multiplayer](#multiplayer)).

## Getting started

1. **Build a Build Beacon.** Hammer, *Misc* tab. It needs a workbench nearby and costs **20 Stone and 1 Surtling
   Core**. While you hold a hammer, hoe or cultivator, a blue ring on the ground shows its radius: **30 metres** to start.
2. **Build boss trophy holders near it.** Also in the Hammer's *Misc* tab, near a workbench, each costing
   **10 Stone and 5 Wood**:
   - the **Boss Trophy Pillar**, a short stone pillar with an iron hook, for floors and the ground;
   - the **Boss Trophy Mount**, a hexagonal plaque with a hook, for walls only.

   A holder has to stand within **30 metres** of a beacon, so holders can line a hall or ring the base rather than
   crowd the beacon. A yellow thread links them while you place it, just like a workbench and its upgrades.
3. **Hang a boss trophy on each holder.** Hold the trophy and press Use on the holder; press Use again to take it back.
   The boss's materials are now free inside the beacon's radius (95% off, if the server chooses).
4. **Slot creature trophies into the beacon itself.** Press Use on the beacon to open its panel, or hold a trophy and
   press Use on the beacon. Each creature trophy discounts a few of its own materials.
5. **Add trophy racks for more creature trophies.** Also in the Hammer's *Misc* tab, near a workbench, each costing
   **10 Wood and 5 Stone**:
   - the **Trophy Column**, a 2 m column with four alcoves round it, for floors and the ground;
   - the **Trophy Panel**, a 2 m square wall panel with four alcoves, made to tile a wall edge to edge.

   Each rack links to a beacon within 30 metres like a holder, adds **four creature trophy slots** on top of the
   beacon's own, and raises the beacon's level. Look at an alcove and press Use with a creature trophy to place it;
   press Use on a filled alcove to take it back.
6. **Later, raise a Great Beacon.** An 8-metre beacon whose own alcoves hold your boss trophies; see
   [The Great Beacon](#the-great-beacon).

## Levelling the beacon

Every boss trophy holder and trophy rack linked to a beacon raises it one level, and each level widens its radius by
10 metres. A holder or rack counts whether or not it holds a trophy yet, and any mix of the two counts.

| Holders and racks linked | 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7 |
|---|---|---|---|---|---|---|---|---|
| Level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
| Radius | 30 m | 40 m | 50 m | 60 m | 70 m | 80 m | 90 m | 100 m |

Level 8 is the top; further holders and racks still hold trophies (and racks still add their slots) but add no
radius. Each links to the closest beacon within 30 metres, so where two beacons are near each other, each counts for
whichever one is closer. Removing one lowers the level again straight away. The beacon's hover text and panel show its current level.

Beacons of either kind need **6 metres** between them; the hammer refuses a closer spot ("Need more space").

## The Great Beacon

A towering, 8-metre version of the beacon, for the trophies of the bosses you have slain. It is a Build Beacon in every
other way: the same discounts, ring, glow and panel, and boss trophy holders and trophy racks link to it just as they
do to a beacon.

- **Building it.** Hammer, *Misc* tab, next to a **Stonecutter**, for **20 Grausten, 5 Surtling Cores and 1 Crystal**.
- **Seven boss alcoves.** Its own alcoves take boss trophies, not creature trophies (those go on trophy racks beside
  it). Use a boss trophy on it, or pick one in its panel. Each boss has its own alcove: on the top row Eikthyr, the
  Elder, Bonemass and Moder, and on the lower row Yagluth, the Queen and Fader. The front of the lower row carries a
  great rune in place of an eighth alcove. As everywhere, each boss counts once: one already in an alcove or on a
  linked holder is refused.
- **Level and radius.** Every boss in its alcoves raises its level, before holders and racks do. Its radius starts
  wider and grows in smaller steps: **65 metres** at level 1, **5 metres** more each level, 100 metres at level 8.

| Level | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
|---|---|---|---|---|---|---|---|---|
| Radius | 65 m | 70 m | 75 m | 80 m | 85 m | 90 m | 95 m | 100 m |

- **Trophy sizes.** The bosses are shown close to their vanilla size, the largest a little smaller so they fit round the
  pillar (Bonemass at 75%, the Queen, Yagluth and Fader at 65%). Turn off `ShrinkGreatBeaconTrophies` to see them all at
  full size.
- **Lit.** While it counts any trophy, its great crystal turns and bobs, and the four small crystals around it circle
  with it, each spinning on its own.

## How discounts work

**Boss trophies make materials free.** Each boss is tied to the building materials of its biome: Eikthyr to the
Meadows' Wood, Flint and Feathers; the Elder to Fine Wood, Core Wood, Copper, Tin and Bronze; Bonemass to Stone, Iron,
Chain, Coal and Surtling Cores; and so on up to Fader and the Ashlands (the Deep North has no boss trophy yet). While a
boss's trophy hangs on a linked holder or sits in a Great Beacon, those materials cost nothing inside the radius. Each
boss counts once per beacon; a second holder with the same boss's trophy is refused.

**Creature trophies give discount levels.** Each creature trophy gives one discount level to a few materials, usually
the ones that creature drops or its biome is known for: a Greydwarf gives Wood, Stone, Resin and Greydwarf Eyes a level
each; a Troll gives Fine Wood, Core Wood, Stone and Troll Hide; a Seeker gives Yggdrasil Wood. Levels from different
trophies add up, and each level takes more off:

| Level | 1 | 2 | 3 | Boss |
|---|---|---|---|---|
| Discount | 50% | 80% | 90% | 100%, or 95% |
| Multiplier | 2x | 5x | 10x | free, or 20x |

- Level 3 is the top, and no material has more than three creature trophies. Two common creatures of a biome take its
  building woods and stones to level 2, and a rarer one, such as a Bear, Stone Golem, Gjall or Morgen, takes them to 3.
  Metals, cores and other powerful materials stop at level 2, and a material only one creature drops stays at level 1.
- The beacon itself has **4 creature slots**. Each trophy rack linked to it adds **4 more**.
- A beacon and its racks count each creature trophy once, so a second copy of the same trophy anywhere among them is
  refused, unless the server marks that trophy stackable.
- Costs round up, and the part of an item you overpay is saved for each material, even between sessions: once it adds
  up to a whole item, the next piece using that material costs one less. At level 1, a 1-Wood piece costs 1, 0, 1, 0;
  at level 3, 2-Wood floors cost 1, then four are free. Only a boss trophy goes further.

### Free, or 95% off: the server's choice

Out of the box, a boss trophy makes its materials **free**: once a biome is beaten, you never haul its stone and wood
again. Some servers would rather keep a little gathering in building, so the `BossPercent` setting decides what a
boss trophy takes off:

- **100** (the default): the boss's materials cost nothing.
- **95**: they cost a twentieth; each item goes 20x as far, still twice as far as the best creature discount. Costs
  round up and the savings carry over, as for creature trophies: 2-Stone floors cost 1, then the next nine are free, so
  a big build still needs a stack or two brought in.

Any value from 50 to 100 works; keep it at or above the top creature level (90) so a boss is never worth less than
creature trophies. In the panel a boss's materials show all three level segments in blue, a fourth tier; hovering it says
*Free* at 100, or *Level 4: 95% off* and the 20x multiplier at 95.
- When the savings take an item off the piece you are placing, a buff with the material's icon says so: "-1 Wood",
  or "4 free" when they make the next four free.

**Where it applies.** The discount depends on where the piece goes, not where you are standing: anything placed inside
the ring is discounted. It works with every building tool, including the hoe and cultivator where they cost materials.
The build menu shows the discounted cost with the full price struck through beside it; a material a
beacon makes free shows a cost of 0.

**Overlapping beacons.** A boss's discount applies if any beacon covering the spot has that boss. Creature discounts from several
beacons combine by taking the beacon with the highest level for each material, or by adding their levels up if the
server chooses (`Stacking`).

**Refunds are fair.** When you tear down a piece you built at a discount, you get back what you actually paid, not the
full price, even after a restart and whoever removes it.

Every default value is listed in **DEFAULT_DISCOUNTS.md**, included in the download: all seven bosses with the
materials each one frees, and every creature trophy with its discount levels.

## The beacon panel

Press Use on a beacon to open its panel. Your inventory opens beside it.

- **Trophies tab.** The boss trophies the beacon counts, each marked if it hangs on a holder, and its creature trophy
  slots. Click an empty slot to choose a trophy from your inventory, or **Ctrl+click** a trophy in your inventory to
  slot it straight away. **Ctrl+click** a slotted creature trophy to take it back, or use its Remove button. Click any
  trophy to see exactly what it does on the Discounts tab. On a Great Beacon the tab lists its boss alcoves instead
  (with an empty slot to add a boss), then bosses on its holders and trophies on its racks.
- **Discounts tab.** One line per material the beacon affects: a bar of discount levels, orange at level 1, yellow at
  level 2 and green at level 3, or for a boss's materials the same bar with all three segments blue, and the trophies
  responsible. Hover a row for its level out of the most that material can reach ("Level 1 of 2"), what it takes off and
  the effective multiplier (80% off makes each item go 5x as far), with every trophy that can discount the material:
  full colour when your beacon counts it, grey when it does not yet (turn off `ShowTrophySources` to keep that a
  mystery). A bar under each creature-discounted material fills as your rounding savings build towards the next
  item. Type in the filter box to find a material or trophy quickly.

The beacon's alcoves show the trophies slotted in it, and each holder shows its boss trophy on its hook.

## Sharing and protection

Everyone building inside a beacon's radius gets its discounts, whoever built it. Changing its trophies is protected
like a chest: inside a ward you do not have access to, the ward flashes and refuses. Only one player can have a
beacon's panel open at a time; anyone else is told it is in use.

Destroying a beacon or holder drops its trophies on the ground, so nothing is lost.

## Multiplayer

The mod must be installed on the server and on every client. Jötunn enforces this: players without the mod, or with a
different version, cannot join.

Gameplay settings are synced from the server and only admins can change them. The server's rules files are the ones
that count, and changes reach every player without a restart.

## Configuration

The config file is `BepInEx/config/xaivous.buildbeacon.cfg` (before 0.2.0, `com.xaivous.buildbeacon.cfg`; its settings
carry over automatically).

| Section | Setting | Default | Meaning |
|---|---|---|---|
| General | Enabled | true | Master switch. |
| General | LevelPercents | 50, 80, 90 | Percentage off at each creature discount level, level 1 first. The number of entries sets the top level. The default rules are balanced for three levels. |
| General | BossPercent | 100 | Percentage off a boss trophy takes from its materials: 100 makes them free, 95 leaves some gathering (each item goes 20x as far). 50 to 100; keep it at or above the last LevelPercents entry. See [Free, or 95% off](#free-or-95-off-the-servers-choice). |
| General | Stacking | Max | How overlapping beacons combine creature discounts: `Max` (the beacon with the highest level counts) or `Sum` (their levels add up). |
| General | Refund | PaidCost | `PaidCost` refunds what was paid; `CurrentCost` refunds the discounted cost at the piece's spot when it is removed. |
| General | ShowRadius | true | Draw the radius ring while holding a build tool. Per player. |
| General | ShowTrophySources | true | Hovering a row on the Discounts tab also shows every trophy that can discount that material, including ones not slotted yet (greyed). Turn off to keep it a mystery. Per player. |
| General | TrophyPlacement | Mounted | How trophies sit in the beacon's alcoves: `Mounted` (full size) or `Inset` (scaled to fit). Per player. |
| General | ShrinkGreatBeaconTrophies | true | Show the Great Beacon's largest boss trophies a little smaller so they fit round it; off shows them all at vanilla size. Per player. |
| Radius & Slots | RadiusAtLevel1 | 30 | Radius in metres at level 1. |
| Radius & Slots | RadiusPerLevel | 10 | Extra radius per level. Each linked boss trophy holder adds a level. |
| Radius & Slots | GreatRadiusAtLevel1 | 65 | The Great Beacon's radius in metres at level 1. |
| Radius & Slots | GreatRadiusPerLevel | 5 | The Great Beacon's extra radius per level. |
| Radius & Slots | MaxLevel | 8 | Highest level, for both kinds of beacon. |
| Radius & Slots | HolderRange | 30 | How far a boss trophy holder may stand from its beacon, in metres. |
| Radius & Slots | BossSlots | 8 | How many boss trophies a beacon counts. |
| Radius & Slots | BeaconMobSlots | 4 | Creature trophy slots in the beacon itself; bosses add none. Racks add four each. |
| Boss Rules | BossRules | see below | Mirror of the boss rules file. Do not edit here. |
| Mob Rules | MobRules | see below | Mirror of the creature rules file. Do not edit here. |

### Changing which trophies do what

The rules live in two readable files in `BepInEx/config`, created with the defaults on first run. Save either file and
the rules reload in game, no restart needed. On a server, edit the server's copies; players receive them on connect and
whenever they change.

`BuildBeacon.BossRules.txt`, one trophy and one material per line:

```
# Trophy            | Material
TrophyEikthyr       | Wood
TrophyTheElder      | Fine Wood
TrophyBonemass      | Iron
```

`BuildBeacon.MobRules.txt`, one trophy, material and level per line, plus a line for each stackable trophy:

```
# Trophy            | Material       | Level
TrophyGreydwarf     | Resin          | 1
TrophySGolem        | Stone          | 1
TrophyDeer          | Stackable
```

- Materials can be written as their in-game name (`Fine Wood`) or internal name (`FineWood`); spaces and capitals
  are ignored.
- `Level` is a whole number from 1 (every default rule gives 1). A trophy's levels add to other trophies' levels for
  the same material, up to the top level.
- `Stackable` lets a beacon count every copy of that trophy instead of one.
- Use `*` as the material to affect every material.
- A trophy listed in the boss file is a boss trophy; it goes on a holder or in a Great Beacon, not in the beacon.
- A malformed line is skipped with a warning in the log instead of breaking the rest.

Any item can be made a trophy by giving it a rule, if you want to reshape the system.

## Tips and known limitations

- The Boss Trophy Mount and the Trophy Panel snap to a wall by their centre, and sit on the wall's face even on wood
  and Grausten walls, whose snap points are on their centre line. Snapping by their edges and corners is switched off
  for now, so place Trophy Panels side by side by eye.
- Beacon trophies in worlds from before boss trophy holders existed keep working and can be taken out, but new boss
  trophies go on holders.
