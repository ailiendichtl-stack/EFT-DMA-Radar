/*
 * Lone EFT DMA Radar
 * MIT License - Copyright (c) 2025 Lone DMA
 */

using System;
using System.Linq;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.Web.TarkovDev.Data;

namespace LoneEftDmaRadar.UI.Misc
{
    public sealed class FirearmManager
    {
        private readonly LocalPlayer _localPlayer;
        private CachedHandsInfo _hands;

        /// <summary>
        /// Returns the Hands Controller Address and if the held item is a weapon.
        /// </summary>
        public Tuple<ulong, bool> HandsController => new(_hands, _hands?.IsWeapon ?? false);

        /// <summary>
        /// Cached Hands Information.
        /// </summary>
        public CachedHandsInfo CurrentHands => _hands;

        /// <summary>
        /// Magazine (if any) contained in this firearm.
        /// </summary>
        public MagazineManager Magazine { get; private set; }

        /// <summary>
        /// Current Firearm Fireport Transform.
        /// </summary>
        public UnityTransform FireportTransform { get; private set; }

        /// <summary>
        /// Last known Fireport Position.
        /// </summary>
        public Vector3? FireportPosition { get; private set; }

        /// <summary>
        /// Last known Fireport Rotation.
        /// </summary>
        public Quaternion? FireportRotation { get; private set; }

        public FirearmManager(LocalPlayer localPlayer)
        {
            _localPlayer = localPlayer;
            Magazine = new(localPlayer);
        }


        /// <summary>
        /// Update Hands/Firearm/Magazine information for LocalPlayer.
        /// </summary>
        public void Update()
        {
            try
            {
                var hands = _localPlayer.HandsController;
                if (!MemDMA.IsValidVirtualAddress(hands))
                {
                    DebugLogger.LogDebug("[FirearmManager] Invalid HandsController address");
                    return;
                }

                if (hands != _hands)
                {
                    _hands = null;
                    ResetFireport();
                    Magazine = new(_localPlayer);
                    _hands = GetHandsInfo(hands);
                    DebugLogger.LogDebug($"[FirearmManager] New hands detected. IsWeapon: {_hands?.IsWeapon}");
                }

                if (_hands?.IsWeapon == true)
                {
                    if (FireportTransform is UnityTransform fireportTransform) // Validate Fireport Transform
                    {
                        try
                        {
                            var v = MemoryInterface.Memory.ReadPtrChain(hands, false, Offsets.FirearmController.To_FirePortVertices);
                            if (fireportTransform.VerticesAddr != v)
                                ResetFireport();
                        }
                        catch
                        {
                            ResetFireport();
                        }
                    }

                    if (FireportTransform is null)
                    {
                        try
                        {
                            var t = MemoryInterface.Memory.ReadPtrChain(hands, false, Offsets.FirearmController.To_FirePortTransformInternal);
                            FireportTransform = new(t, false);

                            // ✅ Update position AND rotation once to validate
                            var pos = FireportTransform.UpdatePosition();
                            var rot = FireportTransform.GetRotation();

                            // If the fireport is implausibly far (common briefly during weapon swaps), drop and reacquire next tick.
                            if (Vector3.Distance(pos, _localPlayer.Position) > 100f)
                            {
                                ResetFireport();
                                return;
                            }

                            FireportPosition = pos;
                            FireportRotation = rot; // ✅ Store rotation
                        }
                        catch
                        {
                            ResetFireport();
                        }
                    }
                    else
                    {
                        // ✅ Update fireport position AND rotation every frame
                        try
                        {
                            FireportPosition = FireportTransform.UpdatePosition();
                            FireportRotation = FireportTransform.GetRotation();

                            // Sanity: if it jumps far away, reset so we reacquire with the new weapon.
                            if (Vector3.Distance(FireportPosition.Value, _localPlayer.Position) > 100f)
                            {
                                ResetFireport();
                            }
                        }
                        catch
                        {
                            ResetFireport();
                        }
                    }

                    // Update ammo count for the ammo counter widget
                    Magazine?.UpdateAmmoCount();
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"[FirearmManager] ERROR: {ex}");
            }
        }

        /// <summary>
        /// Reset the Fireport Data.
        /// </summary>
        private void ResetFireport()
        {
            FireportTransform = null;
            FireportPosition = null;
            FireportRotation = null;
        }

        /// <summary>
        /// Get updated hands information.
        /// </summary>
        private static CachedHandsInfo GetHandsInfo(ulong handsController)
        {
            var itemBase = MemoryInterface.Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
            var itemTemp = MemoryInterface.Memory.ReadPtr(itemBase + Offsets.LootItem.Template, false);
            var itemIdPtr = MemoryInterface.Memory.ReadValue<MongoID>(itemTemp + Offsets.ItemTemplate._id, false);
            var itemId = itemIdPtr.ReadString(64, false); // Use ReadString() method
            
            ArgumentOutOfRangeException.ThrowIfNotEqual(itemId.Length, 24, nameof(itemId));
            
            if (!TarkovDataManager.AllItems.TryGetValue(itemId, out var heldItem))
                return new(handsController);
            
            return new(handsController, heldItem, itemBase, itemId);
        }

        #region Magazine Info

        /// <summary>
        /// Helper class to track a Player's Magazine Ammo Count.
        /// </summary>
        public sealed class MagazineManager
        {
            private readonly LocalPlayer _localPlayer;

            /// <summary>
            /// Current ammo count in magazine.
            /// </summary>
            public int CurrentAmmo { get; private set; }

            /// <summary>
            /// Maximum ammo capacity of magazine.
            /// </summary>
            public int MaxAmmo { get; private set; }

            /// <summary>
            /// Short name of the currently loaded ammo type.
            /// </summary>
            public string AmmoTypeName { get; private set; }

            /// <summary>
            /// True if valid ammo data is available.
            /// </summary>
            public bool HasValidAmmo => MaxAmmo > 0 && CurrentAmmo >= 0;

            public MagazineManager(LocalPlayer localPlayer)
            {
                _localPlayer = localPlayer;
            }

            /// <summary>
            /// Updates the current and max ammo count from memory.
            /// Uses pointer chain: HandsController -> Item -> MagSlot -> Magazine -> Cartridges
            /// </summary>
            public void UpdateAmmoCount()
            {
                CurrentAmmo = 0;
                MaxAmmo = 0;
                AmmoTypeName = null;

                try
                {
                    var handsController = _localPlayer.HandsController;
                    if (!MemDMA.IsValidVirtualAddress(handsController))
                        return;

                    // Read the held item (weapon)
                    var itemBase = MemoryInterface.Memory.ReadPtr(handsController + Offsets.ItemHandsController.Item, false);
                    if (!MemDMA.IsValidVirtualAddress(itemBase))
                        return;

                    // Read the magazine slot
                    var magSlot = MemoryInterface.Memory.ReadPtr(itemBase + Offsets.LootItemWeapon._magSlotCache, false);
                    if (!MemDMA.IsValidVirtualAddress(magSlot))
                        return;

                    // Read the magazine item from slot
                    var magItem = MemoryInterface.Memory.ReadPtr(magSlot + Offsets.Slot.ContainedItem, false);
                    if (!MemDMA.IsValidVirtualAddress(magItem))
                        return;

                    // Read cartridges (StackSlot) from magazine
                    var cartridges = MemoryInterface.Memory.ReadPtr(magItem + Offsets.Item.Cartridges, false);
                    if (!MemDMA.IsValidVirtualAddress(cartridges))
                        return;

                    // Read max ammo from StackSlot.Max
                    MaxAmmo = MemoryInterface.Memory.ReadValue<int>(cartridges + Offsets.StackSlot.Max, false);

                    // Read items list from StackSlot.Items
                    var itemsList = MemoryInterface.Memory.ReadPtr(cartridges + Offsets.StackSlot.Items, false);
                    if (!MemDMA.IsValidVirtualAddress(itemsList))
                        return;

                    // Read the internal array from List<T> (offset 0x10)
                    var listItems = MemoryInterface.Memory.ReadPtr(itemsList + 0x10, false);
                    if (!MemDMA.IsValidVirtualAddress(listItems))
                        return;

                    // Read first ammo item (offset 0x20 from array base)
                    var firstAmmoItem = MemoryInterface.Memory.ReadPtr(listItems + 0x20, false);
                    if (!MemDMA.IsValidVirtualAddress(firstAmmoItem))
                        return;

                    // Read current ammo count from Item.StackCount
                    CurrentAmmo = MemoryInterface.Memory.ReadValue<int>(firstAmmoItem + Offsets.Item.StackCount, false);

                    // Read ammo type name from template
                    try
                    {
                        var ammoTemplate = MemoryInterface.Memory.ReadPtr(firstAmmoItem + Offsets.LootItem.Template, false);
                        if (MemDMA.IsValidVirtualAddress(ammoTemplate))
                        {
                            var ammoIdPtr = MemoryInterface.Memory.ReadValue<MongoID>(ammoTemplate + Offsets.ItemTemplate._id, false);
                            var ammoId = ammoIdPtr.ReadString(64, false);
                            if (ammoId?.Length == 24 && TarkovDataManager.AllItems.TryGetValue(ammoId, out var ammoItem))
                            {
                                AmmoTypeName = string.IsNullOrWhiteSpace(ammoItem.ShortName) ? ammoItem.Name : ammoItem.ShortName;
                            }
                        }
                    }
                    catch
                    {
                        // Ammo type lookup failed, but we still have count
                    }
                }
                catch
                {
                    CurrentAmmo = 0;
                    MaxAmmo = 0;
                    AmmoTypeName = null;
                }
            }

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
