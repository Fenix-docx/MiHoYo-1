using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Rendering;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Slime
{
    public struct SlimeShapeSdfInfo
    {
        public int3 Resolution;
        public float3 MinLocal;
        public float CellSize;

        public int Length => Resolution.x * Resolution.y * Resolution.z;
    }

    public static class SlimeSdf
    {
        public static float Sample(NativeArray<float> sdf, SlimeShapeSdfInfo info, float3 local)
        {
            if (!sdf.IsCreated || sdf.Length == 0 || info.CellSize <= 1e-6f || any(info.Resolution < 2))
                return 0f;
            if (!IsFinite(local))
                return 0f;

            float3 maxLocal = info.MinLocal + (float3)(info.Resolution - 1) * info.CellSize;
            float3 clampedLocal = clamp(local, info.MinLocal, maxLocal);
            float outsideDistance = length(local - clampedLocal);
            float3 grid = (clampedLocal - info.MinLocal) / info.CellSize;
            int3 baseCoord = clamp((int3)floor(grid), new int3(0), info.Resolution - 2);
            float3 t = saturate(grid - (float3)baseCoord);

            float c000 = Read(sdf, info, baseCoord + new int3(0, 0, 0));
            float c100 = Read(sdf, info, baseCoord + new int3(1, 0, 0));
            float c010 = Read(sdf, info, baseCoord + new int3(0, 1, 0));
            float c110 = Read(sdf, info, baseCoord + new int3(1, 1, 0));
            float c001 = Read(sdf, info, baseCoord + new int3(0, 0, 1));
            float c101 = Read(sdf, info, baseCoord + new int3(1, 0, 1));
            float c011 = Read(sdf, info, baseCoord + new int3(0, 1, 1));
            float c111 = Read(sdf, info, baseCoord + new int3(1, 1, 1));

            float c00 = lerp(c000, c100, t.x);
            float c10 = lerp(c010, c110, t.x);
            float c01 = lerp(c001, c101, t.x);
            float c11 = lerp(c011, c111, t.x);
            float c0 = lerp(c00, c10, t.y);
            float c1 = lerp(c01, c11, t.y);
            return lerp(c0, c1, t.z) + outsideDistance;
        }

        public static float3 Gradient(NativeArray<float> sdf, SlimeShapeSdfInfo info, float3 local)
        {
            float h = max(info.CellSize, 1e-4f);
            float dx = Sample(sdf, info, local + new float3(h, 0f, 0f)) -
                       Sample(sdf, info, local - new float3(h, 0f, 0f));
            float dy = Sample(sdf, info, local + new float3(0f, h, 0f)) -
                       Sample(sdf, info, local - new float3(0f, h, 0f));
            float dz = Sample(sdf, info, local + new float3(0f, 0f, h)) -
                       Sample(sdf, info, local - new float3(0f, 0f, h));
            return normalizesafe(new float3(dx, dy, dz), new float3(0f, 1f, 0f));
        }

        static float Read(NativeArray<float> sdf, SlimeShapeSdfInfo info, int3 coord)
        {
            coord = clamp(coord, new int3(0), info.Resolution - 1);
            return sdf[coord.x + info.Resolution.x * (coord.y + info.Resolution.y * coord.z)];
        }

        static bool IsFinite(float value)
        {
            return !isnan(value) && isfinite(value);
        }

        static bool IsFinite(float3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }

    public struct SlimeShapeTarget : IDisposable
    {
        public NativeArray<float3> RestLocalPositions;
        public NativeArray<float> Sdf;
        public SlimeShapeSdfInfo SdfInfo;
        public float3 LocalCenter;
        public bool IsClosedVolume;
        public bool UsesSurfaceFallback;

        public bool IsCreated => RestLocalPositions.IsCreated && Sdf.IsCreated;

        public static SlimeShapeTarget Build(Mesh mesh, int particleCount, int maxAxisResolution, Allocator allocator)
        {
            if (!TryBuild(mesh, particleCount, maxAxisResolution, allocator, out SlimeShapeTarget target, out string error))
                throw new ArgumentException(error, nameof(mesh));

            return target;
        }

        public static bool TryBuild(
            Mesh mesh,
            int particleCount,
            int maxAxisResolution,
            Allocator allocator,
            out SlimeShapeTarget target,
            out string error)
        {
            if (mesh == null)
            {
                target = default;
                error = "Shape target mesh is null.";
                return false;
            }
            if (particleCount <= 0)
            {
                target = default;
                error = "Shape target particle count must be greater than zero.";
                return false;
            }

            if (!TryGetMeshData(mesh, out Vector3[] meshVertices, out int[] meshTriangles, out error))
            {
                target = default;
                return false;
            }

            if (meshVertices == null || meshVertices.Length == 0 || meshTriangles == null || meshTriangles.Length < 3)
            {
                target = default;
                error = "Shape target mesh must contain vertices and triangles.";
                return false;
            }

            bool isClosedVolume = IsWatertight(meshTriangles);
            var vertices = new float3[meshVertices.Length];
            for (int i = 0; i < meshVertices.Length; i++)
                vertices[i] = meshVertices[i];

            Bounds bounds = mesh.bounds;
            float3 boundsSize = max((float3)bounds.size, new float3(1e-3f));
            int axisResolution = math.max(6, maxAxisResolution);
            float cellSize = cmax(boundsSize) / math.max(1, axisResolution - 5);
            float padding = cellSize * 2f;
            float3 minLocal = (float3)bounds.min - padding;
            float3 maxLocal = (float3)bounds.max + padding;
            int3 resolution = max(new int3(3), (int3)ceil((maxLocal - minLocal) / cellSize) + 1);

            var sdf = new NativeArray<float>(resolution.x * resolution.y * resolution.z, allocator, NativeArrayOptions.UninitializedMemory);
            var insideSamples = new List<float3>(particleCount * 2);
            SlimeShapeSdfInfo info = new SlimeShapeSdfInfo
            {
                Resolution = resolution,
                MinLocal = minLocal,
                CellSize = cellSize,
            };

            for (int z = 0; z < resolution.z; z++)
            for (int y = 0; y < resolution.y; y++)
            for (int x = 0; x < resolution.x; x++)
            {
                float3 p = minLocal + new float3(x, y, z) * cellSize;
                float unsignedDistance = DistanceToMesh(p, vertices, meshTriangles);
                bool inside = isClosedVolume && IsInsideClosedMesh(p, vertices, meshTriangles);
                float signedDistance = inside ? -unsignedDistance : unsignedDistance;
                int idx = x + resolution.x * (y + resolution.y * z);
                sdf[idx] = signedDistance;

                if (signedDistance <= 0f)
                    insideSamples.Add(p);
            }

            var restPositions = new NativeArray<float3>(particleCount, allocator, NativeArrayOptions.UninitializedMemory);
            bool usesSurfaceFallback = FillRestPositions(restPositions, insideSamples, vertices, meshTriangles, bounds, cellSize, isClosedVolume);
            float3 localCenter = ComputeCenter(restPositions);

            target = new SlimeShapeTarget
            {
                RestLocalPositions = restPositions,
                Sdf = sdf,
                SdfInfo = info,
                LocalCenter = localCenter,
                IsClosedVolume = isClosedVolume,
                UsesSurfaceFallback = usesSurfaceFallback,
            };
            error = null;
            return true;
        }

        static bool TryGetMeshData(Mesh mesh, out Vector3[] vertices, out int[] triangles, out string error)
        {
            vertices = null;
            triangles = null;
            error = null;

            if (mesh.isReadable)
            {
                vertices = mesh.vertices;
                triangles = mesh.triangles;
                return true;
            }

            try
            {
                using Mesh.MeshDataArray meshDataArray = Mesh.AcquireReadOnlyMeshData(mesh);
                if (meshDataArray.Length == 0)
                {
                    error = $"Shape target mesh '{mesh.name}' has no readable mesh data. Enable Read/Write in the mesh import settings.";
                    return false;
                }

                Mesh.MeshData meshData = meshDataArray[0];
                vertices = new Vector3[meshData.vertexCount];
                using (var vertexData = new NativeArray<Vector3>(meshData.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory))
                {
                    meshData.GetVertices(vertexData);
                    vertexData.CopyTo(vertices);
                }

                var indices = new List<int>();
                for (int subMesh = 0; subMesh < meshData.subMeshCount; subMesh++)
                {
                    SubMeshDescriptor descriptor = meshData.GetSubMesh(subMesh);
                    if (descriptor.topology != MeshTopology.Triangles)
                        continue;

                    using NativeArray<int> subMeshIndices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    meshData.GetIndices(subMeshIndices, subMesh);
                    for (int i = 0; i < subMeshIndices.Length; i++)
                        indices.Add(subMeshIndices[i]);
                }

                triangles = indices.ToArray();
                return true;
            }
            catch (Exception ex)
            {
                error = $"Shape target mesh '{mesh.name}' is not readable. Enable Read/Write in the mesh import settings. Details: {ex.Message}";
                return false;
            }
        }

        public static float SampleSdf(NativeArray<float> sdf, SlimeShapeSdfInfo info, float3 local)
        {
            return SlimeSdf.Sample(sdf, info, local);
        }

        public void Dispose()
        {
            if (RestLocalPositions.IsCreated) RestLocalPositions.Dispose();
            if (Sdf.IsCreated) Sdf.Dispose();
        }

        static bool FillRestPositions(
            NativeArray<float3> restPositions,
            List<float3> insideSamples,
            float3[] vertices,
            int[] triangles,
            Bounds bounds,
            float surfaceThickness,
            bool isClosedVolume)
        {
            if (!isClosedVolume || insideSamples.Count < max(8, restPositions.Length / 16))
            {
                FillSurfaceFallback(restPositions, vertices, triangles, bounds, surfaceThickness);
                return true;
            }

            for (int i = 0; i < restPositions.Length; i++)
            {
                int idx = (int)floor(((i + 0.5f) / restPositions.Length) * insideSamples.Count);
                restPositions[i] = insideSamples[clamp(idx, 0, insideSamples.Count - 1)];
            }

            return false;
        }

        static void FillSurfaceFallback(
            NativeArray<float3> restPositions,
            float3[] vertices,
            int[] triangles,
            Bounds bounds,
            float surfaceThickness)
        {
            var cumulativeAreas = new List<float>(triangles.Length / 3);
            float totalArea = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float3 a = vertices[triangles[i]];
                float3 b = vertices[triangles[i + 1]];
                float3 c = vertices[triangles[i + 2]];
                float area = length(cross(b - a, c - a)) * 0.5f;
                if (area <= 1e-8f || !IsFinite(area))
                {
                    cumulativeAreas.Add(totalArea);
                    continue;
                }

                totalArea += area;
                cumulativeAreas.Add(totalArea);
            }

            if (totalArea <= 1e-8f)
            {
                FillBoundsFallback(restPositions, bounds);
                return;
            }

            float thickness = max(surfaceThickness * 0.45f, 1e-4f);

            for (int i = 0; i < restPositions.Length; i++)
            {
                float areaTarget = ((i + 0.5f) / restPositions.Length) * totalArea;
                int tri = FindAreaIndex(cumulativeAreas, areaTarget) * 3;
                float3 a = vertices[triangles[tri]];
                float3 b = vertices[triangles[tri + 1]];
                float3 c = vertices[triangles[tri + 2]];
                float2 sample = Hammersley2D(i);
                float sqrtU = sqrt(sample.x);
                float u = 1f - sqrtU;
                float v = sample.y * sqrtU;
                float w = 1f - u - v;
                float3 normal = normalizesafe(cross(b - a, c - a), new float3(0f, 1f, 0f));
                float layer = ((i % 5) - 2) * 0.25f;
                restPositions[i] = a * u + b * v + c * w + normal * thickness * layer;
            }
        }

        static int FindAreaIndex(List<float> cumulativeAreas, float value)
        {
            int lo = 0;
            int hi = cumulativeAreas.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cumulativeAreas[mid] < value)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return clamp(lo, 0, cumulativeAreas.Count - 1);
        }

        static float2 Hammersley2D(int i)
        {
            return new float2(frac(i * 0.61803398875f), RadicalInverseBase2((uint)i));
        }

        static float RadicalInverseBase2(uint bits)
        {
            bits = (bits << 16) | (bits >> 16);
            bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
            bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
            bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
            bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
            return bits * 2.3283064365386963e-10f;
        }

        static void FillBoundsFallback(NativeArray<float3> restPositions, Bounds bounds)
        {
            float3 center = bounds.center;
            float3 extents = max((float3)bounds.extents * 0.75f, new float3(1e-3f));
            float goldenAngle = PI * (3f - sqrt(5f));

            for (int i = 0; i < restPositions.Length; i++)
            {
                float t = (i + 0.5f) / max(1, restPositions.Length);
                float y = 1f - 2f * t;
                float r = sqrt(max(0f, 1f - y * y));
                float theta = goldenAngle * i;
                float shell = pow(t, 1f / 3f);
                float3 unit = new float3(cos(theta) * r, y, sin(theta) * r) * shell;
                restPositions[i] = center + unit * extents;
            }
        }

        static float3 ComputeCenter(NativeArray<float3> positions)
        {
            float3 sum = new float3(0f, 0f, 0f);
            for (int i = 0; i < positions.Length; i++)
                sum += positions[i];

            return positions.Length > 0 ? sum / positions.Length : new float3(0f, 0f, 0f);
        }

        static bool IsWatertight(int[] triangles)
        {
            if (triangles == null || triangles.Length < 3 || triangles.Length % 3 != 0)
                return false;

            var edgeUseCounts = new Dictionary<EdgeKey, int>(triangles.Length);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                AddEdge(edgeUseCounts, triangles[i], triangles[i + 1]);
                AddEdge(edgeUseCounts, triangles[i + 1], triangles[i + 2]);
                AddEdge(edgeUseCounts, triangles[i + 2], triangles[i]);
            }

            foreach (int count in edgeUseCounts.Values)
            {
                if (count != 2)
                    return false;
            }

            return edgeUseCounts.Count > 0;
        }

        static void AddEdge(Dictionary<EdgeKey, int> edgeUseCounts, int a, int b)
        {
            var key = new EdgeKey(a, b);
            edgeUseCounts.TryGetValue(key, out int count);
            edgeUseCounts[key] = count + 1;
        }

        readonly struct EdgeKey : IEquatable<EdgeKey>
        {
            readonly int _a;
            readonly int _b;

            public EdgeKey(int a, int b)
            {
                _a = min(a, b);
                _b = max(a, b);
            }

            public bool Equals(EdgeKey other)
            {
                return _a == other._a && _b == other._b;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_a * 397) ^ _b;
                }
            }
        }

        static bool IsFinite(float value)
        {
            return !isnan(value) && isfinite(value);
        }

        static float DistanceToMesh(float3 p, float3[] vertices, int[] triangles)
        {
            float minSq = float.MaxValue;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float3 a = vertices[triangles[i]];
                float3 b = vertices[triangles[i + 1]];
                float3 c = vertices[triangles[i + 2]];
                minSq = min(minSq, DistanceSqToTriangle(p, a, b, c));
            }
            return sqrt(minSq);
        }

        static bool IsInsideClosedMesh(float3 p, float3[] vertices, int[] triangles)
        {
            float3 dir = normalize(new float3(1f, 0.371f, 0.619f));
            int hits = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                float3 a = vertices[triangles[i]];
                float3 b = vertices[triangles[i + 1]];
                float3 c = vertices[triangles[i + 2]];
                if (RayIntersectsTriangle(p, dir, a, b, c, out float t) && t > 1e-5f)
                    hits++;
            }
            return (hits & 1) == 1;
        }

        static bool RayIntersectsTriangle(float3 origin, float3 direction, float3 a, float3 b, float3 c, out float t)
        {
            t = 0f;
            float3 edge1 = b - a;
            float3 edge2 = c - a;
            float3 h = cross(direction, edge2);
            float det = dot(edge1, h);
            if (abs(det) < 1e-7f)
                return false;

            float invDet = 1f / det;
            float3 s = origin - a;
            float u = invDet * dot(s, h);
            if (u < 0f || u > 1f)
                return false;

            float3 q = cross(s, edge1);
            float v = invDet * dot(direction, q);
            if (v < 0f || u + v > 1f)
                return false;

            t = invDet * dot(edge2, q);
            return t > 0f;
        }

        static float DistanceSqToTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;
            float d1 = dot(ab, ap);
            float d2 = dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return lengthsq(ap);

            float3 bp = p - b;
            float d3 = dot(ab, bp);
            float d4 = dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return lengthsq(bp);

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                float3 projection = a + v * ab;
                return lengthsq(p - projection);
            }

            float3 cp = p - c;
            float d5 = dot(ab, cp);
            float d6 = dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return lengthsq(cp);

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                float3 projection = a + w * ac;
                return lengthsq(p - projection);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                float3 projection = b + w * (c - b);
                return lengthsq(p - projection);
            }

            float3 normal = normalize(cross(ab, ac));
            float distance = dot(p - a, normal);
            return distance * distance;
        }
    }
}
