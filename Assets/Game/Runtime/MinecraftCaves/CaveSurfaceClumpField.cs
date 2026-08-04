using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Attributes shared by every placement inside one clump cell. Grass scattered
    /// with independent random values reads as a synthetic carpet; sharing height
    /// and facing across a cell produces the patches of tall and short grass that
    /// make a field look grown rather than sprinkled.
    /// <para>
    /// Tint is deliberately absent: the blade shader derives it from the same cell
    /// quantisation on the GPU (<c>GrassClumpHash</c>), so it costs no per-instance
    /// transport. Keep the two cell sizes in sync or the colour patches will cut
    /// across the height and facing patches.
    /// </para>
    /// </summary>
    public readonly struct CaveSurfaceClumpAttributes
    {
        public CaveSurfaceClumpAttributes(
            float heightMultiplier,
            float widthMultiplier,
            float yawBiasDegrees)
        {
            HeightMultiplier = heightMultiplier;
            WidthMultiplier = widthMultiplier;
            YawBiasDegrees = yawBiasDegrees;
        }

        public float HeightMultiplier { get; }
        public float WidthMultiplier { get; }
        public float YawBiasDegrees { get; }

        public static CaveSurfaceClumpAttributes Neutral =>
            new CaveSurfaceClumpAttributes(1f, 1f, 0f);
    }

    /// <summary>
    /// Deterministic clump field sampled per placement.
    /// <para>
    /// Two invariants matter and are covered by tests:
    /// </para>
    /// <list type="number">
    /// <item>Sampling is keyed on <b>world</b> voxel coordinates, never on section
    /// local ones, so a clump straddling a chunk boundary resolves to the same cell
    /// from either side and no seam appears where sections meet.</item>
    /// <item>The hash is self-contained and draws nothing from the placement
    /// random stream. Consuming that stream here would shift every subsequent
    /// value and silently redistribute all existing surface content.</item>
    /// </list>
    /// </summary>
    public static class CaveSurfaceClumpField
    {
        private const float MinimumCellSize = 0.05f;
        private const double Inverse53BitRange = 1.0 / 9007199254740992.0;

        /// <summary>
        /// Samples the clump covering <paramref name="worldVoxelPosition"/>.
        /// Ranges are supplied by the brush so a biome can hold coarse or fine
        /// clumping without a second asset.
        /// </summary>
        public static CaveSurfaceClumpAttributes Sample(
            Vector3 worldVoxelPosition,
            float horizontalCellSize,
            float verticalCellSize,
            Vector2 heightRange,
            Vector2 widthRange,
            float yawBiasDegrees,
            int worldSeed,
            int seedSalt)
        {
            Vector3Int cell = GetCell(
                worldVoxelPosition,
                horizontalCellSize,
                verticalCellSize);
            ulong seed = BuildSeed(cell, worldSeed, seedSalt);

            // One draw per attribute, in a fixed order, so adding an attribute
            // later only affects values after it.
            float height = Mathf.Lerp(
                heightRange.x,
                heightRange.y,
                NextUnit(ref seed));
            float width = Mathf.Lerp(
                widthRange.x,
                widthRange.y,
                NextUnit(ref seed));
            float yaw = (NextUnit(ref seed) * 2f - 1f) * yawBiasDegrees;
            return new CaveSurfaceClumpAttributes(height, width, yaw);
        }

        /// <summary>
        /// Quantises a world voxel position to its clump cell. Vertical cells are
        /// separate because cave floors stack, and a shared column of clumps would
        /// tie unrelated ledges together.
        /// </summary>
        public static Vector3Int GetCell(
            Vector3 worldVoxelPosition,
            float horizontalCellSize,
            float verticalCellSize)
        {
            float horizontal = Mathf.Max(MinimumCellSize, horizontalCellSize);
            float vertical = Mathf.Max(MinimumCellSize, verticalCellSize);
            return new Vector3Int(
                Mathf.FloorToInt(worldVoxelPosition.x / horizontal),
                Mathf.FloorToInt(worldVoxelPosition.y / vertical),
                Mathf.FloorToInt(worldVoxelPosition.z / horizontal));
        }

        private static ulong BuildSeed(Vector3Int cell, int worldSeed, int seedSalt)
        {
            ulong value = (uint)worldSeed;
            value ^= (ulong)(uint)seedSalt * 0x9E3779B185EBCA87UL;
            value ^= (ulong)(uint)cell.x * 0xC2B2AE3D27D4EB4FUL;
            value ^= (ulong)(uint)cell.y * 0x165667B19E3779F9UL;
            value ^= (ulong)(uint)cell.z * 0x85EBCA77C2B2AE63UL;
            return Mix(value);
        }

        private static float NextUnit(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            return (float)((Mix(state) >> 11) * Inverse53BitRange);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
