/*
 * Twilight PVE Radar - WPF Modular GUI
 * PanelManagerViewModel: Manages visibility, position, and size of all floating panels
 */

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.ViewModels
{
    /// <summary>
    /// Stores the state of a single panel (position, size, visibility).
    /// </summary>
    public class PanelState : INotifyPropertyChanged
    {
        private bool _isOpen;
        private double _x = 100;
        private double _y = 100;
        private double _width = 400;
        private double _height = 400;

        public bool IsOpen
        {
            get => _isOpen;
            set { _isOpen = value; OnPropertyChanged(); }
        }

        public double X
        {
            get => _x;
            set { _x = value; OnPropertyChanged(); }
        }

        public double Y
        {
            get => _y;
            set { _y = value; OnPropertyChanged(); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        public double Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Manages all floating panel states for the modular GUI system.
    /// </summary>
    public class PanelManagerViewModel : INotifyPropertyChanged
    {
        #region Panel States

        public PanelState SettingsPanel { get; } = new() { X = 260, Y = 50, Width = 400, Height = 500 };
        public PanelState EspPanel { get; } = new() { X = 280, Y = 70, Width = 400, Height = 450 };
        public PanelState LootFiltersPanel { get; } = new() { X = 300, Y = 90, Width = 600, Height = 400 };
        public PanelState ContainersPanel { get; } = new() { X = 320, Y = 110, Width = 350, Height = 400 };
        public PanelState LootListPanel { get; } = new() { X = 340, Y = 130, Width = 500, Height = 400 };
        public PanelState QuestsPanel { get; } = new() { X = 360, Y = 150, Width = 400, Height = 450 };
        public PanelState HideoutPanel { get; } = new() { X = 380, Y = 170, Width = 450, Height = 500 };
        public PanelState HotkeysPanel { get; } = new() { X = 400, Y = 50, Width = 500, Height = 400 };
        public PanelState AimbotPanel { get; } = new() { X = 420, Y = 70, Width = 400, Height = 350 };
        public PanelState MemWritesPanel { get; } = new() { X = 440, Y = 90, Width = 350, Height = 300 };
        public PanelState WebRadarPanel { get; } = new() { X = 460, Y = 110, Width = 450, Height = 350 };
        public PanelState DebugPanel { get; } = new() { X = 480, Y = 130, Width = 400, Height = 300 };
        public PanelState VisibilityPanel { get; } = new() { X = 500, Y = 150, Width = 400, Height = 450 };

        #endregion

        #region Panel Name Mapping

        private readonly Dictionary<string, PanelState> _panelMap;
        private readonly List<PanelState> _openPanelOrder = new();

        #endregion

        #region Sidebar State

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCollapsed)); }
        }

        public bool IsCollapsed => !_isExpanded;

        #endregion

        #region Toggle Commands

        public ICommand ToggleSettingsPanelCommand { get; }
        public ICommand ToggleEspPanelCommand { get; }
        public ICommand ToggleLootFiltersPanelCommand { get; }
        public ICommand ToggleContainersPanelCommand { get; }
        public ICommand ToggleLootListPanelCommand { get; }
        public ICommand ToggleQuestsPanelCommand { get; }
        public ICommand ToggleHideoutPanelCommand { get; }
        public ICommand ToggleHotkeysPanelCommand { get; }
        public ICommand ToggleAimbotPanelCommand { get; }
        public ICommand ToggleMemWritesPanelCommand { get; }
        public ICommand ToggleWebRadarPanelCommand { get; }
        public ICommand ToggleDebugPanelCommand { get; }
        public ICommand ToggleVisibilityPanelCommand { get; }
        public ICommand ResetAllPanelsCommand { get; }

        #endregion

        public PanelManagerViewModel()
        {
            // Build panel name map
            _panelMap = new Dictionary<string, PanelState>
            {
                ["SettingsPanel"] = SettingsPanel,
                ["EspPanel"] = EspPanel,
                ["LootFiltersPanel"] = LootFiltersPanel,
                ["ContainersPanel"] = ContainersPanel,
                ["LootListPanel"] = LootListPanel,
                ["QuestsPanel"] = QuestsPanel,
                ["HideoutPanel"] = HideoutPanel,
                ["HotkeysPanel"] = HotkeysPanel,
                ["AimbotPanel"] = AimbotPanel,
                ["MemWritesPanel"] = MemWritesPanel,
                ["WebRadarPanel"] = WebRadarPanel,
                ["DebugPanel"] = DebugPanel,
                ["VisibilityPanel"] = VisibilityPanel
            };

            // Initialize toggle commands
            ToggleSettingsPanelCommand = new RelayCommand(() => TogglePanel(SettingsPanel));
            ToggleEspPanelCommand = new RelayCommand(() => TogglePanel(EspPanel));
            ToggleLootFiltersPanelCommand = new RelayCommand(() => TogglePanel(LootFiltersPanel));
            ToggleContainersPanelCommand = new RelayCommand(() => TogglePanel(ContainersPanel));
            ToggleLootListPanelCommand = new RelayCommand(() => TogglePanel(LootListPanel));
            ToggleQuestsPanelCommand = new RelayCommand(() => TogglePanel(QuestsPanel));
            ToggleHideoutPanelCommand = new RelayCommand(() => TogglePanel(HideoutPanel));
            ToggleHotkeysPanelCommand = new RelayCommand(() => TogglePanel(HotkeysPanel));
            ToggleAimbotPanelCommand = new RelayCommand(() => TogglePanel(AimbotPanel));
            ToggleMemWritesPanelCommand = new RelayCommand(() => TogglePanel(MemWritesPanel));
            ToggleWebRadarPanelCommand = new RelayCommand(() => TogglePanel(WebRadarPanel));
            ToggleDebugPanelCommand = new RelayCommand(() => TogglePanel(DebugPanel));
            ToggleVisibilityPanelCommand = new RelayCommand(() => TogglePanel(VisibilityPanel));
            ResetAllPanelsCommand = new RelayCommand(ResetAllPanels);

            // Load saved panel states from config
            LoadFromConfig();
        }

        private void TogglePanel(PanelState panel)
        {
            if (panel.IsOpen)
            {
                // Closing - remove from order tracking
                panel.IsOpen = false;
                _openPanelOrder.Remove(panel);
            }
            else
            {
                // Opening - add to end of order (most recent)
                panel.IsOpen = true;
                _openPanelOrder.Remove(panel); // Remove if already exists
                _openPanelOrder.Add(panel);
            }
        }

        /// <summary>
        /// Closes a specific panel.
        /// </summary>
        public void ClosePanel(PanelState panel)
        {
            panel.IsOpen = false;
            _openPanelOrder.Remove(panel);
        }

        /// <summary>
        /// Closes the most recently opened panel (for Escape key handling).
        /// </summary>
        /// <returns>True if a panel was closed, false if no panels were open.</returns>
        public bool CloseTopmostPanel()
        {
            // Clean up any panels that might have been closed externally
            _openPanelOrder.RemoveAll(p => !p.IsOpen);

            if (_openPanelOrder.Count == 0)
                return false;

            // Close the most recently opened panel (last in list)
            var topmost = _openPanelOrder[_openPanelOrder.Count - 1];
            topmost.IsOpen = false;
            _openPanelOrder.RemoveAt(_openPanelOrder.Count - 1);
            return true;
        }

        /// <summary>
        /// Resets all panels to their default positions and closes them.
        /// </summary>
        public void ResetAllPanels()
        {
            _openPanelOrder.Clear();
            ResetPanel(SettingsPanel, 260, 50, 400, 500);
            ResetPanel(EspPanel, 280, 70, 400, 450);
            ResetPanel(LootFiltersPanel, 300, 90, 600, 400);
            ResetPanel(ContainersPanel, 320, 110, 350, 400);
            ResetPanel(LootListPanel, 340, 130, 500, 400);
            ResetPanel(QuestsPanel, 360, 150, 400, 450);
            ResetPanel(HideoutPanel, 380, 170, 450, 500);
            ResetPanel(HotkeysPanel, 400, 50, 500, 400);
            ResetPanel(AimbotPanel, 420, 70, 400, 350);
            ResetPanel(MemWritesPanel, 440, 90, 350, 300);
            ResetPanel(WebRadarPanel, 460, 110, 450, 350);
            ResetPanel(DebugPanel, 480, 130, 400, 300);
            ResetPanel(VisibilityPanel, 500, 150, 400, 450);
        }

        private void ResetPanel(PanelState panel, double x, double y, double width, double height)
        {
            panel.X = x;
            panel.Y = y;
            panel.Width = width;
            panel.Height = height;
            panel.IsOpen = false;
        }

        #region Config Persistence

        /// <summary>
        /// Loads panel states from the application config.
        /// </summary>
        public void LoadFromConfig()
        {
            try
            {
                var config = App.Config?.PanelLayout;
                if (config == null)
                    return;

                foreach (var kvp in config.Panels)
                {
                    if (_panelMap.TryGetValue(kvp.Key, out var panel))
                    {
                        panel.IsOpen = kvp.Value.IsOpen;
                        panel.X = kvp.Value.X;
                        panel.Y = kvp.Value.Y;
                        panel.Width = kvp.Value.Width;
                        panel.Height = kvp.Value.Height;
                    }
                }
            }
            catch
            {
                // Ignore errors during load, use defaults
            }
        }

        /// <summary>
        /// Saves panel states to the application config.
        /// </summary>
        public void SaveToConfig()
        {
            try
            {
                var config = App.Config?.PanelLayout;
                if (config == null)
                    return;

                config.Panels.Clear();

                foreach (var kvp in _panelMap)
                {
                    config.Panels[kvp.Key] = new PanelStateConfig
                    {
                        IsOpen = kvp.Value.IsOpen,
                        X = kvp.Value.X,
                        Y = kvp.Value.Y,
                        Width = kvp.Value.Width,
                        Height = kvp.Value.Height
                    };
                }
            }
            catch
            {
                // Ignore errors during save
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        #endregion
    }

    /// <summary>
    /// Simple ICommand implementation for panel toggle commands.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();
    }
}
