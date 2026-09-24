using System.Collections.Generic;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// A boss trophy holder: an add-on to a Build Beacon that holds one boss trophy.
    ///
    /// It is a vanilla station extension. The piece carries StationExtension pointing at the beacon's CraftingStation,
    /// so placement needs a beacon within range, the yellow connection thread shows while placing and while the
    /// beacon panel is open, and the holder links to the closest beacon in range
    /// (StationExtension.FindClosestStationInRange). The beacon counts the linked holders' trophies with its own rules:
    /// each boss once, up to BossSlots.
    ///
    /// The trophy lives on the holder's ZDO. Changes follow ItemStand on Valheim 1.0: the ZDO owner writes; another
    /// player asks the owner for ownership (RPC_RequestOwn) and applies the queued action once it arrives. Edits are
    /// ward-protected like the beacon's. Destroying the holder drops its trophy.
    /// </summary>
    public class BossHolder : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyTrophy = "xai_holder_trophy"; // item prefab name, "" when empty
        private const string RpcRequestOwn = "xai_holder_request_own";
        private const float QueueTimeout = 2f;
        private const float DropHeight = 1f;
        /// <summary>Child marking where the trophy's back rests, +Z out (the beacon alcoves' convention).</summary>
        public const string AttachPoint = "attach_trophy";

        private static readonly List<BossHolder> s_all = new List<BossHolder>();
        /// <summary>Loaded, networked holders.</summary>
        public static IReadOnlyList<BossHolder> All => s_all;

        private ZNetView _nview;
        private StationExtension _extension;
        private Piece _piece;
        private ItemDrop.ItemData _queuedItem; // trophy to place once this client owns the ZDO
        private bool _queuedTake;              // take the trophy back once this client owns the ZDO
        private float _queuedAt;
        private GameObject _visual;            // the trophy shown on the hook
        private string _shown = "";

        public bool IsValid => _nview != null && _nview.IsValid();
        public string Trophy => IsValid ? _nview.GetZDO().GetString(KeyTrophy, "") : "";
        public bool HasTrophy => Trophy.Length > 0;

        /// <summary>The beacon this holder belongs to: the closest one within the extension's range, as vanilla picks.</summary>
        public BeaconController LinkedBeacon
        {
            get
            {
                var station = _extension != null ? _extension.FindClosestStationInRange(transform.position) : null;
                return station != null ? station.GetComponent<BeaconController>() : null;
            }
        }

        /// <summary>The link range, from the synced HolderRange setting. Read live: a client gets the server's value after
        /// the pieces were registered.</summary>
        public static float Range => BuildBeaconPlugin.Cfg.HolderRange.Value;

        /// <summary>Give every loaded holder the current range. Their beacons recount within a second.</summary>
        public static void ApplyRange()
        {
            foreach (var h in s_all)
                if (h._extension != null) h._extension.m_maxStationDistance = Range;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _extension = GetComponent<StationExtension>();
            if (_extension != null) _extension.m_maxStationDistance = Range; // the placement ghost too
            _piece = GetComponent<Piece>();
            if (!IsValid) return; // placement ghost
            s_all.Add(this);
            var wnt = GetComponent<WearNTear>();
            if (wnt != null) wnt.m_onDestroyed += OnDestroyed;
            _nview.Register(RpcRequestOwn, RPC_RequestOwn);
            InvokeRepeating(nameof(RefreshVisual), 0f, 1f); // picks up other players' changes
            LinkedBeacon?.Resync(); // a new holder raises its beacon's level at once
        }

        /// <summary>Show the held trophy on the hook at full size, mounted the way the beacon mounts trophies.</summary>
        private void RefreshVisual()
        {
            if (!IsValid) return;
            var trophy = Trophy;
            if (trophy == _shown && (trophy.Length == 0 || _visual != null)) return;
            if (_visual != null) Destroy(_visual);
            _visual = null;
            _shown = trophy;
            var mount = transform.Find(AttachPoint);
            if (mount != null && trophy.Length > 0) _visual = BeaconTrophyDisplay.Build(mount, trophy, TrophyPlacement.Mounted);
        }

        private void OnDestroy()
        {
            if (!s_all.Remove(this)) return;
            var beacon = LinkedBeacon;
            if (beacon != null) beacon.Invoke(nameof(BeaconController.Resync), 0f); // next frame, once this holder is gone
        }

        /// <summary>Only the owner drops, so there is one drop.</summary>
        private void OnDestroyed()
        {
            if (!IsValid || !_nview.IsOwner()) return;
            var trophy = Trophy;
            if (trophy.Length == 0) return;
            BeaconController.SpawnItems(trophy, 1, transform.position + Vector3.up * DropHeight);
            SetTrophy("");
            BuildBeaconPlugin.Log.LogInfo($"Boss holder destroyed at {transform.position:F1}: dropped {trophy}");
        }

        private void SetTrophy(string prefab)
        {
            _nview.GetZDO().Set(KeyTrophy, prefab ?? "");
            RefreshVisual();
            LinkedBeacon?.Resync(); // the beacon recounts at once here; other clients within a second
        }

        /// <summary>ItemStand.RPC_RequestOwn.</summary>
        private void RPC_RequestOwn(long sender)
        {
            if (!_nview.IsOwner()) return;
            ZDOMan.instance.ForceSendZDO(sender, _nview.GetZDO().m_uid);
            _nview.GetZDO().SetOwner(sender);
        }

        // ---- Player actions ----

        private bool WardAllowsEdit() => PrivateArea.CheckAccess(transform.position, 0f, true, false);

        /// <summary>Why this boss trophy cannot go on this holder now, as a localisation key, or null if it can.</summary>
        private string PlaceBlockedReason(string prefab)
        {
            if (DiscountRules.KindOf(prefab) != TrophyKind.Boss) return "$xai_holder_boss_only";
            if (HasTrophy) return "$xai_holder_full";
            var beacon = LinkedBeacon;
            if (beacon == null) return "$xai_holder_no_beacon";
            return beacon.HolderInsertBlockedReason(prefab, this);
        }

        /// <summary>Using a trophy on the holder places it (ItemStand.UseItem).</summary>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            var p = user as Player;
            if (p == null || item == null || item.m_dropPrefab == null || !IsValid) return false;
            var prefab = item.m_dropPrefab.name;
            if (DiscountRules.KindOf(prefab) == TrophyKind.None) return false; // vanilla's "can't use that here"
            if (!WardAllowsEdit()) return true;
            var blocked = PlaceBlockedReason(prefab);
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return true;
            }

            if (_nview.IsOwner()) { PlaceNow(p, item); return true; }
            Queue(item, false);
            return true;
        }

        /// <summary>Use takes the trophy back.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            var p = user as Player;
            if (hold || p == null || !IsValid) return false;
            if (!HasTrophy)
            {
                p.Message(MessageHud.MessageType.Center, "$xai_holder_empty_hint");
                return true;
            }
            if (!WardAllowsEdit()) return true;
            if (!RoomFor(p, Trophy))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return true;
            }

            if (_nview.IsOwner()) { TakeNow(p); return true; }
            Queue(null, true);
            return true;
        }

        private void Queue(ItemDrop.ItemData item, bool take)
        {
            _nview.InvokeRPC(RpcRequestOwn);
            _queuedItem = item;
            _queuedTake = take;
            _queuedAt = Time.time;
            CancelInvoke(nameof(UpdateQueued));
            InvokeRepeating(nameof(UpdateQueued), 0f, 0.1f);
        }

        /// <summary>ItemStand.UpdateAttach: act once this client owns the ZDO; give up after QueueTimeout.</summary>
        private void UpdateQueued()
        {
            if (_queuedItem == null && !_queuedTake) { CancelInvoke(nameof(UpdateQueued)); return; }
            var player = Player.m_localPlayer;
            if (!_nview.IsOwner())
            {
                if (Time.time - _queuedAt < QueueTimeout) return;
                CancelInvoke(nameof(UpdateQueued));
                _queuedItem = null;
                _queuedTake = false;
                if (player != null) player.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return;
            }
            CancelInvoke(nameof(UpdateQueued));
            var item = _queuedItem;
            bool take = _queuedTake;
            _queuedItem = null;
            _queuedTake = false;
            if (player == null) return;
            if (take) TakeNow(player);
            else if (item != null && player.GetInventory().ContainsItem(item)) PlaceNow(player, item);
        }

        /// <summary>As the owner: check again against the current state, then move the trophy onto the holder.</summary>
        private void PlaceNow(Player p, ItemDrop.ItemData item)
        {
            var prefab = item.m_dropPrefab.name;
            var blocked = PlaceBlockedReason(prefab);
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return;
            }
            SetTrophy(prefab);
            p.GetInventory().RemoveOneItem(item);
        }

        private void TakeNow(Player p)
        {
            var prefab = Trophy;
            if (prefab.Length == 0) return;
            var drop = DiscountRules.ItemDropFor(prefab);
            if (drop == null || !p.GetInventory().CanAddItem(drop.gameObject, 1))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return;
            }
            SetTrophy("");
            p.GetInventory().AddItem(drop.gameObject, 1);
        }

        private static bool RoomFor(Player p, string prefab)
        {
            var drop = DiscountRules.ItemDropFor(prefab);
            return drop != null && p.GetInventory().CanAddItem(drop.gameObject, 1);
        }

        // ---- Hover ----

        public string GetHoverText()
        {
            if (!IsValid) return "";
            var lines = new List<string> { GetHoverName() };
            var trophy = Trophy;
            lines.Add(trophy.Length > 0 ? DiscountRules.ItemDisplayName(trophy) ?? trophy : "$xai_holder_empty");

            var beacon = LinkedBeacon;
            if (beacon == null) lines.Add("<color=#ff9080>$xai_holder_unlinked</color>");
            else if (trophy.Length > 0 && !beacon.CountsHolder(this)) lines.Add("<color=#ff9080>$xai_holder_not_counted</color>");
            else lines.Add("$xai_holder_linked");

            lines.Add(trophy.Length > 0
                ? "[<color=yellow><b>$KEY_Use</b></color>] $xai_holder_take"
                : "[<color=yellow><b>1-8</b></color>] $xai_holder_place");
            return Localization.instance.Localize(string.Join("\n", lines));
        }

        public string GetHoverName() => Localization.instance.Localize(_piece != null ? _piece.m_name : "$piece_xai_bossholder_pillar");

        public float GetHoverOffset() => 0f;
    }
}
