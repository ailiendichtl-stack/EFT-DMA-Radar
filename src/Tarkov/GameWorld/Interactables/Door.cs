using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Skia;
using SDK;
using VmmSharpEx.Scatter;
using static LoneEftDmaRadar.Tarkov.Unity.UnitySDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Interactables
{
    public enum InteractableType : byte
    {
        Door,
        Switch,
        CardReader
    }

    public enum EDoorState : byte
    {
        None = 0,
        Locked = 1,
        Shut = 2,
        Open = 4,
        Interacting = 8,
        Breaching = 16
    }

    public sealed class Door : IWorldEntity, IMapEntity, IMouseoverEntity
    {
        private readonly ulong _base;
        private readonly Vector3 _position;

        public InteractableType Type { get; }
        public string Id { get; }
        public string KeyId { get; }
        public string KeyName { get; }
        public ref readonly Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }
        public EDoorState DoorState { get; private set; }
        public bool IsLocked => DoorState == EDoorState.Locked;

        public Door(ulong baseAddr, string className)
        {
            _base = baseAddr;

            Type = className switch
            {
                "Switch" => InteractableType.Switch,
                "CardReader" => InteractableType.CardReader,
                _ => InteractableType.Door
            };

            // Read door state
            DoorState = (EDoorState)Memory.ReadValue<byte>(baseAddr + Offsets.Interactable.DoorState);

            // Read ID
            try
            {
                var idPtr = Memory.ReadPtr(baseAddr + Offsets.Interactable.Id);
                Id = idPtr != 0 ? Memory.ReadUnicodeString(idPtr) : null;
            }
            catch { Id = null; }

            // Read KeyId
            try
            {
                var keyPtr = Memory.ReadPtr(baseAddr + Offsets.Interactable.KeyId);
                KeyId = keyPtr != 0 ? Memory.ReadUnicodeString(keyPtr) : null;
            }
            catch { KeyId = null; }

            // Resolve key name from TarkovDev data (O(1) dictionary lookup)
            if (!string.IsNullOrEmpty(KeyId)
                && TarkovDataManager.AllItems.TryGetValue(KeyId, out var keyItem))
            {
                KeyName = keyItem.ShortName;
            }

            // Read position via transform chain
            try
            {
                var ti = Memory.ReadPtrChain(baseAddr, false, UnityOffsets.TransformChain);
                if (ti != 0)
                {
                    var transform = new UnityTransform(ti);
                    _position = transform.UpdatePosition();
                }
            }
            catch
            {
                _position = Vector3.Zero;
            }
        }

        private ulong DoorStateAddr => _base + Offsets.Interactable.DoorState;

        public void OnRefresh(VmmScatter scatter)
        {
            scatter.PrepareReadValue<byte>(DoorStateAddr);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadValue<byte>(DoorStateAddr, out var state))
                    DoorState = (EDoorState)state;
            };
        }

        #region IMapEntity / IMouseoverEntity

        /// <summary>
        /// Returns true if this interactable should be visible based on current config.
        /// </summary>
        public bool IsVisible => Type switch
        {
            InteractableType.Switch => App.Config.Misc.ShowSwitches,
            InteractableType.CardReader => App.Config.Misc.ShowCardReaders,
            _ => App.Config.Misc.ShowDoors &&
                 (!App.Config.Misc.KeyDoorsOnly || !string.IsNullOrEmpty(KeyId)) &&
                 (IsLocked ? App.Config.Misc.ShowLockedDoors : App.Config.Misc.ShowUnlockedDoors)
        };

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            if (!IsVisible || _position == Vector3.Zero)
                return;

            var mapPos = _position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(mapPos.X, mapPos.Y);
            var size = 3f * App.Config.UI.UIScale;

            switch (Type)
            {
                case InteractableType.Switch:
                    DrawDiamond(canvas, mapPos, size, _switchPaint);
                    break;
                case InteractableType.CardReader:
                    DrawSquare(canvas, mapPos, size, IsLocked ? _cardReaderLockedPaint : _cardReaderPaint);
                    break;
                default:
                    DrawDoorMarker(canvas, mapPos, size, GetDoorPaint());
                    break;
            }
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            string header = Type switch
            {
                InteractableType.Switch => "Switch",
                InteractableType.CardReader => "Card Reader",
                _ => "Door"
            };

            var tooltip = new TooltipData(header, GetAccentColor());

            string stateText = DoorState switch
            {
                EDoorState.Locked => "Locked",
                EDoorState.Shut => "Closed",
                EDoorState.Open => "Open",
                EDoorState.Interacting => "Interacting",
                EDoorState.Breaching => "Breaching",
                _ => "Unknown"
            };
            tooltip.AddRow("State", stateText);

            if (!string.IsNullOrEmpty(KeyName))
                tooltip.AddRow("Key", KeyName, SKColors.Gold);
            else if (!string.IsNullOrEmpty(KeyId) && IsLocked)
                tooltip.AddRow("KeyId", KeyId);

            var distance = Vector3.Distance(localPlayer.Position, _position);
            tooltip.AddRow("Distance", $"{distance:F1} m");

            var pos = _position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            var canvasWidth = mapParams.Bounds.Width * mapParams.XScale;
            var canvasHeight = mapParams.Bounds.Height * mapParams.YScale;
            TooltipCard.Draw(canvas, pos, tooltip, canvasWidth, canvasHeight);
        }

        #region Marker Drawing

        /// <summary>
        /// Locked = X (red), Shut = square (yellow), Open = diamond (green).
        /// </summary>
        private void DrawDoorMarker(SKCanvas canvas, SKPoint pos, float size, SKPaint paint)
        {
            switch (DoorState)
            {
                case EDoorState.Locked:
                    // X
                    canvas.DrawLine(
                        new SKPoint(pos.X - size, pos.Y - size),
                        new SKPoint(pos.X + size, pos.Y + size), paint);
                    canvas.DrawLine(
                        new SKPoint(pos.X + size, pos.Y - size),
                        new SKPoint(pos.X - size, pos.Y + size), paint);
                    break;
                case EDoorState.Open:
                    // Diamond
                    var top = new SKPoint(pos.X, pos.Y - size);
                    var right = new SKPoint(pos.X + size, pos.Y);
                    var bottom = new SKPoint(pos.X, pos.Y + size);
                    var left = new SKPoint(pos.X - size, pos.Y);
                    canvas.DrawLine(top, right, paint);
                    canvas.DrawLine(right, bottom, paint);
                    canvas.DrawLine(bottom, left, paint);
                    canvas.DrawLine(left, top, paint);
                    break;
                default:
                    // Square (shut/closed)
                    canvas.DrawRect(pos.X - size, pos.Y - size, size * 2, size * 2, paint);
                    break;
            }
        }

        private static void DrawDiamond(SKCanvas canvas, SKPoint pos, float size, SKPaint paint)
        {
            var top = new SKPoint(pos.X, pos.Y - size);
            var right = new SKPoint(pos.X + size, pos.Y);
            var bottom = new SKPoint(pos.X, pos.Y + size);
            var left = new SKPoint(pos.X - size, pos.Y);
            canvas.DrawLine(top, right, paint);
            canvas.DrawLine(right, bottom, paint);
            canvas.DrawLine(bottom, left, paint);
            canvas.DrawLine(left, top, paint);
        }

        private static void DrawSquare(SKCanvas canvas, SKPoint pos, float size, SKPaint paint)
        {
            canvas.DrawRect(pos.X - size, pos.Y - size, size * 2, size * 2, paint);
        }

        #endregion

        #region Paints

        private SKPaint GetDoorPaint() => DoorState switch
        {
            EDoorState.Locked => _lockedPaint,
            EDoorState.Open => _openPaint,
            _ => _shutPaint
        };

        private static readonly SKColor _cyanAccent = new(0, 220, 220);

        private SKColor GetAccentColor() => Type switch
        {
            InteractableType.Switch => _cyanAccent,
            InteractableType.CardReader => IsLocked ? SKColors.Magenta : _cyanAccent,
            _ => DoorState switch
            {
                EDoorState.Locked => SKColors.Red,
                EDoorState.Open => SKColors.LimeGreen,
                _ => SKColors.Yellow
            }
        };

        private static readonly SKPaint _lockedPaint = new()
        {
            Color = SKColors.Red,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _openPaint = new()
        {
            Color = SKColors.LimeGreen.WithAlpha(120),
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _shutPaint = new()
        {
            Color = SKColors.Yellow,
            StrokeWidth = 1f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _switchPaint = new()
        {
            Color = new SKColor(0, 220, 220), // Cyan
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _cardReaderPaint = new()
        {
            Color = new SKColor(0, 220, 220), // Cyan
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        private static readonly SKPaint _cardReaderLockedPaint = new()
        {
            Color = SKColors.Magenta,
            StrokeWidth = 1.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        #endregion

        #endregion
    }
}
