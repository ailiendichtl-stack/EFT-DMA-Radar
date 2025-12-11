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

using Collections.Pooled;
using LoneEftDmaRadar.UI.Skia;
using SkiaSharp.Views.WPF;
using Svg.Skia;
using System.IO.Compression;

namespace LoneEftDmaRadar.UI.Radar.Maps
{
    /// <summary>
    /// SVG map implementation that keeps layers as vector SKPicture objects (no pre-rasterization)
    /// and renders them each frame with appropriate scaling, height filtering and optional dimming.
    /// </summary>
    public sealed class EftSvgMap : IEftMap
    {
        private readonly VectorLayer[] _layers;

        /// <summary>Raw map ID.</summary>
        public string ID { get; }
        /// <summary>Loaded configuration for this map instance.</summary>
        public EftMapConfig Config { get; }
        /// <summary>Tracks the last valid zoom level to prevent zoom value drift at boundaries.</summary>
        private static int _lastValidZoom = 100;
        /// <summary>Redundant horizontal space to add around map boundaries.</summary>
        private const float HORIZONTAL_PADDING = 100f;
        /// <summary>Redundant vertical space to add around map boundaries.</summary>
        private const float VERTICAL_PADDING = 35f;

        /// <summary>
        /// Construct a new vector map by loading each SVG layer from the supplied zip archive.
        /// Layers are stored as SKSvg (vector) instead of rasterizing to SKImage.
        /// </summary>
        /// <param name="zip">Archive containing the SVG layer files.</param>
        /// <param name="id">External map identifier.</param>
        /// <param name="config">Configuration describing layers and scaling.</param>
        /// <exception cref="InvalidOperationException">Thrown if any SVG fails to load.</exception>
        public EftSvgMap(ZipArchive zip, string id, EftMapConfig config)
        {
            ID = id;
            Config = config;

            var loaded = new List<VectorLayer>();
            try
            {
                foreach (var layerCfg in config.MapLayers)
                {
                    var entry = zip.Entries.First(x =>
                        x.Name.Equals(layerCfg.Filename, StringComparison.OrdinalIgnoreCase));

                    using var stream = entry.Open();

                    var svg = new SKSvg();
                    if (svg.Load(stream) is null || svg.Picture is null)
                        throw new InvalidOperationException($"Failed to load SVG '{layerCfg.Filename}'.");

                    loaded.Add(new VectorLayer(svg, layerCfg));
                }

                _layers = loaded.Order().ToArray();
            }
            catch
            {
                foreach (var l in loaded) l.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Draw visible layers into the target canvas.
        /// Applies:
        ///  - Height filtering
        ///  - Map bounds → window bounds transform
        ///  - Configured SVG scale
        ///  - Optional dimming of non-top layers
        ///  - Transparent clearing of the window region
        /// </summary>
        /// <param name="canvas">Destination Skia canvas.</param>
        /// <param name="playerHeight">Current player Y height for layer filtering.</param>
        /// <param name="mapBounds">Logical source rectangle (in map coordinates) to show.</param>
        /// <param name="windowBounds">Destination rectangle inside the control.</param>
        public void Draw(SKCanvas canvas, float playerHeight, SKRect mapBounds, SKRect windowBounds)
        {
            if (_layers.Length == 0) return;

            using var visible = new PooledList<VectorLayer>(capacity: 8);
            foreach (var layer in _layers)
            {
                if (layer.IsHeightInRange(playerHeight))
                    visible.Add(layer);
            }

            if (visible.Count == 0) return;
            visible.Sort();

            float scaleX = windowBounds.Width / mapBounds.Width;
            float scaleY = windowBounds.Height / mapBounds.Height;

            canvas.Save();
            // Map coordinate system -> window region
            canvas.Translate(windowBounds.Left, windowBounds.Top);
            canvas.Scale(scaleX, scaleY);
            canvas.Translate(-mapBounds.Left, -mapBounds.Top);
            // Apply configured vector scaling
            canvas.Scale(Config.SvgScale, Config.SvgScale);

            var front = visible[^1];
            foreach (var layer in visible)
            {
                bool dim = !Config.DisableDimming &&        // Make sure dimming is enabled globally
                           layer != front &&                // Make sure the current layer is not in front
                           !front.CannotDimLowerLayers;     // Don't dim the lower layers if the front layer has dimming disabled upon lower layers

                var paint = dim ?
                    SKPaints.PaintBitmapAlpha : SKPaints.PaintBitmap;
                canvas.DrawPicture(layer.Picture, paint);
            }

            canvas.Restore();
        }

        /// <summary>
        /// Compute per-frame map parameters (bounds and scaling factors) based on the
        /// current zoom and player-centered position. Returns the rectangle of the map
        /// (in map coordinates) that should be displayed and the X/Y zoom scale factors.
        /// </summary>
        /// <param name="control">Skia GL element hosting the canvas.</param>
        /// <param name="zoom">Zoom percentage (e.g. 100 = 1:1).</param>
        /// <param name="localPlayerMapPos">Player map-space position (center target); value may be adjusted externally.</param>
        /// <returns>Computed parameters for rendering this frame.</returns>
        public EftMapParams GetParameters(SKGLElement control, int zoom, ref Vector2 localPlayerMapPos)
        {
            if (_layers.Length == 0)
            {
                return new EftMapParams
                {
                    Map = Config,
                    Bounds = SKRect.Empty,
                    XScale = 1f,
                    YScale = 1f
                };
            }

            var baseLayer = _layers[0];

            float fullWidth = baseLayer.RawWidth * Config.SvgScale;
            float fullHeight = baseLayer.RawHeight * Config.SvgScale;

            var zoomWidth = fullWidth * (0.01f * zoom);
            var zoomHeight = fullHeight * (0.01f * zoom);

            var size = control.CanvasSize;
            var bounds = new SKRect(
                localPlayerMapPos.X - zoomWidth * 0.5f,
                localPlayerMapPos.Y - zoomHeight * 0.5f,
                localPlayerMapPos.X + zoomWidth * 0.5f,
                localPlayerMapPos.Y + zoomHeight * 0.5f
            );

            var (constrainedBounds, wasZoomApplied) = ConstrainBoundsToMap(bounds, fullWidth, fullHeight, zoomWidth, zoomHeight, ref localPlayerMapPos);
            bounds = constrainedBounds;

            if (!wasZoomApplied)
            {
                if (App.Config.UI.Zoom != _lastValidZoom)
                {
                    App.Config.UI.Zoom = _lastValidZoom;
                }
            }
            else
            {
                _lastValidZoom = zoom;
            }

            bounds = bounds.AspectFill(size);

            return new EftMapParams
            {
                Map = Config,
                Bounds = bounds,
                XScale = (float)size.Width / bounds.Width,
                YScale = (float)size.Height / bounds.Height
            };
        }

        /// <summary>
        /// Constrain map bounds to prevent showing black areas outside the map borders.
        /// This ensures the map view stays within the actual map boundaries.
        /// Returns whether the zoom operation was actually applied.
        /// </summary>
        /// <param name="bounds">Initial calculated bounds based on player position.</param>
        /// <param name="mapWidth">Total width of the map.</param>
        /// <param name="mapHeight">Total height of the map.</param>
        /// <param name="viewWidth">Width of the current view (zoomed area).</param>
        /// <param name="viewHeight">Height of the current view (zoomed area).</param>
        /// <param name="adjustedPlayerPos">Adjusted player position that respects boundaries.</param>
        /// <returns>Tuple of (constrained bounds, wasZoomApplied).</returns>
        private (SKRect bounds, bool wasZoomApplied) ConstrainBoundsToMap(SKRect bounds, float mapWidth, float mapHeight,
            float viewWidth, float viewHeight, ref Vector2 adjustedPlayerPos)
        {
            // Calculate the minimum and maximum bounds that can be shown with padding
            // This allows some extra space around the map edges for better visibility
            // Use separate padding values for horizontal and vertical directions
            float minLeft = -HORIZONTAL_PADDING;
            float minTop = -VERTICAL_PADDING;
            float maxRight = mapWidth + HORIZONTAL_PADDING;
            float maxBottom = mapHeight + VERTICAL_PADDING;

            bool wasZoomApplied = true;

            if (viewWidth < mapWidth)
            {
                if (bounds.Left < minLeft)
                {
                    float shift = minLeft - bounds.Left;
                    bounds.Left += shift;
                    bounds.Right += shift;
                    adjustedPlayerPos.X += shift;
                }
                else if (bounds.Right > maxRight)
                {
                    float shift = bounds.Right - maxRight;
                    bounds.Left -= shift;
                    bounds.Right -= shift;
                    adjustedPlayerPos.X -= shift;
                }
            }
            else
            {
                float maxAcceptableWidth = mapWidth + (HORIZONTAL_PADDING * 2f);
                if (viewWidth > maxAcceptableWidth)
                {
                    wasZoomApplied = false;
                    bounds.Left = minLeft;
                    bounds.Right = maxRight;
                    adjustedPlayerPos.X = mapWidth * 0.5f;
                }
                else
                {
                    bounds.Left = minLeft;
                    bounds.Right = maxRight;
                    adjustedPlayerPos.X = mapWidth * 0.5f;
                }
            }

            if (viewHeight < mapHeight)
            {
                if (bounds.Top < minTop)
                {
                    float shift = minTop - bounds.Top;
                    bounds.Top += shift;
                    bounds.Bottom += shift;
                    adjustedPlayerPos.Y += shift;
                }
                else if (bounds.Bottom > maxBottom)
                {
                    float shift = bounds.Bottom - maxBottom;
                    bounds.Top -= shift;
                    bounds.Bottom -= shift;
                    adjustedPlayerPos.Y -= shift;
                }
            }
            else
            {
                float maxAcceptableHeight = mapHeight + (VERTICAL_PADDING * 2f);
                if (viewHeight > maxAcceptableHeight && wasZoomApplied)
                {
                    wasZoomApplied = false;
                    bounds.Top = minTop;
                    bounds.Bottom = maxBottom;
                    adjustedPlayerPos.Y = mapHeight * 0.5f;
                }
                else
                {
                    bounds.Top = minTop;
                    bounds.Bottom = maxBottom;
                    adjustedPlayerPos.Y = mapHeight * 0.5f;
                }
            }

            return (bounds, wasZoomApplied);
        }
        
        /// <summary>
        /// Dispose all vector layers (releasing their SKSvg / SKPicture resources).
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < _layers.Length; i++)
                _layers[i].Dispose();
        }

        /// <summary>
        /// Internal wrapper for a single SVG layer that preserves the SKSvg lifetime
        /// (disposing SKSvg also disposes its SKPicture). Stores raw (unscaled) dimensions.
        /// </summary>
        private sealed class VectorLayer : IComparable<VectorLayer>, IDisposable
        {
            private readonly SKSvg _svg;
            public readonly bool IsBaseLayer;
            public readonly bool CannotDimLowerLayers;
            public readonly float? MinHeight;
            public readonly float? MaxHeight;
            public readonly float RawWidth;
            public readonly float RawHeight;

            /// <summary>
            /// The SKPicture representing this layer's vector content.
            /// </summary>
            public SKPicture Picture => _svg.Picture!;

            /// <summary>
            /// Create a vector layer from a loaded SKSvg and its configuration.
            /// </summary>
            public VectorLayer(SKSvg svg, EftMapConfig.Layer cfgLayer)
            {
                _svg = svg;
                IsBaseLayer = cfgLayer.MinHeight is null && cfgLayer.MaxHeight is null;
                CannotDimLowerLayers = cfgLayer.CannotDimLowerLayers;
                MinHeight = cfgLayer.MinHeight;
                MaxHeight = cfgLayer.MaxHeight;

                var cr = svg.Picture!.CullRect;
                RawWidth = cr.Width;
                RawHeight = cr.Height;
            }

            /// <summary>
            /// Determines whether the provided height is inside this layer's vertical range.
            /// Base layers always return true.
            /// </summary>
            public bool IsHeightInRange(float h)
            {
                if (IsBaseLayer) return true;
                if (MinHeight.HasValue && h < MinHeight.Value) return false;
                if (MaxHeight.HasValue && h > MaxHeight.Value) return false;
                return true;
            }

            /// <summary>
            /// Ordering: base layers first, then ascending MinHeight, then ascending MaxHeight.
            /// </summary>
            public int CompareTo(VectorLayer other)
            {
                if (other is null) return -1;
                if (IsBaseLayer && !other.IsBaseLayer)
                    return -1;
                if (!IsBaseLayer && other.IsBaseLayer)
                    return 1;

                var thisMin = MinHeight ?? float.MinValue;
                var otherMin = other.MinHeight ?? float.MinValue;
                int cmp = thisMin.CompareTo(otherMin);
                if (cmp != 0) return cmp;

                var thisMax = MaxHeight ?? float.MaxValue;
                var otherMax = other.MaxHeight ?? float.MaxValue;
                return thisMax.CompareTo(otherMax);
            }

            /// <summary>Dispose backing SKSvg (and thus the SKPicture).</summary>
            public void Dispose() => _svg.Dispose();
        }

    }
}
