using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    public class TcpResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("result")]
        public JsonElement? Result { get; set; }

        [JsonPropertyName("error")]
        public string Error { get; set; }
    }

    /// <summary>
    /// Simple line-based JSON-RPC TCP client for communicating with the
    /// Twilight SPT Fika plugin on port 21220.
    /// </summary>
    public sealed class TcpCommandClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _lock = new();
        private bool _disposed;

        public TcpCommandClient(string host = "127.0.0.1", int port = 21220)
        {
            _host = host;
            _port = port;
        }

        public bool IsConnected => _client?.Connected ?? false;

        /// <summary>
        /// Attempt to connect to the plugin TCP server.
        /// </summary>
        public bool TryConnect(int timeoutMs = 3000)
        {
            lock (_lock)
            {
                if (IsConnected) return true;

                try
                {
                    Disconnect();
                    _client = new TcpClient();
                    var connectTask = _client.ConnectAsync(_host, _port);
                    if (!connectTask.Wait(timeoutMs))
                    {
                        Disconnect();
                        return false;
                    }

                    _client.NoDelay = true;
                    _client.ReceiveTimeout = 10000;
                    _client.SendTimeout = 5000;

                    var stream = _client.GetStream();
                    _reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    _writer = new StreamWriter(stream, System.Text.Encoding.UTF8) { AutoFlush = true };

                    DebugLogger.LogInfo($"[TCP] Connected to {_host}:{_port}");
                    return true;
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[TCP] Connect failed: {ex.Message}");
                    Disconnect();
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnect and clean up the socket.
        /// </summary>
        public void Disconnect()
        {
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _client?.Dispose(); } catch { }
            _reader = null;
            _writer = null;
            _client = null;
        }

        /// <summary>
        /// Send a JSON-RPC command and read the response.
        /// Thread-safe via lock.
        /// </summary>
        public TcpResponse SendCommand(string method, object parameters = null)
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    if (!TryConnect())
                        return new TcpResponse { Success = false, Error = "Not connected" };
                }

                try
                {
                    var request = new Dictionary<string, object> { ["method"] = method };
                    if (parameters != null)
                        request["params"] = parameters;

                    var json = JsonSerializer.Serialize(request);
                    _writer.WriteLine(json);

                    var responseLine = _reader.ReadLine();
                    if (responseLine == null)
                    {
                        Disconnect();
                        return new TcpResponse { Success = false, Error = "Connection closed" };
                    }

                    return JsonSerializer.Deserialize<TcpResponse>(responseLine)
                           ?? new TcpResponse { Success = false, Error = "Failed to parse response" };
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[TCP] SendCommand '{method}' failed: {ex.Message}");
                    Disconnect();
                    return new TcpResponse { Success = false, Error = ex.Message };
                }
            }
        }

        #region Convenience Methods

        public bool Ping()
        {
            var resp = SendCommand("ping");
            return resp.Success;
        }

        /// <summary>
        /// Returns "menu", "loading", "in_raid", or null on error.
        /// </summary>
        public string GetGameState()
        {
            var resp = SendCommand("get_game_state");
            if (!resp.Success || resp.Result == null) return null;

            try
            {
                if (resp.Result.Value.TryGetProperty("state", out var stateProp))
                    return stateProp.GetString();
            }
            catch { }
            return null;
        }

        public bool StartRaid(string map, string side = "PMC", string time = "day")
        {
            var resp = SendCommand("start_raid", new { map, side, time });
            return resp.Success;
        }

        public bool LeaveRaid()
        {
            var resp = SendCommand("leave_raid");
            return resp.Success;
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_lock)
            {
                Disconnect();
            }
        }
    }
}
