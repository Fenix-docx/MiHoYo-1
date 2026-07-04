using UnityEngine;

namespace Slime
{
    public enum SlimeContactEventTriggerMode
    {
        [InspectorName("仅一次")]
        Once,

        [InspectorName("可重复启动")]
        RepeatableStart,

        [InspectorName("循环")]
        Loop,
    }

    public readonly struct SlimeContactEventContext
    {
        public SlimeContactEventContext(
            SlimeDoorSwitchOnContact contactSwitch,
            SlimeSurfaceBuilder slimeSurface,
            SlimeSimulator slimeSimulator)
        {
            ContactSwitch = contactSwitch;
            SlimeSurface = slimeSurface;
            SlimeSimulator = slimeSimulator;
        }

        public SlimeDoorSwitchOnContact ContactSwitch { get; }
        public SlimeSurfaceBuilder SlimeSurface { get; }
        public SlimeSimulator SlimeSimulator { get; }
        public bool HasContact => ContactSwitch != null && ContactSwitch.HasContact;
    }

    public interface ISlimeContactEventHandler
    {
        void HandleContactBegan(SlimeContactEventContext context);
        void HandleContactEnded(SlimeContactEventContext context);
    }

    public abstract class SlimeContactEventHandler : MonoBehaviour, ISlimeContactEventHandler
    {
        public virtual void HandleContactBegan(SlimeContactEventContext context) { }
        public virtual void HandleContactEnded(SlimeContactEventContext context) { }

        protected static bool CanStartContactEvent(SlimeContactEventTriggerMode triggerMode, bool hasTriggered)
        {
            return triggerMode != SlimeContactEventTriggerMode.Once || !hasTriggered;
        }

        protected static bool ShouldLoopContactEvent(SlimeContactEventTriggerMode triggerMode)
        {
            return triggerMode == SlimeContactEventTriggerMode.Loop;
        }

        protected static Collider[] GetTargetColliders(GameObject target, bool includeChildren)
        {
            if (target == null)
                return System.Array.Empty<Collider>();

            return includeChildren
                ? target.GetComponentsInChildren<Collider>(true)
                : target.GetComponents<Collider>();
        }

        protected static void SetCollidersEnabled(Collider[] colliders, bool enabled)
        {
            if (colliders == null)
                return;

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider targetCollider = colliders[i];
                if (targetCollider != null)
                    targetCollider.enabled = enabled;
            }
        }

        protected static void RefreshTargetTransforms()
        {
            Physics.SyncTransforms();
        }
    }
}
