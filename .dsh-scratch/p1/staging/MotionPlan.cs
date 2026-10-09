using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>配置期允许分配；几何输入采用父 RectTransform 的 p 空间。</summary>
    public sealed class MotionPlan
    {
        private AnchorRef from, to; 
        private bool hasFrom, hasTo, hasCurve, clamp;
        private PathSegment path;
        private PathChain chain;
        private float duration = 3f;
        private Ease ease = Motion.Ease.Linear; // 缓动
        private MotionClock clock; // 运动时钟
        private RectTransform target; // 联动跟随的目标
        public MotionPlan From(AnchorRef value) { from = value; hasFrom = true; hasCurve = false; return this; } 
        public MotionPlan To(AnchorRef value) { to = value; hasTo = true; hasCurve = false; return this; }
        public MotionPlan Curve(PathSegment value) { chain = null; path = value; hasCurve = true; hasFrom = hasTo = false; return this; }
        public MotionPlan Curve(PathChain value)
        {
            chain = value ?? throw new ArgumentNullException(nameof(value));
            hasCurve = true; hasFrom = hasTo = false; return this;
        }
        public MotionPlan Duration(float value) { duration = value; return this; }
        public MotionPlan Ease(Ease value) { ease = value; return this; } 
        public MotionPlan Clock(MotionClock value) { clock = value; return this; } // 允许外部共享时钟
        public MotionPlan Bind(RectTransform value) { target = value; return this; } // 联动
        public MotionPlan ClampOutside(bool value = true) { clamp = value; return this; }
        public MotionHandle Start()
        {
            if (target == null || !(target.parent is RectTransform parent))
                throw new InvalidOperationException("绑定目标必须具有 RectTransform 父级。");
            if (!hasCurve && !(hasFrom && hasTo)) throw new InvalidOperationException("请设置 Curve 或 From/To。");
            var track = hasCurve && chain != null
                ? new MotionTrack(chain, duration, ease, clock, target, new PathSpace { rect = parent }, clamp)
                : new MotionTrack(hasCurve ? path : PathSegment.Line(default, default), duration, ease, clock,
                target, new PathSpace { rect = parent }, !hasCurve, from, to, clamp);
            return MotionSystem.Instance.Register(track);
        }
    }
}
