using Supernova.Inputs;
using UnityEngine;

namespace Supernova.UI
{
    /// <summary>
    /// Runtime driver used by the dedicated pause portrait preview scene.
    /// It reuses the production pause UI and RenderTexture pipeline.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PausePortraitPreviewController : MonoBehaviour
    {
        [SerializeField] private bool autoReplay = true;
        [SerializeField, Min(1f)] private float replayInterval = 2.5f;

        private GameHudController hud;
        private PauseMenuPresentation presentation;
        private float nextReplayTime;

        private void Start()
        {
            SetupPreview();
        }

        private void Update()
        {
            if (GameInput.Pressed(GameInputActionId.Submit))
                Replay();

            if (autoReplay && presentation != null
                && Time.unscaledTime >= nextReplayTime)
            {
                Replay();
            }
        }

        private void OnDisable()
        {
            if (hud != null)
                hud.ResumeGame();
        }

        [ContextMenu("Replay Pause Portrait")]
        public void Replay()
        {
            if (presentation == null)
                presentation = FindObjectOfType<PauseMenuPresentation>();

            if (presentation == null)
            {
                Debug.LogWarning(
                    "Pause portrait preview could not find a PauseMenuPresentation.");
                return;
            }

            presentation.PlayIntro();
            nextReplayTime = Time.unscaledTime + Mathf.Max(1f, replayInterval);
        }

        private void SetupPreview()
        {
            hud = FindObjectOfType<GameHudController>();
            if (hud == null)
            {
                GameObject hudObject = new GameObject("Game HUD Preview");
                hud = hudObject.AddComponent<GameHudController>();
            }

            bool openedPauseMenu = !hud.IsPauseMenuVisible;
            if (openedPauseMenu)
                hud.PauseGame();

            if (hud.RootCanvas != null)
                hud.RootCanvas.gameObject.SetActive(false);
            if (hud.CrosshairCanvas != null)
                hud.CrosshairCanvas.gameObject.SetActive(false);
            if (hud.LoadingCanvas != null)
                hud.LoadingCanvas.gameObject.SetActive(false);

            presentation = hud.PauseCanvas != null
                ? hud.PauseCanvas.GetComponentInChildren<PauseMenuPresentation>(true)
                : null;
            if (presentation == null)
                presentation = FindObjectOfType<PauseMenuPresentation>();

            if (presentation == null)
            {
                Debug.LogWarning(
                    "Pause portrait preview could not find a PauseMenuPresentation.");
                enabled = false;
                return;
            }

            if (openedPauseMenu)
                nextReplayTime = Time.unscaledTime + Mathf.Max(1f, replayInterval);
            else
                Replay();
        }
    }
}
