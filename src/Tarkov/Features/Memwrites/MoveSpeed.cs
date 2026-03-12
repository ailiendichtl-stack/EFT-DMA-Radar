using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Multiplies player movement speed via the body animator.
    /// Auto-limits when player is overweight (>=39.8kg).
    /// </summary>
    public sealed class MoveSpeed : MemWriteFeature<MoveSpeed>
    {
        private const float BASE_SPEED = 1.0f;
        private const float WEIGHT_LIMIT = 39.8f;
        private const float SPEED_TOLERANCE = 0.1f;

        private bool _lastEnabledState;
        private float _lastSpeed;
        private ulong _cachedAnimator;

        public override bool Enabled
        {
            get => App.Config.MemWrites.MoveSpeedEnabled;
            set => App.Config.MemWrites.MoveSpeedEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var configSpeed = App.Config.MemWrites.MoveSpeedMultiplier;
                var stateChanged = Enabled != _lastEnabledState;
                var speedChanged = Math.Abs(_lastSpeed - configSpeed) > SPEED_TOLERANCE;

                var animator = GetAnimator(localPlayer);
                if (!MemDMA.IsValidVirtualAddress(animator))
                    return;

                // Check weight
                var physical = Memory.ReadPtr(localPlayer + SDK.Offsets.Player.Physical, false);
                if (MemDMA.IsValidVirtualAddress(physical))
                {
                    var weight = Memory.ReadValue<float>(physical + SDK.Offsets.Physical.PreviousWeight, false);
                    if (weight >= WEIGHT_LIMIT)
                    {
                        // Reset to normal if overweight
                        var currentSpeed = Memory.ReadValue<float>(animator + SDK.Offsets.UnityAnimator.Speed, false);
                        if (currentSpeed > 0f && Math.Abs(currentSpeed - BASE_SPEED) > 0.01f)
                            Memory.WriteValue(animator + SDK.Offsets.UnityAnimator.Speed, BASE_SPEED);
                        return;
                    }
                }

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        Memory.WriteValue(animator + SDK.Offsets.UnityAnimator.Speed, BASE_SPEED);
                        _lastEnabledState = false;
                    }
                    return;
                }

                if (stateChanged || speedChanged)
                {
                    var currentSpeed = Memory.ReadValue<float>(animator + SDK.Offsets.UnityAnimator.Speed, false);
                    if (currentSpeed > 0f && currentSpeed < 100f && Math.Abs(currentSpeed - configSpeed) > 0.01f)
                        Memory.WriteValue(animator + SDK.Offsets.UnityAnimator.Speed, configSpeed);

                    _lastEnabledState = true;
                    _lastSpeed = configSpeed;
                }
            }
            catch
            {
                ClearCache();
            }
        }

        private ulong GetAnimator(LocalPlayer localPlayer)
        {
            if (MemDMA.IsValidVirtualAddress(_cachedAnimator))
                return _cachedAnimator;

            try
            {
                var animatorsPtr = Memory.ReadPtr(localPlayer + SDK.Offsets.Player._animators, false);
                if (!MemDMA.IsValidVirtualAddress(animatorsPtr))
                    return 0;

                using var animators = UnityArray<ulong>.Create(animatorsPtr, false);
                if (animators.Count <= 1)
                    return 0;

                var bodyAnimator = animators.ElementAtOrDefault(1);
                if (!MemDMA.IsValidVirtualAddress(bodyAnimator))
                    return 0;

                var unityAnimator = Memory.ReadPtr(bodyAnimator + SDK.Offsets.BodyAnimator.UnityAnimator, false);
                if (!MemDMA.IsValidVirtualAddress(unityAnimator))
                    return 0;

                var nativeAnimator = Memory.ReadPtr(unityAnimator + ObjectClass.MonoBehaviourOffset, false);
                if (!MemDMA.IsValidVirtualAddress(nativeAnimator))
                    return 0;

                _cachedAnimator = nativeAnimator;
                return nativeAnimator;
            }
            catch { return 0; }
        }

        private void ClearCache() => _cachedAnimator = 0;

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _lastSpeed = 0;
            ClearCache();
        }
    }
}
