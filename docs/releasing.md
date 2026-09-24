# Releasing BuildBeacon

One script runs the whole flow: `tools/release/release.py` (Python 3.11+, standard library only). It checks the
version, builds the Release DLL, packages the Thunderstore zip and uploads it. GitHub Actions runs the same checks on
every push (`.github/workflows/release-checks.yml`). In Claude Code, the `release` skill runs the same commands with
the same flags: `/release check --online`, `/release bump patch`, `/release upload`.

| Command | Does |
|---|---|
| `check [--release] [--online] [--tag v1.2.3]` | The version and package checks |
| `bump major\|minor\|patch\|1.2.3 [--force]` | A new version in all three places |
| `package [--release]` | Release build and the verified zip, for testing |
| `publish` | The rehearsal: every check and the zip `upload` would send, without sending it |
| `upload` | `publish`, then a typed confirmation, the upload to Thunderstore and the `v<version>` tag |

## Why publishing runs on a developer machine

The mod compiles against Valheim's own assemblies and BepInEx, which Jötunn's build step reads from the local game
install (`VALHEIM_INSTALL` in `BuildBeacon/Environment.props`). They cannot be committed to a public repository, so a
GitHub runner cannot build the DLL. CI checks everything that needs no game files; the build and the upload run where
the game is installed.

## One-time setup

1. **Thunderstore team.** Create or join the team that will own the package on thunderstore.io, and put its name in
   `thunderstore.toml` (`package.namespace`, now `xaivous`). The package name comes from the manifest (`BuildBeacon`).
2. **API token.** On thunderstore.io: Settings > Teams > your team > Service Accounts > Add service account. Copy the
   token it shows (it is shown once). Keep it out of the repository. `upload` reads it from the `TCLI_AUTH_TOKEN`
   environment variable, else from the file `TCLI_AUTH_TOKEN_FILE` names, else from
   `~/.config/thunderstore/buildbeacon-token` (`C:\Users\<you>\.config\thunderstore\buildbeacon-token`). The file is
   the way to go for the Claude skill, whose shell does not see a variable set in your own terminal:
   ```powershell
   New-Item -ItemType Directory -Force "$HOME\.config\thunderstore" | Out-Null
   Set-Content -NoNewline -Encoding ascii "$HOME\.config\thunderstore\buildbeacon-token" "tss_..."
   ```
3. **Tools.** The .NET SDK builds the mod; the Thunderstore CLI is pinned in `dotnet-tools.json` and installed with
   `dotnet tool restore` (the script runs it before uploading). `uv` runs the script.

## Where the version lives

Three places, which must agree; `check` fails otherwise:

| Place | What reads it |
|---|---|
| `PluginVersion` in `BuildBeacon/BuildBeaconPlugin.cs` | BepInEx, and Jötunn's network check (players and server must match on major.minor) |
| `version_number` in `BuildBeacon/Package/manifest.json` | Thunderstore and mod managers |
| The first `## x.y.z` heading of `BuildBeacon/CHANGELOG.md` | Thunderstore's Changelog tab, the site's Changelog tab |

A release tag is `v` plus the version (`v0.1.0`); `upload` creates it.

## Releasing a version

```bash
uv run --no-project python tools/release/release.py bump minor        # or patch, major, or 1.2.3
```

`bump` sets the version in all three places and adds a `## x.y.z` changelog entry with a placeholder line. Write the
entry, then check, commit and push:

```bash
uv run --no-project python tools/release/release.py check --release --online
git commit -am "Release 0.2.0"
git push
```

Then rehearse, and upload:

```bash
uv run --no-project python tools/release/release.py publish
uv run --no-project python tools/release/release.py upload
git push origin v0.2.0
```

`publish` stops before building if anything is wrong, then builds, packages and verifies the zip, and says what
`upload` would run. `upload` does the same, then always asks you to type the version to confirm (Thunderstore versions
cannot be deleted, only deprecated; there is no flag to skip it), uploads with `tcli publish`, and tags the commit.
Pushing the tag runs the release tag checks in CI.

## What each command checks

`check` (also on every push and pull request in CI):
- the version is `Major.Minor.Patch` and the same in the three places; the changelog entry has text;
- the manifest follows Thunderstore's rules: name `A-Z a-z 0-9 _` (128 at most) and equal to `PluginName`,
  description 1 to 250 characters, `website_url` present, dependencies `Team-Package-x.y.z`, including BepInExPack and
  Jötunn at the JotunnLib version the csproj builds against;
- `icon.png` is a 256×256 PNG; `README.md` and `CHANGELOG.md` are UTF-8 and not empty;
- `thunderstore.toml` names a valid team, the same package name, and the `valheim` community.

`check --release` (and `publish`, `upload`) also refuses: `DevMode` defaulting to true, an empty `website_url`, a changelog entry
still holding the placeholder. `--tag v1.2.3` checks a tag against the version.

`check --online` (and `publish`, `upload`) also asks Thunderstore: every dependency version exists (and says when a
newer one is out), and the published version is not ahead of this one. `publish` and `upload` also need this version
to be unpublished.

`publish` and `upload` also check git: the working tree is clean, the branch is `main` and matches `origin/main`, and
the tag is free locally and on the remote. `upload` needs the token; `publish` only says whether it found one.

`package` builds `dist/BuildBeacon-<version>.zip` (git ignores `dist/`) without publishing: for a local test in a
clean r2modman profile (PREPUBLISH C11). Its layout:

```
manifest.json  icon.png  README.md  CHANGELOG.md  DEFAULT_DISCOUNTS.md  plugins/BuildBeacon.dll
```

The zip is reproducible: fixed timestamps and order, and the build is deterministic, so the same commit gives the
same bytes (the script prints the SHA-256).

## Files

| File | Role |
|---|---|
| `tools/release/release.py` | The flow |
| `.claude/skills/release/SKILL.md` | The `release` skill: the same commands from Claude Code |
| `thunderstore.toml` | Team, community and categories for `tcli`; everything else comes from the manifest |
| `dotnet-tools.json` | Pins the Thunderstore CLI (`tcli` 0.2.4) |
| `.github/workflows/release-checks.yml` | The checks in CI |
| `BuildBeacon/Package/` | `manifest.json`, `icon.png`, `DEFAULT_DISCOUNTS.md` (generated by `tools/balance/gen_defaults.py`) |
