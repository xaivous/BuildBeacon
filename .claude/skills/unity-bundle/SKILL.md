---
name: unity-bundle
description: Take a change made in the BuildBeaconUnity project (materials, prefab, model, icon, colours, emission) all the way into the game: reimport, rebuild the buildbeacon asset bundle, copy it into the mod, rebuild the plugin. Use this whenever the user edits anything under BuildBeaconUnity/Assets/Beacon, asks to change how the beacon looks, mentions the bundle, prefab, material, albedo, emission or texture, or asks "how do I change X in Unity". Also covers the Unity MCP tools and their fallbacks.
---

# Unity change to in-game beacon

The beacon's model, materials, icon and prefab live in `BuildBeaconUnity/Assets/Beacon/`. They are labelled into one
asset bundle named `buildbeacon`, which is built into `BuildBeaconUnity/Assets/Output/` (gitignored), copied by hand
to `BuildBeacon/Assets/buildbeacon`, and embedded into the DLL by the csproj. The prefab asset is named
`xai_build_beacon_prefab`; the plugin clones it under the piece name `xai_build_beacon` at registration.

Nothing in the bundle is read live: every Unity change needs the bundle rebuilt and the plugin rebuilt before it shows
up in game. Materials are referenced by the prefab by GUID, so editing a material never requires resaving the prefab.

## The pipeline

1. **Make the change.** Either the user does it in the editor, or edit the YAML asset on disk (`.mat`, `.prefab`).
   Disk edits must be reimported before they exist for the editor: call `Unity_ManageAsset` with `Action: Import` and
   the asset path, e.g. `Assets/Beacon/BeaconCrystal.mat`. Changes made inside the editor do not need this.
2. **Rebuild the bundle** with `Unity_RunCommand` using the snippet in `references/rebuild-bundle.cs`. It saves assets,
   prints the emission and albedo of the beacon materials so you can confirm the values that went in, builds all
   labelled bundles for `StandaloneWindows64` into `Assets/Output`, and prints the bundle size.
3. **Copy and rebuild** the plugin:
   ```bash
   bash .claude/skills/unity-bundle/scripts/copy-bundle-and-build.sh
   ```
   This copies the bundle into the mod and runs the `build-deploy` verification, which also checks the bundle is
   actually embedded.
4. Tell the user what changed and what to look at in game. The `valheim-debug` skill has the log lines to expect.

## Unity MCP tool notes

- The relay is configured in `.mcp.json` and needs the Unity editor open on `BuildBeaconUnity`. If the tools are not
  in the session, the connection failed at session start; the fix history is in `docs/development.md` under
  "Unity project notes".
- `Unity_RunCommand` sometimes fails with "Unity not detected (no fresh discovery files found)" right after an
  import. Call `Unity_ManageEditor` with `GetState`, then retry the command once. It has always succeeded on retry.
- `Unity_GetConsoleLogs` reads the editor console. Warnings from `com.unity.ai.assistant` (account API, handshake,
  signature collection) are noise from the assistant package, not the project.
- If MCP is unavailable, the user can run the same steps in the editor: reimport via right-click, then build bundles
  with the AssetBundle Browser window (Window > AssetBundle Browser) targeting Windows x64 into `Assets/Output`.

## Editing materials on disk

Unity `.mat` files are YAML. The lines that matter for this project:

- `- _Color: {r, g, b, a}` is the Albedo swatch; on stone parts it becomes the tint over the vanilla texture.
- `- _EmissionColor: {r, g, b, a}` is HDR emission. `_EMISSION` must appear under `m_ValidKeywords` for it to render.
- To turn emission off, clearing the keyword in YAML is not enough: Unity re-derives it on import. Do it in the
  editor via `Unity_RunCommand` with `DisableKeyword("_EMISSION")`, `SetColor("_EmissionColor", Color.black)`,
  `EditorUtility.SetDirty`, `AssetDatabase.SaveAssets()`.

## How the plugin treats materials (so you can predict the in-game result)

`BuildBeaconPlugin.SwapToVanillaShaders` runs at registration:

- Emissive materials without a glow template are left exactly as authored on Unity's Standard shader, which is
  what vanilla does for glowing parts (the ward's runes). Keep emission peak around 2 or below and the colour
  saturated with one channel near zero; Valheim's tonemapping turns bright pastel emission white.
- Every material is dressed per the `MaterialTemplates` table at the top of the plugin. Stone entries (black marble
  for `BeaconStoneDark` and `BeaconBandDark`, grausten for `BeaconStoneLight`, `stone_mat` otherwise) move to the
  game's `Custom/Piece` shader, copy the vanilla material, and reapply the authored Albedo as tint, so a dark swatch
  makes dark stone. Glow entries (`BeaconCrystal` and `BeaconRune` on the crystal wall material) keep the authored
  emission colour on top of the vanilla look. Vertex noise, ripple and parallax are disabled on copies because they
  deform this low-poly mesh.
- Materials named `JVLmock_<vanilla name>` are replaced wholesale by Jötunn and skipped by the plugin.
- Place, hit and destroy sounds and particles come from the vanilla piece named by `EffectTemplatePrefab` in the
  plugin (`stone_wall_2x1`), copied onto the prefab's empty effect lists at registration. To give the beacon its own
  effects, fill the lists on the prefab in Unity and the plugin leaves them alone.
- Use the `beacon_materials <text>` console command in game to find vanilla material names. A table token of the
  form `prefab:<prefab>` or `prefab:<prefab>/<material>` takes the material off that prefab instead, which works
  even when the material is not loaded by name at registration (the crystal wall's was not). Name the material
  when the prefab has several: `beacon_prefabs <text>` lists them, and a piece's first renderer is often a snow
  overlay or LOD mesh.

## Unity project gotchas already handled

Every DLL `.meta` under `Assets/Assemblies` has Auto Reference and Validate References turned off. The
"Unity project notes" section of `docs/development.md` explains why (global-namespace type collisions and Jötunn's unshipped YamlDotNet
reference). When the post-build copy drops a new DLL into that folder, Unity generates a `.meta` with both on;
turn them off and commit the `.meta`.

## The model is generated by a script

`xai_build_beacon` in `totem.blend` is built by `xai_build_beacon.py`, stored in Blender's Text editor as
`bpy.data.texts['xai_build_beacon.py']` and mirrored to `tools/blender/xai_build_beacon.py`. Never edit the mesh
directly with bmesh or mesh operators; the next run of the script would discard it. Change the script, usually a
`PARAMS` value or a new piece in `build()`, then regenerate.

With the Blender MCP server connected, do it from here with `execute_blender_code`:

1. Read `bpy.data.texts['xai_build_beacon.py'].as_string()`, patch it with exact-anchor string replacement (assert
   each anchor occurs once), and write it back with `t.clear(); t.write(new_src)`.
2. `bpy.ops.ed.undo_push(...)`, then `exec(compile(new_src, ...), {"__name__": "__main__"})` inside
   `bpy.context.temp_override(window=..., area=<VIEW_3D area>, region=<WINDOW region>)` so the mesh operators have
   a viewport context. Push another undo step after. Capture stdout to get the script's tris and dimensions line.
3. Write the text back to `tools/blender/xai_build_beacon.py` so the repository copy matches.
4. Take a `VIEW_3D` screenshot to confirm. The script exports the FBX itself on every run: its `fbx_path` param is
   `//../BuildBeaconUnity/Assets/Beacon/xai_build_beacon.fbx`, relative to `totem.blend`, and it prints
   `[beacon] exported to ...` (or says it skipped because the `.blend` is unsaved). Then continue with the bundle
   pipeline above, starting with an Import of the FBX.

Leave the `.blend` unsaved for the user to review unless asked to save. `undo` replaces mesh datablocks, so re-fetch
`bpy.data.objects[...]`/`.data` after any undo instead of reusing references.
