using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables weapon collision/occlusion by zeroing the WEAPON_OCCLUSION_LAYERS mask
    /// in EFTHardSettings.
    /// </summary>
    public sealed class DisableWeaponCollision : MemWriteFeature<DisableWeaponCollision>
    {
        private bool _lastEnabledState;
        private ulong _cachedInstance;

        private const int ORIGINAL_OCCLUSION_LAYERS = 1082136832;

        public override bool Enabled
        {
            get => App.Config.MemWrites.DisableWeaponCollisionEnabled;
            set => App.Config.MemWrites.DisableWeaponCollisionEnabled = value;
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

                var targetValue = Enabled ? 0 : ORIGINAL_OCCLUSION_LAYERS;
                Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.WEAPON_OCCLUSION_LAYERS, targetValue);

                _lastEnabledState = Enabled;
                DebugLogger.LogDebug($"[DisableWeaponCollision] {(Enabled ? "Enabled" : "Disabled")}");
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
