using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Slime
{
    /// <summary>
    /// Explicit group (split / absorb / control-switch) manager.
    ///
    /// Unlike the reference CCL approach (which infers groups from the density
    /// field every frame), this treats <see cref="SlimeParticle.ID"/> as the
    /// authoritative group label. Splitting reassigns IDs deterministically;
    /// absorbing merges IDs on contact; control-switching just changes which
    /// group the motor drives. This avoids frame-to-frame ID remapping entirely.
    ///
    /// All particle/velocity/rest arrays passed in must be in the same order
    /// (the simulator keeps <c>_particles</c>, <c>_restOffsets</c> and
    /// <c>_velocityBuffer</c> aligned after a simulation step).
    /// </summary>
    public class SlimeGroupManager
    {
        public const int MaxGroups = 8;

        /// <summary>Number of group slots ever allocated (alive or dead).</summary>
        public int SlotCount { get; private set; }

        /// <summary>Currently alive group count.</summary>
        public int AliveCount { get; private set; }

        /// <summary>Group currently under player control (receives motor drive).</summary>
        public int ControlledGroupId { get; private set; }

        readonly bool[] _alive = new bool[MaxGroups];
        readonly float[] _concentration = new float[MaxGroups];
        readonly float[] _stiffness = new float[MaxGroups];
        readonly int[] _cohesionStep = new int[MaxGroups];
        readonly bool[,] _absorbReady = new bool[MaxGroups, MaxGroups];
        readonly List<int> _freeSlots = new List<int>(MaxGroups);

        // Initial (game-start) cohesion params. New split bodies always inherit
        // THESE, not the parent's current Q/E-adjusted value, so a split always
        // produces a coherent body even when the parent was in a diffuse state.
        float _initialConcentration;
        float _initialStiffness;
        int _initialCohesionStep;

        public bool CanSplit => AliveCount < MaxGroups;

        public SlimeGroupManager()
        {
            // Group 0 is the initial body; allocated by Init().
        }

        /// <summary>Allocate the initial single group covering the whole blob.</summary>
        public void Init(float concentration, float stiffness, int cohesionStep = 1)
        {
            _initialConcentration = concentration;
            _initialStiffness = stiffness;
            _initialCohesionStep = clamp(cohesionStep, 1, 3);
            SlotCount = 0;
            AliveCount = 0;
            ControlledGroupId = 0;
            for (int i = 0; i < MaxGroups; i++)
            {
                _alive[i] = false;
                _concentration[i] = 0f;
                _stiffness[i] = 0f;
                _cohesionStep[i] = 0;
                for (int j = 0; j < MaxGroups; j++)
                    _absorbReady[i, j] = true;
            }
            _freeSlots.Clear();
            AllocateSlot(concentration, stiffness, _initialCohesionStep);
        }

        int AllocateSlot(float concentration, float stiffness, int cohesionStep)
        {
            int slot;
            if (_freeSlots.Count > 0)
            {
                int last = _freeSlots.Count - 1;
                slot = _freeSlots[last];
                _freeSlots.RemoveAt(last);
            }
            else
            {
                slot = SlotCount;
                SlotCount++;
            }
            _alive[slot] = true;
            _concentration[slot] = concentration;
            _stiffness[slot] = stiffness;
            _cohesionStep[slot] = clamp(cohesionStep, 1, 3);
            AliveCount++;
            return slot;
        }

        public bool IsAlive(int groupId) => (uint)groupId < (uint)MaxGroups && _alive[groupId];

        public float GetConcentration(int groupId) =>
            (uint)groupId < (uint)MaxGroups ? _concentration[groupId] : 0f;

        public float GetStiffness(int groupId) =>
            (uint)groupId < (uint)MaxGroups ? _stiffness[groupId] : 0f;

        public int GetCohesionStep(int groupId) =>
            (uint)groupId < (uint)MaxGroups ? _cohesionStep[groupId] : 1;

        /// <summary>Update the controlled group's complete cohesion UI/sim state.</summary>
        public void SetControlledCohesionState(float concentration, float stiffness, int cohesionStep)
        {
            if ((uint)ControlledGroupId >= (uint)MaxGroups)
                return;

            _concentration[ControlledGroupId] = concentration;
            _stiffness[ControlledGroupId] = stiffness;
            _cohesionStep[ControlledGroupId] = clamp(cohesionStep, 1, 3);
        }

        /// <summary>Update the controlled group's concentration from the simulator field.</summary>
        public void SetControlledConcentration(float concentration) =>
            _concentration[ControlledGroupId] = concentration;

        /// <summary>Update the controlled group's shape-match stiffness from the simulator field.</summary>
        public void SetControlledStiffness(float stiffness) =>
            _stiffness[ControlledGroupId] = stiffness;

        public void SetControlledCohesionStep(int cohesionStep) =>
            _cohesionStep[ControlledGroupId] = clamp(cohesionStep, 1, 3);

        /// <summary>
        /// Split the controlled group. A fixed fraction of particles — those
        /// furthest along <paramref name="facingDir"/> — are peeled off into a
        /// new group and placed a <paramref name="separation"/> distance ahead.
        ///
        /// Design choices:
        /// • Control stays on the source (main) body. The split body is
        ///   non-controlled, so it self-coheres around its OWN COM and is never
        ///   yanked back toward the motor.
        /// • Separation is a one-time POSITION offset, not an ejection impulse.
        /// • The split body's velocity is zeroed so it sits still and independent.
        /// • The simulator rewrites each body's current rest offsets to a compact
        ///   local blob after splitting, then restores the canonical full-body
        ///   baseline after absorbing.
        /// • The new body inherits the INITIAL concentration/stiffness, not the
        ///   parent's current Q/E-adjusted value ("分裂不受 Q/E 影响").
        /// </summary>
        /// <returns>The new group id, or -1 if the split was rejected.</returns>
        public int Split(
            NativeArray<SlimeParticle> particles,
            NativeArray<float3> velocity,
            float3 facingDir,
            float splitFraction,
            float separation,
            float minParticlesToSplit)
        {
            if (!CanSplit) return -1;

            int src = ControlledGroupId;
            // Collect indices + projection of the source group.
            var indices = new List<int>(particles.Length);
            for (int i = 0; i < particles.Length; i++)
                if (particles[i].ID == src) indices.Add(i);

            if (indices.Count < minParticlesToSplit * 2f) return -1;

            int splitCount = max(1, (int)(indices.Count * splitFraction));
            if (indices.Count - splitCount < (int)minParticlesToSplit) return -1;

            // Sort by projection onto facing dir (descending): the front-most
            // particles become the new split-off body.
            facingDir = normalizesafe(facingDir, new float3(1f, 0f, 0f));
            indices.Sort((a, b) =>
                math.dot(particles[b].Position, facingDir)
                    .CompareTo(math.dot(particles[a].Position, facingDir)));

            int newGroup = AllocateSlot(_initialConcentration, _initialStiffness, _initialCohesionStep);

            for (int g = 0; g < SlotCount; g++)
            {
                if (g == newGroup || !_alive[g]) continue;
                SetAbsorbReady(newGroup, g, false);
            }

            for (int k = 0; k < splitCount; k++)
            {
                int i = indices[k];
                SlimeParticle p = particles[i];
                p.ID = newGroup;
                p.Position += facingDir * separation; // one-time separation, no impulse
                particles[i] = p;
                velocity[i] = new float3(0f, 0f, 0f); // independent body starts at rest
            }

            return newGroup;
        }

        /// <summary>
        /// Assign a complete compact local blob rest shape to one group. This
        /// avoids using a sliced subset of the original full-body rest shape after
        /// splitting, matching the reference project's per-component cohesion.
        /// </summary>
        public static void WriteCompactRestOffsets(
            NativeArray<float3> restOffsets,
            NativeArray<SlimeParticle> particles,
            int groupId,
            int totalParticleCount)
        {
            int count = 0;
            for (int i = 0; i < particles.Length; i++)
                if (particles[i].ID == groupId)
                    count++;

            if (count == 0)
                return;

            float baseRadius = SlimeConst.Width * 0.25f;
            float fraction = (float)count / max(1, totalParticleCount);
            float radius = max(SlimeConst.H * 0.5f, baseRadius * pow(fraction, 1f / 3f));
            float goldenAngle = PI * (3f - sqrt(5f));

            int k = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId)
                    continue;

                float t = (k + 0.5f) / count;
                float y = 1f - 2f * t;
                float r = sqrt(max(0f, 1f - y * y));
                float theta = goldenAngle * k;
                float shell = radius * pow(t, 1f / 3f);
                restOffsets[i] = new float3(cos(theta) * r, y, sin(theta) * r) * shell;
                k++;
            }

            RecenterRestOffsets(restOffsets, particles, groupId);
        }

        /// <summary>Restore a group's current rest offsets from the canonical baseline.</summary>
        public static void RestoreRestOffsets(
            NativeArray<float3> restOffsets,
            NativeArray<float3> canonicalRestOffsets,
            NativeArray<SlimeParticle> particles,
            int groupId)
        {
            if (!canonicalRestOffsets.IsCreated)
                return;

            int length = min(restOffsets.Length, canonicalRestOffsets.Length);
            for (int i = 0; i < length && i < particles.Length; i++)
            {
                if (particles[i].ID == groupId)
                    restOffsets[i] = canonicalRestOffsets[i];
            }
        }

        /// <summary>
        /// Subtract a group's average rest offset from each of its particles'
        /// rest offsets, so the group's rest offsets are centred on its own COM.
        /// Preserves the relative (original) arrangement; eliminates the constant
        /// shape-match force that appears when a group's rest offsets are relative
        /// to a different COM than its current one.
        /// </summary>
        public static void RecenterRestOffsets(
            NativeArray<float3> restOffsets,
            NativeArray<SlimeParticle> particles,
            int groupId)
        {
            float3 sum = new float3(0f, 0f, 0f);
            int count = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId) continue;
                sum += restOffsets[i];
                count++;
            }
            if (count == 0) return;
            float3 avg = sum / count;

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId) continue;
                restOffsets[i] = restOffsets[i] - avg;
            }
        }

        /// <summary>
        /// Compute a group's centre of mass and an approximate bounding radius
        /// (max distance from COM) for contact testing.
        /// </summary>
        public static void GroupBounds(
            NativeArray<SlimeParticle> particles, int groupId,
            out float3 com, out float radius, out int count)
        {
            com = new float3(0f, 0f, 0f);
            radius = 0f;
            count = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId) continue;
                com += particles[i].Position;
                count++;
            }
            if (count == 0) return;
            com /= count;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != groupId) continue;
                float d = length(particles[i].Position - com);
                if (d > radius) radius = d;
            }
        }

        /// <summary>
        /// Find an alive group whose body contacts the controlled group.
        /// Contact = COM distance ≤ (rControlled + rOther + slack). Returns -1
        /// if none. Only the controlled group triggers absorption — split bodies
        /// touching each other do not merge on their own.
        /// </summary>
        public int FindAbsorbTarget(
            NativeArray<SlimeParticle> particles,
            float contactSlack,
            float separationSlack = SlimeConst.H)
        {
            int ctrl = ControlledGroupId;
            GroupBounds(particles, ctrl, out float3 ctrlCom, out float ctrlR, out int ctrlCount);
            if (ctrlCount == 0) return -1;

            float bestDist = float.MaxValue;
            int best = -1;
            for (int g = 0; g < SlotCount; g++)
            {
                if (g == ctrl || !_alive[g]) continue;
                GroupBounds(particles, g, out float3 com, out float r, out int count);
                if (count == 0) continue;
                float d = length(com - ctrlCom);
                float threshold = ctrlR + r + contactSlack;

                if (!IsAbsorbReady(ctrl, g))
                {
                    if (d > threshold + max(0f, separationSlack))
                        SetAbsorbReady(ctrl, g, true);
                    else
                        continue;
                }

                if (d <= threshold && d < bestDist)
                {
                    bestDist = d;
                    best = g;
                }
            }
            return best;
        }

        bool IsAbsorbReady(int a, int b)
        {
            if ((uint)a >= (uint)MaxGroups || (uint)b >= (uint)MaxGroups)
                return true;
            return _absorbReady[a, b];
        }

        void SetAbsorbReady(int a, int b, bool ready)
        {
            if ((uint)a >= (uint)MaxGroups || (uint)b >= (uint)MaxGroups)
                return;
            _absorbReady[a, b] = ready;
            _absorbReady[b, a] = ready;
        }

        /// <summary>
        /// Merge <paramref name="absorbedGroupId"/> into the controlled group:
        /// reassign all its particles' IDs, blend their velocity toward the
        /// controlled group's average velocity (kills the merge jolt), and free
        /// the slot. Rest offsets stay canonical so repeated split/absorb cycles
        /// always return to the same initial shape baseline.
        /// </summary>
        public void Absorb(
            NativeArray<SlimeParticle> particles,
            NativeArray<float3> restOffsets,
            NativeArray<float3> velocity,
            int absorbedGroupId)
        {
            Absorb(particles, restOffsets, default, velocity, absorbedGroupId);
        }

        public void Absorb(
            NativeArray<SlimeParticle> particles,
            NativeArray<float3> restOffsets,
            NativeArray<float3> canonicalRestOffsets,
            NativeArray<float3> velocity,
            int absorbedGroupId)
        {
            // restOffsets is kept in the signature for existing callers. The
            // canonical rest baseline must not be rewritten during absorb.
            int ctrl = ControlledGroupId;
            if (absorbedGroupId == ctrl || (uint)absorbedGroupId >= (uint)MaxGroups || !_alive[absorbedGroupId])
                return;

            // Average velocity of the controlled group — the absorbed particles
            // blend toward this so they don't slam into the body and deform it.
            float3 ctrlVel = new float3(0f, 0f, 0f);
            int ctrlCount = 0;
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != ctrl) continue;
                ctrlVel += velocity[i];
                ctrlCount++;
            }
            if (ctrlCount > 0)
                ctrlVel /= ctrlCount;

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].ID != absorbedGroupId) continue;
                SlimeParticle p = particles[i];
                p.ID = ctrl;
                particles[i] = p;
                velocity[i] = lerp(velocity[i], ctrlVel, 0.5f);
            }

            _alive[absorbedGroupId] = false;
            _concentration[absorbedGroupId] = 0f;
            _stiffness[absorbedGroupId] = 0f;
            _cohesionStep[absorbedGroupId] = 0;
            _freeSlots.Add(absorbedGroupId);
            AliveCount--;
            for (int g = 0; g < MaxGroups; g++)
                SetAbsorbReady(absorbedGroupId, g, true);

            if (AliveCount == 1)
                RestoreRestOffsets(restOffsets, canonicalRestOffsets, particles, ctrl);
            else
                WriteCompactRestOffsets(restOffsets, particles, ctrl, particles.Length);
        }

        /// <summary>
        /// Cycle control to the next alive group. Returns the new controlled id.
        /// </summary>
        public int SwitchControl()
        {
            if (AliveCount <= 1) return ControlledGroupId;
            for (int step = 1; step <= MaxGroups; step++)
            {
                int g = (ControlledGroupId + step) % MaxGroups;
                if (_alive[g])
                {
                    ControlledGroupId = g;
                    return g;
                }
            }
            return ControlledGroupId;
        }
    }
}
