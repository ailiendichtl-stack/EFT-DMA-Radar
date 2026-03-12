using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Removes weight penalties by zeroing overweight values and flags.
    /// One-time application per toggle.
    /// </summary>
    public sealed class MuleMode : MemWriteFeature<MuleMode>
    {
        private bool _lastEnabledState;

        public override bool Enabled
        {
            get => App.Config.MemWrites.MuleModeEnabled;
            set => App.Config.MemWrites.MuleModeEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var physical = Memory.ReadPtr(localPlayer + SDK.Offsets.Player.Physical, false);
                if (!MemDMA.IsValidVirtualAddress(physical))
                    return;

                if (Enabled)
                {
                    // Zero overweight values
                    Memory.WriteValue(physical + SDK.Offsets.Physical.Overweight, 0f);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.WalkOverweight, 0f);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.WalkSpeedLimit, 1f);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.Inertia, 0f);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.SprintOverweight, 0f);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.BerserkRestorationFactor, 1f);

                    // Reset overweight flags
                    Memory.WriteValue(physical + SDK.Offsets.Physical.IsOverweightA, false);
                    Memory.WriteValue(physical + SDK.Offsets.Physical.IsOverweightB, false);
                }

                _lastEnabledState = Enabled;
            }
            catch { }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
        }
    }
}
