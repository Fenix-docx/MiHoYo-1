using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;

namespace Slime
{
    /// <summary>
    /// All PBF simulation Burst jobs, matching the clousq reference pipeline.
    /// Scheduling order: ApplyForce → Hash → Sort → BuildLut → Shuffle →
    ///                    ComputeLambda → ComputeDeltaPos → Update → ApplyViscosity
    /// </summary>
    public static class SlimeSimulation
    {
        // ── 1. Apply forces + predict positions ──

        [BurstCompile]
        public struct ApplyForceJob : IJobParallelFor
        {
            [ReadOnly] public NativeList<SlimeController> Controllers;
            [ReadOnly] public NativeArray<SlimeParticle> Ps;
            [WriteOnly] public NativeArray<SlimeParticle> PsNew;
            public NativeArray<float3> Velocity;
            public float3 Gravity;
            public float VelocityFollowGain; // locomotion: blend toward controller velocity (0 = off)

            /// <summary>
            /// Blob centre of mass (sim-space). Used to compute the core-driven
            /// locomotion falloff: particles near the COM receive full velocity
            /// blend, particles at the periphery follow via PBF constraints.
            /// </summary>
            public float3 BlobCenter;
            public float MoveCoreRadius; // sim units; 0 = uniform (apply to all particles)

            public void Execute(int i)
            {
                SlimeParticle p = Ps[i];
                if (!IsFinite(p.Position))
                    p.Position = new float3(0f, 1f, 0f);

                float3 velocity = Velocity[i] * 0.99f + Gravity * SlimeConst.DeltaTime;
                if (!IsFinite(velocity))
                    velocity = new float3(0f, 0f, 0f);

                if (p.ID >= 0 && p.ID < Controllers.Length)
                {
                    SlimeController cl = Controllers[p.ID];
                    float3 controllerCenter = IsFinite(cl.Center) ? cl.Center : p.Position;
                    float controllerRadius = IsFinite(cl.Radius) ? cl.Radius : 0f;
                    float controllerConcentration = IsFinite(cl.Concentration) ? cl.Concentration : 0f;
                    float3 controllerVelocity = IsFinite(cl.Velocity) ? cl.Velocity : new float3(0f, 0f, 0f);
                    float3 toCenter = controllerCenter - p.Position;
                    float len = length(toCenter);

                    // Core-driven locomotion weight: 1.0 at blob centre, falls off
                    // linearly to 0 at MoveCoreRadius. Particles beyond the core
                    // radius receive no velocity blend and follow via PBF constraints.
                    float distToBlobCenter = length(p.Position - BlobCenter);
                    float coreWeight = MoveCoreRadius <= 0f ? 1f :
                        saturate(1f - distToBlobCenter / max(MoveCoreRadius, 0.001f));

                    if (len < controllerRadius)
                    {
                        velocity = lerp(controllerVelocity, velocity,
                            lerp(1f, len * 0.1f, controllerConcentration * 0.002f));
                        velocity += controllerConcentration * SlimeConst.DeltaTime *
                                    min(1f, len) * normalizesafe(toCenter);
                    }

                    // Core-driven locomotion: only particles near the blob COM
                    // adopt the controller's velocity. The centre leads, the
                    // periphery follows via PBF density constraints.
                    if (VelocityFollowGain > 0f && lengthsq(controllerVelocity) > 1e-6f)
                        velocity = lerp(velocity, controllerVelocity,
                            VelocityFollowGain * coreWeight);
                }

                p.Position += velocity * SlimeConst.PredictStep;
                if (!IsFinite(p.Position))
                    p.Position = new float3(0f, 1f, 0f);

                PsNew[i] = p;
                Velocity[i] = velocity;
            }

            static bool IsFinite(float value)
            {
                return !isnan(value) && isfinite(value);
            }

            static bool IsFinite(float3 value)
            {
                return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            }
        }

        // ── 2. Spatial hash ──

        [BurstCompile]
        public struct HashJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SlimeParticle> Ps;
            [WriteOnly] public NativeArray<int2> Hashes;

            public void Execute(int i)
            {
                int3 gridPos = SlimeConst.GetCoord(Ps[i].Position);
                int hash = SlimeConst.GetKey(gridPos);
                Hashes[i] = new int2(hash, i);
            }
        }

        // ── 3. Build lookup table ──

        [BurstCompile]
        public struct BuildLutJob : IJob
        {
            [ReadOnly] public NativeArray<int2> Hashes;
            public NativeHashMap<int, int2> Lut;

            public void Execute()
            {
                int currentKey = Hashes[0].x;
                int start = 0;
                for (int i = 1; i < Hashes.Length; i++)
                {
                    if (Hashes[i].x == currentKey)
                        continue;

                    Lut.TryAdd(currentKey, new int2(start, i));
                    currentKey = Hashes[i].x;
                    start = i;
                }
                Lut.TryAdd(currentKey, new int2(start, Hashes.Length));
            }
        }

        // ── 4. Shuffle into cell-grouped order ──

        [BurstCompile]
        public struct ShuffleJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int2> Hashes;
            [ReadOnly] public NativeArray<SlimeParticle> PsRaw;
            [ReadOnly] public NativeArray<SlimeParticle> PsNew;
            [ReadOnly] public NativeArray<float3> Velocity;
            [ReadOnly] public NativeArray<float3> RestOffsets;
            [ReadOnly] public NativeArray<float3> ShapeRestLocalPositions;
            [ReadOnly] public NativeArray<float3> ShapeRestMorphFrom;
            [ReadOnly] public NativeArray<float3> ShapeRestMorphTo;

            [WriteOnly] public NativeArray<float3> PosOld;
            [WriteOnly] public NativeArray<float3> PosPredict;
            [WriteOnly] public NativeArray<float3> VelocityOut;
            [WriteOnly] public NativeArray<float3> RestOffsetsOut;
            [WriteOnly] public NativeArray<float3> ShapeRestLocalPositionsOut;
            [WriteOnly] public NativeArray<float3> ShapeRestMorphFromOut;
            [WriteOnly] public NativeArray<float3> ShapeRestMorphToOut;

            public void Execute(int i)
            {
                int id = Hashes[i].y;
                PosPredict[i] = PsNew[id].Position;
                PosOld[i] = PsRaw[id].Position;
                VelocityOut[i] = Velocity[id];
                RestOffsetsOut[i] = RestOffsets[id];
                ShapeRestLocalPositionsOut[i] = ShapeRestLocalPositions[id];
                ShapeRestMorphFromOut[i] = ShapeRestMorphFrom[id];
                ShapeRestMorphToOut[i] = ShapeRestMorphTo[id];
            }
        }

        // ── 5. Compute density + lambda ──

        [BurstCompile]
        public struct ComputeLambdaJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [WriteOnly] public NativeArray<float> Lambda;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = SlimeConst.GetCoord(pos);

                float rho = 0f;
                float3 grad_i = new float3(0f, 0f, 0f);
                float sigmaGrad = 0f;

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = SlimeConst.GetKey(coord + new int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;

                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j) continue;

                        float3 dir = pos - PosPredict[j];
                        float r2 = lengthsq(dir);
                        if (r2 > SlimeConst.H2) continue;

                        float r = sqrt(r2);
                        rho += SmoothingKernelPoly6(r2) / SlimeConst.TargetDensity;
                        float3 grad_j = DerivativeSpikyPow3(r) / SlimeConst.TargetDensity *
                                        normalizesafe(dir, new float3(0f, 0f, 0f));
                        sigmaGrad += lengthsq(grad_j);
                        grad_i += grad_j;
                    }
                }

                sigmaGrad += dot(grad_i, grad_i);
                float c = max(-0.2f, rho / SlimeConst.TargetDensity - 1f);
                Lambda[i] = -c / (sigmaGrad + 1e-5f);
            }
        }

        // ── 6. Compute position corrections ──

        [BurstCompile]
        public struct ComputeDeltaPosJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<float> Lambda;
            [WriteOnly] public NativeArray<SlimeParticle> PsNew;

            public void Execute(int i)
            {
                float3 position = PosPredict[i];
                float3 dp = new float3(0f, 0f, 0f);

                float lam = Lambda[i];
                int3 coord = SlimeConst.GetCoord(position);

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = SlimeConst.GetKey(coord + new int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;

                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j) continue;

                        float3 dir = position - PosPredict[j];
                        float r2 = dot(dir, dir);
                        if (r2 >= SlimeConst.H2) continue;

                        float r = sqrt(r2);
                        float3 wSpiky = SpikyKernelPow3(r) * normalizesafe(dir, new float3(0f, 0f, 0f));
                        float sCorr = SlimeConst.Scorr(r2);
                        dp += (lam + Lambda[j] + sCorr) * wSpiky;
                    }
                }

                dp /= SlimeConst.TargetDensity;

                PsNew[i] = new SlimeParticle
                {
                    Position = position - dp,
                    ID = 0,
                };
            }
        }

        // ── 7. Finalise: floor collision + velocity reconstruction + clamp ──

        [BurstCompile]
        public struct ApplyShapeTargetJob : IJobParallelFor
        {
            public NativeArray<SlimeParticle> Particles;
            [ReadOnly] public NativeArray<float3> RestLocalPositions;
            [ReadOnly] public NativeArray<float3> Center;
            [ReadOnly] public NativeArray<float> Sdf;
            public SlimeShapeSdfInfo SdfInfo;
            public float4x4 LocalToSimVector;
            public float4x4 SimToLocalVector;
            public float3 ShapeLocalCenter;
            public float Strength;
            public float Compliance;
            public float BoundaryStrength;
            public bool DisableBoundaryProjection;
            public float DeltaTime;

            public void Execute(int i)
            {
                if (Strength <= 0f || !RestLocalPositions.IsCreated || i >= RestLocalPositions.Length)
                    return;

                SlimeParticle particle = Particles[i];
                float3 center = Center.IsCreated && Center.Length > 0 ? Center[0] : new float3(0f, 0f, 0f);
                if (!IsFinite(particle.Position) || !IsFinite(center))
                    return;

                float3 restOffset = TransformVector(LocalToSimVector, RestLocalPositions[i] - ShapeLocalCenter);
                float3 target = center + restOffset;
                if (!IsFinite(target))
                    return;

                float alpha = Compliance / max(DeltaTime * DeltaTime, 1e-6f);
                float weight = saturate(Strength / (1f + alpha));
                particle.Position = lerp(particle.Position, target, weight);

                if (!DisableBoundaryProjection && Sdf.IsCreated && Sdf.Length > 0 && BoundaryStrength > 0f)
                {
                    float3 local = ShapeLocalCenter + TransformVector(SimToLocalVector, particle.Position - center);
                    float distance = SlimeSdf.Sample(Sdf, SdfInfo, local);
                    if (distance > 0f)
                    {
                        float3 normalLocal = SlimeSdf.Gradient(Sdf, SdfInfo, local);
                        local -= normalLocal * distance * saturate(BoundaryStrength);
                        float3 projected = center + TransformVector(LocalToSimVector, local - ShapeLocalCenter);
                        if (IsFinite(projected))
                            particle.Position = projected;
                    }
                }

                Particles[i] = particle;
            }

            static float3 TransformVector(float4x4 matrix, float3 vector)
            {
                float4 v = mul(matrix, new float4(vector, 0f));
                return v.xyz;
            }

            static bool IsFinite(float value)
            {
                return !isnan(value) && isfinite(value);
            }

            static bool IsFinite(float3 value)
            {
                return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            }
        }

        [BurstCompile]
        public struct UpdateJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<SlimeBoxCollider> Colliders;
            [ReadOnly] public NativeArray<float3> PosOld;
            [WriteOnly] public NativeArray<float3> Velocity;
            public NativeArray<SlimeParticle> Ps;

            public void Execute(int i)
            {
                SlimeParticle p = Ps[i];
                float3 oldPos = PosOld[i];
                if (!IsFinite(p.Position))
                {
                    p.Position = oldPos;
                    if (!IsFinite(p.Position))
                        p.Position = new float3(0f, 1f, 0f);
                }

                // Floor: keep particles above y = 1 (sim space) = y = 0.1 (world)
                p.Position.y = max(1f, p.Position.y);

                // Two-pass collision so a single-axis push doesn't leave the particle
                // inside another collider or skip a thin wall.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int c = 0; c < Colliders.Length; c++)
                    {
                        SlimeBoxCollider box = Colliders[c];
                        float3 boxMin = box.Center - box.Extent;
                        float3 boxMax = box.Center + box.Extent;

                        // ── Discrete check: particle is currently inside AABB ──
                        float3 toCenter = p.Position - box.Center;
                        float3 vec = abs(toCenter);
                        bool inside = all(vec < box.Extent);

                        if (inside)
                        {
                            float3 remain = box.Extent - vec;
                            bool3 pushAxis = new bool3(false, false, false);
                            int axis = 0;
                            if (remain.y < remain[axis]) axis = 1;
                            if (remain.z < remain[axis]) axis = 2;
                            pushAxis[axis] = true;
                            p.Position = select(p.Position, box.Center + sign(toCenter) * box.Extent, pushAxis);
                            continue;
                        }

                        // ── CCD: segment–AABB intersection (prevents tunneling) ──
                        float3 disp = p.Position - oldPos;
                        float dist = length(disp);
                        if (dist < 1e-6f)
                            continue;

                        float3 dir = disp / dist; // unit direction
                        float3 invDir = select(1f / dir, 1e10f, abs(dir) < 1e-10f);

                        float3 t0 = (boxMin - oldPos) * invDir;
                        float3 t1 = (boxMax - oldPos) * invDir;
                        float3 tNear = min(t0, t1);
                        float3 tFar = max(t0, t1);
                        float tEntry = max(max(tNear.x, tNear.y), tNear.z);
                        float tExit = min(min(tFar.x, tFar.y), tFar.z);

                        if (tEntry <= tExit && tEntry >= 0f && tEntry < dist)
                        {
                            // Hit — push particle just before entry point
                            p.Position = oldPos + dir * max(0f, tEntry - 1e-3f);
                        }
                    }
                }

                float3 vel = (p.Position - PosOld[i]) / SlimeConst.DeltaTime;
                // Clamp velocity to prevent explosions
                float speed = length(vel);
                Velocity[i] = IsFinite(speed) ? min(30f, speed) * normalizesafe(vel) : new float3(0f, 0f, 0f);

                Ps[i] = p;
            }

            static bool IsFinite(float value)
            {
                return !isnan(value) && isfinite(value);
            }

            static bool IsFinite(float3 value)
            {
                return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            }
        }

        // ── 8. XSPH viscosity ──

        [BurstCompile]
        public struct ApplyViscosityJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<float3> VelocityR;
            [WriteOnly] public NativeArray<float3> VelocityW;
            public float ViscosityStrength;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = SlimeConst.GetCoord(pos);
                float3 viscosityForce = new float3(0f, 0f, 0f);
                float3 vel = VelocityR[i];

                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = SlimeConst.GetKey(coord + new int3(dx, dy, dz));
                    if (!Lut.ContainsKey(key)) continue;

                    int2 range = Lut[key];
                    for (int j = range.x; j < range.y; j++)
                    {
                        if (i == j) continue;

                        float3 dir = pos - PosPredict[j];
                        float r2 = lengthsq(dir);
                        if (r2 > SlimeConst.H2) continue;

                        viscosityForce += (VelocityR[j] - vel) * SmoothingKernelPoly6(r2);
                    }
                }

                VelocityW[i] = vel + viscosityForce / SlimeConst.TargetDensity *
                               ViscosityStrength * SlimeConst.DeltaTime;
            }
        }

        // ── Convenience statics (avoid Burst / static-readonly issues) ──

        static float SmoothingKernelPoly6(float r2)
        {
            if (r2 < SlimeConst.H2)
            {
                float v = SlimeConst.H2 - r2;
                return v * v * v * SlimeConst.Poly6Norm;
            }
            return 0f;
        }

        static float DerivativeSpikyPow3(float r)
        {
            if (r <= SlimeConst.H)
            {
                float v = SlimeConst.H - r;
                return -v * v * 3f * SlimeConst.SpikyKernelNorm;
            }
            return 0f;
        }

        static float SpikyKernelPow3(float r)
        {
            if (r < SlimeConst.H)
            {
                float v = SlimeConst.H - r;
                return v * v * v * SlimeConst.SpikyKernelNorm;
            }
            return 0f;
        }
    }
}
