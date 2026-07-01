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
    /// Axis-aligned box obstacle in simulation space.
    /// Built from Unity BoxColliders (world → sim via InvScale) and consumed by
    /// UpdateJob for particle collision (AABB push-out). Matches the clousq
    /// reference's MyBoxCollider.
    /// </summary>
    public struct SlimeBoxCollider
    {
        public float3 Center;  // box centre, sim space
        public float3 Extent;  // half extents, sim space
    }
}
