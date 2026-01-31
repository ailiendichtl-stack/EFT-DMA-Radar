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
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    public class Exfil : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity
    {
        /// <summary>
        /// Memory address of this exfil point (0 if loaded from static data).
        /// </summary>
        private ulong _exfilPointAddr;

        /// <summary>
        /// Extract faction type (PMC, Scav, or Shared).
        /// </summary>
        public enum EFaction { PMC, Scav, Shared }

        public Exfil(TarkovDataManager.ExtractElement extract)
        {
            Name = extract.Name;
            _position = extract.Position.AsVector3();
            Faction = extract.IsPmc ? EFaction.PMC :
                      extract.IsShared ? EFaction.Shared : EFaction.Scav;
        }

        /// <summary>
        /// Constructor for exfil loaded directly from memory.
        /// </summary>
        public Exfil(string name, Vector3 position, ulong exfilPointAddr, EFaction faction = EFaction.Shared)
        {
            Name = name;
            _position = position;
            _exfilPointAddr = exfilPointAddr;
            Faction = faction;
        }

        public string Name { get; }

        /// <summary>
        /// The faction type of this extract (PMC, Scav, or Shared).
        /// </summary>
        public EFaction Faction { get; }

        /// <summary>
        /// Sets the memory address for this exfil to enable live status updates.
        /// </summary>
        public void SetMemoryAddress(ulong addr)
        {
            _exfilPointAddr = addr;
        }

        /// <summary>
        /// Updates the status from memory if address is known.
        /// </summary>
        public void UpdateStatus()
        {
            if (_exfilPointAddr == 0)
                return;

            try
            {
                var memStatus = Memory.ReadValue<int>(_exfilPointAddr + Offsets.ExfiltrationPoint.Status);
                Status = memStatus switch
                {
                    (int)Enums.EExfiltrationStatus.NotPresent => EStatus.Closed,
                    (int)Enums.EExfiltrationStatus.UncompleteRequirements => EStatus.Closed,
                    (int)Enums.EExfiltrationStatus.Countdown => EStatus.Pending,
                    (int)Enums.EExfiltrationStatus.RegularMode => EStatus.Open,
                    (int)Enums.EExfiltrationStatus.Pending => EStatus.Pending,
                    (int)Enums.EExfiltrationStatus.AwaitsManualActivation => EStatus.Pending,
                    (int)Enums.EExfiltrationStatus.Hidden => EStatus.Closed,
                    _ => EStatus.Open
                };
            }
            catch
            {
                // Keep existing status on read failure
            }
        }

        #region Interfaces

        private readonly Vector3 _position;
        public ref readonly Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }

        public EStatus Status { get; set; } = EStatus.Open; // Default open for now

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            // Skip drawing if closed
            if (Status == EStatus.Closed)
                return;

            var heightDiff = Position.Y - localPlayer.ReferenceHeight;
            
            var paint = Status switch
            {
                EStatus.Open => SKPaints.PaintExfilOpen,
                EStatus.Pending => SKPaints.PaintExfilPending,
                EStatus.Closed => SKPaints.PaintExfilClosed,
                _ => SKPaints.PaintExfilOpen
            };

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

            // Select accent color based on status
            var accentColor = Status switch
            {
                EStatus.Open => TooltipColors.ExtractOpen,
                EStatus.Pending => TooltipColors.ExtractPending,
                _ => TooltipColors.ExtractClosed
            };

            var tooltip = new TooltipData(Name ?? "Unknown Extract", accentColor);

            // Status row with colored value
            var statusText = Status switch
            {
                EStatus.Open => "Open",
                EStatus.Pending => "Pending",
                EStatus.Closed => "Closed",
                _ => "Unknown"
            };
            tooltip.AddRow("Status", statusText, accentColor);

            // Faction row
            var factionText = Faction switch
            {
                EFaction.PMC => "PMC",
                EFaction.Scav => "Scav",
                EFaction.Shared => "Shared",
                _ => "Unknown"
            };
            tooltip.AddRow("Type", factionText);

            // Distance row
            var distance = Vector3.Distance(Position, localPlayer.Position);
            tooltip.AddRow("Distance", $"{distance:F0} m");

            // Get canvas dimensions for edge detection
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
