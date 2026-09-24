# Before publishing

The checklist to work through before the first Thunderstore upload. Details and reasons are in
`docs/plans/publish.md` (the item numbers match its tables); tick items off here as they land. "You" means the repo
owner; "Claude" items are changes in the repository.

## Repository (plan Stage A)

Decided 2026-09-24: MIT licence (done), the author email stays in the history, the internal AI-workflow docs stay.

- [x] **A1 Licence**: `LICENSE` is MIT, © 2026 xaivous (was the Jötunn template's MIT-0).
- [ ] **Move the repo to `xaivous/BuildBeacon`** (you, by publish time: transfer it to the `xaivous` account or
      organisation and rename it; GitHub redirects the old URLs). Then `git remote set-url origin
      git@github.com:xaivous/BuildBeacon.git`. The site's GitHub button already points there; the Pages URL becomes
      `https://xaivous.github.io/BuildBeacon/` (use it for the manifest's `website_url`, C3).
- [x] **A4 Rename the GitHub repo**: now `xaivous/BuildBeacon` (the local clone's `origin` points there; the folder is
      `Xaivous_BuildBeacon`). The `vs_` prefix became `xai_` in code and assets, the plugin GUID
      `com.xaivous.buildbeacon` (2026-09-24, branch `rename`).
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
- [ ] **C3 `website_url`** in the manifest (Claude): the Pages site, or the repository until the site exists.
- [ ] **C4 DevMode off by default** (Claude): `BeaconConfig.cs`, the `TODO: BEFORE PUBLISH` line.
- [ ] **C5 Quiet the diagnostics** (Claude): gate `DiagnosticPatches`' beacon ApplyDamage/Destroy/Remove warnings
      (full stack traces for every player) behind DevMode.
- [ ] **C6 `Package/CHANGELOG.md`** (Claude): a first entry.
- [ ] **C7 Dependency versions** (Claude, at publish time): current `BepInExPack_Valheim` and `Jotunn` on
      Thunderstore; bump `JotunnLib` in the csproj and the manifest together, rebuild, retest.
- [ ] **C8 Player README** (Claude): screenshots (absolute URLs), multiplayer note (server and every client need the
      mod), known limitations (wall pieces snap by their centre only), link to the site's matrix.
- [ ] **C9 Version** (you decide, Claude applies): 0.1.0 or 1.0.0; `PluginVersion` and `version_number` together.
- [ ] **C10 Name and team** (you): check "BuildBeacon" is free on Thunderstore; create or choose the team.
- [ ] **C11 Test the zip** (together): Release build, check the zip's root (`manifest.json`, `icon.png`, `README.md`,
      `CHANGELOG.md`, `plugins/BuildBeacon.dll`), import it into a clean r2modman profile, play a local world, then a
      dedicated server with a client.
- [ ] **C12 Open in-game checks** (you): `docs/open-items.md` items 1–3 (the wall snapping revisit, the
      trophy gap, the two-client slot ownership check), finished or accepted as known.

## Later, not blocking the upload

- The GitHub Pages site with the README and the player matrix tool (plan Stage B): built (2026-09-24); goes live once
  the repo is public and Settings > Pages > Source is "GitHub Actions". The Thunderstore page can link the
  repository until it exists.
