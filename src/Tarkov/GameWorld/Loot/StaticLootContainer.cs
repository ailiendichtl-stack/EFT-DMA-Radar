/*
 * Lone EFT DMA Radar
 * Brought to you by Lone (Lone DMA)
 * 
MIT License

Copyright (c) 2025 Lone DMA

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 *
*/

using Collections.Pooled;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    public sealed class StaticLootContainer : LootItem
    {
        private static readonly TarkovMarketItem _default = new();
        private readonly ulong _interactiveClass;
        private List<ContainerItem> _contents;
        private bool _contentsLoaded = false;
        private string _cachedFilterColor;

        public override string Name { get; }

        /// <summary>
        /// Container's BSG ID.
        /// </summary>
        public override string ID { get; }

        /// <summary>
        /// True if the container has been searched by LocalPlayer or another Networked Entity.
        /// </summary>
        public bool Searched { get; private set; }

        /// <summary>
        /// Items contained within this container (offline PVE only).
        /// </summary>
        public IReadOnlyList<ContainerItem> Contents => _contents;

        /// <summary>
        /// Total value of all items in this container.
        /// </summary>
        public int TotalValue => _contents?.Sum(x => x.Price) ?? 0;

        /// <summary>
        /// True if this container has valuable contents (meets minimum loot value threshold).
        /// </summary>
        public bool HasValuableContents => TotalValue >= App.Config.Loot.MinValue;

        /// <summary>
        /// True if this container has very valuable contents (meets valuable loot threshold).
        /// </summary>
        public bool IsValuableContainer => TotalValue >= App.Config.Loot.MinValueValuable;

        /// <summary>
        /// True if this container has any items marked as Important via custom loot filters.
        /// </summary>
        public bool HasImportantContents => _contents?.Any(x => x.IsImportant) ?? false;

        /// <summary>
        /// Gets the filter color from the highest value important item (cached on contents load).
        /// </summary>
        public string ImportantItemFilterColor => _cachedFilterColor;

        /// <summary>
        /// True if this container has any items needed for tracked hideout upgrades.
        /// </summary>
        public bool HasHideoutContents => _contents?.Any(x => x.IsHideoutItem) ?? false;

        /// <summary>
        /// True if this container has any items needed for active quest objectives.
        /// </summary>
        public bool HasQuestContents => _contents?.Any(x => x.IsQuestItem) ?? false;

        /// <summary>
        /// Checks if this container should be displayed based on min value filter.
        /// Containers with important, hideout, or quest items always pass the filter.
        /// When PVE scanning is disabled, all containers pass (we can't determine value).
        /// </summary>
        private bool PassesMinValueFilter()
        {
            // If PVE scan is disabled, we can't determine value - show all containers
            if (!App.Config.Containers.PveScanEnabled) return true;
            var minValue = App.Config.Containers.MinValue;
            if (minValue <= 0) return true; // No filter
            if (HasImportantContents) return true; // Important items always show
            if (HasHideoutContents) return true; // Hideout items always show
            if (HasQuestContents) return true; // Quest items always show
            return TotalValue >= minValue;
        }

        public StaticLootContainer(string containerId, Vector3 position)
            : base(_default, position)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(containerId, nameof(containerId));
            ID = containerId;
            if (TarkovDataManager.AllContainers.TryGetValue(containerId, out var containerData))
            {
                Name = containerData.ShortName ?? "Container";
            }
            else
            {
                Name = "Container";
            }
        }

        // Internal constructor for LootManager with InteractiveClass
        internal StaticLootContainer(string containerId, Vector3 position, ulong interactiveClass)
            : this(containerId, position)
        {
            _interactiveClass = interactiveClass;
        }

        /// <summary>
        /// Updates the searched status of this container by reading memory.
        /// Called periodically by LootManager.
        /// </summary>
        internal void UpdateSearchedStatus()
        {
            if (_interactiveClass == 0)
                return;

            try
            {
                var interactingPlayer = Memory.ReadValue<ulong>(_interactiveClass + Offsets.LootableContainer.InteractingPlayer);
                if (interactingPlayer != 0)
                {
                    Searched = true;
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Refreshes container contents from memory.
        /// Only works in offline PVE where loot is pre-generated.
        /// Contents are cached after first successful read.
        /// </summary>
        internal void RefreshContents()
        {
            // Only scan contents if PVE scanning is enabled (offline mode only)
            if (!App.Config.Containers.PveScanEnabled)
                return;

            if (_contentsLoaded || _interactiveClass == 0)
                return;

            _contents = ContainerContentsReader.GetContainerContents(_interactiveClass);
            if (_contents.Count > 0)
            {
                _contentsLoaded = true;
                // Cache filter color from highest value important item
                _cachedFilterColor = _contents
                    .Where(x => x.IsImportant && !string.IsNullOrEmpty(x.FilterColor))
                    .OrderByDescending(x => x.Price)
                    .FirstOrDefault()?.FilterColor;
            }
        }

        public override void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            if (!App.Config.Containers.Enabled)
                return;

            if (App.Config.Containers.HideSearched && Searched)
                return;

            // Filter by min value (important items always pass)
            if (!PassesMinValueFilter())
                return;

            if (Position.WithinDistance(localPlayer.Position, App.Config.Containers.DrawDistance))
            {
                var label = GetContainerLabel();
                var paints = GetContainerPaints();
                var heightDiff = Position.Y - localPlayer.ReferenceHeight;
                var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
                MouseoverPosition = new Vector2(point.X, point.Y);
                SKPaints.ShapeOutline.StrokeWidth = 2f;
                var widgetFont = CustomFontManager.GetCurrentRadarWidgetFont();

                if (heightDiff > 1.45) // loot is above player
                {
                    var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                    canvas.DrawText("▲", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                    canvas.DrawText("▲", adjustedPoint, SKTextAlign.Center, widgetFont, paints.Item1);
                }
                else if (heightDiff < -1.45) // loot is below player
                {
                    var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                    canvas.DrawText("▼", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                    canvas.DrawText("▼", adjustedPoint, SKTextAlign.Center, widgetFont, paints.Item1);
                }
                else // loot is level with player
                {
                    var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                    canvas.DrawText("●", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                    canvas.DrawText("●", adjustedPoint, SKTextAlign.Center, widgetFont, paints.Item1);
                }

                // Draw label with value
                point.Offset(7 * App.Config.UI.UIScale, 3 * App.Config.UI.UIScale);
                canvas.DrawText(label, point, SKTextAlign.Left, widgetFont, SKPaints.TextOutline);
                canvas.DrawText(label, point, SKTextAlign.Left, widgetFont, paints.Item2);
            }
        }

        /// <summary>
        /// Gets the display label for this container (with value if available).
        /// </summary>
        private string GetContainerLabel()
        {
            if (TotalValue > 0)
                return $"[{Utilities.FormatNumberKM(TotalValue)}] {Name}";
            return Name;
        }

        /// <summary>
        /// Gets the paint colors for this container based on its contents.
        /// Priority: Important items (with filter color) > Quest items > Hideout items > Valuable container > Has valuable contents > Default
        /// </summary>
        private (SKPaint, SKPaint) GetContainerPaints()
        {
            // Important items have highest priority - use the actual filter color
            if (HasImportantContents)
            {
                var filterColor = ImportantItemFilterColor;
                if (!string.IsNullOrEmpty(filterColor))
                {
                    var filterPaints = LootItem.GetFilterPaints(filterColor);
                    return (filterPaints.Item1, filterPaints.Item2);
                }
                return (SKPaints.PaintFilteredLoot, SKPaints.TextFilteredLoot);
            }
            // Quest items (YellowGreen like loose loot quest items)
            if (HasQuestContents)
                return (SKPaints.PaintQuestItem, SKPaints.TextQuestItem);
            // Hideout items (user-selected color)
            if (HasHideoutContents)
                return (SKPaints.PaintHideoutItem, SKPaints.TextHideoutItem);
            if (IsValuableContainer)
                return (SKPaints.PaintImportantLoot, SKPaints.TextImportantLoot);
            if (HasValuableContents)
                return (SKPaints.PaintLoot, SKPaints.TextLoot);
            return (SKPaints.PaintContainerLoot, SKPaints.TextLoot);
        }

        public override void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var isExpanded = TooltipCard.IsExpanded(this);

            // Determine accent color based on container contents
            SKColor accentColor;
            if (HasImportantContents)
                accentColor = !string.IsNullOrEmpty(ImportantItemFilterColor) && SKColor.TryParse(ImportantItemFilterColor, out var filterColor)
                    ? filterColor
                    : SKColors.MediumPurple;
            else if (HasQuestContents)
                accentColor = TooltipColors.LootQuest;
            else if (HasHideoutContents)
                accentColor = TooltipColors.LootHideout;
            else if (IsValuableContainer)
                accentColor = TooltipColors.LootValuable;
            else
                accentColor = TooltipColors.Container;

            var tooltip = new TooltipData(Name, accentColor);

            // Status
            if (Searched)
                tooltip.AddRow("Status", "Searched", TooltipColors.Default);

            // Value info
            if (_contents?.Count > 0)
            {
                tooltip.AddRow("Value", $"${Utilities.FormatNumberKM(TotalValue)}", TooltipColors.LootValuable);
                tooltip.AddRow("Items", $"{_contents.Count}");

                // Show items - more when expanded
                var itemLimit = isExpanded ? 15 : 5;
                var topItems = _contents.OrderByDescending(x => x.Price).Take(itemLimit);
                foreach (var item in topItems)
                {
                    var itemColor = item.IsQuestItem ? TooltipColors.LootQuest
                        : item.IsHideoutItem ? TooltipColors.LootHideout
                        : (SKColor?)null;
                    tooltip.AddRow($"  ${Utilities.FormatNumberKM(item.Price)}", item.Name, itemColor);
                }

                // Show remaining count if not expanded
                if (!isExpanded && _contents.Count > 5)
                    tooltip.AddRow("", $"(+{_contents.Count - 5} more)", TooltipColors.Default);
            }
            else
            {
                tooltip.AddRow("Contents", "Unknown", TooltipColors.Default);
            }

            // Distance
            var distance = Vector3.Distance(localPlayer.Position, Position);
            tooltip.AddRow("Distance", $"{distance:F1} m");

            // Click hint when not expanded and has more items
            if (!isExpanded && _contents?.Count > 5)
                tooltip.AddRow("", "[Click to expand]", TooltipColors.Default);

            var pos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var canvasWidth = mapParams.Bounds.Width * mapParams.XScale;
            var canvasHeight = mapParams.Bounds.Height * mapParams.YScale;
            TooltipCard.Draw(canvas, pos, tooltip, canvasWidth, canvasHeight);
        }
    }
}
