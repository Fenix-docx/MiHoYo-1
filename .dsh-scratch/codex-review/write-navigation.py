from pathlib import Path
p=Path('Assets/A_UI/UI/Package/UIbase.cs')
s=p.read_text(encoding='utf-8-sig').replace('using UnityEngine;','using System;\nusing UIPackage.Motion;\nusing UnityEngine;')
a=s.index('        public void Open()'); b=s.index('        //负责',a)
s=s[:a]+'''        public bool IsTransitioning { get; private set; }
        public event Action TransitionCompleted;
        public MotionHandle? TransitionHandle { get; private set; }
        public float ExpectedTransitionDuration => TransitionHandle?.ExpectedDuration ?? 0;
        protected bool TransitionInterrupted { get; private set; }
        private bool opening;
        private int transitionVersion;

        /// <summary>默认无配方时返回 null，保留同步生命周期。</summary>
        protected virtual MotionHandle? PlayOpen()
        {
            var recipe = GetComponent<UIPanelMotion>();
            return recipe != null ? recipe.Play(true, TransitionInterrupted) : (MotionHandle?)null;
        }
        protected virtual MotionHandle? PlayClose()
        {
            var recipe = GetComponent<UIPanelMotion>();
            return recipe != null ? recipe.Play(false, TransitionInterrupted) : (MotionHandle?)null;
        }
        public void Open() { BeginTransition(true, false); }
        public void Close() { BeginTransition(false, false); }

        /// <summary>instant 不创建轨道，狂点返回的后续意图不会进入过渡态。</summary>
        internal void SetOpenInstant(bool open) { BeginTransition(open, true); }
        private void BeginTransition(bool open, bool instant)
        {
            if (IsTransitioning && opening == open) { if (instant) ForceCompleteTransition(); return; }
            if (!IsTransitioning && IsOpen == open) return;
            TransitionInterrupted = IsTransitioning;
            ++transitionVersion;
            TransitionHandle?.Kill(false);
            TransitionHandle = null;
            opening = open;
            if (open) gameObject.SetActive(true);
            IsTransitioning = !instant;
            int version = transitionVersion;
            if (instant) { FinishTransition(version); return; }
            try { TransitionHandle = open ? PlayOpen() : PlayClose(); }
            catch (Exception ex) { Debug.LogException(ex, this); FinishTransition(version); return; }
            var handle = TransitionHandle;
            if (!handle.HasValue || (!handle.Value.IsValid && !handle.Value.IsDone)) FinishTransition(version);
            else handle.Value.OnComplete(() => FinishTransition(version));
        }
        private void FinishTransition(int version)
        {
            if (version != transitionVersion) return;
            ++transitionVersion;
            IsTransitioning = false;
            TransitionHandle = null;
            if (!opening) gameObject.SetActive(false);
            try
            {
                if (opening) OnOpened(); else OnClosed();
            }
            finally { TransitionCompleted?.Invoke(); }
        }
        /// <summary>句柄失效或看门狗超时时也必须释放导航等待。</summary>
        public void ForceCompleteTransition()
        {
            if (!IsTransitioning) return;
            int version = transitionVersion;
            TransitionHandle?.Skip();
            if (IsTransitioning && version == transitionVersion) FinishTransition(version);
        }
        protected virtual void OnDestroy()
        {
            ++transitionVersion;
            TransitionHandle?.Kill(false);
            TransitionHandle = null;
            IsTransitioning = false;
        }

'''+s[b:];p.write_text(s,encoding='utf-8')
p=Path('Assets/A_UI/UI/Package/Navigation/UINavigationQueue.cs')
p.write_text('''using System;
using System.Collections.Generic;

namespace UIPackage
{
    public enum NavIntentKind { Push, Back, Replace, ToRoot, CloseAll }
    public struct NavIntent
    {
        public NavIntentKind kind;
        public UIBase target;
        public string targetId;
        public int enqueueFrame;
        public bool instant;
    }
    /// <summary>只保存意图；目标解析、拓扑校验和视觉完成属于导航器。</summary>
    public sealed class UINavigationQueue
    {
        private readonly List<NavIntent> items = new List<NavIntent>();
        private readonly Action<string> warn;
        private int backBurst;
        public int MaxQueue { get; set; } = 8;
        public int FastForwardThreshold { get; set; } = 3;
        public int Count => items.Count;
        public UINavigationQueue(Action<string> warning = null) { warn = warning; }
        public bool Enqueue(NavIntent intent)
        {
            if (intent.kind == NavIntentKind.ToRoot || intent.kind == NavIntentKind.CloseAll) Clear();
            if (intent.kind == NavIntentKind.Push && items.Count > 0)
            {
                var last = items[items.Count - 1];
                if (last.kind == NavIntentKind.Push && ReferenceEquals(last.target, intent.target) && last.targetId == intent.targetId)
                    return true;
            }
            if (items.Count >= Math.Max(1, MaxQueue))
            {
                warn?.Invoke("[UINavigator] 导航队列已满，丢弃最新意图。");
                return false;
            }
            if (intent.kind == NavIntentKind.Back) intent.instant = ++backBurst > FastForwardThreshold;
            else backBurst = 0;
            items.Add(intent);
            return true;
        }
        public bool TryDequeue(out NavIntent intent)
        {
            if (items.Count == 0) { intent = default; return false; }
            intent = items[0]; items.RemoveAt(0); return true;
        }
        public void Clear() { items.Clear(); backBurst = 0; }
        public void ResetBurst() { if (items.Count == 0) backBurst = 0; }
    }
}
''',encoding='utf-8')
p=Path('Assets/A_UI/UI/Package/Navigation/UINavigator.cs');s=p.read_text(encoding='utf-8-sig')
s=s.replace('        private readonly Stack<UIBase>', '''        [SerializeField] private int maxQueue = 8;
        [SerializeField] private int fastForwardThreshold = 3;
        [SerializeField] private bool blockEnteringRaycasts;
        public event Action NavigationIdle;
        public bool IsNavigating => transitioning || pumping || (queue != null && queue.Count > 0);
        private UINavigationQueue queue;
        private bool transitioning, pumping;
        private Action commit;
        private readonly List<TransitionWait> waiting = new List<TransitionWait>();
        private sealed class TransitionWait
        {
            public UIBase page;
            public CanvasGroup group;
            public bool blocksRaycasts;
            public double deadline;
        }

        private readonly Stack<UIBase>''')
s=s.replace('            if (Instance == this) Instance = null;', '''            RestoreInput();
            queue?.Clear();
            if (Instance == this) Instance = null;''')
a=s.index('        public bool Push(UIBase target)');b=s.index('        // ---------------- 树的编辑 API',a)
s=s[:a]+'''        /// <summary>返回值表示已接受；排队期间 Current 不变，落定请等待 NavigationIdle。</summary>
        public bool Push(UIBase target) => Submit(new NavIntent { kind = NavIntentKind.Push, target = target });
        /// <summary>只记录 PanelId，出队时用最新索引解析。</summary>
        public bool Push(string panelId) => Submit(new NavIntent { kind = NavIntentKind.Push, targetId = panelId });
        public bool Back() => Submit(new NavIntent { kind = NavIntentKind.Back });
        public bool Replace(UIBase target) => Submit(new NavIntent { kind = NavIntentKind.Replace, target = target });
        public void ResetToRoot() { Submit(new NavIntent { kind = NavIntentKind.ToRoot }); }
        public void CloseAll() { Submit(new NavIntent { kind = NavIntentKind.CloseAll }); }

        private bool Submit(NavIntent intent)
        {
            if (queue == null) queue = new UINavigationQueue(message => Debug.LogWarning(message, this));
            queue.MaxQueue = maxQueue;
            queue.FastForwardThreshold = fastForwardThreshold;
            intent.enqueueFrame = Time.frameCount;
            bool accepted = queue.Enqueue(intent);
            if (accepted) ProcessNavigation(Time.unscaledTimeAsDouble);
            return accepted;
        }
        private void Update() { ProcessNavigation(Time.unscaledTimeAsDouble); }

        /// <summary>统一的 unscaled 时间入口；测试可传入确定性时间，不依赖第二个 Editor 实例。</summary>
        public void ProcessNavigation(double unscaledNow)
        {
            if (pumping) return;
            bool hadWork = transitioning || (queue != null && queue.Count > 0);
            pumping = true;
            try
            {
                while (true)
                {
                    if (transitioning)
                    {
                        bool ready = true;
                        foreach (var wait in waiting)
                        {
                            var page = wait.page;
                            if (page == null || !page.IsTransitioning) continue;
                            var handle = page.TransitionHandle;
                            // 完成优先于失效，墙钟只兜底；暂停菜单不使用 scaledTime。
                            if (handle.HasValue && handle.Value.IsDone) page.ForceCompleteTransition();
                            else if (!handle.HasValue || !handle.Value.IsValid)
                            {
                                Debug.LogWarning("[UINavigator] 转移句柄失效，强制完成。", this);
                                page.ForceCompleteTransition();
                            }
                            else if (unscaledNow >= wait.deadline)
                            {
                                Debug.LogError("[UINavigator] 转移看门狗超时，强制完成并继续队列。", this);
                                page.ForceCompleteTransition();
                            }
                            if (page != null && page.IsTransitioning) ready = false;
                        }
                        if (!ready) break;
                        RestoreInput();
                        transitioning = false;
                        var finish = commit; commit = null;
                        finish?.Invoke();
                    }
                    if (queue == null || !queue.TryDequeue(out var intent)) break;
                    Execute(intent, unscaledNow);
                }
            }
            finally { pumping = false; }
            if (hadWork && !IsNavigating)
            {
                queue?.ResetBurst();
                NavigationIdle?.Invoke();
            }
        }
        private bool ValidateTarget(UIBase target, UIBase current)
        {
            if (target == null || FindNode(target) == null)
            {
                Debug.LogWarning("[UINavigator] 目标为空或已从导航树移除，丢弃意图。", this);
                return false;
            }
            if (current != null && current != target && restrictToDirectChildren)
            {
                var node = FindNode(current);
                if (node == null || !node.HasChildren(target))
                {
                    Debug.LogWarning($"[UINavigator] {target.name} 不是 {current.name} 的直接子节点。", this);
                    return false;
                }
            }
            return true;
        }
        private void Execute(NavIntent intent, double now)
        {
            var from = Current;
            var target = intent.target;
            if (intent.targetId != null)
            {
                EnsureCache();
                pageById.TryGetValue(intent.targetId, out target);
            }
            if (intent.kind == NavIntentKind.Push || intent.kind == NavIntentKind.Replace)
            {
                if (!ValidateTarget(target, from)) return;
                if (target == from) return;
            }
            if (intent.kind == NavIntentKind.Back)
            {
                if (navStack.Count <= 1)
                {
                    Debug.LogWarning("[UINavigator] 导航栈已在根节点，无法回退。", this);
                    return;
                }
                var pages = navStack.ToArray();
                target = pages[1];
                if (target == null || FindNode(target) == null)
                {
                    Debug.LogWarning("[UINavigator] 返回目标已移除，丢弃意图。", this);
                    return;
                }
            }
            if (intent.kind == NavIntentKind.ToRoot)
            {
                target = rootPage;
                if (target == null) { Debug.LogWarning("[UINavigator] 根面板未配置，无法重置到根。", this); return; }
            }
            transitioning = true;
            switch (intent.kind)
            {
                case NavIntentKind.Push:
                    var node = FindNode(from);
                    if (node != null && node.CloseParentOnPush) Touch(from, false, intent.instant, now);
                    Touch(target, true, intent.instant, now);
                    commit = () => { if (target != null) navStack.Push(target); Navigated?.Invoke(from, Current); };
                    break;
                case NavIntentKind.Back:
                case NavIntentKind.Replace:
                    Touch(from, false, intent.instant, now);
                    Touch(target, true, intent.instant, now);
                    commit = () =>
                    {
                        if (navStack.Count > 0 && navStack.Peek() == from) navStack.Pop();
                        if (intent.kind == NavIntentKind.Replace && target != null) navStack.Push(target);
                        Navigated?.Invoke(from, Current);
                    };
                    break;
                case NavIntentKind.ToRoot:
                case NavIntentKind.CloseAll:
                    foreach (var page in navStack) if (page != target || intent.kind == NavIntentKind.CloseAll) Touch(page, false, intent.instant, now);
                    if (intent.kind == NavIntentKind.CloseAll)
                        foreach (var item in nodes) Touch(item != null ? item.Page : null, false, intent.instant, now);
                    else Touch(target, true, intent.instant, now);
                    commit = () =>
                    {
                        navStack.Clear();
                        if (intent.kind == NavIntentKind.ToRoot && target != null) navStack.Push(target);
                        // CloseAll 保留原来不发 Navigated 的约定。
                        if (intent.kind != NavIntentKind.CloseAll) Navigated?.Invoke(from, Current);
                    };
                    break;
            }
        }
        private void Touch(UIBase page, bool open, bool instant, double now)
        {
            if (page == null) return;
            foreach (var entry in waiting) if (entry.page == page) return;
            var wait = new TransitionWait { page = page };
            if (!open || blockEnteringRaycasts)
            {
                wait.group = page.GetComponent<CanvasGroup>();
                if (wait.group == null) wait.group = page.gameObject.AddComponent<CanvasGroup>();
                wait.blocksRaycasts = wait.group.blocksRaycasts;
                wait.group.blocksRaycasts = false;
            }
            waiting.Add(wait);
            if (instant) page.SetOpenInstant(open); else SetOpen(page, open);
            wait.deadline = now + page.ExpectedTransitionDuration * 3 + .5;
        }
        private void RestoreInput()
        {
            foreach (var wait in waiting)
                if (wait.group != null) wait.group.blocksRaycasts = wait.blocksRaycasts;
            waiting.Clear();
        }

'''+s[b:]
p.write_text(s,encoding='utf-8')
