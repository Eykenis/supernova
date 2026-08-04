using System;
using System.Collections.Generic;
using Supernova.Voxels;

namespace Supernova.MinecraftCaves
{
    /// <summary>Static graph checks shared by the inspector, tests, and tooling.</summary>
    public static class JigsawStructureValidator
    {
        public enum Severity
        {
            Warning,
            Error,
        }

        public readonly struct Issue
        {
            public Issue(Severity severity, string message)
            {
                Severity = severity;
                Message = message;
            }

            public Severity Severity { get; }
            public string Message { get; }
        }

        public static IReadOnlyList<Issue> Validate(
            JigsawStructureFeatureSettings feature)
        {
            var issues = new List<Issue>();
            JigsawPieceSettings start = feature.GetPiece(feature.StartPieceIndex);
            if (!HasAnyOutput(start))
            {
                issues.Add(new Issue(
                    Severity.Error,
                    $"Start piece '{start.StableId}' has no output socket."));
            }
            ValidatePlacement(feature, issues);

            for (int pieceIndex = 0;
                pieceIndex < feature.Pieces.Count;
                pieceIndex++)
            {
                JigsawPieceSettings piece = feature.GetPiece(pieceIndex);
                if (piece.HasTemplate && !piece.HasExplicitConnectors)
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Template piece '{piece.StableId}' needs sockets, either authored on the piece or as markers inside its template."));
                }
                if (piece.MinimumCount > 0
                    && (piece.MinimumGraphDepth > feature.MaxDepth
                        || piece.MaximumGraphDepth < 1))
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Required piece '{piece.StableId}' cannot appear within maxDepth."));
                }
                if (piece.HasExplicitConnectors)
                {
                    ValidateExplicitOutputs(feature, piece, issues);
                }
                else if (piece.ConnectorPattern
                    != JigsawPieceDefinition.ConnectorPattern.None)
                {
                    ValidatePool(
                        feature,
                        piece.StableId,
                        piece.OutputPoolId,
                        "*",
                        "*",
                        string.Empty,
                        issues);
                }
                ValidateProcessors(feature, piece, issues);
                ValidateSpawnMarkers(piece, issues);
            }
            return issues;
        }

        private static void ValidateSpawnMarkers(
            JigsawPieceSettings piece,
            List<Issue> issues)
        {
            var markerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < piece.SpawnMarkers.Count; i++)
            {
                StructureSpawnMarkerSettings marker = piece.SpawnMarkers[i];
                if (!markerIds.Add(marker.StableId))
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Piece '{piece.StableId}' has duplicate spawn marker ID '{marker.StableId}'."));
                }
                if (!marker.IsConfigured)
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Spawn marker '{piece.StableId}/{marker.StableId}' has no {marker.Kind} prefab assigned and will never spawn."));
                }
                if (marker.SpawnChance <= 0f)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Spawn marker '{piece.StableId}/{marker.StableId}' has a zero chance and never fires."));
                }
                if (marker.Count > 1 && marker.ScatterRadiusInVoxels <= 0f)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Spawn marker '{piece.StableId}/{marker.StableId}' places {marker.Count} instances with no scatter radius, so they will stack on one voxel."));
                }
                if (marker.SnapToFloor && marker.FloorSearchDistance == 0)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Spawn marker '{piece.StableId}/{marker.StableId}' snaps to floor with a zero search distance, so it only fires when the marker already sits on one."));
                }
            }
        }

        private static void ValidatePlacement(
            JigsawStructureFeatureSettings feature,
            List<Issue> issues)
        {
            if (feature.PlacementStrategy
                == JigsawPlacementStrategy.ConcentricRings)
            {
                int outerRadius = feature.RingCount
                    * feature.RingDistanceInChunks
                    * VoxelColumnChunkData.Width;
                if (feature.RingStructureCount < feature.RingCount)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Structure '{feature.StableId}' has fewer ring candidates ({feature.RingStructureCount}) than rings ({feature.RingCount}), so the outer rings stay empty."));
                }
                if (outerRadius <= feature.MaxHorizontalDistance)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Structure '{feature.StableId}' places all rings within its own layout radius, so candidates will overlap."));
                }
            }
            else if (feature.RegionSizeInChunks * VoxelColumnChunkData.Width
                <= feature.MaxHorizontalDistance * 2)
            {
                issues.Add(new Issue(
                    Severity.Error,
                    $"Structure '{feature.StableId}' has a placement region narrower than twice its layout radius."));
            }
        }

        private static void ValidateProcessors(
            JigsawStructureFeatureSettings feature,
            JigsawPieceSettings piece,
            List<Issue> issues)
        {
            var processorIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < piece.Processors.Count; i++)
            {
                JigsawProcessorSettings processor = piece.Processors[i];
                if (!processorIds.Add(processor.StableId))
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Piece '{piece.StableId}' has duplicate processor ID '{processor.StableId}'."));
                }
                if (processor.Chance <= 0f)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Processor '{piece.StableId}/{processor.StableId}' has a zero chance and never runs."));
                }
                if (processor.Kind
                        == JigsawProcessorDefinition.Kind.Weathering
                    && feature.AccentType == feature.PrimaryType)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Processor '{piece.StableId}/{processor.StableId}' weathers into the primary type, so it has no visible effect."));
                }
                if (processor.DownwardReach > 0
                    && feature.MinFloorHeight - processor.DownwardReach <= 1)
                {
                    issues.Add(new Issue(
                        Severity.Warning,
                        $"Processor '{piece.StableId}/{processor.StableId}' can reach the world floor and will be truncated."));
                }
            }
        }

        private static bool HasAnyOutput(JigsawPieceSettings piece)
        {
            if (!piece.HasExplicitConnectors)
            {
                return piece.ConnectorPattern
                    != JigsawPieceDefinition.ConnectorPattern.None;
            }
            for (int i = 0; i < piece.Connectors.Count; i++)
            {
                if (piece.Connectors[i].CanEmitOutput
                    && piece.Connectors[i].ActivationChance > 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ValidateExplicitOutputs(
            JigsawStructureFeatureSettings feature,
            JigsawPieceSettings piece,
            List<Issue> issues)
        {
            for (int i = 0; i < piece.Connectors.Count; i++)
            {
                JigsawConnectorSettings connector = piece.Connectors[i];
                if (!connector.CanEmitOutput || connector.ActivationChance <= 0f)
                {
                    continue;
                }
                ValidatePool(
                    feature,
                    piece.StableId + "/" + connector.StableId,
                    connector.TargetPoolId,
                    connector.SocketName,
                    connector.TargetName,
                    connector.FallbackPoolId,
                    issues);
            }
        }

        private static void ValidatePool(
            JigsawStructureFeatureSettings feature,
            string source,
            string poolId,
            string socketName,
            string targetName,
            string fallbackPoolId,
            List<Issue> issues)
        {
            if (PoolHasMatch(feature, poolId, socketName, targetName)
                || (fallbackPoolId.Length > 0
                    && PoolHasMatch(
                        feature,
                        fallbackPoolId,
                        socketName,
                        targetName)))
            {
                return;
            }
            issues.Add(new Issue(
                Severity.Warning,
                $"Output '{source}' targets pool '{poolId}', but no compatible piece can consume it."));
        }

        private static bool PoolHasMatch(
            JigsawStructureFeatureSettings feature,
            string poolId,
            string socketName,
            string targetName)
        {
            for (int i = 0; i < feature.Pieces.Count; i++)
            {
                JigsawPieceSettings piece = feature.GetPiece(i);
                if (piece.IsStartPiece
                    || piece.Weight <= 0
                    || !string.Equals(piece.PoolId, poolId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (!piece.HasExplicitConnectors)
                {
                    return true;
                }
                for (int connectorIndex = 0;
                    connectorIndex < piece.Connectors.Count;
                    connectorIndex++)
                {
                    JigsawConnectorSettings input =
                        piece.Connectors[connectorIndex];
                    if (input.CanAcceptInput
                        && NamesMatch(targetName, input.SocketName)
                        && NamesMatch(input.TargetName, socketName))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool NamesMatch(string expected, string actual)
        {
            return expected == "*" || actual == "*"
                || string.Equals(expected, actual, StringComparison.Ordinal);
        }
    }
}
