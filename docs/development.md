# Developing BuildBeacon

How to build and work on the mod. The player-facing README is the repository's `README.md`. The project started
from the [Jötunn](https://github.com/Valheim-Modding/Jotunn) mod stub, whose build tools and Unity project stub it keeps.

#  Setup Guide

Please see [Jötunn Docs](https://valheim-modding.github.io/Jotunn/guides/overview.html) detailed documentation and setup.

### Post Build automations

Included in this repo is a PowerShell script `publish.ps1`.
The script is referenced in the project file as a post-build event.
Depending on the chosen configuration in Visual Studio the script executes the following actions.

### Building Debug

The compiled dll and a dll.mdb debug file are copied to `<ValheimDir>\BepInEx\plugins` (or the path set in MOD_DEPLOYPATH).

### Building Release

A Release build only compiles. The Thunderstore zip is built, checked and published by
`tools/release/release.py`; see [Releasing BuildBeacon](releasing.md) for the version check, packaging and publishing.

## Developing Assets with Unity

New Assets can be created with Unity and imported into Valheim using the mod.
A Unity project is included in this repository under `<JotunnModStub>\JotunnModUnity`.

### Unity Editor Setup

1. [Download](https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup.exe) UnityHub directly from Unity or install it with the Visual Studio Installer via `Individual Components` -> `Visual Studio Tools for Unity`
2. You will need an Unity account to register your PC and get a free licence. Create the account, login with it in Unity Hub and get your licence via `Settings` -> `Licence Management`
3. Install Unity Editor version 2022.3.17f
4. Compile the project. This copies all assemblies into `<JotunnModStub>\JotunnModUnity\Assets\Assemblies`. Don't open Unity yet before this step, it will remove assembly references.
5. **Warning:** These assembly files are copyrighted material and you can theoretically get into trouble when you distribute them in your github repository. To avoid that there is a .gitignore file in the Unity project folder. Keep that when you clone or copy this repository
6. Open Unity Hub and add the JotunnModUnity project
7. Open the project in Unity
8. Install the `AssetBundle Browser` package in the Unity Editor via `Window`-> `Package Manager` for easy bundle creation

## Debugging

See the Wiki page [Debugging Plugins via IDE](https://github.com/Valheim-Modding/Wiki/wiki/Debugging-Plugins-via-IDE) for more information

## Actions after a game update

When Valheim updates it is likely that parts of the assembly files change.
If this is the case, the references to the assembly files must be renewed in Visual Studio and Unity.

### Prebuild actions

1. There is a file called DoPrebuild.props included in the solution. When you set its only value to true, Jötunn will automatically generate publicized assemblies for you. Otherwise you have to do this step manually.

### Unity actions

1. Copy all `assembly_*.dll` from `<ValheimDir>\valheim_Data\Managed` into `<JotunnModStub>\JotunnModUnity\Assets\Assemblies`. <br />
  **Do this directly in the filesystem - don't import the dlls in Unity**.
2. Go to Unity Editor and press `Ctrl+R`. This reloads all files from the filesystem and "re-imports" the copied dlls into the project.

## Unity project notes

### Game DLLs are imported with Auto Reference turned off

Every DLL in `BuildBeaconUnity/Assets/Assemblies` has "Auto Reference" disabled in its `.meta`
(`isExplicitlyReferenced: 1`). Do not turn it back on.

Why: several game assemblies define types in the global namespace whose names collide with
framework types that Unity packages use. `assembly_utils.dll` has a field-only `DisplayNameAttribute`
that shadows `System.ComponentModel.DisplayNameAttribute`, and `assembly_valheim.dll` has a static
class `Version` that shadows `System.Version`. C# resolves names in enclosing namespaces (including
the global one) before it looks at `using` directives, so with Auto Reference on, the Timeline and
Input System packages fail to compile with errors such as:

```
error CS0592: Attribute 'DisplayName' is not valid on this declaration type. It is only valid on 'field' declarations.
error CS0144: Cannot create an instance of the abstract type or interface 'Version'
```

Consequences:

- Prefabs and asset bundles are unaffected. Component references on prefabs resolve through the
  plugin importer, not through script compilation, so bundles containing Valheim components still build.
- Any C# script you add under `Assets` cannot see game types (ZInput, Piece, Vector2i, ...) unless it
  lives in an Assembly Definition that references the needed DLLs explicitly.
- When a new DLL lands in the folder, Unity generates a `.meta` with Auto Reference on. If the game
  adds another global-namespace collision, uncheck Auto Reference on that DLL in the Plugin Inspector
  and commit the `.meta`.
- The post-build copy step overwrites only the DLLs, never the `.meta` files, so the setting survives rebuilds.

### Game and mod DLLs are imported with Validate References turned off

Every DLL `.meta` in `BuildBeaconUnity/Assets/Assemblies` also has `validateReferences: 0` ("Validate References"
unchecked in the Plugin Inspector). Jotunn.dll references YamlDotNet, which neither the Jötunn Thunderstore
package nor its NuGet package ships. In game that is harmless because Mono loads referenced assemblies lazily and
the YAML code path is never hit, but with validation on Unity refuses to load Jotunn.dll ("Unable to resolve
reference 'YamlDotNet'") and then BuildBeacon.dll ("Reference has errors 'Jotunn'"). Do not copy a
YamlDotNet.dll from another mod profile to work around it; turn validation off on the `.meta` instead, and do the
same for any new DLL that Unity imports with validation on.

### Which DLLs are copied, and Mono.Cecil is player-only

Two import warnings shaped the copy list in `BuildBeacon.csproj` (`CopyToUnity`):

- `Duplicate assembly 'Mono.Cecil.dll' with different versions detected` on every domain reload. Unity ships its own
  Cecil (`com.unity.nuget.mono-cecil`, editor-only). BepInEx's copy is still needed for player builds, which is what
  a bundle build is: without it the player type scan warns that `0Harmony.dll` cannot resolve `Mono.Cecil`. So the
  copy stays, and its `.meta` enables it for every platform except the Editor ("Any Platform" with "Exclude Editor").
- `Failed to resolve assembly: 'MagicaClothV2'` during bundle builds, from a `VisEquipment` field in
  `assembly_valheim.dll`. The copy now includes `MagicaClothV2.dll` and what it needs: `Unity.Burst`,
  `Unity.Burst.Unsafe`, `Unity.Collections` and `Unity.Collections.LowLevel.ILSupport`. The game's
  `Unity.Mathematics.dll` is deliberately not copied; the project has `com.unity.mathematics`, and a second copy
  would warn like Cecil did.

Those five DLLs' `.meta` files were written before Unity first imported them, with Auto Reference and Validate
References off like the rest. To add another DLL the same way, copy an existing `.meta` next to it with a new GUID
before the build copies the DLL in.

### Matching the beacon's look to vanilla

Vanilla puts glowing parts on the Standard shader with the `_EMISSION` keyword (the ward's runes sit at
emission intensity ~2) and everything else on `Custom/Piece`. At registration the plugin mirrors that:
Each Unity material is dressed in a vanilla one per the `MaterialTemplates` table at the top of
BuildBeaconPlugin.cs. Stone entries move to `Custom/Piece`, copy the vanilla material (textures, normal map, grime
noise, wear flags), keep the authored albedo as tint and project triplanarly so the result does not depend on the
model's UVs; vertex noise, ripple and parallax are disabled because they deform this low-poly mesh. Glow entries
(crystal, runes) copy the vanilla material when its shader can emit, otherwise borrow only its textures onto the
authored Standard material, and in both cases the authored emission colour is what glows. Keep emission intensity
around 2 and the colour saturated; brighter or pastel emission blooms to white in game. A token may also be
`prefab:<vanilla prefab name>` or `prefab:<prefab>/<material>`, which takes the material off that prefab's
renderers (skipping snow overlays, LOD meshes and particles unless named); use it when the material is not loaded by
name at registration, e.g. `prefab:crystal_wall_1x1/crystal_window`. Use the `beacon_materials` console command to find vanilla material
names for the table.

To use a vanilla material outright, name the Unity material `JVLmock_<vanilla material name>` and assign it
to the mesh. Jötunn swaps in the real material at load, shader, textures, moss and snow included.

Two console commands (F5) help find names and check results:

- `beacon_dump [prefab]` lists every renderer, material, shader and texture on a prefab. Defaults to the
  beacon; try `beacon_dump guard_stone` or a stone piece for reference values.
- `beacon_materials <text>` lists loaded vanilla materials whose name contains the text, with their shader.
- `beacon_prefabs <text>` lists registered prefabs whose name contains the text, with their materials and shaders.

Both also write to the BepInEx log so the output can be copied from the file.

## MCP servers used by Claude Code sessions

`.mcp.json` registers two project-scoped servers. Both use `${VAR}` paths, which Claude Code expands; `%VAR%` is not.
A change to this file only takes effect in a new session.

- **unity-mcp**: Unity's relay (`%USERPROFILE%\.unity\relay\relay_win.exe`), talking to the open `BuildBeaconUnity`
  editor through the MCP add-on that ships with Unity 6's AI Assistant package. Used to reimport assets, run editor
  C# (bundle builds) and read the console.
- **blender**: Blender Lab's Blender MCP server (`blender-mcp`, from the `blender-1.0.3.mcpb` bundle extracted to
  `%USERPROFILE%\.claude\mcp\blender-mcp`, run with `uv`). It talks to a running Blender through the MCP add-on from
  the Blender Lab extensions repository: in Blender, Preferences > Get Extensions > Repositories, add
  `https://lab.blender.org/`, install and enable "MCP", then start its server from the add-on's panel. Used to
  inspect and edit the beacon model, run Python in Blender and search the API and manual.

Machine setup for the Blender server: install `uv` (`winget install astral-sh.uv`), unzip the `.mcpb` into the
folder above, and run `uv run --directory <folder> blender-mcp` once so it fetches Python and its packages.

## Blender model: edit the script, not the mesh

The beacon model is procedural. The Blender file is `Blender/totem.blend` (tracked; `.blend1` backups are ignored).
It is built by `xai_build_beacon.py`, which lives in that file's Text editor (`bpy.data.texts['xai_build_beacon.py']`) and is mirrored to `tools/blender/xai_build_beacon.py`
in this repository. Every shape change goes into that script, usually into its `PARAMS` dictionary, and the model
is regenerated by running it. Direct mesh edits in Blender are discarded the next time the script runs, so do not
make them. After a change: run the script, export the FBX over `BuildBeaconUnity/Assets/Beacon/xai_build_beacon.fbx`,
and follow the Unity bundle pipeline. Keep the repository copy in sync with the copy inside the `.blend`.
