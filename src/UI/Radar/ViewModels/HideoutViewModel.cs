/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Hideout;
using LoneEftDmaRadar.UI.Data;
using LoneEftDmaRadar.UI.Loot;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class HideoutViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Static instance for access from refresh handler.
        /// </summary>
        public static HideoutViewModel Instance { get; private set; }

        public HideoutViewModel()
        {
            Instance = this;
            InitializeStations();
        }

        public ObservableCollection<HideoutStationEntry> Stations { get; } = new();
        public ObservableCollection<TrackedHideoutItem> TrackedItems { get; } = new();

        public bool Enabled
        {
            get => App.Config.Hideout.Enabled;
            set
            {
                if (App.Config.Hideout.Enabled != value)
                {
                    App.Config.Hideout.Enabled = value;
                    HideoutManager.Instance.RefreshTrackedItems();
                    RefreshTrackedItems();
                    OnPropertyChanged(nameof(Enabled));
                    OnPropertyChanged(nameof(TrackedItemCount));
                }
            }
        }

        public bool ShowHideoutItems
        {
            get => App.Config.Hideout.ShowHideoutItems;
            set
            {
                if (App.Config.Hideout.ShowHideoutItems != value)
                {
                    App.Config.Hideout.ShowHideoutItems = value;
                    LootFilter.ShowHideoutItems = value;
                    OnPropertyChanged(nameof(ShowHideoutItems));
                }
            }
        }

        public int TrackedItemCount => HideoutManager.Instance?.TrackedItemCount ?? 0;

        private void InitializeStations()
        {
            if (TarkovDataManager.HideoutData is null || TarkovDataManager.HideoutData.Count == 0)
                return;

            foreach (var station in TarkovDataManager.HideoutData.Values.OrderBy(s => s.Name))
            {
                var entry = new HideoutStationEntry(station);
                entry.LevelChanged += OnLevelChanged;
                Stations.Add(entry);
            }

            // Initialize LootFilter setting
            LootFilter.ShowHideoutItems = App.Config.Hideout.ShowHideoutItems;

            RefreshTrackedItems();
        }

        /// <summary>
        /// Reloads stations from TarkovDataManager after a data refresh.
        /// </summary>
        public void ReloadStations()
        {
            Stations.Clear();
            InitializeStations();
            OnPropertyChanged(nameof(Stations));
        }

        private void OnLevelChanged(object sender, EventArgs e)
        {
            HideoutManager.Instance.RefreshTrackedItems();
            RefreshTrackedItems();
        }

        private void RefreshTrackedItems()
        {
            TrackedItems.Clear();

            if (HideoutManager.Instance is null)
                return;

            foreach (var item in HideoutManager.Instance.GetTrackedItems().Values.OrderBy(x => x.ItemName))
            {
                TrackedItems.Add(item);
            }

            OnPropertyChanged(nameof(TrackedItemCount));
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
