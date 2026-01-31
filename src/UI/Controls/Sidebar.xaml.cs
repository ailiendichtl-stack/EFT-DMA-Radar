/*
 * Twilight PVE Radar - WPF Modular GUI
 * Sidebar: Auto-collapsing navigation sidebar for panel management
 */

using System.Windows.Controls;

namespace LoneEftDmaRadar.UI.Controls
{
    /// <summary>
    /// Auto-collapsing sidebar that provides navigation to floating panels.
    /// Expands when mouse approaches left edge, collapses after timeout.
    /// </summary>
    public partial class Sidebar : UserControl
    {
        public Sidebar()
        {
            InitializeComponent();
        }
    }
}
