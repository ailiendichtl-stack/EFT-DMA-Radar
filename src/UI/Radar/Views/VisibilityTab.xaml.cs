using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows.Controls;

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
