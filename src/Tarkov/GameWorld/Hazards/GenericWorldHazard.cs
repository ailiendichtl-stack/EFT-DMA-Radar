/*
 * Lone EFT DMA Radar
 * Ported from Lone DMA upstream
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using System.Text.Json.Serialization;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Hazards
{
    /// <summary>
    /// Represents a world hazard (radiation zone, gas, minefield, etc.).
    /// </summary>
    public class GenericWorldHazard : IWorldHazard
    {
        private Vector3 _position;

        [JsonPropertyName("hazardType")]
        public string HazardType { get; set; }

        [JsonPropertyName("position")]
        public TarkovDataManager.PositionElement PositionJson { get; set; }

        [JsonIgnore]
        public Vector2 MouseoverPosition { get; set; }

        [JsonIgnore]
        ref readonly Vector3 Position
        {
            get
            {
                if (_position == default && PositionJson != null)
                {
                    _position = PositionJson.AsVector3();
                }
                return ref _position;
            }
        }

        [JsonIgnore]
        ref readonly Vector3 IWorldEntity.Position => ref Position;

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var zoomedPos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(zoomedPos.X, zoomedPos.Y);
            zoomedPos.DrawHazardMarker(canvas);
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var zoomedPos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            zoomedPos.DrawMouseoverText(canvas, $"Hazard: {HazardType ?? "Unknown"}");
        }
    }
}
