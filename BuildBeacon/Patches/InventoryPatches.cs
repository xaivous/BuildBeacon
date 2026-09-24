using HarmonyLib;

namespace BuildBeacon.Patches
{
    /// <summary>
    /// While the beacon panel is open, Ctrl+click (InventoryGrid.Modifier.Move) on the player's inventory slots trophies
    /// into the beacon through BeaconController.TryInsertTrophy, so every slot rule, ownership and the ward still apply.
    /// Without a container open, vanilla InventoryGui.OnSelectedItem (Valheim 1.0) drops the clicked stack on the
    /// ground for Move; with the beacon panel open that click is taken over for every item, so nothing is dropped by
    /// accident. Everything else (other modifiers, dragging, other grids, the panel closed) runs vanilla untouched.
    /// </summary>
    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem))]
    internal static class InventoryGui_OnSelectedItem
    {
        static bool Prefix(InventoryGui __instance, InventoryGrid grid, ItemDrop.ItemData item, InventoryGrid.Modifier mod)
        {
            if (mod != InventoryGrid.Modifier.Move || item == null || BeaconUI.OpenBeacon == null) return true;
            if (__instance.m_dragGo != null || grid != __instance.m_playerGrid || __instance.m_currentContainer != null) return true;
            BeaconUI.OnInventoryCtrlClick(item);
            return false;
        }
    }
}
