using System.Collections.Generic;
using UnityEngine;

namespace UIPackage.Editor
{
    /// <summary>只读校验。不做任何修复 —— 修复由人在窗口 / Inspector 里拖拽完成。</summary>
    public static class UINavigationValidator
    {
        public static List<string> Validate(UINavigator navigator)
        {
            var problems = new List<string>();

            if (navigator == null)
            {
                problems.Add("没有选中 UINavigator。");
                return problems;
            }

            if (navigator.Nodes.Count == 0)
            {
                problems.Add("导航树是空的：先在窗口里把面板拖成节点。");
                return problems;
            }

            if (navigator.RootPage == null)
            {
                problems.Add("Root Page 未配置：把根面板拖到 Root Page 字段。");
            }

            // 1) 节点自身
            var pageToNode = new Dictionary<UIBase, UINavNode>();
            var idOwners = new Dictionary<string, UIBase>();

            foreach (var node in navigator.Nodes)
            {
                if (node == null)
                {
                    problems.Add("节点表里有 null 节点。");
                    continue;
                }

                var page = node.Page;
                if (page == null)
                {
                    problems.Add("有一个节点未绑定面板（面板可能已被删除）。");
                    continue;
                }

                if (pageToNode.ContainsKey(page))
                {
                    problems.Add($"{page.name} 被登记了两次。");
                }
                else
                {
                    pageToNode.Add(page, node);
                }

                var id = page.PanelId;
                if (string.IsNullOrWhiteSpace(id))
                {
                    problems.Add($"{page.name} 未设置 PanelId（UIManager 注册需要它）。");
                }
                else if (idOwners.TryGetValue(id, out var owner))
                {
                    problems.Add($"PanelId 重复：{id}（{owner.name} 与 {page.name}）。");
                }
                else
                {
                    idOwners.Add(id, page);
                }
            }

            if (navigator.RootPage != null && !pageToNode.ContainsKey(navigator.RootPage))
            {
                problems.Add("Root Page 不在节点表里。");
            }

            // 2) 边
            foreach (var pair in pageToNode)
            {
                var seen = new HashSet<UIBase>();
                foreach (var child in pair.Value.Children)
                {
                    if (child == null)
                    {
                        problems.Add($"{pair.Key.name} 的子节点里有空引用。");
                        continue;
                    }

                    if (child == pair.Key)
                    {
                        problems.Add($"{pair.Key.name} 把自己作为子节点。");
                        continue;
                    }

                    if (!seen.Add(child)) problems.Add($"{pair.Key.name} 的子节点 {child.name} 重复。");

                    if (!pageToNode.ContainsKey(child))
                    {
                        problems.Add($"{pair.Key.name} 的子节点 {child.name} 不是节点表里的节点。");
                    }
                }
            }

            // 3) 成环
            if (navigator.RootPage != null && HasCycle(navigator, navigator.RootPage, new HashSet<UIBase>(), new HashSet<UIBase>()))
            {
                problems.Add("导航树存在环。");
            }

            // 4) 从根不可达
            var reachable = new List<UIBase>();
            navigator.CollectReachablePages(reachable);
            var reachableSet = new HashSet<UIBase>(reachable);

            foreach (var page in pageToNode.Keys)
            {
                if (!reachableSet.Contains(page)) problems.Add($"{page.name} 登记了但从根不可达。");
            }

            // 5) 游离面板（在扫描范围里但没登记）
            foreach (var page in navigator.CollectPagesInScope())
            {
                if (!pageToNode.ContainsKey(page)) problems.Add($"{page.name} 在扫描范围里但没登记进导航树。");
            }

            return problems;
        }

        private static bool HasCycle(UINavigator navigator, UIBase page,
                                     HashSet<UIBase> visiting, HashSet<UIBase> visited)
        {
            if (page == null || visited.Contains(page)) return false;
            if (!visiting.Add(page)) return true;

            var node = navigator.FindNode(page);
            if (node != null)
            {
                foreach (var child in node.Children)
                {
                    if (HasCycle(navigator, child, visiting, visited)) return true;
                }
            }

            visiting.Remove(page);
            visited.Add(page);
            return false;
        }
    }
}