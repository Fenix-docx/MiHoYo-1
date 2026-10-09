using System;
using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>
    /// 锚点解析器；
    /// </summary>
    public static class AnchorResolver 
    {
        /// <summary>每次采样读取 rect；不缓存布局结果。返回指定父空间的 local 坐标。</summary>
        public static Vector2 Resolve(AnchorRef anchor, RectTransform parent)
        {
            if (parent == null || (anchor.space != null && anchor.space != parent))
                throw new ArgumentException("P0 锚点必须使用绑定对象的父空间。");
            var target = anchor.target != null ? anchor.target : parent;
            var edge = anchor.edge;
            if (edge == AnchorEdge.Slot)
            {
                if (anchor.slotIndex < 0 || anchor.slotIndex >= target.childCount)
                    throw new ArgumentOutOfRangeException(nameof(anchor.slotIndex));
                target = target.GetChild(anchor.slotIndex) as RectTransform;
                edge = AnchorEdge.Center;
            }
            // TODO：P2 才实现跨父级 Reparent 与共同画布空间。
            if (target == null || (target != parent && target.parent != parent))
                throw new ArgumentException("P0 不支持跨父级锚点。");
            var point = MotionMath.AnchorPoint(target.rect.min, target.rect.size, edge, anchor.offset);
            return target == parent ? point : (Vector2)parent.InverseTransformPoint(target.TransformPoint(point));
        }
        public static Vector2 ToP(Vector2 local, RectTransform space) =>
            (local - space.rect.center) * (2 / Math.Max(.00001f, Math.Abs(space.rect.height)));
        public static Vector2 FromP(Vector2 p, RectTransform space) =>
            space.rect.center + p * (Math.Abs(space.rect.height) * .5f);
    }
}
