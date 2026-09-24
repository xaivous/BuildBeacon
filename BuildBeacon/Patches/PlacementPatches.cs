using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// Wall-mounted pieces (the Boss Trophy Mount and the Trophy Panel) have their snap points on their back plane, so
    /// their back sits flat against whatever they snap to. Many vanilla walls put their snap points on their centre
    /// plane instead (wood walls, Grausten walls, 0.4 m thick), so a snapped mount started half a wall deep and, being
    /// shallower than that, vanished inside it. Walls with snap points on their faces (stone walls) were fine.
    ///
    /// After vanilla has placed the ghost (Player.UpdatePlacementGhost, 1.0: FindClosestSnapPoints moves the ghost so one
    /// of its snap points sits on the target's), find the pair that coincides; if the ghost's back lies inside the
    /// target's collider there, push the ghost out along its facing (+Z) until its back is on the target's surface,
    /// less a centimetre so support still finds the wall. Only for our wall pieces, never against our own pieces (panels
    /// tile on a shared plane), and only while the back is actually inside, so a panel snapped below a wall's edge
    /// stays in the wall's plane.
    /// </summary>
    internal static class PlacementPatches
    {
        private static readonly HashSet<string> WallPieces = new HashSet<string>
        {
            BuildBeaconPlugin.HolderPrefabWall,
            BuildBeaconPlugin.RackPrefabWall,
        };

        /// <summary>How far the pushed-out back stays inside the wall's surface, so the wall still supports it.</summary>
        private const float Embed = 0.01f;
        /// <summary>Snap points closer than this count as snapped together (vanilla puts them exactly on each other).</summary>
        private const float SnapTolerance = 0.005f;
        /// <summary>How far in from the snap point the "is the back inside the wall" test and the ray look: snap points sit
        /// on collider edges and corners, where a ray would only graze.</summary>
        private const float Nudge = 0.05f;
        /// <summary>No wall is thicker than this; a larger push means the ray found something else.</summary>
        private const float MaxPush = 1.5f;

        private static readonly List<Transform> s_ghostPoints = new List<Transform>();
        private static readonly List<Transform> s_targetPoints = new List<Transform>();
        private static readonly List<Collider> s_colliders = new List<Collider>();

        [HarmonyPatch(typeof(Player), nameof(Player.UpdatePlacementGhost))]
        internal static class Player_UpdatePlacementGhost
        {
            static void Postfix(Player __instance)
            {
                var ghost = __instance.m_placementGhost;
                if (ghost == null || !ghost.activeSelf || !WallPieces.Contains(ghost.name)) return;
                var ghostPiece = ghost.GetComponent<Piece>();
                if (ghostPiece == null) return;
                PushOutOfWall(ghost.transform, ghostPiece, __instance.m_tempPieces);
            }
        }

        private static void PushOutOfWall(Transform ghost, Piece ghostPiece, List<Piece> nearby)
        {
            if (nearby == null || nearby.Count == 0) return;
            s_ghostPoints.Clear();
            ghostPiece.GetSnapPoints(s_ghostPoints);
            if (s_ghostPoints.Count == 0) return;

            // The snapped pair: vanilla moved one of the ghost's snap points onto one of a nearby piece's.
            Piece target = null;
            Vector3 at = Vector3.zero;
            float tol2 = SnapTolerance * SnapTolerance;
            foreach (var p in nearby)
            {
                if (p == null || p.gameObject == ghost.gameObject || IsOurs(p)) continue;
                s_targetPoints.Clear();
                p.GetSnapPoints(s_targetPoints);
                foreach (var b in s_targetPoints)
                foreach (var a in s_ghostPoints)
                    if ((a.position - b.position).sqrMagnitude < tol2)
                    {
                        target = p;
                        at = b.position;
                        goto found;
                    }
            }
            return;

            found:
            var forward = ghost.forward;
            // A point just in from the snap point, towards the ghost's middle (its origin is the centre of its back), or
            // towards the target's middle when the ghost snapped by its centre.
            var inward = Vector3.ProjectOnPlane(ghost.position - at, forward);
            s_colliders.Clear();
            target.GetComponentsInChildren(false, s_colliders);
            float push = 0f;
            foreach (var c in s_colliders)
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (c is MeshCollider mc && !mc.convex) continue; // ClosestPoint needs a convex shape
                var dir = inward.sqrMagnitude > 1e-4f ? inward : Vector3.ProjectOnPlane(c.bounds.center - at, forward);
                var q = dir.sqrMagnitude > 1e-4f ? at + dir.normalized * Nudge : at;
                if ((c.ClosestPoint(q) - q).sqrMagnitude > 1e-6f) continue; // the back is not inside this collider here

                // The collider's surface on the side the piece faces: a ray from in front, back towards the point.
                var ray = new Ray(q + forward * MaxPush * 2f, -forward);
                if (!c.Raycast(ray, out var hit, MaxPush * 2f)) continue;
                float depth = Vector3.Dot(hit.point - q, forward);
                if (depth > push && depth < MaxPush) push = depth;
            }
            if (push <= Embed) return;
            ghost.position += forward * (push - Embed);
            if (BuildBeaconPlugin.Cfg.VerboseLogging.Value && (target != s_loggedTarget || Mathf.Abs(push - s_loggedPush) > 0.01f))
            {
                s_loggedTarget = target;
                s_loggedPush = push;
                BuildBeaconPlugin.Log.LogInfo($"[diag] {ghost.name} snapped to {target.name}: pushed {push - Embed:0.###} m out of it");
            }
        }

        // Dev mode logs a push once per target and depth, not every frame.
        private static Piece s_loggedTarget;
        private static float s_loggedPush;

        private static bool IsOurs(Piece p) =>
            p.GetComponent<BossHolder>() != null || p.GetComponent<MobRack>() != null || p.GetComponent<BeaconController>() != null;
    }
}
