# Plan: mob trophy racks (column and wall panel)

Branch `feature/mob-trophy-racks`, from `master` at `58cc83d` (2026-09-23).

Two new pieces that hold creature (mob) trophies for a Build Beacon, following the boss trophy holders' pattern:

- **Trophy Column** (`xai_beacon_mobrack_column`): a free-standing column with symmetric flares at the base and the
  top, four framed alcoves around the shaft like the beacon's.
- **Trophy Panel** (`xai_beacon_mobrack_wall`): a square wall panel with four alcoves in a 2x2 grid, meant to be tiled
  edge to edge across a wall.

Each holds four creature trophies, links to its beacon as a vanilla station extension (like the boss holders), and
raises the beacon's level (and radius) by one, towards `MaxLevel`.

The work runs description -> Blender -> Unity -> code -> in game. Each stage ends with a verification gate; the next
stage starts only when the gate passes (and, where marked, the user has looked).

---

## 0. Decisions to settle first

Recommended answers in bold; the user decides. **Settled by the user in 0B;** where 0B differs, 0B wins, and the spec (`docs/design/mob-trophy-racks.md`) follows 0B.

1. **How rack trophies count against the beacon's creature slots.** Today a beacon has `2 + 2 per boss` creature
   slots, filled from its own alcoves. Options:
   - **The slot cap covers the beacon and all its linked racks together.** Racks are more room to show trophies, not
     more discounts; trophies past the cap sit on the rack but are "not counted" (the boss holders' pattern). Keeps
     the balance work intact.
   - Racks add their four slots on top of the cap. Much more discount per beacon; the balance targets would need
     revisiting.
2. **Level.** **Each linked rack adds one level, trophies or not, sharing `MaxLevel` (8) with the boss holders**
   (level = 1 + boss holders + racks). Alternative: racks add nothing to level.
3. **Duplicates.** **A non-stackable trophy counts once across the beacon and all its racks; a second copy anywhere
   linked to the same beacon is refused** (same message as today). Stackable trophies count every copy.
4. **Choosing a slot.** **Each alcove is its own target: the player looks at an alcove and uses it (place with a
   trophy in hand, take with empty hands),** like four item stands. Alternative: use anywhere on the rack fills the
   first empty slot and takes the last filled one (simpler, less control).
5. **Beacon panel.** **The Trophies tab lists rack trophies read-only, marked "on a rack" (as bosses are "on a
   holder"); they are placed and taken at the rack.** The Discounts tab needs nothing new (it reads the effective set).
6. **Names and cost.** **"Trophy Column" and "Trophy Panel".** Cost like the holders: column 10 Stone + 5 Wood; panel
   6 Stone + 4 Wood (it tiles, so cheaper). Both need a workbench nearby, like the holders.
7. **Sizes.** **Column 2.0 m tall (one wall height), footprint about 0.8 m; panel 1.0 m x 1.0 m, 0.15 m deep**, so
   panels tile on the 1 m / 2 m building grid and four panels cover a 2 m x 2 m wall.

## 0B. Answers
1. Second option - racks add slots.
2. racks add to level. beacon level stills caps at 8, so whether thats 7 boss holders or 7 racks or 4 racks and 6 boss holders or any other combination.
3. refuse duplicates in a rack that exist elsewhere in the beacon's "network" of trophies (itself or attached extensions)
4. alcove-targeting
5. as suggested
6. 10 wood + 5 stone for both.
7. panel should keep roughly the same dimensions as the wall holder (as it will stores 4x as many trophies, albeit smaller ones)

---

## 1. Description (spec)

Write `docs/design/mob-trophy-racks.md` from the decisions: behaviour, IDs, sizes, alcove count and spacing, material
slots, attach and snap point names, colliders, costs, texts. Conventions to reuse:

- Material slot names from the beacon (`BeaconStoneDark`, `BeaconStoneLight`, `BeaconBandDark`, `BeaconRune`), so the
  FBX importer remaps onto the existing `.mat` assets and `MaterialTemplates` dresses them with vanilla textures. No
  new materials unless the spec needs one.
- Attach points `attach_0` .. `attach_3`, oriented like the beacon's (`BeaconTrophyDisplay` mounts on them with the
  wall item stand's rotation).
- Snap points named `snappoint_*`:
  - column: top and bottom centre, so columns stack;
  - panel: the four corners and four edge midpoints on its back plane, so neighbouring panels meet edge to edge.
- One collider per alcove (a child `slot_N` box in front of the niche), for decision 4, plus the body collider.
- Wall panel support: **Wood** (`WearNTear` material), like the boss wall mount. A stone-material piece on a wood
  wall collapses silently (`m_noSupportWear = true` means "requires support").

**Gate 1:** the user reviews the spec (decisions, sizes, names, costs) before any modelling.

Status: spec approved by the user.

---

## 2. Blender

Two generator scripts, stored in `totem.blend`'s text blocks and mirrored to `tools/blender/`: `xai_mobrack_column.py`
and `xai_mobrack_wall.py`. Edits go into the scripts, never the meshes. Each builds from a `PARAMS` dict and exports
its FBX to `BuildBeaconUnity/Assets/Beacon/` when run (as the beacon and holder scripts do).

- **Column:** a shaft with matching flared plinth and capital (mirror one profile so they are truly symmetric), rune
  bands like the beacon's, and four alcoves. Reuse the beacon's alcove code: a raised frame, a boolean niche cutter,
  and no weathering displacement near the openings.
- **Panel:** a square slab with a bevelled border and a 2x2 grid of framed niches, flat back, face runes kept inside
  the border so tiled panels read as one wall. The edges must meet cleanly when tiled: no overhanging frame.
- Empties: `attach_0..3` at each niche's back, facing out; `snappoint_*` as in the spec; the pivot at the column's
  base centre / the panel's back-face centre, matching the holders.
- Run through the MCP `execute_blender_code` path: patch the script, write it to the text block, `exec` under a
  `VIEW_3D` `temp_override` with undo pushes. Print tris and bounds.

**Gate 2 (screenshots, user looks):**
- Front, side and three-quarter viewport screenshots of each.
- Printed bounds match the spec (column 2.0 m tall; panel 1.0 x 1.0 m).
- Attach empties sit in the niches, pointing out.
- A temporary scene with four panels in a 2x2 grid and two stacked columns shows clean seams. Delete it after.
- Tri counts stay near the holders'.
- Then the 128 px icons, with the recipe in the handoff (section 4).

Status: both generators written and run. The column is 0.85 x 0.85 x 2.01 m with 3,636 tris (the weathering
subdivision was dropped to 1, which halved it). The panel is 2.00 x 0.13 x 2.00 m with 2,028 tris. Both FBXs are
exported. The 2x2 panel tiling and the stacked columns were checked, and their test copies deleted. Awaiting the
user's look before the icons. Revised after the user's look: the column's runes are set straight into the shaft (vertical lines
on the four diagonal faces, like the boss pillar's) instead of on rune bands, to set it apart from the beacon; it is
now 3,468 tris, with a 1.32 m shaft. The panel and its niches have chamfered corners (0.08 m on the outline, 0.07 m
on the niches, making irregular octagons); it is now 3,420 tris. Second revision (the user's intent): the column's runes
wrap round the shaft in two horizontal rings (above the plinth flare and under the capital's, all eight faces), set
into the shaft with no band; 3,468 tris. **Gate 2 passed** (the user: "looks good, keep"). Icons rendered with the
holders' recipe to `Assets/Beacon/xai_mobrack_{column,wall}_icon_128.png`. (Icon gotcha: read the temporary copy's
bounds in local space; its world matrix still carries the display offset until the depsgraph updates, which aimed
the first camera at empty space.) The column's alcoves are centred at 1.0 m, the column's mid-height, for symmetry; the
spec said about 1.2 m. Attach and snap positions in Unity axes are printed by each script, for stage 3.

---

## 3. Unity

With the Unity MCP connected (the `unity-bundle` skill):

1. Import both FBXs. Remap the materials by name onto the beacon's `.mat` assets and check the renderer's
   `sharedMaterials`.
2. Build `xai_mobrack_column_prefab` and `xai_mobrack_wall_prefab` by copying the boss holder prefabs' component setup:
   - `ZNetView`, `Piece` (category and comfort as the holders), `WearNTear` (column Stone, panel Wood).
   - The model child rotated -90 degrees about X (bundle meshes are Z-up).
   - Body colliders on the `piece` layer, and `slot_0..3` box colliders in front of each niche, also on `piece`.
   - `attach_0..3`, and snap points tagged `snappoint`.
3. Add the icons, label everything into the `buildbeacon` bundle, and rebuild it (`rebuild-bundle.cs`). Copy the
   bundle over and build.

**Gate 3:**
- Multi-angle scene captures of both prefabs.
- `tools/unitypy/prefab_tree.py` on our bundle lists the model, the colliders (with layer), `slot_0..3`,
  `attach_0..3` and every snap point (with tag).
- The bundle size is printed, and `build-and-verify.sh` reports the bundle embedded.

Status: **gate 3 passed.** FBX material remaps (BeaconStoneDark/Light, BeaconRune), icons with the holders' importer
settings, and both prefabs built from the holders' prefabs (column from the pillar: Stone support; panel from the wall
mount: Wood, `m_notOnFloor`). Column: 3 body boxes (the shaft box 0.52 wide, inside the frames), `slot_0..3` boxes from
the niche backs to 0.33 m out. Panel: a body box stopping at the field (z 0.10), `slot_0..3` boxes from the niche
backs (z 0.03) to 0.14. Unity multi-angle capture fine; the bundle (296,119 bytes) lists both prefabs and icons;
`prefab_tree.py` on the built bundle shows every child; everything on the `piece` layer, 2 and 9 snap points tagged.

---

## 4. Code

Check every vanilla signature used with `tools/sigdump` before writing a patch. In particular, confirm that
`Player.Interact` / the hover text take the hovered collider's object and search upward
(`GetComponentInParent<Interactable>`). The dump shows `FindHoverObject` returning the collider's `gameObject`, and
decision 4 relies on a per-slot child being found first.

- **`MobRack` component** (new file), modelled on `BossHolder`:
  - four slots on the ZDO (`xai_rack_slot_0..3`, item prefab names);
  - ItemStand-style ownership (`RPC_RequestOwn`, queued action once owned), the ward check, and drops for every held
    trophy on destroy;
  - visuals on `attach_N` via `BeaconTrophyDisplay.Build`;
  - `LinkedBeacon` through its `StationExtension`, and `Resync` of the beacon on changes.
- **`RackSlot`** on each `slot_N` child: `Interactable` + `Hoverable`, forwarding to its `MobRack` with the index. It
  shows the slot's trophy, or "Empty", with place / take hints.
- **Registration** (`RegisterMobRacks`, beside `RegisterBossHolders`): the pieces from the bundle, with a vanilla
  `StationExtension` added after `MobRack` (ours must be the first `Interactable`). Use `HolderRange` (30 m), stacking,
  the connection thread, the costs and the localization. One log line per piece: snap point and `attach_N` counts, and
  that `slot_0..3` were found.
- **`BeaconController.RebuildEffective`:**
  - count linked racks into the level (a shared "extensions" count, with `HolderCount` kept for the panel);
  - add their trophies to the effective set, under decisions 1 and 3 (slot cap across beacon and racks, one copy per
    non-stackable trophy);
  - keep the order stable (the beacon's own trophies first, then racks by distance) so what "counts" does not flicker.
- **Refusals:** a shared check for "can this trophy go here" (beacon or rack): duplicates, the cap if decision 1 says
  refuse, and wrong kind (boss trophies go on boss holders).
- **Panel (decision 5):** rack trophies in the Trophies tab, read-only, "on a rack".
- **Docs:** the README (Getting started and Levelling), `DEFAULT_DISCOUNTS.md` unchanged, and `current.md` notes.

Status: code written and built (22:47). New `BuildBeacon/MobRack.cs` (`MobRack`, `RackSlot`);
`BeaconController` (`RebuildRacks`, `CountsRackSlot`, `RackAddsLevel`, `RackInsertBlockedReason`, network-wide
duplicate check, level = holders + racks, hover and panel counts over the network); `BeaconUI` (rack trophies read-only
"on a rack", "not counted" when so); `BeaconTrophyDisplay.Build(..., faceFit)` (the panel fits trophies to 0.50 x 0.58
m, ignoring depth); `RegisterMobRacks` (Wood 10 + Stone 5, workbench, RackSlot on every `slot_N`, StationExtension
after MobRack); texts. Confirmed in the game's IL (MethodSpecs resolved with `dnfile`): `Player.Interact` calls
`GetComponentInParent<Interactable>` and `Hud.UpdateCrosshair` `GetComponentInParent<Hoverable>` on the hovered
object, so the per-alcove `RackSlot` wins. The README covers the racks (Getting started step 5, Levelling).

**Gate 4:**
- `build-and-verify.sh` is OK. The new registration lines show in the log with 4 attach points and 4 slots found.
- `beacon_prefabs mobrack` lists both pieces.
- No Harmony errors.

---

## 5. In game

Checklist (it becomes open items until verified):

- Both pieces are in the Hammer's Misc tab with icons, costs and the workbench requirement. A panel on a wood wall and
  on a stone wall does not collapse.
- Placement links to a beacon within 30 m, the thread shows, and it is refused with no beacon in range. Each rack
  raises the beacon's level by one, up to 8, with the ring growing.
- Looking at each alcove shows its own hover text. Using a trophy places it in that alcove, facing out; empty hands
  take it back.
- Rack trophies show in the Discounts tab and change build costs. The cap and duplicate rules behave as decided.
- Destroying a rack drops its trophies. Taking a rack away lowers the level at once.
- Four panels tile a 2 m x 2 m wall with clean seams. Two columns stack.
- Two clients: ownership, and "In use" behaviour like the holders. The ward refuses edits inside another player's
  ward.

**Gate 5 passed:** behaviour confirmed in game by the user (2026-09-23).

**Gate 5:** the user runs the checklist. Fixes loop back to the stage they belong to (geometry to Blender, colliders
and snap points to Unity, behaviour to code).

---

## Risks and known traps

- **The first `Interactable` wins:** add `MobRack` before `StationExtension`, and make sure the per-slot children
  (not the rack body) catch the ray. `slot_N` boxes slightly proud of the niche fronts.
- **Snapping into wood walls:** vanilla wood walls snap on their centre plane, so a panel snapped to one sinks in
  (the boss wall mount has the same issue; accepted there).
- **Level cap:** eight boss holders plus racks can exceed `MaxLevel`. Extra racks still hold trophies but add no
  level; the hover text should say so.
- **Slot cap vs. displayed trophies (decision 1):** a trophy on a rack past the cap needs a clear "not counted" state,
  or players will think it is broken.
- **Dedicated servers never load pieces:** rack state lives on the ZDO, and only the owner writes (the holders'
  model).
