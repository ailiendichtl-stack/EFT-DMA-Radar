using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using LoneEftDmaRadar.LOS;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public class VisibilityViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly DispatcherTimer _statsTimer;

        #region Live Stats (read-only, refreshed by timer)

        private bool _isConnected;
        private int _enemiesTracked;
        private float _latencyMs;
        private int _framesPerSecond;
        private string _sptStatusText = "Idle";

        public bool IsConnected
        {
            get => _isConnected;
            private set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(ConnectionStatus)); }
        }

        public string ConnectionStatus => IsConnected ? "Connected" : "Disconnected";

        public int EnemiesTracked
        {
            get => _enemiesTracked;
            private set { _enemiesTracked = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatsText)); }
        }

        public float LatencyMs
        {
            get => _latencyMs;
            private set { _latencyMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatsText)); }
        }

        public int FramesPerSecond
        {
            get => _framesPerSecond;
            private set { _framesPerSecond = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatsText)); }
        }

        public string StatsText => IsConnected
            ? $"{EnemiesTracked}/64 tracked  |  {LatencyMs:F1}ms  |  {FramesPerSecond} fps"
            : "Not connected to SHM";

        public string SptStatusText
        {
            get => _sptStatusText;
            private set { _sptStatusText = value; OnPropertyChanged(); }
        }

        #endregion

        #region LOS Config Properties

        public bool Enabled
        {
            get => App.Config.Visibility.Enabled;
            set
            {
                if (App.Config.Visibility.Enabled != value)
                {
                    App.Config.Visibility.Enabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ShmEnabled
        {
            get => App.Config.Visibility.ShmEnabled;
            set
            {
                if (App.Config.Visibility.ShmEnabled != value)
                {
                    App.Config.Visibility.ShmEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool PerBoneColoring
        {
            get => App.Config.Visibility.PerBoneColoring;
            set
            {
                if (App.Config.Visibility.PerBoneColoring != value)
                {
                    App.Config.Visibility.PerBoneColoring = value;
                    OnPropertyChanged();
                }
            }
        }

        public string VisibleColor
        {
            get => App.Config.Visibility.VisibleColor;
            set { App.Config.Visibility.VisibleColor = value; OnPropertyChanged(); }
        }

        public string HitscanOnlyColor
        {
            get => App.Config.Visibility.HitscanOnlyColor;
            set { App.Config.Visibility.HitscanOnlyColor = value; OnPropertyChanged(); }
        }

        public string FlagsHex
        {
            get => $"0x{App.Config.Visibility.Flags:X2}";
            set
            {
                if (uint.TryParse(value.Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                {
                    App.Config.Visibility.Flags = parsed;
                    OnPropertyChanged();
                }
            }
        }

        public string BoneMaskHex
        {
            get => $"0x{App.Config.Visibility.BoneMask:X5}";
            set
            {
                if (uint.TryParse(value.Replace("0x", "").Replace("0X", ""), System.Globalization.NumberStyles.HexNumber, null, out uint parsed))
                {
                    App.Config.Visibility.BoneMask = parsed;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        #region SPT Auto-Start Properties

        public bool AutoStartEnabled
        {
            get => App.Config.Visibility.AutoStartEnabled;
            set
            {
                if (App.Config.Visibility.AutoStartEnabled != value)
                {
                    App.Config.Visibility.AutoStartEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SptPath
        {
            get => App.Config.Visibility.SptPath;
            set
            {
                if (App.Config.Visibility.SptPath != value)
                {
                    App.Config.Visibility.SptPath = value;
                    OnPropertyChanged();
                    // Auto-scan profiles when path changes
                    RefreshProfiles();
                }
            }
        }

        public string SelectedProfileId
        {
            get => App.Config.Visibility.SptProfileId;
            set
            {
                if (App.Config.Visibility.SptProfileId != value)
                {
                    App.Config.Visibility.SptProfileId = value ?? "";
                    // Update display name from the selected profile
                    var profile = AvailableProfiles.FirstOrDefault(p => p.Id == value);
                    App.Config.Visibility.SptProfileName = profile?.Username ?? "";
                    OnPropertyChanged();
                }
            }
        }

        public string SptDefaultSide
        {
            get => App.Config.Visibility.SptDefaultSide;
            set
            {
                if (App.Config.Visibility.SptDefaultSide != value)
                {
                    App.Config.Visibility.SptDefaultSide = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SptDefaultTime
        {
            get => App.Config.Visibility.SptDefaultTime;
            set
            {
                if (App.Config.Visibility.SptDefaultTime != value)
                {
                    App.Config.Visibility.SptDefaultTime = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<SptProfile> AvailableProfiles { get; } = new();

        #endregion

        #region Commands

        public ICommand PresetEyeOnlyCommand { get; }
        public ICommand PresetDualCheckCommand { get; }
        public ICommand PresetDualNoFoliageCommand { get; }
        public ICommand BrowseSptPathCommand { get; }
        public ICommand RefreshProfilesCommand { get; }
        public ICommand StartSptCommand { get; }
        public ICommand StopSptCommand { get; }
        public ICommand ManualStartRaidCommand { get; }
        public ICommand ManualLeaveRaidCommand { get; }

        #endregion

        public VisibilityViewModel()
        {
            // LOS Presets
            PresetEyeOnlyCommand = new SimpleCommand(() =>
            {
                App.Config.Visibility.Flags = 0x00;
                OnPropertyChanged(nameof(FlagsHex));
            });

            PresetDualCheckCommand = new SimpleCommand(() =>
            {
                App.Config.Visibility.Flags = 0x04;
                OnPropertyChanged(nameof(FlagsHex));
            });

            PresetDualNoFoliageCommand = new SimpleCommand(() =>
            {
                App.Config.Visibility.Flags = 0x14;
                OnPropertyChanged(nameof(FlagsHex));
            });

            // SPT commands
            BrowseSptPathCommand = new SimpleCommand(BrowseSptPath);
            RefreshProfilesCommand = new SimpleCommand(RefreshProfiles);
            StartSptCommand = new SimpleCommand(() => SptSessionManager.Instance?.ManualStart());
            StopSptCommand = new SimpleCommand(() => SptSessionManager.Instance?.ManualStop());
            ManualStartRaidCommand = new SimpleCommand(() => SptSessionManager.Instance?.ManualStartRaid());
            ManualLeaveRaidCommand = new SimpleCommand(() => SptSessionManager.Instance?.ManualLeaveRaid());

            // Load profiles on startup if path is set
            if (!string.IsNullOrEmpty(App.Config.Visibility.SptPath))
                RefreshProfiles();

            // Refresh stats every 500ms
            _statsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statsTimer.Tick += (_, _) => RefreshStats();
            _statsTimer.Start();
        }

        private void RefreshStats()
        {
            var mgr = VisibilityManager.Instance;
            IsConnected = mgr?.IsConnected ?? false;
            if (IsConnected)
            {
                EnemiesTracked = mgr.EnemiesTracked;
                LatencyMs = mgr.LatencyMs;
                FramesPerSecond = mgr.FramesPerSecond;
            }
            else
            {
                EnemiesTracked = 0;
                LatencyMs = 0;
                FramesPerSecond = 0;
            }

            // Update SPT status
            SptStatusText = SptSessionManager.Instance?.StatusText ?? "Not initialized";
        }

        private void BrowseSptPath()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Fika/SPT Installation Folder"
            };

            if (!string.IsNullOrEmpty(SptPath) && Directory.Exists(SptPath))
                dialog.InitialDirectory = SptPath;

            if (dialog.ShowDialog() == true)
            {
                SptPath = dialog.FolderName;
            }
        }

        private void RefreshProfiles()
        {
            var currentId = SelectedProfileId;
            AvailableProfiles.Clear();

            var profiles = SptSessionManager.ScanProfiles(App.Config.Visibility.SptPath);
            foreach (var p in profiles)
                AvailableProfiles.Add(p);

            // Restore selection if still available
            if (!string.IsNullOrEmpty(currentId) && AvailableProfiles.Any(p => p.Id == currentId))
                OnPropertyChanged(nameof(SelectedProfileId));
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
