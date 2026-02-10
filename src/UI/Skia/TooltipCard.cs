/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 *
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

namespace LoneEftDmaRadar.UI.Skia
{
    /// <summary>
    /// Represents a single row in a tooltip card (label + value pair).
    /// </summary>
    public readonly struct TooltipRow
    {
        public string Label { get; }
        public string Value { get; }
        public SKColor? ValueColor { get; }

        public TooltipRow(string label, string value, SKColor? valueColor = null)
        {
            Label = label;
            Value = value;
            ValueColor = valueColor;
        }
    }

    /// <summary>
    /// Data model for rendering a tooltip card.
    /// </summary>
    public sealed class TooltipData
    {
        public string Header { get; set; }
        public string SubHeader { get; set; }
        public SKColor AccentColor { get; set; } = SKColors.Cyan;
        public List<TooltipRow> Rows { get; } = new();

        public TooltipData(string header, SKColor accentColor)
        {
            Header = header;
            AccentColor = accentColor;
        }

        public TooltipData AddRow(string label, string value, SKColor? valueColor = null)
        {
            Rows.Add(new TooltipRow(label, value, valueColor));
            return this;
        }

        public TooltipData SetSubHeader(string subHeader)
        {
            SubHeader = subHeader;
            return this;
        }
    }

    /// <summary>
    /// Static renderer for styled tooltip cards with accent colors and glow effects.
    /// </summary>
    public static class TooltipCard
    {
        // Card styling constants
        private const float CornerRadius = 6f;
        private const float PaddingX = 10f;
        private const float PaddingY = 8f;
        private const float AccentBarHeight = 3f;
        private const float HeaderSpacing = 4f;
        private const float RowHeight = 18f;
        private const float LabelValueGap = 12f;
        private const float GlowRadius = 4f;
        private const byte GlowAlpha = 80;

        // Colors
        private static readonly SKColor BackgroundColor = new(30, 35, 45, 230);
        private static readonly SKColor LabelColor = new(144, 153, 160, 255); // #9099A0

        /// <summary>
        /// Currently expanded tooltip entity (click to expand).
        /// </summary>
        public static object ExpandedItem { get; set; }

        /// <summary>
        /// Toggle the expanded state for an item.
        /// </summary>
        public static void ToggleExpanded(object item)
        {
            if (ExpandedItem == item)
                ExpandedItem = null;
            else
                ExpandedItem = item;
        }

        /// <summary>
        /// Check if an item is currently expanded.
        /// </summary>
        public static bool IsExpanded(object item) => ExpandedItem != null && ExpandedItem == item;

        /// <summary>
        /// Clear the expanded item (call when mouse leaves hover area).
        /// </summary>
        public static void ClearExpanded() => ExpandedItem = null;

        // Cached fonts (created once on first use, sizes set before each draw call)
        private const float HeaderFontSize = 13f;
        private const float SubHeaderFontSize = 11f;
        private const float RowFontSize = 11f;

        private static SKFont _headerFont;
        private static SKFont _subHeaderFont;
        private static SKFont _rowFont;

        private static void EnsureFonts()
        {
            if (_headerFont != null) return;
            var typeface = CustomFonts.NeoSansStdRegular ?? SKTypeface.Default;
            _headerFont = new SKFont(typeface, HeaderFontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            _subHeaderFont = new SKFont(typeface, SubHeaderFontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
            _rowFont = new SKFont(typeface, RowFontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        }

        // Cached paints - static (never change)
        private static readonly SKPaint _bgPaint = new() { Color = BackgroundColor, Style = SKPaintStyle.Fill, IsAntialias = true };
        private static readonly SKPaint _headerPaint = new() { Color = SKColors.White, IsAntialias = true };
        private static readonly SKPaint _subHeaderPaint = new() { Color = LabelColor, IsAntialias = true };
        private static readonly SKPaint _separatorPaint = new() { Color = new SKColor(60, 70, 90, 180), StrokeWidth = 1f, IsAntialias = true };
        private static readonly SKPaint _labelPaint = new() { Color = LabelColor, IsAntialias = true };

        // Reusable paints - properties mutated before each use (single-threaded render)
        private static readonly SKPaint _glowPaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = true };
        private static readonly SKPaint _borderPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
        private static readonly SKPaint _accentPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
        private static readonly SKPaint _valuePaint = new() { IsAntialias = true };

        // Cached MaskFilter for glow (recreated only when scale changes)
        private static float _lastGlowScale;
        private static SKMaskFilter _glowMaskFilter;

        /// <summary>
        /// Draws a styled tooltip card at the specified position.
        /// </summary>
        /// <param name="canvas">The canvas to draw on.</param>
        /// <param name="position">Top-left position for the card.</param>
        /// <param name="data">Tooltip content data.</param>
        /// <param name="canvasWidth">Optional canvas width for edge detection.</param>
        /// <param name="canvasHeight">Optional canvas height for edge detection.</param>
        public static void Draw(SKCanvas canvas, SKPoint position, TooltipData data,
            float canvasWidth = 0, float canvasHeight = 0)
        {
            if (data == null || string.IsNullOrEmpty(data.Header))
                return;

            var scale = App.Config.UI.UIScale;

            // Calculate card dimensions
            var (cardWidth, cardHeight) = MeasureCard(data, scale);

            // Adjust position to keep card on screen
            var adjustedPos = AdjustPosition(position, cardWidth, cardHeight, canvasWidth, canvasHeight);

            // Draw the card
            DrawCardBackground(canvas, adjustedPos, cardWidth, cardHeight, data.AccentColor, scale);
            DrawCardContent(canvas, adjustedPos, data, cardWidth, scale);
        }

        private static (float width, float height) MeasureCard(TooltipData data, float scale)
        {
            EnsureFonts();
            _headerFont.Size = HeaderFontSize * scale;
            _subHeaderFont.Size = SubHeaderFontSize * scale;
            _rowFont.Size = RowFontSize * scale;

            float maxWidth = _headerFont.MeasureText(data.Header);

            if (!string.IsNullOrEmpty(data.SubHeader))
            {
                var subWidth = _subHeaderFont.MeasureText(data.SubHeader);
                maxWidth = Math.Max(maxWidth, subWidth);
            }

            foreach (var row in data.Rows)
            {
                var labelWidth = _rowFont.MeasureText(row.Label);
                var valueWidth = _rowFont.MeasureText(row.Value);
                var rowWidth = labelWidth + (LabelValueGap * scale) + valueWidth;
                maxWidth = Math.Max(maxWidth, rowWidth);
            }

            float width = maxWidth + (PaddingX * 2 * scale);
            float height = (AccentBarHeight * scale) +
                           (PaddingY * scale) +
                           _headerFont.Size +
                           (string.IsNullOrEmpty(data.SubHeader) ? 0 : (HeaderSpacing * scale) + _subHeaderFont.Size) +
                           (data.Rows.Count > 0 ? (HeaderSpacing * scale) : 0) +
                           (data.Rows.Count * RowHeight * scale) +
                           (PaddingY * scale);

            return (width, height);
        }

        private static SKPoint AdjustPosition(SKPoint pos, float cardWidth, float cardHeight,
            float canvasWidth, float canvasHeight)
        {
            const float margin = 10f;
            float x = pos.X + margin;
            float y = pos.Y + margin;

            // Keep on screen
            if (canvasWidth > 0 && x + cardWidth > canvasWidth - margin)
                x = pos.X - cardWidth - margin;

            if (canvasHeight > 0 && y + cardHeight > canvasHeight - margin)
                y = pos.Y - cardHeight - margin;

            // Ensure not negative
            x = Math.Max(margin, x);
            y = Math.Max(margin, y);

            return new SKPoint(x, y);
        }

        private static void DrawCardBackground(SKCanvas canvas, SKPoint pos, float width, float height,
            SKColor accentColor, float scale)
        {
            var rect = new SKRect(pos.X, pos.Y, pos.X + width, pos.Y + height);
            var roundRect = new SKRoundRect(rect, CornerRadius * scale);

            // Draw glow/shadow (update cached MaskFilter only when scale changes)
            if (_lastGlowScale != scale)
            {
                _glowMaskFilter?.Dispose();
                _glowMaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, GlowRadius * scale * 0.5f);
                _lastGlowScale = scale;
            }
            _glowPaint.Color = accentColor.WithAlpha(GlowAlpha);
            _glowPaint.StrokeWidth = GlowRadius * scale;
            _glowPaint.MaskFilter = _glowMaskFilter;
            canvas.DrawRoundRect(roundRect, _glowPaint);

            // Draw background
            canvas.DrawRoundRect(roundRect, _bgPaint);

            // Draw border
            _borderPaint.Color = accentColor.WithAlpha(120);
            canvas.DrawRoundRect(roundRect, _borderPaint);

            // Draw accent bar at top
            var accentRect = new SKRect(pos.X, pos.Y, pos.X + width, pos.Y + (AccentBarHeight * scale));
            var accentRoundRect = new SKRoundRect(accentRect);
            accentRoundRect.SetRectRadii(accentRect, new[] {
                new SKPoint(CornerRadius * scale, CornerRadius * scale), // top-left
                new SKPoint(CornerRadius * scale, CornerRadius * scale), // top-right
                new SKPoint(0, 0), // bottom-right
                new SKPoint(0, 0)  // bottom-left
            });

            _accentPaint.Color = accentColor;
            canvas.DrawRoundRect(accentRoundRect, _accentPaint);
        }

        private static void DrawCardContent(SKCanvas canvas, SKPoint pos, TooltipData data,
            float cardWidth, float scale)
        {
            EnsureFonts();
            _headerFont.Size = HeaderFontSize * scale;
            _subHeaderFont.Size = SubHeaderFontSize * scale;
            _rowFont.Size = RowFontSize * scale;

            float x = pos.X + (PaddingX * scale);
            float y = pos.Y + (AccentBarHeight * scale) + (PaddingY * scale) + _headerFont.Size;

            // Draw header
            canvas.DrawText(data.Header, x, y, SKTextAlign.Left, _headerFont, _headerPaint);

            // Draw subheader if present
            if (!string.IsNullOrEmpty(data.SubHeader))
            {
                y += (HeaderSpacing * scale) + _subHeaderFont.Size;
                canvas.DrawText(data.SubHeader, x, y, SKTextAlign.Left, _subHeaderFont, _subHeaderPaint);
            }

            // Draw separator line after header
            if (data.Rows.Count > 0)
            {
                y += (HeaderSpacing * scale);
                canvas.DrawLine(x, y, pos.X + cardWidth - (PaddingX * scale), y, _separatorPaint);
            }

            // Draw rows
            foreach (var row in data.Rows)
            {
                y += (RowHeight * scale);

                // Draw label
                canvas.DrawText(row.Label, x, y, SKTextAlign.Left, _rowFont, _labelPaint);

                // Draw value (right-aligned) — mutate cached paint color
                _valuePaint.Color = row.ValueColor ?? SKColors.White;
                var valueX = pos.X + cardWidth - (PaddingX * scale);
                canvas.DrawText(row.Value, valueX, y, SKTextAlign.Right, _rowFont, _valuePaint);
            }
        }
    }

    /// <summary>
    /// Predefined accent colors for different tooltip types.
    /// </summary>
    public static class TooltipColors
    {
        public static readonly SKColor Quest = SKColor.Parse("FF5722");      // Orange-Red
        public static readonly SKColor LootValuable = SKColor.Parse("FFD700"); // Gold
        public static readonly SKColor LootQuest = SKColor.Parse("9ACD32");   // YellowGreen
        public static readonly SKColor LootHideout = SKColor.Parse("FFA500"); // Orange
        public static readonly SKColor Container = SKColor.Parse("607D8B");   // BlueGray
        public static readonly SKColor EnemyPMC = SKColor.Parse("F44336");    // Red
        public static readonly SKColor EnemyScav = SKColor.Parse("FFEB3B");   // Yellow
        public static readonly SKColor EnemyBoss = SKColor.Parse("FF9800");   // Orange
        public static readonly SKColor EnemyRaider = SKColor.Parse("FFC70F"); // Amber
        public static readonly SKColor Teammate = SKColor.Parse("4CAF50");    // Green
        public static readonly SKColor Default = SKColor.Parse("9E9E9E");     // Gray
        public static readonly SKColor ExtractOpen = SKColor.Parse("32CD32");    // LimeGreen
        public static readonly SKColor ExtractPending = SKColor.Parse("FFD700"); // Gold/Yellow
        public static readonly SKColor ExtractClosed = SKColor.Parse("CD5C5C");  // IndianRed
    }
}
