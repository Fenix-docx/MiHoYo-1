using System.Collections.Generic;
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
        [Range(0.01f, 50f)] public float concentration = 10f;
        [Min(16)] public int initialParticleCount = SlimeConst.Num;

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

        [Header("Collision Broad Phase")]
        [Tooltip("Build an octree over static scene colliders so each particle checks only nearby obstacle candidates.")]
        public bool useCollisionOctree = true;
        [Tooltip("Maximum octree subdivision depth for static obstacle broad-phase queries.")]
        [Range(1, 8)] public int collisionOctreeMaxDepth = 6;
        [Tooltip("Maximum collider count kept in a leaf before subdivision.")]
        [Min(1)] public int collisionOctreeLeafCapacity = 8;
        [Tooltip("Extra sim-space padding applied to the root octree cube.")]
        [Min(0f)] public float collisionOctreePadding = 1f;

        [Header("Core-driven locomotion")]
        [Tooltip("Only particles within this sim-space radius of the blob COM receive the controller pull force. 0 = apply to all (legacy).")]
        [Range(0f, 5f)] public float moveCoreRadius = 1.5f;

        [Header("Split / Absorb")]
        [Tooltip("Fraction of the controlled body peeled off on each split (0.1 = 10% of particles).")]
        [Range(0.05f, 0.5f)] public float splitFraction = 0.25f;
        [Tooltip("One-time sim-space offset the split body is placed ahead along the facing direction. Separates the bodies cleanly with no impulse (no drift / continuous force).")]
        [Min(0f)] public float splitSeparation = 5f;
        [Tooltip("Minimum particles a body must keep / peel to allow a split.")]
        [Min(16)] public int minParticlesToSplit = 128;
        [Tooltip("Extra sim-space slack beyond the two bodies' radii for absorb contact to trigger.")]
        [Min(0f)] public float absorbContactSlack = 0.5f;
        [Tooltip("Seconds after a split during which absorb is disabled, so the ejection can separate the bodies.")]
        [Min(0f)] public float absorbCooldownTime = 0.5f;
        [Tooltip("Extra distance beyond contact threshold a split pair must reach before it can absorb again.")]
        [Min(0f)] public float absorbRearmSlack = 1f;
        [Tooltip("Key to split the controlled body. Facing direction comes from the main camera.")]
        public KeyCode keySplit = KeyCode.F;
        [Tooltip("Key to cycle control between split bodies.")]
        public KeyCode keySwitchControl = KeyCode.Tab;

        [Header("Keyboard")]
        public KeyCode keyDecreaseBoth = KeyCode.LeftBracket;
        public KeyCode keyIncreaseBoth = KeyCode.RightBracket;
        [Range(0.1f, 20f)] public float concentrationStep = 4f;
        [Range(0.01f, 0.2f)] public float stiffnessStep = 0.05f;
        [Range(1, 3)] public int currentstep = 1; //凝聚的阶段，公开出来方便做可视化

        [Header("Runtime UI")]
        [SerializeField] bool showRuntimeHud = true;

        [Header("Debug")]
        public bool simulateOnStart = true;
        // 暴露史莱姆的位置给相机
        public Vector3 CurrentWorldCenter { get; private set; }
        public int ParticleCount => _particles.IsCreated ? _particles.Length : 0;

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
        NativeArray<float3> _restOffsets;         // current shape rest offsets, aligned to _particles
        NativeArray<float3> _restOffsetsShuffled; // cell-grouped order (matches _particles after shuffle)
        NativeArray<float3> _initialRestOffsets;  // canonical initial full-body shape, aligned to _particles
        NativeArray<float3> _initialRestOffsetsShuffled;
        NativeArray<float3> _shapeRestLocalPositions;
        NativeArray<float3> _shapeRestLocalPositionsShuffled;
        NativeArray<float3> _shapeRestMorphFrom;
        NativeArray<float3> _shapeRestMorphTo;
        NativeArray<SlimeBoxCollider> _colliderBuffer;
        NativeArray<SlimeColliderOctreeNode> _colliderOctreeNodes;
        NativeArray<int> _colliderOctreeIndices;
        NativeArray<float3> _groupCentres;   // per-group COM sums (length MaxGroups)
        NativeArray<float3> _groupRestCentres; // per-group canonical rest-offset sums
        NativeArray<int> _groupCounts;       // per-group particle counts (length MaxGroups)
        NativeArray<float> _groupStiffness;  // per-group shape-match stiffness (length MaxGroups)
        NativeArray<int> _shuffledIds;       // particle IDs in cell-grouped (shuffled) order
        SlimeGroupManager _groups;
        SlimeControllerMotor _controllerMotor;
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
        Vector3 _lastControllerBodyPosition;
        bool _hasLastControllerBodyPosition;
        int _lastControllerBodyGroupId = -1;
        float _absorbCooldown; // >0 suppresses absorb right after a split

        int _batchCount = 64;
        SlimeRenderer _renderer;
        SlimeSurfaceBuilder _surfaceBuilder;
        SlimeStatusHud _statusHud;
        bool _initialised;
        bool isQ = true;
        void Start()
        {
            CurrentWorldCenter = transform.position;
            if (simulateOnStart)
                Initialise();
            EnsureStatusHud();
        }

        void Update()
        {
            if (!_initialised) return;


            if (Input.GetKeyDown(KeyCode.Q))
                ChangeControlledCohesion(-concentrationStep, -stiffnessStep, 1);
            if (Input.GetKeyDown(KeyCode.E))
                ChangeControlledCohesion(concentrationStep, stiffnessStep, -1);

            // F: peel a fraction of the controlled body into a new split body.
            if (Input.GetKeyDown(keySplit))
                Split(GetFacingDirection());

            // Tab: cycle control to the next alive body.
            if (Input.GetKeyDown(keySwitchControl))
                SwitchControl();
        }

        void ChangeControlledCohesion(float concentrationDelta, float stiffnessDelta, int stepDelta)
        {
            concentration = Mathf.Clamp(concentration + concentrationDelta, 0.1f, 12f);
            shapeStiffness = Mathf.Clamp(shapeStiffness + stiffnessDelta, 0f, 0.2f);
            currentstep = Mathf.Clamp(currentstep + stepDelta, 1, 3);
            StoreControlledCohesionState();
        }

        void StoreControlledCohesionState()
        {
            if (_groups == null)
                return;

            _groups.SetControlledCohesionState(concentration, shapeStiffness, CohesionStep);
        }

        void SyncCohesionFieldsFromGroup(int groupId)
        {
            if (_groups == null || !_groups.IsAlive(groupId))
                return;

            concentration = _groups.GetConcentration(groupId);
            shapeStiffness = _groups.GetStiffness(groupId);
            currentstep = Mathf.Clamp(_groups.GetCohesionStep(groupId), 1, 3);
        }

        void FixedUpdate()
        {
            if (!_initialised) return;

            SyncTransformTranslation();
            SyncControllerBodyTranslation();
            RebuildShapeTargetIfNeeded();
            UpdateShapeTargetMorph(Time.fixedDeltaTime);

            // Two simulation sub-steps per physics tick (reference pattern)
            for (int i = 0; i < 2; i++)
                Simulate();

            Control();
            UpdateAbsorb();

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

            int n = Mathf.Max(16, initialParticleCount);
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
            _initialRestOffsets = new NativeArray<float3>(n, Allocator.Persistent);
            _initialRestOffsetsShuffled = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestLocalPositions = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestLocalPositionsShuffled = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestMorphFrom = new NativeArray<float3>(n, Allocator.Persistent);
            _shapeRestMorphTo = new NativeArray<float3>(n, Allocator.Persistent);
            _groupCentres = new NativeArray<float3>(SlimeGroupManager.MaxGroups, Allocator.Persistent);
            _groupRestCentres = new NativeArray<float3>(SlimeGroupManager.MaxGroups, Allocator.Persistent);
            _groupCounts = new NativeArray<int>(SlimeGroupManager.MaxGroups, Allocator.Persistent);
            _groupStiffness = new NativeArray<float>(SlimeGroupManager.MaxGroups, Allocator.Persistent);
            _shuffledIds = new NativeArray<int>(n, Allocator.Persistent);
            _groups = new SlimeGroupManager();
            currentstep = Mathf.Clamp(currentstep, 1, 3);

            // Single controller covering the whole initial blob
            _controllerBuffer.Add(new SlimeController
            {
                Center = new float3(0f, 0f, 0f),
                Radius = SlimeConst.InvScale,
                Velocity = new float3(0f, 0f, 0f),
                Concentration = concentration,
            });
            _groups.Init(concentration, shapeStiffness, CohesionStep);

            // Interaction: auto-find a motor body if none assigned.
            if (controllerBody == null)
                controllerBody = ResolveControllerBody();
            _controllerMotor = ResolveControllerMotor();

            RebuildColliders();
            RebuildShapeTargetIfNeeded(force: true);

            _lastTransformPosition = transform.position;
            _hasLastTransformPosition = true;
            if (controllerBody != null)
            {
                _lastControllerBodyPosition = controllerBody.position;
                _hasLastControllerBodyPosition = true;
                _lastControllerBodyGroupId = _groups.ControlledGroupId;
            }
            else
            {
                _lastControllerBodyPosition = transform.position;
                _hasLastControllerBodyPosition = false;
                _lastControllerBodyGroupId = -1;
            }

            SpawnParticles();
            InitRestOffsets();
            _initialised = true;

            _renderer = GetComponent<SlimeRenderer>();
            _renderer?.Initialise(_particles.Length);
            _renderer?.SetParticles(_particles);

            _surfaceBuilder = GetComponent<SlimeSurfaceBuilder>();
            EnsureStatusHud();
        }

        public int AddParticles(int count, Vector3 worldCenter, float worldRadius)
        {
            if (!_initialised || !_particles.IsCreated || count <= 0)
                return 0;

            int oldCount = _particles.Length;
            int newCount = oldCount + count;
            float3 spawnCenter = (float3)worldCenter * SlimeConst.InvScale;
            if (!IsFinite(spawnCenter))
                spawnCenter = ComputeParticleCenter();

            float spawnRadius = max(worldRadius * SlimeConst.InvScale, SlimeConst.CellSize * 0.5f);
            ResizeParticleBuffers(newCount);

            float goldenAngle = PI * (3f - sqrt(5f));
            for (int i = 0; i < count; i++)
            {
                int index = oldCount + i;
                float t = (i + 0.5f) / count;
                float y = 1f - 2f * t;
                float r = sqrt(max(0f, 1f - y * y));
                float theta = goldenAngle * index;
                float shell = spawnRadius * pow(t, 1f / 3f);
                float3 offset = new float3(cos(theta) * r, y, sin(theta) * r) * shell;
                float3 position = spawnCenter + offset;
                SlimeParticle particle = new SlimeParticle
                {
                    Position = position,
                    ID = 0,
                };

                _particles[index] = particle;
                _particlesTemp[index] = particle;
                _posPredict[index] = position;
                _posOld[index] = position;
                _lambdaBuffer[index] = 0f;
                _velocityBuffer[index] = new float3(0f, 0f, 0f);
                _velocityTempBuffer[index] = new float3(0f, 0f, 0f);
                _hashes[index] = new int2(0, index);
                _shapeRestLocalPositions[index] = offset;
                _shapeRestLocalPositionsShuffled[index] = offset;
                _shapeRestMorphFrom[index] = offset;
                _shapeRestMorphTo[index] = offset;
            }

            InitRestOffsets();
            _restOffsetsShuffled.CopyFrom(_restOffsets);
            RefreshParticleRuntimeViews();
            return count;
        }

        public int AddParticlesDistributed(int count, float worldJitterRadius)
        {
            if (!_initialised || !_particles.IsCreated || count <= 0)
                return 0;

            int oldCount = _particles.Length;
            if (oldCount <= 0)
                return 0;

            int newCount = oldCount + count;
            float jitterRadius = max(worldJitterRadius * SlimeConst.InvScale, SlimeConst.CellSize * 0.1f);
            ResizeParticleBuffers(newCount);

            for (int i = 0; i < count; i++)
            {
                int sourceIndex = GetDistributedSourceIndex(i, count, oldCount);
                int index = oldCount + i;
                float3 sourcePosition = _particles[sourceIndex].Position;
                float3 offset = LowDiscrepancyBallOffset(index, jitterRadius);
                float3 position = sourcePosition + offset;
                SlimeParticle particle = new SlimeParticle
                {
                    Position = position,
                    ID = _particles[sourceIndex].ID,
                };

                _particles[index] = particle;
                _particlesTemp[index] = particle;
                _posPredict[index] = position;
                _posOld[index] = position;
                _lambdaBuffer[index] = 0f;
                _velocityBuffer[index] = _velocityBuffer[sourceIndex];
                _velocityTempBuffer[index] = _velocityTempBuffer[sourceIndex];
                _hashes[index] = new int2(0, index);
                _shapeRestLocalPositions[index] = _shapeRestLocalPositions[sourceIndex];
                _shapeRestLocalPositionsShuffled[index] = _shapeRestLocalPositionsShuffled[sourceIndex];
                _shapeRestMorphFrom[index] = _shapeRestMorphFrom[sourceIndex];
                _shapeRestMorphTo[index] = _shapeRestMorphTo[sourceIndex];
            }

            InitRestOffsets();
            _restOffsetsShuffled.CopyFrom(_restOffsets);
            RefreshParticleRuntimeViews();
            return count;
        }

        public void TeleportToWorldCenter(Vector3 worldCenter, Quaternion worldRotation, bool resetVelocity = true)
        {
            if (!IsFinite((float3)worldCenter))
                return;

            if (!_initialised || !_particles.IsCreated)
            {
                transform.SetPositionAndRotation(worldCenter, worldRotation);
                CurrentWorldCenter = worldCenter;
                _lastTransformPosition = transform.position;
                _hasLastTransformPosition = true;
                TeleportControllerBody(worldCenter, worldRotation, resetVelocity);
                return;
            }

            Vector3 currentWorldCenter = CurrentWorldCenter;
            if (!IsFinite((float3)currentWorldCenter))
                currentWorldCenter = (Vector3)(ComputeParticleCenter() * SlimeConst.Scale);

            Vector3 worldDelta = worldCenter - currentWorldCenter;
            float3 simDelta = (float3)worldDelta * SlimeConst.InvScale;
            if (!IsFinite(simDelta))
                return;

            TranslateParticles(_particles, simDelta);
            TranslateParticles(_particlesTemp, simDelta);
            TranslatePositions(_posOld, simDelta);
            TranslatePositions(_posPredict, simDelta);
            TranslateGroupCentres(simDelta);
            TranslateControllerCentres(simDelta);

            if (resetVelocity)
                ResetVelocityBuffers();

            CurrentWorldCenter = worldCenter;
            transform.SetPositionAndRotation(worldCenter, worldRotation);
            _lastTransformPosition = transform.position;
            _hasLastTransformPosition = true;
            TeleportControllerBody(worldCenter, worldRotation, resetVelocity);

            _renderer?.SetParticles(_particles);
            if (enableSurface && _surfaceBuilder != null)
                _surfaceBuilder.BuildSurface(_particles, isoLevel);
        }

        static int GetDistributedSourceIndex(int addIndex, int addCount, int sourceCount)
        {
            if (sourceCount <= 1)
                return 0;

            float t = (addIndex + 0.5f) / max(1, addCount);
            return clamp((int)floor(t * sourceCount), 0, sourceCount - 1);
        }

        static float3 LowDiscrepancyBallOffset(int sequenceIndex, float radius)
        {
            const float goldenRatioConjugate = 0.61803398875f;
            const float secondIrrational = 0.75487766625f;

            float y = 1f - 2f * frac((sequenceIndex + 0.5f) * goldenRatioConjugate);
            float ring = sqrt(max(0f, 1f - y * y));
            float theta = sequenceIndex * PI * (3f - sqrt(5f));
            float shell = pow(frac((sequenceIndex + 0.5f) * secondIrrational), 1f / 3f);
            return new float3(cos(theta) * ring, y, sin(theta) * ring) * shell * radius;
        }

        void ResizeParticleBuffers(int newCount)
        {
            ResizeNativeArray(ref _particles, newCount);
            ResizeNativeArray(ref _particlesTemp, newCount);
            ResizeNativeArray(ref _posPredict, newCount);
            ResizeNativeArray(ref _posOld, newCount);
            ResizeNativeArray(ref _lambdaBuffer, newCount);
            ResizeNativeArray(ref _velocityBuffer, newCount);
            ResizeNativeArray(ref _velocityTempBuffer, newCount);
            ResizeNativeArray(ref _hashes, newCount);
            ResizeNativeArray(ref _restOffsets, newCount);
            ResizeNativeArray(ref _restOffsetsShuffled, newCount);
            ResizeNativeArray(ref _initialRestOffsets, newCount);
            ResizeNativeArray(ref _initialRestOffsetsShuffled, newCount);
            ResizeNativeArray(ref _shapeRestLocalPositions, newCount);
            ResizeNativeArray(ref _shapeRestLocalPositionsShuffled, newCount);
            ResizeNativeArray(ref _shapeRestMorphFrom, newCount);
            ResizeNativeArray(ref _shapeRestMorphTo, newCount);
            ResizeNativeArray(ref _shuffledIds, newCount);

            if (_lut.IsCreated)
                _lut.Dispose();
            _lut = new NativeHashMap<int, int2>(newCount, Allocator.Persistent);
        }

        void RefreshParticleRuntimeViews()
        {
            RebuildShapeTargetIfNeeded(force: true);
            _renderer?.Initialise(_particles.Length);
            _renderer?.SetParticles(_particles);
        }

        static void ResizeNativeArray<T>(ref NativeArray<T> array, int newCount) where T : struct
        {
            NativeArray<T> resized = new NativeArray<T>(newCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            if (array.IsCreated)
            {
                NativeArray<T>.Copy(array, resized, Mathf.Min(array.Length, newCount));
                array.Dispose();
            }

            array = resized;
        }
        #endregion  
        #region Scene references

        static T[] FindSceneObjects<T>(FindObjectsInactive inactive)
            where T : UnityEngine.Object
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }

        Rigidbody ResolveControllerBody()
        {
            SlimeControllerMotor childMotor = GetComponentInChildren<SlimeControllerMotor>();
            if (childMotor != null && childMotor.TryGetComponent(out Rigidbody childBody))
            {
                _controllerMotor = childMotor;
                return childBody;
            }

            Rigidbody childRigidbody = GetComponentInChildren<Rigidbody>();
            if (childRigidbody != null)
                return childRigidbody;

            SlimeControllerMotor[] sceneMotors =
                FindSceneObjects<SlimeControllerMotor>(FindObjectsInactive.Exclude);
            if (sceneMotors.Length == 1 && sceneMotors[0].TryGetComponent(out Rigidbody sceneBody))
            {
                _controllerMotor = sceneMotors[0];
                return sceneBody;
            }

            return null;
        }

        SlimeControllerMotor ResolveControllerMotor()
        {
            if (_controllerMotor != null)
            {
                if (controllerBody == null)
                    return _controllerMotor;

                if (_controllerMotor.TryGetComponent(out Rigidbody cachedBody) && cachedBody == controllerBody)
                    return _controllerMotor;

                _controllerMotor = null;
            }

            if (controllerBody != null)
            {
                if (controllerBody.TryGetComponent(out SlimeControllerMotor bodyMotor))
                    return bodyMotor;

                return null;
            }

            SlimeControllerMotor childMotor = GetComponentInChildren<SlimeControllerMotor>();
            if (childMotor != null)
                return childMotor;

            SlimeControllerMotor[] sceneMotors =
                FindSceneObjects<SlimeControllerMotor>(FindObjectsInactive.Exclude);
            return sceneMotors.Length == 1 ? sceneMotors[0] : null;
        }

        Vector3 ResolveControllerVelocity()
        {
            SlimeControllerMotor motor = ResolveControllerMotor();
            if (motor != null)
            {
                _controllerMotor = motor;
                return motor.CommandedVelocity;
            }

            return controllerBody != null ? controllerBody.linearVelocity : Vector3.zero;
        }

        Vector3 ResolveSpawnCenter()
        {
            return controllerBody != null ? controllerBody.position : transform.position;
        }

        Transform ResolveDefaultShapeTargetTransform()
        {
            if (shapeTargetTransform != null)
                return shapeTargetTransform;

            return controllerBody != null ? controllerBody.transform : transform;
        }

        bool IsTransformDrivenByControllerBody()
        {
            return controllerBody != null && controllerBody.transform == transform;
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
            float3 origin = (float3)ResolveSpawnCenter() * SlimeConst.InvScale;

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

            if (IsTransformDrivenByControllerBody())
                return;

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

        void SyncControllerBodyTranslation()
        {
            if (controllerBody == null || _groups == null)
                return;

            int groupId = _groups.ControlledGroupId;
            Vector3 current = controllerBody.position;
            if (!_hasLastControllerBodyPosition || _lastControllerBodyGroupId != groupId)
            {
                _lastControllerBodyPosition = current;
                _hasLastControllerBodyPosition = true;
                _lastControllerBodyGroupId = groupId;
                return;
            }

            Vector3 worldDelta = current - _lastControllerBodyPosition;
            _lastControllerBodyPosition = current;

            if (worldDelta.sqrMagnitude < 1e-12f)
                return;

            float3 simDelta = (float3)worldDelta * SlimeConst.InvScale;
            if (!IsFinite(simDelta))
                return;

            TranslateParticles(_particles, simDelta, groupId);
            TranslateParticles(_particlesTemp, simDelta, groupId);
            TranslatePositions(_posOld, _particles, simDelta, groupId);
            TranslatePositions(_posPredict, _particles, simDelta, groupId);

            if (_groupCentres.IsCreated && (uint)groupId < (uint)_groupCentres.Length)
                _groupCentres[groupId] += simDelta * max(0, _groupCounts[groupId]);

            if (_controllerBuffer.IsCreated && (uint)groupId < (uint)_controllerBuffer.Length)
            {
                SlimeController ctrl = _controllerBuffer[groupId];
                ctrl.Center = (float3)current * SlimeConst.InvScale;
                ctrl.Radius = SlimeConst.InvScale * 5f;
                ctrl.Velocity = (float3)ResolveControllerVelocity() * SlimeConst.InvScale;
                _controllerBuffer[groupId] = ctrl;
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

        static void TranslateParticles(NativeArray<SlimeParticle> particles, float3 delta, int groupId)
        {
            if (!particles.IsCreated)
                return;

            for (int i = 0; i < particles.Length; i++)
            {
                SlimeParticle particle = particles[i];
                if (particle.ID != groupId)
                    continue;

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

        static void TranslatePositions(
            NativeArray<float3> positions,
            NativeArray<SlimeParticle> particles,
            float3 delta,
            int groupId)
        {
            if (!positions.IsCreated || !particles.IsCreated)
                return;

            int count = min(positions.Length, particles.Length);
            for (int i = 0; i < count; i++)
            {
                if (particles[i].ID == groupId)
                    positions[i] += delta;
            }
        }

        void TranslateControllerCentres(float3 delta)
        {
            if (!_controllerBuffer.IsCreated)
                return;

            for (int i = 0; i < _controllerBuffer.Length; i++)
            {
                SlimeController controller = _controllerBuffer[i];
                controller.Center += delta;
                _controllerBuffer[i] = controller;
            }
        }

        void TranslateGroupCentres(float3 delta)
        {
            if (!_groupCentres.IsCreated || !_groupCounts.IsCreated)
                return;

            int count = min(_groupCentres.Length, _groupCounts.Length);
            for (int i = 0; i < count; i++)
                _groupCentres[i] += delta * max(0, _groupCounts[i]);
        }

        void ResetVelocityBuffers()
        {
            if (_velocityBuffer.IsCreated)
            {
                for (int i = 0; i < _velocityBuffer.Length; i++)
                    _velocityBuffer[i] = new float3(0f, 0f, 0f);
            }

            if (_velocityTempBuffer.IsCreated)
            {
                for (int i = 0; i < _velocityTempBuffer.Length; i++)
                    _velocityTempBuffer[i] = new float3(0f, 0f, 0f);
            }
        }

        void TeleportControllerBody(Vector3 worldCenter, Quaternion worldRotation, bool resetVelocity)
        {
            _controllerMotor = ResolveControllerMotor();

            if (controllerBody != null)
            {
                controllerBody.position = worldCenter;
                controllerBody.rotation = worldRotation;
                controllerBody.transform.SetPositionAndRotation(worldCenter, worldRotation);

                if (resetVelocity)
                {
                    ResetRigidbodyVelocity(controllerBody);
                    _controllerMotor?.ResetMotion();
                }

                _lastControllerBodyPosition = controllerBody.position;
                _hasLastControllerBodyPosition = true;
                _lastControllerBodyGroupId = _groups != null ? _groups.ControlledGroupId : -1;
            }
            else if (resetVelocity)
            {
                _controllerMotor?.ResetMotion();
            }
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

            CurrentWorldCenter = (Vector3)(center * SlimeConst.Scale);

            // Seed per-group COM (group 0 = whole blob at init) so the first
            // ApplyForceJob has a valid core-weight reference.
            for (int g = 0; g < _groupCentres.Length; g++)
            {
                _groupCentres[g] = new float3(0f, 0f, 0f);
                _groupCounts[g] = 0;
            }
            _groupCentres[0] = center * _particles.Length;
            _groupCounts[0] = _particles.Length;
            _groupStiffness[0] = shapeStiffness;

            for (int i = 0; i < _particles.Length; i++)
            {
                float3 restOffset = _particles[i].Position - center;
                _restOffsets[i] = restOffset;
                _initialRestOffsets[i] = restOffset;
            }

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
            DisposeColliderOctree();
            if (_colliderBuffer.IsCreated) _colliderBuffer.Dispose();

            Collider[] colliders =
                FindSceneObjects<Collider>(FindObjectsInactive.Exclude);

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

                if (TryBuildBoxCollider(col, out SlimeBoxCollider slimeCollider))
                    _colliderBuffer[writeIndex++] = slimeCollider;
            }

            if (writeIndex != count)
            {
                NativeArray<SlimeBoxCollider> compact = new NativeArray<SlimeBoxCollider>(writeIndex, Allocator.Persistent);
                for (int i = 0; i < writeIndex; i++)
                    compact[i] = _colliderBuffer[i];
                _colliderBuffer.Dispose();
                _colliderBuffer = compact;
                count = writeIndex;
            }

            BuildColliderOctree();

            int octreeNodeCount = _colliderOctreeNodes.IsCreated ? _colliderOctreeNodes.Length : 0;
            Debug.Log($"[SlimeSimulator] RebuildColliders: {count} static obstacle collider(s) collected (skipped {colliders.Length - count} with Rigidbody / trigger / disabled / other-scene). Octree nodes: {octreeNodeCount}.");
        }

        void BuildColliderOctree()
        {
            DisposeColliderOctree();

            if (!useCollisionOctree || !_colliderBuffer.IsCreated || _colliderBuffer.Length == 0)
                return;

            int colliderCount = _colliderBuffer.Length;
            float3[] boundsMin = new float3[colliderCount];
            float3[] boundsMax = new float3[colliderCount];

            float3 totalMin = new float3(float.MaxValue);
            float3 totalMax = new float3(float.MinValue);
            List<int> allColliders = new List<int>(colliderCount);

            for (int i = 0; i < colliderCount; i++)
            {
                SlimeBoxCollider collider = _colliderBuffer[i];
                GetColliderAabb(collider, out float3 minBounds, out float3 maxBounds);

                if (!IsFinite(minBounds) || !IsFinite(maxBounds))
                    continue;

                boundsMin[i] = minBounds;
                boundsMax[i] = maxBounds;
                totalMin = min(totalMin, minBounds);
                totalMax = max(totalMax, maxBounds);
                allColliders.Add(i);
            }

            if (allColliders.Count == 0)
                return;

            float3 rootCenter = (totalMin + totalMax) * 0.5f;
            float3 rootSize = max(totalMax - totalMin, new float3(0.001f));
            float rootHalfSize = max(cmax(rootSize) * 0.5f + max(0f, collisionOctreePadding), 0.5f);

            List<SlimeColliderOctreeNode> nodes = new List<SlimeColliderOctreeNode>(max(1, colliderCount));
            List<int> indices = new List<int>(colliderCount);
            nodes.Add(default);

            BuildColliderOctreeNode(
                0,
                allColliders,
                rootCenter,
                rootHalfSize,
                0,
                max(1, collisionOctreeMaxDepth),
                max(1, collisionOctreeLeafCapacity),
                boundsMin,
                boundsMax,
                nodes,
                indices);

            _colliderOctreeNodes = new NativeArray<SlimeColliderOctreeNode>(nodes.Count, Allocator.Persistent);
            for (int i = 0; i < nodes.Count; i++)
                _colliderOctreeNodes[i] = nodes[i];

            _colliderOctreeIndices = new NativeArray<int>(indices.Count, Allocator.Persistent);
            for (int i = 0; i < indices.Count; i++)
                _colliderOctreeIndices[i] = indices[i];
        }

        static void BuildColliderOctreeNode(
            int nodeIndex,
            List<int> colliderIndices,
            float3 center,
            float halfSize,
            int depth,
            int maxDepth,
            int leafCapacity,
            float3[] boundsMin,
            float3[] boundsMax,
            List<SlimeColliderOctreeNode> nodes,
            List<int> outIndices)
        {
            int firstCollider = outIndices.Count;
            int colliderCount = colliderIndices.Count;
            int firstChild = -1;

            if (colliderIndices.Count > leafCapacity && depth < maxDepth)
            {
                float childHalfSize = halfSize * 0.5f;
                List<int> localColliders = new List<int>();
                List<int>[] childColliders = new List<int>[8];

                for (int i = 0; i < colliderIndices.Count; i++)
                {
                    int colliderIndex = colliderIndices[i];
                    int childIndex = GetContainingOctreeChild(
                        boundsMin[colliderIndex],
                        boundsMax[colliderIndex],
                        center);

                    if (childIndex < 0)
                    {
                        localColliders.Add(colliderIndex);
                        continue;
                    }

                    childColliders[childIndex] ??= new List<int>();
                    childColliders[childIndex].Add(colliderIndex);
                }

                bool hasChildColliders = false;
                for (int i = 0; i < childColliders.Length; i++)
                {
                    if (childColliders[i] != null && childColliders[i].Count > 0)
                    {
                        hasChildColliders = true;
                        break;
                    }
                }

                if (hasChildColliders)
                {
                    firstCollider = outIndices.Count;
                    for (int i = 0; i < localColliders.Count; i++)
                        outIndices.Add(localColliders[i]);

                    colliderCount = localColliders.Count;
                    firstChild = nodes.Count;
                    for (int i = 0; i < 8; i++)
                        nodes.Add(default);

                    for (int child = 0; child < 8; child++)
                    {
                        List<int> childList = childColliders[child] ?? s_EmptyColliderIndexList;
                        BuildColliderOctreeNode(
                            firstChild + child,
                            childList,
                            GetOctreeChildCenter(center, childHalfSize, child),
                            childHalfSize,
                            depth + 1,
                            maxDepth,
                            leafCapacity,
                            boundsMin,
                            boundsMax,
                            nodes,
                            outIndices);
                    }

                    nodes[nodeIndex] = new SlimeColliderOctreeNode
                    {
                        Center = center,
                        HalfSize = halfSize,
                        FirstChild = firstChild,
                        FirstCollider = firstCollider,
                        ColliderCount = colliderCount,
                    };
                    return;
                }
            }

            firstCollider = outIndices.Count;
            for (int i = 0; i < colliderIndices.Count; i++)
                outIndices.Add(colliderIndices[i]);

            nodes[nodeIndex] = new SlimeColliderOctreeNode
            {
                Center = center,
                HalfSize = halfSize,
                FirstChild = -1,
                FirstCollider = firstCollider,
                ColliderCount = colliderIndices.Count,
            };
        }

        static readonly List<int> s_EmptyColliderIndexList = new List<int>(0);

        static void GetColliderAabb(SlimeBoxCollider collider, out float3 minBounds, out float3 maxBounds)
        {
            float3 axisX = normalizesafe(collider.AxisX, new float3(1f, 0f, 0f));
            float3 axisY = normalizesafe(collider.AxisY, new float3(0f, 1f, 0f));
            float3 axisZ = normalizesafe(collider.AxisZ, new float3(0f, 0f, 1f));
            float3 extent = max(collider.Extent, new float3(0f));
            float3 aabbHalf =
                abs(axisX) * extent.x +
                abs(axisY) * extent.y +
                abs(axisZ) * extent.z;

            minBounds = collider.Center - aabbHalf;
            maxBounds = collider.Center + aabbHalf;
        }

        static int GetContainingOctreeChild(float3 minBounds, float3 maxBounds, float3 parentCenter)
        {
            int child = 0;

            if (maxBounds.x <= parentCenter.x)
            {
                child |= 0;
            }
            else if (minBounds.x >= parentCenter.x)
            {
                child |= 1;
            }
            else
            {
                return -1;
            }

            if (maxBounds.y <= parentCenter.y)
            {
                child |= 0;
            }
            else if (minBounds.y >= parentCenter.y)
            {
                child |= 2;
            }
            else
            {
                return -1;
            }

            if (maxBounds.z <= parentCenter.z)
            {
                child |= 0;
            }
            else if (minBounds.z >= parentCenter.z)
            {
                child |= 4;
            }
            else
            {
                return -1;
            }

            return child;
        }

        static float3 GetOctreeChildCenter(float3 parentCenter, float childHalfSize, int child)
        {
            return parentCenter + new float3(
                (child & 1) == 0 ? -childHalfSize : childHalfSize,
                (child & 2) == 0 ? -childHalfSize : childHalfSize,
                (child & 4) == 0 ? -childHalfSize : childHalfSize);
        }

        void DisposeColliderOctree()
        {
            if (_colliderOctreeNodes.IsCreated)
                _colliderOctreeNodes.Dispose();
            if (_colliderOctreeIndices.IsCreated)
                _colliderOctreeIndices.Dispose();
        }

        static bool TryBuildBoxCollider(Collider col, out SlimeBoxCollider slimeCollider)
        {
            slimeCollider = default;

            Transform t = col.transform;
            Bounds localBounds;
            if (col is BoxCollider box)
            {
                localBounds = new Bounds(box.center, box.size);
            }
            else if (col is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                localBounds = meshCollider.sharedMesh.bounds;
            }
            else
            {
                Bounds bounds = col.bounds;
                slimeCollider = new SlimeBoxCollider
                {
                    Center = (float3)bounds.center * SlimeConst.InvScale,
                    Extent = (float3)bounds.extents * SlimeConst.InvScale + new float3(1f, 1f, 1f),
                    AxisX = new float3(1f, 0f, 0f),
                    AxisY = new float3(0f, 1f, 0f),
                    AxisZ = new float3(0f, 0f, 1f),
                };
                return true;
            }

            float3 scale = abs((float3)t.lossyScale);
            slimeCollider = new SlimeBoxCollider
            {
                Center = (float3)t.TransformPoint(localBounds.center) * SlimeConst.InvScale,
                Extent = (float3)localBounds.extents * scale * SlimeConst.InvScale + new float3(1f, 1f, 1f),
                AxisX = normalizesafe((float3)t.right, new float3(1f, 0f, 0f)),
                AxisY = normalizesafe((float3)t.up, new float3(0f, 1f, 0f)),
                AxisZ = normalizesafe((float3)t.forward, new float3(0f, 0f, 1f)),
            };
            return true;
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
                _activeShapeSdfResolution == shapeSdfResolution &&
                _shapeTarget.RestLocalPositions.Length == _shapeRestLocalPositions.Length)
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
                targetTransform = ResolveDefaultShapeTargetTransform();
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
                ControlledGroupId = _groups.ControlledGroupId,
                GroupCentres = _groupCentres,
                GroupCounts = _groupCounts,
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
            var snapInitialRest = new NativeArray<float3>(_particles.Length, Allocator.TempJob);
            snapInitialRest.CopyFrom(_initialRestOffsets);
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
                InitialRestOffsets = snapInitialRest,
                ShapeRestLocalPositions = snapShapeRest,
                ShapeRestMorphFrom = snapShapeMorphFrom,
                ShapeRestMorphTo = snapShapeMorphTo,
                PosOld = _posOld,
                PosPredict = _posPredict,
                VelocityOut = _velocityTempBuffer,
                RestOffsetsOut = _restOffsetsShuffled,
                InitialRestOffsetsOut = _initialRestOffsetsShuffled,
                ShapeRestLocalPositionsOut = _shapeRestLocalPositionsShuffled,
                ShapeRestMorphFromOut = _shapeRestMorphFrom,
                ShapeRestMorphToOut = _shapeRestMorphTo,
                IdsOut = _shuffledIds,
            }.Schedule(_particles.Length, _batchCount).Complete();

            snapRest.Dispose();
            snapInitialRest.Dispose();
            snapShapeRest.Dispose();
            snapShapeMorphFrom.Dispose();
            snapShapeMorphTo.Dispose();

            // 5. Compute lambda
            new SlimeSimulation.ComputeLambdaJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Ids = _shuffledIds,
                Lambda = _lambdaBuffer,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 6. Compute delta positions
            new SlimeSimulation.ComputeDeltaPosJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                Lambda = _lambdaBuffer,
                Ids = _shuffledIds,
                PsNew = _particles,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 7. Static mesh shape target: XPBD-style position projection.
            if (HasShapeTarget())
            {
                // Recompute per-group COM (positions changed during the solve) so
                // each group projects against its own centre.
                new SlimeCohesion.ComputeCenterJob
                {
                    Particles = _particles,
                    Centres = _groupCentres,
                    Counts = _groupCounts,
                }.Schedule().Complete();

                new SlimeSimulation.ApplyShapeTargetJob
                {
                    Particles = _particles,
                    RestLocalPositions = _shapeRestLocalPositionsShuffled,
                    Centres = _groupCentres,
                    Counts = _groupCounts,
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
            }

            // 8. Update: floor + collider push-out + velocity reconstruction + clamp
            new SlimeSimulation.UpdateJob
            {
                Ps = _particles,
                PosOld = _posOld,
                Colliders = _colliderBuffer,
                ColliderOctreeNodes = _colliderOctreeNodes,
                ColliderOctreeIndices = _colliderOctreeIndices,
                Velocity = _velocityTempBuffer,
                EnableFloor = !_colliderBuffer.IsCreated || _colliderBuffer.Length == 0,
                UseColliderOctree = useCollisionOctree
                    && _colliderOctreeNodes.IsCreated
                    && _colliderOctreeNodes.Length > 0
                    && _colliderOctreeIndices.IsCreated,
                FloorY = 1f,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // 9. Viscosity
            new SlimeSimulation.ApplyViscosityJob
            {
                Lut = _lut,
                PosPredict = _posPredict,
                VelocityR = _velocityTempBuffer,
                Ids = _shuffledIds,
                VelocityW = _velocityBuffer,
                ViscosityStrength = viscosityStrength,
            }.Schedule(_particles.Length, _batchCount).Complete();

            // Sync restOffsets to current (cell-grouped) order for next sub-step
            _restOffsets.CopyFrom(_restOffsetsShuffled);
            _initialRestOffsets.CopyFrom(_initialRestOffsetsShuffled);
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
            if (_initialRestOffsets.IsCreated) _initialRestOffsets.Dispose();
            if (_initialRestOffsetsShuffled.IsCreated) _initialRestOffsetsShuffled.Dispose();
            if (_shapeRestLocalPositions.IsCreated) _shapeRestLocalPositions.Dispose();
            if (_shapeRestLocalPositionsShuffled.IsCreated) _shapeRestLocalPositionsShuffled.Dispose();
            if (_shapeRestMorphFrom.IsCreated) _shapeRestMorphFrom.Dispose();
            if (_shapeRestMorphTo.IsCreated) _shapeRestMorphTo.Dispose();
            if (_colliderBuffer.IsCreated) _colliderBuffer.Dispose();
            DisposeColliderOctree();
            if (_groupCentres.IsCreated) _groupCentres.Dispose();
            if (_groupRestCentres.IsCreated) _groupRestCentres.Dispose();
            if (_groupCounts.IsCreated) _groupCounts.Dispose();
            if (_groupStiffness.IsCreated) _groupStiffness.Dispose();
            if (_shuffledIds.IsCreated) _shuffledIds.Dispose();
            if (_shapeTarget.IsCreated) _shapeTarget.Dispose();
            _controllerMotor = null;
            _activeShapeTargetMesh = null;
            _activeShapeTargetTransform = null;
            _activeShapeSdfResolution = 0;
            _shapeRestMorphActive = false;
            _shapeLocalCenter = new float3(0f, 0f, 0f);
            _hasLastTransformPosition = false;
            _hasLastControllerBodyPosition = false;
            _lastControllerBodyGroupId = -1;
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

            // 1. Per-group centre-of-mass (order-independent; particles are shuffled).
            new SlimeCohesion.ComputeCenterJob
            {
                Particles = _particles,
                Centres = _groupCentres,
                Counts = _groupCounts,
            }.Schedule().Complete();

            // 2. Keep the controller list large enough for every allocated slot.
            while (_controllerBuffer.Length < _groups.SlotCount)
                _controllerBuffer.Add(new SlimeController());

            int ctrlId = _groups.ControlledGroupId;
            StoreControlledCohesionState();

            float3 controlledCom = new float3(0f, 0f, 0f);
            for (int g = 0; g < _groups.SlotCount; g++)
            {
                SlimeController c = _controllerBuffer[g];
                if (!_groups.IsAlive(g))
                {
                    // Dead slot: neutralise so any stray reference is harmless.
                    c.Center = new float3(0f, 0f, 0f);
                    c.Radius = 0f;
                    c.Velocity = new float3(0f, 0f, 0f);
                    c.Concentration = 0f;
                    _controllerBuffer[g] = c;
                    _groupStiffness[g] = 0f;
                    continue;
                }

                float3 com = _groupCounts[g] > 0 ? _groupCentres[g] / _groupCounts[g] : new float3(0f, 0f, 0f);
                c.Concentration = _groups.GetConcentration(g);
                _groupStiffness[g] = _groups.GetStiffness(g);

                if (g == ctrlId)
                {
                    controlledCom = com;
                    // Motor drives the controlled group's cohesion centre (WASD → position).
                    if (controllerBody != null)
                    {
                        c.Center = (float3)controllerBody.position * SlimeConst.InvScale;
                        c.Radius = SlimeConst.InvScale * 5f; // keep whole blob in range
                        c.Velocity = (float3)ResolveControllerVelocity() * SlimeConst.InvScale;
                    }
                    else
                    {
                        c.Center = com;
                        c.Radius = SlimeConst.InvScale;
                        c.Velocity = new float3(0f, 0f, 0f);
                    }
                }
                else
                {
                    // Non-controlled group: self-cohere around its own COM, no motion.
                    c.Center = com;
                    c.Radius = SlimeConst.InvScale * 2f;
                    c.Velocity = new float3(0f, 0f, 0f);
                }
                _controllerBuffer[g] = c;
            }

            CurrentWorldCenter = (Vector3)controlledCom * SlimeConst.Scale;

            // 3. Shape matching (translation-only, per group)
            if (enableShapeMatch && !HasShapeTarget())
            {
                new SlimeCohesion.ComputeRestCenterJob
                {
                    Particles = _particles,
                    RestOffsets = _restOffsetsShuffled,
                    RestCentres = _groupRestCentres,
                }.Schedule().Complete();

                new SlimeCohesion.ApplyShapeMatchJob
                {
                    RestOffsets = _restOffsetsShuffled,
                    RestCentres = _groupRestCentres,
                    Centres = _groupCentres,
                    Counts = _groupCounts,
                    Stiffness = _groupStiffness,
                    Particles = _particles,
                    Velocity = _velocityBuffer,
                    inverseDt = 1f / SlimeConst.DeltaTime,
                }.Schedule(_particles.Length, _batchCount).Complete();
            }
        }

        /// <summary>Currently controlled group id (the body the motor drives).</summary>
        public int ControlledGroupId => _groups != null ? _groups.ControlledGroupId : 0;

        /// <summary>Number of alive split bodies (including the original).</summary>
        public int AliveGroupCount => _groups != null ? _groups.AliveCount : 1;

        public int CohesionStep => Mathf.Clamp(currentstep, 1, 3);

        public float ControlledConcentration =>
            _groups != null && _groups.IsAlive(ControlledGroupId)
                ? _groups.GetConcentration(ControlledGroupId)
                : concentration;

        public float ControlledShapeStiffness =>
            _groups != null && _groups.IsAlive(ControlledGroupId)
                ? _groups.GetStiffness(ControlledGroupId)
                : shapeStiffness;

        public int ControlledGroupOrdinal
        {
            get
            {
                if (_groups == null)
                    return 1;

                int ordinal = 0;
                int controlled = _groups.ControlledGroupId;
                for (int groupId = 0; groupId < SlimeGroupManager.MaxGroups; groupId++)
                {
                    if (!_groups.IsAlive(groupId))
                        continue;

                    ordinal++;
                    if (groupId == controlled)
                        return ordinal;
                }

                return 1;
            }
        }

        public int ControlledParticleCount => GetGroupParticleCount(ControlledGroupId);

        public int GetGroupParticleCount(int groupId)
        {
            if (!_particles.IsCreated)
                return 0;

            int count = 0;
            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i].ID == groupId)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Horizontal facing direction (world space, normalised) used to aim splits.
        /// Falls back to the controller motor's facing, then world forward.
        /// </summary>
        Vector3 GetFacingDirection()
        {
            Vector3 forward = Vector3.forward;
            if (Camera.main != null) forward = Camera.main.transform.forward;
            else if (controllerBody != null) forward = controllerBody.transform.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        }

        /// <summary>
        /// Split the controlled body. A fraction of particles (the front-most along
        /// <paramref name="facingWorld"/>) peel off into a new body. Each resulting
        /// body receives a compact local rest shape, like the reference component
        /// controllers, so neither body keeps a sliced/full-body rest target.
        /// </summary>
        public bool Split(Vector3 facingWorld)
        {
            if (!_initialised || _groups == null) return false;
            StoreControlledCohesionState();
            float3 facing = (float3)facingWorld; // direction only — scale-invariant
            int sourceGroup = _groups.ControlledGroupId;
            int newGroup = _groups.Split(
                _particles, _velocityBuffer,
                facing, splitFraction, splitSeparation, minParticlesToSplit);
            if (newGroup < 0) return false;

            SlimeGroupManager.WriteCompactRestOffsets(
                _restOffsets, _particles, sourceGroup, _particles.Length);
            SlimeGroupManager.WriteCompactRestOffsets(
                _restOffsets, _particles, newGroup, _particles.Length);

            // Ensure the controller list has a slot for the new group before the
            // next Simulate() reads it (Control() pads too, but Simulate runs first).
            while (_controllerBuffer.Length < _groups.SlotCount)
                _controllerBuffer.Add(new SlimeController());

            // Brief absorb cooldown so the separation has time to take effect
            // before contact-detection can re-merge the bodies.
            _absorbCooldown = absorbCooldownTime;
            return true;
        }

        /// <summary>Cycle motor control to the next alive body.</summary>
        public int SwitchControl()
        {
            if (!_initialised || _groups == null) return 0;
            int previousId = _groups.ControlledGroupId;
            int newId = _groups.SwitchControl();
            if (newId != previousId)
                SyncCohesionFieldsFromGroup(newId);

            // Snap the kinematic motor to the newly controlled body so movement
            // resumes from its position instead of teleporting the blob.
            if (newId != previousId && controllerBody != null)
            {
                SlimeGroupManager.GroupBounds(_particles, newId, out float3 com, out _, out int count);
                if (count > 0)
                {
                    Vector3 worldCom = (Vector3)(com * SlimeConst.Scale);
                    CurrentWorldCenter = worldCom;
                    worldCom.y = max(worldCom.y, controllerBody.transform.position.y);
                    controllerBody.position = worldCom;
                    controllerBody.transform.position = worldCom;
                    _lastControllerBodyPosition = controllerBody.position;
                    _hasLastControllerBodyPosition = true;
                    _lastControllerBodyGroupId = newId;
                    ResetRigidbodyVelocity(controllerBody);
                }
            }
            return newId;
        }

        static void ResetRigidbodyVelocity(Rigidbody body)
        {
            if (body == null || body.isKinematic)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        void EnsureStatusHud()
        {
            if (!showRuntimeHud)
                return;

            if (_statusHud == null)
                _statusHud = GetComponentInChildren<SlimeStatusHud>(true);

            if (_statusHud == null)
            {
                GameObject hudObject = new GameObject("Slime Status HUD", typeof(RectTransform));
                hudObject.transform.SetParent(transform, false);
                _statusHud = hudObject.AddComponent<SlimeStatusHud>();
            }

            _statusHud.Bind(this);
        }

        /// <summary>
        /// Absorb check: if the controlled body contacts another body, merge it.
        /// Called automatically from FixedUpdate after simulation. Suppressed for
        /// a short window after each split so the ejection can separate the bodies.
        /// </summary>
        void UpdateAbsorb()
        {
            if (_absorbCooldown > 0f)
                _absorbCooldown = max(0f, _absorbCooldown - Time.fixedDeltaTime);
            if (_groups == null || _groups.AliveCount <= 1 || _absorbCooldown > 0f) return;

            int target = _groups.FindAbsorbTarget(_particles, absorbContactSlack, absorbRearmSlack);
            if (target >= 0)
                _groups.Absorb(_particles, _restOffsets, _initialRestOffsets, _velocityBuffer, target);
        }
    }
}
