using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Brightens the scene by setting ambient mode to Trilight and writing equator color.
    /// Skips indoor/fixed maps (Factory, Lab, Labyrinth).
    /// </summary>
    public sealed class FullBright : MemWriteFeature<FullBright>
    {
        private bool _lastEnabledState;
        private float _lastBrightness;
        private static ulong _cachedLevelSettings;
        private static volatile bool _resolving;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day", "factory4_night", "laboratory", "Labyrinth"
        };

        private enum AmbientMode : int { Skybox, Trilight, Custom = 2, Flat = 3 }

        public override bool Enabled
        {
            get => App.Config.MemWrites.FullBrightEnabled;
            set => App.Config.MemWrites.FullBrightEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(200);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var mapId = Memory.Game?.MapID;
                if (mapId != null && ExcludedMaps.Contains(mapId))
                    return;

                var brightness = App.Config.MemWrites.FullBrightIntensity;
                var stateChanged = Enabled != _lastEnabledState;
                var brightnessChanged = Math.Abs(brightness - _lastBrightness) > 0.001f;

                if (!stateChanged && !brightnessChanged)
                    return;

                var ls = GetLevelSettings();
                if (!MemDMA.IsValidVirtualAddress(ls))
                    return;

                if (Enabled)
                {
                    Memory.WriteValue(ls + SDK.Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Trilight);
                    // Write equator color (RGBA float4)
                    Memory.WriteValue(ls + SDK.Offsets.LevelSettings.EquatorColor, new System.Numerics.Vector4(brightness, brightness, brightness, 1f));
                    Memory.WriteValue(ls + SDK.Offsets.LevelSettings.GroundColor, new System.Numerics.Vector4(0f, 0f, 0f, 1f));
                }
                else
                {
                    Memory.WriteValue(ls + SDK.Offsets.LevelSettings.AmbientMode, (int)AmbientMode.Flat);
                }

                _lastEnabledState = Enabled;
                _lastBrightness = brightness;
            }
            catch
            {
                _cachedLevelSettings = 0;
            }
        }

        private static ulong GetLevelSettings()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedLevelSettings))
                return _cachedLevelSettings;

            if (_resolving)
                return 0;

            _resolving = true;
            try
            {
                if (!IL2CPPLib.Initialized)
                    return 0;

                var klassPtr = IL2CPPLib.Class.FindClass("UnityEngine.RenderSettings");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                var klass = Memory.ReadValue<IL2CPPLib.Class>(klassPtr);
                if (!MemDMA.IsValidVirtualAddress(klass.static_fields))
                    return 0;

                // RenderSettings stores ambient data in static fields
                _cachedLevelSettings = klass.static_fields;
                DebugLogger.LogDebug($"[FullBright] Resolved LevelSettings @ 0x{klass.static_fields:X}");
                return _cachedLevelSettings;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[FullBright] Resolve error: {ex.Message}");
                return 0;
            }
            finally
            {
                _resolving = false;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _lastBrightness = 0;
            _cachedLevelSettings = 0;
            _resolving = false;
        }
    }
}
