using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Contact Switch On Contact")]
    public class SlimeDoorSwitchOnContact : MonoBehaviour
    {
        [Header("Slime Contact")]
        [SerializeField] SlimeSurfaceBuilder slimeSurface;
        [SerializeField] SlimeSimulator slimeSimulator;
        [SerializeField] bool autoFindSlimeSurface = true;
        [SerializeField] bool useSlimeSurfaceContact = true;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField, Min(0f)] float contactPadding = 0.05f;
        [SerializeField, Min(1)] int maxSurfaceVertexChecks = 4096;

        [Header("Unity Physics Contact")]
        [SerializeField] bool usePhysicsCallbacks = true;
        [SerializeField] bool onlyPhysicsContactFromSlime = true;

        [Header("Events")]
        [SerializeField] SlimeContactEventHandler[] eventHandlers = Array.Empty<SlimeContactEventHandler>();
        [SerializeField] UnityEvent onContactBegan;
        [SerializeField] UnityEvent onContactEnded;

        [SerializeField, HideInInspector] GameObject objectToHide;
        [SerializeField, HideInInspector] bool restoreObjectWhenContactEnds = true;

        readonly HashSet<Collider> _physicsContacts = new HashSet<Collider>();
        readonly List<Vector3> _surfaceVertices = new List<Vector3>(4096);

        Collider[] _contactColliders = Array.Empty<Collider>();
        bool _initialObjectActiveSelf = true;
        bool _hasContact;

        public bool HasContact => _hasContact;
        public GameObject ObjectToHide => objectToHide;
        public IReadOnlyList<SlimeContactEventHandler> EventHandlers => eventHandlers;
        public SlimeSurfaceBuilder SlimeSurface => slimeSurface;
        public SlimeSimulator SlimeSimulator => slimeSimulator;
        public UnityEvent OnContactBegan => onContactBegan;
        public UnityEvent OnContactEnded => onContactEnded;

        void Awake()
        {
            CaptureInitialDoorState();
            RefreshContactColliders();
            ResolveSlimeSurface();
        }

        void OnValidate()
        {
            contactPadding = Mathf.Max(0f, contactPadding);
            maxSurfaceVertexChecks = Mathf.Max(1, maxSurfaceVertexChecks);
        }

        void OnEnable()
        {
            RefreshContactColliders();
            ApplyLegacyObjectState();
        }

        void LateUpdate()
        {
            bool isTouching = HasValidPhysicsContact();

            if (useSlimeSurfaceContact && IsSlimeSurfaceTouchingSwitch())
                isTouching = true;

            SetContactState(isTouching);
        }

        void OnDisable()
        {
            _physicsContacts.Clear();
            SetContactState(false);
        }

        void OnCollisionEnter(Collision collision)
        {
            RegisterPhysicsContact(collision != null ? collision.collider : null);
            
        }

        void OnCollisionStay(Collision collision)
        {
            RegisterPhysicsContact(collision != null ? collision.collider : null);
        }

        void OnCollisionExit(Collision collision)
        {
            UnregisterPhysicsContact(collision != null ? collision.collider : null);
        }

        void OnTriggerEnter(Collider other)
        {
            RegisterPhysicsContact(other);
        }

        void OnTriggerStay(Collider other)
        {
            RegisterPhysicsContact(other);
        }

        void OnTriggerExit(Collider other)
        {
            UnregisterPhysicsContact(other);
        }

        public void ResetContactState()
        {
            _physicsContacts.Clear();
            SetContactState(false);
        }

        public void RefreshContactColliders()
        {
            _contactColliders = includeChildColliders
                ? GetComponentsInChildren<Collider>()
                : GetComponents<Collider>();
        }

        void CaptureInitialDoorState()
        {
            _initialObjectActiveSelf = objectToHide == null || objectToHide.activeSelf;
        }

        void RegisterPhysicsContact(Collider other)
        {
            if (!usePhysicsCallbacks || !IsAcceptedPhysicsContact(other))
                return;

            _physicsContacts.Add(other);
        }

        void UnregisterPhysicsContact(Collider other)
        {
            if (other == null)
                return;

            _physicsContacts.Remove(other);
        }

        bool HasValidPhysicsContact()
        {
            if (!usePhysicsCallbacks || _physicsContacts.Count == 0)
                return false;

            bool hasContact = false;
            s_RemoveBuffer.Clear();

            foreach (Collider contact in _physicsContacts)
            {
                if (IsAcceptedPhysicsContact(contact))
                    hasContact = true;
                else
                    s_RemoveBuffer.Add(contact);
            }

            for (int i = 0; i < s_RemoveBuffer.Count; i++)
                _physicsContacts.Remove(s_RemoveBuffer[i]);

            s_RemoveBuffer.Clear();
            return hasContact;
        }

        bool IsAcceptedPhysicsContact(Collider other)
        {
            if (!SlimeSurfaceContactUtility.IsUsable(other))
                return false;

            return !onlyPhysicsContactFromSlime || SlimeSurfaceContactUtility.IsSlimeObject(other);
        }

        bool IsSlimeSurfaceTouchingSwitch()
        {
            if (!ResolveSlimeSurface() || slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            if (_contactColliders == null || _contactColliders.Length == 0)
                RefreshContactColliders();

            return SlimeSurfaceContactUtility.TryGetSurfaceContact(
                slimeSurface,
                _contactColliders,
                _surfaceVertices,
                maxSurfaceVertexChecks,
                contactPadding,
                out _);
        }

        bool ResolveSlimeSurface()
        {
            if (slimeSurface != null)
                return true;

            if (slimeSimulator != null)
                slimeSurface = slimeSimulator.GetComponentInChildren<SlimeSurfaceBuilder>();

            if (slimeSurface == null && autoFindSlimeSurface)
                slimeSurface = UnityEngine.Object.FindAnyObjectByType<SlimeSurfaceBuilder>(FindObjectsInactive.Exclude);

            if (slimeSimulator == null && slimeSurface != null)
                slimeSimulator = slimeSurface.GetComponentInParent<SlimeSimulator>();

            return slimeSurface != null;
        }

        void SetContactState(bool hasContact)
        {
            if (_hasContact == hasContact)
            {
                ApplyLegacyObjectState();
                return;
            }

            _hasContact = hasContact;
            ApplyLegacyObjectState();

            SlimeContactEventContext context = BuildEventContext();

            if (_hasContact)
            {
                InvokeEventHandlers(context, true);
                onContactBegan?.Invoke();
            }
            else
            {
                InvokeEventHandlers(context, false);
                onContactEnded?.Invoke();
            }
        }

        SlimeContactEventContext BuildEventContext()
        {
            return new SlimeContactEventContext(this, slimeSurface, slimeSimulator);
        }

        void InvokeEventHandlers(SlimeContactEventContext context, bool contactBegan)
        {
            if (eventHandlers == null)
                return;

            for (int i = 0; i < eventHandlers.Length; i++)
            {
                SlimeContactEventHandler eventHandler = eventHandlers[i];
                if (eventHandler == null)
                    continue;

                if (contactBegan)
                    eventHandler.HandleContactBegan(context);
                else
                    eventHandler.HandleContactEnded(context);
            }
        }

        void ApplyLegacyObjectState()
        {
            if (objectToHide == null)
                return;

            if (_hasContact)
            {
                if (objectToHide.activeSelf)
                    objectToHide.SetActive(false);
            }
            else if (restoreObjectWhenContactEnds && objectToHide.activeSelf != _initialObjectActiveSelf)
            {
                objectToHide.SetActive(_initialObjectActiveSelf);
            }
        }

        static readonly List<Collider> s_RemoveBuffer = new List<Collider>(16);
    }
}
