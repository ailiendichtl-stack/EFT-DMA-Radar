using System.Linq;
using static LoneEftDmaRadar.Tarkov.Unity.UnitySDK;

namespace SDK
{
    public readonly partial struct ClassNames
    {
        public readonly partial struct NetworkContainer
        {
            public const uint ClassName_ClassToken = 0x2000661; // MDToken
            public const string ClassName = @"\uE32D";
        }

        public readonly partial struct AmmoTemplate
        {
            public const uint ClassName_ClassToken = 0x2002ABD; // MDToken
            public const uint MethodName_MethodToken = 0x60105B5; // MDToken
            public const string ClassName = @"\uEF1A";
            public const string MethodName = @"get_LoadUnloadModifier";
        }

        public readonly partial struct OpticCameraManagerContainer
        {
            public const uint ClassName_ClassToken = 0x2002F60; // MDToken
            public const string ClassName = @"\uF124";
        }

        public readonly partial struct ScreenManager
        {
            public const uint ClassName_ClassToken = 0x200369B; // MDToken
            public const string ClassName = @"\uF1EF";
        }

        public readonly partial struct FirearmController
        {
            public const uint ClassName_ClassToken = 0x20018A1; // MDToken
            public const string ClassName = @"EFT.Player+FirearmController";
        }

        public readonly partial struct ProceduralWeaponAnimation
        {
            public const uint ClassName_ClassToken = 0x20025B1; // MDToken
            public const uint MethodName_MethodToken = 0x600EAFC; // MDToken
            public const string ClassName = @"EFT.Animations.ProceduralWeaponAnimation";
            public const string MethodName = @"get_ShotNeedsFovAdjustments";
        }
    }

    public readonly partial struct Offsets
    {
        public static class AssemblyCSharp
        {
            public const uint TypeStart = 0;
            public const uint TypeCount = 16336;
        }

        public readonly partial struct GameWorld
		{
			public const uint BtrController = 0x28; // EFT.Vehicle.BtrController
			public const uint TransitController = 0x38; // TransitController
			public const uint ExfiltrationController = 0x58; // EFT.Interactive.ExfiltrationController
			public const uint ClientShellingController = 0xA8; // ArtilleryShellingControllerClient
			public const uint LocationId = 0xD0; // string
			public const uint GameDateTime = 0xD8; // GameDateTime
			public const uint LootList = 0x198; // System.Collections.Generic.List<IKillable>
			public const uint RegisteredPlayers = 0x1B8; // System.Collections.Generic.List<IPlayer>
			public const uint BorderZones = 0x1F0; // BorderZone[]
			public const uint MainPlayer = 0x210; // EFT.Player
			public const uint SynchronizableObjectLogicProcessor = 0x248; // EFT.SynchronizableObjects.SynchronizableObjectLogicProcessor
			public const uint Grenades = 0x288; // DictionaryListHydra<int, Throwable>
			public const uint World = 0x218; // EFT.World
		}

        public readonly partial struct World
        {
            public const uint Interactables = 0x30; // WorldInteractiveObject[] _interactableObjectsForNetSync
        }

        public readonly partial struct TransitController
        {
            public const uint TransitPoints = 0x18; // Dictionary<Int32, TransitPoint> - pointsById
        }

        public readonly partial struct TransitPoint
        {
            public const uint parameters = 0x20; // TransitParameters
        }

        public readonly partial struct TransitParameters
        {
            public const uint id = 0x10; // int
            public const uint active = 0x14; // bool
            public const uint name = 0x18; // String
            public const uint description = 0x20; // String
            public const uint target = 0x38; // String
            public const uint location = 0x40; // String
        }

        public readonly partial struct BorderZone
        {
            public const uint Description = 0x60; // String
            public const uint _extents = 0x28; // UnityEngine.Vector3
        }

        public readonly partial struct MineDirectional
        {
            public const uint Mines = 0x8; // List<MineDirectional>
            public const uint MineData = 0x20; // MineSettings
        }

        public readonly partial struct MineSettings
        {
            public const uint _maxExplosionDistance = 0x28; // Single
            public const uint _directionalDamageAngle = 0x64; // Single
        }

        public readonly partial struct LocationScene
        {
            public const uint WorldInteractiveObjects = 0x30; // WorldInteractiveObject[]
        }

        public readonly partial struct Interactable
        {
            public const uint KeyId = 0x60; // string
            public const uint Id = 0x70; // string
            public const uint DoorState = 0xD0; // EDoorState (byte)
        }

        public readonly partial struct ExfiltrationController
        {
            public const uint ExfiltrationPoints = 0x20; // EFT.Interactive.ExfiltrationPoint[]
            public const uint ScavExfiltrationPoints = 0x28; // EFT.Interactive.ScavExfiltrationPoint[]
            public const uint SecretExfiltrationPoints = 0x30; // EFT.Interactive.SecretExfiltrationPoint[]
        }

        public readonly partial struct ScavExfil
        {
            public const uint EligibleIds = 0xF8; // List<String>
        }

        public readonly partial struct ExfiltrationPoint
        {
            public const uint Status = 0x58; // EExfiltrationStatus
            public const uint Settings = 0x98; // ExitTriggerSettings
            public const uint EligibleEntryPoints = 0xC0; // String[] — PMC spawn entry points
        }

        public readonly partial struct ExitSettings
        {
            public const uint Name = 0x18; // string
        }

        public readonly partial struct SynchronizableObject
        {
            public const uint Type = 0x68; // System.Int32
        }

        public readonly partial struct SynchronizableObjectLogicProcessor
        {
            public const uint SynchronizableObjects = 0x10; // _activeSynchronizableObjects (active airdrops/tripwires; 0x18 = static only)
            public const uint TripwireManager = 0x40; // EFT.SynchronizableObjects.TripwireManager
        }

        public readonly partial struct TripwireManager
        {
            public const uint Tripwires = 0x10; // List<TripwireSynchronizableObject>
        }

        public readonly partial struct TripwireSynchronizableObject
        {
            public const uint _tripwireState = 0xE4; // System.Int32
            public const uint ToPosition = 0x158; // UnityEngine.Vector3
        }

        public readonly partial struct BtrController
        {
            public const uint BtrView = 0x50; // EFT.Vehicle.BTRView
        }

        public readonly partial struct BTRView
        {
            public const uint turret = 0x60; // EFT.Vehicle.BTRTurretView
            public const uint _previousPosition = 0xB4;
        }

        public readonly partial struct BTRTurretView
        {
            public const uint AttachedBot = 0x60; // System.ValueTuple<ObservedPlayerView, Boolean>
        }

        public readonly partial struct Throwable
        {
            public const uint _isDestroyed  = 0x4D; // Boolean
        }

        public readonly partial struct ClientShellingController
        {
            public const uint ActiveClientProjectiles = 0x68; // Dictionary<int, ArtilleryProjectileClient>
        }

        public readonly partial struct ArtilleryProjectileClient
        {
            public const uint Position = 0x30; // UnityEngine.Vector3 (_targetPosition)
            public const uint IsActive = 0x3C; // Boolean (_flyOn)
        }

        public readonly partial struct Player
        {
            public const uint _characterController = 0x40; // ICharacterController
            public const uint MovementContext = 0x60; // EFT.MovementContext
            public const uint _playerBody = 0x190; // EFT.PlayerBody
            public const uint ProceduralWeaponAnimation = 0x338; // EFT.Animations.ProceduralWeaponAnimation
            public const uint GameWorld = 0x5F8; // EFT.GameWorld (used by IL2CPP interop)
            public const uint _animators = 0x640; // IAnimator[] array
            public const uint EnabledAnimators = 0x670; // EAnimatorMask
            public const uint Corpse = 0x680; // EFT.Interactive.Corpse
            public const uint Location = 0x870; // String
            public const uint InteractableObject = 0x888; // InteractableObject
            public const uint RaidId = 0x8D8; // int32_t - Raid instance ID
            public const uint VoipID = 0x8F0; // Boolean
            public const uint Id = 0x8F8; // int32_t
            public const uint Profile = 0x900; // EFT.Profile
            public const uint Physical = 0x918; // PhysicalBase
            public const uint AIData = 0x940; // Pointer to AIData (for offline AI SpawnType detection)
            public const uint _healthController = 0x960; // IHealthController
            public const uint _inventoryController = 0x978; // EFT.PlayerInventoryController update
            public const uint _handsController = 0x980; // EFT.PlayerHands update
            public const uint _playerLookRaycastTransform = 0xA10; // UnityEngine.Transform
            public const uint InteractionRayOriginOnStartOperation = 0xA1C; // UnityEngine.Vector3
            public const uint InteractionRayDirectionOnStartOperation = 0xA28; // UnityEngine.Vector3
            public const uint IsYourPlayer = 0xA89; // Boolean
        }

        public readonly partial struct ObservedPlayerView
        {
			public const uint ObservedPlayerController = 0x28; // EFT.NextObservedPlayer.ObservedPlayerController
			public const uint Voice = 0x40; // string
			public const uint VisibleToCameraType = 0x60; // ECameraType
			public const uint AIData = 0x70; // Pointer to AIData (for SpawnType detection)
			public const uint Id = 0x7C; // int32_t - Player's unique in-memory ID
			public const uint GroupID = 0x80; // string
			public const uint Side = 0x94; // EFT.EPlayerSide
			public const uint IsAI = 0xA0; // bool
			public const uint VoipId = 0xB0; // string - VOIP session ID
			public const uint NickName = 0xB8; // String
			public const uint AccountId = 0xC0; // string
			public const uint PlayerBody = 0xD8; // EFT.PlayerBody
        }

        public readonly partial struct ObservedPlayerController
        {
            public const uint InventoryController = 0x10; // EFT.NextObservedPlayer.ObservedPlayerInventoryController
            public const uint PlayerView = 0x18; // EFT.NextObservedPlayer.ObservedPlayerView
            public const uint InfoContainer = 0xD0; // ObservedPlayerInfoContainer
            public const uint MovementController = 0xD8; // EFT.NextObservedPlayer.ObservedPlayerMovementController
            public const uint HealthController = 0xE8; // ObservedPlayerHealthController
            public const uint HandsController = 0x120; // ObservedPlayerHandsController
        }

        public readonly partial struct ObservedPlayerStateContext
        {
            public const uint Rotation = 0x20; // UnityEngine.Vector2
        }

          public readonly partial struct ObservedHealthController
        {
            public const uint HealthStatus = 0x10; // ETagStatus
            public const uint _player = 0x18; // EFT.NextObservedPlayer.ObservedPlayerView
            public const uint _playerCorpse = 0x20; // EFT.Interactive.ObservedCorpse
        }

        public readonly partial struct ObservedMovementState
        {
            public const uint ObservedPlayerHands = 0x130; // EFT.NextObservedPlayer.ObservedPlayerHandsController
        }

        public readonly partial struct ObservedPlayerHands // EFT.NextObservedPlayer.ObservedPlayerHandsController
        {
            public const uint Item = 0x58; // EFT.InventoryLogic.Item
        }

        public readonly partial struct ObservedHandsController
        {
            public const uint ItemInHands = 0x58; // EFT.InventoryLogic.Item
        }

        public readonly partial struct SimpleCharacterController
        {
            public const uint _collisionMask = 0x38; // UnityEngine.LayerMask
            public const uint _speedLimit = 0x54; // Single
            public const uint _sqrSpeedLimit = 0x58; // Single
            public const uint velocity = 0xF0; // UnityEngine.Vector3
        }

        public readonly partial struct InfoContainer
        {
            public const uint Side = 0x18; // EPlayerSide
        }

        public readonly partial struct PlayerSpawnInfo
        {
            public const uint Side = 0x28; // Int32
            public const uint WildSpawnType = 0x2C; // Int32
        }

        public readonly partial struct Profile // EFT, class: Profile
        {
            public const uint Id = 0x10; // String
            public const uint AccountId = 0x18; // String
            public const uint Info = 0x48; // ProfileInfo
            public const uint ProfileInventory = 0x70; // Inventory
            public const uint Skills = 0x80; // EFT.SkillManager
            public const uint TaskConditionCounters = 0x90; // Dictionary<MongoID, TaskConditionCounter>
            public const uint QuestsData = 0x98; // System.Collections.Generic.List<QuestStatusData>
            public const uint WishlistManager = 0x108; // WishlistManager
            public const uint Stats = 0x148; // ProfileStatsSeparator
        }

        public readonly partial struct WishlistManager
        {
            public const uint Items = 0x28; // Dictionary<MongoID, Int32>
        }

        public readonly partial struct QuestStatusData
        {
            public const uint Id = 0x10; // String
            public const uint StartTime = 0x18; // Int32
            public const uint Status = 0x1C; // Int32 (EQuestStatus)
            public const uint StatusStartTimestamps = 0x20; // object
            public const uint CompletedConditions = 0x28; // HashSet<MongoID>
            public const uint AvailableAfter = 0x30; // Int32
        }

        public readonly partial struct TaskConditionCounter
        {
            public const uint OnValueChanged = 0x10; // Action
            public const uint Id = 0x18; // MongoID
            public const uint Type = 0x30; // String
            public const uint SourceId = 0x38; // String
            public const uint Value = 0x40; // Int32 (current count)
            public const uint Template = 0x48; // Condition (target count)
            public const uint Conditional = 0x50; // IConditional
        }

        public readonly partial struct Condition
        {
            public const uint Id = 0x10; // MongoID
            public const uint Value = 0x28; // Single (target count)
            public const uint CompareMethod = 0x2C; // Int32
            public const uint VisibilityConditions = 0x30; // Condition[]
            public const uint Index = 0x38; // Int32
            public const uint ParentId = 0x40; // Nullable<MongoID>
            public const uint DynamicLocale = 0x60; // Boolean
            public const uint IsNecessary = 0x61; // Boolean
        }

        public readonly partial struct TriggerWithId
        {
            public const uint Id = 0x20; // String
            public const uint Description = 0x28; // String
        }

        public readonly partial struct PlayerInfo // EFT, class: ProfileInfo
        {
            public const uint Nickname = 0x10; // String
            public const uint EntryPoint = 0x28; // String
            // Voice field removed in game update
            public const uint Settings = 0x78; // WildSpawnSettings (for offline AI role/type)
            public const uint Side = 0x48; // [HUMAN] Int32
            public const uint RegistrationDate = 0x4C; // Int32
            public const uint GroupId = 0x50; // String
            public const uint MemberCategory = 0x80; // EMemberCategory
            public const uint Experience = 0x84; // Int32
        }

        public readonly partial struct PlayerInfoSettings // EFT.WildSpawnSettings
        {
            public const uint Role = 0x10; // WildSpawnType (ESpawnType enum)
        }

        public readonly partial struct MovementContext // EFT, class: MovementContext
        {
            public const uint Player = 0x48; // EFT.Player
            public const uint _rotation = 0xC8; // UnityEngine.Vector2
            public const uint PlantState = 0x78; // EFT.BaseMovementState <PlantState> PlantState
            public const uint CurrentState = 0x1F0; // EFT.BaseMovementState <CurrentState>k__BackingField
            public const uint _states = 0x480; // System.Collections.Generic.Dictionary<Byte, BaseMovementState> <_states> _states
            public const uint _movementStates = 0x4b0; // -.IPlayerStateContainerBehaviour[] <_movementStates> _movementStates
            public const uint _tilt = 0xb4; // Single <_tilt> _tilt
            public const uint _physicalCondition = 0x198; // System.Int32 <_physicalCondition> _physicalCondition
            public const uint _speedLimitIsDirty = 0x1b9; // Boolean <_speedLimitIsDirty> _speedLimitIsDirty
            public const uint StateSpeedLimit = 0x1bc; // Single <<StateSpeedLimit>k__BackingField> <StateSpeedLimit>k__BackingField
            public const uint StateSprintSpeedLimit = 0x1c0; // Single <<StateSprintSpeedLimit>k__BackingField> <StateSprintSpeedLimit>k__BackingField
            public const uint _lookDirection = 0x3b8; // UnityEngine.Vector3  <_lookDirection> _lookDirection
            public const uint WalkInertia = 0x4bC; // Single
            public const uint SprintBrakeInertia = 0x4C0; // Single
            public const uint _poseInertia = 0x4C4; // Single
            public const uint _currentPoseInertia = 0x4C8; // Single
            public const uint _inertiaAppliedTime = 0x26C; // Single
        }

        public readonly partial struct MovementState //Class: MovementState
        {
            public const uint StickToGround = 0x54; // Boolean <StickToGround> StickToGround
            public const uint PlantTime = 0x58; // Single <PlantTime> PlantTime
            public const uint Name = 0x11; // System.Byte <Name> Name
            public const uint AnimatorStateHash = 0x20; // Int32 <AnimatorStateName> AnimatorStateName
            public const uint AuthoritySpeed = 0x28; // Single
            public const uint _velocity = 0xDC; // UnityEngine.Vector3
            public const uint _velocity2 = 0xF0; // UnityEngine.Vector3
        }

        public readonly partial struct PlayerStateContainer //Class: PlayerStateContainer
        {
            public const uint Name = 0x19; // System.Byte
            public const uint StateFullNameHash = 0x40; // Int32 <StateFullNameHash> StateFullNameHash
        }

        public readonly partial struct InteractiveLootItem // EFT.Interactive, class: LootItem
        {
            public const uint Item = 0xF0; // EFT.InventoryLogic.Item
        }

        public readonly partial struct DizSkinningSkeleton // Diz.Skinning, class: Skeleton
        {
            public const uint _values = 0x30; // System.Collections.Generic.List<Transform>
        }

        public readonly partial struct LootableContainer // EFT.Interactive, class: LootableContainer
        {
            public const uint ItemOwner = 0x168; // -.\uEFB4
            public const uint InteractingPlayer = 0x150; // System.Object <InteractingPlayer>k__BackingField
        }

        public readonly partial struct LootableContainerItemOwner // EFT.InventoryLogic, class: ItemController
        {
            public const uint RootItem = 0xD0; // EFT.InventoryLogic.Item
        }

        public readonly partial struct LootItem
        {
            public const uint StackObjectsCount = 0x24; // Int32
            public const uint Version = 0x28; // Int32
            public const uint Components = 0x40; // ItemComponentCollection
            public const uint Template = 0x60; // EFT.InventoryLogic.ItemTemplate
            public const uint SpawnedInSession = 0x68; // Boolean
        }

        public readonly partial struct Item // EFT.InventoryLogic.Item - Ammo counter offsets
        {
            public const uint StackCount = 0x24; // Current stack/ammo count (Int32)
            public const uint Cartridges = 0xA8; // EFT.InventoryLogic.StackSlot (on magazine items)
        }

        public readonly partial struct ItemTemplate // EFT.InventoryLogic, class: ItemTemplate
        {
            public const uint Name = 0x10; // String
            public const uint ShortName = 0x18; // String
            public const uint QuestItem = 0x34; // Boolean
            public const uint Weight = 0xB0; // Single
            public const uint _id = 0xE0; // EFT.MongoID
        }

        public readonly partial struct PlayerBody // EFT, class: PlayerBody
        {
            public const uint SkeletonRootJoint = 0x30; // Diz.Skinning.Skeleton
            public const uint BodySkins = 0x58; // Dictionary<Int32, LoddedSkin>
            public const uint _bodyRenderers = 0x68; // BodyRenderer[]
            public const uint SlotViews = 0x90; // SlotViewsContainer
            public const uint PointOfView = 0xC0; // PointOfView
        }

        public readonly partial struct PlayerBodySubclass
        {
            public const uint Dresses = 0x40; // EFT.Visual.Dress[]
        }

        public readonly partial struct Dress
        {
            public const uint Renderers = 0x38; // UnityEngine.Renderer[]
        }

        public readonly partial struct LoddedSkin
        {
            public const uint _lods = 0x20; // Diz.Skinning.AbstractSkin[]
        }

        public readonly partial struct Skin
        {
            public const uint _skinnedMeshRenderer = 0x28; // UnityEngine.SkinnedMeshRenderer
        }

        public readonly partial struct TorsoSkin
        {
            public const uint _skin = 0x28; // Diz.Skinning.Skin
        }

        public readonly partial struct SlotViewsContainer
        {
            public const uint Dict = 0x10; // Dictionary<Var, Var>
        }

        public readonly partial struct InteractiveCorpse
        {
            public const uint PlayerBody = 0x130; // EFT.PlayerBody
        }

        public readonly partial struct BarterOtherOffsets
        {
            public const uint Dogtag = 0x80; // EFT.InventoryLogic.BarterOther.Dogtag
        }

        public readonly partial struct DogtagComponent
        {
            public const uint Item = 0x10; // EFT.InventoryLogic.Item
            public const uint GroupId = 0x18; // string
            public const uint AccountId = 0x20; // string
            public const uint ProfileId = 0x28; // string
            public const uint Nickname = 0x30; // string
            public const uint Side = 0x38; // EPlayerSide
            public const uint Level = 0x3C; // int32_t
            public const uint Time = 0x40; // DateTime
            public const uint Status = 0x48; // string
            public const uint KillerAccountId = 0x50; // string
            public const uint KillerProfileId = 0x58; // string
            public const uint KillerName = 0x60; // string
            public const uint WeaponName = 0x68; // string
            public const uint CarriedByGroupMember = 0x70; // bool
        }

        public readonly partial struct FirearmController
        {
            // WeaponAnimation removed - use Player.ProceduralWeaponAnimation (0x338) instead
            public const uint COI = 0xF0; // Single (was TotalCenterOfImpact at 0x2A0)
            public const uint WeaponLn = 0x100; // Single
            public const uint _aimingSens = 0x108; // Single — per-weapon aiming sensitivity (ergo-derived)
            public const uint Fireport = 0x150; // EFT.BifacialTransform <Fireport> Fireport
            public static readonly uint[] To_FirePortTransformInternal = new uint[] { Fireport, 0x10, 0x10 };
            public static readonly uint[] To_FirePortVertices = To_FirePortTransformInternal.Concat(new uint[] { UnityOffsets.TransformInternal_TransformAccessOffset, UnityOffsets.Hierarchy_VerticesOffset }).ToArray();
        }

        public readonly partial struct ClientFirearmController
        {
            public const uint WeaponLn = 0x100; // Single - Inherited from FirearmController
            public const uint ShotIndex = 0x438; // Byte - LastShotId
        }

        public readonly partial struct ProceduralWeaponAnimation // EFT.Animations, class: ProceduralWeaponAnimation
        {
            public const uint HandsContainer = 0x20; // EFT.Animations.PlayerSpring <HandsContainer> HandsContainer
            public const uint Breath = 0x38; // EFT.Animations.BreathEffector <Breath> Breath
            public const uint MotionReact = 0x48; // -.MotionEffector <MotionReact> MotionReact
            public const uint Shootingg = 0x58; // -.ShotEffector <Shootingg> Shootingg
            public const uint _optics = 0x180; // System.Collections.Generic.List<ProceduralWeaponAnimation.SightNBone>
            public const uint Mask = 0x30; // System.Int32 <Mask> Mask
            public const uint IsAiming = 0x145; // Boolean
            public const uint _isAiming = 0x145; // Boolean
            public const uint _fieldOfView = 0xA8; // Float
            public const uint _aimingSpeed = 0x164; // Single <_aimingSpeed> _aimingSpeed
            public const uint _fovCompensatoryDistance = 0x194; // Single <_fovCompensatoryDistance> _fovCompensatoryDistance
            public const uint _compensatoryScale = 0x1c4; // Single <_compensatoryScale> _compensatoryScale
            public const uint _shotDirection = 0x1c8; // UnityEngine.Vector3 <_shotDirection> _shotDirection
            public const uint CameraSmoothOut = 0x20c; // Single <CameraSmoothOut> CameraSmoothOut
            public const uint PositionZeroSum = 0x31c; // UnityEngine.Vector3 <PositionZeroSum> PositionZeroSum
            public const uint ShotNeedsFovAdjustments = 0x433; // Boolean <<ShotNeedsFovAdjustments>k__BackingField> <ShotNeedsFovAdjustments>k__BackingField
            public const uint _firearmController = 0x138; // Player.FirearmController reference
            public const uint _overweightAimingMultiplier = 0x168; // Single — overweight turn speed multiplier
            public const uint _ergonomicWeight = 0x38C; // Single — weapon ergonomic weight
            public const uint _aimingWeight = 0x398; // Single — composite aiming weight
        }

        public readonly partial struct HandsContainer // PlayerSpring
        {
            public const uint HandsRotation = 0x40; // UnityEngine.Quaternion
            public const uint CameraRotation = 0x48; // UnityEngine.Quaternion
            public const uint CameraPosition = 0x50; // UnityEngine.Vector3
            public const uint CameraOffset = 0xDC; // UnityEngine.Vector3
        }

        public readonly partial struct MotionEffector
        {
            public const uint _mouseProcessors = 0x18; // MouseProcessor[]
            public const uint _movementProcessors = 0x20; // MovementProcessor[]
        }

        public readonly partial struct SightNBone
        {
            public const uint Mod = 0x10; // EFT.InventoryLogic.SightComponent
        }

        public readonly partial struct SightComponent
        {
            public const uint _template = 0x20; // EFT.InventoryLogic.ISightComponentTemplate
            public const uint ScopesSelectedModes = 0x30; // System.Int32[]
            public const uint SelectedScope = 0x38; // System.Int32
            public const uint ScopeZoomValue = 0x3C; // System.Single
        }

        public readonly partial struct SightInterface
        {
            public const uint AimSensitivity = 0x1A8; // System.Single[][] — per-scope, per-mode sensitivity
            public const uint Zooms = 0x1B8; // System.Single[][]
        }

        public readonly partial struct Physical //Class: .PhysicalBase
        {
            public const uint Stamina = 0x68; // -.\uE398 <Stamina> Stamina
            public const uint HandsStamina = 0x70; // -.\uE398 <HandsStamina> HandsStamina
            public const uint Oxygen = 0x78; // -.\uE398 <Oxygen> Oxygen
            public const uint Overweight = 0x1c; // Single <Overweight> Overweight
            public const uint WalkOverweight = 0x20; // Single <WalkOverweight> WalkOverweight
            public const uint WalkSpeedLimit = 0x24; // Single <WalkSpeedLimit> WalkSpeedLimit
            public const uint Inertia = 0x28; // Single <Inertia> Inertia
            public const uint WalkOverweightLimits = 0xa4; // UnityEngine.Vector2 <WalkOverweightLimits> WalkOverweightLimits
            public const uint BaseOverweightLimits = 0xac; // UnityEngine.Vector2 <BaseOverweightLimits> BaseOverweightLimits
            public const uint SprintOverweightLimits = 0xc0; // UnityEngine.Vector2 <SprintOverweightLimits> SprintOverweightLimits
            public const uint SprintWeightFactor = 0x104; // Single
            public const uint SprintAcceleration = 0x114; // Single <SprintAcceleration> SprintAcceleration
            public const uint PreSprintAcceleration = 0x118; // Single <PreSprintAcceleration> PreSprintAcceleration
            public const uint BerserkRestorationFactor = 0x110; // Single
            public const uint IsOverweightA = 0x11C; // Boolean
            public const uint IsOverweightB = 0x11D; // Boolean
            public const uint SprintOverweight = 0xD0; // Single
            public const uint PreviousWeight = 0xD4; // Single
        }

        public readonly partial struct PhysicalValue //Class: .Stamina
        {
            public const uint Current = 0x10; // Single
        }

        public readonly partial struct BreathEffector //Class: EFT.Animations.BreathEffector
        {
            public const uint Intensity = 0x30; // Single <Intensity> Intensity
        }

        public readonly partial struct ShotEffector //Class: .ShotEffector
        {
            public const uint NewShotRecoil = 0x20; // EFT.Animations.NewRecoil.NewRecoilShotEffect <NewShotRecoil> NewShotRecoil
        }

        public readonly partial struct NewShotRecoil //Class: EFT.Animations.NewRecoil.NewRecoilShotEffect
        {
            public const uint IntensitySeparateFactors = 0x94; // UnityEngine.Vector3 <IntensitySeparateFactors> IntensitySeparateFactors
        }

        public readonly partial struct ItemHandsController //Class: .ItemHandsController
        {
            public const uint Item = 0x70; // EFT.InventoryLogic.Item
        }

        public readonly partial struct LootItemWeapon //Class: EFT.InventoryLogic.Weapon
        {
            public const uint FireMode = 0xa0; // EFT.InventoryLogic.FireModeComponent <FireMode> FireMode
            public const uint Chambers = 0xb0; // EFT.InventoryLogic.Slot[] <<Chambers>k__BackingField> <Chambers>k__BackingField
            public const uint _magSlotCache = 0xc8; // EFT.InventoryLogic.Slot <_magSlotCache> _magSlotCache
        }

        public readonly partial struct LootItemMagazine //Class: EFT.InventoryLogic.MagazineTemplate
        {
            public const uint Cartridges = 0x1a8; // EFT.InventoryLogic.StackSlot <Cartridges> Cartridges
            public const uint LoadUnloadModifier = 0x1b0; // Single <LoadUnloadModifier> LoadUnloadModifier
        }

        public readonly partial struct StackSlot //Class: EFT.InventoryLogic.StackSlot
        {
            public const uint _items = 0x18; // System.Collections.Generic.List<Item>
            public const uint MaxCount = 0x10; // Int32

            // Ammo counter offsets (from CyNickal - used when reading via Item.Cartridges path)
            public const uint Max = 0x10; // Max capacity (Int32)
            public const uint Items = 0x18; // System.Collections.Generic.List<Item>
        }

        public readonly partial struct Slot //Class: EFT.InventoryLogic.Slot
        {
            public const uint ContainedItem = 0x48; // EFT.InventoryLogic.Item
            public const uint ID = 0x58; // String
            public const uint Required = 0x18; // Boolean
        }

        public readonly partial struct LootItemMod //Class: EFT.InventoryLogic.Mod
        {
            public const uint Grids = 0x78; // -.\uEE74[]
            public const uint Slots = 0x80; // EFT.InventoryLogic.Slot[]
        }

        public readonly partial struct Grid // EFT.InventoryLogic.Grid - Container grid storage
        {
            public const uint ItemCollection = 0x48; // GClass??? - ItemCollection/ContainedItems wrapper
        }

        public readonly partial struct GridItemCollection // Grid's ItemCollection wrapper
        {
            public const uint Items = 0x18; // List<Item> (ItemsList field — 0x10 is the Dictionary, not the List)
        }

        public readonly partial struct ModTemplate //Class: EFT.InventoryLogic.ModTemplate
        {
            public const uint Velocity = 0x188; // Single <Velocity> Velocity
        }

        public readonly partial struct AmmoTemplate //Class: EFT.InventoryLogic.AmmoTemplate
        {
            public const uint InitialSpeed = 0x1a4; // Single <InitialSpeed> InitialSpeed
            public const uint BallisticCoeficient = 0x1b8; // Single <BallisticCoeficient> BallisticCoeficient
            public const uint BulletMassGram = 0x25c; // Single <BulletMassGram> BulletMassGram
            public const uint BulletDiameterMilimeters = 0x260; // Single <BulletDiameterMilimeters> BulletDiameterMilimeters
        }

        public readonly partial struct WeaponTemplate //Class: EFT.InventoryLogic.WeaponTemplate
        {
            public const uint Velocity = 0x254; // Single <Velocity> Velocity
            public const uint AllowJam = 0x308; // Boolean
            public const uint AllowFeed = 0x309; // Boolean
            public const uint AllowMisfire = 0x30A; // Boolean
            public const uint AllowSlide = 0x30B; // Boolean
        }

        public readonly partial struct ObservedMovementController //Class: EFT.NextObservedPlayer.ObservedPlayerMovementModel
        {
            public const uint ObservedPlayerStateContext = 0x98;
            public const uint Rotation = 0x1c; // UnityEngine.Vector2 <HeadRotation> HeadRotation
            public const uint Velocity = 0x30; // UnityEngine.Vector3 <Velocity> Velocity
        }

        public readonly partial struct InventoryController
        {
            public const uint Inventory = 0x100; // EFT.InventoryLogic.Inventory
        }

        public readonly partial struct Inventory
        {
            public const uint Equipment = 0x18; // EFT.InventoryLogic.InventoryEquipment
        }

        public readonly partial struct InventoryEquipment
        {
            public const uint _cachedSlots = 0x90; // EFT.InventoryLogic.Slot[]
        }

        public readonly partial struct AIData
        {
            public const uint BotOwner = 0x28; // Pointer to BotOwner
            public const uint bIsAi = 0x100; // bool
        }

        public readonly partial struct BotOwner
        {
            public const uint SpawnProfileData = 0x3D0; // Pointer to SpawnProfileData
        }

        public readonly partial struct SpawnProfileData
        {
            public const uint SpawnType = 0x10; // ESpawnType enum
        }

        public readonly partial struct GamePlayerOwner
        {
            public const uint _myPlayer = 0x8; // EFT.Player (static field)
        }

        // MemWrite feature offsets

        public readonly partial struct SkillManager
        {
            public const uint SpeedMultiplier = 0x30; // Single
            public const uint StrengthBuffJumpHeightInc = 0x60; // SkillValueContainer
            public const uint StrengthBuffThrowDistanceInc = 0x70; // SkillValueContainer
            public const uint MagDrillsLoadSpeed = 0x180; // SkillValueContainer
            public const uint MagDrillsUnloadSpeed = 0x188; // SkillValueContainer
            public const uint RaidLoadedAmmoAction = 0x480; // Action
            public const uint RaidUnloadedAmmoAction = 0x488; // Action
        }

        public readonly partial struct SkillValueContainer
        {
            public const uint Value = 0x30; // float
        }

        public readonly partial struct TarkovApplication
        {
            public const uint ClientBackEnd = 0x30; // BackendSession
            public const uint _menuOperation = 0x128; // MainMenuShowOperation
            public const uint HideoutControllerAccess = 0x158; // HideoutControllerAccess
        }

        public readonly partial struct MainMenuShowOperation
        {
            public const uint _afkMonitor = 0x38; // AfkMonitor
            public const uint _profile = 0x50; // Profile
            public const uint _preloaderUI = 0x60; // PreloaderUI
        }

        public readonly partial struct PreloaderUI
        {
            public const uint _alphaVersionLabel = 0x20; // TextMeshProUGUI
            public const uint _sessionIdText = 0x118; // TextMeshProUGUI
        }

        public readonly partial struct AfkMonitor
        {
            public const uint Delay = 0x10; // float (seconds)
        }

        public readonly partial struct BodyAnimator
        {
            public const uint UnityAnimator = 0x10; // UnityEngine.Animator
        }

        public readonly partial struct UnityAnimator
        {
            public const uint Speed = 0x4B0; // float (native Unity Animator speed)
        }

        public readonly partial struct BSGGameSetting
        {
            public const uint ValueClass = 0x28; // BSGGameSettingValueClass
        }

        public readonly partial struct BSGGameSettingValueClass
        {
            public const uint Value = 0x30; // float
        }

        public readonly partial struct InventoryBlur
        {
            public const uint _upsampleTexDimension = 0x30; // int (enum)
            public const uint _blurCount = 0x38; // int
        }

        public readonly partial struct TOD_Scattering
        {
            public const uint Sky = 0x28; // TOD_Sky
        }

        public readonly partial struct TOD_Sky
        {
            public const uint Cycle = 0x38; // TOD_CycleParameters
            public const uint TOD_Components = 0xA0; // TOD_Components
        }

        public readonly partial struct TOD_CycleParameters
        {
            public const uint Hour = 0x10; // float
        }

        public readonly partial struct TOD_Components
        {
            public const uint TOD_Time = 0x118; // TOD_Time
        }

        public readonly partial struct TOD_Time
        {
            public const uint LockCurrentTime = 0x20; // bool
        }

        // === Visual / Effects structs ===

        public static class EffectsController
        {
            public const uint _effectsPrefab = 0x20;
            public const uint FastVineteFlicker = 0x28;
            public const uint RainScreenDrops = 0x30;
            public const uint ScreenWater = 0x38;
            public const uint _vignette = 0x40;
            public const uint _doubleVision = 0x48;
            public const uint _hueFocus = 0x50;
            public const uint _radialBlur = 0x58;
            public const uint _sharpen = 0x60;
            public const uint _lowhHealthBlend = 0x68;
            public const uint _bloodlossBlend = 0x70;
            public const uint _wiggle = 0x78;
            public const uint _motionBluer = 0x80;
            public const uint _bloodOnScreen = 0x88;
            public const uint _grenadeFlash = 0x90;
            public const uint _eyeBurn = 0x98;
            public const uint _blur = 0xA0;
            public const uint _dof = 0xA8;
            public const uint _effectAccumulators = 0xB0;
            public const uint _sharpenAccumulator = 0xB8;
            public const uint _radialBlurAccumulator = 0xC0;
            public const uint _chromaticAberration = 0xC8;
            public const uint _thermalVision = 0xD0;
            public const uint _frostbiteEffect = 0xD8;
        }

        public static class FrostbiteEffect
        {
            public const uint _ssaaPropagator = 0x20;
            public const uint _material = 0x28;
            public const uint _shader = 0x30;
            public const uint _baseColor = 0x38;
            public const uint _baseColorMap = 0x48;
            public const uint _normalMap = 0x50;
            public const uint _tiling = 0x58;
            public const uint _speed = 0x60;
            public const uint _opacity = 0x64;
            public const uint _distortion = 0x68;
            public const uint _radius = 0x6C;
            public const uint _shapeRadius = 0x70;
            public const uint _falloff = 0x78;
        }

        public readonly partial struct NightVision
        {
            public const uint _on = 0xC4; // Boolean
        }

        public readonly partial struct ThermalVision
        {
            public const uint On = 0x20; // Boolean
            public const uint IsNoisy = 0x21; // Boolean
            public const uint IsFpsStuck = 0x22; // Boolean
            public const uint IsMotionBlurred = 0x23; // Boolean
            public const uint IsGlitch = 0x24; // Boolean
            public const uint IsPixelated = 0x25; // Boolean
            public const uint ChromaticAberrationThermalShift = 0x68; // Single
            public const uint UnsharpRadiusBlur = 0x90; // Single
            public const uint UnsharpBias = 0x94; // Single
            public const uint Material = 0xB8; // UnityEngine.Material
        }

        public readonly partial struct HealthController
        {
            public const uint Energy = 0x68; // IL2CPP
            public const uint Hydration = 0x70; // IL2CPP
        }

        public readonly partial struct VisorEffect
        {
            public const uint Intensity = 0x20; // Single
        }

        public readonly partial struct CC_BrightnessContrastGamma
        {
            public const uint Brightness = 0x38; // Single
            public const uint Contrast = 0x3C; // Single
            public const uint Gamma = 0x4C; // Single
        }

        public readonly partial struct WeatherController
        {
            public const uint WeatherDebug = 0x88; // WeatherDebug
        }

        public readonly partial struct WeatherDebug
        {
            public const uint isEnabled = 0x10; // Boolean
            public const uint WindMagnitude = 0x14; // Single
            public const uint CloudDensity = 0x24; // Single
            public const uint Fog = 0x28; // Single
            public const uint Rain = 0x2C; // Single
            public const uint LightningThunderProbability = 0x30; // Single
        }

        public readonly partial struct LevelSettings
        {
            public const uint AmbientMode = 0x60; // Int32
            public const uint EquatorColor = 0x74; // Color
            public const uint GroundColor = 0x84; // Color
        }

        public readonly partial struct InertiaSettings
        {
            public const uint FallThreshold = 0x20; // Single
            public const uint BaseJumpPenaltyDuration = 0x4C; // Single
            public const uint BaseJumpPenalty = 0x54; // Single
            public const uint MoveTimeRange = 0xF4; // Vector2
        }

        // === EFT Hard Settings (singleton) ===

        public readonly partial struct EFTHardSettings
        {
            public const uint _instance = 0x0; // static singleton
            public const uint DecelerationSpeed = 0x50; // Single
            public const uint AIR_CONTROL_SAME_DIR = 0x158; // Single
            public const uint AIR_CONTROL_BACK_DIR = 0x15C; // Single
            public const uint AIR_CONTROL_NONE_OR_ORT_DIR = 0x160; // Single
            public const uint LOOT_RAYCAST_DISTANCE = 0x188; // Single
            public const uint DOOR_RAYCAST_DISTANCE = 0x18C; // Single
            public const uint ABOVE_OR_BELOW = 0x204; // Single
            public const uint ABOVE_OR_BELOW_STAIRS = 0x20C; // Single
            public const uint WEAPON_OCCLUSION_LAYERS = 0x238; // LayerMask
            public const uint MOUSE_LOOK_HORIZONTAL_LIMIT = 0x340; // Vector2
            public const uint MOUSE_LOOK_VERTICAL_LIMIT = 0x348; // Vector2
            public const uint MOUSE_LOOK_LIMIT_IN_AIMING_COEF = 0x350; // Single
            public const uint POSE_CHANGING_SPEED = 0x380; // Single
            public const uint AIR_LERP = 0x3AC; // Single
            public const uint AIR_MIN_SPEED = 0x3A8; // Single
            public const uint MED_EFFECT_USING_PANEL = 0x3B4; // Boolean
            public const uint AIM_PROCEDURAL_INTENSITY = 0x3FC; // Single
        }

        // === Singleton / TypeInfo structs ===

        public static class Special
        {
            public const ulong TypeInfoTableRva = 0x5AA90C8;
            public const uint EFTHardSettings_TypeIndex = 225;
            public const uint GPUInstancerManager_TypeIndex = 4917;
            public const uint WeatherController_TypeIndex = 10104;
            public const uint GlobalConfiguration_TypeIndex = 6406;
        }

        public readonly partial struct Il2CppClass
        {
            public const uint Name = 0x10;
            public const uint Namespace = 0x18;
            public const uint Methods = 0x98; // Il2CppClass::methods
            public const uint StaticFields = 0xB8;
            public const uint MethodCount = 0x120; // uint16
        }

        public readonly partial struct GlobalConfiguration
        {
            public const uint Inertia = 0x1A8; // InertiaSettings
        }

        public readonly partial struct EftClientBackendSession
        {
            public const uint GetGlobalConfig_RVA = 0x436580;
        }

        // === Camera / Optic structs ===

        public readonly partial struct EFTCameraManager
        {
            public const uint OpticCameraManager = 0x10; // OpticCameraManager
            public const uint Camera = 0x60; // UnityEngine.Camera - FPS Camera
            public const uint GetInstance_RVA = 0x3CB1050; // RVA for GetInstance
            public const uint CameraDerefOffset = 0x10; // dereference offset
        }

        public readonly partial struct OpticCameraManagerContainer
        {
            public const uint Instance = 0x0; // singleton
            public const uint OpticCameraManager = 0x10; // OpticCameraManager
            public const uint FPSCamera = 0x60; // UnityEngine.Camera
        }

        public readonly partial struct OpticCameraManager
        {
            public const uint Camera = 0x70; // UnityEngine.Camera
            public const uint CurrentOpticSight = 0x78; // EFT.CameraControl.OpticSight
        }

        public readonly partial struct OpticSight
        {
            public const uint LensRenderer = 0x20; // UnityEngine.Renderer
        }

        // === GPU Instancer structs ===

        public static class GPUInstancerManagerOffsets
        {
            public const uint runtimeDataList = 0x58; // List<GPUInstancerRuntimeData>
        }

        public readonly partial struct GPUInstancerRuntimeData
        {
            public const uint instanceBounds = 0x20; // Bounds
        }

        // === Network / Screen structs ===

        public readonly partial struct NetworkContainer
        {
            public const uint NextRequestIndex = 0x8; // Int64
            public const uint PhpSessionId = 0x30; // String
            public const uint AppVersion = 0x38; // String
        }

        public readonly partial struct ScreenManagerOffsets
        {
            public const uint Instance = 0x0; // singleton
            public const uint CurrentScreenController = 0x28; // ScreenController
        }

        public readonly partial struct CurrentScreenController
        {
            public const uint Generic = 0x20; // Var
        }

        public readonly partial struct ClientBackendSession
        {
            public const uint BackEndConfig = 0x158; // BackendConfig
        }

        // === Medical / Quest structs ===

        public readonly partial struct MedicalTemplate
        {
            public const uint UseTime = 0x148; // Single
            public const uint BodyPartTimeMults = 0x150; // KeyValuePair[]
            public const uint HealthEffects = 0x158; // Dictionary
            public const uint DamageEffects = 0x160; // Dictionary
            public const uint StimulatorBuffs = 0x168; // String
            public const uint MaxHpResource = 0x170; // Int32
            public const uint HpResourceRate = 0x174; // Single
        }

        public readonly partial struct QuestData
        {
            public const uint QuestId = 0x10; // String
            public const uint QuestStatus = 0x1C; // EQuestStatus
            public const uint CompletedConditions = 0x28; // CompletedConditionsCollection
            public const uint Template = 0x38; // QuestTemplate
        }

        public readonly partial struct CompletedConditionsCollection
        {
            public const uint BackendData = 0x10; // HashSet<MongoID>
            public const uint LocalChanges = 0x18; // HashSet<MongoID>
        }

        public readonly partial struct QuestTemplate
        {
            public const uint Conditions = 0x60; // ConditionsDict
            public const uint QuestName = 0xC8; // String
        }

        public readonly partial struct QuestConditionsContainer
        {
            public const uint ConditionsList = 0x70; // IEnumerable<Condition>
        }

        public readonly partial struct QuestCondition
        {
            public const uint id = 0x10; // MongoID
        }

        public readonly partial struct QuestConditionItem
        {
            public const uint value = 0x58; // Single
        }

        public readonly partial struct QuestConditionFindItem
        {
            public const uint target = 0x98; // String[]
        }

        public readonly partial struct QuestConditionZone
        {
            public const uint target = 0x98; // String[]
            public const uint zoneId = 0xA0; // String
        }

        public readonly partial struct QuestConditionInZone
        {
            public const uint zoneIds = 0x98; // String[]
        }

        public readonly partial struct QuestConditionLaunchFlare
        {
            public const uint zoneId = 0x98; // String
        }

        public readonly partial struct QuestConditionVisitPlace
        {
            public const uint target = 0x98; // String
        }

        public readonly partial struct QuestConditionPlaceBeacon
        {
            public const uint zoneId = 0x98; // String
            public const uint plantTime = 0xA8; // Single
        }

        public readonly partial struct QuestConditionCounterCreator
        {
            public const uint Conditions = 0xA0; // ConditionCollection
        }

        public readonly partial struct QuestConditionCounterTemplate
        {
            public const uint Conditions = 0x10; // Conditions
        }

        // === Misc offsets ===

        public readonly partial struct FireModeComponent
        {
            public const uint FireMode = 0x28; // Byte
        }

        public readonly partial struct MagazineClass
        {
            public const uint StackObjectsCount = 0x24; // Int32
        }

        public readonly partial struct GenericCollectionContainer
        {
            public const uint List = 0x18; // List<Var>
        }

        public readonly partial struct Stash
        {
            public const uint Slots = 0x80; // Slot[]
            public const uint Grids = 0x98; // Grid[]
        }

        public readonly partial struct Equipment
        {
            public const uint Grids = 0x78; // Grid[]
            public const uint Slots = 0x80; // Slot[]
        }
    }

    public readonly partial struct Enums
    {
        public enum EPlayerSide
        {
            Usec = 1,
            Bear = 2,
            Savage = 4,
        }

        public enum EPlayerState : byte
        {
            None = 0,
            Idle = 1,
            ProneIdle = 2,
            ProneMove = 3,
            Run = 4,
            Sprint = 5,
            Jump = 6,
            FallDown = 7,
            Transition = 8,
            BreachDoor = 9,
            Loot = 10,
            Pickup = 11,
            Open = 12,
            Close = 13,
            Unlock = 14,
            Sidestep = 15,
            DoorInteraction = 16,
            Approach = 17,
            Prone2Stand = 18,
            Transit2Prone = 19,
            Plant = 20,
            Stationary = 21,
            Roll = 22,
            JumpLanding = 23,
            ClimbOver = 24,
            ClimbUp = 25,
            VaultingFallDown = 26,
            VaultingLanding = 27,
            BlindFire = 28,
            IdleWeaponMounting = 29,
            IdleZombieState = 30,
            MoveZombieState = 31,
            TurnZombieState = 32,
            StartMoveZombieState = 33,
            EndMoveZombieState = 34,
            DoorInteractionZombieState = 35,
        }

        [Flags]
        public enum ETagStatus
        {
            Unaware = 1,
            Aware = 2,
            Combat = 4,
            Solo = 8,
            Coop = 16,
            Bear = 32,
            Usec = 64,
            Scav = 128,
            TargetSolo = 256,
            TargetMultiple = 512,
            Healthy = 1024,
            Injured = 2048,
            BadlyInjured = 4096,
            Dying = 8192,
            Birdeye = 16384,
            Knight = 32768,
            BigPipe = 65536,
            BlackDivision = 131072,
            VSRF = 262144
        }

        [Flags]
        public enum EMemberCategory
        {
            Default = 0,
            Developer = 1,
            UniqueId = 2,
            Trader = 4,
            Group = 8,
            System = 16,
            ChatModerator = 32,
            ChatModeratorWithPermanentBan = 64,
            UnitTest = 128,
            Sherpa = 256,
            Emissary = 512,
            Unheard = 1024,
        }

        public enum EExfiltrationStatus
        {
            NotPresent = 1,
            UncompleteRequirements = 2,
            Countdown = 3,
            RegularMode = 4,
            Pending = 5,
            AwaitsManualActivation = 6,
            Hidden = 7,
        }

        public enum SynchronizableObjectType
        {
            AirDrop = 0,
            AirPlane = 1,
            Tripwire = 2,
        }

        public enum ETripwireState
        {
            None = 0,
            Wait = 1,
            Active = 2,
            Exploding = 3,
            Exploded = 4,
            Inert = 5,
        }

        public enum EQuestStatus
        {
            Locked = 0,
            AvailableForStart = 1,
            Started = 2,
            AvailableForFinish = 3,
            Success = 4,
            Fail = 5,
            FailRestartable = 6,
            MarkedAsFailed = 7,
            Expired = 8,
            AvailableAfter = 9,
        }

        [Flags]
        public enum EProceduralAnimationMask
        {
            Breathing = 1,
            Walking = 2,
            MotionReaction = 4,
            ForceReaction = 8,
            Shooting = 16,
            DrawDown = 32,
            Aiming = 64,
            HandShake = 128,
        }

        /// <summary>
        /// AI Spawn Type enum - used for boss/raider detection in both online and offline modes.
        /// </summary>
        public enum ESpawnType : uint
        {
            Marksman = 0,
            Assault = 1,
            BossTest = 2,
            Reshala = 3,
            FollowerTest = 4,
            FollowerBully = 5,
            Killa = 6,
            Shturman = 7,
            FollowerKojaniy = 8,
            PmcBot = 9,
            CursedAssault = 10,
            Gluhar = 11,
            FollowerGluharAssault = 12,
            FollowerGluharSecurity = 13,
            FollowerGluharScout = 14,
            FollowerGluharSnipe = 15,
            FollowerSanitar = 16,
            Sanitar = 17,
            Test = 18,
            AssaultGroup = 19,
            SectantWarrior = 20,
            SectantPriest = 21,
            Tagilla = 22,
            FollowerTagilla = 23,
            ExUsec = 24,
            Gifter = 25,
            Knight = 26,
            BigPipe = 27,
            BirdEye = 28,
            Zryachiy = 29,
            FollowerZryachiy = 30,
            Kaban = 32,
            FollowerBoar = 33,
            ArenaFighter = 34,
            ArenaFighterEvent = 35,
            BossBoarSniper = 36,
            CrazyAssaultEvent = 37,
            PeacefullZryachiyEvent = 38,
            SectactPriestEvent = 39,
            RavangeZryachiyEvent = 40,
            FollowerBoarClose1 = 41,
            FollowerBoarClose2 = 42,
            Kolontay = 43,
            FollowerKolontayAssault = 44,
            FollowerKolontaySecurity = 45,
            ShooterBTR = 46,
            Partisan = 47,
            SpiritWinter = 48,
            SpiritSpring = 49,
            Peacemaker = 50,
            PmcBEAR = 51,
            PmcUSEC = 52,
            Skier = 53,
            SectantPredvestnik = 57,
            SectantPrizrak = 58,
            SectantOni = 59,
            InfectedAssault = 60,
            InfectedPmc = 61,
            InfectedCivil = 62,
            InfectedLaborant = 63,
            InfectedTagilla = 64,
            BossTagillaAgro = 65,
            BossKillaAgro = 66,
            TagillaHelperAgro = 67,
            BlackDivision = 68,
            VsRF = 69,
            VsRFSniper = 70,
            AssaultTutorial = 71,
            Sentry = 72,
            VsRFFight = 73,
            Civilian = 74,
            UNKNOWN = uint.MaxValue
        }
    }
}
