using Supernova.PortalExample;
using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// PortalGun ammunition. It uses the same first-contact lifecycle as SolidGun
    /// ammunition, but places a spawn-checkpoint entrance instead of solid voxels.
    /// </summary>
    public sealed class PortalGunProjectile : BallisticProjectile
    {
        [SerializeField, HideInInspector] private int configurationVersion = 1;

        public int ConfigurationVersion => configurationVersion;
        public PortalExampleGate LastCreatedPortal { get; private set; }

        protected override void ProcessImpact(
            Collider hit,
            Vector3 point,
            Vector3 normal,
            Vector3 impactVelocity,
            Vector3 direction)
        {
            DenseJigsawPortalBridge bridge =
                Object.FindObjectOfType<DenseJigsawPortalBridge>();
            if (bridge == null)
            {
                return;
            }

            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f
                ? normal.normalized
                : direction.sqrMagnitude > 0.0001f
                    ? -direction.normalized
                    : Vector3.up;
            Vector3 preferredInPlaneUp = Vector3.ProjectOnPlane(
                transform.up,
                safeNormal);
            bridge.TryCreateSpawnCheckpointPortal(
                hit,
                point,
                safeNormal,
                preferredInPlaneUp,
                out PortalExampleGate createdPortal);
            LastCreatedPortal = createdPortal;
        }
    }
}
