using NUnit.Framework;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slime.Tests
{
    public class SlimeShapeTargetTests
    {
        [Test]
        public void SimulatorTranslatesParticlesWhenTransformMoves()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Simulator Transform Follow Test");

            try
            {
                simulatorObject.transform.position = new Vector3(0f, 1f, 0f);
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.enableShapeMatch = false;
                simulator.gravity = 0f;
                simulator.concentration = 0f;

                simulator.Initialise();
                float3 centerBefore = GetParticleCenter(simulator);

                simulatorObject.transform.position += new Vector3(1f, 0f, 0f);
                InvokeFixedUpdate(simulator);

                float3 centerAfter = GetParticleCenter(simulator);
                Assert.That(centerAfter.x - centerBefore.x, Is.EqualTo(SlimeConst.InvScale).Within(0.25f));
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void BuildSamplesRestParticlesInsideClosedMesh()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateCubeMesh(), 32, 18, Allocator.TempJob);

            Assert.That(target.IsCreated, Is.True);
            Assert.That(target.RestLocalPositions.Length, Is.EqualTo(32));
            Assert.That(SlimeShapeTarget.SampleSdf(target.Sdf, target.SdfInfo, float3.zero), Is.LessThan(0f));
            Assert.That(SlimeShapeTarget.SampleSdf(target.Sdf, target.SdfInfo, new float3(2f, 0f, 0f)), Is.GreaterThan(0f));

            for (int i = 0; i < target.RestLocalPositions.Length; i++)
            {
                float sdf = SlimeShapeTarget.SampleSdf(target.Sdf, target.SdfInfo, target.RestLocalPositions[i]);
                Assert.That(sdf, Is.LessThanOrEqualTo(target.SdfInfo.CellSize * 1.25f));
            }
        }

        [Test]
        public void TryBuildHandlesUnreadableRuntimeMeshWithoutThrowing()
        {
            Mesh mesh = CreateCubeMesh();
            mesh.UploadMeshData(true);

            try
            {
                bool ok = SlimeShapeTarget.TryBuild(mesh, 8, 12, Allocator.TempJob, out SlimeShapeTarget target, out string error);
                try
                {
                    if (ok)
                    {
                        Assert.That(target.IsCreated, Is.True);
                        Assert.That(target.RestLocalPositions.Length, Is.EqualTo(8));
                    }
                    else
                    {
                        Assert.That(target.IsCreated, Is.False);
                        Assert.That(error, Does.Contain("Read/Write"));
                    }
                }
                finally
                {
                    target.Dispose();
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PbfJobsKeepOverlappingParticlesFinite()
        {
            NativeHashMap<int, int2> lut = new NativeHashMap<int, int2>(1, Allocator.TempJob);
            NativeArray<int2> hashes = new NativeArray<int2>(2, Allocator.TempJob);
            NativeArray<float3> positions = new NativeArray<float3>(2, Allocator.TempJob);
            NativeArray<float> lambda = new NativeArray<float>(2, Allocator.TempJob);
            NativeArray<SlimeParticle> sourceParticles = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            NativeArray<int> ids = new NativeArray<int>(2, Allocator.TempJob);

            try
            {
                positions[0] = float3.zero;
                positions[1] = float3.zero;
                int key = SlimeConst.GetKey(SlimeConst.GetCoord(float3.zero));
                hashes[0] = new int2(key, 0);
                hashes[1] = new int2(key, 1);
                sourceParticles[0] = new SlimeParticle { Position = positions[0], ID = 3 };
                sourceParticles[1] = new SlimeParticle { Position = positions[1], ID = 4 };
                ids[0] = 3;
                ids[1] = 4;
                lut.Add(key, new int2(0, 2));

                new SlimeSimulation.ComputeLambdaJob
                {
                    Lut = lut,
                    PosPredict = positions,
                    Ids = ids,
                    Lambda = lambda,
                }.Schedule(positions.Length, 1).Complete();

                Assert.That(IsFinite(lambda[0]), Is.True);
                Assert.That(IsFinite(lambda[1]), Is.True);

                new SlimeSimulation.ComputeDeltaPosJob
                {
                    Lut = lut,

                    PosPredict = positions,
                    Lambda = lambda,
                    Ids = ids,
                    PsNew = particles,
                }.Schedule(positions.Length, 1).Complete();

                Assert.That(IsFinite(particles[0].Position), Is.True);
                Assert.That(IsFinite(particles[1].Position), Is.True);
                Assert.That(particles[0].ID, Is.EqualTo(3));
                Assert.That(particles[1].ID, Is.EqualTo(4));
            }
            finally
            {
                lut.Dispose();
                hashes.Dispose();
                positions.Dispose();
                lambda.Dispose();
                sourceParticles.Dispose();
                particles.Dispose();
                ids.Dispose();
            }
        }

        [Test]
        public void ApplyForceJobSanitizesNonFiniteInputState()
        {
            NativeList<SlimeController> controllers = new NativeList<SlimeController>(1, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<SlimeParticle> predicted = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> velocity = new NativeArray<float3>(1, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(float.NaN, 0f, 0f), ID = 0 };
                velocity[0] = new float3(0f, float.PositiveInfinity, 0f);

                new SlimeSimulation.ApplyForceJob
                {
                    Controllers = controllers,
                    Ps = particles,
                    PsNew = predicted,
                    Velocity = velocity,
                    Gravity = new float3(0f, -1f, 0f),
                    VelocityFollowGain = 0f,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(IsFinite(predicted[0].Position), Is.True);
                Assert.That(IsFinite(velocity[0]), Is.True);
            }
            finally
            {
                controllers.Dispose();
                particles.Dispose();
                predicted.Dispose();
                velocity.Dispose();
            }
        }

        [Test]
        public void OpenMeshFallbackDoesNotCollapseRestParticlesToOnePoint()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateSingleTriangleMesh(), 16, 12, Allocator.TempJob);

            float maxDistance = 0f;
            for (int i = 1; i < target.RestLocalPositions.Length; i++)
            {
                Assert.That(IsFinite(target.RestLocalPositions[i]), Is.True);
                maxDistance = math.max(maxDistance, math.distance(target.RestLocalPositions[0], target.RestLocalPositions[i]));
            }

            Assert.That(maxDistance, Is.GreaterThan(0.001f));
        }

        [Test]
        public void OpenMeshFallbackSamplesNearMeshSurface()
        {
            Mesh mesh = CreateSeparatedOpenTrianglesMesh();
            using SlimeShapeTarget target = SlimeShapeTarget.Build(mesh, 64, 12, Allocator.TempJob);

            float sumDistance = 0f;
            for (int i = 0; i < target.RestLocalPositions.Length; i++)
            {
                float3 p = target.RestLocalPositions[i];
                Assert.That(IsFinite(p), Is.True);
                sumDistance += DistanceToSeparatedOpenTriangles(p);
            }

            float averageDistance = sumDistance / target.RestLocalPositions.Length;
            Assert.That(averageDistance, Is.LessThan(0.08f));
        }

        [Test]
        public void OpenNonWatertightMeshUsesSurfaceFallbackEvenWhenParityFindsInsideSamples()
        {
            Mesh mesh = CreateOpenSquareMesh();
            using SlimeShapeTarget target = SlimeShapeTarget.Build(mesh, 64, 12, Allocator.TempJob);

            Assert.That(target.IsClosedVolume, Is.False);
            Assert.That(target.UsesSurfaceFallback, Is.True);

            float maxDistanceToPlane = 0f;
            for (int i = 0; i < target.RestLocalPositions.Length; i++)
            {
                float3 p = target.RestLocalPositions[i];
                Assert.That(IsFinite(p), Is.True);
                maxDistanceToPlane = math.max(maxDistanceToPlane, math.abs(p.y));
            }

            Assert.That(maxDistanceToPlane, Is.LessThanOrEqualTo(target.SdfInfo.CellSize * 0.75f));
        }

        [Test]
        public void ShapeTargetJobIgnoresNonFiniteTargetInsteadOfPoisoningParticle()
        {
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);
            NativeArray<float> sdf = new NativeArray<float>(0, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(1f, 2f, 3f), ID = 0 };
                restLocal[0] = new float3(float.NaN, 0f, 0f);
                centres[0] = float3.zero;
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = sdf,
                    SdfInfo = default,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = float3.zero,
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 0f,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(IsFinite(particles[0].Position), Is.True);
                Assert.That(math.distance(particles[0].Position, new float3(1f, 2f, 3f)), Is.LessThan(0.001f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
                sdf.Dispose();
            }
        }

        [Test]
        public void ShapeTargetJobCanDisableSdfBoundaryForOpenSurfaceTargets()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateOpenSquareMesh(), 1, 12, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                float3 position = new float3(0f, target.SdfInfo.CellSize * 0.5f, 0f);
                particles[0] = new SlimeParticle { Position = position, ID = 0 };
                restLocal[0] = position;
                centres[0] = float3.zero;
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = target.Sdf,
                    SdfInfo = target.SdfInfo,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = float3.zero,
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 1f,
                    DisableBoundaryProjection = true,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(math.distance(particles[0].Position, position), Is.LessThan(0.001f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void SimulatorResolvesShapeTargetMeshFromSourceObjectMeshFilter()
        {
            Mesh mesh = CreateCubeMesh();
            GameObject simulatorObject = new GameObject("Slime Simulator Test");
            GameObject sourceObject = new GameObject("Shape Target Source");

            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                MeshFilter meshFilter = sourceObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;
                simulator.shapeTargetObject = sourceObject;

                bool resolved = simulator.TryResolveMeshShapeTarget(out Mesh resolvedMesh, out Transform resolvedTransform, out string error);

                Assert.That(resolved, Is.True, error);
                Assert.That(resolvedMesh, Is.SameAs(mesh));
                Assert.That(resolvedTransform, Is.SameAs(meshFilter.transform));
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SimulatorResolvesShapeTargetMeshFromSourceObjectSkinnedRenderer()
        {
            Mesh mesh = CreateCubeMesh();
            GameObject simulatorObject = new GameObject("Slime Simulator Test");
            GameObject sourceObject = new GameObject("Shape Target Source");

            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                SkinnedMeshRenderer renderer = sourceObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                simulator.shapeTargetObject = sourceObject;

                bool resolved = simulator.TryResolveMeshShapeTarget(out Mesh resolvedMesh, out Transform resolvedTransform, out string error);

                Assert.That(resolved, Is.True, error);
                Assert.That(resolvedMesh, Is.SameAs(mesh));
                Assert.That(resolvedTransform, Is.SameAs(renderer.transform));
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
                Object.DestroyImmediate(sourceObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ShapeTargetJobProjectsParticlesTowardMeshRestShape()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateCubeMesh(), 1, 18, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(4f, 0f, 0f), ID = 0 };
                restLocal[0] = float3.zero;
                centres[0] = float3.zero;
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = target.Sdf,
                    SdfInfo = target.SdfInfo,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = float3.zero,
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 1f,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(math.distance(particles[0].Position, float3.zero), Is.LessThan(4f));
                Assert.That(SlimeShapeTarget.SampleSdf(target.Sdf, target.SdfInfo, particles[0].Position), Is.LessThanOrEqualTo(0f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void ShapeTargetJobFollowsCurrentSlimeCenter()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateCubeMesh(), 1, 18, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(10f, 0f, 0f), ID = 0 };
                restLocal[0] = float3.zero;
                centres[0] = new float3(5f, 0f, 0f);
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = target.Sdf,
                    SdfInfo = target.SdfInfo,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = float3.zero,
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 0f,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(math.distance(particles[0].Position, centres[0]), Is.LessThan(0.001f));
                Assert.That(math.distance(particles[0].Position, float3.zero), Is.GreaterThan(4.9f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void ShapeTargetJobUsesShapeLocalCenterAsPivot()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateCubeMesh(), 1, 18, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(10f, 0f, 0f), ID = 0 };
                restLocal[0] = new float3(2f, 0f, 0f);
                centres[0] = new float3(5f, 0f, 0f);
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = target.Sdf,
                    SdfInfo = target.SdfInfo,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = new float3(2f, 0f, 0f),
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 0f,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(math.distance(particles[0].Position, centres[0]), Is.LessThan(0.001f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        [Test]
        public void ShapeTargetBoundaryUsesCurrentSlimeCenter()
        {
            using SlimeShapeTarget target = SlimeShapeTarget.Build(CreateCubeMesh(), 1, 18, Allocator.TempJob);
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> restLocal = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> centres = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<int> counts = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(9f, 0f, 0f), ID = 0 };
                restLocal[0] = new float3(4f, 0f, 0f);
                centres[0] = new float3(5f, 0f, 0f);
                counts[0] = 1;

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = particles,
                    RestLocalPositions = restLocal,
                    Centres = centres,
                    Counts = counts,
                    Sdf = target.Sdf,
                    SdfInfo = target.SdfInfo,
                    LocalToSimVector = float4x4.identity,
                    SimToLocalVector = float4x4.identity,
                    ShapeLocalCenter = float3.zero,
                    Strength = 1f,
                    Compliance = 0f,
                    BoundaryStrength = 1f,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(particles.Length, 1).Complete();

                float3 local = particles[0].Position - centres[0];
                Assert.That(SlimeShapeTarget.SampleSdf(target.Sdf, target.SdfInfo, local), Is.LessThanOrEqualTo(0f));
                Assert.That(math.distance(particles[0].Position, float3.zero), Is.GreaterThan(4f));
            }
            finally
            {
                particles.Dispose();
                restLocal.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        static Mesh CreateCubeMesh()
        {
            var mesh = new Mesh { name = "Slime Shape Target Test Cube" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f, -0.5f, -0.5f),
                new Vector3( 0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f,  0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f, -0.5f,  0.5f),
                new Vector3( 0.5f,  0.5f,  0.5f),
                new Vector3(-0.5f,  0.5f,  0.5f),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6,
                3, 0, 4, 3, 4, 7,
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateSingleTriangleMesh()
        {
            var mesh = new Mesh { name = "Slime Shape Target Test Open Triangle" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3( 0.5f, 0f, -0.5f),
                new Vector3( 0f,  0f,  0.5f),
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateSeparatedOpenTrianglesMesh()
        {
            var mesh = new Mesh { name = "Slime Shape Target Test Open Surface" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, -1f),
                new Vector3( 1f, 0f, -1f),
                new Vector3( 0f, 0f,  1f),
                new Vector3(-1f, 2f, -1f),
                new Vector3( 1f, 2f, -1f),
                new Vector3( 0f, 2f,  1f),
            };
            mesh.triangles = new[] { 0, 1, 2, 3, 5, 4 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateOpenSquareMesh()
        {
            var mesh = new Mesh { name = "Slime Shape Target Test Open Square" };
            mesh.vertices = new[]
            {
                new Vector3(-1f, 0f, -1f),
                new Vector3( 1f, 0f, -1f),
                new Vector3( 1f, 0f,  1f),
                new Vector3(-1f, 0f,  1f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        static float DistanceToSeparatedOpenTriangles(float3 p)
        {
            float3 a0 = new float3(-1f, 0f, -1f);
            float3 b0 = new float3(1f, 0f, -1f);
            float3 c0 = new float3(0f, 0f, 1f);
            float3 a1 = new float3(-1f, 2f, -1f);
            float3 b1 = new float3(1f, 2f, -1f);
            float3 c1 = new float3(0f, 2f, 1f);
            return math.min(
                math.sqrt(DistanceSqToTriangle(p, a0, b0, c0)),
                math.sqrt(DistanceSqToTriangle(p, a1, c1, b1)));
        }

        static float DistanceSqToTriangle(float3 p, float3 a, float3 b, float3 c)
        {
            float3 ab = b - a;
            float3 ac = c - a;
            float3 ap = p - a;
            float d1 = math.dot(ab, ap);
            float d2 = math.dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return math.lengthsq(ap);

            float3 bp = p - b;
            float d3 = math.dot(ab, bp);
            float d4 = math.dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return math.lengthsq(bp);

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                float3 projection = a + v * ab;
                return math.lengthsq(p - projection);
            }

            float3 cp = p - c;
            float d5 = math.dot(ab, cp);
            float d6 = math.dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return math.lengthsq(cp);

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                float3 projection = a + w * ac;
                return math.lengthsq(p - projection);
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                float3 projection = b + w * (c - b);
                return math.lengthsq(p - projection);
            }

            float3 normal = math.normalize(math.cross(ab, ac));
            float distance = math.dot(p - a, normal);
            return distance * distance;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(float3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static void InvokeFixedUpdate(SlimeSimulator simulator)
        {
            MethodInfo method = typeof(SlimeSimulator).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(simulator, null);
        }

        static float3 GetParticleCenter(SlimeSimulator simulator)
        {
            FieldInfo field = typeof(SlimeSimulator).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            NativeArray<SlimeParticle> particles = (NativeArray<SlimeParticle>)field.GetValue(simulator);
            Assert.That(particles.IsCreated, Is.True);

            float3 center = float3.zero;
            for (int i = 0; i < particles.Length; i++)
                center += particles[i].Position;

            return center / particles.Length;
        }
    }

    public class SlimeStaticColliderCollectionTests
    {
        public static void VerifyStaticColliderCollection()
        {
            var tests = new SlimeStaticColliderCollectionTests();
            tests.RebuildCollidersIncludesStaticSceneMeshCollidersOutsideSimulatorHierarchy();
            tests.RebuildCollidersPreservesStaticBoxColliderOrientation();
            tests.RebuildCollidersIgnoresDynamicRigidbodies();
        }

        [Test]
        public void RebuildCollidersIncludesStaticSceneMeshCollidersOutsideSimulatorHierarchy()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Simulator");
            SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();

            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.transform.position = new Vector3(3f, 4f, 5f);
            obstacle.transform.localScale = new Vector3(2f, 3f, 4f);
            Mesh mesh = obstacle.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(obstacle.GetComponent<BoxCollider>());
            MeshCollider meshCollider = obstacle.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = mesh;

            try
            {
                simulator.RebuildColliders();
                NativeArray<SlimeBoxCollider> colliders = GetColliderBuffer(simulator);

                Assert.That(colliders.IsCreated, Is.True);
                Assert.That(colliders.Length, Is.EqualTo(1));

                Bounds localBounds = mesh.bounds;
                float3 expectedCenter = (float3)meshCollider.transform.TransformPoint(localBounds.center) * SlimeConst.InvScale;
                float3 expectedExtent = (float3)localBounds.extents * math.abs((float3)meshCollider.transform.lossyScale) * SlimeConst.InvScale + new float3(1f, 1f, 1f);

                Assert.That(math.distance(colliders[0].Center, expectedCenter), Is.LessThan(0.0001f));
                Assert.That(math.distance(colliders[0].Extent, expectedExtent), Is.LessThan(0.0001f));
                Assert.That(math.distance(colliders[0].AxisX, (float3)meshCollider.transform.right), Is.LessThan(0.0001f));
                Assert.That(math.distance(colliders[0].AxisY, (float3)meshCollider.transform.up), Is.LessThan(0.0001f));
                Assert.That(math.distance(colliders[0].AxisZ, (float3)meshCollider.transform.forward), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(obstacle);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void RebuildCollidersPreservesStaticBoxColliderOrientation()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Simulator");
            SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();

            GameObject slope = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slope.transform.position = new Vector3(1f, 2f, 3f);
            slope.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
            slope.transform.localScale = new Vector3(4f, 0.5f, 2f);

            try
            {
                simulator.RebuildColliders();
                NativeArray<SlimeBoxCollider> colliders = GetColliderBuffer(simulator);

                Assert.That(colliders.IsCreated, Is.True);
                Assert.That(colliders.Length, Is.EqualTo(1));

                SlimeBoxCollider collider = colliders[0];
                Assert.That(math.distance(collider.AxisX, (float3)slope.transform.right), Is.LessThan(0.0001f));
                Assert.That(math.distance(collider.AxisY, (float3)slope.transform.up), Is.LessThan(0.0001f));
                Assert.That(math.distance(collider.AxisZ, (float3)slope.transform.forward), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(slope);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void RebuildCollidersIgnoresDynamicRigidbodies()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Simulator");
            SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();

            GameObject staticObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            staticObstacle.transform.position = new Vector3(1f, 0f, 0f);

            GameObject dynamicObstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dynamicObstacle.transform.position = new Vector3(2f, 0f, 0f);
            dynamicObstacle.AddComponent<Rigidbody>();

            try
            {
                simulator.RebuildColliders();
                NativeArray<SlimeBoxCollider> colliders = GetColliderBuffer(simulator);

                Assert.That(colliders.IsCreated, Is.True);
                Assert.That(colliders.Length, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(dynamicObstacle);
                Object.DestroyImmediate(staticObstacle);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void UpdateJobPushesParticlesOutAlongObbSlopeNormal()
        {
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> oldPositions = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> velocities = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<SlimeBoxCollider> colliders = new NativeArray<SlimeBoxCollider>(1, Allocator.TempJob);

            try
            {
                float angle = math.radians(45f);
                float s = math.sin(angle);
                float c = math.cos(angle);
                float3 axisX = math.normalize(new float3(c, s, 0f));
                float3 axisY = math.normalize(new float3(-s, c, 0f));
                float3 axisZ = new float3(0f, 0f, 1f);
                float3 center = new float3(0f, 2f, 0f);
                float3 extent = new float3(2f, 0.25f, 2f);

                float3 start = center + axisY * 0.05f;
                float3 expected = center + axisY * extent.y;

                particles[0] = new SlimeParticle { Position = start, ID = 0 };
                oldPositions[0] = start;
                colliders[0] = MakeCollider(center, extent, axisX, axisY, axisZ);

                new SlimeSimulation.UpdateJob
                {
                    Ps = particles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    Velocity = velocities,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(math.distance(particles[0].Position, expected), Is.LessThan(0.001f));
            }
            finally
            {
                particles.Dispose();
                oldPositions.Dispose();
                velocities.Dispose();
                colliders.Dispose();
            }
        }

        [Test]
        public void UpdateJobAppliesFloorOnlyWhenEnabled()
        {
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> oldPositions = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> velocities = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<SlimeBoxCollider> colliders = new NativeArray<SlimeBoxCollider>(0, Allocator.TempJob);

            try
            {
                float3 belowFloor = new float3(0f, 0.25f, 0f);
                particles[0] = new SlimeParticle { Position = belowFloor, ID = 0 };
                oldPositions[0] = belowFloor;

                new SlimeSimulation.UpdateJob
                {
                    Ps = particles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    Velocity = velocities,
                    EnableFloor = false,
                    FloorY = 1f,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(particles[0].Position.y, Is.EqualTo(belowFloor.y).Within(0.0001f));

                particles[0] = new SlimeParticle { Position = belowFloor, ID = 0 };

                new SlimeSimulation.UpdateJob
                {
                    Ps = particles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    Velocity = velocities,
                    EnableFloor = true,
                    FloorY = 1f,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(particles[0].Position.y, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                particles.Dispose();
                oldPositions.Dispose();
                velocities.Dispose();
                colliders.Dispose();
            }
        }

        static NativeArray<SlimeBoxCollider> GetColliderBuffer(SlimeSimulator simulator)
        {
            FieldInfo field = typeof(SlimeSimulator).GetField("_colliderBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (NativeArray<SlimeBoxCollider>)field.GetValue(simulator);
        }

        static SlimeBoxCollider MakeCollider(float3 center, float3 extent, float3 axisX, float3 axisY, float3 axisZ)
        {
            SlimeBoxCollider collider = new SlimeBoxCollider
            {
                Center = center,
                Extent = extent,
                AxisX = axisX,
                AxisY = axisY,
                AxisZ = axisZ,
            };
            return collider;
        }
    }

    public class SlimeControllerMotorTests
    {
        [Test]
        public void MovementInputUsesMotorObjectSpace()
        {
            GameObject motorObject = new GameObject("Slime Controller Motor Direction Test");

            try
            {
                motorObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

                Vector3 velocity = SlimeControllerMotor.CalculatePlanarMoveVelocity(
                    motorObject.transform,
                    Vector2.up,
                    3f);

                Assert.That(velocity.x, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(velocity.y, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(velocity.z, Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(motorObject);
            }
        }

        [Test]
        public void CameraForwardDefinesMotorFacing()
        {
            Quaternion rotation = SlimeControllerMotor.CalculateCameraFacingRotation(
                Quaternion.identity,
                new Vector3(1f, -0.5f, 0f));
            Vector3 forward = rotation * Vector3.forward;

            Assert.That(forward.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(forward.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(forward.z, Is.EqualTo(0f).Within(0.0001f));
        }
    }

    public class CameraFollowSlimeTests
    {
        [Test]
        public void LateUpdateWithoutSimulatorDoesNotThrow()
        {
            GameObject cameraObject = new GameObject("Slime Camera Follow Test");

            try
            {
                CameraFollow_Slime follow = cameraObject.AddComponent<CameraFollow_Slime>();
                MethodInfo method = typeof(CameraFollow_Slime).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                Assert.DoesNotThrow(() => method.Invoke(follow, null));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void CollisionAdjustmentMovesCameraInFrontOfObstacle()
        {
            MethodInfo method = typeof(CameraFollow_Slime).GetMethod(
                "CalculateCollisionAdjustedPosition",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] args =
            {
                Vector3.zero,
                new Vector3(0f, 0f, -5f),
                2f,
                0.2f,
                0.15f,
                false
            };

            Vector3 adjusted = (Vector3)method.Invoke(null, args);

            Assert.That((bool)args[5], Is.True);
            Assert.That(adjusted.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(adjusted.y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(adjusted.z, Is.EqualTo(-1.8f).Within(0.0001f));
        }
    }
}
