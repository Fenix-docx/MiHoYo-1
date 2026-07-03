using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Door Visibility Contact Event")]
    public sealed class SlimeDoorVisibilityContactEvent : SlimeContactEventHandler
    {
        [SerializeField] GameObject objectToHide;
        [SerializeField] bool restoreObjectWhenContactEnds = true;

        bool _initialObjectActiveSelf = true;
        bool _hasCapturedInitialState;

        public GameObject ObjectToHide => objectToHide;

        void Awake()
        {
            CaptureInitialObjectState();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            CaptureInitialObjectState();

            if (objectToHide != null && objectToHide.activeSelf)
                objectToHide.SetActive(false);
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            CaptureInitialObjectState();

            if (objectToHide != null
                && restoreObjectWhenContactEnds
                && objectToHide.activeSelf != _initialObjectActiveSelf)
            {
                objectToHide.SetActive(_initialObjectActiveSelf);
            }
        }

        public void ResetCapturedState()
        {
            _hasCapturedInitialState = false;
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
