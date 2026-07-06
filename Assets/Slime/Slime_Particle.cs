using Unity.Mathematics;

namespace Slime
{
    /// <summary>
    /// Single PBF particle. Position is in simulation space (world × InvScale).
    /// ID maps to a ParticleController for cohesion / connected-component labeling.
    /// </summary>
    public struct SlimeParticle
    {
        public float3 Position;
        public int ID;
    }

    /// <summary>
    /// Defines a cohesion zone. Particles whose ID matches a controller index
    /// experience centre-attraction and velocity-blending forces.
    /// All lengths in simulation space.
    /// </summary>
    public struct SlimeController
    {
        public float3 Center;
        public float Radius;
        public float3 Velocity;
        public float Concentration;
    }

    /// <summary>
    /// Oriented box obstacle in simulation space.
    /// Built from Unity static colliders and consumed by UpdateJob for particle
    /// collision in collider-local space.
    /// </summary>
    public struct SlimeBoxCollider
    {
        public float3 Center;  // box centre, sim space
        public float3 Extent;  // half extents, sim space
        public float3 AxisX;   // local +X axis, unit length
        public float3 AxisY;   // local +Y axis, unit length
        public float3 AxisZ;   // local +Z axis, unit length
    }

    /// <summary>
    /// Burst-friendly particle-vs-scene-collider collision event.
    /// Positions, distances, and speeds are in simulation space.
    /// </summary>
    public struct SlimeCollisionEvent
    {
        public int ParticleIndex;
        public int ColliderIndex;
        public float3 ContactPoint;
        public float3 ContactNormal;
        public float PenetrationDepth;
        public float ImpactSpeed;
        public byte CollisionType; // 0 = discrete push-out, 1 = CCD segment hit
    }

    /// <summary>
    /// Native-friendly octree node used as a broad phase for static slime
    /// collision. Bounds are axis-aligned cubes in simulation space.
    /// </summary>
    public struct SlimeColliderOctreeNode
    {
        public float3 Center;
        public float HalfSize;
        public int FirstChild;      // -1 for leaf, otherwise index of 8 contiguous children
        public int FirstCollider;   // offset into SlimeSimulator's octree collider index buffer
        public int ColliderCount;   // colliders stored directly on this node
    }
}
