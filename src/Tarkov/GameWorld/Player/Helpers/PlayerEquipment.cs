using LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using System.Collections.Frozen;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers
{
    public sealed class PlayerEquipment
    {
        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SecuredContainer", "Dogtag", "Compass", "ArmBand", "Eyewear", "Pockets"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ulong> _slots = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TarkovMarketItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly AbstractPlayer _player;
        private readonly ulong _inventoryControllerAddr;
        private List<ContainerItem> _inventoryContents;
        private bool _inited;

        /// <summary>
        /// Player's equipped gear by slot.
        /// </summary>
        public IReadOnlyDictionary<string, TarkovMarketItem> Items => _items;
        /// <summary>
        /// Player's total equipment value (uses flea price if available, otherwise trader price).
        /// </summary>
        public int Value => (int)_items.Values.Sum(i => i.FleaPrice > 0 ? i.FleaPrice : i.TraderPrice);

        /// <summary>
        /// Items inside the player's containers (Backpack, TacticalVest, Pockets).
        /// Null if not yet loaded or PVE scan disabled.
        /// </summary>
        public IReadOnlyList<ContainerItem> InventoryContents => _inventoryContents;

        /// <summary>
        /// Total value of items inside the player's containers.
        /// </summary>
        public int InventoryValue => _inventoryContents?.Sum(x => x.Price) ?? 0;

        public PlayerEquipment(AbstractPlayer player, ulong inventoryControllerAddr)
        {
            _player = player;
            _inventoryControllerAddr = inventoryControllerAddr;
            Task.Run(InitAsnyc); // Lazy init
        }

        private async Task InitAsnyc()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var inventorycontroller = Memory.ReadPtr(_inventoryControllerAddr);

                    var inventory = Memory.ReadPtr(inventorycontroller + Offsets.InventoryController.Inventory);
                    var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                    var slotsPtr = Memory.ReadPtr(equipment + Offsets.InventoryEquipment._cachedSlots);
                    using var slotsArray = UnityArray<ulong>.Create(slotsPtr, true);

                    ArgumentOutOfRangeException.ThrowIfLessThan(slotsArray.Count, 1);

                    foreach (var slotPtr in slotsArray)
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var name = Memory.ReadUnicodeString(namePtr);
                        if (_skipSlots.Contains(name))
                            continue;
                        _slots.TryAdd(name, slotPtr);
                    }

                    Refresh(checkInit: false);
                    _inited = true;

                    // Read inventory contents (items inside Backpack/Rig/Pockets) if PVE scan enabled
                    if (App.Config.Containers.PveScanEnabled)
                    {
                        try
                        {
                            var contents = CorpseContentsReader.GetContentsFromInventoryController(inventorycontroller);
                            if (contents.Count > 0)
                            {
                                contents.Sort((a, b) => b.Price.CompareTo(a.Price));
                                _inventoryContents = contents;
                            }
                        }
                        catch { }
                    }

                    return;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[Equipment] Error initializing for '{_player.Name}' attempt {i + 1}: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        public void Refresh(bool checkInit = true)
        {
            if (checkInit && !_inited)
                return;
            foreach (var slot in _slots)
            {
                try
                {
                    if (_player.IsPmc && slot.Key == "Scabbard")
                    {
                        continue; // skip pmc scabbard
                    }
                    var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                    var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                    var mongoId = Memory.ReadValue<MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                    var id = mongoId.ReadString();
                    if (TarkovDataManager.AllItems.TryGetValue(id, out var item))
                    {
                        _items[slot.Key] = item;
                    }
                    else
                    {
                        _items.TryRemove(slot.Key, out _);
                    }
                }
                catch
                {
                    _items.TryRemove(slot.Key, out _);
                }
            }
        }

    }
}
