using System.Collections.Generic;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// A creature trophy rack (the Trophy Column and the Trophy Panel): an add-on to a Build Beacon with four alcoves,
    /// each holding one creature trophy.
    ///
    /// Like the boss holders it is a vanilla station extension of the beacon's CraftingStation: placement needs a beacon
    /// in range, the thread shows, and it links to the closest beacon in range. Each linked rack raises the beacon's
    /// level by one (sharing MaxLevel with the boss holders) and adds its four slots on top of the beacon's own. Its
    /// trophies count towards the beacon's discounts, a non-stackable trophy once across the beacon's network (the
    /// beacon and every rack linked to it); see BeaconController.RebuildEffective.
    ///
    /// Each alcove is its own target: its <c>slot_N</c> child carries a collider and a <see cref="RackSlot"/>, which the
    /// player's hover finds first (Player.FindHoverObject takes the collider's object, and the hover and use look
    /// upward from it). The trophies live on the rack's ZDO, one key per slot. Changes follow ItemStand on Valheim 1.0,
    /// as the boss holders do: the ZDO owner writes; another player asks for ownership and applies the queued action
    /// once it arrives. Edits are ward-protected. Destroying the rack drops its trophies.
    /// </summary>
    public class MobRack : MonoBehaviour, Interactable, Hoverable
    {
        public const int SlotCount = 4;
        private const string KeySlot = "xai_rack_slot_";          // + index: item prefab name, "" when empty
        private const string RpcRequestOwn = "xai_rack_request_own";
        private const string AttachPrefix = "attach_";
        private const string SlotPrefix = "slot_";
        private const float QueueTimeout = 2f;
        private const float DropHeight = 1f;

        /// <summary>Set at registration. The Trophy Panel's niches are shallow: fit trophies to the opening (width and
        /// height only) and let them stand out, instead of following the TrophyPlacement setting.</summary>
        public bool m_fitToFace;
        public Vector2 m_faceSize = new Vector2(0.50f, 0.58f);

        private static readonly List<MobRack> s_all = new List<MobRack>();
        /// <summary>Loaded, networked racks.</summary>
        public static IReadOnlyList<MobRack> All => s_all;

        private ZNetView _nview;
        private StationExtension _extension;
        private Piece _piece;
        private readonly Transform[] _mounts = new Transform[SlotCount];
        private readonly GameObject[] _visuals = new GameObject[SlotCount];
        private readonly string[] _shown = { "", "", "", "" };
        private TrophyPlacement _shownPlacement;
        private int _queuedSlot = -1;
        private ItemDrop.ItemData _queuedItem;  // trophy to place once this client owns the ZDO
        private bool _queuedTake;              // take the slot's trophy once this client owns the ZDO
        private float _queuedAt;

        public bool IsValid => _nview != null && _nview.IsValid();
        public string Trophy(int slot) => IsValid && slot >= 0 && slot < SlotCount ? _nview.GetZDO().GetString(KeySlot + slot, "") : "";
        public bool HasTrophy(int slot) => Trophy(slot).Length > 0;

        public int Filled
        {
            get
            {
                int n = 0;
                for (int i = 0; i < SlotCount; i++) if (HasTrophy(i)) n++;
                return n;
            }
        }

        /// <summary>The beacon this rack belongs to: the closest one within the extension's range, as vanilla picks.</summary>
        public BeaconController LinkedBeacon
        {
            get
            {
                var station = _extension != null ? _extension.FindClosestStationInRange(transform.position) : null;
                return station != null ? station.GetComponent<BeaconController>() : null;
            }
        }

        /// <summary>Give every loaded rack the current link range (HolderRange). Their beacons recount within a second.</summary>
        public static void ApplyRange()
        {
            foreach (var r in s_all)
                if (r._extension != null) r._extension.m_maxStationDistance = BossHolder.Range;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _extension = GetComponent<StationExtension>();
            if (_extension != null) _extension.m_maxStationDistance = BossHolder.Range; // the placement ghost too
            _piece = GetComponent<Piece>();
            for (int i = 0; i < SlotCount; i++) _mounts[i] = transform.Find(AttachPrefix + i);
            if (!IsValid) return; // placement ghost
            s_all.Add(this);
            var wnt = GetComponent<WearNTear>();
            if (wnt != null) wnt.m_onDestroyed += OnDestroyed;
            _nview.Register(RpcRequestOwn, RPC_RequestOwn);
            InvokeRepeating(nameof(RefreshVisuals), 0f, 1f); // picks up other players' changes
            LinkedBeacon?.Resync(); // a new rack raises its beacon's level at once
        }

        private void OnDestroy()
        {
            if (!s_all.Remove(this)) return;
            var beacon = LinkedBeacon;
            if (beacon != null) beacon.Invoke(nameof(BeaconController.Resync), 0f); // next frame, once this rack is gone
        }

        /// <summary>Show each slot's trophy in its alcove; rebuild only the slots that changed.</summary>
        private void RefreshVisuals()
        {
            if (!IsValid) return;
            var placement = BuildBeaconPlugin.Cfg.Placement.Value;
            bool all = !m_fitToFace && placement != _shownPlacement;
            _shownPlacement = placement;
            for (int i = 0; i < SlotCount; i++)
            {
                var trophy = Trophy(i);
                if (!all && trophy == _shown[i] && (trophy.Length == 0 || _visuals[i] != null)) continue;
                if (_visuals[i] != null) Destroy(_visuals[i]);
                _visuals[i] = null;
                _shown[i] = trophy;
                if (trophy.Length > 0 && _mounts[i] != null)
                    _visuals[i] = m_fitToFace
                        ? BeaconTrophyDisplay.Build(_mounts[i], trophy, TrophyPlacement.Inset, m_faceSize)
                        : BeaconTrophyDisplay.Build(_mounts[i], trophy, placement);
            }
        }

        /// <summary>Only the owner drops, so there is one set of drops.</summary>
        private void OnDestroyed()
        {
            if (!IsValid || !_nview.IsOwner()) return;
            var dropped = new List<string>();
            for (int i = 0; i < SlotCount; i++)
            {
                var trophy = Trophy(i);
                if (trophy.Length == 0) continue;
                BeaconController.SpawnItems(trophy, 1, transform.position + Vector3.up * DropHeight);
                _nview.GetZDO().Set(KeySlot + i, "");
                dropped.Add(trophy);
            }
            if (dropped.Count > 0)
                BuildBeaconPlugin.Log.LogInfo($"Trophy rack destroyed at {transform.position:F1}: dropped {string.Join(", ", dropped)}");
        }

        private void SetTrophy(int slot, string prefab)
        {
            _nview.GetZDO().Set(KeySlot + slot, prefab ?? "");
            RefreshVisuals();
            LinkedBeacon?.Resync(); // the beacon recounts at once here; other clients within a second
        }

        /// <summary>ItemStand.RPC_RequestOwn.</summary>
        private void RPC_RequestOwn(long sender)
        {
            if (!_nview.IsOwner()) return;
            ZDOMan.instance.ForceSendZDO(sender, _nview.GetZDO().m_uid);
            _nview.GetZDO().SetOwner(sender);
        }

        // ---- Player actions (through RackSlot, one alcove at a time) ----

        private bool WardAllowsEdit() => PrivateArea.CheckAccess(transform.position, 0f, true, false);

        /// <summary>Why this trophy cannot go in this slot now, as a localisation key, or null if it can.</summary>
        private string PlaceBlockedReason(int slot, string prefab)
        {
            if (HasTrophy(slot)) return "$xai_holder_full";
            var beacon = LinkedBeacon;
            if (beacon == null) return "$xai_holder_no_beacon";
            return beacon.RackInsertBlockedReason(prefab, this, slot);
        }

        /// <summary>Using a trophy on an alcove places it there (ItemStand.UseItem); any other item takes the alcove's
        /// trophy back, as empty hands do.</summary>
        public bool UseItemOn(int slot, Humanoid user, ItemDrop.ItemData item)
        {
            var p = user as Player;
            if (p == null || item == null || item.m_dropPrefab == null || !IsValid) return false;
            var prefab = item.m_dropPrefab.name;
            if (DiscountRules.KindOf(prefab) == TrophyKind.None)
                return HasTrophy(slot) && InteractWith(slot, user, false, false);
            if (!WardAllowsEdit()) return true;
            var blocked = PlaceBlockedReason(slot, prefab);
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return true;
            }
            if (_nview.IsOwner()) { PlaceNow(p, slot, item); return true; }
            Queue(slot, item, false);
            return true;
        }

        /// <summary>Use on a filled alcove takes its trophy back.</summary>
        public bool InteractWith(int slot, Humanoid user, bool hold, bool alt)
        {
            var p = user as Player;
            if (hold || p == null || !IsValid) return false;
            if (!HasTrophy(slot))
            {
                p.Message(MessageHud.MessageType.Center, "$xai_rack_empty_hint");
                return true;
            }
            if (!WardAllowsEdit()) return true;
            if (!RoomFor(p, Trophy(slot)))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return true;
            }
            if (_nview.IsOwner()) { TakeNow(p, slot); return true; }
            Queue(slot, null, true);
            return true;
        }

        private void Queue(int slot, ItemDrop.ItemData item, bool take)
        {
            _nview.InvokeRPC(RpcRequestOwn);
            _queuedSlot = slot;
            _queuedItem = item;
            _queuedTake = take;
            _queuedAt = Time.time;
            CancelInvoke(nameof(UpdateQueued));
            InvokeRepeating(nameof(UpdateQueued), 0f, 0.1f);
        }

        /// <summary>ItemStand.UpdateAttach: act once this client owns the ZDO; give up after QueueTimeout.</summary>
        private void UpdateQueued()
        {
            if (_queuedSlot < 0) { CancelInvoke(nameof(UpdateQueued)); return; }
            var player = Player.m_localPlayer;
            if (!_nview.IsOwner())
            {
                if (Time.time - _queuedAt < QueueTimeout) return;
                CancelInvoke(nameof(UpdateQueued));
                ClearQueue();
                if (player != null) player.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return;
            }
            CancelInvoke(nameof(UpdateQueued));
            int slot = _queuedSlot;
            var item = _queuedItem;
            bool take = _queuedTake;
            ClearQueue();
            if (player == null) return;
            if (take) TakeNow(player, slot);
            else if (item != null && player.GetInventory().ContainsItem(item)) PlaceNow(player, slot, item);
        }

        private void ClearQueue()
        {
            _queuedSlot = -1;
            _queuedItem = null;
            _queuedTake = false;
        }

        /// <summary>As the owner: check again against the current state, then move the trophy into the alcove.</summary>
        private void PlaceNow(Player p, int slot, ItemDrop.ItemData item)
        {
            var prefab = item.m_dropPrefab.name;
            var blocked = PlaceBlockedReason(slot, prefab);
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return;
            }
            SetTrophy(slot, prefab);
            p.GetInventory().RemoveOneItem(item);
        }

        private void TakeNow(Player p, int slot)
        {
            var prefab = Trophy(slot);
            if (prefab.Length == 0) return;
            var drop = DiscountRules.ItemDropFor(prefab);
            if (drop == null || !p.GetInventory().CanAddItem(drop.gameObject, 1))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return;
            }
            SetTrophy(slot, "");
            p.GetInventory().AddItem(drop.gameObject, 1);
        }

        private static bool RoomFor(Player p, string prefab)
        {
            var drop = DiscountRules.ItemDropFor(prefab);
            return drop != null && p.GetInventory().CanAddItem(drop.gameObject, 1);
        }

        // ---- Hover ----

        /// <summary>The link line shared by the rack body and its alcoves: unlinked, linked, and whether the rack adds
        /// a level (past MaxLevel it does not).</summary>
        private void AddLinkLines(List<string> lines, BeaconController beacon)
        {
            if (beacon == null) { lines.Add("<color=#ff9080>$xai_holder_unlinked</color>"); return; }
            lines.Add("$xai_holder_linked");
            if (!beacon.RackAddsLevel(this)) lines.Add("<color=#c8b89a>$xai_rack_no_level</color>");
        }

        /// <summary>One alcove: the rack, the trophy (or Empty), link and counting state, and what Use does.</summary>
        public string SlotHoverText(int slot)
        {
            if (!IsValid) return "";
            var lines = new List<string> { GetHoverName() };
            var trophy = Trophy(slot);
            lines.Add(trophy.Length > 0 ? DiscountRules.ItemDisplayName(trophy) ?? trophy : "$xai_rack_empty");
            var beacon = LinkedBeacon;
            if (trophy.Length > 0 && beacon != null && !beacon.CountsRackSlot(this, slot))
                lines.Add("<color=#ff9080>$xai_rack_not_counted</color>");
            AddLinkLines(lines, beacon);
            lines.Add(trophy.Length > 0
                ? "[<color=yellow><b>$KEY_Use</b></color>] $xai_rack_take"
                : "[<color=yellow><b>1-8</b></color>] $xai_rack_place");
            return Localization.instance.Localize(string.Join("\n", lines));
        }

        /// <summary>The rack's body (between the alcoves): name, how full, link state, and where to aim.</summary>
        public string GetHoverText()
        {
            if (!IsValid) return "";
            var lines = new List<string> { $"{GetHoverName()}  {Filled}/{SlotCount}" };
            AddLinkLines(lines, LinkedBeacon);
            lines.Add("$xai_rack_hint");
            return Localization.instance.Localize(string.Join("\n", lines));
        }

        public string GetHoverName() => Localization.instance.Localize(_piece != null ? _piece.m_name : "$piece_xai_mobrack_column");

        public float GetHoverOffset() => 0f;

        /// <summary>Use on the body, between alcoves: say where to aim.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p)) return false;
            p.Message(MessageHud.MessageType.Center, "$xai_rack_hint");
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            if (user is Player p && item != null && DiscountRules.KindOf(item.m_dropPrefab != null ? item.m_dropPrefab.name : "") != TrophyKind.None)
            {
                p.Message(MessageHud.MessageType.Center, "$xai_rack_hint");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One alcove of a <see cref="MobRack"/>, on its <c>slot_N</c> child (added at registration). The alcove's collider
    /// lives on the same object, so the player's hover finds this component before the rack's.
    /// </summary>
    public class RackSlot : MonoBehaviour, Interactable, Hoverable
    {
        public int m_index;
        private MobRack _rack;

        private MobRack Rack => _rack != null ? _rack : (_rack = GetComponentInParent<MobRack>());

        public bool Interact(Humanoid user, bool hold, bool alt) => Rack != null && Rack.InteractWith(m_index, user, hold, alt);
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => Rack != null && Rack.UseItemOn(m_index, user, item);
        public string GetHoverText() => Rack != null ? Rack.SlotHoverText(m_index) : "";
        public string GetHoverName() => Rack != null ? Rack.GetHoverName() : "";
        public float GetHoverOffset() => 0f;
    }
}
