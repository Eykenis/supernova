using UnityEngine;

namespace Supernova.Gameplay
{
    /// <summary>Marks the projectile and muzzle-flash origin on a held weapon prefab.</summary>
    [DisallowMultipleComponent]
    public sealed class WeaponMuzzle : MonoBehaviour
    {
        public Transform Origin => transform;
    }
}
