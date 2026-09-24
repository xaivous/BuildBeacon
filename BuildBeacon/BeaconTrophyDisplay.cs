using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BuildBeacon
{
    /// <summary>
    /// Shows slotted trophies in the beacon's four alcoves, one per <c>attach_N</c> point on the prefab.
    /// Each attach point sits on its niche's back wall at the niche centre, +Z pointing out of the niche.
    ///
    /// Placement (config TrophyPlacement, client-local): Inset scales each trophy to fit inside the niche; Mounted
    /// keeps the vanilla item stand size, so only the back of a trophy sits in the niche and the rest stands out.
    ///
    /// Which trophies: boss trophies first, in progression order (the boss rules file order); with more bosses than
    /// alcoves the most advanced ones show. Alcoves left over take mob trophies, one per kind, in name order.
    ///
    /// How: the vanilla item stand recipe (ItemStand.SetVisualItem on 1.0). The item prefab's child "attach",
    /// narrowed to its "attachobj" child when present, is instantiated with the attach child's own local pose under a
    /// pivot carrying the wall stand's attach_other rotation. That makes the trophy face out of the niche exactly as it
    /// faces out of a wall on a stand. The result is centred on the attach point with its back against the niche
    /// wall, scaled to fit first when the placement is Inset. Colliders and rigidbodies are stripped; the beacon's own collider handles hovering.
    /// </summary>
    public sealed class BeaconTrophyDisplay
    {
        public const int AlcoveCount = 4;
        public const string AttachPrefix = "attach_";

        /// <summary>Rotation of the vanilla wall item stand's attach_other point (itemstand prefab, 1.0).</summary>
        private static readonly Quaternion StandAttachRotation = new Quaternion(0f, 0.7071068f, 0.7071068f, 0f);

        /// <summary>Inset placement: largest trophy size in metres, width, height, depth out of the niche. The niche is 0.20 wide,
        /// 0.42 high and 0.15 deep with a frame proud of it, so trophies may stand a little out of the opening.</summary>
        private static readonly Vector3 FitBox = new Vector3(0.24f, 0.34f, 0.28f);

        /// <summary>Inset placement: small trophies grow at most this much to fill the niche.</summary>
        private const float MaxUpscale = 1.5f;

        private readonly Transform[] _mounts;
        private readonly string[] _shown;
        private readonly GameObject[] _visuals;
        private readonly Vector2? _shrinkTo;
        private TrophyPlacement _placement;
        private bool _shrinking;   // whether the last build shrank (ShrinkGreatBeaconTrophies); a change rebuilds

        /// <summary>The Great Beacon's boss trophies, as a share of their vanilla (item stand) size, chosen by the user in
        /// game (2026-09-24). Keys are trophy prefab names.</summary>
        private static readonly Dictionary<string, float> GreatTrophyScales = new Dictionary<string, float>
        {
            { "TrophyEikthyr", 1.0f }, { "TrophyTheElder", 1.0f }, { "TrophyDragonQueen", 1.0f },
            { "TrophyBonemass", 0.75f },
            { "TrophySeekerQueen", 0.65f }, { "TrophyGoblinKing", 0.65f }, { "TrophyFader", 0.65f },
        };
        /// <summary>Any other trophy in a Great Beacon alcove (a boss added later): at most this share of its size...</summary>
        public const float GreatTrophyMaxScale = 0.9f;
        /// <summary>...and smaller still where larger than this across the face.</summary>
        public static readonly Vector2 GreatTrophyFit = new Vector2(4.5f, 4.5f);

        private static float? GreatScaleFor(string prefab)
        {
            var key = DiscountRules.TrophyKey(prefab);
            foreach (var kv in GreatTrophyScales)
                if (DiscountRules.TrophyKey(kv.Key) == key) return kv.Value;
            return null;
        }

        /// <summary>
        /// The Great Beacon's alcove for each boss (the user, 2026-09-24): the top row, walking round from the front-left
        /// to the right, Eikthyr, the Elder, Bonemass, Moder; the lower row, from just right of the rune panel, Yagluth, the
        /// Queen, Fader. Its attach points: 0 front-left, 1 front-right, 4 back-left, 5 back-right (top row); 2 left,
        /// 3 right, 6 back (lower row); left and right as seen from the front.
        /// </summary>
        private static readonly Dictionary<string, int> GreatTrophyAlcoves = new Dictionary<string, int>
        {
            { "TrophyEikthyr", 0 }, { "TrophyTheElder", 1 }, { "TrophyBonemass", 5 }, { "TrophyDragonQueen", 4 },
            { "TrophyGoblinKing", 3 }, { "TrophySeekerQueen", 6 }, { "TrophyFader", 2 },
        };

        private static int GreatAlcoveFor(string prefab)
        {
            var key = DiscountRules.TrophyKey(prefab);
            foreach (var kv in GreatTrophyAlcoves)
                if (DiscountRules.TrophyKey(kv.Key) == key) return kv.Value;
            return -1;
        }

        /// <summary>The Great Beacon's alcoves: each boss in its own alcove (GreatTrophyAlcoves), any other trophy in the
        /// first alcove left free, in progression order. Null where an alcove is empty.</summary>
        public static List<string> ChooseGreat(IReadOnlyDictionary<string, int> trophies, int count)
        {
            var want = new string[count];
            var rest = new List<string>();
            foreach (var t in trophies.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(DiscountRules.BossOrder))
            {
                int i = GreatAlcoveFor(t);
                if (i >= 0 && i < count && want[i] == null) want[i] = t;
                else rest.Add(t);
            }
            foreach (var t in rest)
            {
                int free = System.Array.IndexOf(want, null);
                if (free < 0) break;
                want[free] = t;
            }
            return want.ToList();
        }

        /// <param name="beacon">The beacon; its alcoves are its attach_0, attach_1, ... children (4 on the Build Beacon,
        /// 7 on the Great Beacon).</param>
        /// <param name="shrinkTo">Mount every trophy the item-stand way whatever the placement setting, and, while
        /// ShrinkGreatBeaconTrophies is on, at most GreatTrophyMaxScale of its size and fitted to this width and height:
        /// the Great Beacon's boss trophies. Null: the placement setting.</param>
        public BeaconTrophyDisplay(Transform beacon, Vector2? shrinkTo = null)
        {
            var mounts = new List<Transform>();
            for (Transform t; (t = beacon.Find(AttachPrefix + mounts.Count)) != null;) mounts.Add(t);
            if (mounts.Count == 0) mounts.AddRange(new Transform[AlcoveCount]); // no attach points: nothing shows
            _mounts = mounts.ToArray();
            _shown = new string[_mounts.Length];
            _visuals = new GameObject[_mounts.Length];
            _shrinkTo = shrinkTo;
        }

        /// <summary>The trophies <paramref name="count"/> alcoves should show, in alcove order; shorter when fewer are
        /// slotted.</summary>
        public static List<string> Choose(IReadOnlyDictionary<string, int> trophies, int count = AlcoveCount)
        {
            var slotted = trophies.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
            var bosses = slotted.Where(DiscountRules.IsBossTrophy).OrderBy(DiscountRules.BossOrder).ToList();
            if (bosses.Count > count) bosses = bosses.Skip(bosses.Count - count).ToList();
            var mobs = slotted.Where(t => !DiscountRules.IsBossTrophy(t)).OrderBy(t => t, System.StringComparer.Ordinal);
            return bosses.Concat(mobs).Take(count).ToList();
        }

        /// <summary>Rebuild only the alcoves whose trophy changed. Cheap when nothing did.</summary>
        public void Refresh(IReadOnlyDictionary<string, int> trophies)
        {
            // The Great Beacon (the one display given a face fit) puts each boss in its own alcove.
            var want = _shrinkTo.HasValue ? ChooseGreat(trophies, _mounts.Length) : Choose(trophies, _mounts.Length);
            var placement = BuildBeaconPlugin.Cfg.Placement.Value;
            bool shrinking = _shrinkTo.HasValue && BuildBeaconPlugin.Cfg.ShrinkGreatBeaconTrophies.Value;
            bool rebuildAll = _shrinkTo.HasValue ? shrinking != _shrinking : placement != _placement;
            _placement = placement;
            _shrinking = shrinking;
            for (int i = 0; i < _mounts.Length; i++)
            {
                var name = i < want.Count ? want[i] : null;
                if (!rebuildAll && name == _shown[i] && (name == null || _visuals[i] != null)) continue;
                if (_visuals[i] != null) Object.Destroy(_visuals[i]);
                _visuals[i] = null;
                _shown[i] = name;
                if (name != null && _mounts[i] != null)
                {
                    var greatScale = shrinking ? GreatScaleFor(name) : null;
                    _visuals[i] = !_shrinkTo.HasValue ? Build(_mounts[i], name, placement)
                        : greatScale.HasValue ? Build(_mounts[i], name, TrophyPlacement.Mounted, fixedScale: greatScale)
                        : shrinking ? Build(_mounts[i], name, TrophyPlacement.Mounted, _shrinkTo, GreatTrophyMaxScale)
                        : Build(_mounts[i], name, TrophyPlacement.Mounted);
                }
            }
        }

        /// <summary>Mount one trophy on <paramref name="mount"/> (+Z out, back against it). Also used by BossHolder and
        /// MobRack. <paramref name="faceFit"/>: scale to fit an opening of that width and height only, ignoring depth
        /// (the Trophy Panel's niches are shallow, so trophies stand out of them); overrides the placement.
        /// <paramref name="maxScale"/>: with a face fit, the largest scale allowed (MaxUpscale lets small trophies grow;
        /// the Great Beacon passes GreatTrophyMaxScale). <paramref name="fixedScale"/>: this scale, overriding the rest (the
        /// Great Beacon's per-boss sizes).</summary>
        internal static GameObject Build(Transform mount, string prefabName, TrophyPlacement placement, Vector2? faceFit = null,
            float maxScale = MaxUpscale, float? fixedScale = null)
        {
            var item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefabName) : null;
            if (item == null)
            {
                BuildBeaconPlugin.Log.LogWarning($"Trophy display: no item prefab \"{prefabName}\"");
                return null;
            }
            var attach = item.transform.Find("attach");
            if (attach == null)
            {
                BuildBeaconPlugin.Log.LogWarning($"Trophy display: \"{prefabName}\" has no attach child");
                return null;
            }
            var attachObj = attach.Find("attachobj");
            var source = attachObj != null ? attachObj : attach;

            // holder: scaled and shifted to fit the niche. pivot: the stand's attach rotation. visual: the trophy.
            var holder = new GameObject("trophy_" + prefabName);
            holder.transform.SetParent(mount, false);
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(holder.transform, false);
            pivot.localRotation = StandAttachRotation;

            var visual = Object.Instantiate(source.gameObject, pivot, false);
            visual.transform.localPosition = attach.localPosition;
            visual.transform.localRotation = attach.localRotation;
            visual.transform.localScale = attach.localScale;
            // DestroyImmediate: the bounds below and the beacon's hover raycasts must not see these this frame.
            foreach (var c in visual.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var rb in visual.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);

            if (!TryMeshBounds(visual, holder.transform, out var bounds))
            {
                BuildBeaconPlugin.Log.LogWarning($"Trophy display: \"{prefabName}\" has no meshes to show");
                return holder;
            }
            var size = bounds.size;
            float scale = fixedScale.HasValue ? fixedScale.Value
                : faceFit.HasValue
                ? Mathf.Min(maxScale,
                            faceFit.Value.x / Mathf.Max(size.x, 1e-3f),
                            faceFit.Value.y / Mathf.Max(size.y, 1e-3f))
                : placement == TrophyPlacement.Inset
                ? Mathf.Min(MaxUpscale,
                            FitBox.x / Mathf.Max(size.x, 1e-3f),
                            FitBox.y / Mathf.Max(size.y, 1e-3f),
                            FitBox.z / Mathf.Max(size.z, 1e-3f))
                : 1f;
            holder.transform.localScale = Vector3.one * scale;
            // Centre sideways and vertically on the attach point, back face on the niche wall.
            holder.transform.localPosition = -scale * new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
            BuildBeaconPlugin.Log.LogInfo($"Trophy display: {prefabName} in {mount.name} ({(fixedScale.HasValue ? "Fixed" : faceFit.HasValue ? "Face" : placement.ToString())}), size {size.ToString("F2")} scaled x{scale:F2}");
            return holder;
        }

        /// <summary>Axis-aligned bounds of every mesh under <paramref name="root"/>, in <paramref name="space"/>'s local
        /// frame. Uses mesh bounds rather than Renderer.bounds, so it works before the first frame and on inactive
        /// objects.</summary>
        private static bool TryMeshBounds(GameObject root, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var toSpace = space.worldToLocalMatrix;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                if (mesh == null) continue;

                var m = toSpace * r.transform.localToWorldMatrix;
                var b = mesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    var sign = new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f);
                    var p = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, sign));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }
    }
}
