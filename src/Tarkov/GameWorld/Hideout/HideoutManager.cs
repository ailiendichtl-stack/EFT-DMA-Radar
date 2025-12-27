/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Misc;
using System.ComponentModel;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Hideout
{
    /// <summary>
    /// Manages hideout item tracking based on user-selected stations/levels.
    /// </summary>
    public sealed class HideoutManager
    {
        private static HideoutManager _instance;
        private static readonly object _lock = new();

        private readonly ConcurrentDictionary<string, TrackedHideoutItem> _items = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Singleton instance of HideoutManager.
        /// </summary>
        public static HideoutManager Instance
        {
            get
            {
                if (_instance is null)
                {
                    lock (_lock)
                    {
                        _instance ??= new HideoutManager();
                    }
                }
                return _instance;
            }
        }

        private HideoutManager()
        {
            // Initialize on first access
            RefreshTrackedItems();
        }

        /// <summary>
        /// Refreshes the tracked items based on current config selections.
        /// Call when user changes hideout selections.
        /// </summary>
        public void RefreshTrackedItems()
        {
            _items.Clear();

            if (!App.Config.Hideout.Enabled)
                return;

            if (TarkovDataManager.HideoutData is null || TarkovDataManager.HideoutData.Count == 0)
            {
                DebugLogger.LogDebug("[HideoutManager] No hideout data available");
                return;
            }

            foreach (var selection in App.Config.Hideout.Selected)
            {
                // selection.Key = "stationId:level" (e.g., "5d484fc0654e76006657e0ab:2")
                var parts = selection.Key.Split(':');
                if (parts.Length != 2)
                    continue;

                var stationId = parts[0];
                if (!int.TryParse(parts[1], out var level))
                    continue;

                if (!TarkovDataManager.HideoutData.TryGetValue(stationId, out var station))
                    continue;

                var levelData = station.Levels?.FirstOrDefault(l => l.Level == level);
                if (levelData?.ItemRequirements == null)
                    continue;

                foreach (var req in levelData.ItemRequirements)
                {
                    if (req.Item?.Id != null)
                    {
                        // If item already exists, aggregate the count
                        _items.AddOrUpdate(
                            req.Item.Id,
                            new TrackedHideoutItem
                            {
                                ItemId = req.Item.Id,
                                ItemName = req.Item.ShortName ?? req.Item.Name ?? "Unknown",
                                StationName = station.Name ?? "Unknown",
                                Level = level,
                                CountRequired = req.Count
                            },
                            (key, existing) =>
                            {
                                // Aggregate count if same item needed for multiple upgrades
                                existing.CountRequired += req.Count;
                                existing.StationName += $", {station.Name} Lv{level}";
                                return existing;
                            });
                    }
                }
            }

            DebugLogger.LogDebug($"[HideoutManager] Tracking {_items.Count} hideout items from {App.Config.Hideout.Selected.Count} selected upgrades");
        }

        /// <summary>
        /// Check if an item ID is required for a tracked hideout upgrade.
        /// Items marked as "found" are excluded.
        /// </summary>
        public bool IsHideoutItem(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !App.Config.Hideout.Enabled)
                return false;
            if (App.Config.Hideout.FoundItems.ContainsKey(itemId))
                return false;
            return _items.ContainsKey(itemId);
        }

        /// <summary>
        /// Get info about a hideout item requirement.
        /// </summary>
        public TrackedHideoutItem GetItemInfo(string itemId)
        {
            _items.TryGetValue(itemId, out var info);
            return info;
        }

        /// <summary>
        /// Get all currently tracked items for UI display.
        /// </summary>
        public IReadOnlyDictionary<string, TrackedHideoutItem> GetTrackedItems() => _items;

        /// <summary>
        /// Get the count of tracked items.
        /// </summary>
        public int TrackedItemCount => _items.Count;
    }

    /// <summary>
    /// Information about a tracked hideout item requirement.
    /// </summary>
    public class TrackedHideoutItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string ItemId { get; set; }
        public string ItemName { get; set; }
        public string StationName { get; set; }
        public int Level { get; set; }
        public int CountRequired { get; set; }

        /// <summary>
        /// Whether this item has been marked as "found" by the user.
        /// Found items are excluded from radar/ESP tracking.
        /// </summary>
        public bool IsFound
        {
            get => App.Config.Hideout.FoundItems.ContainsKey(ItemId);
            set
            {
                var currentlyFound = App.Config.Hideout.FoundItems.ContainsKey(ItemId);
                if (currentlyFound != value)
                {
                    if (value)
                        App.Config.Hideout.FoundItems.TryAdd(ItemId, 0);
                    else
                        App.Config.Hideout.FoundItems.TryRemove(ItemId, out _);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFound)));
                }
            }
        }
    }
}
