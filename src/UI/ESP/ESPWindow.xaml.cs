using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Explosives;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Quests;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System.Drawing;
using System.Linq;
using System.Collections.Concurrent;
using System.Windows.Input;
using System.Windows.Threading;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.DMA;
using SharpDX;
using SharpDX.Mathematics.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using WinForms = System.Windows.Forms;
using SkiaSharp;
using DxColor = SharpDX.Mathematics.Interop.RawColorBGRA;
using LoneEftDmaRadar.Tarkov.GameWorld.Camera;
using LoneEftDmaRadar.UI.Radar;
using LoneEftDmaRadar.UI.Radar.Maps;

namespace LoneEftDmaRadar.UI.ESP
{
    public partial class ESPWindow : Window
    {
        #region Fields/Properties

        private const int MINIRADAR_SIZE = 256;
        private string _lastMapId;
        private MiniRadarParams _miniRadarParams;

        private struct MiniRadarParams
        {
            public float Scale;
            public float OffsetX;
            public float OffsetY;
            public float ScreenX;
            public float ScreenY;
            public float DrawSize;
            public bool IsValid;
        }

        public static bool ShowESP { get; set; } = true;
        private bool _dxInitFailed;

        private readonly System.Diagnostics.Stopwatch _fpsSw = new();
        private int _fpsCounter;
        private int _fps;
        private long _lastFrameTicks;
        private Timer _highFrequencyTimer;
        private int _renderPending;

        // Render surface
        private Dx9OverlayControl _dxOverlay;
        private WindowsFormsHost _dxHost;
        private bool _isClosing;

        // Cached Fonts/Paints
        private readonly SKPaint _skeletonPaint;
        private readonly SKPaint _boxPaint;
        private readonly SKPaint _crosshairPaint;
        private static readonly SKColor[] _espGroupPalette = new SKColor[]
        {
            SKColors.MediumSlateBlue,
            SKColors.MediumSpringGreen,
            SKColors.CadetBlue,
            SKColors.MediumOrchid,
            SKColors.PaleVioletRed,
            SKColors.SteelBlue,
            SKColors.DarkSeaGreen,
            SKColors.Chocolate
        };
        private static readonly ConcurrentDictionary<int, SKPaint> _espGroupPaints = new();

        private bool _isFullscreen;

        // Ammo Counter Widget
        private AmmoCounterWidget _ammoWidget;
        private float _mouseX;
        private float _mouseY;

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory.Players;

        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static IReadOnlyCollection<IExplosiveItem> Explosives => Memory.Explosives;

        private static bool InRaid => Memory.InRaid;

        // Bone Connections for Skeleton
        private static readonly (Bones From, Bones To)[] _boneConnections = new[]
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),
            
            // Left Arm
            (Bones.HumanNeck, Bones.HumanLUpperarm), // Shoulder approx
            (Bones.HumanLUpperarm, Bones.HumanLForearm1),
            (Bones.HumanLForearm1, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),
            
            // Right Arm
            (Bones.HumanNeck, Bones.HumanRUpperarm), // Shoulder approx
            (Bones.HumanRUpperarm, Bones.HumanRForearm1),
            (Bones.HumanRForearm1, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),
            
            // Left Leg
            (Bones.HumanPelvis, Bones.HumanLThigh1),
            (Bones.HumanLThigh1, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLCalf),
            (Bones.HumanLCalf, Bones.HumanLFoot),
            
            // Right Leg
            (Bones.HumanPelvis, Bones.HumanRThigh1),
            (Bones.HumanRThigh1, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRCalf),
            (Bones.HumanRCalf, Bones.HumanRFoot),
        };

        #endregion

        public ESPWindow()
        {
            InitializeComponent();
            InitializeRenderSurface();
            
            // Initial sizes
            this.Width = 400;
            this.Height = 300;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Cache paints/fonts
            _skeletonPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _boxPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.0f,
                IsAntialias = false, // Crisper boxes
                Style = SKPaintStyle.Stroke
            };

            _crosshairPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _fpsSw.Start();
            _lastFrameTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            _highFrequencyTimer = new System.Threading.Timer(
                callback: HighFrequencyRenderCallback,
                state: null,
                dueTime: 0,
                period: App.Config.Debug.EspTimerPeriodMs); // Default 2ms, configurable in Debug panel

            // Initialize ammo counter widget
            _ammoWidget = new AmmoCounterWidget();
        }

        private void InitializeRenderSurface()
        {
            RenderRoot.Children.Clear();

            _dxOverlay = new Dx9OverlayControl
            {
                Dock = WinForms.DockStyle.Fill
            };

            ApplyDxFontConfig();
            _dxOverlay.RenderFrame = RenderSurface;
            _dxOverlay.DeviceInitFailed += Overlay_DeviceInitFailed;
            _dxOverlay.MouseDown += GlControl_MouseDown;
            _dxOverlay.MouseMove += GlControl_MouseMove;
            _dxOverlay.MouseUp += GlControl_MouseUp;
            _dxOverlay.DoubleClick += GlControl_DoubleClick;
            _dxOverlay.KeyDown += GlControl_KeyDown;

            _dxHost = new WindowsFormsHost
            {
                Child = _dxOverlay
            };

            RenderRoot.Children.Add(_dxHost);
        }

        private void HighFrequencyRenderCallback(object state)
        {
            try
            {
                if (_isClosing || _dxOverlay == null)
                    return;

                int maxFPS = App.Config.UI.EspMaxFPS;
                long currentTicks = System.Diagnostics.Stopwatch.GetTimestamp();

                // FPS limiting: Skip frame if not enough time has elapsed
                if (maxFPS > 0)
                {
                    double elapsedMs = (currentTicks - _lastFrameTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    double targetMs = 1000.0 / maxFPS;
                    if (elapsedMs < targetMs)
                        return; // Skip this frame to maintain FPS cap
                }

                _lastFrameTicks = currentTicks;

                // Render DirectX on dedicated timer thread (DirectX 9 is thread-safe)
                // This removes WPF Dispatcher bottleneck - ESP no longer competes with Radar for UI thread
                if (System.Threading.Interlocked.CompareExchange(ref _renderPending, 1, 0) == 0)
                {
                    try
                    {
                        _dxOverlay.Render(); // DirectX render happens on timer thread
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _renderPending, 0);
                    }
                }
            }
            catch { /* Ignore errors during shutdown */ }
        }

        #region Rendering Methods

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = System.Threading.Interlocked.Exchange(ref _fpsCounter, 0);
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        private bool _lastInRaidState = false;

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void RenderSurface(Dx9RenderContext ctx)
        {
            if (_dxInitFailed)
                return;

            float screenWidth = ctx.Width;
            float screenHeight = ctx.Height;

            SetFPS();

            // Clear with black background (transparent for fuser)
            ctx.Clear(new DxColor(0, 0, 0, 255));

                // Detect raid state changes and reset camera/state when leaving raid
                if (_lastInRaidState && !InRaid)
                {
                    CameraManager.Reset();
                    DebugLogger.LogInfo("ESP: Detected raid end - reset all state");
                }
                _lastInRaidState = InRaid;

                if (!InRaid)
                    return;

                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;
                
                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(ctx, screenWidth, screenHeight);
                    }
                    else
                    {
                        ApplyResolutionOverrideIfNeeded();

                        // Render Loot (background layer)
                        if (App.Config.Loot.Enabled && App.Config.UI.EspLoot)
                        {
                            DrawLoot(ctx, screenWidth, screenHeight, localPlayer);
                        }

                        if (App.Config.Loot.Enabled && App.Config.UI.EspContainers)
                        {
                            DrawStaticContainers(ctx, screenWidth, screenHeight, localPlayer);
                        }

                        // Render Exfils
                        if (Exits is not null && App.Config.UI.EspExfils)
                        {
                            foreach (var exit in Exits)
                            {
                                if (exit is Exfil exfil && (exfil.Status == Exfil.EStatus.Open || exfil.Status == Exfil.EStatus.Pending))
                                {
                                     if (WorldToScreen2WithScale(exfil.Position, out var screen, out var scale, screenWidth, screenHeight))
                                     {
                                         var dotColor = exfil.Status == Exfil.EStatus.Pending
                                             ? ToColor(SKPaints.PaintExfilPending)
                                             : ToColor(SKPaints.PaintExfilOpen);
                                         var textColor = GetExfilColorForRender();

                                         // Apply distance-based scaling
                                         float markerSize = 4f * scale;
                                         float textOffsetX = 6f * scale;
                                         float textOffsetY = 4f * scale;
                                         ctx.DrawCircle(ToRaw(screen), markerSize, dotColor, true);
                                         ctx.DrawText(exfil.Name, screen.X + textOffsetX, screen.Y + textOffsetY, textColor, DxTextSize.Medium);
                                     }
                                }
                            }
                        }

                        if (Explosives is not null && App.Config.UI.EspTripwires)
                        {
                            DrawTripwires(ctx, screenWidth, screenHeight);
                        }

                        if (Explosives is not null && App.Config.UI.EspGrenades)
                        {
                            DrawGrenades(ctx, screenWidth, screenHeight);
                        }

                        // Render quest locations
                        if (App.Config.UI.EspQuestLocations && App.Config.QuestHelper.Enabled)
                        {
                            DrawQuestLocations(ctx, screenWidth, screenHeight);
                        }

                        // Render players
                        foreach (var player in allPlayers)
                        {
                            DrawPlayerESP(ctx, player, localPlayer, screenWidth, screenHeight);
                        }

                        DrawDeviceAimbotTargetLine(ctx, screenWidth, screenHeight);

                        if (App.Config.Device.Enabled)
                        {
                            DrawDeviceAimbotFovCircle(ctx, screenWidth, screenHeight);
                        }

                        if (App.Config.UI.EspCrosshair)
                        {
                            DrawCrosshair(ctx, screenWidth, screenHeight);
                        }

                        DrawDeviceAimbotDebugOverlay(ctx, screenWidth, screenHeight);

                        if (App.Config.UI.MiniRadar.Enabled)
                        {
                            DrawMiniRadar(ctx, localPlayer, allPlayers, screenWidth, screenHeight);
                        }

                        // Draw Ammo Counter Widget (always show when enabled, use placeholder if no valid data)
                        if (App.Config.UI.AmmoCounter.Enabled && _ammoWidget != null)
                        {
                            // FirearmManager is updated in the T1 realtime worker thread

                            // Update hover state for transparent-until-hover behavior
                            _ammoWidget.UpdateHoverState(_mouseX, _mouseY, screenWidth, screenHeight);

                            var snapshot = localPlayer?.FirearmManager?.CurrentSnapshot;
                            int currentAmmo = snapshot?.HasValidAmmo == true ? snapshot.CurrentAmmo : -1;
                            int maxAmmo = snapshot?.HasValidAmmo == true ? snapshot.MaxAmmo : -1;
                            string ammoTypeName = snapshot?.HasValidAmmo == true ? snapshot.AmmoTypeName : null;
                            _ammoWidget.Draw(ctx, currentAmmo, maxAmmo, ammoTypeName);
                        }

                        DrawFPS(ctx, screenWidth, screenHeight);
                    }
                }

        }

        private void DrawLoot(Dx9RenderContext ctx, float screenWidth, float screenHeight, LocalPlayer localPlayer)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            var camPos = localPlayer?.Position ?? Vector3.Zero;

            foreach (var item in lootItems)
            {
                // Filter based on ESP settings
                bool isCorpse = item is LootCorpse;
                bool isQuest = item.IsQuestItem;
                bool isHideout = item.IsHideoutItem;
                bool isImportant = item.Important; // User's custom filter list

                if (isQuest && !App.Config.UI.EspQuestLoot)
                    continue;
                if (isCorpse && !App.Config.UI.EspCorpses)
                    continue;

                bool isContainer = item is StaticLootContainer;
                if (isContainer)
                    continue;

                bool isFood = item.IsFood;
                bool isMeds = item.IsMeds;
                bool isBackpack = item.IsBackpack;

                // Skip category types if disabled, BUT Important/Hideout items always show
                if (!isImportant && !isHideout)
                {
                    if (isFood && !App.Config.UI.EspFood)
                        continue;
                    if (isMeds && !App.Config.UI.EspMeds)
                        continue;
                    if (isBackpack && !App.Config.UI.EspBackpacks)
                        continue;
                }

                // Check distance to loot
                float distance = Vector3.Distance(camPos, item.Position);
                if (App.Config.UI.EspLootMaxDistance > 0 && distance > App.Config.UI.EspLootMaxDistance)
                    continue;

                if (WorldToScreen2WithScale(item.Position, out var screen, out var scale, screenWidth, screenHeight))
                {
                     // Calculate cone filter based on screen position
                     bool coneEnabled = App.Config.UI.EspLootConeEnabled && App.Config.UI.EspLootConeAngle > 0f;
                     bool inCone = true;

                     if (coneEnabled)
                     {
                         // Calculate angle from screen center
                         float centerX = screenWidth / 2f;
                         float centerY = screenHeight / 2f;
                         float dx = screen.X - centerX;
                         float dy = screen.Y - centerY;

                         // Calculate angular distance from center (in screen space)
                         // Using FOV to convert screen distance to angle
                         float fov = App.Config.UI.FOV;
                         float screenAngleX = MathF.Abs(dx / centerX) * (fov / 2f);
                         float screenAngleY = MathF.Abs(dy / centerY) * (fov / 2f);
                         float screenAngle = MathF.Sqrt(screenAngleX * screenAngleX + screenAngleY * screenAngleY);

                         inCone = screenAngle <= App.Config.UI.EspLootConeAngle;
                     }

                     // Determine colors based on item type (matching radar GetPaints order)
                     DxColor circleColor;

                     if (isQuest)
                     {
                         circleColor = ToColor(SKPaints.PaintQuestItem);
                     }
                     else if (isHideout)
                     {
                         circleColor = ToColor(SKPaints.PaintHideoutItem);
                     }
                     else if (isBackpack)
                     {
                         circleColor = ToColor(SKPaints.PaintBackpacks);
                     }
                     else if (isMeds)
                     {
                         circleColor = ToColor(SKPaints.PaintMeds);
                     }
                     else if (isFood)
                     {
                         circleColor = ToColor(SKPaints.PaintFood);
                     }
                     else if (!string.IsNullOrEmpty(item.CustomFilter?.Color))
                     {
                         // Custom filter color from loot filter UI
                         circleColor = ToColor(ColorFromHex(item.CustomFilter.Color));
                     }
                     else if (item.IsValuableLoot || item is LootAirdrop)
                     {
                         circleColor = ToColor(SKPaints.PaintImportantLoot);
                     }
                     else if (isCorpse)
                     {
                         circleColor = ToColor(SKPaints.PaintCorpse);
                     }
                     else
                     {
                         // Default loot color (WhiteSmoke) - matches radar
                         circleColor = ToColor(SKPaints.PaintLoot);
                     }

                     DxColor textColor = circleColor;

                     // Apply distance-based scaling to marker size
                     float markerSize = 2f * scale;
                     ctx.DrawCircle(ToRaw(screen), markerSize, circleColor, true);

                     if (item.Important || inCone)
                     {
                         string text;
                         if (isCorpse && item is LootCorpse corpse)
                         {
                             var corpseName = corpse.Player?.Name;
                             text = string.IsNullOrWhiteSpace(corpseName) ? corpse.Name : corpseName;
                         }
                         else if (item is LootAirdrop)
                         {
                             text = "Airdrop";
                         }
                         else
                         {
                             var shortName = string.IsNullOrWhiteSpace(item.ShortName) ? item.Name : item.ShortName;
                             text = shortName;
                             if (App.Config.UI.EspLootPrice)
                             {
                                 text = item.Important
                                     ? shortName
                                     : $"{shortName} ({LoneEftDmaRadar.Misc.Utilities.FormatNumberKM(item.Price)})";
                             }
                         }
                         float textOffset = 4f * scale;
                         ctx.DrawText(text, screen.X + textOffset, screen.Y + textOffset, textColor, DxTextSize.Small);
                    }
                }
            }
        }

        private void DrawStaticContainers(Dx9RenderContext ctx, float screenWidth, float screenHeight, LocalPlayer localPlayer)
        {
            if (!App.Config.Containers.Enabled)
                return;

            var containers = Memory.Game?.Loot?.StaticContainers;
            if (containers is null)
                return;

            bool selectAll = App.Config.Containers.SelectAll;
            var selected = App.Config.Containers.Selected;
            bool hideSearched = App.Config.Containers.HideSearched;
            float maxDistance = App.Config.Containers.EspDrawDistance;
            int minValue = App.Config.Containers.MinValue;
            var defaultColor = GetContainerColorForRender();

            foreach (var container in containers)
            {
                var id = container.ID ?? "UNKNOWN";
                if (!(selectAll || selected.ContainsKey(id)))
                    continue;

                if (hideSearched && container.Searched)
                    continue;

                // Filter by min value (important and hideout items always pass)
                if (minValue > 0 && !container.HasImportantContents && !container.HasHideoutContents && container.TotalValue < minValue)
                    continue;

                float distance = Vector3.Distance(localPlayer.Position, container.Position);
                if (maxDistance > 0 && distance > maxDistance)
                    continue;

                if (!WorldToScreen2WithScale(container.Position, out var screen, out var scale, screenWidth, screenHeight))
                    continue;

                // Calculate cone filter based on screen position (same as loose loot)
                bool coneEnabled = App.Config.UI.EspLootConeEnabled && App.Config.UI.EspLootConeAngle > 0f;
                bool inCone = true;

                if (coneEnabled)
                {
                    float centerX = screenWidth / 2f;
                    float centerY = screenHeight / 2f;
                    float dx = screen.X - centerX;
                    float dy = screen.Y - centerY;

                    float fov = App.Config.UI.FOV;
                    float screenAngleX = MathF.Abs(dx / centerX) * (fov / 2f);
                    float screenAngleY = MathF.Abs(dy / centerY) * (fov / 2f);
                    float screenAngle = MathF.Sqrt(screenAngleX * screenAngleX + screenAngleY * screenAngleY);

                    inCone = screenAngle <= App.Config.UI.EspLootConeAngle;
                }

                // Determine color based on container contents
                // Priority: Important (with filter color) > Hideout > Valuable > HasValuable > Default
                DxColor color = defaultColor;
                if (container.HasImportantContents)
                {
                    var filterColor = container.ImportantItemFilterColor;
                    if (!string.IsNullOrEmpty(filterColor))
                    {
                        var filterPaints = LootItem.GetFilterPaints(filterColor);
                        color = ToColor(filterPaints.Item1);
                    }
                    else
                    {
                        color = ToColor(SKPaints.PaintFilteredLoot);
                    }
                }
                else if (container.HasHideoutContents)
                    color = ToColor(SKPaints.PaintHideoutItem);
                else if (container.IsValuableContainer)
                    color = ToColor(SKPaints.PaintImportantLoot);
                else if (container.HasValuableContents)
                    color = ToColor(SKPaints.PaintLoot);

                // Apply distance-based scaling
                float markerSize = 3f * scale;
                ctx.DrawCircle(ToRaw(screen), markerSize, color, true);

                // Only show text if important/hideout contents OR within cone (same behavior as loose loot)
                if (container.HasImportantContents || container.HasHideoutContents || inCone)
                {
                    // Build label with value if available
                    string text = container.Name ?? "Container";
                    if (App.Config.UI.EspLootPrice && container.TotalValue > 0)
                        text = $"{text} ({LoneEftDmaRadar.Misc.Utilities.FormatNumberKM(container.TotalValue)})";

                    float textOffset = 4f * scale;
                    ctx.DrawText(text, screen.X + textOffset, screen.Y + textOffset, color, DxTextSize.Small);
                }
            }
        }

        private void DrawTripwires(Dx9RenderContext ctx, float screenWidth, float screenHeight)
        {
            if (Explosives is null)
                return;

            foreach (var explosive in Explosives)
            {
                if (explosive is null || explosive is not Tripwire tripwire || !tripwire.IsActive)
                    continue;

                try
                {
                    if (tripwire.Position == Vector3.Zero)
                        continue;

                    if (!WorldToScreen2WithScale(tripwire.Position, out var screen, out var scale, screenWidth, screenHeight))
                        continue;

                    var color = GetTripwireColorForRender();
                    float markerSize = 5f * scale;
                    float textOffset = 6f * scale;
                    ctx.DrawCircle(ToRaw(screen), markerSize, color, true);
                    ctx.DrawText("Tripwire", screen.X + textOffset, screen.Y, color, DxTextSize.Small);
                }
                catch
                {
                    // Silently skip invalid tripwires to prevent ESP from breaking
                    continue;
                }
            }
        }

        private void DrawGrenades(Dx9RenderContext ctx, float screenWidth, float screenHeight)
        {
            if (Explosives is null)
                return;

            var grenadeColor = GetGrenadeColorForRender();

            foreach (var explosive in Explosives)
            {
                if (explosive is null || explosive is not Grenade grenade)
                    continue;

                try
                {
                    if (grenade.Position == Vector3.Zero)
                        continue;

                    if (!WorldToScreen2WithScale(grenade.Position, out var screen, out var scale, screenWidth, screenHeight))
                        continue;

                    // Draw blast radius circle
                    if (App.Config.UI.EspGrenadeBlastRadius)
                    {
                        float blastRadiusWorld = 5f; // Hardcoded for now
                        const int segments = 32;
                        var blastColor = new DxColor(grenadeColor.R, grenadeColor.G, grenadeColor.B, 255);

                        var circlePoints = new List<SKPoint>();
                        for (int i = 0; i <= segments; i++)
                        {
                            float angle = (i / (float)segments) * MathF.PI * 2;
                            var offset = new Vector3(
                                MathF.Cos(angle) * blastRadiusWorld,
                                0,
                                MathF.Sin(angle) * blastRadiusWorld);
                            var worldPos = grenade.Position + offset;

                            if (WorldToScreen2WithScale(worldPos, out var screenPos, out _, screenWidth, screenHeight))
                            {
                                circlePoints.Add(screenPos);
                            }
                        }

                        // Draw line segments between points
                        for (int i = 0; i < circlePoints.Count - 1; i++)
                        {
                            ctx.DrawLine(ToRaw(circlePoints[i]), ToRaw(circlePoints[i + 1]), blastColor, 2f);
                        }
                    }

                    // Draw trail
                    if (App.Config.UI.EspGrenadeTrail && grenade.PositionHistory.Count > 1)
                    {
                        var trailColor = new DxColor(grenadeColor.R, grenadeColor.G, grenadeColor.B, 255);

                        var screenPoints = new List<SKPoint>();
                        foreach (var pos in grenade.PositionHistory)
                        {
                            if (pos == Vector3.Zero)
                                continue;
                            if (WorldToScreen2WithScale(pos, out var posScreen, out _, screenWidth, screenHeight))
                            {
                                screenPoints.Add(posScreen);
                            }
                        }

                        // Draw trail segments with increasing thickness
                        for (int i = 0; i < screenPoints.Count - 1; i++)
                        {
                            float progress = (float)i / (screenPoints.Count - 1);
                            float thickness = 0.5f + (progress * 3.5f); // 0.5f to 4f

                            ctx.DrawLine(ToRaw(screenPoints[i]), ToRaw(screenPoints[i + 1]), trailColor, thickness);
                        }
                    }

                    float markerSize = 5f * scale;
                    float textOffset = 6f * scale;
                    ctx.DrawCircle(ToRaw(screen), markerSize, grenadeColor, true);
                    ctx.DrawText("Grenade", screen.X + textOffset, screen.Y, grenadeColor, DxTextSize.Small);
                }
                catch
                {
                    // Silently skip invalid grenades to prevent ESP from breaking
                    continue;
                }
            }
        }

        private void DrawQuestLocations(Dx9RenderContext ctx, float screenWidth, float screenHeight)
        {
            var locations = Memory.Quests?.LocationConditions;
            if (locations is null || locations.Count == 0)
                return;

            foreach (var kvp in locations)
            {
                var location = kvp.Value;
                if (location is null)
                    continue;

                try
                {
                    if (location.Position == Vector3.Zero)
                        continue;

                    if (!WorldToScreen2WithScale(location.Position, out var screen, out var scale, screenWidth, screenHeight))
                        continue;

                    var color = ToColor(SKPaints.PaintQuestZone);
                    float markerSize = 6f * scale;
                    float textOffset = 8f * scale;

                    // Draw a square marker to differentiate from other markers
                    float halfSize = markerSize;
                    ctx.DrawFilledRect(
                        new RectangleF(screen.X - halfSize, screen.Y - halfSize, halfSize * 2, halfSize * 2),
                        color);

                    ctx.DrawText("Quest Zone", screen.X + textOffset, screen.Y, color, DxTextSize.Small);
                }
                catch
                {
                    // Silently skip invalid locations to prevent ESP from breaking
                    continue;
                }
            }
        }

        /// <summary>
        /// Renders player on ESP
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerESP(Dx9RenderContext ctx, AbstractPlayer player, LocalPlayer localPlayer, float screenWidth, float screenHeight)
        {
            if (player is null || player == localPlayer || !player.IsAlive || !player.IsActive)
                return;

            // Validate player position is valid (not zero or NaN/Infinity)
            var playerPos = player.Position;
            if (playerPos == Vector3.Zero || 
                float.IsNaN(playerPos.X) || float.IsNaN(playerPos.Y) || float.IsNaN(playerPos.Z) ||
                float.IsInfinity(playerPos.X) || float.IsInfinity(playerPos.Y) || float.IsInfinity(playerPos.Z))
                return;

            // Check if this is AI or player
            bool isAI = player.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss or PlayerType.PScav;

            // Optimization: Skip players/AI that are too far before W2S
            float distance = Vector3.Distance(localPlayer.Position, player.Position);
            float maxDistance = isAI ? App.Config.UI.EspAIMaxDistance : App.Config.UI.EspPlayerMaxDistance;

            // If maxDistance is 0, it means unlimited, otherwise check distance
            if (maxDistance > 0 && distance > maxDistance)
                return;

            // Fallback to old MaxDistance if the new settings aren't configured
            if (maxDistance == 0 && distance > App.Config.UI.MaxDistance)
                return;

            // Get Color
            var color = GetPlayerColorForRender(player);
            bool isDeviceAimbotLocked = MemDMA.DeviceAimbot?.LockedTarget == player;
            if (isDeviceAimbotLocked)
            {
                color = ToColor(new SKColor(0, 200, 255, 220));
            }

            bool drawSkeleton = isAI ? App.Config.UI.EspAISkeletons : App.Config.UI.EspPlayerSkeletons;
            var boxStyle = isAI ? App.Config.UI.EspAIBoxStyle : App.Config.UI.EspPlayerBoxStyle;
            bool drawBox = boxStyle != EspBoxStyle.None;
            bool drawName = isAI ? App.Config.UI.EspAINames : App.Config.UI.EspPlayerNames;
            bool drawHealth = isAI ? App.Config.UI.EspAIHealth : App.Config.UI.EspPlayerHealth;
            bool drawDistance = isAI ? App.Config.UI.EspAIDistance : App.Config.UI.EspPlayerDistance;
            bool drawGroupId = isAI ? App.Config.UI.EspAIGroupIds : App.Config.UI.EspGroupIds;
            bool drawLabel = drawName || drawDistance || drawHealth || drawGroupId;

            // Draw Skeleton (only if not in error state to avoid frozen bones)
            if (drawSkeleton && !player.IsError)
            {
                DrawSkeleton(ctx, player, screenWidth, screenHeight, color, _skeletonPaint.StrokeWidth);
            }

            RectangleF bbox = default;
            bool hasBox = false;
            if (drawBox || drawLabel)
            {
                hasBox = TryGetBoundingBox(player, screenWidth, screenHeight, out bbox);
            }

            // Draw Box based on selected style
            if (drawBox && hasBox)
            {
                switch (boxStyle)
                {
                    case EspBoxStyle.Box2D:
                        DrawBoundingBox(ctx, bbox, color, _boxPaint.StrokeWidth);
                        break;
                    case EspBoxStyle.Corner2D:
                        Draw2DCorners(ctx, bbox, color, _boxPaint.StrokeWidth);
                        break;
                    case EspBoxStyle.Corner3D:
                        if (TryGet3DCorners(player, screenWidth, screenHeight, out var corners3dc))
                            Draw3DCorners(ctx, corners3dc, color, _boxPaint.StrokeWidth);
                        break;
                    case EspBoxStyle.Box3D:
                        if (TryGet3DCorners(player, screenWidth, screenHeight, out var corners3db))
                            Draw3DBox(ctx, corners3db, color, _boxPaint.StrokeWidth);
                        break;
                }
            }

            // Draw head marker
            bool drawHeadCircle = isAI ? App.Config.UI.EspHeadCircleAI : App.Config.UI.EspHeadCirclePlayers;
            if (drawHeadCircle)
            {
                var head = player.GetBonePos(Bones.HumanHead);
                var headTop = head;
                headTop.Y += 0.18f; // small offset to estimate head height

                if (TryProject(head, screenWidth, screenHeight, out var headScreen) &&
                    TryProject(headTop, screenWidth, screenHeight, out var headTopScreen))
                {
                    float radius;
                    if (hasBox)
                    {
                        // scale with on-screen box to stay proportional to the model
                        radius = MathF.Min(bbox.Width, bbox.Height) * 0.1f;
                    }
                    else
                    {
                        // fallback: use projected head height
                        var dy = MathF.Abs(headTopScreen.Y - headScreen.Y);
                        radius = dy * 0.65f;
                    }

                    radius = Math.Clamp(radius, 2f, 12f);
                    ctx.DrawCircle(ToRaw(headScreen), radius, color, filled: false);
                }
            }

            if (drawLabel)
            {
                DrawPlayerLabel(ctx, player, distance, color, hasBox ? bbox : (RectangleF?)null, screenWidth, screenHeight, drawName, drawDistance, drawHealth, drawGroupId);
            }
        }

        private void DrawMiniRadar(Dx9RenderContext ctx, LocalPlayer localPlayer, IEnumerable<AbstractPlayer> allPlayers, float screenWidth, float screenHeight)
        {
             try
             {
                 var cfg = App.Config.UI.MiniRadar;

                 if (!cfg.Enabled) return;

                 // Ensure Map Manager is synced with Game Memory
                 var gameMapId = Memory.Game?.MapID;
                 if (!string.IsNullOrEmpty(gameMapId) &&
                     !string.Equals(gameMapId, EftMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                 {
                     DebugLogger.LogInfo($"[MiniRadar] Map Mismatch detected! Game: '{gameMapId}' vs Manager: '{EftMapManager.Map?.ID}'. Loading correct map...");
                     EftMapManager.LoadMap(gameMapId);
                 }

                 var map = EftMapManager.Map;

                 if (map is null) return;

                 // Check if map changed or size changed
                 if (_lastMapId != map.ID || _lastMiniRadarSize != cfg.Size)
                 {
                     UpdateMiniRadarTexture(map, cfg.Size);
                 }

                 if (!_miniRadarParams.IsValid)
                     return;

                 // Define screen position
                 // If ScreenX is < 0, use auto-position (Top Right)
                 float radarScreenX = cfg.ScreenX < 0 
                     ? screenWidth - cfg.Size - 20f 
                     : cfg.ScreenX;
                 
                 float radarScreenY = cfg.ScreenY;
                 
                 // Update internal screen params (Size comes from config now)
                 _miniRadarParams.DrawSize = cfg.Size;
                 _miniRadarParams.ScreenX = radarScreenX;
                 _miniRadarParams.ScreenY = radarScreenY;

                 // Draw Background Texture
                 // Draw Map Texture (No background, no border)
                 var destRect = new RectangleF(radarScreenX, radarScreenY, cfg.Size, cfg.Size);
                 ctx.DrawMapTexture(destRect, 1.0f);

                 // Draw Exits (if enabled)
                 if (Exits is not null && cfg.ShowExits && App.Config.UI.EspExfils)
                 {
                     DrawMiniRadarExits(ctx, map);
                 }

                // Draw Loot (if enabled)
                if (App.Config.Loot.Enabled && cfg.ShowLoot)
                {
                    DrawMiniRadarLoot(ctx, map);
                }
                
                // Draw Containers (if enabled)
                if (App.Config.Loot.Enabled && cfg.ShowContainers)
                {
                    DrawMiniRadarContainers(ctx, map);
                }

                // Draw Explosives (if enabled)
                if (Explosives is not null && cfg.ShowGrenades && (App.Config.UI.EspTripwires || App.Config.UI.EspGrenades))
                {
                    DrawMiniRadarExplosives(ctx, map);
                }

                 // Draw Local Player
                 DrawMiniRadarDot(ctx, localPlayer.Position, localPlayer.Rotation, map, SKColors.Cyan, 4f, true);

                 // Draw Other Players
                 if (allPlayers != null && cfg.ShowPlayers)
                 {
                     foreach (var player in allPlayers)
                     {
                         if (player == localPlayer || !player.IsAlive || !player.IsActive) continue;
                         
                         var color = GetPlayerColor(player).Color;
                         
                         DrawMiniRadarDot(ctx, player.Position, player.Rotation, map, color, 3f, true);
                     }
                 }
                 
                 // Draw Border Outline
                 ctx.DrawRect(destRect, new DxColor(100, 100, 100, 255), 1.0f);
             }
             catch
             {
                 // Ignore drawing errors
             }
        }

        private void DrawMiniRadarExplosives(Dx9RenderContext ctx, IEftMap map)
        {
            if (Explosives is null) return;

            bool showTrip = App.Config.UI.EspTripwires;
            bool showNade = App.Config.UI.EspGrenades;
            var tripColor = ColorFromHex(App.Config.UI.EspColorTripwire);
            var nadeColor = ColorFromHex(App.Config.UI.EspColorGrenade);

            foreach (var explosive in Explosives)
            {
                if (explosive is null || explosive.Position == Vector3.Zero) continue;
                
                if (explosive is Tripwire trip && trip.IsActive && showTrip)
                {
                    DrawMiniRadarDot(ctx, trip.Position, map, tripColor, 2f);
                }
                else if (explosive is Grenade && showNade)
                {
                    DrawMiniRadarDot(ctx, explosive.Position, map, nadeColor, 2f);
                }
            }
        }

        private void DrawMiniRadarContainers(Dx9RenderContext ctx, IEftMap map)
        {
             if (!App.Config.Containers.Enabled) return;
             var containers = Memory.Game?.Loot?.StaticContainers;
             if (containers is null) return;

             bool selectAll = App.Config.Containers.SelectAll;
             var selected = App.Config.Containers.Selected;
             bool hideSearched = App.Config.Containers.HideSearched;
             int minValue = App.Config.Containers.MinValue;
             var defaultColor = ColorFromHex(App.Config.UI.EspColorContainers);

             foreach (var c in containers)
             {
                  if (c.Position == Vector3.Zero) continue;
                  var id = c.ID ?? "UNKNOWN";
                  if (!(selectAll || selected.ContainsKey(id))) continue;
                  if (hideSearched && c.Searched) continue;

                  // Filter by min value (important and hideout items always pass)
                  if (minValue > 0 && !c.HasImportantContents && !c.HasHideoutContents && c.TotalValue < minValue)
                      continue;

                  // Use value-based color - Priority: Important (with filter color) > Hideout > Valuable > HasValuable > Default
                  SKColor color;
                  if (c.HasImportantContents)
                  {
                      var filterColor = c.ImportantItemFilterColor;
                      if (!string.IsNullOrEmpty(filterColor) && SKColor.TryParse(filterColor, out var parsedColor))
                          color = parsedColor;
                      else
                          color = SKColors.Turquoise;
                  }
                  else if (c.HasHideoutContents)
                      color = SKPaints.PaintHideoutItem.Color;
                  else if (c.IsValuableContainer)
                      color = SKColors.Gold;
                  else if (c.HasValuableContents)
                      color = SKColors.White;
                  else
                      color = defaultColor;

                  DrawMiniRadarDot(ctx, c.Position, map, color, 1.5f);
             }
        }

        private void DrawMiniRadarLoot(Dx9RenderContext ctx, IEftMap map)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            foreach (var item in lootItems)
            {
                // Basic filtering consistent with DrawLoot
                 bool isCorpse = item is LootCorpse;
                 bool isQuest = item.IsQuestItem;
                 bool isHideout = item.IsHideoutItem;
                 bool isBackpack = item.IsBackpack;
                 bool isMeds = item.IsMeds;
                 bool isFood = item.IsFood;

                 if (isQuest && !App.Config.UI.EspQuestLoot) continue;
                 if (isCorpse && !App.Config.UI.EspCorpses) continue;
                 if (isBackpack && !App.Config.UI.EspBackpacks) continue;
                 if (isMeds && !App.Config.UI.EspMeds) continue;
                 if (isFood && !App.Config.UI.EspFood) continue;

                 // Match radar GetPaints color order
                 SKColor color;
                 if (isQuest)
                     color = SKPaints.PaintQuestItem.Color;
                 else if (isHideout)
                     color = SKPaints.PaintHideoutItem.Color;
                 else if (isBackpack)
                     color = SKPaints.PaintBackpacks.Color;
                 else if (isMeds)
                     color = SKPaints.PaintMeds.Color;
                 else if (isFood)
                     color = SKPaints.PaintFood.Color;
                 else if (!string.IsNullOrEmpty(item.CustomFilter?.Color) && SKColor.TryParse(item.CustomFilter.Color, out var customColor))
                     color = customColor;
                 else if (item.IsValuableLoot || item is LootAirdrop)
                     color = SKPaints.PaintImportantLoot.Color;
                 else if (isCorpse)
                     color = SKPaints.PaintCorpse.Color;
                 else
                     color = SKPaints.PaintLoot.Color;

                 DrawMiniRadarDot(ctx, item.Position, map, color, 1.5f);
            }
        }

        private void DrawMiniRadarExits(Dx9RenderContext ctx, IEftMap map)
        {
            if (Exits is null) return;

            foreach (var exit in Exits)
            {
                if (exit is Exfil exfil && (exfil.Status == Exfil.EStatus.Open || exfil.Status == Exfil.EStatus.Pending))
                {
                    var color = exfil.Status == Exfil.EStatus.Pending ? SKColors.Yellow : SKColors.LimeGreen;
                    DrawMiniRadarDot(ctx, exfil.Position, map, color, 2.5f);
                }
            }
        }

        private void DrawMiniRadarDot(Dx9RenderContext ctx, Vector3 worldPos, IEftMap map, SKColor color, float size)
        {
            var unused = Vector2.Zero; // Dummy rotation
            DrawMiniRadarDot(ctx, worldPos, unused, map, color, size, false);
        }

        private void DrawMiniRadarDot(Dx9RenderContext ctx, Vector3 worldPos, Vector2 rotation, IEftMap map, SKColor color, float size, bool drawLookDir)
        {
             // Transform
             var mapPos = worldPos.ToMapPos(map.Config);
             
             // Use pre-calculated params from UpdateMiniRadarTexture (already scaled to size)
             float miniX = mapPos.X * _miniRadarParams.Scale + _miniRadarParams.OffsetX;
             float miniY = mapPos.Y * _miniRadarParams.Scale + _miniRadarParams.OffsetY;

             // Screen relative
             float screenX = _miniRadarParams.ScreenX + miniX;
             float screenY = _miniRadarParams.ScreenY + miniY;

             // Clip to radar bounds (optional, but good)
             if (screenX < _miniRadarParams.ScreenX || screenX > _miniRadarParams.ScreenX + _miniRadarParams.DrawSize ||
                 screenY < _miniRadarParams.ScreenY || screenY > _miniRadarParams.ScreenY + _miniRadarParams.DrawSize)
                 return;

             if (drawLookDir)
             {
                 DrawMiniRadarLookDirection(ctx, screenX, screenY, rotation, color);
             }

             ctx.DrawCircle(new SharpDX.Mathematics.Interop.RawVector2(screenX, screenY), size, ToColor(color), true);
        }

        private void DrawMiniRadarLookDirection(Dx9RenderContext ctx, float screenX, float screenY, Vector2 rotation, SKColor color)
        {
             float rX = rotation.X; // Yaw
             float rad = (rX - 90) * (MathF.PI / 180f);
             float cos = MathF.Cos(rad);
             float sin = MathF.Sin(rad);
             
             float len = 10f; // Look length
             
             float endX = screenX + cos * len;
             float endY = screenY + sin * len;

             ctx.DrawLine(new RawVector2(screenX, screenY), new RawVector2(endX, endY), ToColor(color), 1f);
        }

        private int _lastMiniRadarSize = -1;
        private DateTime _lastMiniRadarErrorTime = DateTime.MinValue;

        private void UpdateMiniRadarTexture(IEftMap map, int size)
        {
             try 
             {
                 // Get Map Bounds
                 var bounds = map.GetBounds();
                 if (bounds.IsEmpty) 
                 {
                     if ((DateTime.Now - _lastMiniRadarErrorTime).TotalSeconds > 5)
                     {
                         DebugLogger.LogDebug($"[MiniRadar] Map bounds empty for '{map.ID}'. Retrying...");
                         _lastMiniRadarErrorTime = DateTime.Now;
                     }
                     return;
                 }

                 // We render to a higher resolution texture to ensure map lines are preserved during scaling
                 // Then we let DX9 overlay handle the downsampling to screen size
                 const int TEXTURE_SIZE = 512; // Moderate res for quality/perf balance
                 
                 float mapW = bounds.Width;
                 float mapH = bounds.Height;
                 
                 if (mapW <= 0 || mapH <= 0) return;

                 // The scale used for RENDERING the map to the texture
                 // We want to fit the map into TEXTURE_SIZE (512)
                 float renderScale = Math.Min((float)TEXTURE_SIZE / mapW, (float)TEXTURE_SIZE / mapH);

                 // Determine offsets to center the map in the TEXTURE
                 float renderOffsetX = (TEXTURE_SIZE - (mapW * renderScale)) / 2f;
                 float renderOffsetY = (TEXTURE_SIZE - (mapH * renderScale)) / 2f;

                 float screenScale = renderScale * ((float)size / TEXTURE_SIZE);
                 float screenOffsetX = renderOffsetX * ((float)size / TEXTURE_SIZE);
                 float screenOffsetY = renderOffsetY * ((float)size / TEXTURE_SIZE);
                 
                 // Render to Bitmap
                 using var bitmap = new SKBitmap(TEXTURE_SIZE, TEXTURE_SIZE, SKColorType.Bgra8888, SKAlphaType.Premul);
                 using var canvas = new SKCanvas(bitmap);
                 
                 // Use Transparent background so the underlying FilledRect color shows through
                 canvas.Clear(SKColors.Transparent);

                 // Render map into the 512x512 canvas
                 try
                 {
                     map.RenderThumbnail(canvas, TEXTURE_SIZE, TEXTURE_SIZE);
                 }
                 catch (Exception ex)
                 {
                     DebugLogger.LogError($"[ESPWindow] Failed to render mini-radar thumbnail: {ex.Message}");
                     return;
                 }
                 
                 // Note: Debug Red X removed to clean up view.

                 var bytes = bitmap.Bytes; 
                 // Request texture update
                 _dxOverlay.RequestMapTextureUpdate(TEXTURE_SIZE, TEXTURE_SIZE, bytes);
                 
                 // Set ID/Params only after success to ensure we retry on failure
                 // This ensures that dots are only drawn if the map is valid and loaded
                 _miniRadarParams = new MiniRadarParams
                 {
                     Scale = screenScale,
                     OffsetX = screenOffsetX,
                     OffsetY = screenOffsetY,
                     DrawSize = size,
                     IsValid = true
                 };
                 
                 _lastMapId = map.ID;
                 _lastMiniRadarSize = size;
                 DebugLogger.LogInfo($"[MiniRadar] Texture updated for '{map.ID}' @ {size}px (Scale: {screenScale:F3})");
             }
             catch (Exception ex)
             {
                 if ((DateTime.Now - _lastMiniRadarErrorTime).TotalSeconds > 5)
                 {
                     DebugLogger.LogDebug($"[MiniRadar] Update failed: {ex.Message}");
                     _lastMiniRadarErrorTime = DateTime.Now;
                 }
             }
        }

        private void DrawSkeleton(Dx9RenderContext ctx, AbstractPlayer player, float w, float h, DxColor color, float thickness)
        {
            foreach (var (from, to) in _boneConnections)
            {
                var p1 = player.GetBonePos(from);
                var p2 = player.GetBonePos(to);

                // Skip if either bone position is invalid (zero or NaN)
                if (p1 == Vector3.Zero || p2 == Vector3.Zero)
                    continue;

                if (TryProject(p1, w, h, out var s1) && TryProject(p2, w, h, out var s2))
                {
                    ctx.DrawLine(ToRaw(s1), ToRaw(s2), color, thickness);
                }
            }
        }

            private bool TryGetBoundingBox(AbstractPlayer player, float w, float h, out RectangleF rect)
        {
            rect = default;
            
            // Validate player position before calculating bounding box
            var playerPos = player.Position;
            if (playerPos == Vector3.Zero || 
                float.IsNaN(playerPos.X) || float.IsInfinity(playerPos.X))
                return false;
            
            var projectedPoints = new List<SKPoint>();
            var mins = new Vector3((float)-0.4, 0, (float)-0.4);
            var maxs = new Vector3((float)0.4, (float)1.75, (float)0.4);

            mins = playerPos + mins;
            maxs = playerPos + maxs;

            var points = new List<Vector3> {
                new Vector3(mins.X, mins.Y, mins.Z),
                new Vector3(mins.X, maxs.Y, mins.Z),
                new Vector3(maxs.X, maxs.Y, mins.Z),
                new Vector3(maxs.X, mins.Y, mins.Z),
                new Vector3(maxs.X, maxs.Y, maxs.Z),
                new Vector3(mins.X, maxs.Y, maxs.Z),
                new Vector3(mins.X, mins.Y, maxs.Z),
                new Vector3(maxs.X, mins.Y, maxs.Z)
            };

            foreach (var position in points)
            {
                if (TryProject(position, w, h, out var s))
                    projectedPoints.Add(s);
            }

            if (projectedPoints.Count < 2)
                return false;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var point in projectedPoints)
            {
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;

            if (boxWidth < 1f || boxHeight < 1f || boxWidth > w * 2f || boxHeight > h * 2f)
                return false;

            minX = Math.Clamp(minX, -50f, w + 50f);
            maxX = Math.Clamp(maxX, -50f, w + 50f);
            minY = Math.Clamp(minY, -50f, h + 50f);
            maxY = Math.Clamp(maxY, -50f, h + 50f);

            float padding = 2f;
            rect = new RectangleF(minX - padding, minY - padding, (maxX - minX) + padding * 2f, (maxY - minY) + padding * 2f);
            return true;
        }

        private void DrawBoundingBox(Dx9RenderContext ctx, RectangleF rect, DxColor color, float thickness)
        {
            ctx.DrawRect(rect, color, thickness);
        }

        private bool TryGet3DCorners(AbstractPlayer player, float w, float h, out SKPoint[] corners)
        {
            corners = new SKPoint[8];
            var playerPos = player.Position;
            if (playerPos == Vector3.Zero ||
                float.IsNaN(playerPos.X) || float.IsInfinity(playerPos.X))
                return false;

            var mins = new Vector3(-0.4f, 0, -0.4f);
            var maxs = new Vector3(0.4f, 1.75f, 0.4f);
            mins = playerPos + mins;
            maxs = playerPos + maxs;

            // 8 corners: bottom 4 (0-3), top 4 (4-7)
            var points3D = new Vector3[] {
                new(mins.X, mins.Y, mins.Z), // 0: bottom-front-left
                new(maxs.X, mins.Y, mins.Z), // 1: bottom-front-right
                new(maxs.X, mins.Y, maxs.Z), // 2: bottom-back-right
                new(mins.X, mins.Y, maxs.Z), // 3: bottom-back-left
                new(mins.X, maxs.Y, mins.Z), // 4: top-front-left
                new(maxs.X, maxs.Y, mins.Z), // 5: top-front-right
                new(maxs.X, maxs.Y, maxs.Z), // 6: top-back-right
                new(mins.X, maxs.Y, maxs.Z), // 7: top-back-left
            };

            int projected = 0;
            for (int i = 0; i < 8; i++)
            {
                if (TryProject(points3D[i], w, h, out var s))
                {
                    corners[i] = s;
                    projected++;
                }
            }
            return projected >= 4;
        }

        private void Draw2DCorners(Dx9RenderContext ctx, RectangleF rect, DxColor color, float thickness)
        {
            float cornerLen = Math.Min(rect.Width, rect.Height) * 0.25f;
            cornerLen = Math.Clamp(cornerLen, 4f, 20f);

            // Top-left
            ctx.DrawLine(new RawVector2(rect.Left, rect.Top), new RawVector2(rect.Left + cornerLen, rect.Top), color, thickness);
            ctx.DrawLine(new RawVector2(rect.Left, rect.Top), new RawVector2(rect.Left, rect.Top + cornerLen), color, thickness);

            // Top-right
            ctx.DrawLine(new RawVector2(rect.Right, rect.Top), new RawVector2(rect.Right - cornerLen, rect.Top), color, thickness);
            ctx.DrawLine(new RawVector2(rect.Right, rect.Top), new RawVector2(rect.Right, rect.Top + cornerLen), color, thickness);

            // Bottom-left
            ctx.DrawLine(new RawVector2(rect.Left, rect.Bottom), new RawVector2(rect.Left + cornerLen, rect.Bottom), color, thickness);
            ctx.DrawLine(new RawVector2(rect.Left, rect.Bottom), new RawVector2(rect.Left, rect.Bottom - cornerLen), color, thickness);

            // Bottom-right
            ctx.DrawLine(new RawVector2(rect.Right, rect.Bottom), new RawVector2(rect.Right - cornerLen, rect.Bottom), color, thickness);
            ctx.DrawLine(new RawVector2(rect.Right, rect.Bottom), new RawVector2(rect.Right, rect.Bottom - cornerLen), color, thickness);
        }

        private void Draw3DBox(Dx9RenderContext ctx, SKPoint[] corners, DxColor color, float thickness)
        {
            // Bottom face (4 edges)
            ctx.DrawLine(ToRaw(corners[0]), ToRaw(corners[1]), color, thickness);
            ctx.DrawLine(ToRaw(corners[1]), ToRaw(corners[2]), color, thickness);
            ctx.DrawLine(ToRaw(corners[2]), ToRaw(corners[3]), color, thickness);
            ctx.DrawLine(ToRaw(corners[3]), ToRaw(corners[0]), color, thickness);

            // Top face (4 edges)
            ctx.DrawLine(ToRaw(corners[4]), ToRaw(corners[5]), color, thickness);
            ctx.DrawLine(ToRaw(corners[5]), ToRaw(corners[6]), color, thickness);
            ctx.DrawLine(ToRaw(corners[6]), ToRaw(corners[7]), color, thickness);
            ctx.DrawLine(ToRaw(corners[7]), ToRaw(corners[4]), color, thickness);

            // Vertical edges (4 edges)
            ctx.DrawLine(ToRaw(corners[0]), ToRaw(corners[4]), color, thickness);
            ctx.DrawLine(ToRaw(corners[1]), ToRaw(corners[5]), color, thickness);
            ctx.DrawLine(ToRaw(corners[2]), ToRaw(corners[6]), color, thickness);
            ctx.DrawLine(ToRaw(corners[3]), ToRaw(corners[7]), color, thickness);
        }

        private void Draw3DCorners(Dx9RenderContext ctx, SKPoint[] corners, DxColor color, float thickness)
        {
            float cornerLen = 8f;

            // Each corner draws 3 lines toward adjacent corners
            // Bottom corners (0-3)
            DrawCornerLines(ctx, corners, 0, new[] { 1, 3, 4 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 1, new[] { 0, 2, 5 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 2, new[] { 1, 3, 6 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 3, new[] { 0, 2, 7 }, cornerLen, color, thickness);
            // Top corners (4-7)
            DrawCornerLines(ctx, corners, 4, new[] { 5, 7, 0 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 5, new[] { 4, 6, 1 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 6, new[] { 5, 7, 2 }, cornerLen, color, thickness);
            DrawCornerLines(ctx, corners, 7, new[] { 4, 6, 3 }, cornerLen, color, thickness);
        }

        private void DrawCornerLines(Dx9RenderContext ctx, SKPoint[] corners, int corner, int[] adjacents, float len, DxColor color, float thickness)
        {
            var c = corners[corner];
            foreach (var adj in adjacents)
            {
                DrawPartialLine(ctx, c, corners[adj], len, color, thickness);
            }
        }

        private void DrawPartialLine(Dx9RenderContext ctx, SKPoint from, SKPoint to, float maxLen, DxColor color, float thickness)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 0.001f) return;
            var ratio = Math.Min(maxLen / dist, 1f);
            var end = new RawVector2(from.X + dx * ratio, from.Y + dy * ratio);
            ctx.DrawLine(ToRaw(from), end, color, thickness);
        }

        /// <summary>
        /// Determines player color based on type
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerColor(AbstractPlayer player)
        {
             if (player.IsFocused)
                return SKPaints.PaintAimviewWidgetFocused;
            if (player is LocalPlayer)
                return SKPaints.PaintAimviewWidgetLocalPlayer;
            if (player.IsManualTeammate)
                return SKPaints.PaintAimviewWidgetTeammate;

            if (player.Type == PlayerType.PMC)
            {
                if (App.Config.UI.EspGroupColors && player.GroupID >= 0 && !(player is LocalPlayer))
                {
                    return _espGroupPaints.GetOrAdd(player.GroupID, id =>
                    {
                        var color = _espGroupPalette[Math.Abs(id) % _espGroupPalette.Length];
                        return new SKPaint
                        {
                            Color = color,
                            StrokeWidth = SKPaints.PaintAimviewWidgetPMC.StrokeWidth,
                            Style = SKPaints.PaintAimviewWidgetPMC.Style,
                            IsAntialias = SKPaints.PaintAimviewWidgetPMC.IsAntialias
                        };
                    });
                }

                if (App.Config.UI.EspFactionColors)
                {
                    if (player.PlayerSide == Enums.EPlayerSide.Bear)
                        return SKPaints.PaintPMCBear;
                    if (player.PlayerSide == Enums.EPlayerSide.Usec)
                        return SKPaints.PaintPMCUsec;
                }

                return SKPaints.PaintPMC;
            }

            return player.Type switch
            {
                PlayerType.Teammate => SKPaints.PaintAimviewWidgetTeammate,
                PlayerType.AIScav => SKPaints.PaintAimviewWidgetScav,
                PlayerType.AIRaider => SKPaints.PaintAimviewWidgetRaider,
                PlayerType.AIBoss => SKPaints.PaintAimviewWidgetBoss,
                PlayerType.PScav => SKPaints.PaintAimviewWidgetPScav,
                PlayerType.SpecialPlayer => SKPaints.PaintAimviewWidgetWatchlist,
                PlayerType.Streamer => SKPaints.PaintAimviewWidgetStreamer,
                _ => SKPaints.PaintAimviewWidgetPMC
            };
        }

        /// <summary>
        /// Draws player label (name/distance) relative to the bounding box or head fallback.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerLabel(Dx9RenderContext ctx, AbstractPlayer player, float distance, DxColor color, RectangleF? bbox, float screenWidth, float screenHeight, bool showName, bool showDistance, bool showHealth, bool showGroup)
        {
            if (!showName && !showDistance && !showHealth && !showGroup)
                return;

            var name = showName ? GetPlayerDisplayName(player) ?? "Unknown" : null;
            var distanceText = showDistance ? $"{distance:F0}m" : null;

            string healthText = null;
            if (showHealth && player is ObservedPlayer observed && observed.HealthStatus is not Enums.ETagStatus.Healthy)
                healthText = observed.HealthStatus.ToString();

            string factionText = null;
            if (App.Config.UI.EspPlayerFaction && player.IsPmc)
                factionText = player.PlayerSide.ToString();

            string groupText = null;
            if (showGroup && player.GroupID != -1 && player.IsPmc && !player.IsAI)
                groupText = $"G:{player.GroupID}";

            string text = name;
            if (!string.IsNullOrWhiteSpace(healthText))
                text = string.IsNullOrWhiteSpace(text) ? healthText : $"{text} ({healthText})";
            if (!string.IsNullOrWhiteSpace(distanceText))
                text = string.IsNullOrWhiteSpace(text) ? distanceText : $"{text} ({distanceText})";
            if (!string.IsNullOrWhiteSpace(groupText))
                text = string.IsNullOrWhiteSpace(text) ? groupText : $"{text} [{groupText}]";
            if (!string.IsNullOrWhiteSpace(factionText))
                text = string.IsNullOrWhiteSpace(text) ? factionText : $"{text} [{factionText}]";

            if (string.IsNullOrWhiteSpace(text))
                return;

            float drawX;
            float drawY;

            var bounds = ctx.MeasureText(text, DxTextSize.Medium);
            int textHeight = Math.Max(1, bounds.Bottom - bounds.Top);
            int textPadding = 6;

            var labelPos = player.IsAI ? App.Config.UI.EspLabelPositionAI : App.Config.UI.EspLabelPosition;

            // Check if weapons should be shown based on player type
            bool showWeapon = player.IsAI ? App.Config.UI.EspAIWeapons : App.Config.UI.EspPlayerWeapons;
            int extraHeightForWeapon = showWeapon ? textHeight + 2 : 0;

            if (bbox.HasValue)
            {
                var box = bbox.Value;
                drawX = box.Left + (box.Width / 2f);
                drawY = labelPos == EspLabelPosition.Top
                    ? box.Top - textHeight - textPadding - extraHeightForWeapon
                    : box.Bottom + textPadding;
            }
            else if (TryProject(player.GetBonePos(Bones.HumanHead), screenWidth, screenHeight, out var headScreen))
            {
                drawX = headScreen.X;
                drawY = labelPos == EspLabelPosition.Top
                    ? headScreen.Y - textHeight - textPadding - extraHeightForWeapon
                    : headScreen.Y + textPadding;
            }
            else
            {
                return;
            }

            ctx.DrawText(text, drawX, drawY, color, DxTextSize.Medium, centerX: true);

            // Draw weapon on a new line below the name
            if (showWeapon)
            {
                var weaponText = player.WeaponDisplayText;
                float weaponY = drawY + textHeight + 2;
                ctx.DrawText(weaponText, drawX, weaponY, color, DxTextSize.Medium, centerX: true);
            }
        }

        /// <summary>
        /// Draw 'ESP Hidden' notification.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(Dx9RenderContext ctx, float width, float height)
        {
            ctx.DrawText("ESP Hidden", width / 2f, height / 2f, new DxColor(255, 255, 255, 255), DxTextSize.Large, centerX: true, centerY: true);
        }

        private void DrawCrosshair(Dx9RenderContext ctx, float width, float height)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;
            float length = MathF.Max(2f, App.Config.UI.EspCrosshairLength);

            var color = GetCrosshairColor();
            ctx.DrawLine(new RawVector2(centerX - length, centerY), new RawVector2(centerX + length, centerY), color, _crosshairPaint.StrokeWidth);
            ctx.DrawLine(new RawVector2(centerX, centerY - length), new RawVector2(centerX, centerY + length), color, _crosshairPaint.StrokeWidth);
        }

        private void DrawDeviceAimbotTargetLine(Dx9RenderContext ctx, float width, float height)
        {
            var DeviceAimbot = MemDMA.DeviceAimbot;
            if (DeviceAimbot?.LockedTarget is not { } target)
                return;

            var headPos = target.GetBonePos(Bones.HumanHead);
            if (!WorldToScreen2(headPos, out var screen, width, height))
                return;

            var center = new RawVector2(width / 2f, height / 2f);
            bool engaged = DeviceAimbot.IsEngaged;
            var skColor = engaged ? new SKColor(0, 200, 255, 200) : new SKColor(255, 210, 0, 180);
            ctx.DrawLine(center, ToRaw(screen), ToColor(skColor), 2f);
        }

        private void DrawDeviceAimbotFovCircle(Dx9RenderContext ctx, float width, float height)
        {
            var cfg = App.Config.Device;
            if (!cfg.ShowFovCircle || cfg.FOV <= 0)
                return;

            float limit = Math.Min(width, height);
            if (limit < 6f) return;

            float radius = Math.Clamp(cfg.FOV, 5f, limit);
            bool engaged = MemDMA.DeviceAimbot?.IsEngaged == true;

            // Parse color from config using SKColor.Parse (supports #AARRGGBB and #RRGGBB formats)
            var colorStr = engaged ? cfg.FovCircleColorEngaged : cfg.FovCircleColorIdle;
            var skColor = SKColor.TryParse(colorStr, out var parsed)
                ? parsed
                : new SKColor(255, 255, 255, 180); // Fallback to semi-transparent white

            ctx.DrawCircle(new RawVector2(width / 2f, height / 2f), radius, ToColor(skColor), filled: false);
        }

        private void DrawDeviceAimbotDebugOverlay(Dx9RenderContext ctx, float width, float height)
        {
            if (!App.Config.Device.ShowDebug)
                return;

            var snapshot = MemDMA.DeviceAimbot?.GetDebugSnapshot();

            var lines = snapshot == null
                ? new[] { "Device Aimbot: no data" }
                : new[]
                {
                    "=== Device Aimbot ===",
                    $"Status: {snapshot.Status}",
                    $"Key: {(snapshot.KeyEngaged ? "ENGAGED" : "Idle")} | Enabled: {snapshot.Enabled} | Device: {(snapshot.DeviceConnected ? "Connected" : "Disconnected")}",
                    $"InRaid: {snapshot.InRaid} | FOV: {snapshot.ConfigFov:F0}px | MaxDist: {snapshot.ConfigMaxDistance:F0}m | Mode: {snapshot.TargetingMode}",
                    $"Candidates t:{snapshot.CandidateTotal} type:{snapshot.CandidateTypeOk} dist:{snapshot.CandidateInDistance} skel:{snapshot.CandidateWithSkeleton} w2s:{snapshot.CandidateW2S} final:{snapshot.CandidateCount}",
                    $"Target: {(snapshot.LockedTargetName ?? "None")} [{snapshot.LockedTargetType?.ToString() ?? "-"}] valid={snapshot.TargetValid}",
                    snapshot.LockedTargetDistance.HasValue ? $"  Dist {snapshot.LockedTargetDistance.Value:F1}m | FOV { (float.IsNaN(snapshot.LockedTargetFov) ? "n/a" : snapshot.LockedTargetFov.ToString("F1")) } | Bone {snapshot.TargetBone}" : string.Empty,
                    $"Fireport: {(snapshot.HasFireport ? snapshot.FireportPosition?.ToString() : "None")}",
                    $"Ballistics: {(snapshot.BallisticsValid ? $"OK (Speed {(snapshot.BulletSpeed.HasValue ? snapshot.BulletSpeed.Value.ToString("F1") : "?")} m/s, Predict {(snapshot.PredictionEnabled ? "ON" : "OFF")})" : "Invalid/None")}"
                }.Where(l => !string.IsNullOrEmpty(l)).ToArray();

            float x = 10f;
            float y = 40f;
            float lineStep = 16f;
            var color = ToColor(SKColors.White);

            foreach (var line in lines)
            {
                ctx.DrawText(line, x, y, color, DxTextSize.Small);
                y += lineStep;
            }

            // Add raid mode detection info
            y += lineStep; // Extra spacing
            var players = Memory.Players;
            if (players != null && Memory.InRaid)
            {
                // Check if any non-local players are ObservedPlayers (indicates online raid)
                bool hasObservedPlayers = players.Any(p => !(p is LocalPlayer) && (p is ObservedPlayer));
                string raidMode = hasObservedPlayers ? "ONLINE" : "OFFLINE/PVE";
                var modeColor = hasObservedPlayers ? new DxColor(0, 0, 255, 255) : new DxColor(0, 255, 0, 255);
                ctx.DrawText($"Raid Mode: {raidMode}", x, y, modeColor, DxTextSize.Small);
                y += lineStep;

                // Show PVE scan status
                bool pveEnabled = App.Config.Containers.PveScanEnabled;
                var scanColor = pveEnabled ? new DxColor(0, 255, 0, 255) : new DxColor(0, 0, 255, 255);
                ctx.DrawText($"PVE Content Scan: {(pveEnabled ? "ON" : "OFF")}", x, y, scanColor, DxTextSize.Small);
            }
        }

        private void DrawFPS(Dx9RenderContext ctx, float width, float height)
        {
            var fpsText = $"FPS: {_fps}";
            ctx.DrawText(fpsText, 10, 10, new DxColor(255, 255, 255, 255), DxTextSize.Small);
        }

        private static RawVector2 ToRaw(SKPoint point) => new(point.X, point.Y);

        private static DxColor ToColor(SKPaint paint) => ToColor(paint.Color);

        private static DxColor ToColor(SKColor color) => new(color.Blue, color.Green, color.Red, color.Alpha);

        #endregion

        private DxColor GetPlayerColorForRender(AbstractPlayer player)
        {
            var cfg = App.Config.UI;
            var basePaint = GetPlayerColor(player);

            // Preserve special colouring (local, focused, watchlist/streamer, teammates).
            if (player is LocalPlayer || player.IsFocused || player.IsManualTeammate ||
                player.Type is PlayerType.SpecialPlayer or PlayerType.Streamer or PlayerType.Teammate)
            {
                return ToColor(basePaint);
            }

            // Respect group/faction colours when enabled.
            if (!player.IsAI)
            {
                if (cfg.EspGroupColors && player.GroupID >= 0)
                    return ToColor(basePaint);
                if (cfg.EspFactionColors && player.IsPmc)
                {
                    var factionColor = player.PlayerSide switch
                    {
                        Enums.EPlayerSide.Bear => ColorFromHex(cfg.EspColorFactionBear),
                        Enums.EPlayerSide.Usec => ColorFromHex(cfg.EspColorFactionUsec),
                        _ => ColorFromHex(cfg.EspColorPlayers)
                    };
                    return ToColor(factionColor);
                }
            }

            if (player.IsAI)
            {
                // Handle offline PMC bots - use faction colors if enabled
                if (player.Type == PlayerType.PMC)
                {
                    if (cfg.EspFactionColors)
                    {
                        var factionColor = player.PlayerSide switch
                        {
                            Enums.EPlayerSide.Bear => ColorFromHex(cfg.EspColorFactionBear),
                            Enums.EPlayerSide.Usec => ColorFromHex(cfg.EspColorFactionUsec),
                            _ => ColorFromHex(cfg.EspColorPlayers)
                        };
                        return ToColor(factionColor);
                    }
                    return ToColor(ColorFromHex(cfg.EspColorPlayers));
                }

                var aiHex = player.Type switch
                {
                    PlayerType.AIBoss => cfg.EspColorBosses,
                    PlayerType.AIRaider => cfg.EspColorRaiders,
                    _ => cfg.EspColorAI
                };

                return ToColor(ColorFromHex(aiHex));
            }

            // Handle Player Scavs specifically.
            if (player.Type == PlayerType.PScav)
            {
                return ToColor(ColorFromHex(cfg.EspColorPlayerScavs));
            }

            // Fallback to user-configured player colours.
            return ToColor(ColorFromHex(cfg.EspColorPlayers));
        }

        private DxColor GetLootColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorLoot));
        private DxColor GetExfilColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorExfil));
        private DxColor GetTripwireColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorTripwire));
        private DxColor GetGrenadeColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorGrenade));
        private DxColor GetContainerColorForRender() => ToColor(ColorFromHex(App.Config.UI.EspColorContainers));
        private DxColor GetCrosshairColor() => ToColor(ColorFromHex(App.Config.UI.EspColorCrosshair));

        private static SKColor ColorFromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return SKColors.White;
            try { return SKColor.Parse(hex); }
            catch { return SKColors.White; }
        }

        private void ApplyDxFontConfig()
        {
            var ui = App.Config.UI;
            _dxOverlay?.SetFontConfig(
                ui.EspFontFamily,
                ui.EspFontSizeSmall,
                ui.EspFontSizeMedium,
                ui.EspFontSizeLarge);
        }

        #region DX Init Handling

        private void Overlay_DeviceInitFailed(Exception ex)
        {
            _dxInitFailed = true;
            DebugLogger.LogDebug($"ESP DX init failed: {ex}");

            Dispatcher.BeginInvoke(new Action(() =>
            {
                RenderRoot.Children.Clear();
                RenderRoot.Children.Add(new TextBlock
                {
                    Text = "DX overlay init failed. See log for details.",
                    Foreground = System.Windows.Media.Brushes.White,
                    Background = System.Windows.Media.Brushes.Black,
                    Margin = new Thickness(12)
                });
            }), DispatcherPriority.Send);
        }

        #endregion

        #region WorldToScreen Conversion

        /// <summary>
        /// Resets ESP state when a raid ends (ensures clean slate next raid).
        /// </summary>
        public void OnRaidStopped()
        {
            _lastInRaidState = false;
            _espGroupPaints.Clear();
            _lastMapId = null; // Reset map ID to force update next raid
            _miniRadarParams = default; // Clear render params
            CameraManager.Reset();
            RefreshESP();
            DebugLogger.LogInfo("ESP: RaidStopped -> state reset");
        }

        /// <summary>
        /// Gets the display name for a player.
        /// </summary>
        /// <param name="player">The player to get the display name for</param>
        /// <returns>The player's name</returns>
        private static string GetPlayerDisplayName(AbstractPlayer player)
        {
            return player?.Name;
        }

        private bool WorldToScreen2(in Vector3 world, out SKPoint scr, float screenWidth, float screenHeight)
        {
            return CameraManager.WorldToScreen(in world, out scr, true, true);
        }

        /// <summary>
        /// Converts world position to screen with distance-based scale factor.
        /// </summary>
        private bool WorldToScreen2WithScale(in Vector3 world, out SKPoint scr, out float scale, float screenWidth, float screenHeight)
        {
            return CameraManager.WorldToScreenWithScale(in world, out scr, out scale, true, true);
        }

        private bool TryProject(in Vector3 world, float w, float h, out SKPoint screen)
        {
            screen = default;
            if (world == Vector3.Zero)
                return false;
            if (!WorldToScreen2(world, out screen, w, h))
                return false;
            if (float.IsNaN(screen.X) || float.IsInfinity(screen.X) ||
                float.IsNaN(screen.Y) || float.IsInfinity(screen.Y))
                return false;

            const float margin = 200f; 
            if (screen.X < -margin || screen.X > w + margin ||
                screen.Y < -margin || screen.Y > h + margin)
                return false;

            return true;
        }

        #endregion

        #region Window Management

        private void GlControl_MouseDown(object sender, WinForms.MouseEventArgs e)
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                // Check if ammo widget handles the click first
                if (_ammoWidget != null && App.Config.UI.AmmoCounter.Enabled)
                {
                    float screenWidth = _dxOverlay?.Width ?? 1920;
                    float screenHeight = _dxOverlay?.Height ?? 1080;
                    if (_ammoWidget.OnMouseDown(e.X, e.Y, screenWidth, screenHeight))
                        return; // Widget handled the click
                }

                try { this.DragMove(); } catch { /* ignore dragging errors */ }
            }
        }

        private void GlControl_MouseMove(object sender, WinForms.MouseEventArgs e)
        {
            // Track mouse position for hover detection
            _mouseX = e.X;
            _mouseY = e.Y;

            if (_ammoWidget != null && _ammoWidget.IsInteracting)
            {
                float screenWidth = _dxOverlay?.Width ?? 1920;
                float screenHeight = _dxOverlay?.Height ?? 1080;
                _ammoWidget.OnMouseMove(e.X, e.Y, screenWidth, screenHeight);
            }
        }

        private void GlControl_MouseUp(object sender, WinForms.MouseEventArgs e)
        {
            if (_ammoWidget != null && _ammoWidget.IsInteracting)
            {
                _ammoWidget.OnMouseUp();
            }
        }

        private void GlControl_DoubleClick(object sender, EventArgs e)
        {
            ToggleFullscreen();
        }

        private void GlControl_KeyDown(object sender, WinForms.KeyEventArgs e)
        {
            if (e.KeyCode == WinForms.Keys.F12)
            {
                ForceReleaseCursorAndHide();
                return;
            }

            if (e.KeyCode == WinForms.Keys.Escape && this.WindowState == WindowState.Maximized)
            {
                ToggleFullscreen();
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        protected override void OnClosed(System.EventArgs e)
        {
            _isClosing = true;
            try
            {
                _highFrequencyTimer?.Dispose();
                _dxOverlay?.Dispose();
                _skeletonPaint.Dispose();
                _boxPaint.Dispose();
                _crosshairPaint.Dispose();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP: OnClosed cleanup error: {ex}");
            }
            finally
            {
                base.OnClosed(e);
            }
        }

        // Method to force refresh
        public void RefreshESP()
        {
            if (_isClosing)
                return;

            try
            {
                _dxOverlay?.Render();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP Refresh error: {ex}");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _renderPending, 0);
            }
        }

        private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ToggleFullscreen();
        }

        // Handler for keys (ESC to exit fullscreen)
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F12)
            {
                ForceReleaseCursorAndHide();
                return;
            }

            if (e.Key == Key.Escape && this.WindowState == WindowState.Maximized)
            {
                ToggleFullscreen();
            }
        }

        // Simple fullscreen toggle
        public void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.SingleBorderWindow;
                this.Topmost = false;
                this.ResizeMode = ResizeMode.CanResize;
                this.Width = 400;
                this.Height = 300;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                _isFullscreen = false;
            }
            else
            {
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;
                this.Topmost = true;
                this.WindowState = WindowState.Normal;

                var monitor = GetTargetMonitor();
                var (width, height) = GetConfiguredResolution(monitor);

                this.Left = monitor?.Left ?? 0;
                this.Top = monitor?.Top ?? 0;

                this.Width = width;
                this.Height = height;
                _isFullscreen = true;
            }

            this.RefreshESP();
        }

        public void ApplyResolutionOverride()
        {
            if (!_isFullscreen)
                return;

            var monitor = GetTargetMonitor();
            var (width, height) = GetConfiguredResolution(monitor);
            this.Left = monitor?.Left ?? 0;
            this.Top = monitor?.Top ?? 0;
            this.Width = width;
            this.Height = height;
            this.RefreshESP();
        }

        private (double width, double height) GetConfiguredResolution(MonitorInfo monitor)
        {
            double width = App.Config.UI.EspScreenWidth > 0
                ? App.Config.UI.EspScreenWidth
                : monitor?.Width ?? SystemParameters.PrimaryScreenWidth;
            double height = App.Config.UI.EspScreenHeight > 0
                ? App.Config.UI.EspScreenHeight
                : monitor?.Height ?? SystemParameters.PrimaryScreenHeight;
            return (width, height);
        }

        private void ApplyResolutionOverrideIfNeeded()
        {
            if (!_isFullscreen)
                return;

            if (App.Config.UI.EspScreenWidth <= 0 && App.Config.UI.EspScreenHeight <= 0)
                return;

            double currentWidth, currentHeight;
            if (Dispatcher.CheckAccess())
            {
                currentWidth = Width;
                currentHeight = Height;
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    currentWidth = Width;
                    currentHeight = Height;
                });
                return;
            }

            var monitor = GetTargetMonitor();
            var target = GetConfiguredResolution(monitor);
            if (Math.Abs(currentWidth - target.width) > 0.5 || Math.Abs(currentHeight - target.height) > 0.5)
            {
                if (Dispatcher.CheckAccess())
                {
                    Width = target.width;
                    Height = target.height;
                    Left = monitor?.Left ?? 0;
                    Top = monitor?.Top ?? 0;
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Width = target.width;
                        Height = target.height;
                        Left = monitor?.Left ?? 0;
                        Top = monitor?.Top ?? 0;
                    }));
                }
            }
        }

        private MonitorInfo GetTargetMonitor()
        {
            return MonitorInfo.GetMonitor(App.Config.UI.EspTargetScreen);
        }

        public void ApplyFontConfig()
        {
            ApplyDxFontConfig();
            RefreshESP();
        }

        /// <summary>
        /// Emergency escape hatch if the overlay ever captures the cursor:
        /// releases capture, resets cursors, hides the ESP, and drops Topmost.
        /// Bound to F12 on both WPF and WinForms handlers.
        /// </summary>
        private void ForceReleaseCursorAndHide()
        {
            try
            {
                Mouse.Capture(null);
                WinForms.Cursor.Current = WinForms.Cursors.Default;
                this.Cursor = System.Windows.Input.Cursors.Arrow;
                Mouse.OverrideCursor = null;
                if (_dxOverlay != null)
                {
                    _dxOverlay.Capture = false;
                }
                this.Topmost = false;
                ShowESP = false;
                Hide();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ESP: ForceReleaseCursor failed: {ex}");
            }
        }

        #endregion
    }
}
