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
                groups.Init(concentration: 10f, stiffness: 0.15f, cohesionStep: 1);
                // Simulate the player lowering cohesion (diffuse) before splitting.
                groups.SetControlledCohesionState(0.1f, 0f, 3);

                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.5f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(split, Is.GreaterThanOrEqualTo(0));
                Assert.That(groups.GetConcentration(split), Is.EqualTo(10f),
                    "split body uses initial concentration, not the parent's diffuse value");
                Assert.That(groups.GetStiffness(split), Is.EqualTo(0.15f));
                Assert.That(groups.GetCohesionStep(split), Is.EqualTo(1));
                Assert.That(groups.GetConcentration(0), Is.EqualTo(0.1f),
                    "main body keeps its (player-adjusted) state");
                Assert.That(groups.GetCohesionStep(0), Is.EqualTo(3));
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

        // Control switch cohesion state
        [Test]
        public void SwitchControlPreservesEachGroupCohesionState()
        {
            const int n = 128;
            var particles = new NativeArray<SlimeParticle>(n, Allocator.TempJob);
            var vel = new NativeArray<float3>(n, Allocator.TempJob);
            for (int i = 0; i < n; i++)
                particles[i] = new SlimeParticle { Position = new float3(i, 0f, 0f), ID = 0 };

            try
            {
                var groups = new SlimeGroupManager();
                groups.Init(10f, 0.15f, cohesionStep: 1);
                groups.SetControlledCohesionState(0.1f, 0f, 3);

                int split = groups.Split(particles, vel, new float3(1f, 0f, 0f),
                    splitFraction: 0.25f, separation: 0f, minParticlesToSplit: 16);
                Assert.That(split, Is.GreaterThanOrEqualTo(0));

                int switched = groups.SwitchControl();
                Assert.That(switched, Is.EqualTo(split));
                Assert.That(groups.GetConcentration(switched), Is.EqualTo(10f));
                Assert.That(groups.GetStiffness(switched), Is.EqualTo(0.15f));
                Assert.That(groups.GetCohesionStep(switched), Is.EqualTo(1));

                groups.SetControlledCohesionState(6f, 0.05f, 2);
                Assert.That(groups.GetConcentration(switched), Is.EqualTo(6f));
                Assert.That(groups.GetCohesionStep(switched), Is.EqualTo(2));

                int switchedBack = groups.SwitchControl();
                Assert.That(switchedBack, Is.EqualTo(0));
                Assert.That(groups.GetConcentration(switchedBack), Is.EqualTo(0.1f));
                Assert.That(groups.GetStiffness(switchedBack), Is.EqualTo(0f));
                Assert.That(groups.GetCohesionStep(switchedBack), Is.EqualTo(3));
            }
            finally
            {
                particles.Dispose();
                vel.Dispose();
            }
        }

        [Test]
        public void SimulatorSwitchControlSyncsCohesionStateForHud()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Cohesion HUD Switch Regression");

            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.splitFraction = 0.5f;
                simulator.splitSeparation = 0f;
                simulator.minParticlesToSplit = 16;
                simulator.concentration = 10f;
                simulator.shapeStiffness = 0.15f;
                simulator.currentstep = 1;

                simulator.Initialise();

                simulator.concentration = 0.1f;
                simulator.shapeStiffness = 0f;
                simulator.currentstep = 3;

                Assert.That(simulator.Split(Vector3.right), Is.True);

                int switched = simulator.SwitchControl();
                Assert.That(switched, Is.GreaterThan(0));
                Assert.That(simulator.ControlledGroupId, Is.EqualTo(switched));
                Assert.That(simulator.CohesionStep, Is.EqualTo(1));
                Assert.That(simulator.concentration, Is.EqualTo(10f));
                Assert.That(simulator.shapeStiffness, Is.EqualTo(0.15f));
                Assert.That(simulator.ControlledParticleCount, Is.GreaterThan(0));
                Assert.That(simulator.ControlledParticleCount, Is.LessThan(simulator.ParticleCount));
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
            }
        }

        // ApplyForceJob: velocity-follow only affects the controlled group

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

        [Test]
        public void VelocityFollowUsesGroupCenterOfMassNotCenterSum()
        {
            var ps = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            var psNew = new NativeArray<SlimeParticle>(2, Allocator.TempJob);
            var vel = new NativeArray<float3>(2, Allocator.TempJob);
            var centres = new NativeArray<float3>(2, Allocator.TempJob);
            var counts = new NativeArray<int>(2, Allocator.TempJob);
            var controllers = new NativeList<SlimeController>(1, Allocator.TempJob);

            try
            {
                ps[0] = new SlimeParticle { Position = new float3(10f, 1f, 0f), ID = 0 };
                ps[1] = new SlimeParticle { Position = new float3(12f, 1f, 0f), ID = 0 };
                centres[0] = ps[0].Position + ps[1].Position; // ComputeCenterJob stores a sum.
                counts[0] = 2;

                controllers.Add(new SlimeController
                {
                    Center = new float3(11f, 1f, 0f),
                    Radius = 0f,
                    Velocity = new float3(10f, 0f, 0f),
                    Concentration = 0f,
                });

                new SlimeSimulation.ApplyForceJob
                {
                    Controllers = controllers,
                    Ps = ps,
                    PsNew = psNew,
                    Velocity = vel,
                    Gravity = new float3(0f, 0f, 0f),
                    VelocityFollowGain = 1f,
                    ControlledGroupId = 0,
                    GroupCentres = centres,
                    GroupCounts = counts,
                    MoveCoreRadius = 2f,
                }.Schedule(2, 1).Complete();

                Assert.That(vel[0].x, Is.GreaterThan(1f),
                    "core follow must compare particles against COM, not the raw centre sum");
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

        [Test]
        public void ControllerMovementOnSimulatorObjectTranslatesOnlyControlledGroup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Same Object Controller Regression");

            try
            {
                Rigidbody controllerBody = simulatorObject.AddComponent<Rigidbody>();
                simulatorObject.AddComponent<SlimeControllerMotor>();
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.controllerBody = controllerBody;
                simulator.Initialise();

                NativeArray<SlimeParticle> particles =
                    GetPrivateField<NativeArray<SlimeParticle>>(simulator, "_particles");
                SlimeParticle splitParticle = particles[1];
                splitParticle.ID = 1;
                particles[1] = splitParticle;
                float3 controlledBefore = particles[0].Position;
                float3 splitBefore = particles[1].Position;

                simulatorObject.transform.position += Vector3.right;
                InvokePrivate(simulator, "SyncControllerBodyTranslation");

                Assert.That(distance(particles[0].Position, controlledBefore + new float3(SlimeConst.InvScale, 0f, 0f)),
                    Is.LessThan(0.0001f), "controller movement should directly carry the controlled group");
                Assert.That(distance(particles[1].Position, splitBefore), Is.LessThan(0.0001f),
                    "controller movement must not carry split groups");
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void InitialParticlesSpawnAroundSeparatedControllerBody()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Root");
            GameObject controllerObject = new GameObject("Slime Controller");

            try
            {
                simulatorObject.transform.position = new Vector3(10f, 0f, 0f);
                controllerObject.transform.position = new Vector3(3f, 0f, 0f);
                Rigidbody controllerBody = controllerObject.AddComponent<Rigidbody>();
                controllerObject.AddComponent<SlimeControllerMotor>();

                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.controllerBody = controllerBody;
                simulator.Initialise();

                NativeArray<SlimeParticle> particles =
                    GetPrivateField<NativeArray<SlimeParticle>>(simulator, "_particles");
                float3 center = new float3(0f, 0f, 0f);
                for (int i = 0; i < particles.Length; i++)
                    center += particles[i].Position;
                center /= particles.Length;

                Vector3 worldCenter = (Vector3)(center * SlimeConst.Scale);
                Assert.That(Vector3.Distance(worldCenter, controllerObject.transform.position), Is.LessThan(0.25f),
                    "a separated motor should define the spawned slime centre");
                Assert.That(Vector3.Distance(worldCenter, simulatorObject.transform.position), Is.GreaterThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void DirectMeshShapeTargetDefaultsToControllerTransform()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Root");
            GameObject controllerObject = new GameObject("Slime Controller");
            Mesh mesh = new Mesh();

            try
            {
                Rigidbody controllerBody = controllerObject.AddComponent<Rigidbody>();
                controllerObject.AddComponent<SlimeControllerMotor>();
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.controllerBody = controllerBody;
                simulator.shapeTargetMesh = mesh;

                bool resolved = simulator.TryResolveMeshShapeTarget(
                    out Mesh resolvedMesh,
                    out Transform resolvedTransform,
                    out string error);

                Assert.That(resolved, Is.True, error);
                Assert.That(resolvedMesh, Is.SameAs(mesh));
                Assert.That(resolvedTransform, Is.SameAs(controllerObject.transform),
                    "mesh assets without an explicit shape target transform should follow the controller frame");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void ControlUsesMotorCommandedVelocityWhenRigidbodyVelocityIsZero()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Root");
            GameObject controllerObject = new GameObject("Slime Controller");

            try
            {
                Rigidbody controllerBody = controllerObject.AddComponent<Rigidbody>();
                SlimeControllerMotor motor = controllerObject.AddComponent<SlimeControllerMotor>();
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.controllerBody = controllerBody;
                simulator.Initialise();

                SetPrivateField(motor, "<CommandedVelocity>k__BackingField", Vector3.right * 2f);
                Assert.That(motor.CommandedVelocity.x, Is.EqualTo(2f).Within(0.0001f));

                InvokePrivate(simulator, "Control");

                NativeList<SlimeController> controllers =
                    GetPrivateField<NativeList<SlimeController>>(simulator, "_controllerBuffer");
                Assert.That(controllers[0].Velocity.x, Is.EqualTo(2f * SlimeConst.InvScale).Within(0.0001f),
                    "kinematic motor movement should reach the simulator even when Rigidbody velocity is zero");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void SwitchControlUpdatesCameraTargetCenterImmediately()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Root");
            GameObject controllerObject = new GameObject("Slime Controller");

            try
            {
                Rigidbody controllerBody = controllerObject.AddComponent<Rigidbody>();
                controllerObject.AddComponent<SlimeControllerMotor>();
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.controllerBody = controllerBody;
                simulator.splitFraction = 0.5f;
                simulator.splitSeparation = 5f;
                simulator.minParticlesToSplit = 16;
                simulator.Initialise();

                Assert.That(simulator.Split(Vector3.right), Is.True);
                int controlled = simulator.SwitchControl();

                NativeArray<SlimeParticle> particles =
                    GetPrivateField<NativeArray<SlimeParticle>>(simulator, "_particles");
                SlimeGroupManager.GroupBounds(particles, controlled, out float3 com, out _, out int count);

                Assert.That(count, Is.GreaterThan(0));
                Assert.That(Vector3.Distance(simulator.CurrentWorldCenter, (Vector3)(com * SlimeConst.Scale)),
                    Is.LessThan(0.0001f), "camera target should reflect the newly controlled group immediately");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void FirstControllerSyncAfterSwitchDoesNotMoveNewGroupToOldTarget()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Root");
            GameObject controllerObject = new GameObject("Slime Controller");

            try
            {
                Rigidbody controllerBody = controllerObject.AddComponent<Rigidbody>();
                controllerObject.AddComponent<SlimeControllerMotor>();
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                simulator.simulateOnStart = false;
                simulator.enableSurface = false;
                simulator.enableMeshShapeTarget = false;
                simulator.gravity = 0f;
                simulator.controllerBody = controllerBody;
                simulator.splitFraction = 0.5f;
                simulator.splitSeparation = 5f;
                simulator.minParticlesToSplit = 16;
                simulator.Initialise();

                Assert.That(simulator.Split(Vector3.right), Is.True);
                NativeArray<SlimeParticle> particles =
                    GetPrivateField<NativeArray<SlimeParticle>>(simulator, "_particles");

                Vector3 oldTarget = simulator.CurrentWorldCenter;
                int newGroup = simulator.SwitchControl();
                SlimeGroupManager.GroupBounds(particles, newGroup, out float3 beforeCom, out _, out int count);
                Assert.That(count, Is.GreaterThan(0));

                SetPrivateField(simulator, "_lastControllerBodyPosition", oldTarget);
                SetPrivateField(simulator, "_lastControllerBodyGroupId", 0);
                controllerBody.position = oldTarget;
                controllerBody.transform.position = oldTarget;

                InvokePrivate(simulator, "SyncControllerBodyTranslation");

                SlimeGroupManager.GroupBounds(particles, newGroup, out float3 afterCom, out _, out _);
                Assert.That(distance(afterCom, beforeCom), Is.LessThan(0.0001f),
                    "the first sync after switching control must reset its baseline instead of applying the old target delta");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void UpdateJobOctreeBroadPhaseMatchesFullColliderScan()
        {
            var fullParticles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            var octreeParticles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            var oldPositions = new NativeArray<float3>(1, Allocator.TempJob);
            var fullVelocity = new NativeArray<float3>(1, Allocator.TempJob);
            var octreeVelocity = new NativeArray<float3>(1, Allocator.TempJob);
            var colliders = new NativeArray<SlimeBoxCollider>(2, Allocator.TempJob);
            var octreeNodes = new NativeArray<SlimeColliderOctreeNode>(1, Allocator.TempJob);
            var octreeIndices = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                oldPositions[0] = new float3(-2f, 1f, 0f);
                fullParticles[0] = new SlimeParticle { Position = new float3(2f, 1f, 0f), ID = 0 };
                octreeParticles[0] = fullParticles[0];

                colliders[0] = MakeCollider(
                    new float3(0f, 1f, 0f),
                    new float3(1f, 1f, 1f),
                    new float3(1f, 0f, 0f),
                    new float3(0f, 1f, 0f),
                    new float3(0f, 0f, 1f));
                colliders[1] = MakeCollider(
                    new float3(100f, 100f, 100f),
                    new float3(5f, 5f, 5f),
                    new float3(1f, 0f, 0f),
                    new float3(0f, 1f, 0f),
                    new float3(0f, 0f, 1f));

                octreeNodes[0] = new SlimeColliderOctreeNode
                {
                    Center = new float3(0f, 1f, 0f),
                    HalfSize = 4f,
                    FirstChild = -1,
                    FirstCollider = 0,
                    ColliderCount = 1,
                };
                octreeIndices[0] = 0;

                new SlimeSimulation.UpdateJob
                {
                    Ps = fullParticles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    Velocity = fullVelocity,
                    EnableFloor = false,
                    UseColliderOctree = false,
                    FloorY = 1f,
                }.Schedule(1, 1).Complete();

                new SlimeSimulation.UpdateJob
                {
                    Ps = octreeParticles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    ColliderOctreeNodes = octreeNodes,
                    ColliderOctreeIndices = octreeIndices,
                    Velocity = octreeVelocity,
                    EnableFloor = false,
                    UseColliderOctree = true,
                    FloorY = 1f,
                }.Schedule(1, 1).Complete();

                Assert.That(distance(octreeParticles[0].Position, fullParticles[0].Position), Is.LessThan(0.0001f));
                Assert.That(distance(octreeVelocity[0], fullVelocity[0]), Is.LessThan(0.0001f));
            }
            finally
            {
                fullParticles.Dispose();
                octreeParticles.Dispose();
                oldPositions.Dispose();
                fullVelocity.Dispose();
                octreeVelocity.Dispose();
                colliders.Dispose();
                octreeNodes.Dispose();
                octreeIndices.Dispose();
            }
        }

        static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }

        static void SetPrivateField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
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

        static SlimeBoxCollider MakeCollider(float3 center, float3 extent, float3 axisX, float3 axisY, float3 axisZ)
        {
            return new SlimeBoxCollider
            {
                Center = center,
                Extent = extent,
                AxisX = axisX,
                AxisY = axisY,
                AxisZ = axisZ,
            };
        }
    }
}
