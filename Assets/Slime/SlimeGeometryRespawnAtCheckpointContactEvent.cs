using System.Collections;
using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Geometry Respawn At Checkpoint Contact Event")]
    public sealed class SlimeGeometryRespawnAtCheckpointContactEvent : SlimeContactEventHandler
    {
        [SerializeField] SlimeGeometryCheckpointManager checkpointManager;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField, Min(0.01f)] float loopInterval = 0.25f;
        [SerializeField, HideInInspector] bool triggerOncePerContact = true;
        [SerializeField, HideInInspector] bool triggerModeMigrated;

        Coroutine _loopRoutine;
        bool _hasTriggered;
        bool _hasTriggeredThisContact;

        public SlimeGeometryCheckpointManager CheckpointManager => checkpointManager;

        public void Configure(SlimeGeometryCheckpointManager manager)
        {
            MigrateLegacyTriggerMode();
            checkpointManager = manager;
        }

        void Awake()
        {
            MigrateLegacyTriggerMode();
        }

        void OnValidate()
        {
            MigrateLegacyTriggerMode();
            loopInterval = Mathf.Max(0.01f, loopInterval);
        }

        void OnDisable()
        {
            StopRespawnLoop();
            _hasTriggeredThisContact = false;
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();

            if (!CanStartContactEvent(triggerMode, _hasTriggered)
                || (triggerMode == SlimeContactEventTriggerMode.RepeatableStart && _hasTriggeredThisContact))
                return;

            if (ShouldLoopContactEvent(triggerMode))
                StartRespawnLoop();
            else
                TryRespawnOnce();
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            StopRespawnLoop();
            _hasTriggeredThisContact = false;
        }

        public bool Respawn()
        {
            return checkpointManager != null && checkpointManager.Respawn();
        }

        void TryRespawnOnce()
        {
            if (!Respawn())
                return;

            _hasTriggered = true;
            _hasTriggeredThisContact = true;
        }

        void StartRespawnLoop()
        {
            if (_loopRoutine != null)
                StopCoroutine(_loopRoutine);

            _loopRoutine = StartCoroutine(RespawnLoop());
        }

        void StopRespawnLoop()
        {
            if (_loopRoutine == null)
                return;

            StopCoroutine(_loopRoutine);
            _loopRoutine = null;
        }

        IEnumerator RespawnLoop()
        {
            do
            {
                TryRespawnOnce();
                yield return new WaitForSeconds(loopInterval);
            }
            while (true);
        }

        void MigrateLegacyTriggerMode()
        {
            if (triggerModeMigrated)
                return;

            _ = triggerOncePerContact; // Legacy field is kept so Unity can deserialize old scenes.
            triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
            triggerModeMigrated = true;
        }
    }
}
