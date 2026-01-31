/*
 * Twilight PVE Radar - WPF Modular GUI
 * SidebarCollapseBehavior: Mouse-proximity triggered collapse/expand for sidebar
 */

using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace LoneEftDmaRadar.UI.Behaviors
{
    /// <summary>
    /// Attached behavior that auto-collapses/expands a sidebar based on mouse proximity.
    /// Expands when mouse is within TriggerDistance of left edge, collapses after timeout.
    /// </summary>
    public static class SidebarCollapseBehavior
    {
        #region Constants

        private const int DefaultTriggerDistance = 50;
        private const int DefaultCollapseTimeoutMs = 1000;
        private const double DefaultExpandedWidth = 240;
        private const double DefaultCollapsedWidth = 12;
        private const int AnimationDurationMs = 300;

        #endregion

        #region Attached Properties

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty TriggerDistanceProperty =
            DependencyProperty.RegisterAttached(
                "TriggerDistance",
                typeof(double),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata((double)DefaultTriggerDistance));

        public static readonly DependencyProperty CollapseTimeoutProperty =
            DependencyProperty.RegisterAttached(
                "CollapseTimeout",
                typeof(int),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata(DefaultCollapseTimeoutMs));

        public static readonly DependencyProperty ExpandedWidthProperty =
            DependencyProperty.RegisterAttached(
                "ExpandedWidth",
                typeof(double),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata(DefaultExpandedWidth));

        public static readonly DependencyProperty CollapsedWidthProperty =
            DependencyProperty.RegisterAttached(
                "CollapsedWidth",
                typeof(double),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata(DefaultCollapsedWidth));

        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.RegisterAttached(
                "IsExpanded",
                typeof(bool),
                typeof(SidebarCollapseBehavior),
                new PropertyMetadata(false, OnIsExpandedChanged));

        #endregion

        #region Getters/Setters

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static double GetTriggerDistance(DependencyObject obj) => (double)obj.GetValue(TriggerDistanceProperty);
        public static void SetTriggerDistance(DependencyObject obj, double value) => obj.SetValue(TriggerDistanceProperty, value);

        public static int GetCollapseTimeout(DependencyObject obj) => (int)obj.GetValue(CollapseTimeoutProperty);
        public static void SetCollapseTimeout(DependencyObject obj, int value) => obj.SetValue(CollapseTimeoutProperty, value);

        public static double GetExpandedWidth(DependencyObject obj) => (double)obj.GetValue(ExpandedWidthProperty);
        public static void SetExpandedWidth(DependencyObject obj, double value) => obj.SetValue(ExpandedWidthProperty, value);

        public static double GetCollapsedWidth(DependencyObject obj) => (double)obj.GetValue(CollapsedWidthProperty);
        public static void SetCollapsedWidth(DependencyObject obj, double value) => obj.SetValue(CollapsedWidthProperty, value);

        public static bool GetIsExpanded(DependencyObject obj) => (bool)obj.GetValue(IsExpandedProperty);
        public static void SetIsExpanded(DependencyObject obj, bool value) => obj.SetValue(IsExpandedProperty, value);

        #endregion

        #region State Tracking

        private static readonly Dictionary<FrameworkElement, DispatcherTimer> _collapseTimers = new();
        private static readonly Dictionary<FrameworkElement, Window> _parentWindows = new();

        #endregion

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                element.Loaded += Element_Loaded;
                element.Unloaded += Element_Unloaded;
                if (element.IsLoaded)
                    AttachToWindow(element);
            }
            else
            {
                element.Loaded -= Element_Loaded;
                element.Unloaded -= Element_Unloaded;
                DetachFromWindow(element);
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                AttachToWindow(element);
        }

        private static void Element_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                DetachFromWindow(element);
        }

        private static void AttachToWindow(FrameworkElement element)
        {
            var window = Window.GetWindow(element);
            if (window == null)
                return;

            _parentWindows[element] = window;
            window.PreviewMouseMove += (s, e) => Window_PreviewMouseMove(element, e);
            element.MouseEnter += Element_MouseEnter;
            element.MouseLeave += Element_MouseLeave;

            // Initialize to collapsed state
            var collapsedWidth = GetCollapsedWidth(element);
            element.Width = collapsedWidth;
            SetIsExpanded(element, false);

            // Create collapse timer
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(GetCollapseTimeout(element))
            };
            timer.Tick += (s, e) => CollapseTimer_Tick(element, timer);
            _collapseTimers[element] = timer;
        }

        private static void DetachFromWindow(FrameworkElement element)
        {
            if (_collapseTimers.TryGetValue(element, out var timer))
            {
                timer.Stop();
                _collapseTimers.Remove(element);
            }

            _parentWindows.Remove(element);
        }

        private static void Window_PreviewMouseMove(FrameworkElement sidebar, MouseEventArgs e)
        {
            if (!_parentWindows.TryGetValue(sidebar, out var window))
                return;

            var mousePos = e.GetPosition(window);
            var triggerDistance = GetTriggerDistance(sidebar);
            var isExpanded = GetIsExpanded(sidebar);

            // Check if mouse is within trigger distance of left edge
            if (mousePos.X <= triggerDistance && !isExpanded)
            {
                ExpandSidebar(sidebar);
            }
        }

        private static void Element_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            // Stop collapse timer when mouse enters sidebar
            if (_collapseTimers.TryGetValue(element, out var timer))
                timer.Stop();

            // Expand if not already expanded
            if (!GetIsExpanded(element))
                ExpandSidebar(element);
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            // Start collapse timer when mouse leaves sidebar
            if (_collapseTimers.TryGetValue(element, out var timer))
            {
                timer.Interval = TimeSpan.FromMilliseconds(GetCollapseTimeout(element));
                timer.Start();
            }
        }

        private static void CollapseTimer_Tick(FrameworkElement element, DispatcherTimer timer)
        {
            timer.Stop();

            // Only collapse if mouse is not over the sidebar
            if (!element.IsMouseOver)
                CollapseSidebar(element);
        }

        private static void ExpandSidebar(FrameworkElement element)
        {
            SetIsExpanded(element, true);
            AnimateWidth(element, GetExpandedWidth(element));
        }

        private static void CollapseSidebar(FrameworkElement element)
        {
            SetIsExpanded(element, false);
            AnimateWidth(element, GetCollapsedWidth(element));
        }

        private static void AnimateWidth(FrameworkElement element, double targetWidth)
        {
            var animation = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(AnimationDurationMs))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(FrameworkElement.WidthProperty, animation);
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            // This can be used for binding to show/hide content
            // The actual width animation is handled separately
        }
    }
}
