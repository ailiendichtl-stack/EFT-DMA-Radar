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

        static CameraManager() { }
        public static ulong FPSCameraPtr { get; private set; }
        public static ulong OpticCameraPtr { get; private set; }


        private static readonly Lock _viewportSync = new();
        public static Rectangle Viewport { get; private set; }
        public static SKPoint ViewportCenter => new SKPoint(Viewport.Width / 2f, Viewport.Height / 2f);
        public static bool IsScoped { get; private set; }
        public static float ScopeZoom { get; private set; } = 1f;
        public static bool IsADS { get; private set; }
        public static bool IsInitialized { get; private set; } = false;
        private static float _fov;
        private static float _aspect;
        private static readonly ViewMatrix _viewMatrix = new();
        private static readonly ViewMatrix _opticViewMatrix = new();
        private static float _fpsRightMag;
        private static float _fpsUpMag;
        private static float _opticRightMag;
        private static float _opticUpMag;
        private static float _opticRightMag1x;
        private static float _opticUpMag1x;
        public static Vector3 CameraPosition => new(_viewMatrix.M14, _viewMatrix.M24, _viewMatrix.Translation.Z);

        public static void Reset()
        {
            var identity = Matrix4x4.Identity;
            _viewMatrix.Update(ref identity);
            _opticViewMatrix.Update(ref identity);
            Viewport = new Rectangle();
            OpticCameraPtr = 0;
            FPSCameraPtr = 0;
            _fov = 0f;
            _aspect = 0f;
            _fpsRightMag = 0f;
            _fpsUpMag = 0f;
            _opticRightMag = 0f;
            _opticUpMag = 0f;
            _opticRightMag1x = 0f;
            _opticUpMag1x = 0f;
            ScopeZoom = 1f;
            IsInitialized = false;
            _potentialOpticCameras.Clear();
            _useFpsCameraForCurrentAds = false;
            _maxOpticFov = 0f;
            _calibratedOpticPtr = 0;
        }
        public ulong FPSCamera { get; }

        private static readonly List<ulong> _potentialOpticCameras = new();
        private static bool _useFpsCameraForCurrentAds = false;
        private static float _maxOpticFov;
        private static ulong _calibratedOpticPtr; // Tracks which optic _maxOpticFov belongs to

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
        /// World-to-screen using FPS camera VP only (no optic VP). Use for aimbot —
        /// the FPS VP is stable (no scope sway) and matches mouse input rotation.
        /// </summary>
        public static bool WorldToScreenFPS(ref readonly Vector3 worldPos, out SKPoint scrPos)
        {
            return WorldToScreenWithScale(in worldPos, out scrPos, out _, false, false, forceFpsVP: true);
        }

        /// <summary>
        /// Converts a world position to screen coordinates with distance-based scale factor.
        /// </summary>
        public static bool WorldToScreenWithScale(in Vector3 worldPos, out SKPoint scrPos, out float scale, bool onScreenCheck = false, bool useTolerance = false, bool forceFpsVP = false)
        {
            const float REFERENCE_DISTANCE = 50f;
            const float MIN_SCALE = 0.3f;
            const float MAX_SCALE = 2.0f;

            try
            {
                // Use optic camera VP whenever ADS with a valid optic camera — even at 1x.
                // The optic VP projects from the scope's position so screen center = reticle.
                // Using FPS VP at 1x ADS causes a small offset (scope height above bore)
                // that makes aimbot miss at distance. Scale by FPS/Optic VP magnitude ratio
                // to map the optic's square projection to the widescreen viewport.
                bool useOptic = !forceFpsVP && IsADS && OpticCameraPtr != 0 && _opticRightMag1x > 0.1f && _opticUpMag1x > 0.1f;
                var vm = useOptic ? _opticViewMatrix : _viewMatrix;

                float w = Vector3.Dot(vm.Translation, worldPos) + vm.M44;

                if (w < 0.098f)
                {
                    scrPos = default;
                    scale = 1f;
                    return false;
                }

                scale = Math.Clamp(REFERENCE_DISTANCE / w, MIN_SCALE, MAX_SCALE);

                float x = Vector3.Dot(vm.Right, worldPos) + vm.M14;
                float y = Vector3.Dot(vm.Up, worldPos) + vm.M24;

                if (useOptic)
                {
                    // Map optic VP's square projection to screen coordinates.
                    // Divide by the 1x optic magnitude (scope circle size on screen),
                    // NOT the current magnitude. The current magnitude encodes the zoom
                    // (6x higher at 6x zoom), so preserving it gives correct zoom scaling.
                    // Multiply by FPS magnitude to get screen-pixel scale.
                    x *= _fpsRightMag / _opticRightMag1x;
                    y *= _fpsUpMag / _opticUpMag1x;
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
                FPSCameraPtr = FPSCamera;
                OpticCameraPtr = 0;

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
        private static bool ValidateOpticCameraMatrix(ulong cameraPtr)
        {
            try
            {
                var vm = Memory.ReadValue<Matrix4x4>(cameraPtr + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset, false);

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

        /// <summary>
        /// Current scope sensitivity multiplier (1.0 = no change, &lt;1.0 = slower camera movement when scoped).
        /// Updated each frame when ADS. Used by aimbot for compensation.
        /// </summary>
        public static float ScopeSensitivity { get; private set; } = 1.0f;

        /// <summary>
        /// Reads scope sensitivity for aimbot compensation. Does NOT determine zoom level.
        /// Zoom is computed from optic camera FOV in OnRealtimeLoop scatter callback.
        /// </summary>
        private void UpdateScopeSensitivity(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer is null) return;

                var opticsPtr = Memory.ReadPtr(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._optics);
                using var optics = UnityList<VmmPointer>.Create(opticsPtr, true);

                if (optics.Count <= 0) return;

                var pSightComponent = Memory.ReadPtr(optics[0] + Offsets.SightNBone.Mod);
                var sightComponent = Memory.ReadValue<SightComponent>(pSightComponent);

                float sensitivity = sightComponent.GetSensitivity();
                if (sensitivity > 0f && sensitivity < 10f)
                    ScopeSensitivity = sensitivity;
                else
                    ScopeSensitivity = 1.0f;
            }
            catch
            {
                ScopeSensitivity = 1.0f;
            }
        }

        /// <summary>
        /// Consecutive frames where the FPS VP matrix read failed or was invalid.
        /// Used to trigger re-acquisition of the FPS camera pointer.
        /// </summary>
        private static int _fpsInvalidFrames;
        private const int FPS_REACQUIRE_THRESHOLD = 10;

        public void OnRealtimeLoop(VmmScatter scatter, LocalPlayer localPlayer)
        {
            try
            {
                // Re-acquire FPS camera if the pointer has gone stale
                if (_fpsInvalidFrames >= FPS_REACQUIRE_THRESHOLD)
                {
                    TryReacquireFpsCamera();
                    _fpsInvalidFrames = 0;
                }

                IsADS = localPlayer?.CheckIfADS() ?? false;

                if (!IsADS)
                {
                    _useFpsCameraForCurrentAds = false;
                    // Don't reset _maxOpticFov here — preserve the 1x FOV reference
                    // across ADS cycles so scoping in at 6x works after first calibration.
                    // It only resets when the optic camera pointer changes (different scope).
                    ScopeZoom = 1f;
                    IsScoped = false;
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

                if (IsADS)
                    UpdateScopeSensitivity(localPlayer);

                // Always read FPS camera VP matrix + FOV + aspect
                ulong fpsVmAddr = FPSCameraPtr + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
                var fovAddr = FPSCameraPtr + UnitySDK.UnityOffsets.Camera_FOVOffset;
                var aspectAddr = FPSCameraPtr + UnitySDK.UnityOffsets.Camera_AspectRatioOffset;

                scatter.PrepareReadValue<Matrix4x4>(fpsVmAddr);
                scatter.PrepareReadValue<float>(fovAddr);
                scatter.PrepareReadValue<float>(aspectAddr);

                // When scoped, also read optic camera VP matrix + FOV
                ulong opticVmAddr = 0;
                ulong opticFovAddr = 0;
                if (IsADS && OpticCameraPtr != 0)
                {
                    opticVmAddr = OpticCameraPtr + UnitySDK.UnityOffsets.Camera_ViewMatrixOffset;
                    opticFovAddr = OpticCameraPtr + UnitySDK.UnityOffsets.Camera_FOVOffset;
                    scatter.PrepareReadValue<Matrix4x4>(opticVmAddr);
                    scatter.PrepareReadValue<float>(opticFovAddr);
                }

                var capturedOpticVmAddr = opticVmAddr;
                var capturedOpticFovAddr = opticFovAddr;

                scatter.Completed += (sender, s) =>
                {
                    // FPS camera VP (always used as reference, and for unscoped W2S)
                    bool fpsValid = false;
                    if (s.ReadValue<Matrix4x4>(fpsVmAddr, out var fpsVm) && !Unsafe.IsNullRef(ref fpsVm))
                    {
                        // Validate VP matrix isn't garbage (stale pointer reads zeroes or NaN)
                        float rightMag = MathF.Sqrt(fpsVm.M11 * fpsVm.M11 + fpsVm.M21 * fpsVm.M21 + fpsVm.M31 * fpsVm.M31);
                        float upMag = MathF.Sqrt(fpsVm.M12 * fpsVm.M12 + fpsVm.M22 * fpsVm.M22 + fpsVm.M32 * fpsVm.M32);

                        if (rightMag > 0.01f && upMag > 0.01f &&
                            !float.IsNaN(rightMag) && !float.IsInfinity(rightMag) &&
                            MathF.Abs(fpsVm.M44) > 0.001f)
                        {
                            _viewMatrix.Update(ref fpsVm);
                            _fpsRightMag = rightMag;
                            _fpsUpMag = upMag;
                            fpsValid = true;
                        }
                    }

                    if (fpsValid)
                        _fpsInvalidFrames = 0;
                    else
                        _fpsInvalidFrames++;

                    if (s.ReadValue<float>(fovAddr, out var fov) && fov > 1f && fov < 180f)
                        _fov = fov;

                    if (s.ReadValue<float>(aspectAddr, out var aspect) && aspect > 0.1f && aspect < 5f)
                        _aspect = aspect;

                    // Optic camera VP (used for scoped W2S — projects from scope position)
                    if (capturedOpticVmAddr != 0 &&
                        s.ReadValue<Matrix4x4>(capturedOpticVmAddr, out var opticVm))
                    {
                        if (!Unsafe.IsNullRef(ref opticVm))
                        {
                            _opticViewMatrix.Update(ref opticVm);
                            _opticRightMag = MathF.Sqrt(opticVm.M11 * opticVm.M11 + opticVm.M21 * opticVm.M21 + opticVm.M31 * opticVm.M31);
                            _opticUpMag = MathF.Sqrt(opticVm.M12 * opticVm.M12 + opticVm.M22 * opticVm.M22 + opticVm.M32 * opticVm.M32);
                        }
                    }

                    // Compute zoom from optic camera FOV changes
                    if (capturedOpticFovAddr != 0 &&
                        s.ReadValue<float>(capturedOpticFovAddr, out var opticFov) &&
                        opticFov > 0.5f && opticFov < 90f)
                    {
                        // Reset 1x reference when scope changes (different optic camera)
                        if (OpticCameraPtr != _calibratedOpticPtr)
                        {
                            _maxOpticFov = 0f;
                            _opticRightMag1x = 0f;
                            _opticUpMag1x = 0f;
                            _calibratedOpticPtr = OpticCameraPtr;
                        }

                        // Track widest FOV = 1x reference. Capture 1x VP magnitudes.
                        if (opticFov > _maxOpticFov)
                        {
                            _maxOpticFov = opticFov;
                            // Store the optic VP magnitudes at 1x (widest FOV = lowest zoom)
                            // These define the scope circle size on screen.
                            if (_opticRightMag > 0.1f)
                                _opticRightMag1x = _opticRightMag;
                            if (_opticUpMag > 0.1f)
                                _opticUpMag1x = _opticUpMag;
                        }

                        if (_maxOpticFov > 1f)
                        {
                            float baseHalfRad = _maxOpticFov * MathF.PI / 360f;
                            float currHalfRad = opticFov * MathF.PI / 360f;
                            float zoom = MathF.Tan(baseHalfRad) / MathF.Tan(currHalfRad);
                            ScopeZoom = zoom;
                            IsScoped = zoom > 1.05f;
                        }
                    }
                };
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ERROR in CameraManager OnRealtimeLoop: {ex}");
            }
        }

        /// <summary>
        /// Re-scans AllCameras to find a fresh FPS camera pointer when the current one goes stale.
        /// Also refreshes the optic camera cache.
        /// </summary>
        private void TryReacquireFpsCamera()
        {
            try
            {
                var allCamerasPtr = AllCameras.GetPtr(Memory.UnityBase);
                if (allCamerasPtr == 0) return;

                var listItemsPtr = Memory.ReadPtr(allCamerasPtr + 0x0, false);
                var count = Memory.ReadValue<int>(allCamerasPtr + 0x8, false);

                if (listItemsPtr == 0 || count <= 0) return;

                var newFps = FindFpsCamera(listItemsPtr, count);
                if (newFps != 0 && newFps != FPSCameraPtr)
                {
                    FPSCameraPtr = newFps;
                    DebugLogger.LogDebug($"[CameraManager] Re-acquired FPS camera @ 0x{newFps:X}");
                }

                // Also refresh optic camera cache
                _potentialOpticCameras.Clear();
                OpticCameraPtr = 0;
                _useFpsCameraForCurrentAds = false;
                _maxOpticFov = 0f;
                _calibratedOpticPtr = 0;
                _opticRightMag1x = 0f;
                _opticUpMag1x = 0f;
                CacheOpticCameras(listItemsPtr, count);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[CameraManager] Re-acquire failed: {ex.Message}");
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
                        OpticCameraPtr = cameraPtr;
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

            /// <summary>
            /// Gets the current aim sensitivity multiplier for this sight at its current scope and mode.
            /// Returns the per-optic sensitivity value from ISightComponentTemplate.AimSensitivity.
            /// </summary>
            public readonly float GetSensitivity()
            {
                try
                {
                    var si = SightInterface;
                    using var sensitivityArray = si.AimSensitivity;
                    if (sensitivityArray.Count == 0)
                        return 1.0f;

                    if (SelectedScope >= sensitivityArray.Count || SelectedScope is < 0 or > 10)
                        return 1.0f;

                    using var selectedScopeModes = UnityArray<int>.Create(pScopeSelectedModes, false);
                    int selectedScopeMode = SelectedScope >= selectedScopeModes.Count ? 0 : selectedScopeModes[SelectedScope];
                    ulong sensitivityAddr = sensitivityArray[SelectedScope] + UnityArray<float>.ArrBaseOffset + (uint)selectedScopeMode * 0x4;

                    float sensitivity = Memory.ReadValue<float>(sensitivityAddr, false);
                    if (sensitivity.IsNormalOrZero() && sensitivity is > 0f and < 10f)
                        return sensitivity;

                    return 1.0f;
                }
                catch
                {
                    return 1.0f;
                }
            }

            public readonly SightInterface SightInterface => Memory.ReadValue<SightInterface>(pSightInterface);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly struct SightInterface
        {
            [FieldOffset((int)Offsets.SightInterface.AimSensitivity)]
            private readonly ulong pAimSensitivity;

            public readonly UnityArray<ulong> AimSensitivity => UnityArray<ulong>.Create(pAimSensitivity, true);
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
