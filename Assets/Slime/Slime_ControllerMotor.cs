using UnityEngine;

namespace Slime
{

    [RequireComponent(typeof(Rigidbody))]
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

        SlimeSimulator _simulator;
        Rigidbody _rb;
        float _vy;
        bool _grounded = true;
        bool _jumpQueued;

        public Vector3 CommandedVelocity { get; private set; }

        const int GroundHitCapacity = 16;
        static readonly RaycastHit[] GroundHits = new RaycastHit[GroundHitCapacity];


        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _simulator = GetComponentInParent<SlimeSimulator>();

            Vector3 p = transform.position;
            float resolvedGroundY = ResolveGroundY(p);
            if (p.y < resolvedGroundY) p.y = resolvedGroundY;
            transform.position = p;
        }
        


        void Update()
        {
            // Capture jump in Update so single-frame GetKeyDown isn't missed by FixedUpdate.
            if (Input.GetKeyDown(KeyCode.Space) && _grounded)
                _jumpQueued = true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
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
