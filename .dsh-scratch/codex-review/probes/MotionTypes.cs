#error UIMOTION_COMPILE_PROBE_MotionTypes
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
    /// <summary>offset 使用目标短边比例；Slot 对应目标的直接 RectTransform 子节点。</summary>
    public struct AnchorRef
    {
        public RectTransform target;
        public AnchorEdge edge;
        public Vector2 offset;
        public RectTransform space;
        public int slotIndex;
        public AnchorRef(RectTransform target, AnchorEdge edge = AnchorEdge.Center,
            Vector2 offset = default, RectTransform space = null, int slotIndex = 0)
        {
            this.target = target; this.edge = edge; this.offset = offset;
            this.space = space; this.slotIndex = slotIndex;
        }
    }
}
