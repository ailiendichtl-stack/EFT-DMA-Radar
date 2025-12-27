/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Data
{
    /// <summary>
    /// UI entry for a hideout station (parent node in TreeView).
    /// </summary>
    public sealed class HideoutStationEntry
    {
        public event EventHandler LevelChanged;

        public HideoutStationEntry(TarkovDataManager.HideoutStationElement station)
        {
            Id = station.Id;
            Name = station.Name ?? "Unknown Station";

            Levels = new ObservableCollection<HideoutLevelEntry>(
                station.Levels?
                    .OrderBy(l => l.Level)
                    .Select(l => new HideoutLevelEntry(station.Id, station.Name, l))
                ?? Enumerable.Empty<HideoutLevelEntry>()
            );

            foreach (var level in Levels)
            {
                level.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(HideoutLevelEntry.IsTracked))
                    {
                        LevelChanged?.Invoke(this, EventArgs.Empty);
                    }
                };
            }
        }

        public string Id { get; }
        public string Name { get; }
        public ObservableCollection<HideoutLevelEntry> Levels { get; }
    }

    /// <summary>
    /// UI entry for a hideout station level (child node in TreeView).
    /// </summary>
    public sealed class HideoutLevelEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly string _stationId;
        private readonly string _key;

        public HideoutLevelEntry(string stationId, string stationName, TarkovDataManager.HideoutLevelElement level)
        {
            _stationId = stationId;
            _key = $"{stationId}:{level.Level}";
            Level = level.Level;
            StationName = stationName ?? "Unknown";
            ItemCount = level.ItemRequirements?.Count ?? 0;
            _isTracked = App.Config.Hideout.Selected.ContainsKey(_key);
        }

        public int Level { get; }
        public string StationName { get; }
        public int ItemCount { get; }

        public string LevelDisplay => $"Level {Level}";
        public string ItemSummary => $"{ItemCount} item{(ItemCount != 1 ? "s" : "")} needed";

        private bool _isTracked;
        public bool IsTracked
        {
            get => _isTracked;
            set
            {
                if (_isTracked != value)
                {
                    _isTracked = value;
                    if (_isTracked)
                    {
                        App.Config.Hideout.Selected.TryAdd(_key, 0);
                    }
                    else
                    {
                        App.Config.Hideout.Selected.TryRemove(_key, out _);
                    }
                    OnPropertyChanged(nameof(IsTracked));
                }
            }
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
