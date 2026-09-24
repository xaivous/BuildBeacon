# Spec: the Great Beacon

Stage 1 of `docs/plans/great-beacon.md`, written from the user's answers (section 0B there). Gate 1: the user
reviews this before any modelling. **Approved by the user (2026-09-24).**

## Piece

| | Great Beacon |
|---|---|
| Piece ID | `xai_great_beacon` |
| Bundle prefab / FBXs | `xai_great_beacon_prefab` / `xai_great_beacon.fbx` (body) and `xai_great_beacon_crystal.fbx` (crystal, core and shards) |
| Generator | `tools/blender/xai_build_beacon.py` with a second parameter set (see Model) |
| Where | The ground and floors, like the beacon |
| Size | About 8 m to the crystal's tip; base about 3.8 m across; pillar about 2.2 m across |
| Own slots | 7 boss trophy alcoves (no creature slots) |
| Cost | 20 Grausten, 5 Surtling Core, 1 Crystal |
| Crafting station | Stonecutter nearby |
| Material (`WearNTear`) | Stone, as the beacon (`m_noSupportWear` as the beacon's) |

Hammer, Misc tab, beside the beacon. Icon: a 128 px Blender render, the beacon's recipe.

## Behaviour

It is a Build Beacon (a `BeaconController` on the same crafting station), so everything not listed here is the
beacon's: the panel, discounts, `Stacking`, the ring, the glow and crystal motion while lit, refunds, drops, ward
checks, ownership handshakes.

- **Station:** a `CraftingStation` named `$piece_xai_beacon`, the beacon's station name. Boss trophy holders and trophy
  racks link to it exactly as to a beacon: the closest beacon of either kind within `HolderRange` wins.
- **Own slots:** 7, boss trophies only. Each boss once per beacon: a boss already in its alcoves or on a holder linked
  to it is refused ("That boss trophy is already slotted", the existing text; "No free boss trophy slots" when all 7
  are full). Creature trophies are refused with "Creature trophies go on a
  trophy rack next to the Great Beacon". Using a boss trophy on it (or picking one in the panel) fills the next
  alcove; Remove in the panel takes one back.
- **Level:** 1 + bosses in its own alcoves + linked holders + linked racks, capped at `MaxLevel` (8). Its own bosses
  take the levels first, then holders, then racks by distance (the rule the racks' "adds no level" hover follows).
- **Radius:** 65 m at level 1, +5 m a level: 65, 70, 75 ... 100 m at level 8. Two new synced settings,
  `GreatRadiusAtLevel1` (65) and `GreatRadiusPerLevel` (5), in "2. Radius & Slots".
- **Discounts:** its bosses free their materials as a holder's do; creature trophies on its racks give levels as on a
  beacon's racks.
- **Spacing:** no beacon of either kind within **6 m** of another: vanilla `Piece.m_blockRadius = 6` and
  `m_blockingPieces = [beacon, Great Beacon]` on both pieces, set at registration. Placement shows vanilla's "Need more
  space". Existing beacons closer than that stay; only new placements are checked.
- **Panel:** the Trophies tab shows "Boss trophies n/7" with its alcoves (Remove, the empty "Click to add a boss
  trophy" row, the boss picker), then any linked holders' bosses ("on a holder"), then "On racks" as today. No own
  "Mob trophies" section. The header line reads "Great Beacon". The Discounts tab is unchanged.
- **Hugin:** the first beacon of either kind brings him; one message per character.
- **Lit:** as the beacon: any counted trophy lights it; the core glows and the crystal (with its shards) turns and bobs.

## Layout

The pillar is octagonal like the beacon's, one face towards the front (Unity +Z). Faces are numbered from the front
clockwise seen from above: 0 front, 1 front-right, 2 right, 3 back-right, 4 back, 5 back-left, 6 left, 7 front-left.

| Row | Height (alcove centre, from the ground) | Faces |
|---|---|---|
| Top | about 4.6 m | 1, 3, 5, 7 (the diagonals): 4 alcoves |
| Lower | about 2.7 m | 2, 4, 6: 3 alcoves; face 0 carries the rune panel |

- **Rune panel (face 0, the front):** a framed, recessed panel the size of an alcove frame with one large glowing
  glyph (a bind-rune of Othala and Algiz, "home" and "protection"), `BeaconRune` on `BeaconBandDark`. It marks the
  front and stands in for the eighth boss.
- **Each boss has its own alcove** (the user, 2026-09-24; `BeaconTrophyDisplay.GreatTrophyAlcoves`): the top row,
  walking round from the front-left to the right, Eikthyr (`attach_0`, front-left), the Elder (`attach_1`,
  front-right), Bonemass (`attach_5`, back-right), Moder (`attach_4`, back-left); the lower row, from just right of
  the rune panel, Yagluth (`attach_3`, right), the Queen (`attach_6`, back), Fader (`attach_2`, left). Left and right
  as seen from the front. A boss not in the table takes the first free alcove.
- **Alcoves:** about 0.62 m wide, 1.05 m tall, 0.45 m deep, with the beacon's raised frames scaled. The attach empty
  sits on the niche's back wall at its centre, +Z out of the niche, as on the beacon.
- **Trophy size:** boss trophies are mounted the item-stand way (back in the niche) at **per-boss sizes chosen by the
  user in game (2026-09-24, then tuned by hand): Eikthyr, the Elder, Moder 100%; Bonemass 75%; the Queen, Yagluth,
  Fader 65%** of vanilla
  (`BeaconTrophyDisplay.GreatTrophyScales`). A boss not in the table (a later one) gets at most 90%, fitted to
  4.5 m x 4.5 m. Earlier tries: a 1.8 m fit (33-62%, too small), then 90% with a 4.5 m fit. The per-player setting
  `ShrinkGreatBeaconTrophies` (default on) off: every trophy at vanilla size.

## Model

One design with the beacon: `xai_build_beacon.py` gains a parameter set `GREAT` (the beacon's `PARAMS` with overrides)
and the generalisations it needs; running it builds both, and the beacon must come out unchanged (same triangle count,
same attach and pivot positions).

- **Proportions:** the beacon's profile, about 2.4x taller, with a stouter pillar so the faces take boss-sized
  alcoves. Heights (approximate): base tiers 1.15 m (four tiers, one more than the beacon), pillar to 5.6 m with the top
  rune band, capital and cap to about 6.9 m, crystal centre about 7.6 m, tip about 8.1 m.
- **Alcoves** as a list of (face, height) instead of an evenly spaced count (the beacon's becomes its four faces at its
  one height).
- **Extras over the beacon:** a second rune band between the two alcove rows (built there rather than low on the
  pillar: the buttresses and the lower row fill the low part), eight buttresses on the pillar's edges, standing on
  the plinth's second tier and meeting the pillar 0.75 m above its foot (on the edges, so no face is blocked), the rune panel, and four small crystal shards around the main crystal.
- **Crystal:** the main crystal about 2x the beacon's with its `BeaconCore` core; four shards (about a third of its
  size, `BeaconCrystal`, no core) floating around it at its height, 0.8 m out, tilted. All in
  `xai_great_beacon_crystal.fbx` with the origin at the crystal's centre, so the pivot turns them together; the
  shards are **separate objects** (`xai_great_beacon_shard_0..3`, each with its origin at its own centre) so the code
  can move each one on its own.
- **Shard motion (the user, 2026-09-24):** while lit, the satellite shards revolve around the main crystal and bob with
  it (they are under its pivot, so they follow its turn and bob), and each also spins about its own axis and bobs on
  its own, at rates and phases that differ from shard to shard and from the main crystal, for an intricate effect.
  Eased in and out with the lit state like the main crystal. Visual only, not synced.
- **Materials:** the beacon's (`BeaconStoneDark`, `BeaconStoneLight`, `BeaconBandDark`, `BeaconRune`) on the body;
  `BeaconCrystal`, `BeaconCore` on the crystal. No new materials.
- **Weathering:** as the beacon (no displacement near the alcoves, the rune panel or the bands).
- Prints: attach positions (Unity space) and out-directions, the crystal centre, total height.

## Unity

- `xai_great_beacon_prefab` from the beacon prefab as the template: `model` (instance of `xai_great_beacon.fbx`,
  rotated -90 about X), `crystal` pivot at the printed centre holding `crystal_model`, `attach_0..6`, `Bottom Center`
  snap point, colliders: base (box over the tiers), pillar (box), capital (box); nothing over the alcove mouths.
- `Piece`: name `$piece_xai_great_beacon`, description, category Misc; `m_blockRadius` and `m_blockingPieces` are set
  in code (they reference the registered pieces). `WearNTear` as the beacon's, health scaled up (the beacon's x2).
- Label `buildbeacon`; rebuild the bundle.

## Code

- **Registration:** `RegisterGreatBeacon` mirrors `RegisterPiece` for the beacon (station with the beacon's station
  name, ring, glow clone under the crystal pivot, effects from `stone_wall_2x1`, material dressing, snap tags), with
  the Stonecutter recipe. Then `m_blockRadius`/`m_blockingPieces` on both beacon prefabs.
- **`BeaconController`:** a per-prefab mode set at registration: `m_ownSlotKind` (Mob for the beacon, Boss for the Great
  Beacon) and `m_ownSlotCount` (4, 7). `InsertBlockedReason`, `MobSlots`, the own boss slot count, `Level` (own bosses
  added for the Great Beacon, feeding `RackAddsLevel`'s room), `Radius` (the Great settings) follow the mode.
  `RebuildEffective` already counts own bosses; its `BossSlots` cap stays (8).
- **`BeaconTrophyDisplay`:** alcove count from the prefab's `attach_N` children instead of the constant 4, and the
  face-fit box for the Great Beacon.
- **Crystal motion:** `AnimateCrystal` also moves the shards found under the crystal pivot (names starting
  `xai_great_beacon_shard`): each spins about its own long axis and bobs along it, with its own speed, period and phase
  (fixed per shard index: 40, 51, 62, 73 degrees/s alternating direction, bob periods 2.5 to 3.6 s, 5 cm), scaled by
  the same lit ease; unlit, their bob settles back and they stop turning where they are, like the main crystal. The
  pivot's turn and bob carry them round the main crystal.
- **`BeaconUI`:** sections from the mode (own boss slots with the picker for the Great Beacon, no own mob section).
- **Texts:** `piece_xai_great_beacon` "Great Beacon"; `piece_xai_great_beacon_desc` "A towering beacon of carved stone
  whose alcoves hold the trophies of the bosses you have slain. It works like a Build Beacon, and each boss trophy set
  in it raises its level."; `xai_great_beacon_mob_refused` "Creature trophies go on a trophy rack next to the Great
  Beacon"; the boss duplicate and full texts exist already (`xai_beacon_boss_duplicate`, `xai_beacon_boss_full`).
- **Hugin** (`TutorialPatches`): either piece.
- **`beacon_dump` / diagnostics:** name the kind.

## Out of scope

- Converting a beacon into a Great Beacon or back.
- A Deep North boss alcove (the rune panel is the placeholder).
- The wall snapping revisit and the floating trophy gap (open items 1 and 2) apply here too but are not part of this.
