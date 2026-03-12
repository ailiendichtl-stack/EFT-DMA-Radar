using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;
using System.Numerics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Toggles third-person camera by modifying the HandsContainer camera offset.
    /// </summary>
    public sealed class ThirdPerson : MemWriteFeature<ThirdPerson>
    {
        private bool _lastEnabledState;
        private ulong _cachedHandsContainer;

        private static readonly Vector3 THIRD_PERSON_ON = new(0.04f, 0.14f, -2.2f);
        private static readonly Vector3 THIRD_PERSON_OFF = new(0.04f, 0.04f, 0.05f);

        public override bool Enabled
        {
            get => App.Config.MemWrites.ThirdPersonEnabled;
            set => App.Config.MemWrites.ThirdPersonEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(500);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (Enabled == _lastEnabledState)
                    return;

                var handsContainer = GetHandsContainer(localPlayer);
                if (!MemDMA.IsValidVirtualAddress(handsContainer))
                    return;

                var offset = Enabled ? THIRD_PERSON_ON : THIRD_PERSON_OFF;
                Memory.WriteValue(handsContainer + SDK.Offsets.HandsContainer.CameraOffset, offset);

                _lastEnabledState = Enabled;
                DebugLogger.LogDebug($"[ThirdPerson] {(Enabled ? "Enabled" : "Disabled")}");
            }
            catch
            {
                _cachedHandsContainer = 0;
            }
        }

        private ulong GetHandsContainer(LocalPlayer localPlayer)
        {
            if (MemDMA.IsValidVirtualAddress(_cachedHandsContainer))
                return _cachedHandsContainer;

            var pwa = localPlayer.PWA;
            if (!MemDMA.IsValidVirtualAddress(pwa))
                return 0;

            var handsContainer = Memory.ReadPtr(pwa + SDK.Offsets.ProceduralWeaponAnimation.HandsContainer, false);
            if (!MemDMA.IsValidVirtualAddress(handsContainer))
                return 0;

            _cachedHandsContainer = handsContainer;
            return handsContainer;
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedHandsContainer = 0;
        }
    }
}
