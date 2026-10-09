using System;
using UnityEngine;

namespace UIPackage.Motion
{
    // P1 才实现其余几何类型；参数区可扩展，采样入口保持 tag + switch。
    public enum PathSegmentKind { Line, CubicBezier }
    [Serializable]
    public struct PathSegment
    {
        public PathSegmentKind kind;
        public Vector2 p0, p1, p2, p3;
        public static PathSegment Line(Vector2 from, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.Line, p0 = from, p3 = to };
        public static PathSegment CubicBezier(Vector2 from, Vector2 c0, Vector2 c1, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.CubicBezier, p0 = from, p1 = c0, p2 = c1, p3 = to };
        public Vector2 Evaluate(float t)
        {
            switch (kind)
            {
                case PathSegmentKind.Line: return p0 + (p3 - p0) * t;
                case PathSegmentKind.CubicBezier: return MotionMath.Cubic(p0, p1, p2, p3, t);
                default: throw new NotSupportedException("未实现的路径段。");
            }
        }
        public Vector2 Tangent(float t)
        {
            Vector2 v;
            switch (kind)
            {
                case PathSegmentKind.Line: v = p3 - p0; break;
                case PathSegmentKind.CubicBezier:
                    v = MotionMath.CubicTangent(p0, p1, p2, p3, t);
                    // 重合控制点使用首个非零割线，退化路径返回零向量。
                    if (v.sqrMagnitude < 1e-12f)
                        v = t <= 0 ? (p2 - p0) : (p3 - p1);
                    if (v.sqrMagnitude < 1e-12f) v = p3 - p0;
                    break;
                default: throw new NotSupportedException("未实现的路径段。");
            }
            return v.normalized;
        }
    }
}
