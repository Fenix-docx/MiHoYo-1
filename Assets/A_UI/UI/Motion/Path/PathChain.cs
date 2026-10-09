using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>构造时复制、拼接并烘焙；采样只读取数组，调用方改原数组不会污染链条。</summary>
    public sealed class PathChain
    {
        private readonly PathSegment[] segments;
        private readonly float[] distances, parameters;
        private readonly int[] owners;
        public int SegmentCount => segments.Length;
        public float TotalLength => distances[distances.Length - 1];
        public PathSegment GetSegment(int index) => segments[index];

        public PathChain(PathSegment[] source, bool g1 = false)
        {
            if (source == null || source.Length == 0) throw new ArgumentException("路径链至少需要一段。", nameof(source));
            segments = (PathSegment[])source.Clone();
            if (g1)
                for (int i = 0; i < segments.Length; i++) segments[i] = Editable(segments[i]);
            for (int i = 1; i < segments.Length; i++)
            {
                var previous = segments[i - 1]; var next = segments[i];
                // 整段平移避免改变后段内部形状；CR 的真实起点是 p1，Arc 是解析端点。
                next = Translate(next, previous.Evaluate(1) - next.Evaluate(0));
                if (g1)
                {
                    Vector2 direction = (next.Evaluate(1) - previous.Evaluate(0)).normalized;
                    if (direction.sqrMagnitude == 0) direction = previous.Tangent(1);
                    if (direction.sqrMagnitude == 0) direction = next.Tangent(0);
                    if (previous.kind == PathSegmentKind.Arc) direction = previous.Tangent(1);
                    if (next.kind == PathSegmentKind.Arc)
                    {
                        // 椭圆没有自由控制柄，绕真实起点旋转，仍保留解析椭圆与 C0。
                        var start = next.Evaluate(0); var tangent = next.Tangent(0);
                        if (direction.sqrMagnitude > 0 && tangent.sqrMagnitude > 0)
                        {
                            next.rotation += (float)Math.Atan2(tangent.x * direction.y - tangent.y * direction.x,
                                Vector2.Dot(tangent, direction));
                            next = Translate(next, start - next.Evaluate(0));
                        }
                    }
                    previous = SetEndTangent(previous, direction);
                    next = SetStartTangent(next, direction);
                    segments[i - 1] = previous;
                }
                segments[i] = next;
            }
            int count = 1;
            for (int i = 0; i < segments.Length; i++) count += Samples(segments[i]) - 1;
            distances = new float[count]; parameters = new float[count]; owners = new int[count];
            int cursor = 0; float cumulative = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i]; int samples = Samples(segment);
                var last = segment.Evaluate(0); float baseLength = cumulative;
                for (int j = 1; j < samples; j++)
                {
                    float t = (float)j / (samples - 1); var point = segment.Evaluate(t);
                    cumulative = segment.kind == PathSegmentKind.Arc
                        ? baseLength + segment.ArcLength(t) : cumulative + Vector2.Distance(last, point);
                    cursor++; distances[cursor] = cumulative; parameters[cursor] = t; owners[cursor] = i;
                    last = point;
                }
            }
        }
        // 高曲率和强偏心弧需更细的反查表；弧的长度值仍全部来自解析积分。
        private static int Samples(PathSegment segment) => segment.kind == PathSegmentKind.Line ? 2 : 1025;
        public Vector2 Evaluate(float u)
        {
            if (u < 0 || u > 1) return Outside(u);
            float scaled = u * segments.Length;
            int index = Math.Min((int)scaled, segments.Length - 1);
            return segments[index].Evaluate(scaled - index);
        }
        public Vector2 Tangent(float u)
        {
            float scaled = MotionMath.Clamp01(u) * segments.Length;
            int index = Math.Min((int)scaled, segments.Length - 1);
            return segments[index].Tangent(scaled - index);
        }
        public Vector2 EvaluateByArc(float u, bool clampOutside = false)
        {
            if (clampOutside) u = MotionMath.Clamp01(u);
            if (u < 0 || u > 1) return Outside(u);
            if (u == 0 || TotalLength <= 1e-12f) return segments[0].Evaluate(0);
            if (u == 1) return segments[segments.Length - 1].Evaluate(1);
            float target = u * TotalLength;
            int lo = 0, hi = distances.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (distances[mid] < target) lo = mid; else hi = mid;
            }
            int owner = owners[hi];
            float t0 = owners[lo] == owner ? parameters[lo] : 0;
            float width = distances[hi] - distances[lo];
            float alpha = width > 1e-12f ? (target - distances[lo]) / width : 0;
            return segments[owner].Evaluate(t0 + (parameters[hi] - t0) * alpha);
        }
        private Vector2 Outside(float u) => u < 0
            ? segments[0].Evaluate(0) + segments[0].Tangent(0) * (u * TotalLength)
            : segments[segments.Length - 1].Evaluate(1) + segments[segments.Length - 1].Tangent(1) * ((u - 1) * TotalLength);
        private static PathSegment Translate(PathSegment s, Vector2 delta)
        {
            s.p0 += delta; s.p1 += delta; s.p2 += delta; s.p3 += delta;
            if (s.kind == PathSegmentKind.Arc)
            {
                s.center += delta; s.p0 = s.Evaluate(0); s.p3 = s.Evaluate(1);
            }
            return s;
        }
        private static PathSegment Editable(PathSegment s)
        {
            // 直线和二次曲线先精确升阶，才有彼此独立的两端控制柄。
            if (s.kind == PathSegmentKind.Line)
                return PathSegment.CubicBezier(s.p0, s.p0 + (s.p3 - s.p0) / 3, s.p0 + 2 * (s.p3 - s.p0) / 3, s.p3);
            if (s.kind == PathSegmentKind.QuadBezier)
                return PathSegment.CubicBezier(s.p0, s.p0 + 2 * (s.p1 - s.p0) / 3, s.p3 + 2 * (s.p1 - s.p3) / 3, s.p3);
            return s;
        }
        private static PathSegment SetStartTangent(PathSegment s, Vector2 direction)
        {
            float length = Vector2.Distance(s.Evaluate(0), s.Evaluate(1)) / 6;
            if (s.kind == PathSegmentKind.CubicBezier) s.p1 = s.p0 + direction * Math.Max(length, Vector2.Distance(s.p1, s.p0));
            if (s.kind == PathSegmentKind.CatmullRom) s.p0 = s.p2 - direction * Math.Max(2 * length, Vector2.Distance(s.p2, s.p0));
            return s;
        }
        private static PathSegment SetEndTangent(PathSegment s, Vector2 direction)
        {
            float length = Vector2.Distance(s.Evaluate(0), s.Evaluate(1)) / 6;
            if (s.kind == PathSegmentKind.CubicBezier) s.p2 = s.p3 - direction * Math.Max(length, Vector2.Distance(s.p3, s.p2));
            if (s.kind == PathSegmentKind.CatmullRom) s.p3 = s.p1 + direction * Math.Max(2 * length, Vector2.Distance(s.p3, s.p1));
            return s;
        }
    }
}
