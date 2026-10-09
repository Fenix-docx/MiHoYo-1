using System;
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
        private readonly PathChain chain;
        public MotionTrack(PathSegment path, float duration, Ease ease, MotionClock clock, RectTransform target,
            PathSpace space, bool anchors = false, AnchorRef from = default, AnchorRef to = default, bool clamp = false)
        {
            if (float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0) throw new ArgumentOutOfRangeException(nameof(duration));
            this.path = path; Duration = duration; this.ease = ease; Clock = clock ?? new MotionClock();
            Target = target; this.space = space; this.anchors = anchors; this.from = from; this.to = to; this.clamp = clamp;
            lut = new float[path.kind == PathSegmentKind.Line ? 2 : 257];
            MotionMath.BuildArcLut(path, lut);
            // 新段使用链条的真实端点与解析弧长，P0 单段路径仍走原实现。
            if (!anchors && path.kind != PathSegmentKind.Line && path.kind != PathSegmentKind.CubicBezier)
                chain = new PathChain(new[] { path });
        }
        public MotionTrack(PathChain chain, float duration, Ease ease, MotionClock clock, RectTransform target,
            PathSpace space, bool clamp = false)
            : this(PathSegment.Line(default, default), duration, ease, clock, target, space, clamp: clamp)
        {
            this.chain = chain ?? throw new ArgumentNullException(nameof(chain));
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
            var point = chain != null ? chain.EvaluateByArc(ease.Evaluate(t), clamp)
                : MotionMath.EvaluateByArc(path, ease.Evaluate(t), lut, clamp);
            var p = AnchorResolver.FromP(point, space.rect);
            return RectBinder.LocalToAnchored(Target, p);
        }
    }
}
