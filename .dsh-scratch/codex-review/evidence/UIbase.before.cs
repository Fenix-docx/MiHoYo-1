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
        public void Open()
        {
            if(IsOpen) return;
            gameObject.SetActive(true);
            OnOpened();
        }
        public void Close()
        {
            if(!IsOpen) return;
            gameObject.SetActive(false);
            OnClosed();
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
