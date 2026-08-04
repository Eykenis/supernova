using System;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>Worker-thread-safe snapshot of one jigsaw piece module.</summary>
    public readonly struct JigsawPieceSettings
    {
        private readonly JigsawConnectorSettings[] connectors;
        private readonly JigsawProcessorSettings[] processors;
        private readonly StructureSpawnMarkerSettings[] spawnMarkers;
        private readonly float[] templateDensities;
        private readonly VoxelTypeId[] templateTypes;

        public JigsawPieceSettings(
            string stableId,
            string displayName,
            string poolId,
            string outputPoolId,
            bool isStartPiece,
            int weight,
            int minimumGraphDepth,
            int maximumGraphDepth,
            int minimumCount,
            int maximumCount,
            bool allowConsecutive,
            int requiredByDepth,
            JigsawPieceDefinition.Shape shape,
            JigsawPieceDefinition.BuildStyle buildStyle,
            JigsawPieceDefinition.ConnectorPattern connectorPattern,
            JigsawPieceDefinition.Decoration decoration,
            int minimumWidth,
            int maximumWidth,
            int minimumDepth,
            int maximumDepth,
            int minimumHeight,
            int maximumHeight,
            int minimumLength,
            int maximumLength,
            int width,
            int height,
            int verticalDelta,
            float sideBranchChance,
            float descendingChance,
            int decorationSpacing,
            JigsawConnectorSettings[] connectorSettings,
            JigsawProcessorSettings[] processorSettings,
            StructureSpawnMarkerSettings[] spawnMarkerSettings,
            Vector3Int templateSize,
            Vector3Int templateAnchor,
            bool templateWritesAir,
            float[] templateDensitySettings,
            VoxelTypeId[] templateTypeSettings)
        {
            if (string.IsNullOrWhiteSpace(stableId))
            {
                throw new ArgumentException(
                    "A jigsaw piece requires a stable ID.",
                    nameof(stableId));
            }

            StableId = stableId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? StableId
                : displayName.Trim();
            PoolId = string.IsNullOrWhiteSpace(poolId) ? "main" : poolId.Trim();
            OutputPoolId = string.IsNullOrWhiteSpace(outputPoolId)
                ? PoolId
                : outputPoolId.Trim();
            IsStartPiece = isStartPiece;
            Weight = Math.Max(0, weight);
            MinimumGraphDepth = Math.Max(0, minimumGraphDepth);
            MaximumGraphDepth = Math.Max(
                MinimumGraphDepth,
                maximumGraphDepth);
            MinimumCount = Math.Max(0, minimumCount);
            MaximumCount = Math.Max(0, maximumCount);
            if (MaximumCount > 0)
            {
                MinimumCount = Math.Min(MinimumCount, MaximumCount);
            }
            AllowConsecutive = allowConsecutive;
            RequiredByDepth = Math.Max(0, requiredByDepth);
            Shape = shape;
            BuildStyle = buildStyle;
            ConnectorPattern = connectorPattern;
            Decoration = decoration;
            MinimumWidth = MakeOdd(Math.Max(3, minimumWidth));
            MaximumWidth = MakeOdd(Math.Max(MinimumWidth, maximumWidth));
            MinimumDepth = MakeOdd(Math.Max(3, minimumDepth));
            MaximumDepth = MakeOdd(Math.Max(MinimumDepth, maximumDepth));
            MinimumHeight = Math.Max(4, minimumHeight);
            MaximumHeight = Math.Max(MinimumHeight, maximumHeight);
            MinimumLength = Math.Max(3, minimumLength);
            MaximumLength = Math.Max(MinimumLength, maximumLength);
            Width = MakeOdd(Math.Max(3, width));
            Height = Math.Max(4, height);
            VerticalDelta = Math.Max(1, verticalDelta);
            SideBranchChance = Clamp01(sideBranchChance);
            DescendingChance = Clamp01(descendingChance);
            DecorationSpacing = Math.Max(2, decorationSpacing);
            connectors = connectorSettings == null
                ? Array.Empty<JigsawConnectorSettings>()
                : (JigsawConnectorSettings[])connectorSettings.Clone();
            processors = processorSettings == null
                ? Array.Empty<JigsawProcessorSettings>()
                : (JigsawProcessorSettings[])processorSettings.Clone();
            spawnMarkers = spawnMarkerSettings == null
                ? Array.Empty<StructureSpawnMarkerSettings>()
                : (StructureSpawnMarkerSettings[])spawnMarkerSettings.Clone();
            int downwardReach = 0;
            int upwardReach = 0;
            for (int i = 0; i < processors.Length; i++)
            {
                downwardReach = Math.Max(
                    downwardReach,
                    processors[i].DownwardReach);
                upwardReach = Math.Max(upwardReach, processors[i].UpwardReach);
            }
            ProcessorDownwardReach = downwardReach;
            ProcessorUpwardReach = upwardReach;
            bool hasTemplateData = templateDensitySettings != null
                || templateTypeSettings != null;
            if (hasTemplateData)
            {
                int sampleCount = templateSize.x
                    * templateSize.y
                    * templateSize.z;
                if (templateSize.x < 1
                    || templateSize.y < 1
                    || templateSize.z < 1
                    || templateDensitySettings == null
                    || templateTypeSettings == null
                    || templateDensitySettings.Length != sampleCount
                    || templateTypeSettings.Length != sampleCount
                    || (uint)templateAnchor.x >= templateSize.x
                    || (uint)templateAnchor.y >= templateSize.y
                    || (uint)templateAnchor.z >= templateSize.z)
                {
                    throw new ArgumentException(
                        $"Piece '{StableId}' has invalid voxel template data.",
                        nameof(templateDensitySettings));
                }
            }
            HasTemplate = hasTemplateData;
            TemplateSize = hasTemplateData ? templateSize : default;
            TemplateAnchor = hasTemplateData ? templateAnchor : default;
            TemplateWritesAir = hasTemplateData && templateWritesAir;
            templateDensities = hasTemplateData
                ? (float[])templateDensitySettings.Clone()
                : null;
            templateTypes = hasTemplateData
                ? (VoxelTypeId[])templateTypeSettings.Clone()
                : null;
            TemplateContentHash = 0UL;
            TemplateContentHash = ComputeTemplateContentHash();
        }

        public string StableId { get; }
        public string DisplayName { get; }
        public string PoolId { get; }
        public string OutputPoolId { get; }
        public bool IsStartPiece { get; }
        public int Weight { get; }
        public int MinimumGraphDepth { get; }
        public int MaximumGraphDepth { get; }
        public int MinimumCount { get; }
        public int MaximumCount { get; }
        public bool AllowConsecutive { get; }
        public int RequiredByDepth { get; }
        public JigsawPieceDefinition.Shape Shape { get; }
        public JigsawPieceDefinition.BuildStyle BuildStyle { get; }
        public JigsawPieceDefinition.ConnectorPattern ConnectorPattern { get; }
        public JigsawPieceDefinition.Decoration Decoration { get; }
        public int MinimumWidth { get; }
        public int MaximumWidth { get; }
        public int MinimumDepth { get; }
        public int MaximumDepth { get; }
        public int MinimumHeight { get; }
        public int MaximumHeight { get; }
        public int MinimumLength { get; }
        public int MaximumLength { get; }
        public int Width { get; }
        public int Height { get; }
        public int VerticalDelta { get; }
        public float SideBranchChance { get; }
        public float DescendingChance { get; }
        public int DecorationSpacing { get; }
        public System.Collections.Generic.IReadOnlyList<JigsawConnectorSettings>
            Connectors => connectors;
        public System.Collections.Generic.IReadOnlyList<JigsawProcessorSettings>
            Processors => processors;
        public bool HasProcessors => processors.Length > 0;
        public System.Collections.Generic.IReadOnlyList<StructureSpawnMarkerSettings>
            SpawnMarkers => spawnMarkers;
        public bool HasSpawnMarkers => spawnMarkers.Length > 0;
        public int ProcessorDownwardReach { get; }
        public int ProcessorUpwardReach { get; }
        public bool HasExplicitConnectors => connectors.Length > 0;
        public bool HasTemplate { get; }
        public Vector3Int TemplateSize { get; }
        public Vector3Int TemplateAnchor { get; }
        public bool TemplateWritesAir { get; }
        public ulong TemplateContentHash { get; }
        public int MaximumVoxelHeight => HasTemplate
            ? TemplateSize.y
            : Math.Max(MaximumHeight, Height + VerticalDelta);

        internal VoxelSample GetTemplateSample(int x, int y, int z)
        {
            if (!HasTemplate
                || (uint)x >= TemplateSize.x
                || (uint)y >= TemplateSize.y
                || (uint)z >= TemplateSize.z)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(x),
                    "Template coordinate is outside the piece field.");
            }
            int index = x + TemplateSize.x * (y + TemplateSize.y * z);
            return new VoxelSample(templateDensities[index], templateTypes[index]);
        }

        public bool IsEligible(
            string poolId,
            int graphDepth,
            int generatedCount = 0,
            int parentModuleIndex = -1,
            int ownModuleIndex = -2)
        {
            return !IsStartPiece
                && Weight > 0
                && string.Equals(PoolId, poolId, StringComparison.Ordinal)
                && graphDepth >= MinimumGraphDepth
                && graphDepth <= MaximumGraphDepth
                && (MaximumCount == 0 || generatedCount < MaximumCount)
                && (AllowConsecutive || parentModuleIndex != ownModuleIndex);
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            return value > 1f ? 1f : value;
        }

        private ulong ComputeTemplateContentHash()
        {
            if (!HasTemplate)
            {
                return 0UL;
            }
            ulong hash = 1469598103934665603UL;
            AddHash(ref hash, TemplateSize.x);
            AddHash(ref hash, TemplateSize.y);
            AddHash(ref hash, TemplateSize.z);
            AddHash(ref hash, TemplateAnchor.x);
            AddHash(ref hash, TemplateAnchor.y);
            AddHash(ref hash, TemplateAnchor.z);
            AddHash(ref hash, TemplateWritesAir ? 1 : 0);
            for (int i = 0; i < templateDensities.Length; i++)
            {
                AddHash(ref hash, templateDensities[i].GetHashCode());
                AddHash(ref hash, templateTypes[i].Value);
            }
            return hash;
        }

        private static void AddHash(ref ulong hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 1099511628211UL;
            }
        }
    }
}
