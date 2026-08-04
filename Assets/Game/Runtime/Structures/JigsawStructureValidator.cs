using System;
using System.Collections.Generic;

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

            for (int pieceIndex = 0;
                pieceIndex < feature.Pieces.Count;
                pieceIndex++)
            {
                JigsawPieceSettings piece = feature.GetPiece(pieceIndex);
                if (piece.HasTemplate && !piece.HasExplicitConnectors)
                {
                    issues.Add(new Issue(
                        Severity.Error,
                        $"Template piece '{piece.StableId}' requires explicit sockets for deterministic alignment."));
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
            }
            return issues;
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
