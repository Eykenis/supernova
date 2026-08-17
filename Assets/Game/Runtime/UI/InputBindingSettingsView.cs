using System;
using System.Collections.Generic;
using Supernova.Inputs;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    [DisallowMultipleComponent]
    public sealed class InputBindingSettingsView : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(14, 16, 20, 255);
        private static readonly Color Paper = new Color32(235, 239, 244, 255);
        private static readonly Color Accent = new Color32(255, 208, 52, 255);

        private RectTransform content;
        private TMP_Text statusLabel;
        private Action backAction;
        private bool built;
        private bool rebinding;

        public static InputBindingSettingsView Create(
            RectTransform parent,
            Action onBack)
        {
            GameObject root = new GameObject(
                UiHierarchyPaths.Pause.InputBindingsPanel,
                typeof(RectTransform));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            Stretch(rect);

            InputBindingSettingsView view =
                root.AddComponent<InputBindingSettingsView>();
            view.backAction = onBack;
            view.Build();
            root.SetActive(false);
            return view;
        }

        public void Show()
        {
            GameInput.SetActiveBindingGroup(
                GameInputDefinitions.KeyboardMouseScheme);
            gameObject.SetActive(true);
            RebuildRows();
        }

        public void Hide()
        {
            if (rebinding)
                GameInput.CancelInteractiveRebind();
            rebinding = false;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            GameInput.BindingsChanged += RebuildRows;
            if (built)
                RebuildRows();
        }

        private void OnDisable()
        {
            GameInput.BindingsChanged -= RebuildRows;
        }

        private void OnDestroy()
        {
            if (rebinding)
                GameInput.CancelInteractiveRebind();
        }

        private void Build()
        {
            if (built)
                return;
            built = true;

            TMP_Text eyebrow = CreateText(
                "Eyebrow",
                transform,
                "输入设置",
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                Ink);
            SetRect(
                eyebrow.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -34f),
                new Vector2(-96f, 28f));

            TMP_Text title = CreateText(
                "Title",
                transform,
                "控制",
                44f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                Ink);
            SetRect(
                title.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -76f),
                new Vector2(-96f, 56f));

            RectTransform scrollRoot = CreateRect("Bindings Scroll", transform);
            SetRect(
                scrollRoot,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0f, -8f),
                new Vector2(-96f, -250f));
            Image scrollBackground = scrollRoot.gameObject.AddComponent<Image>();
            scrollBackground.color = new Color(1f, 1f, 1f, 0.12f);

            ScrollRect scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 36f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform viewport = CreateRect("Viewport", scrollRoot);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout =
                content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 14, 14);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter =
                content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;

            Button reset = CreateButton(
                "Reset",
                transform,
                "恢复默认按键",
                ResetBindings);
            SetRect(
                (RectTransform)reset.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(48f, 42f),
                new Vector2(230f, 48f));

            Button back = CreateButton(
                "Back",
                transform,
                "返回",
                () => backAction?.Invoke());
            SetRect(
                (RectTransform)back.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(292f, 42f),
                new Vector2(160f, 48f));

            statusLabel = CreateText(
                "Status",
                transform,
                "按下键盘以绑定",
                12f,
                FontStyles.Bold,
                TextAlignmentOptions.Right,
                Ink);
            SetRect(
                statusLabel.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 48f),
                new Vector2(-96f, 26f));
        }

        private void RebuildRows()
        {
            if (!built || content == null || rebinding)
                return;

            for (int i = content.childCount - 1; i >= 0; i--)
                Destroy(content.GetChild(i).gameObject);

            CreateMapHeader("移动视角");
            CreateSensitivityRow();

            IReadOnlyList<GameInputBindingInfo> bindings =
                GameInput.GetRebindableBindings(
                    GameInputDefinitions.KeyboardMouseScheme);
            string currentMap = string.Empty;
            for (int i = 0; i < bindings.Count; i++)
            {
                GameInputBindingInfo binding = bindings[i];
                if (!string.Equals(
                        currentMap,
                        binding.MapName,
                        StringComparison.Ordinal))
                {
                    currentMap = binding.MapName;
                    CreateMapHeader(currentMap);
                }
                CreateBindingRow(binding);
            }
        }

        private void CreateMapHeader(string mapName)
        {
            TMP_Text header = CreateText(
                mapName,
                content,
                mapName.ToUpperInvariant(),
                15f,
                FontStyles.Bold,
                TextAlignmentOptions.Left,
                Ink);
            LayoutElement layout = header.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 34f;
            layout.minHeight = 34f;
        }

        private void CreateBindingRow(GameInputBindingInfo binding)
        {
            RectTransform row = CreateRect(binding.Label, content);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 44f;
            rowLayout.minHeight = 44f;

            Image background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.36f);

            TMP_Text label = CreateText(
                "Label",
                row,
                binding.Label,
                13f,
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft,
                Ink);
            SetRect(
                label.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0.72f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(12f, 0f),
                new Vector2(-12f, 0f));

            Button button = CreateButton(
                "Binding",
                row,
                string.IsNullOrWhiteSpace(binding.DisplayString)
                    ? "未绑定"
                    : binding.DisplayString.ToUpperInvariant(),
                () => BeginRebind(binding));
            if (!string.IsNullOrWhiteSpace(binding.DisplayString))
            {
                InputPromptTextRuntime.SetBindingDisplay(
                    button.GetComponentInChildren<TMP_Text>(),
                    binding.ControlPath,
                    binding.DisplayString.ToUpperInvariant());
            }
            RectTransform buttonRect = (RectTransform)button.transform;
            buttonRect.anchorMin = new Vector2(0.72f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
            buttonRect.pivot = new Vector2(0.5f, 0.5f);
            buttonRect.anchoredPosition = Vector2.zero;
            buttonRect.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// Look sensitivity is a scalar rather than a binding, so it gets a
        /// slider row instead of a rebind button.
        /// </summary>
        private void CreateSensitivityRow()
        {
            RectTransform row = CreateRect("Look Sensitivity", content);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredHeight = 44f;
            rowLayout.minHeight = 44f;

            Image background = row.gameObject.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.36f);

            TMP_Text label = CreateText(
                "Label",
                row,
                "鼠标灵敏度",
                13f,
                FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft,
                Ink);
            SetRect(
                label.rectTransform,
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(12f, 0f),
                new Vector2(-12f, 0f));

            TMP_Text readout = CreateText(
                "Value",
                row,
                FormatSensitivity(LookSensitivitySettings.Multiplier),
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.MidlineRight,
                Ink);
            SetRect(
                readout.rectTransform,
                new Vector2(0.86f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-12f, 0f),
                new Vector2(-12f, 0f));

            Slider slider = CreateSensitivitySlider(row);
            slider.onValueChanged.AddListener(value =>
            {
                LookSensitivitySettings.Multiplier = value;
                readout.text = FormatSensitivity(
                    LookSensitivitySettings.Multiplier);
            });
        }

        private static Slider CreateSensitivitySlider(RectTransform row)
        {
            RectTransform rect = CreateRect("Slider", row);
            SetRect(
                rect,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.86f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(-16f, 12f));

            Image track = rect.gameObject.AddComponent<Image>();
            track.color = new Color(0f, 0f, 0f, 0.28f);

            RectTransform fillArea = CreateRect("Fill Area", rect);
            Stretch(fillArea);
            RectTransform fill = CreateRect("Fill", fillArea);
            Stretch(fill);
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = Accent;

            RectTransform handleArea = CreateRect("Handle Slide Area", rect);
            Stretch(handleArea);
            RectTransform handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(14f, 22f);
            Image handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Ink;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = LookSensitivitySettings.MinimumMultiplier;
            slider.maxValue = LookSensitivitySettings.MaximumMultiplier;
            slider.SetValueWithoutNotify(LookSensitivitySettings.Multiplier);
            return slider;
        }

        private static string FormatSensitivity(float multiplier)
        {
            return multiplier.ToString(
                "0.00",
                System.Globalization.CultureInfo.InvariantCulture)
                + "x";
        }

        private void BeginRebind(GameInputBindingInfo binding)
        {
            if (rebinding)
                return;

            rebinding = true;
            GameInput.StartInteractiveRebind(
                binding.ActionId,
                binding.BindingIndex,
                applied =>
                {
                    rebinding = false;
                    statusLabel.text = applied
                        ? "已保存"
                        : "取消绑定";
                    RebuildRows();
                });
        }

        private void ResetBindings()
        {
            if (rebinding)
                return;
            GameInput.ResetBindingOverrides();
            LookSensitivitySettings.ResetToDefault();
            statusLabel.text = "恢复默认";
            RebuildRows();
        }

        private static Button CreateButton(
            string objectName,
            Transform parent,
            string label,
            UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = CreateRect(objectName, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Ink;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Accent;
            colors.selectedColor = Accent;
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            button.onClick.AddListener(action);

            TMP_Text text = CreateText(
                "Label",
                rect,
                label,
                12f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                Paper);
            Stretch(text.rectTransform);
            return button;
        }

        private static TMP_Text CreateText(
            string objectName,
            Transform parent,
            string value,
            float size,
            FontStyles style,
            TextAlignmentOptions alignment,
            Color color)
        {
            RectTransform rect = CreateRect(objectName, parent);
            TextMeshProUGUI text =
                rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(
            string objectName,
            Transform parent)
        {
            GameObject instance = new GameObject(
                objectName,
                typeof(RectTransform));
            RectTransform rect = (RectTransform)instance.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void SetRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }
}
