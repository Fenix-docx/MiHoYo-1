#error UIMOTION_COMPILE_PROBE_MotionSystem
using System;
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
                    if (Application.isPlaying) DontDestroyOnLoad(host);
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
}
