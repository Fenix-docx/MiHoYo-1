#error UIMOTION_COMPILE_PROBE_MotionClock
using System;

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
}
