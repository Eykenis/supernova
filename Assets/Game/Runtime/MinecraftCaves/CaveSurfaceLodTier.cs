using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// One distance band of an instanced brush. Held as a list of pairs rather than
    /// two parallel arrays so a mesh can never desynchronise from its distance.
    /// </summary>
    [System.Serializable]
    public struct CaveSurfaceLodTier
    {
        [SerializeField] private Mesh mesh;
        [Tooltip("Upper distance bound for this tier, in world units.")]
        [SerializeField, Min(0f)] private float maximumDistance;

        public CaveSurfaceLodTier(Mesh tierMesh, float tierMaximumDistance)
        {
            mesh = tierMesh;
            maximumDistance = Mathf.Max(0f, tierMaximumDistance);
        }

        public Mesh Mesh => mesh;
        public float MaximumDistance => Mathf.Max(0f, maximumDistance);
    }
}
