---
name: valheim-debug
description: Diagnose BuildBeacon behaviour against the running game: read the BepInEx log for the last session, recognise the healthy startup signature and the known failure lines, use the mod's in-game console commands (beacon_dump, beacon_materials), and check a game method's real Valheim 1.0 signature or callers before writing a Harmony patch. Use this whenever something "doesn't work in game", a piece breaks, a cost is wrong, a patch might not apply, the user pastes a log, or a new Harmony patch or vanilla material lookup is being written.
---

# Debugging the mod against Valheim

## Read the log first

The Dev profile writes `%APPDATA%\r2modmanPlus-local\Valheim\profiles\Dev\BepInEx\LogOutput.log`, overwritten each
launch. The bundled script prints only the current session's relevant lines:

```bash
bash .claude/skills/valheim-debug/scripts/log-tail.sh
```

It finds the last `Loading [BuildBeacon` line and shows BuildBeacon output, Harmony and Unity errors, exceptions and
placement lines after it. Read the whole thing before forming a theory; the cause is usually stated outright.

### Healthy startup looks like

```
Loading [BuildBeacon 0.1.0]
Loaded 8 boss trophies (59 free materials), 59 mob rules
BuildBeacon 0.1.0 loaded
Vanilla material for "blackmarble": "..."          (one per StoneTemplates token)
Beacon material "BeaconStoneDark": Standard -> Custom/Piece with textures from "..."
Beacon material "BeaconRune": kept Standard (emissive)
```

### Known failure lines and what they mean

| Line | Meaning | Fix |
|---|---|---|
| `Failed to patch ... Cannot get result from void method` (HarmonyX) | A patch signature does not match 1.0. `PatchAll` aborts, so **every** patch is missing, not just this one. | Check the real signature with sigdump (below) and fix the patch. |
| `AssetBundle buildbeacon not found in assembly manifest` | The bundle is not embedded in the DLL. | Restore the `EmbeddedResource` line in the csproj; see `build-deploy`. |
| `Falling back to a guard_stone clone` | Bundle or prefab missing; the beacon is a ward clone. | Same as above, or the prefab name in the bundle changed. |
| `Cannot instantiate objects with a parent which is persistent` | Something tried to parent a new object under a bundle asset. | Clone the asset into a scene object first (the plugin already does). |
| `Failed to clone prefab, name already exists` | Jötunn clone helper refused a name already used by a loaded asset. | The bundle asset must not share the piece name. |
| `Ambiguous asset name for path ... using old path` | Jötunn scanning vanilla Deep North rocks. | Noise; ignore. |
| `Missing audio clip in music respawn` | Vanilla. | Ignore. |

### Silent failures

WearNTear does not log wear damage. A piece that breaks by itself with nothing in the log is a WearNTear settings
problem on the prefab (health, support, roof, biome, event fields). Compare with `beacon_dump guard_stone`.

Cost patches not applying but no error: confirm the patch classes are actually reached; the `UpdatePlacement`
wrapper plus depth-counted `RequirementSwap` in `Patches/RequirementPatches.cs` is the shape 1.0 needs.

## In-game console commands (F5)

- `beacon_dump [prefab]` lists WearNTear and Piece settings, then every renderer, material, shader (with its declared
  keywords), texture and property. Defaults to the beacon. Run it on vanilla pieces (`guard_stone`, `stone_wall_2x1`)
  for reference values. Materials show `(Instance)` after the hammer has hovered the piece; that is vanilla.
- `beacon_materials <text>` lists loaded vanilla materials whose name contains the text, with shader and main texture.
  Use it to pick names for the `MaterialTemplates` table in the plugin or for `JVLmock_` materials in Unity.
- `beacon_prefabs <text>` lists registered prefabs whose name contains the text, with each one's materials and
  shaders. Use it to confirm a piece id and see which material it carries before using `prefab:<name>` in the table.
- `beacon_tree [prefab]` prints a prefab's child hierarchy with component types, lights (type, colour, range) and
  local positions. Use it to find the effect or light object to borrow from a vanilla piece.

Both also write to the BepInEx log. Ask the user to paste the relevant block, or read the log file directly.

## Checking game code before patching

Valheim 1.0 changed signatures and call flow (`PlacePiece` is void, `ConsumeResources` moved into `UpdatePlacement`,
`Hoverable` gained `GetHoverOffset`). Never assume a signature from memory or from older mod code; the compiler does not
catch Harmony target mismatches because `nameof` only checks the member exists.

`tools/sigdump` reads the game assembly's metadata without loading it, which avoids the reflection-only loader failures
that Valheim's default-interface-method types cause in PowerShell.

```bash
cd tools/sigdump && dotnet build -nologo -v:q
DLL="E:/Steam/steamapps/common/Valheim/valheim_Data/Managed/assembly_valheim.dll"
dotnet bin/Debug/net10.0/sigdump.dll "$DLL" Player 'HaveRequirements|PlacePiece|TryPlacePiece'   # signatures
dotnet bin/Debug/net10.0/sigdump.dll "$DLL" Player '^UpdatePlacement$' --calls                    # what it calls
dotnet bin/Debug/net10.0/sigdump.dll "$DLL" '*' '.' --calls > /tmp/all.txt                         # everything, for grepping callers
awk '/^[^ ]/{m=$0} /-> Player::ConsumeResources$/{print m}' /tmp/all.txt                            # who calls X
```

Arguments: DLL path, comma-separated type names (nested as `Outer+Inner`, `*` for all), a regex on method names, and
`--calls` to list called methods and touched fields from the IL, and `--il` to disassemble the method with resolved
member names and branch targets when the order of calls is not enough to see the logic. Adjust the Steam path from `BuildBeacon/Environment.props`.

For enum values or field lists, a reflection-only load in PowerShell works for most types but throws on `Player`;
sigdump with a `.ctor` regex lists a type's existence and prefab names can be checked against
`valheim_Data/StreamingAssets/SoftRef/manifest_extended` with `grep -a -o`.

## Unity editor console

When the Unity MCP tools are connected, `Unity_GetConsoleLogs` reads the editor console. Otherwise read
`%LOCALAPPDATA%\Unity\Editor\Editor.log` and filter from the last `Loading project` line. Import errors about
`buildbeacon.dll` version mismatches are transient, caused by a mod build landing while Unity was importing.

### WearNTear field names that mean the opposite of what they say

`m_noSupportWear = true` means the piece **requires structural support** and takes 100% damage on the first wear tick
without it (`UpdateWear`: `if (m_noSupportWear) { UpdateSupport(); if (!HaveSupport()) damage = 100; }`). The ward
has it `false` and never collapses; stone walls have it `true`. A Stone-material piece with it `true` dies when
placed on wood. For a utility piece like the beacon, set `m_noSupportWear = false` and `m_supports = false`.
Diagnosed with the `DiagnosticPatches` log lines (`[diag] beacon ApplyDamage 1000 ... no HitData`, with the
`VerboseLogging` setting on) and
`tools/sigdump ... WearNTear '^UpdateWear$' --il`, which prints a method's IL with resolved names and branch targets.
