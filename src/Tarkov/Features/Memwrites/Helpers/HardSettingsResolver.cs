using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites.Helpers
{
    /// <summary>
    /// Resolves the EFTHardSettings singleton instance for memwrite features.
    /// Cached per-raid, shared across all features that need it.
    /// </summary>
    internal static class HardSettingsResolver
    {
        private static ulong _cached;

        public static ulong GetInstance()
        {
            if (MemDMA.IsValidVirtualAddress(_cached))
                return _cached;

            try
            {
                if (!IL2CPPLib.Initialized)
                    return 0;

                var klassPtr = IL2CPPLib.Class.FindClass("EFT.EFTHardSettings");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                var klass = Memory.ReadValue<IL2CPPLib.Class>(klassPtr);
                if (!MemDMA.IsValidVirtualAddress(klass.static_fields))
                    return 0;

                var instance = Memory.ReadPtr(klass.static_fields + Offsets.EFTHardSettings._instance);
                if (!MemDMA.IsValidVirtualAddress(instance))
                    return 0;

                _cached = instance;
                DebugLogger.LogDebug($"[HardSettingsResolver] Resolved @ 0x{instance:X}");
                return instance;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[HardSettingsResolver] Error: {ex.Message}");
                return 0;
            }
        }

        public static void Reset() => _cached = 0;
    }
}
