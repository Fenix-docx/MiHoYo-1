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

            /// <summary>Group currently under player control. Only this group receives
            /// velocity-follow (movement). Other groups self-cohere but are not driven.</summary>
            public int ControlledGroupId;

            /// <summary>
            /// Per-group resolved centres of mass (sim-space). Used to compute the
            /// core-driven locomotion falloff per group: particles near their own
            /// group COM receive full velocity blend, periphery follows via PBF.
            /// </summary>
            [ReadOnly] public NativeArray<float3> GroupCentres; // length MaxGroups
            [ReadOnly] public NativeArray<int> GroupCounts;     // length MaxGroups
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

                    // Core-driven locomotion weight, evaluated against the particle's
                    // OWN group COM (not a global blob centre) so a split-off group's
                    // periphery is never driven by the main body's motion.
                    float3 groupCom = controllerCenter;
                    if (GroupCentres.IsCreated && (uint)p.ID < (uint)GroupCentres.Length && GroupCounts[p.ID] > 0)
                        groupCom = GroupCentres[p.ID] / GroupCounts[p.ID];
                    float distToGroupCom = length(p.Position - groupCom);
                    float coreWeight = MoveCoreRadius <= 0f ? 1f :
                        saturate(1f - distToGroupCom / max(MoveCoreRadius, 0.001f));

                    // Centre-attraction (cohesion) acts on every group toward its own
                    // controller centre — split-off slime stays self-cohesive.
                    if (len < controllerRadius)
                    {
                        velocity = lerp(controllerVelocity, velocity,
                            lerp(1f, len * 0.1f, controllerConcentration * 0.002f));
                        velocity += controllerConcentration * SlimeConst.DeltaTime *
                                    min(1f, len) * normalizesafe(toCenter);
                    }

                    // Velocity-follow (locomotion) is restricted to the controlled
                    // group. Split-off groups have controllerVelocity = 0 and are
                    // additionally gated out here so no motion bleeds into them.
                    if (p.ID == ControlledGroupId &&
                        VelocityFollowGain > 0f && lengthsq(controllerVelocity) > 1e-6f)
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
            [ReadOnly] public NativeArray<float3> InitialRestOffsets;
            [ReadOnly] public NativeArray<float3> ShapeRestLocalPositions;
            [ReadOnly] public NativeArray<float3> ShapeRestMorphFrom;
            [ReadOnly] public NativeArray<float3> ShapeRestMorphTo;

            [WriteOnly] public NativeArray<float3> PosOld;
            [WriteOnly] public NativeArray<float3> PosPredict;
            [WriteOnly] public NativeArray<float3> VelocityOut;
            [WriteOnly] public NativeArray<float3> RestOffsetsOut;
            [WriteOnly] public NativeArray<float3> InitialRestOffsetsOut;
            [WriteOnly] public NativeArray<float3> ShapeRestLocalPositionsOut;
            [WriteOnly] public NativeArray<float3> ShapeRestMorphFromOut;
            [WriteOnly] public NativeArray<float3> ShapeRestMorphToOut;
            [WriteOnly] public NativeArray<int> IdsOut; // shuffled particle group IDs

            public void Execute(int i)
            {
                int id = Hashes[i].y;
                PosPredict[i] = PsNew[id].Position;
                PosOld[i] = PsRaw[id].Position;
                VelocityOut[i] = Velocity[id];
                RestOffsetsOut[i] = RestOffsets[id];
                if (InitialRestOffsets.IsCreated && InitialRestOffsetsOut.IsCreated)
                    InitialRestOffsetsOut[i] = InitialRestOffsets[id];
                ShapeRestLocalPositionsOut[i] = ShapeRestLocalPositions[id];
                ShapeRestMorphFromOut[i] = ShapeRestMorphFrom[id];
                ShapeRestMorphToOut[i] = ShapeRestMorphTo[id];
                IdsOut[i] = PsNew[id].ID;
            }
        }

        // ── 5. Compute density + lambda ──

        [BurstCompile]
        public struct ComputeLambdaJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<int> Ids; // shuffled group IDs (group-aware PBF)
            [WriteOnly] public NativeArray<float> Lambda;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = SlimeConst.GetCoord(pos);
                int id = Ids[i];

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
                        // Group-aware PBF: a split body is an independent fluid —
                        // particles only feel density/pressure from their OWN group,
                        // so separate bodies neither push nor drag each other.
                        if (Ids[j] != id) continue;

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
            [ReadOnly] public NativeArray<int> Ids; // shuffled group IDs
            [WriteOnly] public NativeArray<SlimeParticle> PsNew;

            public void Execute(int i)
            {
                float3 position = PosPredict[i];
                float3 dp = new float3(0f, 0f, 0f);

                float lam = Lambda[i];
                int id = Ids[i];
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
                        // Group-aware PBF: skip other groups (independent fluids).
                        if (Ids[j] != id) continue;

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

                // Preserve the particle's group ID through the PBF solve — it was
                // previously hard-coded to 0, which silently erased split/absorb
                // group assignment every sub-step.
                PsNew[i] = new SlimeParticle
                {
                    Position = position - dp,
                    ID = Ids[i],
                };
            }
        }

        // ── 7. Finalise: floor collision + velocity reconstruction + clamp ──

        [BurstCompile]
        public struct ApplyShapeTargetJob : IJobParallelFor
        {
            public NativeArray<SlimeParticle> Particles;
            [ReadOnly] public NativeArray<float3> RestLocalPositions;
            [ReadOnly] public NativeArray<float3> Centres; // length MaxGroups — running sums
            [ReadOnly] public NativeArray<int> Counts;     // length MaxGroups
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
                int g = particle.ID;
                float3 center = new float3(0f, 0f, 0f);
                if (Centres.IsCreated && (uint)g < (uint)Centres.Length && Counts[g] > 0)
                    center = Centres[g] / Counts[g];
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
            [ReadOnly] public NativeArray<SlimeColliderOctreeNode> ColliderOctreeNodes;
            [ReadOnly] public NativeArray<int> ColliderOctreeIndices;
            [ReadOnly] public NativeArray<float3> PosOld;
            [WriteOnly] public NativeArray<float3> Velocity;
            public NativeArray<SlimeParticle> Ps;
            public bool EnableFloor;
            public bool UseColliderOctree;
            public float FloorY;
            public bool RecordCollisionEvents;
            public NativeList<SlimeCollisionEvent>.ParallelWriter CollisionEvents;
            public NativeParallelHashSet<ulong>.ParallelWriter CollisionEventSet;
            public float MinCollisionEventSpeed;

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

                if (EnableFloor)
                    p.Position.y = max(FloorY, p.Position.y);

                // Two-pass collision so a single-axis push doesn't leave the particle
                // inside another collider or skip a thin wall.
                for (int pass = 0; pass < 2; pass++)
                {
                    if (UseColliderOctree
                        && ColliderOctreeNodes.IsCreated
                        && ColliderOctreeNodes.Length > 0
                        && ColliderOctreeIndices.IsCreated)
                        ResolveCollisionsOctree(ref p, oldPos, i, pass);
                    else
                        ResolveCollisionsFull(ref p, oldPos, i, pass);
                }

                float3 vel = (p.Position - PosOld[i]) / SlimeConst.DeltaTime;
                // Clamp velocity to prevent explosions
                float speed = length(vel);
                Velocity[i] = IsFinite(speed) ? min(30f, speed) * normalizesafe(vel) : new float3(0f, 0f, 0f);

                Ps[i] = p;
            }

            void ResolveCollisionsFull(ref SlimeParticle particle, float3 oldPos, int particleIndex, int pass)
            {
                if (!Colliders.IsCreated)
                    return;

                for (int c = 0; c < Colliders.Length; c++)
                    ResolveColliderIndex(ref particle, oldPos, c, particleIndex, pass);
            }

            void ResolveCollisionsOctree(ref SlimeParticle particle, float3 oldPos, int particleIndex, int pass)
            {
                float3 queryMin = min(oldPos, particle.Position) - new float3(0.01f);
                float3 queryMax = max(oldPos, particle.Position) + new float3(0.01f);

                FixedList4096Bytes<int> stack = default;
                stack.Add(0);

                while (stack.Length > 0)
                {
                    int nodeIndex = stack[stack.Length - 1];
                    stack.RemoveAt(stack.Length - 1);

                    if ((uint)nodeIndex >= (uint)ColliderOctreeNodes.Length)
                        continue;

                    SlimeColliderOctreeNode node = ColliderOctreeNodes[nodeIndex];
                    if (!IntersectsNode(queryMin, queryMax, node))
                        continue;

                    for (int i = 0; i < node.ColliderCount; i++)
                    {
                        int indexOffset = node.FirstCollider + i;
                        if ((uint)indexOffset >= (uint)ColliderOctreeIndices.Length)
                            continue;

                        ResolveColliderIndex(ref particle, oldPos, ColliderOctreeIndices[indexOffset], particleIndex, pass);
                    }

                    if (node.FirstChild < 0)
                        continue;

                    for (int childOffset = 0; childOffset < 8; childOffset++)
                    {
                        int childIndex = node.FirstChild + childOffset;
                        if ((uint)childIndex >= (uint)ColliderOctreeNodes.Length)
                            continue;

                        if (!IntersectsNode(queryMin, queryMax, ColliderOctreeNodes[childIndex]))
                            continue;

                        if (stack.Length >= stack.Capacity)
                        {
                            ResolveCollisionsFull(ref particle, oldPos, particleIndex, pass);
                            return;
                        }

                        stack.Add(childIndex);
                    }
                }
            }

            void ResolveColliderIndex(ref SlimeParticle particle, float3 oldPos, int colliderIndex, int particleIndex, int pass)
            {
                if (!Colliders.IsCreated || (uint)colliderIndex >= (uint)Colliders.Length)
                    return;

                SlimeBoxCollider box = Colliders[colliderIndex];
                ResolveAgainstCollider(ref particle, oldPos, box, out SlimeCollisionResult result);
                if (RecordCollisionEvents && pass == 0 && result.Hit)
                    TryRecordCollisionEvent(particleIndex, colliderIndex, box, result);
            }

            void TryRecordCollisionEvent(
                int particleIndex,
                int colliderIndex,
                SlimeBoxCollider box,
                SlimeCollisionResult result)
            {
                if (result.ImpactSpeed < max(0f, MinCollisionEventSpeed))
                    return;

                ulong key = CollisionEventKey(particleIndex, colliderIndex);
                if (!CollisionEventSet.Add(key))
                    return;

                CollisionEvents.AddNoResize(ComputeCollisionEvent(particleIndex, colliderIndex, box, result));
            }

            static SlimeCollisionEvent ComputeCollisionEvent(
                int particleIndex,
                int colliderIndex,
                SlimeBoxCollider box,
                SlimeCollisionResult result)
            {
                float3 axisX = SafeAxis(box.AxisX, new float3(1f, 0f, 0f));
                float3 axisY = SafeAxis(box.AxisY, new float3(0f, 1f, 0f));
                float3 axisZ = SafeAxis(box.AxisZ, new float3(0f, 0f, 1f));
                float3 normal = normalizesafe(
                    ObbLocalVectorToWorld(result.NormalLocal, axisX, axisY, axisZ),
                    new float3(0f, 1f, 0f));

                return new SlimeCollisionEvent
                {
                    ParticleIndex = particleIndex,
                    ColliderIndex = colliderIndex,
                    ContactPoint = ObbLocalToWorld(result.ContactPointLocal, box.Center, axisX, axisY, axisZ),
                    ContactNormal = normal,
                    PenetrationDepth = max(0f, result.PenetrationDepth),
                    ImpactSpeed = max(0f, result.ImpactSpeed),
                    CollisionType = result.CollisionType,
                };
            }

            static ulong CollisionEventKey(int particleIndex, int colliderIndex)
            {
                return ((ulong)(uint)particleIndex << 32) | (uint)colliderIndex;
            }

            static void ResolveAgainstCollider(
                ref SlimeParticle particle,
                float3 oldPos,
                SlimeBoxCollider box,
                out SlimeCollisionResult result)
            {
                result = default;
                float3 axisX = SafeAxis(box.AxisX, new float3(1f, 0f, 0f));
                float3 axisY = SafeAxis(box.AxisY, new float3(0f, 1f, 0f));
                float3 axisZ = SafeAxis(box.AxisZ, new float3(0f, 0f, 1f));
                float3 extent = max(box.Extent, new float3(0.0001f));
                float3 preResolvePosition = particle.Position;

                // Discrete check: particle is currently inside the OBB.
                float3 local = WorldToObbLocal(particle.Position, box.Center, axisX, axisY, axisZ);
                bool inside = all(abs(local) < extent);

                if (inside)
                {
                    float3 remain = extent - abs(local);
                    int axis = 0;
                    if (remain.y < remain[axis]) axis = 1;
                    if (remain.z < remain[axis]) axis = 2;

                    float sign = FaceSign(local[axis]);
                    float3 normalLocal = AxisNormalLocal(axis, sign);
                    float3 contactLocal = local;
                    contactLocal[axis] = sign * extent[axis];
                    float3 normal = normalizesafe(
                        ObbLocalVectorToWorld(normalLocal, axisX, axisY, axisZ),
                        new float3(0f, 1f, 0f));

                    result = new SlimeCollisionResult
                    {
                        Hit = true,
                        ContactPointLocal = contactLocal,
                        NormalLocal = normalLocal,
                        PenetrationDepth = remain[axis],
                        ImpactSpeed = ComputeImpactSpeed(oldPos, preResolvePosition, normal),
                        CollisionType = 0,
                    };
                    particle.Position = ObbLocalToWorld(contactLocal, box.Center, axisX, axisY, axisZ);
                    return;
                }

                // CCD: segment-box intersection in OBB-local space.
                float3 localOld = WorldToObbLocal(oldPos, box.Center, axisX, axisY, axisZ);
                float3 localNow = local;
                float3 disp = localNow - localOld;
                float dist = length(disp);
                if (dist < 1e-6f)
                    return;

                float3 dir = disp / dist;
                float3 invDir = select(1f / dir, 1e10f, abs(dir) < 1e-10f);

                float3 t0 = (-extent - localOld) * invDir;
                float3 t1 = (extent - localOld) * invDir;
                float3 tNear = min(t0, t1);
                float3 tFar = max(t0, t1);
                float tEntry = max(max(tNear.x, tNear.y), tNear.z);
                float tExit = min(min(tFar.x, tFar.y), tFar.z);

                if (tEntry <= tExit && tEntry >= 0f && tEntry < dist)
                {
                    // Hit: push particle just before entry point.
                    int axis = 0;
                    if (tNear.y > tNear[axis]) axis = 1;
                    if (tNear.z > tNear[axis]) axis = 2;

                    float3 normalLocal = AxisNormalLocal(axis, -FaceSign(dir[axis]));
                    float3 contactLocal = localOld + dir * tEntry;
                    float3 localHit = localOld + dir * max(0f, tEntry - 1e-3f);
                    float3 normal = normalizesafe(
                        ObbLocalVectorToWorld(normalLocal, axisX, axisY, axisZ),
                        new float3(0f, 1f, 0f));

                    result = new SlimeCollisionResult
                    {
                        Hit = true,
                        ContactPointLocal = contactLocal,
                        NormalLocal = normalLocal,
                        PenetrationDepth = max(0f, dist - tEntry),
                        ImpactSpeed = ComputeImpactSpeed(oldPos, preResolvePosition, normal),
                        CollisionType = 1,
                    };
                    particle.Position = ObbLocalToWorld(localHit, box.Center, axisX, axisY, axisZ);
                }
            }

            struct SlimeCollisionResult
            {
                public bool Hit;
                public float3 ContactPointLocal;
                public float3 NormalLocal;
                public float PenetrationDepth;
                public float ImpactSpeed;
                public byte CollisionType;
            }

            static float3 AxisNormalLocal(int axis, float sign)
            {
                float3 normal = new float3(0f, 0f, 0f);
                normal[axis] = sign;
                return normal;
            }

            static float ComputeImpactSpeed(float3 oldPos, float3 currentPos, float3 normal)
            {
                float3 velocity = (currentPos - oldPos) / SlimeConst.DeltaTime;
                float speed = -dot(velocity, normalizesafe(normal, new float3(0f, 1f, 0f)));
                return IsFinite(speed) ? max(0f, speed) : 0f;
            }

            static bool IsFinite(float value)
            {
                return !isnan(value) && isfinite(value);
            }

            static bool IsFinite(float3 value)
            {
                return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            }

            static bool IntersectsNode(float3 queryMin, float3 queryMax, SlimeColliderOctreeNode node)
            {
                float3 nodeHalf = new float3(max(0f, node.HalfSize));
                float3 nodeMin = node.Center - nodeHalf;
                float3 nodeMax = node.Center + nodeHalf;
                return all(queryMax >= nodeMin) && all(queryMin <= nodeMax);
            }
        }

        static float3 SafeAxis(float3 axis, float3 fallback)
        {
            bool finite = !isnan(axis.x) && isfinite(axis.x) &&
                          !isnan(axis.y) && isfinite(axis.y) &&
                          !isnan(axis.z) && isfinite(axis.z);
            return lengthsq(axis) > 1e-8f && finite ? normalize(axis) : fallback;
        }

        static float FaceSign(float value)
        {
            return value < 0f ? -1f : 1f;
        }

        static float3 WorldToObbLocal(float3 position, float3 center, float3 axisX, float3 axisY, float3 axisZ)
        {
            float3 offset = position - center;
            return new float3(dot(offset, axisX), dot(offset, axisY), dot(offset, axisZ));
        }

        static float3 ObbLocalToWorld(float3 local, float3 center, float3 axisX, float3 axisY, float3 axisZ)
        {
            return center + axisX * local.x + axisY * local.y + axisZ * local.z;
        }

        static float3 ObbLocalVectorToWorld(float3 local, float3 axisX, float3 axisY, float3 axisZ)
        {
            return axisX * local.x + axisY * local.y + axisZ * local.z;
        }

        // ── 8. XSPH viscosity ──

        [BurstCompile]
        public struct ApplyViscosityJob : IJobParallelFor
        {
            [ReadOnly] public NativeHashMap<int, int2> Lut;
            [ReadOnly] public NativeArray<float3> PosPredict;
            [ReadOnly] public NativeArray<float3> VelocityR;
            [ReadOnly] public NativeArray<int> Ids; // shuffled group IDs (group-aware)
            [WriteOnly] public NativeArray<float3> VelocityW;
            public float ViscosityStrength;

            public void Execute(int i)
            {
                float3 pos = PosPredict[i];
                int3 coord = SlimeConst.GetCoord(pos);
                int id = Ids[i];
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
                        // Group-aware: viscosity only blends velocities within a
                        // body, so a split body's motion stays independent.
                        if (Ids[j] != id) continue;

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
