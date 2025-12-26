/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Represents a quest objective location on the map.
    /// </summary>
    public sealed class QuestLocation : IWorldEntity, IMapEntity, IMouseoverEntity
    {
        private readonly Vector3 _position;

        /// <summary>
        /// Quest ID this location belongs to.
        /// </summary>
        public string QuestId { get; }

        /// <summary>
        /// Objective ID.
        /// </summary>
        public string ObjectiveId { get; }

        /// <summary>
        /// Position in world space.
        /// </summary>
        public ref readonly Vector3 Position => ref _position;

        /// <summary>
        /// Position for mouseover detection.
        /// </summary>
        public Vector2 MouseoverPosition { get; set; }

        public QuestLocation(string questId, string objectiveId, Vector3 position)
        {
            QuestId = questId;
            ObjectiveId = objectiveId;
            _position = position;
        }

        /// <summary>
        /// Draw the quest location on the radar map.
        /// </summary>
        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var heightDiff = Position.Y - localPlayer.ReferenceHeight;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);

            var paint = SKPaints.PaintQuestZone;
            SKPaints.ShapeOutline.StrokeWidth = QuestConstants.QuestMarkerStrokeWidth;

            if (heightDiff > QuestConstants.QuestMarkerHeightThreshold)
            {
                // Location is above player - draw up arrow
                using var path = point.GetUpArrow(QuestConstants.QuestMarkerSquareSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else if (heightDiff < -QuestConstants.QuestMarkerHeightThreshold)
            {
                // Location is below player - draw down arrow
                using var path = point.GetDownArrow(QuestConstants.QuestMarkerSquareSize);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else
            {
                // Location is level with player - draw square
                float size = QuestConstants.QuestMarkerSquareSize * App.Config.UI.UIScale;
                var rect = new SKRect(point.X - size, point.Y - size, point.X + size, point.Y + size);
                canvas.DrawRect(rect, SKPaints.ShapeOutline);
                canvas.DrawRect(rect, paint);
            }
        }

        /// <summary>
        /// Draw mouseover tooltip.
        /// </summary>
        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            string text = GetDisplayText();
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, text);
        }

        private string GetDisplayText()
        {
            if (!TarkovDataManager.TaskData.TryGetValue(QuestId, out var task))
                return "Quest Location";

            var traderName = task.Trader?.Name ?? "Unknown";
            var questName = task.Name ?? "Unknown Quest";

            // Find the objective description
            string objDesc = null;
            if (task.Objectives != null)
            {
                var obj = task.Objectives.FirstOrDefault(o => o.Id == ObjectiveId);
                if (obj != null)
                {
                    objDesc = obj.Description;
                    if (objDesc != null && objDesc.Length > QuestConstants.MaxDescriptionLength)
                        objDesc = objDesc.Substring(0, QuestConstants.MaxDescriptionLength) + "...";
                }
            }

            return objDesc != null
                ? $"[{traderName}] {questName}\n{objDesc}"
                : $"[{traderName}] {questName}";
        }
    }
}
