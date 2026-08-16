using Supernova.Voxels;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Adapts a live voxel terrain to <see cref="IVoxelSolidityQuery"/>. Mirrors
    /// the solidity rule the rest of the world uses, where a sample counts as
    /// solid when its density reaches the iso level and its type is not air.
    /// </summary>
    public sealed class VoxelTerrainSolidityQuery : IVoxelSolidityQuery
    {
        private readonly IVoxelTerrain terrain;

        public VoxelTerrainSolidityQuery(IVoxelTerrain terrain)
        {
            this.terrain = terrain;
        }

        public bool TryGetSolid(int voxelX, int voxelY, int voxelZ, out bool isSolid)
        {
            isSolid = false;
            InfiniteVoxelWorld world = terrain?.World;
            if (world == null)
            {
                return false;
            }

            // A missing chunk reports failure so the graph rejects the node
            // instead of walking a creature into ungenerated terrain.
            if (!world.TryGetSample(voxelX, voxelY, voxelZ, out VoxelSample sample))
            {
                return false;
            }

            isSolid = sample.IsSolid(terrain.IsoLevel);
            return true;
        }
    }
}
