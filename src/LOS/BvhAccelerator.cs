using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LoneEftDmaRadar.UI.Misc;

namespace LoneEftDmaRadar.LOS
{
    /// <summary>
    /// BVH node — 32 bytes, flat array layout for cache-friendly traversal.
    /// Leaf:     RightOrCount &lt; 0, |RightOrCount| = triangle count, LeftOrOffset = first tri in sorted array.
    /// Internal: RightOrCount >= 0 = right child index, left child = thisIndex + 1 (DFS order).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BvhNode
    {
        public Vector3 BoundsMin;
        public Vector3 BoundsMax;
        public int LeftOrOffset;
        public int RightOrCount;
    }

    /// <summary>
    /// BVH builder and ray traversal engine for collision meshes.
    /// Uses binned SAH for construction and stack-based iterative traversal.
    /// </summary>
    public sealed class BvhAccelerator : IDisposable
    {
        private const int MAX_LEAF_TRIS = 4;
        private const int SAH_BINS = 12;
        private const float TRAVERSAL_COST = 1.0f;
        private const float INTERSECT_COST = 1.5f;

        private float[] _vertices;
        private int[] _indices;
        private BvhNode[] _nodes;
        private int[] _triOrder; // Sorted triangle indices for BVH leaves
        private int _nodeCount;
        private volatile bool _disposed;

        public BvhNode[] Nodes => _nodes;
        public int[] TriOrder => _triOrder;
        public int NodeCount => _nodeCount;

        #region Construction

        /// <summary>
        /// Build a BVH from mesh data. Takes ~5-15s for 22M triangles.
        /// </summary>
        public BvhAccelerator(in MeshData mesh, Action<string> progress = null)
            : this(mesh.Vertices, mesh.Indices, mesh.VertexCount, mesh.TriangleCount, progress)
        {
        }

        /// <summary>
        /// Build from raw arrays.
        /// </summary>
        public BvhAccelerator(float[] vertices, int[] indices, int vertexCount, int triangleCount, Action<string> progress = null)
        {
            _vertices = vertices;
            _indices = indices;

            progress?.Invoke($"Building BVH for {triangleCount:N0} triangles...");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Compute triangle centroids and AABBs
            var centroids = new Vector3[triangleCount];
            var triAABBMin = new Vector3[triangleCount];
            var triAABBMax = new Vector3[triangleCount];
            _triOrder = new int[triangleCount];

            for (int i = 0; i < triangleCount; i++)
            {
                _triOrder[i] = i;
                int b = i * 3;
                GetTriangleVerts(indices, vertices, b, out var v0, out var v1, out var v2);
                centroids[i] = (v0 + v1 + v2) * (1.0f / 3.0f);
                triAABBMin[i] = Vector3.Min(Vector3.Min(v0, v1), v2);
                triAABBMax[i] = Vector3.Max(Vector3.Max(v0, v1), v2);
            }

            progress?.Invoke("Centroids computed, building tree...");

            // Allocate nodes (worst case: 2*N-1 for N leaves, each leaf has 1+ tris)
            int maxNodes = Math.Max(triangleCount * 2, 64);
            _nodes = new BvhNode[maxNodes];
            _nodeCount = 0;

            // Recursive build
            BuildRecursive(0, triangleCount, centroids, triAABBMin, triAABBMax);

            sw.Stop();
            DebugLogger.LogInfo($"[BVH] Built: {_nodeCount:N0} nodes, {triangleCount:N0} triangles in {sw.Elapsed.TotalSeconds:F1}s");
            progress?.Invoke($"BVH built: {_nodeCount:N0} nodes in {sw.Elapsed.TotalSeconds:F1}s");
        }

        /// <summary>
        /// Load from pre-built cache data (no build needed).
        /// </summary>
        public BvhAccelerator(float[] vertices, int[] indices, BvhNode[] nodes, int[] triOrder, int nodeCount)
        {
            _vertices = vertices;
            _indices = indices;
            _nodes = nodes;
            _triOrder = triOrder;
            _nodeCount = nodeCount;
        }

        private int BuildRecursive(int start, int end, Vector3[] centroids, Vector3[] triMin, Vector3[] triMax)
        {
            int nodeIdx = _nodeCount++;
            int count = end - start;

            // Compute bounds for this range
            var bMin = new Vector3(float.MaxValue);
            var bMax = new Vector3(float.MinValue);
            for (int i = start; i < end; i++)
            {
                bMin = Vector3.Min(bMin, triMin[_triOrder[i]]);
                bMax = Vector3.Max(bMax, triMax[_triOrder[i]]);
            }

            if (count <= MAX_LEAF_TRIS)
            {
                // Leaf node
                _nodes[nodeIdx] = new BvhNode
                {
                    BoundsMin = bMin,
                    BoundsMax = bMax,
                    LeftOrOffset = start,
                    RightOrCount = -count,
                };
                return nodeIdx;
            }

            // Find best split using binned SAH
            int bestAxis = -1;
            int bestSplit = -1;
            float bestCost = float.MaxValue;

            // Centroid bounds
            var cMin = new Vector3(float.MaxValue);
            var cMax = new Vector3(float.MinValue);
            for (int i = start; i < end; i++)
            {
                cMin = Vector3.Min(cMin, centroids[_triOrder[i]]);
                cMax = Vector3.Max(cMax, centroids[_triOrder[i]]);
            }

            float parentArea = SurfaceArea(bMin, bMax);
            if (parentArea <= 0) parentArea = 1e-10f;

            for (int axis = 0; axis < 3; axis++)
            {
                float cMinA = GetAxis(cMin, axis);
                float cMaxA = GetAxis(cMax, axis);
                if (cMaxA - cMinA < 1e-8f) continue; // Flat on this axis

                // Bin triangles
                Span<int> binCount = stackalloc int[SAH_BINS];
                Span<float> binMinX = stackalloc float[SAH_BINS];
                Span<float> binMinY = stackalloc float[SAH_BINS];
                Span<float> binMinZ = stackalloc float[SAH_BINS];
                Span<float> binMaxX = stackalloc float[SAH_BINS];
                Span<float> binMaxY = stackalloc float[SAH_BINS];
                Span<float> binMaxZ = stackalloc float[SAH_BINS];

                for (int b = 0; b < SAH_BINS; b++)
                {
                    binCount[b] = 0;
                    binMinX[b] = float.MaxValue; binMinY[b] = float.MaxValue; binMinZ[b] = float.MaxValue;
                    binMaxX[b] = float.MinValue; binMaxY[b] = float.MinValue; binMaxZ[b] = float.MinValue;
                }

                float scale = SAH_BINS / (cMaxA - cMinA);
                for (int i = start; i < end; i++)
                {
                    int ti = _triOrder[i];
                    int bin = Math.Clamp((int)((GetAxis(centroids[ti], axis) - cMinA) * scale), 0, SAH_BINS - 1);
                    binCount[bin]++;
                    binMinX[bin] = Math.Min(binMinX[bin], triMin[ti].X);
                    binMinY[bin] = Math.Min(binMinY[bin], triMin[ti].Y);
                    binMinZ[bin] = Math.Min(binMinZ[bin], triMin[ti].Z);
                    binMaxX[bin] = Math.Max(binMaxX[bin], triMax[ti].X);
                    binMaxY[bin] = Math.Max(binMaxY[bin], triMax[ti].Y);
                    binMaxZ[bin] = Math.Max(binMaxZ[bin], triMax[ti].Z);
                }

                // Sweep from left to find costs
                Span<float> leftArea = stackalloc float[SAH_BINS - 1];
                Span<int> leftCount = stackalloc int[SAH_BINS - 1];
                {
                    var lMin = new Vector3(float.MaxValue);
                    var lMax = new Vector3(float.MinValue);
                    int lc = 0;
                    for (int i = 0; i < SAH_BINS - 1; i++)
                    {
                        if (binCount[i] > 0)
                        {
                            lMin = Vector3.Min(lMin, new Vector3(binMinX[i], binMinY[i], binMinZ[i]));
                            lMax = Vector3.Max(lMax, new Vector3(binMaxX[i], binMaxY[i], binMaxZ[i]));
                        }
                        lc += binCount[i];
                        leftArea[i] = lc > 0 ? SurfaceArea(lMin, lMax) : 0;
                        leftCount[i] = lc;
                    }
                }

                // Sweep from right
                {
                    var rMin = new Vector3(float.MaxValue);
                    var rMax = new Vector3(float.MinValue);
                    int rc = 0;
                    for (int i = SAH_BINS - 1; i >= 1; i--)
                    {
                        if (binCount[i] > 0)
                        {
                            rMin = Vector3.Min(rMin, new Vector3(binMinX[i], binMinY[i], binMinZ[i]));
                            rMax = Vector3.Max(rMax, new Vector3(binMaxX[i], binMaxY[i], binMaxZ[i]));
                        }
                        rc += binCount[i];
                        float rArea = rc > 0 ? SurfaceArea(rMin, rMax) : 0;

                        int splitIdx = i - 1;
                        if (leftCount[splitIdx] > 0 && rc > 0)
                        {
                            float cost = TRAVERSAL_COST + INTERSECT_COST *
                                (leftCount[splitIdx] * leftArea[splitIdx] + rc * rArea) / parentArea;

                            if (cost < bestCost)
                            {
                                bestCost = cost;
                                bestAxis = axis;
                                bestSplit = splitIdx;
                            }
                        }
                    }
                }
            }

            // If SAH didn't find a good split, fallback to median on longest axis
            float leafCost = INTERSECT_COST * count;
            if (bestAxis < 0 || bestCost >= leafCost)
            {
                if (count <= MAX_LEAF_TRIS * 4)
                {
                    // Make a bigger leaf
                    _nodes[nodeIdx] = new BvhNode
                    {
                        BoundsMin = bMin,
                        BoundsMax = bMax,
                        LeftOrOffset = start,
                        RightOrCount = -count,
                    };
                    return nodeIdx;
                }

                // Force median split on longest axis
                var extent = cMax - cMin;
                bestAxis = extent.X >= extent.Y && extent.X >= extent.Z ? 0 :
                           extent.Y >= extent.Z ? 1 : 2;
            }

            // Partition triOrder[start..end) by the split
            int mid;
            if (bestSplit >= 0)
            {
                float cMinA = GetAxis(cMin, bestAxis);
                float cMaxA = GetAxis(cMax, bestAxis);
                float splitPos = cMinA + (bestSplit + 1) * (cMaxA - cMinA) / SAH_BINS;
                mid = Partition(start, end, centroids, bestAxis, splitPos);
            }
            else
            {
                // Median split
                mid = (start + end) / 2;
                NthElement(start, end, mid, centroids, bestAxis);
            }

            // Guard against degenerate splits
            if (mid <= start) mid = start + 1;
            if (mid >= end) mid = end - 1;

            // Recurse — left child is nodeIdx+1 (DFS order)
            _nodes[nodeIdx].BoundsMin = bMin;
            _nodes[nodeIdx].BoundsMax = bMax;

            BuildRecursive(start, mid, centroids, triMin, triMax); // Left child = nodeIdx + 1

            int rightIdx = BuildRecursive(mid, end, centroids, triMin, triMax);
            _nodes[nodeIdx].RightOrCount = rightIdx;

            return nodeIdx;
        }

        private int Partition(int start, int end, Vector3[] centroids, int axis, float splitPos)
        {
            int lo = start, hi = end - 1;
            while (lo <= hi)
            {
                if (GetAxis(centroids[_triOrder[lo]], axis) <= splitPos)
                    lo++;
                else
                {
                    (_triOrder[lo], _triOrder[hi]) = (_triOrder[hi], _triOrder[lo]);
                    hi--;
                }
            }
            return lo;
        }

        private void NthElement(int start, int end, int nth, Vector3[] centroids, int axis)
        {
            // Simple Quickselect for median split
            while (start < end - 1)
            {
                float pivot = GetAxis(centroids[_triOrder[(start + end) / 2]], axis);
                int lo = start, hi = end - 1;
                while (lo <= hi)
                {
                    while (GetAxis(centroids[_triOrder[lo]], axis) < pivot) lo++;
                    while (GetAxis(centroids[_triOrder[hi]], axis) > pivot) hi--;
                    if (lo <= hi)
                    {
                        (_triOrder[lo], _triOrder[hi]) = (_triOrder[hi], _triOrder[lo]);
                        lo++; hi--;
                    }
                }

                if (nth <= hi) end = hi + 1;
                else if (nth >= lo) start = lo;
                else break;
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _vertices = null;
            _indices = null;
            _nodes = null;
            _triOrder = null;
        }

        #endregion

        #region Traversal

        /// <summary>
        /// Returns true if a ray hits any triangle before maxDistance (i.e., LOS is blocked).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool AnyHit(Vector3 origin, Vector3 direction, float maxDistance)
        {
            if (_disposed) return false;

            // Pre-compute reciprocal direction for slab test
            var invDir = new Vector3(
                direction.X != 0 ? 1.0f / direction.X : float.MaxValue,
                direction.Y != 0 ? 1.0f / direction.Y : float.MaxValue,
                direction.Z != 0 ? 1.0f / direction.Z : float.MaxValue);

            Span<int> stack = stackalloc int[64];
            int sp = 0;
            stack[sp++] = 0; // root

            while (sp > 0)
            {
                int idx = stack[--sp];
                ref readonly BvhNode node = ref _nodes[idx];

                if (!RayAABB(origin, invDir, node.BoundsMin, node.BoundsMax, maxDistance))
                    continue;

                if (node.RightOrCount < 0)
                {
                    // Leaf
                    int triCount = -node.RightOrCount;
                    int triStart = node.LeftOrOffset;
                    for (int i = 0; i < triCount; i++)
                    {
                        int ti = _triOrder[triStart + i] * 3;
                        if (RayTriangle(origin, direction, ti, maxDistance))
                            return true; // Any-hit early exit
                    }
                }
                else
                {
                    // Internal — push right, then left (left processed first)
                    stack[sp++] = node.RightOrCount;
                    stack[sp++] = idx + 1;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if there is clear line-of-sight from 'from' to 'to' (no hit between them).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            if (_disposed) return true;
            var delta = to - from;
            float distance = delta.Length();
            if (distance < 1e-6f) return true;

            var direction = delta / distance;
            // Shrink distance slightly to avoid self-intersection at the target
            return !AnyHit(from, direction, distance - 0.01f);
        }

        #endregion

        #region Intersection Primitives

        /// <summary>
        /// Slab-based ray-AABB intersection test.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RayAABB(Vector3 origin, Vector3 invDir, Vector3 bMin, Vector3 bMax, float maxT)
        {
            float t1 = (bMin.X - origin.X) * invDir.X;
            float t2 = (bMax.X - origin.X) * invDir.X;
            float tmin = Math.Min(t1, t2);
            float tmax = Math.Max(t1, t2);

            t1 = (bMin.Y - origin.Y) * invDir.Y;
            t2 = (bMax.Y - origin.Y) * invDir.Y;
            tmin = Math.Max(tmin, Math.Min(t1, t2));
            tmax = Math.Min(tmax, Math.Max(t1, t2));

            t1 = (bMin.Z - origin.Z) * invDir.Z;
            t2 = (bMax.Z - origin.Z) * invDir.Z;
            tmin = Math.Max(tmin, Math.Min(t1, t2));
            tmax = Math.Min(tmax, Math.Max(t1, t2));

            return tmax >= Math.Max(tmin, 0f) && tmin < maxT;
        }

        /// <summary>
        /// Moller-Trumbore ray-triangle intersection.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool RayTriangle(Vector3 origin, Vector3 direction, int indexOffset, float maxT)
        {
            int i0 = _indices[indexOffset];
            int i1 = _indices[indexOffset + 1];
            int i2 = _indices[indexOffset + 2];

            var v0 = new Vector3(_vertices[i0 * 3], _vertices[i0 * 3 + 1], _vertices[i0 * 3 + 2]);
            var v1 = new Vector3(_vertices[i1 * 3], _vertices[i1 * 3 + 1], _vertices[i1 * 3 + 2]);
            var v2 = new Vector3(_vertices[i2 * 3], _vertices[i2 * 3 + 1], _vertices[i2 * 3 + 2]);

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var h = Vector3.Cross(direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -1e-8f && a < 1e-8f)
                return false; // Parallel

            float f = 1.0f / a;
            var s = origin - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f)
                return false;

            var q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(direction, q);
            if (v < 0f || u + v > 1f)
                return false;

            float t = f * Vector3.Dot(edge2, q);
            return t > 1e-6f && t < maxT;
        }

        #endregion

        #region Helpers

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float GetAxis(Vector3 v, int axis)
        {
            return axis switch
            {
                0 => v.X,
                1 => v.Y,
                _ => v.Z,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float SurfaceArea(Vector3 bMin, Vector3 bMax)
        {
            var d = bMax - bMin;
            return 2.0f * (d.X * d.Y + d.Y * d.Z + d.Z * d.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetTriangleVerts(int[] indices, float[] verts, int indexOffset,
            out Vector3 v0, out Vector3 v1, out Vector3 v2)
        {
            int i0 = indices[indexOffset] * 3;
            int i1 = indices[indexOffset + 1] * 3;
            int i2 = indices[indexOffset + 2] * 3;
            v0 = new Vector3(verts[i0], verts[i0 + 1], verts[i0 + 2]);
            v1 = new Vector3(verts[i1], verts[i1 + 1], verts[i1 + 2]);
            v2 = new Vector3(verts[i2], verts[i2 + 1], verts[i2 + 2]);
        }

        #endregion
    }
}
