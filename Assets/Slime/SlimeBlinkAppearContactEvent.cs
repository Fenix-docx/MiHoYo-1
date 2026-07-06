using System.Collections;
using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Blink Appear Contact Event")]
    public sealed class SlimeBlinkAppearContactEvent : SlimeContactEventHandler
    {
        [SerializeField] GameObject target;
        [SerializeField, Min(0.01f)] float blinkFrequencyHz = 5f;
        [SerializeField, Min(1)] int blinkCount = 6;
        [SerializeField] bool finalVisible = true;
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.Once;
        [SerializeField] bool useUnscaledTime = false;
        [SerializeField, HideInInspector] bool triggerOnce = true;
        [SerializeField, HideInInspector] bool triggerModeMigrated;

        Coroutine _blinkRoutine;
        bool _hasTriggered;

        public GameObject Target => target;
        public float BlinkFrequencyHz => blinkFrequencyHz;
        public bool HasTriggered => _hasTriggered;

        void Awake()
        {
            MigrateLegacyTriggerMode();
        }

        void OnValidate()
        {
            MigrateLegacyTriggerMode();
            blinkFrequencyHz = Mathf.Max(0.01f, blinkFrequencyHz);
            blinkCount = Mathf.Max(1, blinkCount);
        }

        void OnDisable()
        {
            StopBlink(false);
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            MigrateLegacyTriggerMode();

            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            StartBlink(ShouldLoopContactEvent(triggerMode));
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            if (!ShouldLoopContactEvent(triggerMode))
                return;

            StopBlink(true);
        }

        public void StartBlink()
        {
            StartBlink(ShouldLoopContactEvent(triggerMode));
        }

        public void StopBlink(bool applyFinalState)
        {
            if (_blinkRoutine == null)
                return;

            StopCoroutine(_blinkRoutine);
            _blinkRoutine = null;

            if (!applyFinalState)
                return;

            GameObject blinkTarget = ResolveTarget();
            if (blinkTarget != null)
            {
                blinkTarget.SetActive(finalVisible);
                RefreshTargetTransforms();
            }
        }

        void StartBlink(bool loop)
        {
            GameObject blinkTarget = ResolveTarget();
            if (blinkTarget == null)
                return;

            if (_blinkRoutine != null)
                StopCoroutine(_blinkRoutine);

            _hasTriggered = true;
            _blinkRoutine = StartCoroutine(BlinkRoutine(blinkTarget, loop));
        }

        public void ResetBlinkState()
        {
            _hasTriggered = false;
        }

        IEnumerator BlinkRoutine(GameObject blinkTarget, bool loop)
        {
            do
            {
                for (int i = 0; i < blinkCount; i++)
                {
                    if (blinkTarget == null)
                        yield break;

                    blinkTarget.SetActive(!blinkTarget.activeSelf);
                    RefreshTargetTransforms();
                    yield return WaitForInterval();
                }
            }
            while (loop);

            if (blinkTarget != null)
            {
                blinkTarget.SetActive(finalVisible);
                RefreshTargetTransforms();
            }

            _blinkRoutine = null;
        }

        object WaitForInterval()
        {
            float blinkInterval = 1f / Mathf.Max(0.01f, blinkFrequencyHz);

            if (useUnscaledTime)
                return new WaitForSecondsRealtime(blinkInterval);

            return new WaitForSeconds(blinkInterval);
        }

        GameObject ResolveTarget()
        {
            return target != null ? target : gameObject;
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
