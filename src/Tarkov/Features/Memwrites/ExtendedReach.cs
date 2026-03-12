using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Extends loot and door interaction distances via EFTHardSettings.
    /// </summary>
    public sealed class ExtendedReach : MemWriteFeature<ExtendedReach>
    {
        private bool _lastEnabledState;
        private float _lastDistance;
        private ulong _cachedInstance;

        private const float ORIGINAL_LOOT_DISTANCE = 1.3f;
        private const float ORIGINAL_DOOR_DISTANCE = 1.2f;

        public override bool Enabled
        {
            get => App.Config.MemWrites.ExtendedReachEnabled;
            set => App.Config.MemWrites.ExtendedReachEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var currentDistance = App.Config.MemWrites.ExtendedReachDistance;
                var stateChanged = Enabled != _lastEnabledState;
                var distanceChanged = Math.Abs(currentDistance - _lastDistance) > 0.001f;

                if (!stateChanged && !(Enabled && distanceChanged))
                    return;

                var instance = GetInstance();
                if (!MemDMA.IsValidVirtualAddress(instance))
                    return;

                if (Enabled)
                {
                    Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.LOOT_RAYCAST_DISTANCE, currentDistance);
                    Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.DOOR_RAYCAST_DISTANCE, currentDistance);
                }
                else
                {
                    Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.LOOT_RAYCAST_DISTANCE, ORIGINAL_LOOT_DISTANCE);
                    Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.DOOR_RAYCAST_DISTANCE, ORIGINAL_DOOR_DISTANCE);
                }

                _lastEnabledState = Enabled;
                _lastDistance = currentDistance;
                DebugLogger.LogDebug($"[ExtendedReach] {(Enabled ? $"Enabled ({currentDistance:F1})" : "Disabled")}");
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
            _lastDistance = 0;
            _cachedInstance = 0;
        }
    }
}
