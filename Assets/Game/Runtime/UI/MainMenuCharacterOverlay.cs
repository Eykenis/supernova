using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Supernova.UI
{
    /// <summary>
    /// Re-composites the live menu character above a screen-space overlay canvas.
    /// The original renderers keep their animation and materials; only their layer
    /// is temporarily isolated while the integrated Home menu is active.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class MainMenuCharacterOverlay : MonoBehaviour
    {
        private readonly Dictionary<GameObject, int> originalLayers =
            new Dictionary<GameObject, int>();

        private RawImage overlayImage;
        private Camera sourceCamera;
        private Camera captureCamera;
        private RenderTexture captureTexture;
        private int captureLayerMask;
        private int originalSourceCullingMask;
        private bool overlayActive;

        public RawImage OverlayImage => ResolveOverlayImage();

        public void Begin(Transform characterRoot, Camera configuredSourceCamera)
        {
            StopOverlay();
            if (characterRoot == null || configuredSourceCamera == null)
                return;

            int captureLayer = LayerMask.NameToLayer(UiLayerNames.PausePortrait);
            if (captureLayer < 0)
            {
                Debug.LogError(
                    $"The required UI layer '{UiLayerNames.PausePortrait}' is missing.",
                    this);
                return;
            }

            Renderer[] renderers = characterRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || originalLayers.ContainsKey(renderer.gameObject))
                    continue;

                originalLayers.Add(renderer.gameObject, renderer.gameObject.layer);
                renderer.gameObject.layer = captureLayer;
            }

            if (originalLayers.Count == 0)
                return;

            sourceCamera = configuredSourceCamera;
            captureLayerMask = 1 << captureLayer;
            originalSourceCullingMask = sourceCamera.cullingMask;
            sourceCamera.cullingMask &= ~captureLayerMask;

            GameObject cameraObject = new GameObject("Main Menu Character Capture Camera");
            cameraObject.hideFlags = HideFlags.HideAndDontSave;
            cameraObject.transform.SetParent(transform, false);
            captureCamera = cameraObject.AddComponent<Camera>();
            captureCamera.enabled = true;

            ResolveOverlayImage().color = Color.white;
            overlayActive = true;
            SyncWithSourceCamera();
        }

        public void SyncWithSourceCamera()
        {
            if (!overlayActive || sourceCamera == null || captureCamera == null)
                return;

            EnsureCaptureTexture();
            captureCamera.CopyFrom(sourceCamera);
            captureCamera.transform.SetPositionAndRotation(
                sourceCamera.transform.position,
                sourceCamera.transform.rotation);
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.cullingMask = captureLayerMask;
            captureCamera.targetTexture = captureTexture;
            captureCamera.depth = sourceCamera.depth + 1f;
            captureCamera.allowHDR = false;
            captureCamera.enabled = true;
        }

        public void StopOverlay()
        {
            if (sourceCamera != null && overlayActive)
                sourceCamera.cullingMask = originalSourceCullingMask;

            foreach (KeyValuePair<GameObject, int> entry in originalLayers)
            {
                if (entry.Key != null)
                    entry.Key.layer = entry.Value;
            }
            originalLayers.Clear();

            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                Destroy(captureCamera.gameObject);
            }
            captureCamera = null;

            if (captureTexture != null)
            {
                captureTexture.Release();
                Destroy(captureTexture);
            }
            captureTexture = null;

            RawImage image = ResolveOverlayImage();
            image.texture = null;
            image.color = Color.clear;
            sourceCamera = null;
            captureLayerMask = 0;
            overlayActive = false;
        }

        private void OnDisable()
        {
            StopOverlay();
        }

        private void OnDestroy()
        {
            StopOverlay();
        }

        private RawImage ResolveOverlayImage()
        {
            if (overlayImage == null)
                overlayImage = GetComponent<RawImage>();
            return overlayImage;
        }

        private void EnsureCaptureTexture()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (captureTexture != null
                && captureTexture.width == width
                && captureTexture.height == height)
            {
                return;
            }

            if (captureTexture != null)
            {
                if (captureCamera != null)
                    captureCamera.targetTexture = null;
                captureTexture.Release();
                Destroy(captureTexture);
            }

            captureTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.ARGB32)
            {
                name = "Main Menu Character Overlay",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false
            };
            captureTexture.Create();
            ResolveOverlayImage().texture = captureTexture;
        }
    }
}
