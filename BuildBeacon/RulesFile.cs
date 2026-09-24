using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// A human-readable rules file: one rule per line, comments allowed, hot-reloaded on save.
    ///
    /// Each file mirrors one admin-only config entry. The file is the source of truth on whichever side has
    /// authority (dedicated server, host, or single player); its parsed content is written into the entry, which
    /// Jötunn then synchronises to clients. Clients keep their file for reference but the server's rules win.
    /// </summary>
    internal sealed class RulesFile
    {
        public const string BossFileName = "BuildBeacon.BossRules.txt";
        public const string MobFileName = "BuildBeacon.MobRules.txt";

        private const float ReloadDebounceSeconds = 0.5f;

        public static RulesFile Boss { get; private set; }
        public static RulesFile Mob { get; private set; }

        public static bool HasAuthority => ZNet.instance == null || ZNet.instance.IsServer();

        public static void InitAll()
        {
            Boss = new RulesFile(BossFileName, BuildBeaconPlugin.Cfg.BossRules, NormalizeBossLine, BossHeader, BossExamples);
            Mob = new RulesFile(MobFileName, BuildBeaconPlugin.Cfg.MobRules, NormalizeMobLine, MobHeader, MobExamples);
            Boss.Init();
            Mob.Init();
        }

        /// <summary>Call from a MonoBehaviour Update: applies pending file changes on the main thread.</summary>
        public static void PollAll()
        {
            Boss?.Poll();
            Mob?.Poll();
        }

        // ---- Instance ----

        private readonly string _fileName;
        private readonly ConfigEntry<string> _entry;
        private readonly Func<string, string> _normalizeLine;
        private readonly string[] _header;
        private readonly string[] _examples;

        private FileSystemWatcher _watcher;
        private volatile bool _dirty;
        private float _dirtySince;

        private RulesFile(string fileName, ConfigEntry<string> entry, Func<string, string> normalizeLine, string[] header, string[] examples)
        {
            _fileName = fileName;
            _entry = entry;
            _normalizeLine = normalizeLine;
            _header = header;
            _examples = examples;
        }

        public string Path => System.IO.Path.Combine(Paths.ConfigPath, _fileName);

        private void Init()
        {
            try
            {
                if (!File.Exists(Path))
                {
                    File.WriteAllText(Path, DefaultContent());
                    BuildBeaconPlugin.Log.LogInfo($"Wrote default rules to {Path}");
                }
                Apply(File.ReadAllText(Path));
                Watch();
            }
            catch (Exception e)
            {
                BuildBeaconPlugin.Log.LogWarning($"{_fileName} unavailable, using config entry value: {e.Message}");
            }
        }

        private void Poll()
        {
            if (!_dirty) return;
            if (Time.unscaledTime - _dirtySince < ReloadDebounceSeconds) return; // let the editor finish writing
            _dirty = false;

            if (!HasAuthority)
            {
                BuildBeaconPlugin.Log.LogInfo($"{_fileName} changed, but this client is connected to a server; the server's rules apply.");
                return;
            }

            try
            {
                Apply(File.ReadAllText(Path));
            }
            catch (IOException)
            {
                _dirty = true; // still locked by the editor, try again next frame
                _dirtySince = Time.unscaledTime;
            }
            catch (Exception e)
            {
                BuildBeaconPlugin.Log.LogWarning($"Failed to reload {_fileName}: {e.Message}");
            }
        }

        private void Watch()
        {
            _watcher = new FileSystemWatcher(Paths.ConfigPath, _fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            FileSystemEventHandler mark = (_, __) => { _dirty = true; _dirtySince = Time.unscaledTime; };
            _watcher.Changed += mark;
            _watcher.Created += mark;
            _watcher.Renamed += (_, __) => { _dirty = true; _dirtySince = Time.unscaledTime; };
        }

        /// <summary>Parse the file text and push the normalised rules into the synced config entry.</summary>
        private void Apply(string text)
        {
            var lines = ParseLines(text, _normalizeLine, out var rejected);
            foreach (var bad in rejected)
                BuildBeaconPlugin.Log.LogWarning($"{_fileName}: ignoring malformed line \"{bad}\"");

            if (lines.Count == 0)
            {
                BuildBeaconPlugin.Log.LogWarning($"{_fileName} contains no valid rules; keeping the current rules.");
                return;
            }

            var normalised = string.Join("\n", lines);
            if (_entry.Value == normalised)
            {
                DiscountRules.Rebuild();
                return;
            }

            // Setting the value raises SettingChanged, which rebuilds the rule tables and lets Jötunn sync to clients.
            _entry.Value = normalised;
            BuildBeaconPlugin.Log.LogInfo($"Applied {lines.Count} rules from {_fileName}");
        }

        // ---- Parsing (shared with DiscountRules so file and config entry use one grammar) ----

        /// <summary>
        /// Splits text into rule lines, strips '#' comments and blank lines, and normalises each with
        /// <paramref name="normalizeLine"/>, which returns null for a malformed line.
        /// </summary>
        public static List<string> ParseLines(string text, Func<string, string> normalizeLine, out List<string> rejected)
        {
            var result = new List<string>();
            rejected = new List<string>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;

                var normalised = normalizeLine(line);
                if (normalised == null) rejected.Add(raw.Trim());
                else result.Add(normalised);
            }
            return result;
        }

        /// <summary>"Trophy | Material" to "Trophy|Material", or null.</summary>
        public static string NormalizeBossLine(string line)
        {
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0) return null;
            return $"{parts[0]}|{parts[1]}";
        }

        public const string StackableKeyword = "Stackable";

        /// <summary>
        /// "Trophy | Material | Level" to "Trophy|Material|Level" (a whole number from 1), or the trait line
        /// "Trophy | Stackable" to "Trophy|Stackable"; null for anything else.
        /// </summary>
        public static string NormalizeMobLine(string line)
        {
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length == 2 && parts[0].Length > 0 && parts[1].Equals(StackableKeyword, StringComparison.OrdinalIgnoreCase))
                return $"{parts[0]}|{StackableKeyword}";
            if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return null;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) || level < 1) return null;
            return $"{parts[0]}|{parts[1]}|{level.ToString(CultureInfo.InvariantCulture)}";
        }

        // ---- Default file content ----

        private string DefaultContent()
        {
            var defaults = _entry.DefaultValue.ToString()
                .Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0)
                .Select(l => string.Join(" | ", l.Split('|').Select(p => p.Trim())));

            return string.Join("\n", _header.Concat(new[] { "" }).Concat(defaults).Concat(new[] { "" }).Concat(_examples).Concat(new[] { "" }));
        }

        private static readonly string[] CommonFooter =
        {
            "#",
            "# Material names may be the internal prefab name (FineWood) or the in-game name (Fine Wood); spaces and case are ignored.",
            "# Use * as the material to affect every material.",
            "# Lines starting with # are comments. Save the file and the rules reload in game without a restart.",
            "# On a dedicated server the server's copy of this file is the one that counts; it is synced to all clients.",
        };

        private static readonly string[] BossHeader = new[]
        {
            "# BuildBeacon boss rules",
            "#",
            "# One rule per line:   TrophyPrefab | Material",
            "#",
            "# A slotted boss trophy takes the BossPercent setting off every listed material for pieces built within the",
            "# beacon's radius: 100 (the default) makes them FREE, 95 makes each item go 20x as far.",
            "# Each boss trophy counts once per beacon.",
            "# A trophy may have several lines, one per material.",
        }.Concat(CommonFooter).ToArray();

        private static readonly string[] BossExamples =
        {
            "# Examples:",
            "# TrophyDragonQueen | Obsidian",
            "# TrophyEikthyr | Stone",
        };

        private static readonly string[] MobHeader = new[]
        {
            "# BuildBeacon creature rules",
            "#",
            "# One rule per line:          TrophyPrefab | Material | Level",
            "# Mark a trophy stackable:    TrophyPrefab | Stackable",
            "#",
            "# A creature trophy slotted in a beacon gives each listed material that many discount levels. Levels from",
            "# different trophies add up. With the default LevelPercents setting, level 1 takes 50% off (2x), 2 takes 80%",
            "# (5x) and 3, the top level, 90% (10x). A beacon counts each trophy once, unless the trophy is",
            "# marked Stackable: then every copy adds its levels.",
            "# Costs round up; the part of an item rounding overpays is saved per player and makes a later piece cheaper.",
            "# A trophy that also appears in the boss rules is treated as a boss trophy.",
        }.Concat(CommonFooter).ToArray();

        private static readonly string[] MobExamples =
        {
            "# Examples:",
            "# TrophyForestTroll | Stone | 1",
            "# TrophyAbomination | * | 1",
            "# TrophyNeck | Stackable",
        };
    }
}
