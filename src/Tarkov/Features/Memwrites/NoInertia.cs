using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Removes movement inertia by zeroing walk/sprint/pose inertia values
    /// and boosting deceleration speed.
    /// </summary>
    public sealed class NoInertia : MemWriteFeature<NoInertia>
    {
        private bool _lastEnabledState;

        public override bool Enabled
        {
            get => App.Config.MemWrites.NoInertiaEnabled;
            set => App.Config.MemWrites.NoInertiaEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var mc = localPlayer.MovementContext;
                if (!MemDMA.IsValidVirtualAddress(mc))
                    return;

                float inertiaValue = Enabled ? 0f : 1f;

                Memory.WriteValue(mc + SDK.Offsets.MovementContext.WalkInertia, inertiaValue);
                Memory.WriteValue(mc + SDK.Offsets.MovementContext.SprintBrakeInertia, inertiaValue);
                Memory.WriteValue(mc + SDK.Offsets.MovementContext._poseInertia, inertiaValue);
                Memory.WriteValue(mc + SDK.Offsets.MovementContext._currentPoseInertia, inertiaValue);
                Memory.WriteValue(mc + SDK.Offsets.MovementContext._inertiaAppliedTime, inertiaValue);

                // Boost deceleration via EFTHardSettings
                var hs = HardSettingsResolver.GetInstance();
                if (MemDMA.IsValidVirtualAddress(hs))
                {
                    float decelSpeed = Enabled ? 100f : 1f;
                    Memory.WriteValue(hs + SDK.Offsets.EFTHardSettings.DecelerationSpeed, decelSpeed);
                }

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
