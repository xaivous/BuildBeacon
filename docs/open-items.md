# BuildBeacon open action items

The single list of what is still to do. The handoff files under `docs/handoff/` point here instead of carrying their
own lists. Move an item to "Done" with the date when it lands; delete Done entries at the next handoff.

## Visuals

1. **Wall piece snapping: revisit** (noted 2026-09-24). The Boss Trophy Mount and the Trophy Panel sank into walls
   whose snap points are on their centre plane (Grausten walls, 0.4 m thick, hid them entirely; wood walls too).
   `Patches/PlacementPatches.cs` pushes the ghost out to the wall's surface after vanilla snaps it. In game (the user,
   2026-09-24) the centre snap point works, but the edge and corner ones did not work consistently, so for now
   `BuildBeaconPlugin.CentreSnapOnlyOnWalls` untags every snap point but the centre on those two pieces at
   registration (the points stay in the prefabs). Side effect: Trophy Panels no longer snap edge to edge to tile.
   To revisit: find out why the edge and corner points misbehave (which walls, which points; the `[diag] ... pushed N
   m out of it` lines in dev mode show when the push fires), fix, and bring them back by removing the call.
2. **Floating trophy heads** (later; noted 2026-09-23). Mounted trophies stand off their surface by a gap that varies
   from trophy to trophy instead of sitting against it. Mounting is `BeaconTrophyDisplay.Build` (vanilla item stand
   style, each trophy's own attach child), used by the beacon's alcoves, the holders' hooks and the rack alcoves;
   likely a per-trophy offset from the attach point to the model's back, to measure and correct.

## Performance

- **Later: one discount result per frame** (noted 2026-09-23). While building inside a radius, the discount is worked
  out three times a frame for the same piece and spot: the `Player.UpdatePlacement` prefix, `Hud.SetupPieceInfo` and
  the savings-buff postfix on `SEMan.GetHUDStatusEffects` (`BeaconRegistry.GetEffectiveRequirements`). They could share
  one result per frame, keyed by piece and ghost position and cleared when trophies, rules or savings change
  (`DiscountSavings.Commit` runs mid-frame on placement). Cheap since the rule index, so only worth it if `beacon_cost`
  timings or a profiler say so.

## Multiplayer and release

Publishing (public repository, GitHub Pages site with the matrix tool, Thunderstore) is planned in
`docs/plans/publish.md` (saved for later); what must happen before the first upload is the checklist
`docs/PREPUBLISH.md`, which covers item 4 below.

3. **Check the vanilla-style slot ownership in game.** With two clients: while one has the beacon panel open, the
   other gets "In use", and using a trophy on the beacon gives "In use" after about 2 seconds; the same for holders
   and rack alcoves. Inside someone else's ward, opening the beacon or using a trophy on the beacon, a holder or a rack
   makes the ward flash and refuse, while building nearby is still discounted.
4. **Before publishing: turn `DevMode` off by default.** `BeaconConfig.cs` defaults it to `true` for development
   (the user's `TODO: BEFORE PUBLISH, CHANGE THIS TO FALSE`); shipped as is, every player's local worlds would start
   with devcommands, god mode and fly. The Thunderstore page does not mention it.

## Notes, not actions

- The default creature rules are a balance starting point (the user); refine with `tools/balance` (see `docs/tools.md`).
- Beacons that filled more than 4 own creature slots under the old boss-based rule keep all of them counting.
- `RefundPatches` skips recording the paid cost for cheated (nocost) placements, so such pieces refund the full cost;
  dev-only.
- The Unity `Assets/Output` folder is gitignored; the bundle's home is `BuildBeacon/Assets/buildbeacon`.
- The ingredient CSV in `docs/devlog/artifacts` (gitignored) predates the trophy levels and the rebalance.
- Possibly revisit: tune the three skills' descriptions with the skill-creator optimizer. The trigger query sets are
  in `.claude/skill-evals/`. On 2026-09-23 it could not run: its `claude -p` calls used an account without credits,
  and its scripts need two Windows fixes (no `select` on pipes; point `CLAUDE_BIN` at the desktop app's
  `claude.exe`). Run it from a scratch project holding only CLAUDE.md and the other two skills, so the real skill
  cannot take the hits. The functional half (real builds, deploys, bundle rebuilds) is best skipped or kept read-only.

## Done

Cleared at the 26_9_23C handoff; see that file and the git history for what landed (confirmed in game on 2026-09-23:
the rounding savings, trophy levels, level bars and their hover, the trophy racks, and the rebalanced rules loading
with no unknown-trophy warnings). Since then:

- 2026-09-23: confirmed in game by the user: the blue radius ring, shown only while holding a build tool.
- 2026-09-23: confirmed in game by the user: build panel prices (full price struck through) and the rounding-savings
  buff.
- 2026-09-24: confirmed in game by the user:
  - the xai_ rename build (new pieces, the `xai_` log names, `com.xaivous.buildbeacon.cfg`);
  - Trophy Columns stack (not red on top of each other, linked like any rack), and the Trophy Column and Trophy Panel
    without runes;
  - the crystal's glowing core (the wisp orb's `BeaconCore` material seen through the crystal), the core following the
    lit state, and the lit crystal turning and bobbing with the wisp light and particles;
  - the performance fixes (no inventory stutter; `beacon_cost` timing and the dev-mode inventory log);
  - the wall mounts pushed out of walls when snapped by their centre (the edge and corner points stay item 1);
  - the latest beacon panel (list height, help text, slot counts, grey unreachable levels, the row hover's trophy
    icons and names, `ShowTrophySources`);
  - Hugin on the first beacon, filed in the compendium, not repeated;
  - Stacking Max: with overlapping beacons, the one whose level takes the most off a material counts.
- 2026-09-24: decided by the user: the crystal stays visible when the beacon is unlit (without its core, at rest).
- 2026-09-24: confirmed in game by the user: the Great Beacon (registration, placement and 6 m spacing, boss alcoves in
  their fixed places at the tuned sizes, level and radius, holders and racks linking, the lit crystal and shards).
- 2026-09-24: the Thunderstore icon: the user's art, `art/icon.webp` (1254×1254 master) scaled to the 256×256
  `BuildBeacon/Package/icon.png`.
