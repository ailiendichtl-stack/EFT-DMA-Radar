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

using Collections.Pooled;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Misc;
using LoneEftDmaRadar.UI.Radar.Maps;
using LoneEftDmaRadar.UI.Radar.ViewModels;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using VmmSharpEx.Scatter;
using static LoneEftDmaRadar.Tarkov.Unity.Structures.UnityTransform;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Player
{
    /// <summary>
    /// Base class for Tarkov Players.
    /// Tarkov implements several distinct classes that implement a similar player interface.
    /// </summary>
    public abstract class AbstractPlayer : IWorldEntity, IMapEntity, IMouseoverEntity
    {
        #region Static Interfaces

        public static implicit operator ulong(AbstractPlayer x) => x.Base;
        protected static readonly ConcurrentDictionary<string, int> _groups = new(StringComparer.OrdinalIgnoreCase);
        protected static int _lastGroupNumber;
        protected static int _lastPscavNumber;

        static AbstractPlayer()
        {
            MemDMA.RaidStopped += MemDMA_RaidStopped;
        }

        private static void MemDMA_RaidStopped(object sender, EventArgs e)
        {
            _groups.Clear();
            _lastGroupNumber = default;
            _lastPscavNumber = default;
        }

        #endregion

        #region Cached Skia Paths

        private static readonly SKPath _playerPill = CreatePlayerPillBase();
        private static readonly SKPath _deathMarker = CreateDeathMarkerPath();
        private const float PP_LENGTH = 9f;
        private const float PP_RADIUS = 3f;
        private const float PP_HALF_HEIGHT = PP_RADIUS * 0.85f;
        private const float PP_NOSE_X = PP_LENGTH / 2f + PP_RADIUS * 0.18f;

        private static SKPath CreatePlayerPillBase()
        {
            var path = new SKPath();

            // Rounded back (left side)
            var backRect = new SKRect(-PP_LENGTH / 2f, -PP_HALF_HEIGHT, -PP_LENGTH / 2f + PP_RADIUS * 2f, PP_HALF_HEIGHT);
            path.AddArc(backRect, 90, 180);

            // Pointed nose (right side)
            float backFrontX = -PP_LENGTH / 2f + PP_RADIUS;

            float c1X = backFrontX + PP_RADIUS * 1.1f;
            float c2X = PP_NOSE_X - PP_RADIUS * 0.28f;
            float c1Y = -PP_HALF_HEIGHT * 0.55f;
            float c2Y = -PP_HALF_HEIGHT * 0.3f;

            path.CubicTo(c1X, c1Y, c2X, c2Y, PP_NOSE_X, 0f);
            path.CubicTo(c2X, -c2Y, c1X, -c1Y, backFrontX, PP_HALF_HEIGHT);

            path.Close();
            return path;
        }

        private static SKPath CreateDeathMarkerPath()
        {
            const float length = 6f;
            var path = new SKPath();

            path.MoveTo(-length, length);
            path.LineTo(length, -length);
            path.MoveTo(-length, -length);
            path.LineTo(length, length);

            return path;
        }

        #endregion

        #region Allocation

        /// <summary>
        /// Allocates a player.
        /// </summary>
        /// <param name="regPlayers">Player Dictionary collection to add the newly allocated player to.</param>
        /// <param name="playerBase">Player base memory address.</param>
        public static void Allocate(ConcurrentDictionary<ulong, AbstractPlayer> regPlayers, ulong playerBase)
        {
            try
            {
                _ = regPlayers.GetOrAdd(
                    playerBase,
                    addr => AllocateInternal(addr));
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"ERROR during Player Allocation for player @ 0x{playerBase.ToString("X")}: {ex}");
            }
        }

        private static AbstractPlayer AllocateInternal(ulong playerBase)
        {
            SpawnDebugLogger.Log($"=== AllocateInternal START: playerBase=0x{playerBase:X} ===");
            AbstractPlayer player;
            var className = ObjectClass.ReadName(playerBase, 64);
            var isClientPlayer = className == "ClientPlayer" || className == "LocalPlayer";

            SpawnDebugLogger.Log($"  className='{className}', isClientPlayer={isClientPlayer}");

            if (isClientPlayer)
                player = new ClientPlayer(playerBase);
            else
                player = new ObservedPlayer(playerBase);
            SpawnDebugLogger.Log($"  Player allocated: Name='{player.Name}', Type={player.Type}");
            DebugLogger.LogDebug($"Player '{player.Name}' allocated.");
            return player;
        }

        /// <summary>
        /// Player Constructor.
        /// </summary>
        protected AbstractPlayer(ulong playerBase)
        {
            ArgumentOutOfRangeException.ThrowIfZero(playerBase, nameof(playerBase));
            Base = playerBase;
        }

        #endregion

        #region Fields / Properties
        /// <summary>
        /// Player Class Base Address
        /// </summary>
        public ulong Base { get; }

        /// <summary>
        /// True if the Player is Active (in the player list).
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Type of player unit.
        /// </summary>
        public virtual PlayerType Type { get; protected set; }

        private Vector2 _rotation;
        /// <summary>
        /// Player's Rotation in Local Game World.
        /// </summary>
        public Vector2 Rotation
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _rotation;
            private set
            {
                _rotation = value;
                float mapRotation = value.X; // Cache value
                mapRotation -= 90f;
                while (mapRotation < 0f)
                    mapRotation += 360f;
                MapRotation = mapRotation;
            }
        }

        /// <summary>
        /// Player's Map Rotation (with 90 degree correction applied).
        /// </summary>
        public float MapRotation { get; private set; }

        /// <summary>
        /// Corpse field value.
        /// </summary>
        public ulong? Corpse { get; private set; }

        /// <summary>
        /// Player's Skeleton Root.
        /// </summary>
        public UnityTransform SkeletonRoot { get; protected set; }

        /// <summary>
        /// Dictionary of Player Bones.
        /// </summary>
        public ConcurrentDictionary<Bones, UnityTransform> PlayerBones { get; } = new();
        /// <summary>
        /// Lightweight wrapper for skeleton access (used by DeviceAimbot/silent aim features).
        /// </summary>
        public PlayerSkeleton Skeleton { get; protected set; }
        protected int _verticesCount;
        private bool _skeletonErrorLogged;
        protected Vector3 _cachedPosition; // Fallback position cache

        /// <summary>
        /// TRUE if critical memory reads (position/rotation) have failed.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Timer to track how long player has been in error state.
        /// Used to trigger re-allocation if errors persist.
        /// </summary>
        public Stopwatch ErrorTimer { get; } = new Stopwatch();

        /// <summary>
        /// Reset a specific bone transform using its internal address.
        /// From Pre-1.0 fork
        /// </summary>
        /// <param name="bone">The bone to reset</param>
        private void ResetBoneTransform(Bones bone)
        {
            try
            {
                if (PlayerBones.TryGetValue(bone, out var boneTransform))
                {
                    DebugLogger.LogDebug($"Resetting transform for bone '{bone}' for Player '{Name ?? "Unknown"}'");
                    PlayerBones[bone] = new UnityTransform(boneTransform.TransformInternal);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"Failed to reset bone '{bone}' transform for Player '{Name ?? "Unknown"}': {ex}");
            }
        }

        /// <summary>
        /// True if player is being focused via Right-Click (UI).
        /// </summary>
        public bool IsFocused { get; set; }

        /// <summary>
        /// Dead Player's associated loot container object.
        /// </summary>
        public LootCorpse LootObject { get; set; }
        /// <summary>
        /// Alerts for this Player Object.
        /// Used by Player History UI Interop.
        /// </summary>
        public virtual string Alerts { get; protected set; }

        #endregion

        #region Virtual Properties

        /// <summary>
        /// Player name.
        /// </summary>
        public virtual string Name { get; set; }

        /// <summary>
        /// Account UUID for Human Controlled Players.
        /// </summary>
        public virtual string AccountID { get; }

        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public virtual int GroupID { get; protected set; } = -1;

        /// <summary>
        /// Player's Faction.
        /// </summary>
        public virtual Enums.EPlayerSide PlayerSide { get; protected set; }

        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public virtual bool IsHuman { get; protected set; }

        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public virtual ulong MovementContext { get; }

        /// <summary>
        /// Corpse field address..
        /// </summary>
        public virtual ulong CorpseAddr { get; }

        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public virtual ulong RotationAddress { get; }

        /// <summary>
        /// Hands Controller address.
        /// </summary>
        protected ulong HandsController { get; set; }

        /// <summary>
        /// Currently held weapon name (null if unarmed or unreadable).
        /// </summary>
        public virtual string HeldWeaponName { get; protected set; }

        /// <summary>
        /// Display text for weapon (returns "Unarmed" if no weapon).
        /// </summary>
        public string WeaponDisplayText => HeldWeaponName ?? "Unarmed";

        #endregion

        #region Boolean Getters

        /// <summary>
        /// Player is AI-Controlled.
        /// </summary>
        public bool IsAI => !IsHuman;

        /// <summary>
        /// Player is a PMC Operator.
        /// </summary>
        public bool IsPmc => PlayerSide is Enums.EPlayerSide.Usec || PlayerSide is Enums.EPlayerSide.Bear;

        /// <summary>
        /// Player is a SCAV.
        /// </summary>
        public bool IsScav => PlayerSide is Enums.EPlayerSide.Savage;

        /// <summary>
        /// Player is alive (not dead).
        /// </summary>
        public bool IsAlive => Corpse is null;

        /// <summary>
        /// True if Player is Friendly to LocalPlayer.
        /// </summary>
        public bool IsFriendly =>
            this is LocalPlayer || Type is PlayerType.Teammate;

        /// <summary>
        /// True if player is Hostile to LocalPlayer.
        /// </summary>
        public bool IsHostile => !IsFriendly;

        /// <summary>
        /// Player is Alive/Active and NOT LocalPlayer.
        /// </summary>
        public bool IsNotLocalPlayerAlive =>
            this is not LocalPlayer && IsActive && IsAlive;

        /// <summary>
        /// Player is a Hostile PMC Operator.
        /// </summary>
        public bool IsHostilePmc => IsPmc && IsHostile;

        /// <summary>
        /// Player is human-controlled (Not LocalPlayer).
        /// </summary>
        public bool IsHumanOther => IsHuman && this is not LocalPlayer;

        /// <summary>
        /// Player is AI Controlled and Alive/Active.
        /// </summary>
        public bool IsAIActive => IsAI && IsActive && IsAlive;

        /// <summary>
        /// Player is AI Controlled and Alive/Active & their AI Role is default.
        /// </summary>
        public bool IsDefaultAIActive => IsAI && Name == "defaultAI" && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled and Active/Alive.
        /// </summary>
        public bool IsHumanActive =>
            IsHuman && IsActive && IsAlive;

        /// <summary>
        /// Player is hostile and alive/active.
        /// </summary>
        public bool IsHostileActive => IsHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is human-controlled & Hostile.
        /// </summary>
        public bool IsHumanHostile => IsHuman && IsHostile;

        /// <summary>
        /// Player is human-controlled, hostile, and Active/Alive.
        /// </summary>
        public bool IsHumanHostileActive => IsHumanHostile && IsActive && IsAlive;

        /// <summary>
        /// Player is friendly to LocalPlayer (including LocalPlayer) and Active/Alive.
        /// </summary>
        public bool IsFriendlyActive => IsFriendly && IsActive && IsAlive;

        /// <summary>
        /// Player has exfil'd/left the raid.
        /// </summary>
        public bool HasExfild => !IsActive && IsAlive;

        #endregion

        #region Methods

        /// <summary>
        /// Attempts to read weapon name from a HandsController address.
        /// </summary>
        /// <param name="handsController">HandsController memory address.</param>
        /// <param name="playerName">Player name for debug logging.</param>
        /// <returns>Weapon short name, or null if not a weapon/unarmed.</returns>
        protected static string ReadWeaponNameFromHands(ulong handsController, string playerName = null)
        {
            try
            {
                if (!MemDMA.IsValidVirtualAddress(handsController))
                {
                    DebugLogger.LogDebug($"[Weapon] {playerName}: HandsController invalid (0x{handsController:X})");
                    return null;
                }

                var itemBase = Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
                if (!MemDMA.IsValidVirtualAddress(itemBase))
                {
                    DebugLogger.LogDebug($"[Weapon] {playerName}: Item pointer invalid (0x{itemBase:X})");
                    return null;
                }

                var itemTemp = Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
                if (!MemDMA.IsValidVirtualAddress(itemTemp))
                {
                    DebugLogger.LogDebug($"[Weapon] {playerName}: Template pointer invalid");
                    return null;
                }

                var mongoId = Memory.ReadValue<MongoID>(itemTemp + Offsets.ItemTemplate._id, false);
                var itemId = mongoId.ReadString(64, false);

                if (string.IsNullOrEmpty(itemId) || itemId.Length != 24)
                {
                    DebugLogger.LogDebug($"[Weapon] {playerName}: Invalid item ID '{itemId}'");
                    return null;
                }

                if (TarkovDataManager.AllItems.TryGetValue(itemId, out var item))
                {
                    if (item.IsWeapon)
                    {
                        DebugLogger.LogDebug($"[Weapon] {playerName}: Found weapon '{item.ShortName}' (ID: {itemId})");
                        return item.ShortName;
                    }
                    else
                    {
                        DebugLogger.LogDebug($"[Weapon] {playerName}: Holding non-weapon '{item.ShortName}'");
                        return null;
                    }
                }

                DebugLogger.LogDebug($"[Weapon] {playerName}: Item ID not in database: {itemId}");
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[Weapon] {playerName}: Exception - {ex.Message}");
                return null;
            }
        }

        private readonly Lock _alertsLock = new();
        /// <summary>
        /// Update the Alerts for this Player Object.
        /// </summary>
        /// <param name="alert">Alert to set.</param>
        public void UpdateAlerts(string alert)
        {
            if (alert is null)
                return;
            lock (_alertsLock)
            {
                if (Alerts is null)
                    Alerts = alert;
                else
                    Alerts = $"{alert} | {Alerts}";
            }
        }

        /// <summary>
        /// Validates the Rotation Address.
        /// </summary>
        /// <param name="rotationAddr">Rotation va</param>
        /// <returns>Validated rotation virtual address.</returns>
        protected static ulong ValidateRotationAddr(ulong rotationAddr)
        {
            var rotation = Memory.ReadValue<Vector2>(rotationAddr, false);
            if (!rotation.IsNormalOrZero() ||
                Math.Abs(rotation.X) > 360f ||
                Math.Abs(rotation.Y) > 90f)
                throw new ArgumentOutOfRangeException(nameof(rotationAddr));

            return rotationAddr;
        }

        /// <summary>
        /// Refreshes non-realtime player information. Call in the Registered Players Loop (T0).
        /// </summary>
        /// <param name="scatter"></param>
        /// <param name="registered"></param>
        /// <param name="isActiveParam"></param>
        public virtual void OnRegRefresh(VmmScatter scatter, ISet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                SetAlive();
            }
            else if (IsAlive) // Not in list, but alive
            {
                scatter.PrepareReadPtr(CorpseAddr);
                scatter.Completed += (sender, x1) =>
                {
                    if (x1.ReadPtr(CorpseAddr, out var corpsePtr))
                        SetDead(corpsePtr);
                    else
                        SetExfild();
                };
            }
        }

        /// <summary>
        /// Mark player as dead.
        /// </summary>
        /// <param name="corpse">Corpse address.</param>
        public void SetDead(ulong corpse)
        {
            Corpse = corpse;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as exfil'd.
        /// </summary>
        private void SetExfild()
        {
            Corpse = null;
            IsActive = false;
        }

        /// <summary>
        /// Mark player as alive.
        /// </summary>
        private void SetAlive()
        {
            Corpse = null;
            LootObject = null;
            IsActive = true;
        }

        /// <summary>
        /// Executed on each Realtime Loop.
        /// </summary>
        /// <param name="index">Scatter read index dedicated to this player.</param>
        public virtual void OnRealtimeLoop(VmmScatter scatter)
        {
            if (SkeletonRoot == null)
            {
                IsError = true;
                return;
            }

            int vertexCount = SkeletonRoot.Count;

            // Calculate actual vertex requirements including all bones
            int maxBoneRequirement = 0;
            foreach (var bone in PlayerBones.Values)
            {
                if (bone.Count > maxBoneRequirement)
                    maxBoneRequirement = bone.Count;
            }

            int actualRequired = Math.Max(vertexCount, maxBoneRequirement);

            if (actualRequired <= 0 || actualRequired > 10000)
            {
                try
                {
                    DebugLogger.LogDebug($"Invalid vertex count detected for '{Name}': {actualRequired} (skeleton: {vertexCount}, bones: {maxBoneRequirement})");

                    // Resetting all bone transforms
                    foreach (var bone in PlayerBones.Keys.ToList())
                    {
                        ResetBoneTransform(bone);
                    }

                    Skeleton = new PlayerSkeleton(SkeletonRoot, PlayerBones);
                    DebugLogger.LogDebug($"Fast skeleton recovery for Player '{Name}' - vertexCount was {actualRequired}");

                    vertexCount = SkeletonRoot.Count;
                    maxBoneRequirement = 0;
                    foreach (var bone in PlayerBones.Values)
                    {
                        if (bone.Count > maxBoneRequirement)
                            maxBoneRequirement = bone.Count;
                    }
                    actualRequired = Math.Max(vertexCount, maxBoneRequirement);
                }
                catch (Exception ex)
                {
                    DebugLogger.LogDebug($"ERROR in fast skeleton recovery for '{Name}': {ex}");
                }

                // If still bad after recovery attempt, skip this frame
                if (actualRequired <= 0 || actualRequired > 10000)
                {
                    IsError = true;
                    _verticesCount = 0;
                    return;
                }
            }

            scatter.PrepareReadValue<Vector2>(RotationAddress); // Rotation
            int requestedVertices = _verticesCount > 0 ? _verticesCount : actualRequired;
            scatter.PrepareReadArray<TrsX>(SkeletonRoot.VerticesAddr, requestedVertices); // ESP Vertices

            scatter.Completed += (sender, s) =>
            {
                bool successRot = s.ReadValue<Vector2>(RotationAddress, out var rotation) && SetRotation(rotation);
                bool successPos = false;

                if (s.ReadArray<TrsX>(SkeletonRoot.VerticesAddr, requestedVertices) is PooledMemory<TrsX> vertices)
                {
                    using (vertices)
                    {
                        try
                        {
                            if (vertices.Span.Length >= requestedVertices)
                            {
                                try
                                {
                                    _ = SkeletonRoot.UpdatePosition(vertices.Span);
                                    successPos = true;
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.LogDebug($"ERROR updating skeleton root for '{Name}': {ex}");
                                    successPos = false;
                                    return;
                                }

                                foreach (var bonePair in PlayerBones)
                                {
                                    try
                                    {
                                        if (bonePair.Value.Count <= vertices.Span.Length)
                                        {
                                            bonePair.Value.UpdatePosition(vertices.Span);
                                        }
                                        else
                                        {
                                            DebugLogger.LogDebug($"Bone '{bonePair.Key}' needs {bonePair.Value.Count} vertices but only {vertices.Span.Length} available for '{Name}'");
                                            ResetBoneTransform(bonePair.Key);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLogger.LogDebug($"ERROR updating bone '{bonePair.Key}' for '{Name}': {ex}");
                                        ResetBoneTransform(bonePair.Key);
                                    }
                                }

                                _cachedPosition = SkeletonRoot.Position;

                                if (_skeletonErrorLogged)
                                {
                                    DebugLogger.LogDebug($"Skeleton update successful for Player '{Name}'");
                                    _skeletonErrorLogged = false;
                                }
                            }
                            else
                            {
                                DebugLogger.LogDebug($"Insufficient vertices for '{Name}': got {vertices.Span.Length}, expected {requestedVertices}");
                                _verticesCount = 0;
                                successPos = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.LogDebug($"ERROR updating skeleton position for '{Name}': {ex}");
                            successPos = false;
                        }
                    }
                }
                else
                {
                    successPos = false;
                }

                bool hasError = !successRot || !successPos;
                if (hasError && !IsError)
                {
                    ErrorTimer.Restart();
                }
                else if (!hasError && IsError)
                {
                    ErrorTimer.Stop();
                    ErrorTimer.Reset();
                }

                IsError = hasError;
            };
        }

        /// <summary>
        /// Override this method to validate custom transforms.
        /// </summary>
        public virtual void OnValidateTransforms()
        {
        }

        /// <summary>
        /// Executed on each Transform Validation Loop.
        /// </summary>
        /// <param name="round1">Index (round 1)</param>
        /// <param name="round2">Index (round 2)</param>
        public void OnValidateTransforms(VmmScatter round1, VmmScatter round2)
        {
            round1.PrepareReadPtr(SkeletonRoot.TransformInternal + UnitySDK.UnityOffsets.TransformAccess_HierarchyOffset); // Bone Hierarchy
            round1.Completed += (sender, x1) =>
            {
                if (x1.ReadPtr(SkeletonRoot.TransformInternal + UnitySDK.UnityOffsets.TransformAccess_HierarchyOffset, out var tra))
                {
                    round2.PrepareReadPtr(tra + UnitySDK.UnityOffsets.Hierarchy_VerticesOffset); // Vertices Ptr
                    round2.Completed += (sender, x2) =>
                    {
                        if (x2.ReadPtr(tra + UnitySDK.UnityOffsets.Hierarchy_VerticesOffset, out var verticesPtr))
                        {
                            if (SkeletonRoot.VerticesAddr != verticesPtr) // check if any addr changed
                            {
                                DebugLogger.LogDebug($"WARNING - SkeletonRoot Transform has changed for Player '{Name}'");
                                var transform = new UnityTransform(SkeletonRoot.TransformInternal);
                                SkeletonRoot = transform;
                                _verticesCount = 0; // force fresh vertex count on next read

                                // IMPORTANT: also rebuild all bone transforms and skeleton wrapper
                                try
                                {
                                    foreach (var bone in PlayerBones.Keys.ToList())
                                    {
                                        ResetBoneTransform(bone);
                                    }

                                    Skeleton = new PlayerSkeleton(SkeletonRoot, PlayerBones);
                                    DebugLogger.LogDebug($"Skeleton rebuilt for Player '{Name}'");
                                }
                                catch (Exception ex)
                                {
                                    DebugLogger.LogDebug($"ERROR rebuilding skeleton for '{Name}': {ex}");
                                }
                            }
                        }
                    };
                }
            };
        }

        /// <summary>
        /// Set player rotation (Direction/Pitch)
        /// </summary>
        protected virtual bool SetRotation(Vector2 rotation)
        {
            try
            {
                rotation.ThrowIfAbnormalAndNotZero(nameof(rotation));
                rotation.X = rotation.X.NormalizeAngle();
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.X, 0f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.X, 360f);
                ArgumentOutOfRangeException.ThrowIfLessThan(rotation.Y, -90f);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(rotation.Y, 90f);
                Rotation = rotation;
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region AI Player Types

        public readonly struct AIRole
        {
            public readonly string Name { get; init; }
            public readonly PlayerType Type { get; init; }
        }

        /// <summary>
        /// Lookup AI Info based on SpawnType enum (works in both online and offline modes).
        /// </summary>
        /// <param name="spawnType">The spawn type read from memory.</param>
        /// <returns>AIRole with name and type.</returns>
        public static AIRole GetAIRoleFromSpawnType(Enums.ESpawnType spawnType)
        {
            return spawnType switch
            {
                // Bosses
                Enums.ESpawnType.Reshala => new AIRole { Name = "Reshala", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Killa or Enums.ESpawnType.BossKillaAgro => new AIRole { Name = "Killa", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Gluhar => new AIRole { Name = "Gluhar", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Sanitar => new AIRole { Name = "Sanitar", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Shturman => new AIRole { Name = "Shturman", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Tagilla or Enums.ESpawnType.BossTagillaAgro or Enums.ESpawnType.InfectedTagilla => new AIRole { Name = "Tagilla", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Knight => new AIRole { Name = "Knight", Type = PlayerType.AIBoss },
                Enums.ESpawnType.BigPipe => new AIRole { Name = "Big Pipe", Type = PlayerType.AIBoss },
                Enums.ESpawnType.BirdEye => new AIRole { Name = "Birdeye", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Zryachiy => new AIRole { Name = "Zryachiy", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Kaban => new AIRole { Name = "Kaban", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Kolontay => new AIRole { Name = "Kollontay", Type = PlayerType.AIBoss },
                Enums.ESpawnType.Partisan => new AIRole { Name = "Partisan", Type = PlayerType.AIBoss },
                Enums.ESpawnType.SectantPriest or Enums.ESpawnType.SectactPriestEvent => new AIRole { Name = "Priest", Type = PlayerType.AIBoss },

                // Cultists/Sectants
                Enums.ESpawnType.SectantWarrior => new AIRole { Name = "Cultist", Type = PlayerType.AIRaider },
                Enums.ESpawnType.SectantPredvestnik or Enums.ESpawnType.SectantPrizrak or Enums.ESpawnType.SectantOni => new AIRole { Name = "Cultist", Type = PlayerType.AIRaider },

                // Rogues/ExUSEC
                Enums.ESpawnType.ExUsec => new AIRole { Name = "Rogue", Type = PlayerType.AIRaider },

                // Raiders (PMC Bots)
                Enums.ESpawnType.PmcBot or Enums.ESpawnType.PmcBEAR or Enums.ESpawnType.PmcUSEC => new AIRole { Name = "Raider", Type = PlayerType.AIRaider },

                // Arena fighters
                Enums.ESpawnType.ArenaFighter or Enums.ESpawnType.ArenaFighterEvent => new AIRole { Name = "Arena Fighter", Type = PlayerType.AIRaider },

                // Black Division / VSRF
                Enums.ESpawnType.BlackDivision => new AIRole { Name = "Black Division", Type = PlayerType.AIRaider },
                Enums.ESpawnType.VsRF or Enums.ESpawnType.VsRFSniper or Enums.ESpawnType.VsRFFight => new AIRole { Name = "VSRF", Type = PlayerType.AIRaider },

                // BTR Shooter
                Enums.ESpawnType.ShooterBTR => new AIRole { Name = "BTR Gunner", Type = PlayerType.AIRaider },

                // Boss Followers / Guards
                Enums.ESpawnType.FollowerBully => new AIRole { Name = "Reshala Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerSanitar => new AIRole { Name = "Sanitar Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerTagilla or Enums.ESpawnType.TagillaHelperAgro => new AIRole { Name = "Tagilla Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerKojaniy => new AIRole { Name = "Shturman Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerZryachiy => new AIRole { Name = "Zryachiy Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerBoar or Enums.ESpawnType.FollowerBoarClose1 or Enums.ESpawnType.FollowerBoarClose2 or Enums.ESpawnType.BossBoarSniper => new AIRole { Name = "Kaban Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerKolontayAssault or Enums.ESpawnType.FollowerKolontaySecurity => new AIRole { Name = "Kollontay Guard", Type = PlayerType.AIRaider },
                Enums.ESpawnType.FollowerGluharAssault or Enums.ESpawnType.FollowerGluharSecurity or Enums.ESpawnType.FollowerGluharScout or Enums.ESpawnType.FollowerGluharSnipe => new AIRole { Name = "Gluhar Guard", Type = PlayerType.AIRaider },

                // Infected / Zombies
                Enums.ESpawnType.InfectedAssault or Enums.ESpawnType.InfectedPmc or Enums.ESpawnType.InfectedCivil or Enums.ESpawnType.InfectedLaborant => new AIRole { Name = "Zombie", Type = PlayerType.AIScav },

                // Special
                Enums.ESpawnType.Gifter => new AIRole { Name = "Santa", Type = PlayerType.AIScav },
                Enums.ESpawnType.SpiritWinter or Enums.ESpawnType.SpiritSpring => new AIRole { Name = "Spirit", Type = PlayerType.AIScav },
                Enums.ESpawnType.Peacemaker => new AIRole { Name = "Peacemaker", Type = PlayerType.AIScav },
                Enums.ESpawnType.Sentry => new AIRole { Name = "Sentry", Type = PlayerType.AIRaider },
                Enums.ESpawnType.Civilian => new AIRole { Name = "Civilian", Type = PlayerType.AIScav },

                // Default scavs (marksman, assault, cursedAssault, etc.)
                _ => new AIRole { Name = "Scav", Type = PlayerType.AIScav }
            };
        }

        /// <summary>
        /// Lookup AI Info based on Voice Line.
        /// </summary>
        /// <param name="voiceLine"></param>
        /// <returns></returns>
        public static AIRole GetAIRoleInfo(string voiceLine)
        {
            switch (voiceLine)
            {
                case "BossSanitar":
                    return new AIRole
                    {
                        Name = "Sanitar",
                        Type = PlayerType.AIBoss
                    };
                case "BossBully":
                    return new AIRole
                    {
                        Name = "Reshala",
                        Type = PlayerType.AIBoss
                    };
                case "BossGluhar":
                    return new AIRole
                    {
                        Name = "Gluhar",
                        Type = PlayerType.AIBoss
                    };
                case "SectantPriest":
                    return new AIRole
                    {
                        Name = "Priest",
                        Type = PlayerType.AIBoss
                    };
                case "SectantWarrior":
                    return new AIRole
                    {
                        Name = "Cultist",
                        Type = PlayerType.AIRaider
                    };
                case "BossKilla":
                    return new AIRole
                    {
                        Name = "Killa",
                        Type = PlayerType.AIBoss
                    };
                case "BossTagilla":
                    return new AIRole
                    {
                        Name = "Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Partizan":
                    return new AIRole
                    {
                        Name = "Partisan",
                        Type = PlayerType.AIBoss
                    };
                case "BossBigPipe":
                    return new AIRole
                    {
                        Name = "Big Pipe",
                        Type = PlayerType.AIBoss
                    };
                case "BossBirdEye":
                    return new AIRole
                    {
                        Name = "Birdeye",
                        Type = PlayerType.AIBoss
                    };
                case "BossKnight":
                    return new AIRole
                    {
                        Name = "Knight",
                        Type = PlayerType.AIBoss
                    };
                case "Arena_Guard_1":
                    return new AIRole
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Arena_Guard_2":
                    return new AIRole
                    {
                        Name = "Arena Guard",
                        Type = PlayerType.AIScav
                    };
                case "Boss_Kaban":
                    return new AIRole
                    {
                        Name = "Kaban",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Kollontay":
                    return new AIRole
                    {
                        Name = "Kollontay",
                        Type = PlayerType.AIBoss
                    };
                case "Boss_Sturman":
                    return new AIRole
                    {
                        Name = "Shturman",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Generic":
                    return new AIRole
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "BossZombieTagilla":
                    return new AIRole
                    {
                        Name = "Zombie Tagilla",
                        Type = PlayerType.AIBoss
                    };
                case "Zombie_Fast":
                    return new AIRole
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
                case "Zombie_Medium":
                    return new AIRole
                    {
                        Name = "Zombie",
                        Type = PlayerType.AIScav
                    };
            }

            // Nickname detection for offline mode (bosses have specific nicknames)
            if (!string.IsNullOrEmpty(voiceLine))
            {
                AIRole? nicknameMatch = voiceLine.ToLowerInvariant() switch
                {
                    // Russian boss nicknames
                    "решала" => new AIRole { Name = "Reshala", Type = PlayerType.AIBoss },
                    "санитар" => new AIRole { Name = "Sanitar", Type = PlayerType.AIBoss },
                    "глухарь" => new AIRole { Name = "Gluhar", Type = PlayerType.AIBoss },
                    "килла" => new AIRole { Name = "Killa", Type = PlayerType.AIBoss },
                    "тагилла" => new AIRole { Name = "Tagilla", Type = PlayerType.AIBoss },
                    "штурман" => new AIRole { Name = "Shturman", Type = PlayerType.AIBoss },
                    "кабан" => new AIRole { Name = "Kaban", Type = PlayerType.AIBoss },
                    "партизан" => new AIRole { Name = "Partisan", Type = PlayerType.AIBoss },
                    "коллонтай" or "колонтай" => new AIRole { Name = "Kollontay", Type = PlayerType.AIBoss },
                    "зрячий" => new AIRole { Name = "Zryachiy", Type = PlayerType.AIBoss },
                    "найт" or "рыцарь" => new AIRole { Name = "Knight", Type = PlayerType.AIBoss },
                    "птичий глаз" => new AIRole { Name = "Birdeye", Type = PlayerType.AIBoss },
                    "большая труба" => new AIRole { Name = "Big Pipe", Type = PlayerType.AIBoss },
                    // English boss nicknames (Goons use English names in offline mode)
                    "knight" => new AIRole { Name = "Knight", Type = PlayerType.AIBoss },
                    "birdeye" => new AIRole { Name = "Birdeye", Type = PlayerType.AIBoss },
                    "big pipe" => new AIRole { Name = "Big Pipe", Type = PlayerType.AIBoss },
                    _ => null
                };
                if (nicknameMatch.HasValue)
                    return nicknameMatch.Value;
            }

            // Offline mode voice format parsing (e.g., "bot_Shturman_2456", "boSanitar", etc.)
            if (voiceLine.StartsWith("bot_", StringComparison.OrdinalIgnoreCase) ||
                voiceLine.StartsWith("bo", StringComparison.OrdinalIgnoreCase))
            {
                // Try to extract the boss/AI name from the voice string
                string namePart = voiceLine;
                if (voiceLine.StartsWith("bot_", StringComparison.OrdinalIgnoreCase))
                {
                    // Format: "bot_Shturman_2456" -> extract "Shturman"
                    namePart = voiceLine.Substring(4);
                    var underscoreIdx = namePart.IndexOf('_');
                    if (underscoreIdx > 0)
                        namePart = namePart.Substring(0, underscoreIdx);
                }
                else if (voiceLine.StartsWith("bo", StringComparison.OrdinalIgnoreCase))
                {
                    // Format: "boSanitar" or "bosanitar" -> extract "Sanitar"
                    namePart = voiceLine.Substring(2);
                }

                var lowerName = namePart.ToLowerInvariant();
                return lowerName switch
                {
                    "shturman" or "sturman" => new AIRole { Name = "Shturman", Type = PlayerType.AIBoss },
                    "sanitar" => new AIRole { Name = "Sanitar", Type = PlayerType.AIBoss },
                    "gluhar" => new AIRole { Name = "Gluhar", Type = PlayerType.AIBoss },
                    "reshala" or "bully" => new AIRole { Name = "Reshala", Type = PlayerType.AIBoss },
                    "killa" => new AIRole { Name = "Killa", Type = PlayerType.AIBoss },
                    "tagilla" => new AIRole { Name = "Tagilla", Type = PlayerType.AIBoss },
                    "knight" => new AIRole { Name = "Knight", Type = PlayerType.AIBoss },
                    "bigpipe" => new AIRole { Name = "Big Pipe", Type = PlayerType.AIBoss },
                    "birdeye" => new AIRole { Name = "Birdeye", Type = PlayerType.AIBoss },
                    "kaban" or "boar" => new AIRole { Name = "Kaban", Type = PlayerType.AIBoss },
                    "kolontay" or "kollontay" => new AIRole { Name = "Kollontay", Type = PlayerType.AIBoss },
                    "partisan" => new AIRole { Name = "Partisan", Type = PlayerType.AIBoss },
                    "zryachiy" => new AIRole { Name = "Zryachiy", Type = PlayerType.AIBoss },
                    _ => new AIRole { Name = "Scav", Type = PlayerType.AIScav }
                };
            }

            if (voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase))
                return new AIRole
                {
                    Name = "Scav",
                    Type = PlayerType.AIScav
                };
            if (voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase))
                return new AIRole
                {
                    Name = "Boss",
                    Type = PlayerType.AIBoss
                };
            if (voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase))
                return new AIRole
                {
                    Name = "Usec",
                    Type = PlayerType.AIRaider
                };
            if (voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase))
                return new AIRole
                {
                    Name = "Bear",
                    Type = PlayerType.AIRaider
                };
            DebugLogger.LogDebug($"Unknown Voice Line: {voiceLine}");
            return new AIRole
            {
                Name = "Scav",
                Type = PlayerType.AIScav
            };
        }

        #endregion

        #region Interfaces

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            DrawReference(canvas, mapParams, localPlayer, localPlayer.Position, false);
        }

        /// <summary>
        /// Draw this Entity on the Radar with custom reference position.
        /// </summary>
        public void DrawReference(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer, Vector3 referencePosition, bool isFollowTarget = false)
        {
            try
            {
                var point = Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
                MouseoverPosition = new Vector2(point.X, point.Y);
                if (!IsAlive) // Player Dead -- Draw 'X' death marker and move on
                {
                    DrawDeathMarker(canvas, point);
                }
                else
                {
                    DrawPlayerPill(canvas, localPlayer, point);
                    if (this == localPlayer)
                        return;
                    var height = Position.Y - localPlayer.ReferenceHeight;
                    var refPos = referencePosition;
                    var dist = Vector3.Distance(refPos, Position);
                    var roundedHeight = (int)Math.Round(height);
                    var roundedDist = (int)Math.Round(dist);
                    using var lines = new PooledList<string>();
                    if (!App.Config.UI.HideNames) // show full names & info
                    {
                        string name = null;
                        if (IsError)
                            name = "ERROR"; // In case POS stops updating, let us know!
                        else
                        {
                            var whitelistEntry = App.Config.PlayerWhitelist
                                .FirstOrDefault(w => w.AcctID == AccountID);

                            if (whitelistEntry != null && !string.IsNullOrEmpty(whitelistEntry.CustomName))
                                name = whitelistEntry.CustomName;
                            else
                                name = Name;
                        }
                        string health = null; string level = null;
                        if (this is ObservedPlayer observed)
                        {
                            health = observed.HealthStatus is Enums.ETagStatus.Healthy
                                ? null
                                : $" ({observed.HealthStatus})"; // Only display abnormal health status
                            if (observed.Profile?.Level is int levelResult)
                                level = $"{levelResult}:";
                        }
                        var isWhitelisted = App.Config.PlayerWhitelist
                            .Any(w => w.AcctID == AccountID && !string.IsNullOrEmpty(w.CustomName));

                        if (IsPmc && !isWhitelisted)
                        {
                            char faction = PlayerSide.ToString()[0]; // Get faction letter (U/B)
                            lines.Add($"[{faction}] {name}{health}");
                        }
                        else
                        {
                            lines.Add($"{name}{health}");
                        }

                        if (!isFollowTarget)
                        {
                            lines.Add(roundedHeight != 0 ? $"{roundedDist}M {(roundedHeight > 0 ? $"▲{roundedHeight}" : $"▼{Math.Abs(roundedHeight)}")}" : $"{roundedDist}M");
                        }
                        else
                        {
                            if (roundedHeight != 0)
                                lines.Add(roundedHeight > 0 ? $"▲{roundedHeight}" : $"▼{Math.Abs(roundedHeight)}");
                        }
                    }
                    else // just height, distance
                    {
                        if (!isFollowTarget)
                        {
                            lines.Add(roundedHeight != 0 ? $"{roundedDist}M {(roundedHeight > 0 ? $"▲{roundedHeight}" : $"▼{Math.Abs(roundedHeight)}")}" : $"{roundedDist}M");
                        }
                        else
                        {
                            if (roundedHeight != 0)
                                lines.Add(roundedHeight > 0 ? $"▲{roundedHeight}" : $"▼{Math.Abs(roundedHeight)}");
                        }

                        if (IsError)
                            lines[0] = "ERROR"; // In case POS stops updating, let us know!
                    }

                    DrawPlayerText(canvas, point, lines);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"WARNING! Player Draw Error: {ex}");
            }
        }

    
        public virtual ref readonly Vector3 Position
        {
            get
            {
                var skeletonPos = SkeletonRoot.Position;
                if (skeletonPos != Vector3.Zero && !float.IsNaN(skeletonPos.X) && !float.IsInfinity(skeletonPos.X))
                {
                    _cachedPosition = skeletonPos;
                    return ref SkeletonRoot.Position;
                }
                return ref _cachedPosition;
            }
        }
        public Vector2 MouseoverPosition { get; set; }

        /// <summary>
        /// Draws a Player Pill on this location.
        /// </summary>
        private void DrawPlayerPill(SKCanvas canvas, LocalPlayer localPlayer, SKPoint point)
        {
            var paints = GetPaints();
            if (this != localPlayer && RadarViewModel.MouseoverGroup is int grp && grp == GroupID)
                paints.Item1 = SKPaints.PaintMouseoverGroup;

            float scale = 1.65f * App.Config.UI.UIScale;

            canvas.Save();
            canvas.Translate(point.X, point.Y);
            canvas.Scale(scale, scale);
            canvas.RotateDegrees(MapRotation);

            SKPaints.ShapeOutline.StrokeWidth = paints.Item1.StrokeWidth * 1.3f;
            // Draw the pill
            canvas.DrawPath(_playerPill, SKPaints.ShapeOutline); // outline
            canvas.DrawPath(_playerPill, paints.Item1);

            var aimlineLength = this == localPlayer || (IsFriendly && App.Config.UI.TeammateAimlines) ?
                App.Config.UI.AimLineLength : 0;
            if (!IsFriendly &&
                !(IsAI && !App.Config.UI.AIAimlines) &&
                this.IsFacingTarget(localPlayer, App.Config.UI.MaxDistance)) // Hostile Player, check if aiming at a friendly (High Alert)
                aimlineLength = 9999;

            if (aimlineLength > 0)
            {
                // Draw line from nose tip forward
                canvas.DrawLine(PP_NOSE_X, 0, PP_NOSE_X + aimlineLength, 0, SKPaints.ShapeOutline); // outline
                canvas.DrawLine(PP_NOSE_X, 0, PP_NOSE_X + aimlineLength, 0, paints.Item1);
            }

            canvas.Restore();
        }

        /// <summary>
        /// Draws a Death Marker on this location.
        /// </summary>
        private static void DrawDeathMarker(SKCanvas canvas, SKPoint point)
        {
            float scale = App.Config.UI.UIScale;

            canvas.Save();
            canvas.Translate(point.X, point.Y);
            canvas.Scale(scale, scale);
            canvas.DrawPath(_deathMarker, SKPaints.PaintDeathMarker);
            canvas.Restore();
        }

        /// <summary>
        /// Draws Player Text on this location.
        /// </summary>
        private void DrawPlayerText(SKCanvas canvas, SKPoint point, IList<string> lines)
        {
            var paints = GetPaints();
            if (RadarViewModel.MouseoverGroup is int grp && grp == GroupID)
                paints.Item2 = SKPaints.TextMouseoverGroup;
            point.Offset(9.5f * App.Config.UI.UIScale, 0);
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line?.Trim()))
                    continue;


                canvas.DrawText(line, point, SKTextAlign.Left, SKFonts.UIRegular, SKPaints.TextOutline); // Draw outline
                canvas.DrawText(line, point, SKTextAlign.Left, SKFonts.UIRegular, paints.Item2); // draw line text

                point.Offset(0, 12f * App.Config.UI.UIScale); // Compact
            }
        }

        private ValueTuple<SKPaint, SKPaint> GetPaints()
        {
            if (IsFocused)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintFocused, SKPaints.TextFocused);
            if (this is LocalPlayer)
                return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintLocalPlayer, SKPaints.TextLocalPlayer);
            switch (Type)
            {
                case PlayerType.Teammate:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintTeammate, SKPaints.TextTeammate);
                case PlayerType.PMC:
                    return PlayerSide switch
                    {
                        Enums.EPlayerSide.Bear => new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPMCBear, SKPaints.TextPMCBear),
                        Enums.EPlayerSide.Usec => new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPMCUsec, SKPaints.TextPMCUsec),
                        _ => new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPMC, SKPaints.TextPMC)
                    };
                case PlayerType.AIScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintScav, SKPaints.TextScav);
                case PlayerType.AIRaider:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintRaider, SKPaints.TextRaider);
                case PlayerType.AIBoss:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintBoss, SKPaints.TextBoss);
                case PlayerType.PScav:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPScav, SKPaints.TextPScav);
                case PlayerType.SpecialPlayer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintWatchlist, SKPaints.TextWatchlist);
                case PlayerType.Streamer:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintStreamer, SKPaints.TextStreamer);
                default:
                    return new ValueTuple<SKPaint, SKPaint>(SKPaints.PaintPMC, SKPaints.TextPMC);
            }
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            if (this == localPlayer)
                return;
            using var lines = new PooledList<string>();
            var name = App.Config.UI.HideNames && IsHuman ? "<Hidden>" : Name;
            string health = null;
            if (this is ObservedPlayer observed)
                health = observed.HealthStatus is Enums.ETagStatus.Healthy
                    ? null
                    : $" ({observed.HealthStatus.ToString()})"; // Only display abnormal health status
            if (this is ObservedPlayer obs && obs.IsStreaming) // Streamer Notice
                lines.Add("[LIVE TTV - Double Click]");
            string alert = Alerts?.Trim();
            if (!string.IsNullOrEmpty(alert)) // Special Players,etc.
                lines.Add(alert);
            if (IsHostileActive) // Enemy Players, display information
            {
                lines.Add($"{name}{health} {AccountID}".Trim());
                var faction = PlayerSide.ToString();
                string g = null;
                if (GroupID != -1)
                    g = $" G:{GroupID} ";
                lines.Add($"{faction}{g}");
            }
            else if (!IsAlive)
            {
                lines.Add($"{Type.ToString()}:{name}");
                string g = null;
                if (GroupID != -1)
                    g = $"G:{GroupID} ";
                if (g is not null) lines.Add(g);
            }
            else if (IsAIActive)
            {
                lines.Add(name);
                lines.Add($"Weapon: {WeaponDisplayText}");
            }

            if (this is ObservedPlayer obs2 && obs2.Equipment.Items is IReadOnlyDictionary<string, TarkovMarketItem> equipment)
            {
                // This is outside of the previous conditionals to always show equipment even if they're dead,etc.
                lines.Add($"Value: {Utilities.FormatNumberKM(obs2.Equipment.Value)}");
                foreach (var item in equipment.OrderBy(e => e.Key))
                {
                    lines.Add($"{item.Key.Substring(0, 5)}: {item.Value.ShortName}");
                }
            }

            Position.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, lines.Span);
        }

        #endregion

        #region High Alert

        /// <summary>
        /// True if Current Player is facing <paramref name="target"/>.
        /// </summary>
        public bool IsFacingTarget(AbstractPlayer target, float? maxDist = null)
        {
            Vector3 delta = target.Position - this.Position;

            if (maxDist is float m)
            {
                float maxDistSq = m * m;
                float distSq = Vector3.Dot(delta, delta);
                if (distSq > maxDistSq) return false;
            }

            float distance = delta.Length();
            if (distance <= 1e-6f)
                return true;

            Vector3 fwd = RotationToDirection(this.Rotation);

            float cosAngle = Vector3.Dot(fwd, delta) / distance;

            const float A = 31.3573f;
            const float B = 3.51726f;
            const float C = 0.626957f;
            const float D = 15.6948f;

            float x = MathF.Abs(C - D * distance);
            float angleDeg = A - B * MathF.Log(MathF.Max(x, 1e-6f));
            if (angleDeg < 1f) angleDeg = 1f;
            if (angleDeg > 179f) angleDeg = 179f;

            float cosThreshold = MathF.Cos(angleDeg * (MathF.PI / 180f));
            return cosAngle >= cosThreshold;

            static Vector3 RotationToDirection(Vector2 rotation)
            {
                float yaw = rotation.X * (MathF.PI / 180f);
                float pitch = rotation.Y * (MathF.PI / 180f);

                float cp = MathF.Cos(pitch);
                float sp = MathF.Sin(pitch);
                float sy = MathF.Sin(yaw);
                float cy = MathF.Cos(yaw);

                var dir = new Vector3(
                    cp * sy,
                   -sp,
                    cp * cy
                );

                float lenSq = Vector3.Dot(dir, dir);
                if (lenSq > 0f && MathF.Abs(lenSq - 1f) > 1e-4f)
                {
                    float invLen = 1f / MathF.Sqrt(lenSq);
                    dir *= invLen;
                }
                return dir;
            }
        }

        /// <summary>
        /// Get Bone Position (if available).
        /// </summary>
        /// <param name="bone">Bone Index.</param>
        /// <returns>World Position of Bone, or SkeletonRoot position as fallback.</returns>
        public Vector3 GetBonePos(Bones bone)
        {
            try
            {
                if (PlayerBones.TryGetValue(bone, out var boneTransform))
                {
                    var pos = boneTransform.Position;
                    // Validate the position is reasonable (not zero, not NaN/Infinity)
                    if (pos != Vector3.Zero && !float.IsNaN(pos.X) && !float.IsInfinity(pos.X))
                        return pos;
                }
            }
            catch { }

            // Fallback to skeleton root position instead of zero
            // This prevents players from "teleporting" to the origin
            var rootPos = SkeletonRoot?.Position ?? Vector3.Zero;
            if (rootPos != Vector3.Zero && !float.IsNaN(rootPos.X) && !float.IsInfinity(rootPos.X))
                return rootPos;

            return Position; // Ultimate fallback to cached position
        }

        #endregion
    }

    /// <summary>
    /// Simple wrapper exposing skeleton root and bone transforms for aim helpers.
    /// </summary>
    public sealed class PlayerSkeleton
    {
        public PlayerSkeleton(UnityTransform root, ConcurrentDictionary<Bones, UnityTransform> bones)
        {
            Root = root;
            BoneTransforms = bones;
        }

        public UnityTransform Root { get; }
        public ConcurrentDictionary<Bones, UnityTransform> BoneTransforms { get; }
    }
}
