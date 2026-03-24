using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using WinForms = System.Windows.Forms;
using DxColor = SharpDX.Mathematics.Interop.RawColorBGRA;
using LoneEftDmaRadar.UI.Misc;

using D2DFactory = SharpDX.Direct2D1.Factory;
using D2DBitmap = SharpDX.Direct2D1.Bitmap;
using D2DPixelFormat = SharpDX.Direct2D1.PixelFormat;
using D3D11Device = SharpDX.Direct3D11.Device;
using DWriteFactory = SharpDX.DirectWrite.Factory;
using TextFormat = SharpDX.DirectWrite.TextFormat;
using TextLayout = SharpDX.DirectWrite.TextLayout;

namespace LoneEftDmaRadar.UI.ESP
{
    internal enum DxTextSize
    {
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// DX11 overlay control using Direct2D1 + DirectWrite for high-performance 2D ESP rendering.
    /// Drop-in replacement for the former DX9 overlay with identical public API.
    /// </summary>
    internal sealed class Dx11OverlayControl : WinForms.Control, IDisposable
    {
        public event Action<Exception> DeviceInitFailed;

        private D3D11Device _d3dDevice;
        private SwapChain _swapChain;
        private D2DFactory _d2dFactory;
        private DWriteFactory _dwFactory;
        private RenderTarget _renderTarget;
        private SolidColorBrush _brush;

        private TextFormat _fontSmall;
        private TextFormat _fontMedium;
        private TextFormat _fontLarge;
        private readonly ConcurrentDictionary<int, TextFormat> _fontCache = new();

        private string _fontFamily = "Segoe UI";
        private int _fontSizeSmall = 10;
        private int _fontSizeMedium = 12;
        private int _fontSizeLarge = 24;

        private D2DBitmap _mapBitmap;
        private byte[] _pendingMapPixels;
        private int _pendingMapW, _pendingMapH;
        private bool _mapUpdatePending;

        private readonly object _deviceLock = new();
        private bool _vsync;

        /// <summary>
        /// The actual D3D feature level negotiated with the GPU (e.g. "11.1", "11.0", "10.1").
        /// </summary>
        public string FeatureLevelString { get; private set; } = "Unknown";

        public Action<Dx11RenderContext> RenderFrame;

        public Dx11OverlayControl()
        {
            SetStyle(WinForms.ControlStyles.AllPaintingInWmPaint |
                     WinForms.ControlStyles.OptimizedDoubleBuffer |
                     WinForms.ControlStyles.Opaque |
                     WinForms.ControlStyles.UserPaint, true);
            DoubleBuffered = true;
            BackColor = Color.Black;
            TabStop = false;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                InitializeDevice();
            }
            catch (Exception ex)
            {
                DeviceInitFailed?.Invoke(ex);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            ResizeSwapChain();
        }

        protected override void OnPaint(WinForms.PaintEventArgs e)
        {
            Render();
        }

        protected override void OnPaintBackground(WinForms.PaintEventArgs pevent)
        {
            // Suppress default background painting to avoid flicker.
        }

        public void Render()
        {
            lock (_deviceLock)
            {
                if (_renderTarget is null || _swapChain is null)
                    return;

                try
                {
                    if (_mapUpdatePending)
                    {
                        UpdateMapBitmapInternal(_pendingMapW, _pendingMapH, _pendingMapPixels);
                        _mapUpdatePending = false;
                        _pendingMapPixels = null;
                    }

                    _renderTarget.BeginDraw();
                    _renderTarget.Clear(new RawColor4(0, 0, 0, 1));

                    var ctx = new Dx11RenderContext(
                        _renderTarget, _brush, _dwFactory,
                        _fontSmall, _fontMedium, _fontLarge,
                        _mapBitmap, Width, Height, GetOrCreateFont);
                    RenderFrame?.Invoke(ctx);

                    _renderTarget.EndDraw();
                    _swapChain.Present(_vsync ? 1 : 0, PresentFlags.None);
                }
                catch (SharpDXException)
                {
                    ResetDevice();
                }
            }
        }

        private void InitializeDevice()
        {
            DisposeDevice();

            int w = Math.Max(Width, 1);
            int h = Math.Max(Height, 1);

            var swapDesc = new SwapChainDescription
            {
                BufferCount = 1,
                ModeDescription = new ModeDescription(w, h, new Rational(0, 1), Format.B8G8R8A8_UNorm),
                IsWindowed = true,
                OutputHandle = Handle,
                SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.Discard,
                Usage = Usage.RenderTargetOutput,
                Flags = SwapChainFlags.AllowModeSwitch
            };

            D3D11Device.CreateWithSwapChain(
                SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                new[]
                {
                    SharpDX.Direct3D.FeatureLevel.Level_11_1,
                    SharpDX.Direct3D.FeatureLevel.Level_11_0,
                    SharpDX.Direct3D.FeatureLevel.Level_10_1,
                    SharpDX.Direct3D.FeatureLevel.Level_10_0
                },
                swapDesc,
                out _d3dDevice,
                out _swapChain);

            // Record the actual feature level the GPU negotiated
            FeatureLevelString = _d3dDevice.FeatureLevel switch
            {
                SharpDX.Direct3D.FeatureLevel.Level_11_1 => "11.1",
                SharpDX.Direct3D.FeatureLevel.Level_11_0 => "11.0",
                SharpDX.Direct3D.FeatureLevel.Level_10_1 => "10.1",
                SharpDX.Direct3D.FeatureLevel.Level_10_0 => "10.0",
                _ => _d3dDevice.FeatureLevel.ToString()
            };

            // Disable ALT+Enter fullscreen toggle
            using (var factory = _swapChain.GetParent<SharpDX.DXGI.Factory>())
                factory.MakeWindowAssociation(Handle, WindowAssociationFlags.IgnoreAll);

            _d2dFactory = new D2DFactory(SharpDX.Direct2D1.FactoryType.SingleThreaded);
            _dwFactory = new DWriteFactory(SharpDX.DirectWrite.FactoryType.Shared);

            CreateRenderTarget();
            RebuildFonts();
        }

        private void CreateRenderTarget()
        {
            _brush?.Dispose();
            _brush = null;
            _renderTarget?.Dispose();
            _renderTarget = null;

            using var backBuffer = _swapChain.GetBackBuffer<Surface>(0);
            var rtProps = new RenderTargetProperties(
                new D2DPixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));

            _renderTarget = new RenderTarget(_d2dFactory, backBuffer, rtProps);
            _renderTarget.AntialiasMode = AntialiasMode.PerPrimitive;
            _renderTarget.TextAntialiasMode = TextAntialiasMode.Cleartype;

            _brush = new SolidColorBrush(_renderTarget, new RawColor4(1, 1, 1, 1));
        }

        private void ResizeSwapChain()
        {
            lock (_deviceLock)
            {
                if (_swapChain is null)
                    return;

                _brush?.Dispose(); _brush = null;
                _renderTarget?.Dispose(); _renderTarget = null;
                _mapBitmap?.Dispose(); _mapBitmap = null;

                int w = Math.Max(Width, 1);
                int h = Math.Max(Height, 1);

                try
                {
                    _swapChain.ResizeBuffers(1, w, h, Format.B8G8R8A8_UNorm, SwapChainFlags.AllowModeSwitch);
                    CreateRenderTarget();
                }
                catch (SharpDXException)
                {
                    ResetDevice();
                }
            }
        }

        private void ResetDevice()
        {
            DisposeDevice();
            try
            {
                InitializeDevice();
            }
            catch (Exception ex)
            {
                DeviceInitFailed?.Invoke(ex);
            }
        }

        /// <summary>
        /// Enable or disable VSync. Takes effect on the next Present() call — no device reset needed.
        /// </summary>
        public void SetVSync(bool enabled)
        {
            lock (_deviceLock)
            {
                _vsync = enabled;
            }
        }

        /// <summary>
        /// Update font family/sizes and rebuild DirectWrite text formats. Safe to call at runtime.
        /// </summary>
        public void SetFontConfig(string fontFamily, int small, int medium, int large)
        {
            lock (_deviceLock)
            {
                if (!string.IsNullOrWhiteSpace(fontFamily))
                    _fontFamily = fontFamily.Trim();

                _fontSizeSmall = ClampFontSize(small);
                _fontSizeMedium = ClampFontSize(medium);
                _fontSizeLarge = ClampFontSize(large);

                if (_dwFactory != null)
                    RebuildFonts();
            }
        }

        /// <summary>
        /// Schedules a map texture update for the next frame.
        /// </summary>
        public void RequestMapTextureUpdate(int width, int height, byte[] bgraPixels)
        {
            lock (_deviceLock)
            {
                _pendingMapW = width;
                _pendingMapH = height;
                _pendingMapPixels = bgraPixels;
                _mapUpdatePending = true;
            }
        }

        public string LastTextureError { get; private set; } = "None";

        private void UpdateMapBitmapInternal(int width, int height, byte[] bgraPixels)
        {
            if (_renderTarget is null)
            {
                LastTextureError = "RenderTarget is null";
                return;
            }

            int expectedBytes = width * height * 4;
            if (bgraPixels.Length != expectedBytes)
            {
                LastTextureError = $"Size mismatch: {bgraPixels.Length} != {expectedBytes}";
                return;
            }

            try
            {
                if (_mapBitmap is null ||
                    _mapBitmap.PixelSize.Width != width ||
                    _mapBitmap.PixelSize.Height != height)
                {
                    _mapBitmap?.Dispose();
                    var bmpProps = new BitmapProperties(
                        new D2DPixelFormat(Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied));
                    _mapBitmap = new D2DBitmap(_renderTarget, new Size2(width, height), bmpProps);
                }

                var handle = GCHandle.Alloc(bgraPixels, GCHandleType.Pinned);
                try
                {
                    _mapBitmap.CopyFromMemory(handle.AddrOfPinnedObject(), width * 4);
                }
                finally
                {
                    handle.Free();
                }

                LastTextureError = "Success";
            }
            catch (Exception ex)
            {
                LastTextureError = $"Update Failed: {ex.Message}";
                DebugLogger.LogDebug($"[Dx11Overlay] Map Update Error: {ex}");
            }
        }

        private static int ClampFontSize(int value)
        {
            if (value <= 0) return 10;
            return Math.Clamp(value, 6, 72);
        }

        private TextFormat GetOrCreateFont(int size)
        {
            size = ClampFontSize(size);
            return _fontCache.GetOrAdd(size, s => CreateTextFormat(s));
        }

        private TextFormat CreateTextFormat(int height)
        {
            var fmt = new TextFormat(_dwFactory, _fontFamily, height)
            {
                WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap
            };
            return fmt;
        }

        private void ClearFontCache()
        {
            foreach (var fmt in _fontCache.Values)
                fmt?.Dispose();
            _fontCache.Clear();
        }

        private void RebuildFonts()
        {
            _fontSmall?.Dispose();
            _fontMedium?.Dispose();
            _fontLarge?.Dispose();
            ClearFontCache();

            _fontSmall = CreateTextFormat(_fontSizeSmall);
            _fontMedium = CreateTextFormat(_fontSizeMedium);
            _fontLarge = CreateTextFormat(_fontSizeLarge);
        }

        private void DisposeDevice()
        {
            _brush?.Dispose();
            _fontSmall?.Dispose();
            _fontMedium?.Dispose();
            _fontLarge?.Dispose();
            ClearFontCache();
            _mapBitmap?.Dispose();
            _renderTarget?.Dispose();
            _swapChain?.Dispose();
            _d3dDevice?.Dispose();
            _dwFactory?.Dispose();
            _d2dFactory?.Dispose();

            _brush = null;
            _fontSmall = null;
            _fontMedium = null;
            _fontLarge = null;
            _mapBitmap = null;
            _renderTarget = null;
            _swapChain = null;
            _d3dDevice = null;
            _dwFactory = null;
            _d2dFactory = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_deviceLock)
                {
                    DisposeDevice();
                }
            }

            base.Dispose(disposing);
        }
    }

    internal readonly struct Dx11RenderContext
    {
        private readonly RenderTarget _rt;
        private readonly SolidColorBrush _brush;
        private readonly DWriteFactory _dwFactory;
        private readonly TextFormat _fontSmall;
        private readonly TextFormat _fontMedium;
        private readonly TextFormat _fontLarge;
        private readonly D2DBitmap _mapBitmap;
        private readonly Func<int, TextFormat> _fontGetter;

        public int Width { get; }
        public int Height { get; }

        public Dx11RenderContext(
            RenderTarget rt,
            SolidColorBrush brush,
            DWriteFactory dwFactory,
            TextFormat fontSmall,
            TextFormat fontMedium,
            TextFormat fontLarge,
            D2DBitmap mapBitmap,
            int width,
            int height,
            Func<int, TextFormat> fontGetter = null)
        {
            _rt = rt;
            _brush = brush;
            _dwFactory = dwFactory;
            _fontSmall = fontSmall;
            _fontMedium = fontMedium;
            _fontLarge = fontLarge;
            _mapBitmap = mapBitmap;
            Width = width;
            Height = height;
            _fontGetter = fontGetter;
        }

        private static RawColor4 ToColor4(DxColor c) =>
            new RawColor4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

        public void Clear(DxColor color)
        {
            _rt.Clear(ToColor4(color));
        }

        public void DrawLine(RawVector2 p1, RawVector2 p2, DxColor color, float thickness)
        {
            _brush.Color = ToColor4(color);
            _rt.DrawLine(p1, p2, _brush, Math.Max(1f, thickness));
        }

        public void DrawRect(RectangleF rect, DxColor color, float thickness)
        {
            _brush.Color = ToColor4(color);
            _rt.DrawRectangle(
                new RawRectangleF(rect.Left, rect.Top, rect.Right, rect.Bottom),
                _brush, Math.Max(1f, thickness));
        }

        public void DrawFilledRect(RectangleF rect, DxColor color)
        {
            _brush.Color = ToColor4(color);
            _rt.FillRectangle(
                new RawRectangleF(rect.Left, rect.Top, rect.Right, rect.Bottom),
                _brush);
        }

        public void DrawCircle(RawVector2 center, float radius, DxColor color, bool filled)
        {
            if (radius <= 0.5f)
                return;

            _brush.Color = ToColor4(color);
            var ellipse = new Ellipse(center, radius, radius);

            if (filled)
                _rt.FillEllipse(ellipse, _brush);
            else
                _rt.DrawEllipse(ellipse, _brush, 1.5f);
        }

        public void DrawMapTexture(RectangleF destRect, float opacity = 1.0f)
        {
            if (_mapBitmap is null)
            {
                DrawRect(destRect, new DxColor(255, 0, 255, 128), 2f);
                return;
            }

            _rt.DrawBitmap(
                _mapBitmap,
                new RawRectangleF(destRect.Left, destRect.Top, destRect.Right, destRect.Bottom),
                opacity,
                BitmapInterpolationMode.Linear);
        }

        public RawRectangle MeasureText(string text, DxTextSize size)
        {
            var format = GetFont(size);
            if (format is null) return new RawRectangle(0, 0, 0, 0);

            using var layout = new TextLayout(_dwFactory, text, format, float.MaxValue, float.MaxValue);
            var m = layout.Metrics;
            return new RawRectangle(0, 0, (int)Math.Ceiling(m.Width), (int)Math.Ceiling(m.Height));
        }

        public void DrawText(string text, float x, float y, DxColor color, DxTextSize size, bool centerX = false, bool centerY = false)
        {
            var format = GetFont(size);
            if (format is null) return;

            if (centerX || centerY)
            {
                using var layout = new TextLayout(_dwFactory, text, format, float.MaxValue, float.MaxValue);
                var m = layout.Metrics;
                if (centerX) x -= m.Width / 2f;
                if (centerY) y -= m.Height / 2f;
            }

            _brush.Color = ToColor4(color);
            _rt.DrawText(text, format,
                new RawRectangleF(x, y, x + 4096, y + 4096),
                _brush, DrawTextOptions.None);
        }

        /// <summary>
        /// Draw text with a specific font size in pixels.
        /// </summary>
        public void DrawTextScaled(string text, float x, float y, DxColor color, int fontSize, bool centerX = false, bool centerY = false)
        {
            if (_fontGetter is null) return;

            var format = _fontGetter(fontSize);
            if (format is null) return;

            if (centerX || centerY)
            {
                using var layout = new TextLayout(_dwFactory, text, format, float.MaxValue, float.MaxValue);
                var m = layout.Metrics;
                if (centerX) x -= m.Width / 2f;
                if (centerY) y -= m.Height / 2f;
            }

            _brush.Color = ToColor4(color);
            _rt.DrawText(text, format,
                new RawRectangleF(x, y, x + 4096, y + 4096),
                _brush, DrawTextOptions.None);
        }

        private TextFormat GetFont(DxTextSize size) => size switch
        {
            DxTextSize.Small => _fontSmall,
            DxTextSize.Large => _fontLarge,
            _ => _fontMedium
        };
    }
}
