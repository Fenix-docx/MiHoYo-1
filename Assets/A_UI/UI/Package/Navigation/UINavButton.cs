using UnityEngine;
using UnityEngine.UI;

namespace UIPackage
{
    /// <summary>
    /// 按钮 → 导航的适配器。动作会在 OnEnable 自动绑到 Button.onClick。
    ///
    /// 刻意不把这个绑定做进 UIBase：UIBase 是"面板"（一棵子树的根），
    /// 一个面板里有 0..N 个按钮且各自目标不同，UIBase 无法知道"哪个按钮去哪"；
    /// 而且那会让 UIBase 同时依赖 uGUI 与导航包，重新制造刚拆掉的耦合。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public class UINavButton : MonoBehaviour
    {
        public enum NavAction { Push, Back, ToRoot }

        [Header("导航器配置")]
        [Tooltip("留空则运行时自动解析（父级 → 场景）")]
        [SerializeField] private UINavigator navigator;

        [Header("动作配置")]
        [SerializeField] private NavAction action = NavAction.Push;
        [Tooltip("action == Push 时使用。直接拖一个挂 UIBase 的面板进来。")]
        [SerializeField] private UIBase target;
        [Tooltip("自动把动作绑到 Button.onClick；若 Inspector 里已有持久化绑定则跳过，避免一次点击触发两次")]
        [SerializeField] private bool autoBindOnClick = true;
        [Tooltip("可选：数据驱动 / 跨场景时用；target 为空才按它解析。")]
        [SerializeField] private string targetNodeId;

        private Button _button;


        private void Awake()
        {
            _button = GetComponent<Button>();
        }

        private void OnEnable()
        {
            if (!autoBindOnClick || _button == null) return;
            if (_button.onClick.GetPersistentEventCount() > 0) return;   // 已手动绑定，避免双触发

            _button.onClick.AddListener(Invoke);
        }

        private void OnDisable()
        {
            if (_button != null) _button.onClick.RemoveListener(Invoke);
        }

        /// <summary>执行当前 action。供 Button.onClick 绑定，或代码直接调用。</summary>
        public void Invoke()
        {
            switch (action)
            {
                case NavAction.Back:   Back();       break;
                case NavAction.ToRoot: ToRoot();     break;
                default:               PushTarget(); break;
            }
        }

        public void PushTarget()
        {
            var nav = ResolveNavigator();
            if (nav == null) return;

            if (target != null) { nav.Push(target); return; }

            if (!string.IsNullOrWhiteSpace(targetNodeId)) { nav.Push(targetNodeId); return; }

            Debug.LogWarning("[UINavButton] 既没有 Target 面板也没有 Target Node Id，Push 未执行。", this);
        }

        public void Back()
        {
            var nav = ResolveNavigator();
            if (nav != null) nav.Back();          // 不用 ?.：Unity 假 null 会穿透
        }

        public void ToRoot()
        {
            var nav = ResolveNavigator();
            if (nav != null) nav.ResetToRoot();
        }

        private UINavigator ResolveNavigator()
        {
            if (navigator != null) return navigator;

            navigator = NavigationLocator.FindNavigator(this);
            if (navigator == null)
            {
                Debug.LogWarning("[UINavButton] 场景中找不到 UINavigator。", this);
            }

            return navigator;
        }
    }
}
