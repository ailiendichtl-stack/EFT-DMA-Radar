using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Toggles night vision on/off by writing to the NightVision component on the FPS camera.
    /// </summary>
    public sealed class NightVision : MemWriteFeature<NightVision>
    {
        private bool _lastEnabledState;
        private ulong _cachedComponent;

        public override bool Enabled
        {
            get => App.Config.MemWrites.NightVisionEnabled;
            set => App.Config.MemWrites.NightVisionEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var component = GetComponent();
                if (!MemDMA.IsValidVirtualAddress(component))
                    return;

                Memory.WriteValue(component + SDK.Offsets.NightVision._on, Enabled);
                _lastEnabledState = Enabled;
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

            var objClass = MonoBehaviour.GetComponentFromBehaviour(fps, "NightVision");
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
            _lastEnabledState = false;
            _cachedComponent = 0;
        }
    }
}
