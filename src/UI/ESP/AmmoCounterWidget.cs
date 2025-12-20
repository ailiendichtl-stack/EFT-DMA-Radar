/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System;
using System.Diagnostics;
using System.Drawing;
using SharpDX.Mathematics.Interop;
using SkiaSharp;
using DxColor = SharpDX.Mathematics.Interop.RawColorBGRA;

namespace LoneEftDmaRadar.UI.ESP
{
    /// <summary>
    /// Draggable and resizable ammo counter widget for the ESP overlay.
    /// Displays current/max ammo in "XX/XX" format.
    /// </summary>
    internal sealed class AmmoCounterWidget
    {
        private const float RESIZE_HANDLE_SIZE = 12f;
        private const float MIN_WIDTH = 60f;
        private const float MIN_HEIGHT = 30f;
        private const float HOVER_FADE_MS = 1500f; // How long outlines stay visible after hover ends

        // Widget bounds
        private float _x;
        private float _y;
        private float _width;
        private float _height;

        // Interaction state
        private bool _isDragging;
        private bool _isResizing;
        private bool _isHovering;
        private long _lastHoverTicks;
        private float _dragOffsetX;
        private float _dragOffsetY;
        private float _resizeStartX;
        private float _resizeStartY;
        private float _resizeStartWidth;
        private float _resizeStartHeight;

        /// <summary>
        /// Current bounds of the widget.
        /// </summary>
        public RectangleF Bounds => new RectangleF(_x, _y, _width, _height);

        /// <summary>
        /// Whether the widget is currently being interacted with.
        /// </summary>
        public bool IsInteracting => _isDragging || _isResizing;

        /// <summary>
        /// Whether outlines should be visible (hovering, interacting, or recently hovered).
        /// </summary>
        private bool ShouldShowOutlines
        {
            get
            {
                if (_isHovering || IsInteracting)
                    return true;

                // Fade out after hover ends
                var elapsed = (Stopwatch.GetTimestamp() - _lastHoverTicks) * 1000.0 / Stopwatch.Frequency;
                return elapsed < HOVER_FADE_MS;
            }
        }

        /// <summary>
        /// Calculate outline opacity based on hover/fade state (0.0 to 1.0).
        /// </summary>
        private float OutlineOpacity
        {
            get
            {
                if (_isHovering || IsInteracting)
                    return 1.0f;

                var elapsed = (Stopwatch.GetTimestamp() - _lastHoverTicks) * 1000.0 / Stopwatch.Frequency;
                if (elapsed >= HOVER_FADE_MS)
                    return 0.0f;

                return 1.0f - (float)(elapsed / HOVER_FADE_MS);
            }
        }

        public AmmoCounterWidget()
        {
            LoadFromConfig();
        }

        /// <summary>
        /// Load widget position and size from config.
        /// </summary>
        private void LoadFromConfig()
        {
            var cfg = App.Config.UI.AmmoCounter;
            _x = cfg.PositionX;
            _y = cfg.PositionY;
            _width = Math.Max(MIN_WIDTH, cfg.Width);
            _height = Math.Max(MIN_HEIGHT, cfg.Height);
        }

        /// <summary>
        /// Save widget position and size to config.
        /// </summary>
        public void SaveToConfig()
        {
            var cfg = App.Config.UI.AmmoCounter;
            cfg.PositionX = _x;
            cfg.PositionY = _y;
            cfg.Width = _width;
            cfg.Height = _height;
        }

        /// <summary>
        /// Check if a point is within the widget bounds.
        /// </summary>
        public bool HitTest(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            var bounds = GetActualBounds(screenWidth, screenHeight);
            return bounds.Contains(mouseX, mouseY);
        }

        /// <summary>
        /// Check if a point is within the resize handle (bottom-right corner).
        /// </summary>
        private bool IsInResizeHandle(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            var bounds = GetActualBounds(screenWidth, screenHeight);
            float handleX = bounds.Right - RESIZE_HANDLE_SIZE;
            float handleY = bounds.Bottom - RESIZE_HANDLE_SIZE;
            return mouseX >= handleX && mouseY >= handleY &&
                   mouseX <= bounds.Right && mouseY <= bounds.Bottom;
        }

        /// <summary>
        /// Get actual bounds, handling auto-positioning when X/Y are -1.
        /// </summary>
        private RectangleF GetActualBounds(float screenWidth, float screenHeight)
        {
            // Default: center of screen horizontally, ~60% down vertically (visible and accessible)
            float x = _x < 0 ? (screenWidth / 2f) - (_width / 2f) : _x;
            float y = _y < 0 ? (screenHeight * 0.6f) - (_height / 2f) : _y;
            return new RectangleF(x, y, _width, _height);
        }

        /// <summary>
        /// Handle mouse down event.
        /// </summary>
        /// <returns>True if the widget handled the event.</returns>
        public bool OnMouseDown(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            if (!HitTest(mouseX, mouseY, screenWidth, screenHeight))
                return false;

            var bounds = GetActualBounds(screenWidth, screenHeight);

            if (IsInResizeHandle(mouseX, mouseY, screenWidth, screenHeight))
            {
                _isResizing = true;
                _resizeStartX = mouseX;
                _resizeStartY = mouseY;
                _resizeStartWidth = _width;
                _resizeStartHeight = _height;

                // If widget was auto-positioned, fix it to current position
                if (_x < 0) _x = bounds.X;
                if (_y < 0) _y = bounds.Y;
            }
            else
            {
                _isDragging = true;
                _dragOffsetX = mouseX - bounds.X;
                _dragOffsetY = mouseY - bounds.Y;

                // If widget was auto-positioned, fix it to current position
                if (_x < 0) _x = bounds.X;
                if (_y < 0) _y = bounds.Y;
            }

            return true;
        }

        /// <summary>
        /// Handle mouse move event.
        /// </summary>
        public void OnMouseMove(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            if (_isDragging)
            {
                _x = Math.Clamp(mouseX - _dragOffsetX, 0, screenWidth - _width);
                _y = Math.Clamp(mouseY - _dragOffsetY, 0, screenHeight - _height);
            }
            else if (_isResizing)
            {
                float deltaX = mouseX - _resizeStartX;
                float deltaY = mouseY - _resizeStartY;
                _width = Math.Max(MIN_WIDTH, _resizeStartWidth + deltaX);
                _height = Math.Max(MIN_HEIGHT, _resizeStartHeight + deltaY);

                // Clamp to screen bounds
                if (_x + _width > screenWidth) _width = screenWidth - _x;
                if (_y + _height > screenHeight) _height = screenHeight - _y;
            }
        }

        /// <summary>
        /// Handle mouse up event.
        /// </summary>
        public void OnMouseUp()
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                SaveToConfig();
            }
        }

        /// <summary>
        /// Update hover state based on mouse position.
        /// </summary>
        public void UpdateHoverState(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            var bounds = GetActualBounds(screenWidth, screenHeight);
            bool wasHovering = _isHovering;
            _isHovering = bounds.Contains(mouseX, mouseY);

            // Track when hover ends for fade effect
            if (wasHovering && !_isHovering)
            {
                _lastHoverTicks = Stopwatch.GetTimestamp();
            }
        }

        /// <summary>
        /// Draw the ammo counter widget.
        /// </summary>
        public void Draw(Dx9RenderContext ctx, int currentAmmo, int maxAmmo, string ammoTypeName = null)
        {
            var cfg = App.Config.UI.AmmoCounter;
            if (!cfg.Enabled)
                return;

            float screenWidth = ctx.Width;
            float screenHeight = ctx.Height;
            var bounds = GetActualBounds(screenWidth, screenHeight);

            // Only draw background and outlines when hovering/interacting (with fade)
            if (ShouldShowOutlines)
            {
                float opacity = OutlineOpacity;
                byte alpha = (byte)(opacity * 180); // Max alpha 180 for semi-transparent

                // Draw semi-transparent background
                var bgColor = new DxColor(0, 0, 0, (byte)(alpha * 0.5f));
                ctx.DrawFilledRect(bounds, bgColor);

                // Draw border (highlight when interacting)
                var borderColor = IsInteracting
                    ? new DxColor(255, 200, 100, (byte)(opacity * 255))  // Cyan when dragging/resizing
                    : new DxColor(200, 200, 200, alpha); // White/gray normally
                ctx.DrawRect(bounds, borderColor, 1f);

                // Draw resize handle
                DrawResizeHandle(ctx, bounds, opacity);
            }

            var textColor = ParseColor(cfg.TextColor);
            string ammoText = (currentAmmo < 0 || maxAmmo < 0) ? "--/--" : $"{currentAmmo}/{maxAmmo}";

            // Calculate font sizes based on widget dimensions - text should fill the box
            var (countFontSize, typeFontSize, verticalSpacing) = CalculateScaledSizes(bounds);

            bool hasAmmoType = !string.IsNullOrWhiteSpace(ammoTypeName);
            float centerX = bounds.X + bounds.Width / 2f;
            float centerY = bounds.Y + bounds.Height / 2f;

            if (hasAmmoType)
            {
                // Draw ammo count slightly above center
                float ammoCountY = centerY - verticalSpacing;
                ctx.DrawTextScaled(ammoText, centerX, ammoCountY, textColor, countFontSize, centerX: true, centerY: true);

                // Draw ammo type below, smaller and close to the count
                float ammoTypeY = centerY + verticalSpacing;
                ctx.DrawTextScaled(ammoTypeName, centerX, ammoTypeY, textColor, typeFontSize, centerX: true, centerY: true);
            }
            else
            {
                // No ammo type, just center the count
                ctx.DrawTextScaled(ammoText, centerX, centerY, textColor, countFontSize, centerX: true, centerY: true);
            }
        }

        /// <summary>
        /// Calculate font sizes and spacing that scale with widget dimensions.
        /// Returns (countFontSize, typeFontSize, verticalSpacing) in pixels.
        /// </summary>
        private static (int countFontSize, int typeFontSize, float verticalSpacing) CalculateScaledSizes(RectangleF bounds)
        {
            // Use height as primary scaling factor since text is stacked vertically
            float height = bounds.Height;
            float width = bounds.Width;

            // Ammo count should be about 45% of height, type about 25%
            // This leaves room for spacing between them
            int countFontSize = Math.Clamp((int)(height * 0.45f), 10, 72);
            int typeFontSize = Math.Clamp((int)(height * 0.22f), 8, 48);

            // Vertical spacing is distance from center to each text line
            float verticalSpacing = height * 0.18f;

            return (countFontSize, typeFontSize, verticalSpacing);
        }

        /// <summary>
        /// Draw the resize handle indicator in the bottom-right corner.
        /// </summary>
        private void DrawResizeHandle(Dx9RenderContext ctx, RectangleF bounds, float opacity)
        {
            byte alpha = (byte)(opacity * 200);
            var handleColor = _isResizing
                ? new DxColor(255, 200, 100, (byte)(opacity * 255))  // Cyan when resizing
                : new DxColor(200, 200, 200, alpha); // White/gray normally

            // Draw a small filled square in the corner
            var handleRect = new RectangleF(
                bounds.Right - RESIZE_HANDLE_SIZE,
                bounds.Bottom - RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE,
                RESIZE_HANDLE_SIZE);

            ctx.DrawFilledRect(handleRect, handleColor);
        }

        /// <summary>
        /// Parse a hex color string to DxColor.
        /// </summary>
        private static DxColor ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return new DxColor(0, 0, 0, 200);

            try
            {
                var skColor = SKColor.Parse(hex);
                return new DxColor(skColor.Blue, skColor.Green, skColor.Red, skColor.Alpha);
            }
            catch
            {
                return new DxColor(0, 0, 0, 200);
            }
        }
    }
}
