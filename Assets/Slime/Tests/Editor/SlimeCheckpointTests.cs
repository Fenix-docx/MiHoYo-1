using NUnit.Framework;
using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Slime.Tests
{
    public class SlimeCheckpointTests
    {
        [Test]
        public void CheckpointManagerStoresAndRespawnsToLatestCheckpoint()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject simulatorObject = new GameObject("Slime Simulator");
            GameObject managerObject = new GameObject("Checkpoint Manager");

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

                SlimeCheckpointManager manager = managerObject.AddComponent<SlimeCheckpointManager>();
                SetPrivateField(manager, "slimeSimulator", simulator);

                Vector3 checkpoint = new Vector3(4f, 2f, -3f);
                manager.SetCheckpoint(checkpoint);

                simulator.TeleportToWorldCenter(new Vector3(-5f, 1f, 0f), Quaternion.identity, true);
                Assert.That(manager.Respawn(), Is.True);

                Assert.That(Vector3.Distance(simulator.CurrentWorldCenter, checkpoint), Is.LessThan(0.01f));
                Assert.That(math.distance(GetParticleCenter(simulator), (float3)checkpoint * SlimeConst.InvScale), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(managerObject);
                Object.DestroyImmediate(simulatorObject);
            }
        }

        [Test]
        public void CheckpointZoneRefreshesManagerCheckpoint()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Checkpoint Manager");
            GameObject zoneObject = new GameObject("Checkpoint Zone");
            GameObject markerObject = new GameObject("Checkpoint Marker");

            try
            {
                SlimeCheckpointManager manager = managerObject.AddComponent<SlimeCheckpointManager>();

                markerObject.transform.SetPositionAndRotation(
                    new Vector3(7f, 1.5f, 2f),
                    Quaternion.Euler(0f, 90f, 0f));

                SlimeCheckpointZone zone = zoneObject.AddComponent<SlimeCheckpointZone>();
                SetPrivateField(zone, "checkpointManager", manager);
                SetPrivateField(zone, "checkpointLocation", markerObject.transform);

                zone.Activate();

                Assert.That(zone.HasActivated, Is.True);
                Assert.That(manager.HasCheckpoint, Is.True);
                Assert.That(Vector3.Distance(manager.CheckpointPosition, markerObject.transform.position), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(manager.CheckpointRotation, markerObject.transform.rotation), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(markerObject);
                Object.DestroyImmediate(zoneObject);
                Object.DestroyImmediate(managerObject);
            }
        }

        [Test]
        public void CheckpointZoneActivateOnceIgnoresRepeatedActivationUntilReset()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject managerObject = new GameObject("Checkpoint Manager");
            GameObject zoneObject = new GameObject("Checkpoint Zone");

            try
            {
                SlimeCheckpointManager manager = managerObject.AddComponent<SlimeCheckpointManager>();

                zoneObject.transform.position = Vector3.one;
                SlimeCheckpointZone zone = zoneObject.AddComponent<SlimeCheckpointZone>();
                SetPrivateField(zone, "checkpointManager", manager);

                zone.Activate();
                Vector3 firstCheckpoint = manager.CheckpointPosition;

                zoneObject.transform.position = Vector3.one * 5f;
                zone.Activate();
                Assert.That(Vector3.Distance(manager.CheckpointPosition, firstCheckpoint), Is.LessThan(0.001f));

                zone.ResetActivationState();
                zone.Activate();
                Assert.That(Vector3.Distance(manager.CheckpointPosition, zoneObject.transform.position), Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(zoneObject);
                Object.DestroyImmediate(managerObject);
            }
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

        static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
