using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Rotate Contact Event")]
    public sealed class SlimeRotateContactEvent : SlimeContactEventHandler
    {
        public enum RotationSpace
        {
            Local,
            World,
        }

        [SerializeField] Transform target;
        [SerializeField] RotationSpace rotationSpace = RotationSpace.Local;
        [SerializeField] Vector3 rotationAxis = Vector3.up;
        [SerializeField] float rotationAngle = 90f;
        [SerializeField] bool triggerOnce = true;
        [SerializeField] bool restoreWhenContactEnds = false;

        Quaternion _initialLocalRotation;
        Quaternion _initialWorldRotation;
        bool _hasCapturedInitialRotation;
        bool _hasRotated;

        public Transform Target => target;
        public Vector3 RotationAxis => rotationAxis;
        public float RotationAngle => rotationAngle;
        public bool HasRotated => _hasRotated;

        void Awake()
        {
            CaptureInitialRotation();
        }

        void OnValidate()
        {
            if (rotationAxis.sqrMagnitude <= 0.0001f)
                rotationAxis = Vector3.up;
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (triggerOnce && _hasRotated)
                return;

            RotateTarget();
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            if (!restoreWhenContactEnds)
                return;

            RestoreInitialRotation();
            _hasRotated = false;
        }

        public void RotateTarget()
        {
            Transform rotateTarget = ResolveTarget();
            if (rotateTarget == null)
                return;

            CaptureInitialRotation();

            Vector3 axis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
            Quaternion delta = Quaternion.AngleAxis(rotationAngle, axis);

            if (rotationSpace == RotationSpace.Local)
                rotateTarget.localRotation = rotateTarget.localRotation * delta;
            else
                rotateTarget.rotation = delta * rotateTarget.rotation;

            _hasRotated = true;
        }

        public void RestoreInitialRotation()
        {
            Transform rotateTarget = ResolveTarget();
            if (rotateTarget == null || !_hasCapturedInitialRotation)
                return;

            if (rotationSpace == RotationSpace.Local)
                rotateTarget.localRotation = _initialLocalRotation;
            else
                rotateTarget.rotation = _initialWorldRotation;
        }

        public void ResetRotationState()
        {
            _hasRotated = false;
            _hasCapturedInitialRotation = false;
            CaptureInitialRotation();
        }

        void CaptureInitialRotation()
        {
            Transform rotateTarget = ResolveTarget();
            if (_hasCapturedInitialRotation || rotateTarget == null)
                return;

            _initialLocalRotation = rotateTarget.localRotation;
            _initialWorldRotation = rotateTarget.rotation;
            _hasCapturedInitialRotation = true;
        }

        Transform ResolveTarget()
        {
            return target != null ? target : transform;
        }
    }
}
