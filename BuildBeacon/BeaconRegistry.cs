using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>Tracks all loaded beacons and computes effective requirements at a world position.</summary>
    public static class BeaconRegistry
    {
        private static readonly HashSet<BeaconController> Beacons = new HashSet<BeaconController>();

        public static void Register(BeaconController b) => Beacons.Add(b);
        public static IEnumerable<BeaconController> All => Beacons;
        public static void Unregister(BeaconController b) => Beacons.Remove(b);

        public static IEnumerable<BeaconController> InRange(Vector3 pos) =>
            Beacons.Where(b => b != null && b.IsValid && Vector3.Distance(b.transform.position, pos) <= b.Radius);

        /// <summary>Returns a fresh requirement array with beacon effects applied, or the original if none apply.</summary>
        public static Piece.Requirement[] GetEffectiveRequirements(Piece piece, Vector3 pos) =>
            GetEffectiveRequirements(piece.m_resources, pos);

        /// <summary>
        /// Same, from an explicit source array (e.g. the prefab's, when the instance copy may already be discounted).
        /// Boss rules take BossPercent off (zero the material at 100) and win over creature rules. Creature rules give
        /// discount levels, mapped to a percentage off;
        /// overlapping beacons combine by the Stacking setting: Max takes the beacon whose level takes the most off,
        /// Sum adds every beacon's levels (capped at the top level).
        /// </summary>
        /// <param name="plan">When given (the local player's placement), creature discounts use the rounding savings
        /// (DiscountSavings) and the steps to commit on placement are added to it.</param>
        /// <param name="lines">When given, one CostLine per requirement is added (for the HUD buffs).</param>
        public static Piece.Requirement[] GetEffectiveRequirements(Piece.Requirement[] source, Vector3 pos,
            List<DiscountSavings.Step> plan = null, List<CostLine> lines = null)
        {
            if (source == null || !BuildBeaconPlugin.Cfg.Enabled.Value) return source;
            var beacons = InRange(pos).ToList();
            if (beacons.Count == 0) return source;

            var result = new Piece.Requirement[source.Length];
            bool changed = false, planned = false;
            for (int i = 0; i < result.Length; i++)
            {
                var src = source[i];
                int amount = src.m_amount;
                bool bossFree = false;
                float usedPct = 0f;
                int streak = 0;

                bool boss = beacons.Any(b => DiscountRules.BossCovers(b.Trophies, src.m_resItem));
                if (boss && DiscountRules.BossMakesFree)
                {
                    amount = 0;
                    bossFree = true;
                }
                else
                {
                    if (boss) usedPct = DiscountRules.BossPercent; // below 100: rounded and saved like a creature discount
                    else
                    {
                        bool sum = BuildBeaconPlugin.Cfg.Stacking.Value == StackMode.Sum;
                        int levels = 0;         // Sum: every beacon's levels together
                        float bestPct = 0f;     // Max: the beacon whose level takes the most off
                        foreach (var b in beacons)
                        {
                            int l = DiscountRules.MobLevel(b.Trophies, src.m_resItem);
                            if (l <= 0) continue;
                            levels += l;
                            bestPct = Mathf.Max(bestPct, DiscountRules.PercentForLevel(l));
                        }
                        if (levels > 0) usedPct = sum ? DiscountRules.PercentForLevel(levels) : bestPct;
                    }
                    if (usedPct > 0f)
                    {
                        float exact = DiscountRules.ExactMobCost(src.m_amount, usedPct);
                        amount = DiscountRules.RoundCost(exact);
                        if (plan != null)
                        {
                            int before = plan.Count;
                            amount = DiscountSavings.Apply(src.m_resItem, exact, amount, usedPct, plan);
                            planned |= plan.Count > before; // a carried fraction must be committed even at full price
                            if (plan.Count > before) streak = plan[plan.Count - 1].Streak;
                        }
                    }
                }

                changed |= amount != src.m_amount;
                lines?.Add(new CostLine
                {
                    Item = src.m_resItem, Base = src.m_amount, Amount = amount, Percent = usedPct, BossFree = bossFree, Streak = streak,
                });
                result[i] = new Piece.Requirement
                {
                    m_resItem = src.m_resItem,
                    m_amount = amount,
                    m_amountPerLevel = src.m_amountPerLevel,
                    m_recover = src.m_recover,
                };
            }
            return changed || planned ? result : source;
        }
    }
}
