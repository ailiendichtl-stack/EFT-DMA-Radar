/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows.Controls;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class HideoutTab : UserControl
    {
        public HideoutTab()
        {
            InitializeComponent();
            DataContext = new HideoutViewModel();
        }
    }
}
