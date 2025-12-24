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

using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc.Services;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using LoneEftDmaRadar.Web.ProfileApi;
using LoneEftDmaRadar.Web.ProfileApi.Schema;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player
{
    public class ObservedPlayer : AbstractPlayer
    {
        /// <summary>
        /// Player's Profile & Stats.
        /// </summary>
        public PlayerProfile Profile { get; }
        /// <summary>
        /// Player's Current Items.
        /// </summary>
        public PlayerEquipment Equipment { get; }
        /// <summary>
        /// Address of InventoryController field.
        /// </summary>
        public ulong InventoryControllerAddr { get; }
        /// <summary>
        /// ObservedPlayerController for non-clientplayer players.
        /// </summary>
        private ulong ObservedPlayerController { get; }
        /// <summary>
        /// ObservedHealthController for non-clientplayer players.
        /// </summary>
        private ulong ObservedHealthController { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name
        {
            get => Profile?.Name ?? "Unknown";
            set
            {
                if (Profile is PlayerProfile profile)
                    profile.Name = value;
            }
        }
        /// <summary>
        /// Type of player unit.
        /// </summary>
        public override PlayerType Type
        {
            get => Profile?.Type ?? PlayerType.Default;
            protected set
            {
                if (Profile is PlayerProfile profile)
                    profile.Type = value;
            }
        }
        /// <summary>
        /// Player Alerts.
        /// </summary>
        public override string Alerts
        {
            get => Profile?.Alerts;
            protected set
            {
                if (Profile is PlayerProfile profile)
                    profile.Alerts = value;
            }
        }
        /// <summary>
        /// Twitch.tv Channel URL for this player (if available).
        /// </summary>
        public string TwitchChannelURL => Profile?.TwitchChannelURL;
        /// <summary>
        /// True if player is TTV Streaming.
        /// </summary>
        public bool IsStreaming => TwitchChannelURL is not null;
        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public override string AccountID
        {
            get
            {
                if (Profile?.AccountID is string id)
                    return id;
                return "";
            }
        }
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public override int GroupID
        {
            get => Profile?.GroupID ?? -1;
            protected set
            {
                if (Profile is PlayerProfile profile)
                    profile.GroupID = value;
            }
        }
        /// <summary>
        /// Player's Faction.
        /// </summary>
        public override Enums.EPlayerSide PlayerSide
        {
            get => Profile?.PlayerSide ?? Enums.EPlayerSide.Savage;
            protected set
            {
                if (Profile is PlayerProfile profile)
                    profile.PlayerSide = value;
            }
        }
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
        /// <summary>
        /// Player's Current Health Status
        /// </summary>
        public Enums.ETagStatus HealthStatus { get; private set; } = Enums.ETagStatus.Healthy;

        #region Weapon Detection

        private string _cachedWeaponName;
        private ulong _lastObservedHands;
        private DateTime _lastWeaponUpdate = DateTime.MinValue;
        private static readonly TimeSpan _weaponUpdateInterval = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Currently held weapon name for online players.
        /// Chain: MovementContext (StateContext) → ObservedPlayerHands → Item
        /// </summary>
        public override string HeldWeaponName
        {
            get
            {
                if (DateTime.UtcNow - _lastWeaponUpdate < _weaponUpdateInterval)
                    return _cachedWeaponName;

                _lastWeaponUpdate = DateTime.UtcNow;

                try
                {
                    // MovementContext is already the ObservedPlayerStateContext (ObservedMovementState)
                    // Chain: MovementContext -> ObservedPlayerHands (0x130) -> Item (0x58)
                    if (!MemDMA.IsValidVirtualAddress(MovementContext))
                        return _cachedWeaponName;

                    var observedHands = Memory.ReadPtr(MovementContext + Offsets.ObservedMovementState.ObservedPlayerHands, false);
                    if (!MemDMA.IsValidVirtualAddress(observedHands))
                        return _cachedWeaponName;

                    if (observedHands != _lastObservedHands)
                    {
                        _lastObservedHands = observedHands;

                        // Read item from ObservedPlayerHands (offset 0x58)
                        var itemBase = Memory.ReadPtr(observedHands + Offsets.ObservedPlayerHands.Item, false);
                        if (!MemDMA.IsValidVirtualAddress(itemBase))
                        {
                            _cachedWeaponName = null;
                            return _cachedWeaponName;
                        }

                        // Use the same template chain as offline
                        _cachedWeaponName = ReadWeaponNameFromHandsItem(itemBase);
                    }
                }
                catch
                {
                    _cachedWeaponName = null;
                }

                return _cachedWeaponName;
            }
        }

        /// <summary>
        /// Reads weapon name from item base address (skips HandsController offset).
        /// </summary>
        private static string ReadWeaponNameFromHandsItem(ulong itemBase)
        {
            try
            {
                var itemTemp = Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
                if (!MemDMA.IsValidVirtualAddress(itemTemp))
                    return null;

                var mongoId = Memory.ReadValue<MongoID>(itemTemp + Offsets.ItemTemplate._id, false);
                var itemId = mongoId.ReadString(64, false);

                if (string.IsNullOrEmpty(itemId) || itemId.Length != 24)
                    return null;

                if (TarkovDataManager.AllItems.TryGetValue(itemId, out var item) && item.IsWeapon)
                    return item.ShortName;

                return null;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        internal ObservedPlayer(ulong playerBase) : base(playerBase)
        {
            var localPlayer = Memory.LocalPlayer;
            ArgumentNullException.ThrowIfNull(localPlayer, nameof(localPlayer));
            ObservedPlayerController = Memory.ReadPtr(this + Offsets.ObservedPlayerView.ObservedPlayerController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this, Memory.ReadValue<ulong>(ObservedPlayerController + Offsets.ObservedPlayerController.PlayerView), nameof(ObservedPlayerController));
            InventoryControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.InventoryController;
            ObservedHealthController = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.HealthController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this, Memory.ReadValue<ulong>(ObservedHealthController + Offsets.ObservedHealthController._player), nameof(ObservedHealthController));
            CorpseAddr = ObservedHealthController + Offsets.ObservedHealthController._playerCorpse;

            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.ObservedPlayerStateContext.Rotation);
            /// Setup Transform
            var ti = Memory.ReadPtrChain(this, false, _transformInternalChain);
            SkeletonRoot = new UnityTransform(ti);
            var initialPos = SkeletonRoot.UpdatePosition();
            SetupBones();
            // Initialize cached position for fallback (in case skeleton updates fail later)
            _cachedPosition = initialPos;

            bool isAI = Memory.ReadValue<bool>(this + Offsets.ObservedPlayerView.IsAI);
            IsHuman = !isAI;
            Profile = new PlayerProfile(this, GetAccountID());
            // Get Group ID - temporarily disabled, needs investigation for online players
            GroupID = -1;
            /// Determine Player Type
            PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Side); // Usec,Bear,Scav,etc.
            if (!Enum.IsDefined(PlayerSide)) // Make sure PlayerSide is valid
                throw new ArgumentOutOfRangeException(nameof(PlayerSide));

            // Debug logging for player detection
            DebugLogger.LogDebug($"[PlayerDetect] ObservedPlayer: Base=0x{(ulong)this:X}, IsAI={isAI}, PlayerSide={PlayerSide} ({(int)PlayerSide}), IsScav={IsScav}, IsPmc={IsPmc}");

            if (IsScav)
            {
                if (isAI)
                {
                    // Try SpawnType first (works reliably in both online and offline modes)
                    var spawnType = ReadSpawnType();
                    DebugLogger.LogDebug($"[PlayerDetect] AI Scav SpawnType={spawnType} ({(int)spawnType})");
                    if (spawnType != Enums.ESpawnType.UNKNOWN)
                    {
                        var role = GetAIRoleFromSpawnType(spawnType);
                        Name = role.Name;
                        Type = role.Type;
                        DebugLogger.LogDebug($"[PlayerDetect] Role from SpawnType: Name='{role.Name}', Type={role.Type}");
                    }
                    else
                    {
                        // Fallback to voice parsing
                        var voicePtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.Voice);
                        string voice = Memory.ReadUnicodeString(voicePtr);
                        var role = GetAIRoleInfo(voice);
                        Name = role.Name;
                        Type = role.Type;
                        DebugLogger.LogDebug($"[PlayerDetect] Role from voice '{voice}': Name='{role.Name}', Type={role.Type}");
                    }
                }
                else
                {
                    Name = $"PScav{GetPlayerId()}";
                    Type = GroupID != -1 && GroupID == localPlayer.GroupID ?
                        PlayerType.Teammate : PlayerType.PScav;
                    DebugLogger.LogDebug($"[PlayerDetect] Player Scav: Name='{Name}', Type={Type}");
                }
            }
            else if (IsPmc)
            {
                Name = $"PMC{GetPlayerId()}";
                Type = GroupID != -1 && GroupID == localPlayer.GroupID ?
                    PlayerType.Teammate : PlayerType.PMC;
                DebugLogger.LogDebug($"[PlayerDetect] PMC: Name='{Name}', Type={Type}");
            }
            else
            {
                throw new NotImplementedException(nameof(PlayerSide));
            }
            if (IsHuman)
            {
                long.TryParse(AccountID, out long acctIdLong);
                var cache = LocalCache.GetProfileCollection();
                if (cache.FindById(acctIdLong) is EftProfileDto dto &&
                    dto.IsCachedRecent)
                {
                    try
                    {
                        var profileData = dto.ToProfileData();
                        Profile.Data = profileData;
                    }
                    catch
                    {
                        _ = cache.Delete(acctIdLong); // Corrupted cache data, remove it
                        EFTProfileService.RegisterProfile(Profile); // Re-register for lookup
                    }
                }
                else
                {
                    EFTProfileService.RegisterProfile(Profile);
                }
                PlayerHistoryViewModel.Add(this); /// Log To Player History
            }
            if (IsHumanHostile) /// Special Players Check on Hostiles Only
            {
                if (MainWindow.Instance?.PlayerWatchlist?.ViewModel is PlayerWatchlistViewModel vm &&
                    vm.Watchlist.TryGetValue(AccountID, out var watchlistEntry)) // player is on watchlist
                {
                    Type = PlayerType.SpecialPlayer; // Flag watchlist player
                    UpdateAlerts($"[Watchlist] {watchlistEntry.Reason} @ {watchlistEntry.Timestamp}");
                }
            }
            Equipment = new PlayerEquipment(this);
        }

        /// <summary>
        /// Get Player's Account ID.
        /// </summary>
        /// <returns>Account ID Numeric String.</returns>
        private string GetAccountID()
        {
            if (!IsHuman)
                return "AI";
            var idPTR = Memory.ReadPtr(this + Offsets.ObservedPlayerView.AccountId);
            return Memory.ReadUnicodeString(idPTR);
        }

        /// <summary>
        /// Gets player's Group Number.
        /// </summary>
        private int GetGroupNumber()
        {
            try
            {
                var groupIdPtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.GroupID);
                string groupId = Memory.ReadUnicodeString(groupIdPtr);
                return _groups.GetOrAdd(
                    groupId,
                    _ => Interlocked.Increment(ref _lastGroupNumber));
            }
            catch { return -1; } // will return null if Solo / Don't have a team
        }

        /// <summary>
        /// Gets the player's unique in-memory ID.
        /// </summary>
        /// <returns>Player ID or 0 if read fails.</returns>
        private int GetPlayerId()
        {
            try
            {
                return Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Id);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Reads the SpawnType enum from memory via AIData -> BotOwner -> SpawnProfileData chain.
        /// Works in both online and offline modes for reliable boss/raider detection.
        /// </summary>
        /// <returns>ESpawnType enum value, or UNKNOWN if reading fails.</returns>
        private Enums.ESpawnType ReadSpawnType()
        {
            try
            {
                var aiData = Memory.ReadPtr(this + Offsets.ObservedPlayerView.AIData);
                DebugLogger.LogDebug($"[PlayerDetect] ReadSpawnType: AIData=0x{aiData:X}");
                if (aiData == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var botOwner = Memory.ReadPtr(aiData + Offsets.AIData.BotOwner);
                DebugLogger.LogDebug($"[PlayerDetect] ReadSpawnType: BotOwner=0x{botOwner:X}");
                if (botOwner == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var spawnProfileData = Memory.ReadPtr(botOwner + Offsets.BotOwner.SpawnProfileData);
                DebugLogger.LogDebug($"[PlayerDetect] ReadSpawnType: SpawnProfileData=0x{spawnProfileData:X}");
                if (spawnProfileData == 0)
                    return Enums.ESpawnType.UNKNOWN;

                var spawnTypeValue = Memory.ReadValue<uint>(spawnProfileData + Offsets.SpawnProfileData.SpawnType);
                DebugLogger.LogDebug($"[PlayerDetect] ReadSpawnType: Raw value={spawnTypeValue}");

                // Validate that it's a known enum value
                if (Enum.IsDefined(typeof(Enums.ESpawnType), spawnTypeValue))
                    return (Enums.ESpawnType)spawnTypeValue;

                return Enums.ESpawnType.UNKNOWN;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[PlayerDetect] ReadSpawnType exception: {ex.Message}");
                return Enums.ESpawnType.UNKNOWN;
            }
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            var movementController = Memory.ReadPtrChain(ObservedPlayerController, true, Offsets.ObservedPlayerController.MovementController, Offsets.ObservedMovementController.ObservedPlayerStateContext);
            return movementController;
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
        /// Refresh Player Information.
        /// </summary>
        public override void OnRegRefresh(VmmScatter scatter, ISet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                UpdateHealthStatus();
            }
            base.OnRegRefresh(scatter, registered, isActive);
        }

        /// <summary>
        /// Get Player's Updated Health Condition
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateHealthStatus()
        {
            try
            {
                var tag = (Enums.ETagStatus)Memory.ReadValue<int>(ObservedHealthController + Offsets.ObservedHealthController.HealthStatus);
                if ((tag & Enums.ETagStatus.Dying) == Enums.ETagStatus.Dying)
                    HealthStatus = Enums.ETagStatus.Dying;
                else if ((tag & Enums.ETagStatus.BadlyInjured) == Enums.ETagStatus.BadlyInjured)
                    HealthStatus = Enums.ETagStatus.BadlyInjured;
                else if ((tag & Enums.ETagStatus.Injured) == Enums.ETagStatus.Injured)
                    HealthStatus = Enums.ETagStatus.Injured;
                else
                    HealthStatus = Enums.ETagStatus.Healthy;
            }
            catch { } // Ignore health status errors
        }

        private static readonly uint[] _transformInternalChain =
        [
            Offsets.ObservedPlayerView.PlayerBody,
            Offsets.PlayerBody.SkeletonRootJoint,
            Offsets.DizSkinningSkeleton._values,
            UnityList<byte>.ArrOffset,
            UnityList<byte>.ArrStartOffset + (uint)Bones.HumanBase * 0x8,
            0x10
        ];
    }
}
