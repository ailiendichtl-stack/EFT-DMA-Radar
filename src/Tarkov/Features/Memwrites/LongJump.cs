using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Increases jump distance by multiplying air control values in EFTHardSettings.
    /// </summary>
    public sealed class LongJump : MemWriteFeature<LongJump>
    {
        private const float BASE_SAME_DIR = 1.2f;
        private const float BASE_ORT_DIR = 0.9f;
        private bool _lastEnabledState;
        private float _lastMultiplier;

        public override bool Enabled
        {
            get => App.Config.MemWrites.LongJumpEnabled;
            set => App.Config.MemWrites.LongJumpEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var multiplier = App.Config.MemWrites.LongJumpMultiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var multChanged = Math.Abs(multiplier - _lastMultiplier) > 0.001f;

                if (!stateChanged && !multChanged)
                    return;

                var hs = HardSettingsResolver.GetInstance();
                if (!MemDMA.IsValidVirtualAddress(hs))
                    return;

                if (Enabled)
                {
                    Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.AIR_CONTROL_SAME_DIR, BASE_SAME_DIR * multiplier);
                    Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.AIR_CONTROL_NONE_OR_ORT_DIR, BASE_ORT_DIR * multiplier);
                }
                else
                {
                    Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.AIR_CONTROL_SAME_DIR, BASE_SAME_DIR);
                    Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.AIR_CONTROL_NONE_OR_ORT_DIR, BASE_ORT_DIR);
                }

                _lastEnabledState = Enabled;
                _lastMultiplier = multiplier;
            }
            catch { }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _lastMultiplier = 0;
            HardSettingsResolver.Reset();
        }
    }
}
