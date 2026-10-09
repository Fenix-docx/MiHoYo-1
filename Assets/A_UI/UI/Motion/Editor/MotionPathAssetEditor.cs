using UnityEditor;
using UnityEngine;

namespace UIPackage.Motion.Editor
{
    [CustomEditor(typeof(MotionPathAsset))]
    public sealed class MotionPathAssetEditor : UnityEditor.Editor
    {
        // 预览父级属于编辑器会话，不保存场景对象引用到项目资产。
        private RectTransform previewSpace;
        private void OnEnable() { SceneView.duringSceneGui += DrawScene; }
        private void OnDisable() { SceneView.duringSceneGui -= DrawScene; }
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            previewSpace = (RectTransform)EditorGUILayout.ObjectField("预览父 Rect", previewSpace, typeof(RectTransform), true);
            EditorGUILayout.HelpBox("点存为 p 空间（父高一半为单位）。指定预览父 Rect 后在 Scene 拖点；未指定时 1 p = 1 世界单位。Shift + 左键在平面上添加点。", MessageType.Info);
            var asset = (MotionPathAsset)target;
            if (GUILayout.Button("添加末点"))
            {
                Undo.RecordObject(asset, "添加路径点");
                asset.AddPoint(asset.PointCount > 0 ? asset.GetPoint(asset.PointCount - 1) + new Vector2(.2f, 0) : Vector2.zero);
                EditorUtility.SetDirty(asset);
            }
            using (new EditorGUI.DisabledScope(asset.PointCount <= 2))
                if (GUILayout.Button("删除末点"))
                {
                    Undo.RecordObject(asset, "删除路径点"); asset.RemoveLastPoint(); EditorUtility.SetDirty(asset);
                }
            if (GUI.changed) SceneView.RepaintAll();
        }
        private void DrawScene(SceneView view)
        {
            var asset = target as MotionPathAsset;
            if (asset == null) return;
            if (asset.PointCount >= 2) MotionGizmoDrawer.Draw(asset.BuildChain(), previewSpace);
            for (int i = 0; i < asset.PointCount; i++)
            {
                var world = MotionGizmoDrawer.ToWorld(asset.GetPoint(i), previewSpace);
                Handles.Label(world, "p" + i);
                EditorGUI.BeginChangeCheck();
                var moved = Handles.PositionHandle(world, previewSpace != null ? previewSpace.rotation : Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(asset, "拖动路径点");
                    asset.SetPoint(i, MotionGizmoDrawer.ToP(moved, previewSpace));
                    EditorUtility.SetDirty(asset); Repaint(); view.Repaint();
                }
            }
            var evt = Event.current;
            if (evt.shift && evt.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (evt.shift && evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                var origin = MotionGizmoDrawer.ToWorld(Vector2.zero, previewSpace);
                var plane = new Plane(previewSpace != null ? previewSpace.forward : Vector3.forward, origin);
                var ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
                if (plane.Raycast(ray, out float distance))
                {
                    Undo.RecordObject(asset, "画路径点");
                    asset.AddPoint(MotionGizmoDrawer.ToP(ray.GetPoint(distance), previewSpace));
                    EditorUtility.SetDirty(asset); evt.Use(); Repaint(); view.Repaint();
                }
            }
        }
    }
}
