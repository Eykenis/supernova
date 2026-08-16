using UnityEngine;

namespace Supernova.PortalExample
{
    /// <summary>
    /// Removes a runtime-created portal when the terrain that supported its
    /// placement no longer exists. Destruction or replacement of the support
    /// collider and mesh invalidates the dependency, and so does mining the
    /// surface out from under the portal: voxel terrain rewrites a chunk mesh in
    /// place, so the mesh reference survives and only a positional probe can tell
    /// that the anchored surface is gone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PortalSurfaceDependency : MonoBehaviour
    {
        [SerializeField] private Collider supportCollider;
        [SerializeField] private Mesh supportMesh;
        [SerializeField] private Vector3 localSurfaceAnchor;
        [SerializeField] private Vector3 localSurfaceNormal;
        [SerializeField] private bool hasSurfaceAnchor;
        [Tooltip(
            "Distance in front of the anchored surface that the probe ray starts "
            + "from. It must clear the mined voxel so the ray begins outside the "
            + "support collider.")]
        [SerializeField, Min(0.01f)] private float surfaceProbeDistance = 0.6f;
        [Tooltip(
            "How far the supporting surface may recede before the portal is "
            + "considered unsupported.")]
        [SerializeField, Min(0.01f)] private float surfaceTolerance = 0.25f;

        public Collider SupportCollider => supportCollider;
        public Mesh SupportMesh => supportMesh;
        public bool HasSurfaceAnchor => hasSurfaceAnchor;

        public void Configure(Collider support)
        {
            supportCollider = support;
            supportMesh = support is MeshCollider meshCollider
                ? meshCollider.sharedMesh
                : null;
            hasSurfaceAnchor = false;
        }

        /// <summary>
        /// Anchors the dependency to the exact surface point the portal was placed
        /// on, so mining that spot removes the portal even though the chunk mesh
        /// object and reference stay alive.
        /// </summary>
        public void Configure(
            Collider support,
            Vector3 surfacePoint,
            Vector3 surfaceNormal)
        {
            Configure(support);
            if (support == null || surfaceNormal.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Transform supportTransform = support.transform;
            localSurfaceAnchor = supportTransform.InverseTransformPoint(
                surfacePoint);
            localSurfaceNormal = supportTransform.InverseTransformDirection(
                surfaceNormal.normalized);
            hasSurfaceAnchor = localSurfaceNormal.sqrMagnitude > 0.0001f;
        }

        private void LateUpdate()
        {
            if (HasValidSupport())
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(gameObject);
            }
            else
            {
                DestroyImmediate(gameObject);
            }
        }

        private bool HasValidSupport()
        {
            if (supportCollider == null
                || !supportCollider.enabled
                || !supportCollider.gameObject.activeInHierarchy)
            {
                return false;
            }

            if (supportCollider is MeshCollider meshCollider
                && (supportMesh == null
                    || meshCollider.sharedMesh != supportMesh))
            {
                return false;
            }

            return HasSupportingSurface();
        }

        private bool HasSupportingSurface()
        {
            if (!hasSurfaceAnchor)
            {
                return true;
            }

            Transform supportTransform = supportCollider.transform;
            Vector3 anchor = supportTransform.TransformPoint(
                localSurfaceAnchor);
            Vector3 normal = supportTransform.TransformDirection(
                localSurfaceNormal);
            if (normal.sqrMagnitude <= 0.0001f)
            {
                return true;
            }
            normal = normal.normalized;

            // Probe along the surface normal from outside the geometry, and again
            // from the far side. A MeshCollider only reports hits on front faces,
            // so a single direction would depend on the chunk mesh winding; either
            // hit confirms the anchored surface is still there. Restricting the
            // cast to the support collider keeps other terrain sections and the
            // portal's own trigger from standing in for the mined geometry.
            float probeDistance = Mathf.Max(0.01f, surfaceProbeDistance);
            float tolerance = Mathf.Max(0.01f, surfaceTolerance);
            float probeLength = probeDistance + tolerance;
            return IsSurfaceHit(anchor + normal * probeDistance, -normal, probeLength, anchor, tolerance)
                || IsSurfaceHit(anchor - normal * probeDistance, normal, probeLength, anchor, tolerance);
        }

        private bool IsSurfaceHit(
            Vector3 origin,
            Vector3 direction,
            float probeLength,
            Vector3 anchor,
            float tolerance)
        {
            return supportCollider.Raycast(
                    new Ray(origin, direction),
                    out RaycastHit hit,
                    probeLength)
                && Vector3.Distance(hit.point, anchor) <= tolerance;
        }
    }
}
