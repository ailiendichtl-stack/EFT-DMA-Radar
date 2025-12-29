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

using LoneEftDmaRadar.Common.DMA;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc.JSON;
using LoneEftDmaRadar.UI.ColorPicker;
using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using Size = System.Windows.Size;
using VmmSharpEx.Extensions.Input;

namespace LoneEftDmaRadar
{
    /// <summary>
    /// Global Program Configuration (Config.json)
    /// </summary>
    public sealed class EftDmaConfig
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true
        };
        /// <summary>
        /// Public Constructor required for deserialization.
        /// DO NOT CALL - USE LOAD().
        /// </summary>
        public EftDmaConfig() { }

        /// <summary>
        /// DMA Config
        /// </summary>
        [JsonPropertyName("dma")]
        [JsonInclude]
        public DMAConfig DMA { get; private set; } = new();

        /// <summary>
        /// UI/Radar Config
        /// </summary>
        [JsonPropertyName("ui")]
        [JsonInclude]
        public UIConfig UI { get; private set; } = new();

        /// <summary>
        /// Web Radar Config
        /// </summary>
        [JsonPropertyName("webRadar")]
        [JsonInclude]
        public WebRadarConfig WebRadar { get; private set; } = new();

        /// <summary>
        /// FilteredLoot Config
        /// </summary>
        [JsonPropertyName("loot")]
        [JsonInclude]
        public LootConfig Loot { get; private set; } = new LootConfig();

        /// <summary>
        /// Containers configuration.
        /// </summary>
        [JsonPropertyName("containers")]
        [JsonInclude]
        public ContainersConfig Containers { get; private set; } = new();

        /// <summary>
        /// Hotkeys Dictionary for Radar.
        /// </summary>
        [JsonPropertyName("hotkeys_v2")]
        [JsonInclude]
        public ConcurrentDictionary<Win32VirtualKey, string> Hotkeys { get; private set; } = new(); // Default entries

        /// <summary>
        /// All defined Radar Colors.
        /// </summary>
        [JsonPropertyName("radarColors")]
        [JsonConverter(typeof(ColorDictionaryConverter))]
        [JsonInclude]
        public ConcurrentDictionary<ColorPickerOption, string> RadarColors { get; private set; } = new();

        /// <summary>
        /// Widgets Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("aimviewWidget")]
        public AimviewWidgetConfig AimviewWidget { get; private set; } = new();

        /// <summary>
        /// Widgets Configuration.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("infoWidget")]
        public InfoWidgetConfig InfoWidget { get; private set; } = new();

        /// <summary>
        /// Settings for Device Aimbot (DeviceAimbot/KMBox).
        /// </summary>
        [JsonPropertyName("device")]
        [JsonInclude]
        public DeviceAimbotConfig Device { get; private set; } = new();

        /// <summary>
        /// Settings for memory write based features.
        /// </summary>
        [JsonPropertyName("memWrites")]
        [JsonInclude]
        public MemWritesConfig MemWrites { get; private set; } = new();

        /// <summary>
        /// Debug and performance tuning settings.
        /// </summary>
        [JsonPropertyName("debug")]
        [JsonInclude]
        public DebugConfig Debug { get; private set; } = new();

        /// <summary>
        /// Quest Helper configuration.
        /// </summary>
        [JsonPropertyName("questHelper")]
        [JsonInclude]
        public QuestHelperConfig QuestHelper { get; private set; } = new();

        /// <summary>
        /// Hideout item tracking configuration.
        /// </summary>
        [JsonPropertyName("hideout")]
        [JsonInclude]
        public HideoutConfig Hideout { get; private set; } = new();

        /// <summary>
        /// FilteredLoot Filters Config.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("lootFilters")]
        public LootFilterConfig LootFilters { get; private set; } = new();

        /// <summary>
        /// Misc Config.
        /// </summary>
        [JsonPropertyName("misc")]
        [JsonInclude]
        public MiscConfig Misc { get; private set; } = new();

        /// <summary>
        /// Persistent Cache Access (not saved to config, runtime only).
        /// </summary>
        [JsonIgnore]
        public PersistentCache Cache { get; set; } = new();

        #region Config Interface

        /// <summary>
        /// Filename of this Config File (not full path).
        /// </summary>
        [JsonIgnore]
        internal const string Filename = "Config-EFT.json";

        [JsonIgnore]
        private static readonly Lock _syncRoot = new();

        [JsonIgnore]
        private static readonly FileInfo _configFile = new(Path.Combine(App.ConfigPath.FullName, Filename));

        [JsonIgnore]
        private static readonly FileInfo _tempFile = new(Path.Combine(App.ConfigPath.FullName, Filename + ".tmp"));

        [JsonIgnore]
        private static readonly FileInfo _backupFile = new(Path.Combine(App.ConfigPath.FullName, Filename + ".bak"));

        /// <summary>
        /// Loads the configuration from disk.
        /// Creates a new config if it does not exist.
        /// ** ONLY CALL ONCE PER MUTEX **
        /// </summary>
        /// <returns>Loaded Config.</returns>
        public static EftDmaConfig Load()
        {
            EftDmaConfig config;
            lock (_syncRoot)
            {
                App.ConfigPath.Create();
                if (_configFile.Exists)
                {
                    config = TryLoad(_tempFile) ??
                        TryLoad(_configFile) ??
                        TryLoad(_backupFile);

                    if (config is null)
                    {
                        var dlg = MessageBox.Show(
                            "Config File Corruption Detected! If you backed up your config, you may attempt to restore it.\n" +
                            "Press OK to Reset Config and continue startup, or CANCEL to terminate program.",
                            App.Name,
                            MessageBoxButton.OKCancel,
                            MessageBoxImage.Error);
                        if (dlg == MessageBoxResult.Cancel)
                            Environment.Exit(0); // Terminate program
                        config = new EftDmaConfig();
                        SaveInternal(config);
                    }
                }
                else
                {
                    config = new();
                    SaveInternal(config);
                }

                return config;
            }
        }

        private static EftDmaConfig TryLoad(FileInfo file)
        {
            try
            {
                if (!file.Exists)
                    return null;
                string json = File.ReadAllText(file.FullName);
                return JsonSerializer.Deserialize<EftDmaConfig>(json, _jsonOptions);
            }
            catch
            {
                return null; // Ignore errors, return null to indicate failure
            }
        }

        /// <summary>
        /// Save the current configuration to disk.
        /// </summary>
        /// <exception cref="IOException"></exception>
        public void Save()
        {
            lock (_syncRoot)
            {
                try
                {
                    SaveInternal(this);
                }
                catch (Exception ex)
                {
                    throw new IOException($"ERROR Saving Config: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Saves the current configuration to disk asynchronously.
        /// </summary>
        /// <returns></returns>
        public async Task SaveAsync() => await Task.Run(Save);

        private static void SaveInternal(EftDmaConfig config)
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            using (var fs = new FileStream(
                _tempFile.FullName,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.WriteThrough))
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(json);
                sw.Flush();
                fs.Flush(flushToDisk: true);
            }
            if (_configFile.Exists)
            {
                File.Replace(
                    sourceFileName: _tempFile.FullName,
                    destinationFileName: _configFile.FullName,
                    destinationBackupFileName: _backupFile.FullName,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Copy(
                    sourceFileName: _tempFile.FullName,
                    destFileName: _backupFile.FullName);
                File.Move(
                    sourceFileName: _tempFile.FullName,
                    destFileName: _configFile.FullName);
            }
        }

        #endregion
    }

    public sealed class DMAConfig
    {
        /// <summary>
        /// FPGA Read Algorithm
        /// </summary>
        [JsonPropertyName("fpgaAlgo")]
        public FpgaAlgo FpgaAlgo { get; set; } = FpgaAlgo.Auto;

        /// <summary>
        /// Use a Memory Map for FPGA DMA Connection.
        /// </summary>
        [JsonPropertyName("enableMemMap")]
        public bool MemMapEnabled { get; set; } = true;
    }

    public sealed class UIConfig
    {
        /// <summary>
        /// UI Scale Value (0.5-2.0 , default: 1.0)
        /// </summary>
        [JsonPropertyName("scale")]
        public float UIScale { get; set; } = 1.0f;

        /// <summary>
        /// Size of the Radar Window.
        /// </summary>
        [JsonPropertyName("windowSize")]
        public Size WindowSize { get; set; } = new(1280, 720);
        /// <summary>
        /// Preferred rendering resolution for ESP/aim helpers.
        /// </summary>
        [JsonPropertyName("resolution")]
        public Size Resolution { get; set; } = new(1920, 1080);

        /// <summary>
        /// Window is maximized.
        /// </summary>
        [JsonPropertyName("windowMaximized")]
        public bool WindowMaximized { get; set; }

        /// <summary>
        /// Last used 'Zoom' level.
        /// </summary>
        [JsonPropertyName("zoom")]
        public int Zoom { get; set; } = 100;

        /// <summary>
        /// Player/Teammates Aimline Length (Max: 1500)
        /// </summary>
        [JsonPropertyName("aimLineLength")]
        public int AimLineLength { get; set; } = 1500;

        /// <summary>
        /// Camera Field of View (Vertical).
        /// </summary>
        [JsonPropertyName("fov")]
        public float FOV { get; set; } = 50.0f;

        /// <summary>
        /// Show Mines/Claymores in the Radar UI.
        /// </summary>
        [JsonPropertyName("showMines")]
        public bool ShowMines { get; set; } = true;

        /// <summary>
        /// Hides player names & extended player info in Radar GUI.
        /// </summary>
        [JsonPropertyName("hideNames")]
        public bool HideNames { get; set; }

        /// <summary>
        /// Connects grouped players together via a semi-transparent line.
        /// </summary>
        [JsonPropertyName("connectGroups")]
        public bool ConnectGroups { get; set; } = true;

        /// <summary>
        /// Max game distance to render targets in Aimview,
        /// and to display dynamic aimlines between two players.
        /// </summary>
        [JsonPropertyName("maxDistance")]
        public float MaxDistance { get; set; } = 350;
        /// <summary>
        /// True if teammate aimlines should be the same length as LocalPlayer.
        /// </summary>
        [JsonPropertyName("teammateAimlines")]
        public bool TeammateAimlines { get; set; }

        /// <summary>
        /// True if AI Aimlines should dynamically extend.
        /// </summary>
        [JsonPropertyName("aiAimlines")]
        public bool AIAimlines { get; set; } = true;

        /// <summary>
        /// Mark players with suspicious stats.
        /// </summary>
        [JsonPropertyName("markSusPlayers")]
        public bool MarkSusPlayers { get; set; } = false;

        /// <summary>
        /// Show Player Skeletons in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerSkeletons")]
        public bool EspPlayerSkeletons { get; set; } = true;

        /// <summary>
        /// Player Box Style in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerBoxStyle")]
        public EspBoxStyle EspPlayerBoxStyle { get; set; } = EspBoxStyle.Box2D;

        /// <summary>
        /// Show AI Skeletons in ESP.
        /// </summary>
        [JsonPropertyName("espAISkeletons")]
        public bool EspAISkeletons { get; set; } = true;

        /// <summary>
        /// AI Box Style in ESP.
        /// </summary>
        [JsonPropertyName("espAIBoxStyle")]
        public EspBoxStyle EspAIBoxStyle { get; set; } = EspBoxStyle.Box2D;

        /// <summary>
        /// Mini Radar Configuration.
        /// </summary>
        [JsonPropertyName("miniRadar")]
        [JsonInclude]
        public MiniRadarConfig MiniRadar { get; private set; } = new();

        /// <summary>
        /// Ammo Counter Widget Configuration.
        /// </summary>
        [JsonPropertyName("ammoCounter")]
        [JsonInclude]
        public AmmoCounterConfig AmmoCounter { get; private set; } = new();

        /// <summary>
        /// Show Player Names in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerNames")]
        public bool EspPlayerNames { get; set; } = true;
        /// <summary>
        /// Show Player Group IDs in ESP.
        /// </summary>
        [JsonPropertyName("espGroupIds")]
        public bool EspGroupIds { get; set; } = true;
        /// <summary>
        /// Show Player faction (USEC/BEAR) in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerFaction")]
        public bool EspPlayerFaction { get; set; } = false;
        /// <summary>
        /// Color PMCs by faction (USEC/BEAR) when group colors are disabled.
        /// </summary>
        [JsonPropertyName("espFactionColors")]
        public bool EspFactionColors { get; set; } = true;
        /// <summary>
        /// Color hostile human players by group.
        /// </summary>
        [JsonPropertyName("espGroupColors")]
        public bool EspGroupColors { get; set; } = true;
        /// <summary>
        /// Show Player Health status in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerHealth")]
        public bool EspPlayerHealth { get; set; } = true;
        /// <summary>
        /// Show Player Distance in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerDistance")]
        public bool EspPlayerDistance { get; set; } = true;
        /// <summary>
        /// Show Player Weapons in ESP.
        /// </summary>
        [JsonPropertyName("espPlayerWeapons")]
        public bool EspPlayerWeapons { get; set; } = true;

        /// <summary>
        /// Show AI Names in ESP.
        /// </summary>
        [JsonPropertyName("espAINames")]
        public bool EspAINames { get; set; } = true;
        /// <summary>
        /// Show AI Group IDs in ESP.
        /// </summary>
        [JsonPropertyName("espAIGroupIds")]
        public bool EspAIGroupIds { get; set; } = false;
        /// <summary>
        /// Show AI Health status in ESP.
        /// </summary>
        [JsonPropertyName("espAIHealth")]
        public bool EspAIHealth { get; set; } = true;
        /// <summary>
        /// Show AI Distance in ESP.
        /// </summary>
        [JsonPropertyName("espAIDistance")]
        public bool EspAIDistance { get; set; } = true;
        /// <summary>
        /// Show AI Weapons in ESP.
        /// </summary>
        [JsonPropertyName("espAIWeapons")]
        public bool EspAIWeapons { get; set; } = true;

        /// <summary>
        /// Show ESP Overlay.
        /// </summary>
        [JsonPropertyName("showESP")]
        public bool ShowESP { get; set; } = true;
        
        /// <summary>
        /// Show Exfils on ESP.
        /// </summary>
        [JsonPropertyName("espExfils")]
        public bool EspExfils { get; set; } = true;

        [JsonPropertyName("espTripwires")]
        public bool EspTripwires { get; set; } = true;

        [JsonPropertyName("espGrenades")]
        public bool EspGrenades { get; set; } = true;

        /// <summary>
        /// Show grenade trail on ESP.
        /// </summary>
        [JsonPropertyName("espGrenadeTrail")]
        public bool EspGrenadeTrail { get; set; } = true;

        /// <summary>
        /// Number of position history points to track for grenade trail.
        /// </summary>
        [JsonPropertyName("espGrenadeTrailLength")]
        public int EspGrenadeTrailLength { get; set; } = 50;

        /// <summary>
        /// Show grenade blast radius circle on ESP.
        /// </summary>
        [JsonPropertyName("espGrenadeBlastRadius")]
        public bool EspGrenadeBlastRadius { get; set; } = true;

        /// <summary>
        /// Show Loot on ESP.
        /// </summary>
        [JsonPropertyName("espLoot")]
        public bool EspLoot { get; set; } = true;

        /// <summary>
        /// Show quest items on ESP.
        /// </summary>
        [JsonPropertyName("espQuestLoot")]
        public bool EspQuestLoot { get; set; } = true;

        /// <summary>
        /// Show quest locations/zones on ESP.
        /// </summary>
        [JsonPropertyName("espQuestLocations")]
        public bool EspQuestLocations { get; set; } = true;

        /// <summary>
        /// Show Loot Prices on ESP.
        /// </summary>
        [JsonPropertyName("espLootPrice")]
        public bool EspLootPrice { get; set; } = true;

        [JsonPropertyName("espLootConeEnabled")]
        public bool EspLootConeEnabled { get; set; } = true;

        [JsonPropertyName("espLootConeAngle")]
        public float EspLootConeAngle { get; set; } = 15f;

        /// <summary>
        /// Show Food items on ESP.
        /// </summary>
        [JsonPropertyName("espFood")]
        public bool EspFood { get; set; } = false;

        /// <summary>
        /// Show Medical items on ESP.
        /// </summary>
        [JsonPropertyName("espMeds")]
        public bool EspMeds { get; set; } = false;

        /// <summary>
        /// Show Backpacks on ESP.
        /// </summary>
        [JsonPropertyName("espBackpacks")]
        public bool EspBackpacks { get; set; } = false;

        /// <summary>
        /// Show Corpses on ESP.
        /// </summary>
        [JsonPropertyName("espCorpses")]
        public bool EspCorpses { get; set; } = false;

        /// <summary>
        /// Show Containers on ESP.
        /// </summary>
        [JsonPropertyName("espContainers")]
        public bool EspContainers { get; set; } = false;

        /// <summary>
        /// Show a crosshair overlay on ESP window.
        /// </summary>
        [JsonPropertyName("espCrosshair")]
        public bool EspCrosshair { get; set; }

        /// <summary>
        /// Crosshair half-length in pixels.
        /// </summary>
        [JsonPropertyName("espCrosshairLength")]
        public float EspCrosshairLength { get; set; } = 25f;

        /// <summary>
        /// Font family used for ESP text (DX overlay).
        /// </summary>
        [JsonPropertyName("espFontFamily")]
        public string EspFontFamily { get; set; } = "Segoe UI";

        /// <summary>
        /// Small font size used for ESP text (loot labels, etc).
        /// </summary>
        [JsonPropertyName("espFontSizeSmall")]
        public int EspFontSizeSmall { get; set; } = 10;

        /// <summary>
        /// Medium font size used for ESP text (player names, exfil labels).
        /// </summary>
        [JsonPropertyName("espFontSizeMedium")]
        public int EspFontSizeMedium { get; set; } = 12;

        /// <summary>
        /// Large font size used for ESP text (status banners).
        /// </summary>
        [JsonPropertyName("espFontSizeLarge")]
        public int EspFontSizeLarge { get; set; } = 24;

        /// <summary>
        /// Font size used for Radar ESP widgets (loot height indicators ▲▼●, etc).
        /// </summary>
        [JsonPropertyName("radarWidgetFontSize")]
        public float RadarWidgetFontSize { get; set; } = 9f;

        /// <summary>
        /// Custom ESP Screen Width (0 = Auto).
        /// </summary>
        [JsonPropertyName("espScreenWidth")]
        public int EspScreenWidth { get; set; } = 0;

        /// <summary>
        /// Custom ESP Screen Height (0 = Auto).
        /// </summary>
        [JsonPropertyName("espScreenHeight")]
        public int EspScreenHeight { get; set; } = 0;

        /// <summary>
        /// ESP Max FPS (0 = VSync/Unlimited).
        /// </summary>
        [JsonPropertyName("espMaxFPS")]
        public int EspMaxFPS { get; set; } = 0;

        // ESP Colors (independent from radar colors)
        [JsonPropertyName("espColorPlayers")]
        public string EspColorPlayers { get; set; } = "#FFFFFFFF";

        [JsonPropertyName("espColorAI")]
        public string EspColorAI { get; set; } = "#FFFFA500";

        /// <summary>
        /// ESP color for player-controlled scavs (PScavs).
        /// </summary>
        [JsonPropertyName("espColorPlayerScavs")]
        public string EspColorPlayerScavs { get; set; } = "#FFFFFFFF";

        [JsonPropertyName("espColorRaiders")]
        public string EspColorRaiders { get; set; } = "#FFFFC70F";

        [JsonPropertyName("espColorBosses")]
        public string EspColorBosses { get; set; } = "#FFFF00FF";

        [JsonPropertyName("espColorLoot")]
        public string EspColorLoot { get; set; } = "#FFD0D0D0";

        /// <summary>
        /// ESP color for static containers.
        /// </summary>
        [JsonPropertyName("espColorContainers")]
        public string EspColorContainers { get; set; } = "#FFFFFFCC";

        [JsonPropertyName("espColorExfil")]
        public string EspColorExfil { get; set; } = "#FF7FFFD4";

        [JsonPropertyName("espColorTripwire")]
        public string EspColorTripwire { get; set; } = "#FFFF4500";

        [JsonPropertyName("espColorGrenade")]
        public string EspColorGrenade { get; set; } = "#FFFF5500";

        [JsonPropertyName("espColorCrosshair")]
        public string EspColorCrosshair { get; set; } = "#FFFFFFFF";

        /// <summary>
        /// ESP faction color for BEAR PMCs (ESP only, not radar).
        /// </summary>
        [JsonPropertyName("espColorFactionBear")]
        public string EspColorFactionBear { get; set; } = "#FFFF0000";
        /// <summary>
        /// ESP faction color for USEC PMCs (ESP only, not radar).
        /// </summary>
        [JsonPropertyName("espColorFactionUsec")]
        public string EspColorFactionUsec { get; set; } = "#FF0000FF";

        /// <summary>
        /// Radar Max FPS (0 = unlimited). Lowering this can free headroom for ESP.
        /// </summary>
        [JsonPropertyName("radarMaxFPS")]
        public int RadarMaxFPS { get; set; } = 0;

        /// <summary>
        /// Maximum distance to render players on ESP (in meters). 0 = unlimited.
        /// </summary>
        [JsonPropertyName("espPlayerMaxDistance")]
        public float EspPlayerMaxDistance { get; set; } = 0f;

        /// <summary>
        /// Maximum distance to render AI/Scavs on ESP (in meters). 0 = unlimited.
        /// </summary>
        [JsonPropertyName("espAIMaxDistance")]
        public float EspAIMaxDistance { get; set; } = 0f;

        /// <summary>
        /// Maximum distance to render loot on ESP (in meters). 0 = unlimited.
        /// </summary>
        [JsonPropertyName("espLootMaxDistance")]
        public float EspLootMaxDistance { get; set; } = 0f;

        /// <summary>
        /// Target screen index for ESP window (0 = Primary, 1+ = Secondary screens).
        /// </summary>
        [JsonPropertyName("espTargetScreen")]
        public int EspTargetScreen { get; set; } = 0;

        /// <summary>
        /// Position of the name/distance label relative to the bounding box.
        /// </summary>
        [JsonPropertyName("espLabelPosition")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EspLabelPosition EspLabelPosition { get; set; } = EspLabelPosition.Top;
        /// <summary>
        /// Position of the AI/scav label relative to the bounding box.
        /// </summary>
        [JsonPropertyName("espLabelPositionAI")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public EspLabelPosition EspLabelPositionAI { get; set; } = EspLabelPosition.Top;

        /// <summary>
        /// Draw a small circle on the head bone for ESP.
        /// </summary>
        [JsonPropertyName("espHeadCirclePlayers")]
        public bool EspHeadCirclePlayers { get; set; } = false;
        /// <summary>
        /// Draw a small circle on the head bone for AI/Scavs.
        /// </summary>
        [JsonPropertyName("espHeadCircleAI")]
        public bool EspHeadCircleAI { get; set; } = false;
    }

    public enum EspLabelPosition
    {
        Top,
        Bottom
    }

    public enum EspBoxStyle
    {
        None,
        Box2D,
        Corner2D,
        Corner3D,
        Box3D
    }

    public sealed class LootConfig
    {
        /// <summary>
        /// Shows loot on map.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Show quest items on map/ESP.
        /// </summary>
        [JsonPropertyName("showQuestItems")]
        public bool ShowQuestItems { get; set; } = true;

        /// <summary>
        /// Shows bodies/corpses on map.
        /// </summary>
        [JsonPropertyName("hideCorpses")]
        public bool HideCorpses { get; set; }

        /// <summary>
        /// Minimum loot value (rubles) to display 'normal loot' on map.
        /// </summary>
        [JsonPropertyName("minValue")]
        public int MinValue { get; set; } = 50000;

        /// <summary>
        /// Minimum loot value (rubles) to display 'important loot' on map.
        /// </summary>
        [JsonPropertyName("minValueValuable")]
        public int MinValueValuable { get; set; } = 200000;

        /// <summary>
        /// Show FilteredLoot by "Price per Slot".
        /// </summary>
        [JsonPropertyName("pricePerSlot")]
        public bool PricePerSlot { get; set; }

        /// <summary>
        /// FilteredLoot Price Mode.
        /// </summary>
        [JsonPropertyName("priceMode")]
        public LootPriceMode PriceMode { get; set; } = LootPriceMode.FleaMarket;

    }

    public sealed class ContainersConfig
    {
        /// <summary>
        /// Shows static containers on map.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Maximum distance to draw static containers.
        /// </summary>
        [JsonPropertyName("drawDistance")]
        public float DrawDistance { get; set; } = 100f;

        /// <summary>
        /// Maximum distance to draw static containers on ESP.
        /// </summary>
        [JsonPropertyName("espDrawDistance")]
        public float EspDrawDistance { get; set; } = 100f;

        /// <summary>
        /// Minimum container contents value to display (0 = show all).
        /// </summary>
        [JsonPropertyName("minValue")]
        public int MinValue { get; set; } = 0;

        /// <summary>
        /// Select all containers.
        /// </summary>
        [JsonPropertyName("selectAll")]
        public bool SelectAll { get; set; } = true;

        /// <summary>
        /// Hide containers that have been searched by local player.
        /// </summary>
        [JsonPropertyName("hideSearched")]
        public bool HideSearched { get; set; } = false;

        /// <summary>
        /// Enable scanning container/corpse contents.
        /// Only works in offline PVE mode where loot is pre-generated.
        /// Disabled by default to avoid unnecessary memory reads in online mode.
        /// </summary>
        [JsonPropertyName("pveScanEnabled")]
        public bool PveScanEnabled { get; set; } = false;

        /// <summary>
        /// Selected containers to display.
        /// </summary>
        [JsonPropertyName("selected_v4")]
        [JsonInclude]
        [JsonConverter(typeof(CaseInsensitiveConcurrentDictionaryConverter<byte>))]
        public ConcurrentDictionary<string, byte> Selected { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Settings for device aimbot integration (DeviceAimbot/KMBox).
    /// </summary>
    public sealed class DeviceAimbotConfig
    {
        public bool Enabled { get; set; }
        public bool AutoConnect { get; set; }
        public string LastComPort { get; set; }

        // Debug
        public bool ShowDebug { get; set; } = true;

        /// <summary>
        /// Smoothing factor for DeviceAimbot device aim. 1 = instant, higher = slower/smoother.
        /// </summary>
        public float Smoothing { get; set; } = 1.0f;

        /// <summary>
        /// Speed multiplier when hip-firing (not ADS). 0.1 = 10% speed, 1.0 = full speed.
        /// Lower values prevent overshooting when not aiming down sights.
        /// </summary>
        public float HipfireSpeedFactor { get; set; } = 0.3f;

        /// <summary>
        /// Polling rate in Hz for the aimbot loop. Higher = more responsive but more CPU usage.
        /// Default: 125 Hz (~8ms per tick). Range: 30-1000 Hz.
        /// </summary>
        public int PollingRateHz { get; set; } = 125;

        // Targeting
        public Bones TargetBone { get; set; } = Bones.HumanHead;
        public float FOV { get; set; } = 90f;
        public float MaxDistance { get; set; } = 300f;
        public TargetingMode Targeting { get; set; } = TargetingMode.ClosestToCrosshair;
        public bool EnablePrediction { get; set; } = true;
        public float DropCompensationFactor { get; set; } = 0.5f; // 0.1 to 1.0 multiplier for drop compensation

        // Target Filters
        public bool TargetPMC { get; set; } = true;
        public bool TargetPlayerScav { get; set; } = true;
        public bool TargetAIScav { get; set; } = true;
        public bool TargetBoss { get; set; } = true;
        public bool TargetRaider { get; set; } = true;
        public bool TargetSpecialPlayer { get; set; } = true;
        public bool TargetStreamer { get; set; } = true;

        /// <summary>
        /// When true, automatically switches to next target when current target becomes invalid.
        /// When false, user must release and re-engage aimbot to switch targets.
        /// </summary>
        public bool AutoTargetSwitch { get; set; } = true;

        // KMBox NET
        public bool UseKmBoxNet { get; set; } = false;
        public string KmBoxNetIp { get; set; } = "192.168.2.4";
        public int KmBoxNetPort { get; set; } = 8888;
        public string KmBoxNetMac { get; set; } = "";

        // FOV Circle Display
        public bool ShowFovCircle { get; set; } = true;
        public string FovCircleColorEngaged { get; set; } = "#FF00FF00"; // Green when engaged
        public string FovCircleColorIdle { get; set; } = "#80FFFFFF"; // Semi-transparent white when idle

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TargetingMode
        {
            ClosestToCrosshair,
            ClosestDistance
        }
    }

    /// <summary>
    /// Settings for memory write based features.
    /// </summary>
    public sealed class MemWritesConfig
    {
        public bool Enabled { get; set; }
        public bool NoRecoilEnabled { get; set; }
        public float NoRecoilAmount { get; set; } = 80f;
        public float NoSwayAmount { get; set; } = 80f;
        public bool InfiniteStaminaEnabled { get; set; }
        public bool MemoryAimEnabled { get; set; }
        public Bones MemoryAimTargetBone { get; set; } = Bones.HumanHead;
    }

    /// <summary>
    /// Debug and performance tuning settings.
    /// </summary>
    public sealed class DebugConfig
    {
        /// <summary>
        /// Loot scan interval in seconds (how often to rescan loot after initial scan).
        /// </summary>
        [JsonPropertyName("lootScanIntervalSeconds")]
        public int LootScanIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// Corpse contents scan interval in seconds.
        /// </summary>
        [JsonPropertyName("corpseScanIntervalSeconds")]
        public int CorpseScanIntervalSeconds { get; set; } = 30;

        /// <summary>
        /// T1 Realtime worker sleep duration in milliseconds.
        /// </summary>
        [JsonPropertyName("t1SleepMs")]
        public int T1SleepMs { get; set; } = 8;

        /// <summary>
        /// T2 Slow worker sleep duration in milliseconds.
        /// </summary>
        [JsonPropertyName("t2SleepMs")]
        public int T2SleepMs { get; set; } = 50;

        /// <summary>
        /// T3 Explosives worker sleep duration in milliseconds.
        /// </summary>
        [JsonPropertyName("t3SleepMs")]
        public int T3SleepMs { get; set; } = 30;

        /// <summary>
        /// ESP render timer period in milliseconds. Lower = higher FPS ceiling.
        /// </summary>
        [JsonPropertyName("espTimerPeriodMs")]
        public int EspTimerPeriodMs { get; set; } = 2;
    }

    /// <summary>
    /// FilteredLoot Filter Config.
    /// </summary>
    public sealed class LootFilterConfig
    {
        /// <summary>
        /// Currently selected filter.
        /// </summary>
        [JsonPropertyName("selected")]
        public string Selected { get; set; } = "Value Items";
        /// <summary>
        /// Filter Entries.
        /// </summary>
        [JsonInclude]
        [JsonPropertyName("filters")]
        public ConcurrentDictionary<string, UserLootFilter> Filters { get; private set; } = new() // Key is just a name, doesnt need to be case insensitive
        {
            ["Value Items"] = new() { Enabled = true, Color = "#FFFF0000", Entries = DefaultFilters.ValueItems },
            ["Top Ammo"] = new() { Enabled = true, Color = "#FF44A8FF", Entries = DefaultFilters.TopAmmo },
            ["Valuable Keys"] = new() { Enabled = true, Color = "#FFEF40FF", Entries = DefaultFilters.ValuableKeys },
            ["Kappa Items"] = new() { Enabled = true, Color = "#FFFF9421", Entries = DefaultFilters.KappaItems },
            ["Prestige Items"] = new() { Enabled = true, Color = "#FF696C1B", Entries = DefaultFilters.PrestigeItems },
            ["Quest Items"] = new() { Enabled = true, Color = "#FF16641E", Entries = DefaultFilters.QuestItems },
        };
    }

    public sealed class AimviewWidgetConfig
    {
        /// <summary>
        /// True if the Aimview Widget is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// True if the Aimview Widget is minimized.
        /// </summary>
        [JsonPropertyName("minimized")]
        public bool Minimized { get; set; } = false;

        /// <summary>
        /// Aimview Location
        /// </summary>
        [JsonPropertyName("location")]
        [JsonConverter(typeof(SKRectJsonConverter))]
        public SKRect Location { get; set; }
    }

    public sealed class InfoWidgetConfig
    {
        /// <summary>
        /// True if the Info Widget is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// True if the Info Widget is minimized.
        /// </summary>
        [JsonPropertyName("minimized")]
        public bool Minimized { get; set; } = false;

        /// <summary>
        /// ESP Widget Location
        /// </summary>
        [JsonPropertyName("location")]
        [JsonConverter(typeof(SKRectJsonConverter))]
        public SKRect Location { get; set; }
    }

    /// <summary>
    /// Configuration for Web Radar.
    /// </summary>
    public sealed class WebRadarConfig
    {
        /// <summary>
        /// True if UPnP should be enabled.
        /// </summary>
        [JsonPropertyName("upnp")]
        public bool UPnP { get; set; } = true;
        /// <summary>
        /// IP to bind to.
        /// </summary>
        [JsonPropertyName("host")]
        public string IP { get; set; } = "0.0.0.0";
        /// <summary>
        /// TCP Port to bind to.
        /// </summary>
        [JsonPropertyName("port")]
        public string Port { get; set; } = Random.Shared.Next(50000, 60000).ToString();
        /// <summary>
        /// Server Tick Rate (Hz).
        /// </summary>
        [JsonPropertyName("tickRate")]
        public string TickRate { get; set; } = "60";
    }

    public sealed class MiniRadarConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("size")]
        public int Size { get; set; } = 256;

        [JsonPropertyName("screenX")]
        public float ScreenX { get; set; } = -1; // -1 = Auto (Top Right)

        [JsonPropertyName("screenY")]
        public float ScreenY { get; set; } = 20;

        [JsonPropertyName("showLoot")]
        public bool ShowLoot { get; set; } = true;

        [JsonPropertyName("showPlayers")]
        public bool ShowPlayers { get; set; } = true;

        [JsonPropertyName("showExits")]
        public bool ShowExits { get; set; } = true;

        [JsonPropertyName("showContainers")]
        public bool ShowContainers { get; set; } = true;

        [JsonPropertyName("showGrenades")]
        public bool ShowGrenades { get; set; } = true;

        [JsonPropertyName("invertColors")]
        public bool InvertColors { get; set; } = true;
    }

    /// <summary>
    /// Configuration for the Ammo Counter ESP widget.
    /// </summary>
    public sealed class AmmoCounterConfig
    {
        /// <summary>
        /// Enable/Disable the ammo counter widget.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// X position on screen (-1 = auto center bottom).
        /// </summary>
        [JsonPropertyName("positionX")]
        public float PositionX { get; set; } = -1;

        /// <summary>
        /// Y position on screen (-1 = auto center bottom).
        /// </summary>
        [JsonPropertyName("positionY")]
        public float PositionY { get; set; } = -1;

        /// <summary>
        /// Widget width in pixels.
        /// </summary>
        [JsonPropertyName("width")]
        public float Width { get; set; } = 100;

        /// <summary>
        /// Widget height in pixels.
        /// </summary>
        [JsonPropertyName("height")]
        public float Height { get; set; } = 40;

        /// <summary>
        /// Background color (ARGB hex format).
        /// </summary>
        [JsonPropertyName("backgroundColor")]
        public string BackgroundColor { get; set; } = "#80000000";

        /// <summary>
        /// Text color (ARGB hex format).
        /// </summary>
        [JsonPropertyName("textColor")]
        public string TextColor { get; set; } = "#FFFF00FF"; // Magenta default

        /// <summary>
        /// Font size for the ammo text.
        /// </summary>
        [JsonPropertyName("fontSize")]
        public int FontSize { get; set; } = 24;
    }

    /// <summary>
    /// Quest Helper configuration.
    /// </summary>
    public sealed class QuestHelperConfig
    {
        /// <summary>
        /// Enable quest helper features.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Show quest objective locations on radar.
        /// </summary>
        [JsonPropertyName("showLocations")]
        public bool ShowLocations { get; set; } = true;

        /// <summary>
        /// Only show actively tracked quests.
        /// </summary>
        [JsonPropertyName("activeOnly")]
        public bool ActiveOnly { get; set; } = false;

        /// <summary>
        /// Only show Kappa-required quests.
        /// </summary>
        [JsonPropertyName("kappaOnly")]
        public bool KappaOnly { get; set; } = false;

        /// <summary>
        /// Only show Lightkeeper quests.
        /// </summary>
        [JsonPropertyName("lightkeeperOnly")]
        public bool LightkeeperOnly { get; set; } = false;

        /// <summary>
        /// Maximum distance to draw quest zones (meters).
        /// </summary>
        [JsonPropertyName("zoneDrawDistance")]
        public float ZoneDrawDistance { get; set; } = 100f;

        /// <summary>
        /// Tracked quest IDs.
        /// </summary>
        [JsonPropertyName("trackedQuests")]
        [JsonInclude]
        public HashSet<string> TrackedQuests { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Blacklisted quest IDs (won't show locations).
        /// </summary>
        [JsonPropertyName("blacklistedQuests")]
        [JsonInclude]
        public ConcurrentDictionary<string, byte> BlacklistedQuests { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hideout item tracking configuration.
    /// </summary>
    public sealed class HideoutConfig
    {
        /// <summary>
        /// Enable hideout item tracking.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Show hideout items on map/ESP.
        /// </summary>
        [JsonPropertyName("showHideoutItems")]
        public bool ShowHideoutItems { get; set; } = true;

        /// <summary>
        /// Selected hideout station/level combinations to track.
        /// Key format: "stationId:level" (e.g., "5d484fc0654e76006657e0ab:2")
        /// </summary>
        [JsonPropertyName("selected")]
        [JsonInclude]
        public ConcurrentDictionary<string, byte> Selected { get; private set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Items marked as "found" by the user (temporarily excluded from tracking).
        /// Key: item BSG ID
        /// </summary>
        [JsonPropertyName("foundItems")]
        [JsonInclude]
        public ConcurrentDictionary<string, byte> FoundItems { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Misc configuration settings.
    /// </summary>
    public sealed class MiscConfig
    {
        /// <summary>
        /// Enables the 'Auto Groups' feature that attempts to automatically group players together (best-effort).
        /// </summary>
        [JsonPropertyName("autoGroups")]
        public bool AutoGroups { get; set; } = true;
    }

    /// <summary>
    /// Persistent Cache that stores data between sessions for the same Process ID.
    /// Not serialized to config file - runtime only.
    /// </summary>
    public sealed class PersistentCache
    {
        /// <summary>
        /// Process Id this cache is tied to.
        /// </summary>
        public uint PID { get; set; }

        /// <summary>
        /// Key: RaidId | Value: Dictionary: Key: PlayerId | Value: GroupId
        /// </summary>
        public ConcurrentDictionary<int, ConcurrentDictionary<int, int>> Groups { get; set; } = new();

        /// <summary>
        /// Clears the cache if the PID has changed.
        /// </summary>
        public void ValidateOrReset(uint currentPid)
        {
            if (PID != currentPid)
            {
                PID = currentPid;
                Groups.Clear();
            }
        }
    }
}
