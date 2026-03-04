using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;

namespace LoneEftDmaRadar.UI.Skia
{
    /// <summary>
    /// Renders recent kill feed entries as a screen-space overlay on the radar canvas.
    /// Positioned in the top-right corner, entries fade out over time.
    /// </summary>
    internal static class KillFeedRenderer
    {
        private const float Padding = 6f;
        private const float LineSpacing = 2f;
        private const float RightMargin = 10f;
        private const float TopMargin = 10f;
        private const float BackgroundAlpha = 160;

        private static readonly SKFont _font = new(CustomFonts.NeoSansStdRegular, 11f)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

        private static readonly SKPaint _bgPaint = new()
        {
            Color = SKColors.Black.WithAlpha((byte)BackgroundAlpha),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        private static readonly SKPaint _textPaint = new()
        {
            IsStroke = false,
            IsAntialias = true
        };

        private static readonly SKPaint _skullPaint = new()
        {
            Color = SKColors.White,
            IsStroke = false,
            IsAntialias = true
        };

        /// <summary>
        /// Draw kill feed entries on the canvas. Call after all other overlays.
        /// </summary>
        public static void Draw(SKCanvas canvas, float canvasWidth)
        {
            if (!App.Config.KillFeed.Enabled)
                return;

            var maxAge = App.Config.KillFeed.MaxAgeSeconds;
            KillFeedManager.PruneOlderThan(TimeSpan.FromSeconds(maxAge));
            var entries = KillFeedManager.Entries;

            if (entries.Count == 0)
                return;

            var lineHeight = _font.Size + LineSpacing;
            var y = TopMargin;

            foreach (var entry in entries)
            {
                var age = entry.AgeSeconds;
                if (age > maxAge)
                    continue;

                // Fade out in last 25% of lifetime
                var fadeStart = maxAge * 0.75;
                byte alpha = age > fadeStart
                    ? (byte)(255 * (1.0 - (age - fadeStart) / (maxAge - fadeStart)))
                    : (byte)255;

                if (alpha < 10)
                    continue;

                var text = $"X  {entry.VictimName}";
                var textWidth = _font.MeasureText(text, _textPaint);
                var x = canvasWidth - RightMargin - textWidth - Padding * 2;

                // Background
                _bgPaint.Color = SKColors.Black.WithAlpha((byte)(BackgroundAlpha * alpha / 255));
                var bgRect = new SKRect(x, y, canvasWidth - RightMargin, y + lineHeight + Padding);
                canvas.DrawRoundRect(bgRect, 3f, 3f, _bgPaint);

                // Skull marker
                _skullPaint.Color = SKColors.White.WithAlpha(alpha);
                canvas.DrawText("X", new SKPoint(x + Padding, y + Padding + _font.Size * 0.85f), _font, _skullPaint);

                // Victim name in type-appropriate color
                var color = GetColorForType(entry.VictimType);
                _textPaint.Color = color.WithAlpha(alpha);
                canvas.DrawText(entry.VictimName,
                    new SKPoint(x + Padding + _font.MeasureText("X  ", _textPaint), y + Padding + _font.Size * 0.85f),
                    _font, _textPaint);

                y += lineHeight + Padding + 2f;
            }
        }

        private static SKColor GetColorForType(PlayerType type) => type switch
        {
            PlayerType.PMC => SKColors.Red,
            PlayerType.Teammate => SKColors.LimeGreen,
            PlayerType.AIScav => SKColors.Yellow,
            PlayerType.AIRaider => SKColor.Parse("ffc70f"),
            PlayerType.AIBoss => SKColors.Fuchsia,
            PlayerType.PScav => SKColors.White,
            PlayerType.SpecialPlayer => SKColors.HotPink,
            PlayerType.Streamer => SKColors.MediumPurple,
            _ => SKColors.Gray,
        };
    }
}
