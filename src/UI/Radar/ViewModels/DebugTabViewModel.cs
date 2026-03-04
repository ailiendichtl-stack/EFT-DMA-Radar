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
            _showPerformanceMonitor = App.Config.Debug.ShowPerformanceMonitor;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += (_, _) => OnTimerTick();
        }

        public ICommand ToggleDebugConsoleCommand { get; }

        #region Panel Visibility

        private bool _isPanelVisible;

        public void SetPanelVisible(bool visible)
        {
            _isPanelVisible = visible;
            UpdateTimerState();

            if (visible)
            {
                RefreshDeviceAimbotDebug();
                if (_showPerformanceMonitor)
                    RefreshPerformanceStats();
                RefreshQuestTracker();
            }
        }

        private void UpdateTimerState()
        {
            if (_isPanelVisible)
                _timer.Start();
            else
                _timer.Stop();
        }

        private void OnTimerTick()
        {
            RefreshDeviceAimbotDebug();
            if (_showPerformanceMonitor)
                RefreshPerformanceStats();
            RefreshQuestTracker();
        }

        #endregion

        #region Performance Stats

        private bool _showPerformanceMonitor;

        public bool ShowPerformanceMonitor
        {
            get => _showPerformanceMonitor;
            set
            {
                if (_showPerformanceMonitor == value)
                    return;
                _showPerformanceMonitor = value;
                App.Config.Debug.ShowPerformanceMonitor = value;
                OnPropertyChanged(nameof(ShowPerformanceMonitor));
                if (value && _isPanelVisible)
                    RefreshPerformanceStats();
            }
        }

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

        private static string GetStatus(double ms, double goodMax, double warnMax)
        {
            if (ms <= goodMax) return "[Good]";
            if (ms <= warnMax) return "[Warning]";
            return "[High]";
        }

        private void RefreshPerformanceStats()
        {
            var t1Avg = PerformanceStats.T1AvgLoopMs;
            var t2Avg = PerformanceStats.T2AvgLoopMs;
            var t3Avg = PerformanceStats.T3AvgLoopMs;
            var lootMs = PerformanceStats.LastLootScanMs;
            var sinceScan = PerformanceStats.SecondsSinceLastLootScan;

            var sb = new StringBuilder();
            sb.AppendLine("=== Worker Threads ===");
            sb.AppendLine($"T1 Realtime:   {PerformanceStats.T1LastLoopMs,6:F2}ms | Avg {t1Avg,6:F2}ms  {GetStatus(t1Avg, 8, 15)}");
            sb.AppendLine($"T2 Slow:       {PerformanceStats.T2LastLoopMs,6:F2}ms | Avg {t2Avg,6:F2}ms  {GetStatus(t2Avg, 500, 2000)}");
            sb.AppendLine($"T3 Explosives: {PerformanceStats.T3LastLoopMs,6:F2}ms | Avg {t3Avg,6:F2}ms  {GetStatus(t3Avg, 15, 30)}");
            sb.AppendLine();
            sb.AppendLine("=== Loot Scan ===");
            sb.AppendLine($"Duration: {lootMs,6:F0}ms  {GetStatus(lootMs, 1000, 3000)}");
            sb.AppendLine($"Last Scan: {sinceScan:F0}s ago");
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
