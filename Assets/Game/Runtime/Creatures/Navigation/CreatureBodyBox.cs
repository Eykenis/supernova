using UnityEngine;

namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Creature bounding box expressed in whole voxels. The width is forced odd so
    /// the box stays centred on the foot node's column.
    /// </summary>
    public readonly struct CreatureBodyBox
    {
        public CreatureBodyBox(int widthInVoxels, int heightInVoxels)
        {
            WidthInVoxels = Mathf.Max(1, widthInVoxels);
            if (WidthInVoxels % 2 == 0)
            {
                WidthInVoxels++;
            }
            HeightInVoxels = Mathf.Max(1, heightInVoxels);
        }

        public int WidthInVoxels { get; }
        public int HeightInVoxels { get; }

        /// <summary>Voxels the box extends sideways from its centre column.</summary>
        public int HorizontalRadius => WidthInVoxels / 2;

        /// <summary>
        /// Sizes the box from metric dimensions.
        /// <para>
        /// Minecraft uses floor(size + 1) because one voxel there is one whole
        /// block, so a creature is never much smaller than a cell. Here a voxel is a
        /// fraction of a creature and that formula inflates every body: a 0.68 m
        /// skeleton would demand a three-cell (1.26 m) wide, five-cell (2.10 m) tall
        /// cavity, which caves rarely provide, so no node classifies and no path
        /// exists at all.
        /// </para>
        /// <para>
        /// A body up to two cells wide therefore plans through a single column, and
        /// the height is rounded to the nearest cell rather than up. Width clearance
        /// is left to physics: the collider slides along a wall it brushes, and the
        /// stuck watchdog recovers if it genuinely wedges. Height still gets a real
        /// check so a creature is never routed under a ceiling it cannot fit below.
        /// </para>
        /// </summary>
        public static CreatureBodyBox FromMetricSize(
            float widthInMeters,
            float heightInMeters,
            float voxelSize)
        {
            float scale = Mathf.Max(0.0001f, voxelSize);
            float widthInVoxels = Mathf.Max(0f, widthInMeters) / scale;
            // Stay single-column until the body is clearly broader than the walking
            // corridor. A wider box has to find its whole footprint clear, so on a
            // slope the uphill terrain intrudes and every slope-adjacent node is
            // rejected, leaving the creature unable to climb where a slimmer body
            // walks up freely. Only genuinely large bodies (past ~3 cells) widen.
            int radius = Mathf.Max(0, Mathf.CeilToInt(widthInVoxels * 0.5f - 1.5f));
            return new CreatureBodyBox(
                radius * 2 + 1,
                Mathf.RoundToInt(Mathf.Max(0f, heightInMeters) / scale));
        }

        public override string ToString() =>
            $"CreatureBodyBox({WidthInVoxels}x{HeightInVoxels}x{WidthInVoxels})";
    }
}
