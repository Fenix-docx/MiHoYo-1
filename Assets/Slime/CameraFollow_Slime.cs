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

        [Header("相机旋转控制")]
        public float horizontalRotateSpeed = 3f;
        public float verticalRotateSpeed = 2f;
        public float minVerticalAngle = -30f;
        public float maxVerticalAngle = 60f;

        private float cameraAngleX = 0f;
        private float cameraAngleY = 0f;
        private Vector3 smoothDampVelocity = Vector3.zero;
        public float smoothTime = 0.3f;

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

            if (InputManager.Instance != null && InputManager.Instance.inputEnable)
            {
                cameraAngleX += InputManager.Instance.GetMouseX() * horizontalRotateSpeed;
                cameraAngleY += InputManager.Instance.GetMouseY() * verticalRotateSpeed;
                cameraAngleX = Mathf.Clamp(cameraAngleX, minVerticalAngle, maxVerticalAngle);
            }

            Quaternion rotation = Quaternion.Euler(cameraAngleX, cameraAngleY, 0);
            Vector3 rotatedOffset = rotation * cameraOffset;

            Vector3 targetPosition = target + rotatedOffset;
            transform.position = Vector3.SmoothDamp(
                transform.position,
                targetPosition,
                ref smoothDampVelocity,
                smoothTime
            );

            transform.LookAt(target + Vector3.up * 1f);
        }

        public float GetCameraAngleY()
        {
            return cameraAngleY;
        }

        public Vector3 GetCameraForward()
        {
            return transform.forward;
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
    }
}
