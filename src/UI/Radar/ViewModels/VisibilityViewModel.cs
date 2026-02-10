using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using LoneEftDmaRadar.LOS;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public class VisibilityViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly DispatcherTimer _refreshTimer;

        public VisibilityViewModel()
        {
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (_, _) => RefreshStatus();
            _refreshTimer.Start();
        }

        public void Dispose()
        {
            _refreshTimer.Stop();
        }

        #region Config Bindings

        public bool Enabled
        {
            get => App.Config.Visibility.Enabled;
            set
            {
                App.Config.Visibility.Enabled = value;
                OnPropertyChanged();

                // Start/stop worker based on toggle
                if (value)
                    VisibilityManager.Start();
                else
                    VisibilityManager.Stop();
            }
        }

        public bool DualCheck
        {
            get => App.Config.Visibility.DualCheck;
            set { App.Config.Visibility.DualCheck = value; OnPropertyChanged(); }
        }

        public bool NoFoliage
        {
            get => App.Config.Visibility.NoFoliage;
            set { App.Config.Visibility.NoFoliage = value; OnPropertyChanged(); }
        }

        public bool PerBoneColoring
        {
            get => App.Config.Visibility.PerBoneColoring;
            set { App.Config.Visibility.PerBoneColoring = value; OnPropertyChanged(); }
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

        #endregion

        #region Live Status

        private string _meshStatusText = "Not running";
        public string MeshStatusText
        {
            get => _meshStatusText;
            private set { _meshStatusText = value; OnPropertyChanged(); }
        }

        private string _workerStatusText = "";
        public string WorkerStatusText
        {
            get => _workerStatusText;
            private set { _workerStatusText = value; OnPropertyChanged(); }
        }

        private string _availableMapsText = "";
        public string AvailableMapsText
        {
            get => _availableMapsText;
            private set { _availableMapsText = value; OnPropertyChanged(); }
        }

        private void RefreshStatus()
        {
            var mgr = VisibilityManager.Instance;
            if (mgr == null)
            {
                MeshStatusText = Enabled ? "Starting..." : "Not running";
                WorkerStatusText = "";
            }
            else
            {
                MeshStatusText = mgr.MeshStatus;
                WorkerStatusText = mgr.IsReady
                    ? $"Tracked: {mgr.EnemiesTracked} | {mgr.LatencyMs:F1}ms | {mgr.FramesPerSecond} fps"
                    : "Waiting for mesh...";
            }

            // Show available maps
            var maps = MeshRaycastService.GetAvailableMaps();
            AvailableMapsText = maps.Count > 0
                ? $"Available: {string.Join(", ", maps)}"
                : $"No maps in {MeshRaycastService.MapDataPath}";
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }
}
