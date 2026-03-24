/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Quests;
using LoneEftDmaRadar.UI.Data;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using static LoneEftDmaRadar.Tarkov.TarkovDataManager.TaskElement;

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

        /// <summary>
        /// Per-map quest groups for the UI.
        /// </summary>
        public ObservableCollection<MapQuestGroup> MapGroups { get; } = new();

        public int ActiveQuestCount { get; private set; }

        public string SourceText { get; private set; } = "";

        #region Quest Helper Toggles

        public bool QuestHelperEnabled
        {
            get => App.Config.QuestHelper.Enabled;
            set
            {
                if (App.Config.QuestHelper.Enabled != value)
                {
                    App.Config.QuestHelper.Enabled = value;
                    OnPropertyChanged(nameof(QuestHelperEnabled));
                }
            }
        }

        public bool QuestHelperShowLocations
        {
            get => App.Config.QuestHelper.ShowLocations;
            set
            {
                if (App.Config.QuestHelper.ShowLocations != value)
                {
                    App.Config.QuestHelper.ShowLocations = value;
                    OnPropertyChanged(nameof(QuestHelperShowLocations));
                }
            }
        }

        #endregion

        /// <summary>
        /// Refresh the list of active quests.
        /// In raid: reads from QuestManager.
        /// In lobby: reads from LobbyQuestReader.
        /// </summary>
        public void RefreshQuests()
        {
            try
            {
                RefreshQuestsCore();
            }
            catch (Exception)
            {
                // DMA not connected or memory read failed — silently retry next cycle
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (MapGroups.Count == 0)
                    {
                        SourceText = "Waiting...";
                        OnPropertyChanged(nameof(SourceText));
                    }
                });
            }
        }

        private void RefreshQuestsCore()
        {
            // Collect all entries into a flat list first
            var allEntries = new List<ActiveQuestEntry>();
            var sourceText = "";

            var taskData = TarkovDataManager.TaskData;
            if (taskData == null)
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    SourceText = "No data";
                    MapGroups.Clear();
                    ActiveQuestCount = 0;
                    OnPropertyChanged(nameof(ActiveQuestCount));
                    OnPropertyChanged(nameof(SourceText));
                });
                return;
            }

            // Try in-raid data first (Memory may be null if DMA not connected)
            var questManager = Memory?.Quests;
            if (questManager?.Quests != null && questManager.Quests.Count > 0)
            {
                sourceText = "In Raid";
                foreach (var kvp in questManager.Quests)
                {
                    if (!taskData.TryGetValue(kvp.Key, out var task))
                        continue;

                    var maps = ResolveAllMaps(task);
                    foreach (var mapName in maps)
                    {
                        var entry = new ActiveQuestEntry(
                            kvp.Key, task.Name ?? "Unknown",
                            task.Trader?.Name ?? "Unknown",
                            task.KappaRequired, task.LightkeeperRequired,
                            mapName);
                        PopulateObjectives(entry, task, kvp.Value, mapName);
                        if (entry.Objectives.Count == 0 || entry.Objectives.All(o => o.IsCompleted))
                            continue;
                        allEntries.Add(entry);
                    }
                }
            }
            else
            {
                // Lobby: use LobbyQuestReader
                if (LobbyQuestReader.TryRefresh())
                {
                    sourceText = "Lobby";
                    var questIds = LobbyQuestReader.StartedQuestIds;
                    var lobbyCompleted = LobbyQuestReader.CompletedConditions;
                    var lobbyCounters = LobbyQuestReader.ConditionCounters;
                    if (questIds != null)
                    {
                        foreach (var questId in questIds)
                        {
                            if (!taskData.TryGetValue(questId, out var task))
                                continue;

                            // Get completed conditions for this quest
                            HashSet<string> completed = null;
                            lobbyCompleted?.TryGetValue(questId, out completed);

                            var maps = ResolveAllMaps(task);
                            foreach (var mapName in maps)
                            {
                                var entry = new ActiveQuestEntry(
                                    questId, task.Name ?? "Unknown",
                                    task.Trader?.Name ?? "Unknown",
                                    task.KappaRequired, task.LightkeeperRequired,
                                    mapName);
                                PopulateObjectivesLobby(entry, task, completed, lobbyCounters, mapName);
                                if (entry.Objectives.Count == 0 || entry.Objectives.All(o => o.IsCompleted))
                                    continue;
                                allEntries.Add(entry);
                            }
                        }
                    }
                }
                else
                {
                    sourceText = "No data";
                }
            }

            // Build bring lists per map
            var bringListsByMap = BuildBringLists(allEntries, taskData);

            // Count unique quest IDs (not duplicated across maps)
            var questCount = allEntries.Select(e => e.Id).Distinct().Count();

            // Marshal all UI updates to the dispatcher thread
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                // Remember which maps were collapsed before refresh
                var collapsedMaps = new HashSet<string>(
                    MapGroups.Where(g => !g.IsExpanded).Select(g => g.MapName),
                    StringComparer.OrdinalIgnoreCase);

                // Group by map, sort maps alphabetically with "Any" last, quests alphabetically within each map
                var groups = allEntries
                    .GroupBy(e => e.MapName)
                    .OrderBy(g => g.Key == "Any" ? "zzz" : g.Key)
                    .Select(g =>
                    {
                        bringListsByMap.TryGetValue(g.Key, out var bringItems);
                        var group = new MapQuestGroup(
                            g.Key,
                            g.OrderBy(e => e.QuestName).ToList(),
                            bringItems ?? []);
                        if (collapsedMaps.Contains(g.Key))
                            group.IsExpanded = false;
                        return group;
                    })
                    .ToList();

                MapGroups.Clear();
                foreach (var group in groups)
                    MapGroups.Add(group);

                SourceText = sourceText;
                ActiveQuestCount = questCount;
                OnPropertyChanged(nameof(ActiveQuestCount));
                OnPropertyChanged(nameof(SourceText));
            });
        }

        /// <summary>
        /// Build a bring list per map from task data (keys, quest items, markers).
        /// </summary>
        private static Dictionary<string, List<BringItemEntry>> BuildBringLists(
            List<ActiveQuestEntry> entries,
            IReadOnlyDictionary<string, TarkovDataManager.TaskElement> taskData)
        {
            var result = new Dictionary<string, List<BringItemEntry>>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // dedupe by "map|itemName"

            // Get unique quest IDs per map
            var questsByMap = entries
                .GroupBy(e => e.MapName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Id).Distinct().ToList());

            foreach (var (mapName, questIds) in questsByMap)
            {
                var items = new List<BringItemEntry>();

                foreach (var questId in questIds)
                {
                    if (!taskData.TryGetValue(questId, out var task))
                        continue;

                    var questLabel = task.Name ?? "Unknown";

                    // Needed keys (task-level)
                    if (task.NeededKeys != null)
                    {
                        foreach (var keyGroup in task.NeededKeys)
                        {
                            // Filter keys to this map if the key group has a map
                            if (keyGroup.Map != null && !string.IsNullOrEmpty(keyGroup.Map.Name)
                                && !string.Equals(keyGroup.Map.Name, mapName, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (keyGroup.Keys != null)
                            {
                                foreach (var key in keyGroup.Keys)
                                {
                                    var name = key.ShortName ?? key.Name;
                                    if (!string.IsNullOrEmpty(name) && seen.Add($"{mapName}|{name}"))
                                        items.Add(new BringItemEntry(name, "Key", questLabel));
                                }
                            }
                        }
                    }

                    // Per-objective items
                    if (task.Objectives != null)
                    {
                        foreach (var obj in task.Objectives)
                        {
                            if (!IsObjectiveOnMap(obj, task, mapName))
                                continue;

                            // Required keys on objectives
                            if (obj.RequiredKeys != null)
                            {
                                foreach (var keyAlts in obj.RequiredKeys)
                                {
                                    if (keyAlts == null) continue;
                                    // Show first alternative (they're interchangeable)
                                    var key = keyAlts.FirstOrDefault();
                                    if (key != null)
                                    {
                                        var name = key.ShortName ?? key.Name;
                                        if (!string.IsNullOrEmpty(name) && seen.Add($"{mapName}|{name}"))
                                            items.Add(new BringItemEntry(name, "Key", questLabel));
                                    }
                                }
                            }

                            // plantItem — bring the item from stash to plant (SV-98, Roler, etc.)
                            if (obj.Type == QuestObjectiveType.PlantItem && obj.Item != null)
                            {
                                var name = obj.Item.ShortName ?? obj.Item.Name;
                                if (!string.IsNullOrEmpty(name) && seen.Add($"{mapName}|{name}"))
                                    items.Add(new BringItemEntry(name, "Item", questLabel));
                            }

                            // mark — bring the marker tool (MS2000, etc.)
                            if (obj.Type == QuestObjectiveType.Mark && obj.MarkerItem != null)
                            {
                                var name = obj.MarkerItem.ShortName ?? obj.MarkerItem.Name;
                                if (!string.IsNullOrEmpty(name) && seen.Add($"{mapName}|{name}"))
                                    items.Add(new BringItemEntry(name, "Marker", questLabel));
                            }
                        }
                    }
                }

                if (items.Count > 0)
                    result[mapName] = items;
            }

            return result;
        }

        /// <summary>
        /// Resolve all unique map names for a task.
        /// If the task has a top-level map, that's the only map.
        /// Otherwise, collect unique maps from all objectives.
        /// Returns at least ["Any"] if no maps found.
        /// </summary>
        private static List<string> ResolveAllMaps(TarkovDataManager.TaskElement task)
        {
            // Task-level map takes priority — quest belongs to this map only
            if (task.Map != null && !string.IsNullOrEmpty(task.Map.Name))
                return [task.Map.Name];

            // Collect unique maps from objectives
            var maps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (task.Objectives != null)
            {
                foreach (var obj in task.Objectives)
                {
                    if (obj.Maps is { Count: > 0 })
                    {
                        foreach (var map in obj.Maps)
                        {
                            if (!string.IsNullOrEmpty(map.Name))
                                maps.Add(map.Name);
                        }
                    }
                }
            }

            return maps.Count > 0 ? maps.ToList() : ["Any"];
        }

        /// <summary>
        /// Populate objectives for a quest entry, filtering to objectives relevant to the given map.
        /// </summary>
        private static void PopulateObjectives(
            ActiveQuestEntry entry,
            TarkovDataManager.TaskElement task,
            QuestEntry raidQuest,
            string mapName)
        {
            if (task.Objectives == null)
                return;

            foreach (var obj in task.Objectives)
            {
                if (string.IsNullOrEmpty(obj.Description))
                    continue;

                // Filter objectives to this map group (if quest spans multiple maps)
                if (!IsObjectiveOnMap(obj, task, mapName))
                    continue;

                var typeLabel = GetTypeLabel(obj.Type);
                int target = obj.Count > 0 ? obj.Count : 1;
                int current = 0;
                bool isCompleted = false;

                if (raidQuest != null)
                {
                    isCompleted = raidQuest.CompletedConditions.Contains(obj.Id);

                    if (raidQuest.ConditionCounters.TryGetValue(obj.Id, out var counter))
                    {
                        current = counter.CurrentCount;
                        target = counter.TargetCount > 0 ? counter.TargetCount : target;
                        isCompleted = current >= target;
                    }
                    else if (isCompleted)
                    {
                        current = target;
                    }
                }

                entry.Objectives.Add(new QuestObjectiveEntry(obj.Id, obj.Description, typeLabel, isCompleted, current, target));
            }
        }

        /// <summary>
        /// Populate objectives for a quest entry using lobby profile data.
        /// </summary>
        private static void PopulateObjectivesLobby(
            ActiveQuestEntry entry,
            TarkovDataManager.TaskElement task,
            HashSet<string> completedConditions,
            IReadOnlyDictionary<string, (int CurrentCount, int TargetCount)> counters,
            string mapName)
        {
            if (task.Objectives == null)
                return;

            foreach (var obj in task.Objectives)
            {
                if (string.IsNullOrEmpty(obj.Description))
                    continue;

                if (!IsObjectiveOnMap(obj, task, mapName))
                    continue;

                var typeLabel = GetTypeLabel(obj.Type);
                int target = obj.Count > 0 ? obj.Count : 1;
                int current = 0;
                bool isCompleted = completedConditions?.Contains(obj.Id) == true;

                // Check condition counters from lobby profile
                if (counters != null && counters.TryGetValue(obj.Id, out var counter))
                {
                    current = counter.CurrentCount;
                    if (counter.TargetCount > 0)
                        target = counter.TargetCount;
                    isCompleted = current >= target;
                }
                else if (isCompleted)
                {
                    current = target;
                }

                entry.Objectives.Add(new QuestObjectiveEntry(obj.Id, obj.Description, typeLabel, isCompleted, current, target));
            }
        }

        private static string GetTypeLabel(QuestObjectiveType type) => type switch
        {
            QuestObjectiveType.Shoot => "Kill",
            QuestObjectiveType.FindItem or QuestObjectiveType.FindQuestItem => "Find",
            QuestObjectiveType.GiveItem or QuestObjectiveType.GiveQuestItem => "Hand Over",
            QuestObjectiveType.PlantItem or QuestObjectiveType.PlantQuestItem => "Plant",
            QuestObjectiveType.Visit => "Visit",
            QuestObjectiveType.Extract => "Extract",
            QuestObjectiveType.Mark => "Mark",
            QuestObjectiveType.BuildWeapon => "Build",
            QuestObjectiveType.Skill => "Skill",
            QuestObjectiveType.UseItem => "Use",
            QuestObjectiveType.SellItem => "Sell",
            QuestObjectiveType.TraderLevel => "Trader Lv.",
            QuestObjectiveType.TraderStanding => "Standing",
            QuestObjectiveType.TaskStatus => "Quest",
            QuestObjectiveType.Experience => "XP",
            _ => ""
        };

        /// <summary>
        /// Check if an objective belongs to a given map group.
        /// </summary>
        private static bool IsObjectiveOnMap(
            TarkovDataManager.TaskElement.ObjectiveElement obj,
            TarkovDataManager.TaskElement task,
            string mapName)
        {
            // If the task has a top-level map, all objectives belong to it
            if (task.Map != null && !string.IsNullOrEmpty(task.Map.Name))
                return true;

            // "Any" map group gets objectives with no map attribution
            if (mapName == "Any")
                return obj.Maps == null || obj.Maps.Count == 0;

            // Check if objective's maps include this map
            if (obj.Maps is { Count: > 0 })
                return obj.Maps.Any(m => string.Equals(m.Name, mapName, StringComparison.OrdinalIgnoreCase));

            return false;
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// A group of quests for a single map.
    /// </summary>
    public sealed class MapQuestGroup : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isExpanded = true;

        public MapQuestGroup(string mapName, List<ActiveQuestEntry> quests, List<BringItemEntry> bringItems)
        {
            MapName = mapName;
            Header = $"{mapName} ({quests.Count})";
            Quests = new ObservableCollection<ActiveQuestEntry>(quests);
            BringItems = new ObservableCollection<BringItemEntry>(bringItems);
            HasBringItems = bringItems.Count > 0;
        }

        public string MapName { get; }
        public string Header { get; }
        public ObservableCollection<ActiveQuestEntry> Quests { get; }
        public ObservableCollection<BringItemEntry> BringItems { get; }
        public bool HasBringItems { get; }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                }
            }
        }
    }
}
