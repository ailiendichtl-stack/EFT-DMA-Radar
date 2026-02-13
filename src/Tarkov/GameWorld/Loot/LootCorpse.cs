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
using LoneEftDmaRadar.UI.Misc;
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
        private bool _labelLogged; // For one-time debug logging
        private bool _syncFailLogged; // For one-time sync fail logging
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
        /// True if any item in the corpse's inventory is needed for an active quest objective.
        /// </summary>
        public bool HasQuestContents => Contents?.Any(x => x.IsQuestItem) ?? false;

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
            var wasNull = Player is null;
            Player ??= deadPlayers?.FirstOrDefault(x => x.Corpse == _corpse);

            // Debug logging for corpse sync
            if (wasNull && Player is not null)
            {
                DebugLogger.LogDebug($"[CorpseSync] Synced corpse 0x{_corpse:X} to player '{Player.Name}' (Type: {Player.Type}, IsObserved: {Player is ObservedPlayer})");
                if (Player.Equipment is { } eq)
                {
                    DebugLogger.LogDebug($"[CorpseSync] Equipment items: {eq.Items.Count}, Value: {eq.Value}");
                }
            }
            else if (wasNull && Player is null && !_syncFailLogged)
            {
                _syncFailLogged = true;
                // Log all dead player corpse addresses for comparison (only once per corpse to avoid spam)
                if (deadPlayers?.Count > 0)
                {
                    var deadCorpseAddrs = string.Join(", ", deadPlayers.Select(p => $"0x{p.Corpse:X}"));
                    DebugLogger.LogDebug($"[CorpseSync] Failed to sync corpse 0x{_corpse:X} - deadPlayers corpse addrs: [{deadCorpseAddrs}]");
                }
                else
                {
                    DebugLogger.LogDebug($"[CorpseSync] Failed to sync corpse 0x{_corpse:X} - no dead players available");
                }
            }

            if (Player is not null && Player.LootObject is null)
                Player.LootObject = this;

            // Refresh inventory contents after syncing player
            RefreshContents();
        }

        /// <summary>
        /// Refreshes the corpse's inventory contents.
        /// Works for ObservedPlayer, ClientPlayer, and pre-existing/environmental corpses.
        /// </summary>
        public void RefreshContents()
        {
            // Only scan contents if PVE scanning is enabled (offline mode only)
            if (!App.Config.Containers.PveScanEnabled)
                return;

            // Once contents are loaded, don't re-read (corpse loot doesn't change mid-raid)
            if (_contentsLoaded)
                return;

            try
            {
                List<ContainerItem> result = null;

                // Support both ObservedPlayer (online mode) and ClientPlayer (offline PVE mode)
                if (Player is ObservedPlayer obs)
                {
                    result = CorpseContentsReader.GetCorpseContents(obs);
                }
                else if (Player is ClientPlayer client)
                {
                    result = CorpseContentsReader.GetCorpseContents(client);
                }
                else
                {
                    // Fallback: Try reading directly from corpse interactiveClass
                    // This works for pre-existing/environmental corpses that don't have a player sync
                    result = CorpseContentsReader.GetCorpseContentsFromInteractiveClass(_corpse);
                }

                if (result?.Count > 0)
                {
                    Contents = result;
                    _contentsLoaded = true;

                    // Cache filter color from highest value important item
                    _cachedFilterColor = Contents
                        .Where(x => x.IsImportant && !string.IsNullOrEmpty(x.FilterColor))
                        .OrderByDescending(x => x.Price)
                        .FirstOrDefault()?.FilterColor;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[CorpseContents] Error reading contents for '{Player?.Name ?? "UnknownCorpse"}': {ex.Message}");
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
        /// Priority: Important (with filter color) > Quest > Hideout > Valuable > Equipment > Default
        /// </summary>
        private SKPaint GetCorpsePaint()
        {
            // Check inventory contents first (when PVE scan enabled)
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
            if (HasQuestContents)
                return SKPaints.TextQuestItem;
            if (HasHideoutContents)
                return SKPaints.TextHideoutItem;
            if (HasValuableContents)
                return SKPaints.TextFilteredLoot;

            // Fallback: Show equipment-based coloring when no inventory contents
            if (Player?.Equipment is { } eqFallback && eqFallback.Value >= App.Config.Containers.MinValue)
                return SKPaints.TextFilteredLoot;

            return SKPaints.TextCorpse;
        }

        /// <summary>
        /// Gets the label for the corpse including value if available.
        /// Shows inventory value when PVE scan enabled, equipment value otherwise.
        /// </summary>
        private string GetCorpseLabel()
        {
            // Debug logging (once per corpse)
            if (!_labelLogged && Player is not null)
            {
                _labelLogged = true;
                var isObs = Player is ObservedPlayer;
                var isClient = Player is ClientPlayer;
                var eqValue = Player.Equipment?.Value ?? -1;
                var eqItems = Player.Equipment?.Items.Count ?? -1;
                DebugLogger.LogDebug($"[CorpseLabel] '{Name}' - Player={Player.Name}, IsObserved={isObs}, IsClient={isClient}, Equipment.Items={eqItems}, Equipment.Value={eqValue}, InventoryValue={InventoryValue}, ContentsCount={Contents?.Count ?? 0}, PveScan={App.Config.Containers.PveScanEnabled}");
            }

            // If PVE scanning is enabled and we have inventory contents, show that value
            if (App.Config.Containers.PveScanEnabled && InventoryValue > 0)
                return $"[{Utilities.FormatNumberKM(InventoryValue)}] {Name}";

            // Otherwise show equipment value
            if (Player?.Equipment is { } eqLabel && eqLabel.Value > 0)
                return $"[{Utilities.FormatNumberKM(eqLabel.Value)}] {Name}";

            return Name;
        }

        public override void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            using var lines = new PooledList<string>();

            // Header: Player info if available, otherwise just "Corpse"
            if (Player is AbstractPlayer player)
            {
                var name = App.Config.UI.HideNames && player.IsHuman ? "<Hidden>" : player.Name;
                lines.Add($"{player.Type.ToString()}:{name}");
                string g = null;
                if (player.GroupID != -1) g = $"G:{player.GroupID} ";
                if (g is not null) lines.Add(g);

                // Show equipment info
                if (player.Equipment is { } eqInfo && eqInfo.Items.Count > 0)
                {
                    lines.Add($"Gear Value: {Utilities.FormatNumberKM(eqInfo.Value)}");
                    foreach (var item in eqInfo.Items.OrderBy(e => e.Key))
                    {
                        lines.Add($"{item.Key.Substring(0, 5)}: {item.Value.ShortName}");
                    }
                }
            }
            else
            {
                lines.Add(Name);
            }

            // Show inventory contents if available (works for all corpse types, including pre-existing)
            if (Contents?.Count > 0)
            {
                lines.Add($"--- Inventory ({Utilities.FormatNumberKM(InventoryValue)}) ---");
                foreach (var item in Contents.OrderByDescending(x => x.Price).Take(10))
                {
                    var prefix = item.IsImportant ? "★ " : (item.IsQuestItem ? "[Q] " : (item.IsHideoutItem ? "[H] " : ""));
                    lines.Add($"{prefix}{item.Name} ({Utilities.FormatNumberKM(item.Price)})");
                }
                if (Contents.Count > 10)
                    lines.Add($"... and {Contents.Count - 10} more");
            }

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines.Span);
        }
    }
}
