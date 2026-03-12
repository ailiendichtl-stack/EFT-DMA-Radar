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
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using SDK;
using VmmSharpEx.Options;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Exits
{
    /// <summary>
    /// List of PMC/Scav 'Exits' in Local Game World and their position/status.
    /// </summary>
    public sealed class ExitManager : IReadOnlyCollection<IExitPoint>
    {
        private readonly List<IExitPoint> _exits;
        private readonly List<Exfil> _exfils = new();
        private readonly List<TransitPoint> _transits = new();
        private readonly ulong _localGameWorld;
        private readonly bool _isPMC;
        private readonly Player.LocalPlayer _localPlayer;
        private bool _memoryExfilsLoaded = false;
        private DateTime _lastRefresh = DateTime.MinValue;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

        public ExitManager(string mapId, bool isPMC, ulong localGameWorld = 0, Player.LocalPlayer localPlayer = null)
        {
            _localGameWorld = localGameWorld;
            _isPMC = isPMC;
            _localPlayer = localPlayer;

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
                    var transitObj = new TransitPoint(transit);
                    list.Add(transitObj);
                    _transits.Add(transitObj);
                }
            }

            _exits = list; // mutable to allow adding secret exfils from memory

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

                            // Load eligibility data
                            if (_isPMC)
                                matchingExfil.LoadEligibleEntryPoints(exfilPointAddr);
                            else
                                matchingExfil.LoadEligibleScavIds(exfilPointAddr);
                        }
                    }
                    catch
                    {
                        // Skip this exfil point on error
                    }
                }

                // Also load secret exfils (available to both PMC and Scav)
                TryLoadSecretExfils(exfilController);

                // Load transit point active status from memory
                TryLoadTransitStatus();

                _memoryExfilsLoaded = true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ExitManager] Failed to load memory exfils: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads secret exfiltration points from memory. These are not in static map data,
        /// so we create Exfil objects dynamically with positions read from their Transforms.
        /// </summary>
        private void TryLoadSecretExfils(ulong exfilController)
        {
            try
            {
                var secretArrayAddr = Memory.ReadPtr(exfilController + Offsets.ExfiltrationController.SecretExfiltrationPoints);
                if (secretArrayAddr == 0)
                    return;

                using var secretArray = UnityArray<ulong>.Create(secretArrayAddr, useCache: false);

                foreach (var exfilPointAddr in secretArray)
                {
                    if (exfilPointAddr == 0)
                        continue;

                    try
                    {
                        var settingsAddr = Memory.ReadPtr(exfilPointAddr + Offsets.ExfiltrationPoint.Settings);
                        if (settingsAddr == 0)
                            continue;

                        var namePtr = Memory.ReadPtr(settingsAddr + Offsets.ExitSettings.Name);
                        if (namePtr == 0)
                            continue;

                        var exfilName = Memory.ReadUnicodeString(namePtr, 64);
                        if (string.IsNullOrEmpty(exfilName))
                            continue;

                        // Check if already matched to static data
                        var existing = _exfils.FirstOrDefault(e =>
                            e.Name.Equals(exfilName, StringComparison.OrdinalIgnoreCase));

                        if (existing != null)
                        {
                            existing.SetMemoryAddress(exfilPointAddr);
                            continue;
                        }

                        // Secret exfil not in static data — read position from Transform
                        var ti = Memory.ReadPtrChain(exfilPointAddr, false, UnitySDK.UnityOffsets.TransformChain);
                        if (ti == 0)
                            continue;

                        var transform = new UnityTransform(ti);
                        var position = transform.UpdatePosition();

                        var secretExfil = new Exfil(exfilName, position, exfilPointAddr, Exfil.EFaction.Shared);
                        _exits.Add(secretExfil);
                        _exfils.Add(secretExfil);
                    }
                    catch
                    {
                        // Skip this secret exfil on error
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ExitManager] Failed to load secret exfils: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads transit controller from memory and updates active status of transit points.
        /// </summary>
        private void TryLoadTransitStatus()
        {
            if (_transits.Count == 0)
                return;

            try
            {
                var transitController = Memory.ReadPtr(_localGameWorld + Offsets.GameWorld.TransitController);
                if (transitController == 0)
                    return;

                // TransitPoints is a Dictionary<int, TransitPoint> at offset 0x18
                var transitDictAddr = Memory.ReadPtr(transitController + Offsets.TransitController.TransitPoints);
                if (transitDictAddr == 0)
                    return;

                using var transitDict = UnityDictionary<int, ulong>.Create(transitDictAddr, useCache: false);

                foreach (var entry in transitDict)
                {
                    if (entry.Value == 0)
                        continue;

                    try
                    {
                        // Read TransitParameters from the transit point object
                        var paramsAddr = Memory.ReadPtr(entry.Value + Offsets.TransitPoint.parameters);
                        if (paramsAddr == 0)
                            continue;

                        var isActive = Memory.ReadValue<bool>(paramsAddr + Offsets.TransitParameters.active);

                        // Read transit name for matching
                        var namePtr = Memory.ReadPtr(paramsAddr + Offsets.TransitParameters.name);
                        if (namePtr == 0)
                            continue;

                        var transitName = Memory.ReadUnicodeString(namePtr, 64);
                        if (string.IsNullOrEmpty(transitName))
                            continue;

                        // Match to static transit data by description
                        var match = _transits.FirstOrDefault(t =>
                            t.Description.Equals(transitName, StringComparison.OrdinalIgnoreCase));

                        if (match != null)
                        {
                            match.IsActive = isActive;
                            match.SetMemoryAddress(paramsAddr);
                        }
                    }
                    catch
                    {
                        // Skip this transit point on error
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[ExitManager] Failed to load transit status: {ex.Message}");
            }
        }

        /// <summary>
        /// Refreshes exfil statuses from memory using batched scatter reads.
        /// Throttled to every 2 seconds — exfil statuses don't change faster than that.
        /// </summary>
        public void RefreshStatuses()
        {
            if (DateTime.UtcNow - _lastRefresh < RefreshInterval)
                return;
            _lastRefresh = DateTime.UtcNow;

            // Try to load memory addresses if not done yet
            if (!_memoryExfilsLoaded)
            {
                TryLoadMemoryExfils();
            }

            // Batch all exfil status reads into a single scatter operation
            using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
            foreach (var exfil in _exfils)
                exfil.OnRefresh(scatter);
            scatter.Execute();

        }

        #region IReadOnlyCollection

        public int Count => _exits?.Count ?? 0;
        public IEnumerator<IExitPoint> GetEnumerator() => _exits?.GetEnumerator() ?? Enumerable.Empty<IExitPoint>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        #endregion
    }
}