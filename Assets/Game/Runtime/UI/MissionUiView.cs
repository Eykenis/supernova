using System.Collections;
using System.Collections.Generic;
using Supernova.Inputs;
using TMPro;
using UnityEngine;

namespace Supernova.UI
{
    /// <summary>
    /// Mission-facing portion of the unified runtime UI. Mission logic only publishes
    /// presentation state through this view and never creates a second Canvas hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionUiView : MonoBehaviour
    {
        [SerializeField] private TMP_Text objectiveLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private TMP_Text earlyEvacuationPromptLabel;
        [SerializeField] private GameObject earlyEvacuationProgressRoot;
        [SerializeField] private RectTransform earlyEvacuationProgressFill;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text resultLabel;
        [SerializeField] private CanvasGroup sceneFade;
        [SerializeField] private float fadeOutSeconds = 0.65f;
        [SerializeField] private float fadeInSeconds = 0.55f;

        private Coroutine resultAnimation;
        private bool earlyEvacuationAvailabilityInitialized;
        private bool earlyEvacuationAvailable;
        private readonly List<Canvas> suppressedWorldValueCanvases =
            new List<Canvas>();

        public bool IsResultVisible =>
            resultPanel != null && resultPanel.activeSelf;
        public CanvasGroup SceneFade => sceneFade;
        public TMP_Text PromptLabel => promptLabel;
        public TMP_Text EarlyEvacuationPromptLabel =>
            earlyEvacuationPromptLabel;
        public GameObject EarlyEvacuationProgressRoot =>
            earlyEvacuationProgressRoot;
        public RectTransform EarlyEvacuationProgressFill =>
            earlyEvacuationProgressFill;
        public float FadeOutSeconds => fadeOutSeconds;
        public float FadeInSeconds => fadeInSeconds;

        public void Configure(
            TMP_Text objective,
            TMP_Text prompt,
            TMP_Text evacuationPrompt,
            GameObject evacuationProgressRoot,
            RectTransform evacuationProgressFill,
            GameObject result,
            TMP_Text resultText,
            CanvasGroup fade,
            UiDesignTokens configuration)
        {
            objectiveLabel = objective;
            promptLabel = prompt;
            earlyEvacuationPromptLabel = evacuationPrompt;
            earlyEvacuationProgressRoot = evacuationProgressRoot;
            earlyEvacuationProgressFill = evacuationProgressFill;
            resultPanel = result;
            resultLabel = resultText;
            sceneFade = fade;
            fadeOutSeconds = configuration != null
                ? configuration.SceneFadeOutSeconds
                : 0.65f;
            fadeInSeconds = configuration != null
                ? configuration.SceneFadeInSeconds
                : 0.55f;
            SetEarlyEvacuationState(false, 0f);
        }

        public void SetObjective(string value)
        {
            InputPromptTextRuntime.SetText(objectiveLabel, value);
        }

        public void SetPrompt(string value)
        {
            InputPromptTextRuntime.SetText(promptLabel, value);
            if (promptLabel != null)
            {
                promptLabel.gameObject.SetActive(
                    !string.IsNullOrWhiteSpace(value));
            }
        }

        public void SetEarlyEvacuationState(
            bool available,
            float progress)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            if (!earlyEvacuationAvailabilityInitialized
                || earlyEvacuationAvailable != available)
            {
                earlyEvacuationAvailabilityInitialized = true;
                earlyEvacuationAvailable = available;
                if (earlyEvacuationPromptLabel != null)
                {
                    InputPromptTextRuntime.SetText(
                        earlyEvacuationPromptLabel,
                        available
                            ? "长按 {{input:Gameplay/Interact}} 提前撤离"
                            : string.Empty);
                    earlyEvacuationPromptLabel.gameObject.SetActive(available);
                }
            }

            if (earlyEvacuationProgressFill != null)
            {
                Vector2 anchorMax =
                    earlyEvacuationProgressFill.anchorMax;
                anchorMax.x = clampedProgress;
                earlyEvacuationProgressFill.anchorMax = anchorMax;
            }
            if (earlyEvacuationProgressRoot != null)
            {
                bool progressVisible =
                    available && clampedProgress > 0f;
                if (earlyEvacuationProgressRoot.activeSelf != progressVisible)
                    earlyEvacuationProgressRoot.SetActive(progressVisible);
            }
        }

        public void ShowResult(string value)
        {
            StopResultAnimation();
            SetWorldValueLabelsSuppressed(true);
            InputPromptTextRuntime.SetText(resultLabel, value);
            if (resultPanel != null)
                resultPanel.SetActive(true);
        }

        public void ShowResultCountAnimation(
            string prefix,
            string suffix,
            int targetValue,
            float durationSeconds)
        {
            StopResultAnimation();
            SetWorldValueLabelsSuppressed(true);
            int clampedTarget = Mathf.Max(0, targetValue);
            SetAnimatedResultText(prefix, suffix, 0);
            if (resultPanel != null)
                resultPanel.SetActive(true);

            if (durationSeconds <= 0f || !isActiveAndEnabled)
            {
                SetAnimatedResultText(prefix, suffix, clampedTarget);
                return;
            }

            resultAnimation = StartCoroutine(AnimateResultCount(
                prefix,
                suffix,
                clampedTarget,
                durationSeconds));
        }

        public void HideResult()
        {
            StopResultAnimation();
            SetWorldValueLabelsSuppressed(false);
            if (resultPanel != null)
                resultPanel.SetActive(false);
        }

        private void SetWorldValueLabelsSuppressed(bool suppressed)
        {
            if (!suppressed)
            {
                for (int i = 0; i < suppressedWorldValueCanvases.Count; i++)
                {
                    Canvas canvas = suppressedWorldValueCanvases[i];
                    if (canvas != null)
                        canvas.enabled = true;
                }
                suppressedWorldValueCanvases.Clear();
                return;
            }

            if (suppressedWorldValueCanvases.Count > 0)
                return;

            ValuableObjectWorldUi[] worldValueViews =
                FindObjectsOfType<ValuableObjectWorldUi>(true);
            for (int i = 0; i < worldValueViews.Length; i++)
            {
                Canvas canvas = worldValueViews[i].WorldCanvas;
                if (canvas == null || !canvas.enabled)
                    continue;

                canvas.enabled = false;
                suppressedWorldValueCanvases.Add(canvas);
            }
        }

        private IEnumerator AnimateResultCount(
            string prefix,
            string suffix,
            int targetValue,
            float durationSeconds)
        {
            float elapsed = 0f;
            int displayedValue = 0;
            while (elapsed < durationSeconds)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                int nextValue = EvaluateResultCount(
                    targetValue,
                    elapsed / durationSeconds);
                if (nextValue == displayedValue) continue;

                displayedValue = nextValue;
                SetAnimatedResultText(prefix, suffix, displayedValue);
            }

            SetAnimatedResultText(prefix, suffix, targetValue);
            resultAnimation = null;
        }

        private void SetAnimatedResultText(
            string prefix,
            string suffix,
            int value)
        {
            InputPromptTextRuntime.SetText(
                resultLabel,
                (prefix ?? string.Empty)
                    + Mathf.Max(0, value)
                    + (suffix ?? string.Empty));
        }

        private void StopResultAnimation()
        {
            if (resultAnimation == null) return;

            StopCoroutine(resultAnimation);
            resultAnimation = null;
        }

        private static int EvaluateResultCount(
            int targetValue,
            float normalizedTime)
        {
            float time = Mathf.Clamp01(normalizedTime);
            float inverse = 1f - time;
            float easedTime = 1f - inverse * inverse * inverse;
            return Mathf.RoundToInt(
                Mathf.Max(0, targetValue) * easedTime);
        }
    }
}
