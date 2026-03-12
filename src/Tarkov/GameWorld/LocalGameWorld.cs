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

using System.Runtime;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Misc.Workers;
using LoneEftDmaRadar.Tarkov.Features.MemWrites;
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Explosives;
using LoneEftDmaRadar.Tarkov.GameWorld.Interactables;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Quests;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using VmmSharpEx.Options;

namespace LoneEftDmaRadar.Tarkov.GameWorld
{
    /// <summary>
    /// Class containing Game (Raid) instance.
    /// IDisposable.
    /// </summary>
    public sealed class LocalGameWorld : IDisposable
    {
        #region Fields / Properties / Constructors

        public static implicit operator ulong(LocalGameWorld x) => x.Base;

        /// <summary>
        /// LocalGameWorld Address.
        /// </summary>
        private ulong Base { get; }

        private RegisteredPlayers _rgtPlayers;
        private ExitManager _exfilManager;
        private ExplosivesManager _explosivesManager;
        private WorkerThread _t1;
        private WorkerThread _t2;
        private WorkerThread _t3;
        private WorkerThread _t4;
        private MemWritesManager _memWritesManager;
        private QuestManager _questManager;
        private WorldInteractablesManager _interactablesManager;

        // Pre-allocated list to avoid LINQ allocations in hot path
        private readonly List<AbstractPlayer> _activePlayersCache = new(32);

        // Loot scan throttling - only scan on raid start, then at configurable interval
        private bool _initialLootScanComplete = false;
        private DateTime _lastFullLootScan = DateTime.MinValue;

        /// <summary>
        /// Gets the loot scan interval from config.
        /// </summary>
        private static TimeSpan LootScanInterval => TimeSpan.FromSeconds(App.Config.Debug.LootScanIntervalSeconds);

        /// <summary>
        /// Map ID of Current Map.
        /// </summary>
        public string MapID { get; }

        public bool InRaid => !_disposed;
        public IReadOnlyCollection<AbstractPlayer> Players => _rgtPlayers;
        public IReadOnlyCollection<IExplosiveItem> Explosives => _explosivesManager;
        public IReadOnlyCollection<IExitPoint> Exits => _exfilManager;
        public LocalPlayer LocalPlayer => _rgtPlayers?.LocalPlayer;
        public LootManager Loot { get; private set; }
        public QuestManager Quests => _questManager;
        public IReadOnlyList<Hazards.IWorldHazard> Hazards { get; private set; }
        public IReadOnlyList<Door> Doors => _interactablesManager?.Doors;

        /// <summary>
        /// True once the raid has started (player has control).
        /// </summary>
        public bool RaidStarted { get; private set; }

        /// <summary>
        /// True if no ObservedPlayers exist (offline/PVE raid).
        /// Auto-detected from player types — enables container/corpse content scanning.
        /// </summary>
        public bool IsOfflineRaid { get; internal set; } = true;

        private LocalGameWorld() { }

        /// <summary>
        /// Phase 1 Constructor — captures base address and map ID only.
        /// Heavy initialization deferred to <see cref="WaitForRaidReady"/>.
        /// </summary>
        private LocalGameWorld(ulong localGameWorld, string mapID)
        {
            Base = localGameWorld;
            MapID = mapID;
        }

        /// <summary>
        /// Phase 2 — waits for local player readiness, then initializes game data.
        /// Camera initialization is handled separately by MemDMA game loop.
        /// </summary>
        private void WaitForRaidReady(CancellationToken ct)
        {
            try
            {
                // Wait until registered players are populated and MainPlayer is valid
                const int maxAttempts = 60; // 60 × 500ms = 30s timeout
                bool playerReady = false;
                for (int i = 0; i < maxAttempts; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (IsLocalPlayerInRaid())
                        {
                            playerReady = true;
                            DebugLogger.LogDebug($"[GameWorld] LocalPlayer confirmed in raid after {i + 1} attempt(s).");
                            break;
                        }
                    }
                    catch { /* Not ready yet */ }
                    Thread.Sleep(500);
                }
                if (!playerReady)
                    DebugLogger.LogDebug("[GameWorld] WARNING: LocalPlayer not confirmed after 30s — proceeding anyway.");

                // Initialize all game data managers and worker threads
                InitializeGameData(ct);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Checks if the local player is present and registered players list is valid.
        /// </summary>
        private bool IsLocalPlayerInRaid()
        {
            var playerBase = Memory.ReadPtr(Base + Offsets.GameWorld.MainPlayer, false);
            if (playerBase == 0 || !MemDMA.IsValidVirtualAddress(playerBase))
                return false;

            var rgtPlayersAddr = Memory.ReadPtr(Base + Offsets.GameWorld.RegisteredPlayers, false);
            var count = Memory.ReadValue<int>(rgtPlayersAddr + 0x18, false); // ManagedList.Count
            return count >= 1 && count < 100;
        }

        /// <summary>
        /// Initializes all game data managers and worker threads.
        /// </summary>
        private void InitializeGameData(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            BossSpawnTracker.Reset();
            KillFeedManager.Reset();
            App.PlayerHistory.OnNewRaid();
            _t1 = new WorkerThread()
            {
                Name = "Realtime Worker",
                ThreadPriority = ThreadPriority.AboveNormal,
                SleepDuration = TimeSpan.FromMilliseconds(8),
                SleepMode = WorkerThreadSleepMode.DynamicSleep
            };
            _t1.PerformWork += RealtimeWorker_PerformWork;
            _t2 = new WorkerThread()
            {
                Name = "Slow Worker",
                ThreadPriority = ThreadPriority.BelowNormal,
                SleepDuration = TimeSpan.FromMilliseconds(50)
            };
            _t2.PerformWork += SlowWorker_PerformWork;
            _t3 = new WorkerThread()
            {
                Name = "Explosives Worker",
                SleepDuration = TimeSpan.FromMilliseconds(30),
                SleepMode = WorkerThreadSleepMode.DynamicSleep
            };
            _t3.PerformWork += ExplosivesWorker_PerformWork;
            var rgtPlayersAddr = Memory.ReadPtr(Base + Offsets.GameWorld.RegisteredPlayers, false);
            _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
            var playerCount = _rgtPlayers.GetPlayerCount();
            ArgumentOutOfRangeException.ThrowIfLessThan(playerCount, 1, nameof(_rgtPlayers));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(playerCount, 100, nameof(_rgtPlayers));
            Loot = new(localGameWorld: Base);
            _exfilManager = new(MapID, _rgtPlayers.LocalPlayer.IsPmc, Base, _rgtPlayers.LocalPlayer);
            _explosivesManager = new(Base);
            if (TarkovDataManager.MapData.TryGetValue(MapID, out var mapData) && mapData.Hazards?.Count > 0)
            {
                Hazards = mapData.Hazards.Cast<Hazards.IWorldHazard>().ToList();
            }
            else
            {
                Hazards = Array.Empty<Hazards.IWorldHazard>();
            }
            try
            {
                _interactablesManager = new WorldInteractablesManager(Base);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[GameWorld] Failed to init interactables: {ex.Message}");
            }
            _memWritesManager = new MemWritesManager();
            _questManager = new QuestManager(_rgtPlayers.LocalPlayer.Profile);
            GameWorld.Loot.WishlistTracker.Initialize(_rgtPlayers.LocalPlayer.Profile);
            GameWorld.Loot.DogtagReader.Initialize();
            _t4 = new WorkerThread()
            {
                Name = "MemWrites Worker",
                ThreadPriority = ThreadPriority.Normal,
                SleepDuration = TimeSpan.FromMilliseconds(100)
            };
            _t4.PerformWork += MemWritesWorker_PerformWork;
        }

        /// <summary>
        /// Start all Game Threads.
        /// </summary>
        public void Start()
        {
            // Switch to low-latency GC mode during raid for better realtime performance
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            _memWritesManager?.OnRaidStart();
            _t1.Start();
            _t2.Start();
            _t3.Start();
            _t4.Start();
        }

        // Track last error type to avoid spam logging the same error repeatedly
        private static string _lastInstantiationError = null;

        /// <summary>
        /// Blocks until a LocalGameWorld Singleton Instance can be instantiated.
        /// </summary>
        public static LocalGameWorld CreateGameInstance(CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                ResourceJanitor.Run();
                Memory.ThrowIfProcessNotRunning();
                try
                {
                    var instance = GetLocalGameWorld(ct);
                    _lastInstantiationError = null; // Reset on success
                    DebugLogger.LogDebug("Raid has started!");
                    return instance;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (ex.InnerException?.Message?.Contains("GameWorld not found") == true)
                {
                    // Expected when not in raid - silently continue polling
                }
                catch (Exception ex)
                {
                    // Only log if this is a new/different error type to avoid spam
                    var errorKey = ex.InnerException?.GetType().Name ?? ex.GetType().Name;
                    if (_lastInstantiationError != errorKey)
                    {
                        _lastInstantiationError = errorKey;
                        DebugLogger.LogDebug($"ERROR Instantiating Game Instance: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }
                finally
                {
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Checks if a Raid has started.
        /// Loads Local Game World resources using two-phase initialization.
        /// Phase 1: Capture base address and map ID.
        /// Phase 2: Wait for camera + player readiness, then init game data.
        /// </summary>
        private static LocalGameWorld GetLocalGameWorld(CancellationToken ct)
        {
            try
            {
                var localGameWorld = GameObjectManager.Get().GetGameWorld(ct, out string map);
                if (localGameWorld == 0) throw new Exception("GameWorld Address is 0");
                // Phase 1: Minimal constructor
                var instance = new LocalGameWorld(localGameWorld, map);
                // Phase 2: Wait for readiness, then initialize
                instance.WaitForRaidReady(ct);
                return instance;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR Getting LocalGameWorld", ex);
            }
        }

        /// <summary>
        /// Main Game Loop executed by Memory Worker Thread. Refreshes/Updates Player List and performs Player Allocations.
        /// </summary>
        public void Refresh()
        {
            try
            {
                ThrowIfRaidEnded();
                if (MapID.Equals("tarkovstreets", StringComparison.OrdinalIgnoreCase) ||
                    MapID.Equals("woods", StringComparison.OrdinalIgnoreCase))
                    TryAllocateBTR();
                _rgtPlayers.Refresh(); // Check for new players, add to list, etc.
            }
            catch (OperationCanceledException ex) // Raid Ended
            {
                DebugLogger.LogDebug(ex.Message);
                Dispose();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"CRITICAL ERROR - Raid ended due to unhandled exception: {ex}");
                throw;
            }
        }

        /// <summary>
        /// Throws an exception if the current raid instance has ended.
        /// </summary>
        /// <exception cref="OperationCanceledException"></exception>
        private void ThrowIfRaidEnded()
        {
            for (int i = 0; i < 5; i++) // Re-attempt if read fails -- 5 times
            {
                try
                {
                    if (!IsRaidActive())
                        continue;
                    return;
                }
                catch { Thread.Sleep(10); } // short delay between read attempts
            }
            throw new OperationCanceledException("Raid has ended!"); // Still not valid? Raid must have ended.
        }

        /// <summary>
        /// Checks if the Current Raid is Active, and LocalPlayer is alive/active.
        /// </summary>
        /// <returns>True if raid is active, otherwise False.</returns>
        private bool IsRaidActive()
        {
            try
            {
                var mainPlayer = Memory.ReadPtr(this + Offsets.GameWorld.MainPlayer, false);
                ArgumentOutOfRangeException.ThrowIfNotEqual(mainPlayer, _rgtPlayers.LocalPlayer, nameof(mainPlayer));
                return _rgtPlayers.GetPlayerCount() > 0;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Realtime Thread T1

        /// <summary>
        /// Managed Worker Thread that does realtime (player position/info) updates.
        /// </summary>
        private void RealtimeWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                // Build active player list without LINQ allocations
                _activePlayersCache.Clear();
                foreach (var player in _rgtPlayers)
                {
                    if (player.IsActive && player.IsAlive)
                        _activePlayersCache.Add(player);
                }

                var localPlayer = LocalPlayer;
                if (_activePlayersCache.Count == 0) // No players - Throttle
                {
                    Thread.Sleep(1);
                    return;
                }

                using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
                if (MemDMA.CameraManager != null && localPlayer != null)
                {
                    MemDMA.CameraManager.OnRealtimeLoop(scatter, localPlayer);
                }
                foreach (var player in _activePlayersCache)
                {
                    player.OnRealtimeLoop(scatter);
                }
                // Add fireport transform to scatter for realtime aimbot accuracy
                localPlayer?.FirearmManager?.OnRealtimeLoop(scatter);
                scatter.Execute();
            }
            finally
            {
                PerformanceStats.UpdateT1(Stopwatch.GetTimestamp() - start);
            }
        }

        #endregion

        #region Slow Thread T2

        /// <summary>
        /// Managed Worker Thread that does ~Slow Local Game World Updates.
        /// *** THIS THREAD HAS A LONG RUN TIME! LOOPS ~MAY~ TAKE ~10 SECONDS OR MORE ***
        /// </summary>
        private void SlowWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                var ct = e.CancellationToken;
                ValidatePlayerTransforms(); // Check for transform anomalies
                PreRaidStartChecks(ct); // Auto Groups detection before raid start

                // Throttle loot scanning - only on raid start, then at configurable interval
                if (!_initialLootScanComplete ||
                    DateTime.UtcNow - _lastFullLootScan > LootScanInterval)
                {
                    var lootScanStart = Stopwatch.GetTimestamp();
                    Loot.Refresh(ct);
                    _lastFullLootScan = DateTime.UtcNow;
                    _initialLootScanComplete = true;
                    PerformanceStats.UpdateLootScan(Stopwatch.GetTimestamp() - lootScanStart);
                }

                // Refresh exfil statuses from memory (live status updates)
                _exfilManager.RefreshStatuses();

                // Refresh door states
                _interactablesManager?.Refresh();

                // Refresh quest tracking data
                _questManager?.Refresh(ct);

                // Refresh player equipment
                RefreshEquipment();

                // Update firearm manager (ammo, weapon detection, fireport acquisition)
                // Fireport position is updated at T1 rate via scatter; this handles the slow parts
                LocalPlayer?.UpdateFirearmManager();
            }
            finally
            {
                PerformanceStats.UpdateT2(Stopwatch.GetTimestamp() - start);
            }
        }

        private void RefreshEquipment()
        {
            foreach (var player in _rgtPlayers)
            {
                if (player is ObservedPlayer observed && !observed.IsAI && observed.IsActive && observed.IsAlive)
                    observed.Equipment.Refresh();
            }
        }

        /// <summary>
        /// Executes pre-raid start checks to determine if the raid has started, and various child operations.
        /// </summary>
        private void PreRaidStartChecks(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (RaidStarted || this.LocalPlayer is not LocalPlayer localPlayer)
                return;
            try
            {
                if (localPlayer.Hands is ulong hands && hands != 0)
                {
                    string handsType = ObjectClass.ReadName(hands);
                    RaidStarted = !string.IsNullOrWhiteSpace(handsType) && handsType != "ClientEmptyHandsController";
                    if (!RaidStarted && !localPlayer.IsScav && App.Config.Misc.AutoGroups)
                    {
                        RefreshGroups(localPlayer, ct);
                    }
                    else if (RaidStarted)
                    {
                        DebugLogger.LogDebug("[PreRaidStartChecks] Raid has started!");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[PreRaidStartChecks] ERROR: {ex}");
            }
        }

        /// <summary>
        /// Refreshes Player Groups based on proximity to each other before raid start.
        /// </summary>
        private void RefreshGroups(LocalPlayer localPlayer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            const float groupDistanceThreshold = 10f;
            int raidId = localPlayer.RaidId;
            if (!App.Config.Cache.Groups.TryGetValue(raidId, out var groups))
                App.Config.Cache.Groups[raidId] = groups = new();

            var humanPlayers = _rgtPlayers
                .Where(p => p.IsPmc && p.Position != Vector3.Zero)
                .OfType<ObservedPlayer>()
                .ToList();

            if (humanPlayers.Count == 0)
                return;

            // Players near LocalPlayer (probable teammates)
            var nearLocalPlayer = humanPlayers
                .Where(p => p.Type != PlayerType.Teammate && Vector3.Distance(localPlayer.Position, p.Position) <= groupDistanceThreshold);

            foreach (var player in nearLocalPlayer)
            {
                groups[player.PlayerId] = -100; // -100 indicates teammate
                player.AssignTeammate();
            }

            var hostilePlayers = humanPlayers
                .Where(p => p.IsHostile)
                .ToList();

            // Group hostile players within threshold distance using transitive clustering
            var ungrouped = new HashSet<ObservedPlayer>(hostilePlayers);
            int groupId = groups.Values.Where(v => v >= 0).DefaultIfEmpty(-1).Max() + 1; // Start after existing max group ID

            while (ungrouped.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var seed = ungrouped.First();
                ungrouped.Remove(seed);

                // Check if player already has a group assignment
                if (groups.ContainsKey(seed.PlayerId))
                    continue;

                var group = new List<ObservedPlayer> { seed };

                // Expand group transitively - find all players connected within threshold
                IEnumerable<ObservedPlayer> toAdd;
                do
                {
                    toAdd = ungrouped
                        .Where(p => group.Any(g => Vector3.Distance(g.Position, p.Position) <= groupDistanceThreshold))
                        .ToList();

                    foreach (var p in toAdd)
                    {
                        ungrouped.Remove(p);
                        group.Add(p);
                    }
                } while (toAdd.Any());

                // Assign group ID to all members if group has 2+ players, otherwise leave -1 (solo)
                if (group.Count > 1)
                {
                    foreach (var p in group)
                    {
                        groups[p.PlayerId] = groupId;
                        p.AssignGroup(groupId);
                    }
                    groupId++;
                }
            }
        }

        // Pre-allocated list to avoid LINQ allocations in ValidatePlayerTransforms
        private readonly List<AbstractPlayer> _validatePlayersCache = new(32);

        public void ValidatePlayerTransforms()
        {
            try
            {
                _validatePlayersCache.Clear();
                foreach (var player in _rgtPlayers)
                {
                    if (player.IsActive && player.IsAlive && player is not BtrPlayer)
                        _validatePlayersCache.Add(player);
                }
                if (_validatePlayersCache.Count > 0)
                {
                    using var map = Memory.CreateScatterMap();
                    var round1 = map.AddRound();
                    var round2 = map.AddRound();
                    foreach (var player in _validatePlayersCache)
                    {
                        player.OnValidateTransforms(round1, round2);
                    }
                    map.Execute(); // execute scatter read
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"CRITICAL ERROR - ValidatePlayerTransforms Loop FAILED: {ex}");
            }
        }

        #endregion

        #region MemWrites Thread T4

        private void MemWritesWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            try
            {
                if (!App.Config.MemWrites.Enabled)
                {
                    Thread.Sleep(100);
                    return;
                }

                var localPlayer = LocalPlayer;
                if (localPlayer is null)
                    return;

                _memWritesManager.Apply(localPlayer);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[MemWritesWorker] Error: {ex}");
            }
        }

        #endregion

        #region Explosives Thread T3

        /// <summary>
        /// Managed Worker Thread that does Explosives (grenades,etc.) updates.
        /// </summary>
        private void ExplosivesWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            var start = Stopwatch.GetTimestamp();
            try
            {
                _explosivesManager.Refresh(e.CancellationToken);
            }
            finally
            {
                PerformanceStats.UpdateT3(Stopwatch.GetTimestamp() - start);
            }
        }

        #endregion

        #region BTR Vehicle

        /// <summary>
        /// Checks if there is a Bot attached to the BTR Turret and re-allocates the player instance.
        /// </summary>
        public void TryAllocateBTR()
        {
            try
            {
                if (_rgtPlayers.Any(p => p is BtrPlayer))
                    return;

                var btrController = Memory.ReadPtr(this + Offsets.GameWorld.BtrController);
                var btrView = Memory.ReadPtr(btrController + Offsets.BtrController.BtrView);
                var btrTurretView = Memory.ReadPtr(btrView + Offsets.BTRView.turret);
                var btrOperator = Memory.ReadPtr(btrTurretView + Offsets.BTRTurretView.AttachedBot);
                _rgtPlayers.TryAllocateBTR(btrView, btrOperator);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ERROR Allocating BTR: {ex}");
            }
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                // Restore interactive GC mode when raid ends
                GCSettings.LatencyMode = GCLatencyMode.Interactive;
                _t1?.Dispose();
                _t2?.Dispose();
                _t3?.Dispose();
                _t4?.Dispose();
                GameWorld.Loot.WishlistTracker.Clear();
                GameWorld.Loot.DogtagReader.Clear();
            }
        }

        #endregion
    }
}
