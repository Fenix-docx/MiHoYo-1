using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Increase Particles On Slime Contact")]
    public class SlimeGameOverOnContact : MonoBehaviour
    {
        [Header("Slime Surface")]
        [SerializeField] SlimeSurfaceBuilder slimeSurface;
        [SerializeField] SlimeSimulator slimeSimulator;
        [SerializeField] bool autoFindSlimeSurface = true;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField, Min(0f)] float contactPadding = 0.05f;
        [SerializeField, Min(32)] int maxSurfaceVertexChecks = 4096;

        [Header("Particle Gain")]
        [SerializeField, Min(1)] int particlesToAdd = 128;
        [Tooltip("World-space jitter around evenly sampled existing slime particles.")]
        [SerializeField, Min(0.01f)] float spawnRadius = 0.15f;
        [Tooltip("0 means unlimited.")]
        [SerializeField, Min(0)] int maxParticleCount = 4096;
        [SerializeField] bool triggerOnce = true;
        [SerializeField, Min(0f)] float repeatCooldown = 0.5f;
        [SerializeField] bool destroyObjectOnFirstContact = true;
        [SerializeField] GameObject objectToDestroy;
        [FormerlySerializedAs("onGameOver")]
        [SerializeField] UnityEvent onParticlesAdded;

        readonly List<Vector3> _surfaceVertices = new List<Vector3>(4096);
        Collider[] _targetColliders = new Collider[0];
        bool _hasTriggered;
        float _lastTriggerTime = float.NegativeInfinity;

        public bool HasTriggered => _hasTriggered;
        public UnityEvent OnParticlesAdded => onParticlesAdded;
        public AudioSource audioSource;
        public AudioClip audioClip;

        void Awake()
        {
            CacheTargetColliders();
            ResolveSlimeSurface();
            ResolveSlimeSimulator();
        }

        void OnEnable()
        {
            _hasTriggered = false;
            _lastTriggerTime = float.NegativeInfinity;
            CacheTargetColliders();
        }

        void OnValidate()
        {
            contactPadding = Mathf.Max(0f, contactPadding);
            maxSurfaceVertexChecks = Mathf.Max(32, maxSurfaceVertexChecks);
            particlesToAdd = Mathf.Max(1, particlesToAdd);
            spawnRadius = Mathf.Max(0.01f, spawnRadius);
            maxParticleCount = Mathf.Max(0, maxParticleCount);
            repeatCooldown = Mathf.Max(0f, repeatCooldown);
        }

        void LateUpdate()
        {
            if (!CanTrigger())
                return;

            if (slimeSurface == null && !ResolveSlimeSurface())
                return;

            if (TouchesCurrentSlimeSurface())
                TryAddParticles();
        }

        void OnCollisionEnter(Collision collision)
        {
            TryTriggerFromPhysics(collision.collider);
        }

        void OnCollisionStay(Collision collision)
        {
            TryTriggerFromPhysics(collision.collider);
        }

        void OnTriggerEnter(Collider other)
        {
            TryTriggerFromPhysics(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryTriggerFromPhysics(other);
        }

        public bool TouchesCurrentSlimeSurface()
        {
            return TouchesCurrentSlimeSurface(out _);
        }

        public bool TouchesCurrentSlimeSurface(out Vector3 contactPoint)
        {
            contactPoint = transform.position;
            if (slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            if (_targetColliders == null || _targetColliders.Length == 0)
                CacheTargetColliders();

            if (!SlimeSurfaceContactUtility.TryGetSurfaceContact(
                slimeSurface,
                _targetColliders,
                _surfaceVertices,
                maxSurfaceVertexChecks,
                contactPadding,
                out Vector3 foundPoint))
                return false;

            contactPoint = foundPoint;
            return true;
        }

        public void ResetTriggerState()
        {
            _hasTriggered = false;
            _lastTriggerTime = float.NegativeInfinity;
        }

        bool TryAddParticles()
        {
            if (audioSource != null && audioClip != null)
                audioSource.PlayOneShot(audioClip);

            if (!CanTrigger() || !ResolveSlimeSimulator())
                return false;

            int requestedCount = particlesToAdd;
            if (maxParticleCount > 0)
                requestedCount = Mathf.Min(requestedCount, maxParticleCount - slimeSimulator.ParticleCount);

            if (requestedCount <= 0)
                return false;

            int added = slimeSimulator.AddParticlesDistributed(requestedCount, spawnRadius);
            if (added <= 0)
                return false;

            _hasTriggered = true;
            _lastTriggerTime = Time.time;
            Debug.Log($"[SlimeGameOverOnContact] Added {added} slime particle(s) across the fluid from '{name}'.", this);
            onParticlesAdded?.Invoke();
            DestroyTargetObject();
            return true;
        }

        bool ResolveSlimeSurface()
        {
            if (slimeSurface != null)
                return true;

            if (slimeSimulator != null)
                slimeSurface = slimeSimulator.GetComponent<SlimeSurfaceBuilder>();

            if (slimeSurface == null && autoFindSlimeSurface)
                slimeSurface = Object.FindAnyObjectByType<SlimeSurfaceBuilder>(FindObjectsInactive.Exclude);

            return slimeSurface != null;
        }

        bool ResolveSlimeSimulator()
        {
            if (slimeSimulator != null)
                return true;

            if (slimeSurface != null)
                slimeSimulator = slimeSurface.GetComponent<SlimeSimulator>();

            if (slimeSimulator == null && autoFindSlimeSurface)
                slimeSimulator = Object.FindAnyObjectByType<SlimeSimulator>(FindObjectsInactive.Exclude);

            if (slimeSurface == null && slimeSimulator != null)
                slimeSurface = slimeSimulator.GetComponent<SlimeSurfaceBuilder>();

            return slimeSimulator != null;
        }

        void CacheTargetColliders()
        {
            _targetColliders = includeChildColliders
                ? GetComponentsInChildren<Collider>(false)
                : GetComponents<Collider>();
        }

        void TryTriggerFromPhysics(Collider other)
        {
            if (!CanTrigger() || !IsSlimeObject(other))
                return;

            TryAddParticles();
        }

        void DestroyTargetObject()
        {
            if (!destroyObjectOnFirstContact)
                return;

            GameObject target = objectToDestroy != null ? objectToDestroy : gameObject;
            if (target != null)
                Destroy(target);
        }

        bool CanTrigger()
        {
            if (triggerOnce && _hasTriggered)
                return false;

            return repeatCooldown <= 0f || Time.time >= _lastTriggerTime + repeatCooldown;
        }

        static bool IsSlimeObject(Component component)
        {
            return SlimeSurfaceContactUtility.IsSlimeObject(component);
        }
    }
}
