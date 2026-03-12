using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Toggles the medical effect using panel in EFTHardSettings.
    /// </summary>
    public sealed class MedPanel : MemWriteFeature<MedPanel>
    {
        private bool _lastEnabledState;
        private ulong _cachedInstance;

        public override bool Enabled
        {
            get => App.Config.MemWrites.MedPanelEnabled;
            set => App.Config.MemWrites.MedPanelEnabled = value;
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

                Memory.WriteValue(instance + SDK.Offsets.EFTHardSettings.MED_EFFECT_USING_PANEL, Enabled);

                _lastEnabledState = Enabled;
                DebugLogger.LogDebug($"[MedPanel] {(Enabled ? "Enabled" : "Disabled")}");
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
