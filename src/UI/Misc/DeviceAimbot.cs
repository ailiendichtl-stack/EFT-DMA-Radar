/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc.Ballistics;
using LoneEftDmaRadar.LOS;
using SkiaSharp;
using LoneEftDmaRadar.Tarkov.GameWorld.Camera;

namespace LoneEftDmaRadar.UI.Misc
{
    /// <summary>
    /// Device-based hardware aimbot (DeviceAimbot/KMBox) with ballistics prediction.
    /// </summary>
    public sealed class DeviceAimbot : IDisposable
    {
        #region Constants

        // Timing constants (in Stopwatch ticks - use Stopwatch.Frequency to convert)
        private static readonly long BallisticsCacheDurationTicks = Stopwatch.Frequency / 2; // 500ms
        private static readonly long ClosestBoneCacheDurationTicks = Stopwatch.Frequency / 20; // 50ms

        // Smoothing constants
        private const float SMOOTHING_REFERENCE_RATE = 60f; // Reference polling rate for smoothing calculations
        private const float MAX_MOVE_PER_TICK = 127f; // Device hardware limit (-127 to 127)

        // Sanity check constants
        private const float MAX_SCREEN_DELTA = 2000f; // Max reasonable screen delta before considering data corrupt
        private const float DEADZONE_PIXELS = 0.1f; // Minimum movement threshold (reduced from 0.5 to allow convergence at long range)
        private const int MAX_ATTACHMENT_SLOTS = 100; // Sanity limit for attachment recursion

        #endregion

        #region Fields

        private static DeviceAimbotConfig Config => App.Config.Device;
        private readonly MemDMA _memory;
        private readonly Thread _worker;
        private readonly object _targetLock = new(); // Lock for thread-safe target access
        private readonly object _ballisticsLock = new(); // Lock for ballistics cache atomicity
        private readonly Stopwatch _cacheTimer = Stopwatch.StartNew(); // High-precision timer for caching

        // Thread-safe state
        private volatile bool _disposed;
        private volatile string _debugStatus = "Initializing...";
        private AbstractPlayer _lockedTarget; // Protected by _targetLock
        private BallisticsInfo _lastBallistics; // Protected by _ballisticsLock

        // FOV / target debug fields (read by UI thread, written by worker)
        private volatile int _lastCandidateCount;
        private volatile float _lastLockedTargetFov = float.NaN;
        private volatile bool _lastIsTargetValid;
        private Vector3 _lastFireportPos;
        private volatile bool _hasLastFireport;

        // Per-stage debug counters
        private volatile int _dbgTotalPlayers;
        private volatile int _dbgEligibleType;
        private volatile int _dbgWithinDistance;
        private volatile int _dbgHaveSkeleton;
        private volatile int _dbgW2SPassed;

        // Ballistics cache - weapon/ammo rarely changes, avoid re-reading every frame
        private ulong _cachedBallisticsHandsAddr;
        private long _ballisticsCacheTimeTicks;

        // Closest bone cache - avoid recalculating every frame
        private Bones _cachedClosestBone;
        private AbstractPlayer _cachedClosestBoneTarget;
        private long _closestBoneCacheTimeTicks;

        // Precision timing for main loop
        private readonly Stopwatch _loopTimer = new();

        // Fractional accumulators - track sub-pixel movement to prevent rounding loss
        private float _accumX, _accumY;

        // In-flight move tracking (Smith Predictor for render delay compensation).
        // Mouse moves take ~2 game frames to appear in the screen projection.
        // At tick rates matching game FPS (~60Hz), we'd send 2 full corrections before
        // either renders, causing the effective gain to exceed 1.0 → divergent oscillation.
        // We track pending device-scale moves and subtract their expected displacement
        // from the observed delta, so each tick only corrects the RESIDUAL error.
        private readonly Queue<(long ticks, float dx, float dy)> _inflightMoves = new();
        private float _lastMoveDeviceX, _lastMoveDeviceY; // Set by CalculateSmoothMovement

        // Stale-data detection: the aimbot loop runs at 850Hz but DMA camera data only
        // updates at game FPS (~60-120Hz). Sending moves on unchanged screen positions
        // causes accumulation overshoot. We skip frames where W2S result hasn't changed.
        private SKPoint _lastW2SResult;
        private bool _hasLastW2SResult;

        // Actual update rate measurement — tracks real Hz between non-stale frames
        private long _lastFreshFrameTicks;
        private float _measuredUpdateRate = 60f; // Start conservative

        // Velocity tracking for PD control (screen-space)
        private Vector2 _lastTargetScreenPos;
        private Vector2 _targetScreenVelocity;
        private bool _hasLastScreenPos;
        private long _lastVelocityUpdateTicks;

        // World-space velocity tracking (works for ALL target types including AI)
        private Vector3 _velocitySamplePos;      // Position used for velocity calculation
        private Vector3 _targetWorldVelocity;
        private bool _hasVelocitySample;
        private long _velocitySampleTicks;
        private string _lastTrackedTargetId;
        private volatile float _lastWorldSpeed;   // Magnitude of world velocity, used by hitscan grace scaling

        // Exposed aim state for ESP debug markers
        private Vector3 _lastPredictedAimPos;
        private Vector3 _lastRawBoneAimPos;

        // Ergo scale tracking for diagnostics
        private float _lastErgoScale = 1f;

        // Direction-change detection for Smith Predictor queue flush
        private float _prevPendingX, _prevPendingY;

        // Scoped engagement ramp-up (prevents initial overshoot when scoping onto a target)
        private int _scopedEngageFrames;

        // Ergo/sensitivity compensation — cached from memory reads
        private volatile float _cachedOverweightAimMult = 1.0f; // PWA._overweightAimingMultiplier (carry weight)
        private volatile float _cachedAimingSens = 1.0f; // FirearmController._aimingSens (weapon ergo + user ADS setting)
        private volatile float _maxAimingSens = 0.4f; // Highest _aimingSens seen (reference baseline)
        private long _ergoReadTimeTicks;
        private static readonly long ErgoCacheDurationTicks = Stopwatch.Frequency / 10; // 100ms

        // Hitscan bone priority state
        private Bones _hitscanCurrentBone;         // Currently locked bone
        private long _hitscanBoneLockTick;         // Stopwatch tick when bone was locked
        private long _hitscanBoneLostTick;         // Tick when current bone lost visibility (0 = visible)
        private Bones _hitscanPendingUpgrade;      // Higher-priority bone being confirmed
        private long _hitscanPendingStartTick;     // Tick when pending upgrade first became shootable
        private ulong _hitscanTargetBase;          // Target address these states belong to
        private const float HITSCAN_FALLBACK_MAX_FOV = 200f; // Prevents snapping to extremities

        // Reusable list to avoid allocations in hot path
        private readonly List<TargetCandidate> _candidateBuffer = new(32);

        // Cached fonts and paints for debug overlay (avoid allocations every frame)
        private static readonly SKTypeface DebugTypeface = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Normal);
        private static readonly SKTypeface DebugTypefaceBold = SKTypeface.FromFamilyName("Consolas", SKFontStyle.Bold);

        private static SKFont DebugFont => new(DebugTypeface, 14f)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

        private static SKFont DebugFontBold => new(DebugTypefaceBold, 14f)
        {
            Subpixel = true,
            Edging = SKFontEdging.SubpixelAntialias
        };

        private static readonly SKPaint DebugBgPaint = new()
        {
            Color = new SKColor(0, 0, 0, 180),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        private static readonly SKPaint DebugTextPaint = new()
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        private static readonly SKPaint DebugHeaderPaint = new()
        {
            Color = SKColors.Yellow,
            IsAntialias = true
        };
        private static readonly SKPaint DebugShadowPaint = new()
        {
            Color = SKColors.Black,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3
        };

        // Reusable list for debug lines
        private readonly List<string> _debugLines = new(64);

        // === Aim Diagnostic Logger ===
        // Captures every frame of aim sequences to JSON Lines for analysis.
        // Enable: set AIM_DIAG_ENABLED = true, rebuild, do a few runs, then set back to false.
        private const bool AIM_DIAG_ENABLED = false;
        private static readonly string AIM_DIAG_PATH = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, $"aim_diag_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
        private StreamWriter _diagWriter;
        private int _diagSeqId;       // Increments each new engagement (target lock)
        private int _diagFrameId;     // Frame counter within current sequence
        private string _diagLastTargetId; // Detect target switches

        #endregion

        private void SendDeviceMove(int dx, int dy)
        {
            if (Config.UseKmBoxNet && DeviceNetController.Connected)
            {
                DeviceNetController.Move(dx, dy);
                return;
            }

            Device.move(dx, dy);
        }
        #region Constructor / Disposal

        public DeviceAimbot(MemDMA memory)
        {
            _memory = memory;

            // Try auto-connect if configured and device isn't ready.
            if (Config.AutoConnect && !Device.connected)
            {
                try
                {
                    Device.TryAutoConnect(Config.LastComPort);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[DeviceAimbot] Auto-connect failed: {ex.Message}");
                }
            }

            if (Config.UseKmBoxNet && !DeviceNetController.Connected)
            {
                try
                {
                    DeviceNetController.Connect(Config.KmBoxNetIp, Config.KmBoxNetPort, Config.KmBoxNetMac);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[DeviceAimbot] KmBoxNet connect failed: {ex.Message}");
                }
            }

            // Initialize diagnostic logger
            if (AIM_DIAG_ENABLED)
            {
                try
                {
                    _diagWriter = new StreamWriter(AIM_DIAG_PATH, append: false) { AutoFlush = true };
                    DebugLogger.LogDebug($"[DeviceAimbot] Aim diagnostics logging to: {AIM_DIAG_PATH}");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"[DeviceAimbot] Failed to open diag file: {ex.Message}");
                }
            }

            _worker = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "DeviceAimbotWorker"
            };
            _worker.Start();

            DebugLogger.LogDebug("[DeviceAimbot] Started");
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Close diagnostic logger
            try { _diagWriter?.Dispose(); } catch { }

            // Wait for worker thread to exit (with timeout)
            if (_worker != null && _worker.IsAlive)
            {
                if (!_worker.Join(TimeSpan.FromSeconds(2)))
                {
                    DebugLogger.LogWarning("[DeviceAimbot] Worker thread did not exit cleanly");
                }
            }

            DebugLogger.LogDebug("[DeviceAimbot] Disposed");
        }

        public void OnRaidEnd()
        {
            ResetTarget();
        }

        #endregion

        #region Main Loop

        private void WorkerLoop()
        {
            _debugStatus = "Worker starting...";

            while (!_disposed)
            {
                // Start precision timer at beginning of each iteration
                _loopTimer.Restart();

                try
                {
                    // 1) Check if we're in raid with a valid local player
                    if (!_memory.InRaid)
                    {
                        _debugStatus = "Not in raid";
                        ResetTarget();
                        Thread.Sleep(100);
                        continue;
                    }

                    if (_memory.LocalPlayer is not LocalPlayer localPlayer)
                    {
                        _debugStatus = "LocalPlayer == null";
                        ResetTarget();
                        Thread.Sleep(50);
                        continue;
                    }

                    // 3) Check if anything wants to run (hardware aimbot or memory silent aim)
                    bool memoryAimActive = App.Config.MemWrites.Enabled && App.Config.MemWrites.MemoryAimEnabled;
                    bool anyAimbotEnabled = Config.Enabled || memoryAimActive;

                    if (!anyAimbotEnabled)
                    {
                        _debugStatus = "Aimbot & MemoryAim disabled (NoRecoil still active if enabled)";
                        ResetTarget();
                        Thread.Sleep(100);
                        continue;
                    }


                    if (!memoryAimActive && !Device.connected && !DeviceNetController.Connected)
                    {
                        _debugStatus = "Device/KMBoxNet NOT connected (enable MemoryAim to use without device)";
                        ResetTarget();
                        Thread.Sleep(250);
                        continue;
                    }

                    if (_memory.Game is not LocalGameWorld game)
                    {
                        _debugStatus = "Game instance == null";
                        ResetTarget();
                        Thread.Sleep(50);
                        continue;
                    }

                    // 4) Check engagement for AIMBOT only
                    if (!IsEngaged)
                    {
                        _debugStatus = "Waiting for aim key (IsEngaged == false)";
                        ResetTarget();
                        Thread.Sleep(10);
                        continue;
                    }

                    // 5) Weapon / Fireport check - read from immutable snapshot (updated by T1 worker)
                    _debugStatus = "Reading FirearmManager snapshot...";
                    var snapshot = localPlayer.FirearmManager?.CurrentSnapshot;
                    var fireportPosOpt = snapshot?.FireportPosition;
                    bool needsFireport = Config.EnablePrediction ||
                        (App.Config.MemWrites.Enabled && App.Config.MemWrites.MemoryAimEnabled);

                    if (needsFireport && fireportPosOpt is not Vector3 fireportPos)
                    {
                        _debugStatus = "No valid weapon / fireport (needed for prediction/MemoryAim)";
                        ResetTarget();
                        _hasLastFireport = false;
                        Thread.Sleep(16);
                        continue;
                    }

                    if (fireportPosOpt is Vector3 fp)
                    {
                        _lastFireportPos = fp;
                        _hasLastFireport = true;
                    }
                    else
                    {
                        _hasLastFireport = false;
                    }

                    // 6) Target acquisition (thread-safe)
                    AbstractPlayer currentTarget;
                    lock (_targetLock)
                    {
                        currentTarget = _lockedTarget;
                    }

                    if (currentTarget == null)
                    {
                        // No target - always try to acquire one
                        _debugStatus = "Scanning for target...";
                        var newTarget = FindBestTarget(game, localPlayer);

                        if (newTarget == null)
                        {
                            _debugStatus = "No target in FOV / range";
                            Thread.Sleep(10);
                            continue;
                        }

                        // Full reset (hitscan, world speed, dead-reckoning, velocity) then set new target
                        ResetTarget();
                        lock (_targetLock)
                        {
                            _lockedTarget = newTarget;
                            currentTarget = newTarget;
                        }
                    }
                    else if (!IsTargetValid(currentTarget, localPlayer))
                    {
                        if (Config.AutoTargetSwitch)
                        {
                            // Auto switch enabled - find new target
                            _debugStatus = "Target invalid, auto-switching...";
                            var newTarget = FindBestTarget(game, localPlayer);

                            if (newTarget == null)
                            {
                                _debugStatus = "No target in FOV / range";
                                Thread.Sleep(10);
                                continue;
                            }

                            // Full reset (hitscan, world speed, dead-reckoning, velocity) then set new target
                            ResetTarget();
                            lock (_targetLock)
                            {
                                _lockedTarget = newTarget;
                                currentTarget = newTarget;
                            }
                        }
                        else
                        {
                            // Auto switch disabled - wait for user to release and re-engage
                            _debugStatus = "Target lost - release and re-engage to switch";
                            Thread.Sleep(10);
                            continue;
                        }
                    }

                    _debugStatus = $"Target locked: {currentTarget.Name}";

                    // 7) Ballistics
                    _lastBallistics = GetBallisticsInfo(localPlayer);
                    if (_lastBallistics == null || !_lastBallistics.IsAmmoValid)
                    {
                        _debugStatus = $"Target {currentTarget.Name} - No valid ammo/ballistics (using raw aim)";
                    }
                    else
                    {
                        _debugStatus = $"Target {currentTarget.Name} - Ballistics OK";
                    }

                    // 7.5) Update ergo compensation cache
                    if (Config.ErgoCompensation)
                        UpdateErgoCompensation(localPlayer);

                    // 8) Aim
                    AimAtTarget(localPlayer, currentTarget, fireportPosOpt);

                    // === PRECISION TIMING (Step 2) ===
                    // Use configurable polling rate with sub-ms precision
                    int pollingRate = Math.Max(30, Config.PollingRateHz);
                    int targetIntervalMs = 1000 / pollingRate;
                    double elapsedMs = _loopTimer.Elapsed.TotalMilliseconds;
                    int remainingMs = targetIntervalMs - (int)elapsedMs;

                    // Sleep for bulk of remaining time (if > 1ms)
                    if (remainingMs > 1)
                    {
                        Thread.Sleep(remainingMs - 1);
                    }

                    // Spin-wait for final sub-millisecond precision
                    while (_loopTimer.Elapsed.TotalMilliseconds < targetIntervalMs)
                    {
                        Thread.SpinWait(10);
                    }
                }
                catch (Exception ex)
                {
                    _debugStatus = $"Error: {ex.Message}";
                    DebugLogger.LogDebug($"[DeviceAimbot] Error: {ex}");
                    ResetTarget();
                    Thread.Sleep(100);
                }
            }

            _debugStatus = "Worker stopped";
        }

        #endregion

        #region Targeting

        private AbstractPlayer FindBestTarget(LocalGameWorld game, LocalPlayer localPlayer)
        {
            // Reuse buffer to avoid allocations
            _candidateBuffer.Clear();
            _lastCandidateCount = 0;

            // Treat zero/negative limits as "unlimited" so users can clear the field without breaking targeting.
            float maxDistance = Config.MaxDistance <= 0 ? float.MaxValue : Config.MaxDistance;
            float maxFov = Config.FOV <= 0 ? float.MaxValue : Config.FOV;

            // Reset per-stage counters
            _dbgTotalPlayers = 0;
            _dbgEligibleType = 0;
            _dbgWithinDistance = 0;
            _dbgHaveSkeleton = 0;
            _dbgW2SPassed = 0;

            foreach (var player in game.Players)
            {
                _dbgTotalPlayers++;

                if (!ShouldTargetPlayer(player, localPlayer))
                    continue;

                _dbgEligibleType++;

                var distance = Vector3.Distance(localPlayer.Position, player.Position);
                if (distance > maxDistance)
                    continue;

                _dbgWithinDistance++;

                // Check if skeleton exists
                if (player.Skeleton?.BoneTransforms == null)
                    continue;

                _dbgHaveSkeleton++;

                // Hitscan: skip targets with no shootable bones
                if (Config.HitscanEnabled)
                {
                    var visMgr = VisibilityManager.Instance;
                    if (visMgr != null)
                    {
                        var vis = visMgr.GetVisibility(player);
                        if (vis == null || vis.Value.HitscanMask == 0)
                            continue;
                    }
                }

                // Check if any bone is within FOV
                float bestFovForThisPlayer = float.MaxValue;
                bool anyBoneProjected = false;

                foreach (var bone in player.Skeleton.BoneTransforms.Values)
                {
                    // IMPORTANT: use same W2S style as ESP - "in" + default flags
                    // Disable on-screen check so viewport issues don't discard candidates.
                    if (CameraManager.WorldToScreen(in bone.Position, out var screenPos))
                    {
                        anyBoneProjected = true;
                        float fovDist = CameraManager.GetFovMagnitude(screenPos);
                        if (fovDist < bestFovForThisPlayer)
                        {
                            bestFovForThisPlayer = fovDist;
                        }
                    }
                }

                if (anyBoneProjected)
                    _dbgW2SPassed++;

                if (bestFovForThisPlayer <= maxFov)
                {
                    _candidateBuffer.Add(new TargetCandidate
                    {
                        Player = player,
                        FOVDistance = bestFovForThisPlayer,
                        WorldDistance = distance
                    });
                }
            }

            _lastCandidateCount = _candidateBuffer.Count;

            if (_candidateBuffer.Count == 0)
                return null;

            // Select best target based on mode - manual loop to avoid LINQ allocation
            AbstractPlayer bestTarget = null;
            float bestValue = float.MaxValue;

            bool useDistance = Config.Targeting == DeviceAimbotConfig.TargetingMode.ClosestDistance;

            foreach (var candidate in _candidateBuffer)
            {
                float value = useDistance ? candidate.WorldDistance : candidate.FOVDistance;
                if (value < bestValue)
                {
                    bestValue = value;
                    bestTarget = candidate.Player;
                }
            }

            return bestTarget;
        }

private bool ShouldTargetPlayer(AbstractPlayer player, LocalPlayer localPlayer)
{
    // Don't target self
    if (player == localPlayer || player is LocalPlayer)
        return false;

    if (!player.IsActive || !player.IsAlive)
        return false;

    // Check player type filters
    if (player.Type == PlayerType.Teammate)
        return false;

    bool shouldTarget = player.Type switch
    {
        PlayerType.PMC => Config.TargetPMC,
        PlayerType.PScav => Config.TargetPlayerScav,
        PlayerType.AIScav => Config.TargetAIScav,
        PlayerType.AIBoss => Config.TargetBoss,
        PlayerType.AIRaider => Config.TargetRaider,
        PlayerType.SpecialPlayer => Config.TargetSpecialPlayer,
        PlayerType.Streamer => Config.TargetStreamer,
        PlayerType.Default => Config.TargetAIScav,
        _ => false
    };

    return shouldTarget;
}
        /// <summary>
        /// Fast validation for locked targets - O(1), skips expensive bone iteration.
        /// Used when we already have a locked target and just need to verify it's still valid.
        /// </summary>
        private bool IsTargetValidFast(AbstractPlayer target, LocalPlayer localPlayer)
        {
            // Quick checks only - no bone iteration
            if (target == null || !target.IsActive || !target.IsAlive)
                return false;

            float maxDistance = Config.MaxDistance <= 0 ? float.MaxValue : Config.MaxDistance;
            float distance = Vector3.Distance(localPlayer.Position, target.Position);
            if (distance > maxDistance)
                return false;

            // Just check skeleton exists, don't iterate bones
            return target.Skeleton?.BoneTransforms != null && target.Skeleton.BoneTransforms.Count > 0;
        }

        /// <summary>
        /// Full validation with FOV check - O(n bones), used for initial target acquisition.
        /// </summary>
        private bool IsTargetValidFull(AbstractPlayer target, LocalPlayer localPlayer)
        {
            _lastIsTargetValid = false;
            _lastLockedTargetFov = float.NaN;

            if (target == null || !target.IsActive || !target.IsAlive)
                return false;

            float maxDistance = Config.MaxDistance <= 0 ? float.MaxValue : Config.MaxDistance;
            float maxFov = Config.FOV <= 0 ? float.MaxValue : Config.FOV;

            var distance = Vector3.Distance(localPlayer.Position, target.Position);
            if (distance > maxDistance)
                return false;

            // Check if skeleton exists
            if (target.Skeleton?.BoneTransforms == null)
                return false;

            // Compute min FOV distance for this target
            float minFov = float.MaxValue;
            bool anyBoneProjected = false;

            foreach (var bone in target.Skeleton.BoneTransforms.Values)
            {
                if (CameraManager.WorldToScreen(in bone.Position, out var screenPos))
                {
                    anyBoneProjected = true;
                    float fovDist = CameraManager.GetFovMagnitude(screenPos);
                    if (fovDist < minFov)
                        minFov = fovDist;
                }
            }

            if (!anyBoneProjected)
            {
                _lastLockedTargetFov = float.NaN;
                return false;
            }

            _lastLockedTargetFov = minFov;

            if (minFov < maxFov)
            {
                _lastIsTargetValid = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Uses fast validation for locked targets, full validation only when needed.
        /// </summary>
        private bool IsTargetValid(AbstractPlayer target, LocalPlayer localPlayer)
        {
            // Use fast path for already-locked targets (skip FOV re-check)
            // The target was already validated when acquired, just check it's still alive/in-range
            lock (_targetLock)
            {
                if (target == _lockedTarget && _lockedTarget != null)
                {
                    return IsTargetValidFast(target, localPlayer);
                }
            }

            // Full validation for new targets (outside lock to avoid contention)
            return IsTargetValidFull(target, localPlayer);
        }

        /// <summary>
        /// Resets all aim-state that must be cleared when the target changes:
        /// dead-reckoning, fractional accumulators, DMA screen position, and velocity tracking.
        /// Called from ResetTarget() and from target-switch paths.
        /// </summary>
        private void ResetAimState()
        {
            _accumX = 0;
            _accumY = 0;
            _inflightMoves.Clear();
            _lastMoveDeviceX = 0;
            _lastMoveDeviceY = 0;
            _hasLastW2SResult = false;
            _lastFreshFrameTicks = 0;
            _measuredUpdateRate = 60f;
            _prevPendingX = 0;
            _prevPendingY = 0;
            _scopedEngageFrames = 0;
            ResetVelocityTracking();
        }

        private void ResetTarget()
        {
            lock (_targetLock)
            {
                if (_lockedTarget != null)
                {
                    _lockedTarget = null;
                    // Clear bone cache when target changes
                    _cachedClosestBone = default;
                    _cachedClosestBoneTarget = null;
                }
            }
            // Reset hitscan state
            _hitscanCurrentBone = default;
            _hitscanBoneLostTick = 0;
            _hitscanPendingUpgrade = default;
            _hitscanPendingStartTick = 0;
            _hitscanTargetBase = 0;
            _lastWorldSpeed = 0;
            // Reset all aim state (dead-reckoning, accumulators, velocity)
            ResetAimState();
        }

        /// <summary>
        /// Hitscan bone selection: walks the user's priority list and picks the highest-priority
        /// bone that has clear ballistic LOS. Includes hysteresis (grace period + confirmation
        /// window) to prevent rapid bone switching from frame-to-frame visibility flicker.
        /// </summary>
        private bool TryGetHitscanBone(AbstractPlayer target, out UnityTransform boneTransform)
        {
            boneTransform = null;
            var bones = target.PlayerBones;
            if (bones == null || bones.Count == 0) return false;

            // Get visibility data — struct copy, no allocation
            var visMgr = VisibilityManager.Instance;
            if (visMgr == null) return false;
            var vis = visMgr.GetVisibility(target);
            if (vis == null) return false;

            uint hitscanMask = vis.Value.HitscanMask;
            long now = _cacheTimer.ElapsedTicks;
            var priority = Config.BonePriority;

            // Reset state if target changed
            if (target.Base != _hitscanTargetBase)
            {
                _hitscanTargetBase = target.Base;
                _hitscanCurrentBone = default;
                _hitscanBoneLostTick = 0;
                _hitscanPendingUpgrade = default;
                _hitscanPendingStartTick = 0;
            }

            // Find highest-priority shootable bone
            Bones bestPrioBone = default;
            int bestPrioIndex = int.MaxValue;
            for (int i = 0; i < priority.Count; i++)
            {
                if (BoneMappings.IsBoneSet(hitscanMask, priority[i]))
                {
                    bestPrioBone = priority[i];
                    bestPrioIndex = i;
                    break;
                }
            }

            // Current bone's priority index (-1 from IndexOf → use MaxValue for "not in list")
            int currentIndex = _hitscanCurrentBone != default
                ? priority.IndexOf(_hitscanCurrentBone)
                : -1;
            if (currentIndex < 0) currentIndex = int.MaxValue;

            bool currentShootable = _hitscanCurrentBone != default
                && BoneMappings.IsBoneSet(hitscanMask, _hitscanCurrentBone);

            // Velocity-scaled grace: halve at 5+ m/s to prevent wall-tracking
            float targetSpeed = _lastWorldSpeed;
            float graceScale = targetSpeed > 5f ? 0.5f
                : targetSpeed > 2f ? 1f - 0.5f * ((targetSpeed - 2f) / 3f)
                : 1f;
            long holdTicks = (long)(Config.BoneHoldTimeMs * graceScale / 1000.0 * Stopwatch.Frequency);
            long confirmTicks = (long)(Config.BoneConfirmTimeMs / 1000.0 * Stopwatch.Frequency);

            // === Case 1: Current bone still shootable ===
            if (currentShootable)
            {
                _hitscanBoneLostTick = 0;

                // Check for higher-priority upgrade
                if (bestPrioIndex < currentIndex)
                {
                    if (_hitscanPendingUpgrade == bestPrioBone)
                    {
                        if ((now - _hitscanPendingStartTick) >= confirmTicks)
                        {
                            _hitscanCurrentBone = bestPrioBone;
                            _hitscanBoneLockTick = now;
                            _hitscanPendingUpgrade = default;
                        }
                    }
                    else
                    {
                        _hitscanPendingUpgrade = bestPrioBone;
                        _hitscanPendingStartTick = now;
                    }
                }
                else
                {
                    _hitscanPendingUpgrade = default;
                }

                return bones.TryGetValue(_hitscanCurrentBone, out boneTransform);
            }

            // === Case 2: Current bone lost visibility ===
            if (_hitscanCurrentBone != default)
            {
                if (_hitscanBoneLostTick == 0)
                    _hitscanBoneLostTick = now;

                if ((now - _hitscanBoneLostTick) < holdTicks)
                {
                    // Grace period — hold current bone position (transform still valid)
                    return bones.TryGetValue(_hitscanCurrentBone, out boneTransform);
                }

                // Grace expired — if pending upgrade is still shootable, take it immediately
                if (_hitscanPendingUpgrade != default
                    && BoneMappings.IsBoneSet(hitscanMask, _hitscanPendingUpgrade))
                {
                    _hitscanCurrentBone = _hitscanPendingUpgrade;
                    _hitscanBoneLockTick = now;
                    _hitscanBoneLostTick = 0;
                    _hitscanPendingUpgrade = default;
                    return bones.TryGetValue(_hitscanCurrentBone, out boneTransform);
                }
            }

            // === Case 3: Grace expired — switch to best priority bone ===
            _hitscanPendingUpgrade = default;
            if (bestPrioIndex < int.MaxValue)
            {
                _hitscanCurrentBone = bestPrioBone;
                _hitscanBoneLockTick = now;
                _hitscanBoneLostTick = 0;
                return bones.TryGetValue(_hitscanCurrentBone, out boneTransform);
            }

            // === Case 4: No priority bones — fallback to any shootable bone (FOV-capped) ===
            float bestFov = HITSCAN_FALLBACK_MAX_FOV;
            Bones fallbackBone = default;
            for (int i = 0; i < BoneMappings.BoneCount; i++)
            {
                if ((hitscanMask & (1u << i)) == 0) continue;
                var bone = BoneMappings.IndexToBone[i];
                if (!bones.TryGetValue(bone, out var bt)) continue;
                if (CameraManager.WorldToScreen(in bt.Position, out var sp))
                {
                    float fov = CameraManager.GetFovMagnitude(sp);
                    if (fov < bestFov)
                    {
                        bestFov = fov;
                        fallbackBone = bone;
                        boneTransform = bt;
                    }
                }
            }

            if (boneTransform != null)
            {
                _hitscanCurrentBone = fallbackBone;
                _hitscanBoneLockTick = now;
                _hitscanBoneLostTick = 0;
                return true;
            }

            return false;
        }

        private bool TryGetTargetBone(AbstractPlayer target, Bones targetBone, out UnityTransform boneTransform)
        {
            boneTransform = null;

            // Hitscan mode: use priority-based bone selection with visibility
            if (Config.HitscanEnabled && VisibilityManager.Instance?.IsReady == true)
            {
                return TryGetHitscanBone(target, out boneTransform);
            }

            // Use PlayerBones directly - this is the live data source
            // Skeleton.BoneTransforms references PlayerBones but Skeleton object can become stale
            var bones = target.PlayerBones;
            if (bones == null || bones.Count == 0)
                return false;

            // Closest-visible bone option - with caching to avoid expensive recalculation every frame
            if (targetBone == Bones.Closest)
            {
                long nowTicks = _cacheTimer.ElapsedTicks;

                // Check if we can use cached closest bone (same target and cache not expired)
                if (_cachedClosestBone != default &&
                    _cachedClosestBoneTarget == target &&
                    (nowTicks - _closestBoneCacheTimeTicks) < ClosestBoneCacheDurationTicks)
                {
                    // Use cached bone selection
                    if (bones.TryGetValue(_cachedClosestBone, out boneTransform))
                        return true;
                }

                // Cache miss or expired - recalculate closest bone
                float bestFov = float.MaxValue;
                Bones bestBone = default;

                foreach (var kvp in bones)
                {
                    if (CameraManager.WorldToScreen(in kvp.Value.Position, out var screenPos))
                    {
                        float fov = CameraManager.GetFovMagnitude(screenPos);
                        if (fov < bestFov)
                        {
                            bestFov = fov;
                            boneTransform = kvp.Value;
                            bestBone = kvp.Key;
                        }
                    }
                }

                if (boneTransform != null)
                {
                    // Cache the result
                    _cachedClosestBone = bestBone;
                    _cachedClosestBoneTarget = target;
                    _closestBoneCacheTimeTicks = nowTicks;
                    return true;
                }
            }

            // Specific bone
            if (bones.TryGetValue(targetBone, out boneTransform))
                return true;

            // Fallback to chest if configured bone not found
            return bones.TryGetValue(Bones.HumanSpine3, out boneTransform);
        }

        #endregion

        #region Aiming

        private void AimAtTarget(LocalPlayer localPlayer, AbstractPlayer target, Vector3? fireportPos)
        {
            // Check if bones exist
            if (target.PlayerBones == null || target.PlayerBones.Count == 0)
                return;

            var selectedBone = (App.Config.MemWrites.Enabled && App.Config.MemWrites.MemoryAimEnabled)
                ? App.Config.MemWrites.MemoryAimTargetBone
                : Config.TargetBone;

            // Get target bone position
            if (!TryGetTargetBone(target, selectedBone, out var boneTransform))
                return;

            Vector3 rawTargetPos = boneTransform.Position;

            // Cranium offset: HumanHead bone is anchored at the neck joint, not the
            // cranium center. Apply a fixed upward offset (~2.5cm) for head bones only.
            // This corrects the systematic downward bias observed in engagements.
            Bones activeBone = Config.HitscanEnabled && _hitscanCurrentBone != default
                ? _hitscanCurrentBone : selectedBone;
            if (activeBone == Bones.HumanHead)
                rawTargetPos.Y += 0.025f;

            Vector3 targetPos = rawTargetPos;

            // Always update world-space velocity tracking (needed for adaptive smoothing + prediction)
            string targetId = target.Base.ToString();
            UpdateTargetWorldVelocity(rawTargetPos, targetId);

            // Apply ballistics prediction if enabled
            if (Config.EnablePrediction && localPlayer.FirearmManager != null && fireportPos.HasValue && _lastBallistics?.IsAmmoValid == true)
            {
                targetPos = PredictHitPoint(localPlayer, target, fireportPos.Value, targetPos);
            }

            // Store aim positions for ESP debug markers (read by render thread)
            _lastRawBoneAimPos = rawTargetPos;
            _lastPredictedAimPos = targetPos;

            // Check if MemoryAim is enabled
            if (App.Config.MemWrites.Enabled && App.Config.MemWrites.MemoryAimEnabled)
            {
                LoneEftDmaRadar.Tarkov.Features.MemWrites.MemoryAim.Instance.SetTargetPosition(targetPos);
                DebugLogger.LogDebug($"[DeviceAimbot] Delegating to MemoryAim for target {target.Name}");
                return;
            }

            // Original DeviceAimbot device aiming (only if MemoryAim is disabled)
            if (!CameraManager.WorldToScreen(in targetPos, out var screenPos))
                return;

            // Stale-data detection: the aimbot runs at 850Hz but DMA camera data only
            // updates at game FPS (~60-120Hz). If screen position hasn't changed, the view
            // matrix is stale — sending moves on stale data causes accumulation overshoot.
            const float STALE_EPSILON = 0.01f;
            if (_hasLastW2SResult &&
                MathF.Abs(screenPos.X - _lastW2SResult.X) < STALE_EPSILON &&
                MathF.Abs(screenPos.Y - _lastW2SResult.Y) < STALE_EPSILON)
            {
                return; // Skip — no new camera data yet
            }
            _lastW2SResult = screenPos;
            _hasLastW2SResult = true;

            // Measure actual update rate from time between fresh (non-stale) frames
            long nowTicks = Stopwatch.GetTimestamp();
            if (_lastFreshFrameTicks != 0)
            {
                double elapsedSec = (nowTicks - _lastFreshFrameTicks) / (double)Stopwatch.Frequency;
                if (elapsedSec > 0.001 && elapsedSec < 0.5) // Sanity: 2Hz-1000Hz
                {
                    float instantRate = (float)(1.0 / elapsedSec);
                    // EMA smoothing to avoid jitter — converges in ~10 frames
                    _measuredUpdateRate = _measuredUpdateRate * 0.85f + instantRate * 0.15f;
                }
            }
            _lastFreshFrameTicks = nowTicks;

            // Also W2S the raw bone position for velocity tracking (prevents prediction offset
            // from being misinterpreted as target movement)
            Vector3 rawForW2S = rawTargetPos;
            if (!CameraManager.WorldToScreen(in rawForW2S, out var rawScreenPos))
                rawScreenPos = screenPos; // Fallback if raw W2S fails

            // Update target screen velocity using RAW bone position (Fix #2)
            // This prevents prediction offset changes from inflating velocity
            UpdateTargetScreenVelocity(rawScreenPos);

            // Calculate delta from center
            var center = CameraManager.ViewportCenter;
            float deltaX = screenPos.X - center.X;
            float deltaY = screenPos.Y - center.Y;

            // === In-flight move compensation (Smith Predictor) ===
            // Fixed render delay: ~1.2 game frames at 60 FPS ≈ 20ms.
            // Tighter window than 31.7ms to prevent stale pending accumulation.
            // At 60Hz fresh frames, queue depth is ~1-2 entries (was 4-5 at 31.7ms).
            const float RENDER_DELAY_SEC = 1.2f / 60f;
            long renderDelayTicks = (long)(RENDER_DELAY_SEC * Stopwatch.Frequency);

            while (_inflightMoves.Count > 0 && (nowTicks - _inflightMoves.Peek().ticks) > renderDelayTicks)
                _inflightMoves.Dequeue();

            // Cap queue to 3 entries. Prevents unbounded accumulation on DMA stalls.
            while (_inflightMoves.Count > 3)
                _inflightMoves.Dequeue();

            // Sum pending (not-yet-rendered) corrections
            float pendingX = 0f, pendingY = 0f;
            foreach (var (_, px, py) in _inflightMoves)
            {
                pendingX += px;
                pendingY += py;
            }

            // Direction-change detection: when the pending correction flips sign,
            // the queued moves are predicting motion in the wrong direction (target
            // reversed). Flush the stale entries to prevent overshoot on direction changes.
            if ((_prevPendingX != 0 && pendingX != 0 && MathF.Sign(pendingX) != MathF.Sign(_prevPendingX)) ||
                (_prevPendingY != 0 && pendingY != 0 && MathF.Sign(pendingY) != MathF.Sign(_prevPendingY)))
            {
                _inflightMoves.Clear();
                pendingX = 0f;
                pendingY = 0f;
            }
            _prevPendingX = pendingX;
            _prevPendingY = pendingY;

            // Effective delta: subtract moves the screen hasn't reflected yet.
            // This prevents sending redundant corrections during the render delay,
            // which is what causes the limit-cycle oscillation at high tick rates.
            deltaX -= pendingX;
            deltaY -= pendingY;

            // Check if player is aiming down sights
            bool isAiming = localPlayer.CheckIfADS();

            // Scoped engagement ramp-up: limit move magnitude for the first few frames
            // when scoped to prevent massive initial overshoot (scope sensitivity is very low,
            // so capped 127-unit bursts create a pendulum effect).
            bool isScoped = CameraManager.IsScoped;
            if (isScoped)
                _scopedEngageFrames++;
            else
                _scopedEngageFrames = 0;

            // Calculate smooth movement with fractional accumulation
            var (moveX, moveY) = CalculateSmoothMovement(deltaX, deltaY, isAiming);

            // Record this tick's device-scale move as pending
            _inflightMoves.Enqueue((nowTicks, _lastMoveDeviceX, _lastMoveDeviceY));

            // Apply movement
            if (moveX != 0 || moveY != 0)
            {
                SendDeviceMove(moveX, moveY);
            }

            // === Diagnostic logging ===
            if (AIM_DIAG_ENABLED && _diagWriter != null)
            {
                try
                {
                    string tid = target.Base.ToString();
                    if (tid != _diagLastTargetId)
                    {
                        _diagSeqId++;
                        _diagFrameId = 0;
                        _diagLastTargetId = tid;
                    }
                    _diagFrameId++;

                    var entry = new
                    {
                        seq = _diagSeqId,
                        frame = _diagFrameId,
                        ts = _cacheTimer.Elapsed.TotalMilliseconds,
                        target = target.Name,
                        bone = (Config.HitscanEnabled && _hitscanCurrentBone != default ? _hitscanCurrentBone : selectedBone).ToString(),
                        isADS = isAiming,
                        isScoped = CameraManager.IsScoped,
                        scopeSens = CameraManager.ScopeSensitivity,
                        worldPos = new { x = targetPos.X, y = targetPos.Y, z = targetPos.Z },
                        rawWorldPos = new { x = rawTargetPos.X, y = rawTargetPos.Y, z = rawTargetPos.Z },
                        screenPos = new { x = screenPos.X, y = screenPos.Y },
                        center = new { x = center.X, y = center.Y },
                        rawDelta = new { x = screenPos.X - center.X, y = screenPos.Y - center.Y },
                        adjDelta = new { x = deltaX, y = deltaY },
                        pending = new { x = pendingX, y = pendingY, n = _inflightMoves.Count },
                        distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY),
                        moveOutput = new { x = moveX, y = moveY },
                        accumBefore = new { x = _accumX + moveX, y = _accumY + moveY }, // before extraction
                        worldSpeed = _targetWorldVelocity.Length(),
                        screenVel = new { x = _targetScreenVelocity.X, y = _targetScreenVelocity.Y },
                        smoothing = Config.Smoothing,
                        pollingRate = Config.PollingRateHz,
                        measuredRate = _measuredUpdateRate,
                        ergoSens = _cachedAimingSens,
                        maxErgoSens = _maxAimingSens,
                        overweightMult = _cachedOverweightAimMult,
                        ergoScale = _lastErgoScale,
                        aspect = (float)CameraManager.Viewport.Width / CameraManager.Viewport.Height,
                        viewport = new { w = CameraManager.Viewport.Width, h = CameraManager.Viewport.Height },
                    };

                    _diagWriter.WriteLine(JsonSerializer.Serialize(entry));
                }
                catch { /* never crash the aimbot for diagnostics */ }
            }
        }

        /// <summary>
        /// Calculates smooth mouse movement with fractional accumulation.
        /// Returns (deviceX, deviceY) with ergo compensation applied.
        /// </summary>
        private (int dx, int dy) CalculateSmoothMovement(float deltaX, float deltaY, bool isAiming)
        {
            float distance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);

            // Skip frame if data is corrupted (NaN, Infinity, or impossibly large delta)
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance > MAX_SCREEN_DELTA)
            {
                _accumX = 0;
                _accumY = 0;
                return (0, 0);
            }

            // Fixed deadzone at all zoom levels
            if (distance < DEADZONE_PIXELS)
            {
                _accumX = 0;
                _accumY = 0;
                return (0, 0);
            }

            // Rate compensation based on ACTUAL update rate (measured from fresh frames)
            // With stale-data detection, we only act on fresh camera data (~60-120Hz),
            // not the configured polling rate (850Hz). Using measured rate prevents
            // over-scaling that makes smoothing feel sluggish.
            float actualRate = Math.Clamp(_measuredUpdateRate, 30f, 500f);
            float rateScale = actualRate / SMOOTHING_REFERENCE_RATE;

            // Smoothing 1 = instant (no rate scaling)
            // Smoothing 2+ = apply rate scaling for consistent feel at high poll rates
            float baseSmoothing = Config.Smoothing <= 1f
                ? 1f
                : Config.Smoothing * rateScale;

            // Adaptive smoothing based on target world-space speed (distance-invariant)
            float worldSpeed = _targetWorldVelocity.Length();
            float effectiveSmoothing = GetAdaptiveSmoothing(baseSmoothing, worldSpeed);

            // Apply hipfire speed reduction when not aiming down sights
            // This prevents massive overshoots in hipfire where screen delta is much larger
            float speedFactor = isAiming ? 1.0f : Config.HipfireSpeedFactor;

            // === PD Control ===
            // P term: Proportional to error
            float Kp = 1.0f / effectiveSmoothing;

            // D term: Adds target velocity to "lead" the aim
            // Velocity is in pixels/sec, so divide by actual update rate to get per-tick contribution
            float Kd = (Config.DerivativeGain * 0.01f) / actualRate;

            // === Aim Settling (soft dead zone) ===
            // When very close to target, reduce correction strength to prevent micro-oscillation
            // Uses linear falloff with a floor to ensure convergence at all ranges
            float errorDistance = MathF.Sqrt(deltaX * deltaX + deltaY * deltaY);
            const float settleRadius = 2.0f;  // Pixels - start settling within this radius
            const float settleFloor = 0.15f;   // Minimum settle factor - ensures we always make SOME correction
            float settleFactor = 1.0f;

            if (errorDistance < settleRadius)
            {
                // Linear falloff with floor: corrections reduce near target but never fully stop
                float t = errorDistance / settleRadius;
                settleFactor = settleFloor + t * (1.0f - settleFloor);
            }

            // Control output: P term + D term (velocity feed-forward), with settling
            // This is in pixel-scale — matches the screen delta units.
            float moveX = (deltaX * Kp * settleFactor + _targetScreenVelocity.X * Kd) * speedFactor;
            float moveY = (deltaY * Kp * settleFactor + _targetScreenVelocity.Y * Kd) * speedFactor;

            // === Ergo/overweight compensation ===
            // Inflates mouse counts to compensate for the game's reduced sensitivity
            // with lower-ergo weapons / overweight / scope sensitivity.
            // Linear compensation (capped at 4x to prevent ±127 saturation).
            float ergoScale = 1.0f;
            if (isAiming && Config.ErgoCompensation)
            {
                float maxSens = _maxAimingSens;
                if (maxSens > 0.01f)
                {
                    float ergoRatio = _cachedAimingSens / maxSens;
                    float compensation = ergoRatio * _cachedOverweightAimMult;
                    if (compensation > 0.01f && compensation < 0.99f)
                    {
                        ergoScale = Math.Min(1.0f / compensation, 4.0f);
                    }
                }
            }

            // Convert pixel-space move to device-space (inflate for ergo)
            float deviceX = moveX * ergoScale;
            float deviceY = moveY * ergoScale;

            // Scoped ramp-up: limit move magnitude for the first 8 fresh frames when scoped.
            // Prevents the initial overshoot pendulum caused by capped 127-unit bursts
            // interacting with low scope sensitivity.
            if (_scopedEngageFrames > 0 && _scopedEngageFrames <= 8)
            {
                // Ramp from 30% to 100% over 8 frames
                float ramp = 0.3f + 0.7f * (_scopedEngageFrames / 8f);
                deviceX *= ramp;
                deviceY *= ramp;
            }

            // Clamp to device limits (-127 to 127)
            deviceX = Math.Clamp(deviceX, -MAX_MOVE_PER_TICK, MAX_MOVE_PER_TICK);
            deviceY = Math.Clamp(deviceY, -MAX_MOVE_PER_TICK, MAX_MOVE_PER_TICK);

            // Save PIXEL-SPACE expected displacement for Smith Predictor.
            // The predictor subtracts pending values from pixel-space delta (line ~1111),
            // so pending must be in pixel-space too. If the device was cap-limited,
            // back-calculate actual pixel displacement to prevent over-subtraction.
            _lastMoveDeviceX = (ergoScale > 1.001f) ? deviceX / ergoScale : moveX;
            _lastMoveDeviceY = (ergoScale > 1.001f) ? deviceY / ergoScale : moveY;
            _lastErgoScale = ergoScale;

            // Accumulate fractional parts (prevents rounding loss)
            _accumX += deviceX;
            _accumY += deviceY;

            // Extract integer part for this frame
            int resultX = (int)_accumX;
            int resultY = (int)_accumY;

            // Keep only fractional remainder for next frame
            _accumX -= resultX;
            _accumY -= resultY;

            return (resultX, resultY);
        }

        /// <summary>
        /// Updates target screen-space velocity for PD control.
        /// Call this each frame with the current target screen position.
        /// </summary>
        private void UpdateTargetScreenVelocity(Vector2 currentScreenPos)
        {
            long now = _cacheTimer.ElapsedTicks;

            if (_hasLastScreenPos)
            {
                // Calculate time delta in seconds
                float dt = (now - _lastVelocityUpdateTicks) / (float)Stopwatch.Frequency;

                if (dt > 0.001f && dt < 0.5f) // Valid delta time range
                {
                    Vector2 posDelta = currentScreenPos - _lastTargetScreenPos;
                    Vector2 newVelocity = posDelta / dt;

                    // Smooth velocity with exponential moving average (reduces jitter)
                    const float velocitySmoothing = 0.3f;
                    _targetScreenVelocity = Vector2.Lerp(_targetScreenVelocity, newVelocity, velocitySmoothing);
                }
            }

            _lastTargetScreenPos = currentScreenPos;
            _lastVelocityUpdateTicks = now;
            _hasLastScreenPos = true;
        }

        /// <summary>
        /// Resets velocity tracking when target changes.
        /// </summary>
        private void ResetVelocityTracking()
        {
            _hasLastScreenPos = false;
            _targetScreenVelocity = Vector2.Zero;
            _hasVelocitySample = false;
            _targetWorldVelocity = Vector3.Zero;
        }

        /// <summary>
        /// Updates world-space velocity tracking by calculating velocity from position changes.
        /// Works for ALL target types (AI scavs, PMCs, etc.) unlike memory-read velocity.
        /// Uses accumulated position delta over ~20ms for stable velocity estimation.
        /// </summary>
        private void UpdateTargetWorldVelocity(Vector3 currentWorldPos, string targetId)
        {
            long now = _cacheTimer.ElapsedTicks;

            // Reset if target changed
            if (_lastTrackedTargetId != targetId)
            {
                _hasVelocitySample = false;
                _targetWorldVelocity = Vector3.Zero;
                _lastTrackedTargetId = targetId;
            }

            if (!_hasVelocitySample)
            {
                // First frame - just store position, can't calculate velocity yet
                _velocitySamplePos = currentWorldPos;
                _velocitySampleTicks = now;
                _hasVelocitySample = true;
                return;
            }

            // Calculate time since last velocity sample
            float dt = (now - _velocitySampleTicks) / (float)Stopwatch.Frequency;

            // Use shorter interval for first reading (faster response), longer for subsequent (stability)
            bool isFirstReading = _targetWorldVelocity == Vector3.Zero;
            float sampleInterval = isFirstReading ? 0.005f : 0.025f; // 5ms first, 25ms after

            if (dt >= sampleInterval)
            {
                Vector3 posDelta = currentWorldPos - _velocitySamplePos;
                float posDeltaMag = posDelta.Length();

                // Minimum movement threshold - filters out rotation/jitter
                // Walking at 2m/s for 25ms = 5cm movement, so 3cm is safe threshold
                float minMovement = isFirstReading ? 0.02f : 0.03f; // 2cm first, 3cm after

                if (posDeltaMag > minMovement)
                {
                    Vector3 newVelocity = posDelta / dt;
                    float newSpeed = newVelocity.Length();

                    // Minimum speed to count as moving (filters rotation jitter)
                    // Walking = ~2m/s, rotation jitter = ~0.3-0.8m/s
                    const float minSpeedThreshold = 1.2f;

                    // Clamp to realistic max speed (15 m/s ~= sprinting speed)
                    const float maxRealisticSpeed = 15f;

                    if (newSpeed >= minSpeedThreshold && newSpeed <= maxRealisticSpeed)
                    {
                        if (isFirstReading)
                        {
                            // First reading - initialize immediately
                            _targetWorldVelocity = newVelocity;
                        }
                        else
                        {
                            // Smooth subsequent readings (lower = more stable)
                            const float velocitySmoothing = 0.18f;
                            _targetWorldVelocity = Vector3.Lerp(_targetWorldVelocity, newVelocity, velocitySmoothing);
                        }

                        _lastWorldSpeed = _targetWorldVelocity.Length();

                        // Update sample position for next calculation
                        _velocitySamplePos = currentWorldPos;
                        _velocitySampleTicks = now;
                    }
                    else if (newSpeed > maxRealisticSpeed)
                    {
                        // Spike - ignore this sample but don't update position
                    }
                    else
                    {
                        // Below threshold - decay velocity
                        _targetWorldVelocity = Vector3.Lerp(_targetWorldVelocity, Vector3.Zero, 0.15f);
                        _lastWorldSpeed = _targetWorldVelocity.Length();
                        _velocitySamplePos = currentWorldPos;
                        _velocitySampleTicks = now;
                    }
                }
                else if (dt > 0.050f)
                {
                    // Target stationary for 50ms+ - decay velocity toward zero
                    _targetWorldVelocity = Vector3.Lerp(_targetWorldVelocity, Vector3.Zero, 0.2f);
                    _lastWorldSpeed = _targetWorldVelocity.Length();
                    _velocitySamplePos = currentWorldPos;
                    _velocitySampleTicks = now;
                }
            }
        }

        /// <summary>
        /// Calculates adaptive smoothing based on target world-space speed (m/s).
        /// Uses world speed so the result is distance-invariant — a target walking at
        /// 2 m/s gets the same treatment at 5m and 200m, fixing close-range sluggishness.
        /// </summary>
        private float GetAdaptiveSmoothing(float baseSmoothing, float worldSpeed)
        {
            if (!Config.AdaptiveSmoothing)
                return baseSmoothing;

            // World-space thresholds (m/s) — distance-invariant
            const float stationaryThreshold = 1.5f;  // standing / turning
            const float fastThreshold = 5.0f;        // sprinting

            // Monotonically decreasing: faster targets get lower smoothing (higher Kp)
            // so the aimbot can keep up. The D term provides stability for fast movers.
            const float stationaryMult = 0.50f;  // Stationary: 50% smoothing (fast lock)
            const float fastMult = 0.35f;        // Fast movers: 35% smoothing (aggressive tracking)

            if (worldSpeed < stationaryThreshold)
                return baseSmoothing * stationaryMult;
            if (worldSpeed > fastThreshold)
                return baseSmoothing * fastMult;

            // Linear interpolation
            float t = (worldSpeed - stationaryThreshold) / (fastThreshold - stationaryThreshold);
            return baseSmoothing * (stationaryMult + t * (fastMult - stationaryMult));
        }

        private Vector3 PredictHitPoint(LocalPlayer localPlayer, AbstractPlayer target, Vector3 fireportPos, Vector3 targetPos)
        {
            try
            {
                // Get ballistics info from weapon
                var ballistics = GetBallisticsInfo(localPlayer);
                if (ballistics == null || !ballistics.IsAmmoValid)
                    return targetPos;

                // Get target velocity - prefer calculated velocity as it works for all targets
                // Memory-read velocity only works for ObservedPlayer types
                Vector3 targetVelocity = _targetWorldVelocity;

                // Try memory read for ObservedPlayer (may be more accurate for PMCs/player scavs)
                if (target is ObservedPlayer)
                {
                    try
                    {
                        Vector3 memVelocity = _memory.ReadValue<Vector3>(
                            target.MovementContext + Offsets.ObservedMovementController.Velocity,
                            false);

                        // Use memory velocity if it's valid and faster than calculated
                        // This handles cases where memory is more responsive
                        if (memVelocity.Length() > targetVelocity.Length() * 0.5f)
                        {
                            targetVelocity = memVelocity;
                        }
                    }
                    catch
                    {
                        // Memory read failed, stick with calculated velocity
                    }
                }

                // Calculate actual distance to target
                float actualDistance = Vector3.Distance(fireportPos, targetPos);

                // Default zeroing distance (most weapons are zeroed at 50m)
                const float zeroingDistance = 50f;

                // Apply prediction
                Vector3 predictedPos = targetPos;

                // Run full ballistics simulation to target (used for both drop and lead)
                var fullSim = BallisticsSimulation.Run(ref fireportPos, ref targetPos, ballistics);

                // Calculate drop compensation using correct physics:
                // When zeroed at 50m, the sight line is angled so bullet hits aim point at 50m.
                // Actual compensation needed = fullDrop - zeroingDrop
                if (actualDistance > zeroingDistance)
                {
                    // Simulate drop at zeroing distance
                    Vector3 directionToTarget = Vector3.Normalize(targetPos - fireportPos);
                    Vector3 zeroingTargetPos = fireportPos + directionToTarget * zeroingDistance;
                    var zeroingSim = BallisticsSimulation.Run(ref fireportPos, ref zeroingTargetPos, ballistics);

                    // True drop compensation = total drop minus what zeroing already accounts for
                    float dropCompensation = fullSim.DropCompensation - zeroingSim.DropCompensation;
                    predictedPos.Y += dropCompensation * Config.DropCompensationFactor;
                }
                // Within zeroing distance: bullet is still rising to meet sight line, minimal compensation needed

                // Add lead for moving targets (if enabled)
                if (Config.EnableLeadPrediction && targetVelocity != Vector3.Zero)
                {
                    float speed = targetVelocity.Length();
                    if (speed > 1.0f) // Only predict if actually moving (filters rotation)
                    {
                        // Ballistic lead: based on bullet travel time (reuse fullSim)
                        Vector3 ballisticLead = targetVelocity * fullSim.TravelTime * Config.LeadCompensationFactor;

                        // DMA latency lead: constant extra lead based on estimated read delay
                        // This is the main compensation for DMA latency at all ranges
                        float latencySeconds = Config.DmaLatencyMs / 1000f;
                        Vector3 latencyLead = targetVelocity * latencySeconds;

                        Vector3 totalLead = ballisticLead + latencyLead;
                        predictedPos += totalLead;
                    }
                }

                return predictedPos;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DeviceAimbot] Prediction failed: {ex}");
                return targetPos;
            }
        }

        /// <summary>
        /// Reads the overweight aiming multiplier from PWA.
        /// This value represents how much EFT reduces turn speed when overweight/low ergo.
        /// Cached with 100ms TTL to avoid per-tick memory reads.
        /// </summary>
        private void UpdateErgoCompensation(LocalPlayer localPlayer)
        {
            long nowTicks = _cacheTimer.ElapsedTicks;
            if ((nowTicks - _ergoReadTimeTicks) < ErgoCacheDurationTicks)
                return; // Cache still valid

            _ergoReadTimeTicks = nowTicks;

            try
            {
                ulong pwa = localPlayer.PWA;
                if (!MemDMA.IsValidVirtualAddress(pwa))
                {
                    _cachedOverweightAimMult = 1.0f;
                    _cachedAimingSens = 1.0f;
                    return;
                }

                // Read overweight multiplier (carry weight penalty)
                float mult = _memory.ReadValue<float>(pwa + Offsets.ProceduralWeaponAnimation._overweightAimingMultiplier, false);
                if (float.IsNormal(mult) && mult is > 0f and <= 2f)
                    _cachedOverweightAimMult = mult;
                else
                    _cachedOverweightAimMult = 1.0f;

                // Read per-weapon aiming sensitivity (ergo-derived) via PWA -> FirearmController
                // IMPORTANT: Only update when NOT scoped — _aimingSens includes zoom sensitivity
                // and we only want the ergo component (1x/hipfire value).
                if (!CameraManager.IsScoped)
                {
                    ulong fc = _memory.ReadPtr(pwa + Offsets.ProceduralWeaponAnimation._firearmController, false);
                    if (MemDMA.IsValidVirtualAddress(fc))
                    {
                        float aimSens = _memory.ReadValue<float>(fc + Offsets.FirearmController._aimingSens, false);
                        float prevSens = _cachedAimingSens;
                        if (float.IsNormal(aimSens) && aimSens is > 0f and <= 2f)
                        {
                            _cachedAimingSens = aimSens;
                            if (aimSens > _maxAimingSens)
                                _maxAimingSens = aimSens;
                            if (MathF.Abs(aimSens - prevSens) > 0.01f)
                            {
                                float ergoRatio = aimSens / _maxAimingSens;
                                DebugLogger.LogDebug($"[DeviceAimbot] AimingSens: {aimSens:F3} | Max: {_maxAimingSens:F3} | ErgoRatio: {ergoRatio:F3} | OverweightMult: {_cachedOverweightAimMult:F3}");
                            }
                        }
                        else
                            _cachedAimingSens = 1.0f;
                    }
                }
            }
            catch
            {
                _cachedOverweightAimMult = 1.0f;
                _cachedAimingSens = 1.0f;
            }
        }

        private BallisticsInfo GetBallisticsInfo(LocalPlayer localPlayer)
        {
            try
            {
                var snapshot = localPlayer.FirearmManager?.CurrentSnapshot;
                if (snapshot == null || !snapshot.IsWeapon || snapshot.ItemAddr == 0)
                    return null;

                ulong handsAddr = snapshot.ItemAddr;
                long nowTicks = _cacheTimer.ElapsedTicks;

                // === BALLISTICS CACHING (thread-safe) ===
                // Check if we can use cached ballistics (same weapon and cache not expired)
                lock (_ballisticsLock)
                {
                    if (_lastBallistics != null &&
                        handsAddr == _cachedBallisticsHandsAddr &&
                        (nowTicks - _ballisticsCacheTimeTicks) < BallisticsCacheDurationTicks)
                    {
                        return _lastBallistics; // Return cached result
                    }
                }

                ulong itemBase = handsAddr;

                // Validate itemBase before proceeding
                if (!MemDMA.IsValidVirtualAddress(itemBase))
                    return CreateFallbackBallistics();

                ulong itemTemplate;
                try
                {
                    itemTemplate = _memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
                }
                catch
                {
                    return CreateFallbackBallistics();
                }

                if (!MemDMA.IsValidVirtualAddress(itemTemplate))
                    return CreateFallbackBallistics();

                // Get ammo template - wrap in try/catch since weapon may be empty
                ulong ammoTemplate;
                try
                {
                    ammoTemplate = FirearmManager.MagazineManager.GetAmmoTemplateFromWeapon(itemBase);
                }
                catch
                {
                    // Weapon has no ammo loaded
                    return CreateFallbackBallistics();
                }

                if (ammoTemplate == 0 || !MemDMA.IsValidVirtualAddress(ammoTemplate))
                    return CreateFallbackBallistics();

                // Read ballistics data
                var ballistics = new BallisticsInfo
                {
                    BulletMassGrams = _memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletMassGram, false),
                    BulletDiameterMillimeters = _memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletDiameterMilimeters, false),
                    BallisticCoefficient = _memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BallisticCoeficient, false)
                };

                // Calculate bullet velocity with mods
                float baseSpeed = _memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.InitialSpeed, false);
                float velMod = _memory.ReadValue<float>(itemTemplate + Offsets.WeaponTemplate.Velocity, false);

                // Recursively add attachment velocity modifiers
                AddAttachmentVelocity(itemBase, ref velMod);

                ballistics.BulletSpeed = baseSpeed * (1f + (velMod / 100f));

                if (!ballistics.IsAmmoValid)
                    return CreateFallbackBallistics();

                // Cache the result (thread-safe atomic update)
                lock (_ballisticsLock)
                {
                    _lastBallistics = ballistics;
                    _cachedBallisticsHandsAddr = handsAddr;
                    _ballisticsCacheTimeTicks = nowTicks;
                }

                return ballistics;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DeviceAimbot] Failed to get ballistics: {ex.Message}");
                return CreateFallbackBallistics();
            }
        }

        private static BallisticsInfo CreateFallbackBallistics()
        {
            // Generic 7.62-ish defaults to keep prediction running when reads fail.
            return new BallisticsInfo
            {
                BulletMassGrams = 8.0f,
                BulletDiameterMillimeters = 7.6f,
                BallisticCoefficient = 0.35f,
                BulletSpeed = 800f
            };
        }

        private void AddAttachmentVelocity(ulong itemBase, ref float velocityModifier)
        {
            try
            {
                var slotsPtr = _memory.ReadPtr(itemBase + Offsets.LootItemMod.Slots, false);
                using var slots = UnityArray<ulong>.Create(slotsPtr, true);

                if (slots.Count > MAX_ATTACHMENT_SLOTS) // Sanity check
                    return;

                foreach (var slot in slots.Span)
                {
                    var containedItem = _memory.ReadPtr(slot + Offsets.Slot.ContainedItem, false);
                    if (containedItem == 0)
                        continue;

                    var itemTemplate = _memory.ReadPtr(containedItem + Offsets.LootItem.Template, false);
                    velocityModifier += _memory.ReadValue<float>(itemTemplate + Offsets.ModTemplate.Velocity, false);

                    // Recurse
                    AddAttachmentVelocity(containedItem, ref velocityModifier);
                }
            }
            catch
            {
                // Ignore errors in attachment recursion
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Set to true while the aim-key is held (from hotkey/ui).
        /// Thread-safe: marked volatile for cross-thread visibility.
        /// </summary>
        private volatile bool _isEngaged;
        public bool IsEngaged
        {
            get => _isEngaged;
            set
            {
                if (_isEngaged == value)
                    return;

                _isEngaged = value;

                // Keep MemoryAim in sync with hotkey state (if enabled).
                try
                {
                    if (App.Config.MemWrites.Enabled && App.Config.MemWrites.MemoryAimEnabled)
                        LoneEftDmaRadar.Tarkov.Features.MemWrites.MemoryAim.Instance.SetEngaged(value);
                }
                catch { /* best-effort sync */ }

                if (!value)
                {
                    // When key is released, drop any lock/target immediately.
                    ResetTarget();
                }
            }
        }

        /// <summary>
        /// Returns the currently locked target (if any). May be null.
        /// </summary>
        public AbstractPlayer LockedTarget => _lockedTarget;

        /// <summary>
        /// The bone currently targeted by the hitscan system.
        /// Read by ESP render thread — atomic uint enum read, no lock needed.
        /// </summary>
        public Bones ActiveHitscanBone => _hitscanCurrentBone;

        /// <summary>
        /// Last predicted world-space aim point (bone + bullet drop + lead).
        /// Read by ESP for debug marker overlay.
        /// </summary>
        public Vector3 PredictedAimPos => _lastPredictedAimPos;

        /// <summary>
        /// Last raw bone world position (before prediction offsets).
        /// Read by ESP for debug marker overlay.
        /// </summary>
        public Vector3 RawBoneAimPos => _lastRawBoneAimPos;

        private readonly struct TargetCandidate
        {
            public AbstractPlayer Player { get; init; }
            public float FOVDistance { get; init; }
            public float WorldDistance { get; init; }
        }

        #region Debug Overlay

        /// <summary>
        /// Draws debug information on the ESP overlay.
        /// </summary>
        public void DrawDebug(SKCanvas canvas, LocalPlayer localPlayer)
        {
            try
            {
                // Reuse list to avoid allocations
                _debugLines.Clear();
                var lines = _debugLines;

                // Header
                lines.Add("=== DeviceAimbot AIMBOT DEBUG ===");
                lines.Add($"Status:       {_debugStatus}");
                lines.Add($"Key State:    {(IsEngaged ? "ENGAGED" : "Idle")}");
                bool devConnected = Device.connected || DeviceNetController.Connected;
                lines.Add($"Device:       {(devConnected ? "Connected" : "Disconnected")}");
                lines.Add($"Enabled:      {(Config.Enabled ? "TRUE" : "FALSE")}");
                lines.Add($"InRaid:       {_memory.InRaid}");
                lines.Add("");

                // LocalPlayer / Firearm / Fireport info
                if (localPlayer != null)
                {
                    lines.Add($"LocalPlayer:  OK @ {localPlayer.Position}");
                    lines.Add($"FirearmMgr:   {(localPlayer.FirearmManager != null ? "OK" : "NULL")}");
                }
                else
                {
                    lines.Add("LocalPlayer:  NULL");
                    lines.Add("FirearmMgr:   n/a");
                }

                if (_hasLastFireport)
                {
                    lines.Add($"FireportPos:  {_lastFireportPos}");
                }
                else
                {
                    lines.Add("FireportPos:  (no valid fireport)");
                }

                lines.Add("");

                // Per-stage filter stats
                lines.Add("Players (this scan):");
                lines.Add($"  Total:      {_dbgTotalPlayers}");
                lines.Add($"  Type OK:    {_dbgEligibleType}");
                lines.Add($"  InDist:     {_dbgWithinDistance}");
                lines.Add($"  Skeleton:   {_dbgHaveSkeleton}");
                lines.Add($"  W2S OK:     {_dbgW2SPassed}");
                lines.Add($"  Candidates: {_lastCandidateCount}");
                lines.Add("");

                // Target info / FOV diagnostics
                lines.Add($"Config FOV:   {Config.FOV:F1} (pixels from screen center)");
                lines.Add($"TargetValid:  {_lastIsTargetValid}");

                if (_lockedTarget != null && localPlayer != null)
                {
                    var dist = Vector3.Distance(localPlayer.Position, _lockedTarget.Position);
                    lines.Add($"Locked Target: {_lockedTarget.Name} [{_lockedTarget.Type}]");
                    lines.Add($"  Distance:   {dist:F1}m");
                    if (!float.IsNaN(_lastLockedTargetFov) && !float.IsInfinity(_lastLockedTargetFov))
                        lines.Add($"  FOVDist:    {_lastLockedTargetFov:F1}");
                    else
                        lines.Add("  FOVDist:    n/a");

                    lines.Add($"  TargetBone: {Config.TargetBone}");

                    if (_lockedTarget is ObservedPlayer obs)
                    {
                        lines.Add($"  Health:     {obs.HealthStatus}");
                    }
                }
                else
                {
                    lines.Add("Locked Target: None");
                }

                lines.Add("");

                // Ballistics Info
                if (_lastBallistics != null && _lastBallistics.IsAmmoValid)
                {
                    lines.Add("Ballistics:");
                    lines.Add($"  BulletSpeed: {(_lastBallistics.BulletSpeed):F1} m/s");
                    lines.Add($"  Mass:        {_lastBallistics.BulletMassGrams:F2} g");
                    lines.Add($"  BC:          {_lastBallistics.BallisticCoefficient:F3}");
                    lines.Add($"  Prediction:  {(Config.EnablePrediction ? "ON" : "OFF")}");
                }
                else
                {
                    lines.Add("Ballistics:   No / invalid ammo");
                }

                lines.Add("");

                // Settings / filters
                lines.Add("Settings:");
                lines.Add($"  MaxDist:    {Config.MaxDistance:F0}m");
                lines.Add($"  Targeting:  {Config.Targeting}");
                lines.Add("");
                lines.Add("Target Filters:");
                lines.Add($"  PMC:    {Config.TargetPMC}   PScav: {Config.TargetPlayerScav}");
                lines.Add($"  AI:     {Config.TargetAIScav}   Boss:  {Config.TargetBoss}   Raider: {Config.TargetRaider}");
                
                lines.Add("");
                lines.Add("No Recoil:");
                lines.Add($"  Enabled:    {(App.Config.MemWrites.NoRecoilEnabled ? "ON" : "OFF")}");
                if (App.Config.MemWrites.NoRecoilEnabled)
                {
                    lines.Add($"  Recoil:     {App.Config.MemWrites.NoRecoilAmount:F0}%");
                    lines.Add($"  Sway:       {App.Config.MemWrites.NoSwayAmount:F0}%");
                }
                float x = 10;
                float y = 30;
                float lineHeight = 18;

                // Use cached fonts and paints to avoid allocations
                using var font = DebugFont;
                using var fontBold = DebugFontBold;

                // Background size
                float maxWidth = 0;
                foreach (var line in lines)
                {
                    float width = font.MeasureText(line);
                    if (width > maxWidth) maxWidth = width;
                }

                canvas.DrawRect(x - 5, y - 20, maxWidth + 25, lines.Count * lineHeight + 20, DebugBgPaint);

                // Text with shadow (fake bold / shading)
                foreach (var line in lines)
                {
                    bool isHeader = line.StartsWith("===") ||
                                    line == "Ballistics:" ||
                                    line == "Settings:" ||
                                    line == "Target Filters:" ||
                                    line == "Players (this scan):";

                    var paint = isHeader ? DebugHeaderPaint : DebugTextPaint;
                    var lineFont = isHeader ? fontBold : font;

                    canvas.DrawText(line, x + 1.5f, y + 1.5f, SKTextAlign.Left, fontBold, DebugShadowPaint);
                    canvas.DrawText(line, x, y, SKTextAlign.Left, lineFont, paint);
                    y += lineHeight;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DeviceAimbot] DrawDebug error: {ex}");
            }
        }

        /// <summary>
        /// Returns the latest debug snapshot for UI/ESP overlays.
        /// </summary>
        public DeviceAimbotDebugSnapshot GetDebugSnapshot()
        {
            try
            {
                var localPlayer = _memory.LocalPlayer;
                float? distanceToTarget = null;
                if (localPlayer != null && _lockedTarget != null)
                    distanceToTarget = Vector3.Distance(localPlayer.Position, _lockedTarget.Position);

                return new DeviceAimbotDebugSnapshot
                {
                    Status = _debugStatus,
                    KeyEngaged = IsEngaged,
                    Enabled = Config.Enabled,
                    DeviceConnected = Device.connected || DeviceNetController.Connected,
                    InRaid = _memory.InRaid,
                    CandidateTotal = _dbgTotalPlayers,
                    CandidateTypeOk = _dbgEligibleType,
                    CandidateInDistance = _dbgWithinDistance,
                    CandidateWithSkeleton = _dbgHaveSkeleton,
                    CandidateW2S = _dbgW2SPassed,
                    CandidateCount = _lastCandidateCount,
                    ConfigFov = Config.FOV,
                    ConfigMaxDistance = Config.MaxDistance,
                    TargetValid = _lastIsTargetValid,
                    LockedTargetName = _lockedTarget?.Name,
                    LockedTargetType = _lockedTarget?.Type,
                    LockedTargetDistance = distanceToTarget,
                    LockedTargetFov = _lastLockedTargetFov,
                    TargetBone = Config.HitscanEnabled && _hitscanCurrentBone != default
                        ? _hitscanCurrentBone : Config.TargetBone,
                    HasFireport = _hasLastFireport,
                    FireportPosition = _hasLastFireport ? _lastFireportPos : (Vector3?)null,
                    BallisticsValid = _lastBallistics?.IsAmmoValid ?? false,
                    BulletSpeed = _lastBallistics?.BulletSpeed,
                    PredictionEnabled = Config.EnablePrediction,
                    TargetingMode = Config.Targeting
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DeviceAimbot] GetDebugSnapshot error: {ex}");
                return null;
            }
        }
        #endregion
        #endregion
        
    }

    public sealed class DeviceAimbotDebugSnapshot
    {
        public string Status { get; set; }
        public bool KeyEngaged { get; set; }
        public bool Enabled { get; set; }
        public bool DeviceConnected { get; set; }
        public bool InRaid { get; set; }

        public int CandidateTotal { get; set; }
        public int CandidateTypeOk { get; set; }
        public int CandidateInDistance { get; set; }
        public int CandidateWithSkeleton { get; set; }
        public int CandidateW2S { get; set; }
        public int CandidateCount { get; set; }

        public float ConfigFov { get; set; }
        public float ConfigMaxDistance { get; set; }
        public DeviceAimbotConfig.TargetingMode TargetingMode { get; set; }
        public Bones TargetBone { get; set; }
        public bool PredictionEnabled { get; set; }

        public bool TargetValid { get; set; }
        public string LockedTargetName { get; set; }
        public PlayerType? LockedTargetType { get; set; }
        public float? LockedTargetDistance { get; set; }
        public float LockedTargetFov { get; set; }

        public bool HasFireport { get; set; }
        public Vector3? FireportPosition { get; set; }

        public bool BallisticsValid { get; set; }
        public float? BulletSpeed { get; set; }
    }
    public static class Vector3Extensions
    {
        public static Vector3 CalculateDirection(this Vector3 source, Vector3 destination)
        {
            Vector3 dir = destination - source;
            return Vector3.Normalize(dir);
        }
    }

    public static class QuaternionExtensions
    {
        public static Vector3 InverseTransformDirection(this Quaternion rotation, Vector3 direction)
        {
            return Quaternion.Conjugate(rotation).Multiply(direction);
        }

        public static Vector3 Multiply(this Quaternion q, Vector3 v)
        {
            float x = q.X * 2.0f;
            float y = q.Y * 2.0f;
            float z = q.Z * 2.0f;
            float xx = q.X * x;
            float yy = q.Y * y;
            float zz = q.Z * z;
            float xy = q.X * y;
            float xz = q.X * z;
            float yz = q.Y * z;
            float wx = q.W * x;
            float wy = q.W * y;
            float wz = q.W * z;
        
            Vector3 res;
            res.X = (1.0f - (yy + zz)) * v.X + (xy - wz) * v.Y + (xz + wy) * v.Z;
            res.Y = (xy + wz) * v.X + (1.0f - (xx + zz)) * v.Y + (yz - wx) * v.Z;
            res.Z = (xz - wy) * v.X + (yz + wx) * v.Y + (1.0f - (xx + yy)) * v.Z;
        
            return res;
        }
    }  
}
