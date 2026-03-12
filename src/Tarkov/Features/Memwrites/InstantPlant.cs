using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Makes planting actions near-instant by setting plant time to 0.001.
    /// Continuously writes while enabled since plant time resets per action.
    /// </summary>
    public sealed class InstantPlant : MemWriteFeature<InstantPlant>
    {
        private ulong _cachedPlantState;
        private const float INSTANT_SPEED = 0.001f;

        public override bool Enabled
        {
            get => App.Config.MemWrites.InstantPlantEnabled;
            set => App.Config.MemWrites.InstantPlantEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (!Enabled)
                    return;

                var plantState = GetPlantState(localPlayer);
                if (!MemDMA.IsValidVirtualAddress(plantState))
                    return;

                var plantTimeAddr = plantState + SDK.Offsets.MovementState.PlantTime;
                var currentPlantTime = Memory.ReadValue<float>(plantTimeAddr, false);

                if (Math.Abs(currentPlantTime - INSTANT_SPEED) > 0.0001f && currentPlantTime > 0)
                {
                    Memory.WriteValue(plantTimeAddr, INSTANT_SPEED);
                    DebugLogger.LogDebug($"[InstantPlant] Set plant time {currentPlantTime:F6} -> {INSTANT_SPEED:F6}");
                }
            }
            catch
            {
                _cachedPlantState = 0;
            }
        }

        private ulong GetPlantState(LocalPlayer localPlayer)
        {
            if (MemDMA.IsValidVirtualAddress(_cachedPlantState))
                return _cachedPlantState;

            var movementContext = localPlayer.MovementContext;
            if (!MemDMA.IsValidVirtualAddress(movementContext))
                return 0;

            var plantState = Memory.ReadPtr(movementContext + SDK.Offsets.MovementContext.PlantState, false);
            if (!MemDMA.IsValidVirtualAddress(plantState))
                return 0;

            _cachedPlantState = plantState;
            return plantState;
        }

        public override void OnRaidStart()
        {
            _cachedPlantState = 0;
        }
    }
}
