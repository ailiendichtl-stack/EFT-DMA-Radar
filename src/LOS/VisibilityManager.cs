using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Per-player visibility result cached from the T5 worker.
    /// </summary>
    public struct PlayerVisibility
    {
        public uint VisibleMask;   // Bit per bone: eye LOS clear
        public uint HitscanMask;   // Bit per bone: ballistic LOS clear
        public long Timestamp;     // Stopwatch ticks when computed
    }

    /// <summary>
    /// T5 worker thread that performs local mesh raycasting for per-bone visibility.
    /// Public API is identical to the SHM-based prototype — ESP coloring code unchanged.
    /// </summary>
    public sealed class VisibilityManager : IDisposable
    {
        #region Singleton

        private static volatile VisibilityManager _instance;
        private static readonly object _singletonLock = new();

        public static VisibilityManager Instance => _instance;

        public static void Start()
        {
            lock (_singletonLock)
            {
                if (_instance != null) return;
                _instance = new VisibilityManager();
                _instance.StartWorker();
                DebugLogger.LogInfo("[VisibilityManager] Started");
            }
        }

        public static void Stop()
        {
            lock (_singletonLock)
            {
                if (_instance == null) return;
                _instance.Dispose();
                _instance = null;
                DebugLogger.LogInfo("[VisibilityManager] Stopped");
            }
        }

        #endregion

        #region Fields

        private Thread _workerThread;
        private volatile bool _running;
        private bool _disposed;

        private readonly MeshRaycastService _raycast = new();
        private readonly ConcurrentDictionary<ulong, PlayerVisibility> _visibility = new();
        private readonly HashSet<ulong> _activeIds = new(); // Reused per frame to avoid allocation
        private string _lastMapId;

        // Stats
        private int _frameCount;
        private int _enemiesTracked;
        private long _lastStatsTick;
        private float _latencyMs;
        private int _fps;

        #endregion

        #region Properties

        public bool IsReady => _raycast.IsReady;
        public string MeshStatus => _raycast.StatusText;
        public int EnemiesTracked => _enemiesTracked;
        public float LatencyMs => _latencyMs;
        public int FramesPerSecond => _fps;

        #endregion

        #region Public Query API (called from ESP render thread)

        /// <summary>
        /// Check if any bone of a player is eye-visible.
        /// </summary>
        public bool IsAnyBoneVisible(AbstractPlayer player)
        {
            if (!_visibility.TryGetValue(player.Base, out var vis))
                return false;
            return vis.VisibleMask != 0;
        }

        /// <summary>
        /// Check if a specific bone is eye-visible.
        /// </summary>
        public bool IsBoneVisible(AbstractPlayer player, Bones bone)
        {
            if (!_visibility.TryGetValue(player.Base, out var vis))
                return false;
            return BoneMappings.IsBoneSet(vis.VisibleMask, bone);
        }

        /// <summary>
        /// Check if a specific bone is hitscan-reachable (ballistic LOS).
        /// </summary>
        public bool IsBoneShootable(AbstractPlayer player, Bones bone)
        {
            if (!_visibility.TryGetValue(player.Base, out var vis))
                return false;
            return BoneMappings.IsBoneSet(vis.HitscanMask, bone);
        }

        /// <summary>
        /// Get full visibility data for a player, or null if not tracked.
        /// </summary>
        public PlayerVisibility? GetVisibility(AbstractPlayer player)
        {
            if (_visibility.TryGetValue(player.Base, out var vis))
                return vis;
            return null;
        }

        #endregion

        #region Worker Thread

        private void StartWorker()
        {
            MeshRaycastService.EnsureMapDataDirectory();

            _running = true;
            _lastStatsTick = Stopwatch.GetTimestamp();

            _workerThread = new Thread(WorkerLoop)
            {
                Name = "T5_VisibilityLOS",
                Priority = ThreadPriority.AboveNormal,
                IsBackground = true,
            };
            _workerThread.Start();
        }

        private void WorkerLoop()
        {
            DebugLogger.LogInfo("[T5] Worker thread started");

            while (_running)
            {
                try
                {
                    if (!Memory.InRaid)
                    {
                        // Unload mesh scenes when not in raid to free ~1.5-3GB of LOH memory
                        if (_lastMapId != null)
                        {
                            _raycast.Unload();
                            _lastMapId = null;
                            GC.Collect(2, GCCollectionMode.Forced, true);
                            GC.WaitForPendingFinalizers();
                            DebugLogger.LogInfo("[T5] Raid ended — mesh data unloaded");
                        }
                        _visibility.Clear();
                        _enemiesTracked = 0;
                        Thread.Sleep(100);
                        continue;
                    }

                    var config = App.Config.Visibility;
                    if (!config.Enabled)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    // Auto-detect map change → discover files
                    var currentMap = Memory.MapID;
                    if (currentMap != null && currentMap != _lastMapId)
                    {
                        _lastMapId = currentMap;
                        _visibility.Clear();
                        DebugLogger.LogInfo($"[T5] Map changed to {currentMap}");
                        _ = _raycast.LoadMapAsync(currentMap);
                    }

                    // Wait for file discovery to complete
                    if (!_raycast.IsReady)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    // Ensure correct scenes are loaded for current mode
                    // First call after discovery loads scenes; subsequent calls are cheap null checks
                    bool needsLos = !config.NoFoliage;
                    bool needsBal = config.DualCheck || config.NoFoliage;
                    _raycast.EnsureScenes(needsLos, needsBal);

                    // Wait for at least one scene to be loaded
                    if (!_raycast.HasLoadedScenes)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    ProcessFrame(config);
                    UpdateStats();

                    // Throttle: ~60-120 fps target
                    Thread.Sleep(8);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[T5] Worker error: {ex.Message}");
                    Thread.Sleep(100);
                }
            }

            DebugLogger.LogInfo("[T5] Worker thread exiting");
        }

        private void ProcessFrame(VisibilityConfig config)
        {
            var sw = Stopwatch.StartNew();

            var localPlayer = Memory.LocalPlayer;
            var players = Memory.Players;
            if (localPlayer == null || players == null)
                return;

            var eyePos = localPlayer.GetBonePos(Bones.HumanHead);
            if (eyePos == Vector3.Zero) return;

            bool dualCheck = config.DualCheck;
            bool noFoliage = config.NoFoliage;
            uint boneMask = config.BoneMask;

            int tracked = 0;
            _activeIds.Clear();

            foreach (var player in players)
            {
                if (player == localPlayer) continue;
                if (!player.IsActive || !player.IsAlive || player.HasExfild) continue;
                if (tracked >= 64) break;

                _activeIds.Add(player.Base);
                uint visMask = 0;
                uint hitMask = 0;

                for (int i = 0; i < BoneMappings.BoneCount; i++)
                {
                    // Skip bones not in the configured mask
                    if ((boneMask & (1u << i)) == 0) continue;

                    var bone = BoneMappings.IndexToBone[i];
                    var bonePos = player.GetBonePos(bone);

                    // Skip invalid positions
                    if (bonePos == Vector3.Zero) continue;

                    // Eye LOS check
                    if (_raycast.HasEyeLOS(eyePos, bonePos, noFoliage))
                        visMask |= (1u << i);

                    // Ballistic check
                    if (dualCheck)
                    {
                        if (_raycast.HasBallisticLOS(eyePos, bonePos))
                            hitMask |= (1u << i);
                    }
                }

                _visibility[player.Base] = new PlayerVisibility
                {
                    VisibleMask = visMask,
                    HitscanMask = dualCheck ? hitMask : visMask,
                    Timestamp = Stopwatch.GetTimestamp(),
                };

                tracked++;
            }

            // Clean stale entries
            foreach (var key in _visibility.Keys)
            {
                if (!_activeIds.Contains(key))
                    _visibility.TryRemove(key, out _);
            }

            sw.Stop();
            _latencyMs = (float)sw.Elapsed.TotalMilliseconds;
            _enemiesTracked = tracked;
            _frameCount++;
        }

        private void UpdateStats()
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = (now - _lastStatsTick) / (double)Stopwatch.Frequency;
            if (elapsed >= 1.0)
            {
                _fps = (int)(_frameCount / elapsed);
                _frameCount = 0;
                _lastStatsTick = now;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _workerThread?.Join(3000);
            _raycast.Dispose();
            _visibility.Clear();

            // Force Gen2 collection to reclaim LOH arrays immediately
            GC.Collect(2, GCCollectionMode.Forced, true);
            GC.WaitForPendingFinalizers();
        }

        #endregion
    }
}
