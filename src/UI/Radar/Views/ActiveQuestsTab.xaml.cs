/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class ActiveQuestsTab : UserControl
    {
        private readonly ActiveQuestsViewModel _vm;

        public ActiveQuestsTab()
        {
            InitializeComponent();
            _vm = new ActiveQuestsViewModel();
            DataContext = _vm;

            // Initial load
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm.RefreshQuests();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            _vm.RefreshQuests();
        }
    }
}
