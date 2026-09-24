using System.Collections.Generic;
using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// Makes destroy refunds honour beacon discounts.
    ///
    /// PaidCost mode: when a piece is placed at a discount, the amounts actually paid are written to the new
    /// instance's ZDO. On destroy, the drop uses that record. The ZDO syncs and persists, so it survives reloads
    /// and applies on whichever client owns the piece when it dies.
    ///
    /// CurrentCost mode: on destroy, the drop uses the discount that applies at the piece's position right now.
    ///
    /// Both modes start from the prefab's requirement list, not the instance's. Unity's Instantiate copies the
    /// (temporarily discounted) prefab array into the instance at placement, so the instance copy is not a
    /// trustworthy base within the placing session.
    /// </summary>
    internal static class RefundPatches
    {
        public const string KeyPaid = "xai_paidcost"; // ZPackage: int n, then (string item prefab, int amount)*

        /// <summary>Set for the duration of PlacePiece when the placed piece is being paid at a discount.</summary>
        private static Piece.Requirement[] _placingPaid;

        // ---- Record what was paid ----

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        internal static class Player_PlacePiece
        {
            static void Prefix(Piece piece, bool cheated)
            {
                // Only record when a beacon actually changed the cost; otherwise the prefab amounts are the truth.
                _placingPaid = !cheated && RequirementSwap.IsSwapped(piece) ? piece.m_resources : null;
            }

            static void Postfix() => _placingPaid = null;
        }

        /// <summary>SetCreator is called exactly once, inside PlacePiece, on the freshly instantiated piece.</summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        internal static class Piece_SetCreator
        {
            static void Postfix(Piece __instance)
            {
                if (_placingPaid == null) return;
                var paid = _placingPaid;
                _placingPaid = null; // one instance per placement
                RecordPaidCost(__instance, paid);
            }
        }

        public static void RecordPaidCost(Piece instance, Piece.Requirement[] paid)
        {
            if (instance == null || instance.m_nview == null || !instance.m_nview.IsValid()) return;
            ZdoUtil.SetPackage(instance.m_nview, KeyPaid, pkg =>
            {
                pkg.Write(paid.Length);
                foreach (var r in paid)
                {
                    pkg.Write(r.m_resItem != null ? r.m_resItem.name : "");
                    pkg.Write(r.m_amount);
                }
            });
        }

        public static bool TryReadPaidCost(Piece instance, out Dictionary<string, int> paid)
        {
            paid = null;
            if (instance == null || !ZdoUtil.TryGetPackage(instance.m_nview, KeyPaid, out var pkg)) return false;
            paid = new Dictionary<string, int>();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++) paid[pkg.ReadString()] = pkg.ReadInt();
            return true;
        }

        // ---- Apply on destroy ----

        /// <summary>
        /// Piece.DropResources is the single drop path: WearNTear.Destroy, Player.RemovePiece and damage all end here.
        /// Swap the instance's list for the refund list around the call.
        /// </summary>
        [HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
        internal static class Piece_DropResources
        {
            static void Prefix(Piece __instance, out Piece.Requirement[] __state)
            {
                __state = null;
                if (!BuildBeaconPlugin.Cfg.Enabled.Value) return;

                var refund = RefundListFor(__instance);
                if (refund == null || ReferenceEquals(refund, __instance.m_resources)) return;

                __state = __instance.m_resources;
                __instance.m_resources = refund;
            }

            static void Postfix(Piece __instance, Piece.Requirement[] __state)
            {
                if (__state != null) __instance.m_resources = __state;
            }
        }

        private static Piece.Requirement[] RefundListFor(Piece instance)
        {
            var baseList = PrefabRequirements(instance) ?? instance.m_resources;
            if (baseList == null) return null;

            switch (BuildBeaconPlugin.Cfg.Refund.Value)
            {
                case RefundMode.PaidCost:
                    if (!TryReadPaidCost(instance, out var paid)) return baseList;
                    var result = new Piece.Requirement[baseList.Length];
                    for (int i = 0; i < baseList.Length; i++)
                    {
                        var src = baseList[i];
                        var name = src.m_resItem != null ? src.m_resItem.name : "";
                        result[i] = new Piece.Requirement
                        {
                            m_resItem = src.m_resItem,
                            m_amount = paid.TryGetValue(name, out var amt) ? amt : src.m_amount,
                            m_amountPerLevel = src.m_amountPerLevel,
                            m_recover = src.m_recover,
                        };
                    }
                    return result;

                case RefundMode.CurrentCost:
                    return BeaconRegistry.GetEffectiveRequirements(baseList, instance.transform.position);

                default:
                    return baseList;
            }
        }

        /// <summary>The prefab's untouched requirement list for a placed instance.</summary>
        private static Piece.Requirement[] PrefabRequirements(Piece instance)
        {
            if (ZNetScene.instance == null) return null;
            var prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(instance.gameObject));
            var piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            return piece != null ? piece.m_resources : null;
        }
    }
}
