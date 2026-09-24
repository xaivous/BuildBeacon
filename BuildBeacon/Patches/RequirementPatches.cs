using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// Temporarily swaps piece.m_resources for the discounted array while vanilla evaluates/consumes,
    /// then restores it. Position comes from the placement ghost so the discount is based on where
    /// the piece will be, not where the player is.
    ///
    /// Swaps nest: Player.UpdatePlacement wraps the whole check -> place -> consume sequence, and
    /// Player.HaveRequirements is called again inside it. A depth count per piece makes sure the inner
    /// End() does not restore the original array before ConsumeResources has run.
    /// </summary>
    internal static class RequirementSwap
    {
        private sealed class Entry
        {
            public Piece.Requirement[] Original;
            public int Depth;
            public List<DiscountSavings.Step> Plan; // rounding savings to commit if this piece is placed
        }

        private static readonly Dictionary<Piece, Entry> Active = new Dictionary<Piece, Entry>();

        public static void Begin(Piece piece, Vector3 pos)
        {
            if (piece == null) return;

            if (Active.TryGetValue(piece, out var e))
            {
                e.Depth++;
                return;
            }

            var plan = new List<DiscountSavings.Step>();
            var eff = BeaconRegistry.GetEffectiveRequirements(piece.m_resources, pos, plan);
            if (ReferenceEquals(eff, piece.m_resources)) return; // no beacon in range: nothing to swap, nothing to track

            Active[piece] = new Entry { Original = piece.m_resources, Depth = 1, Plan = plan };
            piece.m_resources = eff;
        }

        public static void End(Piece piece)
        {
            if (piece == null || !Active.TryGetValue(piece, out var e)) return;
            if (--e.Depth > 0) return;
            piece.m_resources = e.Original;
            Active.Remove(piece);
        }

        /// <summary>The rounding savings planned for the piece while it is swapped, else null.</summary>
        public static List<DiscountSavings.Step> PlanFor(Piece piece) =>
            piece != null && Active.TryGetValue(piece, out var e) ? e.Plan : null;

        /// <summary>The piece's own requirements, even while the discounted array is swapped in.</summary>
        public static Piece.Requirement[] OriginalOf(Piece piece) =>
            Active.TryGetValue(piece, out var e) ? e.Original : piece.m_resources;

        /// <summary>True while the piece's m_resources is the discounted array.</summary>
        public static bool IsSwapped(Piece piece) => piece != null && Active.ContainsKey(piece);

        /// <summary>True when <paramref name="req"/> is one of the swapped-in requirements and a beacon took it from a
        /// real cost down to zero. Requirements that are zero in vanilla are left alone.</summary>
        public static bool DiscountedToZero(Piece piece, Piece.Requirement req)
        {
            if (piece == null || req == null || req.m_amount > 0 || !Active.TryGetValue(piece, out var e)) return false;
            int i = Array.IndexOf(piece.m_resources, req);
            return i >= 0 && i < e.Original.Length && e.Original[i].m_amount > 0;
        }

        /// <summary>The piece's own amount for a swapped-in requirement, or -1 when the piece is not swapped.</summary>
        public static int OriginalAmount(Piece piece, Piece.Requirement req)
        {
            if (piece == null || req == null || !Active.TryGetValue(piece, out var e)) return -1;
            int i = Array.IndexOf(piece.m_resources, req);
            return i >= 0 && i < e.Original.Length ? e.Original[i].m_amount : -1;
        }

        public static Vector3 GhostPos(Player p) =>
            p.m_placementGhost != null ? p.m_placementGhost.transform.position : p.transform.position;
    }

    /// <summary>
    /// 1.0 flow: UpdatePlacement -> HaveRequirements(piece) -> TryPlacePiece(piece) -> ConsumeResources(piece.m_resources).
    /// Wrapping the whole method keeps the discounted array in place through the consume step.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacement))]
    internal static class Player_UpdatePlacement
    {
        static void Prefix(Player __instance, out Piece __state)
        {
            __state = __instance.GetSelectedPiece();
            RequirementSwap.Begin(__state, RequirementSwap.GhostPos(__instance));
        }

        static void Postfix(Piece __state) => RequirementSwap.End(__state);
    }

    /// <summary>
    /// Also called outside UpdatePlacement: the build menu tints unaffordable pieces, and PieceTable filters
    /// available pieces. Inside UpdatePlacement this nests harmlessly thanks to the depth count.
    ///
    /// IsKnown is left alone: it only asks whether the player has seen each material, never how many they carry, so a
    /// discount changes nothing there except that a material a boss made free (amount 0) would be skipped and the piece
    /// discovered early. Player.UpdateKnownRecipesList asks it for every piece the player does not know yet on every
    /// inventory change, which made working out discounts for it a stutter of seconds inside a beacon's radius.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
    internal static class Player_HaveRequirements
    {
        static void Prefix(Player __instance, Piece piece, Player.RequirementMode mode)
        {
            if (mode != Player.RequirementMode.IsKnown) RequirementSwap.Begin(piece, RequirementSwap.GhostPos(__instance));
        }

        // Must skip exactly what the prefix skipped: an End without its Begin would take a level off an enclosing swap.
        static void Postfix(Piece piece, Player.RequirementMode mode)
        {
            if (mode != Player.RequirementMode.IsKnown) RequirementSwap.End(piece);
        }
    }

    /// <summary>The build HUD's requirement list for the hovered/selected piece.</summary>
    [HarmonyPatch(typeof(Hud), nameof(Hud.SetupPieceInfo))]
    internal static class Hud_SetupPieceInfo
    {
        /// <summary>The piece whose requirements the HUD is drawing, for the duration of SetupPieceInfo.</summary>
        internal static Piece Current;

        static void Prefix(Piece piece)
        {
            if (Player.m_localPlayer) RequirementSwap.Begin(piece, RequirementSwap.GhostPos(Player.m_localPlayer));
            Current = piece;
        }

        static void Postfix(Piece piece)
        {
            Current = null;
            RequirementSwap.End(piece);
        }
    }

    /// <summary>
    /// The build HUD's requirement rows, for pieces a beacon discounts:
    /// - the piece's own amount is shown struck through before the discounted one ("~~2~~ 1"), in grey, so the player
    ///   sees what the beacon takes off; vanilla's red/white "can afford" colour stays on the discounted number;
    /// - vanilla InventoryGui.SetupRequirement (1.0) hides a requirement whose amount is 0, so a material a beacon made
    ///   free vanished. Put that row back the way vanilla sets up a normal one (icon, tooltip, name) with "~~2~~ 0".
    /// Only while the build HUD draws a piece (crafting menus use the same method) and only for rows the beacon made
    /// cheaper; requirements that are 0 in vanilla are left alone.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.SetupRequirement))]
    internal static class InventoryGui_SetupRequirement
    {
        static void Postfix(bool __result, Transform elementRoot, Piece.Requirement req)
        {
            if (elementRoot == null || req == null || req.m_resItem == null) return;
            int original = RequirementSwap.OriginalAmount(Hud_SetupPieceInfo.Current, req);
            if (original <= req.m_amount) return;

            var amount = elementRoot.Find("res_amount")?.GetComponent<TMP_Text>();
            if (amount == null) return;
            if (!__result)
            {
                if (req.m_amount != 0) return;
                var icon = elementRoot.Find("res_icon")?.GetComponent<Image>();
                var name = elementRoot.Find("res_name")?.GetComponent<TMP_Text>();
                if (icon == null || name == null) return;

                var shared = req.m_resItem.m_itemData.m_shared;
                var label = Localization.instance.Localize(shared.m_name);
                icon.gameObject.SetActive(true);
                name.gameObject.SetActive(true);
                amount.gameObject.SetActive(true);
                icon.sprite = req.m_resItem.m_itemData.GetIcon();
                icon.color = Color.white;
                var tooltip = elementRoot.GetComponent<UITooltip>();
                if (tooltip != null) tooltip.m_text = label;
                name.text = label;
                amount.color = Color.white;
            }
            amount.text = $"<size=80%><color=#9a9a9a><s>{original}</s></color></size> {req.m_amount}";
        }
    }
}
