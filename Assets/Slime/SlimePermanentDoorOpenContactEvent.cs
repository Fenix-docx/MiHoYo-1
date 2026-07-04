using System;
using System.Collections.Generic;
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

        readonly List<Collider> _cachedDoorColliders = new List<Collider>(8);
        readonly HashSet<Collider> _cachedDoorColliderSet = new HashSet<Collider>();
        bool[] _initialColliderEnabled = Array.Empty<bool>();
        bool _hasCapturedInitialColliderState;
        bool _hasOpened;

        public bool HasOpened => _hasOpened;

        void Awake()
        {
            MigrateLegacyTriggerMode();
            RebuildDoorColliderCache();
            CaptureInitialColliderState();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();
            if (TryOpenDoor())
                RefreshSlimeObstacleCaches(context.SlimeSimulator);
        }

        public void OpenDoor()
        {
            MigrateLegacyTriggerMode();

            if (TryOpenDoor())
                RefreshSlimeObstacleCaches(null);
        }

        bool TryOpenDoor()
        {
            if (!CanStartContactEvent(triggerMode, _hasOpened))
                return false;

            RebuildDoorColliderCache();
            CaptureInitialColliderState();

            SetDoorCollidersEnabled(false);

            if (doorModel != null && doorModel.activeSelf)
                doorModel.SetActive(false);

            RefreshTargetTransforms();
            _hasOpened = true;
            return true;
        }

        public void ResetOpenState(bool restoreDoor)
        {
            _hasOpened = false;

            if (!restoreDoor)
                return;

            if (doorModel != null && !doorModel.activeSelf)
                doorModel.SetActive(true);

            RestoreDoorColliders();

            RefreshTargetTransforms();
            RefreshSlimeObstacleCaches(null);
            _hasCapturedInitialColliderState = false;
        }

        void RebuildDoorColliderCache()
        {
            _cachedDoorColliders.Clear();
            _cachedDoorColliderSet.Clear();

            AddDoorColliders(doorColliders);

            GameObject colliderRoot = doorModel != null ? doorModel : gameObject;
            AddDoorColliders(GetTargetColliders(colliderRoot, includeChildColliders));
        }

        void AddDoorColliders(Collider[] colliders)
        {
            if (colliders == null)
                return;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider doorCollider = colliders[i];
                if (doorCollider != null && _cachedDoorColliderSet.Add(doorCollider))
                    _cachedDoorColliders.Add(doorCollider);
            }
        }

        void CaptureInitialColliderState()
        {
            if (_hasCapturedInitialColliderState
                && _initialColliderEnabled.Length == _cachedDoorColliders.Count)
            {
                return;
            }

            _initialColliderEnabled = new bool[_cachedDoorColliders.Count];
            for (int i = 0; i < _cachedDoorColliders.Count; i++)
            {
                Collider doorCollider = _cachedDoorColliders[i];
                _initialColliderEnabled[i] = doorCollider == null || doorCollider.enabled;
            }

            _hasCapturedInitialColliderState = true;
        }

        void SetDoorCollidersEnabled(bool enabled)
        {
            for (int i = 0; i < _cachedDoorColliders.Count; i++)
            {
                Collider doorCollider = _cachedDoorColliders[i];
                if (doorCollider != null)
                    doorCollider.enabled = enabled;
            }
        }

        void RestoreDoorColliders()
        {
            int colliderCount = Mathf.Min(_cachedDoorColliders.Count, _initialColliderEnabled.Length);
            for (int i = 0; i < colliderCount; i++)
            {
                Collider doorCollider = _cachedDoorColliders[i];
                if (doorCollider != null)
                    doorCollider.enabled = _initialColliderEnabled[i];
            }
        }

        void RefreshSlimeObstacleCaches(SlimeSimulator contextSimulator)
        {
            if (contextSimulator != null && contextSimulator.isActiveAndEnabled)
            {
                contextSimulator.RebuildColliders();
                return;
            }

#if UNITY_6000_4_OR_NEWER
            SlimeSimulator[] simulators = UnityEngine.Object.FindObjectsByType<SlimeSimulator>(FindObjectsInactive.Exclude);
#else
            SlimeSimulator[] simulators = UnityEngine.Object.FindObjectsByType<SlimeSimulator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif

            for (int i = 0; i < simulators.Length; i++)
            {
                SlimeSimulator simulator = simulators[i];
                if (simulator != null && simulator.isActiveAndEnabled)
                    simulator.RebuildColliders();
            }
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
