using LoneEftDmaRadar.UI.Radar.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Radar.Views
{
    /// <summary>
    /// Interaction logic for LootListTab.xaml
    /// Displays live loot in a sortable list with radar ping integration.
    /// </summary>
    public partial class LootListTab : UserControl
    {
        public LootListViewModel ViewModel { get; }

        public LootListTab()
        {
            InitializeComponent();
            ViewModel = new LootListViewModel();
            DataContext = ViewModel;
        }

        /// <summary>
        /// Handle double-click to ping item on radar.
        /// </summary>
        private void LootGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LootGrid.SelectedItem is LootListViewModel.LootEntry entry && entry.LootItem != null)
            {
                RadarViewModel.PingMapEntity(entry.LootItem);
            }
        }

        /// <summary>
        /// Handle column sorting.
        /// </summary>
        private void LootGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true;
            ViewModel.SortBy(e.Column.SortMemberPath, e.Column.SortDirection);

            // Toggle sort direction for next click
            e.Column.SortDirection = e.Column.SortDirection == System.ComponentModel.ListSortDirection.Ascending
                ? System.ComponentModel.ListSortDirection.Descending
                : System.ComponentModel.ListSortDirection.Ascending;
        }
    }
}
