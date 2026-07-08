using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Slime.Tests
{
    public class SlimeSurfaceBuilderTests
    {
        [Test]
        public void MarchingCubesReportsSkippedBlocksBelowIso()
        {
            var keys = new NativeArray<int3>(1, Allocator.TempJob);
            var grid = new NativeArray<float>(SlimeConst.GridSize, Allocator.TempJob);
            var gridLut = new NativeHashMap<int3, int>(1, Allocator.TempJob);
            var blockScratch = new NativeArray<float>(SlimeMC.MarchingCubesJob.BlockScratchLength, Allocator.TempJob);
            var stats = new NativeArray<int>(SlimeMC.MarchingCubesJob.StatsLength, Allocator.TempJob);
            var verts = new NativeList<float3>(4096, Allocator.TempJob);
            var norms = new NativeList<float3>(4096, Allocator.TempJob);
            var tris = new NativeList<int>(4096, Allocator.TempJob);

            try
            {
                keys[0] = new int3(0, 0, 0);
                Assert.That(gridLut.TryAdd(keys[0], 0), Is.True);
                for (int i = 0; i < grid.Length; i++)
                    grid[i] = 0.25f;

                new SlimeMC.MarchingCubesJob
                {
                    Keys = keys,
                    GridLut = gridLut,
                    Grid = grid,
                    BlockScratch = blockScratch,
                    Stats = stats,
                    VertsOut = verts,
                    NormsOut = norms,
                    TrisOut = tris,
                    IsoLevel = 1f,
                    Origin = new float3(0f, 0f, 0f),
                    CellScale = 1f,
                }.Schedule().Complete();

                Assert.That(stats[0], Is.EqualTo(1), "one sparse block should be inspected");
                Assert.That(stats[1], Is.EqualTo(1), "below-iso blocks should be skipped before cube traversal");
                Assert.That(verts.Length, Is.EqualTo(0));
                Assert.That(tris.Length, Is.EqualTo(0));
            }
            finally
            {
                tris.Dispose();
                norms.Dispose();
                verts.Dispose();
                stats.Dispose();
                blockScratch.Dispose();
                gridLut.Dispose();
                grid.Dispose();
                keys.Dispose();
            }
        }

        [Test]
        public void MarchingCubesKeepsBoundarySurfaceFromNeighbourDensity()
        {
            var keys = new NativeArray<int3>(1, Allocator.TempJob);
            var grid = new NativeArray<float>(SlimeConst.GridSize * 2, Allocator.TempJob);
            var gridLut = new NativeHashMap<int3, int>(2, Allocator.TempJob);
            var blockScratch = new NativeArray<float>(SlimeMC.MarchingCubesJob.BlockScratchLength, Allocator.TempJob);
            var stats = new NativeArray<int>(SlimeMC.MarchingCubesJob.StatsLength, Allocator.TempJob);
            var verts = new NativeList<float3>(4096, Allocator.TempJob);
            var norms = new NativeList<float3>(4096, Allocator.TempJob);
            var tris = new NativeList<int>(4096, Allocator.TempJob);

            try
            {
                int3 ownerKey = new int3(0, 0, 0);
                int3 neighbourKey = new int3(1, 0, 0);
                keys[0] = ownerKey;
                Assert.That(gridLut.TryAdd(ownerKey, 0), Is.True);
                Assert.That(gridLut.TryAdd(neighbourKey, SlimeConst.GridSize), Is.True);

                for (int j = 0; j < SlimeConst.GridSize; j++)
                {
                    int3 c = LocalCoord(j);
                    if (c.x == 0)
                        grid[SlimeConst.GridSize + j] = 2f;
                }

                new SlimeMC.MarchingCubesJob
                {
                    Keys = keys,
                    GridLut = gridLut,
                    Grid = grid,
                    BlockScratch = blockScratch,
                    Stats = stats,
                    VertsOut = verts,
                    NormsOut = norms,
                    TrisOut = tris,
                    IsoLevel = 1f,
                    Origin = new float3(0f, 0f, 0f),
                    CellScale = 1f,
                }.Schedule().Complete();

                Assert.That(stats[0], Is.EqualTo(1));
                Assert.That(stats[1], Is.EqualTo(0), "neighbour density must keep the owner block active");
                Assert.That(verts.Length, Is.GreaterThan(0));
                Assert.That(tris.Length, Is.GreaterThan(0));
            }
            finally
            {
                tris.Dispose();
                norms.Dispose();
                verts.Dispose();
                stats.Dispose();
                blockScratch.Dispose();
                gridLut.Dispose();
                grid.Dispose();
                keys.Dispose();
            }
        }

        [Test]
        public void SurfaceContactUtilityFindsColliderNearMeshVertex()
        {
            GameObject colliderObject = new GameObject("surface contact target");
            Mesh mesh = new Mesh { name = "contact mesh" };
            var vertices = new System.Collections.Generic.List<Vector3>(16);

            try
            {
                BoxCollider box = colliderObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = Vector3.one * 0.25f;

                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();

                bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
                    mesh,
                    new Collider[] { box },
                    vertices,
                    maxVertexChecks: 16,
                    contactPadding: 0.05f,
                    out Vector3 contactPoint);

                Assert.That(touched, Is.True);
                Assert.That(Vector3.Distance(contactPoint, Vector3.zero), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(colliderObject);
            }
        }

        [Test]
        public void SurfaceContactUtilityCapsVertexChecksAcrossColliders()
        {
            GameObject missObject = new GameObject("surface contact budget miss");
            GameObject hitObject = new GameObject("surface contact budget hit");
            Mesh mesh = new Mesh { name = "budget contact mesh" };
            var vertices = new System.Collections.Generic.List<Vector3>(16);

            try
            {
                BoxCollider missBox = missObject.AddComponent<BoxCollider>();
                missBox.center = Vector3.right;
                missBox.size = Vector3.one * 0.1f;

                BoxCollider hitBox = hitObject.AddComponent<BoxCollider>();
                hitBox.center = Vector3.zero;
                hitBox.size = Vector3.one * 0.1f;

                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();

                bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
                    mesh,
                    new Collider[] { missBox, hitBox },
                    vertices,
                    maxVertexChecks: 1,
                    contactPadding: 0.01f,
                    out Vector3 contactPoint);

                Assert.That(touched, Is.True);
                Assert.That(Vector3.Distance(contactPoint, Vector3.zero), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(missObject);
                Object.DestroyImmediate(hitObject);
            }
        }

        [Test]
        public void SurfaceContactUtilityTreatsNonPositiveVertexChecksAsOne()
        {
            GameObject colliderObject = new GameObject("surface contact nonpositive budget");
            Mesh mesh = new Mesh { name = "nonpositive budget contact mesh" };
            var vertices = new System.Collections.Generic.List<Vector3>(16);

            try
            {
                BoxCollider box = colliderObject.AddComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = Vector3.one * 0.25f;

                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();

                bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
                    mesh,
                    new Collider[] { box },
                    vertices,
                    maxVertexChecks: 0,
                    contactPadding: 0.05f,
                    out Vector3 contactPoint);

                Assert.That(touched, Is.True);
                Assert.That(Vector3.Distance(contactPoint, Vector3.zero), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(colliderObject);
            }
        }

        [Test]
        public void SurfaceContactUtilityIgnoresFarCollider()
        {
            GameObject colliderObject = new GameObject("far surface contact target");
            Mesh mesh = new Mesh { name = "far contact mesh" };
            var vertices = new System.Collections.Generic.List<Vector3>(16);

            try
            {
                colliderObject.transform.position = Vector3.one * 10f;
                BoxCollider box = colliderObject.AddComponent<BoxCollider>();
                box.size = Vector3.one * 0.25f;

                mesh.vertices = new[]
                {
                    Vector3.zero,
                    Vector3.right,
                    Vector3.up,
                };
                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();

                bool touched = SlimeSurfaceContactUtility.TryGetMeshContact(
                    mesh,
                    new Collider[] { box },
                    vertices,
                    maxVertexChecks: 16,
                    contactPadding: 0.05f,
                    out _);

                Assert.That(touched, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(colliderObject);
            }
        }

        [Test]
        public void SurfaceContactUtilityRecognizesSlimeComponents()
        {
            GameObject slimeObject = new GameObject("slime object");
            GameObject plainObject = new GameObject("plain object");

            try
            {
                SlimeSimulator simulator = slimeObject.AddComponent<SlimeSimulator>();
                BoxCollider slimeCollider = slimeObject.AddComponent<BoxCollider>();
                BoxCollider plainCollider = plainObject.AddComponent<BoxCollider>();

                Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(slimeCollider), Is.True);
                Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(simulator), Is.True);
                Assert.That(SlimeSurfaceContactUtility.IsSlimeObject(plainCollider), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(slimeObject);
                Object.DestroyImmediate(plainObject);
            }
        }

        [Test]
        public void SurfaceBuilderDefaultsToCpuSurfaceMode()
        {
            GameObject slimeObject = new GameObject("surface builder mode test");

            try
            {
                slimeObject.AddComponent<MeshFilter>();
                slimeObject.AddComponent<MeshRenderer>();
                SlimeSurfaceBuilder builder = slimeObject.AddComponent<SlimeSurfaceBuilder>();

                Assert.That(builder.SurfaceBuildMode, Is.EqualTo(SlimeSurfaceBuildMode.Cpu));
            }
            finally
            {
                Object.DestroyImmediate(slimeObject);
            }
        }

        static int3 LocalCoord(int idx)
        {
            return new int3(idx & 3, (idx >> 2) & 3, (idx >> 4) & 3);
        }
    }
}
