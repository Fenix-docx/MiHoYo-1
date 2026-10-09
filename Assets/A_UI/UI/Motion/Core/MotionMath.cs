using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>只使用托管数学，控制台与 Unity 使用同一份采样代码。</summary>
    public static class MotionMath
    {
        public static float Clamp01(float x) => Math.Max(0, Math.Min(1, x));
        public static Vector2 Cubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float q = 1 - t;
            return q * q * q * a + 3 * q * q * t * b + 3 * q * t * t * c + t * t * t * d;
        }
        public static Vector2 CubicTangent(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
        {
            float q = 1 - t;
            return 3 * q * q * (b - a) + 6 * q * t * (c - b) + 3 * t * t * (d - c);
        }
        /// <summary>LUT 由创作期分配，动态锚点更新时复用原数组。</summary>
        public static float BuildArcLut(PathSegment path, float[] lut)
        {
            if (lut == null || lut.Length < 2) throw new ArgumentException("LUT 至少需要两个点。");
            lut[0] = 0;
            var last = path.p0;
            for (int i = 1; i < lut.Length; i++)
            {
                var next = path.Evaluate((float)i / (lut.Length - 1));
                lut[i] = lut[i - 1] + Vector2.Distance(last, next);
                last = next;
            }
            return lut[lut.Length - 1];
        }
        public static float ArcParameter(float u, float[] lut)
        {
            float distance = Clamp01(u) * lut[lut.Length - 1];
            int lo = 0, hi = lut.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (lut[mid] < distance) lo = mid; else hi = mid;
            }
            float width = lut[hi] - lut[lo];
            return (lo + (width > 1e-12f ? (distance - lut[lo]) / width : 0)) / (lut.Length - 1);
        }
        public static Vector2 EvaluateByArc(PathSegment path, float u, float[] lut, bool clampOutside = false)
        {
            if (clampOutside) u = Clamp01(u);
            float length = lut[lut.Length - 1];
            if (u < 0) return path.p0 + path.Tangent(0) * (u * length);
            if (u > 1) return path.p3 + path.Tangent(1) * ((u - 1) * length);
            if (u == 0) return path.p0;
            if (u == 1) return path.p3;
            return path.Evaluate(ArcParameter(u, lut));
        }
        public static Vector2 AnchorPoint(Vector2 min, Vector2 size, AnchorEdge edge, Vector2 offset)
        {
            var p = min + size * .5f;
            switch (edge)
            {
                case AnchorEdge.Left: p.x = min.x; break;
                case AnchorEdge.Right: p.x = min.x + size.x; break;
                case AnchorEdge.Top: p.y = min.y + size.y; break;
                case AnchorEdge.Bottom: p.y = min.y; break;
                case AnchorEdge.Slot: throw new ArgumentException("Slot 须先解析子节点。");
            }
            return p + offset * Math.Min(Math.Abs(size.x), Math.Abs(size.y));
        }
    }
}
