using System;
using System.Collections.Generic;
using NUnit.Framework;
using UIPackage;
using UIPackage.Motion;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>可控时钟的假面板，不进入 Play Mode；所有时长来自实际 MotionHandle。</summary>
public class NavigationTestPanel : UIBase
{
    public bool Animate;
    public int Started, Opened, Closed;
    public MotionClock Clock;
    protected override MotionHandle? PlayOpen() => Play();
    protected override MotionHandle? PlayClose() => Play();
    private MotionHandle? Play()
    {
        if (!Animate) return null;
        Started++;
        Clock = new MotionClock(true);
        return new MotionPlan().Curve(PathSegment.Line(Vector2.zero, Vector2.one))
            .Duration(1).Clock(Clock).Bind((RectTransform)transform).Start();
    }
    protected override void OnOpened() { Opened++; }
    protected override void OnClosed() { Closed++; }
}

public class UINavigationQueueTests
{
    private GameObject root;
    private UINavigator nav;
    private readonly List<NavigationTestPanel> pages = new List<NavigationTestPanel>();
    private double now;
    [SetUp]
    public void SetUp()
    {
        root = new GameObject("test canvas", typeof(RectTransform));
        var host = new GameObject("navigator"); host.transform.SetParent(root.transform);
        nav = host.AddComponent<UINavigator>();
        now = Time.unscaledTimeAsDouble;
        for (int i = 0; i < 7; i++)
        {
            var go = new GameObject("page" + i, typeof(RectTransform));
            go.transform.SetParent(root.transform, false); go.SetActive(false);
            var page = go.AddComponent<NavigationTestPanel>(); page.PanelId = "p" + i;
            pages.Add(page); nav.AddNode(page);
            if (i > 0) nav.AddChild(pages[i - 1], page);
        }
        nav.ResetToRoot();
    }
    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(MotionSystem.Instance.gameObject);
        pages.Clear();
    }
    private void Finish()
    {
        MotionSystem.Instance.Advance(1, 1); now += 1; nav.ProcessNavigation(now);
    }
    private void StackTo(int index)
    { for (int i = 1; i <= index; i++) nav.Push(pages[i]); }
    [Test]
    public void DefaultPanelPathRemainsSynchronous()
    {
        Assert.IsTrue(pages[0].IsOpen); Assert.AreEqual(1, pages[0].Opened);
        nav.Push(pages[1]);
        Assert.IsFalse(pages[0].IsOpen); Assert.AreEqual(1, pages[0].Closed);
        Assert.IsTrue(pages[1].IsOpen); Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test12_QueuedPushWaitsAndCommitsOnce()
    {
        pages[1].Animate = pages[2].Animate = true;
        int events = 0; nav.Navigated += (a, b) => events++;
        nav.Push(pages[1]); Assert.IsTrue(nav.Push(pages[2]));
        Assert.AreSame(pages[0], nav.Current); Assert.AreEqual(0, events);
        Finish(); Assert.AreSame(pages[1], nav.Current); Assert.AreEqual(1, events);
        Finish(); Assert.AreSame(pages[2], nav.Current); Assert.AreEqual(2, events);
        Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test13_TwoBacksReturnTwoLevels()
    {
        StackTo(2); foreach (var page in pages) page.Animate = true;
        nav.Back(); nav.Back(); Finish(); Finish();
        Assert.AreSame(pages[0], nav.Current);
    }
    [Test]
    public void Test14_RepeatedPushCollapses()
    {
        pages[1].Animate = true; int events = 0; nav.Navigated += (a, b) => events++;
        nav.Push(pages[1]); nav.Push(pages[1]); nav.Push(pages[1]);
        Finish(); Assert.AreEqual(1, events); Assert.AreSame(pages[1], nav.Current);
        nav.Back(); Finish(); Assert.AreSame(pages[0], nav.Current);
    }
    [Test]
    public void Test15_FourthAndFifthBackAreInstant()
    {
        StackTo(5); foreach (var page in pages) page.Animate = true;
        for (int i = 0; i < 5; i++) nav.Back();
        Finish(); Finish(); Finish();
        Assert.AreSame(pages[0], nav.Current); Assert.IsFalse(nav.IsNavigating);
        int started = 0; foreach (var page in pages) started += page.Started;
        Assert.AreEqual(6, started); Assert.AreEqual(0, pages[0].Started); Assert.AreEqual(0, pages[1].Started);
    }
    [Test]
    public void Test16_ToRootClearsPendingPushes()
    {
        pages[1].Animate = true; nav.Push(pages[1]); nav.Push(pages[2]); nav.Push(pages[3]); nav.ResetToRoot();
        Finish(); Finish(); Assert.AreSame(pages[0], nav.Current); Assert.AreEqual(0, pages[2].Opened);
    }
    [Test]
    public void Test17_RemovedTargetIsDroppedAndNextIntentStillRuns()
    {
        pages[1].Animate = true; nav.Push(pages[1]); nav.Push("p2"); nav.Back(); nav.RemoveNode(pages[2]);
        LogAssert.Expect(LogType.Warning, "[UINavigator] 目标为空或已从导航树移除，丢弃意图。");
        Finish(); Finish(); Assert.AreSame(pages[0], nav.Current); Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test18_WatchdogUsesUnscaledTimeAndContinuesQueue()
    {
        pages[1].Animate = true; float old = Time.timeScale;
        try
        {
            Time.timeScale = 0; nav.Push(pages[1]); pages[1].Clock.Pause(); nav.Back();
            nav.ProcessNavigation(now + .1); Assert.AreSame(pages[0], nav.Current);
            LogAssert.Expect(LogType.Error, "[UINavigator] 转移看门狗超时，强制完成并继续队列。");
            nav.ProcessNavigation(now + 4); Assert.AreSame(pages[1], nav.Current);
            Finish(); Assert.AreSame(pages[0], nav.Current); Assert.IsFalse(nav.IsNavigating);
        }
        finally { Time.timeScale = old; }
    }
    [Test]
    public void Test18_CompletedHandleWinsEvenIfCallbackWasLost()
    {
        pages[1].Animate = true; nav.Push(pages[1]);
        var slots = (System.Collections.IList)typeof(MotionSystem).GetField("slots",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(MotionSystem.Instance);
        // ??????????????????????????? IsDone?
        foreach (var slot in slots) slot.GetType().GetField("complete").SetValue(slot, null);
        MotionSystem.Instance.Advance(1, 1);
        Assert.IsTrue(pages[1].IsTransitioning);
        nav.ProcessNavigation(now + 100);
        Assert.IsFalse(pages[1].IsTransitioning); Assert.AreSame(pages[1], nav.Current);
        Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test18_DisabledPanelInvalidatesHandleButQueueRecovers()
    {
        pages[1].Animate = true; nav.Push(pages[1]); pages[1].gameObject.SetActive(false);
        MotionSystem.Instance.Advance(.1, .1);
        LogAssert.Expect(LogType.Warning, "[UINavigator] ????????????");
        nav.ProcessNavigation(now + .1);
        Assert.AreSame(pages[1], nav.Current); Assert.IsTrue(pages[1].IsOpen);
        Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test19_TimeoutAlsoRestoresOriginalRaycastValue()
    {
        var group = pages[0].gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false;
        pages[0].Animate = true; nav.Push(pages[1]); pages[0].Clock.Pause();
        LogAssert.Expect(LogType.Error, "[UINavigator] ??????????????????");
        nav.ProcessNavigation(now + 4);
        Assert.IsFalse(group.blocksRaycasts); Assert.IsFalse(nav.IsNavigating);
    }
    [Test]
    public void Test19_LeavingInputRestoredAfterCompletion()
    {
        var group = pages[0].gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = true;
        pages[0].Animate = true; nav.Push(pages[1]);
        Assert.IsTrue(pages[0].IsOpen); Assert.IsFalse(group.blocksRaycasts);
        Finish(); Assert.IsTrue(group.blocksRaycasts); Assert.IsFalse(pages[0].IsOpen);
    }
    [Test]
    public void Test20_QueueDropsNewestAndRetainsOrder()
    {
        var warnings = new List<string>(); var queue = new UINavigationQueue(warnings.Add) { MaxQueue = 2 };
        Assert.IsTrue(queue.Enqueue(new NavIntent { kind = NavIntentKind.Push, target = pages[1] }));
        Assert.IsTrue(queue.Enqueue(new NavIntent { kind = NavIntentKind.Push, target = pages[2] }));
        Assert.IsFalse(queue.Enqueue(new NavIntent { kind = NavIntentKind.Back })); Assert.AreEqual(1, warnings.Count);
        queue.TryDequeue(out var a); queue.TryDequeue(out var b);
        Assert.AreSame(pages[1], a.target); Assert.AreSame(pages[2], b.target);
    }
    [Test]
    public void ReopeningDuringExitKillsOldCompletionAndKeepsPosition()
    {
        var panel = pages[0]; panel.Animate = true; panel.Close();
        var old = panel.TransitionHandle.Value; MotionSystem.Instance.Advance(.25, .25);
        panel.Open(); old.Skip(); Assert.IsTrue(panel.IsOpen); Assert.IsTrue(panel.IsTransitioning);
        Finish(); Assert.IsTrue(panel.IsOpen); Assert.IsFalse(panel.IsTransitioning);
    }
}
