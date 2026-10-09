using System;
using NUnit.Framework;
using UIPackage.Motion;
using UnityEngine;

public class MotionTests
{
    private static PathSegment Curve => PathSegment.CubicBezier(new Vector2(0, 0), new Vector2(.1f, 2),
        new Vector2(2, -1), new Vector2(3, 1));
    [Test]
    public void Test01_ArcLengthHasLessThanTwoPercentVariation()
    {
        var path = Curve;
        var lut = new float[257]; MotionMath.BuildArcLut(path, lut);
        double sum = 0, squares = 0;
        var last = path.p0;
        for (int i = 1; i <= 100; i++)
        {
            var p = MotionMath.EvaluateByArc(path, Ease.Linear.Evaluate(i / 100f), lut);
            double distance = Vector2.Distance(last, p); last = p;
            sum += distance; squares += distance * distance;
        }
        double mean = sum / 100;
        Assert.Less(Math.Sqrt(squares / 100 - mean * mean) / mean, .02);
    }
    [TestCase(-.1f)]
    [TestCase(.7f)]
    public void Test03_BackOvershootExtendsEndpointTangent(float time)
    {
        var path = Curve;
        var lut = new float[257]; float length = MotionMath.BuildArcLut(path, lut);
        float u = Ease.OutBack().Evaluate(time);
        Assert.IsTrue(u < 0 || u > 1);
        var endpoint = u < 0 ? path.p0 : path.p3;
        var tangent = path.Tangent(u < 0 ? 0 : 1);
        var delta = MotionMath.EvaluateByArc(path, u, lut) - endpoint;
        Assert.Less(Math.Abs(delta.x * tangent.y - delta.y * tangent.x), 1e-5);
        Assert.AreEqual((u < 0 ? u : u - 1) * length, Vector2.Dot(delta, tangent), 1e-5);
    }
    [Test]
    public void Test04_DeterministicSamplingAndSharedSeek()
    {
        var path = Curve; var lut = new float[257]; MotionMath.BuildArcLut(path, lut);
        var a = new MotionClock(); var b = new MotionClock();
        for (int i = 0; i < 8; i++) a.Advance(.0625);
        b.Advance(.125); b.Advance(.375);
        var p = MotionMath.EvaluateByArc(path, (float)a.Time, lut);
        var q = MotionMath.EvaluateByArc(path, (float)b.Time, lut);
        Assert.AreEqual(BitConverter.SingleToInt32Bits(p.x), BitConverter.SingleToInt32Bits(q.x));
        Assert.AreEqual(BitConverter.SingleToInt32Bits(p.y), BitConverter.SingleToInt32Bits(q.y));
        var child = new MotionClock(parent: a); a.Seek(1); Assert.AreEqual(.5, child.Time);
        child.Pause(); a.Seek(2); Assert.AreEqual(.5, child.Time);
        child.Resume(); a.Seek(3); Assert.AreEqual(1.5, child.Time);
    }
    [Test]
    public void Test09_AnchorResamplesResizedRect()
    {
        var parent = new GameObject("space", typeof(RectTransform));
        var source = new GameObject("anchor", typeof(RectTransform));
        try
        {
            var root = (RectTransform)parent.transform;
            var rect = (RectTransform)source.transform; rect.SetParent(root, false);
            rect.sizeDelta = new Vector2(100, 60);
            var anchor = new AnchorRef(rect, AnchorEdge.Right, new Vector2(.1f, 0));
            var before = AnchorResolver.Resolve(anchor, root);
            rect.sizeDelta = new Vector2(200, 80);
            var after = AnchorResolver.Resolve(anchor, root);
            Assert.AreEqual(52, after.x - before.x, 1e-5);
        }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }
    [Test]
    public void SkipCompletesExactlyOnceAndOldHandleCannotKillReusedSlot()
    {
        var root = new GameObject("space", typeof(RectTransform));
        var target = new GameObject("target", typeof(RectTransform));
        try
        {
            target.transform.SetParent(root.transform, false);
            var rect = (RectTransform)target.transform;
            var h = new MotionPlan().Curve(PathSegment.Line(Vector2.zero, Vector2.one)).Bind(rect).Start();
            int count = 0; h.OnComplete(() => count++); h.Skip(); h.Skip();
            Assert.AreEqual(1, count); Assert.IsTrue(h.IsDone);
            var next = new MotionPlan().Curve(PathSegment.Line(Vector2.zero, Vector2.one)).Bind(rect).Start();
            h.Kill(false); Assert.IsTrue(next.IsValid); next.Kill(false);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(MotionSystem.Instance.gameObject); }
    }
}
