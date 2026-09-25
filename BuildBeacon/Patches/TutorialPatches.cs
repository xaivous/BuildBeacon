using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// Hugin explains the Build Beacon the first time the local player comes to know one: walking within its discover
    /// range (a Build Beacon or a Great Beacon, theirs or anyone's), which is also what lists the holders and racks in the
    /// build menu. Placing one of their own shows it too, for a player who met a beacon before but never talked to Hugin.
    ///
    /// Uses vanilla's tutorial path unchanged: the text is added to Tutorial.m_texts under our own key, and
    /// Player.ShowTutorial spawns Hugin with it unless this character has already seen it. Talking to Hugin marks it
    /// seen (saved in the character profile) and files it in the compendium under its label, as vanilla tips are.
    /// Hugin's own rules apply, so players who turned tutorials off do not get it.
    /// </summary>
    internal static class TutorialPatches
    {
        public const string Key = "xai_buildbeacon";

        /// <summary>
        /// Player.AddKnownStation runs when the player first comes within a station's discover range, and again each
        /// time that station's level rises; only the first counts. The prefix notes whether the beacon's station was
        /// already known.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownStation))]
        internal static class Player_AddKnownStation
        {
            static void Prefix(Player __instance, CraftingStation station, out bool __state) =>
                __state = IsBeaconStation(station) && __instance.m_knownStations.ContainsKey(station.m_name);

            static void Postfix(Player __instance, CraftingStation station, bool __state)
            {
                if (__state || __instance != Player.m_localPlayer || !IsBeaconStation(station)) return;
                Show(__instance);
            }

            private static bool IsBeaconStation(CraftingStation station) =>
                station != null && station.GetComponent<BeaconController>() != null;
        }

        /// <summary>SetCreator is called once, inside PlacePiece, on the freshly placed piece.</summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        internal static class Piece_SetCreator
        {
            static void Postfix(Piece __instance, long uid)
            {
                var player = Player.m_localPlayer;
                if (player == null || uid != player.GetPlayerID()) return;
                if (__instance == null || __instance.GetComponent<BeaconController>() == null) return;
                Show(player);
            }
        }

        public static void Show(Player player)
        {
            var tutorial = Tutorial.instance;
            if (tutorial == null || player.HaveSeenTutorial(Key)) return;
            if (!tutorial.m_texts.Exists(t => t.m_name == Key))
            {
                tutorial.m_texts.Add(new Tutorial.TutorialText
                {
                    m_name = Key,
                    m_topic = "$xai_tutorial_beacon_topic",
                    m_label = "$xai_tutorial_beacon_label",
                    m_text = "$xai_tutorial_beacon_text",
                });
            }
            player.ShowTutorial(Key, false);
            BuildBeaconPlugin.Verbose("Hugin: Build Beacon tutorial shown");
        }
    }
}
