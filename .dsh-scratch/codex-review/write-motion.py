from pathlib import Path
base=Path('Assets/A_UI/UI/Motion')
def w(p,s):
 p=base/p;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(s.strip()+'\n',encoding='utf-8')
w('Core/MotionClock.cs', '''using System;

namespace UIPackage.Motion
{
    /// <summary>子时钟读取父时间的仿射映射，共享 Seek，不按轨道数量重复推进。</summary>
    public sealed class MotionClock
    {
        private double time, origin, scale = 1;
        private bool paused;
        private long lastTick = long.MinValue;
        public MotionClock Parent { get; }
        public bool UseUnscaledTime { get; }
        public MotionClock(bool useUnscaledTime = false, MotionClock parent = null)
        {
            Parent = parent;
            UseUnscaledTime = useUnscaledTime;
            origin = parent == null ? 0 : parent.Time;
        }
        public double Time => time + (Parent != null && !paused ? (Parent.Time - origin) * scale : 0);
        public double TimeScale
        {
            get => scale;
            set { Check(value); Rebase(); scale = value; }
        }
        public bool IsPaused => paused;
        public void Pause() { Rebase(); paused = true; }
        public void Resume() { Rebase(); paused = false; }
        public void Seek(double seconds) { Check(seconds); time = seconds; origin = Parent == null ? 0 : Parent.Time; }
        public void Advance(double delta)
        {
            Check(delta);
            if (Parent != null) throw new InvalidOperationException("子时钟由父时钟推进。");
            if (!paused) time += delta * scale;
        }
        internal void Tick(long token, double scaled, double unscaled)
        {
            if (lastTick == token) return;
            lastTick = token;
            if (Parent != null) Parent.Tick(token, scaled, unscaled);
            else Advance(UseUnscaledTime ? unscaled : scaled);
        }
        private void Rebase() { time = Time; origin = Parent == null ? 0 : Parent.Time; }
        private static void Check(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}''')
w('Core/MotionTypes.cs','''using UnityEngine;

namespace UIPackage.Motion
{
    public struct MotionSample
    {
        public Vector2 position, velocity;
        public float rotation, scale, weight;
    }
    public enum AnchorEdge { Center, Left, Right, Top, Bottom, Slot }
    public enum MotionChannel { AnchoredPosition }
    public struct PathSpace { public RectTransform rect; }
    public struct BindSpace { public RectTransform target; }
    /// <summary>offset 使用目标短边比例；Slot 对应目标的直接 RectTransform 子节点。</summary>
    public struct AnchorRef
    {
        public RectTransform target;
        public AnchorEdge edge;
        public Vector2 offset;
        public RectTransform space;
        public int slotIndex;
        public AnchorRef(RectTransform target, AnchorEdge edge = AnchorEdge.Center,
            Vector2 offset = default, RectTransform space = null, int slotIndex = 0)
        {
            this.target = target; this.edge = edge; this.offset = offset;
            this.space = space; this.slotIndex = slotIndex;
        }
    }
}''')
w('Path/PathSegment.cs','''using System;
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
}''')
w('Path/Ease.cs','''using System;

namespace UIPackage.Motion
{
    public enum EaseKind { Linear, CubicOut, QuintOut, OutBack, OutElastic }
    public readonly struct Ease
    {
        public readonly EaseKind Kind;
        public readonly float Overshoot;
        private Ease(EaseKind kind, float overshoot = 1) { Kind = kind; Overshoot = overshoot; }
        public static Ease Linear => new Ease(EaseKind.Linear);
        public static Ease CubicOut => new Ease(EaseKind.CubicOut);
        public static Ease QuintOut => new Ease(EaseKind.QuintOut);
        public static Ease OutBack(float overshoot = 1.70158f) => new Ease(EaseKind.OutBack, overshoot);
        public static Ease OutElastic(float overshoot = 1) => new Ease(EaseKind.OutElastic, overshoot);
        // 不钳制输出：几何层负责沿切线外推，缓动层只重映射时间。
        public float Evaluate(float t)
        {
            float x = t - 1;
            switch (Kind)
            {
                case EaseKind.Linear: return t;
                case EaseKind.CubicOut: return 1 + x * x * x;
                case EaseKind.QuintOut: return 1 + x * x * x * x * x;
                case EaseKind.OutBack: return 1 + (Overshoot + 1) * x * x * x + Overshoot * x * x;
                case EaseKind.OutElastic:
                    if (t == 0 || t == 1) return t;
                    double amplitude = Math.Max(1, Math.Abs(Overshoot));
                    double phase = Math.Asin(1 / amplitude);
                    return (float)(1 + amplitude * Math.Pow(2, -10 * t) * Math.Sin(t * (2 * Math.PI / .3) - phase));
                default: throw new ArgumentOutOfRangeException();
            }
        }
    }
}''')
w('Core/MotionMath.cs','''using System;
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
}''')
w('Tracks/AnchorResolver.cs','''using System;
using UnityEngine;

namespace UIPackage.Motion
{
    public static class AnchorResolver
    {
        /// <summary>每次采样读取 rect；不缓存布局结果。返回指定父空间的 local 坐标。</summary>
        public static Vector2 Resolve(AnchorRef anchor, RectTransform parent)
        {
            if (parent == null || (anchor.space != null && anchor.space != parent))
                throw new ArgumentException("P0 锚点必须使用绑定对象的父空间。");
            var target = anchor.target != null ? anchor.target : parent;
            var edge = anchor.edge;
            if (edge == AnchorEdge.Slot)
            {
                if (anchor.slotIndex < 0 || anchor.slotIndex >= target.childCount)
                    throw new ArgumentOutOfRangeException(nameof(anchor.slotIndex));
                target = target.GetChild(anchor.slotIndex) as RectTransform;
                edge = AnchorEdge.Center;
            }
            // TODO：P2 才实现跨父级 Reparent 与共同画布空间。
            if (target == null || (target != parent && target.parent != parent))
                throw new ArgumentException("P0 不支持跨父级锚点。");
            var point = MotionMath.AnchorPoint(target.rect.min, target.rect.size, edge, anchor.offset);
            return target == parent ? point : (Vector2)parent.InverseTransformPoint(target.TransformPoint(point));
        }
        public static Vector2 ToP(Vector2 local, RectTransform space) =>
            (local - space.rect.center) * (2 / Math.Max(.00001f, Math.Abs(space.rect.height)));
        public static Vector2 FromP(Vector2 p, RectTransform space) =>
            space.rect.center + p * (Math.Abs(space.rect.height) * .5f);
    }
}''')
w('Tracks/MotionTrack.cs','''using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>P0 只输出位置；单段的 LUT 不依赖帧历史，PathChain 在 P1 实现。</summary>
    public sealed class MotionTrack
    {
        public readonly float Duration;
        public readonly MotionClock Clock;
        public readonly MotionChannel Channel = MotionChannel.AnchoredPosition;
        public readonly RectTransform Target;
        private readonly PathSpace space;
        private readonly Ease ease;
        private readonly bool anchors, clamp;
        private readonly AnchorRef from, to;
        private PathSegment path;
        private readonly float[] lut;
        public MotionTrack(PathSegment path, float duration, Ease ease, MotionClock clock, RectTransform target,
            PathSpace space, bool anchors = false, AnchorRef from = default, AnchorRef to = default, bool clamp = false)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
            this.path = path; Duration = duration; this.ease = ease; Clock = clock ?? new MotionClock();
            Target = target; this.space = space; this.anchors = anchors; this.from = from; this.to = to; this.clamp = clamp;
            lut = new float[path.kind == PathSegmentKind.Line ? 2 : 257];
            MotionMath.BuildArcLut(path, lut);
        }
        public MotionSample Sample(double seconds)
        {
            if (anchors)
            {
                var a = AnchorResolver.ToP(AnchorResolver.Resolve(from, space.rect), space.rect);
                var b = AnchorResolver.ToP(AnchorResolver.Resolve(to, space.rect), space.rect);
                path = PathSegment.Line(a, b);
                MotionMath.BuildArcLut(path, lut);
            }
            float t = Duration == 0 ? 1 : MotionMath.Clamp01((float)(seconds / Duration));
            var p = Position(t);
            // 固定的解析邻域差分不读取上一帧，Seek 与帧率不会改变速度输出。
            float lo = Math.Max(0, t - .0001f), hi = Math.Min(1, t + .0001f);
            var v = Duration == 0 ? Vector2.zero : (Position(hi) - Position(lo)) / ((hi - lo) * Duration);
            return new MotionSample { position = p, velocity = v, scale = 1, weight = 1 };
        }
        private Vector2 Position(float t)
        {
            var p = AnchorResolver.FromP(MotionMath.EvaluateByArc(path, ease.Evaluate(t), lut, clamp), space.rect);
            return RectBinder.LocalToAnchored(Target, p);
        }
    }
}''')
w('Bind/RectBinder.cs','''using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>唯一的位置写入口；补偿 anchor/pivot，支持拉伸锚点。</summary>
    public static class RectBinder
    {
        public static Vector2 LocalToAnchored(RectTransform target, Vector2 local)
        {
            var parent = (RectTransform)target.parent;
            var a = target.anchorMin + Vector2.Scale(target.anchorMax - target.anchorMin, target.pivot);
            return local - (parent.rect.min + Vector2.Scale(parent.rect.size, a));
        }
        public static Vector2 AnchoredToLocal(RectTransform target, Vector2 anchored) =>
            anchored - LocalToAnchored(target, Vector2.zero);
        internal static void Apply(RectTransform target, MotionSample sample) { target.anchoredPosition = sample.position; }
    }
}''')
