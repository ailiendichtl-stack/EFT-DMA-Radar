/*
 * Lone EFT DMA Radar
 * Loot List Widget ViewModel
 *
 * Displays live loot in a sortable, searchable list.
 * Double-click items to ping them on the radar map.
 */

using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Radar.Maps;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class LootListViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly PeriodicTimer _refreshTimer;
        private CancellationTokenSource _timerCts;
        private string _searchText = "";
        private int _minValue;
        private bool _autoRefresh = true;
        private string _currentSortProperty = "Price";
        private ListSortDirection? _currentSortDirection = ListSortDirection.Descending;

        public LootListViewModel()
        {
            _minValue = App.Config.Loot.MinValue;
            RefreshCommand = new RelayCommand(_ => RefreshLoot());
            _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            StartAutoRefresh();
        }

        /// <summary>
        /// Filtered and sorted loot items.
        /// </summary>
        public ObservableCollection<LootEntry> FilteredLoot { get; } = new();

        /// <summary>
        /// Search text for filtering items by name.
        /// </summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged(nameof(SearchText));
                    RefreshLoot();
                }
            }
        }

        /// <summary>
        /// Minimum value filter.
        /// </summary>
        public int MinValue
        {
            get => _minValue;
            set
            {
                if (_minValue != value)
                {
                    _minValue = value;
                    OnPropertyChanged(nameof(MinValue));
                    RefreshLoot();
                }
            }
        }

        /// <summary>
        /// Enable auto-refresh of loot list.
        /// </summary>
        public bool AutoRefresh
        {
            get => _autoRefresh;
            set
            {
                if (_autoRefresh != value)
                {
                    _autoRefresh = value;
                    OnPropertyChanged(nameof(AutoRefresh));
                    if (value) StartAutoRefresh();
                    else StopAutoRefresh();
                }
            }
        }

        /// <summary>
        /// Total number of items shown.
        /// </summary>
        public int TotalItems => FilteredLoot.Count;

        /// <summary>
        /// Total value of all items shown.
        /// </summary>
        public string TotalValue => FormatNumber(FilteredLoot.Sum(x => x.Price));

        /// <summary>
        /// Command to manually refresh the loot list.
        /// </summary>
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Sort the loot list by a property.
        /// </summary>
        public void SortBy(string propertyName, ListSortDirection? direction)
        {
            _currentSortProperty = propertyName;
            _currentSortDirection = direction == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;
            RefreshLoot();
        }

        /// <summary>
        /// Refresh the loot list from game memory.
        /// </summary>
        public void RefreshLoot()
        {
            try
            {
                var loot = Memory.Loot?.FilteredLoot;
                var localPlayer = Memory.LocalPlayer;

                if (loot == null || localPlayer == null)
                {
                    FilteredLoot.Clear();
                    UpdateTotals();
                    return;
                }

                var playerPos = localPlayer.Position;
                var searchLower = _searchText?.ToLowerInvariant() ?? "";

                var entries = loot
                    .Where(item => item.Price >= _minValue)
                    .Where(item => string.IsNullOrEmpty(searchLower) ||
                        item.Name?.ToLowerInvariant().Contains(searchLower) == true)
                    .Select(item => new LootEntry(item, playerPos));

                // Apply sorting
                entries = _currentSortProperty switch
                {
                    "Name" => _currentSortDirection == ListSortDirection.Ascending
                        ? entries.OrderBy(x => x.Name)
                        : entries.OrderByDescending(x => x.Name),
                    "Distance" => _currentSortDirection == ListSortDirection.Ascending
                        ? entries.OrderBy(x => x.Distance)
                        : entries.OrderByDescending(x => x.Distance),
                    _ => _currentSortDirection == ListSortDirection.Ascending
                        ? entries.OrderBy(x => x.Price)
                        : entries.OrderByDescending(x => x.Price)
                };

                // Update on UI thread
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    FilteredLoot.Clear();
                    foreach (var entry in entries.Take(500)) // Limit to prevent UI slowdown
                    {
                        FilteredLoot.Add(entry);
                    }
                    UpdateTotals();
                });
            }
            catch { }
        }

        private void UpdateTotals()
        {
            OnPropertyChanged(nameof(TotalItems));
            OnPropertyChanged(nameof(TotalValue));
        }

        private async void StartAutoRefresh()
        {
            StopAutoRefresh();
            _timerCts = new CancellationTokenSource();
            try
            {
                while (await _refreshTimer.WaitForNextTickAsync(_timerCts.Token))
                {
                    if (_autoRefresh)
                        RefreshLoot();
                }
            }
            catch (OperationCanceledException) { }
        }

        private void StopAutoRefresh()
        {
            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = null;
        }

        private static string FormatNumber(int value)
        {
            if (value >= 1_000_000)
                return $"{value / 1_000_000.0:F1}M";
            if (value >= 1_000)
                return $"{value / 1_000.0:F0}K";
            return value.ToString();
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>
        /// Represents a loot item entry in the list.
        /// </summary>
        public sealed class LootEntry
        {
            public LootEntry(LootItem lootItem, Vector3 playerPos)
            {
                LootItem = lootItem;
                Name = lootItem.Name ?? "Unknown";
                Price = lootItem.Price;
                Distance = Vector3.Distance(playerPos, lootItem.Position);
                IsImportant = lootItem.Important;
                IsQuestItem = lootItem.IsQuestItem;
                ItemType = lootItem switch
                {
                    LootCorpse => "Corpse",
                    LootAirdrop => "Airdrop",
                    StaticLootContainer => "Container",
                    _ => "Loose"
                };
            }

            /// <summary>
            /// Reference to the actual loot item for pinging.
            /// </summary>
            public LootItem LootItem { get; }

            public string Name { get; }
            public int Price { get; }
            public float Distance { get; }
            public bool IsImportant { get; }
            public bool IsQuestItem { get; }
            public string ItemType { get; }

            public string FormattedPrice => FormatNumber(Price);
            public string FormattedDistance => $"{Distance:F0}m";

            private static string FormatNumber(int value)
            {
                if (value >= 1_000_000)
                    return $"{value / 1_000_000.0:F1}M";
                if (value >= 1_000)
                    return $"{value / 1_000.0:F0}K";
                return value.ToString();
            }
        }

        /// <summary>
        /// Simple relay command implementation.
        /// </summary>
        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object> _execute;
            public RelayCommand(Action<object> execute) => _execute = execute;
            public event EventHandler CanExecuteChanged;
            public bool CanExecute(object parameter) => true;
            public void Execute(object parameter) => _execute(parameter);
        }
    }
}
