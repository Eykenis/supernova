using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Editable module used by a jigsaw structure. New structure families are
    /// assembled by composing these modules instead of adding generator types.
    /// </summary>
    [Serializable]
    public sealed class JigsawPieceDefinition
    {
        public enum Shape
        {
            Room,
            Corridor,
            Crossing,
            Stairs,
        }

        public enum BuildStyle
        {
            Excavated,
            Masonry,
        }

        public enum ConnectorPattern
        {
            None,
            Forward,
            ForwardAndSides,
            ThreeWay,
            FourWay,
        }

        public enum Decoration
        {
            None,
            SupportFrames,
            LibraryShelves,
            Pillars,
            PrisonCells,
            PortalFrame,
        }

        [Header("Identity and Pool")]
        [SerializeField] private string stableId = "piece";
        [SerializeField] private string displayName = "Piece";
        [SerializeField] private string poolId = "main";
        [SerializeField] private string outputPoolId = "main";
        [SerializeField] private bool startPiece;
        [SerializeField, Min(0)] private int weight = 1;
        [SerializeField, Min(0)] private int minimumGraphDepth;
        [SerializeField, Min(0)] private int maximumGraphDepth = 12;

        [Header("Selection Constraints")]
        [Tooltip("Minimum desired count. Layout retries prefer results satisfying it.")]
        [SerializeField, Min(0)] private int minimumCount;
        [Tooltip("Zero means unlimited.")]
        [SerializeField, Min(0)] private int maximumCount;
        [SerializeField] private bool allowConsecutive = true;
        [Tooltip("Depth at which an unmet minimum count becomes selection priority. Zero uses the structure depth limit.")]
        [SerializeField, Min(0)] private int requiredByDepth;

        [Header("Geometry and Behaviour")]
        [SerializeField] private Shape shape;
        [SerializeField] private BuildStyle buildStyle;
        [SerializeField] private ConnectorPattern connectorPattern;
        [SerializeField] private Decoration decoration;

        [Header("Optional Voxel Template")]
        [Tooltip("When assigned, this authored voxel field replaces procedural rasterization and dimensions.")]
        [SerializeField] private VoxelStructureAsset voxelTemplate;
        [SerializeField] private bool templateWritesAir = true;

        [Header("Box Dimensions")]
        [SerializeField, Min(3)] private int minimumWidth = 7;
        [SerializeField, Min(3)] private int maximumWidth = 7;
        [SerializeField, Min(3)] private int minimumDepth = 7;
        [SerializeField, Min(3)] private int maximumDepth = 7;
        [SerializeField, Min(4)] private int minimumHeight = 5;
        [SerializeField, Min(4)] private int maximumHeight = 5;

        [Header("Passage Dimensions")]
        [SerializeField, Min(3)] private int minimumLength = 8;
        [SerializeField, Min(3)] private int maximumLength = 12;
        [SerializeField, Min(3)] private int width = 3;
        [SerializeField, Min(4)] private int height = 4;
        [SerializeField, Min(1)] private int verticalDelta = 4;

        [Header("Outgoing Connections")]
        [SerializeField, Range(0f, 1f)] private float sideBranchChance = 0.3f;
        [SerializeField, Range(0f, 1f)] private float descendingChance = 0.65f;
        [SerializeField, Range(2, 12)] private int decorationSpacing = 4;

        [Header("Explicit Sockets (optional)")]
        [Tooltip("When non-empty these sockets replace Connector Pattern for this module.")]
        [SerializeField] private List<JigsawConnectorDefinition> connectors =
            new List<JigsawConnectorDefinition>();

        public string StableId => stableId;
        public string DisplayName => displayName;
        public bool IsStartPiece => startPiece;
        public IReadOnlyList<JigsawConnectorDefinition> Connectors => connectors;

        public void ConfigureTemplate(
            VoxelStructureAsset template,
            bool writeTemplateAir = true)
        {
            voxelTemplate = template;
            templateWritesAir = writeTemplateAir;
        }

        public void ConfigureSelectionConstraints(
            int desiredMinimumCount,
            int hardMaximumCount,
            bool mayRepeatImmediately = true,
            int objectiveDepth = 0)
        {
            minimumCount = desiredMinimumCount;
            maximumCount = hardMaximumCount;
            allowConsecutive = mayRepeatImmediately;
            requiredByDepth = objectiveDepth;
            ClampConfiguration();
        }

        public void AddConnector(JigsawConnectorDefinition connector)
        {
            if (connector == null)
            {
                throw new ArgumentNullException(nameof(connector));
            }
            if (connectors == null)
            {
                connectors = new List<JigsawConnectorDefinition>();
            }
            connectors.Add(connector);
        }

        public void ConfigureBox(
            string pieceId,
            string pieceDisplayName,
            Shape pieceShape,
            BuildStyle style,
            ConnectorPattern connections,
            Decoration pieceDecoration,
            bool isStart,
            int pieceWeight,
            int minGraphDepth,
            int maxGraphDepth,
            int minWidth,
            int maxWidth,
            int minDepth,
            int maxDepth,
            int minHeight,
            int maxHeight,
            string inputPool = "main",
            string childPool = "main")
        {
            stableId = pieceId;
            displayName = pieceDisplayName;
            shape = pieceShape;
            buildStyle = style;
            connectorPattern = connections;
            decoration = pieceDecoration;
            startPiece = isStart;
            weight = pieceWeight;
            minimumGraphDepth = minGraphDepth;
            maximumGraphDepth = maxGraphDepth;
            minimumWidth = minWidth;
            maximumWidth = maxWidth;
            minimumDepth = minDepth;
            maximumDepth = maxDepth;
            minimumHeight = minHeight;
            maximumHeight = maxHeight;
            poolId = inputPool;
            outputPoolId = childPool;
            ClampConfiguration();
        }

        public void ConfigurePassage(
            string pieceId,
            string pieceDisplayName,
            Shape pieceShape,
            BuildStyle style,
            ConnectorPattern connections,
            Decoration pieceDecoration,
            int pieceWeight,
            int minGraphDepth,
            int maxGraphDepth,
            int minPassageLength,
            int maxPassageLength,
            int passageWidth,
            int passageHeight,
            int stairVerticalDelta = 4,
            float branchChance = 0.3f,
            float stairDescendingChance = 0.65f,
            int spacing = 4,
            string inputPool = "main",
            string childPool = "main")
        {
            stableId = pieceId;
            displayName = pieceDisplayName;
            shape = pieceShape;
            buildStyle = style;
            connectorPattern = connections;
            decoration = pieceDecoration;
            startPiece = false;
            weight = pieceWeight;
            minimumGraphDepth = minGraphDepth;
            maximumGraphDepth = maxGraphDepth;
            minimumLength = minPassageLength;
            maximumLength = maxPassageLength;
            width = passageWidth;
            height = passageHeight;
            verticalDelta = stairVerticalDelta;
            sideBranchChance = branchChance;
            descendingChance = stairDescendingChance;
            decorationSpacing = spacing;
            poolId = inputPool;
            outputPoolId = childPool;
            ClampConfiguration();
        }

        internal JigsawPieceSettings CreateSettings()
        {
            ClampConfiguration();
            var connectorSettings = new JigsawConnectorSettings[connectors.Count];
            for (int i = 0; i < connectors.Count; i++)
            {
                if (connectors[i] == null)
                {
                    throw new InvalidOperationException(
                        $"Connector at index {i} on piece '{stableId}' is null.");
                }
                connectorSettings[i] = connectors[i].CreateSettings();
            }
            Vector3Int templateSize = default;
            Vector3Int templateAnchor = default;
            float[] templateDensities = null;
            VoxelTypeId[] templateTypes = null;
            if (voxelTemplate != null)
            {
                templateSize = voxelTemplate.Size;
                templateAnchor = voxelTemplate.Anchor;
                voxelTemplate.CopyData(
                    out templateDensities,
                    out templateTypes);
            }
            return new JigsawPieceSettings(
                stableId,
                displayName,
                poolId,
                outputPoolId,
                startPiece,
                weight,
                minimumGraphDepth,
                maximumGraphDepth,
                minimumCount,
                maximumCount,
                allowConsecutive,
                requiredByDepth,
                shape,
                buildStyle,
                connectorPattern,
                decoration,
                minimumWidth,
                maximumWidth,
                minimumDepth,
                maximumDepth,
                minimumHeight,
                maximumHeight,
                minimumLength,
                maximumLength,
                width,
                height,
                verticalDelta,
                sideBranchChance,
                descendingChance,
                decorationSpacing,
                connectorSettings,
                templateSize,
                templateAnchor,
                templateWritesAir,
                templateDensities,
                templateTypes);
        }

        internal void ClampConfiguration()
        {
            stableId = string.IsNullOrWhiteSpace(stableId)
                ? "piece"
                : stableId.Trim();
            displayName = string.IsNullOrWhiteSpace(displayName)
                ? stableId
                : displayName.Trim();
            poolId = string.IsNullOrWhiteSpace(poolId) ? "main" : poolId.Trim();
            outputPoolId = string.IsNullOrWhiteSpace(outputPoolId)
                ? poolId
                : outputPoolId.Trim();
            weight = Mathf.Max(0, weight);
            minimumGraphDepth = Mathf.Max(0, minimumGraphDepth);
            maximumGraphDepth = Mathf.Max(
                minimumGraphDepth,
                maximumGraphDepth);
            minimumCount = Mathf.Max(0, minimumCount);
            maximumCount = Mathf.Max(0, maximumCount);
            if (maximumCount > 0)
            {
                minimumCount = Mathf.Min(minimumCount, maximumCount);
            }
            requiredByDepth = Mathf.Max(0, requiredByDepth);
            minimumWidth = MakeOdd(Mathf.Max(3, minimumWidth));
            maximumWidth = MakeOdd(Mathf.Max(minimumWidth, maximumWidth));
            minimumDepth = MakeOdd(Mathf.Max(3, minimumDepth));
            maximumDepth = MakeOdd(Mathf.Max(minimumDepth, maximumDepth));
            minimumHeight = Mathf.Max(4, minimumHeight);
            maximumHeight = Mathf.Max(minimumHeight, maximumHeight);
            minimumLength = Mathf.Max(3, minimumLength);
            maximumLength = Mathf.Max(minimumLength, maximumLength);
            width = MakeOdd(Mathf.Max(3, width));
            height = Mathf.Max(4, height);
            verticalDelta = Mathf.Max(1, verticalDelta);
            sideBranchChance = Mathf.Clamp01(sideBranchChance);
            descendingChance = Mathf.Clamp01(descendingChance);
            decorationSpacing = Mathf.Clamp(decorationSpacing, 2, 12);
            if (connectors == null)
            {
                connectors = new List<JigsawConnectorDefinition>();
            }
            for (int i = 0; i < connectors.Count; i++)
            {
                connectors[i]?.ClampConfiguration();
            }
        }

        private static int MakeOdd(int value)
        {
            return (value & 1) == 0 ? value + 1 : value;
        }
    }
}
