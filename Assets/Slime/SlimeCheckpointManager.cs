using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Checkpoint Manager")]
    public sealed class SlimeCheckpointManager : MonoBehaviour
    {
        [Header("Player")]
        [SerializeField] SlimeSimulator slimeSimulator;
        [SerializeField] bool autoFindSlimeSimulator = true;

        [Header("Checkpoint")]
        [SerializeField] Transform initialCheckpoint;
        [SerializeField] bool captureInitialPositionOnAwake = true;
        [SerializeField] bool updateTransformPositionOnCheckpoint = false;
        [SerializeField] bool resetVelocityOnRespawn = true;

        [Header("Events")]
        [SerializeField] UnityEvent onCheckpointChanged;
        [SerializeField] UnityEvent onRespawned;

        Vector3 _checkpointPosition;
        Quaternion _checkpointRotation = Quaternion.identity;
        bool _hasCheckpoint;

        public bool HasCheckpoint => _hasCheckpoint;
        public Vector3 CheckpointPosition => _checkpointPosition;
        public Quaternion CheckpointRotation => _checkpointRotation;
        public SlimeSimulator SlimeSimulator => slimeSimulator;
        public UnityEvent OnCheckpointChanged => onCheckpointChanged;
        public UnityEvent OnRespawned => onRespawned;

        void Awake()
        {
            ResolveSlimeSimulator();

            if (initialCheckpoint != null)
                SetCheckpoint(initialCheckpoint);
            else if (captureInitialPositionOnAwake)
                CaptureInitialCheckpoint();
        }

        public void SetCheckpoint(Transform checkpoint)
        {
            if (checkpoint == null)
                return;

            SetCheckpoint(checkpoint.position, checkpoint.rotation);
        }

        public void SetCheckpoint(Vector3 worldPosition)
        {
            SetCheckpoint(worldPosition, _hasCheckpoint ? _checkpointRotation : Quaternion.identity);
        }

        public void SetCheckpoint(Vector3 worldPosition, Quaternion worldRotation)
        {
            _checkpointPosition = worldPosition;
            _checkpointRotation = worldRotation;
            _hasCheckpoint = true;

            if (updateTransformPositionOnCheckpoint)
                transform.SetPositionAndRotation(worldPosition, worldRotation);

            onCheckpointChanged?.Invoke();
        }

        public bool Respawn()
        {
            if (!_hasCheckpoint)
                CaptureInitialCheckpoint();

            if (!_hasCheckpoint || !ResolveSlimeSimulator())
                return false;

            slimeSimulator.TeleportToWorldCenter(_checkpointPosition, _checkpointRotation, resetVelocityOnRespawn);
            onRespawned?.Invoke();
            return true;
        }

        void CaptureInitialCheckpoint()
        {
            if (ResolveSlimeSimulator())
                SetCheckpoint(slimeSimulator.CurrentWorldCenter, slimeSimulator.transform.rotation);
            else
                SetCheckpoint(transform.position, transform.rotation);
        }

        bool ResolveSlimeSimulator()
        {
            if (slimeSimulator != null)
                return true;

            if (autoFindSlimeSimulator)
                slimeSimulator = Object.FindAnyObjectByType<SlimeSimulator>(FindObjectsInactive.Exclude);

            return slimeSimulator != null;
        }
    }
}
