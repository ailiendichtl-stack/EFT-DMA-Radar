/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System.ComponentModel;

namespace LoneEftDmaRadar.UI.Data
{
    /// <summary>
    /// UI entry for an active quest with blacklist toggle.
    /// </summary>
    public sealed class ActiveQuestEntry : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool _isEnabled;

        public ActiveQuestEntry(string id, string questName, string traderName, bool kappaRequired, bool lightkeeperRequired)
        {
            Id = id;
            QuestName = questName;
            TraderName = traderName;
            KappaRequired = kappaRequired;
            LightkeeperRequired = lightkeeperRequired;

            // Check if quest is currently blacklisted (inverted logic)
            _isEnabled = !App.Config.QuestHelper.BlacklistedQuests.ContainsKey(id);
        }

        public string Id { get; }
        public string QuestName { get; }
        public string TraderName { get; }
        public bool KappaRequired { get; }
        public bool LightkeeperRequired { get; }

        public string DisplayName => $"[{TraderName}] {QuestName}";
        public string Badges
        {
            get
            {
                if (KappaRequired) return "[Kappa]";
                if (LightkeeperRequired) return "[Lightkeeper]";
                return string.Empty;
            }
        }

        /// <summary>
        /// True if quest is enabled (NOT blacklisted), false if disabled (blacklisted).
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;

                    // Update blacklist (inverted logic)
                    if (_isEnabled)
                    {
                        // Enable = remove from blacklist
                        App.Config.QuestHelper.BlacklistedQuests.TryRemove(Id, out _);
                    }
                    else
                    {
                        // Disable = add to blacklist
                        App.Config.QuestHelper.BlacklistedQuests.TryAdd(Id, 0);
                    }

                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
