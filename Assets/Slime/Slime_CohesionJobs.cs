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
    ///
    /// Group-aware: each particle carries a group ID (SlimeParticle.ID). Centres and
    /// shape-matching are computed per group so a split-off slime keeps its own
    /// cohesion and is unaffected by the main body's controller / shape target.
    /// </summary>
    public static class SlimeCohesion
    {
        /// <summary>
        /// Per-group centre of mass + particle count from current (shuffled) positions.
        /// Centres/Counts are length MaxGroups; group g's COM = Centres[g] / Counts[g]
        /// (zero when the group has no particles).
        /// </summary>
        [BurstCompile]
        public struct ComputeCenterJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Particles;
            public NativeArray<float3> Centres; // length MaxGroups — running sum of positions
            public NativeArray<int> Counts;     // length MaxGroups — running particle count

            public void Execute()
            {
                for (int g = 0; g < Centres.Length; g++)
                {
                    Centres[g] = new float3(0f, 0f, 0f);
                    Counts[g] = 0;
                }

                for (int i = 0; i < Particles.Length; i++)
                {
                    int g = Particles[i].ID;
                    if ((uint)g >= (uint)Centres.Length) continue;
                    Centres[g] += Particles[i].Position;
                    Counts[g]++;
                }
            }
        }

        /// <summary>
        /// Per-group rest-offset sums from the canonical rest shape.
        /// RestCentres[g] / Counts[g] is subtracted virtually during shape matching,
        /// so split bodies can self-cohere without rewriting the original baseline.
        /// </summary>
        [BurstCompile]
        public struct ComputeRestCenterJob : IJob
        {
            [ReadOnly] public NativeArray<SlimeParticle> Particles;
            [ReadOnly] public NativeArray<float3> RestOffsets;
            public NativeArray<float3> RestCentres; // length MaxGroups — running sum of rest offsets

            public void Execute()
            {
                for (int g = 0; g < RestCentres.Length; g++)
                    RestCentres[g] = new float3(0f, 0f, 0f);

                for (int i = 0; i < Particles.Length && i < RestOffsets.Length; i++)
                {
                    int g = Particles[i].ID;
                    if ((uint)g >= (uint)RestCentres.Length) continue;
                    RestCentres[g] += RestOffsets[i];
                }
            }
        }

        /// <summary>
        /// Translation-only shape matching, per group.
        /// For each particle: target = groupCentre + (restOffset - groupRestCentre).
        /// stiffness ∈ [0, 1] — 0 = no restoration, 1 = instant snap.
        /// </summary>
        [BurstCompile]
        public struct ApplyShapeMatchJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> RestOffsets;   // shuffled
            [ReadOnly] public NativeArray<float3> RestCentres;   // length MaxGroups — running sums
            [ReadOnly] public NativeArray<float3> Centres;       // length MaxGroups — running sums
            [ReadOnly] public NativeArray<int> Counts;           // length MaxGroups
            [ReadOnly] public NativeArray<float> Stiffness;      // length MaxGroups — per group
            [ReadOnly] public NativeArray<SlimeParticle> Particles; // shuffled
            public NativeArray<float3> Velocity;                 // shuffled
            public float inverseDt;

            public void Execute(int i)
            {
                int g = Particles[i].ID;
                float3 center = new float3(0f, 0f, 0f);
                if ((uint)g < (uint)Centres.Length && Counts[g] > 0)
                    center = Centres[g] / Counts[g];

                float3 restCenter = new float3(0f, 0f, 0f);
                if (RestCentres.IsCreated && (uint)g < (uint)RestCentres.Length && Counts[g] > 0)
                    restCenter = RestCentres[g] / Counts[g];

                float stiffness = (uint)g < (uint)Stiffness.Length ? Stiffness[g] : 0f;

                float3 currentPos = Particles[i].Position;
                float3 target = center + RestOffsets[i] - restCenter;
                float3 correction = (target - currentPos) * stiffness;

                Velocity[i] += correction * inverseDt;
            }
        }
    }
}
