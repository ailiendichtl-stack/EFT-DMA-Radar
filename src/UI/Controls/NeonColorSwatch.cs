using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LoneEftDmaRadar.UI.Controls
{
    public class NeonColorSwatch : Control
    {
        private Popup? _popup;
        private ScrollViewer? _parentScrollViewer;

        static NeonColorSwatch()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(NeonColorSwatch),
                new FrameworkPropertyMetadata(typeof(NeonColorSwatch)));
        }

        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(
                nameof(SelectedColor),
                typeof(Color?),
                typeof(NeonColorSwatch),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public Color? SelectedColor
        {
            get => (Color?)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public static readonly DependencyProperty UsingAlphaChannelProperty =
            DependencyProperty.Register(
                nameof(UsingAlphaChannel),
                typeof(bool),
                typeof(NeonColorSwatch),
                new PropertyMetadata(true));

        public bool UsingAlphaChannel
        {
            get => (bool)GetValue(UsingAlphaChannelProperty);
            set => SetValue(UsingAlphaChannelProperty, value);
        }

        public static readonly DependencyProperty IsDropDownOpenProperty =
            DependencyProperty.Register(
                nameof(IsDropDownOpen),
                typeof(bool),
                typeof(NeonColorSwatch),
                new PropertyMetadata(false, OnIsDropDownOpenChanged));

        public bool IsDropDownOpen
        {
            get => (bool)GetValue(IsDropDownOpenProperty);
            set => SetValue(IsDropDownOpenProperty, value);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _popup = GetTemplateChild("PART_Popup") as Popup;
        }

        private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var swatch = (NeonColorSwatch)d;
            if ((bool)e.NewValue)
            {
                swatch.SubscribeClose();
                swatch.SubscribeScroll();
            }
            else
            {
                swatch.UnsubscribeClose();
                swatch.UnsubscribeScroll();
            }
        }

        private void SubscribeClose()
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewMouseDown += OnWindowMouseDown;
                window.Deactivated += OnWindowDeactivated;
            }
        }

        private void UnsubscribeClose()
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.PreviewMouseDown -= OnWindowMouseDown;
                window.Deactivated -= OnWindowDeactivated;
            }
        }

        private void OnWindowMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Click landed on main window (not inside popup, which is a separate Win32 window).
            // Close if the click is not on the swatch itself (toggle handles that).
            if (!IsMouseOver)
                IsDropDownOpen = false;
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            IsDropDownOpen = false;
        }

        private void SubscribeScroll()
        {
            _parentScrollViewer ??= FindParent<ScrollViewer>(this);
            if (_parentScrollViewer != null)
                _parentScrollViewer.ScrollChanged += OnParentScrollChanged;
        }

        private void UnsubscribeScroll()
        {
            if (_parentScrollViewer != null)
                _parentScrollViewer.ScrollChanged -= OnParentScrollChanged;
        }

        private void OnParentScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange != 0 || e.HorizontalChange != 0)
                IsDropDownOpen = false;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T found)
                    return found;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
