using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    public readonly struct CardinalCaveTarget
    {
        public CardinalCaveTarget(
            Vector3Int airVoxel,
            Vector3Int chunk,
            Vector3Int chunkDirection,
            float squaredDistance)
        {
            AirVoxel = airVoxel;
            Chunk = chunk;
            ChunkDirection = chunkDirection;
            SquaredDistance = squaredDistance;
        }

        public Vector3Int AirVoxel { get; }
        public Vector3Int Chunk { get; }
        public Vector3Int ChunkDirection { get; }
        public float SquaredDistance { get; }
    }

    /// <summary>
    /// Finds a standable cave point in the four horizontal chunk columns directly
    /// adjacent to the spawn chunk. The vertical layer may vary by one chunk so a
    /// cave floor just above or below a chunk seam remains discoverable; diagonal
    /// horizontal chunks are deliberately excluded.
    /// </summary>
    public static class CardinalCaveConnectionSearch
    {
        private const int VerticalChunkSearchRadius = 1;

        private static readonly Vector3Int[] CardinalChunkDirections =
        {
            Vector3Int.right,
            Vector3Int.left,
            new Vector3Int(0, 0, 1),
            new Vector3Int(0, 0, -1),
        };

        public static bool TryFindNearest(
            InfiniteVoxelWorld world,
            Vector3Int spawnAirVoxel,
            float isoLevel,
            int headroomSamples,
            int clearanceRadiusSamples,
            int minimumHorizontalDistanceSamples,
            out CardinalCaveTarget target)
        {
            target = default;
            if (world == null)
            {
                return false;
            }

            headroomSamples = Mathf.Max(2, headroomSamples);
            clearanceRadiusSamples = Mathf.Max(0, clearanceRadiusSamples);
            minimumHorizontalDistanceSamples = Mathf.Max(
                0,
                minimumHorizontalDistanceSamples);
            float minimumHorizontalDistanceSquared =
                minimumHorizontalDistanceSamples
                * minimumHorizontalDistanceSamples;
            Vector3Int spawnChunk = InfiniteVoxelWorld.WorldToChunk(
                spawnAirVoxel.x,
                spawnAirVoxel.y,
                spawnAirVoxel.z);
            bool found = false;
            float bestSquaredDistance = float.PositiveInfinity;

            for (int directionIndex = 0;
                directionIndex < CardinalChunkDirections.Length;
                directionIndex++)
            {
                Vector3Int chunkDirection =
                    CardinalChunkDirections[directionIndex];
                for (int verticalOffset = -VerticalChunkSearchRadius;
                    verticalOffset <= VerticalChunkSearchRadius;
                    verticalOffset++)
                {
                    Vector3Int chunkCoordinate = spawnChunk
                        + chunkDirection
                        + Vector3Int.up * verticalOffset;
                    if (!world.TryGetChunk(chunkCoordinate, out _))
                    {
                        continue;
                    }

                    Vector3Int chunkOrigin = chunkCoordinate * VoxelVolume.Size;
                    int minimumY = chunkOrigin.y + 1;
                    int maximumY = chunkOrigin.y + VoxelVolume.Size
                        - headroomSamples;
                    int minimumX = chunkOrigin.x + clearanceRadiusSamples;
                    int maximumX = chunkOrigin.x + VoxelVolume.Size
                        - clearanceRadiusSamples - 1;
                    int minimumZ = chunkOrigin.z + clearanceRadiusSamples;
                    int maximumZ = chunkOrigin.z + VoxelVolume.Size
                        - clearanceRadiusSamples - 1;

                    for (int z = minimumZ; z <= maximumZ; z++)
                    {
                        for (int y = minimumY; y <= maximumY; y++)
                        {
                            for (int x = minimumX; x <= maximumX; x++)
                            {
                                var candidate = new Vector3Int(x, y, z);
                                if (!IsStandableCavePoint(
                                        world,
                                        candidate,
                                        isoLevel,
                                        headroomSamples,
                                        clearanceRadiusSamples))
                                {
                                    continue;
                                }

                                Vector3 offset = candidate - spawnAirVoxel;
                                float horizontalDistanceSquared =
                                    offset.x * offset.x + offset.z * offset.z;
                                float horizontalDistance = Mathf.Sqrt(
                                    horizontalDistanceSquared);
                                if (horizontalDistanceSquared
                                        < minimumHorizontalDistanceSquared
                                    || horizontalDistance <= Mathf.Epsilon
                                    || Mathf.Abs(offset.y)
                                        > Mathf.Max(
                                            4f,
                                            horizontalDistance * 0.5f))
                                {
                                    continue;
                                }

                                float squaredDistance = offset.sqrMagnitude;
                                if (squaredDistance >= bestSquaredDistance)
                                {
                                    continue;
                                }

                                bestSquaredDistance = squaredDistance;
                                target = new CardinalCaveTarget(
                                    candidate,
                                    chunkCoordinate,
                                    chunkDirection,
                                    squaredDistance);
                                found = true;
                            }
                        }
                    }
                }
            }

            return found;
        }

        private static bool IsStandableCavePoint(
            InfiniteVoxelWorld world,
            Vector3Int candidate,
            float isoLevel,
            int headroomSamples,
            int clearanceRadiusSamples)
        {
            if (!world.TryGetDensity(
                    candidate.x,
                    candidate.y - 1,
                    candidate.z,
                    out float groundDensity)
                || groundDensity < isoLevel)
            {
                return false;
            }

            for (int zOffset = -clearanceRadiusSamples;
                zOffset <= clearanceRadiusSamples;
                zOffset++)
            {
                for (int xOffset = -clearanceRadiusSamples;
                    xOffset <= clearanceRadiusSamples;
                    xOffset++)
                {
                    for (int yOffset = 0;
                        yOffset < headroomSamples;
                        yOffset++)
                    {
                        if (!world.TryGetDensity(
                                candidate.x + xOffset,
                                candidate.y + yOffset,
                                candidate.z + zOffset,
                                out float density)
                            || density >= isoLevel)
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }
    }
}
