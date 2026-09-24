# Changelog

## 0.3.0

- **The rules files are now `xaivous.BuildBeacon.BossRules.txt` and `xaivous.BuildBeacon.MobRules.txt`** (were
  `BuildBeacon.BossRules.txt` and `BuildBeacon.MobRules.txt`). A server's own rules carry over automatically the
  first time 0.3.0 starts; the old files are kept as a backup and can be deleted afterwards.
- Server and players must all update together, as for any new minor version.

## 0.2.0

- **The config file is now `BepInEx/config/xaivous.buildbeacon.cfg`** (was `com.xaivous.buildbeacon.cfg`): the mod's ID
  dropped its `com.` prefix. Your settings carry over automatically the first time 0.2.0 starts; the old file is kept
  as a backup and can be deleted afterwards. Nothing in your worlds or characters changes.
- **A quieter log.** How the pieces are set up, and the diagnostic lines, now appear only with the new `VerboseLogging`
  setting (Dev section, off by default); the log shows one line listing the pieces instead.
- On joining a server, the log says "Using the server's settings", with the values in force.
- No more false "Rule trophy ... does not match any item" warnings at the main menu alongside some other mods; real
  typos in the rules files are still reported, once.
- Server and players must all update together, as for any new minor version.

## 0.1.0

Initial release.

- **The Build Beacon**, a crafting station whose radius makes building cheaper, with four creature trophy slots of its
  own.
- **The Great Beacon**, an 8-metre beacon whose seven alcoves hold boss trophies.
- **Boss trophy holders**: the Boss Trophy Pillar for floors and the Boss Trophy Mount for walls.
- **Trophy racks** for more creature trophies: the Trophy Column and the Trophy Panel.
- **Levels and radius** from linked holders and racks.
- **Discounts**: three creature discount levels (2x, 5x, 10x), and boss materials free or 95% off, by the server's
  choice.
- **The beacon panel**, with Trophies and Discounts tabs.
- **Rules files** that sync from the server, and fair refunds of what you paid.
