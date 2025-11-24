using System;
using System.Net;
using System.Threading.Tasks;

namespace LoneEftDmaRadar.UI.Misc
{
    /// <summary>
    /// Lightweight KMBox NET wrapper for mouse movement.
    /// </summary>
    internal static class DeviceNetController
    {
        private static KmBoxNetClient _client;
        private static readonly object _lock = new();

        public static bool Connected { get; private set; }

        public static bool Connect(string ip, int port, string macHex)
        {
            try
            {
                lock (_lock)
                {
                    Disconnect();

                    if (!IPAddress.TryParse(ip, out var address))
                    {
                        DebugLogger.LogDebug($"[KMBoxNet] Invalid IP: {ip}");
                        return false;
                    }

                    _client = new KmBoxNetClient(address, port, macHex);
                }

                // Connect asynchronously but block until done to keep existing flow simple.
                var ok = _client.ConnectAsync().GetAwaiter().GetResult();
                Connected = ok;
                DebugLogger.LogDebug(ok
                    ? "[KMBoxNet] Connected"
                    : "[KMBoxNet] Connection failed");
                return ok;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[KMBoxNet] Connect error: {ex}");
                Connected = false;
                return false;
            }
        }

        public static void Disconnect()
        {
            lock (_lock)
            {
                Connected = false;
                _client?.Dispose();
                _client = null;
            }
        }

        public static void Move(int dx, int dy)
        {
            if (!Connected || _client == null)
                return;

            try
            {
                _client.MouseMoveAsync((short)dx, (short)dy).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[KMBoxNet] Move error: {ex}");
                Connected = false;
            }
        }
    }
}
