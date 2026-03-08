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

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.UI.ColorPicker;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.Views;
using LoneEftDmaRadar.UI.Skia;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly SettingsTab _parent;
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public SettingsViewModel(SettingsTab parent)
        {
            _parent = parent ?? throw new ArgumentNullException(nameof(parent));
            RestartRadarCommand = new SimpleCommand(OnRestartRadar);
            OpenColorPickerCommand = new SimpleCommand(OnOpenColorPicker);
            BackupConfigCommand = new SimpleCommand(OnBackupConfig);
            OpenConfigCommand = new SimpleCommand(OnOpenConfig);
            RefreshDataCommand = new SimpleCommand(OnRefreshData);
            LoadProfileCommand = new SimpleCommand(OnLoadProfile);
            SaveProfileCommand = new SimpleCommand(OnSaveProfile);
            SaveAsProfileCommand = new SimpleCommand(OnSaveAsProfile);
            DeleteProfileCommand = new SimpleCommand(OnDeleteProfile);
            SetScaleValues(UIScale);
            RefreshProfileList();
        }

        #region Config Profiles

        private List<string> _profileList = new();
        public List<string> ProfileList
        {
            get => _profileList;
            set
            {
                _profileList = value;
                OnPropertyChanged(nameof(ProfileList));
            }
        }

        private string _selectedProfile;
        public string SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (_selectedProfile != value)
                {
                    _selectedProfile = value;
                    OnPropertyChanged(nameof(SelectedProfile));
                }
            }
        }

        public string ActiveProfileLabel =>
            string.IsNullOrEmpty(ConfigProfileManager.ActiveProfile)
                ? "Active: (default)"
                : $"Active: {ConfigProfileManager.ActiveProfile}";

        public ICommand LoadProfileCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand SaveAsProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }

        private void RefreshProfileList()
        {
            ProfileList = ConfigProfileManager.GetAvailableProfiles();
            if (ProfileList.Count > 0 && SelectedProfile is null)
                SelectedProfile = ProfileList.FirstOrDefault();
            OnPropertyChanged(nameof(ActiveProfileLabel));
        }

        private void OnLoadProfile()
        {
            if (string.IsNullOrEmpty(SelectedProfile))
                return;
            try
            {
                ConfigProfileManager.LoadProfile(SelectedProfile);
                OnPropertyChanged(nameof(ActiveProfileLabel));
                MessageBox.Show(MainWindow.Instance, $"Profile '{SelectedProfile}' loaded.", "Config Profiles");
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Config Profiles", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveProfile()
        {
            if (string.IsNullOrEmpty(SelectedProfile))
            {
                OnSaveAsProfile();
                return;
            }
            try
            {
                ConfigProfileManager.SaveProfile(SelectedProfile);
                OnPropertyChanged(nameof(ActiveProfileLabel));
                MessageBox.Show(MainWindow.Instance, $"Profile '{SelectedProfile}' saved.", "Config Profiles");
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Config Profiles", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveAsProfile()
        {
            // Simple input dialog using WPF Window
            var dlg = new Window
            {
                Title = "Save As New Profile",
                Width = 300,
                Height = 130,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = MainWindow.Instance,
                ResizeMode = ResizeMode.NoResize
            };
            var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(10) };
            var tb = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 8) };
            var btn = new System.Windows.Controls.Button
            {
                Content = "Save",
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Padding = new Thickness(16, 4, 16, 4)
            };
            btn.Click += (s, e) => { dlg.DialogResult = true; dlg.Close(); };
            sp.Children.Add(new System.Windows.Controls.TextBlock { Text = "Enter profile name:" });
            sp.Children.Add(tb);
            sp.Children.Add(btn);
            dlg.Content = sp;

            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(tb.Text))
                return;

            string name = tb.Text.Trim();
            try
            {
                ConfigProfileManager.SaveProfile(name);
                RefreshProfileList();
                SelectedProfile = name;
                OnPropertyChanged(nameof(ActiveProfileLabel));
                MessageBox.Show(MainWindow.Instance, $"Profile '{name}' saved.", "Config Profiles");
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Config Profiles", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDeleteProfile()
        {
            if (string.IsNullOrEmpty(SelectedProfile))
                return;
            if (MessageBox.Show(MainWindow.Instance, $"Delete profile '{SelectedProfile}'?", "Config Profiles",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            ConfigProfileManager.DeleteProfile(SelectedProfile);
            RefreshProfileList();
            OnPropertyChanged(nameof(ActiveProfileLabel));
        }

        #endregion

        #region General Settings

        public ICommand RestartRadarCommand { get; }
        private void OnRestartRadar() =>
            Memory.RestartRadar();

        private bool _colorPickerIsEnabled = true;
        public bool ColorPickerIsEnabled
        {
            get => _colorPickerIsEnabled;
            set
            {
                if (_colorPickerIsEnabled != value)
                {
                    _colorPickerIsEnabled = value;
                    OnPropertyChanged(nameof(ColorPickerIsEnabled));
                }
            }
        }
        public ICommand OpenColorPickerCommand { get; }
        private void OnOpenColorPicker()
        {
            ColorPickerIsEnabled = false;
            try
            {
                var wnd = new ColorPickerWindow()
                {
                    Owner = MainWindow.Instance
                };
                wnd.ShowDialog();
            }
            finally
            {
                ColorPickerIsEnabled = true;
            }
        }

        public ICommand BackupConfigCommand { get; }
        private async void OnBackupConfig()
        {
            try
            {
                var backupFile = Path.Combine(App.ConfigPath.FullName, $"{EftDmaConfig.Filename}.userbak");
                if (File.Exists(backupFile) &&
                    MessageBox.Show(MainWindow.Instance, "Overwrite backup?", "Backup Config", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;

                await File.WriteAllTextAsync(backupFile, JsonSerializer.Serialize(App.Config, new JsonSerializerOptions { WriteIndented = true }));
                MessageBox.Show(MainWindow.Instance, $"Backed up to {backupFile}", "Backup Config");
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Backup Config", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public ICommand OpenConfigCommand { get; }
        private async void OnOpenConfig()
        {
            try
            {
                Process.Start(new ProcessStartInfo(App.ConfigPath.FullName) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Save Config", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public ICommand RefreshDataCommand { get; }
        private async void OnRefreshData()
        {
            try
            {
                await TarkovDataManager.ForceRefreshDataAsync();
                // Refresh hideout manager and UI with new data
                Tarkov.GameWorld.Hideout.HideoutManager.Instance.RefreshTrackedItems();
                HideoutViewModel.Instance?.ReloadStations();
                MessageBox.Show(MainWindow.Instance, $"Data refreshed!\nTasks: {TarkovDataManager.TaskData?.Count ?? 0}\nItems: {TarkovDataManager.AllItems?.Count ?? 0}\nHideout Stations: {TarkovDataManager.HideoutData?.Count ?? 0}", "Refresh Data");
            }
            catch (Exception ex)
            {
                MessageBox.Show(MainWindow.Instance, $"Error: {ex.Message}", "Refresh Data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public int AimlineLength
        {
            get => App.Config.UI.AimLineLength;
            set
            {
                if (App.Config.UI.AimLineLength != value)
                {
                    App.Config.UI.AimLineLength = value;
                    OnPropertyChanged(nameof(AimlineLength));
                }
            }
        }

        public int MaxDistance
        {
            get => (int)Math.Round(App.Config.UI.MaxDistance);
            set
            {
                if (App.Config.UI.MaxDistance != value)
                {
                    App.Config.UI.MaxDistance = value;
                    OnPropertyChanged(nameof(MaxDistance));
                }
            }
        }

        public float UIScale
        {
            get => App.Config.UI.UIScale;
            set
            {
                if (App.Config.UI.UIScale == value)
                    return;
                App.Config.UI.UIScale = value;
                SetScaleValues(value);
                OnPropertyChanged(nameof(UIScale));
            }
        }

        private static void SetScaleValues(float newScale)
        {
            // Update Widgets
            MainWindow.Instance?.Radar?.ViewModel?.AimviewWidget?.SetScaleFactor(newScale);
            MainWindow.Instance?.Radar?.ViewModel?.InfoWidget?.SetScaleFactor(newScale);

            #region UpdatePaints

            /// Outlines
            SKPaints.TextOutline.StrokeWidth = 2f * newScale;
            // Shape Outline is computed before usage due to different stroke widths

            SKPaints.PaintConnectorGroup.StrokeWidth = 2.25f * newScale;
            SKPaints.PaintMouseoverGroup.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintLocalPlayer.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintTeammate.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintPMC.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintWatchlist.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintStreamer.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintScav.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintRaider.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintBoss.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintFocused.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintPScav.StrokeWidth = 1.66f * newScale;
            SKPaints.PaintCorpse.StrokeWidth = 3 * newScale;
            SKPaints.PaintMeds.StrokeWidth = 3 * newScale;
            SKPaints.PaintFood.StrokeWidth = 3 * newScale;
            SKPaints.PaintBackpacks.StrokeWidth = 3 * newScale;
            SKPaints.PaintQuestItem.StrokeWidth = 3 * newScale;
            SKPaints.QuestHelperPaint.StrokeWidth = 3 * newScale;
            SKPaints.PaintDeathMarker.StrokeWidth = 3 * newScale;
            SKPaints.PaintLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintImportantLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintContainerLoot.StrokeWidth = 3 * newScale;
            SKPaints.PaintTransparentBacker.StrokeWidth = 1 * newScale;
            SKPaints.PaintExplosives.StrokeWidth = 3 * newScale;
            SKPaints.PaintExfilOpen.StrokeWidth = 1 * newScale;
            SKPaints.PaintExfilTransit.StrokeWidth = 1 * newScale;
            // Fonts
            SKFonts.UIRegular.Size = 12f * newScale;
            SKFonts.UILarge.Size = 48f * newScale;
            SKFonts.InfoWidgetFont.Size = 12f * newScale;
            // Loot Paints
            LootItem.ScaleLootPaints(newScale);

            #endregion
        }

        private bool _showMapSetupHelper;
        public bool ShowMapSetupHelper
        {
            get => _showMapSetupHelper;
            set
            {
                if (_showMapSetupHelper != value)
                {
                    _showMapSetupHelper = value;
                    if (MainWindow.Instance?.Radar?.MapSetupHelper?.ViewModel is MapSetupHelperViewModel vm)
                    {
                        vm.IsVisible = value;
                    }
                    OnPropertyChanged(nameof(ShowMapSetupHelper));
                }
            }
        }

        public bool AimviewWidget
        {
            get => App.Config.AimviewWidget.Enabled;
            set
            {
                if (App.Config.AimviewWidget.Enabled != value)
                {
                    App.Config.AimviewWidget.Enabled = value;
                    OnPropertyChanged(nameof(AimviewWidget));
                }
            }
        }

        public bool PlayerInfoWidget
        {
            get => App.Config.InfoWidget.Enabled;
            set
            {
                if (App.Config.InfoWidget.Enabled != value)
                {
                    App.Config.InfoWidget.Enabled = value;
                    OnPropertyChanged(nameof(PlayerInfoWidget));
                }
            }
        }

        public bool ConnectGroups
        {
            get => App.Config.UI.ConnectGroups;
            set
            {
                if (App.Config.UI.ConnectGroups != value)
                {
                    App.Config.UI.ConnectGroups = value;
                    OnPropertyChanged(nameof(ConnectGroups));
                }
            }
        }

        public bool AutoGroups
        {
            get => App.Config.Misc.AutoGroups;
            set
            {
                if (App.Config.Misc.AutoGroups != value)
                {
                    App.Config.Misc.AutoGroups = value;
                    OnPropertyChanged(nameof(AutoGroups));
                }
            }
        }

        public bool HideNames
        {
            get => App.Config.UI.HideNames;
            set
            {
                if (App.Config.UI.HideNames != value)
                {
                    App.Config.UI.HideNames = value;
                    OnPropertyChanged(nameof(HideNames));
                }
            }
        }

        public bool ShowPlayerValue
        {
            get => App.Config.UI.ShowPlayerValue;
            set
            {
                if (App.Config.UI.ShowPlayerValue != value)
                {
                    App.Config.UI.ShowPlayerValue = value;
                    OnPropertyChanged(nameof(ShowPlayerValue));
                }
            }
        }

        public bool ShowMines
        {
            get => App.Config.UI.ShowMines;
            set
            {
                if (App.Config.UI.ShowMines != value)
                {
                    App.Config.UI.ShowMines = value;
                    OnPropertyChanged(nameof(ShowMines));
                }
            }
        }

        public bool TeammateAimlines
        {
            get => App.Config.UI.TeammateAimlines;
            set
            {
                if (App.Config.UI.TeammateAimlines != value)
                {
                    App.Config.UI.TeammateAimlines = value;
                    OnPropertyChanged(nameof(TeammateAimlines));
                }
            }
        }

        public bool AIAimlines
        {
            get => App.Config.UI.AIAimlines;
            set
            {
                if (App.Config.UI.AIAimlines != value)
                {
                    App.Config.UI.AIAimlines = value;
                    OnPropertyChanged(nameof(AIAimlines));
                }
            }
        }

        public bool MarkSusPlayers
        {
            get => App.Config.UI.MarkSusPlayers;
            set
            {
                if (App.Config.UI.MarkSusPlayers != value)
                {
                    App.Config.UI.MarkSusPlayers = value;
                    OnPropertyChanged(nameof(MarkSusPlayers));
                }
            }
        }

        public bool ShowDoors
        {
            get => App.Config.Misc.ShowDoors;
            set
            {
                if (App.Config.Misc.ShowDoors != value)
                {
                    App.Config.Misc.ShowDoors = value;
                    OnPropertyChanged(nameof(ShowDoors));
                }
            }
        }

        public bool KeyDoorsOnly
        {
            get => App.Config.Misc.KeyDoorsOnly;
            set
            {
                if (App.Config.Misc.KeyDoorsOnly != value)
                {
                    App.Config.Misc.KeyDoorsOnly = value;
                    OnPropertyChanged(nameof(KeyDoorsOnly));
                }
            }
        }

        public bool ShowLockedDoors
        {
            get => App.Config.Misc.ShowLockedDoors;
            set
            {
                if (App.Config.Misc.ShowLockedDoors != value)
                {
                    App.Config.Misc.ShowLockedDoors = value;
                    OnPropertyChanged(nameof(ShowLockedDoors));
                }
            }
        }

        public bool ShowUnlockedDoors
        {
            get => App.Config.Misc.ShowUnlockedDoors;
            set
            {
                if (App.Config.Misc.ShowUnlockedDoors != value)
                {
                    App.Config.Misc.ShowUnlockedDoors = value;
                    OnPropertyChanged(nameof(ShowUnlockedDoors));
                }
            }
        }

        public bool ShowSwitches
        {
            get => App.Config.Misc.ShowSwitches;
            set
            {
                if (App.Config.Misc.ShowSwitches != value)
                {
                    App.Config.Misc.ShowSwitches = value;
                    OnPropertyChanged(nameof(ShowSwitches));
                }
            }
        }

        public bool ShowCardReaders
        {
            get => App.Config.Misc.ShowCardReaders;
            set
            {
                if (App.Config.Misc.ShowCardReaders != value)
                {
                    App.Config.Misc.ShowCardReaders = value;
                    OnPropertyChanged(nameof(ShowCardReaders));
                }
            }
        }

        public bool ShowESP
        {
            get => UI.ESP.ESPWindow.ShowESP;
            set
            {
                if (UI.ESP.ESPWindow.ShowESP != value)
                {
                    if (value)
                        UI.ESP.ESPManager.ShowESP();
                    else
                        UI.ESP.ESPManager.HideESP();
                    
                    OnPropertyChanged(nameof(ShowESP));
                }
            }
        }

        public bool ShowLoot
        {
            get => App.Config.Loot.Enabled;
            set
            {
                if (App.Config.Loot.Enabled != value)
                {
                    App.Config.Loot.Enabled = value;
                    OnPropertyChanged(nameof(ShowLoot));
                }
            }
        }

        public bool EspQuestLocations
        {
            get => App.Config.UI.EspQuestLocations;
            set
            {
                if (App.Config.UI.EspQuestLocations != value)
                {
                    App.Config.UI.EspQuestLocations = value;
                    OnPropertyChanged(nameof(EspQuestLocations));
                }
            }
        }

        public int RadarMaxFPS
        {
            get => App.Config.UI.RadarMaxFPS;
            set
            {
                if (App.Config.UI.RadarMaxFPS != value)
                {
                    App.Config.UI.RadarMaxFPS = value;
                    OnPropertyChanged(nameof(RadarMaxFPS));
                }
            }
        }

        #endregion

        #region Radar Widget Font

        public float RadarWidgetFontSize
        {
            get => App.Config.UI.RadarWidgetFontSize;
            set
            {
                if (Math.Abs(App.Config.UI.RadarWidgetFontSize - value) > 0.1f)
                {
                    App.Config.UI.RadarWidgetFontSize = value;
                    OnPropertyChanged(nameof(RadarWidgetFontSize));
                }
            }
        }

        #endregion

    }
}
