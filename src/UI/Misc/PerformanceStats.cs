/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

namespace LoneEftDmaRadar.UI.Misc
{
    /// <summary>
    /// Static class to track worker thread performance metrics.
    /// Thread-safe for concurrent reads/writes.
    /// </summary>
    public static class PerformanceStats
    {
        // T1 Realtime Worker
        private static long _t1LastLoopTicks;
        private static long _t1AvgLoopTicks;
        private static int _t1LoopCount;

        // T2 Slow Worker
        private static long _t2LastLoopTicks;
        private static long _t2AvgLoopTicks;
        private static int _t2LoopCount;

        // T3 Explosives Worker
        private static long _t3LastLoopTicks;
        private static long _t3AvgLoopTicks;
        private static int _t3LoopCount;

        // Loot scan
        private static long _lastLootScanTicks;
        private static DateTime _lastLootScanTime;

        /// <summary>
        /// T1 (Realtime) worker last loop time in milliseconds.
        /// </summary>
        public static double T1LastLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t1LastLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// T1 (Realtime) worker average loop time in milliseconds.
        /// </summary>
        public static double T1AvgLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t1AvgLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// T2 (Slow) worker last loop time in milliseconds.
        /// </summary>
        public static double T2LastLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t2LastLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// T2 (Slow) worker average loop time in milliseconds.
        /// </summary>
        public static double T2AvgLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t2AvgLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// T3 (Explosives) worker last loop time in milliseconds.
        /// </summary>
        public static double T3LastLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t3LastLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// T3 (Explosives) worker average loop time in milliseconds.
        /// </summary>
        public static double T3AvgLoopMs => TimeSpan.FromTicks(Volatile.Read(ref _t3AvgLoopTicks)).TotalMilliseconds;

        /// <summary>
        /// Last loot scan duration in milliseconds.
        /// </summary>
        public static double LastLootScanMs => TimeSpan.FromTicks(Volatile.Read(ref _lastLootScanTicks)).TotalMilliseconds;

        /// <summary>
        /// Time since last loot scan in seconds.
        /// </summary>
        public static double SecondsSinceLastLootScan => (DateTime.UtcNow - _lastLootScanTime).TotalSeconds;

        /// <summary>
        /// Update T1 worker loop time.
        /// </summary>
        public static void UpdateT1(long elapsedTicks)
        {
            Volatile.Write(ref _t1LastLoopTicks, elapsedTicks);
            var count = Interlocked.Increment(ref _t1LoopCount);
            // Exponential moving average with decay
            if (count == 1)
            {
                Volatile.Write(ref _t1AvgLoopTicks, elapsedTicks);
            }
            else
            {
                var currentAvg = Volatile.Read(ref _t1AvgLoopTicks);
                var newAvg = (long)(currentAvg * 0.95 + elapsedTicks * 0.05);
                Volatile.Write(ref _t1AvgLoopTicks, newAvg);
            }
        }

        /// <summary>
        /// Update T2 worker loop time.
        /// </summary>
        public static void UpdateT2(long elapsedTicks)
        {
            Volatile.Write(ref _t2LastLoopTicks, elapsedTicks);
            var count = Interlocked.Increment(ref _t2LoopCount);
            if (count == 1)
            {
                Volatile.Write(ref _t2AvgLoopTicks, elapsedTicks);
            }
            else
            {
                var currentAvg = Volatile.Read(ref _t2AvgLoopTicks);
                var newAvg = (long)(currentAvg * 0.95 + elapsedTicks * 0.05);
                Volatile.Write(ref _t2AvgLoopTicks, newAvg);
            }
        }

        /// <summary>
        /// Update T3 worker loop time.
        /// </summary>
        public static void UpdateT3(long elapsedTicks)
        {
            Volatile.Write(ref _t3LastLoopTicks, elapsedTicks);
            var count = Interlocked.Increment(ref _t3LoopCount);
            if (count == 1)
            {
                Volatile.Write(ref _t3AvgLoopTicks, elapsedTicks);
            }
            else
            {
                var currentAvg = Volatile.Read(ref _t3AvgLoopTicks);
                var newAvg = (long)(currentAvg * 0.95 + elapsedTicks * 0.05);
                Volatile.Write(ref _t3AvgLoopTicks, newAvg);
            }
        }

        /// <summary>
        /// Update loot scan duration.
        /// </summary>
        public static void UpdateLootScan(long elapsedTicks)
        {
            Volatile.Write(ref _lastLootScanTicks, elapsedTicks);
            _lastLootScanTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Reset all stats (call on raid end).
        /// </summary>
        public static void Reset()
        {
            Volatile.Write(ref _t1LastLoopTicks, 0);
            Volatile.Write(ref _t1AvgLoopTicks, 0);
            Volatile.Write(ref _t1LoopCount, 0);
            Volatile.Write(ref _t2LastLoopTicks, 0);
            Volatile.Write(ref _t2AvgLoopTicks, 0);
            Volatile.Write(ref _t2LoopCount, 0);
            Volatile.Write(ref _t3LastLoopTicks, 0);
            Volatile.Write(ref _t3AvgLoopTicks, 0);
            Volatile.Write(ref _t3LoopCount, 0);
            Volatile.Write(ref _lastLootScanTicks, 0);
            _lastLootScanTime = DateTime.MinValue;
        }
    }
}
