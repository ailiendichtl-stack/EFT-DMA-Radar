/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System;
using System.Buffers;
using System.Linq;
using System.Threading;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.Web.TarkovDev.Data;
using VmmSharpEx.Scatter;
using static LoneEftDmaRadar.Tarkov.Unity.Structures.UnityTransform;

namespace LoneEftDmaRadar.UI.Misc
{
    /// <summary>
    /// Immutable snapshot of firearm state. Thread-safe for concurrent readers.
    /// Consumers (ESP, aimbot) read from this - never see partial/invalid states.
    /// </summary>
    public sealed class FirearmSnapshot
    {
        public static readonly FirearmSnapshot Empty = new();

        public ulong HandsController { get; }
        public bool IsWeapon { get; }
        public ulong ItemAddr { get; }
        public string ItemId { get; }

        // Magazine info
        public int CurrentAmmo { get; }
        public int MaxAmmo { get; }
        public string AmmoTypeName { get; }
        public bool HasValidAmmo => MaxAmmo > 0 && CurrentAmmo >= 0;

        // Fireport info
        public Vector3? FireportPosition { get; }
        public Quaternion? FireportRotation { get; }

        private FirearmSnapshot()
        {
            // Empty snapshot for when no weapon is held
        }

        public FirearmSnapshot(
            ulong handsController,
            bool isWeapon,
            ulong itemAddr,
            string itemId,
            int currentAmmo,
            int maxAmmo,
            string ammoTypeName,
            Vector3? fireportPosition,
            Quaternion? fireportRotation)
        {
            HandsController = handsController;
            IsWeapon = isWeapon;
            ItemAddr = itemAddr;
            ItemId = itemId;
            CurrentAmmo = currentAmmo;
            MaxAmmo = maxAmmo;
            AmmoTypeName = ammoTypeName;
            FireportPosition = fireportPosition;
            FireportRotation = fireportRotation;
        }
    }

    public sealed class FirearmManager
    {
        private readonly LocalPlayer _localPlayer;
        private volatile FirearmSnapshot _snapshot = FirearmSnapshot.Empty;
        private ulong _lastHandsAddr;
        private UnityTransform _fireportTransform;

        /// <summary>
        /// Current immutable snapshot of firearm state.
        /// Thread-safe - consumers always see complete, valid state.
        /// </summary>
        public FirearmSnapshot CurrentSnapshot => _snapshot;

        /// <summary>
        /// Returns the Hands Controller Address and if the held item is a weapon.
        /// </summary>
        public Tuple<ulong, bool> HandsController => new(_snapshot.HandsController, _snapshot.IsWeapon);

        public FirearmManager(LocalPlayer localPlayer)
        {
            _localPlayer = localPlayer;
        }

        /// <summary>
        /// Add fireport vertex read to T1 scatter for realtime position updates.
        /// Transform acquisition and ammo info handled by Update() on T2.
        /// </summary>
        public void OnRealtimeLoop(VmmScatter scatter)
        {
            var transform = _fireportTransform;
            if (transform == null)
                return;

            var addr = transform.VerticesAddr;
            var count = transform.Count;
            scatter.PrepareReadArray<TrsX>(addr, count);
            scatter.Completed += (_, s) =>
            {
                if (s.ReadPooled<TrsX>(addr, count) is IMemoryOwner<TrsX> vertices)
                {
                    using (vertices)
                    {
                        try
                        {
                            var span = vertices.Memory.Span;
                            if (span.Length >= count)
                            {
                                var pos = transform.UpdatePosition(span);
                                var rot = transform.GetRotation(span);

                                if (Vector3.Distance(pos, _localPlayer.Position) <= 100f)
                                {
                                    var old = _snapshot;
                                    _snapshot = new FirearmSnapshot(
                                        old.HandsController, old.IsWeapon, old.ItemAddr, old.ItemId,
                                        old.CurrentAmmo, old.MaxAmmo, old.AmmoTypeName, pos, rot);
                                }
                            }
                        }
                        catch { }
                    }
                }
            };
        }

        /// <summary>
        /// Update Hands/Firearm/Magazine information for LocalPlayer.
        /// Builds a complete immutable snapshot and publishes it atomically.
        /// </summary>
        public void Update()
        {
            try
            {
                var hands = _localPlayer.HandsController;
                if (!MemDMA.IsValidVirtualAddress(hands))
                {
                    // Keep current snapshot - don't publish empty state on transient read failure
                    return;
                }

                // Detect weapon change - invalidate cached fireport transform
                bool weaponJustChanged = false;
                if (hands != _lastHandsAddr)
                {
                    _fireportTransform = null;
                    _lastHandsAddr = hands;
                    weaponJustChanged = true;
                    DebugLogger.LogDebug("[FirearmManager] Hands changed, will reacquire fireport");
                }

                // Get hands info (item ID, is weapon, etc.)
                var handsInfo = GetHandsInfo(hands);

                // Initialize snapshot values
                ulong itemAddr = handsInfo.ItemAddr;
                string itemId = handsInfo.ItemId;
                bool isWeapon = handsInfo.IsWeapon;
                int currentAmmo = 0;
                int maxAmmo = 0;
                string ammoTypeName = null;
                Vector3? fireportPos = null;
                Quaternion? fireportRot = null;

                if (isWeapon)
                {
                    // Update/acquire fireport transform
                    fireportPos = UpdateFireport(hands, ref fireportRot);

                    // Read ammo info
                    ReadAmmoInfo(hands, out currentAmmo, out maxAmmo, out ammoTypeName);

                    // If fireport acquisition failed but we have previous valid data from SAME weapon, carry it forward
                    // This prevents flickering null values during transient DMA read failures
                    // Don't carry forward if weapon just changed (would be old weapon's fireport)
                    if (fireportPos == null && !weaponJustChanged && _snapshot.IsWeapon && _snapshot.FireportPosition.HasValue)
                    {
                        fireportPos = _snapshot.FireportPosition;
                        fireportRot = _snapshot.FireportRotation;
                    }
                }

                // Build and publish immutable snapshot atomically
                var newSnapshot = new FirearmSnapshot(
                    handsController: hands,
                    isWeapon: isWeapon,
                    itemAddr: itemAddr,
                    itemId: itemId,
                    currentAmmo: currentAmmo,
                    maxAmmo: maxAmmo,
                    ammoTypeName: ammoTypeName,
                    fireportPosition: fireportPos,
                    fireportRotation: fireportRot
                );

                // Atomic publish - consumers always see complete state
                _snapshot = newSnapshot;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[FirearmManager] ERROR: {ex}");
                // Keep current snapshot on error - don't publish invalid state
            }
        }

        /// <summary>
        /// Updates fireport transform and returns current position/rotation.
        /// </summary>
        private Vector3? UpdateFireport(ulong hands, ref Quaternion? rotation)
        {
            try
            {
                // Validate existing transform
                if (_fireportTransform is UnityTransform transform)
                {
                    var v = MemoryInterface.Memory.ReadPtrChain(hands, false, Offsets.FirearmController.To_FirePortVertices);
                    if (transform.VerticesAddr != v)
                    {
                        _fireportTransform = null;
                    }
                }

                // Acquire new transform if needed
                if (_fireportTransform is null)
                {
                    var t = MemoryInterface.Memory.ReadPtrChain(hands, false, Offsets.FirearmController.To_FirePortTransformInternal);
                    _fireportTransform = new(t, false);
                }

                // Read position and rotation
                var pos = _fireportTransform.UpdatePosition();
                rotation = _fireportTransform.GetRotation();

                // Sanity check - if too far from player, data is invalid
                if (Vector3.Distance(pos, _localPlayer.Position) > 100f)
                {
                    _fireportTransform = null;
                    return null;
                }

                return pos;
            }
            catch
            {
                _fireportTransform = null;
                return null;
            }
        }

        /// <summary>
        /// Reads current ammo count and type from weapon's magazine.
        /// Iterates all items in magazine (handles split stacks) and checks chambers for +1.
        /// </summary>
        private void ReadAmmoInfo(ulong handsController, out int currentAmmo, out int maxAmmo, out string ammoTypeName)
        {
            currentAmmo = 0;
            maxAmmo = 0;
            ammoTypeName = null;

            try
            {
                // Read the held item (weapon)
                var itemBase = MemoryInterface.Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
                if (!MemDMA.IsValidVirtualAddress(itemBase))
                    return;

                // Read the magazine slot → magazine item → cartridges (StackSlot)
                var magSlot = MemoryInterface.Memory.ReadPtr(itemBase + Offsets.LootItemWeapon._magSlotCache, false);
                if (!MemDMA.IsValidVirtualAddress(magSlot))
                    return;
                var magItem = MemoryInterface.Memory.ReadPtr(magSlot + Offsets.Slot.ContainedItem, false);
                if (!MemDMA.IsValidVirtualAddress(magItem))
                    return;
                var cartridges = MemoryInterface.Memory.ReadPtr(magItem + Offsets.Item.Cartridges, false);
                if (!MemDMA.IsValidVirtualAddress(cartridges))
                    return;

                // Read max ammo capacity
                maxAmmo = MemoryInterface.Memory.ReadValue<int>(cartridges + Offsets.StackSlot.Max, false);

                // Iterate all items in magazine and sum StackCount (handles split stacks)
                ulong firstAmmoItem = 0;
                var itemsList = MemoryInterface.Memory.ReadPtr(cartridges + Offsets.StackSlot.Items, false);
                if (MemDMA.IsValidVirtualAddress(itemsList))
                {
                    int itemCount = MemoryInterface.Memory.ReadValue<int>(itemsList + 0x18, false); // List<T>._size
                    if (itemCount > 0)
                    {
                        var listArray = MemoryInterface.Memory.ReadPtr(itemsList + 0x10, false); // List<T>._items
                        if (MemDMA.IsValidVirtualAddress(listArray))
                        {
                            int maxItems = Math.Min(itemCount, 10); // Cap iteration
                            for (int i = 0; i < maxItems; i++)
                            {
                                var ammoPtr = MemoryInterface.Memory.ReadPtr(listArray + 0x20 + (ulong)(i * 8), false);
                                if (MemDMA.IsValidVirtualAddress(ammoPtr))
                                {
                                    if (firstAmmoItem == 0)
                                        firstAmmoItem = ammoPtr;
                                    currentAmmo += MemoryInterface.Memory.ReadValue<int>(ammoPtr + Offsets.Item.StackCount, false);
                                }
                            }
                        }
                    }
                }

                // Check chambers for +1 bullet
                ulong chamberBullet = 0;
                try
                {
                    var chambersArr = MemoryInterface.Memory.ReadPtr(itemBase + Offsets.LootItemWeapon.Chambers, false);
                    if (MemDMA.IsValidVirtualAddress(chambersArr))
                    {
                        int chamberCount = MemoryInterface.Memory.ReadValue<int>(chambersArr + 0x18, false);
                        if (chamberCount > 0)
                        {
                            var chamberSlot = MemoryInterface.Memory.ReadPtr(chambersArr + 0x20, false);
                            if (MemDMA.IsValidVirtualAddress(chamberSlot))
                            {
                                chamberBullet = MemoryInterface.Memory.ReadValue<ulong>(chamberSlot + Offsets.Slot.ContainedItem, false);
                                if (chamberBullet != 0)
                                    currentAmmo++;
                            }
                        }
                    }
                }
                catch { }

                // Sanity validation
                if (maxAmmo <= 0 || maxAmmo > 200 || currentAmmo < 0 || currentAmmo > maxAmmo + 1)
                {
                    currentAmmo = 0;
                    maxAmmo = 0;
                    return;
                }

                // Read ammo type name from first available round (magazine or chamber)
                try
                {
                    var roundForLookup = firstAmmoItem != 0 ? firstAmmoItem : chamberBullet;
                    if (roundForLookup == 0)
                        return;

                    var ammoTemplate = MemoryInterface.Memory.ReadPtr(roundForLookup + Offsets.LootItem.Template, false);
                    if (MemDMA.IsValidVirtualAddress(ammoTemplate))
                    {
                        var ammoIdPtr = MemoryInterface.Memory.ReadValue<MongoID>(ammoTemplate + Offsets.ItemTemplate._id, false);
                        var ammoId = ammoIdPtr.ReadString(64, false);
                        if (ammoId?.Length == 24 && TarkovDataManager.AllItems.TryGetValue(ammoId, out var ammoItem))
                        {
                            ammoTypeName = string.IsNullOrWhiteSpace(ammoItem.ShortName) ? ammoItem.Name : ammoItem.ShortName;
                        }
                    }
                }
                catch { }
            }
            catch
            {
                currentAmmo = 0;
                maxAmmo = 0;
                ammoTypeName = null;
            }
        }

        /// <summary>
        /// Get updated hands information.
        /// </summary>
        private static CachedHandsInfo GetHandsInfo(ulong handsController)
        {
            try
            {
                var itemBase = MemoryInterface.Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
                if (!MemDMA.IsValidVirtualAddress(itemBase))
                    return new(handsController);

                var itemTemp = MemoryInterface.Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
                if (!MemDMA.IsValidVirtualAddress(itemTemp))
                    return new(handsController);

                var itemIdPtr = MemoryInterface.Memory.ReadValue<MongoID>(itemTemp + Offsets.ItemTemplate._id, false);
                var itemId = itemIdPtr.ReadString(64, false);

                if (itemId.Length != 24)
                    return new(handsController);

                if (!TarkovDataManager.AllItems.TryGetValue(itemId, out var heldItem))
                    return new(handsController);

                return new(handsController, heldItem, itemBase, itemId);
            }
            catch
            {
                return new(handsController);
            }
        }

        #region Magazine Utilities

        /// <summary>
        /// Static utility class for magazine/ammo operations.
        /// </summary>
        public static class MagazineManager
        {
            /// <summary>
            /// Returns the Ammo Template from a Weapon (First loaded round).
            /// </summary>
            /// <param name="lootItemBase">EFT.InventoryLogic.Weapon instance</param>
            /// <returns>Ammo Template Ptr</returns>
            public static ulong GetAmmoTemplateFromWeapon(ulong lootItemBase)
            {
                var chambersPtr = MemoryInterface.Memory.ReadValue<ulong>(lootItemBase + Offsets.LootItemWeapon.Chambers, false);
                ulong firstRound = 0;
                UnityArray<Chamber> chambers = null;
                UnityArray<Chamber> magChambers = null;
                UnityList<ulong> magStack = null;

                try
                {
                    if (chambersPtr != 0x0)
                    {
                        chambers = UnityArray<Chamber>.Create(chambersPtr, true);
                        if (chambers.Count > 0)
                        {
                            var chamberWithBullet = chambers.Span.ToArray().FirstOrDefault(x => x.HasBullet(true));
                            if (chamberWithBullet._base != 0)
                            {
                                firstRound = MemoryInterface.Memory.ReadPtr(chamberWithBullet._base + Offsets.Slot.ContainedItem, false);
                                return MemoryInterface.Memory.ReadPtr(firstRound + Offsets.LootItem.Template, false);
                            }
                        }
                    }

                    // Try magazine
                    var magSlot = MemoryInterface.Memory.ReadPtr(lootItemBase + Offsets.LootItemWeapon._magSlotCache, false);
                    var magItemPtr = MemoryInterface.Memory.ReadPtr(magSlot + Offsets.Slot.ContainedItem, false);
                    var magChambersPtr = MemoryInterface.Memory.ReadPtr(magItemPtr + Offsets.LootItemMod.Slots, false);

                    magChambers = UnityArray<Chamber>.Create(magChambersPtr, true);
                    
                    if (magChambers.Count > 0) // Revolvers, etc.
                    {
                        var chamberWithBullet = magChambers.Span.ToArray().FirstOrDefault(x => x.HasBullet(true));
                        if (chamberWithBullet._base != 0)
                        {
                            firstRound = MemoryInterface.Memory.ReadPtr(chamberWithBullet._base + Offsets.Slot.ContainedItem, false);
                        }
                    }
                    else // Regular magazines
                    {
                        var cartridges = MemoryInterface.Memory.ReadPtr(magItemPtr + Offsets.LootItemMagazine.Cartridges, false);
                        var magStackPtr = MemoryInterface.Memory.ReadPtr(cartridges + Offsets.StackSlot._items, false);
                        magStack = UnityList<ulong>.Create(magStackPtr, true);
                        if (magStack.Count > 0)
                            firstRound = magStack.Span[0];
                    }

                    if (firstRound != 0)
                        return MemoryInterface.Memory.ReadPtr(firstRound + Offsets.LootItem.Template, false);

                    return 0;
                }
                finally
                {
                    chambers?.Dispose();
                    magChambers?.Dispose();
                    magStack?.Dispose();
                }
            }

            /// <summary>
            /// Wrapper defining a Chamber Structure.
            /// </summary>
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            public readonly struct Chamber
            {
                public readonly ulong _base;

                public readonly bool HasBullet(bool useCache = false)
                {
                    if (_base == 0x0)
                        return false;
                    return MemoryInterface.Memory.ReadValue<ulong>(_base + Offsets.Slot.ContainedItem, useCache) != 0x0;
                }
            }
        }

        #endregion

        #region Hands Cache

        public sealed class CachedHandsInfo
        {
            public static implicit operator ulong(CachedHandsInfo x) => x?._hands ?? 0x0;

            private readonly ulong _hands;
            private readonly TarkovMarketItem _item;

            /// <summary>
            /// Address of currently held item (if any).
            /// </summary>
            public ulong ItemAddr { get; }

            /// <summary>
            /// BSG Item ID (24 character string).
            /// </summary>
            public string ItemId { get; }

            /// <summary>
            /// True if the Item being currently held (if any) is a weapon, otherwise False.
            /// </summary>
            public bool IsWeapon => _item?.IsWeapon ?? false;

            public CachedHandsInfo(ulong handsController)
            {
                _hands = handsController;
            }

            public CachedHandsInfo(ulong handsController, TarkovMarketItem item, ulong itemAddr, string itemId)
            {
                _hands = handsController;
                _item = item;
                ItemAddr = itemAddr;
                ItemId = itemId;
            }
        }

        #endregion
    }
}
