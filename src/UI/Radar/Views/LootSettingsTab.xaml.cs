using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows.Controls;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class LootSettingsTab : UserControl
    {
        public LootSettingsViewModel ViewModel { get; }
        public LootSettingsTab()
        {
            InitializeComponent();
            DataContext = ViewModel = new LootSettingsViewModel();
        }
    }
}
