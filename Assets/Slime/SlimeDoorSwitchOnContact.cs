using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Door Switch On Contact")]
    public class SlimeDoorSwitchOnContact : MonoBehaviour
    {
        [Header("Door")]
        [SerializeField] GameObject objectToHide;
        [SerializeField] bool restoreObjectWhenContactEnds = true;

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
        [SerializeField] UnityEvent onContactBegan;
        [SerializeField] UnityEvent onContactEnded;

        readonly HashSet<Collider> _physicsContacts = new HashSet<Collider>();
        readonly List<Vector3> _surfaceVertices = new List<Vector3>(4096);

        Collider[] _contactColliders = Array.Empty<Collider>();
        bool _initialObjectActiveSelf = true;
        bool _hasContact;

        public bool HasContact => _hasContact;
        public GameObject ObjectToHide => objectToHide;
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
            ApplyObjectState();
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
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy)
                return false;

            if (!onlyPhysicsContactFromSlime)
                return true;

            if (other.GetComponentInParent<SlimeSimulator>() != null)
                return true;

            if (other.GetComponentInParent<SlimeControllerMotor>() != null)
                return true;

            return false;
        }

        bool IsSlimeSurfaceTouchingSwitch()
        {
            if (!ResolveSlimeSurface() || slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            if (_contactColliders == null || _contactColliders.Length == 0)
                RefreshContactColliders();

            Mesh surfaceMesh = slimeSurface.CurrentSurfaceMesh;
            if (surfaceMesh == null || surfaceMesh.vertexCount == 0)
                return false;

            _surfaceVertices.Clear();
            surfaceMesh.GetVertices(_surfaceVertices);

            if (_surfaceVertices.Count == 0)
                return false;

            int step = Mathf.Max(1, Mathf.CeilToInt(_surfaceVertices.Count / (float)maxSurfaceVertexChecks));
            float paddingSqr = contactPadding * contactPadding;

            for (int vertexIndex = 0, checks = 0;
                 vertexIndex < _surfaceVertices.Count && checks < maxSurfaceVertexChecks;
                 vertexIndex += step, checks++)
            {
                Vector3 worldPoint = _surfaceVertices[vertexIndex];

                for (int colliderIndex = 0; colliderIndex < _contactColliders.Length; colliderIndex++)
                {
                    Collider contactCollider = _contactColliders[colliderIndex];
                    if (IsPointTouchingCollider(worldPoint, contactCollider, paddingSqr))
                        return true;
                }
            }

            return false;
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

        bool IsPointTouchingCollider(Vector3 worldPoint, Collider contactCollider, float paddingSqr)
        {
            if (contactCollider == null || !contactCollider.enabled || !contactCollider.gameObject.activeInHierarchy)
                return false;

            if (contactPadding > 0f && contactCollider.bounds.SqrDistance(worldPoint) > paddingSqr)
                return false;

            Vector3 closestPoint = contactCollider.ClosestPoint(worldPoint);
            return (closestPoint - worldPoint).sqrMagnitude <= paddingSqr;
        }

        void SetContactState(bool hasContact)
        {
            if (_hasContact == hasContact)
            {
                ApplyObjectState();
                return;
            }

            _hasContact = hasContact;
            ApplyObjectState();

            if (_hasContact)
                onContactBegan?.Invoke();
            else
                onContactEnded?.Invoke();
        }

        void ApplyObjectState()
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
