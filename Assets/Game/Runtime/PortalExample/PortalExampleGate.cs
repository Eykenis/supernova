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
        private const float CharacterControllerEntryTolerance = 0.04f;
        private const float WalkablePortalMinimumUpDot = 0.65f;
        private const float WalkablePortalEntryTolerance = 0.12f;
        private static bool isRenderingPortal;

        [SerializeField] private PortalExampleGate linkedGate;
        [SerializeField] private Renderer surfaceRenderer;
        [SerializeField] private Camera portalCamera;
        [SerializeField] private Shader seamlessClipShader;
        [SerializeField, Min(0.1f)] private float apertureRadius = 1.025f;
        [SerializeField, Range(0.25f, 1f)] private float resolutionScale = 0.7f;
        [SerializeField, Min(256)] private int maximumTextureSize = 1280;

        private readonly Dictionary<PortalExampleTraveller, float> travellerSides =
            new Dictionary<PortalExampleTraveller, float>();
        private readonly Dictionary<PortalExampleTraveller, HashSet<Collider>>
            travellerColliders =
                new Dictionary<PortalExampleTraveller, HashSet<Collider>>();

        private MaterialPropertyBlock propertyBlock;
        private RenderTexture renderTexture;
        private PortalExampleTraveller exclusiveTraveller;
        private bool restrictTraversal;

        public PortalExampleGate LinkedGate => linkedGate;
        internal Shader SeamlessClipShader => seamlessClipShader != null
            ? seamlessClipShader
            : linkedGate != null
                ? linkedGate.seamlessClipShader
                : null;
        internal float WorldApertureRadius
        {
            get
            {
                Vector3 scale = transform.lossyScale;
                return apertureRadius * Mathf.Max(
                    Mathf.Abs(scale.x),
                    Mathf.Abs(scale.y));
            }
        }

        /// <summary>
        /// Selects the destination used for rendering and traversal. Runtime-created
        /// checkpoint entrances use this to share the scene's landing-cell exit.
        /// </summary>
        public void LinkTo(PortalExampleGate destination)
        {
            linkedGate = destination;
        }

        /// <summary>
        /// Restricts entry through this gate to one traveller. The gate can still
        /// receive every traveller from its linked entrances, but arrivals other
        /// than the configured traveller cannot use it for a return trip.
        /// </summary>
        public void RestrictTraversalTo(PortalExampleTraveller traveller)
        {
            exclusiveTraveller = traveller;
            restrictTraversal = true;
        }

        private void OnEnable()
        {
            EnsureTriggerRelays();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            ReleaseRenderTexture();
            foreach (PortalExampleTraveller traveller
                in travellerColliders.Keys)
            {
                if (traveller != null)
                {
                    traveller.CancelPortalTraversal(this);
                }
            }
            travellerSides.Clear();
            travellerColliders.Clear();
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
            if (traveller == null)
            {
                return;
            }
            if (!IsTraversalAllowed(traveller))
            {
                ForgetTraveller(traveller);
                traveller.CompletePortalTraversal(this);
                return;
            }
            if (linkedGate != null)
            {
                HashSet<Collider> colliders = TrackCollider(traveller, other);
                float currentSide = GetTraversalSide(traveller);
                if (!travellerSides.ContainsKey(traveller))
                {
                    travellerSides[traveller] = currentSide;
                }
                if (IsInsideAperture(colliders))
                {
                    traveller.BeginPortalTraversal(this, linkedGate);
                    TryTeleportSweptEntry(traveller, currentSide);
                }
            }
        }

        internal void HandleTriggerStay(Collider other)
        {
            PortalExampleTraveller traveller = ResolveTraveller(other);
            if (traveller == null)
            {
                return;
            }
            if (!IsTraversalAllowed(traveller))
            {
                ForgetTraveller(traveller);
                traveller.CompletePortalTraversal(this);
                return;
            }
            if (linkedGate == null || !traveller.CanTeleport)
            {
                return;
            }

            HashSet<Collider> colliders = TrackCollider(traveller, other);
            float currentSide = GetTraversalSide(traveller);
            if (!travellerSides.TryGetValue(traveller, out float previousSide))
            {
                travellerSides[traveller] = currentSide;
                return;
            }

            if (!IsInsideAperture(colliders))
            {
                travellerSides[traveller] = currentSide;
                return;
            }
            traveller.BeginPortalTraversal(this, linkedGate);

            // A horizontal portal sits over solid terrain, so a grounded
            // CharacterController cannot physically cross its plane. Retry the
            // tolerant swept-entry path after cooldown as long as the feet remain
            // over the circular opening.
            if (TryTeleportSweptEntry(traveller, currentSide))
            {
                return;
            }

            if (previousSide > 0f && currentSide <= 0f
                && traveller.Teleport(this, linkedGate))
            {
                ForgetTraveller(traveller);
                linkedGate.RegisterArrival(traveller);
                return;
            }

            travellerSides[traveller] = currentSide;
        }

        internal void HandleTriggerExit(Collider other)
        {
            PortalExampleTraveller traveller = FindTraveller(other);
            if (traveller == null
                || !travellerColliders.TryGetValue(
                    traveller,
                    out HashSet<Collider> colliders))
            {
                return;
            }

            colliders.Remove(other);
            if (colliders.Count == 0)
            {
                ForgetTraveller(traveller);
                traveller.CompletePortalTraversal(this);
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
            PortalExampleTraveller traveller = FindTraveller(other);
            if (traveller != null)
            {
                return traveller;
            }
            if (other == null)
            {
                return null;
            }

            GameObject travellerObject = null;
            if (other.attachedRigidbody != null)
            {
                if (other.attachedRigidbody.isKinematic)
                {
                    return null;
                }
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

        private static PortalExampleTraveller FindTraveller(Collider other)
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
            if (other.attachedRigidbody != null)
            {
                return other.attachedRigidbody
                    .GetComponent<PortalExampleTraveller>();
            }

            CharacterController controller = other as CharacterController
                ?? other.GetComponentInParent<CharacterController>();
            return controller != null
                ? controller.GetComponent<PortalExampleTraveller>()
                : null;
        }

        private bool IsTraversalAllowed(PortalExampleTraveller traveller)
        {
            return !restrictTraversal || traveller == exclusiveTraveller;
        }

        private bool TryTeleportSweptEntry(
            PortalExampleTraveller traveller,
            float currentSide)
        {
            if (!HasReachedEntryPlane(traveller, currentSide)
                || !traveller.CanTeleport
                || !IsEnteringPortal(traveller)
                || !traveller.Teleport(this, linkedGate))
            {
                return false;
            }

            ForgetTraveller(traveller);
            linkedGate.RegisterArrival(traveller);
            return true;
        }

        private bool IsEnteringPortal(PortalExampleTraveller traveller)
        {
            if (traveller.TryGetWorldVelocity(out Vector3 velocity)
                && Vector3.Dot(velocity, transform.forward) < -0.01f)
            {
                return true;
            }

            // A grounded CharacterController reports its collision-resolved
            // velocity, which is normally zero against the terrain supporting a
            // horizontal portal. A close feet-plane overlap inside an upward-facing
            // aperture is therefore enough to confirm intentional entry.
            return traveller.UsesCharacterController && IsWalkablePortal();
        }

        private bool HasReachedEntryPlane(
            PortalExampleTraveller traveller,
            float currentSide)
        {
            if (currentSide <= 0f)
            {
                return true;
            }

            if (!traveller.UsesCharacterController)
            {
                return false;
            }

            float entryTolerance = IsWalkablePortal()
                ? WalkablePortalEntryTolerance
                : CharacterControllerEntryTolerance;
            return currentSide <= entryTolerance;
        }

        private bool IsWalkablePortal()
        {
            return Vector3.Dot(transform.forward, Vector3.up)
                >= WalkablePortalMinimumUpDot;
        }

        private void RegisterArrival(PortalExampleTraveller traveller)
        {
            travellerSides[traveller] = GetSide(traveller.transform.position);
            if (!travellerColliders.ContainsKey(traveller))
            {
                travellerColliders[traveller] = new HashSet<Collider>();
            }
        }

        private HashSet<Collider> TrackCollider(
            PortalExampleTraveller traveller,
            Collider collider)
        {
            if (!travellerColliders.TryGetValue(
                traveller,
                out HashSet<Collider> colliders))
            {
                colliders = new HashSet<Collider>();
                travellerColliders[traveller] = colliders;
            }
            if (collider != null)
            {
                colliders.Add(collider);
            }
            return colliders;
        }

        private void ForgetTraveller(PortalExampleTraveller traveller)
        {
            travellerSides.Remove(traveller);
            travellerColliders.Remove(traveller);
        }

        private float GetTraversalSide(PortalExampleTraveller traveller)
        {
            if (traveller.TryGetCharacterController(
                out CharacterController controller))
            {
                // A CharacterController cannot move its transform origin through
                // a solid wall. Use the capsule's leading edge so a wall-mounted
                // portal triggers when the player reaches its surface instead.
                return GetMinimumSide(controller.bounds);
            }

            if (!traveller.TryGetRigidbody(out Rigidbody body))
            {
                return GetSide(traveller.transform.position);
            }

            float minimumSide = float.PositiveInfinity;
            float maximumSide = float.NegativeInfinity;
            Collider[] bodyColliders =
                body.GetComponentsInChildren<Collider>(true);
            Vector3 normal = transform.forward;
            for (int index = 0; index < bodyColliders.Length; index++)
            {
                Collider collider = bodyColliders[index];
                if (collider == null || !collider.enabled
                    || !collider.gameObject.activeInHierarchy
                    || collider.attachedRigidbody != body)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                Vector3 extent = bounds.extents;
                float projectedExtent = Mathf.Abs(normal.x) * extent.x
                    + Mathf.Abs(normal.y) * extent.y
                    + Mathf.Abs(normal.z) * extent.z;
                float centerSide = GetSide(bounds.center);
                minimumSide = Mathf.Min(
                    minimumSide,
                    centerSide - projectedExtent);
                maximumSide = Mathf.Max(
                    maximumSide,
                    centerSide + projectedExtent);
            }

            return float.IsPositiveInfinity(minimumSide)
                || float.IsNegativeInfinity(maximumSide)
                    ? GetSide(traveller.transform.position)
                    : (minimumSide + maximumSide) * 0.5f;
        }

        private float GetMinimumSide(Bounds bounds)
        {
            Vector3 normal = transform.forward;
            Vector3 extent = bounds.extents;
            float projectedExtent = Mathf.Abs(normal.x) * extent.x
                + Mathf.Abs(normal.y) * extent.y
                + Mathf.Abs(normal.z) * extent.z;
            return GetSide(bounds.center) - projectedExtent;
        }

        internal void CollectPortalPlaneObstacles(
            List<Collider> results,
            float requiredTunnelDepth)
        {
            if (results == null)
            {
                return;
            }

            float radius = WorldApertureRadius;
            float frontDepth = Mathf.Max(0.08f, radius * 0.08f);
            float backDepth = Mathf.Max(
                frontDepth,
                requiredTunnelDepth);
            float halfDepth = (frontDepth + backDepth) * 0.5f;
            Vector3 queryCenter = transform.position
                + transform.forward * ((frontDepth - backDepth) * 0.5f);
            Collider[] overlaps = Physics.OverlapBox(
                queryCenter,
                new Vector3(radius, radius, halfDepth),
                transform.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            for (int index = 0; index < overlaps.Length; index++)
            {
                Collider overlap = overlaps[index];
                if (overlap == null || !overlap.enabled || overlap.isTrigger)
                {
                    continue;
                }
                Rigidbody obstacleBody = overlap.attachedRigidbody;
                if (obstacleBody != null && !obstacleBody.isKinematic)
                {
                    continue;
                }
                if (!results.Contains(overlap))
                {
                    results.Add(overlap);
                }
            }
        }

        private float GetSide(Vector3 worldPosition)
        {
            return Vector3.Dot(
                worldPosition - transform.position,
                transform.forward);
        }

        private bool IsInsideAperture(HashSet<Collider> colliders)
        {
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled
                    || !collider.gameObject.activeInHierarchy)
                {
                    continue;
                }
                Vector3 localCenter = transform.InverseTransformPoint(
                    collider.bounds.center);
                if (localCenter.x * localCenter.x
                    + localCenter.y * localCenter.y
                    <= apertureRadius * apertureRadius)
                {
                    return true;
                }
            }
            return false;
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
