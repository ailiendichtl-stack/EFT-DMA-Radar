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
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// List of PMC/Scav 'Exits' in Local Game World and their position/status.
    /// </summary>
    public sealed class ExitManager : IReadOnlyCollection<IExitPoint>
    {
        private readonly IReadOnlyList<IExitPoint> _exits;
        private readonly List<Exfil> _exfils = new();
        private readonly ulong _localGameWorld;
        private readonly bool _isPMC;
        private bool _memoryExfilsLoaded = false;

        public ExitManager(string mapId, bool isPMC, ulong localGameWorld = 0)
        {
            _localGameWorld = localGameWorld;
            _isPMC = isPMC;

            var list = new List<IExitPoint>();
            if (TarkovDataManager.MapData.TryGetValue(mapId, out var map))
            {
                var filteredExfils = isPMC ?
                    map.Extracts.Where(x => x.IsShared || x.IsPmc) :
                    map.Extracts.Where(x => !x.IsPmc);
                foreach (var exfil in filteredExfils)
                {
                    var exfilObj = new Exfil(exfil);
                    list.Add(exfilObj);
                    _exfils.Add(exfilObj);
                }
                foreach (var transit in map.Transits)
                {
                    list.Add(new TransitPoint(transit));
                }
            }

            _exits = list;

            // Try to load memory exfil addresses on construction
            if (localGameWorld != 0)
            {
                TryLoadMemoryExfils();
            }
        }

        /// <summary>
        /// Attempts to load exfil point addresses from memory and match them to static data.
        /// </summary>
        private void TryLoadMemoryExfils()
        {
            if (_memoryExfilsLoaded || _localGameWorld == 0)
                return;

            try
            {
                var exfilController = Memory.ReadPtr(_localGameWorld + Offsets.GameWorld.ExfiltrationController);
                if (exfilController == 0)
                    return;

                // Read the appropriate exfil array based on player type
                var exfilArrayOffset = _isPMC
                    ? Offsets.ExfiltrationController.ExfiltrationPoints
                    : Offsets.ExfiltrationController.ScavExfiltrationPoints;

                var exfilArrayAddr = Memory.ReadPtr(exfilController + exfilArrayOffset);
                if (exfilArrayAddr == 0)
                    return;

                using var exfilArray = UnityArray<ulong>.Create(exfilArrayAddr, useCache: false);

                foreach (var exfilPointAddr in exfilArray)
                {
                    if (exfilPointAddr == 0)
                        continue;

                    try
                    {
                        // Read exfil name from memory
                        var settingsAddr = Memory.ReadPtr(exfilPointAddr + Offsets.ExfiltrationPoint.Settings);
                        if (settingsAddr == 0)
                            continue;

                        var namePtr = Memory.ReadPtr(settingsAddr + Offsets.ExitSettings.Name);
                        if (namePtr == 0)
                            continue;

                        var exfilName = Memory.ReadUnicodeString(namePtr, 64);
                        if (string.IsNullOrEmpty(exfilName))
                            continue;

                        // Match to static exfil data by name
                        var matchingExfil = _exfils.FirstOrDefault(e =>
                            e.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase));

                        if (matchingExfil != null)
                        {
                            matchingExfil.SetMemoryAddress(exfilPointAddr);
                        }
                    }
                    catch
                    {
                        // Skip this exfil point on error
                    }
                }

                _memoryExfilsLoaded = true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ExitManager] Failed to load memory exfils: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes exfil statuses from memory.
        /// Call this periodically to update live status.
        /// </summary>
        public void RefreshStatuses()
        {
            // Try to load memory addresses if not done yet
            if (!_memoryExfilsLoaded)
            {
                TryLoadMemoryExfils();
            }

            // Update status for all exfils that have memory addresses
            foreach (var exfil in _exfils)
            {
                exfil.UpdateStatus();
            }
        }

        #region IReadOnlyCollection

        public int Count => _exits?.Count ?? 0;
        public IEnumerator<IExitPoint> GetEnumerator() => _exits?.GetEnumerator() ?? Enumerable.Empty<IExitPoint>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}