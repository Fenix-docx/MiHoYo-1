using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Geometry Checkpoint Zone")]
    public sealed class SlimeGeometryCheckpointZone : MonoBehaviour
    {
        [SerializeField] SlimeGeometryCheckpointManager checkpointManager;
        [SerializeField] Transform checkpointLocation;
        [SerializeField] bool activateOnce = true;
        [SerializeField] UnityEvent onCheckpointActivated;

        bool _activated;

        public bool HasActivated => _activated;
        public SlimeGeometryCheckpointManager CheckpointManager => checkpointManager;
        public UnityEvent OnCheckpointActivated => onCheckpointActivated;

        public void Configure(SlimeGeometryCheckpointManager manager, Transform location, bool once)
        {
            checkpointManager = manager;
            checkpointLocation = location;
            activateOnce = once;
        }

        void OnTriggerEnter(Collider other)
        {
            TryActivate(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryActivate(other);
        }

        public void ResetActivationState()
        {
            _activated = false;
        }

        bool TryActivate(Collider other)
        {
            if ((activateOnce && _activated) || other == null)
                return false;

            SlimeControllerMotor motor = other.GetComponentInParent<SlimeControllerMotor>();
            if (motor == null || checkpointManager == null)
                return false;

            Transform location = checkpointLocation != null ? checkpointLocation : transform;
            checkpointManager.SetCheckpoint(location.position, location.rotation);
            _activated = true;
            onCheckpointActivated?.Invoke();
            return true;
        }
    }
}
