using UnityEngine;

namespace UIPackage.Motion
{
    /// <summary>唯一的位置写入口；补偿 anchor/pivot，支持拉伸锚点。</summary>
    public static class RectBinder  
    {
        public static Vector2 LocalToAnchored(RectTransform target, Vector2 local)
        {
            var parent = (RectTransform)target.parent;
            var a = target.anchorMin + Vector2.Scale(target.anchorMax - target.anchorMin, target.pivot);
            return local - (parent.rect.min + Vector2.Scale(parent.rect.size, a));
        }
        public static Vector2 AnchoredToLocal(RectTransform target, Vector2 anchored) =>
            anchored - LocalToAnchored(target, Vector2.zero);
        internal static void Apply(RectTransform target, MotionSample sample) { target.anchoredPosition = sample.position; }
    }
}
