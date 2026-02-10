using System.Numerics;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// Wraps a loaded collision mesh + BVH accelerator.
    /// Thread-safe for concurrent reads. Disposable to release large LOH arrays.
    /// </summary>
    public sealed class RaycastScene : IDisposable
    {
        private BvhAccelerator _bvh;
        private readonly string _name;
        private volatile bool _disposed;

        public bool IsLoaded => _bvh != null;
        public string Name => _name;
        public int TriangleCount { get; }
        public int NodeCount { get; }

        private RaycastScene(BvhAccelerator bvh, string name, int triangleCount, int nodeCount)
        {
            _bvh = bvh;
            _name = name;
            TriangleCount = triangleCount;
            NodeCount = nodeCount;
        }

        /// <summary>
        /// Load a scene with three-tier priority: .bvhcache → .tmesh → .obj
        /// </summary>
        /// <param name="tmeshPath">Path to .tmesh file (may be null if not available)</param>
        /// <param name="objPath">Path to .obj file (may be null if not available)</param>
        /// <param name="name">Display name for logging</param>
        /// <param name="progress">Progress callback</param>
        public static RaycastScene Load(string tmeshPath, string objPath, string name, Action<string> progress = null)
        {
            // Determine the primary source file for cache invalidation
            string sourceFile = tmeshPath ?? objPath;
            if (sourceFile == null)
                throw new FileNotFoundException($"No mesh file found for {name}");

            var cachePath = Path.ChangeExtension(sourceFile, ".bvhcache");

            // Tier 1: Try .bvhcache
            if (MeshLoader.IsCacheValid(cachePath, sourceFile))
            {
                try
                {
                    progress?.Invoke($"Loading {name} from cache...");
                    var (mesh, nodes, triOrder) = MeshLoader.LoadCache(cachePath);

                    if (nodes != null && triOrder != null)
                    {
                        var cachedBvh = new BvhAccelerator(
                            mesh.Vertices, mesh.Indices, nodes, triOrder, nodes.Length);
                        DebugLogger.LogInfo($"[RaycastScene] {name}: loaded from cache ({mesh.TriangleCount:N0} tris, {nodes.Length:N0} nodes)");
                        return new RaycastScene(cachedBvh, name, mesh.TriangleCount, nodes.Length);
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogInfo($"[RaycastScene] Cache load failed for {name}, rebuilding: {ex.Message}");
                    try { File.Delete(cachePath); } catch { }
                }
            }

            // Tier 2: Try .tmesh (fast binary load + BVH build)
            if (tmeshPath != null && File.Exists(tmeshPath))
            {
                progress?.Invoke($"Loading {name} from TMESH...");
                var meshData = MeshLoader.LoadTmesh(tmeshPath, progress);
                return BuildAndCache(meshData, tmeshPath, cachePath, name, progress);
            }

            // Tier 3: Fall back to .obj (slow text parse + BVH build)
            if (objPath != null && File.Exists(objPath))
            {
                progress?.Invoke($"Parsing {name} OBJ...");
                var meshData = MeshLoader.LoadObj(objPath, progress);
                return BuildAndCache(meshData, objPath, cachePath, name, progress);
            }

            throw new FileNotFoundException($"No mesh file found for {name}");
        }

        /// <summary>
        /// Build BVH from mesh data and save cache.
        /// </summary>
        private static RaycastScene BuildAndCache(MeshData meshData, string sourcePath, string cachePath,
            string name, Action<string> progress)
        {
            progress?.Invoke($"Building BVH ({meshData.TriangleCount:N0} triangles)...");
            var bvh = new BvhAccelerator(meshData, progress);

            // Save cache for next time
            try
            {
                long sourceTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
                progress?.Invoke("Saving cache...");
                MeshLoader.SaveCache(cachePath, meshData, bvh.Nodes, bvh.TriOrder, sourceTicks);
            }
            catch (Exception ex)
            {
                DebugLogger.LogInfo($"[RaycastScene] Failed to save cache for {name}: {ex.Message}");
            }

            DebugLogger.LogInfo($"[RaycastScene] {name}: built ({meshData.TriangleCount:N0} tris, {bvh.NodeCount:N0} nodes)");
            return new RaycastScene(bvh, name, meshData.TriangleCount, bvh.NodeCount);
        }

        /// <summary>
        /// Returns true if clear line-of-sight between two points (no geometry blocking).
        /// </summary>
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            var bvh = _bvh; // Local copy for thread safety during dispose
            if (bvh == null) return true;
            return bvh.HasLineOfSight(from, to);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bvh?.Dispose();
            _bvh = null;
        }
    }
}
