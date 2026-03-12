using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables frostbite screen effect by writing opacity to 0.
    /// </summary>
    public sealed class DisableFrostbite : MemWriteFeature<DisableFrostbite>
    {
        private bool _lastEnabledState;
        private ulong _cachedFrostbite;

        public override bool Enabled
        {
            get => App.Config.MemWrites.DisableFrostbiteEnabled;
            set => App.Config.MemWrites.DisableFrostbiteEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var frostbite = GetFrostbiteEffect();
                if (!MemDMA.IsValidVirtualAddress(frostbite))
                    return;

                float opacity = Enabled ? 0f : 1f;
                Memory.WriteValue(frostbite + SDK.Offsets.FrostbiteEffect._opacity, opacity);
                _lastEnabledState = Enabled;
            }
            catch
            {
                _cachedFrostbite = 0;
            }
        }

        private ulong GetFrostbiteEffect()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedFrostbite))
                return _cachedFrostbite;

            var fps = MemDMA.CameraManager?.FPSCamera ?? 0;
            if (!MemDMA.IsValidVirtualAddress(fps))
                return 0;

            var effectsObjClass = MonoBehaviour.GetComponentFromBehaviour(fps, "EffectsController");
            if (!MemDMA.IsValidVirtualAddress(effectsObjClass))
                return 0;

            var effectsController = Memory.ReadPtr(effectsObjClass + ObjectClass.MonoBehaviourOffset);
            if (!MemDMA.IsValidVirtualAddress(effectsController))
                return 0;

            var frostbite = Memory.ReadPtr(effectsController + SDK.Offsets.EffectsController._frostbiteEffect);
            if (!MemDMA.IsValidVirtualAddress(frostbite))
                return 0;

            _cachedFrostbite = frostbite;
            return frostbite;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedFrostbite = 0;
        }
    }
}
