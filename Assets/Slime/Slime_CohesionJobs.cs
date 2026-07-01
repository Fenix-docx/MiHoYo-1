using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Slime
{
    /// <summary>
    /// Phase 2 cohesion jobs: centre-of-mass computation + shape-matching restoration.
    /// All positions/offsets must be in the same cell-grouped (shuffled) order.
    /// </summary>
    public static class SlimeCohesion
    {
        /// <summary>
        /// Single-threaded centre-of-mass from current (shuffled) positions.
        /// </summary>
        [BurstCompile]
        public struct ComputeCenterJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Particles;
            public NativeArray<float3> Center; // length 1

            public void Execute()
            {
                float3 sum = new float3(0f, 0f, 0f);
                for (int i = 0; i < Particles.Length; i++)
                    sum += Particles[i].Position;

                Center[0] = sum / Particles.Length;
            }
        }

        /// <summary>
        /// Translation-only shape matching.
        /// For each particle: target = centre + restOffset, then blend velocity toward target.
        /// stiffness ∈ [0, 1] — 0 = no restoration, 1 = instant snap.
        /// </summary>
        [BurstCompile]
        public struct ApplyShapeMatchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> RestOffsets;   // shuffled
            [ReadOnly] public NativeArray<float3> Center;        // length 1
            [ReadOnly] public NativeArray<SlimeParticle> Particles; // shuffled
            public NativeArray<float3> Velocity;                 // shuffled
            public float stiffness;
            public float inverseDt;

            public void Execute(int i)
            {
                float3 currentPos = Particles[i].Position;
                float3 target = Center[0] + RestOffsets[i];
                float3 correction = (target - currentPos) * stiffness;

                Velocity[i] += correction * inverseDt;
            }
        }
    }
}
