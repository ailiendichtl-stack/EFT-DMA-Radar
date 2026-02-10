using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Raw mesh geometry: flat interleaved vertex/index arrays.
    /// </summary>
    public struct MeshData
    {
        public float[] Vertices;    // Flat [x0,y0,z0, x1,y1,z1, ...]
        public int[] Indices;       // Flat [i0,i1,i2, i3,i4,i5, ...]
        public int VertexCount;
        public int TriangleCount;
        public Vector3 BoundsMin;
        public Vector3 BoundsMax;
    }

    /// <summary>
    /// Streaming OBJ parser and binary cache for collision meshes.
    /// Handles 1GB+ OBJ files without loading them entirely into memory.
    /// </summary>
    public static class MeshLoader
    {
        private const uint CACHE_MAGIC = 0x54524D43; // 'TRMC'
        private const uint CACHE_VERSION = 1;
        private const uint TMESH_MAGIC = 0x48534D54; // 'TMSH'
        private const uint TMESH_VERSION = 1;

        #region TMESH Loading

        /// <summary>
        /// Load a .tmesh binary mesh file (compact format from CollisionExporter).
        /// </summary>
        public static MeshData LoadTmesh(string path, Action<string> progress = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"TMESH file not found: {path}");

            progress?.Invoke($"Loading {Path.GetFileName(path)}...");

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            using var r = new BinaryReader(fs);

            // Header (64 bytes)
            uint magic = r.ReadUInt32();
            if (magic != TMESH_MAGIC)
                throw new InvalidDataException($"Invalid TMESH magic: 0x{magic:X8}");

            uint version = r.ReadUInt32();
            if (version != TMESH_VERSION)
                throw new InvalidDataException($"TMESH version {version} != expected {TMESH_VERSION}");

            int vertexCount = (int)r.ReadUInt32();
            int triangleCount = (int)r.ReadUInt32();
            var boundsMin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var boundsMax = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            int sourceTriCount = (int)r.ReadUInt32();
            r.ReadBytes(20); // reserved

            // Vertices
            var vertices = ReadFloatArray(r, vertexCount * 3);

            // Indices
            var indices = ReadIntArray(r, triangleCount * 3);

            DebugLogger.LogInfo($"[MeshLoader] TMESH loaded: {path} ({vertexCount:N0}v, {triangleCount:N0}t, original {sourceTriCount:N0}t)");
            progress?.Invoke($"Loaded {vertexCount:N0} vertices, {triangleCount:N0} triangles");

            return new MeshData
            {
                Vertices = vertices,
                Indices = indices,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
            };
        }

        #endregion

        #region OBJ Loading

        /// <summary>
        /// Parse an OBJ file into MeshData. Reads vertex/triangle counts from header
        /// comments to pre-allocate arrays, then fills in a single streaming pass.
        /// </summary>
        public static MeshData LoadObj(string path, Action<string> progress = null)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"OBJ file not found: {path}");

            progress?.Invoke($"Scanning {Path.GetFileName(path)} header...");

            // First: read header comments to get counts
            int vertexCount = 0, triangleCount = 0;
            using (var headerReader = new StreamReader(path))
            {
                string line;
                while ((line = headerReader.ReadLine()) != null)
                {
                    if (!line.StartsWith('#'))
                        break; // Header comments are at the top

                    if (line.StartsWith("# Vertices:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.AsSpan(11).Trim(), out vertexCount);
                    else if (line.StartsWith("# Triangles:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line.AsSpan(12).Trim(), out triangleCount);
                }
            }

            // Fallback: if header didn't have counts, do a counting pass
            if (vertexCount == 0 || triangleCount == 0)
            {
                progress?.Invoke("Counting vertices/triangles (no header counts)...");
                (vertexCount, triangleCount) = CountElements(path);
            }

            if (vertexCount == 0 || triangleCount == 0)
                throw new InvalidDataException($"OBJ has no geometry: {path}");

            DebugLogger.LogInfo($"[MeshLoader] {Path.GetFileName(path)}: {vertexCount:N0} vertices, {triangleCount:N0} triangles");

            // Allocate arrays
            var vertices = new float[vertexCount * 3];
            var indices = new int[triangleCount * 3];
            var boundsMin = new Vector3(float.MaxValue);
            var boundsMax = new Vector3(float.MinValue);

            // Single-pass fill
            progress?.Invoke($"Loading {vertexCount:N0} vertices, {triangleCount:N0} triangles...");
            int vi = 0, fi = 0;
            int lineNum = 0;
            int progressInterval = Math.Max(1, (vertexCount + triangleCount) / 20); // ~5% updates

            using var reader = new StreamReader(path, System.Text.Encoding.UTF8, true, 1024 * 1024); // 1MB buffer
            string l;
            while ((l = reader.ReadLine()) != null)
            {
                lineNum++;

                if (l.Length < 2)
                    continue;

                if (l[0] == 'v' && l[1] == ' ')
                {
                    // Vertex line: "v x y z"
                    if (vi < vertexCount * 3)
                    {
                        ParseVertex(l.AsSpan(2), vertices, vi, ref boundsMin, ref boundsMax);
                        vi += 3;
                    }

                    if (vi % (progressInterval * 3) == 0)
                        progress?.Invoke($"Vertices: {vi / 3:N0} / {vertexCount:N0}");
                }
                else if (l[0] == 'f' && l[1] == ' ')
                {
                    // Face line: "f i1 i2 i3" (1-based)
                    if (fi < triangleCount * 3)
                    {
                        ParseFace(l.AsSpan(2), indices, fi);
                        fi += 3;
                    }
                }
            }

            int actualVerts = vi / 3;
            int actualTris = fi / 3;

            if (actualVerts != vertexCount || actualTris != triangleCount)
            {
                DebugLogger.LogInfo($"[MeshLoader] Count mismatch: expected {vertexCount}v/{triangleCount}t, got {actualVerts}v/{actualTris}t");
                // Trim arrays if counts were off
                if (actualVerts < vertexCount)
                    Array.Resize(ref vertices, actualVerts * 3);
                if (actualTris < triangleCount)
                    Array.Resize(ref indices, actualTris * 3);
                vertexCount = actualVerts;
                triangleCount = actualTris;
            }

            progress?.Invoke($"Loaded {vertexCount:N0} vertices, {triangleCount:N0} triangles");

            return new MeshData
            {
                Vertices = vertices,
                Indices = indices,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
            };
        }

        private static void ParseVertex(ReadOnlySpan<char> span, float[] verts, int offset,
            ref Vector3 bMin, ref Vector3 bMax)
        {
            // "x y z" — split by spaces
            span = span.Trim();

            int s1 = span.IndexOf(' ');
            if (s1 < 0) return;
            float x = FastParseFloat(span[..s1]);

            var rest = span[(s1 + 1)..].TrimStart();
            int s2 = rest.IndexOf(' ');
            if (s2 < 0) return;
            float y = FastParseFloat(rest[..s2]);

            float z = FastParseFloat(rest[(s2 + 1)..].TrimStart());

            verts[offset] = x;
            verts[offset + 1] = y;
            verts[offset + 2] = z;

            // Update bounds
            if (x < bMin.X) bMin.X = x;
            if (y < bMin.Y) bMin.Y = y;
            if (z < bMin.Z) bMin.Z = z;
            if (x > bMax.X) bMax.X = x;
            if (y > bMax.Y) bMax.Y = y;
            if (z > bMax.Z) bMax.Z = z;
        }

        private static void ParseFace(ReadOnlySpan<char> span, int[] indices, int offset)
        {
            // "i1 i2 i3" — 1-based vertex indices, convert to 0-based
            span = span.Trim();

            int s1 = span.IndexOf(' ');
            if (s1 < 0) return;
            indices[offset] = FastParseInt(span[..s1]) - 1;

            var rest = span[(s1 + 1)..].TrimStart();
            int s2 = rest.IndexOf(' ');
            if (s2 < 0) return;
            indices[offset + 1] = FastParseInt(rest[..s2]) - 1;

            indices[offset + 2] = FastParseInt(rest[(s2 + 1)..].TrimStart()) - 1;
        }

        private static float FastParseFloat(ReadOnlySpan<char> span)
        {
            float.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out float val);
            return val;
        }

        private static int FastParseInt(ReadOnlySpan<char> span)
        {
            // Trim any slash content (e.g., "123/456/789" → take first part)
            int slash = span.IndexOf('/');
            if (slash >= 0) span = span[..slash];
            int.TryParse(span, out int val);
            return val;
        }

        private static (int vertices, int triangles) CountElements(string path)
        {
            int v = 0, f = 0;
            using var reader = new StreamReader(path, System.Text.Encoding.UTF8, true, 1024 * 1024);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length >= 2)
                {
                    if (line[0] == 'v' && line[1] == ' ') v++;
                    else if (line[0] == 'f' && line[1] == ' ') f++;
                }
            }
            return (v, f);
        }

        #endregion

        #region Binary Cache

        /// <summary>
        /// Save mesh data + pre-built BVH to a binary cache file.
        /// </summary>
        public static void SaveCache(string path, in MeshData mesh, BvhNode[] nodes, int[] triOrder, long objLastModifiedTicks)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
            using var w = new BinaryWriter(fs);

            // Header
            w.Write(CACHE_MAGIC);
            w.Write(CACHE_VERSION);
            w.Write(mesh.VertexCount);
            w.Write(mesh.TriangleCount);
            w.Write(nodes?.Length ?? 0);
            w.Write(mesh.BoundsMin.X); w.Write(mesh.BoundsMin.Y); w.Write(mesh.BoundsMin.Z);
            w.Write(mesh.BoundsMax.X); w.Write(mesh.BoundsMax.Y); w.Write(mesh.BoundsMax.Z);
            w.Write(objLastModifiedTicks);

            // Vertices
            WriteFloatArray(w, mesh.Vertices, mesh.VertexCount * 3);

            // Indices
            WriteIntArray(w, mesh.Indices, mesh.TriangleCount * 3);

            // BVH nodes
            if (nodes != null && nodes.Length > 0)
            {
                var nodeBytes = MemoryMarshal.AsBytes(nodes.AsSpan());
                w.Write(nodeBytes);
            }

            // Triangle order
            if (triOrder != null && triOrder.Length > 0)
                WriteIntArray(w, triOrder, triOrder.Length);

            DebugLogger.LogInfo($"[MeshLoader] Cache saved: {path} ({fs.Length / (1024 * 1024):N0} MB)");
        }

        /// <summary>
        /// Load mesh data + pre-built BVH from a binary cache file.
        /// </summary>
        public static (MeshData mesh, BvhNode[] nodes, int[] triOrder) LoadCache(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024);
            using var r = new BinaryReader(fs);

            // Header
            uint magic = r.ReadUInt32();
            if (magic != CACHE_MAGIC)
                throw new InvalidDataException($"Invalid cache magic: 0x{magic:X8}");

            uint version = r.ReadUInt32();
            if (version != CACHE_VERSION)
                throw new InvalidDataException($"Cache version {version} != expected {CACHE_VERSION}");

            int vertexCount = r.ReadInt32();
            int triangleCount = r.ReadInt32();
            int nodeCount = r.ReadInt32();

            var boundsMin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var boundsMax = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            long objTicks = r.ReadInt64(); // not used here, just consumed

            // Vertices
            var vertices = ReadFloatArray(r, vertexCount * 3);

            // Indices
            var indices = ReadIntArray(r, triangleCount * 3);

            // BVH nodes
            BvhNode[] nodes = null;
            if (nodeCount > 0)
            {
                nodes = new BvhNode[nodeCount];
                r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(nodes.AsSpan()));
            }

            // Triangle order
            int[] triOrder = null;
            if (nodeCount > 0 && fs.Position < fs.Length)
                triOrder = ReadIntArray(r, triangleCount);

            var mesh = new MeshData
            {
                Vertices = vertices,
                Indices = indices,
                VertexCount = vertexCount,
                TriangleCount = triangleCount,
                BoundsMin = boundsMin,
                BoundsMax = boundsMax,
            };

            DebugLogger.LogInfo($"[MeshLoader] Cache loaded: {path} ({vertexCount:N0}v, {triangleCount:N0}t, {nodeCount:N0} BVH nodes)");
            return (mesh, nodes, triOrder);
        }

        /// <summary>
        /// Check if a cache file exists and is valid (newer than OBJ source).
        /// </summary>
        public static bool IsCacheValid(string cachePath, string objPath)
        {
            if (!File.Exists(cachePath) || !File.Exists(objPath))
                return false;

            try
            {
                using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var r = new BinaryReader(fs);

                if (fs.Length < 52) return false;

                uint magic = r.ReadUInt32();
                if (magic != CACHE_MAGIC) return false;

                uint version = r.ReadUInt32();
                if (version != CACHE_VERSION) return false;

                // Skip counts (12 bytes: vertexCount + triangleCount + nodeCount)
                r.ReadBytes(12);
                // Skip bounds (24 bytes: boundsMin + boundsMax)
                r.ReadBytes(24);

                long cachedObjTicks = r.ReadInt64();
                long currentObjTicks = File.GetLastWriteTimeUtc(objPath).Ticks;

                return cachedObjTicks == currentObjTicks;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteFloatArray(BinaryWriter w, float[] arr, int count)
        {
            var bytes = MemoryMarshal.AsBytes(arr.AsSpan(0, count));
            w.Write(bytes);
        }

        private static void WriteIntArray(BinaryWriter w, int[] arr, int count)
        {
            var bytes = MemoryMarshal.AsBytes(arr.AsSpan(0, count));
            w.Write(bytes);
        }

        private static float[] ReadFloatArray(BinaryReader r, int count)
        {
            var arr = new float[count];
            r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(arr.AsSpan()));
            return arr;
        }

        private static int[] ReadIntArray(BinaryReader r, int count)
        {
            var arr = new int[count];
            r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(arr.AsSpan()));
            return arr;
        }

        #endregion
    }
}
