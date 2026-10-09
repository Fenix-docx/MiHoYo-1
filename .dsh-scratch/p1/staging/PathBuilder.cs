using System;
using UnityEngine;

namespace UIPackage.Motion
{
    public static class Path
    {
        /// <summary>每对相邻点成为 CR 的真实端点；两端虚点用镜像延拓，避免起止减速。</summary>
        public static PathChain Through(params Vector2[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("至少需要两个路径点。", nameof(points));
            var segments = new PathSegment[points.Length - 1];
            for (int i = 0; i < segments.Length; i++)
                segments[i] = PathSegment.CatmullRom(i == 0 ? 2 * points[0] - points[1] : points[i - 1],
                    points[i], points[i + 1], i + 2 < points.Length ? points[i + 2] : 2 * points[i + 1] - points[i]);
            return new PathChain(segments);
        }
        /// <summary>弧高是中点到弦的有符号垂距；用三次抛物线弧，正值在行进方向左侧。</summary>
        public static PathSegment Arc(Vector2 from, Vector2 to, float arcHeight)
        {
            var chord = to - from; var normal = new Vector2(-chord.y, chord.x).normalized;
            return PathSegment.CubicBezier(from, from + chord / 3 + normal * (4 * arcHeight / 3),
                from + chord * (2f / 3) + normal * (4 * arcHeight / 3), to);
        }
    }
}
