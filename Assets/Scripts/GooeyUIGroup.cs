using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GooeyUI
{
    /// <summary>
    /// UI 逐像素 SDF 交集融合（唯一实现）。
    ///
    /// 原理 —— 回答"能不能直接基于纹理边缘采集作为表面做 SDF 交集"：
    ///   ① 只用 alpha 拿不到 SDF：d ≈ (a-0.5)/|∇a| 只在 1px 抗锯齿带里成立，
    ///      形状**外面**（融合真正发生的缝隙）alpha 恒为 0、梯度为 0 ⇒ smin 无值可用。
    ///   ② 所以先把每个 sprite 的 alpha 烘成 **带 padding 的有符号距离场**（GooeySdf，8SSEDT，
    ///      内区按**目标矩形宽高比**重采样 —— Image 是把 sprite 拉伸到 RectTransform 的）。
    ///      有了 padding，形状外最多 padding 距离内都有正确距离 ⇒ smin 才有意义。
    ///   ③ UI/SdfFusionUI 对每个像素求平滑并集 d = smin(d0,d1,…)；
    ///      再沿各自距离场梯度，把美术坐标映到融合场的内部深度。
    ///      轮廓描边和内部颜色随融合表面连续变化，不需要取色贴图。
    ///
    /// 用法：挂在承载参与组件的 Canvas 上（子物体里的 Image 自动收集），
    ///      把主 Canvas 里的全屏显示层（RawImage）拖到 displayLayer。
    ///      参与组件自身的 CanvasRenderer 会被临时关掉（由融合层独占绘制），
    ///      子物体上的文字等照常显示 —— 建议把显示层所在 Canvas 的 sortingOrder 放在源画布之下。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Gooey SDF Fusion")]
    public class GooeyUIGroup : MonoBehaviour
    {
        const int MaxSlots = 4;
        const string FusionShaderName = "UI/SdfFusionUI";

        static readonly int[] SdfTexIds = { Shader.PropertyToID("_Sdf0"), Shader.PropertyToID("_Sdf1"), Shader.PropertyToID("_Sdf2"), Shader.PropertyToID("_Sdf3") };
        static readonly int[] ArtTexIds = { Shader.PropertyToID("_Art0"), Shader.PropertyToID("_Art1"), Shader.PropertyToID("_Art2"), Shader.PropertyToID("_Art3") };
        static readonly int[] RectIds = { Shader.PropertyToID("_Rect0"), Shader.PropertyToID("_Rect1"), Shader.PropertyToID("_Rect2"), Shader.PropertyToID("_Rect3") };
        static readonly int[] SdfInfoIds = { Shader.PropertyToID("_SdfInfo0"), Shader.PropertyToID("_SdfInfo1"), Shader.PropertyToID("_SdfInfo2"), Shader.PropertyToID("_SdfInfo3") };
        static readonly int[] ArtUvIds = { Shader.PropertyToID("_ArtUv0"), Shader.PropertyToID("_ArtUv1"), Shader.PropertyToID("_ArtUv2"), Shader.PropertyToID("_ArtUv3") };
        static readonly int[] ArtTintIds = { Shader.PropertyToID("_ArtTint0"), Shader.PropertyToID("_ArtTint1"), Shader.PropertyToID("_ArtTint2"), Shader.PropertyToID("_ArtTint3") };
        static readonly int BlendKId = Shader.PropertyToID("_BlendK");
        static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        static readonly int DisplayUvRectId = Shader.PropertyToID("_DisplayUvRect");
        static readonly int DisplayScreenOriginId = Shader.PropertyToID("_DisplayScreenOrigin");
        static readonly int DisplayScreenAxesId = Shader.PropertyToID("_DisplayScreenAxes");

        [Header("参与融合的组件（留空则自动收集子物体里的 Image）")]
        [SerializeField] List<Graphic> sources = new List<Graphic>();
        [SerializeField] bool autoCollectChildren = true;

        [Header("显示层：主 Canvas 里的全屏 RawImage（材质会自动换成 UI/SdfFusionUI）")]
        [SerializeField] RawImage displayLayer;
        [Tooltip("可选的融合材质资产；留空则运行时用 Shader.Find 建实例。")]
        [SerializeField] Material fusionMaterial;

        [Header("融合参数")]
        [Tooltip("交融强度（屏幕像素）≈ 颈部半径。建议 ≈ 组件间隙的 1~2 倍。")]
        [SerializeField, Range(1f, 300f)] float blendK = 45f;

        [Tooltip("边缘软化（屏幕像素）。")]
        [SerializeField, Range(0.05f, 20f)] float softness = 1.2f;

        [Tooltip("烘 SDF 的内区分辨率（长边 texel 数）。")]
        [SerializeField, Range(16, 256)] int sdfResolution = 96;

        [Tooltip("烘 SDF 的 padding（texel）：决定形状外多远仍有有效距离，需覆盖交融强度。")]
        [SerializeField, Range(8, 128)] int sdfPadding = 40;

        [Tooltip("隐藏参与组件自身的渲染（由融合层独占绘制）。子物体上的文字等不受影响。")]
        [SerializeField] bool hideSources = true;

        [Header("运行")]
        [SerializeField] bool driveInEditMode = true;

        readonly List<Graphic> _resolved = new List<Graphic>();
        readonly List<Graphic> _hidden = new List<Graphic>();
        readonly Dictionary<Graphic, float> _originalAlpha = new Dictionary<Graphic, float>();
        readonly List<Graphic> _participating = new List<Graphic>();
        // One sprite can be used by several differently sized Images in the same frame.
        readonly Dictionary<Vector4, GooeySdf.Baked> _sdfCache = new Dictionary<Vector4, GooeySdf.Baked>();

        Material _material;
        int _lastHierarchyCount = -1;

        /// <summary>上一帧真正参与融合的组件数量。</summary>
        public int ActiveSourceCount { get; private set; }

        /// <summary>交融强度（屏幕像素）。</summary>
        public float BlendK
        {
            get => blendK;
            set => blendK = Mathf.Clamp(value, 1f, 300f);
        }

        /// <summary>边缘软化（屏幕像素）。</summary>
        public float Softness
        {
            get => softness;
            set => softness = Mathf.Clamp(value, 0.05f, 20f);
        }

        /// <summary>当前使用的融合材质（调试用）。</summary>
        public Material FusionMaterial => _material;

        void OnEnable()
        {
            _lastHierarchyCount = -1;
            Canvas.willRenderCanvases += BeforeCanvasRender;
            Refresh();
        }

        void OnDisable()
        {
            Canvas.willRenderCanvases -= BeforeCanvasRender;
            RestoreHidden();
            ReleaseMaterial();
        }

        void OnValidate()
        {
            blendK = Mathf.Clamp(blendK, 1f, 300f);
            softness = Mathf.Clamp(softness, 0.05f, 20f);
            sdfResolution = Mathf.Clamp(sdfResolution, 16, 256);
            sdfPadding = Mathf.Clamp(sdfPadding, 8, 128);
            _lastHierarchyCount = -1;
            if (isActiveAndEnabled)
                Refresh();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying && !driveInEditMode)
                return;

            Refresh();
        }

        void BeforeCanvasRender()
        {
            if (isActiveAndEnabled && (Application.isPlaying || driveInEditMode))
                Refresh();
        }

        /// <summary>重新解析参与组件并丢弃 sprite 缓存。</summary>
        [ContextMenu("Rebuild")]
        public void Rebuild()
        {
            ResolveSources();
            ReleaseSdfCache();
            _lastHierarchyCount = transform.hierarchyCount;
        }

        /// <summary>立刻重算一次（编辑模式也生效，便于实时调参）。</summary>
        [ContextMenu("Refresh")]
        public void Refresh()
        {
            if (transform.hierarchyCount != _lastHierarchyCount)
                Rebuild();
            else if (_resolved.Count == 0)
                ResolveSources();

            Apply();
        }

        // ---------------------------------------------------------------- 组件收集
        void ResolveSources()
        {
            _resolved.Clear();

            for (int i = 0; i < sources.Count; i++)
            {
                Graphic g = sources[i];
                if (g != null && !_resolved.Contains(g))
                    _resolved.Add(g);
            }

            if (!autoCollectChildren)
                return;

            var found = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < found.Length; i++)
            {
                Graphic g = found[i];
                if (g == null || g == transform)
                    continue;
                // 只收 Image（sprite + rect 才能烘 SDF）；文字等不参与
                if (!(g is Image))
                    continue;
                if (!_resolved.Contains(g))
                    _resolved.Add(g);
            }

            _lastHierarchyCount = transform.hierarchyCount;
        }

        // ---------------------------------------------------------------- 主流程
        void Apply()
        {
            if (displayLayer == null)
            {
                Debug.LogError("[GooeyUIGroup] 没有指定显示层（displayLayer）。", this);
                return;
            }

            Material mat = EnsureMaterial();
            if (mat == null)
                return;

            if (displayLayer.material != mat)
                displayLayer.material = mat;
            if (displayLayer.texture != null)
                displayLayer.texture = null;
            if (displayLayer.raycastTarget)
                displayLayer.raycastTarget = false;

            // Undo the RawImage UV rect, then map its corners to the same screen coordinates as the sources.
            // Canvas batching rewrites vertex positions, so do not depend on object-local vertex coordinates.
            Rect displayRect = displayLayer.GetPixelAdjustedRect();
            RectTransform drt = displayLayer.rectTransform;
            Camera displayCamera = CanvasCameraOf(displayLayer);
            Vector2 origin = RectTransformUtility.WorldToScreenPoint(displayCamera,
                drt.TransformPoint(new Vector3(displayRect.x, displayRect.y)));
            Vector2 right = RectTransformUtility.WorldToScreenPoint(displayCamera,
                drt.TransformPoint(new Vector3(displayRect.xMax, displayRect.y))) - origin;
            Vector2 up = RectTransformUtility.WorldToScreenPoint(displayCamera,
                drt.TransformPoint(new Vector3(displayRect.x, displayRect.yMax))) - origin;
            Rect uvRect = displayLayer.uvRect;
            mat.SetVector(DisplayUvRectId, new Vector4(uvRect.x, uvRect.y, uvRect.width, uvRect.height));
            mat.SetVector(DisplayScreenOriginId, new Vector4(origin.x, origin.y, 0f, 0f));
            mat.SetVector(DisplayScreenAxesId, new Vector4(right.x, right.y, up.x, up.y));

            int slot = 0;
            _participating.Clear();
            for (int i = 0; i < _resolved.Count && slot < MaxSlots; i++)
            {
                Graphic g = _resolved[i];
                if (g == null || !g.isActiveAndEnabled)
                    continue;

                var img = g as Image;
                Sprite sp = img != null ? img.overrideSprite : null;
                if (sp == null || sp.texture == null)
                    continue;
                if (img.type != Image.Type.Simple || img.useSpriteMesh)
                    continue;

                RectTransform rt = g.rectTransform;
                Rect drawingRect = GetSpriteDrawingRect(img, sp);
                Camera cam = CanvasCameraOf(g);
                Vector2 p0 = RectTransformUtility.WorldToScreenPoint(cam,
                    rt.TransformPoint(new Vector3(drawingRect.x, drawingRect.y)));
                Vector2 p2 = RectTransformUtility.WorldToScreenPoint(cam,
                    rt.TransformPoint(new Vector3(drawingRect.xMax, drawingRect.yMax)));

                Vector2 center = (p0 + p2) * 0.5f;
                // Keep the sign: negative RectTransform sizes/scales mirror the original Image.
                Vector2 half = (p2 - p0) * 0.5f;
                if (Mathf.Abs(half.x) < 1f || Mathf.Abs(half.y) < 1f)
                    continue;

                GooeySdf.Baked baked = BakeSdfFor(sp, half);
                if (!baked.valid)
                    continue;

                Texture2D tex = sp.texture;
                Rect tr = sp.textureRect;
                Vector4 artUv = new Vector4(
                    (tr.x + tr.width * 0.5f) / tex.width,
                    (tr.y + tr.height * 0.5f) / tex.height,
                    tr.width * 0.5f / tex.width,
                    tr.height * 0.5f / tex.height);

                mat.SetTexture(SdfTexIds[slot], baked.texture);
                mat.SetTexture(ArtTexIds[slot], tex);
                mat.SetVector(RectIds[slot], new Vector4(center.x, center.y, half.x, half.y));
                mat.SetVector(SdfInfoIds[slot], new Vector4(baked.padding, baked.innerSize.x, 1f, baked.innerSize.y));
                mat.SetVector(ArtUvIds[slot], artUv);
                mat.SetColor(ArtTintIds[slot], g.color);

                if (hideSources)
                {
                    Hide(g);
                    _participating.Add(g);
                }

                slot++;
            }

            ActiveSourceCount = slot;
            for (int i = _hidden.Count - 1; i >= 0; i--)
                if (!_participating.Contains(_hidden[i]))
                    Restore(_hidden[i]);

            for (int s = slot; s < MaxSlots; s++)
            {
                mat.SetVector(SdfInfoIds[s], new Vector4(0f, 1f, 0f, 0f));
                mat.SetTexture(SdfTexIds[s], Texture2D.whiteTexture);
                mat.SetTexture(ArtTexIds[s], Texture2D.whiteTexture);
            }

            mat.SetFloat(BlendKId, Mathf.Max(1f, blendK));
            mat.SetFloat(SoftnessId, Mathf.Max(0.05f, softness));
        }

        static Camera CanvasCameraOf(Graphic g)
        {
            Canvas c = g.canvas;
            if (c == null || c.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;
            return c.worldCamera;
        }

        /// <summary>Match Image.GenerateSimpleSprite, including transparent trimming and preserveAspect.</summary>
        public static Rect GetSpriteDrawingRect(Image image, Sprite sprite)
        {
            Rect r = image.GetPixelAdjustedRect();
            Vector2 size = sprite.rect.size;
            if (image.preserveAspect && size.sqrMagnitude > 0f)
            {
                float spriteRatio = size.x / size.y;
                if (spriteRatio > r.width / r.height)
                {
                    float height = r.width / spriteRatio;
                    r.y += (r.height - height) * image.rectTransform.pivot.y;
                    r.height = height;
                }
                else
                {
                    float width = r.height * spriteRatio;
                    r.x += (r.width - width) * image.rectTransform.pivot.x;
                    r.width = width;
                }
            }
            Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(sprite);
            float sw = Mathf.Max(1, Mathf.RoundToInt(size.x));
            float sh = Mathf.Max(1, Mathf.RoundToInt(size.y));
            return new Rect(r.x + r.width * padding.x / sw, r.y + r.height * padding.y / sh,
                r.width * (sw - padding.x - padding.z) / sw,
                r.height * (sh - padding.y - padding.w) / sh);
        }

        // ---------------------------------------------------------------- SDF 烘制
        GooeySdf.Baked BakeSdfFor(Sprite sp, Vector2 half)
        {
            float aspect = Mathf.Abs(half.x / half.y);
            int iw = aspect >= 1f ? sdfResolution : Mathf.Max(8, Mathf.RoundToInt(sdfResolution * aspect));
            int ih = aspect >= 1f ? Mathf.Max(8, Mathf.RoundToInt(sdfResolution / aspect)) : sdfResolution;
            float texelSize = Mathf.Min(2f * Mathf.Abs(half.x) / iw, 2f * Mathf.Abs(half.y) / ih);
            // Cover the smooth-union support even after a button is scaled down.
            int pad = Mathf.Clamp(Mathf.Max(sdfPadding, Mathf.CeilToInt((blendK + softness) / texelSize) + 2), 4, 256);
            Vector4 key = new Vector4(sp.GetInstanceID(), iw, ih, pad);
            GooeySdf.Baked baked;
            if (_sdfCache.TryGetValue(key, out baked))
                return baked;

            baked = GooeySdf.Bake(sp, sdfResolution, pad, iw / (float)ih);
            _sdfCache[key] = baked;

            if (!baked.valid)
                Debug.LogWarning("[GooeyUIGroup] 烘 SDF 失败：" + sp.name, this);

            return baked;
        }

        static void ReleaseBaked(GooeySdf.Baked baked)
        {
            if (baked.texture == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(baked.texture);
            else
                Object.DestroyImmediate(baked.texture);
        }

        // ---------------------------------------------------------------- 显示/还原
        void Hide(Graphic g)
        {
            if (g == null || g.canvasRenderer == null)
                return;

            if (!_originalAlpha.ContainsKey(g))
                _originalAlpha.Add(g, g.canvasRenderer.GetAlpha());

            if (g.canvasRenderer.GetAlpha() > 0f)
                g.canvasRenderer.SetAlpha(0f);

            if (!_hidden.Contains(g))
                _hidden.Add(g);
        }

        void RestoreHidden()
        {
            for (int i = _hidden.Count - 1; i >= 0; i--)
                Restore(_hidden[i]);
        }

        void Restore(Graphic g)
        {
            float alpha;
            if (g != null && g.canvasRenderer != null && _originalAlpha.TryGetValue(g, out alpha))
                g.canvasRenderer.SetAlpha(alpha);
            _originalAlpha.Remove(g);
            _hidden.Remove(g);
        }

        Material EnsureMaterial()
        {
            if (_material != null)
                return _material;

            if (fusionMaterial != null)
            {
                _material = new Material(fusionMaterial) { name = fusionMaterial.name + " (Instance)" };
                return _material;
            }

            Shader shader = Shader.Find(FusionShaderName);
            if (shader == null)
            {
                Debug.LogError("[GooeyUIGroup] 找不到 shader \"" + FusionShaderName + "\"。", this);
                return null;
            }

            _material = new Material(shader) { name = "SdfFusionUI (auto)" };
            return _material;
        }

        void ReleaseMaterial()
        {
            if (displayLayer != null && _material != null && displayLayer.material == _material)
                displayLayer.material = fusionMaterial;

            if (_material != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_material);
                else
                    Object.DestroyImmediate(_material);
                _material = null;
            }

            ReleaseSdfCache();
        }

        void ReleaseSdfCache()
        {
            foreach (var pair in _sdfCache)
                ReleaseBaked(pair.Value);
            _sdfCache.Clear();
        }
    }
}
