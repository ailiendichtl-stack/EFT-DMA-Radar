/*
 * Lone EFT DMA Radar
 * Corpse Contents Reader
 *
 * Reads corpse inventory contents (backpack, rig, pockets) for offline PVE matches.
 * Uses the same grid reading logic as ContainerContentsReader.
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers
{
    /// <summary>
    /// Helper class to read corpse inventory contents from memory.
    /// Reads items from Backpack, TacticalVest (rig), and Pockets slots.
    /// </summary>
    public static class CorpseContentsReader
    {
        // Slots that can contain items (have grids)
        private static readonly string[] _containerSlots = { "Backpack", "TacticalVest", "Pockets" };

        /// <summary>
        /// Returns a list of items inside a corpse's inventory containers.
        /// </summary>
        /// <param name="player">The ObservedPlayer (corpse) to read from</param>
        /// <returns>List of ContainerItem objects representing the corpse's inventory contents</returns>
        public static List<ContainerItem> GetCorpseContents(ObservedPlayer player)
        {
            var inventoryController = Memory.ReadPtr(player.InventoryControllerAddr);
            return GetContentsFromInventoryController(inventoryController);
        }

        /// <summary>
        /// Returns a list of items inside a corpse's inventory containers for offline AI (ClientPlayer).
        /// Uses the Player._inventoryController offset directly.
        /// </summary>
        /// <param name="player">The ClientPlayer (offline AI corpse) to read from</param>
        /// <returns>List of ContainerItem objects representing the corpse's inventory contents</returns>
        public static List<ContainerItem> GetCorpseContents(ClientPlayer player)
        {
            var inventoryController = Memory.ReadPtr(player.Base + Offsets.Player._inventoryController);
            return GetContentsFromInventoryController(inventoryController);
        }

        /// <summary>
        /// Returns a list of items inside a corpse directly from its interactiveClass pointer.
        /// Used for pre-existing/environmental corpses that don't have a player sync.
        /// Uses InteractiveLootItem.Item -> LootItemMod.Slots path (different from containers).
        /// </summary>
        /// <param name="corpseInteractiveClass">The corpse's interactiveClass pointer</param>
        /// <returns>List of ContainerItem objects representing the corpse's inventory contents</returns>
        public static List<ContainerItem> GetCorpseContentsFromInteractiveClass(ulong corpseInteractiveClass)
        {
            var items = new List<ContainerItem>();

            try
            {
                if (corpseInteractiveClass == 0) return items;

                // Corpses use InteractiveLootItem.Item -> Slots path (NOT container path)
                // interactiveClass + 0xF0 (InteractiveLootItem.Item) -> itemBase
                // itemBase + 0x80 (LootItemMod.Slots) -> slots array
                var itemBase = Memory.ReadPtr(corpseInteractiveClass + Offsets.InteractiveLootItem.Item);
                if (itemBase == 0) return items;

                var slotsPtr = Memory.ReadPtr(itemBase + Offsets.LootItemMod.Slots);
                if (slotsPtr == 0) return items;

                using var slotsArray = UnityArray<ulong>.Create(slotsPtr, false);

                foreach (var slotPtr in slotsArray)
                {
                    try
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var slotName = Memory.ReadUnicodeString(namePtr);

                        // Only read container slots (Backpack, TacticalVest, Pockets)
                        if (!_containerSlots.Contains(slotName, StringComparer.OrdinalIgnoreCase))
                            continue;

                        var containedItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        if (containedItem == 0) continue;

                        // Read the grids from this container item
                        ReadItemGrids(containedItem, items);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                LoneEftDmaRadar.UI.Misc.DebugLogger.LogDebug($"[CorpseRead] 0x{corpseInteractiveClass:X} - Exception: {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// Reads inventory contents (Backpack, TacticalVest, Pockets) from an inventory controller pointer.
        /// Works for both corpses and alive players.
        /// </summary>
        internal static List<ContainerItem> GetContentsFromInventoryController(ulong inventoryController)
        {
            var items = new List<ContainerItem>();

            try
            {
                if (inventoryController == 0) return items;

                var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
                if (inventory == 0) return items;

                var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                if (equipment == 0) return items;

                var slotsPtr = Memory.ReadPtr(equipment + Offsets.InventoryEquipment._cachedSlots);
                if (slotsPtr == 0) return items;

                using var slotsArray = UnityArray<ulong>.Create(slotsPtr, false);

                foreach (var slotPtr in slotsArray)
                {
                    try
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var slotName = Memory.ReadUnicodeString(namePtr);

                        // Only read container slots (Backpack, TacticalVest, Pockets)
                        if (!_containerSlots.Contains(slotName, StringComparer.OrdinalIgnoreCase))
                            continue;

                        var containedItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        if (containedItem == 0) continue;

                        // Read the grids from this container item
                        ReadItemGrids(containedItem, items);
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }

        /// <summary>
        /// Reads all items from an item's grids (for containers like backpacks, rigs, etc.)
        /// </summary>
        private static void ReadItemGrids(ulong itemPtr, List<ContainerItem> items)
        {
            try
            {
                var gridsPtr = Memory.ReadPtr(itemPtr + Offsets.LootItemMod.Grids);
                if (gridsPtr == 0) return;

                using var gridsArray = UnityArray<ulong>.Create(gridsPtr, false);

                foreach (var gridPtr in gridsArray)
                {
                    // Grid+0x48 -> ItemCollection, ItemCollection+0x18 -> Items list
                    var itemCollectionPtr = Memory.ReadPtr(gridPtr + Offsets.Grid.ItemCollection);
                    if (itemCollectionPtr == 0) continue;

                    var itemsPtr = Memory.ReadPtr(itemCollectionPtr + Offsets.GridItemCollection.Items);
                    if (itemsPtr == 0) continue;

                    try
                    {
                        using var itemsList = UnityList<ulong>.Create(itemsPtr, false);

                        foreach (var itemBase in itemsList)
                        {
                            try
                            {
                                var template = Memory.ReadPtr(itemBase + Offsets.LootItem.Template);
                                var mongoId = Memory.ReadValue<MongoID>(template + Offsets.ItemTemplate._id);
                                var itemId = mongoId.ReadString();

                                if (TarkovDataManager.AllItems.TryGetValue(itemId, out var marketItem))
                                {
                                    items.Add(new ContainerItem
                                    {
                                        Id = itemId,
                                        Name = marketItem.ShortName ?? marketItem.Name,
                                        Price = (int)(marketItem.FleaPrice > 0 ? marketItem.FleaPrice : marketItem.TraderPrice),
                                        MarketItem = marketItem
                                    });
                                }

                                // Recursively read nested containers (items inside items)
                                ReadItemGrids(itemBase, items);
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }
    }
}
