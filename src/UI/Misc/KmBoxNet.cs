using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace LoneEftDmaRadar.UI.Misc
{
    internal sealed class KmBoxNetClient : IDisposable
    {
        private readonly IPAddress _remote;
        private readonly int _port;
        private readonly string _macHex;
        private readonly UdpClient _udp = new();
        private uint _index;
        private bool _disposed;

        public KmBoxNetClient(IPAddress remote, int port, string macHex)
        {
            _remote = remote ?? throw new ArgumentNullException(nameof(remote));
            _port = port;
            _macHex = macHex ?? throw new ArgumentNullException(nameof(macHex));
        }

        public async Task<bool> ConnectAsync()
        {
            _udp.Connect(_remote, _port);
            var head = NextHead(KmCommand.CmdConnect);
            var response = await SendAndReceiveAsync<CmdHead, CmdHead>(head);
            return CheckResponse(head, response);
        }

        public async Task<bool> MouseMoveAsync(short x, short y)
        {
            var head = NextHead(KmCommand.CmdMouseMove);
            var action = new MouseAction { X = x, Y = y, Points = new int[10] };
            var response = await SendAndReceiveAsync<CmdHead, MouseAction, CmdHead>(head, action);
            return CheckResponse(head, response);
        }

        private async Task<TResponse> SendAndReceiveAsync<THead, TResponse>(THead head)
            where THead : struct
            where TResponse : struct
        {
            var payload = StructHelper.StructsToBytes(head);
            await _udp.SendAsync(payload);
            var result = await _udp.ReceiveAsync();
            return StructHelper.BytesToStruct<TResponse>(result.Buffer);
        }

        private async Task<TResponse> SendAndReceiveAsync<THead, TBody, TResponse>(THead head, TBody body)
            where THead : struct
            where TBody : struct
            where TResponse : struct
        {
            var payload = StructHelper.StructsToBytes(head, body);
            await _udp.SendAsync(payload);
            var result = await _udp.ReceiveAsync();
            return StructHelper.BytesToStruct<TResponse>(result.Buffer);
        }

        private CmdHead NextHead(KmCommand cmd)
        {
            return new CmdHead
            {
                mac = HexHelper.MacToUInt(_macHex),
                rand = (uint)RandomNumberGenerator.GetInt32(int.MaxValue),
                indexpts = _index++,
                cmd = cmd
            };
        }

        private static bool CheckResponse(CmdHead request, CmdHead response) =>
            request.cmd == response.cmd && request.indexpts == response.indexpts;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _udp.Dispose();
        }
    }

    #region Helper structures (trimmed from KMBox.NET)

    internal enum KmCommand : uint
    {
        CmdConnect = 0xaf3c2828,
        CmdMouseMove = 0xaede7345
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct CmdHead
    {
        [FieldOffset(0)] public uint mac;
        [FieldOffset(4)] public uint rand;
        [FieldOffset(8)] public uint indexpts;
        [FieldOffset(12)] public KmCommand cmd;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct MouseAction
    {
        [FieldOffset(0)] public int Buttons;
        [FieldOffset(4)] public int X;
        [FieldOffset(8)] public int Y;
        [FieldOffset(12)] public int Wheel;
        [FieldOffset(16)] [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)] public int[] Points;
    }

    internal static class StructHelper
    {
        public static byte[] StructsToBytes<T1>(T1 s1) where T1 : struct =>
            StructsToBytesInternal(new object[] { s1 });

        public static byte[] StructsToBytes<T1, T2>(T1 s1, T2 s2) where T1 : struct where T2 : struct =>
            StructsToBytesInternal(new object[] { s1, s2 });

        private static byte[] StructsToBytesInternal(object[] structs)
        {
            using var ms = new MemoryStream();
            foreach (var s in structs)
            {
                var type = s.GetType();
                int size = Marshal.SizeOf(type);
                var buffer = new byte[size];
                var ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(s, ptr, false);
                    Marshal.Copy(ptr, buffer, 0, size);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
                ms.Write(buffer, 0, size);
            }
            return ms.ToArray();
        }

        public static T BytesToStruct<T>(byte[] data) where T : struct
        {
            var type = typeof(T);
            int size = Marshal.SizeOf(type);
            if (data.Length < size) throw new ArgumentException("Buffer too small", nameof(data));
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(data, 0, ptr, size);
                return Marshal.PtrToStructure<T>(ptr);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    internal static class HexHelper
    {
        public static uint MacToUInt(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac))
                throw new ArgumentException("MAC is required", nameof(mac));

            return Convert.ToUInt32(mac, 16);
        }
    }

    #endregion
}
