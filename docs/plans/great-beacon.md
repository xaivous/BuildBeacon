# Plan: the Great Beacon (a giant beacon for boss trophies)

From `master` at `31b2cf3` (2026-09-24). Follows the racks' staged plan (`docs/plans/mob-trophy-racks.md`):
decisions -> spec -> Blender -> Unity -> code -> in game, with a gate after each; the next stage starts only when the
gate passes and, where marked, the user has looked.

A second kind of Build Beacon, about 8 m tall: the same design as the beacon, grander (more tiers, extra crystal
shards, more runes), with its alcoves sized for boss trophies. It is a beacon in every other way: the same crafting
station, so boss trophy holders and trophy racks link to it as they do to the beacon; the same discounts, level, radius,
ring, panel and glow. The difference: **its own alcoves hold boss trophies** (the beacon's hold creature trophies).

## What the existing code already does (why this is mostly a model and a switch)

- **Two beacons near each other are handled.** Holders and racks are vanilla station extensions: each links to the
  closest beacon in range, so nothing is counted twice. Discounts at a spot come from every beacon covering it,
  combined by `Stacking` (Max: the beacon that takes the most off; boss-free if any covering beacon frees it); this was
  confirmed in game on 2026-09-24. Levels, slots and duplicates are per beacon and its own network. So the two kinds
  do not need 100 m between them; **an 8 m spacing** (so they do not overlap physically) is enough.
- **The spacing needs no patch:** vanilla `Piece.m_blockRadius` with `m_blockingPieces` refuses placement ("Need more
  space") when a listed piece is within the radius (`Player.UpdatePlacementGhost`, 1.0). Set on both beacons.
- **Boss trophies in a beacon's own slots still work:** `RebuildEffective` counts them (each boss once, up to
  `BossSlots`, in progression order), the panel still has the boss picker, the "Empty boss slot" and "Click to add a
  boss trophy" texts, and the alcove display puts bosses first. Only `InsertBlockedReason` refuses new ones ("Boss
  trophies go on a boss trophy holder"). The Great Beacon turns that around: bosses in, creature trophies out.
- **The same station:** extensions find stations by the `CraftingStation`'s name, so the Great Beacon carries a
  `CraftingStation` with the beacon's name; `BossHolder.LinkedBeacon`/`MobRack.LinkedBeacon` then find its
  `BeaconController` as they find the beacon's.

---

## 0. Decisions to settle first

Recommended answers in bold; the user decides (answers go in 0B).

1. **Name and ID.** **"Great Beacon", `xai_great_beacon`** (models `xai_great_beacon`, `xai_great_beacon_crystal`).
   Alternatives: "Grand Beacon", "Boss Beacon", "Beacon of the Fallen".
2. **Its own slots.** **Boss trophies only, in 8 alcoves** (every boss so far, 7, plus room for the Deep North's; the
   pillar is octagonal, one alcove per face). Creature trophies go on racks linked to it. Alternatives: 4 boss alcoves;
   or boss alcoves plus the beacon's 4 creature slots.
3. **Do bosses in its own alcoves raise its level?** Holders do (level = 1 + holders + racks). **Yes: each boss trophy
   in its alcoves counts as a holder would, sharing `MaxLevel` (8)**, so a Great Beacon with 7 bosses is level 8 without
   a single holder. Alternative: only linked holders and racks raise it, as for the beacon.
4. **Radius.** **The same as the beacon's (30 m at level 1, +10 m a level).** Alternative: a larger base, e.g. 40 m.
5. **Spacing.** **8 m between any two beacons of either kind** (the beacon's 8 m to the Great Beacon, and the same
   rule beacon-to-beacon and Great-to-Great, which is just `m_blockRadius` on both). Alternative: only between the two
   kinds.
6. **Size and shape.** **8 m to the crystal's tip, footprint about 4 m, the beacon's profile scaled about 2.7x**, with
   extras: a second rune band, buttresses at the base, four smaller crystal shards floating around the main crystal
   (they turn with it while lit), the glowing core. Alcoves big enough that boss trophies at vanilla item-stand size
   (Eikthyr's is about 2.3 x 2.9 m) sit with their backs in the alcove, mounted like the holders show them.
7. **Cost and station.** **Stonecutter nearby; 60 Stone, 20 Grausten or Black Marble, 5 Surtling Core, 1 Crystal**
   (a late build that says "big stone thing"). Needs your call: something from the endgame, or reachable earlier?
8. **Extras.** **The ring, glow, Hugin and the panel as the beacon's; Hugin's first-beacon message fires for either.**

## 0B. Answers

Given by the user on 2026-09-24; where they differ from section 0, these win.

1. "Great Beacon", `xai_great_beacon`.
2. Boss trophies only, **7 alcoves in 2 offset rows**: the top row has 4 on alternating faces; a lower row, about
   midway up, has 3 on the other alternating faces, and its fourth face carries a large runic symbol instead of an
   alcove (the last boss has no trophy yet). That face is the Great Beacon's front.
3. Yes: each boss in its alcoves raises its level, in addition to linked holders and racks (cap `MaxLevel` 8).
4. Radius **65 m at level 1, +5 m per level** (so 100 m at level 8).
5. Spacing between any two beacons of either kind, **6 m**.
6. As recommended.
7. **20 Grausten, 5 Surtling Core, 1 Crystal** (station: the Stonecutter, as recommended; not contradicted).
8. As recommended.

---

## Status

- Gate 1 passed (spec approved, 2026-09-24).
- Gate 2 passed (model approved after the buttresses were rooted on the plinth); shards made separate objects for
  their own motion (the user's note); icon rendered.
- Gate 3 passed: the prefab in the built bundle lists the model, pivot with crystal and four shards, seven attach
  points, three colliders and the snap point.
- Gate 4 passed: builds with 0 errors and deploys. Code: `RegisterGreatBeacon` and `SetBeaconSpacing` in
  `BuildBeaconPlugin`; `BeaconController` own-slot mode (`m_ownSlotKind`, `m_ownSlotCount`, `IsGreat`, `OwnBossSlots`,
  `OwnBossCount`, own bosses in `Level` and `RackAddsLevel`, Great radius, insert rules, hover, shard motion);
  `BeaconTrophyDisplay` alcove count from `attach_N` and shrink-only face fit; `BeaconUI` sections for the Great
  Beacon; config `GreatRadiusAtLevel1`/`GreatRadiusPerLevel`; texts. Not done: `beacon_dump` naming the kind.
- Gate 5 passed: confirmed in game by the user (2026-09-24), after the registration fix, the per-boss sizes (tuned
  by the user) and the fixed boss alcoves. The README describes it ("The Great Beacon" section, config rows).

## 1. Description (spec)

`docs/design/great-beacon.md` from the decisions: IDs, dimensions, alcove count and positions (attach points
`attach_0..7`), colliders (body, base, no collider in the alcove mouths so trophies can be targeted), snap point
(`Bottom Center`), material slots (the beacon's five plus `BeaconCore`), the crystal pivot and its extra shards,
station and block radius settings, costs, texts, and what the code changes.

**Gate 1:** the user approves the spec.

## 2. Blender

- Either a `great` parameter set for `xai_build_beacon.py` (scale, tiers, extras) so the two stay one design, or its
  own generator `xai_great_beacon.py` built from the beacon's; the spec decides. Exports `xai_great_beacon.fbx` and
  `xai_great_beacon_crystal.fbx` (the crystal and its shards, for the lit motion), prints attach and pivot positions.
- Icon render at 128 px (the icon pipeline in the handoff).

**Gate 2:** the user reviews snapshots (front, three-quarter, beside the beacon for scale, an alcove close-up).

## 3. Unity

- `xai_great_beacon_prefab` built from the beacon prefab as the template: model and crystal pivot swapped, eight
  `attach_N` empties, colliders sized, `Piece`/`WearNTear` settings (stone, `m_noSupportWear` as the beacon's),
  `m_blockRadius` 8 and `m_blockingPieces` (both beacons; the beacon prefab gets the same), bundle label.
- Rebuild the bundle.

**Gate 3:** `prefab_tree.py` on the built bundle lists the model, pivot, eight attach points, colliders and snap point.

## 4. Code

- Register the piece like the beacon (its own `CraftingStation` with the beacon's station name, glow clone under its
  pivot, ring, effects, materials), a Stonecutter recipe and the English texts.
- `BeaconController`: a per-prefab setting for what its own slots take (creature trophies, or boss trophies with a
  slot count). `InsertBlockedReason`, `MobSlots`/boss slot counts, the level rule (decision 3), and the panel's
  sections follow it.
- `BeaconTrophyDisplay`: the alcove count from the prefab's `attach_N` points instead of the constant 4.
- `m_blockingPieces` on both beacons at registration (the prefabs are known then).
- Hugin: fires for either beacon.

**Gate 4:** builds, deploys, the log registers the Great Beacon with its attach points and station.

## 5. In game

Place it (and see it refused within 8 m of a beacon), slot bosses in its alcoves (they show, count, free their
materials, raise the level if decision 3), link a holder and a rack to it, use the panel, lit crystal and shards turning,
two players (ownership as the beacon).

**Gate 5:** the user confirms.
