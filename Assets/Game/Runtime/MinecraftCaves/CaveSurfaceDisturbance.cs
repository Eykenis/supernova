using System.Collections.Generic;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Identifies surfaces created by gameplay excavation. Marching-cubes faces
    /// lie between solid and air samples, so a one-sample neighbourhood reliably
    /// catches the new cut while also clearing vegetation from its immediate rim.
    /// </summary>
    public static class CaveSurfaceDisturbance
    {
        public static bool IsNearCarvedVoxel(
            Vector3 worldVoxelPosition,
            ISet<Vector3Int> carvedVoxels)
        {
            if (carvedVoxels == null || carvedVoxels.Count == 0)
            {
                return false;
            }

            var centre = new Vector3Int(
                Mathf.RoundToInt(worldVoxelPosition.x),
                Mathf.RoundToInt(worldVoxelPosition.y),
                Mathf.RoundToInt(worldVoxelPosition.z));
            for (int z = -1; z <= 1; z++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        if (carvedVoxels.Contains(
                            centre + new Vector3Int(x, y, z)))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }
}
