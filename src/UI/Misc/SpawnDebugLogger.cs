using System;
using System.IO;

namespace LoneEftDmaRadar.UI.Misc
{
    /// <summary>
    /// File-based debug logger for spawn detection debugging.
    /// Writes to spawn_debug.log - tries multiple locations.
    /// </summary>
    public static class SpawnDebugLogger
    {
        private static string _logPath;
        private static readonly object _lock = new object();
        private static bool _initialized = false;
        private static bool _disabled = false;

        public static void Log(string message)
        {
            if (_disabled) return;

            lock (_lock)
            {
                try
                {
                    if (!_initialized)
                    {
                        // Try multiple locations in order of preference
                        var candidates = new[]
                        {
                            Path.Combine(AppContext.BaseDirectory, "spawn_debug.log"),
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "spawn_debug.log"),
                            Path.Combine(Path.GetTempPath(), "spawn_debug.log")
                        };

                        foreach (var path in candidates)
                        {
                            try
                            {
                                File.WriteAllText(path, $"=== Spawn Debug Log Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                                File.AppendAllText(path, $"Log file location: {path}{Environment.NewLine}");
                                _logPath = path;
                                _initialized = true;
                                break;
                            }
                            catch
                            {
                                // Try next location
                            }
                        }

                        if (!_initialized)
                        {
                            _disabled = true; // Give up if all locations fail
                            return;
                        }
                    }

                    var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                    File.AppendAllText(_logPath, $"[{timestamp}] {message}{Environment.NewLine}");
                }
                catch
                {
                    // If writing fails after initialization, disable to avoid repeated failures
                    _disabled = true;
                }
            }
        }
    }
}
