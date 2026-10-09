using System;
using NUnit.Framework;
using UIPackage.Motion;
using UnityEditor;
using UnityEngine;

public class MotionPathP1Tests
{
    [Test] public void ChainContinuityUniformityAndSegmentMath() { MotionPathP1Metrics.Run(TestContext.WriteLine); }

    [Test]
    public void ThreeRectResolutionsNormalizeToSameShape()
    {
        var chain = MotionPathP1Metrics.Mixed(); var baseline = new Vector2[100];
        foreach (int width in new[] { 1920, 2560, 1440 })
        {
            var root = new GameObject("P1 space", typeof(RectTransform));
            try
            {
                var rect = (RectTransform)root.transform; rect.pivot = new Vector2(.3f, .7f);
                rect.sizeDelta = new Vector2(width, 1080);
                for (int i = 0; i < 100; i++)
                {
                    var p = chain.EvaluateByArc(i / 99f);
                    var normalized = AnchorResolver.ToP(AnchorResolver.FromP(p, rect), rect);
                    if (width == 1920) baseline[i] = normalized;
                    Assert.Less(Vector2.Distance(normalized, baseline[i]), 1e-3);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
    [Test]
    public void TrackOvershootClampSeekAndPlanCompatibility()
    {
        var root = new GameObject("P1 parent", typeof(RectTransform));
        var child = new GameObject("P1 target", typeof(RectTransform));
        try
        {
            var parent = (RectTransform)root.transform; parent.sizeDelta = new Vector2(1920, 1080);
            var target = (RectTransform)child.transform; target.SetParent(parent, false);
            var chain = MotionPathP1Metrics.Mixed(); var ease = Ease.OutBack();
            var track = new MotionTrack(chain, 1, ease, null, target, new PathSpace { rect = parent });
            var endpoint = chain.Evaluate(1); var tangent = chain.Tangent(1);
            var sample = track.Sample(.7).position;
            var local = sample + (Vector2)target.localPosition - target.anchoredPosition;
            var delta = AnchorResolver.ToP(local, parent) - endpoint;
            Assert.Greater(ease.Evaluate(.7f), 1);
            Assert.Less(Math.Abs(delta.x * tangent.y - delta.y * tangent.x), 1e-5);
            Assert.AreEqual(sample, track.Sample(.7).position);
            var clamped = new MotionTrack(chain, 1, ease, null, target, new PathSpace { rect = parent }, true);
            Assert.Less(Vector2.Distance(clamped.Sample(.7).position, clamped.Sample(1).position), 1e-4);
            var h = new MotionPlan().Curve(chain).Duration(1).Bind(target).Start(); h.Skip(); Assert.IsTrue(h.IsDone);
            h = new MotionPlan().Curve(chain).Curve(PathSegment.Line(Vector2.zero, Vector2.one)).Bind(target).Start(); h.Skip();
            Assert.AreEqual(AnchorResolver.FromP(Vector2.one, parent), target.anchoredPosition);
            h = new MotionPlan().Curve(chain).From(new AnchorRef(parent, AnchorEdge.Left)).To(new AnchorRef(parent, AnchorEdge.Right)).Bind(target).Start();
            h.Skip(); Assert.AreEqual(AnchorResolver.Resolve(new AnchorRef(parent, AnchorEdge.Right), parent), target.anchoredPosition);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(MotionSystem.Instance.gameObject);
        }
    }
    [Test]
    public void AssetRebuildAndUndoDoNotReturnStaleChains()
    {
        var asset = ScriptableObject.CreateInstance<MotionPathAsset>();
        try
        {
            var old = asset.BuildChain(); var original = asset.GetPoint(0);
            Undo.RecordObject(asset, "P1 test edit"); asset.SetPoint(0, original + Vector2.one); Undo.FlushUndoRecordObjects();
            var updated = asset.BuildChain();
            Assert.AreEqual(original, old.Evaluate(0)); Assert.AreEqual(original + Vector2.one, updated.Evaluate(0));
            Undo.PerformUndo(); Assert.AreEqual(original, asset.BuildChain().Evaluate(0));
        }
        finally { Undo.ClearUndo(asset); UnityEngine.Object.DestroyImmediate(asset); }
    }
}
