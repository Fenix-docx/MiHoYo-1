using System;
using System.Collections;
using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Blink Sequence Controller Contact Event")]
    public sealed class SlimeBlinkSequenceControllerContactEvent : SlimeContactEventHandler
    {
        [Serializable]
        public sealed class BlinkTarget
        {
            public GameObject target;
            public bool visibleDuringStep = true;
        }

        [SerializeField] BlinkTarget[] blinkTargets = Array.Empty<BlinkTarget>();
        [SerializeField, Min(0.01f)] float stepInterval = 0.2f;
        [SerializeField, Min(1)] int cycles = 1;
        [SerializeField] bool hideAllBeforeSequence = true;
        [SerializeField] bool restoreInitialVisibilityAfterSequence = false;
        [SerializeField] bool finalVisible = true;
        [SerializeField] bool useUnscaledTime = false;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField, HideInInspector] bool loopWhileContact = false;
        [SerializeField, HideInInspector] bool triggerOnce = false;
        [SerializeField, HideInInspector] bool triggerModeMigrated;

        Coroutine _sequenceRoutine;
        bool[] _initialVisibility = Array.Empty<bool>();
        bool _hasCapturedInitialVisibility;
        bool _hasTriggered;

        public BlinkTarget[] BlinkTargets => blinkTargets;
        public float StepInterval => stepInterval;
        public bool IsRunning => _sequenceRoutine != null;

        void Awake()
        {
            MigrateLegacyTriggerMode();
            CaptureInitialVisibility();
        }

        void OnValidate()
        {
            MigrateLegacyTriggerMode();
            stepInterval = Mathf.Max(0.01f, stepInterval);
            cycles = Mathf.Max(1, cycles);
        }

        void OnDisable()
        {
            StopSequence();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();

            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            StartSequence();
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            if (!ShouldLoopContactEvent(triggerMode))
                return;

            StopSequence();
            ApplyFinalVisibility();
        }

        public void StartSequence()
        {
            MigrateLegacyTriggerMode();

            if (blinkTargets == null || blinkTargets.Length == 0)
                return;

            CaptureInitialVisibility();

            if (_sequenceRoutine != null)
                StopCoroutine(_sequenceRoutine);

            _hasTriggered = true;
            _sequenceRoutine = StartCoroutine(SequenceRoutine());
        }

        public void StopSequence()
        {
            if (_sequenceRoutine == null)
                return;

            StopCoroutine(_sequenceRoutine);
            _sequenceRoutine = null;
        }

        public void ResetSequenceState()
        {
            _hasTriggered = false;
            _hasCapturedInitialVisibility = false;
            CaptureInitialVisibility();
        }

        IEnumerator SequenceRoutine()
        {
            bool loop = ShouldLoopContactEvent(triggerMode);

            do
            {
                int repeatCount = loop ? int.MaxValue : cycles;
                for (int cycle = 0; cycle < repeatCount; cycle++)
                {
                    for (int i = 0; i < blinkTargets.Length; i++)
                    {
                        ApplySequenceStep(i);
                        yield return WaitForInterval();
                    }
                }
            }
            while (loop);

            ApplyFinalVisibility();
            _sequenceRoutine = null;
        }

        void ApplySequenceStep(int activeIndex)
        {
            for (int i = 0; i < blinkTargets.Length; i++)
            {
                GameObject target = GetTarget(i);
                if (target == null)
                    continue;

                if (i == activeIndex)
                    target.SetActive(blinkTargets[i].visibleDuringStep);
                else if (hideAllBeforeSequence)
                    target.SetActive(false);
            }
        }

        void ApplyFinalVisibility()
        {
            if (restoreInitialVisibilityAfterSequence)
            {
                RestoreInitialVisibility();
                return;
            }

            for (int i = 0; i < blinkTargets.Length; i++)
            {
                GameObject target = GetTarget(i);
                if (target != null)
                    target.SetActive(finalVisible);
            }
        }

        void CaptureInitialVisibility()
        {
            if (_hasCapturedInitialVisibility)
                return;

            int targetCount = blinkTargets != null ? blinkTargets.Length : 0;
            _initialVisibility = new bool[targetCount];

            for (int i = 0; i < targetCount; i++)
            {
                GameObject target = GetTarget(i);
                _initialVisibility[i] = target == null || target.activeSelf;
            }

            _hasCapturedInitialVisibility = true;
        }

        void RestoreInitialVisibility()
        {
            if (!_hasCapturedInitialVisibility)
                return;

            int targetCount = Mathf.Min(blinkTargets != null ? blinkTargets.Length : 0, _initialVisibility.Length);
            for (int i = 0; i < targetCount; i++)
            {
                GameObject target = GetTarget(i);
                if (target != null)
                    target.SetActive(_initialVisibility[i]);
            }
        }

        object WaitForInterval()
        {
            if (useUnscaledTime)
                return new WaitForSecondsRealtime(stepInterval);

            return new WaitForSeconds(stepInterval);
        }

        GameObject GetTarget(int index)
        {
            if (blinkTargets == null || index < 0 || index >= blinkTargets.Length)
                return null;

            return blinkTargets[index] != null ? blinkTargets[index].target : null;
        }

        void MigrateLegacyTriggerMode()
        {
            if (triggerModeMigrated)
                return;

            if (loopWhileContact)
                triggerMode = SlimeContactEventTriggerMode.Loop;
            else
                triggerMode = triggerOnce
                    ? SlimeContactEventTriggerMode.Once
                    : SlimeContactEventTriggerMode.RepeatableStart;

            triggerModeMigrated = true;
        }
    }
}
