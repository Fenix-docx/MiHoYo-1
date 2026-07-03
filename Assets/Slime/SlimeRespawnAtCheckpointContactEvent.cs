using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Respawn At Checkpoint Contact Event")]
    public sealed class SlimeRespawnAtCheckpointContactEvent : SlimeContactEventHandler
    {
        [SerializeField] SlimeCheckpointManager checkpointManager;
        [SerializeField] bool autoFindCheckpointManager = true;
        [SerializeField] bool triggerOncePerContact = true;

        bool _hasTriggeredThisContact;

        public SlimeCheckpointManager CheckpointManager => checkpointManager;

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (triggerOncePerContact && _hasTriggeredThisContact)
                return;

            if (Respawn())
                _hasTriggeredThisContact = true;
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            _hasTriggeredThisContact = false;
        }

        public bool Respawn()
        {
            if (!ResolveCheckpointManager())
                return false;

            return checkpointManager.Respawn();
        }

        bool ResolveCheckpointManager()
        {
            if (checkpointManager != null)
                return true;

            if (autoFindCheckpointManager)
                checkpointManager = Object.FindAnyObjectByType<SlimeCheckpointManager>(FindObjectsInactive.Exclude);

            return checkpointManager != null;
        }
    }
}
