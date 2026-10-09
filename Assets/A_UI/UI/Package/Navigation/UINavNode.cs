using System;
using System.Collections.Generic;
using UnityEngine;

///<summary>
/// 
/// </summary>
namespace UIPackage
{
    [Serializable]
    public class UINavNode
    {
        [SerializeField] private UIBase page;
        [SerializeField] private List<UIBase> children = new List<UIBase>();
        
        [SerializeField] private bool closeParentOnPush = true;// 进入子节点时是否关闭父节点
        //==========================
        public UIBase Page {get=>page;set=>page=value;}// 节点对应的面板展示
        public IReadOnlyList<UIBase> Children=> children;
        public bool CloseParentOnPush {get=>closeParentOnPush;set=>closeParentOnPush=value;}
        public string ID =>page!=null ? page.PanelId : string.Empty;
        public string DisplayName => page!=null ? page.name : "null";
        public bool HasChildren(UIBase child) => children!=null && children.Contains(child);


        // internal：只有 UINavigator 能改边（它负责防环 / 去重 / 清缓存）。
        // 注意 Editor 是另一个程序集，看不到 internal —— 窗口只能走 UINavigator 的 public API。
        internal bool AddChildRaw(UIBase child)
        {
            if (child == null || children.Contains(child)) return false;
            children.Add(child);
            return true;
        }
        internal bool RemoveChildRaw(UIBase child) => child != null && children.Remove(child);

        internal void RemoveChildReferences(UIBase target)
        {
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i] == null || children[i] == target) children.RemoveAt(i);
            }
        }
    }
}
