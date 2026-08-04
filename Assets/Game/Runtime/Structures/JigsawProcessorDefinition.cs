using System;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// A landing-time processor applied after a piece has been rasterized.
    /// Processors never participate in layout collision, so a pillar reaching
    /// far below a bridge cannot cause the bridge itself to be rejected.
    /// </summary>
    [Serializable]
    public sealed class JigsawProcessorDefinition
    {
        public enum Kind
        {
            /// <summary>Extends columns downwards until existing terrain is hit.</summary>
            SupportToGround,

            /// <summary>Fills a fixed slab below the piece floor.</summary>
            FoundationFill,

            /// <summary>Carves headroom above the piece so terrain cannot cap it.</summary>
            ClearAbove,

            /// <summary>Randomly substitutes shell voxels for a weathered palette.</summary>
            Weathering,
        }

        public enum Palette
        {
            Primary,
            Accent,
        }

        [SerializeField] private string stableId = "processor";
        [SerializeField] private Kind kind = Kind.SupportToGround;
        [SerializeField] private Palette palette = Palette.Primary;

        [Tooltip("Maximum voxels the processor may travel vertically.")]
        [SerializeField, Min(1)] private int maximumDistance = 24;
        [Tooltip("Inset from the piece bounds. Zero covers the whole footprint.")]
        [SerializeField, Min(0)] private int inset;
        [Tooltip("Chance a single eligible voxel is affected.")]
        [SerializeField, Range(0f, 1f)] private float chance = 1f;
        [Tooltip("Only place support columns under the piece perimeter.")]
        [SerializeField] private bool perimeterOnly = true;

        public string StableId => stableId;
        public Kind ProcessorKind => kind;

        public void Configure(
            string processorId,
            Kind processorKind,
            int distance,
            float applyChance = 1f,
            Palette voxelPalette = Palette.Primary,
            int footprintInset = 0,
            bool restrictToPerimeter = true)
        {
            stableId = processorId;
            kind = processorKind;
            maximumDistance = distance;
            chance = applyChance;
            palette = voxelPalette;
            inset = footprintInset;
            perimeterOnly = restrictToPerimeter;
            ClampConfiguration();
        }

        internal JigsawProcessorSettings CreateSettings()
        {
            ClampConfiguration();
            return new JigsawProcessorSettings(
                stableId,
                kind,
                palette,
                maximumDistance,
                inset,
                chance,
                perimeterOnly);
        }

        internal void ClampConfiguration()
        {
            stableId = string.IsNullOrWhiteSpace(stableId)
                ? "processor"
                : stableId.Trim();
            maximumDistance = Mathf.Max(1, maximumDistance);
            inset = Mathf.Max(0, inset);
            chance = Mathf.Clamp01(chance);
        }
    }

    /// <summary>Worker-thread-safe snapshot of one authored processor.</summary>
    public readonly struct JigsawProcessorSettings
    {
        public JigsawProcessorSettings(
            string stableId,
            JigsawProcessorDefinition.Kind kind,
            JigsawProcessorDefinition.Palette palette,
            int maximumDistance,
            int inset,
            float chance,
            bool perimeterOnly)
        {
            StableId = string.IsNullOrWhiteSpace(stableId)
                ? "processor"
                : stableId.Trim();
            Kind = kind;
            Palette = palette;
            MaximumDistance = Math.Max(1, maximumDistance);
            Inset = Math.Max(0, inset);
            Chance = chance < 0f ? 0f : chance > 1f ? 1f : chance;
            PerimeterOnly = perimeterOnly;
            Salt = ComputeStableSalt(StableId, kind);
        }

        /// <summary>
        /// Deterministic per-processor salt. Derived from the authored ID rather
        /// than <see cref="string.GetHashCode"/>, which is not stable across runs.
        /// </summary>
        public int Salt { get; }

        public string StableId { get; }
        public JigsawProcessorDefinition.Kind Kind { get; }
        public JigsawProcessorDefinition.Palette Palette { get; }
        public int MaximumDistance { get; }
        public int Inset { get; }
        public float Chance { get; }
        public bool PerimeterOnly { get; }

        /// <summary>
        /// Extra voxels this processor may write below a piece floor. The layout
        /// stage uses it only for world-bounds headroom, never for collision.
        /// </summary>
        public int DownwardReach => Kind switch
        {
            JigsawProcessorDefinition.Kind.SupportToGround => MaximumDistance,
            JigsawProcessorDefinition.Kind.FoundationFill => MaximumDistance,
            _ => 0,
        };

        /// <summary>Extra voxels this processor may write above a piece ceiling.</summary>
        public int UpwardReach => Kind == JigsawProcessorDefinition.Kind.ClearAbove
            ? MaximumDistance
            : 0;

        public VoxelTypeId ResolveType(
            VoxelTypeId primaryType,
            VoxelTypeId accentType)
        {
            return Palette == JigsawProcessorDefinition.Palette.Accent
                ? accentType
                : primaryType;
        }

        private static int ComputeStableSalt(
            string stableId,
            JigsawProcessorDefinition.Kind kind)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < stableId.Length; i++)
                {
                    hash ^= stableId[i];
                    hash *= 16777619u;
                }
                hash ^= (uint)kind;
                hash *= 16777619u;
                return (int)hash;
            }
        }
    }
}
