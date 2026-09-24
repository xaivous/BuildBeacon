---
name: build-deploy
description: Build the BuildBeacon Valheim mod and get it into the r2modman Dev profile for testing, then verify the deploy actually happened. Use this whenever C# under BuildBeacon/ changes, whenever the user says build, rebuild, redeploy, "try it in game", or reports that an in-game change isn't showing up, and after any Unity bundle rebuild. Also use it (with tools/release/release.py) to check the version and package or publish a Release for Thunderstore.
---

# Build and deploy the mod

The solution is `BuildBeacon.sln` at the repository root. A Debug build compiles `BuildBeacon/BuildBeacon.csproj`, runs the
Jötunn prebuild (publicized game assemblies), then the post-build step in the csproj copies the DLL into the Valheim
install's Unity folder and runs `scripts/publish.ps1`, which copies `BuildBeacon.dll`, `.pdb` and `.mdb` into the
r2modman Dev profile: `%APPDATA%\r2modmanPlus-local\Valheim\profiles\Dev\BepInEx\plugins\BuildBeacon\`. The paths come
from `BuildBeacon/Environment.props` (gitignored, per machine).

## The one command

Run the bundled script rather than a bare `dotnet build`; it does the checks that have bitten us before:

```bash
bash .claude/skills/build-deploy/scripts/build-and-verify.sh
```

It builds, prints only errors and the result line, then reports:

- whether the deployed DLL's timestamp matches the freshly built one,
- whether `valheim.exe` is running, which is the usual reason the two differ,
- whether the asset bundle is embedded in the DLL (the `buildbeacon` manifest resource).

Treat "deployed timestamp is older than the build" as a failed deploy even though MSBuild says "Build succeeded". The
publish script's copy fails with an IOException when the game holds the DLL open, and MSBuild does not turn that into
an error. Tell the user to close Valheim and rerun; there is no way around the file lock.

## Things that have gone wrong, and what they look like

**Bundle missing from the DLL.** The build reports success, the DLL is about 84 KB instead of about 215 KB, and in game
the log says `AssetBundle buildbeacon not found in assembly manifest` followed by a fallback to the ward clone. Cause:
the `<EmbeddedResource Include="Assets/buildbeacon" LogicalName="buildbeacon" />` entry vanished from the csproj. Rider
has reverted the csproj to HEAD at least once when files were open in it. Check with
`grep EmbeddedResource BuildBeacon/BuildBeacon.csproj` and restore the line inside its own `<ItemGroup>`.

**Edits silently reverted.** Same root cause. If a change you made earlier in the session is not on disk, check
`git diff` before assuming the code is wrong, and warn the user that uncommitted work is exposed until committed.

**HarmonyX "Failed to patch" in the game log.** One bad patch aborts the whole `PatchAll`, so every Harmony patch in
the mod is missing, not just the failing one. See the `valheim-debug` skill for how to read the log and how to check a
method's real 1.0 signature before patching it.

**Stale DLL in the Unity project.** The post-build copy also refreshes `BuildBeaconUnity/Assets/Assemblies/BuildBeacon.dll`.
If Unity is focused during the build it may log "Build asset version error"; that is transient and clears on the next
refresh.

## Release packaging and publishing

Use the release flow, not a bare Release build; it is documented in `docs/releasing.md`:

```bash
uv run --no-project python tools/release/release.py check      # version agrees in all three places, package rules
uv run --no-project python tools/release/release.py bump patch # new version: PluginVersion, manifest, CHANGELOG
uv run --no-project python tools/release/release.py package    # Release build + dist/BuildBeacon-<version>.zip
uv run --no-project python tools/release/release.py publish    # the rehearsal: every check and the zip, no upload
```

`package` builds the zip from the sources (manifest and icon from `BuildBeacon/Package/`, the root `README.md`,
`BuildBeacon/CHANGELOG.md`, `DEFAULT_DISCOUNTS.md`, the Release DLL under `plugins/`) and verifies it. A plain
`dotnet build -c Release` only compiles now; the publish script no longer zips. The bundle is inside the DLL, so
nothing else needs to ship. The version must match in `PluginVersion`, the manifest's `version_number` and the top
CHANGELOG heading (`bump` moves all three); the Jötunn compatibility check uses minor-version strictness, so server
and clients must match on `major.minor`. `upload` sends it with the Thunderstore CLI (it
needs the token); the `release` skill runs these commands and owns the upload confirmation.

## After deploying

Launch the game and confirm the healthy startup signature in the BepInEx log (see `valheim-debug`). The lines to expect
are `Loading [BuildBeacon x.y.z]`, `Loaded N boss trophies`, one `Beacon material ...` line per material, and no
`HarmonyX` errors or `Falling back to a guard_stone clone` warnings.
