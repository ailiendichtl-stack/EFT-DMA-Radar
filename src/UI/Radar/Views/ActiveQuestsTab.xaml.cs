/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class ActiveQuestsTab : UserControl
    {
        private readonly ActiveQuestsViewModel _vm;
        private readonly DispatcherTimer _autoRefreshTimer;

        public ActiveQuestsTab()
        {
            InitializeComponent();
            _vm = new ActiveQuestsViewModel();
            DataContext = _vm;

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15)
            };
            _autoRefreshTimer.Tick += async (_, _) =>
            {
                _autoRefreshTimer.Stop();
                try
                {
                    await Task.Run(() => _vm.RefreshQuests());
                }
                finally
                {
                    _autoRefreshTimer.Start();
                }
            };

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => _vm.RefreshQuests());
            _autoRefreshTimer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _autoRefreshTimer.Stop();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() => _vm.RefreshQuests());
        }
    }
}
