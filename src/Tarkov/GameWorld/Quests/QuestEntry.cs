/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

namespace LoneEftDmaRadar.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Represents a single tracked quest with its completion state.
    /// </summary>
    public sealed class QuestEntry
    {
        private readonly HashSet<string> _completedConditions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (int CurrentCount, int TargetCount)> _conditionCounters = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Quest ID.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Set of completed condition IDs for this quest.
        /// </summary>
        public IReadOnlySet<string> CompletedConditions => _completedConditions;

        /// <summary>
        /// Condition counters for objectives with progress tracking.
        /// </summary>
        public IReadOnlyDictionary<string, (int CurrentCount, int TargetCount)> ConditionCounters => _conditionCounters;

        public QuestEntry(string id)
        {
            Id = id;
        }

        /// <summary>
        /// Updates the set of completed conditions.
        /// </summary>
        public void UpdateCompletedConditions(IEnumerable<string> conditions)
        {
            _completedConditions.Clear();
            foreach (var condition in conditions)
            {
                if (!string.IsNullOrEmpty(condition))
                    _completedConditions.Add(condition);
            }
        }

        /// <summary>
        /// Updates condition counters from memory.
        /// </summary>
        public void UpdateConditionCounters(IEnumerable<KeyValuePair<string, (int CurrentCount, int TargetCount)>> counters)
        {
            foreach (var kvp in counters)
            {
                _conditionCounters[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Check if an objective is completed.
        /// </summary>
        public bool IsObjectiveCompleted(string objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
                return false;
            return _completedConditions.Contains(objectiveId);
        }

        /// <summary>
        /// Get current progress count for an objective.
        /// </summary>
        public int GetObjectiveProgress(string objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
                return 0;
            return _conditionCounters.TryGetValue(objectiveId, out var counter) ? counter.CurrentCount : 0;
        }

        /// <summary>
        /// Get target count for an objective.
        /// </summary>
        public int GetObjectiveTargetCount(string objectiveId)
        {
            if (string.IsNullOrEmpty(objectiveId))
                return 0;
            return _conditionCounters.TryGetValue(objectiveId, out var counter) ? counter.TargetCount : 0;
        }
    }
}
