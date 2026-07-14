using NUnit.Framework;
using System.IO;
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

        [Test]
        public void GpuSurfacePipelineReportsUnsupportedWithoutShader()
        {
            using SlimeGpuSurfacePipeline pipeline = new SlimeGpuSurfacePipeline(null);
            Assert.That(pipeline.IsSupported, Is.False);
            Assert.That(pipeline.LastScheduleStatus, Is.EqualTo(SlimeGpuSurfaceScheduleStatus.NotScheduled));
        }

        [Test]
        public void GpuSurfacePipelineExposesReadbackFreshnessDiagnostics()
        {
            using SlimeGpuSurfacePipeline pipeline = new SlimeGpuSurfacePipeline(null);

            Assert.That(pipeline.LastScheduledReadbackGeneration, Is.EqualTo(0));
            Assert.That(pipeline.LastAppliedReadbackGeneration, Is.EqualTo(0));
            Assert.That(pipeline.LastDroppedReadbackGeneration, Is.EqualTo(0));
            Assert.That(pipeline.PendingReadbackAgeFrames, Is.EqualTo(0));
            Assert.That(pipeline.LastAppliedReadbackFrame, Is.EqualTo(0));
        }

        [Test]
        public void GpuSurfacePipelineOnlyClearsMeshForSuccessfulEmptySurface()
        {
            Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
                readbackSucceeded: false, readbackSuppressed: false, vertexCount: 0), Is.False);
            Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
                readbackSucceeded: true, readbackSuppressed: true, vertexCount: 0), Is.False);
            Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
                readbackSucceeded: true, readbackSuppressed: false, vertexCount: 0), Is.True);
            Assert.That(SlimeGpuSurfacePipeline.ShouldClearMeshAfterReadback(
                readbackSucceeded: true, readbackSuppressed: false, vertexCount: 3), Is.False);
        }

        [Test]
        public void GpuSurfacePipelineUsesSinglePendingReadback()
        {
            using SlimeGpuSurfacePipeline pipeline = new SlimeGpuSurfacePipeline(null);
            Assert.That(pipeline.MaxPendingReadbacks, Is.EqualTo(1));
        }

        [Test]
        public void GpuSurfacePipelineEstimatesSurfaceVertexReadbackBytes()
        {
            Assert.That(SlimeGpuSurfacePipeline.EstimateSurfaceVertexReadbackBytes(-1), Is.EqualTo(0));
            Assert.That(SlimeGpuSurfacePipeline.EstimateSurfaceVertexReadbackBytes(3), Is.EqualTo(72));
            Assert.That(SlimeGpuSurfacePipeline.EstimateSurfaceVertexReadbackBytes(1024), Is.EqualTo(24576));
        }

        [Test]
        public void SurfaceBuildModeIncludesGpuDirectRender()
        {
            Assert.That(System.Enum.IsDefined(typeof(SlimeSurfaceBuildMode), "GpuDirectRender"), Is.True);
        }

        [Test]
        public void SurfaceBuilderDoesNotUseDirectGpuDrawPath()
        {
            string builderSource = File.ReadAllText("Assets/Slime/Slime_SurfaceBuilder.cs");

            Assert.That(builderSource, Does.Not.Contain(".DrawDirectSurface("));
        }

        [Test]
        public void SurfaceBuilderAcceptsOnlyGpuDirectSurfaceShaderName()
        {
            Assert.That(SlimeSurfaceBuilder.IsGpuDirectSurfaceShaderName("Slime/GPU Surface Draw"), Is.True);
            Assert.That(SlimeSurfaceBuilder.IsGpuDirectSurfaceShaderName("HDRP/Lit"), Is.False);
            Assert.That(SlimeSurfaceBuilder.IsGpuDirectSurfaceShaderName(string.Empty), Is.False);
        }

        [Test]
        public void GpuDirectSurfaceShaderUsesUrpWorldSpaceProjection()
        {
            string shaderSource = File.ReadAllText("Assets/Slime/SlimeGpuSurfaceDraw.shader");

            Assert.That(shaderSource, Does.Contain("\"RenderPipeline\" = \"UniversalPipeline\""));
            Assert.That(shaderSource, Does.Contain("\"LightMode\" = \"UniversalForward\""));
            Assert.That(shaderSource, Does.Contain("Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"));
            Assert.That(shaderSource, Does.Contain("TransformWorldToHClip(vertex.Position)"));
            Assert.That(shaderSource, Does.Not.Contain("HDRenderPipeline"));
            Assert.That(shaderSource, Does.Not.Contain("render-pipelines.high-definition"));
        }

        [Test]
        public void GpuSurfaceComputeUsesGradientNormalsForReadbackMesh()
        {
            string computeSource = File.ReadAllText("Assets/Slime/Slime_Surface.compute");

            Assert.That(computeSource, Does.Contain("ReadGridTrilinear("));
            Assert.That(computeSource, Does.Contain("CalcNormal("));
            Assert.That(computeSource, Does.Contain("MatchHemisphere("));
            Assert.That(computeSource, Does.Contain("TryAppendTriangle(v0, v1, v2, n0, n1, n2)"));
            Assert.That(computeSource, Does.Not.Contain("TryAppendTriangle(v0, v1, v2, normalize(cp))"));
        }

        [Test]
        public void SurfaceBuilderKeepsLastMeshWhileGpuReadbackIsPending()
        {
            string builderSource = File.ReadAllText("Assets/Slime/Slime_SurfaceBuilder.cs");

            Assert.That(builderSource, Does.Contain("_gpuPipeline.HasPendingReadback)"));
            Assert.That(builderSource, Does.Not.Contain("cpuFallbackWhileGpuReadbackStale"));
            Assert.That(builderSource, Does.Not.Contain("ShouldCpuFallbackForStaleGpuReadback"));
        }

        [Test]
        public void GpuSurfacePipelineCapsAsyncReadbackSize()
        {
            long cappedBytes = SlimeGpuSurfacePipeline.EstimateSurfaceVertexReadbackBytes(
                SlimeGpuSurfacePipeline.MaxAsyncReadbackVertexCount);

            Assert.That(cappedBytes, Is.LessThanOrEqualTo(8L * 1024L * 1024L));
            Assert.That(SlimeGpuSurfacePipeline.MaxAsyncReadbackVertexCount, Is.LessThan(768 * 125 * 15));
        }

        [Test]
        public void GpuSurfacePipelineSuppressesOverflowedReadback()
        {
            SlimeGpuSurfacePipeline.ReadbackStatsSnapshot overflowed =
                new SlimeGpuSurfacePipeline.ReadbackStatsSnapshot(12, 0, true, 128);
            SlimeGpuSurfacePipeline.ReadbackStatsSnapshot oversized =
                new SlimeGpuSurfacePipeline.ReadbackStatsSnapshot(
                    12,
                    0,
                    false,
                    SlimeGpuSurfacePipeline.MaxAsyncReadbackVertexCount + 1);

            Assert.That(SlimeGpuSurfacePipeline.ShouldSuppressVertexReadback(overflowed), Is.True);
            Assert.That(SlimeGpuSurfacePipeline.ShouldSuppressVertexReadback(oversized), Is.True);
        }

        [Test]
        public void GpuSurfacePipelineReportsUnsupportedScheduleAttempt()
        {
            using SlimeGpuSurfacePipeline pipeline = new SlimeGpuSurfacePipeline(null);

            bool scheduled = pipeline.TrySchedule(
                default,
                default,
                default,
                default,
                default,
                1f,
                requestReadback: true);

            Assert.That(scheduled, Is.False);
            Assert.That(pipeline.LastScheduleStatus, Is.EqualTo(SlimeGpuSurfaceScheduleStatus.Unsupported));
            Assert.That(pipeline.LastScheduleFailure, Does.Contain("compute"));
        }

        [Test]
        public void GpuSurfacePipelineSnapshotsReadbackStatsBeforeVertexReadback()
        {
            SlimeGpuSurfacePipeline.ReadbackStatsSnapshot snapshot =
                SlimeGpuSurfacePipeline.DecodeReadbackStats(new uint[] { 12u, 3u, 0u, 99u }, vertexCapacity: 80);

            Assert.That(snapshot.VisitedBlockCount, Is.EqualTo(12));
            Assert.That(snapshot.SkippedBlockCount, Is.EqualTo(3));
            Assert.That(snapshot.Overflow, Is.False);
            Assert.That(snapshot.VertexCount, Is.EqualTo(80));
        }

        [Test]
        public void GpuSurfacePipelineCalculatesPowerOfTwoHashCapacity()
        {
            Assert.That(SlimeGpuSurfacePipeline.CalculateKeyHashCapacity(0), Is.EqualTo(1));
            Assert.That(SlimeGpuSurfacePipeline.CalculateKeyHashCapacity(1), Is.EqualTo(2));
            Assert.That(SlimeGpuSurfacePipeline.CalculateKeyHashCapacity(3), Is.EqualTo(8));
            Assert.That(SlimeGpuSurfacePipeline.CalculateKeyHashCapacity(768), Is.EqualTo(2048));
        }

        [Test]
        public void GpuSurfacePipelineShrinksActiveHashCapacityAfterBufferGrowth()
        {
            Assert.That(
                SlimeGpuSurfacePipeline.ResolveActiveKeyHashCapacity(keyCount: 64, allocatedCapacity: 2048),
                Is.EqualTo(128));
            Assert.That(
                SlimeGpuSurfacePipeline.ResolveActiveKeyHashCapacity(keyCount: 768, allocatedCapacity: 2048),
                Is.EqualTo(2048));
        }

        [Test]
        public void GpuSurfacePipelineHashesNegativeBlockKeysDeterministically()
        {
            int3 key = new int3(-7, 13, -29);
            int first = SlimeGpuSurfacePipeline.HashBlockKey(key);
            int second = SlimeGpuSurfacePipeline.HashBlockKey(key);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.Not.EqualTo(SlimeGpuSurfacePipeline.HashBlockKey(new int3(-7, 13, -28))));
        }

        static int3 LocalCoord(int idx)
        {
            return new int3(idx & 3, (idx >> 2) & 3, (idx >> 4) & 3);
        }
    }
}
