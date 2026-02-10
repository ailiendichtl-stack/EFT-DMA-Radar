using System.Windows.Controls;
using LoneEftDmaRadar.UI.Radar.ViewModels;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    public partial class VisibilityTab : UserControl
    {
        public VisibilityViewModel ViewModel { get; }

        public VisibilityTab()
        {
            InitializeComponent();
            DataContext = ViewModel = new VisibilityViewModel();
        }
    }
}
