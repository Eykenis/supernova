using UnityEngine;

namespace Supernova.Effects
{
    /// <summary>Randomizes each muzzle flash using the same axes as the demo effect.</summary>
    [DisallowMultipleComponent]
    public sealed class MuzzleFlashRandomRotation : MonoBehaviour
    {
        public bool RotateX;
        public bool RotateY;
        public bool RotateZ = true;

        private void OnEnable()
        {
            Vector3 rotation = Vector3.zero;
            if (RotateX) rotation.x = Random.Range(0f, 360f);
            if (RotateY) rotation.y = Random.Range(0f, 360f);
            if (RotateZ) rotation.z = Random.Range(0f, 360f);
            transform.Rotate(rotation, Space.Self);
        }
    }
}
