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

using System.Collections.ObjectModel;

namespace LoneEftDmaRadar.UI.Loot
{
    /// <summary>
    /// Contains pre-built default loot filter entries for new installations.
    /// </summary>
    internal static class DefaultFilters
    {
        /// <summary>
        /// High-value items (GPUs, LEDx, BTCs, etc.)
        /// </summary>
        public static ObservableCollection<LootFilterEntry> ValueItems => new()
        {
            new() { ItemID = "68f11adfcd0babab2c0fb003" },
            new() { ItemID = "59faff1d86f7746c51718c9c" }, // GPU
            new() { ItemID = "68f119c6121d878a2303eee3" },
            new() { ItemID = "5c94bbff86f7747ee735c08f" }, // TerraGroup Labs keycard (Blue)
            new() { ItemID = "5c1d0f4986f7744bb01837fa" },
            new() { ItemID = "57347ca924597744596b4e71" },
            new() { ItemID = "5c12620d86f7743f8b198b72" },
            new() { ItemID = "5c1f79a086f7746ed066fb8f" },
            new() { ItemID = "5c1d0c5f86f7744bb2683cf0" },
            new() { ItemID = "5c1d0efb86f7744baf2e7b7b" },
            new() { ItemID = "5c1d0dc586f7744baf2e7b79" },
            new() { ItemID = "5c1e495a86f7743109743dfb" },
            new() { ItemID = "5c1d0d6d86f7744bb2683e1f" },
            new() { ItemID = "6711039f9e648049e50b3307" },
            new() { ItemID = "5c0530ee86f774697952d952" }, // LEDx
            new() { ItemID = "5aafbde786f774389d0cbc0f" }, // Thermal weapon scope
            new() { ItemID = "619cbf7d23893217ec30b689" }, // BTR GPS
            new() { ItemID = "590c60fc86f77412b13fddcf" }, // Evasion armband
            new() { ItemID = "5c093e3486f77430cb02e593" }, // Ophthalmoscope
            new() { ItemID = "5c0e534186f7747fa1419867" },
            new() { ItemID = "63a3a93f8a56922e82001f5d" },
            new() { ItemID = "5780cf7f2459777de4559322" }, // Slickers
            new() { ItemID = "64ccc25f95763a1ae376e447" },
            new() { ItemID = "5d80c60f86f77440373c4ece" }, // Rogue USEC stash key
            new() { ItemID = "5ede7a8229445733cb4c18e2" },
            new() { ItemID = "5d80c62a86f7744036212b3f" }, // Rogue USEC workshop key
            new() { ItemID = "62987dfc402c7f69bf010923" },
            new() { ItemID = "63a39fc0af870e651d58e6ae" },
            new() { ItemID = "5c1e2a1e86f77431ea0ea84c" },
            new() { ItemID = "5c1e2d1f86f77431e9280bee" },
            new() { ItemID = "619cbf9e0a7c3a1a2731940a" },
            new() { ItemID = "5efde6b4f5448336730dbd61" },
            new() { ItemID = "67c031b79320f644db06f456" },
            new() { ItemID = "5c0126f40db834002a125382" }, // Red keycard
            new() { ItemID = "590c657e86f77412b013051d" }, // Portable defibrillator
            new() { ItemID = "64d4b23dc1b37504b41ac2b6" },
        };

        /// <summary>
        /// Top-tier ammunition (M993, BS, BP, MAI AP, etc.)
        /// </summary>
        public static ObservableCollection<LootFilterEntry> TopAmmo => new()
        {
            new() { ItemID = "657024bdc5d7d4cb4d078564" },
            new() { ItemID = "65702474bfc87b3a34093226" },
            new() { ItemID = "65702591c5d7d4cb4d07857c" },
            new() { ItemID = "648987d673c462723909a151" },
            new() { ItemID = "6489879db5a2df1c815a04ef" },
            new() { ItemID = "6489870774a806211e4fb685" },
            new() { ItemID = "5c1260dc86f7746b106e8748" }, // M993
            new() { ItemID = "657025dabfc87b3a34093256" },
            new() { ItemID = "657025cfbfc87b3a34093253" },
            new() { ItemID = "657023f81419851aef03e6f1" },
            new() { ItemID = "657025ebc5d7d4cb4d078588" },
            new() { ItemID = "5c1262a286f7743f8a69aab2" },
            new() { ItemID = "57372bad245977670b7cd242" },
            new() { ItemID = "57372b832459776701014e41" },
            new() { ItemID = "57372bd3245977670b7cd243" },
            new() { ItemID = "57372a7f24597766fe0de0c1" },
            new() { ItemID = "5737292724597765e5728562" },
            new() { ItemID = "57372ac324597767001bc261" },
            new() { ItemID = "6570900858b315e8b70a8a98" },
            new() { ItemID = "64898602f09d032aa9399d56" },
            new() { ItemID = "65702681bfc87b3a3409325f" },
            new() { ItemID = "64898583d5b4df6140000a1d" },
            new() { ItemID = "6570265f1419851aef03e739" },
            new() { ItemID = "657024f01419851aef03e715" },
            new() { ItemID = "65702652cfc010a0f5006a53" },
            new() { ItemID = "657024e3c5d7d4cb4d07856a" },
            new() { ItemID = "6570265bcfc010a0f5006a56" },
            new() { ItemID = "657024ecc5d7d4cb4d07856d" },
            new() { ItemID = "6489851fc827d4637f01791b" },
            new() { ItemID = "64acea16c4eda9354b0226b0" },
            new() { ItemID = "64ace9f9c4eda9354b0226aa" },
            new() { ItemID = "648985c074a806211e4fb682" },
            new() { ItemID = "657023a9126cc4a57d0e17a6" },
            new() { ItemID = "67600a42b32eb5d23e0eb459" },
            new() { ItemID = "67600a516f01341c9106ab4c" },
            new() { ItemID = "6769b8e3c1a1466c850658a8" },
            new() { ItemID = "648984e3f09d032aa9399d53" },
            new() { ItemID = "6570254fcfc010a0f5006a22" },
            new() { ItemID = "65702558cfc010a0f5006a25" },
            new() { ItemID = "560d75f54bdc2da74d8b4573" },
            new() { ItemID = "65702572c5d7d4cb4d078576" },
            new() { ItemID = "6570257cc5d7d4cb4d078579" },
            new() { ItemID = "68e915dfd996b7754e0f25c9" },
            new() { ItemID = "68e915cf55ba5bd1c6083a32" },
            new() { ItemID = "68e91494ad87d322fb0497f9" },
            new() { ItemID = "6489848173c462723909a14b" },
            new() { ItemID = "68ee0103b5db65191e0ec07c" },
        };

        /// <summary>
        /// Valuable keys (Labs, Streets, Reserve, etc.)
        /// </summary>
        public static ObservableCollection<LootFilterEntry> ValuableKeys => new()
        {
            new() { ItemID = "64ccc2111779ad6ba200a139" },
            new() { ItemID = "63a39c7964283b5e9c56b280" },
            new() { ItemID = "64ccc1d4a0f13c24561edf27" },
            new() { ItemID = "64ccc1f4ff54fb38131acf27" },
            new() { ItemID = "63a71e922b25f7513905ca20" },
            new() { ItemID = "63a71e86b7f4570d3a293169" },
            new() { ItemID = "64ccc1ec1779ad6ba200a137" },
            new() { ItemID = "63a71e781031ac76fe773c7d" },
            new() { ItemID = "63a71ed21031ac76fe773c7f" },
            new() { ItemID = "63a39667c9b3aa4b61683e98" },
            new() { ItemID = "6582dc5740562727a654ebb1" },
            new() { ItemID = "64ccc206793ca11c8f450a38" },
            new() { ItemID = "6582dbf0b8d7830efc45016f" },
            new() { ItemID = "64ccc24de61ea448b507d34d" },
            new() { ItemID = "63a39f6e64283b5e9c56b289" },
            new() { ItemID = "6582dc4b6ba9e979af6b79f4" },
            new() { ItemID = "63a39df18a56922e82001f25" },
            new() { ItemID = "6582dbe43a2e5248357dbe9a" },
            new() { ItemID = "63a397d3af870e651d58e65b" },
            new() { ItemID = "63a399193901f439517cafb6" },
            new() { ItemID = "5eff09cd30a7dc22fd1ddfed" }, // RB-PKPM
            new() { ItemID = "5a0ea64786f7741707720468" },
            new() { ItemID = "5a13f35286f77413ef1436b0" },
            new() { ItemID = "5a13f24186f77410e57c5626" },
            new() { ItemID = "5a0ee4b586f7743698200d22" },
            new() { ItemID = "5a145d4786f7744cbb6f4a12" },
            new() { ItemID = "5a0eec9686f77402ac5c39f2" },
            new() { ItemID = "5a0eecf686f7740350630097" },
            new() { ItemID = "5a0eee1486f77402aa773226" },
            new() { ItemID = "5a0dc95c86f77452440fc675" },
            new() { ItemID = "5a0dc45586f7742f6b0b73e3" },
            new() { ItemID = "5a0ee34586f774023b6ee092" },
            new() { ItemID = "5a13eebd86f7746fd639aa93" },
            new() { ItemID = "5a0ee30786f774023b6ee08f" },
            new() { ItemID = "5a0ec6d286f7742c0b518fb5" },
            new() { ItemID = "5a13ef7e86f7741290491063" },
        };

        /// <summary>
        /// Kappa container quest items (Collector's quest)
        /// </summary>
        public static ObservableCollection<LootFilterEntry> KappaItems => new()
        {
            new() { ItemID = "5bc9c377d4351e3bac12251b" },
            new() { ItemID = "5bc9c1e2d4351e00367fbcf0" },
            new() { ItemID = "5bc9c049d4351e44f824d360" },
            new() { ItemID = "5bc9b355d4351e6d1509862a" },
            new() { ItemID = "5bc9bc53d4351e00367fbcee" },
            new() { ItemID = "5bc9bdb8d4351e003562b8a1" },
            new() { ItemID = "5bc9b9ecd4351e3bac122519" },
            new() { ItemID = "5bc9b720d4351e450201234b" },
            new() { ItemID = "5bc9b156d4351e00367fbce9" },
            new() { ItemID = "5bc9c29cd4351e003562b8a3" },
            new() { ItemID = "5bd073a586f7747e6f135799" },
            new() { ItemID = "5bd073c986f7747f627e796c", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "5e54f6af86f7742199090bf3" },
            new() { ItemID = "5e54f79686f7744022011103" },
            new() { ItemID = "5e54f62086f774219b0f1937" },
            new() { ItemID = "5e54f76986f7740366043752" },
            new() { ItemID = "5f745ee30acaeb0d490d8c5b" },
            new() { ItemID = "5bc9be8fd4351e00334cae6e" },
            new() { ItemID = "5fd8d28367cb5e077335170f" },
            new() { ItemID = "60b0f988c4449e4cb624c1da" },
            new() { ItemID = "60b0f93284c20f0feb453da7", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "60b0f7057897d47c5b04ab94" },
            new() { ItemID = "60b0f6c058e0b0481a09ad11" },
            new() { ItemID = "60b0f561c4449e4cb624c1d7" },
            new() { ItemID = "62a09ec84f842e1bd12da3f2" },
            new() { ItemID = "62a09e974f842e1bd12da3f0" },
            new() { ItemID = "62a09e73af34e73a266d932a" },
            new() { ItemID = "62a09e410b9d3c46de5b6e78" },
            new() { ItemID = "62a09e08de7ac81993580532" },
            new() { ItemID = "62a09dd4621468534a797ac7" },
            new() { ItemID = "62a09d79de7ac81993580530" },
            new() { ItemID = "62a09d3bcf4a99369e262447" },
            new() { ItemID = "62a09cfe4f842e1bd12da3e4" },
            new() { ItemID = "62a09cb7a04c0c5c6e0a84f8" },
            new() { ItemID = "62a091170b9d3c46de5b6cf2" },
            new() { ItemID = "62a08f4c4f842e1bd12d9d62" },
            new() { ItemID = "66b37f114410565a8f6789e2" },
            new() { ItemID = "66b37eb4acff495a29492407" },
            new() { ItemID = "66b37ea4c5d72b0277488439" },
        };

        /// <summary>
        /// Prestige items
        /// </summary>
        public static ObservableCollection<LootFilterEntry> PrestigeItems => new()
        {
            new() { ItemID = "655c652d60d0ac437100fed7" },
            new() { ItemID = "655c66e40b2de553b618d4b8" },
            new() { ItemID = "66572c82ad599021091c6118" },
            new() { ItemID = "66572be36a723f7f005a066e" },
            new() { ItemID = "655c67782a1356436041c9c5" },
            new() { ItemID = "655c673673a43e23e857aebd" },
            new() { ItemID = "66572cbdad599021091c611a" },
            new() { ItemID = "655c663a6689c676ce57af85" },
            new() { ItemID = "655c669103999d3c810c025b" },
            new() { ItemID = "66572b8d80b1cd4b6a67847f" },
        };

        /// <summary>
        /// General quest items (Quest prep for Kappa tasks)
        /// </summary>
        public static ObservableCollection<LootFilterEntry> QuestItems => new()
        {
            new() { ItemID = "590c5d4b86f774784e1b9c45", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "656df4fec921ad01000481a2" },
            new() { ItemID = "57347da92459774491567cf5" },
            new() { ItemID = "59e7635f86f7742cbf2c1095", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "5a3a859786f7747e2305e8bf", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "590a3efd86f77437d351a25b" },
            new() { ItemID = "544fb3f34bdc2d03748b456a" },
            new() { ItemID = "5d1b36a186f7742523398433" },
            new() { ItemID = "590c621186f774138d11ea29" },
            new() { ItemID = "59e36c6f86f774176c10a2a7", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "57347cd0245977445a2d6ff1" },
            new() { ItemID = "590a3b0486f7743954552bdb" },
            new() { ItemID = "635a758bfefc88a93f021b8a" },
            new() { ItemID = "57347d7224597744596b4e72" },
            new() { ItemID = "5733279d245977289b77ec24" },
            new() { ItemID = "590a3c0a86f774385a33c450" },
            new() { ItemID = "573477e124597737dd42e191" },
            new() { ItemID = "590a358486f77429692b2790" },
            new() { ItemID = "56742c324bdc2d150f8b456d" },
            new() { ItemID = "573476d324597737da2adc13" },
            new() { ItemID = "5734770f24597738025ee254" },
            new() { ItemID = "573476f124597737e04bf328" },
            new() { ItemID = "5734779624597737e04bf329" },
            new() { ItemID = "59e7708286f7742cbd762753" },
            new() { ItemID = "5aa2b9ede5b5b000137b758b" },
            new() { ItemID = "5c05308086f7746b2101e90b" },
            new() { ItemID = "5c052f6886f7746b1e3db148" },
            new() { ItemID = "5d03794386f77420415576f5" },
            new() { ItemID = "5d0379a886f77420407aa271" },
            new() { ItemID = "5ab8f20c86f7745cdb629fb2" },
            new() { ItemID = "59e763f286f7742ee57895da" },
            new() { ItemID = "5c052e6986f7746b207bc3c9" },
            new() { ItemID = "5d02778e86f774203e7dedbe" },
            new() { ItemID = "5c06779c86f77426e00dd782" },
            new() { ItemID = "5c06782b86f77426df5407d2" },
            new() { ItemID = "5c052fb986f7746b2101e909" },
            new() { ItemID = "5c05300686f7746dce784e5d" },
            new() { ItemID = "55d482194bdc2d1d4e8b456b" },
            new() { ItemID = "5b43575a86f77424f443fe62" },
            new() { ItemID = "5af0534a86f7743b6f354284" },
            new() { ItemID = "59e7715586f7742ee5789605" },
            new() { ItemID = "5b4335ba86f7744d2837a264" },
            new() { ItemID = "59e7643b86f7742cbf2c109a" },
            new() { ItemID = "5648a69d4bdc2ded0b8b457b" },
            new() { ItemID = "5d1b3a5d86f774252167ba22" },
            new() { ItemID = "62a0a043cf4a99369e2624a5" },
            new() { ItemID = "5b44abe986f774283e2e3512" },
            new() { ItemID = "5b3b713c5acfc4330140bd8d" },
            new() { ItemID = "572b7fa524597762b747ce82" },
            new() { ItemID = "59e3639286f7741777737013" },
            new() { ItemID = "573478bc24597738002c6175" },
            new() { ItemID = "59e3658a86f7741776641ac4" },
            new() { ItemID = "59faf7ca86f7740dbe19f6c2" },
            new() { ItemID = "590c5bbd86f774785762df04" },
            new() { ItemID = "59e358a886f7741776641ac3" },
            new() { ItemID = "59e35cbb86f7741778269d83" },
            new() { ItemID = "59e3556c86f7741776641ac2", Type = LootFilterEntryType.BlacklistedLoot }, // Blacklisted
            new() { ItemID = "57e26fc7245977162a14b800" },
            new() { ItemID = "5841474424597759ba49be91" },
            new() { ItemID = "5447a9cd4bdc2dbd208b4567" },
            new() { ItemID = "5841499024597759f825ff3e" },
            new() { ItemID = "590de71386f774347051a052" },
            new() { ItemID = "590de7e986f7741b096e5f32" },
            new() { ItemID = "5d08d21286f774736e7c94c3" },
            new() { ItemID = "5ed51652f6c34d2cc26336a1" },
            new() { ItemID = "5ed5166ad380ab312177c100" },
            new() { ItemID = "5ed5160a87bb8443d10680b5" },
            new() { ItemID = "5ed515f6915ec335206e4152" },
            new() { ItemID = "5ed515ece452db0eb56fc028" },
            new() { ItemID = "5ed515e03a40a50460332579" },
            new() { ItemID = "5ed515c8d380ab312177c0fa" },
            new() { ItemID = "59faf98186f774067b6be103" },
            new() { ItemID = "59fafb5d86f774067a6f2084" },
            new() { ItemID = "60a7acf20c5cb24b01346648" },
            new() { ItemID = "657bc821aab96fccee08becc" },
        };
    }
}
