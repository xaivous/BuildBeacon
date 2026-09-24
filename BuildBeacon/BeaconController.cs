using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// Per-beacon state. The beacon's own slots live on its ZDO: mob trophies (small discounts), plus any boss trophies
    /// slotted before boss holders existed, which keep counting but cannot be added any more.
    /// Boss trophies now sit on BossHolder add-ons linked to the beacon as a vanilla crafting station (the beacon
    /// carries a CraftingStation; holders a StationExtension). The effective set merges both: each boss counts once,
    /// up to BossSlots, in progression order; bosses make materials free. The beacon's own creature slots are fixed
    /// (BeaconMobSlots).
    /// Level is 1 + the holders linked here (with or without a trophy), up to MaxLevel; the radius grows with it.
    /// Creature trophy racks (MobRack) link the same way: each adds a level (sharing MaxLevel with the holders) and four
    /// creature slots of its own. Their trophies join the effective set; a non-stackable trophy counts once across the
    /// beacon's network (its own slots and every linked rack), in a stable order: own slots, then racks by distance.
    /// </summary>
    public class BeaconController : MonoBehaviour, Interactable, Hoverable
    {
        private const string KeyTrophies = "xai_beacon_trophies"; // ZPackage: int count, then (string prefab, int n)*
        private const float InteractRange = 6f;

        // Slot changes follow vanilla ownership (Valheim 1.0): only the ZDO owner writes, and a player becomes the
        // owner by asking the current one. The panel uses Container's open handshake (RPC_RequestOpen: refuse while
        // the owner has it open, else ForceSendZDO + SetOwner + respond). Using a trophy on the beacon uses
        // ItemStand's (RPC_RequestOwn, then apply the queued item once this client owns the ZDO).
        private const string RpcRequestOpen = "xai_beacon_request_open";
        private const string RpcOpenResponse = "xai_beacon_open_response";
        private const string RpcRequestOwn = "xai_beacon_request_own";
        /// <summary>How long a queued trophy waits for ownership before giving up (the owner may be in the panel).</summary>
        private const float QueueTimeout = 2f;
        private const float DropHeight = 1.2f; // around the alcoves, so drops fall clear of the plinth

        // Visuals inherited from the guard_stone clone; assigned by BuildBeaconPlugin.StripWardBehaviour.
        public CircleProjector m_areaMarker;
        public GameObject m_activeEffect;

        // What this beacon's own slots take, set on the prefab at registration: creature trophies (the Build Beacon, its
        // slot count from BeaconMobSlots) or boss trophies (the Great Beacon, m_ownSlotCount of them). A Great Beacon's own
        // bosses also raise its level, and its radius follows the Great settings.
        public TrophyKind m_ownSlotKind = TrophyKind.Mob;
        public int m_ownSlotCount;

        /// <summary>The Great Beacon: its own slots hold boss trophies.</summary>
        public bool IsGreat => m_ownSlotKind == TrophyKind.Boss;

        private ZNetView _nview;
        private BeaconTrophyDisplay _display;
        private bool _inUse;                    // this client has the panel open (Container.m_inUse)
        private ItemDrop.ItemData _queuedItem;  // trophy used on the beacon, waiting for ownership (ItemStand.m_queuedItem)
        private float _queuedAt;
        private readonly Dictionary<string, int> _trophies = new Dictionary<string, int>();  // this beacon's ZDO
        private readonly Dictionary<string, int> _effective = new Dictionary<string, int>(); // what counts: own + holders
        private readonly List<(string prefab, BossHolder holder)> _bosses = new List<(string prefab, BossHolder holder)>();
        private readonly List<MobRack> _racks = new List<MobRack>();   // linked racks, nearest first
        private readonly List<(MobRack rack, int slot, string prefab, bool counted)> _rackTrophies =
            new List<(MobRack rack, int slot, string prefab, bool counted)>();

        public bool IsValid => _nview != null && _nview.IsValid();
        /// <summary>The trophies that count: own mob trophies, and each counted boss once. Discounts, radius and the
        /// Discounts tab use this.</summary>
        public IReadOnlyDictionary<string, int> Trophies => _effective;
        /// <summary>The counted bosses in order, each with the holder it sits on, or null for a legacy boss slotted
        /// in the beacon itself.</summary>
        public IReadOnlyList<(string prefab, BossHolder holder)> BossEntries => _bosses;

        public int BossCount => _bosses.Count;
        public int MobCount => _trophies.Where(kv => !DiscountRules.IsBossTrophy(kv.Key)).Sum(kv => kv.Value);
        public int TrophyCount => _effective.Values.Sum();

        /// <summary>Boss holders linked to this beacon (vanilla's closest-station-in-range), with or without a trophy.</summary>
        public int HolderCount { get; private set; }
        /// <summary>Creature trophy racks linked to this beacon, with or without trophies.</summary>
        public int RackCount => _racks.Count;
        /// <summary>Every trophy on a linked rack, in counting order, and whether it counts.</summary>
        public IReadOnlyList<(MobRack rack, int slot, string prefab, bool counted)> RackTrophies => _rackTrophies;
        /// <summary>Creature slots on linked racks, four per rack; separate from the beacon's own (MobSlots).</summary>
        public int RackSlots => RackCount * MobRack.SlotCount;
        /// <summary>Creature trophies on linked racks; separate from the beacon's own (MobCount).</summary>
        public int RackTrophyCount => _rackTrophies.Count;

        /// <summary>The summary line's creature count, beacon and racks together ("5/12"). The Trophies tab counts the
        /// beacon's own slots and the racks' apart, since only the racks' alcoves take the racks' trophies.</summary>
        public string MobSlotsText => $"{MobCount + RackTrophyCount}/{MobSlots + RackSlots}";
        /// <summary>1 + the Great Beacon's own counted bosses + linked holders + linked racks, capped at MaxLevel.</summary>
        public int Level => DiscountRules.LevelFor(OwnBossLevels + HolderCount + RackCount);
        public int MaxLevel => BuildBeaconPlugin.Cfg.MaxLevel.Value;
        public float Radius => IsGreat ? DiscountRules.GreatRadiusFor(Level) : DiscountRules.RadiusFor(Level);
        public int BossSlots => BuildBeaconPlugin.Cfg.BossSlots.Value;
        /// <summary>The beacon's own creature slots: fixed (BeaconMobSlots), whatever bosses it counts; none on the Great
        /// Beacon.</summary>
        public int MobSlots => IsGreat ? 0 : BuildBeaconPlugin.Cfg.BeaconMobSlots.Value;
        /// <summary>The Great Beacon's own boss slots (its alcoves); none on the Build Beacon.</summary>
        public int OwnBossSlots => IsGreat ? m_ownSlotCount : 0;
        /// <summary>Boss trophies in this beacon's own slots (on the Build Beacon, only ones slotted before holders).</summary>
        public int OwnBossCount => _trophies.Where(kv => DiscountRules.IsBossTrophy(kv.Key)).Sum(kv => kv.Value);
        /// <summary>Levels the Great Beacon's own counted bosses add; they take the levels before holders and racks.</summary>
        private int OwnBossLevels => IsGreat ? _bosses.Count(b => b.holder == null) : 0;

        /// <summary>This beacon's own slotted trophies of one kind, one entry per trophy, in a stable order for the UI.</summary>
        public IEnumerable<string> SlotsOf(TrophyKind kind) =>
            _trophies.Where(kv => DiscountRules.KindOf(kv.Key) == kind || (kind == TrophyKind.Mob && DiscountRules.KindOf(kv.Key) == TrophyKind.None))
                     .OrderBy(kv => kv.Key)
                     .SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value));

        public bool InRangeOf(Player p) =>
            p != null && Vector3.Distance(p.transform.position, transform.position) <= InteractRange;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (!IsValid) return;
            // The Great Beacon's boss trophies: a little under vanilla size, the giants fitted (ShrinkGreatBeaconTrophies).
            _display = new BeaconTrophyDisplay(transform, IsGreat ? BeaconTrophyDisplay.GreatTrophyFit : (Vector2?)null);
            // Same hook vanilla item stands use: WearNTear.Destroy invokes it on the owner for every kind of
            // destruction (hammer removal, damage, collapse), after the build cost refund.
            var wnt = GetComponent<WearNTear>();
            if (wnt != null) wnt.m_onDestroyed += OnDestroyed;
            _nview.Register<long>(RpcRequestOpen, RPC_RequestOpen);
            _nview.Register<bool>(RpcOpenResponse, RPC_OpenResponse);
            _nview.Register(RpcRequestOwn, RPC_RequestOwn);
            _crystal = transform.Find(BuildBeaconPlugin.CrystalPivot);
            if (_crystal != null)
            {
                _crystalRestPos = _crystal.localPosition;
                _crystalRestRot = _crystal.localRotation;
                _bobPhase = Random.value * BobPeriod; // neighbouring beacons do not bob in step
                FindShards();
            }
            LoadFromZdo();
            BeaconRegistry.Register(this);
            InvokeRepeating(nameof(LoadFromZdo), 1f, 1f); // cheap resync for non-owners
        }

        private void OnDestroy() => BeaconRegistry.Unregister(this);

        // Every frame so the ring appears as soon as a build tool is equipped; the level and radius only change once a second.
        private void Update()
        {
            UpdateRing();
            AnimateCrystal();
        }

        /// <summary>The ring shows while ShowRadius is on and the local player holds a build tool.</summary>
        private void UpdateRing()
        {
            if (m_areaMarker == null) return;
            bool show = BuildBeaconPlugin.Cfg.ShowRadius.Value && HoldingBuildTool;
            if (show)
            {
                m_areaMarker.m_radius = Radius;
                m_areaMarker.m_nrOfSegments = Mathf.Max(20, Mathf.RoundToInt(Radius * 2f));
            }
            if (m_areaMarker.gameObject.activeSelf != show) m_areaMarker.gameObject.SetActive(show);
        }

        private static int s_toolFrame = -1;
        private static bool s_holdingTool;

        /// <summary>
        /// Whether the local player holds a build tool: any item with a build menu (hammer, hoe, cultivator, and modded
        /// tools like them), the tools the beacon's discounts apply to. Worked out once per frame for all beacons.
        /// </summary>
        public static bool HoldingBuildTool
        {
            get
            {
                if (s_toolFrame == Time.frameCount) return s_holdingTool;
                s_toolFrame = Time.frameCount;
                var player = Player.m_localPlayer;
                var item = player != null ? player.GetRightItem() : null;
                s_holdingTool = item != null && item.m_shared.m_buildPieces != null;
                return s_holdingTool;
            }
        }

        /// <summary>Drop every slotted trophy on the ground when the beacon is destroyed. Only the owner drops, as
        /// with item stands, so there is one set of drops.</summary>
        private void OnDestroyed()
        {
            if (!IsValid || !_nview.IsOwner()) return;
            LoadFromZdo(); // the newest slot state, in case another client changed it within the last second
            int dropped = 0;
            foreach (var kv in _trophies.ToList())
            {
                dropped += DropTrophy(kv.Key, kv.Value);
                _trophies.Remove(kv.Key);
            }
            if (dropped > 0)
            {
                SaveToZdo(); // empty, so a second destroy call cannot drop the trophies again
                BuildBeaconPlugin.Log.LogInfo($"Beacon destroyed at {transform.position:F1}: dropped {dropped} trophies");
            }
        }

        private int DropTrophy(string prefab, int count) => SpawnItems(prefab, count, transform.position + Vector3.up * DropHeight);

        /// <summary>
        /// Spawn <paramref name="count"/> of an item as ground items near <paramref name="at"/>, split into stacks no
        /// larger than the item allows. Mirrors Piece.DropResources on 1.0 (Instantiate, SetStack,
        /// ItemDrop.OnCreateNew) and deliberately skips Game.ScaleDrops, so the world's resource rate modifier never
        /// changes how many trophies come back. Returns how many were spawned.
        /// </summary>
        public static int SpawnItems(string prefab, int count, Vector3 at)
        {
            var drop = DiscountRules.ItemDropFor(prefab);
            if (drop == null)
            {
                BuildBeaconPlugin.Log.LogWarning($"No item prefab \"{prefab}\"; {count} lost");
                return 0;
            }
            int maxStack = Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int left = count;
            while (left > 0)
            {
                int stack = Mathf.Min(left, maxStack);
                var offset = Random.insideUnitSphere * 0.3f;
                offset.y = Mathf.Abs(offset.y);
                var go = Instantiate(drop.gameObject, at + offset, Quaternion.identity);
                var item = go.GetComponent<ItemDrop>();
                item.SetStack(stack);
                ItemDrop.OnCreateNew(item, false);
                left -= stack;
            }
            return count;
        }

        /// <summary>Re-read the ZDO and recount linked holders now, rather than at the next one-second poll.</summary>
        public void Resync() => LoadFromZdo();

        /// <summary>
        /// Rebuild what counts: own mob trophies, then bosses from this beacon's legacy slots and from every loaded
        /// holder that links here (vanilla's closest-station-in-range). Each boss counts once, up to BossSlots, in
        /// progression order, legacy slots first on ties.
        /// </summary>
        private void RebuildEffective()
        {
            _effective.Clear();
            _bosses.Clear();
            foreach (var kv in _trophies)
                if (kv.Value > 0 && !DiscountRules.IsBossTrophy(kv.Key)) _effective[kv.Key] = kv.Value;

            var candidates = new List<(string prefab, BossHolder holder)>();
            foreach (var kv in _trophies)
                if (kv.Value > 0 && DiscountRules.IsBossTrophy(kv.Key)) candidates.Add((kv.Key, null));
            int holders = 0;
            foreach (var h in BossHolder.All)
            {
                if (h == null || !h.IsValid || h.LinkedBeacon != this) continue;
                holders++;
                if (h.HasTrophy) candidates.Add((h.Trophy, h));
            }
            HolderCount = holders;
            RebuildRacks();

            foreach (var c in candidates.OrderBy(c => DiscountRules.BossOrder(c.prefab)).ThenBy(c => c.holder == null ? 0 : 1))
            {
                if (_bosses.Count >= BossSlots) break;
                if (!DiscountRules.IsBossTrophy(c.prefab) || _bosses.Any(b => b.prefab == c.prefab)) continue;
                _bosses.Add(c);
                _effective[c.prefab] = 1;
            }
        }

        /// <summary>
        /// Linked racks, nearest first, and their trophies in counting order. A creature trophy on a rack counts unless
        /// it is non-stackable and already counts elsewhere in the network (the beacon's own slots, or an earlier rack
        /// slot); counted ones join the effective set, one per copy (MobLevel counts a non-stackable trophy once).
        /// </summary>
        private void RebuildRacks()
        {
            _racks.Clear();
            _rackTrophies.Clear();
            foreach (var r in MobRack.All)
                if (r != null && r.IsValid && r.LinkedBeacon == this) _racks.Add(r);
            var here = transform.position;
            _racks.Sort((a, b) => (a.transform.position - here).sqrMagnitude.CompareTo((b.transform.position - here).sqrMagnitude));
            foreach (var r in _racks)
                for (int i = 0; i < MobRack.SlotCount; i++)
                {
                    var prefab = r.Trophy(i);
                    if (prefab.Length == 0) continue;
                    bool counted = DiscountRules.KindOf(prefab) == TrophyKind.Mob
                                   && (DiscountRules.IsStackable(prefab) || !_effective.TryGetValue(prefab, out var n) || n <= 0);
                    if (counted)
                    {
                        _effective.TryGetValue(prefab, out var c);
                        _effective[prefab] = c + 1;
                    }
                    _rackTrophies.Add((r, i, prefab, counted));
                }
        }

        /// <summary>Does this rack slot's trophy count towards the discounts?</summary>
        public bool CountsRackSlot(MobRack rack, int slot)
        {
            foreach (var t in _rackTrophies)
                if (t.rack == rack && t.slot == slot) return t.counted;
            return false;
        }

        /// <summary>Does this rack add a level? The Great Beacon's own bosses, holders and racks share MaxLevel - 1 extra
        /// levels, in that order, racks nearest first.</summary>
        public bool RackAddsLevel(MobRack rack)
        {
            int room = MaxLevel - 1 - OwnBossLevels - HolderCount;
            int index = _racks.IndexOf(rack);
            return index >= 0 && index < room;
        }

        /// <summary>Is this trophy anywhere in the network (own slots or a linked rack), apart from one rack slot?</summary>
        private bool NetworkHas(string prefab, MobRack exceptRack = null, int exceptSlot = -1)
        {
            if (_trophies.TryGetValue(prefab, out var own) && own > 0) return true;
            foreach (var t in _rackTrophies)
                if (t.prefab == prefab && !(t.rack == exceptRack && t.slot == exceptSlot)) return true;
            return false;
        }

        /// <summary>Why a trophy cannot go in <paramref name="slot"/> of <paramref name="rack"/> (linked to this beacon),
        /// as a localisation key, or null if it can: creature trophies only, and a non-stackable trophy once per
        /// network.</summary>
        public string RackInsertBlockedReason(string prefab, MobRack rack, int slot)
        {
            RebuildEffective();
            switch (DiscountRules.KindOf(prefab))
            {
                case TrophyKind.Boss:
                    return "$xai_rack_mob_only";
                case TrophyKind.Mob:
                    return !DiscountRules.IsStackable(prefab) && NetworkHas(prefab, rack, slot) ? "$xai_beacon_mob_duplicate" : null;
                default:
                    return "$xai_beacon_not_a_trophy";
            }
        }

        /// <summary>Is this holder's trophy one of the bosses this beacon counts?</summary>
        public bool CountsHolder(BossHolder holder) => _bosses.Any(b => b.holder == holder);

        /// <summary>Why a boss trophy cannot go on <paramref name="holder"/> (linked to this beacon), as a localisation
        /// key, or null if it can: each boss once per beacon, up to BossSlots.</summary>
        public string HolderInsertBlockedReason(string prefab, BossHolder holder)
        {
            RebuildEffective();
            var others = _bosses.Where(b => b.holder != holder || holder == null).ToList();
            if (others.Any(b => b.prefab == prefab)) return "$xai_beacon_boss_duplicate";
            if (others.Count >= BossSlots) return "$xai_beacon_boss_full";
            return null;
        }

        /// <summary>While the panel is open: keep the vanilla connection threads to the holders showing, as
        /// InventoryGui does for a crafting station in use.</summary>
        public void PokeStation()
        {
            if (_station == null) _station = GetComponent<CraftingStation>();
            if (_station != null) _station.PokeInUse();
        }
        private CraftingStation _station;

        private void LoadFromZdo()
        {
            _trophies.Clear();
            if (ZdoUtil.TryGetPackage(_nview, KeyTrophies, out var pkg))
            {
                int n = pkg.ReadInt();
                for (int i = 0; i < n; i++) _trophies[pkg.ReadString()] = pkg.ReadInt();
            }
            RebuildEffective();
            UpdateVisuals();
        }

        private void SaveToZdo()
        {
            ZdoUtil.SetPackage(_nview, KeyTrophies, pkg =>
            {
                pkg.Write(_trophies.Count);
                foreach (var kv in _trophies) { pkg.Write(kv.Key); pkg.Write(kv.Value); }
            });
            RebuildEffective();
            UpdateVisuals();
        }

        /// <summary>Ring shows the discount radius; glow shows the beacon is doing something; alcoves show trophies.</summary>
        private void UpdateVisuals()
        {
            UpdateRing();

            bool lit = TrophyCount > 0;
            _lit = lit;
            if (m_activeEffect != null && m_activeEffect.activeSelf != lit) m_activeEffect.SetActive(lit);
            SetCoreLit(lit);

            _display?.Refresh(_trophies); // the alcoves show what sits in the beacon itself
        }

        // ---- The crystal's motion ----
        // While lit, the crystal's pivot (the crystal, its core and the wisp light under it) turns slowly and bobs; it
        // eases in when the beacon lights and eases back to rest when it goes out. Visual only, per client: nothing is
        // synced, so players see different phases.

        private const float SpinDegreesPerSecond = 18f;  // one turn in 20 s
        private const float BobAmplitude = 0.02f;        // +-2 cm, 4 cm top to bottom
        private const float BobPeriod = 4f;              // seconds per bob
        private const float MotionEaseSeconds = 1.5f;    // from rest to full motion, and back

        private Transform _crystal;
        private Vector3 _crystalRestPos;
        private Quaternion _crystalRestRot;
        private float _bobPhase, _motion, _spin;
        private bool _lit;

        private void AnimateCrystal()
        {
            if (_crystal == null) return;
            float target = _lit ? 1f : 0f;
            if (_motion == 0f && target == 0f) return; // at rest
            _motion = Mathf.MoveTowards(_motion, target, Time.deltaTime / MotionEaseSeconds);
            float ease = Mathf.SmoothStep(0f, 1f, _motion);
            _spin = (_spin + SpinDegreesPerSecond * ease * Time.deltaTime) % 360f;
            float bob = Mathf.Sin((Time.time + _bobPhase) * (2f * Mathf.PI / BobPeriod)) * BobAmplitude * ease;
            _crystal.localPosition = _crystalRestPos + Vector3.up * bob;
            _crystal.localRotation = _crystalRestRot * Quaternion.Euler(0f, _spin, 0f);
            AnimateShards(ease);
        }

        // ---- The Great Beacon's shards ----
        // Satellite crystals under the pivot (children of the crystal model named "..._shard_N"): the pivot carries them
        // round the main crystal and up and down with it, and each also spins about its own long axis and bobs along it,
        // with its own speed, direction, period and phase, so together they move in an intricate, never-repeating way.

        private const string ShardMarker = "_shard_";
        private const float ShardBobAmplitude = 0.05f;

        private sealed class Shard
        {
            public Transform T;
            public Vector3 RestPos, AxisLocal;
            public Quaternion RestRot;
            public float Speed, Period, Phase, Angle;
        }

        private readonly List<Shard> _shards = new List<Shard>();

        private void FindShards()
        {
            foreach (var t in _crystal.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.IndexOf(ShardMarker, System.StringComparison.Ordinal) < 0) continue;
                // The long axis of the stretched shard, in its own space, from its mesh.
                var mf = t.GetComponent<MeshFilter>();
                var size = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.size : Vector3.up;
                var axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
                int i = _shards.Count;
                _shards.Add(new Shard
                {
                    T = t, RestPos = t.localPosition, RestRot = t.localRotation, AxisLocal = axis,
                    Speed = (40f + 11f * i) * (i % 2 == 0 ? 1f : -1f),   // degrees a second, alternating direction
                    Period = 2.5f + 0.37f * i,                          // seconds per bob
                    Phase = 1.7f * i + Random.value,
                });
            }
        }

        private void AnimateShards(float ease)
        {
            foreach (var s in _shards)
            {
                if (s.T == null) continue;
                s.Angle = (s.Angle + s.Speed * ease * Time.deltaTime) % 360f;
                float bob = Mathf.Sin((Time.time + s.Phase) * (2f * Mathf.PI / s.Period)) * ShardBobAmplitude * ease;
                s.T.localRotation = s.RestRot * Quaternion.AngleAxis(s.Angle, s.AxisLocal);
                s.T.localPosition = s.RestPos + s.RestRot * s.AxisLocal * bob;
            }
        }

        // ---- The crystal's core ----
        // The glowing core inside the crystal (the BeaconCore material, the wisp torch orb's look) shows only while the
        // beacon is lit, like the wisp light. The material is shared by every beacon, so the change is a property
        // block on this renderer's core submesh only: unlit, the core is fully transparent and does not emit; lit, an
        // empty block leaves the material as authored.

        private const string CoreMaterial = "BeaconCore";
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private Renderer _coreRenderer;
        private int _coreIndex = -1;
        private bool _coreSearched;
        private bool? _coreLit;
        private MaterialPropertyBlock _coreBlock;

        private void SetCoreLit(bool lit)
        {
            if (_coreLit == lit) return;
            if (!_coreSearched)
            {
                _coreSearched = true;
                foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && mats[i].name.StartsWith(CoreMaterial))
                        {
                            _coreRenderer = r;
                            _coreIndex = i;
                            break;
                        }
                    if (_coreRenderer != null) break;
                }
                if (_coreRenderer == null) BuildBeaconPlugin.Log.LogWarning($"Beacon: no {CoreMaterial} material on the model; the crystal's core cannot follow the lit state");
            }
            _coreLit = lit;
            if (_coreRenderer == null) return;

            if (_coreBlock == null) _coreBlock = new MaterialPropertyBlock();
            _coreBlock.Clear();
            if (!lit)
            {
                var mat = _coreRenderer.sharedMaterials[_coreIndex];
                var color = mat != null && mat.HasProperty(ColorId) ? mat.GetColor(ColorId) : Color.white;
                color.a = 0f;
                _coreBlock.SetColor(ColorId, color);
                _coreBlock.SetColor(EmissionId, Color.black);
            }
            _coreRenderer.SetPropertyBlock(_coreBlock, _coreIndex);
        }

        // ---- Ownership handshakes ----

        /// <summary>Owner side of opening the panel, as Container.RPC_RequestOpen: refuse while this client has the
        /// panel open for someone else, otherwise send the latest state and hand over ownership.</summary>
        private void RPC_RequestOpen(long uid, long playerID)
        {
            if (!_nview.IsOwner()) return; // the request went to a stale owner; the requester can try again
            if (_inUse && uid != ZNet.GetUID())
            {
                _nview.InvokeRPC(uid, RpcOpenResponse, false);
                return;
            }
            ZDOMan.instance.ForceSendZDO(uid, _nview.GetZDO().m_uid);
            _nview.GetZDO().SetOwner(uid);
            _nview.InvokeRPC(uid, RpcOpenResponse, true);
        }

        private void RPC_OpenResponse(long uid, bool granted)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            if (granted) BeaconUI.Open(this);
            else player.Message(MessageHud.MessageType.Center, "$msg_inuse");
        }

        /// <summary>Owner side of ItemStand.RPC_RequestOwn. Unlike the item stand it keeps ownership while this client
        /// has the panel open, so the queued trophy on the other side times out instead.</summary>
        private void RPC_RequestOwn(long sender)
        {
            if (!_nview.IsOwner() || (_inUse && sender != ZNet.GetUID())) return;
            ZDOMan.instance.ForceSendZDO(sender, _nview.GetZDO().m_uid);
            _nview.GetZDO().SetOwner(sender);
        }

        /// <summary>The panel marks the beacon in use while open; see RPC_RequestOpen.</summary>
        public void SetInUse(bool inUse) => _inUse = inUse;

        // ---- Player actions: only the ZDO owner changes slots ----

        /// <summary>Why a trophy cannot be slotted right now, as a localisation key, or null if it can.</summary>
        public string InsertBlockedReason(string prefab)
        {
            switch (DiscountRules.KindOf(prefab))
            {
                case TrophyKind.Boss:
                    if (!IsGreat) return "$xai_beacon_boss_use_holder"; // the Build Beacon's bosses go on BossHolders
                    // The Great Beacon's alcoves: each boss once, whether it would sit here or on a linked holder.
                    RebuildEffective();
                    var key = DiscountRules.TrophyKey(prefab);
                    if (_bosses.Any(b => DiscountRules.TrophyKey(b.prefab) == key)
                        || _trophies.Any(kv => kv.Value > 0 && DiscountRules.TrophyKey(kv.Key) == key))
                        return "$xai_beacon_boss_duplicate";
                    if (OwnBossCount >= OwnBossSlots) return "$xai_beacon_boss_full";
                    return null;
                case TrophyKind.Mob:
                    if (IsGreat) return "$xai_great_beacon_mob_refused"; // its creature trophies go on racks
                    // A second copy of a trophy that does not stack would add nothing: refuse it rather than waste a slot,
                    // whether the first copy is in the beacon or on a linked rack.
                    if (!DiscountRules.IsStackable(prefab) && NetworkHas(prefab))
                        return "$xai_beacon_mob_duplicate";
                    if (MobCount >= MobSlots) return "$xai_beacon_mob_full";
                    return null;
                default:
                    return "$xai_beacon_not_a_trophy";
            }
        }

        /// <summary>Slot a trophy from the player's inventory. Called from the panel, which this client owns.</summary>
        public bool TryInsertTrophy(Player p, ItemDrop.ItemData item)
        {
            if (p == null || item == null || item.m_dropPrefab == null || !IsValid) return false;
            if (!_nview.IsOwner())
            {
                p.Message(MessageHud.MessageType.Center, "$xai_beacon_busy");
                return false;
            }
            return InsertNow(p, item);
        }

        /// <summary>As the owner: re-check the rules against the ZDO's state, then move the trophy into the beacon.</summary>
        private bool InsertNow(Player p, ItemDrop.ItemData item)
        {
            var prefab = item.m_dropPrefab.name;
            if (DiscountRules.KindOf(prefab) == TrophyKind.None) return false;
            LoadFromZdo();
            var blocked = InsertBlockedReason(prefab);
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return false;
            }

            _trophies.TryGetValue(prefab, out var c);
            _trophies[prefab] = c + 1;
            SaveToZdo();
            p.GetInventory().RemoveOneItem(item);
            return true;
        }

        /// <summary>ItemStand.UpdateAttach: once this client owns the ZDO, slot the queued trophy if the player still
        /// carries it. Gives up after QueueTimeout, when the owner is busy in the panel.</summary>
        private void UpdateQueued()
        {
            if (_queuedItem == null) { CancelInvoke(nameof(UpdateQueued)); return; }
            var player = Player.m_localPlayer;
            if (!_nview.IsOwner())
            {
                if (Time.time - _queuedAt < QueueTimeout) return;
                CancelInvoke(nameof(UpdateQueued));
                _queuedItem = null;
                if (player != null) player.Message(MessageHud.MessageType.Center, "$msg_inuse");
                return;
            }
            CancelInvoke(nameof(UpdateQueued));
            var item = _queuedItem;
            _queuedItem = null;
            if (player != null && player.GetInventory().ContainsItem(item)) InsertNow(player, item);
        }

        /// <summary>Slot one trophy of the given prefab from the player's inventory, if they carry one.</summary>
        public bool TryInsertTrophy(Player p, string prefab)
        {
            var item = p.GetInventory().GetAllItems()
                .FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab);
            return item != null && TryInsertTrophy(p, item);
        }

        /// <summary>Take one slotted trophy back into the player's inventory.</summary>
        public bool TryRemoveTrophy(Player p, string prefab)
        {
            if (!_trophies.TryGetValue(prefab, out var c) || c <= 0) return false;

            var drop = DiscountRules.ItemDropFor(prefab);
            if (drop == null) return false;
            if (!p.GetInventory().CanAddItem(drop.gameObject, 1))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_noroom");
                return false;
            }

            if (!_nview.IsOwner())
            {
                p.Message(MessageHud.MessageType.Center, "$xai_beacon_busy");
                return false;
            }
            LoadFromZdo();
            if (!_trophies.TryGetValue(prefab, out c) || c <= 0) return false;
            if (c == 1) _trophies.Remove(prefab); else _trophies[prefab] = c - 1;
            SaveToZdo();
            p.GetInventory().AddItem(drop.gameObject, 1);
            return true;
        }

        // ---- Interactable / Hoverable ----

        /// <summary>
        /// Slot edits are refused inside a ward the player has no access to, with the ward's own flash and message.
        /// Same call as Container.Interact (m_checkGuardStone) on 1.0. Discounts are not affected: everyone within
        /// the radius still gets them.
        /// </summary>
        private bool WardAllowsEdit() => PrivateArea.CheckAccess(transform.position, 0f, true, false);

        /// <summary>Container.Interact: ask the owner to open. The panel opens on the response, as the new owner.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !IsValid || Game.instance == null) return false;
            if (!WardAllowsEdit()) return true;
            _nview.InvokeRPC(RpcRequestOpen, Game.instance.GetPlayerProfile().GetPlayerID());
            return true;
        }

        /// <summary>ItemStand.UseItem: slot now as the owner, otherwise ask for ownership and queue the trophy.</summary>
        public bool UseItem(Humanoid user, ItemDrop.ItemData item)
        {
            var p = user as Player;
            if (p == null || item == null || item.m_dropPrefab == null || !IsValid) return false;
            var prefab = item.m_dropPrefab.name;
            if (DiscountRules.KindOf(prefab) == TrophyKind.None) return false;
            if (!WardAllowsEdit()) return true;
            var blocked = InsertBlockedReason(prefab); // early feedback; InsertNow checks again as the owner
            if (blocked != null)
            {
                p.Message(MessageHud.MessageType.Center, blocked);
                return true;
            }

            if (_nview.IsOwner()) { InsertNow(p, item); return true; }
            _nview.InvokeRPC(RpcRequestOwn);
            _queuedItem = item;
            _queuedAt = Time.time;
            CancelInvoke(nameof(UpdateQueued));
            InvokeRepeating(nameof(UpdateQueued), 0f, 0.1f);
            return true;
        }

        /// <summary>The piece's own name key: "$piece_xai_beacon", or the Great Beacon's.</summary>
        public string PieceNameKey => IsGreat ? "$piece_xai_great_beacon" : "$piece_xai_beacon";

        public string GetHoverText() =>
            Localization.instance.Localize(
                $"{PieceNameKey}\n$xai_beacon_level {Level}/{MaxLevel}  $xai_beacon_radius {Radius:0}m  $xai_beacon_boss {(IsGreat ? $"{OwnBossCount}/{OwnBossSlots}" : BossCount.ToString())}  $xai_beacon_mob {MobSlotsText}\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] $xai_beacon_open");

        public string GetHoverName() => Localization.instance.Localize(PieceNameKey);

        public float GetHoverOffset() => 0f;
    }
}
