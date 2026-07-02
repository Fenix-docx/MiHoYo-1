using UnityEngine;

namespace Slime
{

    [RequireComponent(typeof(Rigidbody))]
    public class SlimeControllerMotor : MonoBehaviour
    {
        [Header("Movement (world units / second)")]
        [Min(0f)] public float moveSpeed = 3f;
        [SerializeField]

        private float sprintSpeed = 3f;//加速倍数



   

        [Min(0f)] public float jumpSpeed = 3f;

        [Header("Facing")]
        [Tooltip("Camera transform used as the horizontal facing reference. Defaults to Camera.main.")]
        public Transform facingCamera;

        [Header("Ground")]
        [Tooltip("Resting world Y for the controller (blob resting centre-of-mass height).")]
        public float groundY = 0.5f;

        SlimeSimulator _simulator;
        Rigidbody _rb;
        float _vy;
        bool _grounded = true;
        bool _jumpQueued;


        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            _simulator = GetComponentInParent<SlimeSimulator>();

            Vector3 p = transform.position;
            if (p.y < groundY) p.y = groundY;
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
            float speed = Input.GetKey(KeyCode.LeftShift) ? moveSpeed*sprintSpeed : moveSpeed;
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

            if (pos.y <= groundY)
            {
                pos.y = groundY;
                _vy = 0f;
                _grounded = true;
            }

            _rb.MovePosition(pos);
            _rb.MoveRotation(rotation);
            // Expose the commanded velocity for the simulator to read back.
            _rb.linearVelocity = new Vector3(v.x, _vy, v.z);

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
    }
}