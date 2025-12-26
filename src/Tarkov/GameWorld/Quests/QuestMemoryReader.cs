/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using Collections.Pooled;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using SDK;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Handles low-level memory reading operations for quest data structures.
    /// </summary>
    internal sealed class QuestMemoryReader
    {
        private readonly ulong _profile;

        /// <summary>
        /// Cached TaskConditionCounter pointers for fast value updates.
        /// Key: condition ID, Value: (counterPtr, targetCount)
        /// </summary>
        private readonly ConcurrentDictionary<string, (ulong CounterPtr, int TargetCount)> _counterPointers = new(StringComparer.OrdinalIgnoreCase);
        private bool _countersInitialized = false;

        public QuestMemoryReader(ulong profile)
        {
            _profile = profile;
        }

        #region HashSet Reading

        /// <summary>
        /// Read completed conditions from HashSet&lt;MongoID&gt; manually.
        /// </summary>
        public void ReadCompletedConditionsHashSet(ulong hashSetPtr, PooledList<string> results)
        {
            var count = Memory.ReadValue<int>(hashSetPtr + QuestConstants.HashSetCountOffset);

            if (count <= 0 || count > QuestConstants.MaxHashSetSlots)
            {
                var altCount = Memory.ReadValue<int>(hashSetPtr + 0x38);
                if (altCount > 0 && altCount <= QuestConstants.MaxHashSetSlots)
                {
                    count = altCount;
                }
                else
                {
                    return;
                }
            }

            var slotsArrayPtr = Memory.ReadPtr(hashSetPtr + QuestConstants.HashSetSlotsOffset);
            if (slotsArrayPtr == 0)
            {
                slotsArrayPtr = Memory.ReadPtr(hashSetPtr + 0x10);
                if (slotsArrayPtr == 0)
                    return;
            }

            var slotsArrayLength = Memory.ReadValue<int>(slotsArrayPtr + QuestConstants.ArrayLengthOffset);
            if (slotsArrayLength <= 0 || slotsArrayLength > QuestConstants.MaxHashSetArrayLength)
                return;

            // Try all possible slot sizes (primary)
            foreach (var slotSize in QuestConstants.HashSetSlotSizes)
            {
                if (TryReadSlotsWithSize(slotsArrayPtr, slotsArrayLength, slotSize, count, results))
                    return;
            }

            // If standard sizes didn't work, try fallback sizes
            foreach (var slotSize in QuestConstants.HashSetSlotSizesFallback)
            {
                if (TryReadSlotsWithSize(slotsArrayPtr, slotsArrayLength, slotSize, count, results))
                    return;
            }

            // Try reading MongoID directly without hashcode check
            TryReadSlotsDirectMongoId(slotsArrayPtr, slotsArrayLength, count, results);
        }

        private static bool TryReadSlotsWithSize(ulong slotsArrayPtr, int slotsArrayLength, int slotSize, int expectedCount, PooledList<string> results)
        {
            results.Clear();
            var slotsStart = slotsArrayPtr + QuestConstants.ArrayHeaderSize;
            int foundCount = 0;

            for (int i = 0; i < slotsArrayLength && i < QuestConstants.MaxHashSetSlots; i++)
            {
                try
                {
                    var slotAddr = slotsStart + (ulong)(i * slotSize);
                    var hashCode = Memory.ReadValue<int>(slotAddr);

                    if (hashCode < 0)
                        continue;

                    var mongoIdOffset = slotAddr + QuestConstants.SlotMongoIdOffset;
                    var stringIdPtr = Memory.ReadPtr(mongoIdOffset + QuestConstants.MongoIdStringOffset);

                    if (stringIdPtr != 0 && stringIdPtr > QuestConstants.MinValidPointer)
                    {
                        var conditionId = Memory.ReadUnicodeString(stringIdPtr, QuestConstants.MaxConditionIdLength, true);
                        if (IsValidConditionId(conditionId))
                        {
                            results.Add(conditionId);
                            foundCount++;
                        }
                    }
                }
                catch
                {
                    // Skip invalid slots
                }
            }

            if (foundCount > 0 && (foundCount >= expectedCount || foundCount >= (expectedCount + 1) / 2))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadSlotsDirectMongoId(ulong slotsArrayPtr, int slotsArrayLength, int expectedCount, PooledList<string> results)
        {
            results.Clear();
            var slotsStart = slotsArrayPtr + QuestConstants.ArrayHeaderSize;
            int foundCount = 0;

            int[] mongoIdOffsets = { 0x00, 0x08, 0x10, 0x18 };
            int[] slotSizes = { 0x20, 0x28, 0x18 };

            foreach (var slotSize in slotSizes)
            {
                foreach (var mongoOffset in mongoIdOffsets)
                {
                    results.Clear();
                    foundCount = 0;

                    for (int i = 0; i < slotsArrayLength && i < QuestConstants.MaxHashSetSlots; i++)
                    {
                        try
                        {
                            var slotAddr = slotsStart + (ulong)(i * slotSize);
                            var mongoId = Memory.ReadValue<MongoID>(slotAddr + (ulong)mongoOffset, false);
                            var conditionId = mongoId.ReadString(QuestConstants.MaxMongoIdLength, true);

                            if (IsValidConditionId(conditionId))
                            {
                                results.Add(conditionId);
                                foundCount++;
                            }
                        }
                        catch
                        {
                            // Skip invalid entries
                        }
                    }

                    if (foundCount > 0 && foundCount >= (expectedCount + 1) / 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsValidConditionId(string conditionId)
        {
            if (string.IsNullOrEmpty(conditionId))
                return false;

            if (conditionId.Length < QuestConstants.MinConditionIdLength ||
                conditionId.Length > QuestConstants.MaxValidConditionIdLength)
                return false;

            foreach (char c in conditionId)
            {
                if (c < 0x20 || c > 0x7E)
                    return false;
            }

            return true;
        }

        #endregion

        #region Condition Counter Reading

        /// <summary>
        /// Read condition counters from the player profile.
        /// </summary>
        public IReadOnlyDictionary<string, (int CurrentCount, int TargetCount)> ReadConditionCounters()
        {
            var result = new ConcurrentDictionary<string, (int CurrentCount, int TargetCount)>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var countersPtr = Memory.ReadPtr(_profile + Offsets.Profile.TaskConditionCounters, false);
                if (countersPtr == 0)
                    return result;

                if (ShouldReinitializeCounters(countersPtr))
                {
                    _countersInitialized = false;
                }

                if (_countersInitialized && _counterPointers.Count > 0)
                {
                    RefreshCounterValuesFromCache(result);
                }
                else
                {
                    InitializeCounterPointers(countersPtr, result);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[QuestMemoryReader] Error reading condition counters: {ex.Message}");
            }

            return result;
        }

        private bool ShouldReinitializeCounters(ulong countersPtr)
        {
            var countPtr = countersPtr + QuestConstants.DictionaryCountOffset;
            var currentCount = Memory.ReadValue<int>(countPtr, false);

            if (_countersInitialized && currentCount != _counterPointers.Count)
            {
                return true;
            }

            return false;
        }

        private void RefreshCounterValuesFromCache(ConcurrentDictionary<string, (int CurrentCount, int TargetCount)> result)
        {
            foreach (var kvp in _counterPointers)
            {
                try
                {
                    var conditionId = kvp.Key;
                    var (counterPtr, targetValue) = kvp.Value;

                    var currentValue = Memory.ReadValue<int>(counterPtr + Offsets.TaskConditionCounter.Value, false);

                    if (currentValue >= 0 && currentValue < QuestConstants.MaxCounterValue)
                    {
                        result[conditionId] = (currentValue, targetValue);
                    }
                }
                catch
                {
                    // Counter may have become invalid
                }
            }
        }

        private void InitializeCounterPointers(ulong countersPtr, ConcurrentDictionary<string, (int CurrentCount, int TargetCount)> result)
        {
            var entriesPtr = Memory.ReadPtr(countersPtr + QuestConstants.DictionaryEntriesOffset, false);
            if (entriesPtr == 0)
                return;

            var arrayLength = Memory.ReadValue<int>(entriesPtr + QuestConstants.ArrayLengthOffset, false);

            if (arrayLength <= 0 || arrayLength > QuestConstants.MaxCounterArrayLength)
                return;

            _counterPointers.Clear();
            int foundCounters = 0;

            var entriesStart = entriesPtr + QuestConstants.ArrayHeaderSize;

            for (int i = 0; i < arrayLength && i < QuestConstants.MaxConditionCounterEntries; i++)
            {
                var counterData = TryReadCounterEntry(entriesStart, i);
                if (counterData.HasValue)
                {
                    var (conditionId, counterPtr, currentValue, targetValue) = counterData.Value;
                    _counterPointers[conditionId] = (counterPtr, targetValue);
                    result[conditionId] = (currentValue, targetValue);
                    foundCounters++;
                }
            }

            if (foundCounters > 0)
            {
                _countersInitialized = true;
            }
        }

        private static (string conditionId, ulong counterPtr, int currentValue, int targetValue)? TryReadCounterEntry(ulong entriesStart, int index)
        {
            try
            {
                var entryAddr = entriesStart + (uint)(index * QuestConstants.DictionaryEntrySize);

                var hashCode = Memory.ReadValue<int>(entryAddr + QuestConstants.EntryHashCodeOffset, false);
                if (hashCode < 0)
                    return null;

                var mongoId = Memory.ReadValue<MongoID>(entryAddr + QuestConstants.EntryKeyOffset, false);
                var conditionId = mongoId.ReadString(QuestConstants.MaxMongoIdLength, false);

                if (string.IsNullOrEmpty(conditionId))
                    return null;

                var counterPtr = Memory.ReadPtr(entryAddr + QuestConstants.EntryValueOffset, false);
                if (counterPtr == 0)
                    return null;

                var currentValue = Memory.ReadValue<int>(counterPtr + Offsets.TaskConditionCounter.Value, false);

                int targetValue = ReadTargetValue(counterPtr);

                if (currentValue >= 0 && currentValue < QuestConstants.MaxCounterValue)
                {
                    return (conditionId, counterPtr, currentValue, targetValue);
                }
            }
            catch
            {
                // Skip invalid entries
            }

            return null;
        }

        private static int ReadTargetValue(ulong counterPtr)
        {
            try
            {
                var templatePtr = Memory.ReadPtr(counterPtr + Offsets.TaskConditionCounter.Template, false);
                if (templatePtr != 0)
                {
                    var floatValue = Memory.ReadValue<float>(templatePtr + Offsets.Condition.Value, false);
                    var targetValue = (int)floatValue;

                    if (targetValue >= 0 && targetValue <= QuestConstants.MaxCounterValue)
                        return targetValue;
                }
            }
            catch
            {
                // Fall through to return 0
            }

            return 0;
        }

        #endregion

        /// <summary>
        /// Reset cached state (call on raid end).
        /// </summary>
        public void Reset()
        {
            _counterPointers.Clear();
            _countersInitialized = false;
        }
    }
}
