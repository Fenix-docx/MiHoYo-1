using NUnit.Framework;
using System.Reflection;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Slime.Tests
{
    public class SlimeCollisionEventTests
    {
        [Test]
        public void UpdateJobRecordsSingleDiscreteColliderEvent()
        {
            NativeArray<SlimeParticle> particles = new NativeArray<SlimeParticle>(1, Allocator.TempJob);
            NativeArray<float3> oldPositions = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<float3> velocities = new NativeArray<float3>(1, Allocator.TempJob);
            NativeArray<SlimeBoxCollider> colliders = new NativeArray<SlimeBoxCollider>(1, Allocator.TempJob);
            NativeArray<SlimeColliderOctreeNode> colliderOctreeNodes =
                new NativeArray<SlimeColliderOctreeNode>(0, Allocator.TempJob);
            NativeArray<int> colliderOctreeIndices = new NativeArray<int>(0, Allocator.TempJob);
            NativeList<SlimeCollisionEvent> events = new NativeList<SlimeCollisionEvent>(4, Allocator.TempJob);
            NativeParallelHashSet<ulong> eventSet = new NativeParallelHashSet<ulong>(4, Allocator.TempJob);

            try
            {
                particles[0] = new SlimeParticle { Position = new float3(0.5f, 0f, 0f), ID = 0 };
                oldPositions[0] = new float3(2f, 0f, 0f);
                colliders[0] = MakeCollider(float3.zero, new float3(1f, 1f, 1f));

                new SlimeSimulation.UpdateJob
                {
                    Ps = particles,
                    PosOld = oldPositions,
                    Colliders = colliders,
                    ColliderOctreeNodes = colliderOctreeNodes,
                    ColliderOctreeIndices = colliderOctreeIndices,
                    Velocity = velocities,
                    RecordCollisionEvents = true,
                    CollisionEvents = events.AsParallelWriter(),
                    CollisionEventSet = eventSet.AsParallelWriter(),
                    MinCollisionEventSpeed = 0f,
                }.Schedule(particles.Length, 1).Complete();

                Assert.That(events.Length, Is.EqualTo(1));

                SlimeCollisionEvent collisionEvent = events[0];
                Assert.That(collisionEvent.ParticleIndex, Is.EqualTo(0));
                Assert.That(collisionEvent.ColliderIndex, Is.EqualTo(0));
                Assert.That(math.distance(collisionEvent.ContactPoint, new float3(1f, 0f, 0f)), Is.LessThan(0.0001f));
                Assert.That(math.distance(collisionEvent.ContactNormal, new float3(1f, 0f, 0f)), Is.LessThan(0.0001f));
                Assert.That(collisionEvent.PenetrationDepth, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(collisionEvent.ImpactSpeed, Is.GreaterThan(0f));
                Assert.That(collisionEvent.CollisionType, Is.EqualTo((byte)0));
            }
            finally
            {
                eventSet.Dispose();
                events.Dispose();
                colliderOctreeIndices.Dispose();
                colliderOctreeNodes.Dispose();
                colliders.Dispose();
                velocities.Dispose();
                oldPositions.Dispose();
                particles.Dispose();
            }
        }

        [Test]
        public void CollisionEventDataConvertsSimValuesToWholeSlimeWorldSpace()
        {
            SlimeCollisionEvent simEvent = new SlimeCollisionEvent
            {
                ParticleIndex = 3,
                ColliderIndex = 7,
                ContactPoint = new float3(10f, 20f, 30f),
                ContactNormal = new float3(0f, 1f, 0f),
                PenetrationDepth = 2f,
                ImpactSpeed = 5f,
                CollisionType = 1,
            };

            SlimeCollisionEventData data = SlimeCollisionEventData.FromSim(simEvent);

            Assert.That(typeof(SlimeCollisionEventData).GetField("ParticleIndex"), Is.Null);
            Assert.That(typeof(SlimeCollisionEventData).GetField("ColliderIndex"), Is.Null);
            Assert.That(data.ContactPoint, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(data.ContactNormal, Is.EqualTo(Vector3.up));
            Assert.That(data.PenetrationDepth, Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(data.ImpactSpeed, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(data.CollisionType, Is.EqualTo(SlimeCollisionType.Ccd));
        }

        [Test]
        public void SimulatorDispatchesOneWholeSlimeEventPerCollider()
        {
            GameObject simulatorObject = new GameObject("Slime Collision Dispatch Test");
            NativeList<SlimeCollisionEvent> events = new NativeList<SlimeCollisionEvent>(4, Allocator.TempJob);
            SlimeSimulator simulator = null;

            try
            {
                simulator = simulatorObject.AddComponent<SlimeSimulator>();
                events.Add(new SlimeCollisionEvent
                {
                    ParticleIndex = 0,
                    ColliderIndex = 2,
                    ContactPoint = new float3(1f, 0f, 0f),
                    ContactNormal = new float3(1f, 0f, 0f),
                    PenetrationDepth = 0.2f,
                    ImpactSpeed = 1f,
                    CollisionType = 0,
                });
                events.Add(new SlimeCollisionEvent
                {
                    ParticleIndex = 1,
                    ColliderIndex = 2,
                    ContactPoint = new float3(2f, 0f, 0f),
                    ContactNormal = new float3(1f, 0f, 0f),
                    PenetrationDepth = 0.3f,
                    ImpactSpeed = 3f,
                    CollisionType = 1,
                });

                SetPrivateField(simulator, "_collisionEvents", events);

                int callbackCount = 0;
                SlimeCollisionEventData received = default;
                simulator.OnSlimeCollision += data =>
                {
                    callbackCount++;
                    received = data;
                };

                InvokePrivate(simulator, "DispatchCollisionEvents");

                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(received.ImpactSpeed, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(received.CollisionType, Is.EqualTo(SlimeCollisionType.Ccd));
            }
            finally
            {
                if (simulator != null)
                    SetPrivateField(simulator, "_collisionEvents", default(NativeList<SlimeCollisionEvent>));

                if (events.IsCreated)
                    events.Dispose();

                Object.DestroyImmediate(simulatorObject);
            }
        }

        static SlimeBoxCollider MakeCollider(float3 center, float3 extent)
        {
            return new SlimeBoxCollider
            {
                Center = center,
                Extent = extent,
                AxisX = new float3(1f, 0f, 0f),
                AxisY = new float3(0f, 1f, 0f),
                AxisZ = new float3(0f, 0f, 1f),
            };
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
    }

    public class SlimeControllerMotorCollisionSoundTests
    {
        [Test]
        public void CollisionSoundVolumeScalesFromImpactThreshold()
        {
            Assert.That(
                SlimeControllerMotor.CalculateCollisionSoundVolume(0.25f, 0.5f, 5f, 0.8f),
                Is.EqualTo(0f).Within(0.0001f));

            Assert.That(
                SlimeControllerMotor.CalculateCollisionSoundVolume(5f, 0.5f, 5f, 0.8f),
                Is.EqualTo(0.8f).Within(0.0001f));

            float middle = SlimeControllerMotor.CalculateCollisionSoundVolume(2.75f, 0.5f, 5f, 0.8f);
            Assert.That(middle, Is.GreaterThan(0f));
            Assert.That(middle, Is.LessThan(0.8f));
        }

        [Test]
        public void SimulatorExposesSplitEventsForAudioListeners()
        {
            GameObject simulatorObject = new GameObject("Slime Split Event Test");
            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();

                int callbackCount = 0;
                int unityEventCount = 0;
                simulator.OnSlimeSplit += () => callbackCount++;
                simulator.onSlimeSplit.AddListener(() => unityEventCount++);

                InvokePrivate(simulator, "DispatchSlimeSplit");

                Assert.That(callbackCount, Is.EqualTo(1));
                Assert.That(unityEventCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void ControllerMotorBindsSeparateSimulatorByControllerBody()
        {
            GameObject simulatorObject = new GameObject("Separate Slime Simulator");
            GameObject otherSimulatorObject = new GameObject("Other Slime Simulator");
            GameObject motorObject = new GameObject("Separate Slime Controller");

            try
            {
                SlimeSimulator simulator = simulatorObject.AddComponent<SlimeSimulator>();
                otherSimulatorObject.AddComponent<SlimeSimulator>();
                Rigidbody body = motorObject.AddComponent<Rigidbody>();
                SlimeControllerMotor motor = motorObject.AddComponent<SlimeControllerMotor>();
                simulator.controllerBody = body;

                Assert.That(motor.BindSimulatorForAudioEvents(), Is.True);
                Assert.That(GetPrivateField<SlimeSimulator>(motor, "_simulator"), Is.SameAs(simulator));
            }
            finally
            {
                Object.DestroyImmediate(motorObject);
                Object.DestroyImmediate(otherSimulatorObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        static void InvokePrivate(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(target, null);
        }

        static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (T)field.GetValue(target);
        }
    }

    public class SlimeContactAudioEventTests
    {
        [Test]
        public void AudioContactEventCanBeUsedAsContactHandler()
        {
            GameObject triggerObject = new GameObject("Slime Audio Contact Test");

            try
            {
                SlimeAudioContactEvent audioEvent = triggerObject.AddComponent<SlimeAudioContactEvent>();
                AudioClip clip = AudioClip.Create("contact-start", 64, 1, 44100, false);
                SetPrivateField(audioEvent, "contactBeganClip", clip);

                audioEvent.HandleContactBegan(default);

                Assert.That(audioEvent, Is.InstanceOf<SlimeContactEventHandler>());
                Assert.That(audioEvent.HasTriggered, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(triggerObject);
            }
        }

        static void SetPrivateField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
