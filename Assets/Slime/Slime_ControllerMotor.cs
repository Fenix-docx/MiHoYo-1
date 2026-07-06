using UnityEngine;

namespace Slime
{

    [RequireComponent(typeof(Rigidbody), typeof(AudioSource))]
    public class SlimeControllerMotor : MonoBehaviour
    {
        [Header("Movement (world units / second)")]
        [Min(0f)] public float moveSpeed = 3f;
        [SerializeField]

        private float sprintMultiplier = 3f;//加速倍数
        [Min(0f)] public float jumpSpeed = 3f;

        [Header("Facing")]
        [Tooltip("Camera transform used as the horizontal facing reference. Defaults to Camera.main.")]
        public Transform facingCamera;

        [Header("Ground")]
        [Tooltip("Resting centre height above sampled ground. Used as an absolute world Y fallback when no scene ground is found.")]
        public float groundY = 0.5f;
        [Tooltip("Sample scene colliders below the controller so elevated levels do not fall back to world Y = 0.")]
        public bool useSceneGround = true;
        [Min(0f)] public float groundProbeStartHeight = 2f;
        [Min(0.1f)] public float groundProbeDistance = 12f;
        public LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [Header("音频播放")]
        public AudioSource audioSource;
        [SerializeField] AudioSource oneShotAudioSource;
        public AudioClip jumpSound;
        public float jumpVolume = 0.8f;
        public AudioClip collisionSound;
        public float collisionVolume = 0.8f;
        [Min(0f)] public float collisionMinImpactSpeed = 0.5f;
        [Min(0.01f)] public float collisionMaxImpactSpeed = 5f;
        [Min(0f)] public float collisionSoundInterval = 0.12f;
        public AudioClip movementSound;
        public float movementVolume = 0.8f;
        public AudioClip splitSound;
        public float splitVolume = 0.8f;

        SlimeSimulator _simulator;
        Rigidbody _rb;
        float _vy;
        bool _grounded = true;
        bool _jumpQueued;
        bool _subscribedToSimulator;
        float _lastCollisionSoundTime = -999f;

        public Vector3 CommandedVelocity { get; private set; }

        const int GroundHitCapacity = 16;
        static readonly RaycastHit[] GroundHits = new RaycastHit[GroundHitCapacity];
        // public bool enableCollisionEvents = true;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _simulator = GetComponentInParent<SlimeSimulator>();
            ResolveAudioSources();
            BindSimulatorForAudioEvents();

            Vector3 p = transform.position;
            float resolvedGroundY = ResolveGroundY(p);
            if (p.y < resolvedGroundY) p.y = resolvedGroundY;
            transform.position = p;
        }

        void OnEnable()
        {
            BindSimulatorForAudioEvents();
        }

        void OnDisable()
        {
            UnbindSimulatorAudioEvents();
        }

        void Start()
        {
            ResolveAudioSources();
            BindSimulatorForAudioEvents();
        }


        void Update()
        {
            if (!_subscribedToSimulator)
                BindSimulatorForAudioEvents();

            // Capture jump in Update so single-frame GetKeyDown isn't missed by FixedUpdate.
            if (Input.GetKeyDown(KeyCode.Space) && _grounded)
            {
                _jumpQueued = true;
                PlayOneShot(jumpSound, jumpVolume);
            }
        }

        void FixedUpdate()
        {
            if (!_subscribedToSimulator)
                BindSimulatorForAudioEvents();

            float dt = Time.fixedDeltaTime;

            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            ResolveAudioSources();
            if (input.magnitude > 0.1f)
            {
                if (audioSource != null && !audioSource.isPlaying)
                {
                    audioSource.loop = true;
                    audioSource.clip = movementSound;
                    audioSource.volume = movementVolume;
                    audioSource.Play();
                }
                print("播放移动音效");
            }
            else
            {
                if (audioSource != null)
                {
                    audioSource.loop = false;
                    audioSource.clip = null;
                    audioSource.volume = 0f;
                    audioSource.Stop();
                }
            }
            Quaternion rotation = ResolveCameraFacingRotation(_rb.rotation);
            float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed*sprintMultiplier : moveSpeed;
            Vector3 v = CalculatePlanarMoveVelocity(rotation, input, speed);

            if (_jumpQueued)
            {
                _vy = jumpSpeed;
                _grounded = false;
                _jumpQueued = false;
            }

            _vy += (_simulator != null ? _simulator.gravity : -9.81f) * dt;

            Vector3 pos = transform.position;
            pos += new Vector3(v.x, _vy, v.z) * dt;

            float resolvedGroundY = ResolveGroundY(pos);
            if (pos.y <= resolvedGroundY)
            {
                pos.y = resolvedGroundY;
                _vy = 0f;
                _grounded = true;
            }

            CommandedVelocity = new Vector3(v.x, _vy, v.z);

            _rb.MovePosition(pos);
            _rb.MoveRotation(rotation);

        }

        public void ResetMotion()
        {
            _vy = 0f;
            _grounded = true;
            _jumpQueued = false;
            CommandedVelocity = Vector3.zero;

            if (_rb == null)
                _rb = GetComponent<Rigidbody>();

            if (_rb != null)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                }
            }
        }

        public bool BindSimulatorForAudioEvents()
        {
            if (_rb == null)
                _rb = GetComponent<Rigidbody>();

            SlimeSimulator resolved = ResolveSimulatorForAudioEvents();
            if (resolved == null)
                return false;

            if (_simulator == resolved && _subscribedToSimulator)
            {
                ApplyCollisionEventSettings(resolved);
                return true;
            }

            UnbindSimulatorAudioEvents();
            _simulator = resolved;
            ApplyCollisionEventSettings(_simulator);
            _simulator.OnSlimeCollision += HandleSlimeCollision;
            _simulator.OnSlimeSplit += HandleSlimeSplit;
            _subscribedToSimulator = true;
            return true;
        }

        void UnbindSimulatorAudioEvents()
        {
            if (_simulator != null && _subscribedToSimulator)
            {
                _simulator.OnSlimeCollision -= HandleSlimeCollision;
                _simulator.OnSlimeSplit -= HandleSlimeSplit;
            }

            _subscribedToSimulator = false;
        }

        void ApplyCollisionEventSettings(SlimeSimulator simulator)
        {
            if (simulator == null)
                return;

            simulator.enableCollisionEvents = true;
            simulator.minCollisionEventSpeed = Mathf.Min(
                simulator.minCollisionEventSpeed,
                Mathf.Max(0f, collisionMinImpactSpeed));
        }

        SlimeSimulator ResolveSimulatorForAudioEvents()
        {
            SlimeSimulator parentSimulator = GetComponentInParent<SlimeSimulator>();
            if (parentSimulator != null)
                return parentSimulator;

            SlimeSimulator[] simulators =
                UnityEngine.Object.FindObjectsByType<SlimeSimulator>(FindObjectsInactive.Exclude);
            SlimeSimulator onlySceneSimulator = null;
            SlimeSimulator nearestSceneSimulator = null;
            int sceneSimulatorCount = 0;
            float nearestDistanceSq = float.PositiveInfinity;

            for (int i = 0; i < simulators.Length; i++)
            {
                SlimeSimulator simulator = simulators[i];
                if (simulator == null || simulator.gameObject.scene != gameObject.scene)
                    continue;

                if (_rb != null && simulator.controllerBody == _rb)
                    return simulator;

                sceneSimulatorCount++;
                onlySceneSimulator = simulator;

                float distanceSq = (simulator.transform.position - transform.position).sqrMagnitude;
                if (distanceSq < nearestDistanceSq)
                {
                    nearestDistanceSq = distanceSq;
                    nearestSceneSimulator = simulator;
                }
            }

            return sceneSimulatorCount == 1 ? onlySceneSimulator : nearestSceneSimulator;
        }

        void HandleSlimeCollision(SlimeCollisionEventData collision)
        {
            // TryPlayCollisionSound(collision.ImpactSpeed);
        }

        void OnCollisionEnter(Collision collision)
        {
            float impactSpeed = collision.relativeVelocity.magnitude;
            if (impactSpeed <= 0f)
                impactSpeed = CommandedVelocity.magnitude;

            // TryPlayCollisionSound(impactSpeed);
        }

        // void TryPlayCollisionSound(float impactSpeed)
        // {
        //     if (Time.time - _lastCollisionSoundTime < collisionSoundInterval)
        //         return;

        //     float volume = CalculateCollisionSoundVolume(
        //         impactSpeed,
        //         collisionMinImpactSpeed,
        //         collisionMaxImpactSpeed,
        //         collisionVolume);
        //     if (volume <= 0f)
        //         return;

        //     _lastCollisionSoundTime = Time.time;
        //     PlayOneShot(collisionSound, volume);
        // }

        void HandleSlimeSplit()
        {
            PlayOneShot(splitSound, splitVolume);
        }

        void PlayOneShot(AudioClip clip, float volume)
        {
            if (clip == null)
                return;

            ResolveAudioSources();
            AudioSource source = oneShotAudioSource != null ? oneShotAudioSource : audioSource;
            if (source == null)
                return;

            source.PlayOneShot(clip, Mathf.Max(0f, volume));
        }

        void ResolveAudioSources()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (oneShotAudioSource == null)
            {
                AudioSource[] sources = GetComponents<AudioSource>();
                if (sources.Length > 1)
                {
                    oneShotAudioSource = sources[1];
                }
                else if (audioSource != null)
                {
                    oneShotAudioSource = gameObject.AddComponent<AudioSource>();
                    oneShotAudioSource.playOnAwake = false;
                    oneShotAudioSource.spatialBlend = audioSource.spatialBlend;
                    oneShotAudioSource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
                }
                else
                {
                    oneShotAudioSource = audioSource;
                }
            }
        }

        public static float CalculateCollisionSoundVolume(
            float impactSpeed,
            float minImpactSpeed,
            float maxImpactSpeed,
            float maxVolume)
        {
            if (impactSpeed < minImpactSpeed || maxVolume <= 0f)
                return 0f;

            float upper = Mathf.Max(minImpactSpeed + 0.0001f, maxImpactSpeed);
            return Mathf.InverseLerp(minImpactSpeed, upper, impactSpeed) * maxVolume;
        }

        public static Vector3 CalculatePlanarMoveVelocity(Transform movementSpace, Vector2 input, float speed)
        {
            return CalculatePlanarMoveVelocity(movementSpace != null ? movementSpace.rotation : Quaternion.identity, input, speed);
        }

        public static Vector3 CalculatePlanarMoveVelocity(Quaternion movementRotation, Vector2 input, float speed)
        {
            Vector3 right = movementRotation * Vector3.right;
            Vector3 forward = movementRotation * Vector3.forward;

            right = FlattenPlanarDirection(right, Vector3.right);
            forward = FlattenPlanarDirection(forward, Vector3.forward);

            return (right * input.x + forward * input.y) * speed;
        }

        public static Quaternion CalculateCameraFacingRotation(Quaternion currentRotation, Vector3 cameraForward)
        {
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude <= 0.0001f)
                return currentRotation;

            return Quaternion.LookRotation(cameraForward.normalized, Vector3.up);
        }

        static Vector3 FlattenPlanarDirection(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : fallback;
        }

        Quaternion ResolveCameraFacingRotation(Quaternion currentRotation)
        {
            Transform source = facingCamera;
            if (source == null && Camera.main != null)
                source = Camera.main.transform;

            return source != null ? CalculateCameraFacingRotation(currentRotation, source.forward) : currentRotation;
        }

        float ResolveGroundY(Vector3 position)
        {
            if (!useSceneGround)
                return groundY;

            return TryFindSceneGround(position, out float surfaceY)
                ? surfaceY + groundY
                : groundY;
        }

        bool TryFindSceneGround(Vector3 position, out float surfaceY)
        {
            surfaceY = 0f;

            float startHeight = Mathf.Max(0f, groundProbeStartHeight);
            float distance = startHeight + Mathf.Max(0.1f, groundProbeDistance);
            Vector3 origin = position + Vector3.up * startHeight;
            int layerMask = groundLayers.value != 0 ? groundLayers.value : Physics.DefaultRaycastLayers;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                GroundHits,
                distance,
                layerMask,
                QueryTriggerInteraction.Ignore);

            float maxSurfaceY = position.y + Mathf.Max(0f, groundY) + 0.25f;
            float bestSurfaceY = float.NegativeInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = GroundHits[i];
                Collider hitCollider = hit.collider;
                if (hitCollider == null)
                    continue;

                if (hitCollider.attachedRigidbody == _rb)
                    continue;

                Transform hitTransform = hitCollider.transform;
                if (hitTransform == transform
                    || hitTransform.IsChildOf(transform)
                    || transform.IsChildOf(hitTransform))
                    continue;

                if (hit.point.y > maxSurfaceY)
                    continue;

                if (hit.point.y > bestSurfaceY)
                    bestSurfaceY = hit.point.y;
            }

            if (float.IsNegativeInfinity(bestSurfaceY))
                return false;

            surfaceY = bestSurfaceY;
            return true;
        }
    }
}
