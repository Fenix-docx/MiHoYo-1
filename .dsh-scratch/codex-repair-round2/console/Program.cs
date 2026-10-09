using System;
using System.Collections.Generic;
using UIPackage;
using UIPackage.Motion;
using UnityEngine;

// 队列只使用对象身份；这个替身不模拟 MonoBehaviour、导航器或 Unity 生命周期。
namespace UIPackage { public class UIBase { } }
class Program
{
    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
    static bool Bits(Vector2 a, Vector2 b) => BitConverter.SingleToInt32Bits(a.x) == BitConverter.SingleToInt32Bits(b.x)
        && BitConverter.SingleToInt32Bits(a.y) == BitConverter.SingleToInt32Bits(b.y);
    static void Main()
    {
        var path = PathSegment.CubicBezier(new Vector2(0, 0), new Vector2(.1f, 2), new Vector2(2, -1), new Vector2(3, 1));
        var lut = new float[257]; float length = MotionMath.BuildArcLut(path, lut);
        double sum = 0, squares = 0; var last = path.p0;
        for (int i = 1; i <= 100; i++)
        {
            var p = MotionMath.EvaluateByArc(path, Ease.Linear.Evaluate(i / 100f), lut);
            double distance = Vector2.Distance(last, p); last = p; sum += distance; squares += distance * distance;
        }
        double mean = sum / 100, cv = Math.Sqrt(squares / 100 - mean * mean) / mean;
        Check(cv < .02, "arc uniformity");
        Console.WriteLine($"ARC intervals=100 LUT=257 length={length:R} mean={mean:R} std/mean={cv:R} threshold=0.02 PASS");
        foreach (float time in new[] { -.1f, .7f })
        {
            float u = Ease.OutBack().Evaluate(time); Check(u < 0 || u > 1, "overshoot");
            var endpoint = u < 0 ? path.p0 : path.p3; var tangent = path.Tangent(u < 0 ? 0 : 1);
            var delta = MotionMath.EvaluateByArc(path, u, lut) - endpoint;
            float cross = delta.x * tangent.y - delta.y * tangent.x, dot = Vector2.Dot(delta, tangent);
            float expected = (u < 0 ? u : u - 1) * length;
            Check(Math.Abs(cross) < 1e-5 && Math.Abs(dot - expected) < 1e-5, "tangent extension");
            Check(Bits(MotionMath.EvaluateByArc(path, u, lut, true), endpoint), "clamp outside");
            Console.WriteLine($"BACK t={time:R} u={u:R} cross={cross:R} projection={dot:R} expected={expected:R} PASS");
        }
        float elastic = Ease.OutElastic(1.4f).Evaluate(.1f);
        Check(elastic > 1, "elastic overshoot"); Console.WriteLine($"ELASTIC overshoot=1.4 t=0.1 u={elastic:R} PASS");
        var a = new MotionClock(); var b = new MotionClock();
        for (int i = 0; i < 8; i++) a.Advance(.0625);
        b.Advance(.125); b.Advance(.375);
        var pa = MotionMath.EvaluateByArc(path, (float)a.Time, lut);
        var pb = MotionMath.EvaluateByArc(path, (float)b.Time, lut);
        Check(Bits(pa, pb) && Bits(pa, MotionMath.EvaluateByArc(path, (float)a.Time, lut)), "bitwise deterministic");
        Console.WriteLine($"DETERMINISM dtA=[0.0625 x8] dtB=[0.125,0.375] t={a.Time:R} xBits={BitConverter.SingleToInt32Bits(pa.x):X8} yBits={BitConverter.SingleToInt32Bits(pa.y):X8} repeated=equal sequences=equal PASS");
        var child = new MotionClock(parent: a); a.Seek(1); Check(child.Time == .5, "shared seek");
        child.Pause(); a.Seek(2); Check(child.Time == .5, "pause"); child.Resume(); a.Seek(3);
        Check(child.Time == 1.5, "resume"); child.TimeScale = -1; a.Seek(4); Check(child.Time == .5, "reverse");
        Console.WriteLine("CLOCK shared-seek pause resume reverse PASS");
        var before = MotionMath.AnchorPoint(new Vector2(-50,-30), new Vector2(100,60), AnchorEdge.Right, new Vector2(.1f,0));
        var after = MotionMath.AnchorPoint(new Vector2(-100,-40), new Vector2(200,80), AnchorEdge.Right, new Vector2(.1f,0));
        Check(after.x - before.x == 52, "relative anchor");
        Console.WriteLine($"ANCHOR right+0.1short before=({before.x:R},{before.y:R}) after=({after.x:R},{after.y:R}) deltaX={after.x-before.x:R} PASS (pure rectangle math only)");
        var warnings = new List<string>(); var q = new UINavigationQueue(warnings.Add); var target = new UIBase();
        q.Enqueue(new NavIntent {kind=NavIntentKind.Push,target=target});q.Enqueue(new NavIntent {kind=NavIntentKind.Push,target=target});
        Check(q.Count == 1, "duplicate push"); q.Clear();
        for(int i=0;i<5;i++) q.Enqueue(new NavIntent {kind=NavIntentKind.Back});
        string flags="";
        for(int i=0;i<5;i++) { q.TryDequeue(out var intent); Check(intent.instant == (i>=3), "back instant threshold");flags += intent.instant ? "I" : "A"; }
        q.Enqueue(new NavIntent {kind=NavIntentKind.Push,target=target});q.Enqueue(new NavIntent {kind=NavIntentKind.Back});
        q.Enqueue(new NavIntent {kind=NavIntentKind.ToRoot});Check(q.Count==1,"root clear");q.TryDequeue(out var root);Check(root.kind==NavIntentKind.ToRoot,"root retained");
        q.Enqueue(new NavIntent {kind=NavIntentKind.Push,target=target});q.Enqueue(new NavIntent {kind=NavIntentKind.CloseAll});Check(q.Count==1,"close clear");
        q.Clear();q.MaxQueue=2;
        q.Enqueue(new NavIntent {kind=NavIntentKind.Push,targetId="A"});q.Enqueue(new NavIntent {kind=NavIntentKind.Push,targetId="B"});
        Check(!q.Enqueue(new NavIntent {kind=NavIntentKind.Back}),"overflow rejected");q.TryDequeue(out var first);q.TryDequeue(out var second);
        Check(first.targetId=="A" && second.targetId=="B" && warnings.Count==1,"overflow order");
        Console.WriteLine($"QUEUE duplicate-fold=1 Back5={flags} ToRoot-clear=PASS CloseAll-clear=PASS overflow=reject-newest order=A,B warnings={warnings.Count} PASS");
        Console.WriteLine($"RESULT {checks} checks PASS; Unity lifecycle/navigation/watchdog/RectTransform resolution NOT EXECUTED; PathChain is P1, not implemented.");
    }
}
