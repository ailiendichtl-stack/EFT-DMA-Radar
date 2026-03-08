/*
 * Twilight PVE Radar - WPF Modular GUI
 * PanelManagerViewModel: Manages visibility, position, and size of all floating panels
 */

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LoneEftDmaRadar.UI.ViewModels
{
    /// <summary>
    /// Stores the state of a single panel (position, size, visibility, group membership).
    /// </summary>
    public class PanelState : INotifyPropertyChanged
    {
        private bool _isOpen;
        private double _x = 100;
        private double _y = 100;
        private double _width = 400;
        private double _height = 400;
        private string _groupHost;

        public bool IsOpen
        {
            get => _isOpen;
            set { _isOpen = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVisible)); }
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

        /// <summary>
        /// Name of the host panel this is grouped into, or null if standalone.
        /// </summary>
        public string GroupHost
        {
            get => _groupHost;
            set { _groupHost = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsGrouped)); OnPropertyChanged(nameof(IsVisible)); }
        }

        public int GroupOrder { get; set; }

        public bool IsGrouped => !string.IsNullOrEmpty(GroupHost);

        /// <summary>
        /// True when the standalone panel should be visible on canvas.
        /// False when grouped (content lives in host's tabs instead).
        /// </summary>
        public bool IsVisible => IsOpen && !IsGrouped;

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Model for a single tab within a grouped panel.
    /// </summary>
    public class TabItemModel : INotifyPropertyChanged
    {
        public string PanelName { get; set; }
        public string Title { get; set; }
        public object Content { get; set; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Event args for tab group changes.
    /// </summary>
    public enum TabGroupAction { Added, Removed, Rebuilt }
    public class TabGroupChangedEventArgs : EventArgs
    {
        public TabGroupAction Action { get; set; }
        public string HostName { get; set; }
        public string PanelName { get; set; }
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
        public PanelState PlayersPanel { get; } = new() { X = 520, Y = 170, Width = 450, Height = 500 };
        public PanelState LootPanel { get; } = new() { X = 300, Y = 50, Width = 420, Height = 450 };

        #endregion

        #region Panel Title Map

        private static readonly Dictionary<string, string> _panelTitles = new()
        {
            ["SettingsPanel"] = "\u2699 Settings",
            ["EspPanel"] = "\U0001F441 ESP Fuser",
            ["LootFiltersPanel"] = "\U0001F3AF Loot Filters",
            ["ContainersPanel"] = "\U0001F4E6 Containers",
            ["LootListPanel"] = "\U0001F4CB Loot List",
            ["QuestsPanel"] = "\U0001F4CD Quest Helper",
            ["HideoutPanel"] = "\U0001F3E0 Hideout Helper",
            ["HotkeysPanel"] = "\u2328 Hotkeys",
            ["AimbotPanel"] = "\U0001F3AF Device Aimbot",
            ["MemWritesPanel"] = "\U0001F4BE MemWrites",
            ["WebRadarPanel"] = "\U0001F310 Web Radar",
            ["DebugPanel"] = "\U0001F527 Debug",
            ["VisibilityPanel"] = "LOS Settings",
            ["PlayersPanel"] = "\U0001F465 Players",
            ["LootPanel"] = "\U0001F39A Loot Settings"
        };

        public static string GetPanelTitle(string name) => _panelTitles.GetValueOrDefault(name, name);

        #endregion

        #region Map Free State

        private bool _isMapFreeEnabled;
        public bool IsMapFreeEnabled
        {
            get => _isMapFreeEnabled;
            set
            {
                if (_isMapFreeEnabled == value) return;
                _isMapFreeEnabled = value;
                MapFreeButtonText = _isMapFreeEnabled ? "\U0001F4CC Map Follow" : "\U0001F5FA Map Free";
                OnPropertyChanged(nameof(IsMapFreeEnabled));
            }
        }

        private string _mapFreeButtonText = "\U0001F5FA Map Free";
        public string MapFreeButtonText
        {
            get => _mapFreeButtonText;
            set
            {
                if (_mapFreeButtonText == value) return;
                _mapFreeButtonText = value;
                OnPropertyChanged(nameof(MapFreeButtonText));
            }
        }

        public ICommand ToggleMapFreeCommand { get; }

        #endregion

        #region Panel Name Mapping

        private readonly Dictionary<string, PanelState> _panelMap;
        private readonly List<PanelState> _openPanelOrder = new();

        /// <summary>
        /// Provides read-only access to panel map for external lookups.
        /// </summary>
        public IReadOnlyDictionary<string, PanelState> PanelMap => _panelMap;

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

        #region Tab Group Events

        /// <summary>
        /// Callback to get the current tab order for a host panel.
        /// Returns panel names in display order, or null if not a tab host.
        /// Set by MainWindow after initialization.
        /// </summary>
        public Func<string, IReadOnlyList<string>> GetTabOrder { get; set; }

        /// <summary>
        /// Fired when tab groups change. MainWindow listens to perform content reparenting.
        /// </summary>
        public event EventHandler<TabGroupChangedEventArgs> TabGroupChanged;

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
        public ICommand TogglePlayersPanelCommand { get; }
        public ICommand ToggleLootPanelCommand { get; }
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
                ["VisibilityPanel"] = VisibilityPanel,
                ["PlayersPanel"] = PlayersPanel,
                ["LootPanel"] = LootPanel
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
            TogglePlayersPanelCommand = new RelayCommand(() => TogglePanel(PlayersPanel));
            ToggleLootPanelCommand = new RelayCommand(() => TogglePanel(LootPanel));
            ToggleMapFreeCommand = new RelayCommand(() => IsMapFreeEnabled = !IsMapFreeEnabled);
            ResetAllPanelsCommand = new RelayCommand(ResetAllPanels);

            // Load saved panel states from config
            LoadFromConfig();
        }

        #region Panel Toggle / Close

        private void TogglePanel(PanelState panel)
        {
            var panelName = GetPanelName(panel);

            if (panel.IsOpen)
            {
                if (panel.IsGrouped && panelName != null)
                {
                    // Grouped non-host panel — remove from group and close
                    UngroupPanel(panelName);
                }
                // Close (works for standalone, host, or freshly ungrouped)
                ClosePanel(panel);
            }
            else
            {
                if (panel.IsGrouped && panelName != null)
                {
                    // Panel is grouped but closed (e.g. after host was closed) — reopen host and activate tab
                    var host = _panelMap.GetValueOrDefault(panel.GroupHost);
                    if (host != null)
                    {
                        if (!host.IsOpen)
                        {
                            host.IsOpen = true;
                            _openPanelOrder.Remove(host);
                            _openPanelOrder.Add(host);
                        }
                        panel.IsOpen = true;
                        TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                        {
                            Action = TabGroupAction.Added,
                            HostName = panel.GroupHost,
                            PanelName = panelName
                        });
                    }
                }
                else
                {
                    // Normal open
                    panel.IsOpen = true;
                    _openPanelOrder.Remove(panel);
                    _openPanelOrder.Add(panel);
                }
            }
        }

        /// <summary>
        /// Closes a specific panel, handling group membership gracefully:
        /// - Non-host member: just close (already ungrouped by caller)
        /// - Host with 1 member: dissolve group, close host
        /// - Host with 2+ members: migrate group to next member, close old host
        /// </summary>
        public void ClosePanel(PanelState panel)
        {
            var panelName = GetPanelName(panel);
            if (panelName != null)
            {
                var members = GetGroupMembers(panelName);
                var groupedMembers = members.Where(n => n != panelName).ToList();

                if (groupedMembers.Count == 1)
                {
                    // Only one member left — dissolve the group entirely
                    var memberName = groupedMembers[0];
                    if (_panelMap.TryGetValue(memberName, out var member))
                    {
                        member.GroupHost = null;
                        TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                        {
                            Action = TabGroupAction.Removed,
                            HostName = panelName,
                            PanelName = memberName
                        });
                        // Member becomes standalone (IsOpen stays true, IsVisible kicks in)
                    }
                }
                else if (groupedMembers.Count > 1)
                {
                    // Multiple members — migrate group to the first remaining member
                    var newHostName = groupedMembers[0];

                    // Remove all tabs from old host
                    foreach (var memberName in groupedMembers)
                    {
                        _panelMap[memberName].GroupHost = null;
                        TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                        {
                            Action = TabGroupAction.Removed,
                            HostName = panelName,
                            PanelName = memberName
                        });
                    }

                    // Re-group remaining members (skip first, it becomes the new host)
                    foreach (var memberName in groupedMembers.Skip(1))
                    {
                        _panelMap[memberName].GroupHost = newHostName;
                        TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                        {
                            Action = TabGroupAction.Added,
                            HostName = newHostName,
                            PanelName = memberName
                        });
                    }
                }
            }

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
            ClosePanel(topmost);
            return true;
        }

        #endregion

        #region Tab Grouping

        /// <summary>
        /// Group sourcePanel into targetPanel as a new tab.
        /// </summary>
        public void GroupPanel(string sourceName, string targetName)
        {
            if (sourceName == targetName) return;
            if (!_panelMap.ContainsKey(sourceName) || !_panelMap.ContainsKey(targetName)) return;

            var source = _panelMap[sourceName];
            var target = _panelMap[targetName];

            // If target is already grouped into another host, use that host
            if (target.IsGrouped)
            {
                targetName = target.GroupHost;
                target = _panelMap[targetName];
            }

            // If source is grouped elsewhere, ungroup first
            if (source.IsGrouped)
                UngroupPanel(sourceName);

            // If source is a host with grouped tabs, move all tabs to target
            var sourceTabs = GetGroupMembers(sourceName);
            if (sourceTabs.Count > 1)
            {
                // Move all tabs (except host itself which we handle after)
                foreach (var tabName in sourceTabs.Where(n => n != sourceName).ToList())
                {
                    _panelMap[tabName].GroupHost = null;
                    TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                    {
                        Action = TabGroupAction.Removed,
                        HostName = sourceName,
                        PanelName = tabName
                    });
                }
                // Now group each into target
                foreach (var tabName in sourceTabs.Where(n => n != sourceName))
                {
                    _panelMap[tabName].GroupHost = targetName;
                    TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                    {
                        Action = TabGroupAction.Added,
                        HostName = targetName,
                        PanelName = tabName
                    });
                }
            }

            // Group the source itself (IsOpen stays true for sidebar indicator)
            source.GroupHost = targetName;

            TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
            {
                Action = TabGroupAction.Added,
                HostName = targetName,
                PanelName = sourceName
            });
        }

        /// <summary>
        /// Remove a panel from its tab group back to standalone.
        /// </summary>
        public void UngroupPanel(string panelName)
        {
            if (!_panelMap.TryGetValue(panelName, out var panel)) return;
            if (!panel.IsGrouped) return;

            var hostName = panel.GroupHost;
            var host = _panelMap.GetValueOrDefault(hostName);

            panel.GroupHost = null;

            // Position at the host's location (same spot, on top via z-order)
            if (host != null)
            {
                panel.X = host.X;
                panel.Y = host.Y;
                panel.Width = host.Width;
                panel.Height = host.Height;
            }

            panel.IsOpen = true;
            _openPanelOrder.Remove(panel);
            _openPanelOrder.Add(panel);

            TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
            {
                Action = TabGroupAction.Removed,
                HostName = hostName,
                PanelName = panelName
            });
        }

        /// <summary>
        /// Gets all panel names grouped into the given host (including the host itself first).
        /// </summary>
        public List<string> GetGroupMembers(string hostName)
        {
            var members = new List<string> { hostName };
            foreach (var kvp in _panelMap)
            {
                if (kvp.Value.GroupHost == hostName)
                    members.Add(kvp.Key);
            }
            return members;
        }

        /// <summary>
        /// Dissolves all tab groups.
        /// </summary>
        public void DissolveAllGroups()
        {
            foreach (var kvp in _panelMap)
                kvp.Value.GroupHost = null;

            TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
            {
                Action = TabGroupAction.Rebuilt
            });
        }

        /// <summary>
        /// Gets the panel name for a given PanelState.
        /// </summary>
        public string GetPanelName(PanelState state)
        {
            foreach (var kvp in _panelMap)
            {
                if (kvp.Value == state)
                    return kvp.Key;
            }
            return null;
        }

        #endregion

        #region Reset

        /// <summary>
        /// Resets all panels to their default positions and closes them.
        /// </summary>
        public void ResetAllPanels()
        {
            DissolveAllGroups();
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
            ResetPanel(PlayersPanel, 520, 170, 450, 500);
            ResetPanel(LootPanel, 300, 50, 420, 450);
        }

        private void ResetPanel(PanelState panel, double x, double y, double width, double height)
        {
            panel.X = x;
            panel.Y = y;
            panel.Width = width;
            panel.Height = height;
            panel.IsOpen = false;
        }

        #endregion

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
                        panel.X = kvp.Value.X;
                        panel.Y = kvp.Value.Y;
                        panel.Width = kvp.Value.Width;
                        panel.Height = kvp.Value.Height;
                        panel.GroupHost = kvp.Value.GroupHost;
                        panel.GroupOrder = kvp.Value.GroupOrder;

                        // IsOpen drives sidebar indicator; IsVisible (IsOpen && !IsGrouped) drives canvas visibility
                        panel.IsOpen = kvp.Value.IsOpen;
                    }
                }
            }
            catch
            {
                // Ignore errors during load, use defaults
            }
        }

        /// <summary>
        /// Rebuilds tab groups from loaded config state.
        /// Called by MainWindow after panels are loaded and content is available.
        /// </summary>
        public void RebuildGroupsFromConfig()
        {
            // Collect grouped panels per host, sorted by GroupOrder
            var hostGroups = new Dictionary<string, List<(string Name, int Order)>>();
            foreach (var kvp in _panelMap)
            {
                if (!kvp.Value.IsGrouped || !_panelMap.ContainsKey(kvp.Value.GroupHost))
                    continue;

                var hostName = kvp.Value.GroupHost;
                if (!hostGroups.TryGetValue(hostName, out var list))
                {
                    list = new List<(string, int)>();
                    hostGroups[hostName] = list;
                }
                list.Add((kvp.Key, kvp.Value.GroupOrder));
            }

            if (hostGroups.Count == 0) return;

            // Fire Added events in GroupOrder so tabs restore in correct order
            foreach (var (hostName, members) in hostGroups)
            {
                members.Sort((a, b) => a.Order.CompareTo(b.Order));
                foreach (var (panelName, _) in members)
                {
                    TabGroupChanged?.Invoke(this, new TabGroupChangedEventArgs
                    {
                        Action = TabGroupAction.Added,
                        HostName = hostName,
                        PanelName = panelName
                    });
                }
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

                // Build group order from actual tab order in the UI
                var groupOrders = new Dictionary<string, int>();
                if (GetTabOrder != null)
                {
                    var hosts = new HashSet<string>();
                    foreach (var kvp in _panelMap)
                        if (kvp.Value.IsGrouped)
                            hosts.Add(kvp.Value.GroupHost);

                    foreach (var hostName in hosts)
                    {
                        var tabOrder = GetTabOrder(hostName);
                        if (tabOrder != null)
                        {
                            for (int i = 0; i < tabOrder.Count; i++)
                                groupOrders[tabOrder[i]] = i;
                        }
                    }
                }

                foreach (var kvp in _panelMap)
                {
                    config.Panels[kvp.Key] = new PanelStateConfig
                    {
                        IsOpen = kvp.Value.IsOpen,
                        X = kvp.Value.X,
                        Y = kvp.Value.Y,
                        Width = kvp.Value.Width,
                        Height = kvp.Value.Height,
                        GroupHost = kvp.Value.GroupHost,
                        GroupOrder = groupOrders.GetValueOrDefault(kvp.Key, 0)
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
