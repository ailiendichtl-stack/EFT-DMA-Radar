/*
 * Twilight PVE Radar - WPF Modular GUI
 * DraggableBehavior: Attached behavior for Canvas-based panel dragging with grid snap
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.Behaviors
{
    /// <summary>
    /// Attached behavior that enables dragging of elements on a Canvas with 16px grid snap.
    /// Attach to any FrameworkElement that is a child of a Canvas.
    /// </summary>
    public static class DraggableBehavior
    {
        private const int GridSize = 16;

        #region Attached Properties

        public static readonly DependencyProperty IsDraggableProperty =
            DependencyProperty.RegisterAttached(
                "IsDraggable",
                typeof(bool),
                typeof(DraggableBehavior),
                new PropertyMetadata(false, OnIsDraggableChanged));

        public static readonly DependencyProperty DragHandleProperty =
            DependencyProperty.RegisterAttached(
                "DragHandle",
                typeof(FrameworkElement),
                typeof(DraggableBehavior),
                new PropertyMetadata(null, OnDragHandleChanged));

        public static readonly DependencyProperty EnableGridSnapProperty =
            DependencyProperty.RegisterAttached(
                "EnableGridSnap",
                typeof(bool),
                typeof(DraggableBehavior),
                new PropertyMetadata(true));

        // Private attached property to track drag state per-element
        private static readonly DependencyProperty DragStateProperty =
            DependencyProperty.RegisterAttached(
                "DragState",
                typeof(DragState),
                typeof(DraggableBehavior),
                new PropertyMetadata(null));

        #endregion

        #region Getters/Setters

        public static bool GetIsDraggable(DependencyObject obj) => (bool)obj.GetValue(IsDraggableProperty);
        public static void SetIsDraggable(DependencyObject obj, bool value) => obj.SetValue(IsDraggableProperty, value);

        public static FrameworkElement GetDragHandle(DependencyObject obj) => (FrameworkElement)obj.GetValue(DragHandleProperty);
        public static void SetDragHandle(DependencyObject obj, FrameworkElement value) => obj.SetValue(DragHandleProperty, value);

        public static bool GetEnableGridSnap(DependencyObject obj) => (bool)obj.GetValue(EnableGridSnapProperty);
        public static void SetEnableGridSnap(DependencyObject obj, bool value) => obj.SetValue(EnableGridSnapProperty, value);

        private static DragState GetDragState(DependencyObject obj) => (DragState)obj.GetValue(DragStateProperty);
        private static void SetDragState(DependencyObject obj, DragState value) => obj.SetValue(DragStateProperty, value);

        #endregion

        #region State Class

        private class DragState
        {
            public bool IsDragging { get; set; }
            public Point StartPoint { get; set; }
            public double StartLeft { get; set; }
            public double StartTop { get; set; }
            public FrameworkElement DragHandle { get; set; }
        }

        #endregion

        private static void OnIsDraggableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            if ((bool)e.NewValue)
            {
                SetDragState(element, new DragState());
                element.Loaded += Element_Loaded;
                if (element.IsLoaded)
                    AttachDragHandlers(element);
            }
            else
            {
                element.Loaded -= Element_Loaded;
                DetachDragHandlers(element);
                SetDragState(element, null);
            }
        }

        private static void OnDragHandleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
                return;

            // Only re-attach if the element is already set up for dragging
            if (!GetIsDraggable(element))
                return;

            var state = GetDragState(element);
            if (state == null)
                return;

            // Detach from old handle
            if (state.DragHandle != null)
            {
                state.DragHandle.MouseLeftButtonDown -= DragHandle_MouseLeftButtonDown;
                state.DragHandle.MouseMove -= DragHandle_MouseMove;
                state.DragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
                state.DragHandle.MouseLeave -= DragHandle_MouseLeave;
            }

            // Attach to new handle
            var newHandle = e.NewValue as FrameworkElement ?? element;
            state.DragHandle = newHandle;

            newHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            newHandle.MouseMove += DragHandle_MouseMove;
            newHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
            newHandle.MouseLeave += DragHandle_MouseLeave;
        }

        private static void Element_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                AttachDragHandlers(element);
        }

        private static void AttachDragHandlers(FrameworkElement element)
        {
            var state = GetDragState(element);
            if (state == null)
                return;

            var dragHandle = GetDragHandle(element) ?? element;
            state.DragHandle = dragHandle;

            dragHandle.MouseLeftButtonDown += DragHandle_MouseLeftButtonDown;
            dragHandle.MouseMove += DragHandle_MouseMove;
            dragHandle.MouseLeftButtonUp += DragHandle_MouseLeftButtonUp;
            dragHandle.MouseLeave += DragHandle_MouseLeave;
        }

        private static void DetachDragHandlers(FrameworkElement element)
        {
            var state = GetDragState(element);
            if (state?.DragHandle == null)
                return;

            var dragHandle = state.DragHandle;
            dragHandle.MouseLeftButtonDown -= DragHandle_MouseLeftButtonDown;
            dragHandle.MouseMove -= DragHandle_MouseMove;
            dragHandle.MouseLeftButtonUp -= DragHandle_MouseLeftButtonUp;
            dragHandle.MouseLeave -= DragHandle_MouseLeave;
        }

        private static void DragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement dragHandle)
                return;

            // Find the draggable parent (the element with IsDraggable=True)
            var element = FindDraggableParent(dragHandle);
            if (element == null)
                return;

            var state = GetDragState(element);
            if (state == null)
                return;

            // Ensure parent is a Canvas
            if (element.Parent is not Canvas canvas)
                return;

            state.StartPoint = e.GetPosition(canvas);
            state.StartLeft = Canvas.GetLeft(element);
            state.StartTop = Canvas.GetTop(element);

            // Handle NaN values (element not positioned yet)
            if (double.IsNaN(state.StartLeft)) state.StartLeft = 0;
            if (double.IsNaN(state.StartTop)) state.StartTop = 0;

            state.IsDragging = true;
            dragHandle.CaptureMouse();

            e.Handled = true;
        }

        private static void DragHandle_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement dragHandle)
                return;

            var element = FindDraggableParent(dragHandle);
            if (element == null)
                return;

            var state = GetDragState(element);
            if (state == null || !state.IsDragging)
                return;

            if (element.Parent is not Canvas canvas)
                return;

            var currentPoint = e.GetPosition(canvas);
            var deltaX = currentPoint.X - state.StartPoint.X;
            var deltaY = currentPoint.Y - state.StartPoint.Y;

            var newLeft = state.StartLeft + deltaX;
            var newTop = state.StartTop + deltaY;

            // Clamp to Canvas bounds
            var canvasWidth = canvas.ActualWidth;
            var canvasHeight = canvas.ActualHeight;
            var elementWidth = element.ActualWidth;
            var elementHeight = element.ActualHeight;

            newLeft = Math.Max(0, Math.Min(newLeft, canvasWidth - elementWidth));
            newTop = Math.Max(0, Math.Min(newTop, canvasHeight - elementHeight));

            // Direct assignment (no animation)
            Canvas.SetLeft(element, newLeft);
            Canvas.SetTop(element, newTop);
        }

        private static void DragHandle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement dragHandle)
                return;

            var element = FindDraggableParent(dragHandle);
            if (element != null)
                CompleteDrag(element, dragHandle);

            e.Handled = true;
        }

        private static void DragHandle_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement dragHandle)
                return;

            var element = FindDraggableParent(dragHandle);
            if (element == null)
                return;

            var state = GetDragState(element);
            if (state == null || !state.IsDragging)
                return;

            // Only complete drag if mouse button is not pressed
            if (e.LeftButton == MouseButtonState.Released)
                CompleteDrag(element, dragHandle);
        }

        private static void CompleteDrag(FrameworkElement element, FrameworkElement dragHandle)
        {
            var state = GetDragState(element);
            if (state == null || !state.IsDragging)
                return;

            dragHandle?.ReleaseMouseCapture();

            // Apply grid snap if enabled (direct assignment, no animation)
            if (GetEnableGridSnap(element))
            {
                var currentLeft = Canvas.GetLeft(element);
                var currentTop = Canvas.GetTop(element);

                var snappedLeft = SnapToGrid(currentLeft);
                var snappedTop = SnapToGrid(currentTop);

                Canvas.SetLeft(element, snappedLeft);
                Canvas.SetTop(element, snappedTop);
            }

            state.IsDragging = false;
        }

        private static double SnapToGrid(double value)
        {
            return Math.Round(value / GridSize) * GridSize;
        }

        private static FrameworkElement FindDraggableParent(FrameworkElement element)
        {
            var current = element;
            while (current != null)
            {
                if (GetIsDraggable(current))
                    return current;
                current = current.Parent as FrameworkElement;
            }
            return null;
        }
    }
}
