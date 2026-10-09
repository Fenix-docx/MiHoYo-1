using System;
using System.Collections.Generic;
using UnityEngine;

namespace UIPackage
{
    /// <summary>
    /// 导航器：既是运行时导航行为（栈）的唯一实现，也是导航树数据的持有者。
    /// 树数据放在这里，是因为它只有一个运行时消费者 —— 就是它自己。
    /// UIBase 只提供身份与显隐，不参与拓扑。
    /// </summary>
    [DisallowMultipleComponent]
    public class UINavigator : MonoBehaviour
    {
        public static UINavigator Instance { get; private set; }

        [Header("导航树（在 UI Navigation 窗口里编辑）")]
        [SerializeField] private UIBase rootPage;
        [SerializeField] private List<UINavNode> nodes = new List<UINavNode>();

        [Header("扫描范围（用于校验游离面板；留空 = 本对象所在子树）")]
        [SerializeField] private Transform pageScope;

        [Header("行为")]
        [SerializeField] private bool openRootOnStart = true;
        [SerializeField] private bool closeAllOnAwake = true;
        [SerializeField] private bool restrictToDirectChildren = true;

        /// <summary>(from, to)，两者都是面板实例；from 为 null 表示此前没有节点。</summary>
        public event Action<UIBase, UIBase> Navigated;

        [SerializeField] private int maxQueue = 8;
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

        private readonly Stack<UIBase> navStack = new Stack<UIBase>();
        private Dictionary<UIBase, UINavNode> nodeByPage;   // 派生缓存
        private Dictionary<string, UIBase> pageById;       // 派生缓存

        public UIBase RootPage => rootPage;
        public IReadOnlyList<UINavNode> Nodes => nodes;
        public Transform PageScope => pageScope != null ? pageScope : transform;

        public UIBase Current
        {
            get
            {
                if (navStack.Count == 0) return null;

                var top = navStack.Peek();
                return top != null ? top : null;    // Unity 假 null 归一成真 null
            }
        }

        public string CurrentId => Current != null ? Current.PanelId : string.Empty;

        // ---------------- 生命周期 ----------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("[UINavigator] 场景中存在多个 UINavigator，已禁用后加入的实例。", this);
                Destroy(this);
                return;
            }

            Instance = this;
            RebuildCache();

            if (closeAllOnAwake) CloseAll();
        }

        private void Start()
        {
            if (openRootOnStart) ResetToRoot();
        }

        private void OnDestroy()
        {
            RestoreInput();
            queue?.Clear();
            if (Instance == this) Instance = null;
        }

        // ---------------- 派生缓存 ----------------

        /// <summary>重建两张派生索引。树被改动后由编辑 API 自动失效，外部改完数据也可手动调。</summary>
        public void RebuildCache()
        {
            nodeByPage = new Dictionary<UIBase, UINavNode>();
            pageById = new Dictionary<string, UIBase>();

            foreach (var node in nodes)
            {
                var page = node != null ? node.Page : null;
                if (page == null) continue;

                if (!nodeByPage.ContainsKey(page)) nodeByPage.Add(page, node);

                var id = page.PanelId;
                if (!string.IsNullOrWhiteSpace(id) && !pageById.ContainsKey(id)) pageById.Add(id, page);
            }
        }

        private void EnsureCache()
        {
            if (nodeByPage == null) RebuildCache();
        }

        private void Invalidate()
        {
            nodeByPage = null;
            pageById = null;
        }

        public UINavNode FindNode(UIBase page)
        {
            if (page == null) return null;

            EnsureCache();
            return nodeByPage.TryGetValue(page, out var node) ? node : null;
        }

        // ---------------- 运行时导航 ----------------

        /// <summary>返回值表示已接受；排队期间 Current 不变，落定请等待 NavigationIdle。</summary>
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
            if (target == null)
            {
                Debug.LogWarning("[UINavigator] 目标为空或已从导航树移除，丢弃意图。", this);
                return false;
            }
            if (current != null && current != target && restrictToDirectChildren)
            {
                var node = FindNode(current);
                if (FindNode(target) == null || node == null || !node.HasChildren(target))
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
            // 游离面板允许入栈；已登记目标仍保留转移期间被移除后的提交保护。
            bool targetWasRegistered = FindNode(target) != null;
            transitioning = true;
            switch (intent.kind)
            {
                case NavIntentKind.Push:
                    var node = FindNode(from);
                    if (node != null && node.CloseParentOnPush) Touch(from, false, intent.instant, now);
                    Touch(target, true, intent.instant, now);
                    commit = () => { if (target != null && (!targetWasRegistered || FindNode(target) != null)) navStack.Push(target); Navigated?.Invoke(from, Current); };
                    break;
                case NavIntentKind.Back:
                case NavIntentKind.Replace:
                    Touch(from, false, intent.instant, now);
                    Touch(target, true, intent.instant, now);
                    commit = () =>
                    {
                        if (navStack.Count > 0 && navStack.Peek() == from) navStack.Pop();
                        if (intent.kind == NavIntentKind.Replace && target != null && (!targetWasRegistered || FindNode(target) != null)) navStack.Push(target);
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
            try
            {
                if (instant) page.SetOpenInstant(open); else SetOpen(page, open);
            }
            catch (Exception ex)
            {
                // 面板回调异常也要结束转移，避免阻塞后续队列和输入恢复。
                Debug.LogException(ex, page);
                page.ForceCompleteTransition();
            }
            wait.deadline = now + page.ExpectedTransitionDuration * 3 + .5;
        }
        private void RestoreInput()
        {
            foreach (var wait in waiting)
                if (wait.group != null) wait.group.blocksRaycasts = wait.blocksRaycasts;
            waiting.Clear();
        }

        // ---------------- 树的编辑 API（窗口 / Inspector 调用；运行时也可用） ----------------

        /// <summary>登记一个节点。已存在则直接返回。第一个登记的节点会成为根。</summary>
        public UINavNode AddNode(UIBase page)
        {
            if (page == null) return null;

            var existing = FindNode(page);
            if (existing != null) return existing;

            var node = new UINavNode { Page = page };
            nodes.Add(node);

            if (rootPage == null) rootPage = page;

            Invalidate();
            return node;
        }

        /// <summary>删除节点：同时摘掉所有指向它的边，以及它在导航栈里的残留。</summary>
        public bool RemoveNode(UIBase page)
        {
            var node = FindNode(page);
            if (node == null) return false;

            nodes.Remove(node);

            foreach (var other in nodes)
            {
                other?.RemoveChildReferences(page);
            }

            if (rootPage == page) rootPage = null;

            // 被删的节点可能还在栈里 —— 不摘掉的话 Current 会指向一个已删除的节点
            if (StackContains(page))
            {
                var kept = new List<UIBase>();
                foreach (var p in navStack)          // Stack 枚举顺序：栈顶 → 栈底
                {
                    if (p != page) kept.Add(p);
                }

                navStack.Clear();
                for (int i = kept.Count - 1; i >= 0; i--) navStack.Push(kept[i]);
            }

            Invalidate();
            return true;
        }

        /// <summary>接一条 parent → child 的边。父/子未登记会自动登记。</summary>
        public bool AddChild(UIBase parent, UIBase child)
        {
            if (parent == null || child == null || parent == child) return false;

            var parentNode = AddNode(parent);
            var childNode = AddNode(child);
            if (parentNode == null || childNode == null) return false;
            if (parentNode.HasChildren(child)) return false;
            if (WouldCreateCycle(parent, child)) return false;

            parentNode.AddChildRaw(child);
            Invalidate();
            return true;
        }

        public bool RemoveChild(UIBase parent, UIBase child)
        {
            var node = FindNode(parent);
            if (node == null || !node.RemoveChildRaw(child)) return false;

            Invalidate();
            return true;
        }

        /// <summary>接上 parent → child 会不会成环：child 沿现有边能否回到 parent。</summary>
        public bool WouldCreateCycle(UIBase parent, UIBase child)
        {
            return Reaches(child, parent, new HashSet<UIBase>());
        }

        public void SetRoot(UIBase page)
        {
            if (page == null) { rootPage = null; return; }

            AddNode(page);
            rootPage = page;
        }

        public void ClearTree()
        {
            nodes.Clear();
            rootPage = null;
            navStack.Clear();
            Invalidate();
        }

        // ---------------- 查询辅助（编辑器用） ----------------

        /// <summary>扫描范围里的全部面板（含没登记进树的），用于"游离面板"提示。</summary>
        public List<UIBase> CollectPagesInScope()
        {
            var result = new List<UIBase>();
            foreach (var page in PageScope.GetComponentsInChildren<UIBase>(true))
            {
                if (page != null) result.Add(page);
            }

            return result;
        }

        /// <summary>从根沿边可达的面板 —— 真正的导航树成员。</summary>
        public void CollectReachablePages(List<UIBase> results)
        {
            results.Clear();
            CollectReachable(rootPage, results, new HashSet<UIBase>());
        }

        private void CollectReachable(UIBase page, List<UIBase> results, HashSet<UIBase> visited)
        {
            if (page == null || !visited.Add(page)) return;

            results.Add(page);

            var node = FindNode(page);
            if (node == null) return;

            foreach (var child in node.Children)
            {
                CollectReachable(child, results, visited);
            }
        }

        // ---------------- 内部 ----------------

        private bool Reaches(UIBase from, UIBase target, HashSet<UIBase> visited)
        {
            if (from == null || target == null || !visited.Add(from)) return false;
            if (from == target) return true;

            var node = FindNode(from);
            if (node == null) return false;

            foreach (var child in node.Children)
            {
                if (Reaches(child, target, visited)) return true;
            }

            return false;
        }

        private bool StackContains(UIBase page)
        {
            foreach (var p in navStack)
            {
                if (p == page) return true;
            }

            return false;
        }

        /// <summary>
        /// 唯一的显隐出口。必须用显式 != null：?. 走 CLR null 检查，
        /// 绕过 UnityEngine.Object 的 == 重载，已销毁对象会被当成非 null 调用并抛 MissingReferenceException。
        /// </summary>
        private static void SetOpen(UIBase page, bool open)
        {
            if (page == null) return;

            if (open) page.Open();
            else page.Close();
        }
    }
}
