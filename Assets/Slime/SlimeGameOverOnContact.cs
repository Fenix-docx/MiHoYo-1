using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Slime
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Slime/Game Over On Slime Contact")]
    public class SlimeGameOverOnContact : MonoBehaviour
    {
        [Header("Slime Surface")]
        [SerializeField] SlimeSurfaceBuilder slimeSurface;
        [SerializeField] bool autoFindSlimeSurface = true;
        [SerializeField] bool includeChildColliders = true;
        [SerializeField, Min(0f)] float contactPadding = 0.05f;
        [SerializeField, Min(32)] int maxSurfaceVertexChecks = 4096;

        [Header("Game Over")]
        [SerializeField] bool pauseTimeOnGameOver = true;
        [SerializeField] bool pauseEditorOnGameOver;
        [SerializeField] bool quitApplicationInPlayer;
        [SerializeField] UnityEvent onGameOver;

        readonly List<Vector3> _surfaceVertices = new List<Vector3>(4096);
        Collider[] _targetColliders = new Collider[0];
        bool _gameOver;

        public bool HasGameOver => _gameOver;
        public UnityEvent OnGameOver => onGameOver;

        void Awake()
        {
            CacheTargetColliders();
            ResolveSlimeSurface();
        }

        void OnEnable()
        {
            _gameOver = false;
            CacheTargetColliders();
        }

        void OnValidate()
        {
            contactPadding = Mathf.Max(0f, contactPadding);
            maxSurfaceVertexChecks = Mathf.Max(32, maxSurfaceVertexChecks);
        }

        void LateUpdate()
        {
            if (_gameOver)
                return;

            if (slimeSurface == null && !ResolveSlimeSurface())
                return;

            if (TouchesCurrentSlimeSurface())
                TriggerGameOver();
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
            if (slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            Mesh surfaceMesh = slimeSurface.CurrentSurfaceMesh;
            if (surfaceMesh == null || surfaceMesh.vertexCount == 0)
                return false;

            if (_targetColliders == null || _targetColliders.Length == 0)
                CacheTargetColliders();

            if (_targetColliders == null || _targetColliders.Length == 0)
                return false;

            _surfaceVertices.Clear();
            surfaceMesh.GetVertices(_surfaceVertices);
            if (_surfaceVertices.Count == 0)
                return false;

            float padding = Mathf.Max(contactPadding, 0.0001f);
            Bounds slimeBounds = surfaceMesh.bounds;
            slimeBounds.Expand(padding * 2f);

            for (int i = 0; i < _targetColliders.Length; i++)
            {
                Collider targetCollider = _targetColliders[i];
                if (!IsUsable(targetCollider))
                    continue;

                Bounds targetBounds = targetCollider.bounds;
                targetBounds.Expand(padding * 2f);
                if (!targetBounds.Intersects(slimeBounds))
                    continue;

                if (SurfaceTouchesCollider(targetCollider, targetBounds, padding))
                    return true;
            }

            return false;
        }

        public void TriggerGameOver()
        {
            if (_gameOver)
                return;

            _gameOver = true;
            Debug.Log($"[SlimeGameOverOnContact] Game over: slime touched '{name}'.", this);
            onGameOver?.Invoke();

            if (pauseTimeOnGameOver)
                Time.timeScale = 0f;

#if UNITY_EDITOR
            if (pauseEditorOnGameOver && Application.isPlaying)
                EditorApplication.isPaused = true;
#endif

            if (quitApplicationInPlayer && !Application.isEditor)
                Application.Quit();
        }

        public void ResetGameOverState(bool restoreTimeScale)
        {
            _gameOver = false;
            if (restoreTimeScale)
                Time.timeScale = 1f;
        }

        bool SurfaceTouchesCollider(Collider targetCollider, Bounds targetBounds, float padding)
        {
            float sqrPadding = padding * padding;
            int stride = Mathf.Max(1, Mathf.CeilToInt(_surfaceVertices.Count / (float)maxSurfaceVertexChecks));

            for (int i = 0; i < _surfaceVertices.Count; i += stride)
            {
                Vector3 point = _surfaceVertices[i];
                if (!IsFinite(point))
                    continue;

                if (targetBounds.SqrDistance(point) > sqrPadding)
                    continue;

                Vector3 closest = targetCollider.ClosestPoint(point);
                if ((closest - point).sqrMagnitude <= sqrPadding)
                    return true;
            }

            return false;
        }

        bool ResolveSlimeSurface()
        {
            if (slimeSurface != null)
                return true;

            if (!autoFindSlimeSurface)
                return false;

            slimeSurface = Object.FindAnyObjectByType<SlimeSurfaceBuilder>(FindObjectsInactive.Exclude);
            return slimeSurface != null;
        }

        void CacheTargetColliders()
        {
            _targetColliders = includeChildColliders
                ? GetComponentsInChildren<Collider>(false)
                : GetComponents<Collider>();
        }

        void TryTriggerFromPhysics(Collider other)
        {
            if (_gameOver || !IsSlimeObject(other))
                return;

            TriggerGameOver();
        }

        static bool IsSlimeObject(Component component)
        {
            if (component == null)
                return false;

            return component.GetComponentInParent<SlimeSimulator>() != null ||
                   component.GetComponentInParent<SlimeSurfaceBuilder>() != null ||
                   component.GetComponentInParent<SlimeControllerMotor>() != null;
        }

        static bool IsUsable(Collider targetCollider)
        {
            return targetCollider != null &&
                   targetCollider.enabled &&
                   targetCollider.gameObject.activeInHierarchy;
        }

        static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
