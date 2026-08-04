using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Supernova.Portals
{
    [DisallowMultipleComponent]
    public sealed class Portal : MonoBehaviour
    {
        private const float ClipPlaneOffset = 0.03f;

        [SerializeField] private Portal pairedPortal;
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private Camera portalCamera;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 0.65f;

        private readonly Dictionary<PortalTraveller, float> travellerSides =
            new Dictionary<PortalTraveller, float>();

        private RenderTexture renderTexture;
        private MaterialPropertyBlock propertyBlock;
        private static bool isRenderingPortal;

        public Portal PairedPortal => pairedPortal;

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            ReleaseRenderTexture();
        }

        private void OnTriggerEnter(Collider other)
        {
            PortalTraveller traveller = other.GetComponentInParent<PortalTraveller>();
            if (traveller == null || pairedPortal == null)
            {
                return;
            }

            travellerSides[traveller] = GetSide(traveller.transform.position);
        }

        private void OnTriggerStay(Collider other)
        {
            PortalTraveller traveller = other.GetComponentInParent<PortalTraveller>();
            if (traveller == null || pairedPortal == null || traveller.IsTeleporting)
            {
                return;
            }

            float currentSide = GetSide(traveller.transform.position);
            if (!travellerSides.TryGetValue(traveller, out float previousSide))
            {
                travellerSides[traveller] = currentSide;
                return;
            }

            if (previousSide > 0f && currentSide <= 0f)
            {
                traveller.Teleport(this, pairedPortal);
                travellerSides.Remove(traveller);
                pairedPortal.RegisterTeleportedTraveller(traveller);
                return;
            }

            travellerSides[traveller] = currentSide;
        }

        private void OnTriggerExit(Collider other)
        {
            PortalTraveller traveller = other.GetComponentInParent<PortalTraveller>();
            if (traveller != null)
            {
                travellerSides.Remove(traveller);
            }
        }

        public Matrix4x4 GetMappingMatrix(Portal destination)
        {
            return destination.transform.localToWorldMatrix
                * Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f))
                * transform.worldToLocalMatrix;
        }

        public static Vector3 TransformDirection(
            Portal source,
            Portal destination,
            Vector3 direction)
        {
            return source.GetMappingMatrix(destination).MultiplyVector(direction);
        }

        private void RegisterTeleportedTraveller(PortalTraveller traveller)
        {
            travellerSides[traveller] = GetSide(traveller.transform.position);
        }

        private float GetSide(Vector3 worldPosition)
        {
            return Vector3.Dot(worldPosition - transform.position, transform.forward);
        }

        private void HandleBeginCameraRendering(
            ScriptableRenderContext context,
            Camera sourceCamera)
        {
            if (isRenderingPortal || pairedPortal == null || surfaceRenderer == null
                || portalCamera == null || sourceCamera == portalCamera
                || sourceCamera.cameraType == CameraType.Preview
                || !surfaceRenderer.isVisible)
            {
                return;
            }

            EnsureRenderTexture(sourceCamera);
            ConfigurePortalCamera(sourceCamera);

            isRenderingPortal = true;
            bool surfaceWasEnabled = pairedPortal.surfaceRenderer != null
                && pairedPortal.surfaceRenderer.enabled;
            if (pairedPortal.surfaceRenderer != null)
            {
                pairedPortal.surfaceRenderer.enabled = false;
            }

            var request = new UniversalRenderPipeline.SingleCameraRequest
            {
                destination = renderTexture
            };
            RenderPipeline.SubmitRenderRequest(portalCamera, request);

            if (pairedPortal.surfaceRenderer != null)
            {
                pairedPortal.surfaceRenderer.enabled = surfaceWasEnabled;
            }
            isRenderingPortal = false;
        }

        private void EnsureRenderTexture(Camera sourceCamera)
        {
            int width = Mathf.Max(256,
                Mathf.RoundToInt(sourceCamera.pixelWidth * resolutionScale));
            int height = Mathf.Max(256,
                Mathf.RoundToInt(sourceCamera.pixelHeight * resolutionScale));

            if (renderTexture != null
                && renderTexture.width == width
                && renderTexture.height == height)
            {
                return;
            }

            ReleaseRenderTexture();
            renderTexture = new RenderTexture(width, height, 24,
                RenderTextureFormat.DefaultHDR)
            {
                name = $"{name}_PortalTexture",
                antiAliasing = 1,
                useMipMap = false
            };
            renderTexture.Create();

            propertyBlock ??= new MaterialPropertyBlock();
            surfaceRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetTexture("_PortalTexture", renderTexture);
            surfaceRenderer.SetPropertyBlock(propertyBlock);
        }

        private void ConfigurePortalCamera(Camera sourceCamera)
        {
            portalCamera.CopyFrom(sourceCamera);
            portalCamera.enabled = false;
            portalCamera.targetTexture = renderTexture;
            portalCamera.useOcclusionCulling = false;

            Matrix4x4 mapping = GetMappingMatrix(pairedPortal);
            Vector3 position = mapping.MultiplyPoint3x4(sourceCamera.transform.position);
            Vector3 forward = mapping.MultiplyVector(sourceCamera.transform.forward);
            Vector3 up = mapping.MultiplyVector(sourceCamera.transform.up);
            portalCamera.transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(forward, up));

            Vector4 clipPlane = CameraSpacePlane(
                portalCamera,
                pairedPortal.transform.position,
                pairedPortal.transform.forward,
                ClipPlaneOffset);
            portalCamera.projectionMatrix =
                sourceCamera.CalculateObliqueMatrix(clipPlane);
        }

        private static Vector4 CameraSpacePlane(
            Camera camera,
            Vector3 planePosition,
            Vector3 planeNormal,
            float offset)
        {
            Vector3 position = planePosition + planeNormal * offset;
            Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
            Vector3 cameraPosition = worldToCamera.MultiplyPoint(position);
            Vector3 cameraNormal =
                worldToCamera.MultiplyVector(planeNormal).normalized;
            return new Vector4(
                cameraNormal.x,
                cameraNormal.y,
                cameraNormal.z,
                -Vector3.Dot(cameraPosition, cameraNormal));
        }

        private void ReleaseRenderTexture()
        {
            if (renderTexture == null)
            {
                return;
            }

            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }
    }
}
