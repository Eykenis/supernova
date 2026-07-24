using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>
    /// Copies an animated bone pose in LateUpdate while keeping an authored local offset.
    /// An intermediate anchor avoids modifying the hierarchy of an imported model prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimatedTransformFollower : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetLocalPosition;
        [SerializeField] private Quaternion targetLocalRotation = Quaternion.identity;

        public Transform Target => target;

        public void Bind(Transform animatedTarget, Vector3 worldPosition, Quaternion worldRotation)
        {
            target = animatedTarget;
            if (target == null) return;
            targetLocalPosition = target.InverseTransformPoint(worldPosition);
            targetLocalRotation = Quaternion.Inverse(target.rotation) * worldRotation;
            ApplyPose();
        }

        private void LateUpdate()
        {
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (target == null) return;
            transform.SetPositionAndRotation(
                target.TransformPoint(targetLocalPosition),
                target.rotation * targetLocalRotation);
        }
    }
}
