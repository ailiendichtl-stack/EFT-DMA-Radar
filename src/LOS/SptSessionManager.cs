using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    public enum SptState
    {
        Idle,
        StartingServer,
        ServerReady,
        StartingLauncher,
        WaitingForGame,
        GameReady,
        RaidSynced,
        Error
    }

    public class SptProfile
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Side { get; set; }
        public int Level { get; set; }
        public string DisplayName => $"{Username} (Lv{Level} {Side})";
    }

    /// <summary>
    /// Manages the full SPT lifecycle: server start, launcher start, raid sync.
    /// Uses SPT.Launcher.exe with auto-login for game start.
    /// Subscribes to MemDMA events for automatic operation.
    /// </summary>
    public sealed class SptSessionManager : IDisposable
    {
        #region Singleton

        private static volatile SptSessionManager _instance;
        private static readonly object _singletonLock = new();

        public static SptSessionManager Instance => _instance;

        public static void Initialize()
        {
            lock (_singletonLock)
            {
                if (_instance != null) return;
                _instance = new SptSessionManager();
                _instance.Start();
                DebugLogger.LogInfo("[SptSession] Initialized");
            }
        }

        public static void Shutdown()
        {
            lock (_singletonLock)
            {
                if (_instance == null) return;
                _instance.Dispose();
                _instance = null;
                DebugLogger.LogInfo("[SptSession] Shutdown");
            }
        }

        #endregion

        #region Fields

        private Thread _workerThread;
        private volatile bool _running;
        private bool _disposed;

        private Process _serverProcess;
        private Process _launcherProcess;
        private TcpCommandClient _tcp;
        private HttpClient _httpClient;

        private volatile SptState _state = SptState.Idle;
        private volatile string _statusText = "Idle";
        private volatile string _lastError = "";
        private DateTime _stateEnteredAt;

        // Event flags set by MemDMA events, consumed by worker thread
        private volatile bool _dmaProcessFound;
        private volatile bool _dmaProcessLost;
        private volatile bool _dmaRaidStarted;
        private volatile bool _dmaRaidStopped;

        private bool _weStartedServer;
        private bool _weStartedLauncher;
        private string _lastSyncedMap;
        private volatile string _detectedMapId;

        /// <summary>
        /// Maps DMA internal LocationId → SPT plugin location Name (from get_available_maps).
        /// </summary>
        private static readonly Dictionary<string, string> SptMapNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bigmap"] = "Customs",
            ["factory4_day"] = "Factory",
            ["factory4_night"] = "Factory",
            ["interchange"] = "Interchange",
            ["laboratory"] = "Laboratory",
            ["lighthouse"] = "Lighthouse",
            ["rezervbase"] = "ReserveBase",
            ["shoreline"] = "Shoreline",
            ["tarkovstreets"] = "Streets of Tarkov",
            ["woods"] = "Woods",
            ["Sandbox"] = "Sandbox",
            ["Sandbox_high"] = "Sandbox",
            ["Sandbox_start"] = "Sandbox",
            ["Labyrinth"] = "Labyrinth",
        };

        #endregion

        #region Properties

        public SptState State => _state;
        public string StatusText => _statusText;
        public string LastError => _lastError;

        #endregion

        #region Lifecycle

        private void Start()
        {
            // Create HTTP client that ignores SSL cert errors (SPT uses self-signed)
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            _tcp = new TcpCommandClient();

            // Subscribe to MemDMA events
            MemDMA.ProcessStarted += OnDmaProcessStarted;
            MemDMA.ProcessStopped += OnDmaProcessStopped;
            MemDMA.MapDetected += OnDmaMapDetected;
            MemDMA.RaidStarted += OnDmaRaidStarted;
            MemDMA.RaidStopped += OnDmaRaidStopped;

            _running = true;
            _workerThread = new Thread(WorkerLoop)
            {
                Name = "SptSessionManager",
                IsBackground = true
            };
            _workerThread.Start();

            // If DMA is already connected, trigger the flow
            if (Memory.Ready)
                _dmaProcessFound = true;
        }

        private void OnDmaProcessStarted(object sender, EventArgs e) => _dmaProcessFound = true;
        private void OnDmaProcessStopped(object sender, EventArgs e) => _dmaProcessLost = true;
        private void OnDmaMapDetected(object sender, MapDetectedEventArgs e)
        {
            _detectedMapId = e.MapID;
            _dmaRaidStarted = true;
            DebugLogger.LogInfo($"[SptSession] MapDetected event: map={e.MapID}, state={_state}");
        }
        private void OnDmaRaidStarted(object sender, EventArgs e)
        {
            _dmaRaidStarted = true;
            DebugLogger.LogInfo($"[SptSession] DMA RaidStarted event (state={_state})");
        }
        private void OnDmaRaidStopped(object sender, EventArgs e) => _dmaRaidStopped = true;

        #endregion

        #region Worker Thread

        private void WorkerLoop()
        {
            DebugLogger.LogInfo("[SptSession] Worker thread started");

            while (_running)
            {
                try
                {
                    // Check for DMA process lost → reset to Idle
                    if (_dmaProcessLost)
                    {
                        _dmaProcessLost = false;
                        _dmaProcessFound = false;
                        _dmaRaidStarted = false;
                        _dmaRaidStopped = false;
                        TransitionTo(SptState.Idle, "DMA process lost");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Handle raid events regardless of state machine position
                    // (covers manual server/launcher start outside our control)
                    HandleRaidEvents();

                    switch (_state)
                    {
                        case SptState.Idle:
                            HandleIdle();
                            break;
                        case SptState.StartingServer:
                            HandleStartingServer();
                            break;
                        case SptState.ServerReady:
                            HandleServerReady();
                            break;
                        case SptState.StartingLauncher:
                            HandleStartingLauncher();
                            break;
                        case SptState.WaitingForGame:
                            HandleWaitingForGame();
                            break;
                        case SptState.GameReady:
                            HandleGameReady();
                            break;
                        case SptState.RaidSynced:
                            HandleRaidSynced();
                            break;
                        case SptState.Error:
                            HandleError();
                            break;
                    }

                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[SptSession] Worker error: {ex.Message}");
                    TransitionTo(SptState.Error, ex.Message);
                    Thread.Sleep(2000);
                }
            }

            DebugLogger.LogInfo("[SptSession] Worker thread exiting");
        }

        private void HandleIdle()
        {
            _statusText = "Idle - waiting for DMA";

            if (_dmaProcessFound)
            {
                _dmaProcessFound = false;

                var config = App.Config.Visibility;
                if (!config.AutoStartEnabled)
                {
                    _statusText = "Auto-start disabled";
                    return;
                }

                if (string.IsNullOrEmpty(config.SptPath))
                {
                    _statusText = "Not configured (set Fika path)";
                    return;
                }

                if (string.IsNullOrEmpty(config.SptProfileId))
                {
                    _statusText = "No profile selected";
                    return;
                }

                // Check if game is already running with plugin
                if (_tcp.TryConnect(2000) && _tcp.Ping())
                {
                    DebugLogger.LogInfo("[SptSession] Game already running with plugin");
                    TransitionTo(SptState.GameReady, "Game already running");
                    return;
                }

                // Check if server is already running
                var serverRunning = IsServerProcessRunning();
                DebugLogger.LogInfo($"[SptSession] Server process check: {serverRunning}");
                if (serverRunning)
                {
                    TransitionTo(SptState.ServerReady, "Server already running");
                }
                else
                {
                    StartServer();
                }
            }
        }

        private void HandleStartingServer()
        {
            // Check if our process handle says it exited
            if (_serverProcess != null && _serverProcess.HasExited)
            {
                DebugLogger.LogInfo($"[SptSession] Server process exited with code {_serverProcess.ExitCode}");
                TransitionTo(SptState.Error, $"Server exited (code {_serverProcess.ExitCode})");
                return;
            }

            // Also verify by process name (in case handle is stale)
            if (_serverProcess == null && !IsServerProcessRunning())
            {
                TransitionTo(SptState.Error, "Server process not found");
                return;
            }

            // Check if server is responding via HTTP
            if (IsServerReady())
            {
                DebugLogger.LogInfo("[SptSession] Server HTTP responding on :6969");
                TransitionTo(SptState.ServerReady, "Server ready");
                return;
            }

            // Timeout check
            if (ElapsedInState() > TimeSpan.FromSeconds(60))
            {
                TransitionTo(SptState.Error, "Server start timeout (60s)");
                return;
            }

            _statusText = $"Starting server... ({ElapsedInState().Seconds}s)";
        }

        private void HandleServerReady()
        {
            // Check if game is already running with plugin
            if (_tcp.TryConnect(2000) && _tcp.Ping())
            {
                DebugLogger.LogInfo("[SptSession] Game already running with plugin");
                TransitionTo(SptState.GameReady, "Game already running");
                return;
            }

            // Check if launcher is already running
            if (IsLauncherProcessRunning())
            {
                DebugLogger.LogInfo("[SptSession] Launcher already running");
                // If the game is also running, wait for plugin TCP
                if (IsGameProcessRunning())
                {
                    TransitionTo(SptState.WaitingForGame, "Game loading, waiting for plugin...");
                }
                else
                {
                    TransitionTo(SptState.StartingLauncher, "Launcher running - click Start Game");
                }
                return;
            }

            // Start the launcher
            StartLauncher();
        }

        private void HandleStartingLauncher()
        {
            // Check if launcher has exited prematurely (before game started)
            if (_launcherProcess != null && _launcherProcess.HasExited && !IsGameProcessRunning())
            {
                TransitionTo(SptState.Error, "Launcher exited without starting game");
                return;
            }

            // Check if game has started (launcher may have started it)
            if (IsGameProcessRunning())
            {
                DebugLogger.LogInfo("[SptSession] Game process detected");
                TransitionTo(SptState.WaitingForGame, "Game loading, waiting for plugin...");
                return;
            }

            // No timeout here - user needs to click Start Game in the launcher
            _statusText = "Click 'Start Game' in SPT Launcher";
        }

        private void HandleWaitingForGame()
        {
            // Try TCP connection to plugin
            if (_tcp.TryConnect(2000) && _tcp.Ping())
            {
                DebugLogger.LogInfo("[SptSession] Plugin TCP connected");
                TransitionTo(SptState.GameReady, "Game ready");
                return;
            }

            // Check if game process died
            if (!IsGameProcessRunning())
            {
                TransitionTo(SptState.Error, "Game process exited before plugin loaded");
                return;
            }

            // Timeout check (3 minutes - game takes a while to load)
            if (ElapsedInState() > TimeSpan.FromSeconds(180))
            {
                TransitionTo(SptState.Error, "Plugin connection timeout (180s)");
                return;
            }

            _statusText = $"Game loading... ({ElapsedInState().Seconds}s)";
        }

        private void HandleGameReady()
        {
            // Only set default status if no raid event is pending (avoids hiding SyncRaid errors)
            if (!_dmaRaidStarted)
                _statusText = "Game ready - awaiting raid";

            // Keep-alive ping every iteration (1s)
            if (!_tcp.Ping())
            {
                DebugLogger.LogInfo("[SptSession] Keep-alive ping failed");
                if (!_tcp.TryConnect(3000) || !_tcp.Ping())
                {
                    TransitionTo(SptState.Error, "Lost connection to plugin");
                    return;
                }
            }
        }

        private void HandleRaidSynced()
        {
            // Keep-alive ping
            if (!_tcp.Ping())
            {
                if (!_tcp.TryConnect(3000) || !_tcp.Ping())
                {
                    TransitionTo(SptState.Error, "Lost connection during raid");
                    return;
                }
            }

            _statusText = $"Raid synced - {_lastSyncedMap ?? "unknown map"}";
        }

        /// <summary>
        /// Handles raid start/stop events independently of the state machine.
        /// This ensures raid sync works whether SPT was started by us or manually.
        /// </summary>
        private void HandleRaidEvents()
        {
            // Handle raid stop - send leave_raid if we're synced
            if (_dmaRaidStopped)
            {
                _dmaRaidStopped = false;
                _detectedMapId = null;
                DebugLogger.LogInfo($"[SptSession] DMA raid stopped (state={_state})");

                if (_state == SptState.RaidSynced)
                {
                    DebugLogger.LogInfo("[SptSession] Sending leave_raid");
                    _tcp.LeaveRaid();
                    TransitionTo(SptState.GameReady, "Raid ended");
                }
            }

            // Handle raid start - try to sync if TCP is available
            if (_dmaRaidStarted)
            {
                _dmaRaidStarted = false;

                // Use early-detected map ID first, fall back to Memory.MapID
                var mapId = _detectedMapId ?? Memory.MapID;
                DebugLogger.LogInfo($"[SptSession] DMA raid event (state={_state}, mapId={mapId ?? "null"}, detectedMap={_detectedMapId ?? "null"})");

                if (string.IsNullOrEmpty(mapId))
                {
                    // Re-queue - MapID might not be set yet
                    _dmaRaidStarted = true;
                    return;
                }

                // Try to connect to plugin if not already connected
                if (!_tcp.IsConnected)
                {
                    DebugLogger.LogInfo("[SptSession] TCP not connected, attempting...");
                    _tcp.TryConnect(2000);
                }

                if (!_tcp.IsConnected || !_tcp.Ping())
                {
                    DebugLogger.LogInfo("[SptSession] Plugin not reachable, queuing...");
                    _dmaRaidStarted = true; // Re-queue for next iteration
                    return;
                }

                // If we weren't in GameReady state, jump there now
                if (_state != SptState.GameReady && _state != SptState.RaidSynced)
                {
                    DebugLogger.LogInfo($"[SptSession] Plugin reachable, jumping to GameReady from {_state}");
                    TransitionTo(SptState.GameReady, "Game ready (plugin connected)");
                }

                SyncRaid(mapId);
            }
        }

        private void HandleError()
        {
            _statusText = $"Error: {_lastError}";
            // Stay in error state, wait for manual intervention or DMA event
            // Auto-recovery: try again after 10 seconds
            if (ElapsedInState() > TimeSpan.FromSeconds(10))
            {
                DebugLogger.LogInfo("[SptSession] Attempting auto-recovery...");
                TransitionTo(SptState.Idle, "Retrying...");
                _dmaProcessFound = Memory.Ready;
            }
        }

        #endregion

        #region Raid Sync

        private void SyncRaid(string mapId)
        {
            // Translate DMA internal LocationId (e.g., "bigmap") to SPT location name (e.g., "Customs")
            // Must happen BEFORE the skip check so both values use the same format.
            if (SptMapNames.TryGetValue(mapId, out var sptName))
                mapId = sptName;

            // Skip if already synced to the same map
            if (_state == SptState.RaidSynced && _lastSyncedMap == mapId)
            {
                DebugLogger.LogInfo($"[SptSession] Already synced to {mapId}, skipping");
                return;
            }

            var config = App.Config.Visibility;
            DebugLogger.LogInfo($"[SptSession] SyncRaid: map={mapId}, side={config.SptDefaultSide}, time={config.SptDefaultTime}");

            // Check if SPT is already in a raid
            var gameState = _tcp.GetGameState();
            DebugLogger.LogInfo($"[SptSession] SPT game state: {gameState ?? "null"}");
            if (gameState == "in_raid")
            {
                DebugLogger.LogInfo("[SptSession] SPT already in raid, leaving first...");
                _tcp.LeaveRaid();

                // Wait for menu state
                for (int i = 0; i < 30; i++)
                {
                    Thread.Sleep(1000);
                    gameState = _tcp.GetGameState();
                    if (gameState == "menu") break;
                }

                if (gameState != "menu")
                {
                    DebugLogger.LogInfo($"[SptSession] Failed to reach menu state (got: {gameState}), re-queuing");
                    _dmaRaidStarted = true; // Re-queue for retry
                    return;
                }
            }

            DebugLogger.LogInfo($"[SptSession] Sending start_raid: map={mapId}, side={config.SptDefaultSide}, time={config.SptDefaultTime}");
            var resp = _tcp.SendCommand("start_raid", new { map = mapId, side = config.SptDefaultSide, time = config.SptDefaultTime });
            DebugLogger.LogInfo($"[SptSession] start_raid response: success={resp.Success}, error={resp.Error ?? "none"}, result={resp.Result}");

            if (!resp.Success)
            {
                _statusText = $"start_raid failed: {resp.Error}";
                DebugLogger.LogInfo($"[SptSession] start_raid TCP failed: {resp.Error}, re-queuing");
                _dmaRaidStarted = true;
                return;
            }

            // The TCP server wraps all results in {"success":true,"result":{...}}.
            // The INNER result from RaidController.StartRaidAsync has its own "success" field.
            // We must check the inner success to know if the raid actually started.
            bool innerSuccess = true;
            string innerError = null;
            if (resp.Result.HasValue)
            {
                try
                {
                    if (resp.Result.Value.TryGetProperty("success", out var innerSuccessProp))
                        innerSuccess = innerSuccessProp.GetBoolean();
                    if (resp.Result.Value.TryGetProperty("error", out var innerErrorProp))
                        innerError = innerErrorProp.GetString();
                }
                catch { }
            }

            if (innerSuccess)
            {
                _lastSyncedMap = mapId;
                TransitionTo(SptState.RaidSynced, $"Raid started: {mapId}");
            }
            else
            {
                _statusText = $"start_raid rejected: {innerError}";
                DebugLogger.LogInfo($"[SptSession] start_raid inner failure: {innerError}, re-queuing");
                _dmaRaidStarted = true;
            }
        }

        #endregion

        #region Process Management

        private void StartServer()
        {
            var config = App.Config.Visibility;

            // Use the .lnk shortcut - this is how SPT is normally launched
            var serverLnk = Path.Combine(config.SptPath, "SPT.Server.lnk");
            if (!File.Exists(serverLnk))
            {
                TransitionTo(SptState.Error, $"Server shortcut not found: {serverLnk}");
                return;
            }

            DebugLogger.LogInfo($"[SptSession] Starting server via shortcut: {serverLnk}");

            try
            {
                _serverProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = serverLnk,
                    UseShellExecute = true
                });
                _weStartedServer = true;

                TransitionTo(SptState.StartingServer, "Starting server...");
            }
            catch (Exception ex)
            {
                TransitionTo(SptState.Error, $"Failed to start server: {ex.Message}");
            }
        }

        private void StartLauncher()
        {
            var config = App.Config.Visibility;

            // Configure launcher for auto-login with the selected profile
            if (!ConfigureLauncherAutoLogin(config))
                return; // Error already set

            // Use the .lnk shortcut - this is how SPT launcher is normally started
            var launcherLnk = Path.Combine(config.SptPath, "SPT.Launcher.lnk");
            if (!File.Exists(launcherLnk))
            {
                TransitionTo(SptState.Error, $"Launcher shortcut not found: {launcherLnk}");
                return;
            }

            DebugLogger.LogInfo($"[SptSession] Starting launcher via shortcut: {launcherLnk}");

            try
            {
                _launcherProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = launcherLnk,
                    UseShellExecute = true
                });
                _weStartedLauncher = true;

                TransitionTo(SptState.StartingLauncher, "Launcher started - click Start Game");
            }
            catch (Exception ex)
            {
                TransitionTo(SptState.Error, $"Failed to start launcher: {ex.Message}");
            }
        }

        /// <summary>
        /// Modifies the SPT launcher config.json to set UseAutoLogin and the
        /// selected profile's username, so the launcher auto-logs in on start.
        /// </summary>
        private bool ConfigureLauncherAutoLogin(VisibilityConfig config)
        {
            var configPath = Path.Combine(config.SptPath, "SPT", "user", "launcher", "config.json");

            if (!File.Exists(configPath))
            {
                TransitionTo(SptState.Error, $"Launcher config not found: {configPath}");
                return false;
            }

            try
            {
                // Find the username for the selected profile
                var profiles = ScanProfiles(config.SptPath);
                var selectedProfile = profiles.FirstOrDefault(p => p.Id == config.SptProfileId);
                if (selectedProfile == null)
                {
                    TransitionTo(SptState.Error, $"Profile not found: {config.SptProfileId}");
                    return false;
                }

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);

                // Build a new config preserving existing fields, updating auto-login
                var options = new JsonSerializerOptions { WriteIndented = true };
                using var ms = new MemoryStream();
                using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

                writer.WriteStartObject();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name == "UseAutoLogin")
                    {
                        writer.WriteBoolean("UseAutoLogin", true);
                    }
                    else if (prop.Name == "Server")
                    {
                        writer.WritePropertyName("Server");
                        writer.WriteStartObject();

                        foreach (var serverProp in prop.Value.EnumerateObject())
                        {
                            if (serverProp.Name == "AutoLoginCreds")
                            {
                                writer.WritePropertyName("AutoLoginCreds");
                                writer.WriteStartObject();
                                writer.WriteString("Username", selectedProfile.Username);
                                writer.WriteEndObject();
                            }
                            else
                            {
                                serverProp.WriteTo(writer);
                            }
                        }

                        // Ensure AutoLoginCreds exists even if it wasn't in the original
                        if (!prop.Value.TryGetProperty("AutoLoginCreds", out _))
                        {
                            writer.WritePropertyName("AutoLoginCreds");
                            writer.WriteStartObject();
                            writer.WriteString("Username", selectedProfile.Username);
                            writer.WriteEndObject();
                        }

                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.WriteTo(writer);
                    }
                }

                // Ensure UseAutoLogin exists even if it wasn't in the original
                if (!doc.RootElement.TryGetProperty("UseAutoLogin", out _))
                    writer.WriteBoolean("UseAutoLogin", true);

                writer.WriteEndObject();
                writer.Flush();

                var newJson = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(configPath, newJson);

                DebugLogger.LogInfo($"[SptSession] Launcher config updated: AutoLogin={selectedProfile.Username}");
                return true;
            }
            catch (Exception ex)
            {
                TransitionTo(SptState.Error, $"Failed to update launcher config: {ex.Message}");
                return false;
            }
        }

        private bool IsServerProcessRunning()
        {
            try
            {
                // Try both with and without dot (some systems report differently)
                var procs = Process.GetProcessesByName("SPT.Server");
                if (procs.Length > 0) return true;

                // Fallback: check by exact exe name in case GetProcessesByName handles dots differently
                procs = Process.GetProcessesByName("SPT");
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.MainModule?.FileName?.EndsWith("SPT.Server.exe", StringComparison.OrdinalIgnoreCase) == true)
                            return true;
                    }
                    catch { }
                }

                return false;
            }
            catch { return false; }
        }

        private bool IsLauncherProcessRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("SPT.Launcher");
                if (procs.Length > 0) return true;

                procs = Process.GetProcessesByName("SPT");
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.MainModule?.FileName?.EndsWith("SPT.Launcher.exe", StringComparison.OrdinalIgnoreCase) == true)
                            return true;
                    }
                    catch { }
                }

                return false;
            }
            catch { return false; }
        }

        private bool IsGameProcessRunning()
        {
            try
            {
                var procs = Process.GetProcessesByName("EscapeFromTarkov");
                return procs.Length > 0;
            }
            catch { return false; }
        }

        private bool IsServerReady()
        {
            try
            {
                var task = _httpClient.GetAsync("https://127.0.0.1:6969");
                task.Wait(3000);
                return task.IsCompletedSuccessfully;
            }
            catch { return false; }
        }

        #endregion

        #region Manual Controls (called from UI)

        /// <summary>
        /// Manually start the SPT stack (server + launcher).
        /// </summary>
        public void ManualStart()
        {
            var config = App.Config.Visibility;

            if (string.IsNullOrEmpty(config.SptPath))
            {
                _statusText = "Set Fika path first";
                return;
            }

            if (string.IsNullOrEmpty(config.SptProfileId))
            {
                _statusText = "Select a profile first";
                return;
            }

            // Check if game is already running with plugin
            if (_tcp.TryConnect(2000) && _tcp.Ping())
            {
                TransitionTo(SptState.GameReady, "Game already running");
                return;
            }

            if (IsServerProcessRunning())
            {
                TransitionTo(SptState.ServerReady, "Server already running");
            }
            else
            {
                StartServer();
            }
        }

        /// <summary>
        /// Manually stop SPT processes (server, launcher, game).
        /// </summary>
        public void ManualStop()
        {
            StopProcesses();
            TransitionTo(SptState.Idle, "Stopped manually");
        }

        /// <summary>
        /// Manually start a raid. Uses DMA MapID if in raid, otherwise uses default map.
        /// Calls TCP directly (bypasses worker thread event system).
        /// </summary>
        public void ManualStartRaid()
        {
            var mapId = Memory?.MapID;
            if (string.IsNullOrEmpty(mapId))
                mapId = "factory4_day"; // Default for manual testing

            DebugLogger.LogInfo($"[SptSession] Manual start raid: map={mapId}");

            // Ensure TCP is connected
            if (!_tcp.IsConnected)
                _tcp.TryConnect(3000);

            if (!_tcp.IsConnected)
            {
                _statusText = "Cannot reach plugin (TCP)";
                DebugLogger.LogInfo("[SptSession] Manual start raid failed: TCP not connected");
                return;
            }

            SyncRaid(mapId);
        }

        /// <summary>
        /// Manually leave the current raid.
        /// </summary>
        public void ManualLeaveRaid()
        {
            DebugLogger.LogInfo("[SptSession] Manual leave raid");
            _tcp?.LeaveRaid();
            if (_state == SptState.RaidSynced)
                TransitionTo(SptState.GameReady, "Left raid manually");
        }

        #endregion

        #region Profile Scanning

        /// <summary>
        /// Scan the SPT profiles directory and return available profiles.
        /// </summary>
        public static List<SptProfile> ScanProfiles(string sptPath)
        {
            var profiles = new List<SptProfile>();
            if (string.IsNullOrEmpty(sptPath)) return profiles;

            var profilesDir = Path.Combine(sptPath, "SPT", "user", "profiles");
            if (!Directory.Exists(profilesDir)) return profiles;

            foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Skip Scav profiles (they have "savage" in the aid field or lack info.side)
                    if (!root.TryGetProperty("info", out var info))
                        continue;

                    if (!info.TryGetProperty("username", out var usernameProp))
                        continue;

                    var username = usernameProp.GetString();
                    if (string.IsNullOrEmpty(username))
                        continue;

                    // Try to get side and level
                    var side = "";
                    var level = 0;

                    if (root.TryGetProperty("characters", out var chars) &&
                        chars.TryGetProperty("pmc", out var pmc) &&
                        pmc.TryGetProperty("Info", out var pmcInfo))
                    {
                        if (pmcInfo.TryGetProperty("Side", out var sideProp))
                            side = sideProp.GetString() ?? "";
                        if (pmcInfo.TryGetProperty("Level", out var levelProp))
                            level = levelProp.GetInt32();
                    }

                    // Only include profiles that have PMC data (filters out scav-only profiles)
                    if (string.IsNullOrEmpty(side))
                        continue;

                    profiles.Add(new SptProfile
                    {
                        Id = Path.GetFileNameWithoutExtension(file),
                        Username = username,
                        Side = side,
                        Level = level
                    });
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[SptSession] Failed to parse profile {file}: {ex.Message}");
                }
            }

            return profiles;
        }

        #endregion

        #region Helpers

        private void TransitionTo(SptState newState, string status)
        {
            var oldState = _state;
            _state = newState;
            _statusText = status;
            _stateEnteredAt = DateTime.UtcNow;

            if (newState == SptState.Error)
                _lastError = status;

            DebugLogger.LogInfo($"[SptSession] {oldState} → {newState}: {status}");
        }

        private TimeSpan ElapsedInState() => DateTime.UtcNow - _stateEnteredAt;

        private void StopProcesses()
        {
            // Kill game processes (EscapeFromTarkov) - launched by the launcher, not by us directly
            try
            {
                foreach (var proc in Process.GetProcessesByName("EscapeFromTarkov"))
                {
                    DebugLogger.LogInfo($"[SptSession] Killing game process (PID {proc.Id})");
                    proc.Kill();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SptSession] Failed to kill game: {ex.Message}");
            }

            // Kill launcher
            try
            {
                if (_weStartedLauncher && _launcherProcess != null && !_launcherProcess.HasExited)
                {
                    DebugLogger.LogInfo("[SptSession] Killing launcher process");
                    _launcherProcess.Kill();
                }
                _launcherProcess = null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SptSession] Failed to kill launcher: {ex.Message}");
            }

            // Kill server
            try
            {
                if (_weStartedServer && _serverProcess != null && !_serverProcess.HasExited)
                {
                    DebugLogger.LogInfo("[SptSession] Killing server process");
                    _serverProcess.Kill();
                }
                _serverProcess = null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SptSession] Failed to kill server: {ex.Message}");
            }

            _weStartedServer = false;
            _weStartedLauncher = false;
            _tcp?.Disconnect();
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            // Unsubscribe events
            MemDMA.ProcessStarted -= OnDmaProcessStarted;
            MemDMA.ProcessStopped -= OnDmaProcessStopped;
            MemDMA.MapDetected -= OnDmaMapDetected;
            MemDMA.RaidStarted -= OnDmaRaidStarted;
            MemDMA.RaidStopped -= OnDmaRaidStopped;

            _workerThread?.Join(3000);
            StopProcesses();
            _tcp?.Dispose();
            _httpClient?.Dispose();
        }

        #endregion
    }
}
