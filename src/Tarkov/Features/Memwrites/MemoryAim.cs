/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Misc.Ballistics;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using System;
using System.Diagnostics;
using System.Numerics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Silent aim by modifying shot direction in memory.
    /// Only writes when target is set AND aim key is held.
    /// </summary>
    public sealed class MemoryAim : MemWriteFeature<MemoryAim>
    {
        private bool _lastEnabledState;
        private Vector3? _targetPosition;
        private bool _isEngaged; // ? Track if aim key is held

        // Ballistics Cache
        private BallisticsInfo _ballistics = new();
        private ulong _loadedAmmoTemplate;
        private int _lastWeaponVersion;
        private ulong _lastWeaponItemBase;

        public override bool Enabled
        {
            get => App.Config.MemWrites.MemoryAimEnabled;
            set => App.Config.MemWrites.MemoryAimEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(1);

        /// <summary>
        /// Set whether aim key is currently held. Called by DeviceAimbot.
        /// </summary>
        public void SetEngaged(bool engaged)
        {
            _isEngaged = engaged;
            if (!engaged)
            {
                _targetPosition = null; // Clear target when key is released
            }
        }

        /// <summary>
        /// Set the target position to aim at. Called by DeviceAimbot.
        /// </summary>
        public void SetTargetPosition(Vector3? targetPos)
        {
            _targetPosition = targetPos;
        }

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer == null)
                    return;

                var stateChanged = Enabled != _lastEnabledState;

                if (!Enabled)
                {
                    if (stateChanged)
                    {
                        _lastEnabledState = false;
                        _targetPosition = null;
                        _isEngaged = false;
                        DebugLogger.LogDebug("[MemoryAim] Disabled");
                    }
                    return;
                }

                if (stateChanged)
                {
                    _lastEnabledState = true;
                    DebugLogger.LogDebug("[MemoryAim] Enabled");
                }
                
                // ? Only apply if aim key is held AND we have a target
                if (!_isEngaged || _targetPosition == null)
                    return;
                
                ApplyMemoryAim(localPlayer, _targetPosition.Value);
                
                // Clear target after writing (will be set again next frame by DeviceAimbot if still aiming)
                _targetPosition = null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[MemoryAim] Error: {ex}");
            }
        }

        public void ApplyMemoryAim(LocalPlayer localPlayer, Vector3 targetPosition)
        {
            try
            {
                var firearmManager = localPlayer.FirearmManager;
                if (firearmManager == null)
                    return;

                var fireportPos = firearmManager.FireportPosition;
                if (!fireportPos.HasValue || fireportPos.Value == Vector3.Zero)
                    return;

                var fpPos = fireportPos.Value;
                
                // Update ballistics for drop compensation
                UpdateBallistics(firearmManager);
                
                // Apply bullet drop compensation to target position
                Vector3 compensatedTarget = targetPosition;
                if (_ballistics.IsAmmoValid)
                {
                    var sim = BallisticsSimulation.Run(ref fpPos, ref targetPosition, _ballistics);
                    compensatedTarget.Y += sim.DropCompensation;
                }

                // 1) Calculate desired aim angle from fireport to (compensated) target
                Vector2 aimAngle = CalcAngle(fpPos, compensatedTarget);

                // 2) Read current view angles from MovementContext._rotation
                ulong movementContext = localPlayer.MovementContext;
                Vector2 viewAngles = Memory.ReadValue<Vector2>(
                    movementContext + Offsets.MovementContext._rotation,
                    false
                );

                // 3) Calculate delta between desired and current angles
                Vector2 delta = aimAngle - viewAngles;
                NormalizeAngle(ref delta);

                // 4) Convert to gun angle format (radians, clamped)
                var gunAngle = new Vector3(
                    DegToRad(delta.X),
                    0.0f,
                    DegToRad(delta.Y)
                );
                
                const float maxRad = 0.35f; // ~20 degrees max offset
                gunAngle.X = Math.Clamp(gunAngle.X, -maxRad, maxRad);
                gunAngle.Z = Math.Clamp(gunAngle.Z, -maxRad, maxRad);

                // 5) Write to _shotDirection
                var shotDirectionAddr = localPlayer.PWA + Offsets.ProceduralWeaponAnimation._shotDirection;
                var fovAdjustAddr = localPlayer.PWA + Offsets.ProceduralWeaponAnimation.ShotNeedsFovAdjustments;
                
                if (!MemDMA.IsValidVirtualAddress(shotDirectionAddr))
                    return;

                // The _shotDirection format: (yaw_radians, -1.0f, -pitch_radians)
                Vector3 writeVec = new Vector3(gunAngle.X, -1.0f, gunAngle.Z * -1.0f);
                
                Memory.WriteValue(fovAdjustAddr, false);
                Memory.WriteValue(shotDirectionAddr, writeVec);
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[MemoryAim] ApplyMemoryAim error: {ex}");
            }
        }
        
        private static Vector2 CalcAngle(Vector3 from, Vector3 to)
        {
            Vector3 delta = from - to;
            float length = delta.Length();
            
            return new Vector2(
                RadToDeg((float)-Math.Atan2(delta.X, -delta.Z)),
                RadToDeg((float)Math.Asin(delta.Y / length))
            );
        }
        
        private static void NormalizeAngle(ref Vector2 angle)
        {
            NormalizeAngle(ref angle.X);
            NormalizeAngle(ref angle.Y);
        }
        
        private static void NormalizeAngle(ref float angle)
        {
            while (angle > 180.0f) angle -= 360.0f;
            while (angle < -180.0f) angle += 360.0f;
        }
        
        private static float DegToRad(float degrees)
            => degrees * ((float)Math.PI / 180.0f);
        
        private static float RadToDeg(float radians)
            => radians * (180.0f / (float)Math.PI);

        private void UpdateBallistics(FirearmManager firearmManager)
        {
            try
            {
                var hands = firearmManager.CurrentHands;
                if (hands == null || !hands.IsWeapon || hands.ItemAddr == 0)
                    return;

                bool weaponChanged = hands.ItemAddr != _lastWeaponItemBase;
                if (weaponChanged)
                {
                    _lastWeaponVersion = -1;
                    _lastWeaponItemBase = hands.ItemAddr;
                }

                int weaponVersion = Memory.ReadValue<int>(hands.ItemAddr + Offsets.LootItem.Version);
                if (weaponVersion != _lastWeaponVersion)
                {
                    var ammoTemplate = FirearmManager.MagazineManager.GetAmmoTemplateFromWeapon(hands.ItemAddr);
                    if (ammoTemplate != 0 && ammoTemplate != _loadedAmmoTemplate)
                    {
                         _loadedAmmoTemplate = ammoTemplate;
                         
                        _ballistics.BulletMassGrams = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletMassGram);
                        _ballistics.BulletDiameterMillimeters = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BulletDiameterMilimeters);
                        _ballistics.BallisticCoefficient = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.BallisticCoeficient);
                        
                        float baseSpeed = Memory.ReadValue<float>(ammoTemplate + Offsets.AmmoTemplate.InitialSpeed);
                        float velMod = 0f;

                        var weaponTemplate = Memory.ReadPtr(hands.ItemAddr + Offsets.LootItem.Template);
                        velMod += Memory.ReadValue<float>(weaponTemplate + Offsets.WeaponTemplate.Velocity);
         
                        RecurseWeaponAttachVelocity(hands.ItemAddr, ref velMod);

                        float modifier = 1f + (velMod / 100f);
                        if (modifier < 0.01f) modifier = 0.01f;

                        _ballistics.BulletSpeed = baseSpeed * modifier;
                        
                        DebugLogger.LogDebug($"[MemoryAim] Ballistics Updated: Speed={_ballistics.BulletSpeed}, Mass={_ballistics.BulletMassGrams}, BC={_ballistics.BallisticCoefficient}");
                    }
                    _lastWeaponVersion = weaponVersion;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[MemoryAim] UpdateBallistics error: {ex}");
            }
        }

        private void RecurseWeaponAttachVelocity(ulong lootItemBase, ref float velocityModifier)
        {
             try
            {
                var parentSlots = Memory.ReadPtr(lootItemBase + Offsets.LootItemMod.Slots);
                using var slots = UnityArray<ulong>.Create(parentSlots, false);
                
                if (slots != null)
                {
                    foreach (var slot in slots.Span)
                    {
                        try
                        {
                            var containedItem = Memory.ReadPtr(slot + Offsets.Slot.ContainedItem);
                            if (containedItem == 0) continue;

                            var itemTemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                            velocityModifier += Memory.ReadValue<float>(itemTemplate + Offsets.ModTemplate.Velocity);
                            
                            RecurseWeaponAttachVelocity(containedItem, ref velocityModifier);
                        }
                        catch {}
                    }
                }
            }
            catch {}
        }

        public override void OnRaidStart()
        {
            DebugLogger.LogDebug("[MemoryAim] OnRaidStart called");
            _lastEnabledState = default;
            _targetPosition = null;
            _isEngaged = false;
            _lastWeaponVersion = -1;
            _lastWeaponItemBase = 0;
            _loadedAmmoTemplate = 0;
            _ballistics = new BallisticsInfo();
        }
    }
}