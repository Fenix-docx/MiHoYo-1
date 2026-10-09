using UnityEngine;

namespace UIPackage.Motion
{
    public struct MotionSample
    {
        public Vector2 position, velocity;
        public float rotation, scale, weight;
    }
    public enum AnchorEdge { Center, Left, Right, Top, Bottom, Slot }
    public enum MotionChannel { AnchoredPosition }
    public struct PathSpace { public RectTransform rect; }
    public struct BindSpace { public RectTransform target; }
    /// <summary>
    /// 锚点引用结构体，描述一个相对于目标 RectTransform 某条边的锚点位置
    /// </summary>
    public struct AnchorRef
    {

        public RectTransform target;
  
        public AnchorEdge edge;

        public Vector2 offset;
  
        public RectTransform space;

        public int slotIndex;
        /// <summary>
        /// 构造锚点引用
        /// </summary>
        /// <param name="target">锚点所依附的目标 RectTransform</param>
        /// <param name="edge">锚点对齐的边，默认 Center</param>
        /// <param name="offset">额外偏移量，默认零向量</param>
        /// <param name="space">坐标空间，默认 null（使用 target 自身空间）</param>
        /// <param name="slotIndex">插槽索引，默认 0</param>
        public AnchorRef(RectTransform target, AnchorEdge edge = AnchorEdge.Center,
            Vector2 offset = default, RectTransform space = null, int slotIndex = 0)
        {
            this.target = target; this.edge = edge; this.offset = offset;
            this.space = space; this.slotIndex = slotIndex;
        }
    }
}