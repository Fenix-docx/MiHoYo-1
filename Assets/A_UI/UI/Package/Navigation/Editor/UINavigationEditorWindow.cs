using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UIPackage.Editor
{
    public class UINavigationEditorWindow : EditorWindow
    {
        private UINavigator navigator;
        private UIBase selectedPage;
        private Vector2 nodeScroll, detailScroll, problemScroll;
        private readonly HashSet<UIBase> expanded = new HashSet<UIBase>();

        [MenuItem("Tools/UI Navigation Editor")]
        public static void Open() => GetWindow<UINavigationEditorWindow>("UI Navigation");

        public static void Open(UINavigator selectedNavigator)
        {
            var window = GetWindow<UINavigationEditorWindow>("UI Navigation");
            window.navigator = selectedNavigator;
            window.selectedPage = selectedNavigator != null ? selectedNavigator.RootPage : null;
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (navigator == null)
            {
                EditorGUILayout.HelpBox("把带 UINavigator 的对象拖到上面的字段。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawTree();
            DrawDetails();
            EditorGUILayout.EndHorizontal();

            DrawProblems();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            navigator = (UINavigator)EditorGUILayout.ObjectField(navigator, typeof(UINavigator), true, GUILayout.MinWidth(200));

            if (GUILayout.Button("用当前选中", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                var selected = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponentInParent<UINavigator>()
                    : null;
                if (selected != null) { navigator = selected; selectedPage = navigator.RootPage; }
            }

            GUI.enabled = navigator != null;
            if (GUILayout.Button("刷新索引", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                if (navigator != null) navigator.RebuildCache();
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            if (navigator == null) return;

            EditorGUILayout.BeginHorizontal();
            var newRoot = (UIBase)EditorGUILayout.ObjectField("Root Page", navigator.RootPage, typeof(UIBase), true);
            if (newRoot != navigator.RootPage)
            {
                Undo.RecordObject(navigator, "Set Nav Root");
                navigator.SetRoot(newRoot);
                EditorUtility.SetDirty(navigator);
                selectedPage = newRoot;
            }
            EditorGUILayout.EndHorizontal();
        }

        // ---------------- 左：树 ----------------

        private void DrawTree()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            EditorGUILayout.LabelField("导航树（把面板拖到某一行 = 成为它的子节点）", EditorStyles.boldLabel);

            nodeScroll = EditorGUILayout.BeginScrollView(nodeScroll, GUI.skin.box, GUILayout.MinHeight(320));

            var rootPage = navigator.RootPage;
            if (rootPage == null)
            {
                EditorGUILayout.HelpBox("Root Page 未配置。", MessageType.Warning);
            }
            else
            {
                DrawNode(rootPage, 0, new HashSet<UIBase>());
            }

            DrawUnlinked();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndVertical();
        }

        private void DrawNode(UIBase page, int depth, HashSet<UIBase> path)
        {
            if (page == null) return;

            if (!path.Add(page))
            {
                EditorGUILayout.LabelField($"{new string(' ', depth * 16)}[环] {page.name}", EditorStyles.miniLabel);
                return;
            }

            var node = navigator.FindNode(page);
            var children = node?.Children;
            var hasChildren = children != null && children.Count > 0;

            EditorGUILayout.BeginHorizontal(GUILayout.Height(EditorGUIUtility.singleLineHeight));
            GUILayout.Space(depth * 16);

            if (hasChildren)
            {
                var foldRect = GUILayoutUtility.GetRect(14, EditorGUIUtility.singleLineHeight, GUILayout.Width(14));
                bool isExpanded = expanded.Contains(page);
                if (EditorGUI.Foldout(foldRect, isExpanded, GUIContent.none, true) != isExpanded)
                {
                    if (isExpanded) expanded.Remove(page); else expanded.Add(page);
                }
            }
            else
            {
                GUILayout.Space(14);
            }

            bool isSelected = selectedPage == page;
            if (GUILayout.Toggle(isSelected, BuildLabel(page), "Button") != isSelected)
            {
                selectedPage = page;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            HandleDrop(GUILayoutUtility.GetLastRect(), page);

            if (hasChildren && expanded.Contains(page))
            {
                foreach (var child in children)
                {
                    DrawNode(child, depth + 1, new HashSet<UIBase>(path));
                }
            }
        }

        private string BuildLabel(UIBase page)
        {
            var label = page.name;
            if (string.IsNullOrWhiteSpace(page.PanelId)) label += "  (无 PanelId)";
            var node = navigator.FindNode(page);
            if (node != null && node.Children.Count > 0) label += $"  ({node.Children.Count})";
            if (!page.IsOpen) label += "  · 隐藏";
            return label;
        }

        private void DrawUnlinked()
        {
            var reachable = new List<UIBase>();
            navigator.CollectReachablePages(reachable);
            var reachableSet = new HashSet<UIBase>(reachable);

            var unlinked = new List<UIBase>();
            foreach (var page in navigator.CollectPagesInScope())
            {
                if (!reachableSet.Contains(page)) unlinked.Add(page);
            }

            if (unlinked.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("游离面板（不在导航树里）", EditorStyles.boldLabel);

            foreach (var page in unlinked)
            {
                EditorGUILayout.BeginHorizontal();
                bool isSelected = selectedPage == page;
                if (GUILayout.Toggle(isSelected, $"[游离] {page.name}", "Button") != isSelected)
                {
                    selectedPage = page;
                    GUI.FocusControl(null);
                }

                if (GUILayout.Button("登记为节点", GUILayout.Width(90)))
                {
                    Undo.RecordObject(navigator, "Add Nav Node");
                    navigator.AddNode(page);
                    EditorUtility.SetDirty(navigator);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // ---------------- 拖放 ----------------

        private static UIBase ResolveDraggedPage()
        {
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is UIBase page) return page;
                if (obj is GameObject go)
                {
                    var found = go.GetComponentInChildren<UIBase>(true);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>行级拖放：拖到某行 = 成为该行的子节点。</summary>
        private void HandleDrop(Rect rowRect, UIBase parent)
        {
            var evt = Event.current;
            if (evt == null || !rowRect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            var dropped = ResolveDraggedPage();
            bool valid = dropped != null
                         && dropped != parent
                         && navigator.FindNode(parent)?.HasChildren(dropped) != true
                         && !navigator.WouldCreateCycle(parent, dropped);

            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (valid && evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(navigator, "Add Nav Child");
                if (navigator.AddChild(parent, dropped))
                {
                    expanded.Add(parent);
                    EditorUtility.SetDirty(navigator);
                    selectedPage = dropped;
                }
            }

            evt.Use();
        }

        private void DrawChildDropZone(UIBase parent)
        {
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            GUI.Box(rect, "拖 UIBase 到这里，成为它的子节点");

            var evt = Event.current;
            if (evt == null || !rect.Contains(evt.mousePosition)) return;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;

            var dropped = ResolveDraggedPage();
            bool valid = dropped != null
                         && dropped != parent
                         && navigator.FindNode(parent)?.HasChildren(dropped) != true
                         && !navigator.WouldCreateCycle(parent, dropped);

            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (valid && evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(navigator, "Add Nav Child");
                if (navigator.AddChild(parent, dropped)) EditorUtility.SetDirty(navigator);
            }

            evt.Use();
        }

        // ---------------- 右：详情 ----------------

        private void DrawDetails()
        {
            EditorGUILayout.BeginVertical();

            if (selectedPage == null)
            {
                EditorGUILayout.HelpBox("在左侧选一个节点。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var node = navigator.FindNode(selectedPage);
            if (node == null)
            {
                EditorGUILayout.HelpBox($"{selectedPage.name} 不是导航树里的节点。", MessageType.Warning);
                if (GUILayout.Button("登记为节点")) { Undo.RecordObject(navigator, "Add Nav Node"); navigator.AddNode(selectedPage); EditorUtility.SetDirty(navigator); }
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField($"选中：{node.DisplayName}", EditorStyles.boldLabel);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUI.skin.box, GUILayout.MinHeight(320));

            // PanelId 是派生值：只读展示，身份属于面板本身
            EditorGUILayout.LabelField("PanelId", string.IsNullOrWhiteSpace(node.ID) ? "(未设置)" : node.ID);
            if (GUILayout.Button("定位面板")) EditorGUIUtility.PingObject(selectedPage);

            EditorGUI.BeginChangeCheck();
            var closeParent = EditorGUILayout.Toggle("Close Parent On Push", node.CloseParentOnPush);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(navigator, "Edit Nav Node");
                node.CloseParentOnPush = closeParent;
                EditorUtility.SetDirty(navigator);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("子节点", EditorStyles.boldLabel);

            for (int i = node.Children.Count - 1; i >= 0; i--)      // 倒序：边遍历边删
            {
                var child = node.Children[i];

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(child, typeof(UIBase), true);

                if (GUILayout.Button("移除", GUILayout.Width(50)))
                {
                    Undo.RecordObject(navigator, "Remove Nav Child");
                    navigator.RemoveChild(selectedPage, child);
                    EditorUtility.SetDirty(navigator);
                }

                EditorGUILayout.EndHorizontal();
            }

            DrawChildDropZone(selectedPage);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ---------------- 问题 ----------------

        private void DrawProblems()
        {
            var problems = UINavigationValidator.Validate(navigator);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Problems", EditorStyles.boldLabel);
            problemScroll = EditorGUILayout.BeginScrollView(problemScroll, GUI.skin.box, GUILayout.Height(120));

            if (problems.Count == 0)
            {
                EditorGUILayout.HelpBox("导航数据有效。", MessageType.Info);
            }
            else
            {
                foreach (var problem in problems) EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }
    }
}