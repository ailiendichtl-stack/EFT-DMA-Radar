/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

namespace LoneEftDmaRadar.Tarkov.GameWorld.Quests
{
    /// <summary>
    /// Constants used throughout the quest tracking system.
    /// </summary>
    internal static class QuestConstants
    {
        /// <summary>
        /// How often to refresh quest data from memory.
        /// </summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Quest status value indicating an active/started quest.
        /// </summary>
        public const int QuestStatusStarted = 2;

        /// <summary>
        /// Maximum length for quest ID strings.
        /// </summary>
        public const int MaxQuestIdLength = 64;

        // HashSet reading constants
        public const uint HashSetCountOffset = 0x3C;
        public const uint HashSetSlotsOffset = 0x18;
        public const uint ArrayLengthOffset = 0x18;
        public const uint ArrayHeaderSize = 0x20;
        public const uint SlotMongoIdOffset = 0x08;
        public const uint MongoIdStringOffset = 0x10;
        public const int MaxHashSetSlots = 100;
        public const int MaxHashSetArrayLength = 200;
        public const int MinConditionIdLength = 10;
        public const int MaxConditionIdLength = 128;
        public const int MaxValidConditionIdLength = 50;
        public const ulong MinValidPointer = 0x10000;

        /// <summary>
        /// HashSet slot sizes to try (primary).
        /// </summary>
        public static readonly int[] HashSetSlotSizes = { 0x20, 0x28, 0x30 };

        /// <summary>
        /// HashSet slot sizes to try (fallback).
        /// </summary>
        public static readonly int[] HashSetSlotSizesFallback = { 0x18, 0x38, 0x40 };

        // Dictionary reading constants
        public const uint DictionaryCountOffset = 0x40;
        public const uint DictionaryEntriesOffset = 0x18;
        public const int DictionaryEntrySize = 0x28;
        public const uint EntryHashCodeOffset = 0x00;
        public const uint EntryKeyOffset = 0x10;
        public const uint EntryValueOffset = 0x20;
        public const int MaxConditionCounterEntries = 300;
        public const int MaxCounterArrayLength = 500;
        public const int MaxCounterValue = 10000;
        public const int MaxMongoIdLength = 128;

        /// <summary>
        /// Default map ID when current map is unknown.
        /// </summary>
        public const string DefaultMapId = "MAPDEFAULT";

        // UI Constants
        public const float QuestMarkerSquareSize = 8f;
        public const float QuestMarkerStrokeWidth = 2f;
        public const float QuestMarkerHeightThreshold = 1.45f;
        public const int MaxDescriptionLength = 50;
    }
}
