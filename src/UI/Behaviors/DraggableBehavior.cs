/*
 * Twilight PVE Radar - WPF Modular GUI
 * DraggableBehavior: Attached behavior for Canvas-based panel dragging with grid snap and drop detection
 */

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LoneEftDmaRadar.UI.Controls;

namespace LoneEftDmaRadar.UI.Behaviors
{
    /// <summary>
    /// Attached behavior that enables dragging of elements on a Canvas with 16px grid snap.
    /// Supports drop detection for panel tab grouping.
    /// </summary>
    public static class DraggableBehavior
    {
        private const int GridSize = 16;
        private const double TitleBarHeight = 32;

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

        #region Drop Detection

        private static DraggablePanel _currentDropTarget;

        /// <summary>
        /// Fired when a panel is dropped onto another panel's title bar area.
        /// Args: (SourcePanelName, TargetPanelName).
        /// </summary>
        public static event EventHandler<(string Source, string Target)> PanelDropped;

        /// <summary>
        /// Programmatically begins a drag on the given element using its drag handle.
        /// The element is positioned so the cursor is centered on its title bar,
        /// then mouse capture is started so the user can drag immediately.
        /// </summary>
        public static void BeginDrag(FrameworkElement element)
        {
            var state = GetDragState(element);
            if (state?.DragHandle == null) return;
            if (element.Parent is not Canvas canvas) return;

            // Ensure layout is up to date (element may have just become visible)
            element.UpdateLayout();

            var elementWidth = element.ActualWidth > 0 ? element.ActualWidth : element.Width;
            var elementHeight = element.ActualHeight > 0 ? element.ActualHeight : element.Height;
            if (double.IsNaN(elementWidth)) elementWidth = 400;
            if (double.IsNaN(elementHeight)) elementHeight = 400;

            // Position the element so the cursor lands on the center of its title bar
            var mousePos = Mouse.GetPosition(canvas);
            var newLeft = mousePos.X - elementWidth / 2;
            var newTop = mousePos.Y - TitleBarHeight / 2;

            // Clamp to canvas bounds
            newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - elementWidth));
            newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - elementHeight));

            Canvas.SetLeft(element, newLeft);
            Canvas.SetTop(element, newTop);

            // Start drag state
            state.StartPoint = mousePos;
            state.StartLeft = newLeft;
            state.StartTop = newTop;
            state.IsDragging = true;
            state.DragHandle.CaptureMouse();
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

            // Drop detection: check if dragged panel overlaps another panel's title bar
            if (element is DraggablePanel draggedPanel)
            {
                var centerX = newLeft + elementWidth / 2;
                var centerY = newTop + 16; // center of title bar

                DraggablePanel dropTarget = null;
                foreach (UIElement child in canvas.Children)
                {
                    if (child is DraggablePanel other && other != draggedPanel
                        && other.Visibility == Visibility.Visible)
                    {
                        var otherLeft = Canvas.GetLeft(other);
                        var otherTop = Canvas.GetTop(other);
                        if (double.IsNaN(otherLeft) || double.IsNaN(otherTop)) continue;

                        // Check if center of dragged title bar is over target's title bar area
                        if (centerX >= otherLeft && centerX <= otherLeft + other.ActualWidth
                            && centerY >= otherTop && centerY <= otherTop + TitleBarHeight)
                        {
                            dropTarget = other;
                            break;
                        }
                    }
                }

                // Update drop target highlight
                if (_currentDropTarget != dropTarget)
                {
                    if (_currentDropTarget != null)
                        _currentDropTarget.IsDropHighlighted = false;
                    _currentDropTarget = dropTarget;
                    if (_currentDropTarget != null)
                        _currentDropTarget.IsDropHighlighted = true;
                }
            }
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
            state.IsDragging = false;

            // Check for drop target (panel grouping)
            if (element is DraggablePanel draggedPanel && _currentDropTarget != null)
            {
                var target = _currentDropTarget;
                _currentDropTarget.IsDropHighlighted = false;
                _currentDropTarget = null;

                var sourceName = draggedPanel.Name;
                var targetName = target.Name;

                if (!string.IsNullOrEmpty(sourceName) && !string.IsNullOrEmpty(targetName))
                {
                    // Restore source to original position (it will be hidden by grouping)
                    Canvas.SetLeft(element, state.StartLeft);
                    Canvas.SetTop(element, state.StartTop);

                    PanelDropped?.Invoke(null, (sourceName, targetName));
                    return;
                }
            }

            // Clear any lingering highlight
            if (_currentDropTarget != null)
            {
                _currentDropTarget.IsDropHighlighted = false;
                _currentDropTarget = null;
            }

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
