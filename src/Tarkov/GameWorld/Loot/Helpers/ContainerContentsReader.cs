/*
 * Lone EFT DMA Radar
 * Container Contents Reader
 *
 * Reads container contents for offline PVE matches.
 * Online matches generate loot server-side on container open, so this won't work there.
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Hideout;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers
{
    /// <summary>
    /// Helper class to read container contents from memory.
    /// Only works in offline PVE where loot is pre-generated.
    /// </summary>
    public static class ContainerContentsReader
    {
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
                                        Price = (int)marketItem.FleaPrice,
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
