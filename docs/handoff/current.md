# BuildBeacon handoff: current

Notes since the last handoff, `26_9_23C.md` (base commit `86cef3c`). Read that file first; this one is the delta.
Append here as work happens: what changed, decisions, new gotchas. Action items live in `docs/open-items.md`. At the
next handoff (the `handoff` skill), fold this into a new dated file and reset it to this template.

## Changed since 26_9_23C

- 2026-09-23: the user confirmed in game the blue radius ring (shown only with a build tool) and the build panel's
  struck-through prices with the savings buff; both moved to Done in `docs/open-items.md`.
- 2026-09-23: Discounts tab row hover: trophies not in the network are now greyscale (`BeaconUI.GreySprite`: the
  sprite's part of its atlas blitted to a render texture, read back, desaturated, cached per sprite; falls back to the
  old 0.3 alpha fade and logs a warning if that fails), faded to 0.6. Each trophy sits in a 56 px cell with its name
  under it ("trophy" stripped, beige in network, grey otherwise; one font size 12 to 9 for the whole hover, the largest
  at which every word fits a cell). The hover box widened from 280 to 316 px (five cells a row). Built, not yet seen
  in game.
- 2026-09-23: new local (per player, not synced) config `General.ShowTrophySources`, default true. Off hides the
  whole trophy section of the Discounts tab row hover (the text stays), so players can keep trophy sources a mystery;
  the row itself still shows the trophies already counting, and the filter only searches those. Read on each hover,
  so it applies without reopening. README (both copies) documents it.
- 2026-09-23: two visual items noted for later (open items 2 and 3): sunken snap points and floating trophy heads
  with a per-trophy gap. The open items were renumbered.
- 2026-09-23: performance survey (read from the code and the game's IL with sigdump; costs are estimates, nothing
  measured or changed yet). The dominant cost is rule matching: `DiscountRules.TrophyMatches` runs `Normalize` (LINQ,
  several allocations) on both names for every rule, so each trophy costs ~365 Normalize calls per requirement per
  beacon in `GetEffectiveRequirements` (61 boss + 121 mob rules). That runs 3 times a frame while building inside a
  radius (UpdatePlacement prefix, Hud.SetupPieceInfo every frame, the savings-buff postfix on
  SEMan.GetHUDStatusEffects), once per unknown piece on every inventory change inside a radius
  (Player.UpdateKnownRecipesList → HaveRequirements(piece, IsKnown), ghost or player position), and once per piece
  button when the build menu opens, changes tab or search. Each beacon's 1 s `LoadFromZdo` (RebuildEffective, display
  Choose) does the same kind of matching via IsBossTrophy/KindOf/BossOrder.
- 2026-09-23: performance fixes for the stutter of seconds on inventory changes the user noticed. (1)
  `DiscountRules` indexes the rules by normalised trophy name when they load (`BuildTrophyIndex`, `TrophyKey` cache);
  IsBossTrophy, IsMobTrophy, KindOf, BossOrder, BossRulesFor, MobRulesFor and IsStackable are dictionary lookups now,
  and `Normalize` is a single StringBuilder pass. BossRulesFor/MobRulesFor return the shared lists (do not modify).
  (2) `Player_HaveRequirements` skips `RequirementMode.IsKnown` in both prefix and postfix (an unpaired End would
  unwind an enclosing swap). Side effect: a boss-freed material no longer lets a piece be discovered before the player
  has seen that material (vanilla discovery again). Not done: sharing one result between the three per-frame calls;
  should be negligible after (1). Verification: `beacon_cost` prints the time per discount calculation; in dev mode
  `[diag] inventory change took X ms` logs any inventory change over 2 ms (vanilla work included). Built, not yet
  seen in game.
- 2026-09-23: wall mounts sinking into walls when snapped (the user: they vanish into Grausten walls). Cause, from the
  game's bundles: Grausten walls are 0.4 m thick with snap points on their centre plane (wood walls the same; stone
  walls have them on both faces), and our wall pieces' snap points are on their back plane, so vanilla's
  FindClosestSnapPoints put their back half a wall deep. New `Patches/PlacementPatches.cs`: a postfix on
  `Player.UpdatePlacementGhost` finds the coincident snap pair among `m_tempPieces` (not our own pieces), checks that
  the ghost's back is inside the target collider just in from the snap point, raycasts the target collider from in
  front and moves the ghost out along its +Z to the surface less 1 cm (WearNTear's support overlap box pads 0.15 m, so
  flush is still supported). Replaces the handoff's "wall pieces sink into wood walls (kept as is)".
- 2026-09-24: in game the centre snap point works, the edge and corner ones not consistently (the user). For now
  `BuildBeaconPlugin.CentreSnapOnlyOnWalls` untags all but `snappoint_center` (mount) / `snappoint_centre` (panel) on
  the two wall pieces at registration; the prefabs keep them. Trophy Panels no longer tile by snapping. Open item 2
  tracks the revisit. Healthy log changes: `Boss holder xai_beacon_bossholder_wall ... 1 snap points` (was 5) and
  `Trophy rack xai_beacon_mobrack_wall ... 1 snap points` (was 9), each after a `... snaps by its centre only (1 kept,
  N snap points switched off)` line.
- 2026-09-24: Trophy Columns stack. The user saw a red ghost placing a column on a column. Cause, from the game's IL
  (Player.UpdatePlacementGhost): when the placement ray hits a piece whose `WearNTear.m_supports` is false, the status
  is Invalid, so nothing can be placed on it. The column prefab had `m_supports: 0`; now 1 (Unity prefab, bundle
  rebuilt). Its snap points (bottom at 0, top at 2 m) already fit. The upper column does not need support
  (`m_noSupportWear: 0`), so it stays up even if the lower one goes. The boss pillar, both wall pieces and the beacon
  still have `m_supports: 0` (nothing can be built on them); unchanged, not asked for.
- 2026-09-24: no runes on the creature trophy pieces (the user). `tools/blender/xai_mobrack_column.py` and
  `xai_mobrack_wall.py` have a `runes` PARAM, now False (the rune code stays); the column's shaft now weathers evenly
  (the rune-ring mask is skipped), the panel's rim stays flat. Regenerated in `totem.blend` (text blocks match the
  repo; the .blend is unsaved for the user to save and commit), FBXs re-exported with only BeaconStoneDark and
  BeaconStoneLight (column 2328 tris, panel 2712), attach and snap positions unchanged. Unity: both FBXs reimported,
  each prefab's `model` renderer set to the FBX's two materials (the BeaconRune entry dropped; it would have drawn
  the last submesh twice), bundle rebuilt (277488 bytes). The beacon and boss pieces keep their runes.
- 2026-09-24: glowing core in the beacon's crystal (the user wanted the wisp torch's colour and brightness without
  washing out the crystal). Findings from the game's bundle: `demister_ball` (the wisp orb) is Standard, Fade,
  albedo (0.629, 0.25, 1), HDR emission (0, 1.247, 1.498), no textures; `crystal_window` has no colour texture, only a
  normal map (`crystal_window_n`, gloss 0.9, alpha 0.376), so a strong emission on it can only be flat. Instead:
  `xai_build_beacon.py` PARAM `core_scale` 0.55 adds the same shard, smaller, inside the crystal (no random draws, rest
  of the model unchanged) with a new material slot `BeaconCore`; new `Assets/Beacon/BeaconCore.mat` with the orb's
  values and render queue 2999 (the crystal runs at 3000 in game, from the crystal_window template), remapped in the
  FBX importer. The plugin needed no change: BeaconCore falls to the "*" template entry, and an emissive Standard
  material there is kept as authored. Expect the log line `Beacon material "BeaconCore": kept Standard (emissive, no
  glow template)`. The beacon prefab file did not change (its model takes the FBX's materials). The core always
  glows, lit or not (open item 1 could tie it to the lit state with a MaterialPropertyBlock on that submesh).
- 2026-09-24: the core follows the lit state (the user). `BeaconController.SetCoreLit`, called from `UpdateVisuals`
  beside the wisp light (lit = `TrophyCount > 0`, so it settles within the 1 s resync on other clients): finds the
  renderer and index of the BeaconCore material once, then sets a MaterialPropertyBlock on that submesh only
  (unlit: `_Color` alpha 0 and `_EmissionColor` black; lit: an empty block, the material as authored). Per beacon,
  since the material is shared; applied only when the state changes. Logs a warning if the model has no BeaconCore.
  Placement ghosts never run it (BeaconController.Awake stops for them), so the ghost shows the core.
- 2026-09-24: the crystal spins and bobs while lit, and the wisp light and particles move with it (the user).
  Structure: `xai_build_beacon.py` PARAM `crystal_separate` (True) exports the crystal and core as their own object
  and FBX, `Assets/Beacon/xai_build_beacon_crystal.fbx`, origin at the crystal's centre (Unity (0, 2.973, 0)); the
  beacon FBX loses those 40 tris and the BeaconCrystal/BeaconCore materials. A second FBX rather than a second object
  in the beacon FBX, because the prefab's `model` is a live instance of that FBX and a changed hierarchy would move
  its fileIDs under the prefab's overrides. Prefab: new child `crystal` (the pivot, identity rotation, at the centre)
  holding `crystal_model` (an instance of the crystal FBX, rotated -90 about X like `model`, layer piece); the crystal
  FBX's importer copies the beacon FBX's settings and remaps BeaconCrystal and BeaconCore. Plugin: the glow clone
  (`ActiveEffect`) goes under the pivot at its origin (`BuildBeaconPlugin.CrystalPivot`); `BeaconController.Update`
  runs `AnimateCrystal`: 18 degrees/s about the vertical (a turn in 20 s), +-2 cm bob every 4 s with a random phase
  per beacon, eased in and out over 1.5 s with the lit state; visual only, not synced. Healthy log changes: `Beacon
  glow: ... on the crystal's pivot at (0.00, 2.97, 0.00)` instead of `centered on the crystal at ...`.
- 2026-09-24: the Elaking is a creature, not the Deep North's boss (the user); the Deep North has no boss trophy yet.
  `DefaultBossRules` loses the seven TrophyElaking lines; `DefaultMobRules` gains `TrophyElaking|Elaking Hair
  Bundle|2` (the item is `ElakingHairBundle`, English "Elaking Hair Bundle"). Tools: gen_matrix's Deep North row makes
  the Elaking a creature (not rare), gen_defaults drops it from the boss table and names it as a creature;
  DEFAULT_DISCOUNTS.md regenerated (7 bosses / 54 boss rules, 55 creatures / 122 creature rules), no "below target"
  lines. README (both copies): "up to Fader and the Ashlands (the Deep North has no boss trophy yet)". Dev profile rules
  files edited by hand to match (boss lines removed, the creature line added after TrophyJotunWarrior); the running
  game hot-reloaded them: `Loaded 7 boss trophies (54 free materials), 122 creature rules`. New healthy signature:
  that line. An Elaking trophy already on a boss holder no longer counts there (not a boss trophy any more); take it
  back and slot it in the beacon or a rack. The balance matrix artifact was not republished.
- 2026-09-24: publish plan written, `docs/plans/publish.md` (the user asked: public repo, GitHub Pages site with the
  README and the matrix as a player tool, Thunderstore readiness, an icon prompt). Audit results in its Stage A: no
  game binaries or extracted assets in the tree or history, no secrets, no packed data in totem.blend; to decide:
  the template LICENSE, the author email in history, the internal AI docs, the repo name. Nothing implemented yet.
- 2026-09-24: the user's publish decisions: MIT licence (`LICENSE`: MIT, © 2026 xaivous, the user's handle; set by
  the user when merging the rename), the author email stays in history, the internal docs stay, the repo stayed
  on the author's personal account for now (the rename branch was merged to master as `8b3517c` and pushed).
- 2026-09-24: the user confirmed in game the rename build, stacking columns, the rune-free rack pieces, the crystal
  core with its lit state and motion, the performance fixes and the centre-snap push; moved to Done in
  `docs/open-items.md`. Then also confirmed: the latest beacon panel, Hugin, Stacking Max; and decided: the crystal
  stays visible when unlit. The open items are now 1 wall snapping revisit, 2 floating trophy heads, 3 two-client slot
  ownership, 4 Thunderstore icon, 5 DevMode off (the "In-game checks" section is gone).
- 2026-09-24: GitHub Pages site built (plan Stage B), not deployed. `tools/balance/matrix_data.py` now holds the matrix
  data (rules reader, catalogue `B`, `RARE`, targets, `LEVEL_PCT`) that `gen_matrix.py` imports (its page is byte-for-
  byte unchanged). `tools/site/build_site.py` renders `BuildBeacon/README.md` (Python-Markdown; it widens 3-space list
  indents and adds blank lines before list items, which Python-Markdown needs) and embeds the matrix data (catalogue +
  default rules as rules-file text) into `site/index.html`, copying `site/site.css` and `site/matrix.js` to
  `site/_build/` (gitignored). The page: header (title, "A Valheim mod by Xaivous", version from the manifest, GitHub
  button, theme toggle), tabs "The Mod" / "Discount Matrix" routed by `#matrix` (the README's own anchors stay on the
  mod tab). The matrix tool parses rules like RulesFile/DiscountRules, draws the table in the browser (filter box,
  biome select, "Only trophies with rules", "Balance view" footer with the targets), sums ticked trophies like a
  beacon ("Your trophies"), and reads a server's pasted or picked rules files plus level percents (saved in
  localStorage). Verified in the browser pane and headless Edge: numbers match gen_matrix (70 trophies, 62 with rules,
  94 materials, 8 groups, 0 misses), the Black Forest set gives Wood/Fine Wood/Core Wood/Stone level 4, pasted rules
  and bad lines are reported, no horizontal page scroll at 375 px, both themes. `.github/workflows/pages.yml` builds
  and deploys on master pushes touching the site's inputs; `.claude/launch.json` has a `site` preview (port 8765).
  Gotcha found: `showTab()` must run at the end of matrix.js, or opening `#matrix` directly builds before `state`
  exists.
- 2026-09-24: the repo moves to `github.com/xaivous/BuildBeacon` by publish time (the user). The site's GitHub button
  points there (`REPO_URL` in build_site.py); the Pages URL will be `https://xaivous.github.io/BuildBeacon/`.
  PREPUBLISH has the move as an item (transfer + rename, then `git remote set-url`).
- 2026-09-24: new piece planned, the Great Beacon (an ~8 m beacon whose own alcoves take boss trophies):
  `docs/plans/great-beacon.md`, staged like the racks. Findings: overlapping beacons are already handled (extensions
  link to the closest station; discounts combine by Stacking), so 8 m spacing is enough, via vanilla
  `Piece.m_blockRadius` + `m_blockingPieces` (UpdatePlacementGhost gives "Need more space"); bosses in a beacon's own
  slots are still counted and the panel's boss picker and texts still exist, only `InsertBlockedReason` refuses them.
  The user answered (plan section 0B: 7 boss alcoves in two offset rows with a rune panel as the front, own bosses
  raise the level, radius 65 m + 5 m a level, 6 m spacing between any two beacons, 20 Grausten + 5 Surtling Core + 1
  Crystal); spec written, `docs/design/great-beacon.md`, approved (gate 1).
- 2026-09-24: Great Beacon, Blender stage (gate 2 pending). `tools/blender/xai_build_beacon.py` now builds both: `GREAT`
  is `PARAMS` with overrides, and new PARAMS keys the beacon leaves off (`alcoves` as (angle, height) list,
  `mid_band`, `rune_panel`, `buttresses`, `shards`, `print_attach`, `display_offset`); helpers `add_wedge`, `add_band`
  (the top band factored out); the bind-rune is in `GLYPHS`, not `RUNES` (adding to RUNES would change the beacon's
  random rune picks). The regenerated beacon body and crystal are identical to before (vertex/face/material
  fingerprints). The Great Beacon: 18084 tris, 3.6 m across, body 7.06 m, crystal centre 7.68 m (tip about 8.0 m),
  crystal FBX 120 tris (crystal, core, 4 shards); exported `xai_great_beacon.fbx` and `xai_great_beacon_crystal.fbx`
  (not yet imported in Unity); shown at x = -6 in the scene. Attach points (Unity): top row (+-0.3457, 4.45,
  +-0.3457), lower row (+-0.5426, 2.6, 0) and (0, 2.6, -0.5426); order front diagonals, sides, back diagonals, back.
  The middle rune band went between the alcove rows instead of low on the pillar (spec updated). totem.blend unsaved.
  The user: the buttresses floated (the pillar, 1.12 m, is wider than the top tier's tapered top, 1.09 m, so their
  feet stood on nothing); now `buttresses` = {foot_tier 1, top 0.75, width}: each stands on the second tier's top (its
  corner reaches 1.49 m) and meets the pillar 0.75 m above the pillar's foot. Beacon still identical.
  Gate 2 passed (the user: "looks good"). Shards are now separate objects (`xai_great_beacon_shard_0..3`, children of
  the crystal, origin at their own centres, tilt kept as rotation) so the code can spin and bob each (the user's note,
  in the spec: they orbit with the crystal's pivot and also spin and bob on their own at differing rates). Icon:
  `Assets/Beacon/xai_great_beacon_icon_128.png` (512 px EEVEE render in a temporary scene, handoff recipe, Lanczos to
  128). Unity (gate 3 passed): both FBXs imported with the beacon FBX's importer settings and material remaps;
  `xai_great_beacon_prefab` built from the beacon prefab (model swapped, crystal pivot at y 7.68 with the crystal
  model and its four shard children, `attach_0..6` (+Z out), colliders body (1.8 x 8.0 x 1.8 at y 4.0), base
  (3.5 x 1.15 x 3.5), top (2.7 x 1.45 x 2.7 at y 6.35), Piece `$piece_xai_great_beacon`/`_desc`, WearNTear health 2000),
  labelled; bundle rebuilt and embedded (453 KB); `prefab_tree.py` on the built bundle shows it all.
  Code (gate 4 passed, built and deployed; gate 5, in game, is open item 3): `RegisterGreatBeacon` (PrepareBundlePrefab,
  Stonecutter recipe 20 Grausten + 5 SurtlingCore + 1 Crystal, `m_ownSlotKind = Boss`, `m_ownSlotCount` = the
  prefab's attach_N count, station with the beacon's name and its connection point at 2.6 m) and `SetBeaconSpacing`
  (m_blockRadius 6 + m_blockingPieces both beacons, on both). `BeaconController`: own-slot mode; `Level` adds the Great
  Beacon's own counted bosses first (also in `RackAddsLevel`); `Radius` via `DiscountRules.GreatRadiusFor`
  (`GreatRadiusAtLevel1` 65, `GreatRadiusPerLevel` 5, synced); `InsertBlockedReason` takes bosses on the Great Beacon
  (duplicate across alcoves and holders, full at 7) and refuses creature trophies; `PieceNameKey` for hover and panel
  title; shard motion (`FindShards`/`AnimateShards`: long axis from the mesh bounds, speeds 40/51/62/73 deg/s
  alternating, bob 5 cm at 2.5-3.6 s). `BeaconTrophyDisplay`: alcoves from `attach_N` (4 or 7), and a shrink-only face
  fit (1.8 x 1.8) for the Great Beacon's bosses. `BeaconUI`: the Great Beacon's Trophies tab (own bosses n/7 with the
  picker, holder bosses, no own creature section). Texts added. Not done: the README does not describe the Great Beacon
  yet; `beacon_dump` does not name the kind.
- **Boss rules rebalanced (the user, 2026-09-24):** `DefaultBossRules` drops Queen Bee from Eikthyr and Bloodgold from
  Fader, and adds Coal to Bonemass, Crystal to Moder and Warrior Trophy (`TrophyCharredMelee`, 5 per Grausten Chest)
  to Fader (55 rules); `DefaultMobRules` adds `TrophyCharredMelee|Warrior Trophy|2` (123 rules, meets the level 2
  target). `DEFAULT_DISCOUNTS.md` and
  `balance_matrix.html` regenerated. The boss rules file's example `TrophyDragonQueen | Crystal` became `| Obsidian`
  (Crystal is now a default). An existing `BuildBeacon.BossRules.txt` keeps its old rules until deleted.
- **New discount curve (branch `balance-curve`, the user, 2026-09-24):** creature levels are 50/80/90% (2x, 5x, 10x;
  `LevelPercents` default "50, 80, 90"; a config file still holding the old default "25, 60, 80, 90, 95" is moved to the
  new one at load). New synced `BossPercent` (General, 100, range 50-100): what a boss trophy takes off its materials;
  100 = free as before, 95 = 20x, rounded and saved like a creature discount (`BeaconRegistry`). `DiscountRules`:
  `BossPercent`, `BossMakesFree`, `IsFree` renamed `BossCovers`, `MaterialDiscount.Boss` (a boss covers it) with `Free`
  now derived (Boss and 100%), `BossAmount` ("Free" or "-95%"), `DescribeAmount` ("Boss · -95%"). Panel: boss rows show
  that text, a tooltip with the multiplier (new text `xai_beacon_boss_tooltip`) and the savings bar below 100; level
  bar colours spread over the levels there are (`LevelColor`: 3 levels are red, yellow, blue). Piece, hint and Hugin
  texts say "free, or nearly free". `DefaultMobRules` retuned: 111 rules, every one +1, at most three trophies per
  material; construction woods and stones 2 from common trophies and 3 with a rare one; powerful materials (metals,
  cores, Crystal, Dvergr parts, Charred Cogwheel) at most 2; single-creature drops 1 (Warrior Trophy and Elaking Hair
  Bundle, which the user had set to 2, are now 1 under the +1 rule). New sources: Brenna -> Surtling Core, Goblin
  Shaman -> Tar; dropped: Brute/Stone, Shaman/Corewood, Draugr/Bone Fragments, Kvastur/Ancient Bark, Wraith and
  Rancid Remains/Iron, Fenring, Cultist and Geirrhafa/Silver, Geirrhafa/Crystal, Goblin Shaman and
  both Brute Brothers/Black Metal, Hexen/Frostcore. Tools: `matrix_data.py` (`LEVEL_PCT`, `BOSS_PCT`, `POWERFUL`,
  `POWERFUL_CAP`), `gen_matrix.py` checks (+1 only, caps; prints "off target"), `gen_defaults.py` builds its level table
  from `LEVEL_PCT`; site: Boss percent field in the server panel, boss cells/results at 95%, new targets. README: the
  curve table with a Boss column, "Free, or 95% off: the server's choice" section, config rows; `docs/tools.md` targets.
  The Dev profile's `BuildBeacon.MobRules.txt` still holds the old rules until deleted.
- **Site matrix: Max level row** (the user): always the last footer row, sticky at the bottom of the table; per material
  the creature trophies' levels added up with one of each, capped at the top level (a stackable trophy reaches the top,
  as `MaxMobLevel`), coloured by level, "·" where only a boss discounts it; the tooltip gives the percent, multiplier
  and trophy count. The balance rows stack above it (`site.css` offsets).
- **Panel colours (the user):** levels orange, yellow, green (`LevelColors`, spread by `LevelColor`); a boss's material
  is the tier above, one solid blue bar (`BossColor`) the width of the level bar (`AddBossBar`), reading "Free" or
  "95% | 20x" (`DiscountRules.BossBarText`, `MultiplierText`; `DescribeAmount` removed). The site matrix and the
  balance page use the same level colours (lv2-lv4 classes). `gen_matrix.py` had its "·" and "†" mangled by a
  PowerShell `Get-Content`/`Set-Content` round trip (read as ANSI); repaired (cp1252 -> UTF-8). Footer level cells
  (Max level, balance rows) keep their dark ink: the footer's muted colour now applies only to non-level cells
  (`site.css`, `gen_matrix.py`).
- Boss bar (the user, after a look in game): the same segments as the level bar (`AddSegments`, shared), every one
  blue, label across them; hover "Level 4: 95% off" (`xai_beacon_boss_tooltip`, "Level {0}: {1}% off" with TopLevel + 1;
  the text had been missing, so the raw key showed) and the multiplier; "Free: ..." at 100.
- Site header: a Discord button (`DISCORD_URL` in `tools/site/build_site.py`, https://discord.gg/7vcX9mujW3) before
  GitHub; the Thunderstore button still goes first once `THUNDERSTORE_URL` is set. Both carry inline SVG icons
  (`DISCORD_ICON` from Simple Icons, CC0; `GITHUB_ICON` the Octicons mark, MIT; `currentColor`) and a popover
  (`header_link`, `.tip` in `site.css`: below the button, left-aligned, on hover and keyboard focus): "Join our Dev
  Community", "See source code". The light/dark toggle has one too ("Switch to light mode" / "Switch to dark mode",
  set by `themeTip` in `matrix.js` from the effective theme, also on a system theme change; it is also the button's
  aria-label; the native title is gone), right-aligned (`.tip.end`) since the toggle sits at the right edge.
- Then (the user): the boss bar has no label any more (`BossBarText` removed); its hover carries it. The level bar's
  hover says "Level n of m" with m the most the row's material can reach (`MaxMobLevel`, the non-grey segments), not
  the top level: Leather Scraps reads "Level 1 of 2".
- Site: a "Building Pieces" tab (`#pieces`, cards at `#piece-<id>`), between The Mod and Discount Matrix. Content in
  `tools/site/pieces.py` (three groups: beacons, boss trophy holders, trophy racks; per piece kind, icon, summary,
  cost, station, facts, notes; default config values). `check_costs` stops the build if a listed cost is not a
  `RequirementConfig` line in `BuildBeaconPlugin.cs`; icons are copied from `BuildBeaconUnity/Assets/Beacon` into
  `_build/img/`. `matrix.js` tab routing now has three tabs (`tabFor`) and scrolls to an anchor inside a panel that
  was hidden. On phones (<= 480 px) the tabs read "The Mod", "Pieces", "Matrix". The Pages workflow also triggers on
  `BuildBeaconPlugin.cs` and the icon PNGs.
- Changelog and Roadmap (the user): the changelog moved out of the README into `BuildBeacon/CHANGELOG.md` (copied to
  `Package/`; `scripts/publish.ps1`/`.sh` copy it at Release, and Thunderstore shows a package's CHANGELOG.md as its
  Changelog tab); a new public `ROADMAP.md` at the root. The user cut it to "Next" (wall pieces that tile, trophies
  that sit flush) and the Deep North, which says plainly that the biome has no boss trophy and may never get one (the
  internal list stays `docs/open-items.md`). Site: "Roadmap" and "Changelog" tabs render them (`markdown_html`, heading ids
  prefixed `roadmap-`/`changelog-`). `matrix.js` routing is generic now: a hash naming a tab shows it, any other shows
  the tab whose panel holds that element; the open tab is scrolled into the tab row. Below 800 px the tabs read
  "Pieces"/"Matrix", and the tab row's scrollbar is hidden. Pages also triggers on both files.
- Release flow (the user asked for building, packaging and publishing with a version check): `tools/release/release.py`
  (stdlib Python): `check` (version agrees in PluginVersion, manifest.json and the top CHANGELOG heading; manifest,
  icon and thunderstore.toml by Thunderstore's rules; `--release` adds DevMode off, website_url set, no changelog
  placeholder; `--online` asks Thunderstore that dependencies exist and the published version is not ahead; `--tag`),
  `bump`, `package` (Release build, reproducible `dist/BuildBeacon-<v>.zip`, verified), `publish` (the rehearsal: all
  checks, git clean/pushed/tag free, the zip, no upload) and `upload` (the same, then always a typed version
  confirmation, `tcli publish --file` with the token from TCLI_AUTH_TOKEN, TCLI_AUTH_TOKEN_FILE or
  `~/.config/thunderstore/buildbeacon-token`, then an annotated tag). The `release` skill
  (`.claude/skills/release/`) runs the same commands and flags from Claude; for `upload` it runs `publish`, asks the
  user to type the version, and pipes exactly that reply to the script's prompt. `thunderstore.toml` holds only the
  team (`xaivous`), community and categories (mods, building, crafting, client-side, server-side, ai-generated);
  `dotnet-tools.json` pins tcli 0.2.4 (it needs only the token for `--file`; Thunderstore reads name and version from
  the zipped manifest). CI: `.github/workflows/release-checks.yml` (check on pushes and PRs; tag checks with
  --release --online on `v*` tags). The Release build no longer zips (`scripts/publish.*`), and the
  `Package/README.md`/`CHANGELOG.md` copies are gone: the zip reads the root README and `BuildBeacon/CHANGELOG.md`.
  Building cannot run in CI: Jötunn compiles against the local game's assemblies. Docs: `docs/releasing.md`. DevMode
  now defaults to false (an existing config file keeps its saved value; the Dev profile's is true), and the manifest's
  `website_url` is `https://xaivous.github.io/BuildBeacon/`: `check --release --online` passes, with one warning
  (BepInExPack 5.4.2351 is out; the manifest has 5.4.2333).
- Quieter log (the user): new local setting `VerboseLogging` (5. Dev, off) and `BuildBeaconPlugin.Verbose(...)`. Behind it:
  the piece registration details (materials, glow, effects, ring, snap points, each piece), each trophy's display fit,
  the savings-rounding and Hugin lines, and every `[diag]` line (placement push, placed-piece plan, slow inventory
  changes, and DiagnosticPatches' beacon ApplyDamage/Destroy warnings with stack traces, PREPUBLISH C5). These were on
  DevMode, which now only means cheats in local worlds. Players see one line instead: "Registered 6 building pieces:
  Build Beacon, ...". Still at normal level: loaded, settings carried over, server settings, rules loaded/written,
  drops on destroy, console command output, warnings and errors. To read diagnostics, turn VerboseLogging on.
- Server settings (the user asked whether configs are server-authoritative, like SpeedyPaths): they already were. Every
  `ConfigUtil.Synced` entry (and the rules, through the BossRules/MobRules entries) is admin-only, which Jotunn's
  SynchronizationManager pushes to clients; the Default profile's log shows it (BossPercent 95 from the server on
  joining, 100 again after leaving). New: `LogServerSettings` on `SynchronizationManager.OnConfigurationSynchronized`
  logs "Using the server's settings: ..." (or "The server changed its settings: ...") on clients, with the values
  in force. README's Multiplayer section says so. Built; not yet seen in game.
- False rule warnings (the user's `Default` profile, published 0.1.0 with Adventure Backpacks, PlantEverything, BetterUI
  and others): 62 "Rule trophy ... does not match any item" and 95 "Rule material ..." warnings at the main menu, right
  after piece registration. That check ran against an ObjectDB some other mod leaves at the menu without the game's
  items (the Dev profile has none there, so it was skipped). Now `ValidateAgainstObjectDB` runs on Jotunn's
  `ItemManager.OnItemsRegistered` (the full database, each game start) and after rules reloads, skips a database without
  `Wood` and `TrophyDeer`, and checks each set of rules once (`s_validatedRules`). The registration-time call is gone.
- 0.2.0 (the user): the plugin GUID drops `com.`: `xaivous.buildbeacon`. It names only BepInEx's config file (saved
  data uses `xai_` keys, the rules files fixed names, Jotunn's version check already needs matching major.minor), so
  `BuildBeaconPlugin.MigrateConfigFile` copies `com.xaivous.buildbeacon.cfg` to the new name and reloads it when the
  new file does not exist yet (the old one stays as a backup); log line `Settings carried over from ...`. README, the
  site hint and CHANGELOG say so. 0.1.0 is tagged `v0.1.0` (on `896d5c1`, what was uploaded).
- Dependencies (the user): the manifest depends on `denikson-BepInExPack_Valheim-5.4.2351` (was 5.4.2333); Jötunn
  stays 2.30.2 (latest on NuGet and Thunderstore). This machine's `Environment.props` (gitignored) sets `BEPINEX_PATH`
  to the Dev profile's `BepInEx`, so r2modman updates reach the build (the Dev profile had Jötunn 2.30.1: update it
  in r2modman). `docs/releasing.md` has an "Updating BepInEx and Jötunn" table. `check --release --online`: no
  warnings.
- Plugin icon (the user's art): the 1254×1254 original kept as `art/icon.webp` (the repository's art masters),
  scaled with Lanczos to the 256×256 `BuildBeacon/Package/icon.png` Thunderstore needs (replacing the stub).
  PREPUBLISH C1 ticked; open item 4 (Thunderstore icon) done, DevMode is now item 4.
- READMEs moved (the user): the player README is now the root `README.md` (was `BuildBeacon/README.md`; still copied
  to `BuildBeacon/Package/README.md` for Thunderstore, by hand while developing and by the Release scripts, which
  now read `$ProjectPath/../README.md`); the old root README (the Jötunn stub's developer guide with the Unity, MCP
  and Blender notes) is `docs/development.md`. Updated: the site build, the Pages trigger, the `.csproj` item (a
  link to `..\README.md`), both skills, `docs/tools.md`, the publish plan and PREPUBLISH A5 (what is left for
  visitors: links, a build pointer, licence, credits).
- New piece icons from the user (their renders, 128 px RGBA): copied over the six existing icon PNGs so the .meta
  files, bundle label and asset names the code loads stay the same (`icon_beacon_mob` -> `xai_beacon_icon`,
  `icon_beacon_boss` -> `xai_great_beacon_icon_128`, `icon_hook_boss` -> `xai_bossholder_pillar_icon_128`,
  `icon_wall_boss` -> `xai_bossholder_wall_icon_128`, `icon_column_mob` -> `xai_mobrack_column_icon_128`,
  `icon_wall_mob` -> `xai_mobrack_wall_icon_128`); the new-named files removed. Reimported, bundle rebuilt (453728
  bytes), copied and embedded.
- Greydwarf Shaman gives Queen Bee +1 (the user; Queen Bee's only source since Eikthyr lost it): 112 creature rules.

## New gotchas (Great Beacon work)

- **Unity's FBX import flips X:** Blender (x, y, z) lands at Unity (-x, z, -y), which keeps the model looking the same
  from the front. The generators' printed "Unity" positions used (x, z, -y) until 2026-09-24 (fixed in the beacon,
  column and panel scripts); every alcove layout so far is mirror-symmetric, so only fill orders were affected (the
  column's attach_1 and attach_3 sit in each other's Blender alcove).
- **A second `PrepareBundlePrefab` broke registration (2026-09-24, found in game):** `TintRing` cloned the ring segment
  as `xai_beacon_ring_segment`; for the Great Beacon, Jötunn refused the second clone ("Failed to clone prefab, name
  already exists"), returned null, and the NullReferenceException aborted `RegisterPiece` after the beacon: no holders,
  racks, Great Beacon or texts (raw `xai_beacon` names). Now the tinted segment is made once and shared
  (`s_ringSegment`), and `RegisterGreatBeacon` runs in a try/catch so its failure only skips itself. Any new shared
  clone must be named per piece or cached.

## Great Beacon, after the first look in game (2026-09-24)

- Registration fixed (above); the user sees all pieces.
- Boss trophies were shrunk too much (the 1.8 m face fit gave 33-62%; the log's vanilla sizes: Fader 5.41 x 4.50,
  Queen 4.62 x 5.16, Yagluth 4.61 x 3.71, Bonemass 3.16 x 3.19, Moder 2.65 x 3.08, Eikthyr 2.30 x 2.92, Elder 1.17 x
  2.05 m). Now at most 90% of vanilla (`BeaconTrophyDisplay.GreatTrophyMaxScale`) and fitted to 4.5 m
  (`GreatTrophyFit`): Fader 0.83, Queen 0.87, the rest 0.9. `Build`'s `shrinkOnly` became `maxScale`. New local
  setting `ShrinkGreatBeaconTrophies` (General, default true): off shows vanilla size; the display rebuilds when it
  changes (within the 1 s resync).
- Then per-boss sizes (the user): `GreatTrophyScales` in `BeaconTrophyDisplay` (keys are trophy prefab names,
  matched like the rules): Eikthyr, Elder, Moder 1.0; Bonemass 0.8; SeekerQueen, GoblinKing, Fader 0.6; anything else
  keeps 0.9 with the 4.5 m fit. `Build` gained `fixedScale` (the log line says "Fixed"). The setting's description
  lists the sizes. The user then tuned them by hand in the source: Bonemass 0.75, SeekerQueen/GoblinKing/Fader 0.65.
- Fixed alcoves per boss (the user): `GreatTrophyAlcoves` + `ChooseGreat` in `BeaconTrophyDisplay` (used when the
  display has a face fit, i.e. the Great Beacon): Eikthyr 0 (front-left top), Elder 1 (front-right top), Bonemass 5
  (back-right top), Moder 4 (back-left top), GoblinKing 3 (right lower), SeekerQueen 6 (back lower), Fader 2 (left
  lower); other trophies take the first free alcove. Left/right as seen from the front (Unity +X is the viewer's left).
- The user confirmed the Great Beacon in game (gate 5 passed; open item moved to Done). README (both copies): Getting
  started step 6, a "The Great Beacon" section (cost and station, the seven boss alcoves and their places, level and
  radius table, trophy sizes and `ShrinkGreatBeaconTrophies`, the lit crystal), the 6 m spacing under Levelling, the
  panel line, config rows (`ShrinkGreatBeaconTrophies`, `GreatRadiusAtLevel1`, `GreatRadiusPerLevel`), the boss-file
  rule; also fixed stale lines: "seven bosses" (was eight, since the Elaking change), the wall-mount tip (centre snap,
  sits on the face), and the changelog now lists the racks and the Great Beacon. Open items renumbered: 1 wall snapping
  revisit, 2 floating trophy heads, 3 two-client slot ownership, 4 Thunderstore icon, 5 DevMode off. The plan is saved for later; `docs/PREPUBLISH.md` is the checklist of
  what must happen before the first upload (who does each item), the site is not blocking.
- 2026-09-24, branch `rename`: the folder is now `E:\ValheimModdingWorkspace\Xaivous_BuildBeacon` and the GitHub repo
  `Xaivous_BuildBeacon` (on the author's personal account then; now `xaivous/BuildBeacon`). The `vs_` prefix (from an old thread's "valheimSuite") is now `xai_` everywhere
  (the user: `xai_` for code, "Xaivous" for people): piece prefab names (`xai_build_beacon`,
  `xai_beacon_bossholder_{pillar,wall}`, `xai_beacon_mobrack_{column,wall}`), saved-data keys (`xai_beacon_trophies`,
  `xai_holder_trophy`, `xai_rack_slot_N`, `xai_paidcost`, `Player.m_customData["xai_beacon_savings"]`, tutorial
  `xai_buildbeacon`), RPC names, localization keys (`$xai_...`, `$piece_xai_...`), bundle asset names, Unity assets
  (renamed with AssetDatabase.RenameAsset, GUIDs kept), the generator scripts (`tools/blender/xai_*.py`, text blocks in
  totem.blend renamed) and their object names. Plugin GUID `com.xaivous.buildbeacon`, so the config file is
  `BepInEx/config/com.xaivous.buildbeacon.cfg` (the Dev profile's old cfg was copied to it). Clean break, by the
  user's choice: pieces placed with the vs_ names do not load, savings reset, Hugin shows again. Regenerating with new
  object names changed the FBX meshes' internal IDs: the holders' and racks' `model` MeshFilters lost their mesh and
  were repointed; the beacon's nested FBX instances survived. `ValheimSuite` is left in the devlog where it names the
  other thread's project. totem.blend must be saved by the user for the renamed text blocks and objects to reach git.

## Decisions

None yet.

## New gotchas

- **`WearNTear.m_supports = false` makes a piece un-buildable-on**: any ghost whose placement ray hits it goes red
  (Invalid), not just support-requiring ones. Separate from `m_noSupportWear` (which, on 1.0, makes the piece itself
  need support).
- **PowerShell 5 `Get-Content`/`Set-Content` garble UTF-8**: files without a BOM are read as ANSI and written back
  with a BOM, so non-ASCII characters (·, †, ö) are mangled. Use `[IO.File]::ReadAllText`/`WriteAllText`, the Edit tool,
  or Python for scripted edits.
