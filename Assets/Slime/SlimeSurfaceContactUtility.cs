using System.Collections.Generic;
using UnityEngine;

namespace Slime
{
    public static class SlimeSurfaceContactUtility
    {
        const float MinContactPadding = 0.0001f;

        public static bool TryGetSurfaceContact(
            SlimeSurfaceBuilder slimeSurface,
            Collider[] colliders,
            List<Vector3> surfaceVertices,
            int maxVertexChecks,
            float contactPadding,
            out Vector3 contactPoint)
        {
            contactPoint = default;

            if (slimeSurface == null || !slimeSurface.HasSurface)
                return false;

            return TryGetMeshContact(
                slimeSurface.CurrentSurfaceMesh,
                colliders,
                surfaceVertices,
                maxVertexChecks,
                contactPadding,
                out contactPoint);
        }

        public static bool TryGetMeshContact(
            Mesh surfaceMesh,
            Collider[] colliders,
            List<Vector3> surfaceVertices,
            int maxVertexChecks,
            float contactPadding,
            out Vector3 contactPoint)
        {
            contactPoint = default;

            if (surfaceMesh == null || surfaceMesh.vertexCount == 0)
                return false;

            if (colliders == null || colliders.Length == 0)
                return false;

            if (surfaceVertices == null)
                return false;

            surfaceVertices.Clear();
            surfaceMesh.GetVertices(surfaceVertices);

            if (surfaceVertices.Count == 0)
                return false;

            float padding = Mathf.Max(MinContactPadding, contactPadding);
            float paddingSqr = padding * padding;
            int maxChecks = Mathf.Max(1, maxVertexChecks);
            int stride = Mathf.Max(1, Mathf.CeilToInt(surfaceVertices.Count / (float)maxChecks));
            int checks = 0;
            Bounds surfaceBounds = surfaceMesh.bounds;
            surfaceBounds.Expand(padding * 2f);

            for (int vertexIndex = 0;
                 vertexIndex < surfaceVertices.Count && checks < maxChecks;
                 vertexIndex += stride)
            {
                Vector3 point = surfaceVertices[vertexIndex];
                if (!IsFinite(point))
                    continue;

                checks++;

                for (int colliderIndex = 0; colliderIndex < colliders.Length; colliderIndex++)
                {
                    Collider targetCollider = colliders[colliderIndex];
                    if (!IsUsable(targetCollider))
                        continue;

                    Bounds targetBounds = targetCollider.bounds;
                    targetBounds.Expand(padding * 2f);

                    if (!targetBounds.Intersects(surfaceBounds))
                        continue;

                    if (targetBounds.SqrDistance(point) > paddingSqr)
                        continue;

                    Vector3 closestPoint = targetCollider.ClosestPoint(point);
                    if ((closestPoint - point).sqrMagnitude > paddingSqr)
                        continue;

                    contactPoint = point;
                    return true;
                }
            }

            return false;
        }

        public static bool IsSlimeObject(Component component)
        {
            if (component == null)
                return false;

            return component.GetComponentInParent<SlimeSimulator>() != null
                || component.GetComponentInParent<SlimeSurfaceBuilder>() != null
                || component.GetComponentInParent<SlimeControllerMotor>() != null;
        }

        public static bool IsUsable(Collider targetCollider)
        {
            return targetCollider != null
                && targetCollider.enabled
                && targetCollider.gameObject.activeInHierarchy;
        }

        static bool IsFinite(Vector3 point)
        {
            return IsFinite(point.x) && IsFinite(point.y) && IsFinite(point.z);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
