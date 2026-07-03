using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Geometry Checkpoint Manager")]
    public sealed class SlimeGeometryCheckpointManager : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Transform initialCheckpoint;
        [SerializeField] bool captureTargetPositionOnAwake = true;
        [SerializeField] bool resetMotionOnRespawn = true;
        [SerializeField] UnityEvent onCheckpointChanged;
        [SerializeField] UnityEvent onRespawned;

        Vector3 _checkpointPosition;
        Quaternion _checkpointRotation = Quaternion.identity;
        bool _hasCheckpoint;

        public bool HasCheckpoint => _hasCheckpoint;
        public Transform Target => target;
        public UnityEvent OnCheckpointChanged => onCheckpointChanged;
        public UnityEvent OnRespawned => onRespawned;

        void Awake()
        {
            if (initialCheckpoint != null)
                SetCheckpoint(initialCheckpoint);
            else if (captureTargetPositionOnAwake)
                CaptureTargetCheckpoint();
        }

        public void Configure(Transform respawnTarget, Transform checkpoint)
        {
            target = respawnTarget;
            initialCheckpoint = checkpoint;

            if (checkpoint != null)
                SetCheckpoint(checkpoint);
            else if (captureTargetPositionOnAwake)
                CaptureTargetCheckpoint();
        }

        public void SetCheckpoint(Transform checkpoint)
        {
            if (checkpoint == null)
                return;

            SetCheckpoint(checkpoint.position, checkpoint.rotation);
        }

        public void SetCheckpoint(Vector3 worldPosition, Quaternion worldRotation)
        {
            _checkpointPosition = worldPosition;
            _checkpointRotation = worldRotation;
            _hasCheckpoint = true;
            onCheckpointChanged?.Invoke();
        }

        public bool Respawn()
        {
            if (!_hasCheckpoint)
                CaptureTargetCheckpoint();

            if (!_hasCheckpoint || target == null)
                return false;

            target.SetPositionAndRotation(_checkpointPosition, _checkpointRotation);

            if (resetMotionOnRespawn && target.TryGetComponent(out SlimeControllerMotor motor))
                motor.ResetMotion();

            onRespawned?.Invoke();
            return true;
        }

        void CaptureTargetCheckpoint()
        {
            if (target != null)
                SetCheckpoint(target.position, target.rotation);
            else
                SetCheckpoint(transform.position, transform.rotation);
        }
    }
}
