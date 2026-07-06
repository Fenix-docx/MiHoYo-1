using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    public enum SlimeCollisionType : byte
    {
        Discrete = 0,
        Ccd = 1,
    }

    /// <summary>
    /// Main-thread collision event data for scripts and Inspector listeners.
    /// Positions, distances, and speeds are in Unity world space.
    /// </summary>
    [Serializable]
    public readonly struct SlimeCollisionEventData
    {
        public readonly Collider Collider;
        public readonly Vector3 ContactPoint;
        public readonly Vector3 ContactNormal;
        public readonly float PenetrationDepth;
        public readonly float ImpactSpeed;
        public readonly SlimeCollisionType CollisionType;

        public SlimeCollisionEventData(
            Collider collider,
            Vector3 contactPoint,
            Vector3 contactNormal,
            float penetrationDepth,
            float impactSpeed,
            SlimeCollisionType collisionType)
        {
            Collider = collider;
            ContactPoint = contactPoint;
            ContactNormal = contactNormal.sqrMagnitude > 0f ? contactNormal.normalized : Vector3.up;
            PenetrationDepth = penetrationDepth;
            ImpactSpeed = impactSpeed;
            CollisionType = collisionType;
        }

        public static SlimeCollisionEventData FromSim(SlimeCollisionEvent simEvent)
        {
            return FromSim(simEvent, null);
        }

        public static SlimeCollisionEventData FromSim(SlimeCollisionEvent simEvent, Collider collider)
        {
            SlimeCollisionType collisionType =
                simEvent.CollisionType == 1 ? SlimeCollisionType.Ccd : SlimeCollisionType.Discrete;

            return new SlimeCollisionEventData(
                collider,
                (Vector3)(simEvent.ContactPoint * SlimeConst.Scale),
                (Vector3)math.normalizesafe(simEvent.ContactNormal, new float3(0f, 1f, 0f)),
                simEvent.PenetrationDepth * SlimeConst.Scale,
                simEvent.ImpactSpeed * SlimeConst.Scale,
                collisionType);
        }
    }

    [Serializable]
    public sealed class SlimeCollisionUnityEvent : UnityEvent<SlimeCollisionEventData> { }
}
