using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Door Visibility Contact Event")]
    public sealed class SlimeDoorVisibilityContactEvent : SlimeContactEventHandler
    {
        [SerializeField] GameObject objectToHide;
        [SerializeField] Collider[] objectColliders;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField] bool restoreObjectWhenContactEnds = true;

        bool _initialObjectActiveSelf = true;
        bool[] _initialColliderEnabled = System.Array.Empty<bool>();
        bool _hasCapturedInitialState;
        bool _hasTriggered;
        bool _isContactApplied;

        public GameObject ObjectToHide => objectToHide;

        void Awake()
        {
            CacheObjectColliders();
            CaptureInitialObjectState();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            CaptureInitialObjectState();

            if (objectToHide != null && objectToHide.activeSelf)
                objectToHide.SetActive(false);

            SetCollidersEnabled(objectColliders, false);
            RefreshTargetTransforms();

            _hasTriggered = true;
            _isContactApplied = true;
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            if (!_isContactApplied)
                return;

            CaptureInitialObjectState();

            if (objectToHide != null
                && restoreObjectWhenContactEnds
                && objectToHide.activeSelf != _initialObjectActiveSelf)
            {
                objectToHide.SetActive(_initialObjectActiveSelf);
            }

            if (restoreObjectWhenContactEnds)
            {
                RestoreObjectColliders();
                RefreshTargetTransforms();
            }

            _isContactApplied = false;
        }

        public void ResetCapturedState()
        {
            _hasCapturedInitialState = false;
            _hasTriggered = false;
            _isContactApplied = false;
            CacheObjectColliders();
            CaptureInitialObjectState();
        }

        void CaptureInitialObjectState()
        {
            if (_hasCapturedInitialState)
                return;

            CacheObjectColliders();
            _initialObjectActiveSelf = objectToHide == null || objectToHide.activeSelf;
            _initialColliderEnabled = objectColliders != null ? new bool[objectColliders.Length] : System.Array.Empty<bool>();

            for (int i = 0; i < _initialColliderEnabled.Length; i++)
            {
                Collider targetCollider = objectColliders[i];
                _initialColliderEnabled[i] = targetCollider == null || targetCollider.enabled;
            }

            _hasCapturedInitialState = true;
        }

        void CacheObjectColliders()
        {
            if (objectColliders != null && objectColliders.Length > 0)
                return;

            objectColliders = GetTargetColliders(objectToHide, includeChildColliders);
        }

        void RestoreObjectColliders()
        {
            if (objectColliders == null)
                return;

            int colliderCount = Mathf.Min(objectColliders.Length, _initialColliderEnabled.Length);
            for (int i = 0; i < colliderCount; i++)
            {
                Collider targetCollider = objectColliders[i];
                if (targetCollider != null)
                    targetCollider.enabled = _initialColliderEnabled[i];
            }
        }
    }
}
