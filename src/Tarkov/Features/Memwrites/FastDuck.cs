using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Speeds up crouch/prone transitions by writing POSE_CHANGING_SPEED to EFTHardSettings.
    /// </summary>
    public sealed class FastDuck : MemWriteFeature<FastDuck>
    {
        private const float FAST_SPEED = 9999f;
        private const float DEFAULT_SPEED = 3f;
        private bool _lastEnabledState;

        public override bool Enabled
        {
            get => App.Config.MemWrites.FastDuckEnabled;
            set => App.Config.MemWrites.FastDuckEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var hs = HardSettingsResolver.GetInstance();
                if (!MemDMA.IsValidVirtualAddress(hs))
                    return;

                var target = Enabled ? FAST_SPEED : DEFAULT_SPEED;
                Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.POSE_CHANGING_SPEED, target);
                _lastEnabledState = Enabled;
            }
            catch { }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            HardSettingsResolver.Reset();
        }
    }
}
