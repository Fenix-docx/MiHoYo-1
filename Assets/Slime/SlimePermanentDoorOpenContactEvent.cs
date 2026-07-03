using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Permanent Door Open Contact Event")]
    public sealed class SlimePermanentDoorOpenContactEvent : SlimeContactEventHandler
    {
        [SerializeField] GameObject doorModel;
        [SerializeField] Collider[] doorColliders;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.Once;
        [SerializeField, HideInInspector] bool triggerOnce = true;
        [SerializeField, HideInInspector] bool triggerModeMigrated;

        bool _hasOpened;

        public bool HasOpened => _hasOpened;

        void Awake()
        {
            MigrateLegacyTriggerMode();
            CacheDoorColliders();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();
            OpenDoor();
        }

        public void OpenDoor()
        {
            if (!CanStartContactEvent(triggerMode, _hasOpened))
                return;

            CacheDoorColliders();

            if (doorModel != null)
                doorModel.SetActive(false);

            if (doorColliders != null)
            {
                for (int i = 0; i < doorColliders.Length; i++)
                {
                    Collider doorCollider = doorColliders[i];
                    if (doorCollider != null)
                        doorCollider.enabled = false;
                }
            }

            _hasOpened = true;
        }

        public void ResetOpenState(bool restoreDoor)
        {
            _hasOpened = false;

            if (!restoreDoor)
                return;

            if (doorModel != null)
                doorModel.SetActive(true);

            if (doorColliders != null)
            {
                for (int i = 0; i < doorColliders.Length; i++)
                {
                    Collider doorCollider = doorColliders[i];
                    if (doorCollider != null)
                        doorCollider.enabled = true;
                }
            }
        }

        void CacheDoorColliders()
        {
            if (doorColliders != null && doorColliders.Length > 0)
                return;

            GameObject colliderRoot = doorModel != null ? doorModel : gameObject;
            doorColliders = includeChildColliders
                ? colliderRoot.GetComponentsInChildren<Collider>(true)
                : colliderRoot.GetComponents<Collider>();
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
