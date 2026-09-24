# BuildBeacon

Valheim 1.0 mod (BepInEx + Jötunn) adding the Build Beacon piece. Start with `docs/handoff/`: the newest dated file
(`YY_M_DD` plus a letter, e.g. `26_9_23A.md`) is the last handoff and holds the project map, where each kind of truth
lives, the loops, the gotchas and the open items; `current.md` holds what changed since it, and `docs/open-items.md` is the to-do list. Use the `handoff` skill before a context clear
(it folds `current.md` into a new dated file and resets it) and the `hydrate` skill after one. The pipelines are skills
under `.claude/skills/` (`build-deploy`, `unity-bundle`, `valheim-debug`); use their scripts rather than bare
commands.

Rules that are easy to get wrong:

- Blender edits go into the generator script (`bpy.data.texts['xai_build_beacon.py']`, mirrored to
  `tools/blender/`), never into the mesh. Regenerate by running it.
- Unity `.mat` assets own material look; the FBX importer remaps by name onto them. The plugin's `MaterialTemplates`
  table decides which vanilla textures dress which part.
- Read the BepInEx log (`valheim-debug` script) before forming a theory. If it is silent, the `[diag]` lines (turn on
  `VerboseLogging` in the Dev section) and the console commands (`beacon_dump`, `beacon_materials`, `beacon_prefabs`, `beacon_tree`) are the next step.
- `WearNTear.m_noSupportWear = true` means the piece *requires* support on 1.0. Check signatures with
  `tools/sigdump` before writing Harmony patches; one bad patch disables them all.
- Commit only when asked, with descriptive messages, unrelated work in separate commits.
