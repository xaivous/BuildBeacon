# Spec: mob trophy racks

Stage 1 of `docs/plans/mob-trophy-racks.md`, written from the user's answers (section 0B there). Gate 1: the user
reviews this before any modelling. **Reviewed and approved by the user (2026-09-23).**

## Pieces

| | Trophy Column | Trophy Panel |
|---|---|---|
| Piece ID | `xai_beacon_mobrack_column` | `xai_beacon_mobrack_wall` |
| Bundle prefab / FBX | `xai_mobrack_column_prefab` / `xai_mobrack_column.fbx` | `xai_mobrack_wall_prefab` / `xai_mobrack_wall.fbx` |
| Generator | `tools/blender/xai_mobrack_column.py` | `tools/blender/xai_mobrack_wall.py` |
| Where | Floors and the ground | Walls only |
| Size | 2.0 m tall; plinth and capital about 0.9 m across; shaft about 0.6 m | 2.0 m x 2.0 m square, 0.12 m deep (about the boss wall mount's 2 m point to point, 0.10 m deep) |
| Alcoves | 4, one per side of the shaft at about 1.2 m, like the beacon's | 4 in a 2x2 grid, one per quarter |
| Cost | 10 Wood, 5 Stone | 10 Wood, 5 Stone |
| Crafting station | Workbench nearby | Workbench nearby |
| Support material (`WearNTear`) | As the boss pillar | Wood, as the boss wall mount (stone on a wood wall collapses) |

Hammer, Misc tab, beside the boss holders. Icons: 128 px Blender renders, the holders' recipe.

## Behaviour

- **Link:** a vanilla `StationExtension` pointing at the beacon's station, like the boss holders. It links to the
  closest beacon within `HolderRange` (30 m), placement needs one in range, and the yellow thread shows.
- **Level:** each linked rack adds one level, trophies or not. Racks and boss holders together raise the level to
  `MaxLevel` (8) in any mix: level = 1 + linked boss holders + linked racks, capped. Past the cap, a rack still holds
  and counts trophies but adds no level; its hover says so.
- **Slots:** each rack adds its own four creature trophy slots, on top of the beacon's own (4, fixed since 2026-09-23; was `2 + 2 per boss`). Every
  trophy on a linked rack counts towards the beacon's discounts, subject to the duplicate rule. Boss trophies are
  refused ("Only creature trophies go here"); they go on boss holders.
- **Duplicates (the beacon's network):** the beacon's own slots plus every rack linked to it. A trophy already
  anywhere in the network is refused, in the beacon and in any rack, with "That trophy is already slotted, and a
  second one adds nothing". **Stackable** trophies (a `Trophy | Stackable` line in the rules) are the exception: every
  copy counts and none is refused (confirmed by the user).
  - The network can gain a duplicate after the fact: a rack re-links when a closer beacon is built, or a rules change
    makes a trophy non-stackable. The duplicate stays where it is and counts once. The extra copy's hover says "Not
    counted: already in this beacon's network".
- **No beacon in range:** a rack whose beacon is gone refuses new trophies ("No Build Beacon in range"). Its held
  trophies stay put and can be taken.
- **Using an alcove:**
  - Each alcove is its own target.
  - Looking at an empty alcove shows "Empty" and "[E] Place creature trophy". Using it with a creature trophy in hand
    places it there.
  - Looking at a filled one shows the trophy and "[E] Take trophy". Using it with empty hands, or holding a
    different item, takes it back into the inventory.
- **Ownership and protection:** as the boss holders. The ZDO owner writes, others ask for ownership first
  (ItemStand's `RequestOwn`). Inside someone else's ward, the ward flashes and refuses.
- **Destroying a rack** drops every trophy it holds, like the boss holders.
- **Beacon panel:** the Trophies tab lists rack trophies in the creature section, read-only, marked "on a rack". The
  section header counts all creature slots, `used/total` (the beacon's own slots plus four per linked rack). The
  Discounts tab needs nothing new. The beacon's hover counts the same way.

## Display

- **Column:** the beacon's alcove, a framed niche 0.20 m wide, 0.42 m high and 0.16 m deep. Trophies follow the
  `TrophyPlacement` setting, as in the beacon.
- **Panel:** each quarter has one framed niche, about 0.55 m wide, 0.65 m high and 0.07 m deep (the panel is thin).
  Trophies are scaled to fit the opening's width and height, and may stand out of the shallow niche. This is a new fit
  mode in `BeaconTrophyDisplay` ("Face"); the existing Inset mode would also fit the depth and shrink them too far.
- A trophy that does not count (duplicate, or no beacon) is still shown.

## Model conventions (Blender and Unity)

- **Materials:** the beacon's slot names (`BeaconStoneDark`, `BeaconStoneLight`, `BeaconBandDark`, `BeaconRune`), so
  the FBX importer remaps onto the existing `.mat` assets and `MaterialTemplates` dresses them. No new materials.
- **Column:** a symmetric profile. The plinth is mirrored upward into the capital, and the rune bands sit under the
  capital and above the plinth. Alcove code follows the beacon's: raised frame, boolean niche, and no weathering
  near the openings.
- **Panel:** a square slab with a bevelled border, four framed niches, and a flat back. Runes stay inside the border.
  Nothing overhangs the square outline, so tiled panels meet cleanly.
- **Pivot:** the column's base centre, and the panel's back-face centre (like the holders).
- **Empties:**
  - `attach_0..3` at each niche's back, pointing out. The column counts from the front (-Y) around. The panel counts
    top-left, top-right, bottom-left, bottom-right, as seen from the front.
  - Column snap points: `snappoint_top` and `snappoint_bottom` at the centres, so columns stack.
  - Panel snap points: the four corners and four edge midpoints on the back plane (`snappoint_c0..c3`,
    `snappoint_e0..e3`), and `snappoint_centre`.
- **Colliders (Unity):** a body collider, plus `slot_0..3` boxes in front of each niche (slightly proud of the
  frame), all on the `piece` layer.

## Texts (English)

| Key | Text |
|---|---|
| `piece_xai_mobrack_column` | Trophy Column |
| `piece_xai_mobrack_column_desc` | A carved stone column with four alcoves for creature trophies. Linked to a nearby Build Beacon, it raises the beacon's level and adds four creature trophy slots. |
| `piece_xai_mobrack_wall` | Trophy Panel |
| `piece_xai_mobrack_wall_desc` | A square stone panel for a wall, with four alcoves for creature trophies. Linked to a nearby Build Beacon, it raises the beacon's level and adds four creature trophy slots. Panels tile edge to edge. |
| `xai_rack_empty` | Empty |
| `xai_rack_place` | Place creature trophy |
| `xai_rack_take` | Take trophy |
| `xai_rack_mob_only` | Only creature trophies go here |
| `xai_rack_not_counted` | Not counted: already in this beacon's network |
| `xai_rack_no_level` | The beacon is at its top level; this rack adds slots but no level |
| `xai_beacon_on_rack` | on a rack |

The existing `xai_holder_linked`, `xai_holder_unlinked`/`xai_holder_no_beacon` and `xai_beacon_mob_duplicate` texts are
reused.

## Balance note

Racks raise how many different creature trophies count at once, not how much one trophy gives. The balance targets
already assume one of every trophy, so they hold, but a beacon reaches them sooner. The balance page's footer rows
("with rare trophies") are now reachable in practice from mid-game.
