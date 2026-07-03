using UnityEngine;

namespace Slime
{
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
    }
}
