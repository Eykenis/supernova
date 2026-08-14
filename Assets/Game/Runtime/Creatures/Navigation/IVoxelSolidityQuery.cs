namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Minimal voxel solidity lookup the navigation graph needs. Keeping this
    /// separate from <see cref="Supernova.Voxels.IVoxelTerrain"/> lets the
    /// pathfinding core stay plain C# so EditMode tests can drive it without a
    /// scene, a terrain component or generated chunks.
    /// </summary>
    public interface IVoxelSolidityQuery
    {
        /// <summary>
        /// Reads whether a voxel is solid. Returns false when the answer is
        /// unknown because the containing chunk has not been generated or the
        /// coordinate is outside the world height. Callers must treat unknown as
        /// impassable rather than as air.
        /// </summary>
        bool TryGetSolid(int voxelX, int voxelY, int voxelZ, out bool isSolid);
    }
}
