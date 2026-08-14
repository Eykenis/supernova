using System;
using System.Collections.Generic;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.WorldGeneration
{
    public readonly struct DenseJigsawFeature
    {
        public DenseJigsawFeature(
            JigsawStructureFeatureSettings settings,
            string[] moduleFamilies,
            int[] familyStartPieceIndices)
        {
            Settings = settings;
            ModuleFamilies = moduleFamilies ?? Array.Empty<string>();
            FamilyStartPieceIndices =
                familyStartPieceIndices ?? Array.Empty<int>();
        }

        public JigsawStructureFeatureSettings Settings { get; }
        public IReadOnlyList<string> ModuleFamilies { get; }
        public IReadOnlyList<int> FamilyStartPieceIndices { get; }

        public bool TryGetFamilyStartPieceIndex(
            string familyId,
            out int pieceIndex)
        {
            for (int i = 0; i < FamilyStartPieceIndices.Count; i++)
            {
                int candidate = FamilyStartPieceIndices[i];
                if (candidate >= 0
                    && candidate < ModuleFamilies.Count
                    && string.Equals(
                        ModuleFamilies[candidate],
                        familyId,
                        StringComparison.Ordinal))
                {
                    pieceIndex = candidate;
                    return true;
                }
            }

            pieceIndex = -1;
            return false;
        }
    }

    /// <summary>
    /// Combines all configured jigsaw families into one permissive pool. Every
    /// socket can select a module from every family, making cross-family edges a
    /// normal part of the generated graph.
    /// </summary>
    public static class DenseJigsawFeatureMixer
    {
        private const string MixedPoolId = "dense_mixed";

        public static bool TryBuild(
            DenseJigsawWorldConfiguration configuration,
            out DenseJigsawFeature mixedFeature,
            out string error)
        {
            return TryBuild(
                configuration,
                null,
                out mixedFeature,
                out error);
        }

        public static bool TryBuild(
            DenseJigsawWorldConfiguration configuration,
            JigsawStructureFeatureDefinition additionalFamily,
            out DenseJigsawFeature mixedFeature,
            out string error)
        {
            mixedFeature = default;
            if (configuration == null)
            {
                error = "Dense jigsaw world configuration is missing.";
                return false;
            }
            if (configuration.StoneType == null)
            {
                error = "Dense jigsaw world requires a Stone voxel type.";
                return false;
            }

            VoxelTypeId primaryId = configuration.StoneType.TypeId;
            VoxelTypeId accentId = primaryId;
            bool assignedPalette = false;
            var modules = new List<JigsawPieceSettings>();
            var families = new List<string>();
            var familyStartPieceIndices = new List<int>();
            bool assignedStart = false;

            var sources = new List<JigsawStructureFeatureDefinition>(
                configuration.StructureFamilies.Count + 1);
            for (int i = 0; i < configuration.StructureFamilies.Count; i++)
            {
                sources.Add(configuration.StructureFamilies[i]);
            }
            if (additionalFamily != null && !sources.Contains(additionalFamily))
            {
                sources.Add(additionalFamily);
            }
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                JigsawStructureFeatureDefinition source = sources[sourceIndex];
                if (source == null)
                {
                    continue;
                }
                if (!source.TryCreateSettings(
                    out JigsawStructureFeatureSettings settings,
                    out string sourceError))
                {
                    if (!string.IsNullOrEmpty(sourceError))
                    {
                        Debug.LogWarning(sourceError, source);
                    }
                    continue;
                }

                if (!assignedPalette)
                {
                    primaryId = settings.PrimaryType;
                    accentId = settings.AccentType;
                    assignedPalette = true;
                }

                string familyId = settings.StableId;
                for (int pieceIndex = 0; pieceIndex < settings.Pieces.Count; pieceIndex++)
                {
                    JigsawPieceSettings original = settings.GetPiece(pieceIndex);
                    bool originalStart = original.IsStartPiece;
                    bool isStart = originalStart && !assignedStart;
                    if (originalStart)
                    {
                        familyStartPieceIndices.Add(modules.Count);
                    }
                    string mixedId = familyId + "__" + original.StableId;
                    if (isStart)
                    {
                        assignedStart = true;
                    }

                    JigsawConnectorSettings[] connectors =
                        CloneConnectors(familyId, original);
                    JigsawPieceDefinition.ConnectorPattern pattern =
                        original.ConnectorPattern;
                    if (connectors.Length == 0
                        && pattern == JigsawPieceDefinition.ConnectorPattern.None)
                    {
                        pattern = original.Shape == JigsawPieceDefinition.Shape.Room
                            || original.Shape == JigsawPieceDefinition.Shape.Crossing
                            ? JigsawPieceDefinition.ConnectorPattern.FourWay
                            : JigsawPieceDefinition.ConnectorPattern.ForwardAndSides;
                    }

                    CopyTemplate(
                        original,
                        out float[] templateDensities,
                        out VoxelTypeId[] templateTypes);
                    modules.Add(new JigsawPieceSettings(
                        mixedId,
                        original.DisplayName + " [" + familyId + "]",
                        MixedPoolId,
                        MixedPoolId,
                        isStart,
                        isStart
                            ? 0
                            : originalStart
                                ? 16
                                : Math.Max(8, original.Weight * 4),
                        0,
                        configuration.MaxDepth,
                        0,
                        0,
                        true,
                        0,
                        original.Shape,
                        original.BuildStyle,
                        pattern,
                        original.Decoration,
                        original.MinimumWidth,
                        original.MaximumWidth,
                        original.MinimumDepth,
                        original.MaximumDepth,
                        original.MinimumHeight,
                        original.MaximumHeight,
                        original.MinimumLength,
                        original.MaximumLength,
                        original.Width,
                        original.Height,
                        original.VerticalDelta,
                        1f,
                        original.DescendingChance,
                        original.DecorationSpacing,
                        connectors,
                        CopyProcessors(original),
                        CopySpawnMarkers(original),
                        original.TemplateSize,
                        original.TemplateAnchor,
                        original.TemplateWritesAir,
                        templateDensities,
                        templateTypes,
                        settings.PrimaryType,
                        settings.AccentType));
                    families.Add(familyId);
                }
            }

            if (!assignedStart || modules.Count < 2)
            {
                error = "At least one valid jigsaw family with multiple pieces is required.";
                return false;
            }

            int regionSize = configuration.StructureRegionSizeInColumns;
            bool allowsCrossRegionLayouts =
                regionSize * VoxelColumnChunkData.Width
                <= configuration.LayoutRadius * 2;
            int maximumPieceHeight = 0;
            int maximumBelowAnchor = 0;
            for (int moduleIndex = 0;
                moduleIndex < modules.Count;
                moduleIndex++)
            {
                JigsawPieceSettings module = modules[moduleIndex];
                maximumPieceHeight = Math.Max(
                    maximumPieceHeight,
                    module.MaximumVoxelHeight);
                maximumBelowAnchor = Math.Max(
                    maximumBelowAnchor,
                    module.HasTemplate
                        ? module.TemplateAnchor.y
                        : module.Shape == JigsawPieceDefinition.Shape.Stairs
                            ? module.VerticalDelta
                            : 0);
            }
            int minimumFloor = maximumBelowAnchor + 2;
            int worldHeight = configuration.WorldHeight;
            int maximumFloor = worldHeight
                - maximumPieceHeight
                - 2;
            if (minimumFloor > maximumFloor)
            {
                error = "At least one mixed jigsaw piece is too tall for the "
                    + $"{worldHeight}-voxel "
                    + "Dense world.";
                return false;
            }
            int resolvedFloor = Math.Max(
                minimumFloor,
                Math.Min(maximumFloor, configuration.FloorHeight));
            try
            {
                var settings = new JigsawStructureFeatureSettings(
                    "dense_mixed_region",
                    primaryId,
                    accentId,
                    7919,
                    regionSize,
                    configuration.StructurePlacementChance,
                    resolvedFloor,
                    resolvedFloor,
                    configuration.MaxPiecesPerLayout,
                    configuration.MaxDepth,
                    configuration.LayoutRadius,
                    string.Empty,
                    configuration.LayoutAttempts,
                    configuration.ConnectorPlacementAttempts,
                    0,
                    modules.ToArray(),
                    worldHeight: worldHeight,
                    allowLayoutOutsidePlacementRegion:
                        allowsCrossRegionLayouts,
                    startPieceCandidates: familyStartPieceIndices.ToArray());
                mixedFeature = new DenseJigsawFeature(
                    settings,
                    families.ToArray(),
                    familyStartPieceIndices.ToArray());
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = "Dense mixed jigsaw configuration is invalid: "
                    + exception.Message;
                return false;
            }
        }

        public static bool TryBuildFixedOriginFeature(
            DenseJigsawFeature mixedFeature,
            JigsawStructureFeatureSettings startFamily,
            out JigsawStructureFeatureSettings fixedFeature,
            out string error)
        {
            fixedFeature = default;
            if (string.IsNullOrWhiteSpace(startFamily.StableId)
                || !mixedFeature.TryGetFamilyStartPieceIndex(
                    startFamily.StableId,
                    out int startPieceIndex))
            {
                error = "The fixed-origin start family is not present in the "
                    + "Dense mixed jigsaw pool.";
                return false;
            }

            JigsawStructureFeatureSettings source = mixedFeature.Settings;
            var modules = new JigsawPieceSettings[source.Pieces.Count];
            for (int i = 0; i < modules.Length; i++)
            {
                JigsawPieceSettings module = source.GetPiece(i);
                bool isStart = i == startPieceIndex;
                int weight = isStart
                    ? 0
                    : module.IsStartPiece
                        ? 16
                        : Math.Max(1, module.Weight);
                modules[i] = CloneMixedPiece(module, isStart, weight);
            }

            try
            {
                fixedFeature = new JigsawStructureFeatureSettings(
                    startFamily.StableId + "__dense_fixed_origin",
                    source.PrimaryType,
                    source.AccentType,
                    startFamily.SeedSalt,
                    source.RegionSizeInChunks,
                    1f,
                    source.MinFloorHeight,
                    source.MaxFloorHeight,
                    source.MaxPieces,
                    source.MaxDepth,
                    source.MaxHorizontalDistance,
                    string.Empty,
                    source.LayoutAttempts,
                    source.ConnectorPlacementAttempts,
                    source.CollisionPadding,
                    modules,
                    placementStrategy: JigsawPlacementStrategy.FixedOrigin,
                    worldHeight: source.WorldHeight,
                    allowLayoutOutsidePlacementRegion: true);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error = "Fixed-origin Dense jigsaw configuration is invalid: "
                    + exception.Message;
                return false;
            }
        }

        private static JigsawConnectorSettings[] CloneConnectors(
            string familyId,
            JigsawPieceSettings piece)
        {
            var result = new JigsawConnectorSettings[piece.Connectors.Count];
            for (int i = 0; i < result.Length; i++)
            {
                JigsawConnectorSettings source = piece.Connectors[i];
                bool verticalTransit =
                    source.Face == JigsawConnectorDefinition.Face.Up
                    || source.Face == JigsawConnectorDefinition.Face.Down
                    || source.TargetPoolId.StartsWith(
                        "vertical",
                        StringComparison.Ordinal);
                result[i] = new JigsawConnectorSettings(
                    familyId + "__" + source.StableId,
                    verticalTransit
                        ? source.Role
                        : JigsawConnectorDefinition.Role.Bidirectional,
                    source.Face,
                    source.Joint,
                    verticalTransit
                        ? PrefixMatchName(familyId, source.SocketName)
                        : "*",
                    verticalTransit
                        ? PrefixMatchName(familyId, source.TargetName)
                        : "*",
                    MixedPoolId,
                    MixedPoolId,
                    source.AlongOffset,
                    source.LateralOffset,
                    source.VerticalOffset,
                    verticalTransit ? source.ActivationChance : 1f,
                    source.OpeningWidth,
                    source.OpeningHeight,
                    source.HasTemplatePosition,
                    source.TemplatePosition);
            }
            return result;
        }

        private static string PrefixMatchName(string familyId, string value)
        {
            return value == "*" ? "*" : familyId + "__" + value;
        }

        private static JigsawPieceSettings CloneMixedPiece(
            JigsawPieceSettings source,
            bool isStartPiece,
            int weight)
        {
            CopyTemplate(
                source,
                out float[] templateDensities,
                out VoxelTypeId[] templateTypes);
            return new JigsawPieceSettings(
                source.StableId,
                source.DisplayName,
                source.PoolId,
                source.OutputPoolId,
                isStartPiece,
                weight,
                source.MinimumGraphDepth,
                source.MaximumGraphDepth,
                source.MinimumCount,
                source.MaximumCount,
                source.AllowConsecutive,
                source.RequiredByDepth,
                source.Shape,
                source.BuildStyle,
                source.ConnectorPattern,
                source.Decoration,
                source.MinimumWidth,
                source.MaximumWidth,
                source.MinimumDepth,
                source.MaximumDepth,
                source.MinimumHeight,
                source.MaximumHeight,
                source.MinimumLength,
                source.MaximumLength,
                source.Width,
                source.Height,
                source.VerticalDelta,
                source.SideBranchChance,
                source.DescendingChance,
                source.DecorationSpacing,
                CopyConnectors(source),
                CopyProcessors(source),
                CopySpawnMarkers(source),
                source.TemplateSize,
                source.TemplateAnchor,
                source.TemplateWritesAir,
                templateDensities,
                templateTypes,
                source.PrimaryTypeOverride,
                source.AccentTypeOverride);
        }

        private static JigsawConnectorSettings[] CopyConnectors(
            JigsawPieceSettings piece)
        {
            var result = new JigsawConnectorSettings[piece.Connectors.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = piece.Connectors[i];
            }
            return result;
        }

        private static JigsawProcessorSettings[] CopyProcessors(
            JigsawPieceSettings piece)
        {
            var result = new JigsawProcessorSettings[piece.Processors.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = piece.Processors[i];
            }
            return result;
        }

        private static StructureSpawnMarkerSettings[] CopySpawnMarkers(
            JigsawPieceSettings piece)
        {
            var result =
                new StructureSpawnMarkerSettings[piece.SpawnMarkers.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = piece.SpawnMarkers[i];
            }
            return result;
        }

        private static void CopyTemplate(
            JigsawPieceSettings piece,
            out float[] densities,
            out VoxelTypeId[] types)
        {
            if (!piece.HasTemplate)
            {
                densities = null;
                types = null;
                return;
            }

            int count = piece.TemplateSize.x
                * piece.TemplateSize.y
                * piece.TemplateSize.z;
            densities = new float[count];
            types = new VoxelTypeId[count];
            for (int z = 0; z < piece.TemplateSize.z; z++)
            {
                for (int y = 0; y < piece.TemplateSize.y; y++)
                {
                    for (int x = 0; x < piece.TemplateSize.x; x++)
                    {
                        int index = x + piece.TemplateSize.x
                            * (y + piece.TemplateSize.y * z);
                        VoxelSample sample = piece.GetTemplateSample(x, y, z);
                        densities[index] = sample.Density;
                        types[index] = sample.Type;
                    }
                }
            }
        }
    }
}
