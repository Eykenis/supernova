using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Supernova.Effects
{
    /// <summary>
    /// Keeps the URP depth and opaque textures available while copied distortion
    /// particles are alive. Reference counting makes overlapping automatic fire safe.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MuzzleFlashCameraTextureRequirement : MonoBehaviour
    {
        private static readonly Dictionary<UniversalAdditionalCameraData, CameraState>
            CameraStates = new Dictionary<UniversalAdditionalCameraData, CameraState>();

        private UniversalAdditionalCameraData cameraData;

        private void OnEnable()
        {
            Camera camera = Camera.main;
            if (camera == null
                || !camera.TryGetComponent(out UniversalAdditionalCameraData data))
            {
                return;
            }

            cameraData = data;
            if (!CameraStates.TryGetValue(data, out CameraState state))
            {
                state = new CameraState(
                    data.requiresColorTexture,
                    data.requiresDepthTexture);
            }

            state.ReferenceCount++;
            CameraStates[data] = state;
            data.requiresColorTexture = true;
            data.requiresDepthTexture = true;
        }

        private void OnDisable()
        {
            if (cameraData == null
                || !CameraStates.TryGetValue(cameraData, out CameraState state))
            {
                cameraData = null;
                return;
            }

            state.ReferenceCount--;
            if (state.ReferenceCount <= 0)
            {
                cameraData.requiresColorTexture = state.RequiresColorTexture;
                cameraData.requiresDepthTexture = state.RequiresDepthTexture;
                CameraStates.Remove(cameraData);
            }
            else
            {
                CameraStates[cameraData] = state;
            }

            cameraData = null;
        }

        private struct CameraState
        {
            public CameraState(bool requiresColorTexture, bool requiresDepthTexture)
            {
                RequiresColorTexture = requiresColorTexture;
                RequiresDepthTexture = requiresDepthTexture;
                ReferenceCount = 0;
            }

            public bool RequiresColorTexture;
            public bool RequiresDepthTexture;
            public int ReferenceCount;
        }
    }
}
