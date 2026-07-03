using UnityEngine;
using UnityEngine.UI;

namespace Slime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SlimeStatusHud : MonoBehaviour
    {
        [SerializeField] SlimeSimulator simulator;
        [SerializeField, Min(0.05f)] float refreshInterval = 0.1f;
        [SerializeField] Vector2 panelOffset = new Vector2(22f, -22f);
        [SerializeField] Vector2 panelSize = new Vector2(350f, 154f);

        readonly Image[] _stepSegments = new Image[3];

        Canvas _canvas;
        Text _titleText;
        Text _groupText;
        Text _particleText;
        Text _cohesionText;
        Text _detailText;
        float _nextRefreshTime;

        static readonly Color PanelColor = new Color(0.03f, 0.04f, 0.05f, 0.72f);
        static readonly Color TextColor = new Color(0.93f, 0.96f, 0.97f, 1f);
        static readonly Color MutedTextColor = new Color(0.68f, 0.76f, 0.78f, 1f);
        static readonly Color SegmentActiveColor = new Color(0.15f, 0.85f, 0.78f, 1f);
        static readonly Color SegmentInactiveColor = new Color(0.22f, 0.27f, 0.29f, 0.95f);

        public void Bind(SlimeSimulator target)
        {
            simulator = target;
            Build();
            Refresh(force: true);
        }

        void Awake()
        {
            if (simulator == null)
                simulator = GetComponentInParent<SlimeSimulator>();

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
            panel.sizeDelta = panelSize;

            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = false;

            Font font = ResolveFont();
            _titleText = CreateText("Title", panel, font, 18, FontStyle.Bold, TextColor);
            SetRect(_titleText.rectTransform, 16f, -12f, panelSize.x - 32f, 24f);

            _groupText = CreateText("Group", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_groupText.rectTransform, 16f, -42f, panelSize.x - 32f, 24f);

            _particleText = CreateText("Particles", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_particleText.rectTransform, 16f, -68f, panelSize.x - 32f, 24f);

            _cohesionText = CreateText("Cohesion", panel, font, 16, FontStyle.Normal, TextColor);
            SetRect(_cohesionText.rectTransform, 16f, -94f, panelSize.x - 32f, 24f);

            _detailText = CreateText("Details", panel, font, 13, FontStyle.Normal, MutedTextColor);
            SetRect(_detailText.rectTransform, 16f, -120f, panelSize.x - 32f, 20f);

            const float segmentWidth = 96f;
            const float segmentGap = 8f;
            for (int i = 0; i < _stepSegments.Length; i++)
            {
                RectTransform segment = CreateRect("Cohesion Segment " + (i + 1), panel);
                SetRect(segment, 16f + i * (segmentWidth + segmentGap), -142f, segmentWidth, 8f);
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

            if (simulator == null)
            {
                _titleText.text = "Slime 状态";
                _groupText.text = "受控凝聚体 -";
                _particleText.text = "粒子 -";
                _cohesionText.text = "凝聚态 -";
                _detailText.text = string.Empty;
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
            SetStepSegment(step);
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
