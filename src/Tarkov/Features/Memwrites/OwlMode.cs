using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;
using System.Numerics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Removes mouse look angle restrictions by setting limits to +/- infinity.
    /// Allows full 360-degree head rotation.
    /// </summary>
    public sealed class OwlMode : MemWriteFeature<OwlMode>
    {
        private bool _lastEnabledState;
        private ulong _cachedInstance;

        private static readonly Vector2 ORIGINAL_HORIZONTAL = new(-40f, 40f);
        private static readonly Vector2 ORIGINAL_VERTICAL = new(-50f, 20f);
        private static readonly Vector2 UNLIMITED = new(-float.MaxValue, float.MaxValue);

        public override bool Enabled
        {
            get => App.Config.MemWrites.OwlModeEnabled;
            set => App.Config.MemWrites.OwlModeEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var instance = GetInstance();
                if (!MemDMA.IsValidVirtualAddress(instance))
                    return;

                var horizontal = Enabled ? UNLIMITED : ORIGINAL_HORIZONTAL;
                var vertical = Enabled ? UNLIMITED : ORIGINAL_VERTICAL;

                Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.MOUSE_LOOK_HORIZONTAL_LIMIT, horizontal);
                Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.MOUSE_LOOK_VERTICAL_LIMIT, vertical);

                _lastEnabledState = Enabled;
                DebugLogger.LogDebug($"[OwlMode] {(Enabled ? "Enabled" : "Disabled")}");
            }
            catch
            {
                _cachedInstance = 0;
            }
        }

        private ulong GetInstance()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedInstance))
                return _cachedInstance;

            var instance = HardSettingsResolver.GetInstance();
            if (MemDMA.IsValidVirtualAddress(instance))
                _cachedInstance = instance;
            return instance;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedInstance = 0;
        }
    }
}
