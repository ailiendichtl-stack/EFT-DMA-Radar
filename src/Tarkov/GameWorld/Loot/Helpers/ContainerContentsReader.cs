/*
 * Lone EFT DMA Radar
 * Container Contents Reader
 *
 * Reads container contents for offline PVE matches.
 * Online matches generate loot server-side on container open, so this won't work there.
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Hideout;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers
{
    /// <summary>
    /// Helper class to read container contents from memory.
    /// Only works in offline PVE where loot is pre-generated.
    /// </summary>
    public static class ContainerContentsReader
    {
        #region Batch Scatter Reading

        /// <summary>
        /// State tracking for a container during batch scatter reads.
        /// </summary>
        private sealed class ContainerState
        {
            public ulong InteractiveClass;
            public ulong ItemOwner;
            public ulong RootItem;
            public ulong GridsPtr;
            public List<ContainerItem> Items = new();
        }

        /// <summary>
        /// Batch reads container contents for multiple containers using scatter reads.
        /// Significantly reduces DMA overhead compared to sequential per-container reads.
        /// </summary>
        /// <param name="containers">List of containers to read contents for</param>
        public static void BatchRefreshContents(List<StaticLootContainer> containers)
        {
            if (containers.Count == 0)
                return;

            try
            {
                // Phase 1: Batch read the initial pointer chain for ALL containers
                // This reduces 3 DMA calls per container to a single batched operation
                using var map = Memory.CreateScatterMap();
                var round1 = map.AddRound(); // ItemOwner
                var round2 = map.AddRound(); // RootItem
                var round3 = map.AddRound(); // Grids pointer

                var states = new Dictionary<StaticLootContainer, ContainerState>();

                // Round 1: Queue ItemOwner reads for all containers
                foreach (var container in containers)
                {
                    var state = new ContainerState { InteractiveClass = container.InteractiveClass };
                    states[container] = state;
                    round1.PrepareReadPtr(container.InteractiveClass + Offsets.LootableContainer.ItemOwner);
                }

                // Round 1 completion: Queue RootItem reads
                round1.Completed += (_, s1) =>
                {
                    foreach (var kvp in states)
                    {
                        if (s1.ReadPtr(kvp.Value.InteractiveClass + Offsets.LootableContainer.ItemOwner, out var itemOwner) && itemOwner != 0)
                        {
                            kvp.Value.ItemOwner = itemOwner;
                            round2.PrepareReadPtr(itemOwner + Offsets.LootableContainerItemOwner.RootItem);
                        }
                    }
                };

                // Round 2 completion: Queue Grids pointer reads
                round2.Completed += (_, s2) =>
                {
                    foreach (var kvp in states)
                    {
                        if (kvp.Value.ItemOwner != 0 &&
                            s2.ReadPtr(kvp.Value.ItemOwner + Offsets.LootableContainerItemOwner.RootItem, out var rootItem) && rootItem != 0)
                        {
                            kvp.Value.RootItem = rootItem;
                            round3.PrepareReadPtr(rootItem + Offsets.LootItemMod.Grids);
                        }
                    }
                };

                // Round 3 completion: Store Grids pointers
                round3.Completed += (_, s3) =>
                {
                    foreach (var kvp in states)
                    {
                        if (kvp.Value.RootItem != 0 &&
                            s3.ReadPtr(kvp.Value.RootItem + Offsets.LootItemMod.Grids, out var gridsPtr) && gridsPtr != 0)
                        {
                            kvp.Value.GridsPtr = gridsPtr;
                        }
                    }
                };

                // Execute all 3 rounds in one batch
                map.Execute();

                // Phase 2: Process grids and items for containers that have valid Grids pointers
                // This is done after the batch to allow parallel processing of the pointer chain
                foreach (var kvp in states)
                {
                    if (kvp.Value.GridsPtr == 0)
                        continue;

                    try
                    {
                        var items = ReadContainerItemsFromGrids(kvp.Value.GridsPtr);
                        if (items.Count > 0)
                        {
                            kvp.Key.SetContents(items);
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogDebug($"[ContainerContentsReader] Error reading container items: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ContainerContentsReader] BatchRefreshContents error: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads all items from a container's grids array.
        /// Used by BatchRefreshContents after the initial pointer chain has been read.
        /// </summary>
        private static List<ContainerItem> ReadContainerItemsFromGrids(ulong gridsPtr)
        {
            var items = new List<ContainerItem>();

            using var gridsArray = UnityArray<ulong>.Create(gridsPtr, false);

            foreach (var gridPtr in gridsArray)
            {
                var itemCollectionPtr = Memory.ReadPtr(gridPtr + Offsets.Grid.ItemCollection);
                if (itemCollectionPtr == 0)
                    continue;

                var itemsPtr = Memory.ReadPtr(itemCollectionPtr + Offsets.GridItemCollection.Items);
                if (itemsPtr == 0)
                    continue;

                try
                {
                    using var itemsList = UnityList<ulong>.Create(itemsPtr, false);

                    foreach (var itemPtr in itemsList)
                    {
                        try
                        {
                            var template = Memory.ReadPtr(itemPtr + Offsets.LootItem.Template);
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
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return items;
        }

        #endregion

        /// <summary>
        /// Returns a list of items inside a container.
        /// </summary>
        /// <param name="interactiveClass">The container's InteractiveClass pointer</param>
        /// <returns>List of ContainerItem objects representing the container contents</returns>
        public static List<ContainerItem> GetContainerContents(ulong interactiveClass)
        {
            var items = new List<ContainerItem>();

            try
            {
                var itemOwner = Memory.ReadPtr(interactiveClass + Offsets.LootableContainer.ItemOwner);
                if (itemOwner == 0) return items;

                var rootItem = Memory.ReadPtr(itemOwner + Offsets.LootableContainerItemOwner.RootItem);
                if (rootItem == 0) return items;

                var gridsPtr = Memory.ReadPtr(rootItem + Offsets.LootItemMod.Grids);
                if (gridsPtr == 0) return items;

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

                        foreach (var itemPtr in itemsList)
                        {
                            try
                            {
                                var template = Memory.ReadPtr(itemPtr + Offsets.LootItem.Template);
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
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return items;
        }
    }

    /// <summary>
    /// Represents an item found inside a container.
    /// </summary>
    public class ContainerItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Price { get; set; }
        public TarkovMarketItem MarketItem { get; set; }

        /// <summary>
        /// True if this item is marked as Important via custom loot filters.
        /// </summary>
        public bool IsImportant => MarketItem?.Important ?? false;

        /// <summary>
        /// The color from the custom filter (if any).
        /// </summary>
        public string FilterColor => MarketItem?.CustomFilter?.Color;

        /// <summary>
        /// The name of the custom filter (if any).
        /// </summary>
        public string FilterName => MarketItem?.CustomFilter?.Name;

        /// <summary>
        /// True if this item is needed for a tracked hideout upgrade.
        /// </summary>
        public bool IsHideoutItem => HideoutManager.Instance?.IsHideoutItem(Id) ?? false;

        /// <summary>
        /// True if this item is needed for an active quest objective.
        /// </summary>
        public bool IsQuestItem => Memory.Quests?.IsQuestItem(Id) ?? false;
    }
}
