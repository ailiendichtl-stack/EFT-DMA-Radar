using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.Features.MemWrites
{
    /// <summary>
    /// Disables weapon malfunctions (jams, feed failures, misfires, slide locks)
    /// by writing AllowJam/AllowFeed/AllowMisfire/AllowSlide = false on weapon templates.
    /// Template writes persist, so we track patched templates to avoid redundant writes.
    /// </summary>
    public sealed class NoWeaponMalfunctions : MemWriteFeature<NoWeaponMalfunctions>
    {
        private bool _lastEnabledState;
        private readonly HashSet<ulong> _patchedTemplates = new();

        public override bool Enabled
        {
            get => App.Config.MemWrites.NoWeaponMalfunctionsEnabled;
            set => App.Config.MemWrites.NoWeaponMalfunctionsEnabled = value;
        }

        protected override TimeSpan Delay => TimeSpan.FromMilliseconds(250);

        public override void TryApply(LocalPlayer localPlayer)
        {
            try
            {
                if (!Enabled)
                {
                    if (_lastEnabledState)
                        _lastEnabledState = false;
                    return;
                }

                _lastEnabledState = true;

                // Patch currently held weapon
                var handsController = localPlayer.Hands;
                if (MemDMA.IsValidVirtualAddress(handsController))
                {
                    TryPatchWeaponFromController(handsController);
                }

                // Patch weapons in equipment slots (FirstPrimaryWeapon, SecondPrimaryWeapon, Holster)
                TryPatchEquipmentWeapons(localPlayer);
            }
            catch
            {
                // Silent - template writes persist anyway
            }
        }

        private void TryPatchWeaponFromController(ulong handsController)
        {
            try
            {
                var item = Memory.ReadPtr(handsController + SDK.Offsets.ItemHandsController.Item, false);
                if (MemDMA.IsValidVirtualAddress(item))
                    TryPatchWeaponItem(item);
            }
            catch { }
        }

        private void TryPatchEquipmentWeapons(LocalPlayer localPlayer)
        {
            try
            {
                var inventoryController = Memory.ReadPtr(localPlayer + SDK.Offsets.Player._inventoryController, false);
                if (!MemDMA.IsValidVirtualAddress(inventoryController))
                    return;

                var inventory = Memory.ReadPtr(inventoryController + SDK.Offsets.InventoryController.Inventory, false);
                if (!MemDMA.IsValidVirtualAddress(inventory))
                    return;

                var equipment = Memory.ReadPtr(inventory + SDK.Offsets.Inventory.Equipment, false);
                if (!MemDMA.IsValidVirtualAddress(equipment))
                    return;

                var slotsPtr = Memory.ReadPtr(equipment + SDK.Offsets.InventoryEquipment._cachedSlots, false);
                if (!MemDMA.IsValidVirtualAddress(slotsPtr))
                    return;

                using var slots = UnityArray<ulong>.Create(slotsPtr, false);
                // Weapon slots: FirstPrimaryWeapon=0, SecondPrimaryWeapon=1, Holster=2
                foreach (var slotPtr in slots)
                {
                    if (!MemDMA.IsValidVirtualAddress(slotPtr))
                        continue;

                    try
                    {
                        var containedItem = Memory.ReadPtr(slotPtr + SDK.Offsets.Slot.ContainedItem, false);
                        if (MemDMA.IsValidVirtualAddress(containedItem))
                            TryPatchWeaponItem(containedItem);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void TryPatchWeaponItem(ulong item)
        {
            try
            {
                var template = Memory.ReadPtr(item + SDK.Offsets.LootItem.Template, false);
                if (!MemDMA.IsValidVirtualAddress(template))
                    return;

                // Already patched this template
                if (_patchedTemplates.Contains(template))
                    return;

                // Write all malfunction flags to false
                Memory.WriteValue(template + SDK.Offsets.WeaponTemplate.AllowJam, false);
                Memory.WriteValue(template + SDK.Offsets.WeaponTemplate.AllowFeed, false);
                Memory.WriteValue(template + SDK.Offsets.WeaponTemplate.AllowMisfire, false);
                Memory.WriteValue(template + SDK.Offsets.WeaponTemplate.AllowSlide, false);

                _patchedTemplates.Add(template);
                DebugLogger.LogDebug($"[NoWeaponMalfunctions] Patched template @ 0x{template:X}");
            }
            catch { }
        }

        public override void OnRaidStart()
        {
            _lastEnabledState = false;
            _patchedTemplates.Clear();
        }
    }
}
