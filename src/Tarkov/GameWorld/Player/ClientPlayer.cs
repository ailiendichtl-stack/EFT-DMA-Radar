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

using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player
{
    public class ClientPlayer : AbstractPlayer
    {
        /// <summary>
        /// True if this is an offline AI bot (not the actual local player).
        /// </summary>
        public bool IsOfflineAI { get; }
        /// <summary>
        /// EFT.Profile Address
        /// </summary>
        public ulong Profile { get; }
        /// <summary>
        /// PlayerInfo Address (GClass1044)
        /// </summary>
        public ulong Info { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name { get; set; }
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public override string AccountID { get; }
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public override int GroupID { get; protected set; } = -1;
        /// <summary>
        /// Player's Faction.
        /// </summary>
        public override Enums.EPlayerSide PlayerSide { get; protected set; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; protected set; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public override ulong MovementContext { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public override ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public override ulong RotationAddress { get; }

        #region Weapon Detection

        private string _cachedWeaponName;
        private ulong _lastHandsController;
        private DateTime _lastWeaponUpdate = DateTime.MinValue;
        private static readonly TimeSpan _weaponUpdateInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Currently held weapon name for offline AI bots.
        /// </summary>
        public override string HeldWeaponName
        {
            get
            {
                if (!IsOfflineAI)
                    return null; // LocalPlayer uses FirearmManager

                if (DateTime.UtcNow - _lastWeaponUpdate < _weaponUpdateInterval)
                    return _cachedWeaponName;

                _lastWeaponUpdate = DateTime.UtcNow;

                try
                {
                    var hands = Memory.ReadPtr(Base + Offsets.Player._handsController, false);

                    if (hands != _lastHandsController)
                    {
                        _lastHandsController = hands;
                        _cachedWeaponName = ReadWeaponNameFromHands(hands);
                    }
                }
                catch
                {
                    _cachedWeaponName = null;
                }

                return _cachedWeaponName;
            }
        }

        #endregion

        internal ClientPlayer(ulong playerBase) : base(playerBase)
        {
            Profile = Memory.ReadPtr(this + Offsets.Player.Profile);
            Info = Memory.ReadPtr(Profile + Offsets.Profile.Info);
            CorpseAddr = this + Offsets.Player.Corpse;
            PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(Info + Offsets.PlayerInfo.Side);
            if (!Enum.IsDefined<Enums.EPlayerSide>(PlayerSide))
                throw new ArgumentOutOfRangeException(nameof(PlayerSide));

            // Check if this is the actual local player or an offline AI bot
            var localPlayerAddr = Memory.LocalPlayer?.Base ?? 0;
            IsOfflineAI = playerBase != localPlayerAddr;

            GroupID = GetGroupNumber();
            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.MovementContext._rotation);
            /// Setup Transform
            var ti = Memory.ReadPtrChain(this, false, _transformInternalChain);
            SkeletonRoot = new UnityTransform(ti);
            var initialPos = SkeletonRoot.UpdatePosition();
            SetupBones();
            // Initialize cached position for fallback (in case skeleton updates fail later)
            _cachedPosition = initialPos;

            // For offline AI bots, we need to determine their type and name
            // Skip if this is the actual LocalPlayer being constructed (LocalPlayer class sets its own properties)
            if (IsOfflineAI && this is not LocalPlayer)
            {
                IsHuman = false; // Offline AI are not human-controlled
                SetupOfflineAI();
            }
            else
            {
                IsHuman = true; // Actual local player is human-controlled
            }
        }

        /// <summary>
        /// Sets up name and type for offline AI bots.
        /// </summary>
        private void SetupOfflineAI()
        {
            DebugLogger.LogDebug($"[PlayerDetect] SetupOfflineAI: Base=0x{Base:X}, PlayerSide={PlayerSide}, IsScav={IsScav}, IsPmc={IsPmc}");

            // First try SpawnType detection (works for PMC bots which have PlayerSide=Savage)
            var spawnType = ReadSpawnType();
            DebugLogger.LogDebug($"[PlayerDetect] SpawnType={spawnType} ({(int)spawnType})");

            if (spawnType != Enums.ESpawnType.UNKNOWN)
            {
                var role = GetAIRoleFromSpawnType(spawnType);
                Name = role.Name;
                Type = role.Type;
                DebugLogger.LogDebug($"[PlayerDetect] Role from SpawnType: Name='{role.Name}', Type={role.Type}");

                // Register boss spawn for guard timing detection
                if (Type == PlayerType.AIBoss)
                {
                    BossSpawnTracker.RegisterBossSpawn(_cachedPosition, Name);
                    DebugLogger.LogDebug($"[PlayerDetect] Registered boss spawn: {Name}");
                }

                DebugLogger.LogDebug($"[PlayerDetect] Final result: Name='{Name}', Type={Type}");
                return;
            }

            // Fallback to nickname-based detection for scavs/bosses
            if (IsScav)
            {
                try
                {
                    // Try reading nickname - bosses have specific names like "Решала" (Reshala)
                    var nicknamePtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.Nickname);
                    string nickname = nicknamePtr != 0 ? Memory.ReadUnicodeString(nicknamePtr) : "";

                    DebugLogger.LogDebug($"[PlayerDetect] Scav detected, nickname='{nickname}', nicknamePtr=0x{nicknamePtr:X}");

                    // Use nickname for role detection
                    var role = GetAIRoleInfo(nickname);
                    Name = role.Name;
                    Type = role.Type;

                    DebugLogger.LogDebug($"[PlayerDetect] Role from GetAIRoleInfo: Name='{role.Name}', Type={role.Type}");

                    // Register boss spawn for guard timing detection
                    if (Type == PlayerType.AIBoss)
                    {
                        BossSpawnTracker.RegisterBossSpawn(_cachedPosition, Name);
                        DebugLogger.LogDebug($"[PlayerDetect] Registered boss spawn: {Name}");
                    }
                    // Check if this is a guard via spawn timing (if detected as regular Scav)
                    else if (Type == PlayerType.AIScav && BossSpawnTracker.TryGetGuardInfo(_cachedPosition, out _))
                    {
                        Name = "Guard";
                        Type = PlayerType.AIRaider;
                        DebugLogger.LogDebug($"[PlayerDetect] Promoted to Guard via spawn timing");
                    }
                }
                catch (Exception ex)
                {
                    // Default to Scav if detection fails
                    Name = "Scav";
                    Type = PlayerType.AIScav;
                    DebugLogger.LogDebug($"[PlayerDetect] Exception in Scav detection: {ex.Message}");
                }
            }
            else if (IsPmc)
            {
                // PMC bots in offline mode (fallback if SpawnType didn't work)
                Name = PlayerSide == Enums.EPlayerSide.Bear ? "Bear" : "Usec";
                Type = PlayerType.PMC;
                DebugLogger.LogDebug($"[PlayerDetect] PMC detected via PlayerSide: Name='{Name}', Type={Type}");
            }
            else
            {
                Name = "Unknown";
                Type = PlayerType.Default;
                DebugLogger.LogDebug($"[PlayerDetect] Unknown player type: PlayerSide={PlayerSide}");
            }

            DebugLogger.LogDebug($"[PlayerDetect] Final result: Name='{Name}', Type={Type}");
        }

        /// <summary>
        /// Reads the SpawnType enum via AIData -> BotOwner -> SpawnProfileData -> SpawnType.
        /// This is the correct path for offline AI bots.
        /// </summary>
        private Enums.ESpawnType ReadSpawnType()
        {
            try
            {
                // Path: Player + AIData -> BotOwner -> SpawnProfileData -> SpawnType
                var aiData = Memory.ReadPtr(Base + Offsets.Player.AIData);
                if (aiData == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var botOwner = Memory.ReadPtr(aiData + Offsets.AIData.BotOwner);
                if (botOwner == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var spawnProfileData = Memory.ReadPtr(botOwner + Offsets.BotOwner.SpawnProfileData);
                if (spawnProfileData == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var spawnTypeValue = Memory.ReadValue<uint>(spawnProfileData + Offsets.SpawnProfileData.SpawnType);

                if (Enum.IsDefined(typeof(Enums.ESpawnType), spawnTypeValue))
                {
                    var spawnType = (Enums.ESpawnType)spawnTypeValue;
                    DebugLogger.LogDebug($"[PlayerDetect] SpawnType={spawnType} ({spawnTypeValue})");
                    return spawnType;
                }

                return Enums.ESpawnType.UNKNOWN;
            }
            catch
            {
                return Enums.ESpawnType.UNKNOWN;
            }
        }

        public int GetPoseLevel()
        {
             return Memory.ReadValue<int>(MovementContext + 0xD0); // 0xD0 = PoseLevel in MovementContext
        }

        public float GetFov()
        {
            try
            {
                var hands = Memory.ReadPtr(Base + Offsets.Player._handsController);
                if (hands == 0) return 0f;
                
                var anim = Memory.ReadPtr(hands + Offsets.FirearmController.WeaponAnimation);
                if (anim == 0) return 0f;

                return Memory.ReadValue<float>(anim + Offsets.ProceduralWeaponAnimation._fieldOfView);
            }
            catch { return 0f; }
        }

        public ulong PWA
        {
            get
            {
                try
                {
                    return Memory.ReadPtr(Base + Offsets.Player.ProceduralWeaponAnimation);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public bool IsAiming
        {
            get
            {
                try
                {
                    var weaponAnim = PWA;
                    if (weaponAnim == 0)
                    {
                        return false;
                    }

                    bool isAiming = Memory.ReadValue<bool>(weaponAnim + Offsets.ProceduralWeaponAnimation.IsAiming);
                    return isAiming;
                }
                catch
                {
                    return false;
                }
            }
        }

        public int GetCurrentOpticZoom()
        {
            try
            {
                // This is a placeholder for getting the current optic zoom
                // You would need to find the actual offset for the current optic zoom level
                // For now, we can return a default value or try to find the offset
                return 1;
            }
            catch { return 1; }
        }

        private void SetupBones()
        {
            var bonesToRegister = new[]
            {
                Bones.HumanHead,
                Bones.HumanNeck,
                Bones.HumanSpine3,
                Bones.HumanSpine2,
                Bones.HumanSpine1,
                Bones.HumanPelvis,
                Bones.HumanLUpperarm,
                Bones.HumanLForearm1,
                Bones.HumanLForearm2,
                Bones.HumanLPalm,
                Bones.HumanRUpperarm,
                Bones.HumanRForearm1,
                Bones.HumanRForearm2,
                Bones.HumanRPalm,
                Bones.HumanLThigh1,
                Bones.HumanLThigh2,
                Bones.HumanLCalf,
                Bones.HumanLFoot,
                Bones.HumanRThigh1,
                Bones.HumanRThigh2,
                Bones.HumanRCalf,
                Bones.HumanRFoot
            };

            foreach (var bone in bonesToRegister)
            {
                try
                {
                    var chain = _transformInternalChain.ToArray();
                    chain[chain.Length - 2] = UnityList<byte>.ArrStartOffset + (uint)bone * 0x8;
                    
                    var ti = Memory.ReadPtrChain(this, false, chain);
                    var transform = new UnityTransform(ti);
                    PlayerBones.TryAdd(bone, transform);
                }
                catch { }
            }
            
            if (PlayerBones.Count > 0)
            {
                 _verticesCount = PlayerBones.Values.Max(x => x.Count);
                 _verticesCount = Math.Max(_verticesCount, SkeletonRoot.Count);
            }
            Skeleton = new PlayerSkeleton(SkeletonRoot, PlayerBones);
        }

        /// <summary>
        /// Gets player's Group Number.
        /// </summary>
        private int GetGroupNumber()
        {
            try
            {
                var groupIdPtr = Memory.ReadPtr(Info + Offsets.PlayerInfo.GroupId);
                string groupId = Memory.ReadUnicodeString(groupIdPtr);
                return _groups.GetOrAdd(
                    groupId,
                    _ => Interlocked.Increment(ref _lastGroupNumber));
            }
            catch { return -1; } // will return null if Solo / Don't have a team
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            var movementContext = Memory.ReadPtr(this + Offsets.Player.MovementContext);
            var player = Memory.ReadPtr(movementContext + Offsets.MovementContext.Player, false);
            if (player != this)
                throw new ArgumentOutOfRangeException(nameof(movementContext));
            return movementContext;
        }

        private static readonly uint[] _transformInternalChain =
        [
            Offsets.Player._playerBody,
            Offsets.PlayerBody.SkeletonRootJoint,
            Offsets.DizSkinningSkeleton._values,
            UnityList<byte>.ArrOffset,
            UnityList<byte>.ArrStartOffset + (uint)Bones.HumanBase * 0x8,
            0x10
        ];
    }
}
