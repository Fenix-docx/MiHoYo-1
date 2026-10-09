using System;
using UnityEngine;

namespace UIPackage.Motion
{
    // 参数区只含值类型，采样入口保持 tag + switch；保留 P0 的枚举数值。
    public enum PathSegmentKind { Line, CubicBezier, QuadBezier, Arc, CatmullRom }
    [Serializable]
    public struct PathSegment
    {
        public PathSegmentKind kind;
        public Vector2 p0, p1, p2, p3;
        public static PathSegment Line(Vector2 from, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.Line, p0 = from, p3 = to };
        public static PathSegment CubicBezier(Vector2 from, Vector2 c0, Vector2 c1, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.CubicBezier, p0 = from, p1 = c0, p2 = c1, p3 = to };
        // Arc 单独编码中心/轴缩放/弧度角/旋转；p0/p3 仍缓存真实端点供旧工具读取。
        // CR 的 p0/p3 是虚点，p1/p2 是真实起止点；其他类型的 p0/p3 为起止点。
        public Vector2 center, ellipseScale;
        public float radius, a0, a1, rotation;
        public static PathSegment QuadBezier(Vector2 from, Vector2 control, Vector2 to) =>
            new PathSegment { kind = PathSegmentKind.QuadBezier, p0 = from, p1 = control, p3 = to };
        public static PathSegment CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3) =>
            new PathSegment { kind = PathSegmentKind.CatmullRom, p0 = p0, p1 = p1, p2 = p2, p3 = p3 };
        public static PathSegment Arc(Vector2 center, float radius, float a0, float a1) =>
            Arc(center, radius, a0, a1, Vector2.one);
        public static PathSegment Arc(Vector2 center, float radius, float a0, float a1, Vector2 ellipseScale)
        {
            var result = new PathSegment { kind = PathSegmentKind.Arc, center = center, radius = radius,
                a0 = a0, a1 = a1, ellipseScale = ellipseScale };
            result.p0 = result.Evaluate(0); result.p3 = result.Evaluate(1);
            return result;
        }
        /// <summary>圆：r·角差；椭圆：第二类椭圆积分，反向和多圈同样有效。</summary>
        public float ArcLength(float t = 1)
        {
            if (kind != PathSegmentKind.Arc) throw new InvalidOperationException("仅 Arc 有解析弧长。");
            return (float)ArcLengthMath.Length(radius * (double)ellipseScale.x, radius * (double)ellipseScale.y,
                a0, a0 + ((double)a1 - a0) * t);
        }
        private Vector2 Rotate(Vector2 v)
        {
            float c = (float)Math.Cos(rotation), s = (float)Math.Sin(rotation);
            return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
        }
        public Vector2 Evaluate(float t)
        {
            switch (kind)
            {
                case PathSegmentKind.Line: return p0 + (p3 - p0) * t;
                case PathSegmentKind.CubicBezier: return MotionMath.Cubic(p0, p1, p2, p3, t);
                case PathSegmentKind.QuadBezier:
                    float q = 1 - t;
                    return q * q * p0 + 2 * q * t * p1 + t * t * p3;
                case PathSegmentKind.Arc:
                    double angle = a0 + ((double)a1 - a0) * t;
                    return center + Rotate(new Vector2((float)Math.Cos(angle) * radius * ellipseScale.x,
                        (float)Math.Sin(angle) * radius * ellipseScale.y));
                case PathSegmentKind.CatmullRom:
                    if (t == 0) return p1;
                    if (t == 1) return p2;
                    return .5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * (t * t)
                        + (-p0 + 3 * p1 - 3 * p2 + p3) * (t * t * t));
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
                case PathSegmentKind.QuadBezier:
                    v = 2 * ((1 - t) * (p1 - p0) + t * (p3 - p1));
                    if (v.sqrMagnitude < 1e-12f) v = p3 - p0;
                    break;
                case PathSegmentKind.Arc:
                    double angle = a0 + ((double)a1 - a0) * t;
                    v = Rotate(new Vector2(-(float)Math.Sin(angle) * radius * ellipseScale.x,
                        (float)Math.Cos(angle) * radius * ellipseScale.y)) * (a1 - a0);
                    if (v.sqrMagnitude < 1e-12f) v = Evaluate(1) - Evaluate(0);
                    break;
                case PathSegmentKind.CatmullRom:
                    v = .5f * ((-p0 + p2) + 2 * (2 * p0 - 5 * p1 + 4 * p2 - p3) * t
                        + 3 * (-p0 + 3 * p1 - 3 * p2 + p3) * (t * t));
                    if (v.sqrMagnitude < 1e-12f) v = p2 - p1;
                    break;
                default: throw new NotSupportedException("未实现的路径段。");
            }
            return v.normalized;
        }
    }
}
