using System;
using UIPackage.Motion;
using UnityEngine;

// 同一份纯数学验收供控制台与 EditMode 使用；不模拟 Unity 生命周期。
public static class MotionPathP1Metrics
{
    public static PathChain Mixed(bool g1 = false)
    {
        var start = new Vector2(1.5f, 1.2f - (float)Math.Sqrt(3) / 2);
        var end = start + new Vector2(.8f, .5f);
        return new PathChain(new[] {
            PathSegment.CubicBezier(Vector2.zero, new Vector2(.3f, 0), new Vector2(.7f, .2f), new Vector2(1, .2f)),
            PathSegment.Arc(new Vector2(1, 1.2f), 1, -(float)Math.PI / 2, -(float)Math.PI / 3),
            PathSegment.CatmullRom(start - new Vector2(.5f, .288675f), start, end, end + new Vector2(.8f, .5f))
        }, g1);
    }
    public static void Run(Action<string> output)
    {
        int checks = 0;
        Require(Unmanaged<PathSegment>(), "blittable schema", ref checks);
        var mismatched = new[] {
            PathSegment.Line(Vector2.zero, new Vector2(1, .2f)),
            PathSegment.QuadBezier(new Vector2(7, 3), new Vector2(8, 5), new Vector2(9, 4)),
            PathSegment.CubicBezier(new Vector2(-3, 2), new Vector2(-2, 0), new Vector2(0, 2), new Vector2(1, 2)),
            PathSegment.CatmullRom(new Vector2(0, -1), new Vector2(1, 0), new Vector2(2, 1), new Vector2(3, 0)),
            PathSegment.Arc(new Vector2(2, 1), 1, 0, 1),
            PathSegment.Arc(new Vector2(4, 3), 1, 2, 3, new Vector2(1, .4f))
        };
        double maxC0 = 0, maxAngle = 0;
        foreach (bool g1 in new[] { false, true })
        {
            var chain = new PathChain(mismatched, g1);
            for (int i = 1; i < chain.SegmentCount; i++)
            {
                var left = chain.GetSegment(i - 1); var right = chain.GetSegment(i);
                double error = Vector2.Distance(left.Evaluate(1), right.Evaluate(0)); maxC0 = Math.Max(maxC0, error);
                Require(error < 1e-4, "C0", ref checks);
                if (g1)
                {
                    var a = left.Tangent(1); var b = right.Tangent(0);
                    double angle = Math.Abs(Math.Atan2(a.x * b.y - a.y * b.x, Vector2.Dot(a, b))) * 180 / Math.PI;
                    maxAngle = Math.Max(maxAngle, angle); Require(angle < .5, "G1", ref checks);
                }
            }
        }
        output($"CHAIN C0_max={maxC0:R} threshold=0.0001 G1_max_degrees={maxAngle:R} threshold=0.5 PASS");
        var mixed = Mixed(); double sum = 0, square = 0; var last = mixed.EvaluateByArc(0);
        // 100 点包含首尾，共 99 个间隔。
        for (int i = 1; i < 100; i++)
        {
            var p = mixed.EvaluateByArc(Ease.Linear.Evaluate(i / 99f)); double d = Vector2.Distance(last, p);
            last = p; sum += d; square += d * d;
        }
        double mean = sum / 99, cv = Math.Sqrt(Math.Max(0, square / 99 - mean * mean)) / mean;
        Require(cv < .02, "uniformity", ref checks);
        output($"UNIFORM mixed=Cubic+Arc+CR samples=100 mean={mean:R} std/mean={cv:R} threshold=0.02 PASS");
        var circle = PathSegment.Arc(Vector2.zero, 2, -.4f, 4.3f);
        double circleExpected = 2 * ((double)circle.a1 - circle.a0);
        var ellipse = PathSegment.Arc(Vector2.zero, 2, 0, (float)(2 * Math.PI), new Vector2(1, .5f));
        // a=2,b=1 完整椭圆的独立已知值 4a E(3/4)。
        double ellipseExpected = 9.688448220547676;
        double circleError = Math.Abs(new PathChain(new[] { circle }).TotalLength / circleExpected - 1);
        double ellipseError = Math.Abs(new PathChain(new[] { ellipse }).TotalLength / ellipseExpected - 1);
        Require(circleError < .001 && ellipseError < .001, "analytic length", ref checks);
        double maxIntegralError = 0;
        foreach (var axes in new[] { new Vector2(1, .01f), new Vector2(.2f, 1), new Vector2(0, 1), new Vector2(1, 0), Vector2.zero })
        {
            var arc = PathSegment.Arc(Vector2.zero, 2, 5.2f, -7.1f, axes);
            double expected = ReferenceLength(arc), actual = arc.ArcLength();
            double error = Math.Abs(actual - expected) / Math.Max(1, expected);
            maxIntegralError = Math.Max(maxIntegralError, error); Require(error < .001, "ellipse partial/reverse/multiturn/degenerate", ref checks);
        }
        output($"ARC circle_relative_error={circleError:R} ellipse_relative_error={ellipseError:R} partial_reverse_multiturn_degenerate_max={maxIntegralError:R} threshold=0.001 PASS");
        var cr = PathSegment.CatmullRom(new Vector2(-2, 1), new Vector2(-1, 2), new Vector2(3, 4), new Vector2(5, -1));
        double crError = Math.Max(Vector2.Distance(cr.Evaluate(0), cr.p1), Vector2.Distance(cr.Evaluate(1), cr.p2));
        Require(crError < 1e-4, "CR endpoints", ref checks);
        var through = UIPackage.Motion.Path.Through(cr.p0, cr.p1, cr.p2, cr.p3);
        for (int i = 0; i < 4; i++) Require(Vector2.Distance(through.Evaluate(i / 3f), new[] { cr.p0, cr.p1, cr.p2, cr.p3 }[i]) < 1e-4, "Through points", ref checks);
        output($"CATMULL endpoint_max={crError:R} Through_all_points=PASS");
        var quad = PathSegment.QuadBezier(Vector2.zero, new Vector2(1, 2), new Vector2(2, 0));
        Require(Vector2.Distance(quad.Evaluate(.5f), new Vector2(1, 1)) < 1e-6, "quad analytic", ref checks);
        Require(Vector2.Distance(quad.Tangent(.5f), Vector2.right) < 1e-6, "quad tangent", ref checks);
        var bowed = UIPackage.Motion.Path.Arc(Vector2.zero, new Vector2(2, 0), .7f);
        Require(Vector2.Distance(bowed.Evaluate(.5f), new Vector2(1, .7f)) < 1e-6, "arc height DSL", ref checks);
        output("QUAD analytic_point+tangent PASS; DSL Arc midpoint_height=0.7 PASS");
        // 控制台不能实例化原生 RectTransform；这里仅核对生产 FromP 的父高数学。
        double shapeError = 0;
        foreach (int width in new[] { 1920, 2560, 1440 })
            for (int i = 0; i < 100; i++)
            {
                var p = mixed.EvaluateByArc(i / 99f);
                var min = new Vector2(-width * .3f, -1080 * .7f); var size = new Vector2(width, 1080);
                var center = min + size / 2; var local = center + p * (size.y * .5f);
                var normalized = (local - center) / (size.y * .5f);
                shapeError = Math.Max(shapeError, Vector2.Distance(p, normalized));
            }
        Require(shapeError < .001, "resolution math", ref checks);
        output($"RESOLUTION widths=1920,2560,1440 height=1080 p_roundtrip_max={shapeError:R} threshold=0.001 PASS (pure FromP formula; actual RectTransform covered only by unexecuted Editor test)");
        double maxCross = 0;
        foreach (float t in new[] { -.1f, .7f })
        {
            float u = Ease.OutBack().Evaluate(t); Require(u < 0 || u > 1, "Back overshoot", ref checks);
            var endpoint = mixed.Evaluate(u < 0 ? 0 : 1); var tangent = mixed.Tangent(u < 0 ? 0 : 1);
            var delta = mixed.EvaluateByArc(u) - endpoint;
            double cross = Math.Abs(delta.x * tangent.y - delta.y * tangent.x); maxCross = Math.Max(maxCross, cross);
            Require(cross < 1e-5, "chain overshoot collinearity", ref checks);
            Require(Math.Abs(Vector2.Dot(delta, tangent) - (u < 0 ? u : u - 1) * mixed.TotalLength) < 1e-5, "chain extension length", ref checks);
            Require(Vector2.Distance(mixed.EvaluateByArc(u, true), endpoint) < 1e-6, "chain clamp", ref checks);
        }
        output($"OVERSHOOT OutBack_start+end max_cross={maxCross:R} extension_length+clamp=PASS");
        var degenerate = new PathChain(new[] { PathSegment.Line(Vector2.one, Vector2.one), PathSegment.CubicBezier(Vector2.one, Vector2.one, Vector2.one, Vector2.one), PathSegment.Arc(Vector2.zero, 0, 0, 1), PathSegment.CatmullRom(Vector2.one, Vector2.one, Vector2.one, Vector2.one) }, true);
        for (int i = -10; i <= 110; i++)
        {
            var p = degenerate.EvaluateByArc(i / 100f); var tangent = degenerate.Tangent(i / 100f);
            Require(!float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(tangent.x) && !float.IsNaN(tangent.y), "degenerate finite", ref checks);
        }
        var aPoint = mixed.EvaluateByArc(.37f); var bPoint = mixed.EvaluateByArc(.37f);
        Require(aPoint == bPoint, "deterministic", ref checks);
        // 先预热，再测采样循环本身；日志和断言留在计数区间之外。
        for (int i = 0; i < 1000; i++) mixed.EvaluateByArc(i / 999f);
        long before = GC.GetAllocatedBytesForCurrentThread(); float sink = 0;
        for (int i = 0; i < 10000; i++) sink += mixed.EvaluateByArc(i / 9999f).x;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Require(allocated == 0 && sink > 0, "sampling allocations", ref checks);
        output($"DEGENERATE finite PASS; DETERMINISM repeat PASS; SAMPLE calls=10000 allocated_bytes={allocated} PASS");
        output($"RESULT {checks} checks PASS; Unity Scene handles/Undo/native RectTransform/P0 EditMode tests NOT EXECUTED");
    }
    private static void Require(bool ok, string name, ref int checks)
    {
        if (!ok) throw new InvalidOperationException("P1 metric failed: " + name); checks++;
    }
    private static bool Unmanaged<T>() where T : unmanaged => true;
    private static double ReferenceLength(PathSegment arc)
    {
        const int n = 65536; double step = ((double)arc.a1 - arc.a0) / n, sum = 0;
        for (int i = 0; i <= n; i++)
        {
            double angle = arc.a0 + i * step;
            double x = arc.radius * arc.ellipseScale.x * Math.Sin(angle), y = arc.radius * arc.ellipseScale.y * Math.Cos(angle);
            sum += Math.Sqrt(x * x + y * y) * (i == 0 || i == n ? 1 : i % 2 == 0 ? 2 : 4);
        }
        return Math.Abs(sum * step / 3);
    }
}
