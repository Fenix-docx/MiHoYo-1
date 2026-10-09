using UnityEditor;
using UnityEngine;

namespace UIPackage.Motion.Editor
{
    internal static class MotionGizmoDrawer
    {
        public static Vector3 ToWorld(Vector2 p, RectTransform space)
        {
            return space != null ? space.TransformPoint(AnchorResolver.FromP(p, space)) : new Vector3(p.x, p.y, 0);
        }
        public static Vector2 ToP(Vector3 world, RectTransform space)
        {
            return space != null ? AnchorResolver.ToP(space.InverseTransformPoint(world), space) : (Vector2)world;
        }
        public static void Draw(PathChain chain, RectTransform space)
        {
            var old = Handles.color;
            Handles.color = Color.cyan;
            var last = ToWorld(chain.EvaluateByArc(0), space);
            for (int i = 1; i <= 128; i++)
            {
                var next = ToWorld(chain.EvaluateByArc(i / 128f), space);
                Handles.DrawLine(last, next); last = next;
            }
            // 黄色箭头同时标出切线和行进方向；所有显示坐标经同一 p 空间变换。
            Handles.color = Color.yellow;
            for (int i = 0; i <= 8; i++)
            {
                float u = i / 8f; var p = chain.Evaluate(u); var tangent = chain.Tangent(u);
                var start = ToWorld(p, space); var end = ToWorld(p + tangent * .08f, space);
                var direction = end - start;
                if (direction.sqrMagnitude > 1e-12f)
                    Handles.ArrowHandleCap(0, start, Quaternion.LookRotation(direction), direction.magnitude, EventType.Repaint);
            }
            Handles.color = old;
        }
    }
}
