using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Enables looting through walls by zeroing the loot raycast layer mask
    /// and provides optional zoom via weapon LN manipulation.
    /// </summary>
    public sealed class LootThroughWalls : MemWriteFeature<LootThroughWalls>
    {
        private bool _lastEnabledState;
        private ulong _cachedInstance;

        public override bool Enabled
        {
            get => App.Config.MemWrites.LootThroughWallsEnabled;
            set => App.Config.MemWrites.LootThroughWallsEnabled = value;
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

                // Set a very large loot raycast distance to enable looting through walls
                var distance = Enabled ? 100.0f : 1.3f;
                Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.LOOT_RAYCAST_DISTANCE, distance);

                _lastEnabledState = Enabled;
                DebugLogger.LogDebug($"[LootThroughWalls] {(Enabled ? "Enabled" : "Disabled")}");
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
