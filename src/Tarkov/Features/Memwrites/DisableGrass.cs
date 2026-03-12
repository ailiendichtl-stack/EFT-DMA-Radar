using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables grass rendering by writing zero bounds to GPUInstancer runtime data.
    /// Resolves GPUInstancerManager list via IL2CPP class search.
    /// </summary>
    public sealed class DisableGrass : MemWriteFeature<DisableGrass>
    {
        private bool _lastEnabledState;
        private ulong _cachedManagerListPtr;
        private volatile bool _resolving;

        private static readonly HashSet<string> ExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "factory4_day", "factory4_night", "laboratory"
        };

        public override bool Enabled
        {
            get => App.Config.MemWrites.DisableGrassEnabled;
            set => App.Config.MemWrites.DisableGrassEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromSeconds(1);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                var mapId = Memory.Game?.MapID;
                if (mapId != null && ExcludedMaps.Contains(mapId))
                    return;

                if (Enabled == _lastEnabledState)
                    return;

                var listPtr = GetManagerListPtr();
                if (!MemDMA.IsValidVirtualAddress(listPtr))
                    return;

                ApplyGrassState(listPtr, Enabled);
                _lastEnabledState = Enabled;
            }
            catch
            {
                _cachedManagerListPtr = 0;
            }
        }

        private ulong GetManagerListPtr()
        {
            if (MemDMA.IsValidVirtualAddress(_cachedManagerListPtr))
                return _cachedManagerListPtr;

            if (_resolving)
                return 0;

            _resolving = true;
            try
            {
                if (!IL2CPPLib.Initialized)
                    return 0;

                var klassPtr = IL2CPPLib.Class.FindClass("GPUInstancerManager");
                if (!MemDMA.IsValidVirtualAddress(klassPtr))
                    return 0;

                var klass = Memory.ReadValue<IL2CPPLib.Class>(klassPtr);
                if (!MemDMA.IsValidVirtualAddress(klass.static_fields))
                    return 0;

                // Static field: List<GPUInstancerManager> activeManagerList
                var listPtr = Memory.ReadPtr(klass.static_fields);
                if (!MemDMA.IsValidVirtualAddress(listPtr))
                    return 0;

                _cachedManagerListPtr = listPtr;
                DebugLogger.LogDebug($"[DisableGrass] Resolved GPUInstancer list @ 0x{listPtr:X}");
                return listPtr;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DisableGrass] Resolve error: {ex.Message}");
                return 0;
            }
            finally
            {
                _resolving = false;
            }
        }

        private static void ApplyGrassState(ulong listPtr, bool hideGrass)
        {
            try
            {
                using var managers = UnityList<ulong>.Create(listPtr, false);
                foreach (var manager in managers)
                {
                    if (!MemDMA.IsValidVirtualAddress(manager))
                        continue;

                    var runtimeDataPtr = Memory.ReadPtr(manager + SDK.Offsets.GPUInstancerManagerOffsets.runtimeDataList);
                    if (!MemDMA.IsValidVirtualAddress(runtimeDataPtr))
                        continue;

                    using var runtimeList = UnityList<ulong>.Create(runtimeDataPtr, false);
                    foreach (var runtime in runtimeList)
                    {
                        if (!MemDMA.IsValidVirtualAddress(runtime))
                            continue;

                        // Write bounds: zero to hide, 0.5 to show
                        var boundsAddr = runtime + SDK.Offsets.GPUInstancerRuntimeData.instanceBounds;
                        if (hideGrass)
                        {
                            Memory.WriteValue(boundsAddr, System.Numerics.Vector3.Zero); // center
                            Memory.WriteValue(boundsAddr + 12, System.Numerics.Vector3.Zero); // extents
                        }
                        else
                        {
                            var half = new System.Numerics.Vector3(0.5f, 0.5f, 0.5f);
                            Memory.WriteValue(boundsAddr, half);
                            Memory.WriteValue(boundsAddr + 12, half);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[DisableGrass] Apply error: {ex.Message}");
            }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _cachedManagerListPtr = 0;
            _resolving = false;
        }
    }
}
