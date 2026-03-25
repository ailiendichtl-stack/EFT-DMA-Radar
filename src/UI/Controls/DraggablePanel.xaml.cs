/*
 * Twilight PVE Radar - WPF Modular GUI
 * DraggablePanel: Floating panel control with drag, resize, tab grouping, and close functionality
 */

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using LoneEftDmaRadar.UI.Behaviors;
using LoneEftDmaRadar.UI.ViewModels;

namespace LoneEftDmaRadar.UI.Controls
{
    /// <summary>
    /// A draggable, resizable floating panel for the modular GUI system.
    /// Supports single-content mode and tabbed mode (when panels are grouped).
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

        public static readonly DependencyProperty TabItemsProperty =
            DependencyProperty.Register(
                nameof(TabItems),
                typeof(ObservableCollection<TabItemModel>),
                typeof(DraggablePanel),
                new PropertyMetadata(null, OnTabItemsChanged));

        public static readonly DependencyProperty ActiveTabProperty =
            DependencyProperty.Register(
                nameof(ActiveTab),
                typeof(string),
                typeof(DraggablePanel),
                new PropertyMetadata(null, OnActiveTabChanged));

        public static readonly DependencyProperty IsDropHighlightedProperty =
            DependencyProperty.Register(
                nameof(IsDropHighlighted),
                typeof(bool),
                typeof(DraggablePanel),
                new PropertyMetadata(false, OnIsDropHighlightedChanged));

        #endregion

        #region Private Fields

        private double _expandedHeight;
        private const double TitleBarHeight = 36;
        private Point? _tabDragStartPoint;
        private TabItemModel _tabDragSource;
#pragma warning disable CS0414 // Reserved for tab reorder feature
        private bool _tabReorderActive;
#pragma warning restore CS0414
        private const double TabDetachVerticalThreshold = 24;

        #endregion

        #region Properties

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public object PanelContent
        {
            get => GetValue(PanelContentProperty);
            set => SetValue(PanelContentProperty, value);
        }

        public bool IsCollapsed
        {
            get => (bool)GetValue(IsCollapsedProperty);
            set => SetValue(IsCollapsedProperty, value);
        }

        public ObservableCollection<TabItemModel> TabItems
        {
            get => (ObservableCollection<TabItemModel>)GetValue(TabItemsProperty);
            set => SetValue(TabItemsProperty, value);
        }

        public string ActiveTab
        {
            get => (string)GetValue(ActiveTabProperty);
            set => SetValue(ActiveTabProperty, value);
        }

        public bool IsDropHighlighted
        {
            get => (bool)GetValue(IsDropHighlightedProperty);
            set => SetValue(IsDropHighlightedProperty, value);
        }

        #endregion

        #region Events

        public event RoutedEventHandler CloseRequested;
        public event EventHandler<string> TabDetachRequested;
        public event EventHandler<string> TabCloseRequested;

        #endregion

        private static int _zCounter;

        public DraggablePanel()
        {
            InitializeComponent();
            Loaded += DraggablePanel_Loaded;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            System.Windows.Controls.Canvas.SetZIndex(this, ++_zCounter);
        }

        private void DraggablePanel_Loaded(object sender, RoutedEventArgs e)
        {
            DraggableBehavior.SetDragHandle(this, TitleBar);

            if (!IsCollapsed && ActualHeight <= TitleBarHeight + 10)
            {
                _expandedHeight = 400;
                Height = _expandedHeight;
            }
            else if (!IsCollapsed && ActualHeight > TitleBarHeight)
            {
                _expandedHeight = ActualHeight;
            }
        }

        #region Collapse / Close

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsCollapsed)
            {
                if (ActualHeight > TitleBarHeight + 10)
                    _expandedHeight = ActualHeight;
                else if (_expandedHeight <= TitleBarHeight)
                    _expandedHeight = 400;
            }
            IsCollapsed = !IsCollapsed;
        }

        private static void OnIsCollapsedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DraggablePanel panel)
                panel.UpdateCollapsedState((bool)e.NewValue);
        }

        private void UpdateCollapsedState(bool isCollapsed)
        {
            if (isCollapsed)
            {
                if (_expandedHeight <= TitleBarHeight && ActualHeight > TitleBarHeight + 10)
                    _expandedHeight = ActualHeight;
                if (_expandedHeight <= TitleBarHeight)
                    _expandedHeight = 400;
                Height = TitleBarHeight;
            }
            else
            {
                if (_expandedHeight <= TitleBarHeight)
                    _expandedHeight = 400;
                Height = _expandedHeight;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, e);
            if (CloseRequested == null)
                Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Tab Mode

        private static void OnTabItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DraggablePanel panel) return;

            // Unsubscribe from old collection
            if (e.OldValue is ObservableCollection<TabItemModel> oldItems)
                oldItems.CollectionChanged -= panel.TabItems_CollectionChanged;

            // Subscribe to new collection
            if (e.NewValue is ObservableCollection<TabItemModel> newItems)
            {
                newItems.CollectionChanged += panel.TabItems_CollectionChanged;
                panel.UpdateTabMode(newItems.Count);
            }
            else
            {
                // Exiting tab mode — re-establish the PanelContent binding.
                // SwitchToTab() set ContentArea.Content directly via SetValue,
                // which destroyed the original XAML binding. ClearValue alone
                // won't restore it, so we recreate the binding explicitly.
                BindingOperations.SetBinding(
                    panel.ContentArea,
                    ContentPresenter.ContentProperty,
                    new Binding(nameof(PanelContent))
                    {
                        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(UserControl), 1)
                    });
                panel.UpdateTabMode(0);
            }
        }

        private void TabItems_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateTabMode(TabItems?.Count ?? 0);
        }

        private void UpdateTabMode(int tabCount)
        {
            bool isTabMode = tabCount > 1;

            // Toggle between single title and tab headers in the title bar
            if (SingleTitleArea != null)
                SingleTitleArea.Visibility = isTabMode ? Visibility.Collapsed : Visibility.Visible;
            if (TabStripItems != null)
                TabStripItems.Visibility = isTabMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void OnActiveTabChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not DraggablePanel panel) return;
            var tabName = e.NewValue as string;
            panel.SwitchToTab(tabName);
        }

        private void SwitchToTab(string tabName)
        {
            if (TabItems == null || string.IsNullOrEmpty(tabName)) return;

            // Update IsActive on all tabs
            foreach (var t in TabItems)
                t.IsActive = t.PanelName == tabName;

            var tab = TabItems.FirstOrDefault(t => t.PanelName == tabName);
            if (tab != null)
                ContentArea.Content = tab.Content;
        }

        private void TabHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TabItemModel tab)
            {
                ActiveTab = tab.PanelName;
                _tabDragStartPoint = e.GetPosition(TitleBar);
                _tabDragSource = tab;
                _tabReorderActive = false;
                fe.CaptureMouse();
                e.Handled = true;
            }
        }

        private void TabHeader_MouseMove(object sender, MouseEventArgs e)
        {
            if (_tabDragSource == null || _tabDragStartPoint == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelTabDrag(sender as FrameworkElement);
                return;
            }

            var pos = e.GetPosition(TitleBar);
            var deltaY = pos.Y - _tabDragStartPoint.Value.Y;

            // Vertical drag outside title bar → detach
            if (Math.Abs(deltaY) > TabDetachVerticalThreshold)
            {
                var panelName = _tabDragSource.PanelName;
                CancelTabDrag(sender as FrameworkElement);
                TabDetachRequested?.Invoke(this, panelName);
                return;
            }

            // Horizontal drag → reorder by checking which tab the cursor is over
            TryReorderTab(pos);
        }

        private void TryReorderTab(Point mousePosInTitleBar)
        {
            if (TabItems == null || _tabDragSource == null || TabItems.Count < 2) return;

            var sourceIndex = TabItems.IndexOf(_tabDragSource);
            if (sourceIndex < 0) return;

            // Walk the generated containers to find which one the mouse X falls within
            for (int i = 0; i < TabItems.Count; i++)
            {
                if (i == sourceIndex) continue;

                var container = TabStripItems.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                // Get the container's position relative to the title bar
                var containerPos = container.TranslatePoint(new Point(0, 0), TitleBar);
                double left = containerPos.X;
                double right = left + container.ActualWidth;
                double midpoint = (left + right) / 2;

                // If dragging right and cursor passed the midpoint of the next tab, swap
                // If dragging left and cursor passed the midpoint of the previous tab, swap
                if (mousePosInTitleBar.X >= left && mousePosInTitleBar.X <= right)
                {
                    bool shouldSwap = (i > sourceIndex && mousePosInTitleBar.X > midpoint) ||
                                      (i < sourceIndex && mousePosInTitleBar.X < midpoint);
                    if (shouldSwap)
                    {
                        _tabReorderActive = true;
                        TabItems.Move(sourceIndex, i);
                    }
                    break;
                }
            }
        }

        private void TabHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            CancelTabDrag(sender as FrameworkElement);
        }

        private void CancelTabDrag(FrameworkElement element)
        {
            _tabDragSource = null;
            _tabDragStartPoint = null;
            _tabReorderActive = false;
            element?.ReleaseMouseCapture();
        }

        private void TabCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string panelName)
            {
                TabCloseRequested?.Invoke(this, panelName);
                e.Handled = true;
            }
        }

        #endregion

        #region Drop Highlight

        private static void OnIsDropHighlightedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DraggablePanel panel && panel.DropHighlight != null)
                panel.DropHighlight.Opacity = (bool)e.NewValue ? 0.6 : 0;
        }

        #endregion

        private void ContentScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (sender is ScrollViewer scrollViewer && scrollViewer.IsLoaded)
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
            }
            catch
            {
                // Guard against layout race during content changes
            }
            e.Handled = true;
        }
    }
}
