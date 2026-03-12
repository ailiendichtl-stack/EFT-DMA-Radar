/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Collections.Generic;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class MemWritesViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public bool Enabled
        {
            get => App.Config.MemWrites.Enabled;
            set
            {
                if (value && !App.Config.MemWrites.Enabled)
                {
                    var result = MessageBox.Show(
                        "FINAL WARNING\n\n" +
                        "Memory writes DIRECTLY MODIFY GAME MEMORY and are HIGHLY DETECTABLE.\n\n" +
                        "Using memory writes significantly INCREASES your risk of detection and permanent account ban.\n\n" +
                        "USE ONLY ON ACCOUNTS YOU ARE WILLING TO LOSE!\n\n" +
                        "ARE YOU ABSOLUTELY SURE YOU WANT TO ENABLE MEMORY WRITES?",
                        "CRITICAL WARNING - Memory Writes",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Stop,
                        MessageBoxResult.No);

                    if (result != MessageBoxResult.Yes)
                    {
                        OnPropertyChanged();
                        return;
                    }
                }

                App.Config.MemWrites.Enabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusColor));
                DebugLogger.LogDebug($"[MemWrites] Master switch {(value ? "ENABLED" : "DISABLED")}");
            }
        }

        public string StatusText => Enabled ? "ENABLED - HIGH RISK" : "Disabled - Safe";
        public string StatusColor => Enabled ? "Red" : "Green";

        // ── Combat ──────────────────────────────────────────────

        public bool MemoryAimEnabled
        {
            get => App.Config.MemWrites.MemoryAimEnabled;
            set { App.Config.MemWrites.MemoryAimEnabled = value; OnPropertyChanged(); }
        }

        public List<Bones> AvailableBones { get; } = new()
        {
            Bones.HumanHead,
            Bones.HumanNeck,
            Bones.HumanSpine3,
            Bones.HumanSpine2,
            Bones.HumanPelvis,
            Bones.Closest
        };

        public Bones MemoryAimTargetBone
        {
            get => App.Config.MemWrites.MemoryAimTargetBone;
            set { App.Config.MemWrites.MemoryAimTargetBone = value; OnPropertyChanged(); }
        }

        public bool NoRecoilEnabled
        {
            get => App.Config.MemWrites.NoRecoilEnabled;
            set { App.Config.MemWrites.NoRecoilEnabled = value; OnPropertyChanged(); }
        }

        public float NoRecoilAmount
        {
            get => App.Config.MemWrites.NoRecoilAmount;
            set { App.Config.MemWrites.NoRecoilAmount = value; OnPropertyChanged(); }
        }

        public float NoSwayAmount
        {
            get => App.Config.MemWrites.NoSwayAmount;
            set { App.Config.MemWrites.NoSwayAmount = value; OnPropertyChanged(); }
        }

        public bool NoWeaponMalfunctionsEnabled
        {
            get => App.Config.MemWrites.NoWeaponMalfunctionsEnabled;
            set { App.Config.MemWrites.NoWeaponMalfunctionsEnabled = value; OnPropertyChanged(); }
        }

        public bool DisableWeaponCollisionEnabled
        {
            get => App.Config.MemWrites.DisableWeaponCollisionEnabled;
            set { App.Config.MemWrites.DisableWeaponCollisionEnabled = value; OnPropertyChanged(); }
        }

        public bool FastWeaponOpsEnabled
        {
            get => App.Config.MemWrites.FastWeaponOpsEnabled;
            set { App.Config.MemWrites.FastWeaponOpsEnabled = value; OnPropertyChanged(); }
        }

        public bool MagDrillsEnabled
        {
            get => App.Config.MemWrites.MagDrillsEnabled;
            set { App.Config.MemWrites.MagDrillsEnabled = value; OnPropertyChanged(); }
        }

        // ── Movement ────────────────────────────────────────────

        public bool InfiniteStaminaEnabled
        {
            get => App.Config.MemWrites.InfiniteStaminaEnabled;
            set { App.Config.MemWrites.InfiniteStaminaEnabled = value; OnPropertyChanged(); }
        }

        public bool MoveSpeedEnabled
        {
            get => App.Config.MemWrites.MoveSpeedEnabled;
            set { App.Config.MemWrites.MoveSpeedEnabled = value; OnPropertyChanged(); }
        }

        public float MoveSpeedMultiplier
        {
            get => App.Config.MemWrites.MoveSpeedMultiplier;
            set { App.Config.MemWrites.MoveSpeedMultiplier = value; OnPropertyChanged(); }
        }

        public bool FastDuckEnabled
        {
            get => App.Config.MemWrites.FastDuckEnabled;
            set { App.Config.MemWrites.FastDuckEnabled = value; OnPropertyChanged(); }
        }

        public bool NoInertiaEnabled
        {
            get => App.Config.MemWrites.NoInertiaEnabled;
            set { App.Config.MemWrites.NoInertiaEnabled = value; OnPropertyChanged(); }
        }

        public bool LongJumpEnabled
        {
            get => App.Config.MemWrites.LongJumpEnabled;
            set { App.Config.MemWrites.LongJumpEnabled = value; OnPropertyChanged(); }
        }

        public float LongJumpMultiplier
        {
            get => App.Config.MemWrites.LongJumpMultiplier;
            set { App.Config.MemWrites.LongJumpMultiplier = value; OnPropertyChanged(); }
        }

        public bool MuleModeEnabled
        {
            get => App.Config.MemWrites.MuleModeEnabled;
            set { App.Config.MemWrites.MuleModeEnabled = value; OnPropertyChanged(); }
        }

        // ── Visual ──────────────────────────────────────────────

        public bool NightVisionEnabled
        {
            get => App.Config.MemWrites.NightVisionEnabled;
            set { App.Config.MemWrites.NightVisionEnabled = value; OnPropertyChanged(); }
        }

        public bool ThermalVisionEnabled
        {
            get => App.Config.MemWrites.ThermalVisionEnabled;
            set { App.Config.MemWrites.ThermalVisionEnabled = value; OnPropertyChanged(); }
        }

        public bool NoVisorEnabled
        {
            get => App.Config.MemWrites.NoVisorEnabled;
            set { App.Config.MemWrites.NoVisorEnabled = value; OnPropertyChanged(); }
        }

        public bool DisableFrostbiteEnabled
        {
            get => App.Config.MemWrites.DisableFrostbiteEnabled;
            set { App.Config.MemWrites.DisableFrostbiteEnabled = value; OnPropertyChanged(); }
        }

        public bool DisableHeadBobbingEnabled
        {
            get => App.Config.MemWrites.DisableHeadBobbingEnabled;
            set { App.Config.MemWrites.DisableHeadBobbingEnabled = value; OnPropertyChanged(); }
        }

        public bool DisableInventoryBlurEnabled
        {
            get => App.Config.MemWrites.DisableInventoryBlurEnabled;
            set { App.Config.MemWrites.DisableInventoryBlurEnabled = value; OnPropertyChanged(); }
        }

        public bool FullBrightEnabled
        {
            get => App.Config.MemWrites.FullBrightEnabled;
            set { App.Config.MemWrites.FullBrightEnabled = value; OnPropertyChanged(); }
        }

        public float FullBrightIntensity
        {
            get => App.Config.MemWrites.FullBrightIntensity;
            set { App.Config.MemWrites.FullBrightIntensity = value; OnPropertyChanged(); }
        }

        public bool ClearWeatherEnabled
        {
            get => App.Config.MemWrites.ClearWeatherEnabled;
            set { App.Config.MemWrites.ClearWeatherEnabled = value; OnPropertyChanged(); }
        }

        public bool DisableGrassEnabled
        {
            get => App.Config.MemWrites.DisableGrassEnabled;
            set { App.Config.MemWrites.DisableGrassEnabled = value; OnPropertyChanged(); }
        }

        public bool TimeOfDayEnabled
        {
            get => App.Config.MemWrites.TimeOfDayEnabled;
            set { App.Config.MemWrites.TimeOfDayEnabled = value; OnPropertyChanged(); }
        }

        public float TimeOfDayHour
        {
            get => App.Config.MemWrites.TimeOfDayHour;
            set { App.Config.MemWrites.TimeOfDayHour = value; OnPropertyChanged(); }
        }

        // ── Utility ─────────────────────────────────────────────

        public bool AntiAfkEnabled
        {
            get => App.Config.MemWrites.AntiAfkEnabled;
            set { App.Config.MemWrites.AntiAfkEnabled = value; OnPropertyChanged(); }
        }

        public bool ExtendedReachEnabled
        {
            get => App.Config.MemWrites.ExtendedReachEnabled;
            set { App.Config.MemWrites.ExtendedReachEnabled = value; OnPropertyChanged(); }
        }

        public float ExtendedReachDistance
        {
            get => App.Config.MemWrites.ExtendedReachDistance;
            set { App.Config.MemWrites.ExtendedReachDistance = value; OnPropertyChanged(); }
        }

        public bool InstantPlantEnabled
        {
            get => App.Config.MemWrites.InstantPlantEnabled;
            set { App.Config.MemWrites.InstantPlantEnabled = value; OnPropertyChanged(); }
        }

        public bool LootThroughWallsEnabled
        {
            get => App.Config.MemWrites.LootThroughWallsEnabled;
            set { App.Config.MemWrites.LootThroughWallsEnabled = value; OnPropertyChanged(); }
        }

        public bool MedPanelEnabled
        {
            get => App.Config.MemWrites.MedPanelEnabled;
            set { App.Config.MemWrites.MedPanelEnabled = value; OnPropertyChanged(); }
        }

        public bool OwlModeEnabled
        {
            get => App.Config.MemWrites.OwlModeEnabled;
            set { App.Config.MemWrites.OwlModeEnabled = value; OnPropertyChanged(); }
        }

        public bool ThirdPersonEnabled
        {
            get => App.Config.MemWrites.ThirdPersonEnabled;
            set { App.Config.MemWrites.ThirdPersonEnabled = value; OnPropertyChanged(); }
        }

        public bool WideLeanEnabled
        {
            get => App.Config.MemWrites.WideLeanEnabled;
            set { App.Config.MemWrites.WideLeanEnabled = value; OnPropertyChanged(); }
        }

        public float WideLeanAmount
        {
            get => App.Config.MemWrites.WideLeanAmount;
            set { App.Config.MemWrites.WideLeanAmount = value; OnPropertyChanged(); }
        }
    }
}
