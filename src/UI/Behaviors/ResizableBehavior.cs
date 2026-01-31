/*
 * Twilight PVE Radar - WPF Modular GUI
 * ResizableBehavior: Attached behavior for panel resizing with minimum bounds
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Behaviors
{
    /// <summary>
    /// Attached behavior that enables resizing of elements on a Canvas.
    /// Resize is triggered from the bottom-right corner (last 16px).
    /// </summary>
    public static class ResizableBehavior
    {
        private const int ResizeHandleSize = 16;
        private const int GridSize = 16;

        #region Attached Properties

        public static readonly DependencyProperty IsResizableProperty =
            DependencyProperty.RegisterAttached(
                "IsResizable",
                typeof(bool),
                typeof(ResizableBehavior),
                new PropertyMetadata(false, OnIsResizableChanged));

        public static readonly DependencyProperty MinWidthProperty =
            DependencyProperty.RegisterAttached(
                "MinWidth",
                typeof(double),
                typeof(ResizableBehavior),
                new PropertyMetadata(280.0));

        public static readonly DependencyProperty MinHeightProperty =
            DependencyProperty.RegisterAttached(
                "MinHeight",
                typeof(double),
                typeof(ResizableBehavior),
                new PropertyMetadata(200.0));

        public static readonly DependencyProperty EnableGridSnapProperty =
            DependencyProperty.RegisterAttached(
                "EnableGridSnap",
                typeof(bool),
                typeof(ResizableBehavior),
                new PropertyMetadata(true));

        // Private attached property to track resize state per-element
        private static readonly DependencyProperty ResizeStateProperty =
            DependencyProperty.RegisterAttached(
                "ResizeState",
                typeof(ResizeState),
                typeof(ResizableBehavior),
                new PropertyMetadata(null));

        #endregion

        #region Getters/Setters

        public static bool GetIsResizable(DependencyObject obj) => (bool)obj.GetValue(IsResizableProperty);
        public static void SetIsResizable(DependencyObject obj, bool value) => obj.SetValue(IsResizableProperty, value);

        public static double GetMinWidth(DependencyObject obj) => (double)obj.GetValue(MinWidthProperty);
        public static void SetMinWidth(DependencyObject obj, double value) => obj.SetValue(MinWidthProperty, value);

        public static double GetMinHeight(DependencyObject obj) => (double)obj.GetValue(MinHeightProperty);
        public static void SetMinHeight(DependencyObject obj, double value) => obj.SetValue(MinHeightProperty, value);

        public static bool GetEnableGridSnap(DependencyObject obj) => (bool)obj.GetValue(EnableGridSnapProperty);
        public static void SetEnableGridSnap(DependencyObject obj, bool value) => obj.SetValue(EnableGridSnapProperty, value);

        private static ResizeState GetResizeState(DependencyObject obj) => (ResizeState)obj.GetValue(ResizeStateProperty);
        private static void SetResizeState(DependencyObject obj, ResizeState value) => obj.SetValue(ResizeStateProperty, value);

        #endregion

        #region State Class

        private class ResizeState
        {
            public bool IsResizing { get; set; }
            public Point StartPoint { get; set; }
            public double StartWidth { get; set; }
            public double StartHeight { get; set; }
            public bool CursorOverridden { get; set; }
        }

        #endregion

        private static void OnIsResizableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                SetResizeState(element, new ResizeState());
                element.Loaded += Element_Loaded;
                if (element.IsLoaded)
                    AttachResizeHandlers(element);
            }
            else
            {
                element.Loaded -= Element_Loaded;
                DetachResizeHandlers(element);
                SetResizeState(element, null);
            }
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                AttachResizeHandlers(element);
        }

        private static void AttachResizeHandlers(FrameworkElement element)
        {
            element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            element.MouseMove += Element_MouseMove;
            element.MouseLeftButtonUp += Element_MouseLeftButtonUp;
            element.MouseLeave += Element_MouseLeave;
        }

        private static void DetachResizeHandlers(FrameworkElement element)
        {
            element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
            element.MouseMove -= Element_MouseMove;
            element.MouseLeftButtonUp -= Element_MouseLeftButtonUp;
            element.MouseLeave -= Element_MouseLeave;
        }

        private static bool IsInResizeZone(FrameworkElement element, Point mousePos)
        {
            var width = element.ActualWidth;
            var height = element.ActualHeight;

            return mousePos.X >= width - ResizeHandleSize &&
                   mousePos.Y >= height - ResizeHandleSize &&
                   mousePos.X <= width &&
                   mousePos.Y <= height;
        }

        private static void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var state = GetResizeState(element);
            if (state == null)
                return;

            var mousePos = e.GetPosition(element);
            if (!IsInResizeZone(element, mousePos))
                return;

            // Ensure parent is a Canvas
            if (element.Parent is not Canvas canvas)
                return;

            state.StartPoint = e.GetPosition(canvas);
            state.StartWidth = element.ActualWidth;
            state.StartHeight = element.ActualHeight;
            state.IsResizing = true;

            element.Cursor = Cursors.SizeNWSE;
            state.CursorOverridden = true;
            element.CaptureMouse();

            e.Handled = true;
        }

        private static void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var state = GetResizeState(element);
            if (state == null)
                return;

            if (state.IsResizing)
            {
                // Handle active resize
                if (element.Parent is not Canvas canvas)
                    return;

                var currentPoint = e.GetPosition(canvas);
                var deltaX = currentPoint.X - state.StartPoint.X;
                var deltaY = currentPoint.Y - state.StartPoint.Y;

                var newWidth = state.StartWidth + deltaX;
                var newHeight = state.StartHeight + deltaY;

                // Apply minimum bounds
                var minWidth = GetMinWidth(element);
                var minHeight = GetMinHeight(element);
                newWidth = Math.Max(minWidth, newWidth);
                newHeight = Math.Max(minHeight, newHeight);

                // Clamp to Canvas bounds
                var elementLeft = Canvas.GetLeft(element);
                if (double.IsNaN(elementLeft)) elementLeft = 0;
                var elementTop = Canvas.GetTop(element);
                if (double.IsNaN(elementTop)) elementTop = 0;
                var canvasWidth = canvas.ActualWidth;
                var canvasHeight = canvas.ActualHeight;

                if (canvasWidth > 0)
                    newWidth = Math.Min(newWidth, canvasWidth - elementLeft);
                if (canvasHeight > 0)
                    newHeight = Math.Min(newHeight, canvasHeight - elementTop);

                // Set size directly (no animation during drag)
                element.Width = newWidth;
                element.Height = newHeight;
            }
            else
            {
                // Update cursor based on hover position
                var mousePos = e.GetPosition(element);
                if (IsInResizeZone(element, mousePos))
                {
                    if (!state.CursorOverridden)
                    {
                        element.Cursor = Cursors.SizeNWSE;
                        state.CursorOverridden = true;
                    }
                }
                else
                {
                    if (state.CursorOverridden)
                    {
                        element.Cursor = null; // Reset to default/inherited cursor
                        state.CursorOverridden = false;
                    }
                }
            }
        }

        private static void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                CompleteResize(element);
                e.Handled = true;
            }
        }

        private static void Element_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element)
                return;

            var state = GetResizeState(element);
            if (state == null)
                return;

            // Reset cursor if not resizing
            if (!state.IsResizing && state.CursorOverridden)
            {
                element.Cursor = null; // Reset to default/inherited cursor
                state.CursorOverridden = false;
            }

            // Only complete resize if mouse button is not pressed
            if (state.IsResizing && e.LeftButton == MouseButtonState.Released)
                CompleteResize(element);
        }

        private static void CompleteResize(FrameworkElement element)
        {
            var state = GetResizeState(element);
            if (state == null || !state.IsResizing)
                return;

            element.ReleaseMouseCapture();

            // Apply grid snap if enabled (direct assignment, no animation)
            if (GetEnableGridSnap(element))
            {
                var snappedWidth = SnapToGrid(element.Width);
                var snappedHeight = SnapToGrid(element.Height);

                // Ensure minimum bounds after snap
                var minWidth = GetMinWidth(element);
                var minHeight = GetMinHeight(element);
                snappedWidth = Math.Max(minWidth, snappedWidth);
                snappedHeight = Math.Max(minHeight, snappedHeight);

                element.Width = snappedWidth;
                element.Height = snappedHeight;
            }

            // Reset cursor
            element.Cursor = null; // Reset to default/inherited cursor
            state.CursorOverridden = false;

            state.IsResizing = false;
        }

        private static double SnapToGrid(double value)
        {
            return Math.Round(value / GridSize) * GridSize;
        }
    }
}
