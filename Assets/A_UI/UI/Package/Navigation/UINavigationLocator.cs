using UnityEngine;

namespace UIPackage
{
    /// <summary>导航器查找的唯一入口，运行时与编辑器共用。必须是 public：Editor 是另一个程序集。</summary>
    public static class NavigationLocator
    {
        public static UINavigator FindNavigator(Component context)
        {
            if (UINavigator.Instance != null) return UINavigator.Instance;

            if (context != null)
            {
                var inParents = context.GetComponentInParent<UINavigator>(true);
                if (inParents != null) return inParents;
            }

            // FindFirstObjectByType 在 6000.4 因依赖 InstanceID 排序而弃用；顺序在这里无关紧要。
            // Include：UI 常挂在初始 inactive 的容器下。
            return Object.FindAnyObjectByType<UINavigator>(FindObjectsInactive.Include);
        }
    }
}