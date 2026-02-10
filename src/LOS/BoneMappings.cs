using LoneEftDmaRadar.Tarkov.Unity.Structures;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Maps between the radar's Bones enum and the 18-bone LOS check indices.
    /// Used by VisibilityManager (to build per-bone masks) and ESPWindow (to read them).
    /// </summary>
    public static class BoneMappings
    {
        /// <summary>
        /// LOS bone index (0-17) → radar Bones enum.
        /// </summary>
        public static readonly Bones[] IndexToBone =
        {
            Bones.HumanHead,        // 0
            Bones.HumanNeck,        // 1
            Bones.HumanSpine3,      // 2
            Bones.HumanSpine2,      // 3
            Bones.HumanSpine1,      // 4
            Bones.HumanPelvis,      // 5
            Bones.HumanLCollarbone, // 6
            Bones.HumanRCollarbone, // 7
            Bones.HumanLUpperarm,   // 8
            Bones.HumanRUpperarm,   // 9
            Bones.HumanLForearm1,   // 10
            Bones.HumanRForearm1,   // 11
            Bones.HumanLThigh1,     // 12
            Bones.HumanRThigh1,     // 13
            Bones.HumanLCalf,       // 14
            Bones.HumanRCalf,       // 15
            Bones.HumanLFoot,       // 16
            Bones.HumanRFoot,       // 17
        };

        public const int BoneCount = 18;

        /// <summary>
        /// Radar Bones enum → LOS bone index (0-17). Returns -1 for unmapped bones.
        /// </summary>
        public static readonly Dictionary<Bones, int> BoneToIndex;

        /// <summary>
        /// Skeleton bones that aren't raycasted but should inherit visibility from their
        /// nearest mapped parent (e.g., Palm inherits from Forearm1).
        /// </summary>
        private static readonly Dictionary<Bones, Bones> BoneFallback = new()
        {
            [Bones.HumanLForearm2] = Bones.HumanLForearm1,
            [Bones.HumanLForearm3] = Bones.HumanLForearm1,
            [Bones.HumanLPalm] = Bones.HumanLForearm1,
            [Bones.HumanRForearm2] = Bones.HumanRForearm1,
            [Bones.HumanRForearm3] = Bones.HumanRForearm1,
            [Bones.HumanRPalm] = Bones.HumanRForearm1,
            [Bones.HumanLThigh2] = Bones.HumanLThigh1,
            [Bones.HumanRThigh2] = Bones.HumanRThigh1,
        };

        static BoneMappings()
        {
            BoneToIndex = new Dictionary<Bones, int>();
            for (int i = 0; i < IndexToBone.Length; i++)
                BoneToIndex[IndexToBone[i]] = i;
        }

        /// <summary>
        /// Check if a specific bone bit is set in a visibility mask.
        /// Falls back to nearest mapped parent for unmapped skeleton bones.
        /// </summary>
        public static bool IsBoneSet(uint mask, Bones bone)
        {
            if (!BoneToIndex.TryGetValue(bone, out int idx))
            {
                if (BoneFallback.TryGetValue(bone, out var parent))
                    return IsBoneSet(mask, parent);
                return false;
            }
            return (mask & (1u << idx)) != 0;
        }
    }
}
