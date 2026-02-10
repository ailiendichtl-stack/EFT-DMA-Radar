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
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    public class LootItem : IMouseoverEntity, IMapEntity, IWorldEntity
    {
        private static EftDmaConfig Config { get; } = App.Config;
        private readonly TarkovMarketItem _item;
        private readonly bool _isQuestItem;

        public LootItem(TarkovMarketItem item, Vector3 position, bool isQuestItem = false)
        {
            ArgumentNullException.ThrowIfNull(item, nameof(item));
            _item = item;
            _position = position;
            _isQuestItem = isQuestItem;
        }

        public LootItem(string id, string name, Vector3 position, bool isQuestItem = false)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(id));
            ArgumentNullException.ThrowIfNull(name, nameof(name));
            _item = new TarkovMarketItem
            {
                Name = name,
                ShortName = name,
                FleaPrice = -1,
                TraderPrice = -1,
                BsgId = id
            };
            _position = position;
            _isQuestItem = isQuestItem;
        }

        /// <summary>
        /// Item's BSG ID.
        /// </summary>
        public virtual string ID => _item.BsgId;

        /// <summary>
        /// Item's Long Name.
        /// </summary>
        public virtual string Name => _item.Name;

        /// <summary>
        /// Item's Short Name.
        /// </summary>
        public string ShortName => _item.ShortName;

        /// <summary>
        /// Item's Price (In roubles).
        /// </summary>
        public int Price
        {
            get
            {
                long price;
                if (Config.Loot.PricePerSlot)
                {
                    if (Config.Loot.PriceMode is LootPriceMode.FleaMarket)
                        price = (long)((float)_item.FleaPrice / GridCount);
                    else
                        price = (long)((float)_item.TraderPrice / GridCount);
                }
                else
                {
                    if (Config.Loot.PriceMode is LootPriceMode.FleaMarket)
                        price = _item.FleaPrice;
                    else
                        price = _item.TraderPrice;
                }
                if (price <= 0)
                {
                    price = Config.Loot.PriceMode is LootPriceMode.FleaMarket ? _item.FleaPrice : _item.TraderPrice;
                }
                return (int)price;
            }
        }


        /// <summary>
        /// Number of grid spaces this item takes up.
        /// </summary>
        public int GridCount => _item.Slots == 0 ? 1 : _item.Slots;

        /// <summary>
        /// Custom filter for this item (if set).
        /// </summary>
        public LootFilterEntry CustomFilter => _item.CustomFilter;

        /// <summary>
        /// True if the item is important via the UI.
        /// </summary>
        public bool Important => CustomFilter?.Important ?? false;

        /// <summary>
        /// True if this item is marked as a quest item by game data or needed for an active quest.
        /// </summary>
        public bool IsQuestItem => _isQuestItem || (Memory?.Quests?.IsQuestItem(ID) ?? false);

        /// <summary>
        /// True if this item is needed for a tracked hideout upgrade.
        /// </summary>
        public bool IsHideoutItem => Hideout.HideoutManager.Instance?.IsHideoutItem(ID) ?? false;

        /// <summary>
        /// True if the item is blacklisted via the UI.
        /// </summary>
        public bool Blacklisted => CustomFilter?.Blacklisted ?? false;

        public bool IsMeds => _item.IsMed;
        public bool IsFood => _item.IsFood;
        public bool IsBackpack => _item.IsBackpack;
        public bool IsWeapon => _item.IsWeapon;
        public bool IsCurrency => _item.IsCurrency;

        /// <summary>
        /// Checks if an item exceeds regular loot price threshold.
        /// Unknown items (price &lt; 0) are always shown since we can't determine their value.
        /// </summary>
        public bool IsRegularLoot
        {
            get
            {
                if (Blacklisted)
                    return false;
                // Show unknown items (new event items, etc.) since we don't have price data
                if (_item.FleaPrice < 0 && _item.TraderPrice < 0)
                    return true;
                return Price >= App.Config.Loot.MinValue;
            }
        }

        /// <summary>
        /// Checks if an item exceeds valuable loot price threshold.
        /// </summary>
        public bool IsValuableLoot
        {
            get
            {
                if (Blacklisted)
                    return false;
                return Price >= App.Config.Loot.MinValueValuable;
            }
        }

        /// <summary>
        /// Checks if an item/container is important.
        /// </summary>
        public bool IsImportant
        {
            get
            {
                if (Blacklisted)
                    return false;
                return _item.Important;
            }
        }

        /// <summary>
        /// True if this item contains the specified Search Predicate.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns>True if search matches, otherwise False.</returns>
        public bool ContainsSearchPredicate(Predicate<LootItem> predicate)
        {
            return predicate(this);
        }

        private Vector3 _position; // Allow position updates for items in case they start rolling around
        public ref readonly Vector3 Position => ref _position;

        /// <summary>
        /// Updates the position of this loot item.
        /// </summary>
        /// <param name="newPosition">The new position</param>
        public void UpdatePosition(Vector3 newPosition)
        {
            _position = newPosition;
        }
        public Vector2 MouseoverPosition { get; set; }

        public virtual void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            if (IsQuestItem && !App.Config.Loot.ShowQuestItems)
                return;

            var label = GetUILabel();
            var paints = GetPaints();
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

            point.Offset(7 * App.Config.UI.UIScale, 3 * App.Config.UI.UIScale);

            canvas.DrawText(
                label,
                point,
                SKTextAlign.Left,
                widgetFont,
                SKPaints.TextOutline); // Draw outline
            canvas.DrawText(
                label,
                point,
                SKTextAlign.Left,
                widgetFont,
                paints.Item2);

        }

        public virtual void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            // Determine accent color based on item type
            SKColor accentColor;
            if (IsQuestItem)
                accentColor = TooltipColors.LootQuest;
            else if (IsHideoutItem)
                accentColor = TooltipColors.LootHideout;
            else if (IsValuableLoot)
                accentColor = TooltipColors.LootValuable;
            else if (!string.IsNullOrEmpty(CustomFilter?.Color) && SKColor.TryParse(CustomFilter.Color, out var filterColor))
                accentColor = filterColor;
            else
                accentColor = TooltipColors.Default;

            var tooltip = new TooltipData(ShortName, accentColor);
            tooltip.SetSubHeader(Name != ShortName ? Name : null);

            // Price info
            if (Price > 0)
            {
                var priceSource = App.Config.Loot.PriceMode == LootPriceMode.FleaMarket ? "Flea" : "Trader";
                tooltip.AddRow("Value", $"${Utilities.FormatNumberKM(Price)}", TooltipColors.LootValuable);
                tooltip.AddRow("Source", priceSource);
            }
            else
            {
                tooltip.AddRow("Value", "Unknown", TooltipColors.Default);
            }

            // Size (slots)
            if (GridCount > 1)
                tooltip.AddRow("Size", $"{GridCount} slots");

            // Tags
            if (IsQuestItem)
                tooltip.AddRow("Tag", "Quest Item", TooltipColors.LootQuest);
            else if (IsHideoutItem)
                tooltip.AddRow("Tag", "Hideout Item", TooltipColors.LootHideout);
            else if (Important)
                tooltip.AddRow("Tag", "Important", SKColors.MediumPurple);

            // Distance
            var distance = Vector3.Distance(localPlayer.Position, Position);
            tooltip.AddRow("Distance", $"{distance:F1} m");

            var pos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var canvasWidth = mapParams.Bounds.Width * mapParams.XScale;
            var canvasHeight = mapParams.Bounds.Height * mapParams.YScale;
            TooltipCard.Draw(canvas, pos, tooltip, canvasWidth, canvasHeight);
        }

        // Cached label string — computed once, price/name don't change mid-raid
        private string _cachedUILabel;

        /// <summary>
        /// Gets a UI Friendly Label.
        /// </summary>
        /// <returns>Item Label string cleaned up for UI usage.</returns>
        public string GetUILabel()
        {
            if (_cachedUILabel != null)
                return _cachedUILabel;

            var label = "";
            if (IsImportant)
                label += "";
            else if (Price > 0)
                label += $"[{Utilities.FormatNumberKM(Price)}] ";
            label += ShortName;

            if (string.IsNullOrEmpty(label))
                label = "Item";

            _cachedUILabel = label;
            return label;
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (IsQuestItem)
                return new(SKPaints.PaintQuestItem, SKPaints.TextQuestItem);
            if (IsHideoutItem)
                return new(SKPaints.PaintHideoutItem, SKPaints.TextHideoutItem);
            if (LootFilter.ShowBackpacks && IsBackpack)
                return new(SKPaints.PaintBackpacks, SKPaints.TextBackpacks);
            if (LootFilter.ShowMeds && IsMeds)
                return new(SKPaints.PaintMeds, SKPaints.TextMeds);
            if (LootFilter.ShowFood && IsFood)
                return new(SKPaints.PaintFood, SKPaints.TextFood);
            var filterColor = CustomFilter?.Color;

            if (!string.IsNullOrEmpty(filterColor))
            {
                var filterPaints = GetFilterPaints(filterColor);
                return new(filterPaints.Item1, filterPaints.Item2);
            }
            if (IsValuableLoot || this is LootAirdrop)
                return new(SKPaints.PaintImportantLoot, SKPaints.TextImportantLoot);
            return new(SKPaints.PaintLoot, SKPaints.TextLoot);
        }

        #region Custom Loot Paints
        private static readonly ConcurrentDictionary<string, Tuple<SKPaint, SKPaint>> _paints = new();

        /// <summary>
        /// Returns the Paints for this color value.
        /// </summary>
        /// <param name="color">Color rgba hex string.</param>
        /// <returns>Tuple of paints. Item1 = Paint, Item2 = Text.</returns>
        internal static Tuple<SKPaint, SKPaint> GetFilterPaints(string color)
        {
            if (!SKColor.TryParse(color, out var skColor))
                return new Tuple<SKPaint, SKPaint>(SKPaints.PaintLoot, SKPaints.TextLoot);

            var result = _paints.AddOrUpdate(color,
                key =>
                {
                    var paint = new SKPaint
                    {
                        Color = skColor,
                        StrokeWidth = 3f * App.Config.UI.UIScale,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    var text = new SKPaint
                    {
                        Color = skColor,
                        IsStroke = false,
                        IsAntialias = true
                    };
                    return new Tuple<SKPaint, SKPaint>(paint, text);
                },
                (key, existingValue) =>
                {
                    existingValue.Item1.StrokeWidth = 3f * App.Config.UI.UIScale;
                    return existingValue;
                });

            return result;
        }

        public static void ScaleLootPaints(float newScale)
        {
            foreach (var paint in _paints)
            {
                paint.Value.Item1.StrokeWidth = 3f * newScale;
            }
        }

        #endregion
    }
}
