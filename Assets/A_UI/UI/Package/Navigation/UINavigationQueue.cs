using System;
using System.Collections.Generic;

namespace UIPackage
{
    public enum NavIntentKind { Push, Back, Replace, ToRoot, CloseAll }
    public struct NavIntent
    {
        public NavIntentKind kind;
        public UIBase target;
        public string targetId;
        public int enqueueFrame;
        public bool instant;
    }
    /// <summary>只保存意图；目标解析、拓扑校验和视觉完成属于导航器。</summary>
    public sealed class UINavigationQueue
    {
        private readonly List<NavIntent> items = new List<NavIntent>();
        private readonly Action<string> warn;
        private int backBurst;
        public int MaxQueue { get; set; } = 8;
        public int FastForwardThreshold { get; set; } = 3;
        public int Count => items.Count;
        public UINavigationQueue(Action<string> warning = null) { warn = warning; }
        public bool Enqueue(NavIntent intent)
        {
            if (intent.kind == NavIntentKind.ToRoot || intent.kind == NavIntentKind.CloseAll) Clear();
            if (intent.kind == NavIntentKind.Push && items.Count > 0)
            {
                var last = items[items.Count - 1];
                if (last.kind == NavIntentKind.Push && ReferenceEquals(last.target, intent.target) && last.targetId == intent.targetId)
                    return true;
            }
            if (items.Count >= Math.Max(1, MaxQueue))
            {
                warn?.Invoke("[UINavigator] 导航队列已满，丢弃最新意图。");
                return false;
            }
            if (intent.kind == NavIntentKind.Back) intent.instant = ++backBurst > FastForwardThreshold;
            else backBurst = 0;
            items.Add(intent);
            return true;
        }
        public bool TryDequeue(out NavIntent intent)
        {
            if (items.Count == 0) { intent = default; return false; }
            intent = items[0]; items.RemoveAt(0); return true;
        }
        public void Clear() { items.Clear(); backBurst = 0; }
        public void ResetBurst() { if (items.Count == 0) backBurst = 0; }
    }
}
