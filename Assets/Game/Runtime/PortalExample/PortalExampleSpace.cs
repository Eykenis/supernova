using UnityEngine;

namespace Supernova.PortalExample
{
    /// <summary>
    /// Pure spatial mapping shared by rendering, characters, and rigidbodies.
    /// The half turn converts entering the front of one portal into leaving the
    /// front of its partner.
    /// </summary>
    public static class PortalExampleSpace
    {
        private static readonly Matrix4x4 HalfTurn =
            Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));

        public static Matrix4x4 BuildMapping(
            Transform source,
            Transform destination)
        {
            Matrix4x4 sourcePose = Matrix4x4.TRS(
                source.position,
                source.rotation,
                Vector3.one);
            Matrix4x4 destinationPose = Matrix4x4.TRS(
                destination.position,
                destination.rotation,
                Vector3.one);
            return destinationPose
                * HalfTurn
                * sourcePose.inverse;
        }

        public static Quaternion MapRotation(
            Matrix4x4 mapping,
            Quaternion rotation)
        {
            Vector3 forward = mapping.MultiplyVector(rotation * Vector3.forward);
            Vector3 up = mapping.MultiplyVector(rotation * Vector3.up);
            return Quaternion.LookRotation(forward, up);
        }
    }
}
