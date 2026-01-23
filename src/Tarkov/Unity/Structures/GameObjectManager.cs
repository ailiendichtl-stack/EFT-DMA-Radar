using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.UI.Misc;
using VmmSharpEx;

namespace LoneEftDmaRadar.Tarkov.Unity.Structures
{
    /// <summary>
    /// Unity Game Object Manager. Contains all Game Objects.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public readonly struct GameObjectManager
    {
        [FieldOffset(0x20)]
        public readonly ulong LastActiveNode; // 0x20
        [FieldOffset(0x28)]
        public readonly ulong ActiveNodes; // 0x28

        /// <summary>
        /// Looks up the Address of the Game Object Manager.
        /// First tries cached address from config, then falls back to signature/hardcoded offset.
        /// </summary>
        /// <param name="unityBase">UnityPlayer.dll module base address.</param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public static ulong GetAddr(ulong unityBase)
        {
            try
            {
                // Try cached GOM address first (faster startup)
                var cachedAddr = App.Config.DMA.CachedGomAddress;
                if (cachedAddr != 0 && TryValidateGomAddress(cachedAddr))
                {
                    DebugLogger.LogDebug($"GOM Located via Cached Address: 0x{cachedAddr:X}");
                    return cachedAddr;
                }

                // Fallback to signature scan
                ulong gomAddr = 0;
                try
                {
                    // GameObjectManager = qword_181A208D8
                    // void CleanupGameObjectManager()
                    //.text: 00000001801BAEFA 48 8B 35 D7 59 86 01                                            mov rsi, cs:qword_181A208D8
                    //.text: 00000001801BAF01 48 85 F6 test    rsi, rsi
                    //.text: 00000001801BAF04 0F 84 F8 00 00 00                                               jz loc_1801BB002
                    //.text: 00000001801BAF0A 8B 46 08                                                        mov eax, [rsi + 8]
                    const string signature = "48 8B 35 ? ? ? ? 48 85 F6 0F 84 ? ? ? ? 8B 46";
                    ulong gomSig = Memory.FindSignature(signature);
                    gomSig.ThrowIfInvalidVirtualAddress(nameof(gomSig));
                    int rva = Memory.ReadValueEnsure<int>(gomSig + 3);
                    var gomPtr = Memory.ReadValueEnsure<VmmPointer>(gomSig.AddRVA(7, rva));
                    gomPtr.ThrowIfInvalidUserVA();
                    gomAddr = gomPtr;
                    DebugLogger.LogDebug("GOM Located via Signature.");
                }
                catch
                {
                    var gomPtr = Memory.ReadValueEnsure<VmmPointer>(unityBase + UnitySDK.UnityOffsets.GameObjectManager);
                    gomPtr.ThrowIfInvalidUserVA();
                    gomAddr = gomPtr;
                    DebugLogger.LogDebug("GOM Located via Hardcoded Offset.");
                }

                // Cache the address for faster startup next time
                if (gomAddr != cachedAddr)
                {
                    App.Config.DMA.CachedGomAddress = gomAddr;
                    _ = Task.Run(() => App.Config.Save()); // Save async in background
                    DebugLogger.LogDebug($"GOM Address cached: 0x{gomAddr:X}");
                }

                return gomAddr;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR Locating Game Object Manager Address", ex);
            }
        }

        /// <summary>
        /// Validates a cached GOM address by attempting to read the structure.
        /// </summary>
        private static bool TryValidateGomAddress(ulong addr)
        {
            try
            {
                var gom = Memory.ReadValueEnsure<GameObjectManager>(addr);
                // Validate by checking if the active nodes pointers look reasonable
                return gom.LastActiveNode != 0 && gom.ActiveNodes != 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the Game Object Manager for the current UnityPlayer.
        /// </summary>
        /// <returns>Game Object Manager</returns>
        public static GameObjectManager Get()
        {
            try
            {
                return Memory.ReadValueEnsure<GameObjectManager>(Memory.GOM);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR Reading Game Object Manager", ex);
            }
        }

        /// <summary>
        /// Helper method to locate GOM Objects.
        /// </summary>
        public ulong GetObjectFromList(string objectName)
        {
            var currentObject = Memory.ReadValue<LinkedListObject>(ActiveNodes);
            var lastObject = Memory.ReadValue<LinkedListObject>(LastActiveNode);

            if (currentObject.ThisObject != 0x0)
            {
                while (currentObject.ThisObject != 0x0 && currentObject.ThisObject != lastObject.ThisObject)
                {
                    var objectNamePtr = Memory.ReadPtr(currentObject.ThisObject + UnitySDK.UnityOffsets.GameObject_NameOffset);
                    var objectNameStr = Memory.ReadUtf8String(objectNamePtr, 64);
                    if (objectNameStr.Equals(objectName, StringComparison.OrdinalIgnoreCase))
                        return currentObject.ThisObject;

                    currentObject = Memory.ReadValue<LinkedListObject>(currentObject.NextObjectLink); // Read next object
                }
            }
            return 0x0;
        }
    }
}