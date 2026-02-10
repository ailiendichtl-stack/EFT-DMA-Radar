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
using LoneEftDmaRadar.Tarkov.GameWorld.Loot.Helpers;
using LoneEftDmaRadar.Tarkov.GameWorld.Quests;
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Loot;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.Tarkov.GameWorld.Loot
{
    public sealed class LootManager
    {
        #region Fields/Properties/Constructor

        private readonly ulong _lgw;
        private readonly Lock _filterSync = new();
        private readonly ConcurrentDictionary<ulong, LootItem> _loot = new();
        /// <summary>
        /// Persistent cache of static container addresses that have been discovered.
        /// Containers in this set won't be removed when they leave the game's LootList range.
        /// </summary>
        private readonly HashSet<ulong> _persistentContainerCache = new();

        /// <summary>
        /// Cached list of static containers — updated during ProcessLootIndex/removal, avoids .OfType enumeration.
        /// </summary>
        private readonly List<StaticLootContainer> _staticContainers = new();

        /// <summary>
        /// All loot (with filter applied).
        /// </summary>
        public IReadOnlyList<LootItem> FilteredLoot { get; private set; }

        /// <summary>
        /// All Static Containers on the map.
        /// </summary>
        public IReadOnlyList<StaticLootContainer> StaticContainers => _staticContainers;

        public LootManager(ulong localGameWorld)
        {
            _lgw = localGameWorld;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Force a filter refresh.
        /// Thread Safe.
        /// </summary>
        private static readonly Comparison<LootItem> _filterSortComparison = (a, b) =>
        {
            int cmp = a.Important.CompareTo(b.Important);
            if (cmp != 0) return cmp;
            return (a?.Price ?? 0).CompareTo(b?.Price ?? 0);
        };

        public void RefreshFilter()
        {
            if (_filterSync.TryEnter())
            {
                try
                {
                    var filter = LootFilter.Create();
                    var buffer = new List<LootItem>();
                    foreach (var item in _loot.Values)
                    {
                        if (filter(item))
                            buffer.Add(item);
                    }
                    buffer.Sort(_filterSortComparison);
                    FilteredLoot = buffer; // Atomic ref swap — UI reads old list until this assignment
                }
                catch { }
                finally
                {
                    _filterSync.Exit();
                }
            }
        }

        /// <summary>
        /// Refreshes loot, only call from a memory thread (Non-GUI).
        /// </summary>
        public void Refresh(CancellationToken ct)
        {
            try
            {
                GetLoot(ct);
                RefreshFilter();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLogger.LogDebug($"CRITICAL ERROR - Failed to refresh loot: {ex}");
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Updates referenced FilteredLoot List with fresh values.
        /// </summary>
        private void GetLoot(CancellationToken ct)
        {
            var lootListAddr = Memory.ReadPtr(_lgw + Offsets.GameWorld.LootList);
            using var lootList = UnityList<ulong>.Create(
                addr: lootListAddr,
                useCache: true);
            // Remove any loot no longer present (except persistently cached containers if enabled)
            var persistentCacheEnabled = App.Config.Containers.PersistentCache;
            using var lootListHs = lootList.ToPooledSet();
            foreach (var existing in _loot.Keys)
            {
                if (!lootListHs.Contains(existing))
                {
                    // Don't remove if persistent caching is enabled and it's a cached container
                    if (persistentCacheEnabled && _persistentContainerCache.Contains(existing))
                        continue;
                    if (_loot.TryRemove(existing, out var removed) && removed is StaticLootContainer removedContainer)
                        _staticContainers.Remove(removedContainer);
                }
            }
            // Update container status and batch refresh contents
            var hideSearched = App.Config.Containers.HideSearched;

            // Collect containers that need content refresh (PVE mode only)
            if (App.Config.Containers.PveScanEnabled)
            {
                var containersToRefresh = new List<StaticLootContainer>();
                foreach (var kvp in _loot)
                {
                    if (kvp.Value is StaticLootContainer container &&
                        !container.ContentsLoaded &&
                        container.InteractiveClass != 0)
                    {
                        containersToRefresh.Add(container);
                    }
                }

                // Batch refresh all container contents using scatter reads
                if (containersToRefresh.Count > 0)
                {
                    ContainerContentsReader.BatchRefreshContents(containersToRefresh);
                }
            }

            // Update searched status for all containers
            foreach (var kvp in _loot)
            {
                if (kvp.Value is StaticLootContainer container)
                {
                    container.UpdateSearchedStatus();

                    // If HideSearched is enabled and container was searched, remove from persistent cache
                    // so it can be cleaned up normally when out of range
                    if (hideSearched && container.Searched)
                    {
                        _persistentContainerCache.Remove(kvp.Key);
                    }
                }
            }
            // Proceed to get new loot
            using var map = Memory.CreateScatterMap();
            var round1 = map.AddRound();
            var round2 = map.AddRound();
            var round3 = map.AddRound();
            var round4 = map.AddRound();
            foreach (var lootBase in lootList)
            {
                ct.ThrowIfCancellationRequested();
                var hasExisting = _loot.TryGetValue(lootBase, out var existing);
                if (hasExisting && existing is not LootItem)
                {
                    continue; // Already processed this non-updatable loot item once before
                }
                round1.PrepareReadPtr(lootBase + ObjectClass.MonoBehaviourOffset); // UnityComponent
                round1.PrepareReadPtr(lootBase + ObjectClass.To_NamePtr[0]); // C1
                round1.Completed += (sender, s1) =>
                {
                    if (s1.ReadPtr(lootBase + ObjectClass.MonoBehaviourOffset, out var monoBehaviour) &&
                        s1.ReadPtr(lootBase + ObjectClass.To_NamePtr[0], out var c1))
                    {
                        round2.PrepareReadPtr(monoBehaviour + UnitySDK.UnityOffsets.Component_ObjectClassOffset); // InteractiveClass
                        round2.PrepareReadPtr(monoBehaviour + UnitySDK.UnityOffsets.Component_GameObjectOffset); // GameObject
                        round2.PrepareReadPtr(c1 + ObjectClass.To_NamePtr[1]); // C2
                        round2.Completed += (sender, s2) =>
                        {
                            if (s2.ReadPtr(monoBehaviour + UnitySDK.UnityOffsets.Component_ObjectClassOffset, out var interactiveClass) &&
                                s2.ReadPtr(monoBehaviour + UnitySDK.UnityOffsets.Component_GameObjectOffset, out var gameObject) &&
                                s2.ReadPtr(c1 + ObjectClass.To_NamePtr[1], out var classNamePtr))
                            {
                                round3.PrepareRead(classNamePtr, 64); // ClassName
                                round3.PrepareReadPtr(gameObject + UnitySDK.UnityOffsets.GameObject_ComponentsOffset); // Components
                                round3.PrepareReadPtr(gameObject + UnitySDK.UnityOffsets.GameObject_NameOffset); // PGameObjectName
                                round3.Completed += (sender, s3) =>
                                {
                                    if (s3.ReadString(classNamePtr, 64, Encoding.UTF8) is string className &&
                                        s3.ReadPtr(gameObject + UnitySDK.UnityOffsets.GameObject_ComponentsOffset, out var components)
                                        && s3.ReadPtr(gameObject + UnitySDK.UnityOffsets.GameObject_NameOffset, out var pGameObjectName))
                                    {
                                        round4.PrepareRead(pGameObjectName, 64); // ObjectName
                                        round4.PrepareReadPtr(components + 0x8); // T1
                                        round4.Completed += (sender, s4) =>
                                        {
                                            if (
                                                s4.ReadString(pGameObjectName, 64, Encoding.UTF8) is string objectName &&
                                                s4.ReadPtr(components + 0x8, out var transformInternal))
                                            {
                                                map.Completed += (sender, _) => // Store this as callback, let scatter reads all finish first (benchmarked faster)
                                                {
                                                    ct.ThrowIfCancellationRequested();
                                                    try
                                                    {
                                                        var @params = new LootIndexParams
                                                        {
                                                            ItemBase = lootBase,
                                                            InteractiveClass = interactiveClass,
                                                            ObjectName = objectName,
                                                            TransformInternal = transformInternal,
                                                            ClassName = className
                                                        };
                                                        var existingLootItem = existing as LootItem;
                                                        ProcessLootIndex(ref @params, existingLootItem);
                                                    }
                                                    catch
                                                    {
                                                    }
                                                };
                                            }
                                        };
                                    }
                                };
                            }
                        };
                    }
                };
            }
            map.Execute(); // execute scatter read
            // Post Scatter Read - Sync Corpses
            var deadPlayers = Memory.Players?
                .Where(x => x.Corpse is not null)?.ToList();
            foreach (var corpse in _loot.Values.OfType<LootCorpse>())
            {
                corpse.Sync(deadPlayers);
            }
        }

        /// <summary>
        /// Process a single loot index.
        /// </summary>
        private void ProcessLootIndex(ref LootIndexParams p, LootItem existingLoot = null)
        {
            var isCorpse = p.ClassName.Contains("Corpse", StringComparison.OrdinalIgnoreCase);
            var isLooseLoot = p.ClassName.Equals("ObservedLootItem", StringComparison.OrdinalIgnoreCase);
            var isContainer = p.ClassName.Equals("LootableContainer", StringComparison.OrdinalIgnoreCase);
            var interactiveClass = p.InteractiveClass;

            if (p.ObjectName.Contains("script", StringComparison.OrdinalIgnoreCase))
            {
                // skip these
            }
            else
            {
                // Get Item Position
                var pos = new UnityTransform(p.TransformInternal, true).UpdatePosition();

                // update position for existing loot items
                if (existingLoot != null)
                {
                    existingLoot.UpdatePosition(pos);
                    return;
                }

                if (isCorpse)
                {
                    var corpse = new LootCorpse(interactiveClass, pos);
                    _ = _loot.TryAdd(p.ItemBase, corpse);
                }
                if (isContainer)
                {
                    try
                    {
                        if (p.ObjectName.Equals("loot_collider", StringComparison.OrdinalIgnoreCase))
                        {
                            if (_loot.TryAdd(p.ItemBase, new LootAirdrop(pos)))
                            {
                                // Airdrops are also persistently cached
                                _persistentContainerCache.Add(p.ItemBase);
                            }
                        }
                        else
                        {
                            var itemOwner = Memory.ReadPtr(interactiveClass + Offsets.LootableContainer.ItemOwner);
                            var ownerItemBase = Memory.ReadPtr(itemOwner + Offsets.LootableContainerItemOwner.RootItem);
                            var ownerItemTemplate = Memory.ReadPtr(ownerItemBase + Offsets.LootItem.Template);
                            var ownerItemMongoId = Memory.ReadValue<MongoID>(ownerItemTemplate + Offsets.ItemTemplate._id);
                            var ownerItemId = ownerItemMongoId.ReadString();
                            var container = new StaticLootContainer(ownerItemId, pos, interactiveClass);
                            if (_loot.TryAdd(p.ItemBase, container))
                            {
                                _staticContainers.Add(container);
                                // Add to persistent cache so container isn't removed when out of range
                                _persistentContainerCache.Add(p.ItemBase);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                else if (isLooseLoot)
                {
                    var item = Memory.ReadPtr(interactiveClass + Offsets.InteractiveLootItem.Item); //EFT.InventoryLogic.Item
                    var itemTemplate = Memory.ReadPtr(item + Offsets.LootItem.Template); //EFT.InventoryLogic.ItemTemplate
                    var isQuestItem = Memory.ReadValue<bool>(itemTemplate + Offsets.ItemTemplate.QuestItem);

                    var mongoId = Memory.ReadValue<MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                    var id = mongoId.ReadString();
                    if (isQuestItem)
                    {
                        // Only show quest items if they're needed for an active quest
                        var isNeededForQuest = Memory.Quests?.IsQuestItem(id) ?? false;
                        if (isNeededForQuest)
                        {
                            var shortNamePtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.ShortName);
                            var shortName = Memory.ReadUnicodeString(shortNamePtr, 128);
                            DebugLogger.LogDebug(shortName);
                            _ = _loot.TryAdd(p.ItemBase, new LootItem(id, $"Q_{shortName}", pos, isQuestItem: true));
                        }
                        // Skip quest items not needed for active quests
                    }
                    else
                    {
                        // Check if this regular item is needed for an active quest
                        var isNeededForQuest = Memory.Quests?.IsQuestItem(id) ?? false;

                        if (TarkovDataManager.AllItems.TryGetValue(id, out var entry))
                        {
                            _ = _loot.TryAdd(p.ItemBase, new LootItem(entry, pos, isQuestItem: isNeededForQuest));
                        }
                        else
                        {
                            // Fallback for unknown items (new event items, etc.)
                            // Read the short name from memory so they still show up
                            try
                            {
                                var shortNamePtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.ShortName);
                                var shortName = Memory.ReadUnicodeString(shortNamePtr, 128);
                                if (!string.IsNullOrEmpty(shortName))
                                {
                                    _ = _loot.TryAdd(p.ItemBase, new LootItem(id, shortName, pos, isQuestItem: isNeededForQuest));
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private readonly struct LootIndexParams
        {
            public ulong ItemBase { get; init; }
            public ulong InteractiveClass { get; init; }
            public string ObjectName { get; init; }
            public ulong TransformInternal { get; init; }
            public string ClassName { get; init; }
        }

        #endregion

    }
}