using System.IO.MemoryMappedFiles;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Result data read back from SHM for a single enemy.
    /// </summary>
    public struct EnemyResult
    {
        public uint VisibleMask;
        public uint HitscanMask;
    }

    /// <summary>
    /// Client for the Twilight SPT Fika shared memory LOS interface (v3).
    /// Writes enemy positions into SHM, reads back per-bone visibility masks.
    /// </summary>
    public sealed class SharedMemoryClient : IDisposable
    {
        #region Constants

        public const string SHM_NAME = "Local\\TwilightSPT_LOS";
        public const int SHM_SIZE = 8704;
        public const uint MAGIC = 0x4D545053; // 'SPTM'
        public const uint VERSION = 3;

        // Layout offsets
        private const int OFF_MAGIC = 0x00;
        private const int OFF_VERSION = 0x04;
        private const int OFF_FRAME_IN = 0x08;
        private const int OFF_FRAME_OUT = 0x0C;
        private const int OFF_ENEMY_COUNT = 0x10;
        private const int OFF_FLAGS = 0x14;
        private const int OFF_PLAYER_X = 0x18;
        private const int OFF_PLAYER_Y = 0x1C;
        private const int OFF_PLAYER_Z = 0x20;
        private const int OFF_FIREPORT_X = 0x24;
        private const int OFF_FIREPORT_Y = 0x28;
        private const int OFF_FIREPORT_Z = 0x2C;

        private const int ENEMY_BASE = 0x40;
        private const int ENEMY_STRIDE = 128;
        private const int MAX_ENEMIES = 64;

        // Enemy entry offsets (relative to enemy base)
        private const int ENT_FOOT_X = 0x00;
        private const int ENT_FOOT_Y = 0x04;
        private const int ENT_FOOT_Z = 0x08;
        private const int ENT_BONE_MASK = 0x0C;
        private const int ENT_VISIBLE_MASK = 0x10;
        private const int ENT_HITSCAN_MASK = 0x28;

        // SHM flags
        public const uint FLAG_CHECK_BONES = 0x01;
        public const uint FLAG_FIND_BEST = 0x02;
        public const uint FLAG_DUAL_CHECK = 0x04;
        public const uint FLAG_DEBUG_VIS = 0x08;
        public const uint FLAG_NO_FOLIAGE = 0x10;

        #endregion

        #region Bone Mapping

        /// <summary>
        /// Maps SHM bone index (0-17) to the radar's Bones enum value.
        /// </summary>
        public static readonly Bones[] ShmToRadarBone =
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

        /// <summary>
        /// Maps the radar's Bones enum value to SHM bone index.
        /// Returns -1 for unmapped bones.
        /// </summary>
        public static readonly Dictionary<Bones, int> RadarBoneToShmId;

        static SharedMemoryClient()
        {
            RadarBoneToShmId = new Dictionary<Bones, int>();
            for (int i = 0; i < ShmToRadarBone.Length; i++)
                RadarBoneToShmId[ShmToRadarBone[i]] = i;
        }

        #endregion

        #region Fields

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _accessor;
        private uint _frameCounter;
        private bool _disposed;

        #endregion

        #region Properties

        public bool IsConnected { get; private set; }

        #endregion

        #region Connection

        /// <summary>
        /// Try to open the existing SHM mapping and validate magic + version.
        /// </summary>
        public bool TryConnect()
        {
            if (IsConnected)
                return true;

            try
            {
                _mmf = MemoryMappedFile.OpenExisting(SHM_NAME, MemoryMappedFileRights.ReadWrite);
                _accessor = _mmf.CreateViewAccessor(0, SHM_SIZE, MemoryMappedFileAccess.ReadWrite);

                // Validate magic and version
                uint magic = _accessor.ReadUInt32(OFF_MAGIC);
                uint version = _accessor.ReadUInt32(OFF_VERSION);

                if (magic != MAGIC || version != VERSION)
                {
                    DebugLogger.LogInfo($"[SHM] Invalid magic/version: 0x{magic:X8}/{version} (expected 0x{MAGIC:X8}/{VERSION})");
                    Disconnect();
                    return false;
                }

                // Read current frame_out to sync our counter
                _frameCounter = _accessor.ReadUInt32(OFF_FRAME_OUT);

                IsConnected = true;
                DebugLogger.LogInfo($"[SHM] Connected to {SHM_NAME} (v{version})");
                return true;
            }
            catch (FileNotFoundException)
            {
                // SHM doesn't exist yet - plugin not running
                return false;
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SHM] Connect failed: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        private void Disconnect()
        {
            IsConnected = false;
            _accessor?.Dispose();
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;
        }

        #endregion

        #region Frame Operations

        /// <summary>
        /// Write the SHM header (enemy count, flags, player positions).
        /// </summary>
        public void WriteHeader(int enemyCount, uint flags, Vector3 playerEye, Vector3 fireport)
        {
            if (!IsConnected || _accessor == null) return;

            try
            {
                _accessor.Write(OFF_ENEMY_COUNT, (uint)Math.Min(enemyCount, MAX_ENEMIES));
                _accessor.Write(OFF_FLAGS, flags);
                _accessor.Write(OFF_PLAYER_X, playerEye.X);
                _accessor.Write(OFF_PLAYER_Y, playerEye.Y);
                _accessor.Write(OFF_PLAYER_Z, playerEye.Z);
                _accessor.Write(OFF_FIREPORT_X, fireport.X);
                _accessor.Write(OFF_FIREPORT_Y, fireport.Y);
                _accessor.Write(OFF_FIREPORT_Z, fireport.Z);
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SHM] WriteHeader failed: {ex.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// Write enemy data at the given index.
        /// </summary>
        public void WriteEnemy(int index, Vector3 footPos, uint boneMask)
        {
            if (!IsConnected || _accessor == null || index < 0 || index >= MAX_ENEMIES) return;

            try
            {
                int offset = ENEMY_BASE + (index * ENEMY_STRIDE);
                _accessor.Write(offset + ENT_FOOT_X, footPos.X);
                _accessor.Write(offset + ENT_FOOT_Y, footPos.Y);
                _accessor.Write(offset + ENT_FOOT_Z, footPos.Z);
                _accessor.Write(offset + ENT_BONE_MASK, boneMask);
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SHM] WriteEnemy[{index}] failed: {ex.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// Increment frame_in to signal the plugin to process this frame.
        /// </summary>
        public void SubmitFrame()
        {
            if (!IsConnected || _accessor == null) return;

            try
            {
                _frameCounter++;
                _accessor.Write(OFF_FRAME_IN, _frameCounter);
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[SHM] SubmitFrame failed: {ex.Message}");
                Disconnect();
            }
        }

        /// <summary>
        /// Spin-wait for the plugin to finish processing (frame_out == frame_in).
        /// Uses SpinWait to avoid Windows timer resolution issues.
        /// </summary>
        /// <returns>True if results are ready, false on timeout.</returns>
        public bool WaitForResults(int timeoutMs = 16)
        {
            if (!IsConnected || _accessor == null) return false;

            try
            {
                var sw = Stopwatch.StartNew();
                var spinner = new SpinWait();

                while (sw.ElapsedMilliseconds < timeoutMs)
                {
                    uint frameOut = _accessor.ReadUInt32(OFF_FRAME_OUT);
                    if (frameOut == _frameCounter)
                        return true;

                    spinner.SpinOnce();
                }

                return false; // Timeout
            }
            catch
            {
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Read the visibility result for a specific enemy index.
        /// </summary>
        public EnemyResult ReadEnemyResult(int index)
        {
            var result = new EnemyResult();
            if (!IsConnected || _accessor == null || index < 0 || index >= MAX_ENEMIES)
                return result;

            try
            {
                int offset = ENEMY_BASE + (index * ENEMY_STRIDE);
                result.VisibleMask = _accessor.ReadUInt32(offset + ENT_VISIBLE_MASK);
                result.HitscanMask = _accessor.ReadUInt32(offset + ENT_HITSCAN_MASK);
            }
            catch
            {
                Disconnect();
            }

            return result;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Disconnect();
        }

        #endregion
    }
}
