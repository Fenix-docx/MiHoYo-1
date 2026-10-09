using UnityEngine;
using System.Collections.Generic;

namespace UIPackage
{
    /// <summary>
    /// 面板注册表：按 PanelId 索引，同类型允许多个实例共存。
    /// 不负责导航，也不负责面板显隐（显隐由 UINavigator → UIBase 独家掌控）。
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("UI面板管理（唯一注册通道）")]
        [SerializeField]
        private UIBase[] uiPanels;

        [Tooltip("启动时关闭 uiPanels 里的面板。\n" +
                 "必须在 Awake 执行：所有 Awake 一定早于任何 Start，因此不会和 UINavigator.Start 的 " +
                 "ResetToRoot 抢顺序（放在 Start 里会偶发把刚打开的根面板又关掉）。")]
        [SerializeField]
        private bool closePanelsOnAwake = true;

        private readonly Dictionary<string, UIBase> _panelKey = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[UIManager] 场景中存在多个 UIManager，销毁后加入的实例。", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            RegisterPanels();

            if (closePanelsOnAwake && uiPanels != null)
            {
                foreach (var panel in uiPanels)
                {
                    panel?.Close();
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 注册 Inspector 中配置的面板。
        /// </summary>
        private void RegisterPanels()
        {
            if (uiPanels == null || uiPanels.Length == 0)
            {
                Debug.LogWarning("[UIManager] uiPanels 为空，GetPanel 系列将取不到任何面板。", this);
                return;
            }

            foreach (var panel in uiPanels) RegisterPanel(panel);
        }

        /// <summary>
        /// 面板注册。键是 PanelId 而不是类型 —— 同一类型可以注册多个不同 PanelId 的实例。
        /// </summary>
        public void RegisterPanel(UIBase panel)
        {
            if (panel == null) return;

            var key = panel.PanelId;
            if (string.IsNullOrWhiteSpace(key))
            {
                // 不能直接拿 panel.PanelId 当键：null 会让 Dictionary 抛 ArgumentNullException，
                // 空串会把所有没填 id 的面板挤进同一个键，于是第二个起被静默丢弃。
                Debug.LogError($"[UIManager] 面板 {panel.name}（{panel.GetType().Name}）未设置 PanelId，注册失败。", this);
                return;
            }

            if (_panelKey.TryGetValue(key, out var existing) && existing != null && existing != panel)
            {
                Debug.LogError($"[UIManager] PanelId 重复：{key}（已有 {existing.name}，新来 {panel.name}）。", this);
                return;
            }

            _panelKey[key] = panel;
        }

        public void UnregisterPanel(UIBase panel)
        {
            if (panel == null) return;

            var id = panel.PanelId;
            if (!string.IsNullOrWhiteSpace(id) &&
                _panelKey.TryGetValue(id, out var existing) && existing == panel)
            {
                _panelKey.Remove(id);
            }
        }

        /// <summary>
        /// 按 PanelId 精确取面板 —— 同类多实例的正确姿势。
        /// </summary>
        public T GetPanel<T>(string panelId) where T : UIBase
        {
            if (string.IsNullOrWhiteSpace(panelId)) return null;

            if (!_panelKey.TryGetValue(panelId, out var panel)) return null;

            if (panel == null)      // 已销毁：惰性清理，避免脏引用
            {
                _panelKey.Remove(panelId);
                return null;
            }

            if (panel is T typed) return typed;

            Debug.LogWarning($"[UIManager] 面板 {panelId} 的实际类型是 {panel.GetType().Name}，不是 {typeof(T).Name}。", this);
            return null;
        }

        /// <summary>
        /// 按类型取"唯一"实例。0 个返回 null；多个实例报错并返回 null ——
        /// 刻意不静默返回第一个，那正是"查到的不是你想要的"这类隐蔽 bug 的来源。
        /// </summary>
        public T GetPanel<T>() where T : UIBase
        {
            T found = null;

            foreach (var panel in _panelKey.Values)
            {
                if (panel == null || panel is not T typed) continue;   // Unity 假 null 在此被拦掉

                if (found != null)
                {
                    Debug.LogError(
                        $"[UIManager] {typeof(T).Name} 存在多个实例，GetPanel<T>() 语义不明确。\n" +
                        "请改用 GetPanel<T>(panelId) 或 GetPanels<T>()。", this);
                    return null;
                }

                found = typed;
            }

            return found;
        }

        /// <summary>
        /// 取某类型的全部存活实例（同类多实例场景）。
        /// </summary>
        public List<T> GetPanels<T>() where T : UIBase
        {
            var result = new List<T>();
            foreach (var panel in _panelKey.Values)
            {
                if (panel is T typed) result.Add(typed);
            }

            return result;
        }
    }
}
