using UnityEditor;
using UnityEngine;

namespace UIPackage.Editor
{
    [CustomEditor(typeof(UINavigator))]
    public class UINavigatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();     // rootPage / nodes 都能直接拖拽编辑（见下方注意事项）

            var navigator = (UINavigator)target;
            EditorGUILayout.Space();

            if (GUILayout.Button("打开 UI Navigation 窗口")) UINavigationEditorWindow.Open(navigator);

            if (GUILayout.Button("重建导航缓存")) navigator.RebuildCache();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("校验（只读）", EditorStyles.boldLabel);

            var problems = UINavigationValidator.Validate(navigator);
            if (problems.Count == 0)
            {
                EditorGUILayout.HelpBox("导航数据有效。", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox($"发现 {problems.Count} 个问题：", MessageType.Warning);
            foreach (var problem in problems)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }
        }
    }
}