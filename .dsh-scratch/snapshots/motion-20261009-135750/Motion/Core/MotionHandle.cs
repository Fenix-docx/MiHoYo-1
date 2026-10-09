using System;

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
}
