using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;
using System.Numerics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Extends lean distance by modifying ProceduralWeaponAnimation.PositionZeroSum.
    /// Direction is controlled externally (e.g. via keybind).
    /// </summary>
    public sealed class WideLean : MemWriteFeature<WideLean>
    {
        private bool _applied;

        /// <summary>
        /// Current lean direction. Set externally via keybind or UI.
        /// </summary>
        public static EWideLeanDirection Direction { get; set; } = EWideLeanDirection.Off;

        public override bool Enabled
        {
            get => App.Config.MemWrites.WideLeanEnabled;
            set => App.Config.MemWrites.WideLeanEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(100);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var dir = Direction;
                var pwa = localPlayer.PWA;

                if (!MemDMA.IsValidVirtualAddress(pwa))
                    return;

                if (Enabled && dir != EWideLeanDirection.Off && !_applied)
                {
                    var amt = App.Config.MemWrites.WideLeanAmount * 0.2f;
                    var vec = dir switch
                    {
                        EWideLeanDirection.Left => new Vector3(-amt, 0f, 0f),
                        EWideLeanDirection.Right => new Vector3(amt, 0f, 0f),
                        EWideLeanDirection.Up => new Vector3(0f, 0f, amt),
                        _ => Vector3.Zero
                    };

                    Memory.WriteValue(pwa + SDK.Offsets.ProceduralWeaponAnimation.PositionZeroSum, vec);
                    _applied = true;
                    DebugLogger.LogDebug("[WideLean] Applied");
                }
                else if (_applied && dir == EWideLeanDirection.Off)
                {
                    Memory.WriteValue(pwa + SDK.Offsets.ProceduralWeaponAnimation.PositionZeroSum, Vector3.Zero);
                    _applied = false;
                    DebugLogger.LogDebug("[WideLean] Reset");
                }
            }
            catch
            {
                Direction = EWideLeanDirection.Off;
                _applied = false;
            }
        }

        public override void OnRaidStart()
        {
            _applied = false;
            Direction = EWideLeanDirection.Off;
        }

        public enum EWideLeanDirection
        {
            Off,
            Left,
            Right,
            Up
        }
    }
}
