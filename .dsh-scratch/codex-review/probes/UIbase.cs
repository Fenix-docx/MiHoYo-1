#error UIMOTION_COMPILE_PROBE_UIbase
using System;
using UIPackage.Motion;
using UnityEngine;

namespace UIPackage
{
    /// <summary>
    /// 所有 UI 面板的基类。只负责自身生命周期与显隐，
    /// 不知道导航栈、也不依赖 UIManager（注册由 UIManager 侧的 uiPanels 统一完成）。
    /// </summary>
    public class UIBase : MonoBehaviour
    {
        /// <summary>
        /// 面板实例身份，同时是 UIManager 的注册键与导航树的节点 id 来源。
        /// 每个面板实例必须唯一且非空，且运行期不要改写（改了注册键就失效）。
        /// </summary>
        public string PanelId;
        public string Name => name;
        public bool  IsOpen => gameObject.activeSelf;   // 真值只有 Unity 一个源
        public bool IsTransitioning { get; private set; }
        public event Action TransitionCompleted;
        public MotionHandle? TransitionHandle { get; private set; }
        public float ExpectedTransitionDuration => TransitionHandle?.ExpectedDuration ?? 0;
        protected bool TransitionInterrupted { get; private set; }
        private bool opening;
        private int transitionVersion;

        /// <summary>默认无配方时返回 null，保留同步生命周期。</summary>
        protected virtual MotionHandle? PlayOpen()
        {
            var recipe = GetComponent<UIPanelMotion>();
            return recipe != null ? recipe.Play(true, TransitionInterrupted) : (MotionHandle?)null;
        }
        protected virtual MotionHandle? PlayClose()
        {
            var recipe = GetComponent<UIPanelMotion>();
            return recipe != null ? recipe.Play(false, TransitionInterrupted) : (MotionHandle?)null;
        }
        public void Open() { BeginTransition(true, false); }
        public void Close() { BeginTransition(false, false); }

        /// <summary>instant 不创建轨道，狂点返回的后续意图不会进入过渡态。</summary>
        internal void SetOpenInstant(bool open) { BeginTransition(open, true); }
        private void BeginTransition(bool open, bool instant)
        {
            if (IsTransitioning && opening == open) { if (instant) ForceCompleteTransition(); return; }
            if (!IsTransitioning && IsOpen == open) return;
            TransitionInterrupted = IsTransitioning;
            ++transitionVersion;
            TransitionHandle?.Kill(false);
            TransitionHandle = null;
            opening = open;
            if (open) gameObject.SetActive(true);
            IsTransitioning = !instant;
            int version = transitionVersion;
            if (instant)
            {
                var recipe = GetComponent<UIPanelMotion>();
                if (recipe != null) recipe.Snap(open);
                FinishTransition(version);
                return;
            }
            try { TransitionHandle = open ? PlayOpen() : PlayClose(); }
            catch (Exception ex) { Debug.LogException(ex, this); FinishTransition(version); return; }
            var handle = TransitionHandle;
            if (!handle.HasValue || (!handle.Value.IsValid && !handle.Value.IsDone)) FinishTransition(version);
            else handle.Value.OnComplete(() => FinishTransition(version));
        }
        private void FinishTransition(int version)
        {
            if (version != transitionVersion) return;
            ++transitionVersion;
            IsTransitioning = false;
            TransitionHandle = null;
            if (!opening) gameObject.SetActive(false);
            else if (!gameObject.activeSelf) gameObject.SetActive(true);
            try
            {
                if (opening) OnOpened(); else OnClosed();
            }
            finally { TransitionCompleted?.Invoke(); }
        }
        /// <summary>句柄失效或看门狗超时时也必须释放导航等待。</summary>
        public void ForceCompleteTransition()
        {
            if (!IsTransitioning) return;
            int version = transitionVersion;
            if (TransitionHandle.HasValue && !TransitionHandle.Value.IsValid && !TransitionHandle.Value.IsDone)
            {
                var recipe = GetComponent<UIPanelMotion>();
                if (recipe != null) recipe.Snap(opening);
            }
            TransitionHandle?.Skip();
            if (IsTransitioning && version == transitionVersion) FinishTransition(version);
        }
        protected virtual void OnDestroy()
        {
            ++transitionVersion;
            TransitionHandle?.Kill(false);
            TransitionHandle = null;
            IsTransitioning = false;
        }

        //负责子面板覆写初始化逻辑
        protected virtual void OnOpened()
        {
            
        }


        protected virtual void OnClosed()
        {
            
        }

    }
}
