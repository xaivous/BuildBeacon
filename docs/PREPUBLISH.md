# Before publishing

The checklist to work through before the first Thunderstore upload. Details and reasons are in
`docs/plans/publish.md` (the item numbers match its tables); tick items off here as they land. "You" means the repo
owner; "Claude" items are changes in the repository. The release flow (`docs/releasing.md`) enforces the ones it can:
`uv run --no-project python tools/release/release.py check --release --online` lists what still blocks a publish.

## Repository (plan Stage A)

Decided 2026-09-24: MIT licence (done), the author email stays in the history, the internal AI-workflow docs stay.

- [x] **A1 Licence**: `LICENSE` is MIT, © 2026 xaivous (was the Jötunn template's MIT-0).
- [ ] **Move the repo to `xaivous/BuildBeacon`** (you, by publish time: transfer it to the `xaivous` account or
      organisation and rename it; GitHub redirects the old URLs). Then `git remote set-url origin
      git@github.com:xaivous/BuildBeacon.git`. The site's GitHub button already points there; the Pages URL becomes
      `https://xaivous.github.io/BuildBeacon/` (use it for the manifest's `website_url`, C3).
- [x] **A4 Rename the GitHub repo**: now `xaivous/BuildBeacon` (the local clone's `origin` points there; the folder is
      `Xaivous_BuildBeacon`). The `vs_` prefix became `xai_` in code and assets, the plugin GUID
      `com.xaivous.buildbeacon` (2026-09-24, branch `rename`); from 0.2.0 `xaivous.buildbeacon`, with the old config
      file carried over.
- [ ] **A5 Public README** (Claude): the root `README.md` is now the player README (moved from `BuildBeacon/`, and
      still the Thunderstore page); the developer guide is `docs/development.md`. Still to add for visitors: links to
      Thunderstore and the site, a pointer to `docs/development.md` for building, licence, credits (Jötunn template,
      `pdb2mdb.exe`).
- [ ] **Go public** (you): Settings > General > Danger Zone > Change visibility, after A4 and A5.

## Thunderstore package (plan Stage C)

- [x] **C1 Icon**: a real 256×256 `BuildBeacon/Package/icon.png`: the user's art (2026-09-24), kept full size as
      `art/icon.webp` (1254×1254) and scaled down with Lanczos.
- [ ] **C2 Description** in `Package/manifest.json` (Claude): ≤ 250 characters, covering creature levels, boss
      holders and racks (today's still says "Boss-trophy-powered").
- [x] **C3 `website_url`**: `https://xaivous.github.io/BuildBeacon/`, the Pages site (2026-09-24).
- [x] **C4 DevMode off by default**: `BeaconConfig.cs` (2026-09-24); the release check refuses `true`.
- [ ] **C5 Quiet the diagnostics** (Claude): gate `DiagnosticPatches`' beacon ApplyDamage/Destroy/Remove warnings
      (full stack traces for every player) behind DevMode.
- [x] **C6 Changelog**: `BuildBeacon/CHANGELOG.md` has the 0.1.0 entry; the release zip takes it from there.
- [x] **C7 Dependency versions** (2026-09-24): `BepInExPack_Valheim-5.4.2351` and `Jotunn-2.30.2`, both the latest;
      the build compiles against the Dev profile's BepInEx (`BEPINEX_PATH`). Recheck at publish time: `check --online`
      says when a newer one is out. Retest in game with the Dev profile updated (it had Jötunn 2.30.1).
- [ ] **C8 Player README** (Claude): screenshots (absolute URLs), multiplayer note (server and every client need the
      mod), known limitations (wall pieces snap by their centre only), link to the site's matrix.
- [ ] **C9 Version** (you decide, Claude applies): 0.1.0 or 1.0.0; `release.py bump 1.0.0` sets `PluginVersion`,
      `version_number` and the CHANGELOG heading together.
- [ ] **C10 Name and team** (you): check "BuildBeacon" is free on Thunderstore; create or choose the team, put its
      name in `thunderstore.toml` (`package.namespace`: `xaivous`), and make a service account token for it
      (`docs/releasing.md`).
- [ ] **C11 Test the zip** (together): `release.py package` builds and verifies `dist/BuildBeacon-<version>.zip`
      (`manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md`, `DEFAULT_DISCOUNTS.md`, `plugins/BuildBeacon.dll`);
      import it into a clean r2modman profile, play a local world, then a dedicated server with a client.
- [ ] **C12 Open in-game checks** (you): `docs/open-items.md` items 1–3 (the wall snapping revisit, the
      trophy gap, the two-client slot ownership check), finished or accepted as known.

## Later, not blocking the upload

- The GitHub Pages site with the README and the player matrix tool (plan Stage B): built (2026-09-24); goes live once
  the repo is public and Settings > Pages > Source is "GitHub Actions". The Thunderstore page can link the
  repository until it exists.
