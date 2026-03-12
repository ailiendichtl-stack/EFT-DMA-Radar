using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Toggles thermal vision. Auto-disables when player is ADS to prevent scope conflicts.
    /// Suppresses visual noise/artifacts when enabled.
    /// </summary>
    public sealed class ThermalVision : MemWriteFeature<ThermalVision>
    {
        private bool _currentState;
        private ulong _cachedComponent;

        public override bool Enabled
        {
            get => App.Config.MemWrites.ThermalVisionEnabled;
            set => App.Config.MemWrites.ThermalVisionEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                // Disable thermal when ADS to avoid scope conflicts
                bool isAiming = false;
                if (MemDMA.IsValidVirtualAddress(localPlayer.PWA))
                    isAiming = Memory.ReadValue<bool>(localPlayer.PWA + SDK.Offsets.ProceduralWeaponAnimation.IsAiming, false);

                var targetState = Enabled && !isAiming;
                if (targetState == _currentState)
                    return;

                var component = GetComponent();
                if (!MemDMA.IsValidVirtualAddress(component))
                    return;

                Memory.WriteValue(component + SDK.Offsets.ThermalVision.On, targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.IsNoisy, !targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.IsFpsStuck, !targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.IsMotionBlurred, !targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.IsGlitch, !targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.IsPixelated, !targetState);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.ChromaticAberrationThermalShift, targetState ? 0f : 0.013f);
                Memory.WriteValue(component + SDK.Offsets.ThermalVision.UnsharpRadiusBlur, targetState ? 0.0001f : 5f);

                _currentState = targetState;
            }
            catch
            {
                _cachedComponent = 0;
            }
        }

        private ulong GetComponent()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedComponent))
                return _cachedComponent;

            var fps = MemDMA.CameraManager?.FPSCamera ?? 0;
            if (!MemDMA.IsValidVirtualAddress(fps))
                return 0;

            var objClass = MonoBehaviour.GetComponentFromBehaviour(fps, "ThermalVision");
            if (!MemDMA.IsValidVirtualAddress(objClass))
                return 0;

            var managed = Memory.ReadPtr(objClass + ObjectClass.MonoBehaviourOffset);
            if (!MemDMA.IsValidVirtualAddress(managed))
                return 0;

            _cachedComponent = managed;
            return managed;
        }

        public override void OnRaidStart()
        {
            _currentState = false;
            _cachedComponent = 0;
        }
    }
}
