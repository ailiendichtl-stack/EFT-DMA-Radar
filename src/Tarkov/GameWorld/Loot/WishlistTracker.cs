/*
 * Wishlist Tracker
 * Reads the player's wishlist from memory and provides fast item ID lookup.
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    /// <summary>
    /// Reads and caches the player's wishlist from Profile.WishlistManager.
    /// Thread-safe singleton per raid — initialized once from the local player profile.
    /// </summary>
    public sealed class WishlistTracker
    {
        private static WishlistTracker _instance;
        private readonly HashSet<string> _wishlistIds = new(StringComparer.OrdinalIgnoreCase);

        // Dictionary reading constants (same layout as quest system)
        private const uint DictCountOffset = 0x40;
        private const uint DictEntriesOffset = 0x18;
        private const uint ArrayLengthOffset = 0x18;
        private const uint ArrayHeaderSize = 0x20;
        private const int EntrySize = 0x28; // hashCode(4) + next(4) + MongoID(0x18) + Int32(4) + pad(4)
        private const uint EntryKeyOffset = 0x10; // MongoID starts after hashCode+next+pad
        private const int MaxEntries = 500;

        public static WishlistTracker Instance => _instance;

        /// <summary>
        /// Initialize the wishlist tracker from a player profile address.
        /// </summary>
        public static void Initialize(ulong profileAddr)
        {
            try
            {
                var tracker = new WishlistTracker();
                tracker.ReadWishlist(profileAddr);
                _instance = tracker;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[WishlistTracker] Failed to initialize: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear the wishlist tracker (on raid end).
        /// </summary>
        public static void Clear() => _instance = null;

        /// <summary>
        /// Check if an item template ID is on the player's wishlist.
        /// </summary>
        public bool IsWishlistItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return false;
            return _wishlistIds.Contains(itemId);
        }

        /// <summary>
        /// Number of items on the wishlist.
        /// </summary>
        public int Count => _wishlistIds.Count;

        private void ReadWishlist(ulong profileAddr)
        {
            var wishlistMgrPtr = Memory.ReadPtr(profileAddr + Offsets.Profile.WishlistManager);
            if (wishlistMgrPtr == 0)
                return;

            var dictPtr = Memory.ReadPtr(wishlistMgrPtr + Offsets.WishlistManager.Items);
            if (dictPtr == 0)
                return;

            var count = Memory.ReadValue<int>(dictPtr + DictCountOffset);
            if (count <= 0 || count > MaxEntries)
                return;

            var entriesPtr = Memory.ReadPtr(dictPtr + DictEntriesOffset);
            if (entriesPtr == 0)
                return;

            var arrayLength = Memory.ReadValue<int>(entriesPtr + ArrayLengthOffset);
            if (arrayLength <= 0 || arrayLength > MaxEntries)
                return;

            var entriesStart = entriesPtr + ArrayHeaderSize;

            for (int i = 0; i < arrayLength; i++)
            {
                try
                {
                    var entryAddr = entriesStart + (uint)(i * EntrySize);
                    var hashCode = Memory.ReadValue<int>(entryAddr);
                    if (hashCode < 0)
                        continue; // empty/deleted slot

                    var mongoId = Memory.ReadValue<MongoID>(entryAddr + EntryKeyOffset);
                    var itemId = mongoId.ReadString(64, false);

                    if (!string.IsNullOrEmpty(itemId) && itemId.Length >= 10)
                    {
                        _wishlistIds.Add(itemId);
                    }
                }
                catch
                {
                    // Skip entry on error
                }
            }

            if (_wishlistIds.Count > 0)
                DebugLogger.LogDebug($"[WishlistTracker] Loaded {_wishlistIds.Count} wishlist items");
        }
    }
}
