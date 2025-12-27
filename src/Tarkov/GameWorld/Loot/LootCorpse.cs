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
    public sealed class LootCorpse : LootItem
    {
        private static readonly TarkovMarketItem _default = new();
        private readonly ulong _corpse;
        private bool _contentsLoaded;
        private DateTime _lastContentsRefresh = DateTime.MinValue;
        private string _cachedFilterColor;

        /// <summary>
        /// Gets the corpse contents refresh interval from config.
        /// </summary>
        private static TimeSpan ContentsRefreshInterval => TimeSpan.FromSeconds(App.Config.Debug.CorpseScanIntervalSeconds);

        /// <summary>
        /// Corpse container's associated player object (if any).
        /// </summary>
        public AbstractPlayer Player { get; set; }

        /// <summary>
        /// Items inside the corpse's inventory (backpack, rig, pockets).
        /// </summary>
        public List<ContainerItem> Contents { get; private set; } = new();

        /// <summary>
        /// Total value of items inside the corpse's inventory containers.
        /// </summary>
        public int InventoryValue => Contents?.Sum(x => x.Price) ?? 0;

        /// <summary>
        /// True if any item in the corpse's inventory is marked as Important.
        /// </summary>
        public bool HasImportantContents => Contents?.Any(x => x.IsImportant) ?? false;

        /// <summary>
        /// Gets the filter color from the highest value important item (cached on contents load).
        /// </summary>
        private string ImportantItemFilterColor => _cachedFilterColor;

        /// <summary>
        /// True if any item in the corpse's inventory is needed for tracked hideout upgrades.
        /// </summary>
        public bool HasHideoutContents => Contents?.Any(x => x.IsHideoutItem) ?? false;

        /// <summary>
        /// True if the corpse has valuable inventory contents (above min value threshold).
        /// </summary>
        public bool HasValuableContents => InventoryValue >= App.Config.Containers.MinValue;

        public override string Name => Player?.Name ?? "Corpse";

        /// <summary>
        /// Constructor.
        /// </summary>
        public LootCorpse(ulong corpse, Vector3 position) : base(_default, position)
        {
            _corpse = corpse;
        }

        /// <summary>
        /// Sync the corpse's player reference from a list of dead players.
        /// </summary>
        /// <param name="deadPlayers"></param>
        public void Sync(IReadOnlyList<AbstractPlayer> deadPlayers)
        {
            Player ??= deadPlayers?.FirstOrDefault(x => x.Corpse == _corpse);
            if (Player is not null && Player.LootObject is null)
                Player.LootObject = this;

            // Refresh inventory contents after syncing player
            RefreshContents();
        }

        /// <summary>
        /// Refreshes the corpse's inventory contents.
        /// Only works for ObservedPlayer corpses in offline PVE mode.
        /// </summary>
        public void RefreshContents()
        {
            // Only scan contents if PVE scanning is enabled (offline mode only)
            if (!App.Config.Containers.PveScanEnabled)
                return;

            if (Player is not ObservedPlayer obs)
                return;

            // Throttle refreshes
            if (DateTime.UtcNow - _lastContentsRefresh < ContentsRefreshInterval && _contentsLoaded)
                return;

            _lastContentsRefresh = DateTime.UtcNow;

            try
            {
                Contents = CorpseContentsReader.GetCorpseContents(obs);
                _contentsLoaded = true;
                // Cache filter color from highest value important item
                _cachedFilterColor = Contents?
                    .Where(x => x.IsImportant && !string.IsNullOrEmpty(x.FilterColor))
                    .OrderByDescending(x => x.Price)
                    .FirstOrDefault()?.FilterColor;
            }
            catch
            {
                // Ignore errors, keep existing contents
            }
        }

        public override void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var heightDiff = Position.Y - localPlayer.ReferenceHeight;
            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;
            var widgetFont = CustomFontManager.GetCurrentRadarWidgetFont();

            // Get paint based on contents value - Priority: Important > Valuable > Default
            var textPaint = GetCorpsePaint();

            if (heightDiff > 1.45) // loot is above player
            {
                var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                canvas.DrawText("▲", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                canvas.DrawText("▲", adjustedPoint, SKTextAlign.Center, widgetFont, textPaint);
            }
            else if (heightDiff < -1.45) // loot is below player
            {
                var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                canvas.DrawText("▼", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                canvas.DrawText("▼", adjustedPoint, SKTextAlign.Center, widgetFont, textPaint);
            }
            else // loot is level with player
            {
                var adjustedPoint = new SKPoint(point.X, point.Y + 3 * App.Config.UI.UIScale);
                canvas.DrawText("●", adjustedPoint, SKTextAlign.Center, widgetFont, SKPaints.TextOutline);
                canvas.DrawText("●", adjustedPoint, SKTextAlign.Center, widgetFont, textPaint);
            }

            point.Offset(7 * App.Config.UI.UIScale, 3 * App.Config.UI.UIScale);

            // Build label with value if contents loaded
            var label = GetCorpseLabel();

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
                textPaint);
        }

        /// <summary>
        /// Gets the appropriate paint for the corpse based on contents.
        /// Priority: Important (with filter color) > Hideout > Valuable > Default
        /// </summary>
        private SKPaint GetCorpsePaint()
        {
            if (HasImportantContents)
            {
                var filterColor = ImportantItemFilterColor;
                if (!string.IsNullOrEmpty(filterColor))
                {
                    var filterPaints = LootItem.GetFilterPaints(filterColor);
                    return filterPaints.Item2; // Text paint
                }
                return SKPaints.TextImportantLoot;
            }
            if (HasHideoutContents)
                return SKPaints.TextHideoutItem;
            if (HasValuableContents)
                return SKPaints.TextFilteredLoot;
            return SKPaints.TextCorpse;
        }

        /// <summary>
        /// Gets the label for the corpse including value if available.
        /// </summary>
        private string GetCorpseLabel()
        {
            var totalValue = InventoryValue;
            if (totalValue > 0)
                return $"[{Utilities.FormatNumberKM(totalValue)}] {Name}";
            return Name;
        }

        public override void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            using var lines = new PooledList<string>();
            if (Player is AbstractPlayer player)
            {
                var name = App.Config.UI.HideNames && player.IsHuman ? "<Hidden>" : player.Name;
                lines.Add($"{player.Type.ToString()}:{name}");
                string g = null;
                if (player.GroupID != -1) g = $"G:{player.GroupID} ";
                if (g is not null) lines.Add(g);

                if (Player is ObservedPlayer obs) // show equipment info
                {
                    lines.Add($"Gear Value: {Utilities.FormatNumberKM(obs.Equipment.Value)}");
                    foreach (var item in obs.Equipment.Items.OrderBy(e => e.Key))
                    {
                        lines.Add($"{item.Key.Substring(0, 5)}: {item.Value.ShortName}");
                    }

                    // Show inventory contents if available
                    if (Contents?.Count > 0)
                    {
                        lines.Add($"--- Inventory ({Utilities.FormatNumberKM(InventoryValue)}) ---");
                        foreach (var item in Contents.OrderByDescending(x => x.Price).Take(10))
                        {
                            var prefix = item.IsImportant ? "★ " : (item.IsHideoutItem ? "[H] " : "");
                            lines.Add($"{prefix}{item.Name} ({Utilities.FormatNumberKM(item.Price)})");
                        }
                        if (Contents.Count > 10)
                            lines.Add($"... and {Contents.Count - 10} more");
                    }
                }
            }
            else
            {
                lines.Add(Name);
            }
            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines.Span);
        }
    }
}
