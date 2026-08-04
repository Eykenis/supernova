using System;
using System.Collections.Generic;
using Supernova.Voxels;
using UnityEngine;

namespace Supernova.MinecraftCaves
{
    /// <summary>
    /// Asset-level definition of a deterministic recursive jigsaw structure.
    /// Structure families are data assets; the world does not know their types.
    /// </summary>
    [CreateAssetMenu(
        fileName = "JigsawStructure",
        menuName = "Supernova/World Generation/Jigsaw Structure")]
    public sealed class JigsawStructureFeatureDefinition : ScriptableObject
    {
        [Header("Identity and Materials")]
        [SerializeField] private bool enabled = true;
        [SerializeField] private string stableId = "jigsaw_structure";
        [SerializeField] private VoxelTypeDefinition primaryVoxelType;
        [SerializeField] private VoxelTypeDefinition accentVoxelType;

        [Header("Placement")]
        [SerializeField]
        private JigsawPlacementStrategy placementStrategy =
            JigsawPlacementStrategy.RandomSpread;
        [SerializeField] private int seedSalt = 104729;
        [SerializeField, Min(4)] private int regionSizeInChunks = 10;
        [SerializeField, Range(0f, 1f)] private float placementChance = 0.4f;
        [SerializeField] private int minFloorHeight = 48;
        [SerializeField] private int maxFloorHeight = 160;

        [Header("Concentric Rings (used by that strategy only)")]
        [Tooltip("Total candidates distributed over all rings.")]
        [SerializeField, Range(1, 512)] private int ringStructureCount = 128;
        [SerializeField, Range(1, 16)] private int ringCount = 8;
        [Tooltip("Radius step per ring, in voxel column chunks.")]
        [SerializeField, Range(4, 256)] private int ringDistanceInChunks = 32;
        [Tooltip("Random radial jitter applied inside a ring, in chunks.")]
        [SerializeField, Range(0, 64)] private int ringSpreadInChunks = 3;

        [Header("Structure Set (optional competition)")]
        [Tooltip("Features sharing a set ID compete for the same candidate cell.")]
        [SerializeField] private string structureSetId;
        [Tooltip("Relative odds of winning a shared candidate cell.")]
        [SerializeField, Min(1)] private int structureSetWeight = 1;

        [Header("Piece Graph")]
        [SerializeField, Range(2, 128)] private int maxPieces = 40;
        [SerializeField, Range(1, 16)] private int maxDepth = 7;
        [SerializeField, Range(16, 192)]
        private int maxHorizontalDistance = 120;
        [Tooltip("Optional piece forced at graph depth 1, such as a mine corridor.")]
        [SerializeField] private string firstPieceId;

        [Header("Layout Quality and Performance")]
        [Tooltip("Deterministic full-layout retries used to satisfy minimum piece counts.")]
        [SerializeField, Range(1, 16)] private int layoutAttempts = 4;
        [Tooltip("Different candidates tried at a socket before that branch terminates.")]
        [SerializeField, Range(1, 16)] private int connectorPlacementAttempts = 6;
        [Tooltip("Extra empty voxels required between unrelated piece bounds.")]
        [SerializeField, Range(0, 4)] private int collisionPadding = 1;

        [Header("Piece Modules")]
        [SerializeField] private List<JigsawPieceDefinition> pieces =
            new List<JigsawPieceDefinition>();

        public bool Enabled => enabled;
        public string StableId => stableId;
        public IReadOnlyList<JigsawPieceDefinition> Pieces => pieces;

        public void ConfigurePlacementStrategy(
            JigsawPlacementStrategy strategy,
            int totalRingStructures = 128,
            int rings = 8,
            int ringStepInChunks = 32,
            int ringJitterInChunks = 3)
        {
            placementStrategy = strategy;
            ringStructureCount = totalRingStructures;
            ringCount = rings;
            ringDistanceInChunks = ringStepInChunks;
            ringSpreadInChunks = ringJitterInChunks;
            ClampConfiguration();
        }

        public void ConfigureStructureSet(string setId, int weight)
        {
            structureSetId = setId;
            structureSetWeight = weight;
            ClampConfiguration();
        }

        public void ConfigureLayoutPolicy(
            int deterministicLayoutAttempts,
            int placementAttemptsPerConnector,
            int pieceCollisionPadding = 1)
        {
            layoutAttempts = deterministicLayoutAttempts;
            connectorPlacementAttempts = placementAttemptsPerConnector;
            collisionPadding = pieceCollisionPadding;
            ClampConfiguration();
        }

        public void AddPiece(JigsawPieceDefinition piece)
        {
            if (piece == null)
            {
                throw new ArgumentNullException(nameof(piece));
            }
            if (pieces == null)
            {
                pieces = new List<JigsawPieceDefinition>();
            }
            pieces.Add(piece);
        }

        public void Configure(
            bool isEnabled,
            string structureId,
            VoxelTypeDefinition primaryType,
            VoxelTypeDefinition accentType,
            int structureSeedSalt,
            int placementRegionSizeInChunks,
            float chance,
            int minimumFloorHeight,
            int maximumFloorHeight,
            int pieceLimit,
            int depthLimit,
            int horizontalDistanceLimit,
            string forcedFirstPieceId,
            IEnumerable<JigsawPieceDefinition> pieceModules)
        {
            enabled = isEnabled;
            stableId = structureId;
            primaryVoxelType = primaryType;
            accentVoxelType = accentType;
            seedSalt = structureSeedSalt;
            regionSizeInChunks = placementRegionSizeInChunks;
            placementChance = chance;
            minFloorHeight = minimumFloorHeight;
            maxFloorHeight = maximumFloorHeight;
            maxPieces = pieceLimit;
            maxDepth = depthLimit;
            maxHorizontalDistance = horizontalDistanceLimit;
            firstPieceId = forcedFirstPieceId;
            pieces = pieceModules != null
                ? new List<JigsawPieceDefinition>(pieceModules)
                : new List<JigsawPieceDefinition>();
            ClampConfiguration();
        }

        public bool TryCreateSettings(
            out JigsawStructureFeatureSettings settings,
            out string error)
        {
            if (!enabled)
            {
                settings = default;
                error = string.Empty;
                return false;
            }
            if (primaryVoxelType == null)
            {
                settings = default;
                error = $"Jigsaw structure '{stableId}' has no primary voxel type.";
                return false;
            }

            try
            {
                ClampConfiguration();
                var snapshots = new JigsawPieceSettings[pieces.Count];
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (pieces[i] == null)
                    {
                        throw new InvalidOperationException(
                            $"Piece module at index {i} is null.");
                    }
                    snapshots[i] = pieces[i].CreateSettings();
                }
                settings = new JigsawStructureFeatureSettings(
                    stableId,
                    primaryVoxelType.TypeId,
                    accentVoxelType != null
                        ? accentVoxelType.TypeId
                        : primaryVoxelType.TypeId,
                    seedSalt,
                    regionSizeInChunks,
                    placementChance,
                    minFloorHeight,
                    maxFloorHeight,
                    maxPieces,
                    maxDepth,
                    maxHorizontalDistance,
                    firstPieceId,
                    layoutAttempts,
                    connectorPlacementAttempts,
                    collisionPadding,
                    snapshots,
                    placementStrategy,
                    ringStructureCount,
                    ringCount,
                    ringDistanceInChunks,
                    ringSpreadInChunks,
                    structureSetId,
                    structureSetWeight);
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                settings = default;
                error = $"Jigsaw structure '{stableId}' is invalid: {exception.Message}";
                return false;
            }
        }

        private void OnValidate()
        {
            ClampConfiguration();
        }

        private void ClampConfiguration()
        {
            stableId = string.IsNullOrWhiteSpace(stableId)
                ? name
                : stableId.Trim();
            regionSizeInChunks = Mathf.Max(4, regionSizeInChunks);
            placementChance = Mathf.Clamp01(placementChance);
            maxPieces = Mathf.Clamp(maxPieces, 2, 128);
            maxDepth = Mathf.Clamp(maxDepth, 1, 16);
            layoutAttempts = Mathf.Clamp(layoutAttempts, 1, 16);
            connectorPlacementAttempts = Mathf.Clamp(
                connectorPlacementAttempts,
                1,
                16);
            collisionPadding = Mathf.Clamp(collisionPadding, 0, 4);
            ringStructureCount = Mathf.Clamp(ringStructureCount, 1, 512);
            ringCount = Mathf.Clamp(ringCount, 1, 16);
            ringDistanceInChunks = Mathf.Clamp(ringDistanceInChunks, 4, 256);
            ringSpreadInChunks = Mathf.Clamp(ringSpreadInChunks, 0, 64);
            structureSetId = structureSetId == null
                ? string.Empty
                : structureSetId.Trim();
            structureSetWeight = Mathf.Max(1, structureSetWeight);
            int regionRadiusLimit = regionSizeInChunks
                * VoxelColumnChunkData.Width / 2 - 1;
            maxHorizontalDistance = Mathf.Clamp(
                maxHorizontalDistance,
                16,
                Mathf.Min(192, regionRadiusLimit));
            firstPieceId = firstPieceId == null ? string.Empty : firstPieceId.Trim();
            if (pieces == null)
            {
                pieces = new List<JigsawPieceDefinition>();
            }
            for (int i = 0; i < pieces.Count; i++)
            {
                pieces[i]?.ClampConfiguration();
            }
        }
    }
}
