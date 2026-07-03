using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Checkpoint Zone")]
    public sealed class SlimeCheckpointZone : MonoBehaviour
    {
        public enum ZoneShape
        {
            Sphere,
            Cylinder,
        }

        [Header("Checkpoint")]
        [SerializeField] SlimeCheckpointManager checkpointManager;
        [SerializeField] Transform checkpointLocation;
        [SerializeField] bool autoFindCheckpointManager = true;
        [SerializeField] bool activateOnce = true;

        [Header("Slime")]
        [SerializeField] SlimeSimulator slimeSimulator;
        [SerializeField] bool autoFindSlimeSimulator = true;
        [SerializeField] bool useSlimeCenter = true;

        [Header("Zone")]
        [SerializeField] ZoneShape zoneShape = ZoneShape.Sphere;
        [SerializeField, Min(0.01f)] float radius = 1f;
        [SerializeField, Min(0.01f)] float cylinderHeight = 2f;

        [Header("Events")]
        [SerializeField] UnityEvent onCheckpointActivated;

        bool _activated;

        public bool HasActivated => _activated;
        public SlimeCheckpointManager CheckpointManager => checkpointManager;
        public UnityEvent OnCheckpointActivated => onCheckpointActivated;

        void Awake()
        {
            ResolveCheckpointManager();
            ResolveSlimeSimulator();
        }

        void OnValidate()
        {
            radius = Mathf.Max(0.01f, radius);
            cylinderHeight = Mathf.Max(0.01f, cylinderHeight);
        }

        void Update()
        {
            if (!CanActivate())
                return;

            if (!ResolveSlimeSimulator())
                return;

            if (IsPointInsideZone(GetSlimePoint(slimeSimulator)))
                Activate(slimeSimulator);
        }

        void OnTriggerEnter(Collider other)
        {
            TryActivateFromCollider(other);
        }

        void OnTriggerStay(Collider other)
        {
            TryActivateFromCollider(other);
        }

        public void Activate()
        {
            Activate(slimeSimulator);
        }

        public void ResetActivationState()
        {
            _activated = false;
        }

        bool TryActivateFromCollider(Collider other)
        {
            if (!CanActivate() || other == null)
                return false;

            SlimeSimulator target = other.GetComponentInParent<SlimeSimulator>();
            if (target == null)
            {
                SlimeControllerMotor motor = other.GetComponentInParent<SlimeControllerMotor>();
                if (motor != null)
                    target = motor.GetComponentInParent<SlimeSimulator>();
            }

            if (target == null)
                return false;

            Activate(target);
            return true;
        }

        void Activate(SlimeSimulator target)
        {
            if (!CanActivate() || !ResolveCheckpointManager())
                return;

            if (target != null)
                slimeSimulator = target;

            Transform location = checkpointLocation != null ? checkpointLocation : transform;
            checkpointManager.SetCheckpoint(location.position, location.rotation);
            _activated = true;

            Debug.Log($"[SlimeCheckpointZone] Checkpoint refreshed at '{name}'.", this);
            onCheckpointActivated?.Invoke();
        }

        bool ResolveCheckpointManager()
        {
            if (checkpointManager != null)
                return true;

            if (autoFindCheckpointManager)
                checkpointManager = Object.FindAnyObjectByType<SlimeCheckpointManager>(FindObjectsInactive.Exclude);

            return checkpointManager != null;
        }

        bool ResolveSlimeSimulator()
        {
            if (slimeSimulator != null)
                return true;

            if (autoFindSlimeSimulator)
                slimeSimulator = Object.FindAnyObjectByType<SlimeSimulator>(FindObjectsInactive.Exclude);

            return slimeSimulator != null;
        }

        bool CanActivate()
        {
            return !activateOnce || !_activated;
        }

        Vector3 GetSlimePoint(SlimeSimulator target)
        {
            if (target == null)
                return transform.position;

            return useSlimeCenter ? target.CurrentWorldCenter : target.transform.position;
        }

        bool IsPointInsideZone(Vector3 worldPoint)
        {
            Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
            switch (zoneShape)
            {
                case ZoneShape.Sphere:
                    return localPoint.sqrMagnitude <= radius * radius;
                case ZoneShape.Cylinder:
                    float halfHeight = cylinderHeight * 0.5f;
                    Vector2 radial = new Vector2(localPoint.x, localPoint.z);
                    return Mathf.Abs(localPoint.y) <= halfHeight && radial.sqrMagnitude <= radius * radius;
                default:
                    return false;
            }
        }

        void OnDrawGizmosSelected()
        {
            Color previousColor = Gizmos.color;
            Matrix4x4 previousMatrix = Gizmos.matrix;

            Gizmos.color = _activated ? Color.green : Color.cyan;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (zoneShape == ZoneShape.Sphere)
                Gizmos.DrawWireSphere(Vector3.zero, radius);
            else
                DrawWireCylinder(radius, cylinderHeight);

            if (checkpointLocation != null)
            {
                Gizmos.matrix = Matrix4x4.identity;
                Gizmos.DrawWireSphere(checkpointLocation.position, 0.25f);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        static void DrawWireCylinder(float cylinderRadius, float height)
        {
            const int segments = 32;
            float halfHeight = height * 0.5f;
            Vector3 topPrevious = CirclePoint(cylinderRadius, halfHeight, segments - 1, segments);
            Vector3 bottomPrevious = CirclePoint(cylinderRadius, -halfHeight, segments - 1, segments);

            for (int i = 0; i < segments; i++)
            {
                Vector3 top = CirclePoint(cylinderRadius, halfHeight, i, segments);
                Vector3 bottom = CirclePoint(cylinderRadius, -halfHeight, i, segments);
                Gizmos.DrawLine(topPrevious, top);
                Gizmos.DrawLine(bottomPrevious, bottom);

                if (i % 8 == 0)
                    Gizmos.DrawLine(bottom, top);

                topPrevious = top;
                bottomPrevious = bottom;
            }
        }

        static Vector3 CirclePoint(float circleRadius, float y, int index, int segments)
        {
            float angle = index * Mathf.PI * 2f / segments;
            return new Vector3(Mathf.Cos(angle) * circleRadius, y, Mathf.Sin(angle) * circleRadius);
        }
    }
}
