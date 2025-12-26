using LoneEftDmaRadar;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Quests;
using LoneEftDmaRadar.UI.Misc;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class DebugTabViewModel : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private string _DeviceAimbotDebugText = "DeviceAimbot Aimbot: (no data)";
        private bool _showDeviceAimbotDebug = App.Config.Device.ShowDebug;
        private string _performanceText = "No data";

        public DebugTabViewModel()
        {
            ToggleDebugConsoleCommand = new SimpleCommand(DebugLogger.Toggle);

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (_, _) =>
            {
                RefreshDeviceAimbotDebug();
                RefreshPerformanceStats();
                RefreshQuestTracker();
            };
            _timer.Start();
            RefreshDeviceAimbotDebug();
            RefreshPerformanceStats();
            RefreshQuestTracker();
        }

        public ICommand ToggleDebugConsoleCommand { get; }

        #region Scan Interval Settings

        /// <summary>
        /// Loot scan interval in seconds (1-120).
        /// </summary>
        public int LootScanInterval
        {
            get => App.Config.Debug.LootScanIntervalSeconds;
            set
            {
                if (App.Config.Debug.LootScanIntervalSeconds != value)
                {
                    App.Config.Debug.LootScanIntervalSeconds = Math.Clamp(value, 1, 120);
                    OnPropertyChanged(nameof(LootScanInterval));
                }
            }
        }

        /// <summary>
        /// Corpse scan interval in seconds (1-120).
        /// </summary>
        public int CorpseScanInterval
        {
            get => App.Config.Debug.CorpseScanIntervalSeconds;
            set
            {
                if (App.Config.Debug.CorpseScanIntervalSeconds != value)
                {
                    App.Config.Debug.CorpseScanIntervalSeconds = Math.Clamp(value, 1, 120);
                    OnPropertyChanged(nameof(CorpseScanInterval));
                }
            }
        }

        /// <summary>
        /// T1 Realtime worker sleep in ms (1-100).
        /// </summary>
        public int T1SleepMs
        {
            get => App.Config.Debug.T1SleepMs;
            set
            {
                if (App.Config.Debug.T1SleepMs != value)
                {
                    App.Config.Debug.T1SleepMs = Math.Clamp(value, 1, 100);
                    OnPropertyChanged(nameof(T1SleepMs));
                }
            }
        }

        /// <summary>
        /// T2 Slow worker sleep in ms (10-500).
        /// </summary>
        public int T2SleepMs
        {
            get => App.Config.Debug.T2SleepMs;
            set
            {
                if (App.Config.Debug.T2SleepMs != value)
                {
                    App.Config.Debug.T2SleepMs = Math.Clamp(value, 10, 500);
                    OnPropertyChanged(nameof(T2SleepMs));
                }
            }
        }

        /// <summary>
        /// T3 Explosives worker sleep in ms (10-500).
        /// </summary>
        public int T3SleepMs
        {
            get => App.Config.Debug.T3SleepMs;
            set
            {
                if (App.Config.Debug.T3SleepMs != value)
                {
                    App.Config.Debug.T3SleepMs = Math.Clamp(value, 10, 500);
                    OnPropertyChanged(nameof(T3SleepMs));
                }
            }
        }

        #endregion

        #region Performance Stats

        public string PerformanceText
        {
            get => _performanceText;
            private set
            {
                if (_performanceText != value)
                {
                    _performanceText = value;
                    OnPropertyChanged(nameof(PerformanceText));
                }
            }
        }

        private void RefreshPerformanceStats()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Worker Performance ===");
            sb.AppendLine($"T1 Realtime:  Last {PerformanceStats.T1LastLoopMs:F2}ms | Avg {PerformanceStats.T1AvgLoopMs:F2}ms");
            sb.AppendLine($"T2 Slow:      Last {PerformanceStats.T2LastLoopMs:F2}ms | Avg {PerformanceStats.T2AvgLoopMs:F2}ms");
            sb.AppendLine($"T3 Explosives: Last {PerformanceStats.T3LastLoopMs:F2}ms | Avg {PerformanceStats.T3AvgLoopMs:F2}ms");
            sb.AppendLine();
            sb.AppendLine("=== Scan Performance ===");
            sb.AppendLine($"Last Loot Scan: {PerformanceStats.LastLootScanMs:F0}ms");
            sb.AppendLine($"Time Since Scan: {PerformanceStats.SecondsSinceLastLootScan:F0}s");
            PerformanceText = sb.ToString();
        }

        #endregion

        #region Quest Tracker

        private string _questTrackerText = "Quest Tracker: (no data)";

        public string QuestTrackerText
        {
            get => _questTrackerText;
            private set
            {
                if (_questTrackerText != value)
                {
                    _questTrackerText = value;
                    OnPropertyChanged(nameof(QuestTrackerText));
                }
            }
        }

        private void RefreshQuestTracker()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Quest Tracker ===");

            var enabled = App.Config.QuestHelper.Enabled;
            sb.AppendLine($"Status: {(enabled ? "ENABLED" : "Disabled")}");

            var inRaid = Memory?.InRaid ?? false;
            if (!inRaid)
            {
                sb.AppendLine("In Raid: No");
                sb.AppendLine("Quest Manager: Waiting for raid...");
                QuestTrackerText = sb.ToString();
                return;
            }

            // Detect raid type based on player types (same logic as ESP)
            var players = Memory?.Players;
            var hasObservedPlayers = players?.Any(p => p is Tarkov.GameWorld.Player.ObservedPlayer) ?? false;
            var raidType = hasObservedPlayers ? "ONLINE" : "OFFLINE/PVE";
            sb.AppendLine($"In Raid: Yes | Mode: {raidType}");
            sb.AppendLine();

            var quests = Memory?.Quests;
            if (quests == null)
            {
                sb.AppendLine("Quest Manager: Not initialized");
                QuestTrackerText = sb.ToString();
                return;
            }

            // Active quests
            var questCount = quests.Quests?.Count ?? 0;
            sb.AppendLine($"Active Quests: {questCount}");

            // Items being tracked
            var itemCount = quests.ItemConditions?.Count ?? 0;
            sb.AppendLine($"Items Tracked: {itemCount}");
            if (itemCount > 0 && quests.ItemConditions != null)
            {
                var itemList = quests.ItemConditions.Keys.Take(5).ToList();
                foreach (var itemId in itemList)
                {
                    // Try to get item name from TarkovDataManager
                    var itemName = Tarkov.TarkovDataManager.AllItems.TryGetValue(itemId, out var item)
                        ? item.ShortName
                        : itemId[..Math.Min(12, itemId.Length)];
                    sb.AppendLine($"  • {itemName}");
                }
                if (itemCount > 5)
                    sb.AppendLine($"  ... and {itemCount - 5} more");
            }

            // Quest locations
            var locationCount = quests.LocationConditions?.Count ?? 0;
            sb.AppendLine($"Quest Locations: {locationCount}");

            QuestTrackerText = sb.ToString();
        }

        #endregion

        public bool ShowDeviceAimbotDebug
        {
            get => _showDeviceAimbotDebug;
            set
            {
                if (_showDeviceAimbotDebug == value)
                    return;
                _showDeviceAimbotDebug = value;
                App.Config.Device.ShowDebug = value;
                OnPropertyChanged(nameof(ShowDeviceAimbotDebug));
            }
        }

        public string DeviceAimbotDebugText
        {
            get => _DeviceAimbotDebugText;
            private set
            {
                if (_DeviceAimbotDebugText != value)
                {
                    _DeviceAimbotDebugText = value;
                    OnPropertyChanged(nameof(DeviceAimbotDebugText));
                }
            }
        }

        private void RefreshDeviceAimbotDebug()
        {
            var snapshot = MemDMA.DeviceAimbot?.GetDebugSnapshot();
            if (snapshot == null)
            {
                DeviceAimbotDebugText = "DeviceAimbot Aimbot: not running or no data yet.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== DeviceAimbot Aimbot ===");
            sb.AppendLine($"Status: {snapshot.Status}");
            sb.AppendLine($"Key: {(snapshot.KeyEngaged ? "ENGAGED" : "Idle")} | Enabled: {snapshot.Enabled} | Device: {(snapshot.DeviceConnected ? "Connected" : "Disconnected")}");
            sb.AppendLine($"InRaid: {snapshot.InRaid} | FOV: {snapshot.ConfigFov:F0}px | MaxDist: {snapshot.ConfigMaxDistance:F0}m | Mode: {snapshot.TargetingMode}");
            sb.AppendLine($"Filters -> PMC:{App.Config.Device.TargetPMC} PScav:{App.Config.Device.TargetPlayerScav} AI:{App.Config.Device.TargetAIScav} Boss:{App.Config.Device.TargetBoss} Raider:{App.Config.Device.TargetRaider}");
            sb.AppendLine($"Candidates: total {snapshot.CandidateTotal}, type {snapshot.CandidateTypeOk}, dist {snapshot.CandidateInDistance}, skeleton {snapshot.CandidateWithSkeleton}, w2s {snapshot.CandidateW2S}, final {snapshot.CandidateCount}");
            sb.AppendLine($"Target: {(snapshot.LockedTargetName ?? "None")} [{snapshot.LockedTargetType?.ToString() ?? "-"}] valid={snapshot.TargetValid}");
            if (snapshot.LockedTargetDistance.HasValue)
                sb.AppendLine($"  Dist {snapshot.LockedTargetDistance.Value:F1}m | FOVDist {(float.IsNaN(snapshot.LockedTargetFov) ? "n/a" : snapshot.LockedTargetFov.ToString("F1"))} | Bone {snapshot.TargetBone}");
            sb.AppendLine($"Fireport: {(snapshot.HasFireport ? snapshot.FireportPosition?.ToString() : "None")}");
            var bulletSpeedText = snapshot.BulletSpeed.HasValue ? snapshot.BulletSpeed.Value.ToString("F1") : "?";
            sb.AppendLine($"Ballistics: {(snapshot.BallisticsValid ? $"OK (Speed {bulletSpeedText} m/s, Predict {(snapshot.PredictionEnabled ? "ON" : "OFF")})" : "Invalid/None")}");

            DeviceAimbotDebugText = sb.ToString();
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        #endregion
    }
}
