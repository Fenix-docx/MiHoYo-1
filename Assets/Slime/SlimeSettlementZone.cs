using System;
using UnityEngine;
using UnityEngine.Events;

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Settlement Zone")]
    public class SlimeSettlementZone : MonoBehaviour
    {
        public enum ZoneShape
        {
            Sphere,
            Cylinder,
        }

        [Serializable]
        public sealed class ParticleCountEvent : UnityEvent<int> { }

        [Header("Player")]
        [SerializeField] SlimeSimulator slimeSimulator;
        [SerializeField] bool autoFindSlimeSimulator = true;
        [SerializeField] bool useSlimeCenter = true;

        [Header("Zone")]
        [SerializeField] ZoneShape zoneShape = ZoneShape.Sphere;
        [SerializeField, Min(0.01f)] float radius = 1f;
        [SerializeField, Min(0.01f)] float cylinderHeight = 2f;
        [SerializeField] bool settleOnce = true;

        [Header("Settlement")]
        [SerializeField] bool pauseTimeOnSettlement = true;
        [SerializeField] bool disablePlayerControlOnSettlement = true;
        [SerializeField] UnityEvent onSettled;
        [SerializeField] ParticleCountEvent onSettledWithParticleCount;

        bool _settled;
        int _settledParticleCount = -1;
        float _settledTime = -1f;

        public bool HasSettled => _settled;
        public int SettledParticleCount => _settledParticleCount;
        public float SettledTime => _settledTime;
        public UnityEvent OnSettled => onSettled;
        public ParticleCountEvent OnSettledWithParticleCount => onSettledWithParticleCount;

        void Awake()
        {
            ResolveSlimeSimulator();
        }

        void OnValidate()
        {
            radius = Mathf.Max(0.01f, radius);
            cylinderHeight = Mathf.Max(0.01f, cylinderHeight);
        }

        void Update()
        {
            if (!CanSettle())
                return;

            if (!ResolveSlimeSimulator())
                return;

            if (IsPointInsideZone(GetPlayerPoint(slimeSimulator)))
                Settle(slimeSimulator);
        }

        void OnTriggerEnter(Collider other)
        {
            TrySettleFromCollider(other);
        }

        void OnTriggerStay(Collider other)
        {
            TrySettleFromCollider(other);
        }

        public int CalculateSimulatedParticleCount()
        {
            return ResolveSlimeSimulator() ? slimeSimulator.ParticleCount : 0;
        }

        public void Settle()
        {
            if (ResolveSlimeSimulator())
                Settle(slimeSimulator);
            else
                Settle(null);
        }

        public void ResetSettlementState(bool restoreTimeScale)
        {
            _settled = false;
            _settledParticleCount = -1;
            _settledTime = -1f;

            if (restoreTimeScale)
                Time.timeScale = 1f;
        }

        void Settle(SlimeSimulator target)
        {
            if (!CanSettle())
                return;

            if (target != null)
                slimeSimulator = target;

            _settled = true;
            _settledTime = Time.time;
            _settledParticleCount = target != null ? target.ParticleCount : 0;

            if (disablePlayerControlOnSettlement && target != null)
            {
                SlimeControllerMotor motor = target.GetComponentInChildren<SlimeControllerMotor>();
                if (motor != null)
                    motor.enabled = false;
            }

            Debug.Log(
                $"[SlimeSettlementZone] Settlement reached at '{name}'. Simulated particle count: {_settledParticleCount}.",
                this);

            onSettled?.Invoke();
            onSettledWithParticleCount?.Invoke(_settledParticleCount);

            if (pauseTimeOnSettlement)
                Time.timeScale = 0f;
        }

        bool TrySettleFromCollider(Collider other)
        {
            if (!CanSettle() || other == null)
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

            Settle(target);
            return true;
        }

        bool ResolveSlimeSimulator()
        {
            if (slimeSimulator != null)
                return true;

            if (autoFindSlimeSimulator)
                slimeSimulator = UnityEngine.Object.FindAnyObjectByType<SlimeSimulator>(FindObjectsInactive.Exclude);

            return slimeSimulator != null;
        }

        bool CanSettle()
        {
            return !settleOnce || !_settled;
        }

        Vector3 GetPlayerPoint(SlimeSimulator target)
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

            Gizmos.color = _settled ? Color.green : Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;

            if (zoneShape == ZoneShape.Sphere)
                Gizmos.DrawWireSphere(Vector3.zero, radius);
            else
                DrawWireCylinder(radius, cylinderHeight);

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
