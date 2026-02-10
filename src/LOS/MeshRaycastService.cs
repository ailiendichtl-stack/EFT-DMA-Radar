using System.Numerics;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Manages dual raycast scenes (LOS + Ballistic) and handles map loading lifecycle.
    ///
    /// Design: LoadMapAsync discovers files only. EnsureScenes handles all scene
    /// loading/unloading based on current mode. No separate mode tracking —
    /// purely reactive: compare what's loaded vs what's needed, act on differences.
    /// </summary>
    public sealed class MeshRaycastService : IDisposable
    {
        public enum LoadState { NotLoaded, Discovering, Ready, Error }

        private volatile RaycastScene _losScene;
        private volatile RaycastScene _ballisticScene;
        private volatile LoadState _state = LoadState.NotLoaded;
        private volatile string _statusText = "No mesh data";
        private volatile string _loadedMapName;
        private CancellationTokenSource _loadCts;
        private readonly object _loadLock = new();

        // File paths (set during discovery, immutable until next map change)
        private string _losTmeshPath;
        private string _losObjPath;
        private string _balTmeshPath;
        private string _balObjPath;
        private bool _losAvailable;
        private bool _balAvailable;

        public LoadState State => _state;
        public string StatusText => _statusText;
        public string LoadedMap => _loadedMapName;
        public int LosTriangles => _losScene?.TriangleCount ?? 0;
        public int BallisticTriangles => _ballisticScene?.TriangleCount ?? 0;

        /// <summary>
        /// True when file discovery is complete and scenes can be loaded.
        /// </summary>
        public bool IsReady => _state == LoadState.Ready;

        /// <summary>
        /// True when at least one scene is loaded and raycasts will produce real results.
        /// </summary>
        public bool HasLoadedScenes => _losScene != null || _ballisticScene != null;

        /// <summary>
        /// Maps DMA internal LocationId to MapData folder name.
        /// </summary>
        private static readonly Dictionary<string, string> MapIdToFolder = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bigmap"] = "Customs",
            ["factory4_day"] = "Factory",
            ["factory4_night"] = "Factory",
            ["woods"] = "Woods",
            ["shoreline"] = "Shoreline",
            ["interchange"] = "Interchange",
            ["laboratory"] = "Labs",
            ["rezervbase"] = "Reserve",
            ["lighthouse"] = "Lighthouse",
            ["tarkovstreets"] = "Streets",
            ["Sandbox"] = "Ground_Zero",
            ["Sandbox_high"] = "Ground_Zero",
            ["Sandbox_start"] = "Ground_Zero",
            ["Labyrinth"] = "Labyrinth",
        };

        /// <summary>
        /// Base path for map data files.
        /// </summary>
        public static string MapDataPath => Path.Combine(AppContext.BaseDirectory, "MapData");

        /// <summary>
        /// Get list of maps that have mesh data available.
        /// Checks for LOS/ and/or Ballistics/ subfolders containing .tmesh or .obj files.
        /// </summary>
        public static List<string> GetAvailableMaps()
        {
            var maps = new List<string>();
            var basePath = MapDataPath;
            if (!Directory.Exists(basePath)) return maps;

            foreach (var dir in Directory.GetDirectories(basePath))
            {
                var dirName = Path.GetFileName(dir);
                if (SubfolderHasMesh(Path.Combine(dir, "LOS")) ||
                    SubfolderHasMesh(Path.Combine(dir, "Ballistics")))
                {
                    maps.Add(dirName);
                }
            }
            return maps;
        }

        /// <summary>
        /// Resolve a DMA MapID to the folder name.
        /// </summary>
        public static string ResolveMapFolder(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return null;
            return MapIdToFolder.TryGetValue(mapId, out var folder) ? folder : null;
        }

        #region Map Discovery

        /// <summary>
        /// Discover mesh files for a map. Does NOT load any scenes —
        /// call EnsureScenes() after this to load what the current mode needs.
        /// </summary>
        public Task LoadMapAsync(string mapId)
        {
            var folderName = ResolveMapFolder(mapId);
            if (folderName == null)
            {
                _statusText = $"Unknown map: {mapId}";
                _state = LoadState.Error;
                return Task.CompletedTask;
            }

            // Already discovered this map?
            if (_state == LoadState.Ready && _loadedMapName == folderName)
                return Task.CompletedTask;

            lock (_loadLock)
            {
                _loadCts?.Cancel();
                _loadCts = new CancellationTokenSource();
                var ct = _loadCts.Token;

                // Dispose previous scenes to free LOH arrays immediately
                _losScene?.Dispose();
                _losScene = null;
                _ballisticScene?.Dispose();
                _ballisticScene = null;

                _state = LoadState.Discovering;
                _statusText = $"Discovering {folderName}...";

                return Task.Run(() => DiscoverFiles(folderName, ct), ct);
            }
        }

        private void DiscoverFiles(string folderName, CancellationToken ct)
        {
            try
            {
                var mapDir = Path.Combine(MapDataPath, folderName);
                if (!Directory.Exists(mapDir))
                {
                    _statusText = $"No data for {folderName}";
                    _state = LoadState.Error;
                    DebugLogger.LogInfo($"[MeshRaycast] Map directory not found: {mapDir}");
                    return;
                }

                ct.ThrowIfCancellationRequested();

                // Discover mesh files in LOS/ and Ballistics/ subfolders
                var losDir = Path.Combine(mapDir, "LOS");
                var balDir = Path.Combine(mapDir, "Ballistics");

                _losTmeshPath = FindMeshInFolder(losDir, "*.tmesh");
                _losObjPath = FindMeshInFolder(losDir, "*.obj");
                _balTmeshPath = FindMeshInFolder(balDir, "*.tmesh");
                _balObjPath = FindMeshInFolder(balDir, "*.obj");

                _losAvailable = _losTmeshPath != null || _losObjPath != null;
                _balAvailable = _balTmeshPath != null || _balObjPath != null;

                if (!_losAvailable && !_balAvailable)
                {
                    _statusText = $"No mesh files in {folderName}/LOS or {folderName}/Ballistics";
                    _state = LoadState.Error;
                    return;
                }

                _loadedMapName = folderName;
                _state = LoadState.Ready;
                _statusText = $"{folderName}: ready, awaiting scene load...";

                DebugLogger.LogInfo($"[MeshRaycast] {folderName} discovered — LOS:{_losAvailable} BAL:{_balAvailable}");
            }
            catch (OperationCanceledException)
            {
                _statusText = "Load cancelled";
                _state = LoadState.NotLoaded;
            }
            catch (Exception ex)
            {
                _statusText = $"Discovery error: {ex.Message}";
                _state = LoadState.Error;
                DebugLogger.LogInfo($"[MeshRaycast] Discovery error: {ex}");
            }
        }

        #endregion

        #region Scene Management

        /// <summary>
        /// Ensure exactly the scenes needed for the current mode are loaded.
        /// Loads missing scenes, unloads unneeded scenes, reclaims memory.
        /// Call from worker thread each frame — cheap when nothing changes (just null checks).
        /// </summary>
        public void EnsureScenes(bool needsLos, bool needsBallistic)
        {
            if (_state != LoadState.Ready) return;

            bool loaded = false;
            bool unloaded = false;

            // --- Load what's needed but missing ---

            if (needsLos && _losScene == null && _losAvailable)
            {
                try
                {
                    void Progress(string msg) { _statusText = msg; DebugLogger.LogInfo($"[MeshRaycast] {msg}"); }
                    Progress($"Loading {_loadedMapName} LOS...");
                    _losScene = RaycastScene.Load(_losTmeshPath, _losObjPath, $"{_loadedMapName}_LOS", Progress);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[MeshRaycast] Failed to load LOS scene: {ex.Message}");
                    _losAvailable = false; // Don't retry on failure
                }
            }

            if (needsBallistic && _ballisticScene == null && _balAvailable)
            {
                try
                {
                    void Progress(string msg) { _statusText = msg; DebugLogger.LogInfo($"[MeshRaycast] {msg}"); }
                    Progress($"Loading {_loadedMapName} Ballistics...");
                    _ballisticScene = RaycastScene.Load(_balTmeshPath, _balObjPath, $"{_loadedMapName}_Ballistics", Progress);
                    loaded = true;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[MeshRaycast] Failed to load Ballistic scene: {ex.Message}");
                    _balAvailable = false; // Don't retry on failure
                }
            }

            // --- Unload what's loaded but not needed ---

            if (!needsLos && _losScene != null)
            {
                _losScene.Dispose();
                _losScene = null;
                unloaded = true;
                DebugLogger.LogInfo("[MeshRaycast] Unloaded LOS scene");
            }

            if (!needsBallistic && _ballisticScene != null)
            {
                _ballisticScene.Dispose();
                _ballisticScene = null;
                unloaded = true;
                DebugLogger.LogInfo("[MeshRaycast] Unloaded Ballistic scene");
            }

            // --- Update status and reclaim memory ---

            if (loaded || unloaded)
            {
                var los = _losScene?.TriangleCount ?? 0;
                var bal = _ballisticScene?.TriangleCount ?? 0;
                _statusText = $"{_loadedMapName}: LOS {los:N0}t, Ballistic {bal:N0}t";
            }

            if (unloaded)
            {
                GC.Collect(2, GCCollectionMode.Forced, false);
                GC.WaitForPendingFinalizers();
            }
        }

        #endregion

        #region Raycast Queries

        /// <summary>
        /// Check eye line-of-sight. Uses LOS scene (includes foliage) or Ballistic scene if noFoliage.
        /// </summary>
        public bool HasEyeLOS(Vector3 from, Vector3 to, bool noFoliage)
        {
            var scene = noFoliage ? (_ballisticScene ?? _losScene) : (_losScene ?? _ballisticScene);
            return scene?.HasLineOfSight(from, to) ?? true;
        }

        /// <summary>
        /// Check ballistic line-of-sight. Always uses Ballistic scene (no foliage).
        /// </summary>
        public bool HasBallisticLOS(Vector3 from, Vector3 to)
        {
            var scene = _ballisticScene ?? _losScene;
            return scene?.HasLineOfSight(from, to) ?? true;
        }

        #endregion

        #region Lifecycle

        /// <summary>
        /// Unload all scenes and reset state.
        /// </summary>
        public void Unload()
        {
            lock (_loadLock)
            {
                _loadCts?.Cancel();
                _losScene?.Dispose();
                _losScene = null;
                _ballisticScene?.Dispose();
                _ballisticScene = null;
                _loadedMapName = null;
                _losAvailable = false;
                _balAvailable = false;
                _state = LoadState.NotLoaded;
                _statusText = "No mesh data";
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = null;
            _losScene?.Dispose();
            _losScene = null;
            _ballisticScene?.Dispose();
            _ballisticScene = null;
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Find the first file matching a pattern in a subfolder. Returns null if folder missing or no match.
        /// </summary>
        private static string FindMeshInFolder(string folder, string pattern)
        {
            if (!Directory.Exists(folder)) return null;
            var files = Directory.GetFiles(folder, pattern);
            return files.Length > 0 ? files[0] : null;
        }

        /// <summary>
        /// Check if a subfolder contains any mesh files (.tmesh or .obj).
        /// </summary>
        private static bool SubfolderHasMesh(string folder)
        {
            if (!Directory.Exists(folder)) return false;
            return Directory.GetFiles(folder, "*.tmesh").Length > 0 ||
                   Directory.GetFiles(folder, "*.obj").Length > 0;
        }

        /// <summary>
        /// All known map folder names for directory scaffolding.
        /// </summary>
        private static readonly string[] KnownMaps =
        [
            "Customs", "Factory", "Woods", "Shoreline", "Interchange",
            "Labs", "Reserve", "Lighthouse", "Streets",
            "Ground_Zero", "Labyrinth"
        ];

        /// <summary>
        /// Create the full MapData directory tree: MapData/{Map}/LOS + Ballistics for all known maps.
        /// </summary>
        public static void EnsureMapDataDirectory()
        {
            try
            {
                var basePath = MapDataPath;
                foreach (var map in KnownMaps)
                {
                    Directory.CreateDirectory(Path.Combine(basePath, map, "LOS"));
                    Directory.CreateDirectory(Path.Combine(basePath, map, "Ballistics"));
                }
                DebugLogger.LogInfo($"[MeshRaycast] MapData directory structure verified: {basePath}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[MeshRaycast] Failed to create MapData directory: {ex.Message}");
            }
        }

        #endregion
    }
}
