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
        /// Polygon outline vertices for area-based quest zones (null for point locations).
        /// </summary>
        public List<Vector3> Outline { get; private set; }

        /// <summary>
        /// True if this location has polygon outline data.
        /// </summary>
        public bool HasOutline => Outline != null && Outline.Count >= 3;

        /// <summary>
        /// Position for mouseover detection.
        /// </summary>
        public Vector2 MouseoverPosition { get; set; }

        public QuestLocation(string questId, string objectiveId, Vector3 position, List<Vector3> outline = null)
        {
            QuestId = questId;
            ObjectiveId = objectiveId;
            _position = position;
            Outline = outline;
        }

        /// <summary>
        /// Draw the quest location on the radar map.
        /// </summary>
        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            // Draw polygon outline if available
            if (HasOutline)
            {
                DrawOutline(canvas, mapParams);
            }

            var heightDiff = Position.Y - localPlayer.ReferenceHeight;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);

            float scale = App.Config.UI.UIScale;
            float markerRadius = (HasOutline ? 4f : 6f) * scale;

            // Quest marker colors
            var fillColor = TooltipColors.Quest;
            var borderColor = SKColors.White;

            using var fillPaint = new SKPaint
            {
                Color = fillColor,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            using var borderPaint = new SKPaint
            {
                Color = borderColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f * scale,
                IsAntialias = true
            };

            if (heightDiff > QuestConstants.QuestMarkerHeightThreshold)
            {
                // Location is above player - draw circle with up indicator
                canvas.DrawCircle(point, markerRadius, fillPaint);
                canvas.DrawCircle(point, markerRadius, borderPaint);

                // Small up arrow above
                var arrowY = point.Y - markerRadius - 4 * scale;
                using var arrowPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var arrowPath = new SKPath();
                arrowPath.MoveTo(point.X, arrowY - 4 * scale);
                arrowPath.LineTo(point.X - 4 * scale, arrowY);
                arrowPath.LineTo(point.X + 4 * scale, arrowY);
                arrowPath.Close();
                canvas.DrawPath(arrowPath, arrowPaint);
            }
            else if (heightDiff < -QuestConstants.QuestMarkerHeightThreshold)
            {
                // Location is below player - draw circle with down indicator
                canvas.DrawCircle(point, markerRadius, fillPaint);
                canvas.DrawCircle(point, markerRadius, borderPaint);

                // Small down arrow below
                var arrowY = point.Y + markerRadius + 4 * scale;
                using var arrowPaint = new SKPaint { Color = fillColor, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var arrowPath = new SKPath();
                arrowPath.MoveTo(point.X, arrowY + 4 * scale);
                arrowPath.LineTo(point.X - 4 * scale, arrowY);
                arrowPath.LineTo(point.X + 4 * scale, arrowY);
                arrowPath.Close();
                canvas.DrawPath(arrowPath, arrowPaint);
            }
            else
            {
                // Location is level with player - draw filled circle
                canvas.DrawCircle(point, markerRadius, fillPaint);
                canvas.DrawCircle(point, markerRadius, borderPaint);
            }
        }

        /// <summary>
        /// Draw mouseover tooltip.
        /// </summary>
        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var isExpanded = TooltipCard.IsExpanded(this);
            var tooltip = new TooltipData("Quest Objective", TooltipColors.Quest);

            if (TarkovDataManager.TaskData.TryGetValue(QuestId, out var task))
            {
                var traderName = task.Trader?.Name ?? "Unknown";
                var questName = task.Name ?? "Unknown Quest";

                tooltip = new TooltipData(questName, TooltipColors.Quest);
                tooltip.SetSubHeader(traderName);

                // Find objective
                var objective = task.Objectives?.FirstOrDefault(o => o.Id == ObjectiveId);
                if (objective != null)
                {
                    // Objective type
                    var typeName = GetObjectiveTypeDisplayName(objective.Type);
                    tooltip.AddRow("Type", typeName);

                    // Progress
                    var progress = Memory.Quests?.GetObjectiveProgress(QuestId, ObjectiveId);
                    if (progress.HasValue)
                    {
                        var (isCompleted, currentCount) = progress.Value;
                        var targetCount = objective.Count > 0 ? objective.Count : 1;

                        if (isCompleted)
                            tooltip.AddRow("Status", "Complete", TooltipColors.Teammate);
                        else if (targetCount > 1)
                            tooltip.AddRow("Progress", $"{currentCount}/{targetCount}");
                    }

                    // FIR requirement
                    if (objective.FoundInRaid)
                        tooltip.AddRow("Requirement", "Found in Raid", TooltipColors.LootQuest);

                    // Description - full when expanded, truncated otherwise
                    if (!string.IsNullOrEmpty(objective.Description))
                    {
                        if (isExpanded)
                        {
                            // Show full description, split into multiple lines if needed
                            var desc = objective.Description;
                            var maxLineLength = 50;
                            var lines = SplitTextIntoLines(desc, maxLineLength);
                            for (int i = 0; i < lines.Count; i++)
                            {
                                tooltip.AddRow(i == 0 ? "Objective" : "", lines[i]);
                            }
                        }
                        else
                        {
                            var desc = objective.Description.Length > 40
                                ? objective.Description.Substring(0, 40) + "..."
                                : objective.Description;
                            tooltip.AddRow("Objective", desc);
                        }
                    }

                    // Expanded: Show item details if applicable
                    if (isExpanded)
                    {
                        if (objective.Item != null && !string.IsNullOrEmpty(objective.Item.Name))
                            tooltip.AddRow("Item", objective.Item.Name, TooltipColors.LootQuest);
                        if (objective.QuestItem != null && !string.IsNullOrEmpty(objective.QuestItem.Name))
                            tooltip.AddRow("Quest Item", objective.QuestItem.Name, TooltipColors.LootQuest);
                        if (objective.MarkerItem != null && !string.IsNullOrEmpty(objective.MarkerItem.Name))
                            tooltip.AddRow("Marker", objective.MarkerItem.Name);
                    }
                }

                // Keys needed - show all when expanded
                if (task.NeededKeys?.Count > 0)
                {
                    var allKeys = task.NeededKeys
                        .SelectMany(group => group.Keys ?? Enumerable.Empty<TarkovDataManager.TaskElement.ObjectiveElement.MarkerItemClass>())
                        .Select(k => k.ShortName ?? k.Name)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList();

                    if (allKeys.Count > 0)
                    {
                        if (isExpanded)
                        {
                            // Show all keys
                            for (int i = 0; i < allKeys.Count; i++)
                            {
                                tooltip.AddRow(i == 0 ? "Keys" : "", allKeys[i], TooltipColors.LootValuable);
                            }
                        }
                        else
                        {
                            // Show first 2 only
                            var keyNames = allKeys.Take(2).ToList();
                            var keysText = string.Join(", ", keyNames);
                            if (allKeys.Count > 2)
                                keysText += $" (+{allKeys.Count - 2})";
                            tooltip.AddRow("Keys", keysText, TooltipColors.LootValuable);
                        }
                    }
                }

                // Badges
                if (task.KappaRequired)
                    tooltip.AddRow("Badge", "Kappa Required", SKColors.Gold);
                else if (task.LightkeeperRequired)
                    tooltip.AddRow("Badge", "Lightkeeper", SKColors.LightBlue);
            }

            // Distance
            var distance = Vector3.Distance(localPlayer.Position, Position);
            tooltip.AddRow("Distance", $"{distance:F1} m");

            // Click hint when not expanded
            if (!isExpanded)
                tooltip.AddRow("", "[Click to expand]", TooltipColors.Default);

            var pos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var canvasWidth = mapParams.Bounds.Width * mapParams.XScale;
            var canvasHeight = mapParams.Bounds.Height * mapParams.YScale;
            TooltipCard.Draw(canvas, pos, tooltip, canvasWidth, canvasHeight);
        }

        /// <summary>
        /// Split text into lines of max length.
        /// </summary>
        private static List<string> SplitTextIntoLines(string text, int maxLength)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var currentLine = "";

            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 <= maxLength)
                {
                    currentLine += (currentLine.Length > 0 ? " " : "") + word;
                }
                else
                {
                    if (currentLine.Length > 0)
                        lines.Add(currentLine);
                    currentLine = word;
                }
            }

            if (currentLine.Length > 0)
                lines.Add(currentLine);

            return lines;
        }

        /// <summary>
        /// Draw polygon outline for area-based quest zones.
        /// </summary>
        private void DrawOutline(SKCanvas canvas, EftMapParams mapParams)
        {
            if (!HasOutline)
                return;

            using var path = new SKPath();
            bool first = true;

            foreach (var vertex in Outline)
            {
                var point = vertex.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
                if (first)
                {
                    path.MoveTo(point);
                    first = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }

            path.Close();

            // Fill with semi-transparent quest color
            using var fillPaint = new SKPaint
            {
                Color = TooltipColors.Quest.WithAlpha(40),  // Very transparent fill
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            canvas.DrawPath(path, fillPaint);

            // Stroke with more opaque quest color
            using var strokePaint = new SKPaint
            {
                Color = TooltipColors.Quest.WithAlpha(180),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f * App.Config.UI.UIScale,
                IsAntialias = true
            };
            canvas.DrawPath(path, strokePaint);
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
    }
}
