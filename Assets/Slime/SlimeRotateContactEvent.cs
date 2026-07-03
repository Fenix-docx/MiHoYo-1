using System.Collections;
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
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.Once;
        [SerializeField, Min(0.01f)] float loopInterval = 0.25f;
        [SerializeField] bool restoreWhenContactEnds = false;
        [SerializeField, HideInInspector] bool triggerOnce = true;
        [SerializeField, HideInInspector] bool triggerModeMigrated;

        Quaternion _initialLocalRotation;
        Quaternion _initialWorldRotation;
        Coroutine _loopRoutine;
        bool _hasCapturedInitialRotation;
        bool _hasTriggered;
        bool _hasRotated;

        public Transform Target => target;
        public Vector3 RotationAxis => rotationAxis;
        public float RotationAngle => rotationAngle;
        public bool HasRotated => _hasRotated;

        void Awake()
        {
            MigrateLegacyTriggerMode();
            CaptureInitialRotation();
        }

        void OnValidate()
        {
            MigrateLegacyTriggerMode();
            loopInterval = Mathf.Max(0.01f, loopInterval);

            if (rotationAxis.sqrMagnitude <= 0.0001f)
                rotationAxis = Vector3.up;
        }

        void OnDisable()
        {
            StopLoopingRotation();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();

            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            if (ShouldLoopContactEvent(triggerMode))
                StartLoopingRotation();
            else
                RotateTarget();
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            StopLoopingRotation();

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
            _hasTriggered = true;
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
            StopLoopingRotation();
            _hasTriggered = false;
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

        void StartLoopingRotation()
        {
            if (_loopRoutine != null)
                StopCoroutine(_loopRoutine);

            _loopRoutine = StartCoroutine(RotationLoop());
        }

        void StopLoopingRotation()
        {
            if (_loopRoutine == null)
                return;

            StopCoroutine(_loopRoutine);
            _loopRoutine = null;
        }

        IEnumerator RotationLoop()
        {
            do
            {
                RotateTarget();
                yield return new WaitForSeconds(loopInterval);
            }
            while (true);
        }

        void MigrateLegacyTriggerMode()
        {
            if (triggerModeMigrated)
                return;

            triggerMode = triggerOnce
                ? SlimeContactEventTriggerMode.Once
                : SlimeContactEventTriggerMode.RepeatableStart;
            triggerModeMigrated = true;
        }
    }
}
