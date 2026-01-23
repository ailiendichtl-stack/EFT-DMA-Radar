/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.Views;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class LootFiltersViewModel : INotifyPropertyChanged
    {
        #region Startup

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string n = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public LootFiltersViewModel(LootFiltersTab parent)
        {
            FilterNames = new ObservableCollection<string>(App.Config.LootFilters.Filters.Keys);
            AvailableItems = new ObservableCollection<TarkovMarketItem>(
                TarkovDataManager.AllItems.Values.OrderBy(x => x.Name));

            AddFilterCommand = new SimpleCommand(OnAddFilter);
            RenameFilterCommand = new SimpleCommand(OnRenameFilter);
            DeleteFilterCommand = new SimpleCommand(OnDeleteFilter);
            RestoreDefaultsCommand = new SimpleCommand(OnRestoreDefaults);
            ResetItemColorsCommand = new SimpleCommand(OnResetItemColors);

            AddEntryCommand = new SimpleCommand(OnAddEntry);
            DeleteEntryCommand = new SimpleCommand(OnDeleteEntry);

            if (FilterNames.Any())
                SelectedFilterName = App.Config.LootFilters.Selected;
            EnsureFirstItemSelected();
            RefreshLootFilter();
            parent.IsVisibleChanged += Parent_IsVisibleChanged;

            // Subscribe to data updates to refresh available items
            TarkovDataManager.DataUpdated += OnDataUpdated;
        }

        private void OnDataUpdated(object sender, EventArgs e)
        {
            // Refresh available items when data is updated
            App.Current?.Dispatcher?.BeginInvoke(() =>
            {
                AvailableItems.Clear();
                foreach (var item in TarkovDataManager.AllItems.Values.OrderBy(x => x.Name))
                {
                    AvailableItems.Add(item);
                }
            });
        }

        private void Parent_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && !visible)
            {
                RefreshLootFilter();
            }
        }

        #endregion

        #region Top Section - Filters

        private bool _currentFilterEnabled;
        public bool CurrentFilterEnabled
        {
            get => _currentFilterEnabled;
            set
            {
                if (_currentFilterEnabled == value) return;
                _currentFilterEnabled = value;
                // persist to config
                App.Config.LootFilters.Filters[SelectedFilterName].Enabled = value;
                OnPropertyChanged();
            }
        }

        private string _currentFilterColor;
        public string CurrentFilterColor
        {
            get => _currentFilterColor;
            set
            {
                if (_currentFilterColor == value) return;
                _currentFilterColor = value;
                // persist to config
                App.Config.LootFilters.Filters[SelectedFilterName].Color = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<string> FilterNames { get; } // ComboBox of filter names
        private string _selectedFilterName;
        public string SelectedFilterName
        {
            get => _selectedFilterName;
            set
            {
                if (_selectedFilterName == value) return;
                _selectedFilterName = value;
                App.Config.LootFilters.Selected = value;
                var userFilter = App.Config.LootFilters.Filters[value];
                CurrentFilterEnabled = userFilter.Enabled;
                CurrentFilterColor = userFilter.Color;
                Entries = userFilter.Entries;
                // Assign parent filter reference to each entry
                foreach (var entry in userFilter.Entries)
                    entry.ParentFilter = userFilter;
                OnPropertyChanged();
            }
        }

        public ICommand AddFilterCommand { get; }
        private void OnAddFilter()
        {
            var dlg = new InputBoxWindow("Loot Filter", "Enter the name of the new loot filter:");
            if (dlg.ShowDialog() != true)
                return; // user cancelled
            var name = dlg.InputText;
            if (string.IsNullOrEmpty(name)) return;

            try
            {
                if (!App.Config.LootFilters.Filters.TryAdd(name, new UserLootFilter
                {
                    Enabled = true,
                    Entries = new()
                }))
                    throw new InvalidOperationException("That filter already exists.");

                FilterNames.Add(name);
                SelectedFilterName = name;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    $"ERROR Adding Filter: {ex.Message}",
                    "Loot Filter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public ICommand RenameFilterCommand { get; }
        private void OnRenameFilter()
        {
            var oldName = SelectedFilterName;
            if (string.IsNullOrEmpty(oldName)) return;

            var dlg = new InputBoxWindow($"Rename {oldName}", "Enter the new filter name:");
            if (dlg.ShowDialog() != true)
                return; // user cancelled
            var newName = dlg.InputText;
            if (string.IsNullOrEmpty(newName)) return;

            try
            {
                if (App.Config.LootFilters.Filters.TryGetValue(oldName, out var filter)
                    && App.Config.LootFilters.Filters.TryAdd(newName, filter)
                    && App.Config.LootFilters.Filters.TryRemove(oldName, out _))
                {
                    var idx = FilterNames.IndexOf(oldName);
                    FilterNames[idx] = newName;
                    SelectedFilterName = newName;
                }
                else
                {
                    throw new InvalidOperationException("Rename failed.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    $"ERROR Renaming Filter: {ex.Message}",
                    "Loot Filter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public ICommand DeleteFilterCommand { get; }
        private void OnDeleteFilter()
        {
            var name = SelectedFilterName;
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    "No loot filter selected!",
                    "Loot Filter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                MainWindow.Instance,
                $"Are you sure you want to delete '{name}'?",
                "Loot Filter",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (!App.Config.LootFilters.Filters.TryRemove(name, out _))
                    throw new InvalidOperationException("Remove failed.");

                // ensure at least one filter remains
                if (App.Config.LootFilters.Filters.IsEmpty)
                    App.Config.LootFilters.Filters.TryAdd("default", new UserLootFilter
                    {
                        Enabled = true,
                        Entries = new()
                    });

                FilterNames.Clear();
                foreach (var key in App.Config.LootFilters.Filters.Keys)
                    FilterNames.Add(key);

                SelectedFilterName = FilterNames[0];
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    $"ERROR Deleting Filter: {ex.Message}",
                    "Loot Filter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public ICommand RestoreDefaultsCommand { get; }
        private void OnRestoreDefaults()
        {
            var result = MessageBox.Show(
                MainWindow.Instance,
                "This will replace ALL your current loot filters with the default presets.\n\n" +
                "Are you sure you want to continue?",
                "Restore Default Filters",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Clear existing filters
                App.Config.LootFilters.Filters.Clear();

                // Add default filters
                App.Config.LootFilters.Filters.TryAdd("Value Items", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FFFF0000",
                    Entries = DefaultFilters.ValueItems
                });
                App.Config.LootFilters.Filters.TryAdd("Top Ammo", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FF44A8FF",
                    Entries = DefaultFilters.TopAmmo
                });
                App.Config.LootFilters.Filters.TryAdd("Valuable Keys", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FFEF40FF",
                    Entries = DefaultFilters.ValuableKeys
                });
                App.Config.LootFilters.Filters.TryAdd("Kappa Items", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FFFF9421",
                    Entries = DefaultFilters.KappaItems
                });
                App.Config.LootFilters.Filters.TryAdd("Prestige Items", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FF696C1B",
                    Entries = DefaultFilters.PrestigeItems
                });
                App.Config.LootFilters.Filters.TryAdd("Quest Items", new UserLootFilter
                {
                    Enabled = true,
                    Color = "#FF16641E",
                    Entries = DefaultFilters.QuestItems
                });

                // Update UI
                FilterNames.Clear();
                foreach (var key in App.Config.LootFilters.Filters.Keys)
                    FilterNames.Add(key);

                App.Config.LootFilters.Selected = "Value Items";
                SelectedFilterName = "Value Items";

                RefreshLootFilter();

                MessageBox.Show(
                    MainWindow.Instance,
                    "Default filters have been restored successfully!",
                    "Restore Default Filters",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    $"ERROR Restoring Defaults: {ex.Message}",
                    "Loot Filter",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public ICommand ResetItemColorsCommand { get; }
        private void OnResetItemColors()
        {
            if (string.IsNullOrEmpty(SelectedFilterName) || !App.Config.LootFilters.Filters.TryGetValue(SelectedFilterName, out var filter))
                return;

            var result = MessageBox.Show(
                MainWindow.Instance,
                $"This will reset all item colors in '{SelectedFilterName}' to inherit from the filter's parent color.\n\n" +
                "Items will use the filter color unless you set a custom color for individual items.\n\n" +
                "Are you sure you want to continue?",
                "Reset Item Colors",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                int count = 0;
                foreach (var entry in filter.Entries)
                {
                    if (!string.IsNullOrEmpty(entry.ExplicitColor))
                    {
                        entry.ExplicitColor = null;
                        count++;
                    }
                }

                MessageBox.Show(
                    MainWindow.Instance,
                    $"Successfully reset {count} item color(s) to inherit from parent filter.",
                    "Reset Item Colors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    MainWindow.Instance,
                    $"ERROR Resetting Colors: {ex.Message}",
                    "Reset Item Colors",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Bottom Section - Entries

        public ObservableCollection<TarkovMarketItem> AvailableItems { get; } // List of items
        private ICollectionView _filteredItems;
        public ICollectionView FilteredItems // Filtered list of items
        {
            get
            {
                if (_filteredItems == null)
                {
                    // create the view once
                    _filteredItems = CollectionViewSource.GetDefaultView(AvailableItems);
                    _filteredItems.Filter = FilterPredicate;
                }
                return _filteredItems;
            }
        }

        private TarkovMarketItem _selectedItemToAdd;
        public TarkovMarketItem SelectedItemToAdd
        {
            get => _selectedItemToAdd;
            set { if (_selectedItemToAdd != value) { _selectedItemToAdd = value; OnPropertyChanged(); } }
        }

        private void EnsureFirstItemSelected()
        {
            var first = FilteredItems.Cast<TarkovMarketItem>().FirstOrDefault();
            SelectedItemToAdd = first;
        }

        private string _itemSearchText;
        public string ItemSearchText
        {
            get => _itemSearchText;
            set
            {
                if (_itemSearchText == value) return;
                _itemSearchText = value;
                OnPropertyChanged();
                _filteredItems.Refresh(); // refresh the filter
                EnsureFirstItemSelected();
            }
        }

        public ICommand AddEntryCommand { get; }
        private void OnAddEntry()
        {
            if (SelectedItemToAdd == null) return;

            var userFilter = App.Config.LootFilters.Filters[SelectedFilterName];
            var entry = new LootFilterEntry
            {
                ItemID = SelectedItemToAdd.BsgId,
                ParentFilter = userFilter
            };

            Entries.Add(entry);
            SelectedItemToAdd = null;
        }

        public ICommand DeleteEntryCommand { get; }
        private void OnDeleteEntry()
        {
            // This command will be called from the DataGrid context menu or key binding
            // The selected entry will be passed as a parameter
        }

        public void DeleteEntry(LootFilterEntry entry)
        {
            if (entry == null) return;
            Entries.Remove(entry);
        }

        public IEnumerable<LootFilterEntryType> FilterEntryTypes { get; } = Enum // ComboBox of Entry Types within DataGrid
            .GetValues<LootFilterEntryType>()
            .Cast<LootFilterEntryType>();

        private ObservableCollection<LootFilterEntry> _entries = new();
        public ObservableCollection<LootFilterEntry> Entries // Entries grid
        {
            get => _entries;
            set
            {
                if (_entries != value)
                {
                    _entries = value;
                    OnPropertyChanged(nameof(Entries));
                }
            }
        }

        #endregion

        #region Misc

        private bool FilterPredicate(object obj)
        {
            if (string.IsNullOrWhiteSpace(_itemSearchText))
                return true;

            var itm = obj as TarkovMarketItem;
            return itm?.Name
                       .IndexOf(_itemSearchText,
                                StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Refreshes the Loot Filter.
        /// Should be called at startup and during validation.
        /// </summary>
        private static void RefreshLootFilter()
        {
            // Remove old filters (if any)
            foreach (var item in TarkovDataManager.AllItems.Values)
                item.SetFilter(null);

            // Ensure every entry has its ParentFilter populated.
            // This is required for inheritance (e.g. color) and for any logic that relies on ParentFilter.
            foreach (var filter in App.Config.LootFilters.Filters.Values)
            {
                if (filter?.Entries is null)
                    continue;

                foreach (var entry in filter.Entries)
                    entry.ParentFilter = filter;
            }

            // Set new filters
            var currentFilters = App.Config.LootFilters.Filters
                .Values
                .Where(x => x.Enabled)
                .SelectMany(x => x.Entries);
            if (!currentFilters.Any())
                return;
            foreach (var filter in currentFilters)
            {
                if (string.IsNullOrEmpty(filter.ItemID))
                    continue;
                if (TarkovDataManager.AllItems.TryGetValue(filter.ItemID, out var item))
                    item.SetFilter(filter);
            }
        }

        #endregion
    }
}
