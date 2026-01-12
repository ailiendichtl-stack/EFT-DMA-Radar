/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.UI.Data;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Radar.ViewModels
{
    public sealed class ActiveQuestsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Static instance for refresh handler access.
        /// </summary>
        public static ActiveQuestsViewModel Instance { get; private set; }

        public ActiveQuestsViewModel()
        {
            Instance = this;
        }

        public ObservableCollection<ActiveQuestEntry> ActiveQuests { get; } = new();

        public int ActiveQuestCount => ActiveQuests.Count;

        /// <summary>
        /// Refresh the list of active quests from QuestManager.
        /// Call this periodically or when quests change.
        /// </summary>
        public void RefreshQuests()
        {
            ActiveQuests.Clear();

            var questManager = Memory.Quests;
            if (questManager?.Quests == null || questManager.Quests.Count == 0)
            {
                OnPropertyChanged(nameof(ActiveQuestCount));
                return;
            }

            foreach (var kvp in questManager.Quests.OrderBy(q => GetQuestName(q.Key)))
            {
                var questId = kvp.Key;

                if (!TarkovDataManager.TaskData.TryGetValue(questId, out var task))
                    continue; // Skip quests not in TaskData (daily/weekly)

                var questName = task.Name ?? "Unknown Quest";
                var traderName = task.Trader?.Name ?? "Unknown";
                var kappaRequired = task.KappaRequired;
                var lightkeeperRequired = task.LightkeeperRequired;

                var entry = new ActiveQuestEntry(questId, questName, traderName, kappaRequired, lightkeeperRequired);
                ActiveQuests.Add(entry);
            }

            OnPropertyChanged(nameof(ActiveQuestCount));
        }

        private string GetQuestName(string questId)
        {
            return TarkovDataManager.TaskData.TryGetValue(questId, out var task)
                ? task.Name ?? "Unknown"
                : "Unknown";
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
