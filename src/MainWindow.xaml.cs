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

using System.Collections.ObjectModel;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using LoneEftDmaRadar.UI.Radar.Views;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.UI.Controls;
using LoneEftDmaRadar.UI.ViewModels;
using LoneEftDmaRadar.UI.Behaviors;
using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;
using LoneEftDmaRadar.UI.ESP;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.LOS;

namespace LoneEftDmaRadar
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private Dictionary<string, DraggablePanel> _panelControls;
        private Dictionary<string, object> _originalContent;

        public MainWindow()
        {
            if (Instance is not null)
                throw new InvalidOperationException("MainWindow instance already exists. Only one instance is allowed.");
            InitializeComponent();
            this.Width = App.Config.UI.WindowSize.Width;
            this.Height = App.Config.UI.WindowSize.Height;
            if (App.Config.UI.WindowMaximized)
                this.WindowState = WindowState.Maximized;
            else
                this.WindowState = WindowState.Normal;
            DataContext = ViewModel = new MainWindowViewModel(this);
            Instance = this;

            // Initialize panel tab grouping system
            InitializePanelGrouping();

            // Start visibility worker if enabled
            if (App.Config.Visibility.Enabled)
                VisibilityManager.Start();
        }

        #region Panel Tab Grouping

        private void InitializePanelGrouping()
        {
            _panelControls = new Dictionary<string, DraggablePanel>
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

            // Snapshot original content (Tab UserControls) before any reparenting
            _originalContent = new Dictionary<string, object>();
            foreach (var kvp in _panelControls)
                _originalContent[kvp.Key] = kvp.Value.PanelContent;

            // Wire drag-drop grouping
            DraggableBehavior.PanelDropped += OnPanelDropped;

            // Wire tab group state changes
            ViewModel.PanelManager.TabGroupChanged += OnTabGroupChanged;

            // Provide tab order callback for config save
            ViewModel.PanelManager.GetTabOrder = hostName =>
            {
                if (_panelControls.TryGetValue(hostName, out var hostPanel) && hostPanel.TabItems != null)
                    return hostPanel.TabItems.Select(t => t.PanelName).ToList();
                return null;
            };

            // Wire tab detach/close events from each panel
            foreach (var panel in _panelControls.Values)
            {
                panel.TabDetachRequested += OnTabDetachRequested;
                panel.TabCloseRequested += OnTabCloseRequested;
            }

            // Rebuild groups from config after panels are loaded
            Loaded += (s, e) => ViewModel.PanelManager.RebuildGroupsFromConfig();
        }

        private void OnPanelDropped(object sender, (string Source, string Target) args)
        {
            ViewModel.PanelManager.GroupPanel(args.Source, args.Target);
        }

        private void OnTabGroupChanged(object sender, TabGroupChangedEventArgs e)
        {
            switch (e.Action)
            {
                case TabGroupAction.Added:
                    ReparentContentToHost(e.HostName, e.PanelName);
                    break;
                case TabGroupAction.Removed:
                    RestoreContentToStandalone(e.PanelName, e.HostName);
                    break;
                case TabGroupAction.Rebuilt:
                    RestoreAllContent();
                    break;
            }
        }

        private void OnTabDetachRequested(object sender, string panelName)
        {
            ViewModel.PanelManager.UngroupPanel(panelName);

            // Begin dragging the detached panel immediately under the cursor
            if (_panelControls.TryGetValue(panelName, out var panel))
            {
                System.Windows.Controls.Canvas.SetZIndex(panel, int.MaxValue);
                // Defer to let layout update after visibility change, then start drag
                Dispatcher.InvokeAsync(() => DraggableBehavior.BeginDrag(panel),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void OnTabCloseRequested(object sender, string panelName)
        {
            ViewModel.PanelManager.UngroupPanel(panelName);
            // Also close the now-standalone panel
            if (ViewModel.PanelManager.PanelMap.TryGetValue(panelName, out var state))
                ViewModel.PanelManager.ClosePanel(state);
        }

        private void ReparentContentToHost(string hostName, string sourceName)
        {
            if (!_panelControls.TryGetValue(hostName, out var hostPanel)) return;
            if (!_panelControls.TryGetValue(sourceName, out var sourcePanel)) return;
            if (!_originalContent.TryGetValue(sourceName, out var sourceContent)) return;

            // Remove content from source panel (WPF single-parent rule)
            sourcePanel.PanelContent = null;

            // Transition host to tab mode if not already
            if (hostPanel.TabItems == null)
            {
                // Clear host's single-content mode first so the UserControl is unparented
                hostPanel.PanelContent = null;

                hostPanel.TabItems = new ObservableCollection<TabItemModel>();

                // Add host's own content as first tab
                if (_originalContent.TryGetValue(hostName, out var hostContent))
                {
                    hostPanel.TabItems.Add(new TabItemModel
                    {
                        PanelName = hostName,
                        Title = PanelManagerViewModel.GetPanelTitle(hostName),
                        Content = hostContent
                    });
                    // Activate host tab immediately so content is visible
                    hostPanel.ActiveTab = hostName;
                }
            }

            // Check if source already exists in tabs (e.g., during rebuild or sidebar toggle)
            var existingTab = hostPanel.TabItems.FirstOrDefault(t => t.PanelName == sourceName);
            if (existingTab != null)
            {
                hostPanel.ActiveTab = sourceName;
                return;
            }

            // Add source content as new tab
            hostPanel.TabItems.Add(new TabItemModel
            {
                PanelName = sourceName,
                Title = PanelManagerViewModel.GetPanelTitle(sourceName),
                Content = sourceContent
            });

            // Activate the host's own tab (keep showing what was visible)
            hostPanel.ActiveTab = hostName;
        }

        private void RestoreContentToStandalone(string panelName, string hostName)
        {
            if (!_panelControls.TryGetValue(panelName, out var panel)) return;
            if (!_originalContent.TryGetValue(panelName, out var content)) return;

            // Remove from host's TabItems
            if (hostName != null && _panelControls.TryGetValue(hostName, out var hostPanel) && hostPanel.TabItems != null)
            {
                var tab = hostPanel.TabItems.FirstOrDefault(t => t.PanelName == panelName);
                if (tab != null)
                {
                    tab.Content = null; // clear parent reference
                    hostPanel.TabItems.Remove(tab);

                    // If host down to 1 tab, revert to single-content mode
                    if (hostPanel.TabItems.Count <= 1)
                    {
                        var lastTab = hostPanel.TabItems.FirstOrDefault();
                        hostPanel.TabItems.Clear();
                        hostPanel.TabItems = null;

                        if (lastTab != null)
                        {
                            lastTab.Content = null;
                            if (_originalContent.TryGetValue(lastTab.PanelName, out var lastContent))
                                hostPanel.PanelContent = lastContent;
                        }
                    }
                    else
                    {
                        // Switch to first remaining tab if active tab was removed
                        if (hostPanel.ActiveTab == panelName)
                            hostPanel.ActiveTab = hostPanel.TabItems[0].PanelName;
                    }
                }
            }

            // Restore content to the standalone panel
            panel.PanelContent = content;
        }

        private void RestoreAllContent()
        {
            // Clear all tab collections
            foreach (var panel in _panelControls.Values)
            {
                if (panel.TabItems != null)
                {
                    foreach (var tab in panel.TabItems)
                        tab.Content = null;
                    panel.TabItems.Clear();
                    panel.TabItems = null;
                }
            }

            // Restore all original content
            foreach (var kvp in _originalContent)
            {
                if (_panelControls.TryGetValue(kvp.Key, out var panel))
                    panel.PanelContent = kvp.Value;
            }
        }

        #endregion

        private void BtnToggleESP_Click(object sender, RoutedEventArgs e)
        {
            ESPManager.ToggleESP();
        }

        private void MenuDebugLogger_Click(object sender, RoutedEventArgs e)
        {
            DebugLogger.Toggle();
        }

        /// <summary>
        /// Handles close requests from floating panels.
        /// </summary>
        private void Panel_CloseRequested(object sender, RoutedEventArgs e)
        {
            if (sender is DraggablePanel panel)
            {
                var pm = ViewModel.PanelManager;
                if (pm.PanelMap.TryGetValue(panel.Name, out var state))
                    pm.ClosePanel(state);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            // Force exit to ensure no background threads keep the process alive
            Environment.Exit(0);
        }

        /// <summary>
        /// Global Singleton instance of the MainWindow.
        /// </summary>
        [MaybeNull]
        public static MainWindow Instance { get; private set; }

        /// <summary>
        /// ViewModel for the MainWindow.
        /// </summary>
        public MainWindowViewModel ViewModel { get; }

        #region Panel Content Accessors

        /// <summary>
        /// Gets the SettingsTab — checks original content map first (handles tabbed mode).
        /// </summary>
        public SettingsTab Settings =>
            (_originalContent?.GetValueOrDefault("SettingsPanel") as SettingsTab)
            ?? (SettingsPanel?.PanelContent as SettingsTab);

        /// <summary>
        /// Gets the DeviceAimbotTab — checks original content map first (handles tabbed mode).
        /// </summary>
        public DeviceAimbotTab DeviceAimbot =>
            (_originalContent?.GetValueOrDefault("AimbotPanel") as DeviceAimbotTab)
            ?? (AimbotPanel?.PanelContent as DeviceAimbotTab);

        #endregion

        /// <summary>
        /// Make sure the program really closes.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            try
            {
                App.Config.UI.WindowSize = new Size(this.Width, this.Height);
                App.Config.UI.WindowMaximized = this.WindowState == WindowState.Maximized;
                if (Radar?.ViewModel?.AimviewWidget is AimviewWidget aimviewWidget)
                {
                    App.Config.AimviewWidget.Location = aimviewWidget.ClientRectangle;
                    App.Config.AimviewWidget.Minimized = aimviewWidget.Minimized;
                }
                if (Radar?.ViewModel?.InfoWidget is PlayerInfoWidget infoWidget)
                {
                    App.Config.InfoWidget.Location = infoWidget.Rectangle;
                    App.Config.InfoWidget.Minimized = infoWidget.Minimized;
                }

                // Save panel layout states
                ViewModel?.PanelManager?.SaveToConfig();

                App.Config.Save(); // Save config before Environment.Exit(0) in OnClosed kills the process

                // Cleanup
                DraggableBehavior.PanelDropped -= OnPanelDropped;
                Memory.Dispose(); // Close FPGA
                VisibilityManager.Stop();
                ESPManager.CloseESP();
                DebugLogger.Close();
            }
            finally
            {
                base.OnClosing(e);
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            try
            {
                // Escape key closes the topmost open panel
                if (e.Key is Key.Escape)
                {
                    if (ViewModel?.PanelManager?.CloseTopmostPanel() == true)
                    {
                        e.Handled = true;
                        return;
                    }
                }

                if (!Radar?.IsVisible ?? false)
                    return; // Ignore if radar is not visible
                if (e.Key is Key.F1)
                {
                    Radar?.ViewModel?.ZoomIn(5);
                }
                else if (e.Key is Key.F2)
                {
                    Radar?.ViewModel?.ZoomOut(5);
                }
                else if (e.Key is Key.F3 && Settings?.ViewModel is SettingsViewModel vm)
                {
                    vm.ShowLoot = !vm.ShowLoot; // Toggle loot
                }
                else if (e.Key is Key.F11)
                {
                    var toFullscreen = this.WindowStyle is WindowStyle.SingleBorderWindow;
                    ViewModel!.ToggleFullscreen(toFullscreen);
                }
            }
            finally
            {
                base.OnPreviewKeyDown(e);
            }
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            const double wheelDelta = 120d; // Standard mouse wheel delta value
            try
            {
                if (!Radar?.IsVisible ?? false)
                    return; // Ignore if radar is not visible

                // Don't zoom if scrolling inside a panel (check if mouse is over a ScrollViewer)
                if (e.OriginalSource is DependencyObject source)
                {
                    var parent = source;
                    while (parent != null)
                    {
                        if (parent is System.Windows.Controls.ScrollViewer)
                            return; // Let the ScrollViewer handle the wheel event
                        parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
                    }
                }

                if (e.Delta > 0) // mouse wheel up (zoom in)
                {
                    int amt = (int)((e.Delta / wheelDelta) * 5d); // Calculate zoom amount based on number of deltas
                    Radar?.ViewModel?.ZoomIn(amt);
                }
                else if (e.Delta < 0) // mouse wheel down (zoom out)
                {
                    int amt = (int)((e.Delta / -wheelDelta) * 5d); // Calculate zoom amount based on number of deltas
                    Radar?.ViewModel?.ZoomOut(amt);
                }
            }
            finally
            {
                base.OnPreviewMouseWheel(e);
            }
        }
    }
}
