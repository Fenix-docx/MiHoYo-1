using System;
using UnityEngine;

namespace UIPackage.Motion
{
    [CreateAssetMenu(menuName = "UI/Motion Path", fileName = "MotionPath")]
    public sealed class MotionPathAsset : ScriptableObject
    {
        // p 空间以父高的一半为单位；禁止把 Scene 世界坐标直接写入这里。
        [SerializeField] private Vector2[] points = { new Vector2(-.5f, 0), new Vector2(0, .4f), new Vector2(.5f, 0) };
        public int PointCount => points == null ? 0 : points.Length;
        public Vector2 GetPoint(int index) => points[index];
        public void SetPoint(int index, Vector2 value) { points[index] = value; }
        public void AddPoint(Vector2 value)
        {
            int count = PointCount; Array.Resize(ref points, count + 1); points[count] = value;
        }
        public void RemoveLastPoint()
        {
            if (PointCount > 2) Array.Resize(ref points, points.Length - 1);
        }
        /// <summary>每次构建都重新烘焙，Inspector/Undo/运行期改点不会命中陈旧缓存。</summary>
        public PathChain BuildChain() => Path.Through(points);
    }
}
