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

using System.Collections.Concurrent;
using System.Numerics;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers
{
    /// <summary>
    /// Tracks boss spawns to identify guards based on spawn timing.
    /// Guards typically spawn within a short time window after their boss.
    /// </summary>
    public static class BossSpawnTracker
    {
        /// <summary>
        /// Time window in milliseconds during which spawns after a boss are considered guards.
        /// </summary>
        private const int GuardSpawnWindowMs = 1500;

        /// <summary>
        /// Maximum distance from boss spawn for a guard to be associated.
        /// </summary>
        private const float MaxGuardDistanceFromBoss = 100f;

        private record BossSpawnRecord(DateTime SpawnTime, Vector3 Position, string BossName);

        private static readonly ConcurrentBag<BossSpawnRecord> _recentBossSpawns = new();
        private static readonly object _cleanupLock = new();
        private static DateTime _lastCleanup = DateTime.UtcNow;

        /// <summary>
        /// Register a boss spawn. Call this when a boss is detected.
        /// </summary>
        /// <param name="position">The spawn position of the boss.</param>
        /// <param name="bossName">The name of the boss.</param>
        public static void RegisterBossSpawn(Vector3 position, string bossName)
        {
            var record = new BossSpawnRecord(DateTime.UtcNow, position, bossName);
            _recentBossSpawns.Add(record);

            // Cleanup old entries periodically
            CleanupOldEntries();
        }

        /// <summary>
        /// Check if a newly spawned AI should be considered a guard based on spawn timing.
        /// </summary>
        /// <param name="position">The spawn position of the AI.</param>
        /// <returns>True if the AI spawned shortly after a boss and should be considered a guard.</returns>
        public static bool IsLikelyGuard(Vector3 position)
        {
            var now = DateTime.UtcNow;

            foreach (var bossSpawn in _recentBossSpawns)
            {
                var timeSinceSpawn = (now - bossSpawn.SpawnTime).TotalMilliseconds;

                // Check if within time window
                if (timeSinceSpawn <= GuardSpawnWindowMs)
                {
                    // Check if within distance
                    var distance = Vector3.Distance(position, bossSpawn.Position);
                    if (distance <= MaxGuardDistanceFromBoss)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a newly spawned AI should be considered a guard and get the boss name.
        /// </summary>
        /// <param name="position">The spawn position of the AI.</param>
        /// <param name="bossName">Output: the name of the boss this guard belongs to.</param>
        /// <returns>True if the AI spawned shortly after a boss and should be considered a guard.</returns>
        public static bool TryGetGuardInfo(Vector3 position, out string bossName)
        {
            var now = DateTime.UtcNow;
            bossName = "";

            BossSpawnRecord? closestBoss = null;
            float closestDistance = float.MaxValue;

            foreach (var bossSpawn in _recentBossSpawns)
            {
                var timeSinceSpawn = (now - bossSpawn.SpawnTime).TotalMilliseconds;

                // Check if within time window
                if (timeSinceSpawn <= GuardSpawnWindowMs)
                {
                    // Check if within distance
                    var distance = Vector3.Distance(position, bossSpawn.Position);
                    if (distance <= MaxGuardDistanceFromBoss && distance < closestDistance)
                    {
                        closestBoss = bossSpawn;
                        closestDistance = distance;
                    }
                }
            }

            if (closestBoss != null)
            {
                bossName = closestBoss.BossName;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clear all tracked boss spawns. Call this when starting a new raid.
        /// </summary>
        public static void Reset()
        {
            while (_recentBossSpawns.TryTake(out _)) { }
        }

        private static void CleanupOldEntries()
        {
            // Only cleanup every 5 seconds
            lock (_cleanupLock)
            {
                if ((DateTime.UtcNow - _lastCleanup).TotalSeconds < 5)
                    return;

                _lastCleanup = DateTime.UtcNow;
            }

            // Remove entries older than 10 seconds (way past the guard window)
            var cutoff = DateTime.UtcNow.AddSeconds(-10);
            var itemsToKeep = _recentBossSpawns.Where(x => x.SpawnTime > cutoff).ToList();

            // Clear and re-add (ConcurrentBag doesn't have a RemoveWhere)
            while (_recentBossSpawns.TryTake(out _)) { }
            foreach (var item in itemsToKeep)
            {
                _recentBossSpawns.Add(item);
            }
        }
    }
}
