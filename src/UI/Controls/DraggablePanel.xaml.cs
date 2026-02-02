/*
 * Twilight PVE Radar - WPF Modular GUI
 * DraggablePanel: Floating panel control with drag, resize, and close functionality
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using LoneEftDmaRadar.UI.Behaviors;

namespace LoneEftDmaRadar.UI.Controls
{
    /// <summary>
    /// A draggable, resizable floating panel for the modular GUI system.
    /// Place on a Canvas and use attached behaviors for drag/resize functionality.
    /// </summary>
    [ContentProperty(nameof(PanelContent))]
    public partial class DraggablePanel : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(DraggablePanel),
                new PropertyMetadata("Panel"));

        public static readonly DependencyProperty PanelContentProperty =
            DependencyProperty.Register(
                nameof(PanelContent),
                typeof(object),
                typeof(DraggablePanel),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsCollapsedProperty =
            DependencyProperty.Register(
                nameof(IsCollapsed),
                typeof(bool),
                typeof(DraggablePanel),
                new PropertyMetadata(false, OnIsCollapsedChanged));

        #endregion

        #region Private Fields

        private double _expandedHeight;
        private const double TitleBarHeight = 36;

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the title displayed in the panel's title bar.
        /// </summary>
        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        /// <summary>
        /// Gets or sets the content to display in the panel.
        /// </summary>
        public object PanelContent
        {
            get => GetValue(PanelContentProperty);
            set => SetValue(PanelContentProperty, value);
        }

        /// <summary>
        /// Gets or sets whether the panel content is collapsed (only title bar visible).
        /// </summary>
        public bool IsCollapsed
        {
            get => (bool)GetValue(IsCollapsedProperty);
            set => SetValue(IsCollapsedProperty, value);
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the close button is clicked.
        /// </summary>
        public event RoutedEventHandler CloseRequested;

        #endregion

        public DraggablePanel()
        {
            InitializeComponent();
            Loaded += DraggablePanel_Loaded;
        }

        private void DraggablePanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Set the TitleBar as the drag handle so dragging only works from title bar
            // This allows resize to work from the rest of the panel
            DraggableBehavior.SetDragHandle(this, TitleBar);

            // Fix for panels that were saved in collapsed state:
            // If height is at or near TitleBarHeight but IsCollapsed is false,
            // the panel was collapsed when saved. Restore to a reasonable height.
            if (!IsCollapsed && ActualHeight <= TitleBarHeight + 10)
            {
                // Panel was saved collapsed - restore to default height
                _expandedHeight = 400;
                Height = _expandedHeight;
            }
            else if (!IsCollapsed && ActualHeight > TitleBarHeight)
            {
                // Normal expanded panel - store the height
                _expandedHeight = ActualHeight;
            }
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsCollapsed)
            {
                // Store current height before collapsing (only if valid)
                if (ActualHeight > TitleBarHeight + 10)
                {
                    _expandedHeight = ActualHeight;
                }
                else if (_expandedHeight <= TitleBarHeight)
                {
                    // No valid expanded height stored, use default
                    _expandedHeight = 400;
                }
            }
            IsCollapsed = !IsCollapsed;
        }

        private static void OnIsCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DraggablePanel panel)
            {
                panel.UpdateCollapsedState((bool)e.NewValue);
            }
        }

        private void UpdateCollapsedState(bool isCollapsed)
        {
            if (isCollapsed)
            {
                // Store height if not already stored (and valid)
                if (_expandedHeight <= TitleBarHeight && ActualHeight > TitleBarHeight + 10)
                    _expandedHeight = ActualHeight;

                // Ensure we have a fallback expanded height
                if (_expandedHeight <= TitleBarHeight)
                    _expandedHeight = 400;

                // Collapse to just title bar
                Height = TitleBarHeight;
            }
            else
            {
                // Ensure we have a valid expanded height
                if (_expandedHeight <= TitleBarHeight)
                    _expandedHeight = 400;

                // Restore expanded height
                Height = _expandedHeight;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, e);

            // If no handler is attached, hide the panel
            if (CloseRequested == null)
                Visibility = Visibility.Collapsed;
        }

        private void ContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Handle mouse wheel scrolling directly to ensure it works over all content
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
    }
}
