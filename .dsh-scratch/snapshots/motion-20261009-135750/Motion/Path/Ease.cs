using System;

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
}
