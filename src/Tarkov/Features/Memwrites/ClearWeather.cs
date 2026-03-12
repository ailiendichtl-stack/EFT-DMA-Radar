using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Forces clear weather by writing to the WeatherDebug component.
    /// Skips indoor maps (Factory, Lab, Labyrinth).
    /// </summary>
    public sealed class ClearWeather : MemWriteFeature<ClearWeather>
    {
        private bool _lastEnabledState;
        private ulong _cachedWeatherDebug;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day", "factory4_night", "laboratory", "Labyrinth"
        };

        public override bool Enabled
        {
            get => App.Config.MemWrites.ClearWeatherEnabled;
            set => App.Config.MemWrites.ClearWeatherEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var mapId = Memory.Game?.MapID;
                if (mapId != null && ExcludedMaps.Contains(mapId))
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var wd = GetWeatherDebug();
                if (!MemDMA.IsValidVirtualAddress(wd))
                    return;

                if (Enabled)
                {
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.isEnabled, true);
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.WindMagnitude, 0f);
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.CloudDensity, 0f);
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.Fog, 0.001f);
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.Rain, 0f);
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.LightningThunderProbability, 0f);
                }
                else
                {
                    Memory.WriteValue(wd + SDK.Offsets.WeatherDebug.isEnabled, false);
                }

                _lastEnabledState = Enabled;
            }
            catch
            {
                _cachedWeatherDebug = 0;
            }
        }

        private ulong GetWeatherDebug()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedWeatherDebug))
                return _cachedWeatherDebug;

            try
            {
                if (!IL2CPPLib.Initialized)
                    return 0;

                var klassPtr = IL2CPPLib.Class.FindClass("EFT.Weather.WeatherController");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                var klass = Memory.ReadValue<IL2CPPLib.Class>(klassPtr);
                if (!MemDMA.IsValidVirtualAddress(klass.static_fields))
                    return 0;

                // WeatherController.Instance is the first static field
                var controller = Memory.ReadPtr(klass.static_fields);
                if (!MemDMA.IsValidVirtualAddress(controller))
                    return 0;

                var weatherDebug = Memory.ReadPtr(controller + SDK.Offsets.WeatherController.WeatherDebug);
                if (!MemDMA.IsValidVirtualAddress(weatherDebug))
                    return 0;

                _cachedWeatherDebug = weatherDebug;
                DebugLogger.LogDebug($"[ClearWeather] Resolved WeatherDebug @ 0x{weatherDebug:X}");
                return weatherDebug;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ClearWeather] Resolve error: {ex.Message}");
                return 0;
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedWeatherDebug = 0;
        }
    }
}
