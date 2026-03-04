using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class LootSettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public LootSettingsViewModel()
        {
            LootFilter.ShowQuestItems = App.Config.Loot.ShowQuestItems;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            Memory.Loot?.RefreshFilter();
        }

        // ─── Loot toggles ──────────────────────────────────────────────────────

        public bool ShowLoot
        {
            get => App.Config.Loot.Enabled;
            set
            {
                if (App.Config.Loot.Enabled != value)
                {
                    App.Config.Loot.Enabled = value;
                    OnPropertyChanged(nameof(ShowLoot));
                }
            }
        }

        public bool ShowStaticContainers
        {
            get => App.Config.Containers.Enabled;
            set
            {
                if (App.Config.Containers.Enabled != value)
                {
                    App.Config.Containers.Enabled = value;
                    OnPropertyChanged(nameof(ShowStaticContainers));
                }
            }
        }

        // ─── Value thresholds ───────────────────────────────────────────────────

        public int RegularValue
        {
            get => App.Config.Loot.MinValue;
            set
            {
                if (App.Config.Loot.MinValue != value)
                {
                    App.Config.Loot.MinValue = value;
                    OnPropertyChanged(nameof(RegularValue));
                }
            }
        }

        public int ValuableValue
        {
            get => App.Config.Loot.MinValueValuable;
            set
            {
                if (App.Config.Loot.MinValueValuable != value)
                {
                    App.Config.Loot.MinValueValuable = value;
                    OnPropertyChanged(nameof(ValuableValue));
                }
            }
        }

        public bool PricePerSlot
        {
            get => App.Config.Loot.PricePerSlot;
            set
            {
                if (App.Config.Loot.PricePerSlot != value)
                {
                    App.Config.Loot.PricePerSlot = value;
                    OnPropertyChanged(nameof(PricePerSlot));
                }
            }
        }

        // ─── Price source ───────────────────────────────────────────────────────

        public bool IsFleaPrices
        {
            get => App.Config.Loot.PriceMode == LootPriceMode.FleaMarket;
            set
            {
                if (value && App.Config.Loot.PriceMode != LootPriceMode.FleaMarket)
                {
                    App.Config.Loot.PriceMode = LootPriceMode.FleaMarket;
                    OnPropertyChanged(nameof(IsTraderPrices));
                }
            }
        }

        public bool IsTraderPrices
        {
            get => App.Config.Loot.PriceMode == LootPriceMode.Trader;
            set
            {
                if (value && App.Config.Loot.PriceMode != LootPriceMode.Trader)
                {
                    App.Config.Loot.PriceMode = LootPriceMode.Trader;
                    OnPropertyChanged(nameof(IsFleaPrices));
                }
            }
        }

        public bool PveMode
        {
            get => App.Config.Loot.PveMode;
            set
            {
                if (App.Config.Loot.PveMode != value)
                {
                    App.Config.Loot.PveMode = value;
                    OnPropertyChanged(nameof(PveMode));
                    _ = RefreshPveModeDataAsync();
                }
            }
        }

        private static async Task RefreshPveModeDataAsync()
        {
            try
            {
                await TarkovDataManager.RefreshDataAsync();
            }
            catch (Exception ex)
            {
                UI.Misc.DebugLogger.LogDebug($"[PveMode] Failed to refresh price data: {ex.Message}");
            }
        }

        // ─── Loot filters ───────────────────────────────────────────────────────

        public bool HideCorpses
        {
            get => App.Config.Loot.HideCorpses;
            set
            {
                if (App.Config.Loot.HideCorpses != value)
                {
                    App.Config.Loot.HideCorpses = value;
                    OnPropertyChanged(nameof(HideCorpses));
                }
            }
        }

        public bool ShowMeds
        {
            get => LootFilter.ShowMeds;
            set
            {
                if (LootFilter.ShowMeds != value)
                {
                    LootFilter.ShowMeds = value;
                    OnPropertyChanged(nameof(ShowMeds));
                }
            }
        }

        public bool ShowFood
        {
            get => LootFilter.ShowFood;
            set
            {
                if (LootFilter.ShowFood != value)
                {
                    LootFilter.ShowFood = value;
                    OnPropertyChanged(nameof(ShowFood));
                }
            }
        }

        public bool ShowBackpacks
        {
            get => LootFilter.ShowBackpacks;
            set
            {
                if (LootFilter.ShowBackpacks != value)
                {
                    LootFilter.ShowBackpacks = value;
                    OnPropertyChanged(nameof(ShowBackpacks));
                }
            }
        }

        public bool ShowQuestItems
        {
            get => LootFilter.ShowQuestItems;
            set
            {
                if (LootFilter.ShowQuestItems != value)
                {
                    LootFilter.ShowQuestItems = value;
                    App.Config.Loot.ShowQuestItems = value;
                    OnPropertyChanged(nameof(ShowQuestItems));
                }
            }
        }

        // ─── Container settings ─────────────────────────────────────────────────

        public int ContainerDistance
        {
            get => (int)Math.Round(App.Config.Containers.DrawDistance);
            set
            {
                if (App.Config.Containers.DrawDistance != value)
                {
                    App.Config.Containers.DrawDistance = value;
                    OnPropertyChanged(nameof(ContainerDistance));
                }
            }
        }

        public int ContainerMinValue
        {
            get => App.Config.Containers.MinValue;
            set
            {
                if (App.Config.Containers.MinValue != value)
                {
                    App.Config.Containers.MinValue = value;
                    OnPropertyChanged(nameof(ContainerMinValue));
                }
            }
        }
    }
}
