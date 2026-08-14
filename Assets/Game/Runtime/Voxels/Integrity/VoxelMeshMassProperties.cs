using UnityEngine;

namespace Supernova.Voxels.Integrity
{
    /// <summary>
    /// Volume and centroid of a closed triangle mesh in the mesh's local units.
    /// </summary>
    public readonly struct VoxelMeshMassProperties
    {
        public VoxelMeshMassProperties(float volume, Vector3 centroid)
        {
            Volume = Mathf.Max(0f, volume);
            Centroid = centroid;
        }

        public float Volume { get; }
        public Vector3 Centroid { get; }
    }
}
