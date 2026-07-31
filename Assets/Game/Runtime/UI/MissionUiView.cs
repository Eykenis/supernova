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
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private TMP_Text resultLabel;
        [SerializeField] private CanvasGroup sceneFade;
        [SerializeField] private float fadeOutSeconds = 0.65f;
        [SerializeField] private float fadeInSeconds = 0.55f;

        public bool IsResultVisible =>
            resultPanel != null && resultPanel.activeSelf;
        public CanvasGroup SceneFade => sceneFade;
        public float FadeOutSeconds => fadeOutSeconds;
        public float FadeInSeconds => fadeInSeconds;

        public void Configure(
            TMP_Text objective,
            TMP_Text prompt,
            GameObject result,
            TMP_Text resultText,
            CanvasGroup fade,
            UiDesignTokens configuration)
        {
            objectiveLabel = objective;
            promptLabel = prompt;
            resultPanel = result;
            resultLabel = resultText;
            sceneFade = fade;
            fadeOutSeconds = configuration != null
                ? configuration.SceneFadeOutSeconds
                : 0.65f;
            fadeInSeconds = configuration != null
                ? configuration.SceneFadeInSeconds
                : 0.55f;
        }

        public void SetObjective(string value)
        {
            if (objectiveLabel != null)
                objectiveLabel.text = value ?? string.Empty;
        }

        public void SetPrompt(string value)
        {
            if (promptLabel != null)
                promptLabel.text = value ?? string.Empty;
        }

        public void ShowResult(string value)
        {
            if (resultLabel != null)
                resultLabel.text = value ?? string.Empty;
            if (resultPanel != null)
                resultPanel.SetActive(true);
        }

        public void HideResult()
        {
            if (resultPanel != null)
                resultPanel.SetActive(false);
        }
    }
}
