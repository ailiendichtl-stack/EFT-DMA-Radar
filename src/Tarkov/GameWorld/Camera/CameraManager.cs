/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using SkiaSharp;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using VmmSharpEx;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Camera
{
    public sealed class CameraManager
    {
        private const int VIEWPORT_TOLERANCE = 800;

        static CameraManager()
        {
            MemDMA.ProcessStarting += MemDMA_ProcessStarting;
            MemDMA.ProcessStopped += MemDMA_ProcessStopped;
        }

        private static void MemDMA_ProcessStarting(object sender, EventArgs e) { }
        private static void MemDMA_ProcessStopped(object sender, EventArgs e) { }
        public static ulong FPSCameraPtr { get; private set; }
        public static ulong OpticCameraPtr { get; private set; }
        public static ulong ActiveCameraPtr { get; private set; }

        private static readonly Lock _viewportSync = new();
        public static Rectangle Viewport { get; private set; }
        public static SKPoint ViewportCenter => new SKPoint(Viewport.Width / 2f, Viewport.Height / 2f);
        public static bool IsScoped { get; private set; }
        public static bool IsADS { get; private set; }
        public static bool IsInitialized { get; private set; } = false;
        private static float _fov;
        private static float _aspect;
        private static readonly ViewMatrix _viewMatrix = new();
        public static Vector3 CameraPosition => new(_viewMatrix.M14, _viewMatrix.M24, _viewMatrix.Translation.Z);
    
        public static void Reset()
        {
            var identity = Matrix4x4.Identity;
            _viewMatrix.Update(ref identity);
            Viewport = new Rectangle();
            ActiveCameraPtr = 0;
            OpticCameraPtr = 0;
            FPSCameraPtr = 0;
            _fov = 0f;
            _aspect = 0f;
            IsInitialized = false;
            _potentialOpticCameras.Clear();
            _useFpsCameraForCurrentAds = false;
        }
        public ulong FPSCamera { get; }
        public ulong OpticCamera { get; }
        private ulong _fpsMatrixAddress;
        private ulong _opticMatrixAddress;
        private bool OpticCameraActive => OpticCameraPtr != 0;

        private static readonly List<ulong> _potentialOpticCameras = new();
        private static bool _useFpsCameraForCurrentAds = false;

        public static void UpdateViewportRes()
        {
            lock (_viewportSync)
            {
                int width, height;

                if (App.Config.UI.EspScreenWidth > 0 && App.Config.UI.EspScreenHeight > 0)
                {
                    // Use manual override
                    width = App.Config.UI.EspScreenWidth;
                    height = App.Config.UI.EspScreenHeight;
                }
                else
                {
                    // Use selected monitor resolution
                    var targetMonitor = MonitorInfo.GetMonitor(App.Config.UI.EspTargetScreen);
                    if (targetMonitor != null)
                    {
                        width = targetMonitor.Width;
                        height = targetMonitor.Height;
                    }
                    else
                    {
                        // Fallback to game resolution
                        width = (int)App.Config.UI.Resolution.Width;
                        height = (int)App.Config.UI.Resolution.Height;
                    }
                }

                if (width <= 0 || height <= 0)
                {
                    width = 1920;
                    height = 1080;
                }

                Viewport = new Rectangle(0, 0, width, height);
            }
        }

        public static bool WorldToScreen( ref readonly Vector3 worldPos, out SKPoint scrPos, bool onScreenCheck = false, bool useTolerance = false)
        {
            return WorldToScreenWithScale(in worldPos, out scrPos, out _, onScreenCheck, useTolerance);
        }

        /// <summary>
        /// Converts a world position to screen coordinates with distance-based scale factor.
        /// </summary>
        /// <param name="worldPos">World position to convert</param>
        /// <param name="scrPos">Resulting screen position</param>
        /// <param name="scale">Distance-based scale factor (1.0 at reference distance, larger when closer, smaller when farther)</param>
        /// <param name="onScreenCheck">Check if position is on screen</param>
        /// <param name="useTolerance">Use tolerance for on-screen check</param>
        /// <returns>True if conversion succeeded and position is valid</returns>
        public static bool WorldToScreenWithScale(in Vector3 worldPos, out SKPoint scrPos, out float scale, bool onScreenCheck = false, bool useTolerance = false)
        {
            const float REFERENCE_DISTANCE = 50f; // Reference distance for scale = 1.0
            const float MIN_SCALE = 0.3f;         // Minimum scale factor
            const float MAX_SCALE = 2.0f;         // Maximum scale factor

            try
            {
                float w = Vector3.Dot(_viewMatrix.Translation, worldPos) + _viewMatrix.M44;

                if (w < 0.098f)
                {
                    scrPos = default;
                    scale = 1f;
                    return false;
                }

                // Calculate scale based on distance (w is approximately the distance)
                scale = Math.Clamp(REFERENCE_DISTANCE / w, MIN_SCALE, MAX_SCALE);

                float x = Vector3.Dot(_viewMatrix.Right, worldPos) + _viewMatrix.M14;
                float y = Vector3.Dot(_viewMatrix.Up, worldPos) + _viewMatrix.M24;

                if (IsScoped)
                {
                    float angleRadHalf = (MathF.PI / 180f) * _fov * 0.5f;
                    float angleCtg = MathF.Cos(angleRadHalf) / MathF.Sin(angleRadHalf);

                    x /= angleCtg * _aspect * 0.5f;
                    y /= angleCtg * 0.5f;
                }

                var center = ViewportCenter;
                scrPos = new()
                {
                    X = center.X * (1f + x / w),
                    Y = center.Y * (1f - y / w)
                };

                if (onScreenCheck)
                {
                    int left = useTolerance ? Viewport.Left - VIEWPORT_TOLERANCE : Viewport.Left;
                    int right = useTolerance ? Viewport.Right + VIEWPORT_TOLERANCE : Viewport.Right;
                    int top = useTolerance ? Viewport.Top - VIEWPORT_TOLERANCE : Viewport.Top;
                    int bottom = useTolerance ? Viewport.Bottom + VIEWPORT_TOLERANCE : Viewport.Bottom;

                    if (scrPos.X < left || scrPos.X > right || scrPos.Y < top || scrPos.Y > bottom)
                    {
                        scrPos = default;
                        scale = 1f;
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ERROR in WorldToScreen: {ex}");
                scrPos = default;
                scale = 1f;
                return false;
            }
        }

        public CameraManager()
        {
            if (IsInitialized)
                return;

            _potentialOpticCameras.Clear();
            _useFpsCameraForCurrentAds = false;

            try
            {
                var allCamerasPtr = AllCameras.GetPtr(Memory.UnityBase);
                if (allCamerasPtr == 0)
                    throw new InvalidOperationException("AllCameras pointer is NULL - offset may be outdated");

                if (allCamerasPtr > 0x7FFFFFFFFFFF)
                    throw new InvalidOperationException($"Invalid AllCameras pointer: 0x{allCamerasPtr:X}");

                // AllCameras is a List<Camera*>
                // Structure: +0x0 = items array pointer, +0x8 = count (int)
                var listItemsPtr = Memory.ReadPtr(allCamerasPtr + 0x0, false);
                var count = Memory.ReadValue<int>(allCamerasPtr + 0x8, false);

                if (listItemsPtr == 0)
                    throw new InvalidOperationException("Camera list items pointer is NULL");

                if (count <= 0)
                    throw new InvalidOperationException($"No cameras in list (count: {count})");

                var fps = FindFpsCamera(listItemsPtr, count);

                if (fps == 0)
                    throw new InvalidOperationException("Could not find required FPS Camera!");

                FPSCamera = fps;
                _fpsMatrixAddress = GetMatrixAddress(FPSCamera, "FPS");

                FPSCameraPtr = FPSCamera;
                OpticCameraPtr = 0;
                ActiveCameraPtr = 0;
                _opticMatrixAddress = 0;

                CacheOpticCameras(listItemsPtr, count);

                IsInitialized = true;
                DebugLogger.LogDebug($"[CameraManager] Initialized: FPS=0x{FPSCamera:X}, OpticCameras={_potentialOpticCameras.Count}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($" CameraManager initialization failed: {ex}");
                throw;
            }
        }

        private static ulong GetMatrixAddress(ulong cameraPtr, string cameraType)
        {
            // Camera (Component) → GameObject
            var gameObject = Memory.ReadPtr(cameraPtr + UnitySDK.UnityOffsets.Component_GameObjectOffset, false);

            if (gameObject == 0 || gameObject > 0x7FFFFFFFFFFF)
                throw new InvalidOperationException($"Invalid {cameraType} GameObject: 0x{gameObject:X}");

            // GameObject + Components offset → Pointer1
            var ptr1 = Memory.ReadPtr(gameObject + UnitySDK.UnityOffsets.GameObject_ComponentsOffset, false);

            if (ptr1 == 0 || ptr1 > 0x7FFFFFFFFFFF)
                throw new InvalidOperationException($"Invalid {cameraType} Ptr1 (GameObject+UnitySDK.UnityOffsets.GameObject_ComponentsOffset): 0x{ptr1:X}");
                
            // Pointer1 + 0x18 → matrixAddress
            var matrixAddress = Memory.ReadPtr(ptr1 + 0x18, false);

            if (matrixAddress == 0 || matrixAddress > 0x7FFFFFFFFFFF)
                throw new InvalidOperationException($"Invalid {cameraType} matrixAddress (Ptr1+0x18): 0x{matrixAddress:X}");

            return matrixAddress;
        }

        private static void VerifyViewMatrix(ulong matrixAddress, string name)
        {
            // Verbose matrix logging removed - only log on errors if needed
        }

        private static ulong FindFpsCamera(ulong listItemsPtr, int count)
        {
            for (int i = 0; i < Math.Min(count, 100); i++)
            {
                try
                {
                    ulong cameraEntryAddr = listItemsPtr + (uint)(i * 0x8);
                    var cameraPtr = Memory.ReadPtr(cameraEntryAddr, false);

                    if (cameraPtr == 0 || cameraPtr > 0x7FFFFFFFFFFF)
                        continue;

                    // Camera+UnitySDK.UnityOffsets.GameObject_ComponentsOffset -> GameObject
                    var gameObjectPtr = Memory.ReadPtr(cameraPtr + UnitySDK.UnityOffsets.GameObject_ComponentsOffset, false);
                    if (gameObjectPtr == 0 || gameObjectPtr > 0x7FFFFFFFFFFF)
                        continue;

                    var namePtr = Memory.ReadPtr(gameObjectPtr + UnitySDK.UnityOffsets.GameObject_NameOffset, false);
                    if (namePtr == 0 || namePtr > 0x7FFFFFFFFFFF)
                        continue;

                    // Read the name string
                    var name = Memory.ReadUtf8String(namePtr, 64, false);
                    if (string.IsNullOrEmpty(name) || name.Length < 3)
                        continue;

                    // Check for FPS Camera
                    bool isFPS = name.Contains("FPS", StringComparison.OrdinalIgnoreCase) &&
                                name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                    if (isFPS)
                        return cameraPtr;
                }
                catch
                {
                    // Continue searching
                }
            }

            return 0;
        }

        /// <summary>
        /// Validates an Optic Camera by checking if its view matrix contains valid data
        /// </summary>
        /// <param name="cameraPtr">Pointer to the camera to validate</param>
        /// <returns>True if the camera has a valid view matrix, false otherwise</returns>
        private static bool ValidateOpticCameraMatrix(ulong cameraPtr)
        {
            try
            {
                var matrixAddress = GetMatrixAddress(cameraPtr, "Optic");
                var vm = Memory.ReadValue<Matrix4x4>(matrixAddress + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset, false);

                if (Math.Abs(vm.M44) < 0.001f)
                    return false;

                if (Math.Abs(vm.M41) < 0.001f && Math.Abs(vm.M42) < 0.001f && Math.Abs(vm.M43) < 0.001f)
                    return false;

                float rightMag = MathF.Sqrt(vm.M11 * vm.M11 + vm.M12 * vm.M12 + vm.M13 * vm.M13);
                float upMag = MathF.Sqrt(vm.M21 * vm.M21 + vm.M22 * vm.M22 + vm.M23 * vm.M23);
                float fwdMag = MathF.Sqrt(vm.M31 * vm.M31 + vm.M32 * vm.M32 + vm.M33 * vm.M33);

                if (rightMag < 0.1f && upMag < 0.1f && fwdMag < 0.1f)
                    return false;

                const float minMagnitude = 0.1f;
                const float maxMagnitude = 100.0f;

                bool hasValidVectors = (rightMag >= minMagnitude && rightMag <= maxMagnitude) ||
                                       (upMag >= minMagnitude && upMag <= maxMagnitude) ||
                                       (fwdMag >= minMagnitude && fwdMag <= maxMagnitude);

                return hasValidVectors;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckIfScoped(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer is null)
                {
                    return false;
                }

                if (!OpticCameraActive)
                {
                    return false;
                }

                var opticsPtr = Memory.ReadPtr(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._optics);

                using var optics = UnityList<VmmPointer>.Create(opticsPtr, true);

                if (optics.Count > 0)
                {
                    var pSightComponent = Memory.ReadPtr(optics[0] + Offsets.SightNBone.Mod);
                    var sightComponent = Memory.ReadValue<SightComponent>(pSightComponent);

                    if (sightComponent.ScopeZoomValue != 0f)
                    {
                        bool result = sightComponent.ScopeZoomValue > 1f;
                        return result;
                    }

                    float zoomLevel = sightComponent.GetZoomLevel();
                    bool zoomResult = zoomLevel > 1f;
                    return zoomResult;
                }

                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"CheckIfScoped() ERROR: {ex}");
                return false;
            }
        }

        public void OnRealtimeLoop(VmmScatterManaged scatter, LocalPlayer localPlayer)
        {
            try
            {
                IsADS = localPlayer?.CheckIfADS() ?? false;

                if (!IsADS)
                {
                    _useFpsCameraForCurrentAds = false;
                }

                if (IsADS && OpticCameraPtr == 0 && !_useFpsCameraForCurrentAds)
                {
                    if (_potentialOpticCameras.Count > 0)
                    {
                        if (!ValidateOpticCameras())
                            _useFpsCameraForCurrentAds = true;
                    }
                    else
                    {
                        _useFpsCameraForCurrentAds = true;
                    }
                }

                IsScoped = IsADS && CheckIfScoped(localPlayer);

                ulong vmAddr;
                if (IsADS && IsScoped && OpticCameraPtr != 0 && _opticMatrixAddress != 0)
                {
                    vmAddr = _opticMatrixAddress + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
                }
                else
                {
                    vmAddr = _fpsMatrixAddress + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
                    if (OpticCameraPtr == 0 || _opticMatrixAddress == 0)
                        IsScoped = false;
                }
                scatter.PrepareReadValue<Matrix4x4>(vmAddr);
                scatter.Completed += (sender, s) =>
                {
                    if (s.ReadValue<Matrix4x4>(vmAddr, out var vm))
                    {
                        if (!Unsafe.IsNullRef(ref vm))
                            _viewMatrix.Update(ref vm);
                    }
                };

                if (IsScoped)
                {
                    var fovAddr = FPSCamera + UnitySDK.UnityOffsets.Camera_FOVOffset;
                    var aspectAddr = FPSCamera + UnitySDK.UnityOffsets.Camera_AspectRatioOffset;

                    scatter.PrepareReadValue<float>(fovAddr); // FOV
                    scatter.PrepareReadValue<float>(aspectAddr); // Aspect

                    scatter.Completed += (sender, s) =>
                    {
                        if (s.ReadValue<float>(fovAddr, out var fov))
                            _fov = fov;

                        if (s.ReadValue<float>(aspectAddr, out var aspect))
                            _aspect = aspect;
                    };
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ERROR in CameraManager OnRealtimeLoop: {ex}");
            }
        }

        /// <summary>
        /// Validates cached potential optic cameras and sets up a valid one if found
        /// </summary>
        private bool ValidateOpticCameras()
        {
            try
            {
                foreach (var cameraPtr in _potentialOpticCameras)
                {
                    if (ValidateOpticCameraMatrix(cameraPtr))
                    {
                        // Found a valid optic camera, set it up
                        OpticCameraPtr = cameraPtr;
                        _opticMatrixAddress = GetMatrixAddress(cameraPtr, "Optic");
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cache all potential optic cameras during initialization
        /// </summary>
        private static void CacheOpticCameras(ulong listItemsPtr, int count)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        ulong cameraEntryAddr = listItemsPtr + (uint)(i * 0x8);
                        var cameraPtr = Memory.ReadPtr(cameraEntryAddr, false);

                        if (cameraPtr == 0 || cameraPtr > 0x7FFFFFFFFFFF) continue;

                        var gameObjectPtr = Memory.ReadPtr(cameraPtr + UnitySDK.UnityOffsets.GameObject_ComponentsOffset, false);
                        if (gameObjectPtr == 0 || gameObjectPtr > 0x7FFFFFFFFFFF) continue;

                        var namePtr = Memory.ReadPtr(gameObjectPtr + UnitySDK.UnityOffsets.GameObject_NameOffset, false);
                        if (namePtr == 0 || namePtr > 0x7FFFFFFFFFFF) continue;

                        var name = Memory.ReadUtf8String(namePtr, 64, false);
                        if (string.IsNullOrEmpty(name) || name.Length < 3) continue;

                        // Check for potential Optic Camera
                        bool isPotentialOptic = (name.Contains("Clone", StringComparison.OrdinalIgnoreCase) ||
                                                 name.Contains("Optic", StringComparison.OrdinalIgnoreCase)) &&
                                                 name.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                        if (isPotentialOptic)
                            _potentialOpticCameras.Add(cameraPtr);
                    }
                    catch
                    {
                        // continue searching
                    }
                }
            }
            catch
            {
                // Silently fail - optic cameras are optional
            }
        }

        #region SightComponent Structures
        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct SightComponent
        {
            [FieldOffset((int)Offsets.SightComponent._template)]
            private readonly ulong pSightInterface;

            [FieldOffset((int)Offsets.SightComponent.ScopesSelectedModes)]
            private readonly ulong pScopeSelectedModes;

            [FieldOffset((int)Offsets.SightComponent.SelectedScope)]
            private readonly int SelectedScope;

            [FieldOffset((int)Offsets.SightComponent.ScopeZoomValue)]
            public readonly float ScopeZoomValue;

            public readonly float GetZoomLevel()
            {
                try
                {
                    using var zoomArray = SightInterface.Zooms;
                    if (SelectedScope >= zoomArray.Count || SelectedScope is < 0 or > 10)
                        return -1.0f;

                    using var selectedScopeModes = UnityArray<int>.Create(pScopeSelectedModes, false);
                    int selectedScopeMode = SelectedScope >= selectedScopeModes.Count ? 0 : selectedScopeModes[SelectedScope];
                    ulong zoomAddr = zoomArray[SelectedScope] + UnityArray<float>.ArrBaseOffset + (uint)selectedScopeMode * 0x4;

                    float zoomLevel = Memory.ReadValue<float>(zoomAddr, false);
                    if (zoomLevel.IsNormalOrZero() && zoomLevel is >= 0f and < 100f)
                        return zoomLevel;

                    return -1.0f;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"ERROR in GetZoomLevel: {ex}");
                    return -1.0f;
                }
            }

            public readonly SightInterface SightInterface => Memory.ReadValue<SightInterface>(pSightInterface);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct SightInterface
        {
            [FieldOffset((int)Offsets.SightInterface.Zooms)]
            private readonly ulong pZooms;

            public readonly UnityArray<ulong> Zooms => UnityArray<ulong>.Create(pZooms, true);
        }

        #endregion



        public static CameraDebugSnapshot GetDebugSnapshot()
        {
            return new CameraDebugSnapshot
            {
                IsADS = IsADS,
                IsScoped = IsScoped,
                FPSCamera = FPSCameraPtr,
                OpticCamera = OpticCameraPtr,
                ActiveCamera = ActiveCameraPtr,
                Fov = _fov,
                Aspect = _aspect,
                M14 = _viewMatrix.M14,
                M24 = _viewMatrix.M24,
                M44 = _viewMatrix.M44,
                RightX = _viewMatrix.Right.X,
                RightY = _viewMatrix.Right.Y,
                RightZ = _viewMatrix.Right.Z,
                UpX = _viewMatrix.Up.X,
                UpY = _viewMatrix.Up.Y,
                UpZ = _viewMatrix.Up.Z,
                TransX = _viewMatrix.Translation.X,
                TransY = _viewMatrix.Translation.Y,
                TransZ = _viewMatrix.Translation.Z,
                ViewportW = Viewport.Width,
                ViewportH = Viewport.Height
            };
        }

        public readonly struct CameraDebugSnapshot
        {
            public bool IsADS { get; init; }
            public bool IsScoped { get; init; }
            public ulong FPSCamera { get; init; }
            public ulong OpticCamera { get; init; }
            public ulong ActiveCamera { get; init; }
            public float Fov { get; init; }
            public float Aspect { get; init; }
            public float M14 { get; init; }
            public float M24 { get; init; }
            public float M44 { get; init; }
            public float RightX { get; init; }
            public float RightY { get; init; }
            public float RightZ { get; init; }
            public float UpX { get; init; }
            public float UpY { get; init; }
            public float UpZ { get; init; }
            public float TransX { get; init; }
            public float TransY { get; init; }
            public float TransZ { get; init; }
            public int ViewportW { get; init; }
            public int ViewportH { get; init; }
        }

        /// <summary>
        /// Returns the FOV Magnitude (Length) between a point, and the center of the screen.
        /// </summary>
        /// <param name="point">Screen point to calculate FOV Magnitude of.</param>
        /// <returns>Screen distance from the middle of the screen (FOV Magnitude).</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFovMagnitude(SKPoint point)
        {
            return Vector2.Distance(ViewportCenter.AsVector2(), point.AsVector2());
        }
    }
}
