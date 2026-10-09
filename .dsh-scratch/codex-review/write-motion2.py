exec(open('.dsh-scratch/codex-review/write-motion.py',encoding='utf-8-sig').read().split("w('Core/MotionClock.cs'")[0])
w('Integration/MotionOwnership.cs','''using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UIPackage.Motion
{
    public enum PositionOwner { Motion, Animator, DOTween, ScrollRect }
    /// <summary>静态声明属性归属；外部补间须先查询声明，不允许同属性动态抢占。</summary>
    [DisallowMultipleComponent]
    public sealed class MotionOwnership : MonoBehaviour
    {
        [SerializeField] private PositionOwner owner = PositionOwner.Motion;
        public PositionOwner Owner => owner;
        private static readonly HashSet<RectTransform> active = new HashSet<RectTransform>();
        public static bool Declare(RectTransform target, PositionOwner writer)
        {
            if (target == null) return false;
            var declaration = target.GetComponent<MotionOwnership>();
            if (declaration != null && declaration.owner != writer)
            {
                Debug.LogError("[UIMotion] anchoredPosition 所有权冲突，Animator/DOTween/ScrollRect 不可与 Motion 同时写入。", target);
                return false;
            }
            if (declaration == null)
            {
                declaration = target.gameObject.AddComponent<MotionOwnership>();
                declaration.owner = writer;
            }
            return true;
        }
        public static bool IsOwnedBy(RectTransform target, PositionOwner writer)
        {
            var declaration = target != null ? target.GetComponent<MotionOwnership>() : null;
            return declaration != null && declaration.owner == writer;
        }
        internal static bool Acquire(RectTransform target)
        {
            if (!Declare(target, PositionOwner.Motion)) return false;
            var animator = target.GetComponent<Animator>();
            var scroll = target.GetComponentInParent<ScrollRect>();
            // P4 才做逐曲线 Validator；P0 对本对象上启用的 Animator 保守报错。
            if ((animator != null && animator.enabled) || (scroll != null && scroll.content == target))
            {
                Debug.LogError("[UIMotion] Animator 或 ScrollRect.content 可能直接写入此位置，拒绝启动。", target);
                return false;
            }
            if (active.Add(target)) return true;
            Debug.LogError("[UIMotion] 同一 anchoredPosition 已有活动轨道；请显式 Kill 后接管。", target);
            return false;
        }
        internal static void Release(RectTransform target) { active.Remove(target); }
        private void OnDestroy() { active.Remove(transform as RectTransform); }
    }
}''')
w('Core/MotionHandle.cs','''using System;

namespace UIPackage.Motion
{
    /// <summary>generation 防止旧句柄修改已被复用的槽位；Kill(false) 不调用完成回调。</summary>
    public readonly struct MotionHandle
    {
        internal readonly MotionSystem system;
        internal readonly int index, generation;
        internal MotionHandle(MotionSystem system, int index, int generation)
        { this.system = system; this.index = index; this.generation = generation; }
        public bool IsValid => system != null && system.Valid(this);
        public bool IsDone => system != null && system.Done(this);
        public float Progress => system != null ? system.ProgressOf(this) : 0;
        public float ExpectedDuration => system != null ? system.DurationOf(this) : 0;
        public void Kill(bool snapToEnd = true) { if (system != null) system.End(this, snapToEnd); }
        public void Skip() { Kill(true); }
        public void OnComplete(Action callback) { if (system != null) system.Subscribe(this, callback); }
        public void OnUpdate(Action<MotionSample> callback) { if (system != null) system.SubscribeUpdate(this, callback); }
    }
}''')
w('Core/MotionSystem.cs','''using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UIPackage.Motion
{
    /// <summary>Update 早于所有 LateUpdate；槽位和布局租约只在开始/结束时分配或回收。</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MotionSystem : MonoBehaviour
    {
        private sealed class Slot
        {
            public int generation;
            public bool active, done, ending;
            public MotionTrack track;
            public double start;
            public float progress, duration;
            public Action complete;
            public Action<MotionSample> update;
            public LayoutElement layout;
            public bool ignoreLayout;
        }
        private readonly List<Slot> slots = new List<Slot>();
        private static MotionSystem instance;
        private static long tick;
        public static MotionSystem Instance
        {
            get
            {
                if (instance == null)
                {
                    var host = new GameObject("UIMotion System");
                    instance = host.AddComponent<MotionSystem>();
                    DontDestroyOnLoad(host);
                }
                return instance;
            }
        }
        public MotionHandle Register(MotionTrack track)
        {
            if (track == null || track.Target == null) throw new ArgumentNullException(nameof(track));
            if (!MotionOwnership.Acquire(track.Target)) return default;
            int index = slots.FindIndex(s => !s.active && !s.ending);
            if (index < 0) { index = slots.Count; slots.Add(new Slot()); }
            var slot = slots[index];
            slot.generation++; slot.active = true; slot.done = false;
            slot.track = track; slot.start = track.Clock.Time; slot.duration = track.Duration;
            slot.progress = 0; slot.complete = null; slot.update = null;
            var parent = track.Target.parent as RectTransform;
            var group = parent != null ? parent.GetComponent<LayoutGroup>() : null;
            slot.layout = null;
            if (group != null && group.isActiveAndEnabled)
            {
                slot.layout = track.Target.GetComponent<LayoutElement>();
                if (slot.layout == null) slot.layout = track.Target.gameObject.AddComponent<LayoutElement>();
                slot.ignoreLayout = slot.layout.ignoreLayout;
                slot.layout.ignoreLayout = true;
            }
            var handle = new MotionHandle(this, index, slot.generation);
            try
            {
                RectBinder.Apply(track.Target, track.Sample(0));
                if (track.Duration == 0) End(handle, true);
            }
            catch { End(handle, false); throw; }
            return handle;
        }
        private Slot Get(MotionHandle h) => h.index >= 0 && h.index < slots.Count &&
            slots[h.index].generation == h.generation ? slots[h.index] : null;
        internal bool Valid(MotionHandle h) => Get(h)?.active == true;
        internal bool Done(MotionHandle h) => Get(h)?.done == true;
        internal float ProgressOf(MotionHandle h) => Get(h)?.progress ?? 0;
        internal float DurationOf(MotionHandle h) => Get(h)?.duration ?? 0;
        internal void Subscribe(MotionHandle h, Action cb)
        {
            var slot = Get(h);
            if (slot == null || cb == null) return;
            if (slot.done) Invoke(cb); else if (slot.active) slot.complete += cb;
        }
        internal void SubscribeUpdate(MotionHandle h, Action<MotionSample> cb)
        { var slot = Get(h); if (slot != null && slot.active) slot.update += cb; }
        internal void End(MotionHandle h, bool complete)
        {
            var slot = Get(h);
            if (slot == null || !slot.active || slot.ending) return;
            slot.ending = true;
            var track = slot.track;
            try
            {
                if (complete && track.Target != null)
                    RectBinder.Apply(track.Target, track.Sample(track.Duration));
            }
            catch (Exception ex) { Debug.LogException(ex, this); }
            slot.active = false; slot.done = complete;
            if (complete) slot.progress = 1;
            var cb = slot.complete;
            slot.complete = null; slot.update = null; slot.track = null;
            MotionOwnership.Release(track.Target);
            if (slot.layout != null)
            {
                slot.layout.ignoreLayout = slot.ignoreLayout;
                var parent = slot.layout.transform.parent as RectTransform;
                if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
                slot.layout = null;
            }
            // 回调可以启动新轨道，但在回调返回前此槽位不能复用。
            if (complete) Invoke(cb);
            slot.ending = false;
        }
        private void Update() { Advance(Time.deltaTime, Time.unscaledDeltaTime); }
        /// <summary>显式推进入口也供 Editor 测试使用，不要求进入 Play Mode。</summary>
        public void Advance(double scaledDelta, double unscaledDelta)
        {
            long token = ++tick;
            int count = slots.Count;
            for (int i = 0; i < count; i++)
                if (slots[i].active) slots[i].track.Clock.Tick(token, scaledDelta, unscaledDelta);
            for (int i = 0; i < count; i++)
            {
                var slot = slots[i];
                if (!slot.active) continue;
                var h = new MotionHandle(this, i, slot.generation);
                if (slot.track.Target == null || !slot.track.Target.gameObject.activeInHierarchy) { End(h, false); continue; }
                try
                {
                    double elapsed = slot.track.Clock.Time - slot.start;
                    slot.progress = slot.duration == 0 ? 1 : MotionMath.Clamp01((float)(elapsed / slot.duration));
                    var sample = slot.track.Sample(elapsed);
                    RectBinder.Apply(slot.track.Target, sample);
                    slot.update?.Invoke(sample);
                    if (Valid(h) && elapsed >= slot.duration) End(h, true);
                }
                catch (Exception ex) { Debug.LogException(ex, this); End(h, false); }
            }
        }
        private void Invoke(Action cb)
        {
            if (cb == null) return;
            foreach (Action action in cb.GetInvocationList())
                try { action(); } catch (Exception ex) { Debug.LogException(ex, this); }
        }
        private void OnDestroy()
        {
            for (int i = 0; i < slots.Count; i++) End(new MotionHandle(this, i, slots[i].generation), false);
            if (instance == this) instance = null;
        }
    }
}''')
w('MotionPlan.cs','''using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>配置期允许分配；几何输入采用父 RectTransform 的 p 空间。</summary>
    public sealed class MotionPlan
    {
        private AnchorRef from, to;
        private bool hasFrom, hasTo, hasCurve, clamp;
        private PathSegment path;
        private float duration = .3f;
        private Ease ease = Motion.Ease.Linear;
        private MotionClock clock;
        private RectTransform target;
        public MotionPlan From(AnchorRef value) { from = value; hasFrom = true; hasCurve = false; return this; }
        public MotionPlan To(AnchorRef value) { to = value; hasTo = true; hasCurve = false; return this; }
        public MotionPlan Curve(PathSegment value) { path = value; hasCurve = true; hasFrom = hasTo = false; return this; }
        public MotionPlan Duration(float value) { duration = value; return this; }
        public MotionPlan Ease(Ease value) { ease = value; return this; }
        public MotionPlan Clock(MotionClock value) { clock = value; return this; }
        public MotionPlan Bind(RectTransform value) { target = value; return this; }
        public MotionPlan ClampOutside(bool value = true) { clamp = value; return this; }
        public MotionHandle Start()
        {
            if (target == null || !(target.parent is RectTransform parent))
                throw new InvalidOperationException("绑定目标必须具有 RectTransform 父级。");
            if (!hasCurve && !(hasFrom && hasTo)) throw new InvalidOperationException("请设置 Curve 或 From/To。");
            var track = new MotionTrack(hasCurve ? path : PathSegment.Line(default, default), duration, ease, clock,
                target, new PathSpace { rect = parent }, !hasCurve, from, to, clamp);
            return MotionSystem.Instance.Register(track);
        }
    }
}''')
w('Integration/UIPanelMotion.cs','''using UnityEngine;

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
}''')
