using NUnit.Framework;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Slime.Tests
{
    /// <summary>
    /// Unit tests for the explicit split / absorb / control-switch system
    /// (<see cref="SlimeGroupManager"/>) and the group-aware force application.
    /// </summary>
    public class SlimeSplitTests
    {
        // ── SlimeGroupManager.Split ──

        [Test]
        public void SplitConservesParticleCountAndKeepsControlOnMainBody()
        {
            const int n = 256;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
            {
                // Spread along +X so the front-most quarter splits off.
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };
                vel[i] = new float3(0f, 0f, 0f);
            }

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(concentration: 10f, stiffness: 0.15f);
                Assert.That(groups.AliveCount, Is.EqualTo(1));
                Assert.That(groups.ControlledGroupId, Is.EqualTo(0));

                int newGroup = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.25f, separation: 5f, minParticlesToSplit: 16);

                Assert.That(newGroup, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.AliveCount, Is.EqualTo(2));

                int group0 = 0, groupNew = 0;
                for (int i = 0; i < n; i++)
                {
                    if (particles[i].ID == 0) group0++;
                    else if (particles[i].ID == newGroup) groupNew++;
                }
                Assert.That(group0 + groupNew, Is.EqualTo(n), "no particle lost or duplicated");
                Assert.That(groupNew, Is.EqualTo(64), "quarter peeled off");
                Assert.That(group0, Is.EqualTo(192));
                // Control stays on the main body so the split body self-coheres
                // around its own COM (no motor snap-back).
                Assert.That(groups.ControlledGroupId, Is.EqualTo(0), "main body keeps control");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        [Test]
        public void SplitRejectsWhenBodyTooSmall()
        {
            const int n = 20;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int result = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.25f, separation: 5f, minParticlesToSplit: 128);

                Assert.That(result, Is.EqualTo(-1));
                Assert.That(groups.AliveCount, Is.EqualTo(1));
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        [Test]
        public void WriteCompactRestOffsetsBuildsCompleteLocalBlob()
        {
            const int n = 64;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var rest = new NativeArray<float3>(n, Allocator.TempJob);

            try
            {
                for (int i = 0; i < n; i++)
                {
                    particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 1 };
                    rest[i] = new float3(i, 0f, 0f);
                }

                SlimeGroupManager.WriteCompactRestOffsets(rest, particles, groupId: 1, totalParticleCount: n);

                float3 sum = new float3(0f, 0f, 0f);
                float3 minRest = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
                float3 maxRest = new float3(float.MinValue, float.MinValue, float.MinValue);
                for (int i = 0; i < n; i++)
                {
                    sum += rest[i];
                    minRest = min(minRest, rest[i]);
                    maxRest = max(maxRest, rest[i]);
                }

                float3 extent = maxRest - minRest;
                Assert.That(length(sum), Is.LessThan(0.001f), "compact rest shape is centred on its own COM");
                Assert.That(extent.y, Is.GreaterThan(0.5f), "new local blob has real height, not a sliced line");
                Assert.That(extent.z, Is.GreaterThan(0.5f), "new local blob has real depth, not a sliced line");
            }
            finally
            {
                particles.Dispose();
                rest.Dispose();
            }
        }

        [Test]
        public void SplitBodyInheritsInitialConcentrationNotParentState()
        {
            // Even if the parent is in a diffuse (low-concentration) state, the
            // split body must inherit the INITIAL concentration so it can cohere.
            const int n = 64;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(concentration: 10f, stiffness: 0.15f);
                // Simulate the player lowering cohesion (diffuse) before splitting.
                groups.SetControlledConcentration(0.1f);
                groups.SetControlledStiffness(0f);

                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(split, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.GetConcentration(split), Is.EqualTo(10f),
                    "split body uses initial concentration, not the parent's diffuse value");
                Assert.That(groups.GetStiffness(split), Is.EqualTo(0.15f));
                Assert.That(groups.GetConcentration(0), Is.EqualTo(0.1f),
                    "main body keeps its (player-adjusted) state");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        // ── Absorb ──

        [Test]
        public void AbsorbMergesContactingGroupIntoControlled()
        {
            const int n = 64;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            var rest = new NativeArray<float3>(n, Allocator.TempJob);
            // Group 0 near origin; the split (front half) is touching it.
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(split, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.ControlledGroupId, Is.EqualTo(0), "main body is controlled");

                int target = groups.FindAbsorbTarget(particles, contactSlack: 2f);
                Assert.That(target, Is.EqualTo(-1),
                    "freshly split bodies must separate before they are allowed to absorb");

                // Move the split body far enough away to arm re-absorb, then bring
                // it back into contact. Only this second contact should merge.
                for (int i = 0; i < n; i++)
                    if (particles[i].ID == split)
                    {
                        SlimeParticle p = particles[i];
                        p.Position += new float3(1000f, 0f, 0f);
                        particles[i] = p;
                    }
                Assert.That(groups.FindAbsorbTarget(particles, contactSlack: 2f), Is.EqualTo(-1));

                for (int i = 0; i < n; i++)
                    if (particles[i].ID == split)
                    {
                        SlimeParticle p = particles[i];
                        p.Position -= new float3(1000f, 0f, 0f);
                        particles[i] = p;
                    }

                target = groups.FindAbsorbTarget(particles, contactSlack: 2f);
                Assert.That(target, Is.EqualTo(split), "the split body is the absorb target");

                groups.Absorb(particles, rest, vel, target);
                Assert.That(groups.AliveCount, Is.EqualTo(1));
                for (int i = 0; i < n; i++)
                    Assert.That(particles[i].ID, Is.EqualTo(0), "all merged into the controlled group");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
                rest.Dispose();
            }
        }

        [Test]
        public void FreshSplitIsNotAbsorbTargetUntilItHasActuallySeparated()
        {
            const int n = 256;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);

            try
            {
                for (int i = 0; i < n; i++)
                    particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.25f, separation: 5f, minParticlesToSplit: 16);

                Assert.That(split, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.FindAbsorbTarget(particles, contactSlack: 0.5f), Is.EqualTo(-1),
                    "default split separation can still overlap broad bounds, so absorb must stay gated");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        [Test]
        public void SimulatorSplitRebuildsGroupsThenAbsorbRestoresInitialRestOffsets()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Split Rest Offset Regression");

            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.enableShapeMatch = true;
                simulator.gravity = 0f;
                simulator.splitFraction = 0.5f;
                simulator.splitSeparation = 0f;
                simulator.minParticlesToSplit = 16;

                simulator.Initialise();

                NativeArray<float3> rest = GetPrivateField<NativeArray<float3>>(simulator, "_restOffsets");
                NativeArray<float3> initialRestBuffer =
                    GetPrivateField<NativeArray<float3>>(simulator, "_initialRestOffsets");
                var initialRest = new float3[rest.Length];
                initialRestBuffer.CopyTo(initialRest);

                Assert.That(simulator.Split(Vector3.right), Is.True);

                SlimeGroupManager groups = GetPrivateField<SlimeGroupManager>(simulator, "_groups");
                NativeArray<SlimeParticle> particles =
                    GetPrivateField<NativeArray<SlimeParticle>>(simulator, "_particles");
                NativeArray<float3> velocity =
                    GetPrivateField<NativeArray<float3>>(simulator, "_velocityBuffer");

                int absorbedGroup = -1;
                bool restChangedAfterSplit = false;
                for (int i = 0; i < particles.Length; i++)
                {
                    if (distance(rest[i], initialRest[i]) > 0.001f)
                        restChangedAfterSplit = true;

                    if (particles[i].ID == 0)
                        continue;

                    absorbedGroup = particles[i].ID;
                    break;
                }

                Assert.That(restChangedAfterSplit, Is.True,
                    "split bodies should receive complete compact local rest shapes instead of original-shape slices");
                Assert.That(absorbedGroup, Is.GreaterThan(0));
                groups.Absorb(particles, rest, initialRestBuffer, velocity, absorbedGroup);

                for (int i = 0; i < rest.Length; i++)
                    Assert.That(distance(rest[i], initialRest[i]), Is.LessThan(0.0001f),
                        "absorb should restore the canonical full-body rest baseline");
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void AbsorbKeepsCompactRestShapeWhenOtherGroupsRemain()
        {
            const int n = 128;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var rest = new NativeArray<float3>(n, Allocator.TempJob);
            var canonicalRest = new NativeArray<float3>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);

            try
            {
                for (int i = 0; i < n; i++)
                {
                    particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };
                    canonicalRest[i] = new float3(i - (n - 1) * 0.5f, 0f, 0f);
                    rest[i] = canonicalRest[i];
                }

                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int g1 = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);
                int g2 = groups.Split(particles, vel, new float3(-1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(g1, Is.GreaterThanOrEqualTo(0));
                Assert.That(g2, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.AliveCount, Is.EqualTo(3));

                SlimeGroupManager.WriteCompactRestOffsets(rest, particles, 0, n);
                SlimeGroupManager.WriteCompactRestOffsets(rest, particles, g1, n);
                SlimeGroupManager.WriteCompactRestOffsets(rest, particles, g2, n);

                groups.Absorb(particles, rest, canonicalRest, vel, g1);

                Assert.That(groups.AliveCount, Is.EqualTo(2));
                AssertCompactGroupRest(rest, particles, groupId: 0,
                    "partial absorb should keep a complete compact local blob");
                Assert.That(AnyRestDiffersFromCanonical(rest, canonicalRest, particles, groupId: 0), Is.True,
                    "partial absorb must not restore a sliced canonical full-body subset");
            }
            finally
            {
                particles.Dispose();
                rest.Dispose();
                canonicalRest.Dispose();
                vel.Dispose();
            }
        }

        [Test]
        public void DistantSplitBodyIsNotAnAbsorbTarget()
        {
            const int n = 64;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);

                // Move the split body far away from the controlled main body.
                for (int i = 0; i < n; i++)
                    if (particles[i].ID == split)
                    {
                        SlimeParticle p = particles[i];
                        p.Position += new float3(1000f, 0f, 0f);
                        particles[i] = p;
                    }

                int target = groups.FindAbsorbTarget(particles, contactSlack: 2f);
                Assert.That(target, Is.EqualTo(-1), "no contact → no absorb");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        // ── Control switch ──

        [Test]
        public void SwitchControlCyclesThroughAliveGroups()
        {
            const int n = 128;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f);
                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.25f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(split, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.ControlledGroupId, Is.EqualTo(0), "main keeps control after split");

                int switched = groups.SwitchControl();
                Assert.That(switched, Is.EqualTo(split), "first switch controls the split body");

                int switched2 = groups.SwitchControl();
                Assert.That(switched2, Is.EqualTo(0), "second switch returns to the main body");
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        // ── ApplyForceJob: velocity-follow only affects the controlled group ──

        [Test]
        public void VelocityFollowDoesNotDriveNonControlledGroup()
        {
            // Two particles: group 0 (controlled, motor moving +X) and group 1
            // (split-off, at rest). Only group 0 may pick up the controller velocity.
            var ps = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            var psNew = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            var vel = new NativeArray<float3>(2, Allocator.TempJob);
            var centres = new NativeArray<float3>(2, Allocator.TempJob);
            var counts = new NativeArray<int>(2, Allocator.TempJob);
            var controllers = new NativeList<SlimeController>(2, Allocator.TempJob);

            try
            {
                ps[0] = new SlimeParticle { Position = new float3(0f, 1f, 0f), ID = 0 };
                ps[1] = new SlimeParticle { Position = new float3(0f, 1f, 0f), ID = 1 };
                vel[0] = new float3(0f, 0f, 0f);
                vel[1] = new float3(0f, 0f, 0f);
                centres[0] = new float3(0f, 1f, 0f);
                centres[1] = new float3(0f, 1f, 0f);
                counts[0] = 1;
                counts[1] = 1;

                // Controlled group 0 has a +X velocity; group 1 is stationary.
                controllers.Add(new SlimeController
                {
                    Center = new float3(0f, 1f, 0f),
                    Radius = 10f,
                    Velocity = new float3(5f, 0f, 0f),
                    Concentration = 0f,
                });
                controllers.Add(new SlimeController
                {
                    Center = new float3(0f, 1f, 0f),
                    Radius = 10f,
                    Velocity = new float3(0f, 0f, 0f),
                    Concentration = 0f,
                });

                new SlimeSimulation.ApplyForceJob
                {
                    Controllers = controllers,
                    Ps = ps,
                    PsNew = psNew,
                    Velocity = vel,
                    Gravity = new float3(0f, 0, 0f),
                    VelocityFollowGain = 1f,
                    ControlledGroupId = 0,
                    GroupCentres = centres,
                    GroupCounts = counts,
                    MoveCoreRadius = 0f, // uniform: every particle is "core"
                }.Schedule(2, 1).Complete();

                // Group 0 absorbed the +X controller velocity.
                Assert.That(vel[0].x, Is.GreaterThan(1f), "controlled group follows motor");
                // Group 1 must have no forward (X) velocity component from the motor.
                Assert.That(vel[1].x, Is.LessThan(1e-3f),
                    "split-off group is not driven by the motor");
            }
            finally
            {
                ps.Dispose();
                psNew.Dispose();
                vel.Dispose();
                centres.Dispose();
                counts.Dispose();
                controllers.Dispose();
            }
        }

        // ── ComputeCenterJob: per-group COM ──

        [Test]
        public void ComputeCenterJobResolvesPerGroupCom()
        {
            var particles = new NativeArray<SlimeParticle>(4, Allocator.TempJob);
            var centres = new NativeArray<float3>(SlimeGroupManager.MaxGroups, Allocator.TempJob);
            var counts = new NativeArray<int>(SlimeGroupManager.MaxGroups, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(0f, 0f, 0f), ID = 0 };
                particles[1] = new SlimeParticle { Position = new float3(2f, 0f, 0f), ID = 0 };
                particles[2] = new SlimeParticle { Position = new float3(10f, 0f, 0f), ID = 1 };
                particles[3] = new SlimeParticle { Position = new float3(12f, 0f, 0f), ID = 1 };

                new SlimeCohesion.ComputeCenterJob
                {
                    Particles = particles,
                    Centres = centres,
                    Counts = counts,
                }.Schedule().Complete();

                Assert.That(counts[0], Is.EqualTo(2));
                Assert.That(counts[1], Is.EqualTo(2));
                float3 com0 = centres[0] / counts[0];
                float3 com1 = centres[1] / counts[1];
                Assert.That(distance(com0, new float3(1f, 0f, 0f)), Is.LessThan(1e-4f));
                Assert.That(distance(com1, new float3(11f, 0f, 0f)), Is.LessThan(1e-4f));
            }
            finally
            {
                particles.Dispose();
                centres.Dispose();
                counts.Dispose();
            }
        }

        // ── ComputeDeltaPosJob preserves IDs ──

        [Test]
        public void ComputeDeltaPosJobPreservesParticleGroupId()
        {
            // Regression: ComputeDeltaPosJob used to hard-code ID = 0, erasing
            // split/absorb group labels every sub-step.
            var psNew = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            var posPredict = new NativeArray<float3>(2, Allocator.TempJob);
            var lambda = new NativeArray<float>(2, Allocator.TempJob);
            var ids = new NativeArray<int>(2, Allocator.TempJob);
            var lut = new NativeHashMap<int, int2>(4, Allocator.TempJob);

            try
            {
                // Two particles far apart (no neighbor interaction) → dp = 0.
                posPredict[0] = new float3(0f, 0f, 0f);
                posPredict[1] = new float3(50f, 0f, 0f);
                lambda[0] = 0f;
                lambda[1] = 0f;
                ids[0] = 0;
                ids[1] = 1;

                new SlimeSimulation.ComputeDeltaPosJob
                {
                    Lut = lut,
                    PosPredict = posPredict,
                    Lambda = lambda,
                    Ids = ids,
                    PsNew = psNew,
                }.Schedule(2, 1).Complete();

                Assert.That(psNew[0].ID, Is.EqualTo(0));
                Assert.That(psNew[1].ID, Is.EqualTo(1), "ID must survive the PBF solve");
            }
            finally
            {
                psNew.Dispose();
                posPredict.Dispose();
                lambda.Dispose();
                ids.Dispose();
                lut.Dispose();
            }
        }

        static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        static void AssertCompactGroupRest(
            NativeArray<float3> rest,
            NativeArray<SlimeParticle> particles,
            int groupId,
            string message)
        {
            float3 sum = new float3(0f, 0f, 0f);
            float3 minRest = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 maxRest = new float3(float.MinValue, float.MinValue, float.MinValue);
            int count = 0;

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId)
                    continue;

                sum += rest[i];
                minRest = min(minRest, rest[i]);
                maxRest = max(maxRest, rest[i]);
                count++;
            }

            Assert.That(count, Is.GreaterThan(0), message);
            float3 extent = maxRest - minRest;
            Assert.That(length(sum), Is.LessThan(0.001f), message);
            Assert.That(extent.y, Is.GreaterThan(0.5f), message);
            Assert.That(extent.z, Is.GreaterThan(0.5f), message);
        }

        static bool AnyRestDiffersFromCanonical(
            NativeArray<float3> rest,
            NativeArray<float3> canonicalRest,
            NativeArray<SlimeParticle> particles,
            int groupId)
        {
            for (int i = 0; i < particles.Length; i++)
                if (particles[i].ID == groupId && distance(rest[i], canonicalRest[i]) > 0.001f)
                    return true;

            return false;
        }
    }
}
