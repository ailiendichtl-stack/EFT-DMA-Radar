/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System;
using System.Diagnostics;
using System.Drawing;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using DxColor = SharpDX.Mathematics.Interop.RawColorBGRA;

namespace LoneEftDmaRadar.UI.ESP
{
    /// <summary>
    /// Draggable and resizable killfeed widget for the ESP overlay.
    /// Minimal Twilight-themed design — no shadows, clean typography.
    /// </summary>
    internal sealed class KillfeedWidget
    {
        private const float HOVER_FADE_MS = 1500f;
        private const int MAX_ENTRIES = 8;
        private const float ROW_HEIGHT = 20f;
        private const float ROW_GAP = 2f;
        private const float PAD_X = 10f;
        private const float PAD_Y = 8f;
        private const float ICON_SIZE = 6f;
        private const float RESIZE_HANDLE_SIZE = 12f;
        private const float MIN_WIDTH = 200f;

        // Twilight palette (BGRA)
        private static readonly DxColor ColorBg = new(0x0D, 0x0D, 0x0D, 0xE0);           // Background #0D0D0D @ 88%
        private static readonly DxColor ColorRowAlt = new(0x1A, 0x1A, 0x1A, 0x60);        // Card #1A1A1A @ 37%
        private static readonly DxColor ColorBorderSubtle = new(0x2A, 0x2A, 0x30, 0x80);  // Subtle purple-tint border
        private static readonly DxColor ColorKiller = new(0xFF, 0xFF, 0xFF, 0xFF);         // Pure white — killer stands out
        private static readonly DxColor ColorArrow = new(0xFF, 0x45, 0x99, 0x90);          // Purple #9945FF @ 56% — accent arrow
        private static readonly DxColor ColorVictim = new(0xCC, 0xCC, 0xCC, 0xFF);         // Light gray #CCCCCC
        private static readonly DxColor ColorDetail = new(0x80, 0x80, 0x80, 0xFF);         // Mid gray #808080 — side/level
        private static readonly DxColor ColorWeapon = new(0xFF, 0x7B, 0xA5, 0xB0);         // Neon soft purple #A57BFF @ 69%
        private static readonly DxColor ColorDot = new(0x66, 0x33, 0xE6, 0xE0);            // Pink #E63366 @ 88%

        // Hover/drag border colors — shared Twilight interaction style
        private static readonly DxColor ColorBorderHover = new(0xFF, 0x7B, 0xA5, 0x8C);   // Purple soft #A57BFF @ 55%
        private static readonly DxColor ColorBorderDrag = new(0xFF, 0x45, 0x99, 0xCC);     // Purple #9945FF @ 80%

        private float _x;
        private float _y;
        private float _width;

        private bool _isDragging;
        private bool _isResizing;
        private bool _isHovering;
        private long _lastHoverTicks;
        private float _dragOffsetX;
        private float _dragOffsetY;
        private float _resizeStartX;
        private float _resizeStartWidth;

        public bool IsInteracting => _isDragging || _isResizing;

        private bool ShouldShowInteractionBorder
        {
            get
            {
                if (_isHovering || IsInteracting) return true;
                var elapsed = (Stopwatch.GetTimestamp() - _lastHoverTicks) * 1000.0 / Stopwatch.Frequency;
                return elapsed < HOVER_FADE_MS;
            }
        }

        private float InteractionBorderOpacity
        {
            get
            {
                if (_isHovering || IsInteracting) return 1.0f;
                var elapsed = (Stopwatch.GetTimestamp() - _lastHoverTicks) * 1000.0 / Stopwatch.Frequency;
                return elapsed >= HOVER_FADE_MS ? 0.0f : 1.0f - (float)(elapsed / HOVER_FADE_MS);
            }
        }

        public KillfeedWidget() => LoadFromConfig();

        private void LoadFromConfig()
        {
            var cfg = App.Config.UI.Killfeed;
            _x = cfg.PositionX;
            _y = cfg.PositionY;
            _width = Math.Max(MIN_WIDTH, cfg.Width);
        }

        public void SaveToConfig()
        {
            var cfg = App.Config.UI.Killfeed;
            cfg.PositionX = _x;
            cfg.PositionY = _y;
            cfg.Width = _width;
        }

        private RectangleF GetBounds(float screenWidth, float screenHeight, int entryCount)
        {
            int count = Math.Max(1, Math.Min(entryCount, MAX_ENTRIES));
            float height = PAD_Y * 2 + count * ROW_HEIGHT + (count - 1) * ROW_GAP;

            float x = _x < 0 ? screenWidth - _width - 20f : _x;
            float y = _y < 0 ? 40f : _y;
            return new RectangleF(x, y, _width, height);
        }

        public bool HitTest(float mouseX, float mouseY, float screenWidth, float screenHeight, int entryCount)
            => GetBounds(screenWidth, screenHeight, entryCount).Contains(mouseX, mouseY);

        private bool IsInResizeHandle(float mouseX, float mouseY, RectangleF bounds)
        {
            float handleX = bounds.Right - RESIZE_HANDLE_SIZE;
            float handleY = bounds.Bottom - RESIZE_HANDLE_SIZE;
            return mouseX >= handleX && mouseY >= handleY &&
                   mouseX <= bounds.Right && mouseY <= bounds.Bottom;
        }

        public bool OnMouseDown(float mouseX, float mouseY, float screenWidth, float screenHeight, int entryCount)
        {
            if (!HitTest(mouseX, mouseY, screenWidth, screenHeight, entryCount))
                return false;

            var bounds = GetBounds(screenWidth, screenHeight, entryCount);

            // Fix auto-position before any interaction
            if (_x < 0) _x = bounds.X;
            if (_y < 0) _y = bounds.Y;

            if (IsInResizeHandle(mouseX, mouseY, bounds))
            {
                _isResizing = true;
                _resizeStartX = mouseX;
                _resizeStartWidth = _width;
            }
            else
            {
                _isDragging = true;
                _dragOffsetX = mouseX - bounds.X;
                _dragOffsetY = mouseY - bounds.Y;
            }

            return true;
        }

        public void OnMouseMove(float mouseX, float mouseY, float screenWidth, float screenHeight)
        {
            if (_isDragging)
            {
                _x = Math.Clamp(mouseX - _dragOffsetX, 0, screenWidth - 100f);
                _y = Math.Clamp(mouseY - _dragOffsetY, 0, screenHeight - 50f);
            }
            else if (_isResizing)
            {
                float delta = mouseX - _resizeStartX;
                _width = Math.Max(MIN_WIDTH, _resizeStartWidth + delta);

                // Clamp to screen
                if (_x + _width > screenWidth)
                    _width = screenWidth - _x;
            }
        }

        public void OnMouseUp()
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                SaveToConfig();
            }
        }

        public void UpdateHoverState(float mouseX, float mouseY, float screenWidth, float screenHeight, int entryCount)
        {
            var bounds = GetBounds(screenWidth, screenHeight, entryCount);
            bool wasHovering = _isHovering;
            _isHovering = bounds.Contains(mouseX, mouseY);
            if (wasHovering && !_isHovering)
                _lastHoverTicks = Stopwatch.GetTimestamp();
        }

        public void Draw(Dx11RenderContext ctx, float screenWidth, float screenHeight)
        {
            var reader = DogtagReader.Instance;
            if (reader is null || reader.Count == 0)
                return;

            var entries = reader.Entries;
            int count = Math.Min(entries.Count, MAX_ENTRIES);
            if (count == 0)
                return;

            var bounds = GetBounds(screenWidth, screenHeight, count);

            // Panel background
            ctx.DrawFilledRect(bounds, ColorBg);

            // Subtle border (always visible — defines the panel edge)
            ctx.DrawRect(bounds, ColorBorderSubtle, 1f);

            // Interaction border + resize handle (hover/drag only, fades out)
            if (ShouldShowInteractionBorder)
            {
                float opacity = InteractionBorderOpacity;
                var borderColor = IsInteracting
                    ? Alpha(ColorBorderDrag, opacity)
                    : Alpha(ColorBorderHover, opacity);
                ctx.DrawRect(bounds, borderColor, 1f);

                // Resize handle — small filled square in bottom-right corner
                var handleColor = _isResizing
                    ? Alpha(ColorBorderDrag, opacity)
                    : Alpha(ColorBorderHover, opacity);
                var handleRect = new RectangleF(
                    bounds.Right - RESIZE_HANDLE_SIZE,
                    bounds.Bottom - RESIZE_HANDLE_SIZE,
                    RESIZE_HANDLE_SIZE,
                    RESIZE_HANDLE_SIZE);
                ctx.DrawFilledRect(handleRect, handleColor);
            }

            float rowY = bounds.Y + PAD_Y;

            for (int i = 0; i < count; i++)
            {
                var entry = entries[i];
                var age = DateTime.UtcNow - entry.Timestamp;

                // Entry fade: full opacity for 2 min, then gradual fade to 40%
                float fade = age.TotalSeconds < 120 ? 1f
                    : Math.Max(0.40f, 1f - (float)((age.TotalMinutes - 2) * 0.10));

                // Alternating row tint
                if (i % 2 == 1)
                {
                    var rowRect = new RectangleF(bounds.X + 1, rowY, bounds.Width - 2, ROW_HEIGHT);
                    ctx.DrawFilledRect(rowRect, Fade(ColorRowAlt, fade));
                }

                float cx = bounds.X + PAD_X;

                // Kill dot — small pink square indicator
                var dotRect = new RectangleF(cx + 1f, rowY + (ROW_HEIGHT - ICON_SIZE) / 2f, ICON_SIZE, ICON_SIZE);
                ctx.DrawFilledRect(dotRect, Fade(ColorDot, fade));
                cx += ICON_SIZE + 6f;

                // Killer name — white, high contrast
                cx = DrawSegment(ctx, entry.KillerName, cx, rowY + 4f, Fade(ColorKiller, fade));

                // Arrow — purple accent
                cx = DrawSegment(ctx, " > ", cx, rowY + 4f, Fade(ColorArrow, fade));

                // Victim name — light gray
                cx = DrawSegment(ctx, entry.Nickname, cx, rowY + 4f, Fade(ColorVictim, fade));

                // Side + Level — mid gray, smaller visual weight
                cx = DrawSegment(ctx, $" {entry.SideName} Lv{entry.Level}", cx, rowY + 4f, Fade(ColorDetail, fade));

                // Weapon — right-aligned, soft purple
                if (!string.IsNullOrEmpty(entry.WeaponName) && entry.WeaponName != "Unknown")
                {
                    var weaponMeasure = ctx.MeasureText(entry.WeaponName, DxTextSize.Small);
                    float weaponWidth = Math.Max(1, weaponMeasure.Right - weaponMeasure.Left);
                    float weaponX = bounds.Right - PAD_X - weaponWidth;

                    if (weaponX > cx + 6f)
                        ctx.DrawText(entry.WeaponName, weaponX, rowY + 4f, Fade(ColorWeapon, fade), DxTextSize.Small);
                }

                rowY += ROW_HEIGHT + ROW_GAP;
            }
        }

        private static float DrawSegment(Dx11RenderContext ctx, string text, float x, float y, DxColor color)
        {
            ctx.DrawText(text, x, y, color, DxTextSize.Small);
            var measured = ctx.MeasureText(text, DxTextSize.Small);
            return x + Math.Max(1, measured.Right - measured.Left);
        }

        private static DxColor Fade(DxColor color, float factor)
        {
            return new DxColor(
                (byte)(color.B * factor),
                (byte)(color.G * factor),
                (byte)(color.R * factor),
                (byte)(color.A * factor));
        }

        private static DxColor Alpha(DxColor color, float opacity)
        {
            return new DxColor(color.B, color.G, color.R, (byte)(color.A * opacity));
        }
    }
}
