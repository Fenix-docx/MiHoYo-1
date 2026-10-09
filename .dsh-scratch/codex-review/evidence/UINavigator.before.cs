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

        public bool Push(UIBase target)
        {
            if (target == null)
            {
                Debug.LogWarning("[UINavigator] Push 目标为空。", this);
                return false;
            }

            var current = Current;
            if (current == target) return true;

            if (current != null && restrictToDirectChildren)
            {
                var currentNode = FindNode(current);
                if (currentNode == null || !currentNode.HasChildren(target))
                {
                    Debug.LogWarning($"[UINavigator] {target.name} 不是 {current.name} 的直接子节点。", this);
                    return false;
                }
            }

            if (current != null)
            {
                var currentNode = FindNode(current);
                if (currentNode != null && currentNode.CloseParentOnPush) SetOpen(current, false);
            }

            navStack.Push(target);
            SetOpen(target, true);
            Navigated?.Invoke(current, target);
            return true;
        }

        /// <summary>边界入口：数据驱动 / 跨场景时按 PanelId 解析成实例，再走同一条路径。</summary>
        public bool Push(string panelId)
        {
            EnsureCache();

            if (string.IsNullOrWhiteSpace(panelId) ||
                !pageById.TryGetValue(panelId, out var page) || page == null)
            {
                Debug.LogWarning($"[UINavigator] 导航树里没有 PanelId = {panelId} 的节点。", this);
                return false;
            }

            return Push(page);
        }

        public bool Back()
        {
            if (navStack.Count <= 1)
            {
                Debug.LogWarning("[UINavigator] 导航栈已在根节点，无法回退。", this);
                return false;
            }

            var from = Current;
            navStack.Pop();
            SetOpen(from, false);

            var to = Current;
            SetOpen(to, true);
            Navigated?.Invoke(from, to);
            return true;
        }

        public bool Replace(UIBase target)
        {
            if (target == null) return false;

            var current = Current;
            if (current != null && restrictToDirectChildren)
            {
                var currentNode = FindNode(current);
                if (currentNode == null || !currentNode.HasChildren(target))
                {
                    Debug.LogWarning($"[UINavigator] {target.name} 不是 {current.name} 的直接子节点。", this);
                    return false;
                }
            }

            if (navStack.Count > 0) SetOpen(navStack.Pop(), false);

            navStack.Push(target);
            SetOpen(target, true);
            Navigated?.Invoke(current, target);
            return true;
        }

        public void ResetToRoot()
        {
            if (rootPage == null)
            {
                Debug.LogWarning("[UINavigator] 根面板未配置，无法重置到根。", this);
                return;
            }

            var from = Current;
            while (navStack.Count > 0) SetOpen(navStack.Pop(), false);

            navStack.Push(rootPage);
            SetOpen(rootPage, true);
            Navigated?.Invoke(from, rootPage);
        }

        public void CloseAll()
        {
            while (navStack.Count > 0) SetOpen(navStack.Pop(), false);

            foreach (var node in nodes)
            {
                SetOpen(node != null ? node.Page : null, false);
            }
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