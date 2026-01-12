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

        private static string GetObjectiveTypeDisplayName(QuestObjectiveType type)
        {
            return type switch
            {
                QuestObjectiveType.Visit => "Visit",
                QuestObjectiveType.Mark => "Mark Location",
                QuestObjectiveType.PlantItem => "Plant Item",
                QuestObjectiveType.PlantQuestItem => "Plant Quest Item",
                QuestObjectiveType.FindQuestItem => "Find Quest Item",
                QuestObjectiveType.FindItem => "Find Item",
                QuestObjectiveType.GiveQuestItem => "Give Quest Item",
                QuestObjectiveType.GiveItem => "Give Item",
                QuestObjectiveType.BuildWeapon => "Build Weapon",
                QuestObjectiveType.Shoot => "Eliminate",
                QuestObjectiveType.Extract => "Extract",
                QuestObjectiveType.UseItem => "Use Item",
                QuestObjectiveType.SellItem => "Sell Item",
                QuestObjectiveType.Skill => "Level Skill",
                QuestObjectiveType.Experience => "Gain XP",
                QuestObjectiveType.TraderLevel => "Trader Level",
                QuestObjectiveType.TaskStatus => "Complete Quest",
                QuestObjectiveType.TraderStanding => "Trader Rep",
                _ => "Unknown"
            };
        }

        private string GetDisplayText()
        {
            if (!TarkovDataManager.TaskData.TryGetValue(QuestId, out var task))
                return "Quest Location";

            var traderName = task.Trader?.Name ?? "Unknown";
            var questName = task.Name ?? "Unknown Quest";

            // Find objective
            var objective = task.Objectives?.FirstOrDefault(o => o.Id == ObjectiveId);
            if (objective == null)
                return $"[{traderName}] {questName}";

            // Line 1: [Trader] Quest Name
            var line1 = $"[{traderName}] {questName}";

            // Line 2: Type (progress) [badges]
            var typeName = GetObjectiveTypeDisplayName(objective.Type);
            var line2 = typeName;

            // Add progress if available
            var progress = Memory.Quests?.GetObjectiveProgress(QuestId, ObjectiveId);
            if (progress.HasValue)
            {
                var (isCompleted, currentCount) = progress.Value;
                var targetCount = objective.Count > 0 ? objective.Count : 1;

                if (isCompleted)
                    line2 += " [Complete]";
                else if (targetCount > 1)
                    line2 += $" ({currentCount}/{targetCount})";
            }

            // Add FIR badge
            if (objective.FoundInRaid)
                line2 += " [FIR]";

            // Add Kappa/Lightkeeper badges
            if (task.KappaRequired)
                line2 += " [Kappa]";
            else if (task.LightkeeperRequired)
                line2 += " [Lightkeeper]";

            // Line 3: Keys (if present)
            string line3 = null;
            if (task.NeededKeys?.Count > 0)
            {
                var keyNames = task.NeededKeys
                    .SelectMany(group => group.Keys ?? Enumerable.Empty<TarkovDataManager.TaskElement.ObjectiveElement.MarkerItemClass>())
                    .Select(k => k.Name)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Take(3)
                    .ToList();

                if (keyNames.Count > 0)
                {
                    var keyText = string.Join(", ", keyNames);
                    var remaining = task.NeededKeys.Sum(g => g.Keys?.Count ?? 0) - keyNames.Count;
                    if (remaining > 0)
                        keyText += $", +{remaining} more";
                    line3 = $"Keys: {keyText}";
                }
            }

            // Line 4: Description (adjusted length)
            string line4 = null;
            if (!string.IsNullOrEmpty(objective.Description))
            {
                var maxLen = QuestConstants.MaxDescriptionLength;
                line4 = objective.Description.Length > maxLen
                    ? objective.Description.Substring(0, maxLen) + "..."
                    : objective.Description;
            }

            // Assemble multi-line tooltip
            var lines = new List<string> { line1, line2 };
            if (line3 != null) lines.Add(line3);
            if (line4 != null) lines.Add(line4);

            return string.Join("\n", lines);
        }
    }
}
