using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Per-player visibility data from the SHM LOS system.
    /// </summary>
    public struct PlayerVisibility
    {
        public uint VisibleMask;
        public uint HitscanMask;
    }

    /// <summary>
    /// Manages the T5 worker thread that writes enemy positions to SHM
    /// and reads back per-bone visibility masks from the Twilight SPT plugin.
    /// </summary>
    public sealed class VisibilityManager : IDisposable
    {
        #region Singleton

        private static volatile VisibilityManager _instance;
        private static readonly object _lock = new();

        public static VisibilityManager Instance => _instance;

        public static void Start()
        {
            lock (_lock)
            {
                if (_instance != null) return;
                _instance = new VisibilityManager();
                _instance.StartWorker();
                DebugLogger.LogInfo("[VisibilityManager] Started");
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                if (_instance == null) return;
                _instance.Dispose();
                _instance = null;
                DebugLogger.LogInfo("[VisibilityManager] Stopped");
            }
        }

        #endregion

        #region Fields

        private SharedMemoryClient _shm;
        private readonly ConcurrentDictionary<ulong, PlayerVisibility> _visibility = new();
        private Thread _workerThread;
        private volatile bool _running;
        private bool _disposed;

        // Stats
        private int _enemiesTracked;
        private float _latencyMs;
        private int _framesPerSecond;
        private int _frameCount;
        private readonly Stopwatch _fpsSw = new();

        #endregion

        #region Properties (thread-safe reads from ESP)

        public bool IsConnected => _shm?.IsConnected ?? false;
        public int EnemiesTracked => _enemiesTracked;
        public float LatencyMs => _latencyMs;
        public int FramesPerSecond => _framesPerSecond;

        #endregion

        #region Query API (called from ESP render thread)

        /// <summary>
        /// Returns true if any bone of this player is visible (eye LOS).
        /// Returns true (assume visible) if no visibility data exists for this player.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsAnyBoneVisible(AbstractPlayer player)
        {
            if (!_visibility.TryGetValue(player.Base, out var v))
                return true; // No data = assume visible
            return v.VisibleMask != 0;
        }

        /// <summary>
        /// Returns true if a specific bone is visible (eye LOS).
        /// Returns true for unmapped bones or missing data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBoneVisible(AbstractPlayer player, Bones bone)
        {
            if (!_visibility.TryGetValue(player.Base, out var v))
                return true;
            if (!SharedMemoryClient.RadarBoneToShmId.TryGetValue(bone, out int shmId))
                return true; // Unmapped bone = assume visible
            return (v.VisibleMask & (1u << shmId)) != 0;
        }

        /// <summary>
        /// Returns true if a specific bone is shootable (fireport hitscan LOS).
        /// Returns true for unmapped bones or missing data.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBoneShootable(AbstractPlayer player, Bones bone)
        {
            if (!_visibility.TryGetValue(player.Base, out var v))
                return true;
            if (!SharedMemoryClient.RadarBoneToShmId.TryGetValue(bone, out int shmId))
                return true;
            return (v.HitscanMask & (1u << shmId)) != 0;
        }

        /// <summary>
        /// Get full visibility data for a player, or null if not tracked.
        /// </summary>
        public PlayerVisibility? GetVisibility(AbstractPlayer player)
        {
            if (_visibility.TryGetValue(player.Base, out var v))
                return v;
            return null;
        }

        #endregion

        #region Worker Thread

        private void StartWorker()
        {
            _shm = new SharedMemoryClient();
            _running = true;
            _fpsSw.Start();

            _workerThread = new Thread(WorkerLoop)
            {
                Name = "T5_VisibilityLOS",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _workerThread.Start();
        }

        private void WorkerLoop()
        {
            DebugLogger.LogInfo("[T5_VisibilityLOS] Worker thread started");

            while (_running)
            {
                try
                {
                    // Try to connect if not connected
                    if (!_shm.IsConnected)
                    {
                        if (!_shm.TryConnect())
                        {
                            Thread.Sleep(2000); // Retry every 2 seconds
                            continue;
                        }
                    }

                    ProcessFrame();
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[T5_VisibilityLOS] Error: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }

            DebugLogger.LogInfo("[T5_VisibilityLOS] Worker thread exiting");
        }

        private void ProcessFrame()
        {
            var config = App.Config.Visibility;
            if (!config.Enabled || !config.ShmEnabled)
            {
                Thread.Sleep(100); // Check again soon
                return;
            }

            // Get player data
            var players = Memory.Players;
            var localPlayer = Memory.LocalPlayer;

            if (players == null || localPlayer == null)
            {
                Thread.Sleep(50);
                return;
            }

            // Build enemy list (alive, active, not local, not exfil'd)
            var enemies = new List<(AbstractPlayer Player, int Index)>();
            int shmIndex = 0;
            foreach (var player in players)
            {
                if (player == localPlayer || !player.IsActive || !player.IsAlive || player.HasExfild)
                    continue;

                if (shmIndex >= 64) break; // SHM limit

                enemies.Add((player, shmIndex));
                shmIndex++;
            }

            if (enemies.Count == 0)
            {
                _enemiesTracked = 0;
                Thread.Sleep(50);
                return;
            }

            // Write header - use head bone for eye position, fall back to foot + 1.5m offset
            var headPos = localPlayer.GetBonePos(Bones.HumanHead);
            if (headPos == Vector3.Zero || headPos == localPlayer.Position)
                headPos = new Vector3(localPlayer.Position.X, localPlayer.Position.Y + 1.5f, localPlayer.Position.Z);
            _shm.WriteHeader(enemies.Count, config.Flags, headPos, headPos);

            // Write each enemy
            foreach (var (player, idx) in enemies)
            {
                _shm.WriteEnemy(idx, player.Position, config.BoneMask);
            }

            // Submit and wait for results
            var latencySw = Stopwatch.StartNew();
            _shm.SubmitFrame();

            if (_shm.WaitForResults(16))
            {
                _latencyMs = (float)latencySw.Elapsed.TotalMilliseconds;

                // Read results and update cache
                foreach (var (player, idx) in enemies)
                {
                    var result = _shm.ReadEnemyResult(idx);
                    _visibility[player.Base] = new PlayerVisibility
                    {
                        VisibleMask = result.VisibleMask,
                        HitscanMask = result.HitscanMask
                    };
                }

                _enemiesTracked = enemies.Count;
            }
            else
            {
                _latencyMs = -1; // Timeout indicator
            }

            // FPS counter
            _frameCount++;
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _framesPerSecond = _frameCount;
                _frameCount = 0;
                _fpsSw.Restart();

                // Clean stale entries (players that left)
                CleanStaleEntries(enemies);
            }
        }

        private void CleanStaleEntries(List<(AbstractPlayer Player, int Index)> currentEnemies)
        {
            var activeKeys = new HashSet<ulong>();
            foreach (var (player, _) in currentEnemies)
                activeKeys.Add(player.Base);

            foreach (var key in _visibility.Keys)
            {
                if (!activeKeys.Contains(key))
                    _visibility.TryRemove(key, out _);
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            _workerThread?.Join(2000);
            _shm?.Dispose();
            _visibility.Clear();
        }

        #endregion
    }
}
