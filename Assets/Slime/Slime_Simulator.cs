using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace Slime
{
    /// <summary>
    /// MonoBehaviour entry point for the PBF slime simulation.
    /// Allocates buffers, spawns particles, drives the fixed-step simulation loop,
    /// and hands data off to the renderer.
    /// </summary>
    [RequireComponent(typeof(SlimeRenderer))]
    public class SlimeSimulator : MonoBehaviour
    {
        [Header("Simulation")]
        [Range(-20f, 10f)] public float gravity = -5f;
        [Range(0f, 50f)] public float viscosityStrength = 1f;
        [Range(0.1f, 50f)] public float concentration = 10f;

        [Header("Phase 2 – Shape Matching")]
        public bool enableShapeMatch = false;
        [Range(0f, 1f)] public float shapeStiffness = 0.15f;

        [Header("Static Mesh Shape Target")]
        public bool enableMeshShapeTarget = false;
        public GameObject shapeTargetObject;
        public Mesh shapeTargetMesh;
        public Transform shapeTargetTransform;
        [Range(0f, 1f)] public float shapeTargetStrength = 0.12f;
        [Min(0f)] public float shapeTargetCompliance = 0.0005f;
        [Range(0f, 1f)] public float shapeBoundaryStrength = 0.85f;
        [Range(6, 48)] public int shapeSdfResolution = 24;
        [Min(0f)] public float shapeMorphTime = 0.35f;

        [Header("Phase 3 – Surface")]
        public bool enableSurface = true;
        [Range(0.1f, 5f)] public float isoLevel = 1.0f;

        [Header("Phase 4 – Interaction")]
        [Tooltip("Velocity source the slime follows (e.g. a SlimeControllerMotor's Rigidbody). Auto-found if left empty.")]
        public Rigidbody controllerBody;
        [Range(0f, 1f)] public float velocityFollowGain = 0.5f;

        [Header("Core-driven locomotion")]
        [Tooltip("Only particles within this sim-space radius of the blob COM receive the controller pull force. 0 = apply to all (legacy).")]
        [Range(0f, 5f)] public float moveCoreRadius = 1.5f;

        [Header("Keyboard")]
        public KeyCode keyDecreaseBoth = KeyCode.LeftBracket;
        public KeyCode keyIncreaseBoth = KeyCode.RightBracket;
        [Range(0.1f, 20f)] public float concentrationStep = 4f;
        [Range(0.01f, 0.2f)] public float stiffnessStep = 0.1f;

        [Header("Debug")]
        public bool simulateOnStart = true;

        // ── Native buffers ──
        NativeArray<SlimeParticle> _particles;
        NativeArray<SlimeParticle> _particlesTemp;
        NativeArray<float3> _posPredict;
        NativeArray<float3> _posOld;
        NativeArray<float> _lambdaBuffer;
        NativeArray<float3> _velocityBuffer;
        NativeArray<float3> _velocityTempBuffer;
        NativeHashMap<int, int2> _lut;
        NativeArray<int2> _hashes;
        NativeList<SlimeController> _controllerBuffer;
        NativeArray<float3> _restOffsets;         // original order (identity-keyed)
        NativeArray<float3> _restOffsetsShuffled; // cell-grouped order (matches _particles after shuffle)
        NativeArray<float3> _shapeRestLocalPositions;
        NativeArray<float3> _shapeRestLocalPositionsShuffled;
        NativeArray<float3> _shapeRestMorphFrom;
        NativeArray<float3> _shapeRestMorphTo;
        NativeArray<SlimeBoxCollider> _colliderBuffer;
        SlimeShapeTarget _shapeTarget;
        Mesh _activeShapeTargetMesh;
        Transform _activeShapeTargetTransform;
        int _activeShapeSdfResolution;
        float _shapeRestMorphElapsed;
        float _shapeRestMorphDuration;
        bool _shapeRestMorphActive;
        float3 _shapeLocalCenter;
        Vector3 _lastTransformPosition;
        bool _hasLastTransformPosition;
        float3 _lastCom; // cached COM for core-driven locomotion

        int _batchCount = 64;
        SlimeRenderer _renderer;
        SlimeSurfaceBuilder _surfaceBuilder;
        bool _initialised;
        bool isQ = true;
        void Start()
        {
            if (simulateOnStart)
                Initialise();
        }

        void Update()
        {
            if (!_initialised) return;
            

            if (Input.GetKeyDown(KeyCode.Q)&& isQ)
            {
                concentration = max(0.1f, concentration - concentrationStep);
                shapeStiffness = max(0f, shapeStiffness - stiffnessStep);
                isQ = false;
            }
            if (Input.GetKeyDown(KeyCode.E)&& !isQ)
            {
                concentration = min(50f, concentration + concentrationStep);
                shapeStiffness = min(1f, shapeStiffness + stiffnessStep);
                isQ = true;
            }
        }

        void FixedUpdate()
        {
            if (!_initialised) return;

            SyncTransformTranslation();
            RebuildShapeTargetIfNeeded();
            UpdateShapeTargetMorph(Time.fixedDeltaTime);

            // Two simulation sub-steps per physics tick (reference pattern)
            for (int i = 0; i < 2; i++)
                Simulate();

            Control();

            if (enableSurface && _surfaceBuilder)
                _surfaceBuilder.BuildSurface(_particles, isoLevel);

            _renderer?.SetParticles(_particles);
        }

        void OnDestroy()
        {
            Dispose();
        }

        #region  Initialisation ──

        public void Initialise()
        {
            Dispose();

            int n = SlimeConst.Num;
            _particles = new NativeArray<SlimeParticle>(n, Allocator.Persistent);
            _particlesTemp = new NativeArray<SlimeParticle>(n, Allocator.Persistent);
            _posPredict = new NativeArray<float3>(n, Allocator.Persistent);
            _posOld = new NativeArray<float3>(n, Allocator.Persistent);
            _lambdaBuffer = new NativeArray<float>(n, Allocator.Persistent);
            _velocityBuffer = new NativeArray<float3>(n, Allocator.Persistent);
            _velocityTempBuffer = new NativeArray<float3>(n, Allocator.Persistent);
            _hashes = new NativeArray<int2>(n, Allocator.Persistent);
            _lut = new NativeHashMap<int, int2>(n, Allocator.Persistent);
            _controllerBuffer = new NativeList<SlimeController>(16, Allocator.Persistent);
            _restOffsets = new NativeArray<float3>(n, Allocator.Persistent);
            _restOffsetsShuffled = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestLocalPositions = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestLocalPositionsShuffled = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestMorphFrom = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestMorphTo = new NativeArray<float3>(n, Allocator.Persistent);

            // Single controller covering the whole initial blob
            _controllerBuffer.Add(new SlimeController
            {
                Center = new float3(0f, 0f, 0f),
                Radius = SlimeConst.InvScale,
                Velocity = new float3(0f, 0f, 0f),
                Concentration = concentration,
            });

            // Interaction: auto-find a Rigidbody motor if none assigned.
            if (controllerBody == null)
                controllerBody = GetComponentInChildren<Rigidbody>();

            RebuildColliders();
            RebuildShapeTargetIfNeeded(force: true);

            _lastTransformPosition = transform.position;
            _hasLastTransformPosition = true;

            SpawnParticles();
            InitRestOffsets();
            _initialised = true;

            _renderer = GetComponent<SlimeRenderer>();
            _renderer?.Initialise(SlimeConst.Num);

            _surfaceBuilder = GetComponent<SlimeSurfaceBuilder>();
        }
        #endregion  
        #region Particle spawn

        void SpawnParticles()
        {
            if (HasShapeTarget())
            {
                float4x4 localToSim = GetShapeLocalToSim();
                for (int i = 0; i < _particles.Length; i++)
                {
                    float3 pos = TransformPoint(localToSim, _shapeRestLocalPositions[i]);
                    _particles[i] = new SlimeParticle
                    {
                        Position = pos,
                        ID = 0,
                    };
                }
                return;
            }

            float radius = SlimeConst.Width * 0.25f;
            float goldenAngle = PI * (3f - sqrt(5f));
            float3 origin = (float3)transform.position * SlimeConst.InvScale;

            for (int i = 0; i < _particles.Length; i++)
            {
                float t = (i + 0.5f) / _particles.Length;
                float y = 1f - 2f * t;
                float r = sqrt(max(0f, 1f - y * y));
                float theta = goldenAngle * i;
                float shell = radius * pow(t, 1f / 3f);
                float3 p = new float3(cos(theta) * r, y, sin(theta) * r) * shell;

                _particles[i] = new SlimeParticle
                {
                    Position = origin + p,
                    ID = 0,
                };
                _shapeRestLocalPositions[i] = p;
            }
        }

        void SyncTransformTranslation()
        {
            Vector3 current = transform.position;
            if (!_hasLastTransformPosition)
            {
                _lastTransformPosition = current;
                _hasLastTransformPosition = true;
                return;
            }

            Vector3 worldDelta = current - _lastTransformPosition;
            _lastTransformPosition = current;

            if (worldDelta.sqrMagnitude < 1e-12f)
                return;

            float3 simDelta = (float3)worldDelta * SlimeConst.InvScale;
            if (!IsFinite(simDelta))
                return;

            TranslateParticles(_particles, simDelta);
            TranslateParticles(_particlesTemp, simDelta);
            TranslatePositions(_posOld, simDelta);
            TranslatePositions(_posPredict, simDelta);

            if (_controllerBuffer.IsCreated)
            {
                for (int i = 0; i < _controllerBuffer.Length; i++)
                {
                    SlimeController ctrl = _controllerBuffer[i];
                    ctrl.Center += simDelta;
                    _controllerBuffer[i] = ctrl;
                }
            }
        }

        static void TranslateParticles(NativeArray<SlimeParticle> particles, float3 delta)
        {
            if (!particles.IsCreated)
                return;

            for (int i = 0; i < particles.Length; i++)
            {
                SlimeParticle particle = particles[i];
                particle.Position += delta;
                particles[i] = particle;
            }
        }

        static void TranslatePositions(NativeArray<float3> positions, float3 delta)
        {
            if (!positions.IsCreated)
                return;

            for (int i = 0; i < positions.Length; i++)
                positions[i] += delta;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static bool IsFinite(float3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        void InitRestOffsets()
        {
            // Compute initial centre of mass
            float3 center = new float3(0f, 0f, 0f);
            for (int i = 0; i < _particles.Length; i++)
                center += _particles[i].Position;

            center /= _particles.Length;

            _lastCom = center; // seed core-driven locomotion

            for (int i = 0; i < _particles.Length; i++)
                _restOffsets[i] = _particles[i].Position - center;

            if (_controllerBuffer.IsCreated && _controllerBuffer.Length > 0)
            {
                var ctrl = _controllerBuffer[0];
                ctrl.Center = center;
                _controllerBuffer[0] = ctrl;
            }
        }

        /// <summary>
        /// Gather static scene colliders into a sim-space buffer.
        /// World → sim via InvScale; +1 cell padding on the half-extent so particles
        /// don't clip the surface (matches the clousq reference).
        /// </summary>
        public void RebuildColliders()
        {
            if (_colliderBuffer.IsCreated) _colliderBuffer.Dispose();

            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude);

            int count = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger)
                    continue;

                if (col.attachedRigidbody != null)
                    continue;

                if (col.gameObject.scene != gameObject.scene)
                    continue;

                count++;
            }

            _colliderBuffer = new NativeArray<SlimeBoxCollider>(count, Allocator.Persistent);

            int writeIndex = 0;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null || !col.enabled || col.isTrigger)
                    continue;

                if (col.attachedRigidbody != null)
                    continue;

                if (col.gameObject.scene != gameObject.scene)
                    continue;

                Bounds bounds = col.bounds;
                _colliderBuffer[writeIndex++] = new SlimeBoxCollider
                {
                    Center = (float3)bounds.center * SlimeConst.InvScale,
                    Extent = (float3)bounds.extents * SlimeConst.InvScale + new float3(1f, 1f, 1f),
                };
            }

            Debug.Log($"[SlimeSimulator] RebuildColliders: {count} static obstacle collider(s) collected (skipped {colliders.Length - count} with Rigidbody / trigger / disabled / other-scene).");
        }

        void RebuildShapeTargetIfNeeded(bool force = false)
        {
            Mesh resolvedShapeMesh = null;
            Transform resolvedShapeTransform = shapeTargetTransform;
            string resolveError = null;
            bool hasResolvedShapeTarget = enableMeshShapeTarget &&
                TryResolveMeshShapeTarget(out resolvedShapeMesh, out resolvedShapeTransform, out resolveError);

            if (!enableMeshShapeTarget || !hasResolvedShapeTarget || !_shapeRestLocalPositions.IsCreated)
            {
                if (enableMeshShapeTarget && _shapeRestLocalPositions.IsCreated && !string.IsNullOrEmpty(resolveError))
                    Debug.LogWarning($"SlimeSimulator: mesh shape target disabled. {resolveError}", this);

                if (_shapeTarget.IsCreated)
                    _shapeTarget.Dispose();
                _activeShapeTargetMesh = null;
                _activeShapeTargetTransform = null;
                _activeShapeSdfResolution = 0;
                _shapeRestMorphActive = false;
                return;
            }

            if (!force && _shapeTarget.IsCreated && _activeShapeTargetMesh == resolvedShapeMesh &&
                _activeShapeTargetTransform == resolvedShapeTransform &&
                _activeShapeSdfResolution == shapeSdfResolution)
                return;

            if (!SlimeShapeTarget.TryBuild(
                resolvedShapeMesh,
                _shapeRestLocalPositions.Length,
                shapeSdfResolution,
                Allocator.Persistent,
                out SlimeShapeTarget next,
                out string buildError))
            {
                Debug.LogWarning($"SlimeSimulator: mesh shape target disabled. {buildError}", this);
                if (_shapeTarget.IsCreated)
                    _shapeTarget.Dispose();
                _activeShapeTargetMesh = null;
                _activeShapeTargetTransform = null;
                _activeShapeSdfResolution = 0;
                _shapeRestMorphActive = false;
                _shapeLocalCenter = new float3(0f, 0f, 0f);
                return;
            }

            if (_shapeTarget.IsCreated && shapeMorphTime > 0f && _particles.IsCreated)
                StartShapeTargetMorph(next.RestLocalPositions, next.LocalCenter);
            else
            {
                _shapeRestLocalPositions.CopyFrom(next.RestLocalPositions);
                _shapeRestMorphActive = false;
            }

            if (_shapeTarget.IsCreated)
                _shapeTarget.Dispose();

            _shapeTarget = next;
            _activeShapeTargetMesh = resolvedShapeMesh;
            _activeShapeTargetTransform = resolvedShapeTransform;
            _activeShapeSdfResolution = shapeSdfResolution;
            _shapeLocalCenter = next.LocalCenter;

            if (next.UsesSurfaceFallback)
            {
                Debug.LogWarning(
                    $"SlimeSimulator: mesh shape target '{resolvedShapeMesh.name}' is not a closed volume; using surface-shell rest positions and disabling SDF boundary projection.",
                    this);
            }
        }

        public void RebuildMeshShapeTarget()
        {
            RebuildShapeTargetIfNeeded(force: true);
        }

        public bool TryResolveMeshShapeTarget(out Mesh mesh, out Transform targetTransform, out string error)
        {
            mesh = null;
            targetTransform = shapeTargetTransform;
            error = null;

            if (shapeTargetMesh != null)
            {
                mesh = shapeTargetMesh;
                targetTransform = shapeTargetTransform != null ? shapeTargetTransform : transform;
                return true;
            }

            if (shapeTargetObject == null)
            {
                error = "Assign a Mesh, or assign a GameObject/FBX instance that contains a MeshFilter or SkinnedMeshRenderer.";
                return false;
            }

            MeshFilter meshFilter = shapeTargetObject.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                mesh = meshFilter.sharedMesh;
                targetTransform = shapeTargetTransform != null ? shapeTargetTransform : meshFilter.transform;
                return true;
            }

            SkinnedMeshRenderer skinnedRenderer = shapeTargetObject.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedRenderer != null && skinnedRenderer.sharedMesh != null)
            {
                mesh = skinnedRenderer.sharedMesh;
                targetTransform = shapeTargetTransform != null ? shapeTargetTransform : skinnedRenderer.transform;
                return true;
            }

            error = $"Shape target object '{shapeTargetObject.name}' does not contain a MeshFilter or SkinnedMeshRenderer with a shared mesh. Drag a mesh child from the FBX, or place the FBX in the scene and assign that instance.";
            return false;
        }

        void StartShapeTargetMorph(NativeArray<float3> nextRestPositions, float3 nextLocalCenter)
        {
            float4x4 simToLocal = GetShapeSimToLocal();
            float3 center = ComputeParticleCenter();
            for (int i = 0; i < _shapeRestLocalPositions.Length; i++)
            {
                float3 currentLocal = nextLocalCenter + TransformVector(simToLocal, _particles[i].Position - center);
                _shapeRestMorphFrom[i] = currentLocal;
                _shapeRestMorphTo[i] = nextRestPositions[i];
                _shapeRestLocalPositions[i] = _shapeRestMorphFrom[i];
            }

            _shapeRestMorphElapsed = 0f;
            _shapeRestMorphDuration = max(shapeMorphTime, SlimeConst.DeltaTime);
            _shapeRestMorphActive = true;
        }

        void UpdateShapeTargetMorph(float dt)
        {
            if (!_shapeRestMorphActive || !_shapeRestMorphFrom.IsCreated || !_shapeRestMorphTo.IsCreated)
                return;

            _shapeRestMorphElapsed += max(0f, dt);
            float t = saturate(_shapeRestMorphElapsed / max(_shapeRestMorphDuration, SlimeConst.DeltaTime));
            t = t * t * (3f - 2f * t);

            for (int i = 0; i < _shapeRestLocalPositions.Length; i++)
                _shapeRestLocalPositions[i] = lerp(_shapeRestMorphFrom[i], _shapeRestMorphTo[i], t);

            if (_shapeRestMorphElapsed >= _shapeRestMorphDuration)
                _shapeRestMorphActive = false;
        }

        bool HasShapeTarget()
        {
            return enableMeshShapeTarget && _shapeTarget.IsCreated && _shapeRestLocalPositions.IsCreated;
        }

        float4x4 GetShapeLocalToSim()
        {
            Matrix4x4 matrix = _activeShapeTargetTransform != null
                ? _activeShapeTargetTransform.localToWorldMatrix
                : Matrix4x4.identity;
            matrix = Matrix4x4.Scale(Vector3.one * SlimeConst.InvScale) * matrix;
            return matrix;
        }

        float4x4 GetShapeSimToLocal()
        {
            Matrix4x4 matrix = _activeShapeTargetTransform != null
                ? _activeShapeTargetTransform.worldToLocalMatrix
                : Matrix4x4.identity;
            matrix *= Matrix4x4.Scale(Vector3.one * SlimeConst.Scale);
            return matrix;
        }

        static float3 TransformPoint(float4x4 matrix, float3 point)
        {
            float4 v = mul(matrix, new float4(point, 1f));
            return v.xyz;
        }

        static float3 TransformVector(float4x4 matrix, float3 vector)
        {
            float4 v = mul(matrix, new float4(vector, 0f));
            return v.xyz;
        }

        float3 ComputeParticleCenter()
        {
            float3 center = new float3(0f, 0f, 0f);
            if (!_particles.IsCreated || _particles.Length == 0)
                return center;

            for (int i = 0; i < _particles.Length; i++)
                center += _particles[i].Position;

            return center / _particles.Length;
        }
        #endregion
        #region  Simulation step

        void Simulate()
        {
            _lut.Clear();

            // 1. Apply forces + predict
            new SlimeSimulation.ApplyForceJob
            {
                Controllers = _controllerBuffer,
                Ps = _particles,
                PsNew = _particlesTemp,
                Velocity = _velocityBuffer,
                Gravity = new float3(0f, gravity, 0f),
                VelocityFollowGain = velocityFollowGain,
                BlobCenter = _lastCom,
                MoveCoreRadius = moveCoreRadius,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 2. Hash
            new SlimeSimulation.HashJob
            {
                Ps = _particlesTemp,
                Hashes = _hashes,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 3. Sort + BuildLut
            _hashes.SortJob(new Int2Comparer()).Schedule().Complete();
            new SlimeSimulation.BuildLutJob
            {
                Hashes = _hashes,
                Lut = _lut,
            }.Schedule().Complete();

            // 4. Shuffle (including rest offsets for shape matching)
            var snapRest = new NativeArray<float3>(_particles.Length, Allocator.TempJob);
            snapRest.CopyFrom(_restOffsets);
            var snapShapeRest = new NativeArray<float3>(_particles.Length, Allocator.TempJob);
            snapShapeRest.CopyFrom(_shapeRestLocalPositions);
            var snapShapeMorphFrom = new NativeArray<float3>(_particles.Length, Allocator.TempJob);
            snapShapeMorphFrom.CopyFrom(_shapeRestMorphFrom);
            var snapShapeMorphTo = new NativeArray<float3>(_particles.Length, Allocator.TempJob);
            snapShapeMorphTo.CopyFrom(_shapeRestMorphTo);

            new SlimeSimulation.ShuffleJob
            {
                Hashes = _hashes,
                PsRaw = _particles,
                PsNew = _particlesTemp,
                Velocity = _velocityBuffer,
                RestOffsets = snapRest,
                ShapeRestLocalPositions = snapShapeRest,
                ShapeRestMorphFrom = snapShapeMorphFrom,
                ShapeRestMorphTo = snapShapeMorphTo,
                PosOld = _posOld,
                PosPredict = _posPredict,
                VelocityOut = _velocityTempBuffer,
                RestOffsetsOut = _restOffsetsShuffled,
                ShapeRestLocalPositionsOut = _shapeRestLocalPositionsShuffled,
                ShapeRestMorphFromOut = _shapeRestMorphFrom,
                ShapeRestMorphToOut = _shapeRestMorphTo,
            }.Schedule(_particles.Length, _batchCount).Complete();

            snapRest.Dispose();
            snapShapeRest.Dispose();
            snapShapeMorphFrom.Dispose();
            snapShapeMorphTo.Dispose();

            // 5. Compute lambda
            new SlimeSimulation.ComputeLambdaJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Lambda = _lambdaBuffer,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 6. Compute delta positions
            new SlimeSimulation.ComputeDeltaPosJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Lambda = _lambdaBuffer,
                PsNew = _particles,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 7. Static mesh shape target: XPBD-style position projection.
            if (HasShapeTarget())
            {
                var center = new NativeArray<float3>(1, Allocator.TempJob);
                new SlimeCohesion.ComputeCenterJob
                {
                    Particles = _particles,
                    Center = center,
                }.Schedule().Complete();

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = _particles,
                    RestLocalPositions = _shapeRestLocalPositionsShuffled,
                    Center = center,
                    Sdf = _shapeTarget.Sdf,
                    SdfInfo = _shapeTarget.SdfInfo,
                    LocalToSimVector = GetShapeLocalToSim(),
                    SimToLocalVector = GetShapeSimToLocal(),
                    ShapeLocalCenter = _shapeLocalCenter,
                    Strength = shapeTargetStrength,
                    Compliance = shapeTargetCompliance,
                    BoundaryStrength = shapeBoundaryStrength,
                    DisableBoundaryProjection = !_shapeTarget.IsClosedVolume,
                    DeltaTime = SlimeConst.DeltaTime,
                }.Schedule(_particles.Length, _batchCount).Complete();

                center.Dispose();
            }

            // 8. Update: floor + collider push-out + velocity reconstruction + clamp
            new SlimeSimulation.UpdateJob
            {
                Ps = _particles,
                PosOld = _posOld,
                Colliders = _colliderBuffer,
                Velocity = _velocityTempBuffer,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 9. Viscosity
            new SlimeSimulation.ApplyViscosityJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                VelocityR = _velocityTempBuffer,
                VelocityW = _velocityBuffer,
                ViscosityStrength = viscosityStrength,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // Sync restOffsets to current (cell-grouped) order for next sub-step
            _restOffsets.CopyFrom(_restOffsetsShuffled);
            _shapeRestLocalPositions.CopyFrom(_shapeRestLocalPositionsShuffled);
        }
        #endregion  
        #region  Cleanup 

        void Dispose()
        {
            if (_particles.IsCreated) _particles.Dispose();
            if (_particlesTemp.IsCreated) _particlesTemp.Dispose();
            if (_posPredict.IsCreated) _posPredict.Dispose();
            if (_posOld.IsCreated) _posOld.Dispose();
            if (_lambdaBuffer.IsCreated) _lambdaBuffer.Dispose();
            if (_velocityBuffer.IsCreated) _velocityBuffer.Dispose();
            if (_velocityTempBuffer.IsCreated) _velocityTempBuffer.Dispose();
            if (_hashes.IsCreated) _hashes.Dispose();
            if (_lut.IsCreated) _lut.Dispose();
            if (_controllerBuffer.IsCreated) _controllerBuffer.Dispose();
            if (_restOffsets.IsCreated) _restOffsets.Dispose();
            if (_restOffsetsShuffled.IsCreated) _restOffsetsShuffled.Dispose();
            if (_shapeRestLocalPositions.IsCreated) _shapeRestLocalPositions.Dispose();
            if (_shapeRestLocalPositionsShuffled.IsCreated) _shapeRestLocalPositionsShuffled.Dispose();
            if (_shapeRestMorphFrom.IsCreated) _shapeRestMorphFrom.Dispose();
            if (_shapeRestMorphTo.IsCreated) _shapeRestMorphTo.Dispose();
            if (_colliderBuffer.IsCreated) _colliderBuffer.Dispose();
            if (_shapeTarget.IsCreated) _shapeTarget.Dispose();
            _activeShapeTargetMesh = null;
            _activeShapeTargetTransform = null;
            _activeShapeSdfResolution = 0;
            _shapeRestMorphActive = false;
            _shapeLocalCenter = new float3(0f, 0f, 0f);
            _hasLastTransformPosition = false;
            _initialised = false;
        }
        #endregion  
        void OnDrawGizmos()
        {
            if (!Application.isPlaying || !_particles.IsCreated)
                return;

            Gizmos.color = Color.cyan;
            int limit = min(200, _particles.Length);
            for (int i = 0; i < limit; i++)
            {
                float3 pos = _particles[i].Position * SlimeConst.Scale;
                Gizmos.DrawWireSphere((Vector3)pos, 0.02f);
            }
        }
        void Control()
        {
            if (_controllerBuffer.Length == 0) return;

            // 1. Compute current centre of mass (from shuffled particles — order-independent)
            var center = new NativeArray<float3>(1, Allocator.TempJob);
            new SlimeCohesion.ComputeCenterJob
            {
                Particles = _particles,
                Center = center,
            }.Schedule().Complete();

            _lastCom = center[0]; // cache for next frame's core-driven locomotion

            // 2. Motor drives the cohesion centre directly (WASD → position).
            //    When no motor is assigned, the blob stays self-cohesive at its own COM.
            var ctrl = _controllerBuffer[0];
            ctrl.Concentration = concentration;
            if (controllerBody != null)
            {
                ctrl.Center = (float3)controllerBody.position * SlimeConst.InvScale;
                ctrl.Radius = SlimeConst.InvScale * 5f; // keep whole blob in range
                ctrl.Velocity = (float3)controllerBody.linearVelocity * SlimeConst.InvScale;
            }
            else
            {
                ctrl.Center = center[0];
                ctrl.Radius = SlimeConst.InvScale;
                ctrl.Velocity = new float3(0f, 0f, 0f);
            }
            _controllerBuffer[0] = ctrl;

            // 3. Shape matching (translation-only)
            if (enableShapeMatch && !HasShapeTarget())
            {
                new SlimeCohesion.ApplyShapeMatchJob
                {
                    RestOffsets = _restOffsetsShuffled,
                    Center = center,
                    Particles = _particles,
                    Velocity = _velocityBuffer,
                    stiffness = shapeStiffness,
                    inverseDt = 1f / SlimeConst.DeltaTime,
                }.Schedule(_particles.Length, _batchCount).Complete();
            }

            center.Dispose();
        }
    }
}
