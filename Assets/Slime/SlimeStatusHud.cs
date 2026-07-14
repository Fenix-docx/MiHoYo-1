using UnityEngine;
using UnityEngine.UI;

namespace Slime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SlimeStatusHud : MonoBehaviour
    {
        [SerializeField] SlimeSimulator simulator;
        [SerializeField] SlimeSurfaceBuilder surfaceBuilder;
        [SerializeField, Min(0.05f)] float refreshInterval = 0.1f;
        [SerializeField] Vector2 panelOffset = new Vector2(22f, -22f);
        [SerializeField] Vector2 panelSize = new Vector2(430f, 178f);

        readonly Image[] _stepSegments = new Image[3];

        Canvas _canvas;
        Text _titleText;
        Text _groupText;
        Text _particleText;
        Text _cohesionText;
        Text _detailText;
        Text _surfaceText;
        float _nextRefreshTime;

        static readonly Color PanelColor = new Color(0.03f, 0.04f, 0.05f, 0.72f);
        static readonly Color TextColor = new Color(0.93f, 0.96f, 0.97f, 1f);
        static readonly Color MutedTextColor = new Color(0.68f, 0.76f, 0.78f, 1f);
        static readonly Color SegmentActiveColor = new Color(0.15f, 0.85f, 0.78f, 1f);
        static readonly Color SegmentInactiveColor = new Color(0.22f, 0.27f, 0.29f, 0.95f);

        public void Bind(SlimeSimulator target)
        {
            simulator = target;
            ResolveSurfaceBuilder();
            Build();
            Refresh(force: true);
        }

        void Awake()
        {
            if (simulator == null)
                simulator = GetComponentInParent<SlimeSimulator>();

            ResolveSurfaceBuilder();
            Build();
        }

        void OnEnable()
        {
            Build();
            Refresh(force: true);
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefreshTime)
                return;

            Refresh(force: false);
            _nextRefreshTime = Time.unscaledTime + refreshInterval;
        }

        void Build()
        {
            if (_canvas != null)
                return;

            Vector2 resolvedPanelSize = ResolvePanelSize();

            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
                _canvas = gameObject.AddComponent<Canvas>();

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 80;

            CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            RectTransform root = (RectTransform)transform;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            RectTransform panel = CreateRect("Panel", root);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = panelOffset;
            panel.sizeDelta = resolvedPanelSize;

            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            Font font = ResolveFont();
            _titleText = CreateText("Title", panel, font, 18, FontStyle.Bold, TextColor);
            SetRect(_titleText.rectTransform, 16f, -12f, resolvedPanelSize.x - 32f, 24f);

            _groupText = CreateText("Group", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_groupText.rectTransform, 16f, -42f, resolvedPanelSize.x - 32f, 24f);

            _particleText = CreateText("Particles", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_particleText.rectTransform, 16f, -68f, resolvedPanelSize.x - 32f, 24f);

            _cohesionText = CreateText("Cohesion", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_cohesionText.rectTransform, 16f, -94f, resolvedPanelSize.x - 32f, 24f);

            _detailText = CreateText("Details", panel, font, 13, FontStyle.Normal, MutedTextColor);
            SetRect(_detailText.rectTransform, 16f, -120f, resolvedPanelSize.x - 32f, 20f);

            _surfaceText = CreateText("Surface", panel, font, 12, FontStyle.Normal, MutedTextColor);
            SetRect(_surfaceText.rectTransform, 16f, -140f, resolvedPanelSize.x - 32f, 20f);

            const float segmentWidth = 96f;
            const float segmentGap = 8f;
            for (int i = 0; i < _stepSegments.Length; i++)
            {
                RectTransform segment = CreateRect("Cohesion Segment " + (i + 1), panel);
                SetRect(segment, 16f + i * (segmentWidth + segmentGap), -164f, segmentWidth, 8f);
                Image image = segment.gameObject.AddComponent<Image>();
                image.color = SegmentInactiveColor;
                image.raycastTarget = false;
                _stepSegments[i] = image;
            }
        }

        void Refresh(bool force)
        {
            if (_titleText == null)
                return;

            ResolveSurfaceBuilder();

            if (simulator == null)
            {
                _titleText.text = "Slime 状态";
                _groupText.text = "受控凝聚体 -";
                _particleText.text = "粒子 -";
                _cohesionText.text = "凝聚态 -";
                _detailText.text = string.Empty;
                RefreshSurfaceText();
                SetStepSegment(0);
                return;
            }

            int alive = Mathf.Max(1, simulator.AliveGroupCount);
            int ordinal = Mathf.Clamp(simulator.ControlledGroupOrdinal, 1, alive);
            int step = simulator.CohesionStep;
            int controlledParticles = simulator.ControlledParticleCount;
            int totalParticles = simulator.ParticleCount;

            _titleText.text = "Slime 状态";
            _groupText.text = $"受控凝聚体 {ordinal}/{alive}  ID {simulator.ControlledGroupId}";
            _particleText.text = $"粒子 {controlledParticles}/{totalParticles}";
            _cohesionText.text = $"凝聚态 {StepName(step)} ({step}/3)";
            _detailText.text = $"浓度 {simulator.ControlledConcentration:0.0}    形变 {simulator.ControlledShapeStiffness:0.00}";
            RefreshSurfaceText();
            SetStepSegment(step);
        }

        void RefreshSurfaceText()
        {
            if (_surfaceText == null)
                return;

            if (surfaceBuilder == null)
            {
                _surfaceText.text = "表面 -";
                return;
            }

            string mode = SurfaceModeLabel(surfaceBuilder.SurfaceBuildMode);
            int blocks = surfaceBuilder.LastMarchingCubeBlockCount;
            int vertices = surfaceBuilder.LastSurfaceVertexCount;
            int capacity = surfaceBuilder.LastGpuSurfaceVertexCapacity;
            string requestedBytes = FormatBytes(surfaceBuilder.LastGpuRequestedVertexReadbackBytes);
            string usedBytes = FormatBytes(surfaceBuilder.LastGpuUsedVertexReadbackBytes);
            string pending = surfaceBuilder.HasPendingGpuReadback ? " P" : string.Empty;
            string directPending = surfaceBuilder.HasPendingGpuDirectStatsReadback ? " DP" : string.Empty;
            string suppressed = surfaceBuilder.LastGpuReadbackSuppressed ? " S" : string.Empty;
            string schedule = ScheduleStatusLabel(surfaceBuilder.LastGpuScheduleStatus);
            string direct = surfaceBuilder.LastGpuDirectDrawSubmitted ? " D" : string.Empty;
            string pendingAge = surfaceBuilder.HasPendingGpuReadback
                ? $" A{surfaceBuilder.LastGpuPendingReadbackAgeFrames}"
                : string.Empty;
            string generation = surfaceBuilder.LastGpuScheduledReadbackGeneration > 0
                ? $" G{surfaceBuilder.LastGpuAppliedReadbackGeneration}/{surfaceBuilder.LastGpuScheduledReadbackGeneration}"
                : string.Empty;

            _surfaceText.text =
                $"表面 {mode}{pending}{directPending}{suppressed}{direct}{pendingAge}{generation} 调{schedule} 块{blocks} 点{vertices}/{capacity} 读{requestedBytes}({usedBytes}) {surfaceBuilder.LastGpuReadbackLatencyFrames}f";
        }

        void ResolveSurfaceBuilder()
        {
            if (surfaceBuilder != null)
                return;

            if (simulator != null)
                surfaceBuilder = simulator.GetComponent<SlimeSurfaceBuilder>();

            if (surfaceBuilder == null)
                surfaceBuilder = GetComponentInParent<SlimeSurfaceBuilder>();
        }

        void SetStepSegment(int activeStep)
        {
            for (int i = 0; i < _stepSegments.Length; i++)
            {
                if (_stepSegments[i] == null)
                    continue;

                _stepSegments[i].color = i == activeStep - 1
                    ? SegmentActiveColor
                    : SegmentInactiveColor;
            }
        }

        static string StepName(int step)
        {
            switch (Mathf.Clamp(step, 1, 3))
            {
                case 1:
                    return "紧致";
                case 2:
                    return "稳定";
                default:
                    return "松散";
            }
        }

        static string SurfaceModeLabel(SlimeSurfaceBuildMode mode)
        {
            switch (mode)
            {
                case SlimeSurfaceBuildMode.GpuAsyncReadback:
                    return "GPU-RB";
                case SlimeSurfaceBuildMode.Auto:
                    return "Auto";
                case SlimeSurfaceBuildMode.GpuDirectRender:
                    return "GPU-RB";
                default:
                    return "CPU";
            }
        }

        static string ScheduleStatusLabel(SlimeGpuSurfaceScheduleStatus status)
        {
            switch (status)
            {
                case SlimeGpuSurfaceScheduleStatus.Scheduled:
                    return "OK";
                case SlimeGpuSurfaceScheduleStatus.Unsupported:
                    return "NO";
                case SlimeGpuSurfaceScheduleStatus.InvalidInput:
                    return "IN";
                case SlimeGpuSurfaceScheduleStatus.PendingReadbackLimit:
                    return "PR";
                case SlimeGpuSurfaceScheduleStatus.PendingBufferResizeBlocked:
                    return "PB";
                case SlimeGpuSurfaceScheduleStatus.MissingKernel:
                    return "MK";
                default:
                    return "--";
            }
        }

        static string FormatBytes(long bytes)
        {
            if (bytes <= 0L)
                return "0B";

            const float kilo = 1024f;
            const float mega = 1024f * 1024f;
            return bytes >= mega
                ? $"{bytes / mega:0.0}MB"
                : bytes >= kilo
                    ? $"{bytes / kilo:0.0}KB"
                    : $"{bytes}B";
        }

        Vector2 ResolvePanelSize()
        {
            return new Vector2(Mathf.Max(430f, panelSize.x), Mathf.Max(178f, panelSize.y));
        }

        static RectTransform CreateRect(string objectName, RectTransform parent)
        {
            GameObject child = new GameObject(objectName, typeof(RectTransform));
            RectTransform rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        static Text CreateText(string objectName, RectTransform parent, Font font, int fontSize, FontStyle style, Color color)
        {
            RectTransform rect = CreateRect(objectName, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void SetRect(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, top);
            rect.sizeDelta = new Vector2(width, height);
        }

        static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}
