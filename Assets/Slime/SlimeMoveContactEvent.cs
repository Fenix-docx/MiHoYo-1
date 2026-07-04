using System.Collections;
using UnityEngine;

namespace Slime
{
    [AddComponentMenu("Slime/Events/Move Contact Event")]
    public sealed class SlimeMoveContactEvent : SlimeContactEventHandler
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3 localOffset = new Vector3(0f, 0f, 3f);
        [SerializeField] SlimeContactEventTriggerMode triggerMode = SlimeContactEventTriggerMode.RepeatableStart;
        [SerializeField, Min(0f)] float moveDuration = 0.75f;
        [SerializeField] bool restoreWhenContactEnds;

        Vector3 _initialLocalPosition;
        Coroutine _moveRoutine;
        Coroutine _loopRoutine;
        bool _hasCapturedInitialPosition;
        bool _hasTriggered;
        bool _isAtOffset;

        public Transform Target => target;
        public Vector3 LocalOffset => localOffset;
        public bool IsAtOffset => _isAtOffset;

        void Awake()
        {
            CaptureInitialPosition();
        }

        void OnValidate()
        {
            moveDuration = Mathf.Max(0f, moveDuration);
        }

        void OnDisable()
        {
            StopMovement();
        }

        public override void HandleContactBegan(SlimeContactEventContext context)
        {
            if (!CanStartContactEvent(triggerMode, _hasTriggered))
                return;

            CaptureInitialPosition();

            if (ShouldLoopContactEvent(triggerMode))
            {
                StartLoopingMovement();
            }
            else
            {
                MoveToState(!_isAtOffset);
                _hasTriggered = true;
            }
        }

        public override void HandleContactEnded(SlimeContactEventContext context)
        {
            StopLoopingMovement();

            if (!restoreWhenContactEnds)
                return;

            MoveToState(false);
        }

        public void ResetMoveState(bool restorePosition)
        {
            StopMovement();
            _hasTriggered = false;
            _isAtOffset = false;
            _hasCapturedInitialPosition = false;
            CaptureInitialPosition();

            if (restorePosition && ResolveTarget() != null)
                ResolveTarget().localPosition = _initialLocalPosition;
        }

        void CaptureInitialPosition()
        {
            Transform moveTarget = ResolveTarget();
            if (_hasCapturedInitialPosition || moveTarget == null)
                return;

            _initialLocalPosition = moveTarget.localPosition;
            _hasCapturedInitialPosition = true;
        }

        Transform ResolveTarget()
        {
            return target != null ? target : transform;
        }

        void MoveToState(bool atOffset)
        {
            Transform moveTarget = ResolveTarget();
            if (moveTarget == null)
                return;

            StopMoveRoutine();

            Vector3 destination = _initialLocalPosition + (atOffset ? localOffset : Vector3.zero);
            _isAtOffset = atOffset;

            if (moveDuration <= 0f)
            {
                moveTarget.localPosition = destination;
                RefreshTargetTransforms();
                return;
            }

            _moveRoutine = StartCoroutine(MoveToPosition(moveTarget, destination));
        }

        IEnumerator MoveToPosition(Transform moveTarget, Vector3 destination)
        {
            Vector3 start = moveTarget.localPosition;
            float elapsed = 0f;

            while (elapsed < moveDuration)
            {
                elapsed += Time.deltaTime;
                float t = moveDuration <= 0f ? 1f : Mathf.Clamp01(elapsed / moveDuration);
                t = Mathf.SmoothStep(0f, 1f, t);
                moveTarget.localPosition = Vector3.LerpUnclamped(start, destination, t);
                RefreshTargetTransforms();
                yield return null;
            }

            moveTarget.localPosition = destination;
            RefreshTargetTransforms();
            _moveRoutine = null;
        }

        void StartLoopingMovement()
        {
            if (_loopRoutine != null)
                return;

            _loopRoutine = StartCoroutine(LoopMovement());
        }

        IEnumerator LoopMovement()
        {
            while (true)
            {
                bool nextState = !_isAtOffset;
                MoveToState(nextState);

                while (_moveRoutine != null)
                    yield return null;

                yield return null;
            }
        }

        void StopMovement()
        {
            StopLoopingMovement();
            StopMoveRoutine();
        }

        void StopLoopingMovement()
        {
            if (_loopRoutine == null)
                return;

            StopCoroutine(_loopRoutine);
            _loopRoutine = null;
        }

        void StopMoveRoutine()
        {
            if (_moveRoutine == null)
                return;

            StopCoroutine(_moveRoutine);
            _moveRoutine = null;
        }
    }
}
