# Repository tools

Scripts under `tools/` that are run by hand. The pipelines (build, deploy, bundle, log reading) are skills under
`.claude/skills/` and are not listed here.

## Trophy balance matrix

`tools/balance/gen_matrix.py` writes `tools/balance/balance_matrix.html`: every trophy in the game, grouped by biome,
against every material the beacon discounts. Each cell is the discount level a creature trophy gives that material, or
Free where a boss trophy frees it. A row per trophy, a column per material, and a bottom row with the levels on offer
for each material.

Run it from the repository root after changing the default rules:

```bash
uv run --no-project python tools/balance/gen_matrix.py
```

- The rules come from `DefaultBossRules` and `DefaultMobRules` in `BuildBeacon/BeaconConfig.cs`, so the page always
  shows the shipped defaults, not a server's or the Dev profile's rules files.
- The trophy list, the biome of each trophy and the notes are kept by hand in `tools/balance/matrix_data.py`
  (`B`), shared with the site's matrix tool. A trophy marked † has
  its biome placed from the game's names and descriptions rather than spawn data.
- The script stops with an error if a rule names a trophy that is not in `B`; add it there first.
- Open the page straight from disk in a browser. It is plain ASCII, so no encoding setting is needed.

### Balance targets

The curve is three creature levels (`LEVEL_PCT`, 50/80/90%: 2x, 5x, 10x) and a boss above them (`BOSS_PCT`, the
`BossPercent` default: 100, free; 95 on servers that keep some gathering). The script checks the rules against these
targets, prints any miss as an "off target" line, and shows them in the page's footer (a dashed outline marks a miss):

- Every creature rule gives exactly one level, and no material may pass level 3: at most three trophies each.
- Construction woods and stones (Wood, Fine Wood, Core Wood, Stone, Yggdrasil Wood, Black Marble, Ashwood, Grausten,
  Timberwood) reach level 2 from the common creature trophies of their biome, and level 3 with a rare one.
- Powerful materials (`POWERFUL`: metals, cores, Crystal, Dvergr parts, Charred Cogwheel) stop at level 2
  (`POWERFUL_CAP`), rare trophies included.
- Materials only a rare creature drops (Bear Hide and Paw, Scale Hide, Morgen Sinew, Celestial Feather, Writhan Roots)
  reach level 1 from that creature's trophy.
- Every other creature-discounted material reaches level 1 from common trophies, with a second or third trophy where
  more creatures drop it or its biome is known for it.

"Rare" trophies (mini-bosses, Hildir's chest bosses, rare spawns, single-cave creatures) are listed in `RARE`, and the
targets in `CONSTRUCTION`, `POWERFUL` and `RARE_DROPS`, in `matrix_data.py`. To rebalance, edit `DefaultMobRules` in
`BeaconConfig.cs`, rerun both scripts, and check the output for "off target" lines.

## Defaults list for Thunderstore

`tools/balance/gen_defaults.py` writes `BuildBeacon/Package/DEFAULT_DISCOUNTS.md`, the list of every default rule
that ships in the mod's zip. Rerun it with the matrix whenever the default rules change:

```bash
uv run --no-project python tools/balance/gen_defaults.py
```

It stops with an error if a rule names a trophy missing from its display-name table (`MOB`); add it there.

## GitHub Pages site

`tools/site/build_site.py` builds the public site into `site/_build/` (gitignored): one page with two tabs, "The Mod"
(the root `README.md`, also the Thunderstore page, rendered with Python-Markdown) and "Discount Matrix" (a player tool:
the matrix drawn in the browser, a "Your trophies" total that adds levels the way a beacon does, a panel that reads a
server's pasted rules files, and a balance view with the targets above). Sources: `site/index.html` (template),
`site/site.css`, `site/matrix.js`; the matrix data comes from `matrix_data.py` and the default rules in `BeaconConfig.cs`.

```bash
uv run --no-project --with markdown python tools/site/build_site.py
```

- Preview: the `site` configuration in `.claude/launch.json` serves `site/_build` on http://localhost:8765.
- `.github/workflows/pages.yml` runs the same build on every push to `master` that touches the site, the README, the
  manifest or the default rules, and publishes it (Settings > Pages > Source must be "GitHub Actions" once).
- `matrix.js` parses rules the way `RulesFile`/`DiscountRules` do (`#` comments, `|` separated, names matched ignoring
  case, spaces and underscores, `*` for every material, a boss trophy's creature rules ignored). Keep it in step if the
  mod's rules format changes.
- The build widens the README's 3-space list indents to 4 and adds a blank line before list items that follow a
  paragraph line, which Python-Markdown needs and GitHub/Thunderstore do not; the README itself stays as it is.
- `THUNDERSTORE_URL` in the build script adds a Thunderstore button to the header once the package exists.
