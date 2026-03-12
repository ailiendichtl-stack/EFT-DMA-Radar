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

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using SDK;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    public class Exfil : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity
    {
        private ulong _exfilPointAddr;

        public enum EFaction { PMC, Scav, Shared }

        /// <summary>
        /// PMC entry points eligible for this exfil.
        /// </summary>
        private readonly HashSet<string> _pmcEntries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Scav profile IDs eligible for this exfil.
        /// </summary>
        private readonly HashSet<string> _scavIds = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether this is a secret exfil (always available).
        /// </summary>
        public bool IsSecret { get; set; }

        public Exfil(TarkovDataManager.ExtractElement extract)
        {
            Name = extract.Name;
            _position = extract.Position.AsVector3();
            Faction = extract.IsPmc ? EFaction.PMC :
                      extract.IsShared ? EFaction.Shared : EFaction.Scav;
        }

        public Exfil(string name, Vector3 position, ulong exfilPointAddr, EFaction faction = EFaction.Shared)
        {
            Name = name;
            _position = position;
            _exfilPointAddr = exfilPointAddr;
            Faction = faction;
            IsSecret = true;
        }

        public string Name { get; }
        public EFaction Faction { get; }

        public void SetMemoryAddress(ulong addr)
        {
            _exfilPointAddr = addr;
        }

        /// <summary>
        /// Whether this exfil is available for the given player (entry point / profile ID match).
        /// </summary>
        public bool IsAvailableForPlayer(Player.LocalPlayer player)
        {
            if (IsSecret)
                return true;

            if (player.IsPmc && _pmcEntries.Count > 0)
                return _pmcEntries.Contains(player.EntryPoint ?? "");

            if (!player.IsPmc && _scavIds.Count > 0)
                return _scavIds.Contains(player.ProfileId ?? "");

            // No eligibility data loaded — show it
            return true;
        }

        /// <summary>
        /// Load eligible PMC entry points from memory.
        /// </summary>
        public void LoadEligibleEntryPoints(ulong exfilPointAddr)
        {
            try
            {
                var arrPtr = Memory.ReadPtr(exfilPointAddr + Offsets.ExfiltrationPoint.EligibleEntryPoints);
                if (arrPtr == 0) return;

                using var arr = UnityArray<ulong>.Create(arrPtr, useCache: false);
                foreach (var strPtr in arr)
                {
                    if (strPtr == 0) continue;
                    var name = Memory.ReadUnicodeString(strPtr, 64);
                    if (!string.IsNullOrEmpty(name))
                        _pmcEntries.Add(name);
                }
            }
            catch { }
        }

        /// <summary>
        /// Load eligible Scav profile IDs from memory.
        /// </summary>
        public void LoadEligibleScavIds(ulong exfilPointAddr)
        {
            try
            {
                var listPtr = Memory.ReadPtr(exfilPointAddr + Offsets.ScavExfil.EligibleIds);
                if (listPtr == 0) return;

                // List<string>: _size at 0x18, _items at 0x10
                var size = Memory.ReadValue<int>(listPtr + 0x18);
                if (size <= 0 || size > 128) return;

                var arrPtr = Memory.ReadPtr(listPtr + 0x10);
                if (arrPtr == 0) return;

                for (int i = 0; i < size; i++)
                {
                    var strPtr = Memory.ReadPtr(arrPtr + 0x20 + (ulong)(i * 8));
                    if (strPtr == 0) continue;
                    var id = Memory.ReadUnicodeString(strPtr, 64);
                    if (!string.IsNullOrEmpty(id))
                        _scavIds.Add(id);
                }
            }
            catch { }
        }

        /// <summary>
        /// Queues a scatter read for exfil status. Call scatter.Execute() after all exfils are queued.
        /// </summary>
        public void OnRefresh(VmmScatter scatter)
        {
            if (_exfilPointAddr == 0)
                return;

            var addr = _exfilPointAddr + Offsets.ExfiltrationPoint.Status;
            scatter.PrepareReadValue<int>(addr);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadValue<int>(addr, out var memStatus))
                {
                    Status = memStatus switch
                    {
                        (int)Enums.EExfiltrationStatus.NotPresent => EStatus.Closed,
                        (int)Enums.EExfiltrationStatus.UncompleteRequirements => EStatus.Pending,
                        (int)Enums.EExfiltrationStatus.Countdown => EStatus.Pending,
                        (int)Enums.EExfiltrationStatus.RegularMode => EStatus.Open,
                        (int)Enums.EExfiltrationStatus.Pending => EStatus.Pending,
                        (int)Enums.EExfiltrationStatus.AwaitsManualActivation => EStatus.Pending,
                        (int)Enums.EExfiltrationStatus.Hidden => EStatus.Closed,
                        _ => EStatus.Open
                    };
                }
            };
        }

        #region Interfaces

        private readonly Vector3 _position;
        public ref readonly Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public EStatus Status { get; set; } = EStatus.Open;

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            // Hide closed or unavailable exfils
            if (Status == EStatus.Closed)
                return;
            if (!IsAvailableForPlayer(localPlayer))
                return;

            var heightDiff = Position.Y - localPlayer.ReferenceHeight;

            var paint = Status == EStatus.Pending
                ? SKPaints.PaintExfilPending
                : SKPaints.PaintExfilOpen;

            var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;
            if (heightDiff > 1.85f) // exfil is above player
            {
                using var path = point.GetUpArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else if (heightDiff < -1.85f) // exfil is below player
            {
                using var path = point.GetDownArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else // exfil is level with player
            {
                float size = 4.75f * App.Config.UI.UIScale;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paint);
            }
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var pos = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);

            var accentColor = Status == EStatus.Pending
                ? TooltipColors.ExtractPending
                : TooltipColors.ExtractOpen;

            var tooltip = new TooltipData(Name ?? "Unknown Extract", accentColor);

            var statusText = Status == EStatus.Pending ? "Pending" : "Open";
            tooltip.AddRow("Status", statusText, accentColor);

            var distance = Vector3.Distance(Position, localPlayer.Position);
            tooltip.AddRow("Distance", $"{distance:F0} m");

            var canvasWidth = canvas.LocalClipBounds.Width;
            var canvasHeight = canvas.LocalClipBounds.Height;
            TooltipCard.Draw(canvas, pos, tooltip, canvasWidth, canvasHeight);
        }

        #endregion

        public enum EStatus
        {
            Open,
            Pending,
            Closed
        }
    }
}
