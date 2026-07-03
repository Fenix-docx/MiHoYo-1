using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Door Visibility Contact Event")]
    public sealed class SlimeDoorVisibilityContactEvent : SlimeContactEventHandler
    {
        [SerializeField] GameObject objectToHide;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField] bool restoreObjectWhenContactEnds = true;

        bool _initialObjectActiveSelf = true;
        bool _hasCapturedInitialState;
        bool _hasTriggered;
        bool _isContactApplied;

        public GameObject ObjectToHide => objectToHide;

        void Awake()
        {
            CaptureInitialObjectState();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            CaptureInitialObjectState();

            if (objectToHide != null && objectToHide.activeSelf)
                objectToHide.SetActive(false);

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

            _isContactApplied = false;
        }

        public void ResetCapturedState()
        {
            _hasCapturedInitialState = false;
            _hasTriggered = false;
            _isContactApplied = false;
            CaptureInitialObjectState();
        }

        void CaptureInitialObjectState()
        {
            if (_hasCapturedInitialState)
                return;

            _initialObjectActiveSelf = objectToHide == null || objectToHide.activeSelf;
            _hasCapturedInitialState = true;
        }
    }
}
