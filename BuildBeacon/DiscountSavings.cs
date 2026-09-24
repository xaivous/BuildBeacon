using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// Carries the part of a percentage discount lost to rounding over to later placements, per material.
    ///
    /// A creature discount's exact cost (say 1.5 Wood for a 2-Wood floor at 25% off) is rounded up to a whole item, so
    /// the player overpays a fraction each time. That fraction goes into this material's balance. When the balance plus
    /// the current placement's fraction reaches a whole item, the placement costs one less and the balance drops by
    /// one: two floors cost 2 + 1, the full 25% on average. The exact cost can be a fraction of an item (95% off
    /// 2 Wood is 0.1: one Wood, then nine free pieces), so savings can make a piece free.
    ///
    /// Only the local player's placements use it (the build HUD, the affordability check and the consume step all see
    /// the same number); refunds ignore it. The balance changes only when a piece is actually placed, and is kept in
    /// the character's custom data (saved with the character), so it survives restarts.
    /// </summary>
    public static class DiscountSavings
    {
        private const string DataKey = "xai_beacon_savings";
        private const float MaxBalance = 0.9999f;

        /// <summary>One material's outcome for one placement, committed if the piece is placed.</summary>
        public sealed class Step
        {
            public string Material;   // item prefab name
            public ItemDrop Item;
            public float Percent;     // the percentage that produced the fraction, for the buff's name
            public bool Bonus;        // this placement costs one item less
            public int Streak;        // how many of this piece in a row, from now, each cost one item less
            public float NewBalance;
        }

        private static Player s_player;
        private static readonly Dictionary<string, float> s_balance = new Dictionary<string, float>();

        /// <summary>
        /// Apply the balance to one requirement. <paramref name="exact"/> is the discounted cost before rounding
        /// (DiscountRules.ExactMobCost), <paramref name="rounded"/> the whole-item cost. Adds a step to <paramref name="plan"/>
        /// when there is a fraction to carry, and returns the cost to charge.
        /// </summary>
        public static int Apply(ItemDrop item, float exact, int rounded, float percent, List<Step> plan)
        {
            float over = rounded - exact;
            if (item == null || plan == null || over <= DiscountRules.Epsilon) return rounded;
            var key = item.name;
            float balance = Get(key);
            float pending = balance + over;
            bool bonus = pending >= 1f - DiscountRules.Epsilon && rounded >= 1;
            plan.Add(new Step
            {
                Material = key,
                Item = item,
                Percent = percent,
                Bonus = bonus,
                Streak = bonus ? StreakFrom(balance, over) : 0,
                NewBalance = Mathf.Clamp(bonus ? pending - 1f : pending, 0f, MaxBalance),
            });
            return bonus ? rounded - 1 : rounded;
        }

        /// <summary>
        /// How many placements in a row of a piece overpaying <paramref name="over"/> each cost one item less, starting
        /// from <paramref name="balance"/>: 1 for 25% off 2 Wood, 4 right after paying for one at 90% off 2 Wood.
        /// </summary>
        private static int StreakFrom(float balance, float over)
        {
            int n = 0;
            while (n < 99)
            {
                float pending = balance + over;
                if (pending < 1f - DiscountRules.Epsilon) break;
                n++;
                balance = Mathf.Clamp(pending - 1f, 0f, MaxBalance);
            }
            return n;
        }

        /// <summary>The piece was placed: keep its balances.</summary>
        public static void Commit(List<Step> plan)
        {
            if (plan == null || plan.Count == 0 || !Load()) return;
            foreach (var s in plan)
            {
                s_balance[s.Material] = s.NewBalance;
                if (s.Bonus) BuildBeaconPlugin.Log.LogInfo($"Savings: rounding added up to one {s.Material} off this piece");
            }
            Save();
        }

        public static float Get(string material) => Load() && s_balance.TryGetValue(material, out var v) ? v : 0f;

        /// <summary>
        /// Diagnostics: the beacons covering <paramref name="pos"/> (distance, radius, trophies) and, per requirement,
        /// base cost, cost without savings, cost with savings and the saving step. For [diag] lines and beacon_cost.
        /// </summary>
        public static string Describe(Piece piece, Piece.Requirement[] source, Vector3 pos)
        {
            var sb = new StringBuilder();
            sb.Append($"{(piece != null ? piece.name : "?")} at {pos:F1}; ");
            var beacons = BeaconRegistry.All.ToList();
            sb.Append($"{beacons.Count} beacon(s) loaded");
            foreach (var b in beacons)
            {
                if (b == null) continue;
                float d = Vector3.Distance(b.transform.position, pos);
                sb.Append($"\n  beacon at {b.transform.position:F1}: {d:0.#} m of {b.Radius:0.#} m{(d <= b.Radius ? " (covers)" : "")}, valid {b.IsValid}, " +
                          $"trophies [{string.Join(", ", b.Trophies.Select(kv => $"{kv.Key} x{kv.Value}"))}]");
            }
            if (source == null) return sb.ToString();
            var plain = BeaconRegistry.GetEffectiveRequirements(source, pos);
            var plan = new List<Step>();
            var withSavings = BeaconRegistry.GetEffectiveRequirements(source, pos, plan);
            for (int i = 0; i < source.Length; i++)
            {
                var item = source[i].m_resItem;
                var name = item != null ? item.name : "?";
                sb.Append($"\n  {name}: base {source[i].m_amount}, discounted {plain[i].m_amount}, with savings {withSavings[i].m_amount}, " +
                          $"balance {Get(name):0.###}");
                var step = plan.FirstOrDefault(s => s.Material == name);
                if (step != null) sb.Append($", step: {step.Percent:0.#}% bonus {step.Bonus} new balance {step.NewBalance:0.###}");
            }
            return sb.ToString();
        }

        /// <summary>Reads the local player's balances whenever the local player changes (character switch, respawn).</summary>
        private static bool Load()
        {
            var p = Player.m_localPlayer;
            if (p == null) return false;
            if (p == s_player) return true;
            s_player = p;
            s_balance.Clear();
            if (p.m_customData != null && p.m_customData.TryGetValue(DataKey, out var text) && !string.IsNullOrEmpty(text))
            {
                foreach (var pair in text.Split(';'))
                {
                    var kv = pair.Split('=');
                    if (kv.Length == 2 && float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                        s_balance[kv[0]] = Mathf.Clamp(v, 0f, MaxBalance);
                }
            }
            return true;
        }

        private static void Save()
        {
            if (s_player == null || s_player.m_customData == null) return;
            var sb = new StringBuilder();
            foreach (var kv in s_balance.Where(kv => kv.Value > DiscountRules.Epsilon))
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("0.####", CultureInfo.InvariantCulture));
            }
            if (sb.Length > 0) s_player.m_customData[DataKey] = sb.ToString();
            else s_player.m_customData.Remove(DataKey);
        }
    }

    /// <summary>One requirement's outcome at a position, for the HUD buffs (BeaconRegistry.GetEffectiveRequirements).</summary>
    public sealed class CostLine
    {
        public ItemDrop Item;
        public int Base;          // the piece's own amount
        public int Amount;        // what this placement costs
        public float Percent;     // the creature discount's percentage, 0 if none
        public bool BossFree;     // a boss trophy makes it free
        public int Streak;        // with savings: how many of this piece in a row each cost one less, from now
    }

    /// <summary>
    /// A HUD buff per material whose rounding savings take an item off the selected piece at the placement ghost:
    /// "-1 Wood" when the piece still costs some of it, "4 free" when the savings make it free, counting how many of
    /// this piece in a row will be. Only the savings show here: a plain level discount (25% off 4 Wood is 3, no
    /// rounding) gets no buff, since the build panel's struck-through price already shows it. The savings take off
    /// exactly one item when they apply, hence "-1".
    ///
    /// Display only: the entries are added to the HUD's status effect list (SEMan.GetHUDStatusEffects) and never enter
    /// the player's SEMan, so nothing is saved, synced or looked up in ObjectDB. Each has the material's icon, the
    /// percentage in its tooltip, and flashes when it appears or turns from "-x" to "free".
    /// </summary>
    public static class DiscountBuffs
    {
        private static readonly Dictionary<string, StatusEffect> s_effects = new Dictionary<string, StatusEffect>();
        private static Dictionary<string, bool> s_shown = new Dictionary<string, bool>(); // material -> shown as free
        private static Dictionary<string, bool> s_now = new Dictionary<string, bool>();
        private static readonly List<DiscountSavings.Step> s_plan = new List<DiscountSavings.Step>();
        private static readonly List<CostLine> s_lines = new List<CostLine>();

        public static void AddTo(List<StatusEffect> effects, Player player)
        {
            s_now.Clear();
            var piece = player.InPlaceMode() ? player.GetSelectedPiece() : null;
            if (piece != null && player.m_placementGhost != null)
            {
                s_plan.Clear();
                s_lines.Clear();
                var source = Patches.RequirementSwap.OriginalOf(piece);
                BeaconRegistry.GetEffectiveRequirements(source, player.m_placementGhost.transform.position, s_plan, s_lines);
                foreach (var line in s_lines)
                {
                    if (line.Item == null || line.BossFree || line.Streak <= 0) continue; // savings not taking an item off
                    bool free = line.Amount == 0;
                    var key = line.Item.name;
                    var se = EffectFor(key, line, free);
                    if (!s_shown.TryGetValue(key, out var wasFree) || wasFree != free) se.m_isNew = true; // flash
                    s_now[key] = free;
                    effects.Add(se);
                }
            }
            var t = s_shown;
            s_shown = s_now;
            s_now = t;
        }

        private static StatusEffect EffectFor(string key, CostLine line, bool free)
        {
            if (!s_effects.TryGetValue(key, out var se) || se == null)
            {
                se = ScriptableObject.CreateInstance<StatusEffect>();
                se.name = "xai_savings_" + key;
                s_effects[key] = se;
            }
            var loc = Localization.instance;
            var item = loc.Localize(line.Item.m_itemData.m_shared.m_name);
            var pct = line.Percent.ToString("0.#", CultureInfo.InvariantCulture);
            se.m_icon = line.Item.m_itemData.GetIcon();
            if (free)
            {
                se.m_name = string.Format(loc.Localize("$xai_savings_buff_free"), line.Streak);
                se.m_tooltip = string.Format(loc.Localize("$xai_savings_buff_free_tooltip"), pct, item, line.Streak);
            }
            else
            {
                const int off = 1; // what the savings take off when they apply
                se.m_name = string.Format(loc.Localize("$xai_savings_buff_off"), off, item);
                se.m_tooltip = string.Format(loc.Localize("$xai_savings_buff_off_tooltip"), pct, off, item);
            }
            return se;
        }
    }
}
