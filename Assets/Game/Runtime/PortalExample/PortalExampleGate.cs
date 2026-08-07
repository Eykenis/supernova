using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Supernova.PortalExample
{
    [DisallowMultipleComponent]
    public sealed class PortalExampleGate : MonoBehaviour
    {
        private const float ClipPlaneOffset = 0.04f;
        private static bool isRenderingPortal;

        [SerializeField] private PortalExampleGate linkedGate;
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private Camera portalCamera;
        [SerializeField, Min(0.1f)] private float apertureRadius = 1.025f;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 0.7f;
        [SerializeField, Min(256)] private int maximumTextureSize = 1280;

        private readonly Dictionary<PortalExampleTraveller, float> travellerSides =
            new Dictionary<PortalExampleTraveller, float>();

        private MaterialPropertyBlock propertyBlock;
        private RenderTexture renderTexture;

        public PortalExampleGate LinkedGate => linkedGate;

        private void OnEnable()
        {
            EnsureTriggerRelays();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseRenderTexture();
            travellerSides.Clear();
        }

        private void OnTriggerEnter(Collider other)
        {
            HandleTriggerEnter(other);
        }

        private void OnTriggerStay(Collider other)
        {
            HandleTriggerStay(other);
        }

        private void OnTriggerExit(Collider other)
        {
            HandleTriggerExit(other);
        }

        internal void HandleTriggerEnter(Collider other)
        {
            PortalExampleTraveller traveller = ResolveTraveller(other);
            if (traveller != null && linkedGate != null)
            {
                float currentSide = GetSide(traveller.transform.position);
                travellerSides[traveller] = currentSide;
                if (IsInsideAperture(other))
                {
                    TryTeleportSweptEntry(traveller, currentSide);
                }
            }
        }

        internal void HandleTriggerStay(Collider other)
        {
            PortalExampleTraveller traveller = ResolveTraveller(other);
            if (traveller == null || linkedGate == null || !traveller.CanTeleport)
            {
                return;
            }

            float currentSide = GetSide(traveller.transform.position);
            if (!travellerSides.TryGetValue(traveller, out float previousSide))
            {
                travellerSides[traveller] = currentSide;
                return;
            }

            if (!IsInsideAperture(other))
            {
                travellerSides[traveller] = currentSide;
                return;
            }

            if (previousSide > 0f && currentSide <= 0f
                && traveller.Teleport(this, linkedGate))
            {
                travellerSides.Remove(traveller);
                linkedGate.RegisterArrival(traveller);
                return;
            }

            travellerSides[traveller] = currentSide;
        }

        internal void HandleTriggerExit(Collider other)
        {
            PortalExampleTraveller traveller =
                other.GetComponentInParent<PortalExampleTraveller>();
            if (traveller != null)
            {
                travellerSides.Remove(traveller);
            }
        }

        private void EnsureTriggerRelays()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider trigger = colliders[i];
                if (trigger == null || !trigger.isTrigger
                    || trigger.gameObject == gameObject)
                {
                    continue;
                }

                PortalExampleTriggerRelay relay =
                    trigger.GetComponent<PortalExampleTriggerRelay>();
                if (relay == null)
                {
                    relay = trigger.gameObject
                        .AddComponent<PortalExampleTriggerRelay>();
                }

                relay.Configure(this);
            }
        }

        private static PortalExampleTraveller ResolveTraveller(Collider other)
        {
            if (other == null)
            {
                return null;
            }

            PortalExampleTraveller traveller =
                other.GetComponentInParent<PortalExampleTraveller>();
            if (traveller != null)
            {
                return traveller;
            }

            GameObject travellerObject = null;
            if (other.attachedRigidbody != null)
            {
                travellerObject = other.attachedRigidbody.gameObject;
            }
            else
            {
                CharacterController controller =
                    other as CharacterController
                    ?? other.GetComponentInParent<CharacterController>();
                if (controller != null)
                {
                    travellerObject = controller.gameObject;
                }
            }

            return travellerObject != null
                ? travellerObject.AddComponent<PortalExampleTraveller>()
                : null;
        }

        private bool TryTeleportSweptEntry(
            PortalExampleTraveller traveller,
            float currentSide)
        {
            if (currentSide > 0f
                || !traveller.CanTeleport
                || !traveller.TryGetWorldVelocity(out Vector3 velocity)
                || Vector3.Dot(velocity, transform.forward) >= -0.01f
                || !traveller.Teleport(this, linkedGate))
            {
                return false;
            }

            travellerSides.Remove(traveller);
            linkedGate.RegisterArrival(traveller);
            return true;
        }

        private void RegisterArrival(PortalExampleTraveller traveller)
        {
            travellerSides[traveller] = GetSide(traveller.transform.position);
        }

        private float GetSide(Vector3 worldPosition)
        {
            return Vector3.Dot(
                worldPosition - transform.position,
                transform.forward);
        }

        private bool IsInsideAperture(Collider other)
        {
            Vector3 localCenter = transform.InverseTransformPoint(
                other.bounds.center);
            return localCenter.x * localCenter.x
                + localCenter.y * localCenter.y
                <= apertureRadius * apertureRadius;
        }

        private void OnBeginCameraRendering(
            ScriptableRenderContext context,
            Camera sourceCamera)
        {
            if (isRenderingPortal || linkedGate == null
                || surfaceRenderer == null || portalCamera == null
                || sourceCamera == portalCamera
                || sourceCamera.cameraType == CameraType.Preview
                || sourceCamera.cameraType == CameraType.Reflection
                || !surfaceRenderer.enabled
                || !IsVisibleFrom(sourceCamera))
            {
                return;
            }

            EnsureRenderTexture(sourceCamera);
            ConfigurePortalCamera(sourceCamera);

            Renderer exitSurface = linkedGate.surfaceRenderer;
            bool exitSurfaceWasEnabled =
                exitSurface != null && exitSurface.enabled;

            try
            {
                isRenderingPortal = true;
                if (exitSurface != null)
                {
                    exitSurface.enabled = false;
                }

#pragma warning disable CS0618
                // Tuanjie 2022.3 rejects SubmitRenderRequest while already in
                // beginCameraRendering as recursive SRP rendering. The URP
                // single-camera entry point is the supported path in this
                // editor generation and respects portalCamera.targetTexture.
                UniversalRenderPipeline.RenderSingleCamera(
                    context,
                    portalCamera);
#pragma warning restore CS0618
            }
            finally
            {
                if (exitSurface != null)
                {
                    exitSurface.enabled = exitSurfaceWasEnabled;
                }

                isRenderingPortal = false;
            }
        }

        private bool IsVisibleFrom(Camera sourceCamera)
        {
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(sourceCamera);
            return GeometryUtility.TestPlanesAABB(planes, surfaceRenderer.bounds);
        }

        private void EnsureRenderTexture(Camera sourceCamera)
        {
            int width = Mathf.Clamp(
                Mathf.RoundToInt(sourceCamera.pixelWidth * resolutionScale),
                256,
                maximumTextureSize);
            int height = Mathf.Clamp(
                Mathf.RoundToInt(sourceCamera.pixelHeight * resolutionScale),
                256,
                maximumTextureSize);

            if (renderTexture != null
                && renderTexture.width == width
                && renderTexture.height == height)
            {
                return;
            }

            ReleaseRenderTexture();
            renderTexture = new RenderTexture(
                width,
                height,
                24,
                RenderTextureFormat.DefaultHDR)
            {
                name = name + "_View",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                useMipMap = false
            };
            renderTexture.Create();

            if (propertyBlock == null)
            {
                propertyBlock = new MaterialPropertyBlock();
            }

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

            Matrix4x4 mapping = PortalExampleSpace.BuildMapping(
                transform,
                linkedGate.transform);
            Vector3 position =
                mapping.MultiplyPoint3x4(sourceCamera.transform.position);
            Quaternion rotation = PortalExampleSpace.MapRotation(
                mapping,
                sourceCamera.transform.rotation);
            portalCamera.transform.SetPositionAndRotation(position, rotation);

            Vector4 clipPlane = CameraSpacePlane(
                portalCamera,
                linkedGate.transform.position,
                linkedGate.transform.forward,
                ClipPlaneOffset);
            portalCamera.projectionMatrix =
                portalCamera.CalculateObliqueMatrix(clipPlane);
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
