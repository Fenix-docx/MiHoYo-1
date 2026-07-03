using UnityEngine;

namespace Slime
{
    public class CameraFollow_Slime : MonoBehaviour
    {
        [Header("跟随目标")]
        public Vector3 target;
        [SerializeField] SlimeSimulator slimeSimulator;

        [Header("相机设置")]
        public float followSpeed = 5f;
        public float rotationSpeed = 5f;
        public Vector3 cameraOffset = new Vector3(0, 2, -5);

        [Header("Camera Zoom")]
        public bool enableZoom = true;
        [Min(0f)] public float zoomSpeed = 5f;
        [Min(0.01f)] public float minZoomDistance = 2f;
        [Min(0.01f)] public float maxZoomDistance = 10f;

        [Header("Camera Damping")]
        [Min(0f)] public float smoothTime = 0.3f;
        [Min(0f)] public float lookAtHeight = 1f;

        [Header("Camera Collision")]
        public bool enableCollision = true;
        public LayerMask collisionMask = ~0;
        [Min(0.01f)] public float collisionRadius = 0.35f;
        [Min(0f)] public float collisionPadding = 0.08f;
        [Min(0f)] public float minCollisionDistance = 0.15f;

        [Header("相机旋转控制")]
        public float horizontalRotateSpeed = 3f;
        public float verticalRotateSpeed = 2f;
        public float minVerticalAngle = -30f;
        public float maxVerticalAngle = 60f;
        public bool inputEnable = true;

        private float cameraAngleX = 0f;
        private float cameraAngleY = 0f;
        private Vector3 smoothDampVelocity = Vector3.zero;
        private readonly RaycastHit[] collisionHits = new RaycastHit[16];

        private const float MinCastDistance = 0.001f;

        void Start()
        {
            ResolveSlimeSimulator();
            Cursor.visible = false;//鼠标隐藏
        }

        void LateUpdate()
        {
            if (!ResolveSlimeSimulator())
                return;

            target = slimeSimulator.CurrentWorldCenter;

            if (inputEnable && InputManager.Instance != null && InputManager.Instance.inputEnable)
            {
                ApplyOrbitInput(
                    ref cameraAngleX,
                    ref cameraAngleY,
                    InputManager.Instance.GetMouseX(),
                    InputManager.Instance.GetMouseY(),
                    horizontalRotateSpeed,
                    verticalRotateSpeed,
                    minVerticalAngle,
                    maxVerticalAngle);

                if (enableZoom)
                    cameraOffset = SetOffsetDistance(
                        cameraOffset,
                        CalculateZoomDistance(
                            cameraOffset.magnitude,
                            ReadScrollDelta(),
                            zoomSpeed,
                            minZoomDistance,
                            maxZoomDistance));
            }

            Quaternion rotation = Quaternion.Euler(cameraAngleX, cameraAngleY, 0);
            Vector3 rotatedOffset = rotation * cameraOffset;

            Vector3 lookTarget = target + Vector3.up * lookAtHeight;
            Vector3 targetPosition = ResolveCameraPosition(
                lookTarget,
                target + rotatedOffset,
                out bool collisionAdjusted);

            Vector3 nextPosition = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref smoothDampVelocity,
                Mathf.Max(MinCastDistance, smoothTime),
                followSpeed > 0f ? followSpeed : float.PositiveInfinity,
                Time.deltaTime
            );

            if (collisionAdjusted && IsBeyondCollisionPosition(lookTarget, nextPosition, targetPosition))
            {
                nextPosition = targetPosition;
                smoothDampVelocity = Vector3.zero;
            }

            transform.position = nextPosition;
            RotateToward(lookTarget);
        }

        public float GetCameraAngleY()
        {
            return cameraAngleY;
        }

        public Vector3 GetCameraForward()
        {
            return transform.forward;
        }

        public static void ApplyOrbitInput(
            ref float verticalAngle,
            ref float horizontalAngle,
            float mouseX,
            float mouseY,
            float horizontalSpeed,
            float verticalSpeed,
            float minVerticalAngle,
            float maxVerticalAngle)
        {
            horizontalAngle += mouseX * horizontalSpeed;
            verticalAngle = Mathf.Clamp(
                verticalAngle - mouseY * verticalSpeed,
                minVerticalAngle,
                maxVerticalAngle);
        }

        public static float CalculateZoomDistance(
            float currentDistance,
            float scrollDelta,
            float zoomSpeed,
            float minDistance,
            float maxDistance)
        {
            float min = Mathf.Max(MinCastDistance, minDistance);
            float max = Mathf.Max(min, maxDistance);
            return Mathf.Clamp(currentDistance - scrollDelta * Mathf.Max(0f, zoomSpeed), min, max);
        }

        bool ResolveSlimeSimulator()
        {
            if (slimeSimulator != null)
                return true;

            slimeSimulator = GetComponentInParent<SlimeSimulator>();
            if (slimeSimulator == null)
                slimeSimulator = Object.FindAnyObjectByType<SlimeSimulator>();

            return slimeSimulator != null;
        }

        static void ReadMouseDelta(out float mouseX, out float mouseY)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            mouseX = Input.GetAxis("Mouse X");
            mouseY = Input.GetAxis("Mouse Y");
#else
            mouseX = 0f;
            mouseY = 0f;
#endif
        }

        static float ReadScrollDelta()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetAxis("Mouse ScrollWheel");
#else
            return 0f;
#endif
        }

        static Vector3 SetOffsetDistance(Vector3 offset, float distance)
        {
            if (offset.sqrMagnitude <= MinCastDistance * MinCastDistance)
                return new Vector3(0f, 0f, -distance);

            return offset.normalized * distance;
        }

        Vector3 ResolveCameraPosition(Vector3 lookTarget, Vector3 desiredPosition, out bool collisionAdjusted)
        {
            collisionAdjusted = false;

            Vector3 toDesired = desiredPosition - lookTarget;
            float desiredDistance = toDesired.magnitude;
            if (!enableCollision || desiredDistance <= MinCastDistance)
                return desiredPosition;

            Vector3 direction = toDesired / desiredDistance;
            int hitCount = Physics.SphereCastNonAlloc(
                lookTarget,
                Mathf.Max(MinCastDistance, collisionRadius),
                direction,
                collisionHits,
                desiredDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore);

            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = collisionHits[i];
                if (hit.collider == null || ShouldIgnoreCollision(hit.collider))
                    continue;

                nearestDistance = Mathf.Min(nearestDistance, hit.distance);
            }

            if (float.IsInfinity(nearestDistance))
                return desiredPosition;

            return CalculateCollisionAdjustedPosition(
                lookTarget,
                desiredPosition,
                nearestDistance,
                collisionPadding,
                minCollisionDistance,
                out collisionAdjusted);
        }

        bool ShouldIgnoreCollision(Collider hitCollider)
        {
            Transform hitTransform = hitCollider.transform;
            return hitTransform.IsChildOf(transform)
                || slimeSimulator != null && hitTransform.IsChildOf(slimeSimulator.transform);
        }

        void RotateToward(Vector3 lookTarget)
        {
            Vector3 lookDirection = lookTarget - transform.position;
            if (lookDirection.sqrMagnitude <= MinCastDistance * MinCastDistance)
                return;

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            float damping = Mathf.Max(0f, rotationSpeed);
            if (damping <= MinCastDistance)
            {
                transform.rotation = targetRotation;
                return;
            }

            float blend = 1f - Mathf.Exp(-damping * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);
        }

        static bool IsBeyondCollisionPosition(Vector3 lookTarget, Vector3 position, Vector3 collisionPosition)
        {
            Vector3 collisionOffset = collisionPosition - lookTarget;
            float collisionDistance = collisionOffset.magnitude;
            if (collisionDistance <= MinCastDistance)
                return true;

            Vector3 collisionDirection = collisionOffset / collisionDistance;
            float positionDistance = Vector3.Dot(position - lookTarget, collisionDirection);
            return positionDistance > collisionDistance;
        }

        static Vector3 CalculateCollisionAdjustedPosition(
            Vector3 lookTarget,
            Vector3 desiredPosition,
            float obstacleDistance,
            float padding,
            float minDistance,
            out bool collisionAdjusted)
        {
            collisionAdjusted = false;

            Vector3 toDesired = desiredPosition - lookTarget;
            float desiredDistance = toDesired.magnitude;
            if (desiredDistance <= MinCastDistance)
                return desiredPosition;

            float safeDistance = obstacleDistance - Mathf.Max(0f, padding);
            float clampedMinDistance = Mathf.Max(0f, minDistance);
            if (safeDistance < clampedMinDistance && clampedMinDistance < obstacleDistance)
                safeDistance = clampedMinDistance;

            safeDistance = Mathf.Max(0f, safeDistance);
            safeDistance = Mathf.Min(safeDistance, desiredDistance);

            collisionAdjusted = safeDistance < desiredDistance;
            return lookTarget + toDesired / desiredDistance * safeDistance;
        }
    }
}
