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
        [SerializeField] bool triggerOnce = true;
        [SerializeField] bool useUnscaledTime = false;

        Coroutine _blinkRoutine;
        bool _hasTriggered;

        public GameObject Target => target;
        public float BlinkFrequencyHz => blinkFrequencyHz;
        public bool HasTriggered => _hasTriggered;

        void OnValidate()
        {
            blinkFrequencyHz = Mathf.Max(0.01f, blinkFrequencyHz);
            blinkCount = Mathf.Max(1, blinkCount);
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (triggerOnce && _hasTriggered)
                return;

            StartBlink();
        }

        public void StartBlink()
        {
            GameObject blinkTarget = ResolveTarget();
            if (blinkTarget == null)
                return;

            if (_blinkRoutine != null)
                StopCoroutine(_blinkRoutine);

            _hasTriggered = true;
            _blinkRoutine = StartCoroutine(BlinkRoutine(blinkTarget));
        }

        public void ResetBlinkState()
        {
            _hasTriggered = false;
        }

        IEnumerator BlinkRoutine(GameObject blinkTarget)
        {
            for (int i = 0; i < blinkCount; i++)
            {
                if (blinkTarget == null)
                    yield break;

                blinkTarget.SetActive(!blinkTarget.activeSelf);
                yield return WaitForInterval();
            }

            if (blinkTarget != null)
                blinkTarget.SetActive(finalVisible);

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
    }
}
