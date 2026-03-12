/*
 * Dogtag Reader / Killfeed
 * Reads dogtag components from corpse loot items to build a raid killfeed.
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Reads dogtag data from corpses and maintains a killfeed.
    /// Thread-safe singleton per raid.
    /// </summary>
    public sealed class DogtagReader
    {
        private static DogtagReader _instance;
        private readonly ConcurrentDictionary<string, DogtagEntry> _entries = new();
        private readonly HashSet<ulong> _attemptedCorpses = new();
        private readonly HashSet<ulong> _attemptedInventories = new();
        private readonly List<DogtagEntry> _orderedEntries = new();
        private readonly Lock _sync = new();

        public static DogtagReader Instance => _instance;

        /// <summary>
        /// All dogtag entries in chronological order (newest first).
        /// </summary>
        public IReadOnlyList<DogtagEntry> Entries
        {
            get
            {
                lock (_sync)
                    return _orderedEntries.ToList();
            }
        }

        /// <summary>
        /// Number of dogtags read this raid.
        /// </summary>
        public int Count => _entries.Count;

        public static void Initialize() => _instance = new DogtagReader();
        public static void Clear() => _instance = null;

        /// <summary>
        /// Try to read a dogtag from a corpse's interactiveClass pointer.
        /// Reads the corpse's equipment slots, finds the "Dogtag" slot,
        /// and extracts DogtagComponent data.
        /// </summary>
        public void TryReadFromCorpse(ulong corpseInteractiveClass)
        {
            if (corpseInteractiveClass == 0 || _attemptedCorpses.Contains(corpseInteractiveClass))
                return;

            _attemptedCorpses.Add(corpseInteractiveClass);

            try
            {
                var itemBase = Memory.ReadPtr(corpseInteractiveClass + Offsets.InteractiveLootItem.Item);
                if (itemBase == 0)
                    return;

                var slotsPtr = Memory.ReadPtr(itemBase + Offsets.LootItemMod.Slots);
                if (slotsPtr == 0)
                    return;

                using var slots = UnityArray<ulong>.Create(slotsPtr, false);

                foreach (var slotPtr in slots)
                {
                    if (slotPtr == 0)
                        continue;

                    try
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var slotName = Memory.ReadUnicodeString(namePtr);

                        if (!slotName.Equals("Dogtag", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var dogtagItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        if (dogtagItem == 0)
                            break;

                        var dogtagComp = Memory.ReadPtr(dogtagItem + Offsets.BarterOtherOffsets.Dogtag);
                        if (dogtagComp == 0)
                            break;

                        ReadDogtagComponent(dogtagComp);
                        break;
                    }
                    catch
                    {
                        // Slot read failed — already marked as attempted, won't retry
                    }
                }
            }
            catch
            {
                // Corpse read failed — already marked as attempted, won't retry
            }
        }

        /// <summary>
        /// Try to read a dogtag from a player's inventory controller.
        /// Used for synced player corpses where we have the player reference.
        /// </summary>
        public void TryReadFromInventory(ulong inventoryControllerAddr)
        {
            if (inventoryControllerAddr == 0 || _attemptedInventories.Contains(inventoryControllerAddr))
                return;

            _attemptedInventories.Add(inventoryControllerAddr);

            try
            {
                var inventoryController = Memory.ReadPtr(inventoryControllerAddr);
                if (inventoryController == 0)
                    return;

                var inventory = Memory.ReadPtr(inventoryController + Offsets.InventoryController.Inventory);
                if (inventory == 0)
                    return;

                var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                if (equipment == 0)
                    return;

                var slotsPtr = Memory.ReadPtr(equipment + Offsets.InventoryEquipment._cachedSlots);
                if (slotsPtr == 0)
                    return;

                using var slots = UnityArray<ulong>.Create(slotsPtr, false);

                foreach (var slotPtr in slots)
                {
                    if (slotPtr == 0)
                        continue;

                    try
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var slotName = Memory.ReadUnicodeString(namePtr);

                        if (!slotName.Equals("Dogtag", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var dogtagItem = Memory.ReadPtr(slotPtr + Offsets.Slot.ContainedItem);
                        if (dogtagItem == 0)
                            break;

                        var dogtagComp = Memory.ReadPtr(dogtagItem + Offsets.BarterOtherOffsets.Dogtag);
                        if (dogtagComp == 0)
                            break;

                        ReadDogtagComponent(dogtagComp);
                        break;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private void ReadDogtagComponent(ulong dogtagComp)
        {
            var profileId = ReadString(dogtagComp + Offsets.DogtagComponent.ProfileId);
            if (string.IsNullOrEmpty(profileId))
                return;

            // Dedupe by ProfileId
            if (_entries.ContainsKey(profileId))
                return;

            var entry = new DogtagEntry
            {
                ProfileId = profileId,
                Nickname = ReadString(dogtagComp + Offsets.DogtagComponent.Nickname) ?? "Unknown",
                Level = Memory.ReadValue<int>(dogtagComp + Offsets.DogtagComponent.Level, false),
                Side = Memory.ReadValue<int>(dogtagComp + Offsets.DogtagComponent.Side, false),
                KillerName = ReadString(dogtagComp + Offsets.DogtagComponent.KillerName) ?? "Unknown",
                WeaponName = ResolveWeaponName(dogtagComp),
                Timestamp = DateTime.UtcNow
            };

            if (_entries.TryAdd(profileId, entry))
            {
                lock (_sync)
                {
                    _orderedEntries.Insert(0, entry); // Newest first
                }
                DebugLogger.LogInfo($"[DogtagReader] {entry.SideName} Lv{entry.Level} '{entry.Nickname}' killed by '{entry.KillerName}' ({entry.WeaponName})");
            }
        }

        /// <summary>
        /// Resolve weapon template ID to short display name via TarkovDataManager.
        /// </summary>
        private static string ResolveWeaponName(ulong dogtagComp)
        {
            try
            {
                var raw = ReadString(dogtagComp + Offsets.DogtagComponent.WeaponName);
                if (string.IsNullOrEmpty(raw))
                    return "Unknown";

                // The dogtag stores something like "5c488a752e221602b412af63 ShortName" or just the template ID
                // Extract the hex ID (first token, 24 chars)
                var id = raw.Contains(' ') ? raw.Split(' ')[0] : raw;

                if (id.Length >= 24 && TarkovDataManager.AllItems.TryGetValue(id, out var item))
                    return item.ShortName ?? item.Name ?? raw;

                return raw;
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string ReadString(ulong fieldAddr)
        {
            try
            {
                var ptr = Memory.ReadPtr(fieldAddr);
                if (ptr == 0)
                    return null;
                return Memory.ReadUnicodeString(ptr);
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A single dogtag entry representing a kill.
    /// </summary>
    public sealed class DogtagEntry
    {
        public string ProfileId { get; init; }
        public string Nickname { get; init; }
        public int Level { get; init; }
        public int Side { get; init; }
        public string KillerName { get; init; }
        public string WeaponName { get; init; }
        public DateTime Timestamp { get; init; }

        /// <summary>
        /// Human-readable faction name.
        /// </summary>
        public string SideName => Side switch
        {
            1 => "USEC",
            2 => "BEAR",
            _ => "Scav"
        };

        /// <summary>
        /// Formatted killfeed string.
        /// </summary>
        public string ToKillfeedString() => $"{KillerName} [{WeaponName}] {SideName} Lv{Level} {Nickname}";
    }
}
