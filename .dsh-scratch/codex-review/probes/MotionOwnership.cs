#error UIMOTION_COMPILE_PROBE_MotionOwnership
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace UIPackage.Motion
{
    public enum PositionOwner { Motion, Animator, DOTween, ScrollRect }
    /// <summary>静态声明属性归属；外部补间须先查询声明，不允许同属性动态抢占。</summary>
    [DisallowMultipleComponent]
    public sealed class MotionOwnership : MonoBehaviour
    {
        [SerializeField] private PositionOwner owner = PositionOwner.Motion;
        public PositionOwner Owner => owner;
        private static readonly HashSet<RectTransform> active = new HashSet<RectTransform>();
        public static bool Declare(RectTransform target, PositionOwner writer)
        {
            if (target == null) return false;
            var declaration = target.GetComponent<MotionOwnership>();
            if (declaration != null && declaration.owner != writer)
            {
                Debug.LogError("[UIMotion] anchoredPosition 所有权冲突，Animator/DOTween/ScrollRect 不可与 Motion 同时写入。", target);
                return false;
            }
            if (declaration == null)
            {
                declaration = target.gameObject.AddComponent<MotionOwnership>();
                declaration.owner = writer;
            }
            return true;
        }
        public static bool IsOwnedBy(RectTransform target, PositionOwner writer)
        {
            var declaration = target != null ? target.GetComponent<MotionOwnership>() : null;
            return declaration != null && declaration.owner == writer;
        }
        internal static bool Acquire(RectTransform target)
        {
            if (!Declare(target, PositionOwner.Motion)) return false;
            var animator = target.GetComponent<Animator>();
            var scroll = target.GetComponentInParent<ScrollRect>();
            // P4 才做逐曲线 Validator；P0 对本对象上启用的 Animator 保守报错。
            if ((animator != null && animator.enabled) || (scroll != null && scroll.content == target))
            {
                Debug.LogError("[UIMotion] Animator 或 ScrollRect.content 可能直接写入此位置，拒绝启动。", target);
                return false;
            }
            if (active.Add(target)) return true;
            Debug.LogError("[UIMotion] 同一 anchoredPosition 已有活动轨道；请显式 Kill 后接管。", target);
            return false;
        }
        internal static void Release(RectTransform target) { active.Remove(target); }
        private void OnDestroy() { active.Remove(transform as RectTransform); }
    }
}
