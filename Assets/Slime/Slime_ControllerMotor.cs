using UnityEngine;

namespace Slime
{

    [RequireComponent(typeof(Rigidbody))]
    public class SlimeControllerMotor : MonoBehaviour
    {
        [Header("Movement (world units / second)")]
        [Min(0f)] public float moveSpeed = 3f;
        [Min(0f)] public float jumpSpeed = 3f;

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

            Vector3 v = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical")) * moveSpeed;

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
            // Expose the commanded velocity for the simulator to read back.
            _rb.linearVelocity = new Vector3(v.x, _vy, v.z);

        }
    }
}
