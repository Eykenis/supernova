namespace Supernova.MinecraftCaves.Creatures.Navigation
{
    /// <summary>
    /// Traversability classification of a candidate foot node, mirroring the
    /// subset of Minecraft's PathNodeType that this project needs. Positions
    /// that cannot be entered at all are reported by a failed classification
    /// instead of an enum member, so every value here is enterable.
    /// </summary>
    public enum PathNodeType
    {
        /// <summary>The node has solid support directly below the foot.</summary>
        Walkable,

        /// <summary>
        /// The node has clearance for the body but no support below, so a
        /// successor must keep scanning downwards for a landing surface.
        /// </summary>
        Open,
    }
}
