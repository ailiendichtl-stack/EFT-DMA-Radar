/*
 * Lone EFT DMA Radar
 * Ported from Lone DMA upstream
 */

using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Skia;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Hazards
{
    /// <summary>
    /// Defines an interface for in-game world hazards (radiation, gas, etc.).
    /// </summary>
    public interface IWorldHazard : IWorldEntity, IMouseoverEntity
    {
        /// <summary>
        /// Description of the hazard/type.
        /// </summary>
        string HazardType { get; }
    }
}
