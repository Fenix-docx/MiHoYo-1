using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Visibility Swap Contact Event")]
    public sealed class SlimeVisibilitySwapContactEvent : SlimeContactEventHandler
    {
        [SerializeField] Transform firstObject;
        [SerializeField] Transform secondObject;
        [SerializeField] Collider[] firstColliders;
        [SerializeField] Collider[] secondColliders;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField] bool firstVisibleAtStart = true;
        [SerializeField] bool applyInitialStateOnAwake = true;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;

        bool _hasTriggered;
        bool _showFirst;
        bool _hasInitialized;

        public Transform FirstObject => firstObject;
        public Transform SecondObject => secondObject;
        public bool ShowingFirst => _showFirst;

        void Awake()
        {
            CacheColliders();
            InitializeState();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            InitializeState();
            SetVisibleState(!_showFirst);
            _hasTriggered = true;
        }

        public void ResetSwapState(bool applyInitialState)
        {
            _hasTriggered = false;
            _hasInitialized = false;
            _showFirst = firstVisibleAtStart;
            CacheColliders();

            if (applyInitialState)
                ApplyVisibility();
        }

        void InitializeState()
        {
            if (_hasInitialized)
                return;

            _showFirst = firstVisibleAtStart;
            _hasInitialized = true;

            if (applyInitialStateOnAwake)
                ApplyVisibility();
        }

        void SetVisibleState(bool showFirst)
        {
            _showFirst = showFirst;
            ApplyVisibility();
        }

        void ApplyVisibility()
        {
            SetTargetActive(firstObject, _showFirst);
            SetTargetActive(secondObject, !_showFirst);
            SetCollidersEnabled(firstColliders, _showFirst);
            SetCollidersEnabled(secondColliders, !_showFirst);
            RefreshTargetTransforms();
        }

        static void SetTargetActive(Transform target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active)
                target.gameObject.SetActive(active);
        }

        void CacheColliders()
        {
            if (firstColliders == null || firstColliders.Length == 0)
                firstColliders = GetTargetColliders(firstObject != null ? firstObject.gameObject : null, includeChildColliders);

            if (secondColliders == null || secondColliders.Length == 0)
                secondColliders = GetTargetColliders(secondObject != null ? secondObject.gameObject : null, includeChildColliders);
        }
    }
}
