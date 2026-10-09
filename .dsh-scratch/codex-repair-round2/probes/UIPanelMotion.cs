#error UIMOTION_COMPILE_PROBE_UIPanelMotion
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>可选面板配方；没有组件时 UIBase 仍同步开关，不需要迁移已有场景。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UIPanelMotion : MonoBehaviour
    {
        [SerializeField] private Vector2 enterOffset = new Vector2(2, 0);
        [SerializeField] private Vector2 exitOffset = new Vector2(-2, 0);
        [SerializeField] private float duration = .3f;
        [SerializeField] private bool useUnscaledTime = true;
        private Vector2 home;
        private bool captured;
        /// <summary>跳过动画时直接写入终态，让面板显隐与位置保持一致。</summary>
        public void Snap(bool opening)
        {
            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            if (!captured) { home = rect.anchoredPosition; captured = true; }
            var end = home + (opening ? Vector2.zero : exitOffset * (Mathf.Abs(parent.rect.height) * .5f));
            if (!MotionOwnership.Acquire(rect)) return;
            try { RectBinder.Apply(rect, new MotionSample { position = end, scale = 1, weight = 1 }); }
            finally { MotionOwnership.Release(rect); }
        }
        public MotionHandle Play(bool opening, bool interrupted)
        {
            var rect = (RectTransform)transform;
            var parent = (RectTransform)rect.parent;
            if (!captured) { home = rect.anchoredPosition; captured = true; }
            var end = AnchorResolver.ToP(RectBinder.AnchoredToLocal(rect, home), parent);
            var now = AnchorResolver.ToP(RectBinder.AnchoredToLocal(rect, rect.anchoredPosition), parent);
            var start = opening && !interrupted ? end + enterOffset : now;
            var target = opening ? end : end + exitOffset;
            return new MotionPlan().Curve(PathSegment.Line(start, target)).Duration(duration)
                .Ease(Ease.CubicOut).Clock(new MotionClock(useUnscaledTime)).Bind(rect).Start();
        }
    }
}
