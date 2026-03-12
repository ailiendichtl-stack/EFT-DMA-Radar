/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.UI.Misc;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Manages all memory write features.
    /// </summary>
    public sealed class MemWritesManager
    {
        private readonly List<Action<LocalPlayer>> _features = new();

        public MemWritesManager()
        {
            // Register all features up-front; the feature itself checks its Enabled flag.
            _features.Add(lp => NoRecoil.Instance.ApplyIfReady(lp));
            _features.Add(lp => InfiniteStamina.Instance.ApplyIfReady(lp));
            _features.Add(lp => MemoryAim.Instance.ApplyIfReady(lp));
            _features.Add(lp => AntiAfk.Instance.ApplyIfReady(lp));
            _features.Add(lp => FastWeaponOps.Instance.ApplyIfReady(lp));
            _features.Add(lp => MagDrills.Instance.ApplyIfReady(lp));
            _features.Add(lp => DisableHeadBobbing.Instance.ApplyIfReady(lp));
            _features.Add(lp => DisableInventoryBlur.Instance.ApplyIfReady(lp));
            _features.Add(lp => TimeOfDay.Instance.ApplyIfReady(lp));

            // Movement features
            _features.Add(lp => MoveSpeed.Instance.ApplyIfReady(lp));
            _features.Add(lp => FastDuck.Instance.ApplyIfReady(lp));
            _features.Add(lp => NoInertia.Instance.ApplyIfReady(lp));
            _features.Add(lp => LongJump.Instance.ApplyIfReady(lp));
            _features.Add(lp => MuleMode.Instance.ApplyIfReady(lp));

            // Visual features
            _features.Add(lp => NightVision.Instance.ApplyIfReady(lp));
            _features.Add(lp => ThermalVision.Instance.ApplyIfReady(lp));
            _features.Add(lp => NoVisor.Instance.ApplyIfReady(lp));
            _features.Add(lp => DisableFrostbite.Instance.ApplyIfReady(lp));
            _features.Add(lp => FullBright.Instance.ApplyIfReady(lp));
            _features.Add(lp => ClearWeather.Instance.ApplyIfReady(lp));
            _features.Add(lp => DisableGrass.Instance.ApplyIfReady(lp));

            // Weapon features
            _features.Add(lp => NoWeaponMalfunctions.Instance.ApplyIfReady(lp));
            _features.Add(lp => DisableWeaponCollision.Instance.ApplyIfReady(lp));

            // Utility features
            _features.Add(lp => ExtendedReach.Instance.ApplyIfReady(lp));
            _features.Add(lp => InstantPlant.Instance.ApplyIfReady(lp));
            _features.Add(lp => LootThroughWalls.Instance.ApplyIfReady(lp));
            _features.Add(lp => MedPanel.Instance.ApplyIfReady(lp));
            _features.Add(lp => OwlMode.Instance.ApplyIfReady(lp));
            _features.Add(lp => ThirdPerson.Instance.ApplyIfReady(lp));
            _features.Add(lp => WideLean.Instance.ApplyIfReady(lp));
        }

        /// <summary>
        /// Apply all enabled memory write features.
        /// </summary>
        public void Apply(LocalPlayer localPlayer)
        {
            if (!App.Config.MemWrites.Enabled)
            {

                return;
            }

            if (localPlayer == null)
            {
                DebugLogger.LogDebug("[MemWritesManager] LocalPlayer is null");
                return;
            }

            try
            {
                if(!Memory.InRaid)
                    return;

                //DebugLogger.LogDebug($"[MemWritesManager] Applying {_features.Count} features");

                foreach (var feature in _features)
                {
                    try
                    {
                        feature(localPlayer);
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogDebug($"[MemWritesManager] Feature error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[MemWritesManager] Apply error: {ex}");
            }
        }

        /// <summary>
        /// Called when raid starts.
        /// </summary>
        public void OnRaidStart()
        {
            NoRecoil.Instance.OnRaidStart();
            InfiniteStamina.Instance.OnRaidStart();
            MemoryAim.Instance.OnRaidStart();
            AntiAfk.Instance.OnRaidStart();
            FastWeaponOps.Instance.OnRaidStart();
            MagDrills.Instance.OnRaidStart();
            DisableHeadBobbing.Instance.OnRaidStart();
            DisableInventoryBlur.Instance.OnRaidStart();
            TimeOfDay.Instance.OnRaidStart();

            // Movement
            MoveSpeed.Instance.OnRaidStart();
            FastDuck.Instance.OnRaidStart();
            NoInertia.Instance.OnRaidStart();
            LongJump.Instance.OnRaidStart();
            MuleMode.Instance.OnRaidStart();

            // Visual
            NightVision.Instance.OnRaidStart();
            ThermalVision.Instance.OnRaidStart();
            NoVisor.Instance.OnRaidStart();
            DisableFrostbite.Instance.OnRaidStart();
            FullBright.Instance.OnRaidStart();
            ClearWeather.Instance.OnRaidStart();
            DisableGrass.Instance.OnRaidStart();

            // Weapon
            NoWeaponMalfunctions.Instance.OnRaidStart();
            DisableWeaponCollision.Instance.OnRaidStart();

            // Utility
            ExtendedReach.Instance.OnRaidStart();
            InstantPlant.Instance.OnRaidStart();
            LootThroughWalls.Instance.OnRaidStart();
            MedPanel.Instance.OnRaidStart();
            OwlMode.Instance.OnRaidStart();
            ThirdPerson.Instance.OnRaidStart();
            WideLean.Instance.OnRaidStart();

            // Shared resolvers
            Helpers.HardSettingsResolver.Reset();
        }
    }
}
