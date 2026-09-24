using System;

namespace BuildBeacon
{
    /// <summary>Helpers for storing structured data on a ZDO and resolving ownership.</summary>
    public static class ZdoUtil
    {
        public const string CreatorKey = "creator"; // vanilla Piece.SetCreator writes this as long

        public static long GetCreator(ZNetView nview) =>
            nview != null && nview.IsValid() ? nview.GetZDO().GetLong(ZDOVars.s_creator, 0L) : 0L;

        public static bool IsLocalPlayerCreator(ZNetView nview)
        {
            var p = Player.m_localPlayer;
            return p != null && GetCreator(nview) == p.GetPlayerID();
        }

        /// <summary>Serialize arbitrary state into a ZPackage and store as a byte[] ZDO value.</summary>
        public static void SetPackage(ZNetView nview, string key, Action<ZPackage> write)
        {
            if (nview == null || !nview.IsValid()) return;
            var pkg = new ZPackage();
            write(pkg);
            nview.GetZDO().Set(key, pkg.GetArray());
        }

        public static bool TryGetPackage(ZNetView nview, string key, out ZPackage pkg)
        {
            pkg = null;
            if (nview == null || !nview.IsValid()) return false;
            var bytes = nview.GetZDO().GetByteArray(key);
            if (bytes == null || bytes.Length == 0) return false;
            pkg = new ZPackage(bytes);
            return true;
        }

        /// <summary>Force the server to own this ZDO so authoritative logic runs there.</summary>
        public static void ClaimForServer(ZNetView nview)
        {
            if (nview == null || !nview.IsValid() || !ZNet.instance.IsServer()) return;
            if (!nview.IsOwner()) nview.ClaimOwnership();
        }
    }
}
